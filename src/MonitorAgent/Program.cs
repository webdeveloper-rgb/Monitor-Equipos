using System;
using System.ServiceProcess;
using MonitorAgent.Api;
using MonitorAgent.Cli;
using MonitorAgent.Collectors;
using MonitorAgent.Infrastructure;
using MonitorAgent.Service;
using Newtonsoft.Json;

namespace MonitorAgent
{
    static class Program
    {
        static int Main(string[] args)
        {
            ApiClient.EnableModernTls();

            // Arrancado por el Service Control Manager
            if (args.Length == 0 && !Environment.UserInteractive)
            {
                ServiceBase.Run(new AgentService());
                return 0;
            }

            var opts = CommandLine.Parse(args);
            try
            {
                if (opts.ContainsKey("install")) { ServiceInstaller.Install(opts); return 0; }
                if (opts.ContainsKey("uninstall")) { ServiceInstaller.Uninstall(); return 0; }
                if (opts.ContainsKey("dump")) return Dump();
                if (opts.ContainsKey("once")) { new AgentWorker().RunOnce(); return 0; }
                if (opts.ContainsKey("console")) return RunConsole();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }

            Console.WriteLine(CommandLine.Help);
            return 0;
        }

        static int Dump()
        {
            var context = new CollectorContext(AgentConfig.Load(), DateTime.UtcNow.AddHours(-24));
            var report = ReportBuilder.CreateDefault().Build(AgentState.Load(), context);
            Console.WriteLine(JsonConvert.SerializeObject(report, Formatting.Indented, JsonDefaults.Settings));
            return 0;
        }

        static int RunConsole()
        {
            var worker = new AgentWorker();
            worker.Start(1);
            Console.WriteLine("Ejecutando. Pulsa Enter para salir.");
            Console.ReadLine();
            worker.Stop();
            return 0;
        }
    }
}