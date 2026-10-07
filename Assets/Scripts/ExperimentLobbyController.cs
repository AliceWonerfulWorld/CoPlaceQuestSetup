using System;
using Styly.NetSync;
using UnityEngine;

// Session activation (Reset ACK) and experiment Start are intentionally separate.
[DefaultExecutionOrder(-500)]
public class ExperimentLobbyController : MonoBehaviour
{
    public const string StartVariable = "experimentLobbyStart";
    [SerializeField] private ExperimentManager experimentManager;
    [SerializeField] private ExperimentParticipantRegistry participantRegistry;
    [SerializeField] private ExperimentSessionManager sessionManager;
    [Serializable] private class StartRecord { public string id, sessionId, mode; public long epoch; public int owner; }
    private StartRecord started;
    private StartRecord pending;
    private float nextRetry;
    public bool IsExperimentStarted => started != null && sessionManager != null && !sessionManager.IsResettingSession &&
        started.sessionId == sessionManager.CurrentSessionId && started.epoch == sessionManager.SessionEpoch;
    public bool IsLobbyVisible => !IsExperimentStarted;
    public bool IsConnected => Network != null && Network.IsReady;
    public bool IsExperimenter => participantRegistry != null && participantRegistry.IsLocalExperimenter;
    public int ConnectedParticipants => IsConnected ? participantRegistry.ParticipantClientNos.Count : 0;
    public int ExpectedParticipants => participantRegistry != null ? participantRegistry.ExpectedParticipantCount : 0;
    public string SessionId => sessionManager != null ? sessionManager.CurrentSessionId : "";
    public bool IsResetting => sessionManager != null && sessionManager.IsResettingSession;
    public string CurrentState => experimentManager != null ? experimentManager.CurrentState.ToString() : "—";
    private NetSyncManager Network => participantRegistry != null ? participantRegistry.Network : null;
    public bool CanNewSession => IsExperimenter && IsConnected && !IsResetting && sessionManager != null;
    public bool CanChangeMode => experimentManager != null && experimentManager.CanChangeMode;
    // Also reads the retained marker: Mode must lock before the next Update and on reconnect.
    public bool HasStartRequestedOrStarted => pending != null || IsExperimentStarted || ReadStartRecord() != null;
    public ExperimentManager.ExperimentMode? StartedMode
    {
        get
        {
            var record = pending ?? ReadStartRecord() ?? (IsExperimentStarted ? started : null);
            if (record == null) return null;
            return record.mode == "Normal" ? ExperimentManager.ExperimentMode.Normal : ExperimentManager.ExperimentMode.Proposed;
        }
    }
    private StartRecord ReadStartRecord()
    {
        if (!IsConnected || IsResetting || sessionManager == null || string.IsNullOrEmpty(SessionId)) return null;
        StartRecord record;
        try { record = JsonUtility.FromJson<StartRecord>(SessionVariableTransport.GetGlobalVariable(Network, sessionManager, StartVariable)); }
        catch { return null; }
        return record != null && !string.IsNullOrEmpty(record.id) && record.sessionId == SessionId &&
            record.epoch == sessionManager.SessionEpoch && record.owner > 0 &&
            (record.mode == "Normal" || record.mode == "Proposed") ? record : null;
    }
    public string StartBlockReason
    {
        get
        {
            if (!IsExperimenter) return "実験者の開始をお待ちください";
            if (!IsConnected) return "接続を待っています";
            if (Network.GetClientVariable(ExperimentParticipantRegistry.RoleVariable, Network.ClientNo) != "Experimenter") return "実験者のRole登録同期を待っています";
            if (IsResetting) return "Session Resetを処理しています";
            if (IsExperimentStarted) return "実験は開始済みです";
            if (pending != null) return "開始を同期しています";
            if (sessionManager == null || string.IsNullOrEmpty(SessionId) || string.IsNullOrEmpty(sessionManager.SessionStartedAtUtc)) return "Sessionの準備が完了していません。New Sessionを実行してください";
            if (ConnectedParticipants != ExpectedParticipants || ExpectedParticipants == 0) return ConnectedParticipants < ExpectedParticipants ? $"参加者が不足しています {ConnectedParticipants} / {ExpectedParticipants}" : $"参加者数を確認してください {ConnectedParticipants} / {ExpectedParticipants}";
            if (experimentManager == null || experimentManager.CurrentState != ExperimentManager.ExperimentState.Idle) return "New Sessionで開始前の状態に戻してください";
            if (SessionVariableTransport.GetGlobalVariable(Network, sessionManager, "experimentMode") != experimentManager.CurrentMode.ToString()) return "方式の同期を待っています";
            foreach (int client in participantRegistry.ParticipantClientNos)
                if (Network.GetClientVariable(ExperimentParticipantRegistry.RoleVariable, client) != "Participant") return "Participant登録の同期を待っています";
            foreach (int client in participantRegistry.ParticipantClientNos)
                if (SessionVariableTransport.GetClientVariable(Network, sessionManager, "experimentState", client) != "Idle") return "Participantの準備を待っています";
            return "";
        }
    }
    public bool CanStart => StartBlockReason == "";
    private void OnEnable() { if (sessionManager != null) sessionManager.OnSessionResetStarted += OnReset; }
    private void OnDisable() { if (sessionManager != null) sessionManager.OnSessionResetStarted -= OnReset; ClearLocal(); }
    private void OnReset(string id) => ClearLocal();
    private void ClearLocal() { started = pending = null; nextRetry = 0; }
    private void Update()
    {
        if (!IsConnected || IsResetting) return;
        if (started != null && (started.sessionId != SessionId || started.epoch != sessionManager.SessionEpoch)) ClearLocal();
        var record = ReadStartRecord();
        if (record != null)
        {
            if (started == null) Debug.Log($"[Experiment Start Received] {record.mode} / Session {record.sessionId}", this);
            started = record; pending = null;
        }
        if (pending != null && Time.unscaledTime >= nextRetry)
        {
            nextRetry = Time.unscaledTime + 1;
            SessionVariableTransport.SetGlobalVariable(Network, sessionManager, StartVariable, JsonUtility.ToJson(pending));
        }
    }
    public bool StartExperiment()
    {
        if (participantRegistry != null) participantRegistry.RefreshParticipants();
        if (!CanStart) { Debug.LogWarning("[Experiment Start Rejected] " + StartBlockReason, this); return false; }
        pending = new StartRecord { id = Guid.NewGuid().ToString("N"), sessionId = SessionId, epoch = sessionManager.SessionEpoch,
            mode = experimentManager.CurrentMode.ToString(), owner = Network.ClientNo };
        nextRetry = 0; Update();
        Debug.Log("[Experiment Start Requested] Waiting for shared Start echo", this);
        return true;
    }
    public void StartFromUI() => StartExperiment();
    public void NewSessionFromUI() { if (CanNewSession) sessionManager.StartNewSession(); }
    public void SelectNormal() { if (experimentManager != null) experimentManager.SetMode(ExperimentManager.ExperimentMode.Normal); }
    public void SelectProposed() { if (experimentManager != null) experimentManager.SetMode(ExperimentManager.ExperimentMode.Proposed); }
}
