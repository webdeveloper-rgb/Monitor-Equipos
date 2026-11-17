using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Win32;
using MonitorAgent.Models;

namespace MonitorAgent.Collectors
{
    sealed class SoftwareInventoryCollector : ICollector
    {
        static readonly string[] AntivirusKeywords = {
            "norton", "panda", "symantec", "avast", "avg", "eset", "bitdefender",
            "mcafee", "kaspersky", "sophos", "trend micro", "malwarebytes",
            "crowdstrike", "sentinelone", "webroot", "f-secure", "defender"
        };

        static readonly string[] BackupKeywords = {
            "acronis", "iperius", "micro focus", "data protector", "veeam",
            "arcserve", "commvault", "veritas", "nakivo", "backup exec",
            "windows server backup", "synology active backup"
        };

        public string Name => "software-inventory";

        public void Collect(Report report, CollectorContext context)
        {
            var inventory = new SoftwareInventory
            {
                WindowsRelease = ReadWindowsRelease(),
                WindowsLicenseStatus = ReadWindowsLicenseStatus(),
                AntivirusProducts = ReadAntivirusProducts(),
                BackupProducts = ReadBackupProducts()
            };
            report.Software = inventory;
        }

        static string ReadWindowsRelease()
        {
            try
            {
                var view = Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Default;
                using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                using (var key = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    var displayVersion = key?.GetValue("DisplayVersion") as string;
                    if (!string.IsNullOrWhiteSpace(displayVersion)) return displayVersion.Trim();
                    var releaseId = key?.GetValue("ReleaseId") as string;
                    if (!string.IsNullOrWhiteSpace(releaseId)) return releaseId.Trim();
                    var build = key?.GetValue("CurrentBuildNumber") as string;
                    return string.IsNullOrWhiteSpace(build) ? null : "Build " + build.Trim();
                }
            }
            catch { return null; }
        }

        static string ReadWindowsLicenseStatus()
        {
            try
            {
                var products = Wmi.Query(@"root\cimv2",
                    "SELECT LicenseStatus FROM SoftwareLicensingProduct " +
                    "WHERE ApplicationID='55c92734-d682-4d71-983e-d6ec3f16059f' AND PartialProductKey IS NOT NULL");
                if (products.Count == 0) return "unknown";
                return products.Any(p => p.Int("LicenseStatus") == 1) ? "licensed" : "not_licensed";
            }
            catch { return "unknown"; }
        }

        static List<string> ReadAntivirusProducts()
        {
            var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var product in Wmi.Query(@"root\SecurityCenter2", "SELECT displayName FROM AntivirusProduct"))
                {
                    var name = product.Str("displayName");
                    if (!string.IsNullOrWhiteSpace(name)) found.Add(name);
                }
            }
            catch { /* SecurityCenter2 no está disponible en algunas versiones de Windows Server. */ }

            foreach (var name in ReadInstalledProductNames())
                if (Matches(name, AntivirusKeywords)) found.Add(name);
            return found.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).Take(20).ToList();
        }

        static List<string> ReadBackupProducts()
        {
            return ReadInstalledProductNames()
                .Where(name => Matches(name, BackupKeywords))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .Take(30)
                .ToList();
        }

        static IEnumerable<string> ReadInstalledProductNames()
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var views = Environment.Is64BitOperatingSystem
                ? new[] { RegistryView.Registry64, RegistryView.Registry32 }
                : new[] { RegistryView.Default };
            foreach (var view in views)
            {
                try
                {
                    using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                    using (var uninstall = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"))
                    {
                        if (uninstall == null) continue;
                        foreach (var subkeyName in uninstall.GetSubKeyNames())
                        {
                            try
                            {
                                using (var product = uninstall.OpenSubKey(subkeyName))
                                {
                                    var displayName = product?.GetValue("DisplayName") as string;
                                    if (!string.IsNullOrWhiteSpace(displayName)) names.Add(displayName.Trim());
                                }
                            }
                            catch { /* Omite entradas de desinstalación que no se puedan leer. */ }
                        }
                    }
                }
                catch { /* La vista del registro puede estar restringida por el sistema. */ }
            }
            return names;
        }

        static bool Matches(string name, IEnumerable<string> keywords)
        {
            return keywords.Any(keyword => name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0);
        }
    }
}
