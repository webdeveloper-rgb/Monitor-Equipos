using System;
using System.IO;
using System.Linq;
using MonitorAgent.Infrastructure;

namespace MonitorAgent.Api
{
    // Cola en disco: si la API no responde, los informes se guardan y se reintentan.
    static class Outbox
    {
        const int MaxFiles = 100;

        public static void Enqueue(string json)
        {
            Directory.CreateDirectory(AppInfo.OutboxDir);
            string name = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + ".json";
            File.WriteAllText(Path.Combine(AppInfo.OutboxDir, name), json);

            var files = Directory.GetFiles(AppInfo.OutboxDir, "*.json").OrderBy(f => f).ToList();
            for (int i = 0; i < files.Count - MaxFiles; i++) File.Delete(files[i]);
        }

        public static void Flush(AgentConfig cfg, string machineId)
        {
            if (!Directory.Exists(AppInfo.OutboxDir)) return;

            foreach (var file in Directory.GetFiles(AppInfo.OutboxDir, "*.json").OrderBy(f => f))
            {
                if (ApiClient.Send(cfg, machineId, File.ReadAllText(file), out var error))
                    File.Delete(file);
                else
                {
                    Log.Error("Reintento fallido: " + error);
                    break;
                }
            }
        }
    }
}