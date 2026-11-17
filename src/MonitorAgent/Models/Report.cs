using System;
using System.Collections.Generic;

namespace MonitorAgent.Models
{
    public class Report
    {
        public string AgentVersion { get; set; }
        public string MachineId { get; set; }
        public string HardwareUuid { get; set; }
        public string Hostname { get; set; }
        public string RustDeskId { get; set; }
        public string EventExtractionMode { get; set; }
        public DateTime TimestampUtc { get; set; }

        public SystemInfo System { get; set; }
        public SoftwareInventory Software { get; set; }
        public BoardInfo Board { get; set; }
        public List<CpuInfo> Cpus { get; set; } = new List<CpuInfo>();
        public List<RamModule> Ram { get; set; } = new List<RamModule>();
        public List<GpuInfo> Gpus { get; set; } = new List<GpuInfo>();
        public List<DiskInfo> Disks { get; set; } = new List<DiskInfo>();
        public List<VolumeInfo> Volumes { get; set; } = new List<VolumeInfo>();
        public List<BatteryInfo> Batteries { get; set; } = new List<BatteryInfo>();
        public bool? SmartPredictFailure { get; set; }

        public List<EventItem> Events { get; set; } = new List<EventItem>();
        public List<string> CollectionErrors { get; set; } = new List<string>();
    }

    public class SoftwareInventory
    {
        public string WindowsRelease { get; set; }
        public string WindowsLicenseStatus { get; set; }
        public List<string> AntivirusProducts { get; set; } = new List<string>();
        public List<string> BackupProducts { get; set; } = new List<string>();
    }
}
