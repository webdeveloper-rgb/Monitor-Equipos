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
                var predictions = new System.Collections.Generic.List<bool>();
                foreach (var row in rows)
                {
                    try
                    {
                        var value = row["PredictFailure"];
                        if (value != null) predictions.Add(Convert.ToBoolean(value));
                    }
                    catch { /* una instancia SMART inválida no invalida las demás */ }
                }
                if (predictions.Count > 0)
                    report.SmartPredictFailure = predictions.Any(prediction => prediction);
            }
            catch { /* clase WMI no disponible en este equipo */ }
        }
    }
}
