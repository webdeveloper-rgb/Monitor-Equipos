using System;
using System.Threading;
using MonitorAgent.Api;
using MonitorAgent.Collectors;
using MonitorAgent.Infrastructure;
using Newtonsoft.Json;

namespace MonitorAgent.Service
{
    sealed class AgentWorker
    {
        readonly ManualResetEvent _stop = new ManualResetEvent(false);
        readonly ReportBuilder _builder = ReportBuilder.CreateDefault();
        Thread _thread;

        public void Start(int initialDelaySeconds)
        {
            _thread = new Thread(() => Loop(initialDelaySeconds)) { IsBackground = true, Name = "agent-loop" };
            _thread.Start();
        }

        public void Stop()
        {
            _stop.Set();
            _thread?.Join(15000);
        }

        void Loop(int initialDelaySeconds)
        {
            Log.Info("Agente iniciado v" + AppInfo.Version);
            if (_stop.WaitOne(TimeSpan.FromSeconds(initialDelaySeconds))) return;
            while (true)
            {
                try { RunOnce(); }
                catch (Exception ex) { Log.Error("RunOnce: " + ex); }

                int minutes = Math.Max(1, AgentConfig.Load().IntervalMinutes);
                if (_stop.WaitOne(TimeSpan.FromMinutes(minutes))) return;
            }
        }

        public void RunOnce()
        {
            var cfg = AgentConfig.Load();
            if (string.IsNullOrWhiteSpace(cfg.ApiUrl))
            {
                Log.Error("ApiUrl no configurada (config.json)");
                return;
            }
            if (string.IsNullOrWhiteSpace(cfg.ApiToken))
            {
                Log.Error("Credencial de agente no configurada (config.json)");
                return;
            }

            var state = AgentState.Load();
            DateTime started = DateTime.UtcNow;
            var context = new CollectorContext(cfg, state.LastEventUtc ?? started.AddHours(-24));

            var report = _builder.Build(state, context);
            string json = JsonConvert.SerializeObject(report, Formatting.None, JsonDefaults.Settings);

            // Los eventos ya van en el informe (o en la cola), asi que avanzamos la marca.
            state.LastEventUtc = started;
            state.Save();

            Outbox.Flush(cfg, state.MachineId);
            if (ApiClient.Send(cfg, state.MachineId, json, out var error))
            {
                Log.Info("Informe enviado (" + report.Events.Count + " eventos, " + report.Disks.Count + " discos)");
            }
            else
            {
                Log.Error("Envio fallido, se encola: " + error);
                Outbox.Enqueue(json);
            }
        }
    }
}