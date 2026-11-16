using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Principal;
using MonitorAgent.Infrastructure;

namespace MonitorAgent.Cli
{
    static class ServiceInstaller
    {
        public static void Install(Dictionary<string, string> options)
        {
            RequireAdmin();

            var cfg = AgentConfig.Load();
            if (options.TryGetValue("url", out var url) && url != "") cfg.ApiUrl = url;
            if (options.TryGetValue("token", out var token) && token != "") cfg.ApiToken = token;
            if (options.TryGetValue("interval", out var iv) && int.TryParse(iv, out var minutes)) cfg.IntervalMinutes = minutes;
            if (string.IsNullOrWhiteSpace(cfg.ApiUrl)) throw new InvalidOperationException("Falta --url");
            cfg.Save();

            string exe = Process.GetCurrentProcess().MainModule.FileName;
            string name = AppInfo.ServiceName;
            Sc("create " + name + " binPath= \"" + exe + "\" start= delayed-auto obj= LocalSystem DisplayName= \"Monitor Agent\"");
            Sc("description " + name + " \"Agente de monitorizacion de hardware y eventos\"");
            Sc("failure " + name + " reset= 86400 actions= restart/60000/restart/60000/restart/60000");
            Sc("start " + name);
            Console.WriteLine("Instalado. Config en: " + AppInfo.DataDir);
        }

        public static void Uninstall()
        {
            RequireAdmin();
            Sc("stop " + AppInfo.ServiceName);
            Sc("delete " + AppInfo.ServiceName);
        }

        static void RequireAdmin()
        {
            using (var id = WindowsIdentity.GetCurrent())
                if (!new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator))
                    throw new InvalidOperationException("Ejecuta este comando como Administrador.");
        }

        static void Sc(string arguments)
        {
            var psi = new ProcessStartInfo("sc.exe", arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true
            };
            using (var p = Process.Start(psi))
            {
                Console.Write(p.StandardOutput.ReadToEnd());
                p.WaitForExit();
            }
        }
    }
}