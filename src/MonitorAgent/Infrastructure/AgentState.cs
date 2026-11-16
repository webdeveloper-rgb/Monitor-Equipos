using System;
using System.IO;
using Newtonsoft.Json;

namespace MonitorAgent.Infrastructure
{
    sealed class AgentState
    {
        public string MachineId { get; set; }
        public DateTime? LastEventUtc { get; set; }

        public static AgentState Load()
        {
            AgentState state = null;
            try
            {
                if (File.Exists(AppInfo.StateFile))
                    state = JsonConvert.DeserializeObject<AgentState>(File.ReadAllText(AppInfo.StateFile));
            }
            catch { }

            if (state == null) state = new AgentState();
            if (string.IsNullOrEmpty(state.MachineId))
            {
                state.MachineId = Guid.NewGuid().ToString();
                state.Save();
            }
            return state;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(AppInfo.DataDir);
                File.WriteAllText(AppInfo.StateFile, JsonConvert.SerializeObject(this, Formatting.Indented));
            }
            catch (Exception ex) { Log.Error("state: " + ex.Message); }
        }
    }
}