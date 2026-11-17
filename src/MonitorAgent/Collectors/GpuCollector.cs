using System.Linq;
using System;
using MonitorAgent.Models;

namespace MonitorAgent.Collectors
{
    sealed class GpuCollector : ICollector
    {
        public string Name => "gpu";

        static readonly string[] RemoteDisplayDrivers =
        {
            "anyviewer", "anydesk", "rustdesk", "supremo", "teamviewer",
            "splashtop", "chrome remote desktop", "connectwise", "screenconnect",
            "ultraviewer", "zoho assist", "logmein", "goto resolve", "meshcentral"
        };

        static bool IsRemoteDisplayDriver(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            return RemoteDisplayDrivers.Any(marker => name.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        public void Collect(Report report, CollectorContext context)
        {
            report.Gpus = Wmi.Query("SELECT * FROM Win32_VideoController").Select(o => new GpuInfo
            {
                Name = o.Str("Name"),
                DriverVersion = o.Str("DriverVersion"),
                AdapterRamBytes = o.ULong("AdapterRAM")
            }).Where(gpu => !IsRemoteDisplayDriver(gpu.Name)).ToList();
        }
    }
}
