using System.Linq;
using MonitorAgent.Models;

namespace MonitorAgent.Collectors
{
    sealed class VolumeCollector : ICollector
    {
        public string Name => "volumes";

        public void Collect(Report report, CollectorContext context)
        {
            report.Volumes = Wmi.Query("SELECT * FROM Win32_LogicalDisk WHERE DriveType=3").Select(o => new VolumeInfo
            {
                Drive = o.Str("DeviceID"),
                Label = o.Str("VolumeName"),
                FileSystem = o.Str("FileSystem"),
                SizeBytes = o.ULong("Size"),
                FreeBytes = o.ULong("FreeSpace")
            }).ToList();
        }
    }
}