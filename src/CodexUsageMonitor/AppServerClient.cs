using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace CodexUsageMonitor
{
    internal sealed class AppServerClient : IDisposable
    {
        private const int MaximumMessageLength = 4 * 1024 * 1024;
        private const int ErrorDrainBufferLength = 4096;
        private readonly object writeGate = new object();
        private readonly object pendingGate = new object();
        private readonly Dictionary<long, PendingRequest> pending =
            new Dictionary<long, PendingRequest>();
        private readonly JavaScriptSerializer readSerializer;
        private readonly JavaScriptSerializer writeSerializer;
        private readonly ManualResetEvent closedEvent = new ManualResetEvent(false);

        private Process process;
        private StreamWriter input;
        private Thread outputThread;
        private Thread errorThread;
        private long nextRequestId;
        private int malformedMessageCount;
        private int closedSignaled;
        private int stopRequested;
        private int disposeState;
        private bool initialized;

        public AppServerClient()
        {
            readSerializer = CreateSerializer();
            writeSerializer = CreateSerializer();
        }

        public event EventHandler RateLimitsUpdated;
        public event EventHandler ConnectionClosed;

        public WaitHandle ClosedWaitHandle
        {
            get { return closedEvent; }
        }

        public bool IsConnected
        {
            get
            {
                return initialized &&
                    process != null &&
                    !SafeHasExited(process) &&
                    !closedEvent.WaitOne(0);
            }
        }

        public void Start()
        {
            ThrowIfDisposed();
            if (process != null)
            {
                throw new InvalidOperationException("The Codex app-server is already started.");
            }

            string codexPath = CodexLocator.FindExecutable();
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = codexPath,
                Arguments = "app-server --stdio",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            process = new Process();
            process.StartInfo = startInfo;
            process.EnableRaisingEvents = true;
            process.Exited += HandleProcessExited;

            try
            {
                if (!process.Start())
                {
                    throw new InvalidOperationException("Codex app-server did not start.");
                }

                input = new StreamWriter(
                    process.StandardInput.BaseStream,
                    new UTF8Encoding(false));
                input.AutoFlush = true;

                outputThread = new Thread(ReadOutputLoop);
                outputThread.IsBackground = true;
                outputThread.Name = "Codex Usage Monitor JSONL Reader";
                outputThread.Start();

                errorThread = new Thread(DrainErrorLoop);
                errorThread.IsBackground = true;
                errorThread.Name = "Codex Usage Monitor Error Drain";
                errorThread.Start();

                Dictionary<string, object> initializeResult = Call(
                    "initialize",
                    new Dictionary<string, object>
                    {
                        {
                            "clientInfo",
                            new Dictionary<string, object>
                            {
                                { "name", "codex-usage-monitor" },
                                { "title", "Codex Usage Monitor" },
                                { "version", BuildInfo.Version }
                            }
                        },
                        {
                            "capabilities",
                            new Dictionary<string, object>
                            {
                                { "experimentalApi", true }
                            }
                        }
                    },
                    TimeSpan.FromSeconds(15));

                if (initializeResult == null)
                {
                    throw new InvalidOperationException(
                        "Codex app-server returned an invalid initialize response.");
                }

                Send(new Dictionary<string, object>
                {
                    { "method", "initialized" }
                });
                initialized = true;
            }
            catch
            {
                DisposeProcess();
                throw;
            }
        }

        public IDictionary<string, object> ReadRateLimits(TimeSpan timeout)
        {
            if (!IsConnected)
            {
                throw new InvalidOperationException("Codex app-server is not connected.");
            }

            return Call("account/rateLimits/read", null, timeout);
        }

        private Dictionary<string, object> Call(
            string method,
            object parameters,
            TimeSpan timeout)
        {
            ThrowIfDisposed();
            long id = Interlocked.Increment(ref nextRequestId);
            PendingRequest request = new PendingRequest();
            lock (pendingGate)
            {
                pending.Add(id, request);
            }

            Dictionary<string, object> message = new Dictionary<string, object>
            {
                { "id", id },
                { "method", method }
            };
            if (parameters != null)
            {
                message.Add("params", parameters);
            }

            try
            {
                Send(message);
                if (!request.Completed.Wait(timeout))
                {
                    throw new TimeoutException(
                        string.Format("Codex request '{0}' timed out.", method));
                }

                if (request.Error != null)
                {
                    throw new InvalidOperationException(
                        string.Format(
                            "Codex request '{0}' failed: {1}",
                            method,
                            DescribeRpcError(request.Error)));
                }

                return request.Result ?? new Dictionary<string, object>();
            }
            finally
            {
                lock (pendingGate)
                {
                    pending.Remove(id);
                }
                request.Dispose();
            }
        }

        private void Send(IDictionary<string, object> message)
        {
            string line = writeSerializer.Serialize(message);
            lock (writeGate)
            {
                if (input == null || SafeHasExited(process))
                {
                    throw new IOException("The Codex app-server input stream is closed.");
                }

                input.WriteLine(line);
                input.Flush();
            }
        }

        private void ReadOutputLoop()
        {
            try
            {
                StreamReader reader = process.StandardOutput;
                string line;
                while ((line = ReadBoundedLine(
                    reader,
                    MaximumMessageLength)) != null)
                {
                    if (line.Length == 0)
                    {
                        continue;
                    }

                    try
                    {
                        HandleMessage(readSerializer.DeserializeObject(line));
                        malformedMessageCount = 0;
                    }
                    catch (ArgumentException)
                    {
                        malformedMessageCount++;
                    }
                    catch (InvalidOperationException)
                    {
                        malformedMessageCount++;
                    }

                    if (malformedMessageCount >= 3)
                    {
                        throw new InvalidDataException(
                            "Codex app-server returned repeated malformed messages.");
                    }
                }
            }
            catch (Exception error)
            {
                FailPending(error);
            }
            finally
            {
                SignalClosed();
            }
        }

        private void HandleMessage(object rawMessage)
        {
            IDictionary<string, object> message = JsonValue.AsObject(rawMessage);
            if (message == null)
            {
                throw new InvalidDataException("Expected a JSON object.");
            }

            object rawId = JsonValue.Get(message, "id");
            object rawMethod = JsonValue.Get(message, "method");
            object rawResult = JsonValue.Get(message, "result");
            object rawError = JsonValue.Get(message, "error");

            if (rawId != null && rawMethod == null)
            {
                long id;
                if (!long.TryParse(Convert.ToString(rawId), out id))
                {
                    return;
                }

                PendingRequest request;
                lock (pendingGate)
                {
                    if (!pending.TryGetValue(id, out request))
                    {
                        return;
                    }

                    pending.Remove(id);
                    request.Result =
                        JsonValue.AsObject(rawResult) as Dictionary<string, object>;
                    if (request.Result == null && rawResult != null)
                    {
                        IDictionary<string, object> resultObject =
                            JsonValue.AsObject(rawResult);
                        if (resultObject != null)
                        {
                            request.Result =
                                new Dictionary<string, object>(resultObject);
                        }
                    }
                    request.Error = JsonValue.AsObject(rawError);
                    request.Completed.Set();
                }
                return;
            }

            string method = Convert.ToString(rawMethod);
            if (string.Equals(
                method,
                "account/rateLimits/updated",
                StringComparison.Ordinal))
            {
                EventHandler handler = RateLimitsUpdated;
                if (handler != null)
                {
                    handler(this, EventArgs.Empty);
                }
            }
        }

        private void DrainErrorLoop()
        {
            try
            {
                StreamReader reader = process.StandardError;
                char[] buffer = new char[ErrorDrainBufferLength];
                while (reader.Read(buffer, 0, buffer.Length) > 0)
                {
                    // Discard bounded chunks. Stderr can contain environment details.
                }
            }
            catch (Exception)
            {
                // Connection health is determined by stdout and process exit.
            }
        }

        private static string ReadBoundedLine(
            StreamReader reader,
            int maximumLength)
        {
            StringBuilder line = new StringBuilder(
                Math.Min(maximumLength, 4096),
                maximumLength);

            while (true)
            {
                int value = reader.Read();
                if (value < 0)
                {
                    return line.Length == 0 ? null : line.ToString();
                }

                char character = (char)value;
                if (character == '\r')
                {
                    if (reader.Peek() == '\n')
                    {
                        reader.Read();
                    }
                    return line.ToString();
                }
                if (character == '\n')
                {
                    return line.ToString();
                }

                if (line.Length >= maximumLength)
                {
                    throw new InvalidDataException(
                        "Codex app-server returned an oversized message.");
                }

                line.Append(character);
            }
        }

        private static string DescribeRpcError(IDictionary<string, object> error)
        {
            if (error == null)
            {
                return "unknown error";
            }

            string code = JsonValue.GetString(error, "code");
            string message = JsonValue.GetString(error, "message");
            if (string.IsNullOrWhiteSpace(code))
            {
                return message ?? "unknown error";
            }

            return string.IsNullOrWhiteSpace(message)
                ? code
                : code + " — " + message;
        }

        private void HandleProcessExited(object sender, EventArgs eventArgs)
        {
            SignalClosed();
        }

        private void SignalClosed()
        {
            if (Interlocked.CompareExchange(
                ref closedSignaled,
                1,
                0) == 0)
            {
                closedEvent.Set();
                FailPending(new IOException("The Codex app-server connection closed."));

                EventHandler handler = ConnectionClosed;
                if (handler != null)
                {
                    handler(this, EventArgs.Empty);
                }
            }
        }

        private void FailPending(Exception error)
        {
            lock (pendingGate)
            {
                foreach (PendingRequest request in pending.Values)
                {
                    request.Error = new Dictionary<string, object>
                    {
                        { "message", error.Message }
                    };
                    request.Completed.Set();
                }
            }
        }

        private static bool SafeHasExited(Process candidate)
        {
            if (candidate == null)
            {
                return true;
            }

            try
            {
                return candidate.HasExited;
            }
            catch (InvalidOperationException)
            {
                return true;
            }
        }

        public void RequestStop()
        {
            if (Interlocked.Exchange(ref stopRequested, 1) != 0)
            {
                return;
            }

            SignalClosed();
            DisposeProcess();
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposeState, 1) != 0)
            {
                return;
            }

            RequestStop();
            bool outputStopped = true;
            bool errorStopped = true;
            if (outputThread != null && outputThread != Thread.CurrentThread)
            {
                outputStopped = outputThread.Join(3000);
            }
            if (errorThread != null && errorThread != Thread.CurrentThread)
            {
                errorStopped = errorThread.Join(3000);
            }
            if (outputStopped && errorStopped)
            {
                closedEvent.Close();
            }
        }

        private void DisposeProcess()
        {
            initialized = false;

            try
            {
                if (input != null)
                {
                    input.Close();
                }
            }
            catch (Exception)
            {
            }

            Process candidate = process;
            process = null;
            input = null;

            if (candidate != null)
            {
                try
                {
                    candidate.Exited -= HandleProcessExited;
                    if (!candidate.HasExited)
                    {
                        if (!candidate.WaitForExit(750))
                        {
                            candidate.Kill();
                            candidate.WaitForExit(2000);
                        }
                    }
                }
                catch (Exception)
                {
                }
                finally
                {
                    candidate.Dispose();
                }
            }
        }

        private void ThrowIfDisposed()
        {
            if (Interlocked.CompareExchange(ref disposeState, 0, 0) != 0 ||
                Interlocked.CompareExchange(ref stopRequested, 0, 0) != 0)
            {
                throw new ObjectDisposedException(GetType().FullName);
            }
        }

        private static JavaScriptSerializer CreateSerializer()
        {
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = MaximumMessageLength;
            serializer.RecursionLimit = 64;
            return serializer;
        }

        private sealed class PendingRequest : IDisposable
        {
            public PendingRequest()
            {
                Completed = new ManualResetEventSlim(false);
            }

            public ManualResetEventSlim Completed { get; private set; }
            public Dictionary<string, object> Result { get; set; }
            public IDictionary<string, object> Error { get; set; }

            public void Dispose()
            {
                Completed.Dispose();
            }
        }
    }
}
