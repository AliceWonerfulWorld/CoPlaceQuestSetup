using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;
using Debug = UnityEngine.Debug;

// PC observations only. ClientNo identifies a connection, never a research participant.
[DefaultExecutionOrder(-2000)]
public sealed class ExperimentLogger : MonoBehaviour
{
    [SerializeField] private ExperimentSessionManager sessionManager;
    [SerializeField] private ExperimentParticipantRegistry participantRegistry;
    [SerializeField] private ExperimentLobbyController lobby;
    [SerializeField] private FinalAgreementManager finalAgreement;
    private ExperimentCsvLogStore store;
    private readonly Queue<ExperimentCsvLogStore.Observation> pending = new Queue<ExperimentCsvLogStore.Observation>();
    private readonly Dictionary<int, string> observedSubmissions = new Dictionary<int, string>();
    private string outputRootOverride = "";
    private ProposedRevealCoordinator reveal;
    private ProposedSharedPlacementController shared;
    private IndependentAnswerSubmission submission;
    private Styly.NetSync.NetSyncManager network;
    private bool subscribed, wasWriter, baselineNeeded;
    private string liveSession;
    private ExperimentCsvLogStore.Observation pendingAbort;
    private float nextRetry;
    private string preparedSession;
    private readonly HashSet<string> savedFinal = new HashSet<string>(), savedIndependent = new HashSet<string>();
    public bool IsOperational => isActiveAndEnabled && subscribed && CanWrite;
    public bool IsSaving { get; private set; }
    public int UnsavedCount => pending.Count;
    public bool HasMonotonicTiming => IsOperational && store != null && sessionManager != null && store.HasMonotonicOrigin(sessionManager.CurrentSessionId);
    public bool IsPreparedInCurrentRun => IsOperational && sessionManager != null && preparedSession == sessionManager.CurrentSessionId && HasMonotonicTiming;
    public string SaveStatus => !IsOperational ? "Loggerが無効です" : LastError != "" ? "保存失敗（詳細を確認してください）" : IsSaving ? "保存中" : pending.Count > 0 ? "保存待ち" : store == null ? "まだ保存対象ではありません" : "保存成功";
    public string FinalSaveStatus => finalAgreement == null || !finalAgreement.IsBoardConfirmed ? "まだ保存対象ではありません" : savedFinal.Contains(sessionManager.CurrentSessionId) ? "保存済み" : "未保存";
    public string IndependentSaveStatus => reveal == null || !reveal.HasRevealed || sessionManager.CurrentMode != ExperimentManager.ExperimentMode.Proposed ? "まだ保存対象ではありません" : savedIndependent.Contains(sessionManager.CurrentSessionId) ? "保存済み" : "未保存";
    internal void MarkPreparationRequested(string id) { if (IsOperational) preparedSession = id; }
    public bool RetrySave() { if (!IsOperational) return false; try { CaptureSnapshot(); Drain(); } catch (Exception error) { LastError = error.Message; } return pending.Count == 0 && LastError == ""; }
    public string LastError { get; private set; } = "";
    public string OutputDirectory => store != null ? store.RootDirectory :
        (string.IsNullOrEmpty(outputRootOverride) ? Path.Combine(Application.persistentDataPath, "ExperimentLogs") : outputRootOverride);
    private bool CanWrite => participantRegistry != null && participantRegistry.IsLocalExperimenter && !Application.isMobilePlatform;
    private string Kind => liveSession == sessionManager.CurrentSessionId ? "notification" : "retained_snapshot";
    private T InScene<T>() where T : Component => gameObject.scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<T>(true)).SingleOrDefault();
    private void OnEnable()
    {
        if (subscribed || sessionManager == null || lobby == null || finalAgreement == null) return;
        reveal = InScene<ProposedRevealCoordinator>(); shared = InScene<ProposedSharedPlacementController>(); submission = InScene<IndependentAnswerSubmission>();
        sessionManager.OnSessionResetStarted += OnResetStarted;
        sessionManager.OnSessionStarted += OnResetCompleted;
        lobby.OnExperimentStarted += OnExperimentStarted;
        finalAgreement.OnBoardConfirmed += OnBoardConfirmed;
        if (reveal != null) reveal.OnRevealChanged += OnRevealed;
        if (shared != null) shared.OnSharedClassificationStarted += OnSharedStarted;
        subscribed = true; SubscribeNetwork(); CaptureSnapshot();
    }
    private void SubscribeNetwork()
    {
        if (network != null || participantRegistry == null) return;
        network = participantRegistry.Network;
        if (network == null) return;
        network.OnReady.AddListener(OnNetworkReady);
        network.OnClientVariableChanged.AddListener(OnClientChanged);
        if (network.IsReady) OnNetworkReady();
    }
    private void OnNetworkReady() { liveSession = null; baselineNeeded = true; }
    private void Start() { wasWriter = CanWrite; SubscribeNetwork(); CaptureSnapshot(); }
    private void Update()
    {
        SubscribeNetwork();
        if (network == null || !network.IsReady) { liveSession = null; baselineNeeded = true; }
        if (CanWrite && !wasWriter) { liveSession = null; baselineNeeded = true; }
        wasWriter = CanWrite;
        if (baselineNeeded && CanWrite && network != null && network.IsReady && !sessionManager.IsResettingSession &&
            network.GetGlobalVariable(ExperimentSessionManager.IdVariable) == sessionManager.CurrentSessionId)
        {
            baselineNeeded = false;
            // A prepared Session with no retained Start can still start live after PC reconnect.
            if (string.IsNullOrEmpty(SessionVariableTransport.GetGlobalVariable(network, sessionManager, ExperimentLobbyController.StartVariable))) liveSession = sessionManager.CurrentSessionId;
            CaptureSnapshot(); ObserveAllSubmissions("retained_snapshot");
        }
        if (pending.Count > 0 && Time.unscaledTime >= nextRetry) Drain();
    }
    private void OnDisable()
    {
        if (subscribed)
        {
            sessionManager.OnSessionResetStarted -= OnResetStarted; sessionManager.OnSessionStarted -= OnResetCompleted;
            lobby.OnExperimentStarted -= OnExperimentStarted; finalAgreement.OnBoardConfirmed -= OnBoardConfirmed;
            if (reveal != null) reveal.OnRevealChanged -= OnRevealed;
            if (shared != null) shared.OnSharedClassificationStarted -= OnSharedStarted;
        }
        if (network != null) { network.OnReady.RemoveListener(OnNetworkReady); network.OnClientVariableChanged.RemoveListener(OnClientChanged); }
        preparedSession = null;
        subscribed = false; network = null; liveSession = null; Drain();
    }
    private void OnApplicationQuit() => Drain();
    private void EnsureSession(string id, string kind) => RecordEvent("SESSION_CREATED", id + ":SESSION_CREATED", observationKind: kind);
    private void OnResetStarted(string id)
    {
        observedSubmissions.Clear();
        if (!CanWrite) return;
        liveSession = sessionManager.HasResetControl ? id : null;
        EnsureSession(id, sessionManager.HasResetControl ? "received_reset_control" : "retained_snapshot");
        if (sessionManager.HasResetControl) RecordEvent("SESSION_RESET_STARTED", id + ":SESSION_RESET_STARTED");
    }
    private void OnResetCompleted(string id)
    {
        if (!CanWrite) return;
        EnsureSession(id, "retained_snapshot"); RecordEvent("SESSION_RESET_COMPLETED", id + ":SESSION_RESET_COMPLETED", observationKind: Kind);
    }
    private void OnExperimentStarted(string id, string startId, ExperimentManager.ExperimentMode mode)
    {
        if (!CanWrite) return;
        EnsureSession(id, "retained_snapshot"); RecordStart(id, startId, mode, Kind);
    }
    private void RecordStart(string id, string startId, ExperimentManager.ExperimentMode mode, string kind)
        => RecordEvent("EXPERIMENT_STARTED", id + ":EXPERIMENT_STARTED:" + startId, condition: mode.ToString(), observationKind: kind,
            participantsAtStart: string.Join(";", participantRegistry.ParticipantClientNos.Select(n => n.ToString(CultureInfo.InvariantCulture))));
    private void OnBoardConfirmed(IReadOnlyDictionary<string, string> placements) { if (CanWrite && lobby.IsExperimentStarted) CaptureFinal(Kind); }
    private void OnRevealed() { if (CanWrite && lobby.IsExperimentStarted && reveal.HasRevealed) CaptureReveal(Kind); }
    private void OnSharedStarted(string id)
    {
        if (CanWrite && lobby.IsExperimentStarted && shared.IsActive)
            RecordEvent("SHARED_CLASSIFICATION_STARTED", sessionManager.CurrentSessionId + ":SHARED_CLASSIFICATION_STARTED:" + id, observationKind: Kind);
    }
    private void OnClientChanged(int client, string name, string oldValue, string value)
    {
        if (!CanWrite || network == null || !network.IsReady || sessionManager.IsResettingSession || !lobby.IsExperimentStarted ||
            sessionManager.CurrentMode != ExperimentManager.ExperimentMode.Proposed || !participantRegistry.IsRegisteredParticipant(client)) return;
        if (name == IndependentAnswerSubmission.SubmittedVariable || name == IndependentAnswerSubmission.ProofVariable ||
            name.StartsWith("candidate_", StringComparison.Ordinal) || name.StartsWith("selectedAt_", StringComparison.Ordinal) || name.StartsWith("answered_", StringComparison.Ordinal))
            ObserveSubmission(client, Kind);
    }
    private void ObserveAllSubmissions(string kind)
    {
        if (submission == null || !lobby.IsExperimentStarted || sessionManager.CurrentMode != ExperimentManager.ExperimentMode.Proposed) return;
        foreach (int client in participantRegistry.ParticipantClientNos) ObserveSubmission(client, kind);
    }
    private void ObserveSubmission(int client, string kind)
    {
        if (submission == null) return;
        string id = submission.GetSubmissionId(client);
        if (id != null)
        {
            if (observedSubmissions.TryGetValue(client, out var known) && known == id) return;
            observedSubmissions[client] = id;
            RecordEvent("PARTICIPANT_SUBMITTED", sessionManager.CurrentSessionId + ":SUBMITTED:" + client + ":" + id, "client:" + client, observationKind: kind);
        }
        else if (SessionVariableTransport.GetClientVariable(network, sessionManager, IndependentAnswerSubmission.SubmittedVariable, client) == "false" &&
            observedSubmissions.TryGetValue(client, out var previous))
        {
            RecordEvent("PARTICIPANT_SUBMISSION_CANCELLED", sessionManager.CurrentSessionId + ":CANCELLED:" + client + ":" + previous, "client:" + client, observationKind: kind);
            observedSubmissions.Remove(client);
        }
    }
    private void CaptureFinal(string kind)
    {
        if (!finalAgreement.IsBoardConfirmed) return;
        var placements = finalAgreement.GetFinalPlacements();
        if (placements.Count == 0 || !new HashSet<string>(placements.Keys).SetEquals(finalAgreement.ExpectedCardIds)) throw new InvalidDataException("Incomplete confirmed final snapshot");
        var entry = CreateObservation("BOARD_CONFIRMED", sessionManager.CurrentSessionId + ":BOARD_CONFIRMED", "", kind);
        entry.Condition = lobby.StartedMode.Value.ToString();
        entry.FinalPlacements = placements.Select(p => new ExperimentCsvLogStore.FinalPlacement { CardId = p.Key, Tier = p.Value }).ToArray();
        pending.Enqueue(entry); Drain();
    }
    private void CaptureReveal(string kind)
    {
        if (reveal == null || !reveal.HasRevealed || sessionManager.CurrentMode != ExperimentManager.ExperimentMode.Proposed) return;
        var snapshot = reveal.GetRevealedSnapshot();
        if (snapshot.sessionId != sessionManager.CurrentSessionId || snapshot.epoch != sessionManager.SessionEpoch || snapshot.cardIds == null ||
            snapshot.cardIds.Length == 0 || !new HashSet<string>(snapshot.cardIds).SetEquals(finalAgreement.ExpectedCardIds) || snapshot.cardIds.Distinct().Count() != snapshot.cardIds.Length || snapshot.participants == null || snapshot.participants.Length == 0 ||
            snapshot.participants.Select(p => p.clientNo).Distinct().Count() != snapshot.participants.Length ||
            snapshot.participants.Any(p => p.answers == null || p.answers.Length != snapshot.cardIds.Length || !new HashSet<string>(p.answers.Select(a => a.cardId)).SetEquals(snapshot.cardIds) || p.answers.Any(a => !a.answered)))
            throw new InvalidDataException("Incomplete or wrong-Session Reveal snapshot");
        var entry = CreateObservation("ANSWERS_REVEALED", snapshot.sessionId + ":ANSWERS_REVEALED:" + snapshot.id, "", kind);
        entry.Condition = "Proposed"; entry.SnapshotId = snapshot.id;
        entry.IndependentAnswers = snapshot.participants.SelectMany(p => p.answers.Select(a => new ExperimentCsvLogStore.IndependentAnswer {
            ClientId = p.clientNo, CardId = a.cardId, Tier = a.tier, SelectedAt = a.selectedAt, SubmissionId = p.submissionId })).ToArray();
        pending.Enqueue(entry); Drain();
    }
    private void CaptureSnapshot()
    {
        if (!CanWrite || sessionManager == null || string.IsNullOrEmpty(sessionManager.CurrentSessionId)) return;
        string id = sessionManager.CurrentSessionId;
        EnsureSession(id, "retained_snapshot");
        if (sessionManager.HasResetControl) RecordEvent("SESSION_RESET_STARTED", id + ":SESSION_RESET_STARTED", observationKind: "retained_snapshot");
        else if (!sessionManager.IsResettingSession && !string.IsNullOrEmpty(sessionManager.SessionStartedAtUtc)) RecordEvent("SESSION_RESET_COMPLETED", id + ":SESSION_RESET_COMPLETED", observationKind: "retained_snapshot");
        if (lobby.IsExperimentStarted && lobby.StartedMode.HasValue)
        {
            RecordStart(id, lobby.StartedEventId, lobby.StartedMode.Value, "retained_snapshot");
            CaptureReveal("retained_snapshot");
            if (shared != null && shared.IsActive) RecordEvent("SHARED_CLASSIFICATION_STARTED", id + ":SHARED_CLASSIFICATION_STARTED:" + reveal.RevealId, observationKind: "retained_snapshot");
            CaptureFinal("retained_snapshot");
        }
    }
    private ExperimentCsvLogStore.Observation CreateObservation(string type, string id, string participant, string kind) => new ExperimentCsvLogStore.Observation {
        SessionId = sessionManager.CurrentSessionId, Epoch = sessionManager.SessionEpoch, EventType = type, EventId = id, ParticipantId = participant,
        ObserverClientId = participantRegistry.Network != null ? participantRegistry.Network.ClientNo.ToString(CultureInfo.InvariantCulture) : "",
        ObservedAtUtc = DateTimeOffset.UtcNow, MonotonicSeconds = (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency,
        ObservationKind = kind, CardSetId = sessionManager.CurrentCardSetId };
    public bool RecordEvent(string eventType, string eventId, string participantId = "", string condition = "", string observationKind = "notification", string participantsAtStart = "")
    {
        if (!CanWrite || sessionManager == null || string.IsNullOrEmpty(sessionManager.CurrentSessionId)) return false;
        var entry = CreateObservation(eventType, eventId, participantId, observationKind); entry.Condition = condition; entry.ParticipantsAtStart = participantsAtStart;
        pending.Enqueue(entry); Drain(); return true;
    }
    public bool TryFlushBeforeReset()
    {
        if (!isActiveAndEnabled || !subscribed || !CanWrite) { LastError = "ExperimentLogger is not active / configured as PC Experimenter"; return false; }
        try {
            if (lobby.IsExperimentStarted && lobby.StartedMode == ExperimentManager.ExperimentMode.Proposed &&
                (reveal == null || shared == null || submission == null || (finalAgreement.IsBoardConfirmed && !reveal.HasRevealed)))
                throw new InvalidDataException("Proposed logging sources / committed Reveal are missing");
            CaptureSnapshot(); ObserveAllSubmissions(Kind); Drain();
            pendingAbort = lobby.IsExperimentStarted && !finalAgreement.IsBoardConfirmed ? CreateObservation("SESSION_ABORTED", sessionManager.CurrentSessionId + ":SESSION_ABORTED", "", "reset_request_accepted") : null; }
        catch (Exception error) { LastError = error.Message; return false; }
        return pending.Count == 0 && string.IsNullOrEmpty(LastError);
    }
    public void MarkResetAccepted()
    {
        if (CanWrite && pendingAbort != null) { pending.Enqueue(pendingAbort); pendingAbort = null; Drain(); }
    }
    private void Drain()
    {
        if (!CanWrite || pending.Count == 0) return;
        IsSaving = true;
        try
        {
            if (store == null) { store = new ExperimentCsvLogStore(OutputDirectory); Debug.Log("[Experiment Logger] CSV root: " + store.RootDirectory, this); }
            while (pending.Count > 0) { var entry = pending.Peek(); store.Record(entry); pending.Dequeue(); if (entry.FinalPlacements != null) savedFinal.Add(entry.SessionId); if (entry.IndependentAnswers != null) savedIndependent.Add(entry.SessionId); }
            LastError = "";
        }
        catch (Exception error)
        {
            nextRetry = Time.unscaledTime + 5;
            if (LastError != error.Message) Debug.LogError("[Experiment Logger] CSV save failed; event retained for retry: " + error.Message, this);
            LastError = error.Message;
        }
        finally { IsSaving = false; }
    }
}
