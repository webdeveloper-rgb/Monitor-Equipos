using System;
using System.IO;

namespace MonitorAgent.Infrastructure
{
    static class AppInfo
    {
        public const string ServiceName = "MonitorAgent";

        public static readonly string Version =
            typeof(AppInfo).Assembly.GetName().Version.ToString();

        public static readonly string DataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "MonitorAgent");

        public static string ConfigFile => Path.Combine(DataDir, "config.json");
        public static string StateFile => Path.Combine(DataDir, "state.json");
        public static string EnrollmentMarkerFile => Path.Combine(DataDir, "enrolled.marker");
        public static string LogFile => Path.Combine(DataDir, "agent.log");
        public static string OutboxDir => Path.Combine(DataDir, "outbox");
    }
}