using System;
using System.Collections.Generic;
using System.Linq;
using MonitorAgent.Collectors;
using MonitorAgent.Infrastructure;
using MonitorAgent.Models;

namespace MonitorAgent.Service
{
    sealed class ReportBuilder
    {
        readonly IReadOnlyList<ICollector> _collectors;

        public ReportBuilder(IEnumerable<ICollector> collectors)
        {
            _collectors = collectors.ToList();
        }

        // Para añadir un colector nuevo: crear la clase y añadirla aqui.
        public static ReportBuilder CreateDefault() => new ReportBuilder(new ICollector[]
        {
            new SystemCollector(),
            new BoardCollector(),
            new CpuCollector(),
            new RamCollector(),
            new GpuCollector(),
            new DiskCollector(),
            new SmartCollector(),
            new VolumeCollector(),
            new BatteryCollector(),
            new EventLogCollector()
        });

        public Report Build(AgentState state, CollectorContext context)
        {
            var report = new Report
            {
                AgentVersion = AppInfo.Version,
                MachineId = state.MachineId,
                Hostname = Environment.MachineName,
                TimestampUtc = DateTime.UtcNow
            };

            // Cada colector va aislado: si uno falla, el resto del informe se envia igual.
            foreach (var collector in _collectors)
            {
                try { collector.Collect(report, context); }
                catch (Exception ex)
                {
                    report.CollectionErrors.Add(collector.Name + ": " + ex.Message);
                    Log.Error(collector.Name + ": " + ex.Message);
                }
            }
            return report;
        }
    }
}