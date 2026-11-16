namespace MonitorAgent.Models
{
    public class DiskInfo
    {
        public string Model { get; set; }
        public string SerialNumber { get; set; }
        public string FirmwareVersion { get; set; }
        public string MediaType { get; set; }     // SSD / HDD / SCM / Unspecified
        public string BusType { get; set; }       // NVMe / SATA / USB ...
        public ulong? SizeBytes { get; set; }
        public string HealthStatus { get; set; }  // Healthy / Warning / Unhealthy
        public int? WearPercent { get; set; }
        public int? LifeLeftPercent { get; set; }
        public int? TemperatureC { get; set; }
        public ulong? PowerOnHours { get; set; }
        public ulong? ReadErrorsTotal { get; set; }
        public ulong? WriteErrorsTotal { get; set; }
    }

    public class VolumeInfo
    {
        public string Drive { get; set; }
        public string Label { get; set; }
        public string FileSystem { get; set; }
        public ulong? SizeBytes { get; set; }
        public ulong? FreeBytes { get; set; }
    }
}