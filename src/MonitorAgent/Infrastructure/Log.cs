using System;
using System.IO;

namespace MonitorAgent.Infrastructure
{
    static class Log
    {
        static readonly object Sync = new object();

        public static void Info(string message) => Write("INFO", message);
        public static void Error(string message) => Write("ERROR", message);

        static void Write(string level, string message)
        {
            string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " [" + level + "] " + message;
            if (Environment.UserInteractive) Console.WriteLine(line);

            try
            {
                lock (Sync)
                {
                    Directory.CreateDirectory(AppInfo.DataDir);
                    var fi = new FileInfo(AppInfo.LogFile);
                    if (fi.Exists && fi.Length > 2000000)
                    {
                        string old = AppInfo.LogFile + ".1";
                        if (File.Exists(old)) File.Delete(old);
                        File.Move(AppInfo.LogFile, old);
                    }
                    File.AppendAllText(AppInfo.LogFile, line + Environment.NewLine);
                }
            }
            catch { }
        }
    }
}