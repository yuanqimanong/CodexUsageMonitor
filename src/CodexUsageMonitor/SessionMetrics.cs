using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Web.Script.Serialization;

namespace CodexUsageMonitor
{
    internal sealed class TokenUsage
    {
        public long? Input { get; set; }
        public long? Cached { get; set; }
        public long? Output { get; set; }
        public long? Reasoning { get; set; }

        public static TokenUsage Parse(object value)
        {
            IDictionary<string, object> data = JsonValue.AsObject(value);
            if (data == null) return null;
            return new TokenUsage
            {
                Input = Count(data, "input_tokens"),
                Cached = Count(data, "cached_input_tokens"),
                Output = Count(data, "output_tokens"),
                Reasoning = Count(data, "reasoning_output_tokens")
            };
        }

        private static long? Count(IDictionary<string, object> data, string key)
        {
            long? count = JsonValue.GetInt64(data, key);
            return count >= 0 ? count : null;
        }

        public bool Matches(TokenUsage other)
        {
            return other != null && Input == other.Input && Cached == other.Cached &&
                Output == other.Output && Reasoning == other.Reasoning;
        }
    }

    internal sealed class SpeedSample
    {
        public DateTimeOffset Timestamp { get; set; }
        public TokenUsage Usage { get; set; }
        public double? Seconds { get; set; }
        public double? TokensPerSecond
        {
            get { return Seconds > 0 && Usage.Output.HasValue ? Usage.Output / Seconds : null; }
        }
    }

    internal sealed class SessionMetrics
    {
        public TokenUsage Total { get; set; }
        public string Model { get; set; }
        public string State { get; set; }
        public bool Incomplete { get; set; }
        public bool IsRunning { get; set; }
        public List<SpeedSample> Samples { get; set; }

    }

    // Rollout timestamps are client observations. Keep unknown intervals out of averages.
    internal sealed class SessionMetricsParser
    {
        private readonly JavaScriptSerializer serializer = new JavaScriptSerializer
        {
            MaxJsonLength = 2 * 1024 * 1024,
            RecursionLimit = 128
        };
        private readonly List<SpeedSample> samples = new List<SpeedSample>();
        private readonly HashSet<string> pendingTools = new HashSet<string>();
        private TokenUsage total;
        private DateTimeOffset? requestStart;
        private DateTimeOffset? responseEnd;
        private DateTimeOffset? lastEvent;
        private string lastResponseId;
        private string model;
        private string state = "等待用量记录";
        private bool incomplete;
        private bool active;
        private bool lifecycleObserved;

        public void MarkIncomplete()
        {
            incomplete = true;
            requestStart = null;
            responseEnd = null;
            pendingTools.Clear();
        }

        public void ReadLine(string line)
        {
            IDictionary<string, object> record;
            try
            {
                record = JsonValue.AsObject(serializer.DeserializeObject(line));
            }
            catch (ArgumentException) { MarkIncomplete(); return; }
            catch (InvalidOperationException) { MarkIncomplete(); return; }
            if (record == null) return;
            IDictionary<string, object> payload = JsonValue.AsObject(JsonValue.Get(record, "payload"));
            string recordType = JsonValue.GetString(record, "type");
            string type = JsonValue.GetString(payload, "type");
            DateTimeOffset timestamp;
            if (!DateTimeOffset.TryParse(JsonValue.GetString(record, "timestamp"),
                CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out timestamp))
            {
                MarkIncomplete();
                return;
            }
            lastEvent = timestamp;

            if (recordType == "turn_context")
            {
                model = JsonValue.GetString(payload, "model") ?? model;
            }
            else if (recordType == "event_msg")
            {
                if (type == "task_started" || type == "turn_started")
                {
                    requestStart = timestamp;
                    responseEnd = null;
                    pendingTools.Clear();
                    active = true;
                    lifecycleObserved = true;
                    nextRequestStart = null;
                    state = "正在响应";
                }
                else if (type == "task_complete" || type == "turn_complete" || type == "turn_aborted")
                {
                    requestStart = null;
                    responseEnd = null;
                    pendingTools.Clear();
                    active = false;
                    lifecycleObserved = true;
                    nextRequestStart = null;
                    state = type == "turn_aborted" ? "已中断" : "已完成";
                }
                else if (type == "token_count")
                {
                    IDictionary<string, object> info = JsonValue.AsObject(JsonValue.Get(payload, "info"));
                    AcceptUsage(TokenUsage.Parse(JsonValue.Get(info, "total_token_usage")),
                        TokenUsage.Parse(JsonValue.Get(info, "last_token_usage")), timestamp);
                }
            }
            else if (recordType == "token_usage_record")
            {
                string responseId = JsonValue.GetString(payload, "response_id");
                if (responseId != null && responseId == lastResponseId) return;
                lastResponseId = responseId;
                AcceptUsage(TokenUsage.Parse(JsonValue.Get(payload, "thread_token_usage")),
                    TokenUsage.Parse(JsonValue.Get(payload, "usage")), timestamp);
            }
            else if (recordType == "response_item")
            {
                if (type == "function_call" || type == "custom_tool_call")
                {
                    // The first tool dispatch closes the model interval, including parallel calls.
                    if (!responseEnd.HasValue) responseEnd = timestamp;
                    string id = JsonValue.GetString(payload, "call_id");
                    if (!string.IsNullOrEmpty(id)) pendingTools.Add(id);
                    state = "工具执行中";
                }
                else if (type == "function_call_output" || type == "custom_tool_call_output")
                {
                    string id = JsonValue.GetString(payload, "call_id");
                    pendingTools.Remove(id ?? string.Empty);
                    if (pendingTools.Count == 0)
                    {
                        // Older logs publish usage after tool output. Keep the previous interval
                        // until that usage arrives, then use this timestamp for the next request.
                        nextRequestStart = timestamp;
                        if (!responseEnd.HasValue) requestStart = timestamp;
                        state = "正在响应";
                    }
                }
                else if (type == "message" && JsonValue.GetString(payload, "role") == "user")
                {
                    if (!active)
                    {
                        requestStart = timestamp;
                        responseEnd = null;
                        active = true;
                        state = "正在响应";
                    }
                }
            }
            else if (recordType == "compacted")
            {
                // Compaction can change counters and introduces an unobservable request boundary.
                requestStart = null;
                responseEnd = null;
            }
        }

        private DateTimeOffset? nextRequestStart;

        private void AcceptUsage(TokenUsage cumulative, TokenUsage usage, DateTimeOffset timestamp)
        {
            if (cumulative == null) return;
            if (!lifecycleObserved) active = true;
            if (cumulative.Matches(total)) return;

            bool reset = total != null &&
                (cumulative.Input < total.Input || cumulative.Output < total.Output);
            if (reset) incomplete = true;
            total = cumulative;
            if (usage == null) return;

            DateTimeOffset end = responseEnd ?? timestamp;
            double seconds = requestStart.HasValue ? (end - requestStart.Value).TotalSeconds : 0;
            samples.Add(new SpeedSample
            {
                Timestamp = timestamp,
                Usage = usage,
                Seconds = !reset && seconds >= 0.05 ? (double?)seconds : null
            });
            if (samples.Count > 10) samples.RemoveAt(0);
            requestStart = pendingTools.Count == 0 ? (DateTimeOffset?)(nextRequestStart ?? timestamp) : null;
            nextRequestStart = null;
            responseEnd = null;
            if (state == "等待用量记录") state = "已记录";
        }

        public SessionMetrics Snapshot()
        {
            return Snapshot(DateTimeOffset.UtcNow);
        }

        internal SessionMetrics Snapshot(DateTimeOffset now)
        {
            string currentState = state;
            bool recent = lastEvent.HasValue && (now - lastEvent.Value).TotalMinutes <= 5;
            if (active && !recent)
                currentState = "暂无新记录";
            return new SessionMetrics
            {
                Total = total,
                Model = model,
                State = currentState,
                Incomplete = incomplete,
                IsRunning = active && recent,
                Samples = samples.ToList()
            };
        }
    }
}
