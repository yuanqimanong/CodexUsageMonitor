using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;

namespace CodexUsageMonitor
{
    internal static class SessionMetricsTests
    {
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
        private static readonly DateTimeOffset Start = DateTimeOffset.UtcNow.AddMinutes(-1);
        private static int passed;

        private static void Main()
        {
            Run("new and legacy events count once", DuplicateUsage);
            Run("legacy timing excludes parallel tools", LegacyToolTiming);
            Run("new timing excludes tools", NewToolTiming);
            Run("idle time never enters next turn", SeparateTurns);
            Run("missing fields stay unknown", MissingFields);
            Run("counter reset invalidates timing", CounterReset);
            Run("64-bit Token counters", LargeCounters);
            Run("bounded sample history", BoundedHistory);
            Run("incremental UTF-8 and partial lines", IncrementalRead);
            Run("truncation and replacement reset state", Truncation);
            Run("oversized lines stay bounded", OversizedLine);
            Run("active turn completion and stale timeout", Lifecycle);
            Run("deleted archived and background sessions excluded", Catalog);
            Run("live catalog timestamps and sidebar names", RunningCatalogMetadata);
            Run("tail reading avoids full history", TailRead);
            Console.WriteLine("PASS " + passed + " regression checks");
        }

        private static void Run(string name, Action test)
        {
            try { test(); }
            catch (Exception error)
            {
                Console.WriteLine("FAIL " + name + " " + error.GetType().Name + " " + error.Message);
                Environment.Exit(1);
            }
            passed++;
            Console.WriteLine("PASS " + name);
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private static Dictionary<string, object> Usage(long input, long output)
        {
            return new Dictionary<string, object>
            {
                { "input_tokens", input }, { "cached_input_tokens", input / 2 },
                { "output_tokens", output }, { "reasoning_output_tokens", output / 2 }
            };
        }

        private static string Record(double seconds, string kind, object payload)
        {
            return Json.Serialize(new { timestamp = Start.AddSeconds(seconds).ToString("o"), type = kind, payload });
        }

        private static void Event(SessionMetricsParser parser, double seconds, string type)
        {
            parser.ReadLine(Record(seconds, "event_msg", new { type }));
        }

        private static void Tool(SessionMetricsParser parser, double seconds, string type, string id)
        {
            parser.ReadLine(Record(seconds, "response_item", new { type, call_id = id }));
        }

        private static void Legacy(SessionMetricsParser parser, double seconds, long cumulativeInput, long cumulativeOutput, long output)
        {
            parser.ReadLine(Record(seconds, "event_msg", new
            {
                type = "token_count",
                info = new { total_token_usage = Usage(cumulativeInput, cumulativeOutput), last_token_usage = Usage(100, output) }
            }));
        }

        private static void Modern(SessionMetricsParser parser, double seconds, long input, long totalOutput, long output, string id)
        {
            parser.ReadLine(Record(seconds, "token_usage_record", new
            {
                response_id = id, thread_token_usage = Usage(input, totalOutput), usage = Usage(100, output)
            }));
        }

        private static void DuplicateUsage()
        {
            SessionMetricsParser parser = new SessionMetricsParser();
            Event(parser, 0, "task_started");
            Modern(parser, 10, 100, 200, 200, "response-1");
            Legacy(parser, 10.1, 100, 200, 200);
            Modern(parser, 10.2, 100, 200, 200, "response-1");
            Assert(parser.Snapshot().Samples.Count == 1, "duplicate sample");
            Assert(parser.Snapshot().Total.Output == 200, "duplicate total");
        }

        private static void LegacyToolTiming()
        {
            SessionMetricsParser parser = new SessionMetricsParser();
            Event(parser, 0, "task_started");
            Tool(parser, 10, "function_call", "a");
            Tool(parser, 10.1, "function_call", "b");
            Tool(parser, 20, "function_call_output", "a");
            Tool(parser, 30, "function_call_output", "b");
            Legacy(parser, 30.1, 100, 200, 200);
            Legacy(parser, 40, 200, 300, 100);
            Assert(parser.Snapshot().Samples[0].Seconds == 10, "tool duration included");
            Assert(parser.Snapshot().Samples[1].Seconds == 10, "next request boundary lost");
        }

        private static void NewToolTiming()
        {
            SessionMetricsParser parser = new SessionMetricsParser();
            Event(parser, 0, "task_started");
            Tool(parser, 10, "custom_tool_call", "a");
            Modern(parser, 10.1, 100, 200, 200, "r1");
            Tool(parser, 80, "custom_tool_call_output", "a");
            Legacy(parser, 80.1, 100, 200, 200);
            Modern(parser, 90, 200, 400, 200, "r2");
            Assert(parser.Snapshot().Samples.Count == 2, "mirrored usage duplicated");
            Assert(parser.Snapshot().Samples.All(sample => sample.Seconds == 10), "modern tool gap included");
        }

        private static void SeparateTurns()
        {
            SessionMetricsParser parser = new SessionMetricsParser();
            Event(parser, 0, "task_started"); Legacy(parser, 10, 100, 100, 100);
            Event(parser, 10.1, "task_complete");
            Event(parser, 5000, "task_started"); Legacy(parser, 5010, 200, 200, 100);
            Assert(parser.Snapshot().Samples[1].Seconds == 10, "idle time included");
        }

        private static void MissingFields()
        {
            SessionMetricsParser parser = new SessionMetricsParser();
            parser.ReadLine(Record(10, "token_usage_record", new
            {
                thread_token_usage = new { input_tokens = 20, output_tokens = 10 },
                usage = new { input_tokens = 20, output_tokens = 10 }
            }));
            Assert(parser.Snapshot().Total.Cached == null && parser.Snapshot().Total.Reasoning == null, "missing field fabricated");
            Assert(parser.Snapshot().Samples[0].TokensPerSecond == null, "missing boundary fabricated");
            parser.ReadLine("{malformed");
            Assert(parser.Snapshot().Incomplete, "malformed record hidden");
        }

        private static void CounterReset()
        {
            SessionMetricsParser parser = new SessionMetricsParser();
            Event(parser, 0, "task_started"); Legacy(parser, 10, 100, 100, 100);
            Legacy(parser, 20, 50, 10, 10);
            Assert(parser.Snapshot().Incomplete, "reset not flagged");
            Assert(parser.Snapshot().Samples[1].Seconds == null, "reset speed fabricated");
        }

        private static void LargeCounters()
        {
            SessionMetricsParser parser = new SessionMetricsParser();
            Event(parser, 0, "task_started"); Legacy(parser, 10, 5000000000L, 100, 100);
            Legacy(parser, 30, 5000000100L, 500, 400);
            Assert(parser.Snapshot().Total.Input == 5000000100L, "32-bit overflow");
        }

        private static void BoundedHistory()
        {
            SessionMetricsParser parser = new SessionMetricsParser();
            Event(parser, 0, "task_started");
            for (int i = 1; i <= 100; i++) Legacy(parser, i, i * 100, i * 10, 10);
            Assert(parser.Snapshot().Samples.Count == 10, "history unbounded");
        }

        private static void WithFile(Action<string> test)
        {
            string path = Path.GetTempFileName();
            try { test(path); }
            finally { File.Delete(path); }
        }

        private static void IncrementalRead()
        {
            WithFile(path =>
            {
                IncrementalJsonlReader reader = new IncrementalJsonlReader();
                List<string> lines = new List<string>();
                byte[] bytes = Encoding.UTF8.GetBytes("测试\n第二行\n");
                File.WriteAllBytes(path, bytes.Take(2).ToArray());
                reader.Read(path, lines.Add, () => lines.Clear(), () => { });
                Assert(lines.Count == 0, "partial line delivered");
                using (FileStream file = new FileStream(path, FileMode.Append)) file.Write(bytes, 2, bytes.Length - 2);
                reader.Read(path, lines.Add, () => lines.Clear(), () => { });
                reader.Read(path, lines.Add, () => lines.Clear(), () => { });
                Assert(lines.SequenceEqual(new[] { "测试", "第二行" }), "UTF-8 or duplicate read");
            });
        }

        private static void Truncation()
        {
            WithFile(path =>
            {
                IncrementalJsonlReader reader = new IncrementalJsonlReader();
                List<string> lines = new List<string>();
                File.WriteAllText(path, "original long line\n");
                reader.Read(path, lines.Add, () => lines.Clear(), () => { });
                File.WriteAllText(path, "new\n");
                reader.Read(path, lines.Add, () => lines.Clear(), () => { });
                Assert(lines.SequenceEqual(new[] { "new" }), "truncated file not reset");
                File.WriteAllText(path, "two\n");
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(10));
                reader.Read(path, lines.Add, () => lines.Clear(), () => { });
                Assert(lines.SequenceEqual(new[] { "two" }), "same-length rewrite not reset");
            });
        }

        private static void OversizedLine()
        {
            WithFile(path =>
            {
                File.WriteAllText(path, new string('x', 2 * 1024 * 1024 + 10) + "\nvalid\n");
                List<string> lines = new List<string>();
                int skipped = 0;
                new IncrementalJsonlReader().Read(path, lines.Add, () => lines.Clear(), () => skipped++);
                Assert(skipped == 1 && lines.SequenceEqual(new[] { "valid" }), "oversized line handling");
            });
        }

        private static void Lifecycle()
        {
            SessionMetricsParser parser = new SessionMetricsParser();
            Event(parser, 0, "task_started");
            Legacy(parser, 10, 100, 100, 100);
            Assert(parser.Snapshot(Start.AddSeconds(20)).IsRunning, "running session missing");
            Event(parser, 21, "task_complete");
            Assert(!parser.Snapshot(Start.AddSeconds(22)).IsRunning, "completed session still running");
            Event(parser, 30, "task_started");
            Assert(parser.Snapshot(Start.AddSeconds(40)).IsRunning, "resumed session missing");
            Assert(!parser.Snapshot(Start.AddSeconds(340)).IsRunning, "stale session still running");
        }

        private static void TailRead()
        {
            WithFile(path =>
            {
                File.WriteAllText(path, new string('x', 3 * 1024 * 1024) + "\nlatest\n");
                List<string> lines = new List<string>();
                new IncrementalJsonlReader(true).Read(path, lines.Add, () => lines.Clear(), () => { });
                Assert(lines.SequenceEqual(new[] { "latest" }), "history was not skipped");
            });
        }

        private static void Catalog()
        {
            string root = Path.Combine(Path.GetTempPath(), "CodexMonitorTests-" + Guid.NewGuid());
            Directory.CreateDirectory(Path.Combine(root, "sessions"));
            string id = "00000000-0000-0000-0000-000000000001";
            string path = Path.Combine(root, "sessions", "rollout-" + id + ".jsonl");
            string database = Path.Combine(root, "state_5.sqlite");
            try
            {
                File.WriteAllText(path, Record(0, "event_msg", new { type = "task_started" }) + "\n");
                ExecuteSql(database, "CREATE TABLE threads (id TEXT, title TEXT, rollout_path TEXT, source TEXT, model TEXT, archived INTEGER, updated_at INTEGER);" +
                    "INSERT INTO threads VALUES ('" + id + "','Live','" + path.Replace("'", "''") + "','vscode','test-model',0,1);" +
                    "INSERT INTO threads VALUES ('archived','Archived','" + path.Replace("'", "''") + "','vscode','test-model',1,1);" +
                    "INSERT INTO threads VALUES ('background','Review','" + path.Replace("'", "''") + "','{\"subagent\":{}}','test-model',0,1);");
                LocalSessionReader reader = new LocalSessionReader(root);
                Assert(LocalThreadCatalog.Read(root).Count == 1, "archived or background session leaked");
                SessionReadResult result = reader.Read();
                Assert(result.Error == null && result.Sessions.Count == 1, "active card missing");
                ExecuteSql(database, "UPDATE threads SET rollout_path='\\\\?\\" + path.Replace("'", "''") + "' WHERE id='" + id + "';" +
                    "INSERT INTO threads VALUES ('invalid','Invalid','bad|path.jsonl','vscode','test-model',0,2);");
                result = reader.Read();
                Assert(result.Error == null && result.Sessions.Count == 1,
                    "extended Windows path or invalid row broke the session list");
                Assert(LocalThreadCatalog.Read(root)[0].Path == path, "extended path was not normalized");
                ExecuteSql(database, "DELETE FROM threads WHERE id='" + id + "';");
                Assert(File.Exists(path), "test removed rollout");
                Assert(reader.Read().Sessions.Count == 0, "deleted chat returned from its leftover log");
            }
            finally
            {
                string full = Path.GetFullPath(root);
                if (Path.GetDirectoryName(full).TrimEnd(Path.DirectorySeparatorChar) != Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar))
                    throw new InvalidOperationException("Unexpected test cleanup path");
                Directory.Delete(full, true);
            }
        }

        private static void ExecuteSql(string path, string sql)
        {
            IntPtr database;
            Assert(sqlite3_open_v2(Encoding.UTF8.GetBytes(path + "\0"), out database, 6, IntPtr.Zero) == 0, "fixture database open failed");
            try
            {
                Assert(sqlite3_exec(database, Encoding.UTF8.GetBytes(sql + "\0"), IntPtr.Zero, IntPtr.Zero, IntPtr.Zero) == 0, "fixture SQL failed");
            }
            finally { sqlite3_close(database); }
        }

        private static void RunningCatalogMetadata()
        {
            string root = Path.Combine(Path.GetTempPath(), "CodexMonitorTests-" + Guid.NewGuid());
            Directory.CreateDirectory(Path.Combine(root, "sessions"));
            string database = Path.Combine(root, "state_5.sqlite");
            long updated = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds;
            try
            {
                ExecuteSql(database, "CREATE TABLE threads (id TEXT, title TEXT, rollout_path TEXT, source TEXT, model TEXT, archived INTEGER, updated_at INTEGER);");
                for (int i = 0; i < 4; i++)
                {
                    string path = Path.Combine(root, "sessions", "running-" + i + ".jsonl");
                    string events = Record(0, "event_msg", new { type = "task_started" }) + "\n";
                    if (i == 3) events += Record(1, "event_msg", new { type = "task_complete" }) + "\n";
                    File.WriteAllText(path, events);
                    File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddHours(-2));
                    ExecuteSql(database, "INSERT INTO threads VALUES ('live-" + i + "','Prompt " + i + "','" +
                        path.Replace("'", "''") + "','vscode','test-model',0," + updated + ");");
                }
                LocalSessionReader reader = new LocalSessionReader(root);
                SessionReadResult result = reader.Read();
                Assert(result.Error == null && result.Sessions.Count == 3,
                    "open logs with delayed file timestamps hid running sessions");
                Assert(result.Sessions.All(item => item.Session.Id != "live-3"), "recently completed session leaked");
                Assert(result.Sessions.First(item => item.Session.Id == "live-0").Session.Title == "Prompt 0",
                    "older catalogs without name lost their title");

                ExecuteSql(database, "ALTER TABLE threads ADD COLUMN name TEXT;" +
                    "UPDATE threads SET name='Sidebar name' WHERE id='live-0';" +
                    "UPDATE threads SET name='  ' WHERE id='live-1';");
                result = reader.Read();
                Assert(result.Sessions.First(item => item.Session.Id == "live-0").Session.Title == "Sidebar name",
                    "sidebar name did not replace the initial prompt");
                Assert(result.Sessions.First(item => item.Session.Id == "live-1").Session.Title == "Prompt 1",
                    "empty sidebar name did not fall back to title");
                Assert(result.Sessions.First(item => item.Session.Id == "live-2").Session.Title == "Prompt 2",
                    "null sidebar name did not fall back to title");
                ExecuteSql(database, "UPDATE threads SET name='Renamed chat' WHERE id='live-0';");
                Assert(reader.Read().Sessions.First(item => item.Session.Id == "live-0").Session.Title == "Renamed chat",
                    "renaming a running chat did not update its card");
            }
            finally
            {
                string full = Path.GetFullPath(root);
                if (Path.GetDirectoryName(full).TrimEnd(Path.DirectorySeparatorChar) != Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar))
                    throw new InvalidOperationException("Unexpected test cleanup path");
                Directory.Delete(full, true);
            }
        }

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_open_v2(byte[] path, out IntPtr database, int flags, IntPtr vfs);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_close(IntPtr database);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_exec(IntPtr database, byte[] sql, IntPtr callback, IntPtr argument, IntPtr error);
    }
}
