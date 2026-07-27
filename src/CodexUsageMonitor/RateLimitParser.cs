using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CodexUsageMonitor
{
    internal static class RateLimitParser
    {
        private const int MaxBucketIdLength = 96;
        private const int MaxBucketNameLength = 96;
        private const int MaxPlanTypeLength = 64;
        private const int MaxCreditsBalanceLength = 64;
        private const int MaxRateLimitReachedTypeLength = 64;

        public static UsageSnapshot Parse(IDictionary<string, object> response)
        {
            if (response == null)
            {
                throw new InvalidOperationException("Codex returned an empty rate-limit response.");
            }

            UsageSnapshot snapshot = new UsageSnapshot();
            snapshot.FetchedAt = DateTimeOffset.Now;

            IDictionary<string, object> buckets =
                JsonValue.AsObject(JsonValue.Get(response, "rateLimitsByLimitId"));

            if (buckets != null && buckets.Count > 0)
            {
                foreach (KeyValuePair<string, object> bucket in buckets.OrderBy(entry => entry.Key))
                {
                    ParseBucket(snapshot, bucket.Key, JsonValue.AsObject(bucket.Value));
                }
            }
            else
            {
                ParseBucket(
                    snapshot,
                    "codex",
                    JsonValue.AsObject(JsonValue.Get(response, "rateLimits")));
            }

            if (snapshot.MostConstrainedWindow == null &&
                snapshot.UnlimitedCredits != true &&
                snapshot.HasCredits != true &&
                string.IsNullOrWhiteSpace(snapshot.CreditsBalance))
            {
                throw new InvalidOperationException(
                    "Codex did not return a usable rate-limit window. Sign in and try again.");
            }

            snapshot.Windows.Sort(delegate(RateWindow left, RateWindow right)
            {
                int bucketOrder = string.Compare(
                    left.BucketName,
                    right.BucketName,
                    StringComparison.CurrentCultureIgnoreCase);
                if (bucketOrder != 0)
                {
                    return bucketOrder;
                }

                long leftDuration = left.WindowDurationMinutes ?? long.MaxValue;
                long rightDuration = right.WindowDurationMinutes ?? long.MaxValue;
                return leftDuration.CompareTo(rightDuration);
            });

            return snapshot;
        }

        private static void ParseBucket(
            UsageSnapshot snapshot,
            string bucketId,
            IDictionary<string, object> bucket)
        {
            if (bucket == null)
            {
                return;
            }

            bucketId = NormalizeDisplayText(bucketId, MaxBucketIdLength);
            if (string.IsNullOrWhiteSpace(bucketId))
            {
                bucketId = "codex";
            }

            string bucketName = GetDisplayText(
                bucket,
                "limitName",
                MaxBucketNameLength);
            if (string.IsNullOrWhiteSpace(bucketName))
            {
                bucketName = bucketId;
            }

            if (string.IsNullOrWhiteSpace(snapshot.PlanType))
            {
                snapshot.PlanType = GetDisplayText(
                    bucket,
                    "planType",
                    MaxPlanTypeLength);
            }

            if (string.IsNullOrWhiteSpace(snapshot.RateLimitReachedType))
            {
                snapshot.RateLimitReachedType =
                    GetDisplayText(
                        bucket,
                        "rateLimitReachedType",
                        MaxRateLimitReachedTypeLength);
            }

            IDictionary<string, object> credits =
                JsonValue.AsObject(JsonValue.Get(bucket, "credits"));
            if (credits != null)
            {
                if (string.IsNullOrWhiteSpace(snapshot.CreditsBalance))
                {
                    snapshot.CreditsBalance = GetDisplayText(
                        credits,
                        "balance",
                        MaxCreditsBalanceLength);
                }

                snapshot.HasCredits =
                    snapshot.HasCredits ?? JsonValue.GetBoolean(credits, "hasCredits");
                snapshot.UnlimitedCredits =
                    snapshot.UnlimitedCredits ?? JsonValue.GetBoolean(credits, "unlimited");
            }

            AddWindow(snapshot, bucketId, bucketName, "primary", bucket);
            AddWindow(snapshot, bucketId, bucketName, "secondary", bucket);

            IDictionary<string, object> individualLimit =
                JsonValue.AsObject(JsonValue.Get(bucket, "individualLimit"));
            if (individualLimit != null)
            {
                int? remaining = JsonValue.GetInt32(individualLimit, "remainingPercent");
                if (remaining.HasValue)
                {
                    int clampedRemaining = ClampPercent(remaining.Value);
                    snapshot.Windows.Add(new RateWindow
                    {
                        BucketId = bucketId,
                        BucketName = bucketName,
                        WindowName = "个人额度",
                        UsedPercent = 100 - clampedRemaining,
                        RemainingPercent = clampedRemaining,
                        WindowDurationMinutes = null,
                        Kind = RateWindowKind.SpendingLimit,
                        ResetsAt = JsonValue.FromUnixSeconds(
                            JsonValue.GetInt64(individualLimit, "resetsAt"))
                    });
                }
            }
        }

        private static void AddWindow(
            UsageSnapshot snapshot,
            string bucketId,
            string bucketName,
            string key,
            IDictionary<string, object> bucket)
        {
            IDictionary<string, object> window =
                JsonValue.AsObject(JsonValue.Get(bucket, key));
            if (window == null)
            {
                return;
            }

            int? used = JsonValue.GetInt32(window, "usedPercent");
            if (!used.HasValue)
            {
                return;
            }

            int clampedUsed = ClampPercent(used.Value);
            long? duration = JsonValue.GetInt64(window, "windowDurationMins");

            snapshot.Windows.Add(new RateWindow
            {
                BucketId = bucketId,
                BucketName = bucketName,
                WindowName = BuildWindowName(duration, key),
                UsedPercent = clampedUsed,
                RemainingPercent = 100 - clampedUsed,
                WindowDurationMinutes = duration,
                Kind = RateWindowKind.RateLimit,
                ResetsAt = JsonValue.FromUnixSeconds(
                    JsonValue.GetInt64(window, "resetsAt"))
            });
        }

        private static int ClampPercent(int value)
        {
            return Math.Max(0, Math.Min(100, value));
        }

        private static string GetDisplayText(
            IDictionary<string, object> value,
            string key,
            int maxLength)
        {
            return NormalizeDisplayText(
                JsonValue.GetString(value, key),
                maxLength);
        }

        internal static string NormalizeDisplayText(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value) || maxLength <= 0)
            {
                return null;
            }

            StringBuilder normalized = new StringBuilder(
                Math.Min(value.Length, maxLength));
            bool pendingSpace = false;
            bool truncated = false;

            for (int index = 0; index < value.Length; index++)
            {
                char current = value[index];
                if (char.IsWhiteSpace(current) || char.IsControl(current))
                {
                    pendingSpace = normalized.Length > 0;
                    continue;
                }

                UnicodeCategory category = char.GetUnicodeCategory(current);
                if (category == UnicodeCategory.Format)
                {
                    continue;
                }

                int characterLength = 1;
                if (char.IsHighSurrogate(current))
                {
                    if (index + 1 >= value.Length ||
                        !char.IsLowSurrogate(value[index + 1]))
                    {
                        continue;
                    }

                    characterLength = 2;
                }
                else if (char.IsLowSurrogate(current))
                {
                    continue;
                }

                int requiredLength = characterLength + (pendingSpace ? 1 : 0);
                if (normalized.Length + requiredLength > maxLength)
                {
                    truncated = true;
                    break;
                }

                if (pendingSpace)
                {
                    normalized.Append(' ');
                    pendingSpace = false;
                }

                normalized.Append(current);
                if (characterLength == 2)
                {
                    normalized.Append(value[++index]);
                }
            }

            if (normalized.Length == 0)
            {
                return null;
            }

            if (truncated)
            {
                int contentLength = Math.Max(0, maxLength - 1);
                if (normalized.Length > contentLength)
                {
                    normalized.Length = contentLength;
                    if (normalized.Length > 0 &&
                        char.IsHighSurrogate(normalized[normalized.Length - 1]))
                    {
                        normalized.Length--;
                    }
                }

                while (normalized.Length > 0 &&
                    char.IsWhiteSpace(normalized[normalized.Length - 1]))
                {
                    normalized.Length--;
                }

                normalized.Append('…');
            }

            return normalized.ToString();
        }

        private static string BuildWindowName(long? minutes, string fallbackKey)
        {
            if (!minutes.HasValue || minutes.Value <= 0)
            {
                return fallbackKey == "primary" ? "主要窗口" : "次要窗口";
            }

            long value = minutes.Value;
            if (value % 10080 == 0)
            {
                return string.Format(
                    CultureInfo.CurrentCulture,
                    "{0} 周窗口",
                    value / 10080);
            }

            if (value % 1440 == 0)
            {
                return string.Format(
                    CultureInfo.CurrentCulture,
                    "{0} 天窗口",
                    value / 1440);
            }

            if (value % 60 == 0)
            {
                return string.Format(
                    CultureInfo.CurrentCulture,
                    "{0} 小时窗口",
                    value / 60);
            }

            return string.Format(
                CultureInfo.CurrentCulture,
                "{0} 分钟窗口",
                value);
        }
    }
}
