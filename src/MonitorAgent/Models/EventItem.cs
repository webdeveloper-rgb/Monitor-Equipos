using System;

namespace MonitorAgent.Models
{
    public class EventItem
    {
        public long? RecordId { get; set; }
        public DateTime? TimeUtc { get; set; }
        public string Log { get; set; }
        public string Source { get; set; }
        public int EventId { get; set; }
        public string Level { get; set; }
        public string Message { get; set; }
    }
}