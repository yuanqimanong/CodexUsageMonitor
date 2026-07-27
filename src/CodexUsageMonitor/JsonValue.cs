using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;

namespace CodexUsageMonitor
{
    internal static class JsonValue
    {
        public static IDictionary<string, object> AsObject(object value)
        {
            IDictionary<string, object> dictionary =
                value as IDictionary<string, object>;
            return dictionary;
        }

        public static IEnumerable<KeyValuePair<string, object>> ObjectEntries(object value)
        {
            IDictionary<string, object> dictionary = AsObject(value);
            if (dictionary != null)
            {
                return dictionary;
            }

            return EmptyEntries;
        }

        public static object Get(IDictionary<string, object> value, string key)
        {
            object result;
            if (value != null && value.TryGetValue(key, out result))
            {
                return result;
            }

            return null;
        }

        public static string GetString(IDictionary<string, object> value, string key)
        {
            object raw = Get(value, key);
            return raw == null
                ? null
                : Convert.ToString(raw, CultureInfo.InvariantCulture);
        }

        public static int? GetInt32(IDictionary<string, object> value, string key)
        {
            object raw = Get(value, key);
            if (raw == null)
            {
                return null;
            }

            int parsed;
            if (int.TryParse(
                Convert.ToString(raw, CultureInfo.InvariantCulture),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out parsed))
            {
                return parsed;
            }

            return null;
        }

        public static long? GetInt64(IDictionary<string, object> value, string key)
        {
            object raw = Get(value, key);
            if (raw == null)
            {
                return null;
            }

            long parsed;
            if (long.TryParse(
                Convert.ToString(raw, CultureInfo.InvariantCulture),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out parsed))
            {
                return parsed;
            }

            return null;
        }

        public static bool? GetBoolean(IDictionary<string, object> value, string key)
        {
            object raw = Get(value, key);
            if (raw == null)
            {
                return null;
            }

            bool parsed;
            if (bool.TryParse(Convert.ToString(raw, CultureInfo.InvariantCulture), out parsed))
            {
                return parsed;
            }

            return null;
        }

        public static DateTimeOffset? FromUnixSeconds(long? value)
        {
            if (!value.HasValue)
            {
                return null;
            }

            try
            {
                return UnixEpoch.AddSeconds(value.Value);
            }
            catch (ArgumentOutOfRangeException)
            {
                return null;
            }
        }

        private static readonly KeyValuePair<string, object>[] EmptyEntries =
            new KeyValuePair<string, object>[0];

        private static readonly DateTimeOffset UnixEpoch =
            new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero);
    }
}
