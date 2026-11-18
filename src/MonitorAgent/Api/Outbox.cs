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
            AgentConfig.ProtectDirectory(AppInfo.OutboxDir);
            string name = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + "-" + Guid.NewGuid().ToString("N") + ".json";
            string path = Path.Combine(AppInfo.OutboxDir, name);
            string temporaryPath = path + ".partial";
            try
            {
                File.WriteAllText(temporaryPath, json);
                AgentConfig.ProtectFile(temporaryPath);
                File.Move(temporaryPath, path);
            }
            catch
            {
                try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); } catch { }
                throw;
            }

            var files = Directory.GetFiles(AppInfo.OutboxDir, "*.json").OrderBy(f => f).ToList();
            int overflow = files.Count - MaxFiles;
            for (int i = 0; i < overflow; i++)
            {
                try { File.Delete(files[i]); }
                catch (Exception ex) { Log.Error("No se pudo eliminar el informe antiguo de la cola: " + ex.Message); }
            }
            if (overflow > 0) Log.Error("La cola alcanzó su límite; se descartaron " + overflow + " informes antiguos.");
        }

        public static void Flush(AgentConfig cfg, string machineId)
        {
            if (!Directory.Exists(AppInfo.OutboxDir)) return;

            AgentConfig.ProtectDirectory(AppInfo.OutboxDir);
            foreach (var file in Directory.GetFiles(AppInfo.OutboxDir, "*.json").OrderBy(f => f))
            {
                AgentConfig.ProtectFile(file);
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
