using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace CodexUsageMonitor
{
    internal sealed class LocalSession
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Path { get; set; }
        public string Model { get; set; }
        public long UpdatedAtUnixSeconds { get; set; }
    }

    // Windows 10/11 provide winsqlite3. Open the live catalog read-only, including its WAL.
    internal static class LocalThreadCatalog
    {
        public static List<LocalSession> Read(string home)
        {
            string databasePath = Directory.GetFiles(home, "state_*.sqlite")
                .OrderByDescending(path => Version(path)).FirstOrDefault();
            if (databasePath == null) throw new InvalidDataException("没有找到 Codex 会话清单，请选择包含 state_*.sqlite 的数据目录。");
            IntPtr database;
            int opened = sqlite3_open_v2(Utf8(databasePath), out database, 1, IntPtr.Zero);
            try
            {
                if (opened != 0) throw new IOException("无法读取 Codex 会话清单。");
                sqlite3_busy_timeout(database, 250);
                IntPtr statement;
                string displayName = HasNameColumn(database)
                    ? "CASE WHEN name IS NULL OR TRIM(name) = '' THEN title ELSE name END"
                    : "title";
                string query = "SELECT id, " + displayName + ", rollout_path, source, model, updated_at FROM threads " +
                    "WHERE archived = 0 AND source NOT LIKE '%\"subagent\"%' ORDER BY updated_at DESC";
                if (sqlite3_prepare_v2(database, Utf8(query), -1, out statement, IntPtr.Zero) != 0)
                    throw new InvalidDataException("当前 Codex 会话清单格式暂不支持。");
                try
                {
                    List<LocalSession> sessions = new List<LocalSession>();
                    string sessionRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(home, "sessions")) + System.IO.Path.DirectorySeparatorChar;
                    int status;
                    while ((status = sqlite3_step(statement)) == 100)
                    {
                        string path = NormalizeRolloutPath(Text(statement, 2));
                        if (path == null || !path.StartsWith(sessionRoot, StringComparison.OrdinalIgnoreCase)) continue;
                        string id = Text(statement, 0);
                        string title = Text(statement, 1);
                        long updatedAt;
                        long.TryParse(Text(statement, 5), out updatedAt);
                        sessions.Add(new LocalSession
                        {
                            Id = id,
                            Title = string.IsNullOrWhiteSpace(title) ? "未命名会话" : title,
                            Path = path,
                            Model = Text(statement, 4),
                            UpdatedAtUnixSeconds = updatedAt
                        });
                    }
                    if (status != 101) throw new IOException("Codex 会话清单正忙，将稍后重试。");
                    return sessions;
                }
                finally { sqlite3_finalize(statement); }
            }
            finally { if (database != IntPtr.Zero) sqlite3_close(database); }
        }

        private static int Version(string path)
        {
            int version;
            return int.TryParse(System.IO.Path.GetFileNameWithoutExtension(path).Substring(6), out version) ? version : 0;
        }

        private static bool HasNameColumn(IntPtr database)
        {
            IntPtr statement;
            if (sqlite3_prepare_v2(database, Utf8("PRAGMA table_info(threads)"), -1, out statement, IntPtr.Zero) != 0)
                throw new IOException("无法读取 Codex 会话清单字段。");
            try
            {
                int status;
                while ((status = sqlite3_step(statement)) == 100)
                    if (string.Equals(Text(statement, 1), "name", StringComparison.OrdinalIgnoreCase)) return true;
                if (status != 101) throw new IOException("Codex 会话清单正忙，将稍后重试。");
                return false;
            }
            finally { sqlite3_finalize(statement); }
        }

        private static string NormalizeRolloutPath(string path)
        {
            if (string.IsNullOrEmpty(path) || !path.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase)) return null;
            // Codex can persist Win32 extended paths that .NET Framework 4 rejects.
            if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
                path = @"\\" + path.Substring(8);
            else if (path.StartsWith(@"\\?\", StringComparison.Ordinal))
                path = path.Substring(4);
            try { return System.IO.Path.GetFullPath(path); }
            catch (ArgumentException) { return null; }
            catch (NotSupportedException) { return null; }
            catch (PathTooLongException) { return null; }
        }

        private static byte[] Utf8(string text) { return Encoding.UTF8.GetBytes(text + "\0"); }

        private static string Text(IntPtr statement, int column)
        {
            IntPtr pointer = sqlite3_column_text(statement, column);
            if (pointer == IntPtr.Zero) return null;
            int length = sqlite3_column_bytes(statement, column);
            if (length > 32768) throw new InvalidDataException("会话清单字段过长。");
            byte[] bytes = new byte[length];
            Marshal.Copy(pointer, bytes, 0, length);
            return Encoding.UTF8.GetString(bytes);
        }

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_open_v2(byte[] path, out IntPtr database, int flags, IntPtr vfs);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_close(IntPtr database);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_busy_timeout(IntPtr database, int milliseconds);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_prepare_v2(IntPtr database, byte[] sql, int bytes, out IntPtr statement, IntPtr tail);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_step(IntPtr statement);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_finalize(IntPtr statement);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr sqlite3_column_text(IntPtr statement, int column);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_column_bytes(IntPtr statement, int column);
    }
}
