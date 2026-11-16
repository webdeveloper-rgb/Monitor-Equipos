using System;
using MonitorAgent.Infrastructure;
using MonitorAgent.Models;

namespace MonitorAgent.Collectors
{
    interface ICollector
    {
        string Name { get; }
        void Collect(Report report, CollectorContext context);
    }

    sealed class CollectorContext
    {
        public CollectorContext(AgentConfig config, DateTime eventsSinceUtc)
        {
            Config = config;
            EventsSinceUtc = eventsSinceUtc;
        }

        public AgentConfig Config { get; }
        public DateTime EventsSinceUtc { get; }
    }
}