using System;
using System.Collections.Generic;
using System.Management;
using MonitorAgent.Models;

namespace MonitorAgent.Collectors
{
    sealed class BatteryCollector : ICollector
    {
        public string Name => "battery";

        public void Collect(Report report, CollectorContext context)
        {
            var batteries = Wmi.Query("SELECT * FROM Win32_Battery");
            if (batteries.Count == 0) return; // equipo de sobremesa

            var design = new List<ManagementObject>();
            var full = new List<ManagementObject>();
            try { design = Wmi.Query(@"root\wmi", "SELECT * FROM BatteryStaticData"); } catch { }
            try { full = Wmi.Query(@"root\wmi", "SELECT * FROM BatteryFullChargedCapacity"); } catch { }

            for (int i = 0; i < batteries.Count; i++)
            {
                var b = new BatteryInfo
                {
                    Name = batteries[i].Str("Name"),
                    ChargePercent = batteries[i].Int("EstimatedChargeRemaining"),
                    DesignCapacity = i < design.Count ? design[i].Long("DesignedCapacity") : null,
                    FullChargeCapacity = i < full.Count ? full[i].Long("FullChargedCapacity") : null
                };
                if (b.DesignCapacity > 0 && b.FullChargeCapacity != null)
                    b.HealthPercent = (int)Math.Min(100, b.FullChargeCapacity.Value * 100 / b.DesignCapacity.Value);

                report.Batteries.Add(b);
            }
        }
    }
}