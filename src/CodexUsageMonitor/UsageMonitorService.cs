using System;
using System.Collections.Generic;
using System.Threading;

namespace CodexUsageMonitor
{
    internal sealed class UsageMonitorService : IDisposable
    {
        private readonly ManualResetEvent stopEvent = new ManualResetEvent(false);
        private readonly AutoResetEvent refreshEvent = new AutoResetEvent(false);
        private readonly Random random = new Random();
        private Thread worker;
        private volatile AppServerClient activeClient;
        private int disposeState;
        private UsageSnapshot lastSnapshot;

        public event EventHandler<UsageSnapshotEventArgs> SnapshotChanged;
        public event EventHandler<MonitorStatusEventArgs> StatusChanged;

        public void Start()
        {
            if (Interlocked.CompareExchange(ref disposeState, 0, 0) != 0)
            {
                throw new ObjectDisposedException(GetType().FullName);
            }

            if (worker != null)
            {
                return;
            }

            worker = new Thread(Run);
            worker.IsBackground = true;
            worker.Name = "Codex Usage Monitor Service";
            worker.Start();
        }

        public void Refresh()
        {
            if (Interlocked.CompareExchange(ref disposeState, 0, 0) == 0)
            {
                try
                {
                    refreshEvent.Set();
                }
                catch (ObjectDisposedException)
                {
                }
            }
        }

        private void Run()
        {
            int attempt = 0;
            while (!stopEvent.WaitOne(0))
            {
                AppServerClient client = null;
                try
                {
                    PublishStatus("正在连接 Codex…", false, lastSnapshot != null);
                    client = new AppServerClient();
                    activeClient = client;
                    client.RateLimitsUpdated += HandleRateLimitsUpdated;
                    client.Start();

                    PublishStatus("实时连接", true, false);
                    bool keepRunning = true;
                    while (keepRunning && !stopEvent.WaitOne(0))
                    {
                        IDictionary<string, object> response =
                            client.ReadRateLimits(TimeSpan.FromSeconds(15));
                        UsageSnapshot snapshot = RateLimitParser.Parse(response);
                        lastSnapshot = snapshot;
                        attempt = 0;
                        PublishSnapshot(snapshot);
                        PublishStatus("实时连接", true, false);

                        int selected = WaitHandle.WaitAny(
                            new WaitHandle[]
                            {
                                stopEvent,
                                refreshEvent,
                                client.ClosedWaitHandle
                            },
                            TimeSpan.FromSeconds(60));

                        if (selected == 0)
                        {
                            keepRunning = false;
                        }
                        else if (selected == 1)
                        {
                            Thread.Sleep(250);
                        }
                        else if (selected == 2)
                        {
                            throw new InvalidOperationException(
                                "Codex app-server connection closed.");
                        }
                    }
                }
                catch (Exception error)
                {
                    if (!stopEvent.WaitOne(0))
                    {
                        PublishStatus(
                            BuildFriendlyError(error),
                            false,
                            lastSnapshot != null);

                        attempt++;
                        int delaySeconds = GetRetryDelaySeconds(attempt);
                        int selected = WaitHandle.WaitAny(
                            new WaitHandle[] { stopEvent, refreshEvent },
                            TimeSpan.FromSeconds(delaySeconds));
                        if (selected == 0)
                        {
                            break;
                        }
                    }
                }
                finally
                {
                    activeClient = null;
                    if (client != null)
                    {
                        client.RateLimitsUpdated -= HandleRateLimitsUpdated;
                        client.Dispose();
                    }
                }
            }
        }

        private void HandleRateLimitsUpdated(object sender, EventArgs eventArgs)
        {
            Refresh();
        }

        private int GetRetryDelaySeconds(int attempt)
        {
            int[] delays = { 1, 2, 5, 10, 30, 60 };
            int index = Math.Min(Math.Max(0, attempt - 1), delays.Length - 1);
            int baseDelay = delays[index];
            return baseDelay + random.Next(0, Math.Max(1, baseDelay / 4 + 1));
        }

        private static string BuildFriendlyError(Exception error)
        {
            string message = error.Message ?? string.Empty;
            string normalized = message.ToLowerInvariant();

            if (normalized.Contains("找不到 codex") ||
                error is System.IO.FileNotFoundException)
            {
                return "未找到 Codex Desktop";
            }

            if (normalized.Contains("401") ||
                normalized.Contains("unauthorized") ||
                normalized.Contains("sign in") ||
                normalized.Contains("登录"))
            {
                return "请先登录 Codex Desktop";
            }

            if (normalized.Contains("timed out") ||
                normalized.Contains("timeout"))
            {
                return "连接超时，正在重试";
            }

            if (normalized.Contains("method") &&
                normalized.Contains("not found"))
            {
                return "当前 Codex 版本不兼容";
            }

            return "连接中断，正在重试";
        }

        private void PublishSnapshot(UsageSnapshot snapshot)
        {
            EventHandler<UsageSnapshotEventArgs> handler = SnapshotChanged;
            if (handler != null)
            {
                handler(this, new UsageSnapshotEventArgs(snapshot));
            }
        }

        private void PublishStatus(string message, bool connected, bool stale)
        {
            EventHandler<MonitorStatusEventArgs> handler = StatusChanged;
            if (handler != null)
            {
                handler(
                    this,
                    new MonitorStatusEventArgs(
                        new MonitorStatus(message, connected, stale)));
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposeState, 1) != 0)
            {
                return;
            }

            stopEvent.Set();
            refreshEvent.Set();

            AppServerClient client = activeClient;
            if (client != null)
            {
                client.RequestStop();
            }

            bool workerStopped =
                worker == null ||
                worker == Thread.CurrentThread ||
                worker.Join(6000);
            if (workerStopped)
            {
                refreshEvent.Close();
                stopEvent.Close();
            }
        }
    }

    internal sealed class UsageSnapshotEventArgs : EventArgs
    {
        public UsageSnapshotEventArgs(UsageSnapshot snapshot)
        {
            Snapshot = snapshot;
        }

        public UsageSnapshot Snapshot { get; private set; }
    }

    internal sealed class MonitorStatusEventArgs : EventArgs
    {
        public MonitorStatusEventArgs(MonitorStatus status)
        {
            Status = status;
        }

        public MonitorStatus Status { get; private set; }
    }
}
