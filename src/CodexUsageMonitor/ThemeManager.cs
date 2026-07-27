using System;
using System.Linq;
using System.Windows;

namespace CodexUsageMonitor
{
    internal enum MonitorTheme
    {
        Minimal,
        Glass,
        Cyberpunk
    }

    internal static class ThemeManager
    {
        public static MonitorTheme Parse(string value)
        {
            MonitorTheme theme;
            return Enum.TryParse(value, true, out theme)
                ? theme
                : MonitorTheme.Cyberpunk;
        }

        public static MonitorTheme Next(MonitorTheme current)
        {
            switch (current)
            {
                case MonitorTheme.Minimal:
                    return MonitorTheme.Glass;
                case MonitorTheme.Glass:
                    return MonitorTheme.Cyberpunk;
                default:
                    return MonitorTheme.Minimal;
            }
        }

        public static string GetDisplayName(MonitorTheme theme)
        {
            switch (theme)
            {
                case MonitorTheme.Glass:
                    return "透明毛玻璃";
                case MonitorTheme.Cyberpunk:
                    return "赛博朋克科技";
                default:
                    return "黑白极简";
            }
        }

        public static void Apply(Window window, MonitorTheme theme)
        {
            ResourceDictionary resources = Application.Current.Resources;
            ResourceDictionary previous = resources.MergedDictionaries
                .FirstOrDefault(dictionary =>
                    dictionary.Source != null &&
                    dictionary.Source.OriginalString.IndexOf(
                        "/Themes/",
                        StringComparison.OrdinalIgnoreCase) >= 0);

            ResourceDictionary replacement = new ResourceDictionary
            {
                Source = GetResourceUri(theme)
            };

            if (previous != null)
            {
                int index = resources.MergedDictionaries.IndexOf(previous);
                resources.MergedDictionaries[index] = replacement;
            }
            else
            {
                resources.MergedDictionaries.Insert(0, replacement);
            }

            WindowBackdrop.Apply(window, theme == MonitorTheme.Glass);
        }

        private static Uri GetResourceUri(MonitorTheme theme)
        {
            string name;
            switch (theme)
            {
                case MonitorTheme.Glass:
                    name = "Glass";
                    break;
                case MonitorTheme.Cyberpunk:
                    name = "Cyberpunk";
                    break;
                default:
                    name = "Minimal";
                    break;
            }

            return new Uri(
                "/CodexUsageMonitor;component/Themes/" + name + ".xaml",
                UriKind.RelativeOrAbsolute);
        }
    }
}
