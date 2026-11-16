using System.Linq;
using System.Management;
using MonitorAgent.Models;

namespace MonitorAgent.Collectors
{
    sealed class BoardCollector : ICollector
    {
        public string Name => "board";

        public void Collect(Report report, CollectorContext context)
        {
            var b = new BoardInfo();

            var bb = Wmi.Query("SELECT * FROM Win32_BaseBoard").FirstOrDefault();
            if (bb != null)
            {
                b.Manufacturer = bb.Str("Manufacturer");
                b.Product = bb.Str("Product");
                b.SerialNumber = bb.Str("SerialNumber");
            }

            var bios = Wmi.Query("SELECT * FROM Win32_BIOS").FirstOrDefault();
            if (bios != null)
            {
                b.BiosVendor = bios.Str("Manufacturer");
                b.BiosVersion = bios.Str("SMBIOSBIOSVersion");
                var date = bios.Str("ReleaseDate");
                if (date != null)
                    b.BiosDate = ManagementDateTimeConverter.ToDateTime(date).ToString("yyyy-MM-dd");
            }

            report.Board = b;
        }
    }
}