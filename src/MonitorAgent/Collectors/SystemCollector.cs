using System.Linq;
using System.Management;
using MonitorAgent.Models;

namespace MonitorAgent.Collectors
{
    sealed class SystemCollector : ICollector
    {
        public string Name => "system";

        public void Collect(Report report, CollectorContext context)
        {
            var s = new SystemInfo();

            var os = Wmi.Query("SELECT * FROM Win32_OperatingSystem").FirstOrDefault();
            if (os != null)
            {
                s.OsName = os.Str("Caption");
                s.OsVersion = os.Str("Version");
                s.OsArchitecture = os.Str("OSArchitecture");
                var boot = os.Str("LastBootUpTime");
                if (boot != null)
                    s.LastBootUtc = ManagementDateTimeConverter.ToDateTime(boot).ToUniversalTime();
            }

            var cs = Wmi.Query("SELECT * FROM Win32_ComputerSystem").FirstOrDefault();
            if (cs != null)
            {
                s.Manufacturer = cs.Str("Manufacturer");
                s.Model = cs.Str("Model");
                s.Domain = cs.Str("Domain");
                s.TotalRamBytes = cs.ULong("TotalPhysicalMemory");
            }

            s.SerialNumber = Wmi.Query("SELECT SerialNumber FROM Win32_BIOS").FirstOrDefault()?.Str("SerialNumber");

            report.System = s;
            report.HardwareUuid = Wmi.Query("SELECT UUID FROM Win32_ComputerSystemProduct").FirstOrDefault()?.Str("UUID");
        }
    }
}