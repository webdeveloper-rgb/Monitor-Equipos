using System.ServiceProcess;
using MonitorAgent.Infrastructure;

namespace MonitorAgent.Service
{
    sealed class AgentService : ServiceBase
    {
        AgentWorker _worker;

        public AgentService()
        {
            ServiceName = AppInfo.ServiceName;
            CanStop = true;
        }

        protected override void OnStart(string[] args)
        {
            _worker = new AgentWorker();
            _worker.Start(30);
        }

        protected override void OnStop()
        {
            _worker?.Stop();
        }
    }
}