using System.Linq;
using MonitorAgent.Models;

namespace MonitorAgent.Collectors
{
    sealed class CpuCollector : ICollector
    {
        public string Name => "cpu";

        public void Collect(Report report, CollectorContext context)
        {
            report.Cpus = Wmi.Query("SELECT * FROM Win32_Processor").Select(o => new CpuInfo
            {
                Name = o.Str("Name"),
                Manufacturer = o.Str("Manufacturer"),
                Cores = o.Int("NumberOfCores"),
                LogicalProcessors = o.Int("NumberOfLogicalProcessors"),
                MaxClockMHz = o.Int("MaxClockSpeed"),
                LoadPercent = o.Int("LoadPercentage")
            }).ToList();
        }
    }
}