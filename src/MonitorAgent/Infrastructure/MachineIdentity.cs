using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using MonitorAgent.Collectors;

namespace MonitorAgent.Infrastructure
{
    static class MachineIdentity
    {
        public static string Create(out bool hardwareBased)
        {
            if (TryCreate(out var id))
            {
                hardwareBased = true;
                return id;
            }

            hardwareBased = false;
            return Guid.NewGuid().ToString("D");
        }

        public static bool TryCreate(out string id)
        {
            var parts = new List<string>();
            Add(parts, "system-uuid", Read("SELECT UUID FROM Win32_ComputerSystemProduct", "UUID"));
            Add(parts, "bios-serial", Read("SELECT SerialNumber FROM Win32_BIOS", "SerialNumber"));
            Add(parts, "board-serial", Read("SELECT SerialNumber FROM Win32_BaseBoard", "SerialNumber"));

            if (parts.Count == 0)
            {
                id = null;
                return false;
            }

            byte[] hash;
            using (var sha = SHA256.Create())
                hash = sha.ComputeHash(Encoding.UTF8.GetBytes("SauberMonitorAgent|hardware-id|v1|" + string.Join("|", parts)));

            var guidBytes = new byte[16];
            Buffer.BlockCopy(hash, 0, guidBytes, 0, guidBytes.Length);
            // Keep the RFC 4122 version/variant bits so the fingerprint fits the server's GUID identity format.
            guidBytes[7] = (byte)((guidBytes[7] & 0x0F) | 0x50);
            guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80);
            id = new Guid(guidBytes).ToString("D");
            return true;
        }

        static string Read(string query, string field)
        {
            try { return Wmi.Query(query).FirstOrDefault()?.Str(field); }
            catch { return null; }
        }

        static void Add(ICollection<string> parts, string name, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            var normalized = Regex.Replace(value.Trim().ToUpperInvariant(), @"\s+", " ");
            var compact = Regex.Replace(normalized, @"[^A-Z0-9]", "");
            if (compact.Length < 4 || Regex.IsMatch(compact, @"\A(?:0+|F+|UNKNOWN|NONE|NA|NULL|DEFAULTSTRING|SYSTEMSERIALNUMBER|TOBEFILLEDBYOEM)\Z"))
                return;
            parts.Add(name + "=" + normalized);
        }
    }
}
