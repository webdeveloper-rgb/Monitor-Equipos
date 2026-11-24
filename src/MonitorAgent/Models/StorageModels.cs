namespace MonitorAgent.Models
{
    public class DiskInfo
    {
        public string DeviceId { get; set; }
        public string Model { get; set; }
        public string SerialNumber { get; set; }
        public string FirmwareVersion { get; set; }
        public string MediaType { get; set; }     // SSD / HDD / SCM / Unspecified
        public string BusType { get; set; }       // NVMe / SATA / USB ...
        public ulong? SizeBytes { get; set; }
        public string HealthStatus { get; set; }  // Healthy / Warning / Unhealthy
        public string OperationalStatus { get; set; }
        public bool? PredictiveFailure { get; set; }
        public int? WearPercent { get; set; }
        public int? LifeLeftPercent { get; set; }
        public int? TemperatureC { get; set; }
        public int? TemperatureMaxC { get; set; }
        public ulong? PowerOnHours { get; set; }
        // UInt64 counters are strings in JSON so large vendor values stay exact end-to-end.
        public string ReadErrorsTotal { get; set; }
        public string ReadErrorsCorrected { get; set; }
        public string ReadErrorsUncorrected { get; set; }
        public string WriteErrorsTotal { get; set; }
        public string WriteErrorsCorrected { get; set; }
        public string WriteErrorsUncorrected { get; set; }
        public string ReadLatencyMaxMs { get; set; }
        public string WriteLatencyMaxMs { get; set; }
        public string FlushLatencyMaxMs { get; set; }
        public ulong? StartStopCycleCount { get; set; }
        public ulong? StartStopCycleCountMax { get; set; }
        public ulong? LoadUnloadCycleCount { get; set; }
        public ulong? LoadUnloadCycleCountMax { get; set; }
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
