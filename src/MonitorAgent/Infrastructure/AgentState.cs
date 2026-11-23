using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace MonitorAgent.Infrastructure
{
    sealed class AgentState
    {
        public string MachineId { get; set; }
        public string EventExtractionMode { get; set; } = "standard";
        public int EventLogScanOffset { get; set; }
        public Dictionary<string, EventLogCursor> EventLogCursors { get; set; } =
            new Dictionary<string, EventLogCursor>(StringComparer.OrdinalIgnoreCase);
        public long? AcronisApplicationLastRecordId { get; set; }
        public Dictionary<string, AcronisOperationState> ActiveAcronisOperations { get; set; } =
            new Dictionary<string, AcronisOperationState>(StringComparer.OrdinalIgnoreCase);
        public string LastBackupActivityHash { get; set; }
        public DateTime? LastBackupActivitySentUtc { get; set; }

        public DateTime? LastInstalledProgramsUtc { get; set; }

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
            if (state.EventLogCursors == null)
                state.EventLogCursors = new Dictionary<string, EventLogCursor>(StringComparer.OrdinalIgnoreCase);
            if (state.ActiveAcronisOperations == null)
                state.ActiveAcronisOperations = new Dictionary<string, AcronisOperationState>(StringComparer.OrdinalIgnoreCase);
            if (state.EventExtractionMode != "standard" && state.EventExtractionMode != "full")
                state.EventExtractionMode = "standard";
            return state;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(AppInfo.DataDir);
                AgentConfig.ProtectDirectory(AppInfo.DataDir);
                string temporaryPath = AppInfo.StateFile + ".partial";
                try
                {
                    File.WriteAllText(temporaryPath, JsonConvert.SerializeObject(this, Formatting.Indented));
                    AgentConfig.ProtectFile(temporaryPath);
                    if (File.Exists(AppInfo.StateFile))
                        File.Replace(temporaryPath, AppInfo.StateFile, null);
                    else
                        File.Move(temporaryPath, AppInfo.StateFile);
                    AgentConfig.ProtectFile(AppInfo.StateFile);
                }
                catch
                {
                    try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); } catch { }
                    throw;
                }
            }
            catch (Exception ex) { Log.Error("state: " + ex.Message); }
        }
    }

    sealed class AcronisOperationState
    {
        public DateTime StartedAtUtc { get; set; }
    }

    sealed class EventLogCursor
    {
        public DateTime BackfillFromUtc { get; set; }
        public long? LastRecordId { get; set; }
        public bool BackfillComplete { get; set; }
        public string Mode { get; set; }
        public int Version { get; set; }
    }
}
