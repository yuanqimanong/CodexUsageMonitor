using System;
using System.Collections.Generic;
using System.Linq;

namespace CodexUsageMonitor
{
    internal enum RateWindowKind
    {
        RateLimit,
        SpendingLimit
    }

    internal sealed class RateWindow
    {
        public string BucketId { get; set; }
        public string BucketName { get; set; }
        public string WindowName { get; set; }
        public int UsedPercent { get; set; }
        public int RemainingPercent { get; set; }
        public long? WindowDurationMinutes { get; set; }
        public DateTimeOffset? ResetsAt { get; set; }
        public RateWindowKind Kind { get; set; }

        public string Identity
        {
            get
            {
                return string.Format(
                    "{0}:{1}:{2}",
                    BucketId ?? string.Empty,
                    WindowName ?? string.Empty,
                    WindowDurationMinutes.HasValue
                        ? WindowDurationMinutes.Value.ToString()
                        : string.Empty);
            }
        }
    }

    internal sealed class UsageSnapshot
    {
        public UsageSnapshot()
        {
            Windows = new List<RateWindow>();
        }

        public string PlanType { get; set; }
        public string CreditsBalance { get; set; }
        public bool? HasCredits { get; set; }
        public bool? UnlimitedCredits { get; set; }
        public string RateLimitReachedType { get; set; }
        public DateTimeOffset FetchedAt { get; set; }
        public List<RateWindow> Windows { get; private set; }

        public RateWindow MostConstrainedWindow
        {
            get
            {
                return Windows
                    .Where(window => window.Kind == RateWindowKind.RateLimit)
                    .OrderBy(window => window.RemainingPercent)
                    .ThenBy(window => window.ResetsAt ?? DateTimeOffset.MaxValue)
                    .FirstOrDefault();
            }
        }
    }

    internal sealed class MonitorStatus
    {
        public MonitorStatus(string message, bool isConnected, bool isStale)
        {
            Message = message;
            IsConnected = isConnected;
            IsStale = isStale;
        }

        public string Message { get; private set; }
        public bool IsConnected { get; private set; }
        public bool IsStale { get; private set; }
    }
}
