using System;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.Linq;
using MonitorAgent.Infrastructure;
using MonitorAgent.Models;

namespace MonitorAgent.Collectors
{
    sealed class EventLogCollector : ICollector
    {
        const int CursorVersion = 2;
        const int MaxEventsPerReport = 500;

        public string Name => "events";

        public void Collect(Report report, CollectorContext context)
        {
            var cfg = context.Config;
            bool fullAudit = context.State.EventExtractionMode == "full";
            string mode = fullAudit ? "full" : "standard";
            var logNames = fullAudit
                ? EventLogSession.GlobalSession.GetLogNames()
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .ToList()
                : (cfg.EventLogs ?? new string[0])
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

            if (logNames.Count == 0)
                return;

            int startIndex = fullAudit ? context.State.EventLogScanOffset : 0;
            if (startIndex < 0 || startIndex >= logNames.Count)
                startIndex = 0;

            int logsProcessed = 0;
            for (int offset = 0; offset < logNames.Count && report.Events.Count < MaxEventsPerReport; offset++)
            {
                string logName = logNames[(startIndex + offset) % logNames.Count];
                logsProcessed = offset + 1;
                try
                {
                    string cursorKey = logName.ToUpperInvariant();
                    if (!context.State.EventLogCursors.TryGetValue(cursorKey, out var cursor)
                        || cursor == null
                        || cursor.Version != CursorVersion
                        || cursor.Mode != mode)
                    {
                        cursor = new EventLogCursor
                        {
                            BackfillFromUtc = context.StartedUtc.AddDays(-30),
                            BackfillComplete = false,
                            Mode = mode,
                            Version = CursorVersion
                        };
                    }

                    DateTime sinceUtc = cursor.BackfillComplete
                        ? context.StartedUtc.AddDays(-30)
                        : cursor.BackfillFromUtc;
                    string timeFilter = fullAudit
                        ? ""
                        : " and TimeCreated[@SystemTime>='" + sinceUtc.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture) + "']";
                    string recordId = cursor.LastRecordId.HasValue
                        ? " and EventRecordID > " + cursor.LastRecordId.Value.ToString(CultureInfo.InvariantCulture)
                        : "";
                    string xpath = fullAudit
                        ? cursor.LastRecordId.HasValue
                            ? "*[System[EventRecordID > " + cursor.LastRecordId.Value.ToString(CultureInfo.InvariantCulture) + "]]"
                            : "*"
                        : "*[System[(Level=1 or Level=2)" + timeFilter + recordId + "]]";
                    var query = new EventLogQuery(logName, PathType.LogName, xpath) { ReverseDirection = false };
                    using (var reader = new EventLogReader(query))
                    {
                        EventRecord e;
                        int count = 0;
                        long? lastRecordId = null;
                        int remaining = MaxEventsPerReport - report.Events.Count;
                        int batchLimit = Math.Min(Math.Max(1, cfg.EventsMaxPerLog), remaining);
                        while (count < batchLimit && (e = reader.ReadEvent()) != null)
                        {
                            using (e)
                            {
                                if (!e.RecordId.HasValue)
                                    throw new InvalidOperationException("El evento no tiene RecordId.");

                                report.Events.Add(new EventItem
                                {
                                    RecordId = e.RecordId,
                                    TimeUtc = e.TimeCreated?.ToUniversalTime(),
                                    Log = logName,
                                    Source = e.ProviderName,
                                    EventId = e.Id,
                                    Level = GetLevelName(e),
                                    Message = SafeMessage(e)
                                });
                                lastRecordId = e.RecordId;
                            }
                            count++;
                        }

                        bool hasMore;
                        using (var next = reader.ReadEvent())
                            hasMore = next != null;

                        bool logReset = false;
                        if (lastRecordId.HasValue)
                        {
                            cursor.LastRecordId = lastRecordId;
                        }
                        else
                        {
                            long? latestRecordId = ReadLatestRecordId(logName);
                            if (cursor.LastRecordId.HasValue
                                && latestRecordId.HasValue
                                && latestRecordId.Value < cursor.LastRecordId.Value)
                            {
                                cursor.LastRecordId = null;
                                cursor.BackfillFromUtc = context.StartedUtc.AddDays(-30);
                                cursor.BackfillComplete = false;
                                logReset = true;
                            }
                            else if (!cursor.LastRecordId.HasValue)
                            {
                                cursor.LastRecordId = latestRecordId ?? 0;
                            }
                        }

                        if (!hasMore && !logReset)
                            cursor.BackfillComplete = true;
                        context.State.EventLogCursors[cursorKey] = cursor;
                    }
                }
                catch (Exception ex)
                {
                    report.CollectionErrors.Add("events[" + logName + "]: " + ex.Message);
                }
            }

            context.State.EventLogScanOffset = fullAudit
                ? (startIndex + logsProcessed) % logNames.Count
                : 0;
            report.Events = report.Events.OrderByDescending(x => x.TimeUtc).ToList();
        }

        static string GetLevelName(EventRecord e)
        {
            if (!e.Level.HasValue)
                return "Unknown";
            switch (e.Level.Value)
            {
                case 0: return "LogAlways";
                case 1: return "Critical";
                case 2: return "Error";
                case 3: return "Warning";
                case 4: return "Information";
                case 5: return "Verbose";
                default: return "Level " + e.Level.Value.ToString(CultureInfo.InvariantCulture);
            }
        }

        static long? ReadLatestRecordId(string logName)
        {
            var query = new EventLogQuery(logName, PathType.LogName, "*") { ReverseDirection = true };
            using (var reader = new EventLogReader(query))
            using (var record = reader.ReadEvent())
                return record?.RecordId;
        }

        static string SafeMessage(EventRecord e)
        {
            string msg = null;
            try { msg = e.FormatDescription(); } catch { }
            if (string.IsNullOrEmpty(msg))
            {
                try { msg = string.Join(" | ", e.Properties.Select(p => p.Value)); } catch { }
            }
            if (msg != null && msg.Length > 2000) msg = msg.Substring(0, 2000);
            return msg;
        }
    }
}
