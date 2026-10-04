using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using Styly.NetSync;
using UnityEngine;

// One session-scoped preparation/commit shared by the entire answering cohort.
// The original candidate variables remain the only editable answer data.
[DefaultExecutionOrder(200)]
public class ProposedRevealCoordinator : MonoBehaviour
{
    public const string PrepareVariable = "proposedRevealPrepare";
    public const string RevealVariable = "proposedReveal";
    public const string AckVariable = "proposedRevealAck";
    [SerializeField] private ExperimentManager experimentManager;
    [SerializeField] private ExperimentParticipantRegistry participantRegistry;
    [SerializeField] private ExperimentSessionManager sessionManager;
    [SerializeField] private CandidateSyncTest candidateSync;
    [SerializeField, Min(3)] private float preparationTimeout = 15;
    [Serializable] public class Answer { public string cardId, tier, selectedAt; public bool answered; }
    [Serializable] public class Participant { public int clientNo, displayId; public Answer[] answers; }
    [Serializable] public class Snapshot { public string id, sessionId; public long epoch; public int owner; public string[] cardIds; public Participant[] participants; }
    private Snapshot prepared, revealed;
    private string observedPrepare, expiredPrepare, sentPrepare, sentCommit;
    private float preparationStarted, nextAttempt;
    public bool HasRevealed => revealed != null;
    public bool IsAnswerLocked => HasRevealed || prepared != null;
    // Return a copy so presentation cannot mutate the committed data.
    public Snapshot GetRevealedSnapshot() => revealed == null ? null : JsonUtility.FromJson<Snapshot>(JsonUtility.ToJson(revealed));
    public event Action OnRevealChanged;
    private NetSyncManager Network => participantRegistry != null ? participantRegistry.Network : null;
    private bool Ready => isActiveAndEnabled && experimentManager != null && candidateSync != null &&
        candidateSync.IsDataReady && sessionManager != null && !sessionManager.IsResettingSession &&
        experimentManager.CurrentMode == ExperimentManager.ExperimentMode.Proposed;

    private void OnEnable()
    {
        if (sessionManager != null) sessionManager.OnSessionResetStarted += OnReset;
    }
    private void OnDisable()
    {
        if (sessionManager != null) sessionManager.OnSessionResetStarted -= OnReset;
        ClearLocal();
    }
    private void OnReset(string session) => ClearLocal();
    private void ClearLocal()
    {
        prepared = revealed = null; observedPrepare = expiredPrepare = sentPrepare = sentCommit = null;
        nextAttempt = 0; OnRevealChanged?.Invoke();
    }
    private void LateUpdate()
    {
        if (!Ready)
        {
            // Keep a committed snapshot during temporary disconnection; Session
            // reset and a mode change clear it. An uncommitted lock never survives.
            prepared = null;
            observedPrepare = expiredPrepare = sentPrepare = sentCommit = null;
            if (experimentManager != null && experimentManager.CurrentMode != ExperimentManager.ExperimentMode.Proposed && HasRevealed) ClearLocal();
            return;
        }
        if (revealed != null && revealed.sessionId != sessionManager.CurrentSessionId) ClearLocal();
        var commit = Read(RevealVariable);
        if (commit != null && !HasRevealed)
        {
            revealed = commit; prepared = null;
            experimentManager.SetState(ExperimentManager.ExperimentState.Revealed);
            Debug.Log($"[Proposed Reveal] Session {commit.sessionId} / {commit.participants.Length} Participants x {commit.cardIds.Length} Cards / {commit.id}", this);
            OnRevealChanged?.Invoke();
        }
        if (HasRevealed)
        {
            if (CandidateSyncTest.IsIndependentState(experimentManager.CurrentState))
                experimentManager.SetState(ExperimentManager.ExperimentState.Revealed);
            return;
        }
        // A locally forced Revealed state alone must not disclose any answer.
        if (!candidateSync.IsIndependentAnswerPhase) return;
        participantRegistry.RefreshParticipants();
        var proposal = Read(PrepareVariable);
        string id = proposal != null ? proposal.id : "";
        if (observedPrepare != id)
        {
            observedPrepare = id; preparationStarted = Time.unscaledTime;
            prepared = null; expiredPrepare = null;
        }
        bool matches = proposal != null && MatchesCurrentAnswers(proposal);
        // Only the elected owner expires the barrier. Other endpoints keep
        // their ACK/lock until the owner clears the proposal, preventing a late
        // ACK from authorizing a commit after an endpoint unlocked on its clock.
        if (proposal != null && proposal.owner == Network.ClientNo && Time.unscaledTime - preparationStarted >= preparationTimeout)
        {
            if (expiredPrepare != id) Debug.LogWarning("[Proposed Reveal Aborted] Preparation timed out; answers unlocked", this);
            expiredPrepare = id;
        }
        bool accept = matches && expiredPrepare != id;
        prepared = accept ? proposal : null;
        if (participantRegistry.IsRegisteredParticipant(Network.ClientNo))
        {
            string ack = accept ? id : "";
            if (SessionVariableTransport.GetClientVariable(Network, sessionManager, AckVariable) != ack)
                SessionVariableTransport.SetClientVariable(Network, sessionManager, AckVariable, ack);
        }
        var cohort = participantRegistry.ParticipantClientNos;
        // Expected count is configurable, never P1/P2 hard-coded. It prevents a
        // missing/disconnected required Participant from causing early Reveal.
        if (cohort.Count != participantRegistry.ExpectedParticipantCount || cohort.Count == 0 || cohort[0] != Network.ClientNo) return;
        if (prepared != null)
        {
            foreach (var p in prepared.participants)
                if (SessionVariableTransport.GetClientVariable(Network, sessionManager, AckVariable, p.clientNo) != prepared.id) return;
            if (sentCommit != prepared.id && Send(RevealVariable, prepared))
            {
                sentCommit = prepared.id;
                Debug.Log("[Proposed Reveal Commit] All Participants acknowledged the same complete snapshot", this);
            }
            return;
        }
        if (proposal != null)
        {
            // Changes/cancellations/cohort changes invalidate the whole proposal.
            SessionVariableTransport.SetGlobalVariable(Network, sessionManager, PrepareVariable, "");
            sentPrepare = null;
            return;
        }
        if (Time.unscaledTime < nextAttempt) return;
        nextAttempt = Time.unscaledTime + 1;
        if (sentPrepare != null && Time.unscaledTime - preparationStarted < preparationTimeout) return;
        var snapshot = BuildCompleteSnapshot();
        if (snapshot == null) return;
        if (Send(PrepareVariable, snapshot))
        {
            sentPrepare = snapshot.id; preparationStarted = Time.unscaledTime;
            Debug.Log($"[Proposed Reveal Ready] {snapshot.participants.Length} Participants x {snapshot.cardIds.Length} Cards; awaiting acknowledgements", this);
        }
    }
    public bool AreAllAnswersComplete() => Ready && BuildCompleteSnapshot() != null;
    private Snapshot BuildCompleteSnapshot()
    {
        var clients = participantRegistry.ParticipantClientNos;
        var cards = candidateSync.AnswerCardIds;
        if (clients.Count == 0 || clients.Count != participantRegistry.ExpectedParticipantCount || cards.Count == 0) return null;
        var result = new Snapshot { id = Guid.NewGuid().ToString("N"), sessionId = sessionManager.CurrentSessionId,
            epoch = sessionManager.SessionEpoch, owner = clients[0], cardIds = new string[cards.Count], participants = new Participant[clients.Count] };
        for (int c = 0; c < cards.Count; c++) result.cardIds[c] = cards[c];
        for (int p = 0; p < clients.Count; p++)
        {
            var answers = candidateSync.GetParticipantCandidates(clients[p]);
            var participant = new Participant { clientNo = clients[p], displayId = p + 1, answers = new Answer[cards.Count] };
            for (int c = 0; c < cards.Count; c++)
            {
                if (!answers.TryGetValue(cards[c], out var a) || !a.Answered ||
                    (clients[p] == Network.ClientNo && !candidateSync.MatchesLocalSubmission(cards[c], a))) return null;
                participant.answers[c] = new Answer { cardId = cards[c], tier = a.Tier, answered = true, selectedAt = a.SelectedAt };
            }
            result.participants[p] = participant;
        }
        return result;
    }
    private bool MatchesCurrentAnswers(Snapshot snapshot)
    {
        var clients = participantRegistry.ParticipantClientNos;
        if (clients.Count != participantRegistry.ExpectedParticipantCount || clients.Count != snapshot.participants.Length) return false;
        for (int p = 0; p < clients.Count; p++)
        {
            if (clients[p] != snapshot.participants[p].clientNo) return false;
            foreach (var a in snapshot.participants[p].answers)
                if (!candidateSync.TryGetParticipantAnswer(clients[p], a.cardId, out var current) || !current.Answered ||
                    current.Tier != a.tier || current.SelectedAt != a.selectedAt ||
                    (clients[p] == Network.ClientNo && !candidateSync.MatchesLocalSubmission(a.cardId, current))) return false;
        }
        return true;
    }
    private Snapshot Read(string key)
    {
        string raw = SessionVariableTransport.GetGlobalVariable(Network, sessionManager, key);
        if (string.IsNullOrEmpty(raw)) return null;
        Snapshot s;
        try { s = JsonUtility.FromJson<Snapshot>(Unpack(raw)); } catch { return null; }
        if (s == null || string.IsNullOrEmpty(s.id) || s.sessionId != sessionManager.CurrentSessionId || s.epoch != sessionManager.SessionEpoch ||
            s.cardIds == null || s.participants == null || s.participants.Length == 0 || s.cardIds.Length != candidateSync.AnswerCardIds.Count) return null;
        var cards = new HashSet<string>(s.cardIds);
        if (cards.Count != s.cardIds.Length) return null;
        foreach (string c in candidateSync.AnswerCardIds) if (!cards.Contains(c)) return null;
        int previous = 0;
        for (int p = 0; p < s.participants.Length; p++)
        {
            var participant = s.participants[p];
            if (participant == null || participant.clientNo <= previous || participant.displayId != p + 1 ||
                participant.answers == null || participant.answers.Length != cards.Count) return null;
            previous = participant.clientNo;
            var seen = new HashSet<string>();
            foreach (var a in participant.answers)
                if (a == null || !a.answered || !cards.Contains(a.cardId) || !seen.Add(a.cardId) || string.IsNullOrEmpty(a.selectedAt) ||
                    (a.tier != "A" && a.tier != "B" && a.tier != "C" && a.tier != "D")) return null;
        }
        return s.owner == s.participants[0].clientNo ? s : null;
    }
    private bool Send(string key, Snapshot snapshot)
    {
        string wire = Pack(JsonUtility.ToJson(snapshot));
        // Verified in the installed package: each value has a 1024-character
        // limit, including SessionVariableTransport's wrapper.
        if (SessionVariableTransport.Encode(sessionManager, wire).Length > 1024)
        {
            Debug.LogWarning("[Proposed Reveal Rejected] Snapshot exceeds NetSync's 1024-character variable limit", this);
            return false;
        }
        return SessionVariableTransport.SetGlobalVariable(Network, sessionManager, key, wire);
    }
    public static string Pack(string json)
    {
        using (var stream = new MemoryStream())
        {
            using (var zip = new DeflateStream(stream, System.IO.Compression.CompressionLevel.Optimal, true))
            {
                byte[] bytes = Encoding.UTF8.GetBytes(json); zip.Write(bytes, 0, bytes.Length);
            }
            return "z1:" + Convert.ToBase64String(stream.ToArray());
        }
    }
    private static string Unpack(string wire)
    {
        if (!wire.StartsWith("z1:", StringComparison.Ordinal)) throw new InvalidDataException();
        using (var stream = new MemoryStream(Convert.FromBase64String(wire.Substring(3))))
        using (var zip = new DeflateStream(stream, CompressionMode.Decompress))
        using (var output = new MemoryStream())
        {
            var buffer = new byte[2048]; int count;
            while ((count = zip.Read(buffer, 0, buffer.Length)) > 0)
            {
                if (output.Length + count > 65536) throw new InvalidDataException();
                output.Write(buffer, 0, count);
            }
            return Encoding.UTF8.GetString(output.ToArray());
        }
    }
}
