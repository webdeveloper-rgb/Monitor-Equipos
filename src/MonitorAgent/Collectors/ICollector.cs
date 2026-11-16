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
        public CollectorContext(AgentConfig config, AgentState state, DateTime startedUtc)
        {
            Config = config;
            State = state;
            StartedUtc = startedUtc;
        }

        public AgentConfig Config { get; }
        public AgentState State { get; }
        public DateTime StartedUtc { get; }
    }
}