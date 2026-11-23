using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.Win32;
using MonitorAgent.Models;

namespace MonitorAgent.Collectors
{
    // Lista de programas instalados leyendo las claves Uninstall del registro
    // (es lo mismo que muestra "Aplicaciones instaladas" / "Programas y características").
    sealed class InstalledProgramsCollector : ICollector
    {
        const string UninstallPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
        const string UninstallPathWow = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall";
        const int MaxPrograms = 1500;

        public string Name => "installed-programs";

        public void Collect(Report report, CollectorContext context)
        {
            var last = context.State.LastInstalledProgramsUtc;
            if (last.HasValue && context.StartedUtc - last.Value < TimeSpan.FromHours(12))
            {
                report.InstalledPrograms = null;
                return;
            }
            var found = new Dictionary<string, InstalledProgram>(StringComparer.OrdinalIgnoreCase);
            bool is64 = Environment.Is64BitOperatingSystem;

            // Instalaciones para todo el equipo
            if (is64)
            {
                ReadHive(RegistryHive.LocalMachine, RegistryView.Registry64, null, UninstallPath, "64-bit", "machine", found);
                ReadHive(RegistryHive.LocalMachine, RegistryView.Registry64, null, UninstallPathWow, "32-bit", "machine", found);
            }
            else
            {
                ReadHive(RegistryHive.LocalMachine, RegistryView.Default, null, UninstallPath, "32-bit", "machine", found);
            }

            // Instalaciones por usuario (el agente corre como SYSTEM, asi que HKCU no sirve:
            // se recorren los perfiles cargados en HKEY_USERS)
            try
            {
                var view = is64 ? RegistryView.Registry64 : RegistryView.Default;
                using (var users = RegistryKey.OpenBaseKey(RegistryHive.Users, view))
                {
                    foreach (var sid in users.GetSubKeyNames())
                    {
                        if (!IsRealUserSid(sid)) continue;
                        ReadHive(RegistryHive.Users, view, sid, UninstallPath, is64 ? "64-bit" : "32-bit", "user", found);
                        if (is64)
                            ReadHive(RegistryHive.Users, view, sid, UninstallPathWow, "32-bit", "user", found);
                    }
                }
            }
            catch { /* sin acceso a HKEY_USERS: se queda solo con lo del equipo */ }

            report.InstalledPrograms = found.Values
                .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .Take(MaxPrograms)
                .ToList();
            context.State.LastInstalledProgramsUtc = context.StartedUtc;
        }

        static bool IsRealUserSid(string sid)
        {
            // Perfiles de usuario reales: S-1-5-21-... (sin el sufijo _Classes)
            return sid.StartsWith("S-1-5-21-", StringComparison.OrdinalIgnoreCase)
                && !sid.EndsWith("_Classes", StringComparison.OrdinalIgnoreCase);
        }

        static void ReadHive(RegistryHive hive, RegistryView view, string sid, string path,
                             string architecture, string scope, Dictionary<string, InstalledProgram> found)
        {
            try
            {
                using (var baseKey = RegistryKey.OpenBaseKey(hive, view))
                using (var uninstall = baseKey.OpenSubKey(sid == null ? path : sid + "\\" + path))
                {
                    if (uninstall == null) return;
                    foreach (var subkeyName in uninstall.GetSubKeyNames())
                    {
                        try
                        {
                            using (var key = uninstall.OpenSubKey(subkeyName))
                            {
                                var program = ReadProgram(key, architecture, scope);
                                if (program == null) continue;

                                // Mismo programa visto en varias vistas/perfiles: se queda una sola vez
                                string id = program.Name + "|" + program.Version + "|" + program.Publisher;
                                if (!found.ContainsKey(id)) found[id] = program;
                            }
                        }
                        catch { /* entrada ilegible: se omite */ }
                    }
                }
            }
            catch { /* clave no disponible */ }
        }

        static InstalledProgram ReadProgram(RegistryKey key, string architecture, string scope)
        {
            if (key == null) return null;

            string name = (key.GetValue("DisplayName") as string)?.Trim();
            if (string.IsNullOrWhiteSpace(name)) return null;

            // Componentes del sistema, parches y actualizaciones: no son "programas"
            if (ToInt(key.GetValue("SystemComponent")) == 1) return null;
            if (!string.IsNullOrWhiteSpace(key.GetValue("ParentKeyName") as string)) return null;
            string releaseType = key.GetValue("ReleaseType") as string;
            if (releaseType != null && (releaseType.Equals("Update", StringComparison.OrdinalIgnoreCase)
                || releaseType.Equals("Hotfix", StringComparison.OrdinalIgnoreCase)
                || releaseType.Equals("Security Update", StringComparison.OrdinalIgnoreCase)))
                return null;
            if (System.Text.RegularExpressions.Regex.IsMatch(name, @"^(Security Update|Update|Hotfix) for ",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                return null;

            int? sizeKb = ToInt(key.GetValue("EstimatedSize"));

            return new InstalledProgram
            {
                Name = name,
                Version = (key.GetValue("DisplayVersion") as string)?.Trim(),
                Publisher = (key.GetValue("Publisher") as string)?.Trim(),
                InstallDate = ParseInstallDate(key.GetValue("InstallDate") as string),
                SizeMb = sizeKb.HasValue && sizeKb.Value > 0 ? (int?)(sizeKb.Value / 1024) : null,
                Architecture = architecture,
                Scope = scope
            };
        }

        static int? ToInt(object value)
        {
            try { return value == null ? (int?)null : Convert.ToInt32(value); }
            catch { return null; }
        }

        // El registro guarda la fecha como "yyyyMMdd"
        static string ParseInstallDate(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            DateTime d;
            return DateTime.TryParseExact(raw.Trim(), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d)
                ? d.ToString("yyyy-MM-dd")
                : null;
        }
    }
}