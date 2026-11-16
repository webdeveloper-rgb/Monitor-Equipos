using System;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.Linq;
using MonitorAgent.Models;

namespace MonitorAgent.Collectors
{
    sealed class EventLogCollector : ICollector
    {
        public string Name => "events";

        public void Collect(Report report, CollectorContext context)
        {
            var cfg = context.Config;
            string since = context.EventsSinceUtc.ToString("yyyy-MM-dd'T'HH:mm:ss.000'Z'", CultureInfo.InvariantCulture);
            // Level 1 = Critico, 2 = Error
            string xpath = "*[System[(Level=1 or Level=2) and TimeCreated[@SystemTime>='" + since + "']]]";

            foreach (var logName in cfg.EventLogs)
            {
                try
                {
                    var query = new EventLogQuery(logName, PathType.LogName, xpath) { ReverseDirection = true };
                    using (var reader = new EventLogReader(query))
                    {
                        EventRecord e;
                        int count = 0;
                        while (count < cfg.EventsMaxPerLog && (e = reader.ReadEvent()) != null)
                        {
                            using (e)
                            {
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
                            }
                            count++;
                        }
                    }
                }
                catch (Exception ex)
                {
                    report.CollectionErrors.Add("events[" + logName + "]: " + ex.Message);
                }
            }

            report.Events = report.Events.OrderByDescending(x => x.TimeUtc).ToList();
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