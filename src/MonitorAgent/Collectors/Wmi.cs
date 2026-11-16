using System;
using System.Collections.Generic;
using System.Management;

namespace MonitorAgent.Collectors
{
    static class Wmi
    {
        public static List<ManagementObject> Query(string ns, string query)
        {
            var list = new List<ManagementObject>();
            using (var s = new ManagementObjectSearcher(ns, query))
            using (var col = s.Get())
                foreach (ManagementObject o in col) list.Add(o);
            return list;
        }

        public static List<ManagementObject> Query(string query) => Query(@"root\cimv2", query);

        public static string Str(this ManagementBaseObject o, string p)
        {
            try { var v = o[p]; return v == null ? null : v.ToString().Trim(); }
            catch { return null; }
        }

        public static int? Int(this ManagementBaseObject o, string p)
        {
            try { var v = o[p]; return v == null ? (int?)null : Convert.ToInt32(v); }
            catch { return null; }
        }

        public static long? Long(this ManagementBaseObject o, string p)
        {
            try { var v = o[p]; return v == null ? (long?)null : Convert.ToInt64(v); }
            catch { return null; }
        }

        public static ulong? ULong(this ManagementBaseObject o, string p)
        {
            try { var v = o[p]; return v == null ? (ulong?)null : Convert.ToUInt64(v); }
            catch { return null; }
        }
    }
}