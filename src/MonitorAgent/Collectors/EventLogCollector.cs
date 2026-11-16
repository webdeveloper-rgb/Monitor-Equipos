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
        public string Name => "events";

        public void Collect(Report report, CollectorContext context)
        {
            var cfg = context.Config;
            foreach (var logName in cfg.EventLogs)
            {
                try
                {
                    string cursorKey = logName.ToUpperInvariant();
                    if (!context.State.EventLogCursors.TryGetValue(cursorKey, out var cursor) || cursor == null)
                    {
                        cursor = new EventLogCursor
                        {
                            BackfillFromUtc = context.StartedUtc.AddDays(-30),
                            BackfillComplete = false
                        };
                    }

                    DateTime sinceUtc = cursor.BackfillComplete
                        ? context.StartedUtc.AddDays(-30)
                        : cursor.BackfillFromUtc;
                    string since = sinceUtc.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
                    string recordId = cursor.LastRecordId.HasValue
                        ? " and EventRecordID > " + cursor.LastRecordId.Value.ToString(CultureInfo.InvariantCulture)
                        : "";
                    string xpath = "*[System[(Level=1 or Level=2) and TimeCreated[@SystemTime>='" + since + "']" + recordId + "]]";
                    var query = new EventLogQuery(logName, PathType.LogName, xpath) { ReverseDirection = false };
                    using (var reader = new EventLogReader(query))
                    {
                        EventRecord e;
                        int count = 0;
                        long? lastRecordId = null;
                        int maxPerLog = Math.Max(1, cfg.EventsMaxPerLog);
                        while (count < maxPerLog && (e = reader.ReadEvent()) != null)
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
                                    Level = e.Level == 1 ? "Critical" : "Error",
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

            report.Events = report.Events.OrderByDescending(x => x.TimeUtc).ToList();
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