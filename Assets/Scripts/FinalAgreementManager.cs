using System;
using System.Collections.Generic;
using System.Text;
using System.Security.Cryptography;
using Styly.NetSync;
using UnityEngine;

// Agreement transport is independent of XR/UI. The same placement adapter serves Normal and Proposed final shared placement.
public class FinalAgreementManager : MonoBehaviour
{
    public const string ReadyVariable = "finalReady";
    public const string HeldVariable = "finalBoardHeld";
    public const string BoardVariable = "finalBoardAgreement";
    private const string VersionPrefix = "normalPlacementVersion_";
    [SerializeField] private ExperimentManager experimentManager;
    [SerializeField] private ExperimentParticipantRegistry participantRegistry;
    [SerializeField] private NormalPlacementSync placementSync;
    [SerializeField] private NetSyncManager netSyncManager;
    [SerializeField] private ExperimentSessionManager sessionManager;
    private NetSyncManager network;
    private bool started, networkWasReady, wasNormal, publishRequested;
    private string lastFingerprint, lastHeld, lastStatus;
    private string sessionId = "initial";
    private string pendingReady;
    private float nextPublishAttempt;
    private readonly Dictionary<string, string> finalPlacements = new Dictionary<string, string>();
    public event Action OnStatusChanged;
    public event Action<IReadOnlyDictionary<string, string>> OnBoardConfirmed;
    public bool IsBoardConfirmed { get; private set; }
    public string StatusMessage { get; private set; } = "";
    public bool IsNormalPhase => experimentManager != null && experimentManager.CurrentMode == ExperimentManager.ExperimentMode.Normal;
    public bool IsAgreementPhase => placementSync != null && placementSync.IsSharedPlacementPhase;
    public int LocalClientNo => network != null ? network.ClientNo : 0;
    public int ExpectedParticipantCount => participantRegistry != null ? participantRegistry.ExpectedParticipantCount : 0;
    public bool LocalApprovalPending => pendingReady != null;
    public bool IsLocalParticipant => participantRegistry != null && participantRegistry.IsLocalParticipant;
    public bool IsLocalReady => IsAgreementPhase && network != null && network.IsReady && IsParticipantReady(network.ClientNo);
    public bool IsLocalInteractionLocked => IsAgreementPhase && (IsBoardConfirmed || IsLocalReady || !string.IsNullOrEmpty(pendingReady));
    public bool CanToggleReady => !SessionResetting && IsAgreementPhase && IsLocalParticipant && network != null && network.IsReady && !IsBoardConfirmed;
    public IReadOnlyList<int> Participants => participantRegistry != null ? participantRegistry.ParticipantClientNos : Array.Empty<int>();
    [Serializable] private class PlacementVersion { public string tier; public string revision; }
    [Serializable] public class FinalEntry { public string cardId; public string tier; public string revision; }
    [Serializable] private class BoardRecord
    {
        public bool confirmed;
        public string sessionId;
        public string fingerprint;
        public int[] participants;
        public string readyProof;
        public FinalEntry[] placements;
    }

    public IReadOnlyDictionary<string, string> GetFinalPlacements() => new Dictionary<string, string>(finalPlacements);
    public string GetFinalTier(string cardId) => cardId != null && finalPlacements.TryGetValue(cardId, out var tier) ? tier : null;
    private bool SessionResetting => sessionManager != null && sessionManager.IsResettingSession;
    private void Start() { started = true; Subscribe(); }
    private void OnEnable() { if (started) Subscribe(); }
    private void OnDisable()
    {
        if (network != null)
        {
            network.OnReady.RemoveListener(OnReady);
            network.OnGlobalVariableChanged.RemoveListener(OnGlobalChanged);
            network.OnClientVariableChanged.RemoveListener(OnClientChanged);
        }
        network = null; networkWasReady = false; pendingReady = null;
    }
    private void Subscribe()
    {
        if (network != null) return;
        network = netSyncManager != null ? netSyncManager : NetSyncManager.Instance;
        if (network == null) return;
        network.OnReady.AddListener(OnReady);
        network.OnGlobalVariableChanged.AddListener(OnGlobalChanged);
        network.OnClientVariableChanged.AddListener(OnClientChanged);
        if (network.IsReady) OnReady();
    }
    private void OnReady()
    {
        if (SessionResetting || network == null || !network.IsReady) return;
        networkWasReady = true; lastFingerprint = null; lastHeld = null; pendingReady = null;
        IsBoardConfirmed = false; finalPlacements.Clear(); sessionId = "initial";
        placementSync.SetBoardConfirmation(finalPlacements, false);
        RestoreBoard(SessionVariableTransport.GetGlobalVariable(network, sessionManager, BoardVariable));
        // A reconnect must not silently reuse this participant's old approval.
        if (sessionManager == null && !IsBoardConfirmed && IsLocalParticipant) SessionVariableTransport.SetClientVariable(network, sessionManager, ReadyVariable, "");
    }
    private void Update()
    {
        Subscribe();
        if (SessionResetting) return;
        if (network == null || !network.IsReady) { networkWasReady = false; return; }
        if (!networkWasReady) OnReady();
        if (IsAgreementPhase != wasNormal)
        {
            wasNormal = IsAgreementPhase; lastFingerprint = null;
            if (wasNormal) RestoreBoard(SessionVariableTransport.GetGlobalVariable(network, sessionManager, BoardVariable));
            else { pendingReady = null; if (IsLocalParticipant) SessionVariableTransport.SetClientVariable(network, sessionManager, ReadyVariable, ""); }
            Changed();
        }
        if (!IsAgreementPhase) return;
        PublishHeldCards();
        string fingerprint = CurrentFingerprint();
        if (lastFingerprint != fingerprint)
        {
            bool hadReady = !string.IsNullOrEmpty(pendingReady);
            foreach (int client in Participants) hadReady |= !string.IsNullOrEmpty(SessionVariableTransport.GetClientVariable(network, sessionManager, ReadyVariable, client));
            if (lastFingerprint != null && !IsBoardConfirmed)
            {
                if (pendingReady != fingerprint) pendingReady = null;
                if (IsLocalParticipant && SessionVariableTransport.GetClientVariable(network, sessionManager, ReadyVariable) != fingerprint) SessionVariableTransport.SetClientVariable(network, sessionManager, ReadyVariable, "");
                if (hadReady) Debug.Log("[Final Agreement Reset] Board changed / All participant readiness cleared", this);
            }
            lastFingerprint = fingerprint; publishRequested = false; Changed();
        }
        if (pendingReady != null && SessionVariableTransport.GetClientVariable(network, sessionManager, ReadyVariable) == pendingReady) pendingReady = null;
    }
    private void LateUpdate()
    {
        if (SessionResetting || !IsAgreementPhase || network == null || !network.IsReady) return;
        participantRegistry.RefreshParticipants();
        int ready = 0; foreach (int client in Participants) if (IsParticipantReady(client)) ready++;
        string status = $"{ready}/{Participants.Count}/{IsBoardConfirmed}/{StatusMessage}";
        if (status != lastStatus)
        {
            lastStatus = status;
            Debug.Log($"[Final Agreement Status] {ready} / {Participants.Count} participants ready");
            Changed();
        }
        if (IsBoardConfirmed || publishRequested || Time.unscaledTime < nextPublishAttempt || Participants.Count != participantRegistry.ExpectedParticipantCount || Participants.Count == 0 || ready != Participants.Count) return;
        // Only one deterministic participant publishes the atomic result.
        if (network.ClientNo != Participants[0] || !IsLocalParticipant) return;
        string fingerprint = CurrentFingerprint();
        var entries = CurrentEntries();
        if (string.IsNullOrEmpty(fingerprint) || entries == null) { FailAgreement("Unclassified cards remain or placement synchronization is pending"); return; }
        foreach (int client in Participants)
            if (SessionVariableTransport.GetClientVariable(network, sessionManager, HeldVariable, client) != "none") return;
        foreach (string cardId in placementSync.CardIds)
            if (!placementSync.GetCard(cardId).CanConfirmNormalPlacement) return;
        var peers = new List<int>(Participants);
        var record = new BoardRecord { confirmed = true, sessionId = sessionId, fingerprint = fingerprint, participants = peers.ToArray(), readyProof = fingerprint, placements = entries };
        nextPublishAttempt = Time.unscaledTime + 1f;
        Debug.Log("[Final Agreement Complete] All participants ready");
        string payload = JsonUtility.ToJson(record);
        if (Encoding.UTF8.GetByteCount(SessionVariableTransport.Encode(sessionManager, payload)) > 1024)
        {
            StatusMessage = "Board result exceeds NetSync variable limit";
            publishRequested = true; // Avoid repeated writes; a Board change/reset permits retry.
            Debug.LogWarning("[Final Agreement Reset] Board result exceeds NetSync 1024-byte limit", this);
            Changed(); return;
        }
        publishRequested = SessionVariableTransport.SetGlobalVariable(network, sessionManager, BoardVariable, payload);
    }
    public bool IsParticipantReady(int clientNo)
    {
        if (SessionResetting || !IsAgreementPhase || network == null || !network.IsReady) return false;
        if (IsBoardConfirmed) return ContainsParticipant(clientNo);
        string fingerprint = CurrentFingerprint();
        return !string.IsNullOrEmpty(fingerprint) && SessionVariableTransport.GetClientVariable(network, sessionManager, ReadyVariable, clientNo) == fingerprint;
    }
    private bool ContainsParticipant(int clientNo) { foreach (int client in Participants) if (client == clientNo) return true; return false; }
    public void ToggleLocalReady()
    {
        if (!CanToggleReady || !ContainsParticipant(network.ClientNo)) return;
        if (IsLocalReady || pendingReady != null)
        {
            pendingReady = null; SessionVariableTransport.SetClientVariable(network, sessionManager, ReadyVariable, ""); StatusMessage = "";
            Debug.Log($"[Final Agreement Cancel] Participant {DisplayId(network.ClientNo)}"); Changed(); return;
        }
        foreach (string cardId in placementSync.CardIds)
            if (!placementSync.GetCard(cardId).CanConfirmNormalPlacement) { StatusMessage = "Release all cards before confirming"; Changed(); return; }
        foreach (string cardId in placementSync.CardIds)
            if (!IsTier(SessionVariableTransport.GetGlobalVariable(network, sessionManager, "normalPlacement_" + cardId))) { FailAgreement("Unclassified cards remain. Place every card in A-D."); return; }
        if (!EnsureVersions()) { StatusMessage = "Waiting for placement synchronization"; Changed(); return; }
        string fingerprint = CurrentFingerprint();
        if (string.IsNullOrEmpty(fingerprint) || CurrentEntries() == null)
        {
            FailAgreement("Unclassified cards remain. Place every card in A-D."); return;
        }
        pendingReady = fingerprint;
        if (!SessionVariableTransport.SetClientVariable(network, sessionManager, ReadyVariable, fingerprint)) { pendingReady = null; StatusMessage = "Ready send failed"; Changed(); return; }
        StatusMessage = "";
        Debug.Log($"[Final Agreement Ready] Participant {DisplayId(network.ClientNo)}"); Changed();
    }
    public int DisplayId(int client) { for (int i = 0; i < Participants.Count; i++) if (Participants[i] == client) return i + 1; return client; }
    private void FailAgreement(string message)
    {
        StatusMessage = message; pendingReady = null; publishRequested = false;
        // Invalid boards cannot carry effective readiness: their fingerprint is empty.
        if (IsLocalParticipant) SessionVariableTransport.SetClientVariable(network, sessionManager, ReadyVariable, "");
        Debug.LogWarning($"[Final Agreement Reset] {message}", this); Changed();
    }
    public bool PreparePlacementChange(string cardId, string tier)
    {
        if (SessionResetting || !IsAgreementPhase || network == null || !network.IsReady || IsLocalInteractionLocked) return false;
        string existing = SessionVariableTransport.GetGlobalVariable(network, sessionManager, "normalPlacement_" + cardId);
        var version = ReadVersion(cardId);
        if (existing == tier && version != null && version.tier == tier) return true;
        // Tier and unique revision are atomic. Even A -> B -> A invalidates old approvals.
        return SessionVariableTransport.SetGlobalVariable(network, sessionManager, VersionPrefix + cardId,
            JsonUtility.ToJson(new PlacementVersion { tier = tier, revision = Guid.NewGuid().ToString("N") }));
    }
    private bool EnsureVersions()
    {
        foreach (string cardId in placementSync.CardIds)
        {
            string tier = SessionVariableTransport.GetGlobalVariable(network, sessionManager, "normalPlacement_" + cardId);
            if (tier == null) return false;
            if (ReadVersion(cardId) == null)
                if (!SessionVariableTransport.SetGlobalVariable(network, sessionManager, VersionPrefix + cardId, JsonUtility.ToJson(new PlacementVersion { tier = tier, revision = Guid.NewGuid().ToString("N") }))) return false;
        }
        return CurrentEntries() != null;
    }
    private PlacementVersion ReadVersion(string cardId)
    {
        string value = SessionVariableTransport.GetGlobalVariable(network, sessionManager, VersionPrefix + cardId);
        if (string.IsNullOrEmpty(value)) return null;
        try { var result = JsonUtility.FromJson<PlacementVersion>(value); return result != null && !string.IsNullOrEmpty(result.revision) ? result : null; } catch { return null; }
    }
    private FinalEntry[] CurrentEntries()
    {
        var ids = new List<string>(placementSync.CardIds); ids.Sort(StringComparer.Ordinal);
        var entries = new List<FinalEntry>();
        foreach (string id in ids)
        {
            var version = ReadVersion(id); string tier = SessionVariableTransport.GetGlobalVariable(network, sessionManager, "normalPlacement_" + id);
            if (version == null || !IsTier(tier) || version.tier != tier) return null;
            entries.Add(new FinalEntry { cardId = id, tier = tier, revision = version.revision });
        }
        return entries.Count > 0 ? entries.ToArray() : null;
    }
    private string CurrentFingerprint()
    {
        if (network == null || !network.IsReady || placementSync == null) return null;
        var entries = CurrentEntries(); if (entries == null) return null;
        return Fingerprint(sessionId, new List<int>(Participants).ToArray(), entries);
    }
    private static string Fingerprint(string session, int[] participants, FinalEntry[] entries)
    {
        var b = new StringBuilder(session); foreach (int client in participants) b.Append('|').Append(client);
        foreach (var entry in entries) b.Append('|').Append(entry.cardId).Append(':').Append(entry.tier).Append(':').Append(entry.revision);
        using (var sha = SHA256.Create()) return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(b.ToString())));
    }
    private static bool IsTier(string tier) => tier == "A" || tier == "B" || tier == "C" || tier == "D";
    private void PublishHeldCards()
    {
        if (!IsLocalParticipant) return;
        var held = new List<string>(); foreach (string id in placementSync.CardIds) if (!placementSync.GetCard(id).CanConfirmNormalPlacement) held.Add(id);
        held.Sort(StringComparer.Ordinal); string value = held.Count == 0 ? "none" : string.Join(",", held);
        if (lastHeld != value && SessionVariableTransport.SetClientVariable(network, sessionManager, HeldVariable, value)) lastHeld = value;
    }
    private void OnGlobalChanged(string name, string oldValue, string newValue)
    {
        if (SessionResetting) return;
        newValue = SessionVariableTransport.Decode(sessionManager, newValue);
        if (newValue == null) return;
        oldValue = SessionVariableTransport.Decode(sessionManager, oldValue);
        if (name == BoardVariable) RestoreBoard(newValue);
        if (name.StartsWith(VersionPrefix, StringComparison.Ordinal) || name.StartsWith("normalPlacement_", StringComparison.Ordinal))
        {
            // Clear this client's approval immediately, before LateUpdate can finalize an obsolete board.
            if (IsAgreementPhase && !IsBoardConfirmed && oldValue != newValue)
            {
                string proof = !string.IsNullOrEmpty(pendingReady) ? pendingReady : SessionVariableTransport.GetClientVariable(network, sessionManager, ReadyVariable);
                if (!string.IsNullOrEmpty(proof) && CurrentFingerprint() == proof) return;
                bool hadLocalApproval = !string.IsNullOrEmpty(pendingReady) ||
                    (IsLocalParticipant && !string.IsNullOrEmpty(SessionVariableTransport.GetClientVariable(network, sessionManager, ReadyVariable)));
                pendingReady = null;
                if (IsLocalParticipant && !string.IsNullOrEmpty(SessionVariableTransport.GetClientVariable(network, sessionManager, ReadyVariable))) SessionVariableTransport.SetClientVariable(network, sessionManager, ReadyVariable, "");
                if (hadLocalApproval) Debug.Log($"[Final Agreement Reset] Placement changed / {name} / {oldValue} -> {newValue}", this);
                publishRequested = false; Changed();
            }
        }
    }
    private void OnClientChanged(int client, string name, string oldValue, string newValue)
    {
        if (name == ReadyVariable || name == HeldVariable || name == ExperimentParticipantRegistry.RoleVariable) Changed();
    }
    private void RestoreBoard(string value)
    {
        if (!IsAgreementPhase || string.IsNullOrEmpty(value)) return;
        BoardRecord record;
        try { record = JsonUtility.FromJson<BoardRecord>(value); } catch { return; }
        if (record == null || string.IsNullOrEmpty(record.sessionId)) return;
        if (!IsNormalPhase && record.sessionId != placementSync.SharedAgreementId) return;
        if (!record.confirmed)
        {
            bool reset = sessionId != record.sessionId || IsBoardConfirmed;
            StatusMessage = "";
            nextPublishAttempt = 0;
            sessionId = record.sessionId; IsBoardConfirmed = false; finalPlacements.Clear(); pendingReady = null; publishRequested = false;
            if (reset)
            {
                if (network.IsReady && IsLocalParticipant) SessionVariableTransport.SetClientVariable(network, sessionManager, ReadyVariable, "");
                placementSync.SetBoardConfirmation(finalPlacements, false);
                if (IsAgreementPhase) experimentManager.SetState(IsNormalPhase ? ExperimentManager.ExperimentState.Idle : ExperimentManager.ExperimentState.Discussion);
                Changed();
            }
            return;
        }
        if (record.placements == null || record.participants == null || record.participants.Length == 0 ||
            record.readyProof != record.fingerprint) return;
        var snapshot = new Dictionary<string, string>();
        foreach (var entry in record.placements)
            if (entry == null || !IsTier(entry.tier) || string.IsNullOrEmpty(entry.revision) || placementSync.GetCard(entry.cardId) == null || !snapshot.TryAdd(entry.cardId, entry.tier)) return;
        if (snapshot.Count != placementSync.CardIds.Count) return;
        if (record.fingerprint != Fingerprint(record.sessionId, record.participants, record.placements)) return;
        var peers = new HashSet<int>();
        for (int i = 0; i < record.participants.Length; i++) if (record.participants[i] <= 0 || !peers.Add(record.participants[i])) return;
        // This atomic final snapshot is the Room result, including for late joins.
        bool first = !IsBoardConfirmed || sessionId != record.sessionId;
        sessionId = record.sessionId; IsBoardConfirmed = true; finalPlacements.Clear(); foreach (var pair in snapshot) finalPlacements.Add(pair.Key, pair.Value);
        pendingReady = null; StatusMessage = "Board confirmed";
        placementSync.SetBoardConfirmation(finalPlacements, true);
        if (IsAgreementPhase) experimentManager.SetState(ExperimentManager.ExperimentState.Confirmed);
        if (first)
        {
            foreach (var pair in finalPlacements) Debug.Log($"[Board Confirmed] {pair.Key} -> {pair.Value}");
            OnBoardConfirmed?.Invoke(GetFinalPlacements());
        }
        Changed();
    }
    public void ResetLocalSession(string id)
    {
        sessionId = id; IsBoardConfirmed = false; finalPlacements.Clear(); pendingReady = null;
        lastFingerprint = lastHeld = lastStatus = null; publishRequested = false; nextPublishAttempt = 0;
        StatusMessage = ""; networkWasReady = false;
        placementSync.SetBoardConfirmation(finalPlacements, false); Changed();
    }
    public void RestoreSessionAgreement()
    {
        Subscribe();
        if (network != null && network.IsReady) RestoreBoard(SessionVariableTransport.GetGlobalVariable(network, sessionManager, BoardVariable));
        networkWasReady = true; wasNormal = IsAgreementPhase;
    }
    // Future Reset Board UI can call this; placements are retained, approval epoch is replaced.
    public bool ResetBoardAgreement()
    {
        if (network == null || !network.IsReady || participantRegistry == null || !participantRegistry.IsLocalExperimenter || !IsAgreementPhase) return false;
        foreach (var pair in finalPlacements)
        {
            if (!SessionVariableTransport.SetGlobalVariable(network, sessionManager, VersionPrefix + pair.Key,
                JsonUtility.ToJson(new PlacementVersion { tier = pair.Value, revision = Guid.NewGuid().ToString("N") }))) return false;
            if (!SessionVariableTransport.SetGlobalVariable(network, sessionManager, "normalPlacement_" + pair.Key, pair.Value)) return false;
        }
        return SessionVariableTransport.SetGlobalVariable(network, sessionManager, BoardVariable, JsonUtility.ToJson(new BoardRecord { confirmed = false, sessionId = IsNormalPhase ? Guid.NewGuid().ToString("N") : placementSync.SharedAgreementId }));
    }
    private void Changed() => OnStatusChanged?.Invoke();
}
