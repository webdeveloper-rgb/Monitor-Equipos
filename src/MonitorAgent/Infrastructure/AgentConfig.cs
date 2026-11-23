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
        public string UpdateManifestUrl { get; set; } = "https://github.com/webdeveloper-rgb/agent-updates/releases/download/v1.3.10/update.json";
        public string RustDeskId { get; set; } = "";
        public string RustDeskPassword { get; set; } = "";
        public string ClientNote { get; set; } = "";
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

        public static void ProtectFile(string path)
        {
            Protect(path);
        }

        public static void ProtectDirectory(string path)
        {
            try
            {
                var di = new DirectoryInfo(path);
                var security = di.GetAccessControl();
                security.SetAccessRuleProtection(true, false);
                foreach (FileSystemAccessRule rule in security.GetAccessRules(true, false, typeof(SecurityIdentifier)))
                    security.RemoveAccessRule(rule);
                var inheritance = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
                security.AddAccessRule(new FileSystemAccessRule(
                    new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                    FileSystemRights.FullControl, inheritance, PropagationFlags.None, AccessControlType.Allow));
                security.AddAccessRule(new FileSystemAccessRule(
                    new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
                    FileSystemRights.FullControl, inheritance, PropagationFlags.None, AccessControlType.Allow));
                di.SetAccessControl(security);
            }
            catch (Exception ex) { Log.Error("ACL directorio protegido: " + ex.Message); }
        }
    }
}
