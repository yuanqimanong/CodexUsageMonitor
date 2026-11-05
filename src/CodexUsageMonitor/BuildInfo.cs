using System.Reflection;

[assembly: AssemblyTitle("Codex Usage Monitor")]
[assembly: AssemblyDescription("A lightweight Windows usage monitor for Codex Desktop")]
[assembly: AssemblyCompany("Codex Usage Monitor contributors")]
[assembly: AssemblyProduct("Codex Usage Monitor")]
[assembly: AssemblyCopyright("Copyright © 2026")]
[assembly: AssemblyVersion("1.2.0.0")]
[assembly: AssemblyFileVersion("1.2.0.0")]

namespace CodexUsageMonitor
{
    internal static class BuildInfo
    {
        public const string Version = "1.2.0";

        public static string ProductName
        {
            get { return "Codex Usage Monitor"; }
        }
    }
}
