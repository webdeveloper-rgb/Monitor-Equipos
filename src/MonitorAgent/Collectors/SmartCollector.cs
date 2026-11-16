using System;
using System.Linq;
using MonitorAgent.Models;

namespace MonitorAgent.Collectors
{
    // Prediccion de fallo SMART clasica (discos SATA). Si no esta disponible, queda en null.
    sealed class SmartCollector : ICollector
    {
        public string Name => "smart";

        public void Collect(Report report, CollectorContext context)
        {
            try
            {
                var rows = Wmi.Query(@"root\wmi", "SELECT * FROM MSStorageDriver_FailurePredictStatus");
                if (rows.Count == 0) return;
                report.SmartPredictFailure = rows.Any(r => r["PredictFailure"] != null && Convert.ToBoolean(r["PredictFailure"]));
            }
            catch { /* clase WMI no disponible en este equipo */ }
        }
    }
}