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
    // A received/retained Start, never merely a queued request.
    public event Action<string, string, ExperimentManager.ExperimentMode> OnExperimentStarted;
    public string StartedEventId => IsExperimentStarted ? started.id : null;
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
            if (IsResetting) return "新規セッションを準備しています";
            if (IsExperimentStarted) return "実験は開始済みです";
            if (pending != null) return "開始を同期しています";
            if (sessionManager == null || string.IsNullOrEmpty(SessionId) || string.IsNullOrEmpty(sessionManager.SessionStartedAtUtc)) return "新規セッションの準備が必要です";
            if (Network.GetGlobalVariable(ExperimentSessionManager.IdVariable) != SessionId) return "セッションIDの同期を待っています";
            if (!sessionManager.HasAppliedSettings || sessionManager.SessionEpoch <= 0) return "新規セッションの準備が必要です（適用済み設定がありません）";
            if (experimentManager == null || experimentManager.CurrentMode != sessionManager.SessionMode) return "適用済み実験方式の一致を待っています";
            if (sessionManager.HasDraftChanges) return "次回設定が未適用です。新規セッションの準備が必要です";
            var logger = experimentManager != null ? experimentManager.GetComponent<ExperimentLogger>() : null;
            if (logger == null || !logger.IsOperational) return "Loggerが有効ではありません";
            if (logger.UnsavedCount != 0 || logger.LastError != "") return "ログの保存が完了していません。保存を再試行してください";
            if (!logger.IsPreparedInCurrentRun || !logger.HasMonotonicTiming) return "新規セッションの準備が必要です（今回の計時を確認できません）";
            if (sessionManager.AcknowledgedCount != sessionManager.SyncTargetCount) return "接続端末の現セッションACK・設定同期を待っています";
            if (ConnectedParticipants != ExpectedParticipants || ExpectedParticipants == 0) return ConnectedParticipants < ExpectedParticipants ? $"参加者が不足しています {ConnectedParticipants} / {ExpectedParticipants}" : $"参加者数を確認してください {ConnectedParticipants} / {ExpectedParticipants}";
            if (experimentManager == null || experimentManager.CurrentState != ExperimentManager.ExperimentState.Idle) return "新規セッションの準備で開始前の状態に戻してください";
            if (SessionVariableTransport.GetGlobalVariable(Network, sessionManager, "experimentMode") != experimentManager.CurrentMode.ToString()) return "方式の同期を待っています";
            foreach (int client in participantRegistry.ParticipantClientNos)
                if (!sessionManager.HasClientAcknowledged(client)) return "参加者の現セッション初期化を待っています";
            foreach (int client in participantRegistry.ParticipantClientNos)
                if (Network.GetClientVariable(ExperimentParticipantRegistry.RoleVariable, client) != "Participant") return "参加者登録の同期を待っています";
            foreach (int client in participantRegistry.ParticipantClientNos)
                if (SessionVariableTransport.GetClientVariable(Network, sessionManager, "experimentState", client) != "Idle") return "参加者の準備を待っています";
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
            bool firstObservation = started == null || started.id != record.id;
            started = record; pending = null;
            if (firstObservation) OnExperimentStarted?.Invoke(record.sessionId, record.id,
                record.mode == "Normal" ? ExperimentManager.ExperimentMode.Normal : ExperimentManager.ExperimentMode.Proposed);
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
    public void NewSessionFromUI() { if (CanNewSession) sessionManager.RequestResetFromUI(); }
    public void SelectNormal() { if (experimentManager != null) experimentManager.SetMode(ExperimentManager.ExperimentMode.Normal); }
    public void SelectProposed() { if (experimentManager != null) experimentManager.SetMode(ExperimentManager.ExperimentMode.Proposed); }
}
