using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using Newtonsoft.Json;

namespace MonitorAgent.Infrastructure
{
    sealed class AgentConfig
    {
        public string ApiUrl { get; set; } = "";
        public string ApiToken { get; set; } = "";
        public int IntervalMinutes { get; set; } = 15;
        public int EventsMaxPerLog { get; set; } = 100;
        public string[] EventLogs { get; set; } = new[] { "System", "Application" };

        public static AgentConfig Load()
        {
            try
            {
                if (File.Exists(AppInfo.ConfigFile))
                    return JsonConvert.DeserializeObject<AgentConfig>(File.ReadAllText(AppInfo.ConfigFile)) ?? new AgentConfig();
            }
            catch (Exception ex) { Log.Error("config: " + ex.Message); }
            return new AgentConfig();
        }

        public void Save()
        {
            Directory.CreateDirectory(AppInfo.DataDir);
            File.WriteAllText(AppInfo.ConfigFile, JsonConvert.SerializeObject(this, Formatting.Indented));
            Protect(AppInfo.ConfigFile);
        }

        // El fichero contiene el token: solo SYSTEM y Administradores.
        static void Protect(string path)
        {
            try
            {
                var fi = new FileInfo(path);
                var sec = fi.GetAccessControl();
                sec.SetAccessRuleProtection(true, false);
                foreach (FileSystemAccessRule r in sec.GetAccessRules(true, false, typeof(SecurityIdentifier)))
                    sec.RemoveAccessRule(r);
                sec.AddAccessRule(new FileSystemAccessRule(
                    new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                    FileSystemRights.FullControl, AccessControlType.Allow));
                sec.AddAccessRule(new FileSystemAccessRule(
                    new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
                    FileSystemRights.FullControl, AccessControlType.Allow));
                fi.SetAccessControl(sec);
            }
            catch (Exception ex) { Log.Error("ACL config: " + ex.Message); }
        }
    }
}