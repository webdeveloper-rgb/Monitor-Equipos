using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
                    DeviceId = d.Str("DeviceId"),
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
                    var statusValues = d["OperationalStatus"] as Array;
                    if (statusValues != null)
                    {
                        var statuses = statusValues.Cast<object>()
                            .Select(Status)
                            .Where(status => status != null)
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList();
                        if (statuses.Count > 0)
                        {
                            info.OperationalStatus = string.Join(", ", statuses);
                            if (statuses.Contains("Predictive Failure", StringComparer.OrdinalIgnoreCase))
                                info.PredictiveFailure = true;
                            else if (statuses.Any(status => !string.Equals(status, "Unknown", StringComparison.OrdinalIgnoreCase)
                                && !string.Equals(status, "Other", StringComparison.OrdinalIgnoreCase)))
                                info.PredictiveFailure = false;
                        }
                    }
                }
                catch { /* algunos proveedores de almacenamiento no exponen OperationalStatus */ }

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
                        CopyIfAvailable(r.Int("Temperature"), value => info.TemperatureC = value);
                        CopyIfAvailable(r.Int("TemperatureMax"), value => info.TemperatureMaxC = value);
                        CopyIfAvailable(r.ULong("PowerOnHours"), value => info.PowerOnHours = value);
                        CopyCounter(r.ULong("ReadErrorsTotal"), value => info.ReadErrorsTotal = value);
                        CopyCounter(r.ULong("ReadErrorsCorrected"), value => info.ReadErrorsCorrected = value);
                        CopyCounter(r.ULong("ReadErrorsUncorrected"), value => info.ReadErrorsUncorrected = value);
                        CopyCounter(r.ULong("WriteErrorsTotal"), value => info.WriteErrorsTotal = value);
                        CopyCounter(r.ULong("WriteErrorsCorrected"), value => info.WriteErrorsCorrected = value);
                        CopyCounter(r.ULong("WriteErrorsUncorrected"), value => info.WriteErrorsUncorrected = value);
                        CopyCounter(r.ULong("ReadLatencyMax"), value => info.ReadLatencyMaxMs = value);
                        CopyCounter(r.ULong("WriteLatencyMax"), value => info.WriteLatencyMaxMs = value);
                        CopyCounter(r.ULong("FlushLatencyMax"), value => info.FlushLatencyMaxMs = value);
                        CopyIfAvailable(r.ULong("StartStopCycleCount"), value => info.StartStopCycleCount = value);
                        CopyIfAvailable(r.ULong("StartStopCycleCountMax"), value => info.StartStopCycleCountMax = value);
                        CopyIfAvailable(r.ULong("LoadUnloadCycleCount"), value => info.LoadUnloadCycleCount = value);
                        CopyIfAvailable(r.ULong("LoadUnloadCycleCountMax"), value => info.LoadUnloadCycleCountMax = value);
                    }
                }
                catch { /* disco sin contadores de fiabilidad (USB, RAID, etc.) */ }

                report.Disks.Add(info);
            }
        }

        static void CopyIfAvailable<T>(T? value, Action<T> set) where T : struct
        {
            if (value.HasValue) set(value.Value);
        }

        static void CopyCounter(ulong? value, Action<string> set)
        {
            if (value.HasValue) set(value.Value.ToString(CultureInfo.InvariantCulture));
        }

        static string Status(object value)
        {
            if (value == null) return null;
            int? code;
            try { code = Convert.ToInt32(value); }
            catch { return value.ToString(); }
            switch (code)
            {
                case 0: return "Unknown";
                case 1: return "Other";
                case 2: return "OK";
                case 3: return "Degraded";
                case 4: return "Stressed";
                case 5: return "Predictive Failure";
                case 6: return "Error";
                case 7: return "Non-Recoverable Error";
                case 8: return "Starting";
                case 9: return "Stopping";
                case 10: return "Stopped";
                case 11: return "In Service";
                case 12: return "No Contact";
                case 13: return "Lost Communication";
                case 14: return "Aborted";
                case 15: return "Dormant";
                case 16: return "Supporting Entity in Error";
                case 17: return "Completed";
                case 18: return "Power Mode";
                case 19: return "Relocating";
                case 20: return "Rebuild";
                case 21: return "Reconstructing";
                case 22: return "Spare";
                default: return "Status " + code.Value;
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
            switch (v) { case 0: return "Healthy"; case 1: return "Warning"; case 2: return "Unhealthy"; case 5: return "Unknown"; default: return "Unknown"; }
        }
    }
}
