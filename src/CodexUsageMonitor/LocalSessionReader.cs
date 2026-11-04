using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace CodexUsageMonitor
{
    // Only complete lines are delivered. A split UTF-8 character stays in the byte buffer.
    internal sealed class IncrementalJsonlReader
    {
        private const int MaximumLineBytes = 2 * 1024 * 1024;
        private readonly MemoryStream pending = new MemoryStream();
        private readonly byte[] buffer = new byte[64 * 1024];
        private long offset;
        private DateTime created;
        private DateTime written;
        private bool skipLine;
        private readonly bool tailOnly;
        private bool initialized;

        public IncrementalJsonlReader(bool tailOnly = false)
        {
            this.tailOnly = tailOnly;
        }

        public bool Read(string path, Action<string> accept, Action reset, Action skipped)
        {
            FileInfo file = new FileInfo(path);
            if (file.Length < offset || (offset > 0 &&
                (file.CreationTimeUtc != created || (file.Length == offset && file.LastWriteTimeUtc != written))))
            {
                offset = 0;
                pending.SetLength(0);
                skipLine = false;
                initialized = false;
                reset();
            }
            if (!initialized)
            {
                offset = tailOnly ? Math.Max(0, file.Length - 1024 * 1024) : 0;
                skipLine = offset > 0;
                initialized = true;
            }
            created = file.CreationTimeUtc;
            written = file.LastWriteTimeUtc;
            long limit = Math.Min(file.Length, offset + 8 * 1024 * 1024);
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete))
            {
                stream.Seek(offset, SeekOrigin.Begin);
                int count;
                while (offset < limit && (count = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, limit - offset))) > 0)
                {
                    for (int i = 0; i < count; i++)
                    {
                        byte value = buffer[i];
                        if (value == 10)
                        {
                            if (!skipLine && pending.Length > 0)
                                accept(Encoding.UTF8.GetString(pending.GetBuffer(), 0, (int)pending.Length).TrimStart('\uFEFF'));
                            pending.SetLength(0);
                            skipLine = false;
                        }
                        else if (!skipLine)
                        {
                            if (pending.Length >= MaximumLineBytes)
                            {
                                pending.SetLength(0);
                                skipLine = true;
                                skipped();
                            }
                            else pending.WriteByte(value);
                        }
                    }
                    offset += count;
                }
            }
            return offset < file.Length;
        }
    }

    internal sealed class RunningSession
    {
        public LocalSession Session { get; set; }
        public SessionMetrics Metrics { get; set; }
    }

    internal sealed class SessionReadResult
    {
        public List<RunningSession> Sessions { get; set; }
        public string Error { get; set; }
    }

    // Read only recent, existing main threads. Orphaned rollout files never enter this list.
    internal sealed class LocalSessionReader
    {
        private readonly string home;
        private readonly Dictionary<string, SessionCache> cache = new Dictionary<string, SessionCache>();

        public LocalSessionReader(string home) { this.home = home; }

        public static string DefaultHome()
        {
            string configured = Environment.GetEnvironmentVariable("CODEX_HOME");
            return string.IsNullOrWhiteSpace(configured)
                ? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex")
                : configured;
        }

        public SessionReadResult Read()
        {
            SessionReadResult result = new SessionReadResult { Sessions = new List<RunningSession>() };
            try
            {
                List<LocalSession> sessions = LocalThreadCatalog.Read(home);
                HashSet<string> retained = new HashSet<string>();
                DateTime now = DateTime.UtcNow;
                double recentSince = (now - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds - 300;
                foreach (LocalSession session in sessions)
                {
                    FileInfo file = new FileInfo(session.Path);
                    // Windows can defer LastWriteTime while Codex keeps the log open.
                    // Catalog activity admits candidates; JSONL events decide whether they are running.
                    if (!file.Exists || (session.UpdatedAtUnixSeconds < recentSince &&
                        (now - file.LastWriteTimeUtc).TotalMinutes > 5) ||
                        (file.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                    retained.Add(session.Id);
                    SessionCache current;
                    if (!cache.TryGetValue(session.Id, out current) || current.Path != session.Path)
                    {
                        current = new SessionCache { Path = session.Path };
                        cache[session.Id] = current;
                    }
                    try
                    {
                        current.Reader.Read(session.Path, line => current.Parser.ReadLine(line),
                            () => current.Parser = new SessionMetricsParser(), () => current.Parser.MarkIncomplete());
                        SessionMetrics metrics = current.Parser.Snapshot();
                        if (metrics.IsRunning) result.Sessions.Add(new RunningSession { Session = session, Metrics = metrics });
                    }
                    catch (IOException) { retained.Remove(session.Id); }
                    catch (UnauthorizedAccessException) { retained.Remove(session.Id); }
                }
                foreach (string id in cache.Keys.Where(id => !retained.Contains(id)).ToList()) cache.Remove(id);
            }
            catch (InvalidDataException error) { result.Error = error.Message; }
            catch (UnauthorizedAccessException) { result.Error = "无法读取 Codex 会话清单，请检查数据目录权限。"; }
            catch (IOException) { result.Error = "暂时读不到 Codex 会话清单，将自动重试。"; }
            catch (ArgumentException) { result.Error = "数据目录无效，请重新选择。"; }
            catch (DllNotFoundException) { result.Error = "此功能需要 Windows 10 或更新版本的系统 SQLite。"; }
            return result;
        }

        private sealed class SessionCache
        {
            public string Path;
            public readonly IncrementalJsonlReader Reader = new IncrementalJsonlReader(true);
            public SessionMetricsParser Parser = new SessionMetricsParser();
        }
    }
}
