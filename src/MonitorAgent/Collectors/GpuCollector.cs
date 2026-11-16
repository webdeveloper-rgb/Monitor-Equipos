using System.Linq;
using MonitorAgent.Models;

namespace MonitorAgent.Collectors
{
    sealed class GpuCollector : ICollector
    {
        public string Name => "gpu";

        public void Collect(Report report, CollectorContext context)
        {
            report.Gpus = Wmi.Query("SELECT * FROM Win32_VideoController").Select(o => new GpuInfo
            {
                Name = o.Str("Name"),
                DriverVersion = o.Str("DriverVersion"),
                AdapterRamBytes = o.ULong("AdapterRAM")
            }).ToList();
        }
    }
}