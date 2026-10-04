using System;
using Styly.NetSync;
using UnityEngine;

// Fixed keys avoid exhausting NetSync's 100-variable limit across repeated sessions.
public static class SessionVariableTransport
{
    [Serializable] private class Value { public string session; public string value; public string repair; }
    public static string Encode(ExperimentSessionManager session, string value) => session == null || string.IsNullOrEmpty(session.CurrentSessionId)
        ? value : JsonUtility.ToJson(new Value { session = session.CurrentSessionId, value = value });
    // NetSync deduplicates last-sent wire values. A repair token makes a restored
    // value sendable even if it was previously sent before the stale event.
    public static string EncodeRepair(ExperimentSessionManager session, string value) => JsonUtility.ToJson(new Value { session = session.CurrentSessionId, value = value, repair = Guid.NewGuid().ToString("N") });
    public static string Decode(ExperimentSessionManager session, string value)
    {
        if (session == null || string.IsNullOrEmpty(session.CurrentSessionId)) return value;
        if (string.IsNullOrEmpty(value)) return null;
        try { var data = JsonUtility.FromJson<Value>(value); return data != null && data.session == session.CurrentSessionId ? data.value : null; }
        catch { return null; }
    }
    public static string GetGlobalVariable(NetSyncManager net, ExperimentSessionManager session, string name) => name == "experimentMode" ? net.GetGlobalVariable(name) : session != null ? session.ReadGlobalValue(name, net.GetGlobalVariable(name)) : net.GetGlobalVariable(name);
    public static bool SetGlobalVariable(NetSyncManager net, ExperimentSessionManager session, string name, string value) => net.SetGlobalVariable(name, name == "experimentMode" ? value : Encode(session, value));
    public static string GetClientVariable(NetSyncManager net, ExperimentSessionManager session, string name, int client = 0) => session != null ? session.ReadClientValue(client > 0 ? client : net.ClientNo, name, client > 0 ? net.GetClientVariable(name, client) : net.GetClientVariable(name)) : (client > 0 ? net.GetClientVariable(name, client) : net.GetClientVariable(name));
    public static bool SetClientVariable(NetSyncManager net, ExperimentSessionManager session, string name, string value) => net.SetClientVariable(name, Encode(session, value));
}
