using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CodexUsageMonitor
{
    internal static class CodexLocator
    {
        private const string OverrideVariable = "CODEX_USAGE_MONITOR_CODEX_EXE";

        public static string FindExecutable()
        {
            string overridePath =
                NormalizePath(Environment.GetEnvironmentVariable(OverrideVariable));
            if (!string.IsNullOrWhiteSpace(overridePath))
            {
                if (IsUsableFile(overridePath))
                {
                    return overridePath;
                }

                throw new FileNotFoundException(
                    "环境变量 " + OverrideVariable +
                    " 指向的 codex.exe 不存在。",
                    overridePath);
            }

            List<string> desktopCandidates = new List<string>();
            string localAppData =
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string codexBinRoot = Path.Combine(localAppData, "OpenAI", "Codex", "bin");
            if (Directory.Exists(codexBinRoot))
            {
                try
                {
                    desktopCandidates.AddRange(
                        Directory.GetFiles(
                            codexBinRoot,
                            "codex.exe",
                            SearchOption.AllDirectories));
                }
                catch (UnauthorizedAccessException)
                {
                    // Continue with the remaining discovery strategies.
                }
                catch (IOException)
                {
                    // The Desktop updater may be rotating a version directory.
                }
            }

            string desktopExecutable = desktopCandidates
                .Where(IsUsableFile)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(GetSafeWriteTime)
                .FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(desktopExecutable))
            {
                return desktopExecutable;
            }

            string path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (string directory in path.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(directory))
                {
                    continue;
                }

                try
                {
                    string candidate = Path.Combine(
                        directory.Trim().Trim('"'),
                        "codex.exe");
                    if (IsUsableFile(candidate))
                    {
                        return candidate;
                    }
                }
                catch (ArgumentException)
                {
                    // Ignore malformed PATH entries.
                }
            }

            throw new FileNotFoundException(
                "找不到 Codex CLI。请先安装并登录 Codex Desktop，" +
                "或设置环境变量 " + OverrideVariable + "。");
        }

        private static string NormalizePath(string path)
        {
            return string.IsNullOrWhiteSpace(path)
                ? null
                : path.Trim().Trim('"');
        }

        private static bool IsUsableFile(string path)
        {
            try
            {
                return File.Exists(path);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static DateTime GetSafeWriteTime(string path)
        {
            try
            {
                return File.GetLastWriteTimeUtc(path);
            }
            catch (Exception)
            {
                return DateTime.MinValue;
            }
        }
    }
}
