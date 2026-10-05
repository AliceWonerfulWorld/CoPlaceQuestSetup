using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

// Consent only. CandidateSyncTest remains the sole answer-data transport.
public class IndependentAnswerSubmission : MonoBehaviour
{
    public const string SubmittedVariable = "independentAnswerSubmitted";
    public const string ProofVariable = "independentAnswerSubmissionProof";
    [SerializeField] private CandidateSyncTest candidateSync;
    [SerializeField] private ExperimentParticipantRegistry participantRegistry;
    [SerializeField] private ExperimentSessionManager sessionManager;
    [SerializeField] private ProposedRevealCoordinator revealCoordinator;
    [SerializeField] private PrivateTierBoardController privateBoard;
    [Serializable] private class Proof { public string id, hash; }
    private Proof pending;
    private bool cancelling;
    private float nextRetry;
    private Styly.NetSync.NetSyncManager Network => participantRegistry != null ? participantRegistry.Network : null;
    private bool Ready => candidateSync != null && candidateSync.IsDataReady && sessionManager != null &&
        !sessionManager.IsResettingSession && Network != null && Network.IsReady;
    public bool IsLocalLocked => pending != null || cancelling || (Ready &&
        SessionVariableTransport.GetClientVariable(Network, sessionManager, SubmittedVariable) == "true");
    public bool IsCancellationPending => cancelling;
    public bool IsLocalSubmitted => pending != null || (Ready &&
        SessionVariableTransport.GetClientVariable(Network, sessionManager, SubmittedVariable) == "true");
    public bool CanSubmit => Ready && candidateSync.IsIndependentAnswerPhase && participantRegistry.IsLocalParticipant &&
        participantRegistry.IsRegisteredParticipant(Network.ClientNo) && !IsLocalLocked &&
        (revealCoordinator == null || !revealCoordinator.IsAnswerLocked) && privateBoard != null && !privateBoard.HasHeldCard &&
        AnswerHash(Network.ClientNo) != null;
    public bool CanCancel => Ready && candidateSync.IsIndependentAnswerPhase && participantRegistry.IsLocalParticipant &&
        participantRegistry.IsRegisteredParticipant(Network.ClientNo) && !cancelling &&
        (revealCoordinator == null || !revealCoordinator.HasRevealed) && IsLocalLocked;
    private void OnEnable() { if (sessionManager != null) sessionManager.OnSessionResetStarted += OnReset; }
    private void OnDisable() { if (sessionManager != null) sessionManager.OnSessionResetStarted -= OnReset; ClearLocal(); }
    private void OnReset(string id) => ClearLocal();
    private void ClearLocal() { pending = null; cancelling = false; nextRetry = 0; }
    private string AnswerHash(int client)
    {
        if (!Ready || candidateSync.AnswerCardIds.Count == 0) return null;
        var data = new StringBuilder();
        foreach (string card in candidateSync.AnswerCardIds)
        {
            if (!candidateSync.TryGetParticipantAnswer(client, card, out var a) || !a.Answered ||
                (a.Tier != "A" && a.Tier != "B" && a.Tier != "C" && a.Tier != "D") ||
                !DateTime.TryParseExact(a.SelectedAt, "o", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var time) ||
                time.Kind != DateTimeKind.Utc || (client == Network.ClientNo && !candidateSync.MatchesLocalSubmission(card, a))) return null;
            data.Append(card).Append('|').Append(a.Tier).Append('|').Append(a.SelectedAt).Append('\n');
        }
        using (var sha = SHA256.Create()) return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(data.ToString())));
    }
    // A new consent ID on every Submit prevents a cancelled preparation from
    // being revived by an old ACK when the same answers are submitted again.
    public string GetSubmissionId(int client)
    {
        if (!Ready || (client == Network.ClientNo && cancelling) ||
            SessionVariableTransport.GetClientVariable(Network, sessionManager, SubmittedVariable, client) != "true") return null;
        Proof proof;
        try { proof = JsonUtility.FromJson<Proof>(SessionVariableTransport.GetClientVariable(Network, sessionManager, ProofVariable, client)); }
        catch { return null; }
        if (proof == null || string.IsNullOrEmpty(proof.id) || proof.hash != AnswerHash(client)) return null;
        if (client == Network.ClientNo && pending != null && pending.id != proof.id) return null;
        return proof.id;
    }
    public bool TrySubmit()
    {
        if (!CanSubmit) return false;
        pending = new Proof { id = Guid.NewGuid().ToString("N"), hash = AnswerHash(Network.ClientNo) };
        nextRetry = 0; Flush();
        Debug.Log("[Independent Answer Submitted] Local answers locked; waiting for Participants", this);
        return true;
    }
    public bool TryCancel()
    {
        if (!CanCancel) return false;
        pending = null; cancelling = true; nextRetry = 0;
        // Revoke our ACK immediately as well as our independent consent.
        SessionVariableTransport.SetClientVariable(Network, sessionManager, ProposedRevealCoordinator.AckVariable, "");
        Flush(); Debug.Log("[Independent Answer Submit Cancelled] Waiting for cancellation echo", this);
        return true;
    }
    private void Update()
    {
        if (!Ready) return;
        if (revealCoordinator != null && revealCoordinator.HasRevealed) { ClearLocal(); return; }
        if (cancelling && SessionVariableTransport.GetClientVariable(Network, sessionManager, SubmittedVariable) == "false" &&
            string.IsNullOrEmpty(SessionVariableTransport.GetClientVariable(Network, sessionManager, ProofVariable))) cancelling = false;
        if (pending != null && GetSubmissionId(Network.ClientNo) == pending.id) pending = null;
        if (pending != null || cancelling) Flush();
    }
    private void Flush()
    {
        if (!Ready || Time.unscaledTime < nextRetry) return;
        nextRetry = Time.unscaledTime + 1;
        if (cancelling)
        {
            SessionVariableTransport.SetClientVariable(Network, sessionManager, SubmittedVariable, "false");
            SessionVariableTransport.SetClientVariable(Network, sessionManager, ProofVariable, "");
        }
        else if (pending != null)
        {
            if (SessionVariableTransport.SetClientVariable(Network, sessionManager, ProofVariable, JsonUtility.ToJson(pending)))
                SessionVariableTransport.SetClientVariable(Network, sessionManager, SubmittedVariable, "true");
        }
    }
}
