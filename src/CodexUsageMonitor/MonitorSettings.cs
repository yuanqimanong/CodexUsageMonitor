using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace CodexUsageMonitor
{
    internal sealed class MonitorSettings
    {
        public bool HasWindowPosition { get; set; }
        public double WindowLeft { get; set; }
        public double WindowTop { get; set; }
        public bool TopMost { get; set; }
        public string Theme { get; set; }
        public bool? EdgeSnapEnabled { get; set; }

        public static MonitorSettings CreateDefault()
        {
            return new MonitorSettings
            {
                TopMost = true,
                Theme = MonitorTheme.Cyberpunk.ToString(),
                EdgeSnapEnabled = true
            };
        }
    }

    internal sealed class SettingsStore
    {
        private readonly string settingsPath;
        private readonly JavaScriptSerializer serializer = new JavaScriptSerializer();

        public SettingsStore()
        {
            string root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CodexUsageMonitor");
            settingsPath = Path.Combine(root, "settings.json");
        }

        public MonitorSettings Load()
        {
            try
            {
                if (!File.Exists(settingsPath))
                {
                    return MonitorSettings.CreateDefault();
                }

                string json = File.ReadAllText(settingsPath);
                MonitorSettings settings =
                    serializer.Deserialize<MonitorSettings>(json);
                return settings ?? MonitorSettings.CreateDefault();
            }
            catch (Exception)
            {
                return MonitorSettings.CreateDefault();
            }
        }

        public void Save(MonitorSettings settings)
        {
            try
            {
                string directory = Path.GetDirectoryName(settingsPath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string temporaryPath = settingsPath + ".tmp";
                File.WriteAllText(temporaryPath, serializer.Serialize(settings));
                if (File.Exists(settingsPath))
                {
                    File.Replace(temporaryPath, settingsPath, null);
                }
                else
                {
                    File.Move(temporaryPath, settingsPath);
                }
            }
            catch (Exception)
            {
                // Position persistence must never interrupt the monitor.
            }
        }
    }

    internal static class StartupManager
    {
        private const string RunKeyPath =
            @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "CodexUsageMonitor";

        public static bool IsEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
                {
                    if (key == null)
                    {
                        return false;
                    }

                    string configured = Convert.ToString(key.GetValue(ValueName));
                    string executable = System.Reflection.Assembly
                        .GetExecutingAssembly()
                        .Location;
                    string expected =
                        "\"" + executable + "\" --background";
                    return string.Equals(
                        configured,
                        expected,
                        StringComparison.OrdinalIgnoreCase);
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static void SetEnabled(bool enabled)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath))
            {
                if (key == null)
                {
                    throw new InvalidOperationException("无法更新开机启动设置。");
                }

                if (enabled)
                {
                    string executable = System.Reflection.Assembly
                        .GetExecutingAssembly()
                        .Location;
                    key.SetValue(
                        ValueName,
                        "\"" + executable + "\" --background",
                        RegistryValueKind.String);
                }
                else
                {
                    key.DeleteValue(ValueName, false);
                }
            }
        }
    }
}
