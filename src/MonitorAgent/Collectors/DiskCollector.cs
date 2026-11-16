using System.Management;
using MonitorAgent.Models;

namespace MonitorAgent.Collectors
{
    sealed class DiskCollector : ICollector
    {
        const string StorageNamespace = @"root\Microsoft\Windows\Storage";

        public string Name => "disks";

        public void Collect(Report report, CollectorContext context)
        {
            foreach (var d in Wmi.Query(StorageNamespace, "SELECT * FROM MSFT_PhysicalDisk"))
            {
                var info = new DiskInfo
                {
                    Model = d.Str("FriendlyName"),
                    SerialNumber = d.Str("SerialNumber"),
                    FirmwareVersion = d.Str("FirmwareVersion"),
                    MediaType = MediaType(d.Int("MediaType")),
                    BusType = BusType(d.Int("BusType")),
                    SizeBytes = d.ULong("Size"),
                    HealthStatus = Health(d.Int("HealthStatus"))
                };

                try
                {
                    foreach (ManagementObject r in d.GetRelated("MSFT_StorageReliabilityCounter"))
                    {
                        var wear = r.Int("Wear");
                        if (wear != null && wear >= 0 && wear <= 100)
                        {
                            info.WearPercent = wear;
                            info.LifeLeftPercent = 100 - wear;
                        }
                        info.TemperatureC = r.Int("Temperature");
                        info.PowerOnHours = r.ULong("PowerOnHours");
                        info.ReadErrorsTotal = r.ULong("ReadErrorsTotal");
                        info.WriteErrorsTotal = r.ULong("WriteErrorsTotal");
                    }
                }
                catch { /* disco sin contadores de fiabilidad (USB, RAID, etc.) */ }

                report.Disks.Add(info);
            }
        }

        static string MediaType(int? v)
        {
            switch (v) { case 3: return "HDD"; case 4: return "SSD"; case 5: return "SCM"; default: return "Unspecified"; }
        }

        static string BusType(int? v)
        {
            switch (v)
            {
                case 1: return "SCSI"; case 3: return "ATA"; case 7: return "USB"; case 8: return "RAID";
                case 10: return "SAS"; case 11: return "SATA"; case 12: return "SD"; case 13: return "MMC";
                case 14: return "Virtual"; case 16: return "StorageSpaces"; case 17: return "NVMe";
                case null: return null;
                default: return "Other(" + v + ")";
            }
        }

        static string Health(int? v)
        {
            switch (v) { case 0: return "Healthy"; case 1: return "Warning"; case 2: return "Unhealthy"; default: return "Unknown"; }
        }
    }
}