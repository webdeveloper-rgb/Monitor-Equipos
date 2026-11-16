using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text.RegularExpressions;
using MonitorAgent.Api;
using MonitorAgent.Infrastructure;

namespace MonitorAgent.Cli
{
    static class ServiceSetup
    {
        public static void Install(Dictionary<string, string> options)
        {
            RequireAdmin();

            var cfg = AgentConfig.Load();
            if (options.TryGetValue("url", out var url) && url != "") cfg.ApiUrl = url;
            if (options.TryGetValue("token", out var token) && token != "") cfg.ApiToken = token;
            if (options.TryGetValue("rustdesk-id", out var rustDeskId) && rustDeskId != "")
            {
                rustDeskId = rustDeskId.Trim();
                if (!Regex.IsMatch(rustDeskId, @"\A[A-Za-z0-9][A-Za-z0-9 ._-]{0,127}\z"))
                    throw new InvalidOperationException("--rustdesk-id no tiene un formato válido.");
                cfg.RustDeskId = rustDeskId;
            }
            if (options.TryGetValue("interval", out var iv) && int.TryParse(iv, out var minutes)) cfg.IntervalMinutes = minutes;
            if (string.IsNullOrWhiteSpace(cfg.ApiUrl)) throw new InvalidOperationException("Falta --url");
            if (!Uri.TryCreate(cfg.ApiUrl, UriKind.Absolute, out var ingestUri) || ingestUri.Scheme != Uri.UriSchemeHttps)
                throw new InvalidOperationException("--url debe ser una URL HTTPS absoluta.");

            var enrollmentCode = options.TryGetValue("enroll-code", out var code) ? code : "";
            if (!string.IsNullOrEmpty(enrollmentCode))
            {
                if (!Regex.IsMatch(enrollmentCode, @"\Amre_[0-9a-f]{48}\z"))
                    throw new InvalidOperationException("--enroll-code no tiene un formato válido.");

                if (!Regex.IsMatch(cfg.ApiToken ?? "", @"\Amra_[0-9a-f]{64}\z"))
                    cfg.ApiToken = ApiClient.CreateDeviceToken();
                cfg.Save();

                var state = AgentState.Load();
                if (!ApiClient.Enroll(cfg.ApiUrl, enrollmentCode, state.MachineId, cfg.ApiToken, out var enrollmentError))
                    throw new InvalidOperationException(enrollmentError);

                Directory.CreateDirectory(AppInfo.DataDir);
                File.WriteAllText(AppInfo.EnrollmentMarkerFile, DateTime.UtcNow.ToString("o"));
            }
            else if (!Regex.IsMatch(cfg.ApiToken ?? "", @"\A(?:mra_[0-9a-f]{64}|sbr_[0-9a-f]{64})\z"))
            {
                throw new InvalidOperationException("Falta una credencial válida. Usa --token o --enroll-code.");
            }

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