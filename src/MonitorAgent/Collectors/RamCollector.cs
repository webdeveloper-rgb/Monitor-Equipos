using System.Linq;
using System.Management;
using MonitorAgent.Models;

namespace MonitorAgent.Collectors
{
    sealed class RamCollector : ICollector
    {
        public string Name => "ram";

        public void Collect(Report report, CollectorContext context)
        {
            report.Ram = Wmi.Query("SELECT * FROM Win32_PhysicalMemory").Select(o => new RamModule
            {
                Slot = o.Str("DeviceLocator"),
                Manufacturer = o.Str("Manufacturer"),
                PartNumber = o.Str("PartNumber"),
                SerialNumber = o.Str("SerialNumber"),
                CapacityBytes = o.ULong("Capacity"),
                SpeedMHz = o.Int("Speed"),
                Type = MemoryType(o)
            }).ToList();
        }

        static string MemoryType(ManagementObject o)
        {
            int? t = o.Int("SMBIOSMemoryType");
            if (t == null || t == 0) t = o.Int("MemoryType");
            switch (t)
            {
                case 20: return "DDR";
                case 21: return "DDR2";
                case 24: return "DDR3";
                case 26: return "DDR4";
                case 34: return "DDR5";
                case null: return null;
                default: return "Unknown(" + t + ")";
            }
        }
    }
}