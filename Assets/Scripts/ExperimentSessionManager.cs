using System;
using System.Collections.Generic;
using System.Linq;
using Styly.NetSync;
using UnityEngine;

[DefaultExecutionOrder(-1000)]
public class ExperimentSessionManager : MonoBehaviour
{
    public const string ControlVariable = "experimentSessionControl";
    public const string IdVariable = "experimentSessionId";
    public const string AckVariable = "experimentSessionAck";
    [SerializeField] private NetSyncManager netSyncManager;
    [SerializeField] private ExperimentParticipantRegistry participantRegistry;
    [SerializeField] private ExperimentManager experimentManager;
    [SerializeField] private NormalPlacementSync normalPlacementSync;
    [SerializeField] private CandidateSyncTest candidateSync;
    [SerializeField] private FinalAgreementManager finalAgreement;
    [SerializeField] private FinalAgreementUI agreementUI;
    [SerializeField] private PilotCardSetController cardSets;
    private NetSyncManager network;
    private ExperimentLobbyController lobby;
    public ExperimentLobbyController Lobby => lobby != null ? lobby : (lobby = gameObject.scene.GetRootGameObjects().SelectMany(o => o.GetComponentsInChildren<ExperimentLobbyController>(true)).SingleOrDefault());
    private readonly Queue<string> pendingControls = new Queue<string>();
    private string observedControl;
    [Serializable] private class Control { public string id; public string startedAt; public string activatedAt; public string phase; public int owner; public string mode; public string cardSetId; public long epoch; public int settingsVersion; public int expectedParticipantCount; }
    private Control control;
    public const string SettingsAckVariable = "experimentSessionSettingsAck";
    public ExperimentManager.ExperimentMode DraftMode { get; private set; } = ExperimentManager.ExperimentMode.Proposed;
    public string DraftCardSetId { get; private set; } = "A";
    public int DraftExpectedParticipants { get; private set; } = 2;
    public bool HasAppliedSettings => control != null && control.settingsVersion == 1 && control.expectedParticipantCount > 0 &&
        (control.mode == "Normal" || control.mode == "Proposed") && (control.cardSetId == "A" || control.cardSetId == "B" || control.cardSetId == "Development");
    public bool IsPreparationRequestPending => pendingSessionRequestId != null;
    public bool HasReceivedControl => control != null;
    public bool IsLocalInitialized => network != null && HasClientAcknowledged(network.ClientNo);
    public bool AreConnectedClientsInitialized => SyncTargetCount > 0 && AcknowledgedCount == SyncTargetCount;
    public int AppliedExpectedParticipants => HasAppliedSettings ? control.expectedParticipantCount : 0;
    public bool HasDraftChanges => !HasAppliedSettings || DraftMode != SessionMode || DraftCardSetId != CurrentCardSetId || DraftExpectedParticipants != AppliedExpectedParticipants;
    public bool SetDraft(ExperimentManager.ExperimentMode mode, string set, int count)
    {
        if (participantRegistry == null || !participantRegistry.IsLocalExperimenter || IsResettingSession ||
            !Enum.IsDefined(typeof(ExperimentManager.ExperimentMode), mode) || count < 1 ||
            (set != "A" && set != "B" && set != "Development")) return false;
        var logger=experimentManager!=null?experimentManager.GetComponent<ExperimentLogger>():null;
        if(logger!=null && !logger.Data.CanSelect(mode,set))return false;
        DraftMode = mode; DraftCardSetId = set; DraftExpectedParticipants = count; return true;
    }
    public string SettingsSignature => HasAppliedSettings ? CurrentSessionId + ":" + SessionEpoch + ":" + control.mode + ":" + CurrentCardSetId + ":" + AppliedExpectedParticipants : "";
    public bool HasClientAcknowledged(int client) => network != null && HasAppliedSettings &&
        network.GetClientVariable(AckVariable, client) == CurrentSessionId && network.GetClientVariable(SettingsAckVariable, client) == SettingsSignature;
    public int SyncTargetCount { get { if (network == null || !network.IsReady) return 0; var clients = new HashSet<int>(network.GetAliveClients(includeStealthClients: true)); clients.Add(network.ClientNo); return clients.Count; } }
    public int AcknowledgedCount { get { if (network == null || !network.IsReady) return 0; var clients = new HashSet<int>(network.GetAliveClients(includeStealthClients: true)); clients.Add(network.ClientNo); int count = 0; foreach (int client in clients) if (HasClientAcknowledged(client)) count++; return count; } }
    public int InitializedParticipants { get { int count = 0; if (participantRegistry != null) foreach (int client in participantRegistry.ParticipantClientNos) if (HasClientAcknowledged(client)) count++; return count; } }
    public string PreparationStatus
    {
        get
        {
            if (localResetFailed) return "準備失敗：保存を確認し、端末構成を修復して全端末とRoomを再起動してください";
            if (IsResettingSession && network != null && network.IsReady && control != null && !new HashSet<int>(network.GetAliveClients(includeStealthClients: true)).Contains(control.owner)) return "準備失敗：準備元端末が未接続です。同じ端末を再接続してください";
            if (IsResettingSession && Time.unscaledTime >= resetDeadline) return "同期タイムアウト：端末を再接続してください。強制準備はできません";
            if (pendingSessionRequestId != null) return "新しいセッションの通知を送信中（要求受付済み）";
            if (IsResettingSession && !localDefaultsQueued) return "実験条件を適用中（新しい通知を受信済み）";
            if (IsResettingSession) return "接続端末の初期化を待機中";
            if (!HasAppliedSettings) return "新規セッションの準備が必要です";
            if (AcknowledgedCount != SyncTargetCount) return "接続端末の初期化を待機中";
            if (participantRegistry.ParticipantClientNos.Count != AppliedExpectedParticipants) return "接続中端末の同期完了・必要参加者の接続待ち";
            var lobby = Lobby;
            return lobby != null && lobby.CanStart ? "セッションの準備完了" : "接続中端末の同期完了";
        }
    }

    private readonly Dictionary<string, string> globalCache = new Dictionary<string, string>();
    private readonly Dictionary<string, string> clientCache = new Dictionary<string, string>();
    private readonly Dictionary<string, string> globalRepairs = new Dictionary<string, string>();
    private readonly Dictionary<string, string> clientRepairs = new Dictionary<string, string>();
    public string CurrentCardSetId => control != null ? (string.IsNullOrEmpty(control.cardSetId) ? "Development" : control.cardSetId) : "Development";
    public long SessionEpoch => control != null ? control.epoch : 0;
    public string ReadGlobalValue(string name, string raw) => ReadValue(globalCache, name, raw);
    public string ReadClientValue(int client, string name, string raw) => ReadValue(clientCache, client + ":" + name, raw);
    private string ReadValue(Dictionary<string,string> cache, string key, string raw)
    {
        string value = SessionVariableTransport.Decode(this, raw);
        if (value != null) { cache[key] = value; return value; }
        return cache.TryGetValue(key, out var current) ? current : null;
    }
    private readonly Dictionary<string, string> globalDefaults = new Dictionary<string, string>();
    private readonly Dictionary<string, string> clientDefaults = new Dictionary<string, string>();
    private string appliedId, completedId;
    private string pendingSessionRequestId;
    private bool started, wasReady, ownerDefaultsQueued, localDefaultsQueued, completionQueued, localResetFailed;
    private bool controlRepairQueued;
    private float nextAttempt, resetDeadline, nextAckRepair;
    public string CurrentSessionId => control != null ? control.id : "";
    public string SessionStartedAtUtc => control != null ? control.activatedAt ?? "" : "";
    public bool HasResetControl => control != null && control.phase == "resetting";
    public string ResetStartedAtUtc => control != null ? control.startedAt : "";
    public ExperimentManager.ExperimentMode CurrentMode => experimentManager.CurrentMode;
    public ExperimentManager.ExperimentMode SessionMode => control != null && control.mode == "Normal" ? ExperimentManager.ExperimentMode.Normal : ExperimentManager.ExperimentMode.Proposed;
    public bool IsResettingSession { get; private set; }
    public string LastFailure { get; private set; } = "";
    public string LogSaveFailure => experimentManager != null ? experimentManager.GetComponent<ExperimentLogger>()?.LastError ?? "ExperimentLogger is missing" : "";
    public event Action<string> OnSessionResetStarted;
    public event Action<string> OnSessionStarted;
    private void Start() { started = true; Subscribe(); if (!Application.isMobilePlatform) EnsureResetUI(); }
    private void OnEnable() { if (started) Subscribe(); }
    private void OnDisable()
    {
        if (network != null) { network.OnReady.RemoveListener(OnReady); network.OnGlobalVariableChanged.RemoveListener(OnGlobal); network.OnClientVariableChanged.RemoveListener(OnClient); }
        network = null; wasReady = false;
        pendingControls.Clear(); observedControl = null;
    }
    private void Subscribe()
    {
        if (network != null) return;
        network = netSyncManager != null ? netSyncManager : NetSyncManager.Instance;
        if (network == null) return;
        network.OnReady.AddListener(OnReady); network.OnGlobalVariableChanged.AddListener(OnGlobal);
        network.OnClientVariableChanged.AddListener(OnClient);
        if (network.IsReady) OnReady();
    }
    private void OnReady()
    {
        if (network == null || !network.IsReady) return;
        wasReady = true;
        // Reconnection restores the current session; it never creates a new one.
        pendingControls.Enqueue(network.GetGlobalVariable(ControlVariable));
    }
    private void OnGlobal(string name, string oldValue, string value)
    {
        if (name == ControlVariable) { pendingControls.Enqueue(value); return; }
        if (control == null || !IsExperimentGlobal(name)) return;
        string current = SessionVariableTransport.Decode(this, value);
        if (current != null) { globalCache[name] = current; return; }
        string previous = SessionVariableTransport.Decode(this, oldValue);
        if (previous != null) globalCache[name] = previous;
        if (network.ClientNo == control.owner && globalCache.TryGetValue(name, out var restored))
            globalRepairs[name] = restored;
    }
    private void OnClient(int client, string name, string oldValue, string value)
    {
        if (control == null || !IsExperimentClient(name)) return;
        string key = client + ":" + name;
        string current = SessionVariableTransport.Decode(this, value);
        if (current != null) { clientCache[key] = current; return; }
        string previous = SessionVariableTransport.Decode(this, oldValue);
        if (previous != null) clientCache[key] = previous;
        if (client == network.ClientNo && clientCache.TryGetValue(key, out var restored))
            clientRepairs[name] = restored;
    }
    private static bool IsExperimentGlobal(string name) => name != null && (name.StartsWith("normalPlacement_", StringComparison.Ordinal) || name.StartsWith("normalPlacementVersion_", StringComparison.Ordinal) || name == FinalAgreementManager.BoardVariable || name == ProposedRevealCoordinator.PrepareVariable || name == ProposedRevealCoordinator.RevealVariable || name == ProposedSharedPlacementController.ReadyVariable || name == ExperimentLobbyController.StartVariable);
    private static bool IsExperimentClient(string name) => name != null && (name.StartsWith("candidate_", StringComparison.Ordinal) || name.StartsWith("answered_", StringComparison.Ordinal) || name.StartsWith("selectedAt_", StringComparison.Ordinal) || name == FinalAgreementManager.ReadyVariable || name == FinalAgreementManager.HeldVariable || name == "experimentState" || name == ProposedRevealCoordinator.AckVariable || name == IndependentAnswerSubmission.SubmittedVariable || name == IndependentAnswerSubmission.ProofVariable);
    private void ReadControl(string value)
    {
        if (string.IsNullOrEmpty(value)) return;
        Control next;
        try { next = JsonUtility.FromJson<Control>(value); } catch { return; }
        if (next == null || string.IsNullOrEmpty(next.id) || (next.phase != "resetting" && next.phase != "active")) return;
        // A delayed phase notification must not regress an already active session.
        if (control != null && ((next.id == control.id && control.phase == "active" && next.phase == "resetting") || next.epoch < control.epoch))
        {
            controlRepairQueued = network.ClientNo == control.owner;
            return;
        }
        if (control != null && next.id == control.id && (next.cardSetId != control.cardSetId || next.mode != control.mode || next.expectedParticipantCount != control.expectedParticipantCount || next.settingsVersion != control.settingsVersion || next.epoch != control.epoch || next.owner != control.owner)) return;
        control = next;
        controlRepairQueued = false;
        if (pendingSessionRequestId == next.id) pendingSessionRequestId = null;
        if (appliedId != next.id)
        {
            globalCache.Clear(); clientCache.Clear(); globalRepairs.Clear(); clientRepairs.Clear(); localResetFailed = false;
            appliedId = next.id; completedId = null; IsResettingSession = true;
            LastFailure = ""; ownerDefaultsQueued = localDefaultsQueued = completionQueued = false;
            nextAttempt = 0; resetDeadline = Time.unscaledTime + 30;
            Debug.Log($"[Session Reset Receive] Session: {next.id}");
            try
            {
                if (cardSets != null) cardSets.ApplySessionSet(CurrentCardSetId);
                else if (CurrentCardSetId != "Development") throw new InvalidOperationException("Pilot card set assets are missing");
                OnSessionResetStarted?.Invoke(next.id);
                ClearLocal();
                if (HasAppliedSettings) experimentManager.ApplySessionMode(SessionMode);
                BuildDefaults();
            }
            catch (Exception ex) { localResetFailed = true; Fail(ex.Message); return; }
            // Existing current-session answers survive a same-session reconnect.
            localDefaultsQueued = network.GetClientVariable(AckVariable) == next.id;
        }
        if (next.phase == "resetting") IsResettingSession = true;
        // Advance only from Update, after NetSync has finished applying its snapshot.
    }
    private void ClearLocal()
    {
        finalAgreement.ResetLocalSession(CurrentSessionId);
        candidateSync.ResetLocalSession();
        normalPlacementSync.ResetLocalSession();
        experimentManager.ResetExperiment();
        if (agreementUI != null) agreementUI.ResetLocalSession();
        Debug.Log("[Session Reset] Normal / Proposed / readiness cleared; cards returned to Unclassified");
    }
    private void BuildDefaults()
    {
        globalDefaults.Clear(); clientDefaults.Clear();
        if (HasAppliedSettings) globalDefaults["experimentMode"] = control.mode;
        // Include historical card keys as well as current Inspector card references.
        var cards = new HashSet<string>(normalPlacementSync.CardIds);
        foreach (var pair in network.GetAllGlobalVariables())
            foreach (string prefix in new[] { "normalPlacement_", "normalConfirmation_", "normalPlacementVersion_", "normalFinalTier_", "normalConfirmed_" })
                if (pair.Key.StartsWith(prefix, StringComparison.Ordinal)) cards.Add(pair.Key.Substring(prefix.Length));
        foreach (var pair in network.GetAllClientVariables(network.ClientNo))
            foreach (string prefix in new[] { "candidate_", "answered_", "selectedAt_" })
                if (pair.Key.StartsWith(prefix, StringComparison.Ordinal)) cards.Add(pair.Key.Substring(prefix.Length));
        foreach (string card in cards)
        {
            globalDefaults["normalPlacement_" + card] = "Unclassified";
            globalDefaults["normalPlacementVersion_" + card] = JsonUtility.ToJson(new PlacementVersion { tier = "Unclassified", revision = CurrentSessionId });
            // Retire legacy confirmation keys only if present; do not create unused keys.
            foreach (string prefix in new[] { "normalConfirmation_", "normalFinalTier_", "normalConfirmed_" })
                if (network.GetAllGlobalVariables().ContainsKey(prefix + card)) globalDefaults[prefix + card] = "";
            clientDefaults["candidate_" + card] = "Unclassified";
            clientDefaults["answered_" + card] = "false";
            clientDefaults["selectedAt_" + card] = "";
        }
        globalDefaults[FinalAgreementManager.BoardVariable] = JsonUtility.ToJson(new BoardReset { confirmed = false, sessionId = CurrentSessionId });
        globalDefaults[ProposedRevealCoordinator.PrepareVariable] = "";
        globalDefaults[ProposedRevealCoordinator.RevealVariable] = "";
        globalDefaults[ProposedSharedPlacementController.ReadyVariable] = "";
        globalDefaults[ExperimentLobbyController.StartVariable] = "";
        clientDefaults[ProposedRevealCoordinator.AckVariable] = "";
        clientDefaults[IndependentAnswerSubmission.SubmittedVariable] = "false";
        clientDefaults[IndependentAnswerSubmission.ProofVariable] = "";
        clientDefaults[FinalAgreementManager.ReadyVariable] = "";
        clientDefaults[FinalAgreementManager.HeldVariable] = "none";
        clientDefaults["experimentState"] = "Idle";
    }
    [Serializable] private class PlacementVersion { public string tier; public string revision; }
    [Serializable] private class BoardReset { public bool confirmed; public string sessionId; }
    private void Update()
    {
        Subscribe();
        if (network == null || !network.IsReady) { wasReady = false; return; }
        if (!wasReady) OnReady();
        // Resetting cards may publish variables through other components. Never do
        // that inside NetSync's change callback while it is enumerating its cache.
        while (pendingControls.Count > 0)
        {
            string value = pendingControls.Dequeue();
            observedControl = value;
            ReadControl(value);
        }
        // Recover even when another listener interrupted notification dispatch,
        // or the component was temporarily disabled when the change arrived.
        string latestControl = network.GetGlobalVariable(ControlVariable);
        if (latestControl != observedControl)
        {
            observedControl = latestControl;
            ReadControl(latestControl);
        }
        if (controlRepairQueued && pendingSessionRequestId == null && network.SetGlobalVariable(ControlVariable, JsonUtility.ToJson(control))) controlRepairQueued = false;
        if (control != null && control.phase == "active" && localDefaultsQueued && !localResetFailed &&
            network.GetClientVariable(AckVariable) != CurrentSessionId && Time.unscaledTime >= nextAckRepair)
        {
            nextAckRepair = Time.unscaledTime + 1;
            network.SetClientVariable(SettingsAckVariable, SettingsSignature);
            network.SetClientVariable(AckVariable, CurrentSessionId);
        }
        // NetSync enumerates its cache while invoking change listeners. Defer
        // repairs until Update rather than modifying that dictionary in a listener.
        FlushRepairs(globalRepairs, true); FlushRepairs(clientRepairs, false);
        TryAdvance();
    }
    private void FlushRepairs(Dictionary<string,string> repairs, bool global)
    {
        foreach (var pair in new List<KeyValuePair<string,string>>(repairs))
        {
            string raw = global ? network.GetGlobalVariable(pair.Key) : network.GetClientVariable(pair.Key);
            // A current-session update that arrived meanwhile supersedes the repair.
            if (SessionVariableTransport.Decode(this, raw) != null) { repairs.Remove(pair.Key); continue; }
            string value = SessionVariableTransport.EncodeRepair(this, pair.Value);
            if (global ? network.SetGlobalVariable(pair.Key, value) : network.SetClientVariable(pair.Key, value)) repairs.Remove(pair.Key);
        }
    }
    private void TryAdvance()
    {
        if (pendingSessionRequestId != null) { ReportTimeout(); return; }
        if (localResetFailed || control == null || network == null || !network.IsReady || !IsResettingSession) return;
        ReportTimeout();
        if (Time.unscaledTime >= nextAttempt)
        {
            nextAttempt = Time.unscaledTime + 1;
            if (!localDefaultsQueued)
            {
                bool ok = true;
                foreach (var pair in clientDefaults) ok &= SessionVariableTransport.SetClientVariable(network, this, pair.Key, pair.Value);
                localDefaultsQueued = ok;
                if (!ok && string.IsNullOrEmpty(LastFailure)) Fail("Client variable initialization failed");
            }
            if (control.phase == "resetting" && network.ClientNo == control.owner && !ownerDefaultsQueued)
            {
                bool ok = true;
                foreach (var pair in globalDefaults) ok &= SessionVariableTransport.SetGlobalVariable(network, this, pair.Key, pair.Value);
                ownerDefaultsQueued = ok;
                if (!ok && string.IsNullOrEmpty(LastFailure)) Fail("Global variable initialization failed");
            }
        }
        if (!localDefaultsQueued) return;
        if (HasAppliedSettings && (experimentManager.CurrentMode != SessionMode || participantRegistry.ExpectedParticipantCount != AppliedExpectedParticipants ||
            (cardSets != null && cardSets.ActiveSetId != CurrentCardSetId))) { localResetFailed = true; Fail("Applied session settings do not match local configuration"); return; }
        if (HasAppliedSettings && network.GetClientVariable(SettingsAckVariable) != SettingsSignature)
        { network.SetClientVariable(SettingsAckVariable, SettingsSignature); return; }
        // Acknowledgement follows observed echoes, not merely accepted/queued writes.
        if (network.GetClientVariable(AckVariable) != CurrentSessionId)
        {
            foreach (var pair in clientDefaults)
                if (SessionVariableTransport.GetClientVariable(network, this, pair.Key) != pair.Value) return;
            network.SetClientVariable(SettingsAckVariable, SettingsSignature);
            network.SetClientVariable(AckVariable, CurrentSessionId);
            return;
        }
        if (control.phase == "resetting" && network.ClientNo == control.owner && !completionQueued)
        {
            foreach (var pair in globalDefaults)
                if (SessionVariableTransport.GetGlobalVariable(network, this, pair.Key) != pair.Value) return;
            foreach (int client in network.GetAliveClients(includeStealthClients: true))
                if (HasAppliedSettings ? !HasClientAcknowledged(client) : network.GetClientVariable(AckVariable, client) != CurrentSessionId) { ReportTimeout(); return; }
            if (!network.SetGlobalVariable(IdVariable, CurrentSessionId)) { Fail("Session ID send failed"); return; }
            var active = new Control { id = control.id, startedAt = control.startedAt, activatedAt = DateTime.UtcNow.ToString("o"), owner = control.owner, mode = control.mode, cardSetId = control.cardSetId, epoch = control.epoch, settingsVersion = control.settingsVersion, expectedParticipantCount = control.expectedParticipantCount, phase = "active" };
            completionQueued = network.SetGlobalVariable(ControlVariable, JsonUtility.ToJson(active));
        }
        if (control.phase == "active") CompleteLocal();
        else ReportTimeout();
    }
    private void ReportTimeout()
    {
        if (Time.unscaledTime >= resetDeadline && string.IsNullOrEmpty(LastFailure)) Fail("Waiting for reset acknowledgements. Reconnect the same peers; operations remain blocked.");
    }
    private void Fail(string reason) { LastFailure = reason; Debug.LogWarning($"[Session Reset Failed] Reason: {reason}", this); }
    private void CompleteLocal()
    {
        IsResettingSession = false;
        LastFailure = "";
        experimentManager.RestoreSessionState();
        normalPlacementSync.RestoreSessionPlacements();
        finalAgreement.RestoreSessionAgreement();
        candidateSync.RestoreSessionCandidates();
        if (completedId == CurrentSessionId) return;
        completedId = CurrentSessionId;
        Debug.Log($"[Session Reset Complete] Session ID: {CurrentSessionId}");
        OnSessionStarted?.Invoke(CurrentSessionId);
    }
    public SessionResetUI EnsureResetUI()
    {
        var ui = GetComponent<SessionResetUI>();
        if (ui == null) ui = gameObject.AddComponent<SessionResetUI>();
        ui.Configure(this, participantRegistry);
        return ui;
    }
    public void RequestResetFromUI() => EnsureResetUI().RequestConfirmation();
    public bool StartNewSession()
    {
        Subscribe();
        if (participantRegistry == null || !participantRegistry.IsLocalExperimenter) { Debug.LogWarning("[Session Reset Rejected] Local role is not Experimenter", this); return false; }
        if (network == null || !network.IsReady) { Fail("NetSync is not ready"); return false; }
        if (IsResettingSession || pendingSessionRequestId != null) { Fail("Reset is already in progress; waiting for Room / participant acknowledgement"); return false; }
        if (normalPlacementSync == null || finalAgreement == null || candidateSync == null || experimentManager == null) { Fail("Session component references are missing"); return false; }
        if (!normalPlacementSync.HasUnclassifiedZone) { Fail("Unclassified TierZone is missing"); return false; }
        var logger = experimentManager.GetComponent<ExperimentLogger>();
        if (logger == null || !logger.TryFlushBeforeReset()) { Fail("CSV save failed; Reset blocked: " + (logger != null ? logger.LastError : "ExperimentLogger is missing")); return false; }
        string metadataError=logger.Data.ValidatePreparation();
        if(metadataError!=""){Fail(metadataError);return false;}
        Debug.Log($"[Session Reset Start] Previous Session: {CurrentSessionId}");
        var next = new Control { id = Guid.NewGuid().ToString("N"), startedAt = DateTime.UtcNow.ToString("o"), phase = "resetting", owner = network.ClientNo, mode = DraftMode.ToString(), cardSetId = DraftCardSetId, expectedParticipantCount = DraftExpectedParticipants, settingsVersion = 1, epoch = SessionEpoch + 1 };
        logger.Data.Prepare(next.id,next.epoch,next.startedAt);
        // Local operations stop immediately while waiting for the Room echo.
        pendingSessionRequestId = next.id;
        resetDeadline = Time.unscaledTime + 30;
        LastFailure = "";
        IsResettingSession = true;
        if (!network.SetGlobalVariable(ControlVariable, JsonUtility.ToJson(next))) { logger.Data.CancelPreparation(next.id); pendingSessionRequestId = null; IsResettingSession = control != null && control.phase == "resetting"; Fail("Reset notification send failed"); return false; }
        logger.MarkPreparationRequested(next.id);
        logger.MarkResetAccepted();
        Debug.Log($"[New Session] Session ID: {next.id}");
        return true;
    }
    public bool ResetSession() => StartNewSession();
    [ContextMenu("New Session (Experimenter / Play Mode)")]
    private void NewSessionContextMenu() { if (Application.isPlaying) StartNewSession(); else Debug.LogWarning("[Session Reset Rejected] Enter Play mode first", this); }
}
