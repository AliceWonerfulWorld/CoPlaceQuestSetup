using System;
using System.Collections.Generic;
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
    private NetSyncManager network;
    private readonly Queue<string> pendingControls = new Queue<string>();
    private string observedControl;
    [Serializable] private class Control { public string id; public string startedAt; public string activatedAt; public string phase; public int owner; public string mode; public long epoch; }
    private Control control;
    private readonly Dictionary<string, string> globalCache = new Dictionary<string, string>();
    private readonly Dictionary<string, string> clientCache = new Dictionary<string, string>();
    private readonly Dictionary<string, string> globalRepairs = new Dictionary<string, string>();
    private readonly Dictionary<string, string> clientRepairs = new Dictionary<string, string>();
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
    public string ResetStartedAtUtc => control != null ? control.startedAt : "";
    public ExperimentManager.ExperimentMode CurrentMode => experimentManager.CurrentMode;
    public ExperimentManager.ExperimentMode SessionMode => control != null && control.mode == "Normal" ? ExperimentManager.ExperimentMode.Normal : ExperimentManager.ExperimentMode.Proposed;
    public bool IsResettingSession { get; private set; }
    public string LastFailure { get; private set; } = "";
    public event Action<string> OnSessionResetStarted;
    public event Action<string> OnSessionStarted;
    private void Start() { started = true; Subscribe(); }
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
    private static bool IsExperimentGlobal(string name) => name != null && (name.StartsWith("normalPlacement_", StringComparison.Ordinal) || name.StartsWith("normalPlacementVersion_", StringComparison.Ordinal) || name == FinalAgreementManager.BoardVariable || name == ProposedRevealCoordinator.PrepareVariable || name == ProposedRevealCoordinator.RevealVariable || name == ProposedSharedPlacementController.ReadyVariable);
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
            OnSessionResetStarted?.Invoke(next.id);
            try { ClearLocal(); BuildDefaults(); }
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
        // Acknowledgement follows observed echoes, not merely accepted/queued writes.
        if (network.GetClientVariable(AckVariable) != CurrentSessionId)
        {
            foreach (var pair in clientDefaults)
                if (SessionVariableTransport.GetClientVariable(network, this, pair.Key) != pair.Value) return;
            network.SetClientVariable(AckVariable, CurrentSessionId);
            return;
        }
        if (control.phase == "resetting" && network.ClientNo == control.owner && !completionQueued)
        {
            foreach (var pair in globalDefaults)
                if (SessionVariableTransport.GetGlobalVariable(network, this, pair.Key) != pair.Value) return;
            foreach (int client in network.GetAliveClients(includeStealthClients: true))
                if (network.GetClientVariable(AckVariable, client) != CurrentSessionId) { ReportTimeout(); return; }
            if (!network.SetGlobalVariable(IdVariable, CurrentSessionId)) { Fail("Session ID send failed"); return; }
            var active = new Control { id = control.id, startedAt = control.startedAt, activatedAt = DateTime.UtcNow.ToString("o"), owner = control.owner, mode = control.mode, epoch = control.epoch, phase = "active" };
            completionQueued = network.SetGlobalVariable(ControlVariable, JsonUtility.ToJson(active));
        }
        if (control.phase == "active") CompleteLocal();
        else ReportTimeout();
    }
    private void ReportTimeout()
    {
        if (Time.unscaledTime >= resetDeadline && string.IsNullOrEmpty(LastFailure)) Fail("Waiting for reset acknowledgements. Reconnect peers or retry New Session; operations remain blocked.");
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
    public bool StartNewSession()
    {
        Subscribe();
        if (participantRegistry == null || !participantRegistry.IsLocalExperimenter) { Debug.LogWarning("[Session Reset Rejected] Local role is not Experimenter", this); return false; }
        if (network == null || !network.IsReady) { Fail("NetSync is not ready"); return false; }
        if (pendingSessionRequestId != null) { Fail("Reset request is awaiting Room acknowledgement"); return false; }
        if (normalPlacementSync == null || finalAgreement == null || candidateSync == null || experimentManager == null) { Fail("Session component references are missing"); return false; }
        if (!normalPlacementSync.HasUnclassifiedZone) { Fail("Unclassified TierZone is missing"); return false; }
        Debug.Log($"[Session Reset Start] Previous Session: {CurrentSessionId}");
        var next = new Control { id = Guid.NewGuid().ToString("N"), startedAt = DateTime.UtcNow.ToString("o"), phase = "resetting", owner = network.ClientNo, mode = experimentManager.CurrentMode.ToString(), epoch = SessionEpoch + 1 };
        // Local operations stop immediately while waiting for the Room echo.
        pendingSessionRequestId = next.id;
        resetDeadline = Time.unscaledTime + 30;
        LastFailure = "";
        IsResettingSession = true;
        if (!network.SetGlobalVariable(ControlVariable, JsonUtility.ToJson(next))) { pendingSessionRequestId = null; IsResettingSession = control != null && control.phase == "resetting"; Fail("Reset notification send failed"); return false; }
        Debug.Log($"[New Session] Session ID: {next.id}");
        return true;
    }
    public bool ResetSession() => StartNewSession();
    [ContextMenu("New Session (Experimenter / Play Mode)")]
    private void NewSessionContextMenu() { if (Application.isPlaying) StartNewSession(); else Debug.LogWarning("[Session Reset Rejected] Enter Play mode first", this); }
}
