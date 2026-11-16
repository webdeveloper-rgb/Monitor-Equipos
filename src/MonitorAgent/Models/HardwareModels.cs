using System;

namespace MonitorAgent.Models
{
    public class SystemInfo
    {
        public string OsName { get; set; }
        public string OsVersion { get; set; }
        public string OsArchitecture { get; set; }
        public string Manufacturer { get; set; }
        public string Model { get; set; }
        public string SerialNumber { get; set; }
        public string Domain { get; set; }
        public ulong? TotalRamBytes { get; set; }
        public DateTime? LastBootUtc { get; set; }
    }

    public class BoardInfo
    {
        public string Manufacturer { get; set; }
        public string Product { get; set; }
        public string SerialNumber { get; set; }
        public string BiosVendor { get; set; }
        public string BiosVersion { get; set; }
        public string BiosDate { get; set; }
    }

    public class CpuInfo
    {
        public string Name { get; set; }
        public string Manufacturer { get; set; }
        public int? Cores { get; set; }
        public int? LogicalProcessors { get; set; }
        public int? MaxClockMHz { get; set; }
        public int? LoadPercent { get; set; }
    }

    public class RamModule
    {
        public string Slot { get; set; }
        public string Manufacturer { get; set; }
        public string PartNumber { get; set; }
        public string SerialNumber { get; set; }
        public ulong? CapacityBytes { get; set; }
        public int? SpeedMHz { get; set; }
        public string Type { get; set; }
    }

    public class GpuInfo
    {
        public string Name { get; set; }
        public string DriverVersion { get; set; }
        // WMI devuelve 32 bits: en GPUs de mas de 4 GB aparece topado.
        public ulong? AdapterRamBytes { get; set; }
    }

    public class BatteryInfo
    {
        public string Name { get; set; }
        public int? ChargePercent { get; set; }
        public long? DesignCapacity { get; set; }
        public long? FullChargeCapacity { get; set; }
        public int? HealthPercent { get; set; }
    }
}