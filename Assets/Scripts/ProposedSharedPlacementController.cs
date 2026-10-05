using System;
using Styly.NetSync;
using UnityEngine;

// Opens the existing Main placement/Agreement flow once per committed Reveal.
// The Ready marker prevents editing while its Unclassified defaults are in flight.
[DefaultExecutionOrder(50)]
public class ProposedSharedPlacementController : MonoBehaviour
{
    public const string ReadyVariable = "proposedSharedPlacementReady";
    [SerializeField] private ExperimentManager experimentManager;
    [SerializeField] private ExperimentParticipantRegistry participantRegistry;
    [SerializeField] private ExperimentSessionManager sessionManager;
    [SerializeField] private ProposedRevealCoordinator revealCoordinator;
    [SerializeField] private NormalPlacementSync placementSync;
    [SerializeField] private FinalAgreementManager finalAgreement;
    private string observedRevealId;
    private float nextInitializationAttempt;
    private bool active;
    public bool IsActive => active && ContextReady;
    public string AgreementId => string.IsNullOrEmpty(observedRevealId) ? null : "proposed:" + observedRevealId;
    private NetSyncManager Network => participantRegistry != null ? participantRegistry.Network : null;
    private bool ContextReady => isActiveAndEnabled && experimentManager != null &&
        experimentManager.CanRunExperiment && experimentManager.CurrentMode == ExperimentManager.ExperimentMode.Proposed && revealCoordinator != null &&
        revealCoordinator.HasRevealed && Network != null && Network.IsReady &&
        sessionManager != null && !sessionManager.IsResettingSession;
    [Serializable] private class PlacementVersion { public string tier; public string revision; }
    [Serializable] private class BoardReset { public bool confirmed; public string sessionId; }
    private void OnEnable()
    {
        if (sessionManager != null) sessionManager.OnSessionResetStarted += OnReset;
    }
    private void OnDisable()
    {
        if (sessionManager != null) sessionManager.OnSessionResetStarted -= OnReset;
        ClearLocal();
    }
    private void OnReset(string id) => ClearLocal();
    private void ClearLocal()
    {
        active = false; observedRevealId = null; nextInitializationAttempt = 0;
    }
    private void Update()
    {
        if (!ContextReady)
        {
            active = false;
            if (experimentManager != null && experimentManager.CurrentMode != ExperimentManager.ExperimentMode.Proposed) ClearLocal();
            return;
        }
        string revealId = revealCoordinator.RevealId;
        if (observedRevealId != revealId)
        {
            // Local reset never copies an independent answer to Main and never
            // writes Room defaults. A reconnect later restores shared placements.
            active = false; observedRevealId = revealId;
            finalAgreement.ResetLocalSession(AgreementId);
            placementSync.ResetLocalSession();
            if (participantRegistry.IsLocalParticipant)
                SessionVariableTransport.SetClientVariable(Network, sessionManager, FinalAgreementManager.ReadyVariable, "");
            nextInitializationAttempt = 0;
        }
        if (SessionVariableTransport.GetGlobalVariable(Network, sessionManager, ReadyVariable) == revealId)
        {
            if (!active)
            {
                active = true;
                placementSync.RestoreSessionPlacements();
                finalAgreement.RestoreSessionAgreement();
                if (!finalAgreement.IsBoardConfirmed) experimentManager.SetState(ExperimentManager.ExperimentState.Discussion);
                Debug.Log($"[Proposed Shared Board] Enabled Main placement / Reveal {revealId}", this);
            }
            return;
        }
        // A retained marker is the only permission to restore an already-started
        // board. Do not reset a live board on reconnect or overwrite its edits.
        active = false;
        participantRegistry.RefreshParticipants();
        var peers = participantRegistry.ParticipantClientNos;
        if (peers.Count == 0 || peers[0] != Network.ClientNo || !participantRegistry.IsLocalParticipant) return;
        if (InitialDefaultsObserved(revealId))
        {
            SessionVariableTransport.SetGlobalVariable(Network, sessionManager, ReadyVariable, revealId);
            return;
        }
        if (Time.unscaledTime < nextInitializationAttempt) return;
        nextInitializationAttempt = Time.unscaledTime + 1;
        bool accepted = SessionVariableTransport.SetGlobalVariable(Network, sessionManager, FinalAgreementManager.BoardVariable,
            JsonUtility.ToJson(new BoardReset { confirmed = false, sessionId = AgreementId }));
        foreach (string id in placementSync.CardIds)
        {
            accepted &= SessionVariableTransport.SetGlobalVariable(Network, sessionManager, "normalPlacementVersion_" + id,
                JsonUtility.ToJson(new PlacementVersion { tier = "Unclassified", revision = InitialRevision(revealId) }));
            accepted &= SessionVariableTransport.SetGlobalVariable(Network, sessionManager, "normalPlacement_" + id, "Unclassified");
        }
        if (accepted) Debug.Log("[Proposed Shared Board Init] All Main cards -> Unclassified; waiting for Room echoes", this);
        else Debug.LogWarning("[Proposed Shared Board Init] Send failed; Main remains locked", this);
    }
    private static string InitialRevision(string revealId) => "proposed:" + revealId;
    private bool InitialDefaultsObserved(string revealId)
    {
        string board = SessionVariableTransport.GetGlobalVariable(Network, sessionManager, FinalAgreementManager.BoardVariable);
        BoardReset reset;
        try { reset = JsonUtility.FromJson<BoardReset>(board); } catch { return false; }
        if (reset == null || reset.confirmed || reset.sessionId != AgreementId) return false;
        foreach (string id in placementSync.CardIds)
        {
            if (SessionVariableTransport.GetGlobalVariable(Network, sessionManager, "normalPlacement_" + id) != "Unclassified") return false;
            PlacementVersion version;
            try { version = JsonUtility.FromJson<PlacementVersion>(SessionVariableTransport.GetGlobalVariable(Network, sessionManager, "normalPlacementVersion_" + id)); }
            catch { return false; }
            if (version == null || version.tier != "Unclassified" || version.revision != InitialRevision(revealId)) return false;
        }
        return placementSync.CardIds.Count > 0;
    }
}
