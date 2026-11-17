using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;
using Debug = UnityEngine.Debug;

// Only observes existing lifecycle notifications. Never writes experiment/network variables.
[DefaultExecutionOrder(-2000)]
public sealed class ExperimentLogger : MonoBehaviour
{
    [SerializeField] private ExperimentSessionManager sessionManager;
    [SerializeField] private ExperimentParticipantRegistry participantRegistry;
    [SerializeField] private ExperimentLobbyController lobby;
    [SerializeField] private FinalAgreementManager finalAgreement;
    private ExperimentCsvLogStore store;
    private readonly Queue<ExperimentCsvLogStore.Observation> pending = new Queue<ExperimentCsvLogStore.Observation>();
    private string outputRootOverride = ""; // Isolated verification destination; not serialized into Scene/build.
    private bool subscribed, wasWriter;
    private float nextRetry;
    public string LastError { get; private set; } = "";
    public string OutputDirectory => store != null ? store.RootDirectory :
        (string.IsNullOrEmpty(outputRootOverride) ? Path.Combine(Application.persistentDataPath, "ExperimentLogs") : outputRootOverride);
    private bool CanWrite => participantRegistry != null && participantRegistry.IsLocalExperimenter && !Application.isMobilePlatform;

    private void OnEnable()
    {
        if (subscribed || sessionManager == null || lobby == null || finalAgreement == null) return;
        sessionManager.OnSessionResetStarted += OnResetStarted;
        sessionManager.OnSessionStarted += OnResetCompleted;
        lobby.OnExperimentStarted += OnExperimentStarted;
        finalAgreement.OnBoardConfirmed += OnBoardConfirmed;
        subscribed = true;
        CaptureSnapshot();
    }
    private void Start() { wasWriter = CanWrite; CaptureSnapshot(); }
    private void Update()
    {
        if (CanWrite && !wasWriter) CaptureSnapshot();
        wasWriter = CanWrite;
        if (pending.Count > 0 && Time.unscaledTime >= nextRetry) Drain();
    }
    private void OnDisable()
    {
        if (subscribed)
        {
            sessionManager.OnSessionResetStarted -= OnResetStarted;
            sessionManager.OnSessionStarted -= OnResetCompleted;
            lobby.OnExperimentStarted -= OnExperimentStarted;
            finalAgreement.OnBoardConfirmed -= OnBoardConfirmed;
        }
        subscribed = false;
        Drain(); // Every event was already flushed; this also retries a pending failure.
    }
    private void OnApplicationQuit() => Drain();

    private void EnsureSession(string id, string kind)
        => RecordEvent("SESSION_CREATED", id + ":SESSION_CREATED", observationKind: kind);
    private void OnResetStarted(string id)
    {
        if (!CanWrite) return;
        EnsureSession(id, sessionManager.HasResetControl ? "received_reset_control" : "retained_snapshot");
        // An active snapshot on reconnect does not prove that we observed Reset starting.
        if (sessionManager.HasResetControl)
            RecordEvent("SESSION_RESET_STARTED", id + ":SESSION_RESET_STARTED");
    }
    private void OnResetCompleted(string id)
    {
        if (!CanWrite) return;
        EnsureSession(id, "retained_snapshot");
        RecordEvent("SESSION_RESET_COMPLETED", id + ":SESSION_RESET_COMPLETED");
    }
    private void OnExperimentStarted(string id, string startId, ExperimentManager.ExperimentMode mode)
    {
        if (!CanWrite) return;
        EnsureSession(id, "retained_snapshot");
        RecordStart(id, startId, mode, "received_start_record");
    }
    private void RecordStart(string id, string startId, ExperimentManager.ExperimentMode mode, string kind)
    {
        RecordEvent("EXPERIMENT_STARTED", id + ":EXPERIMENT_STARTED:" + startId, condition: mode.ToString(), observationKind: kind,
            participantsAtStart: string.Join(";", participantRegistry.ParticipantClientNos.Select(n => n.ToString(CultureInfo.InvariantCulture))));
    }
    private void OnBoardConfirmed(IReadOnlyDictionary<string, string> placements)
    {
        if (!CanWrite || !lobby.IsExperimentStarted) return;
        EnsureSession(sessionManager.CurrentSessionId, "retained_snapshot");
        // A Session has one final result; keep its ID stable across restoration/reconnect.
        RecordEvent("BOARD_CONFIRMED", sessionManager.CurrentSessionId + ":BOARD_CONFIRMED");
    }
    private void CaptureSnapshot()
    {
        if (!CanWrite || sessionManager == null || string.IsNullOrEmpty(sessionManager.CurrentSessionId)) return;
        string id = sessionManager.CurrentSessionId;
        EnsureSession(id, "retained_snapshot");
        if (sessionManager.HasResetControl)
            RecordEvent("SESSION_RESET_STARTED", id + ":SESSION_RESET_STARTED", observationKind: "retained_snapshot");
        else if (!sessionManager.IsResettingSession && !string.IsNullOrEmpty(sessionManager.SessionStartedAtUtc))
            RecordEvent("SESSION_RESET_COMPLETED", id + ":SESSION_RESET_COMPLETED", observationKind: "retained_snapshot");
        if (lobby.IsExperimentStarted && lobby.StartedMode.HasValue)
        {
            RecordStart(id, lobby.StartedEventId, lobby.StartedMode.Value, "retained_snapshot");
            if (finalAgreement.IsBoardConfirmed)
                RecordEvent("BOARD_CONFIRMED", id + ":BOARD_CONFIRMED", observationKind: "retained_snapshot");
        }
    }

    // Future operation adapters may supply Participant ID + stable source Event ID here.
    // This does not transfer events from Quest; timestamps still mean local PC observation.
    public bool RecordEvent(string eventType, string eventId, string participantId = "", string condition = "",
        string observationKind = "notification", string participantsAtStart = "")
    {
        if (!CanWrite || sessionManager == null || string.IsNullOrEmpty(sessionManager.CurrentSessionId)) return false;
        pending.Enqueue(new ExperimentCsvLogStore.Observation {
            SessionId = sessionManager.CurrentSessionId, Epoch = sessionManager.SessionEpoch,
            EventType = eventType, EventId = eventId, ParticipantId = participantId, Condition = condition,
            ObserverClientId = participantRegistry.Network != null ? participantRegistry.Network.ClientNo.ToString(CultureInfo.InvariantCulture) : "",
            ObservedAtUtc = DateTimeOffset.UtcNow, MonotonicSeconds = (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency,
            ObservationKind = observationKind, ParticipantsAtStart = participantsAtStart,
            CardSetId = sessionManager.CurrentCardSetId
        });
        Drain();
        return true; // Accepted for saving; LastError reports pending IO failure.
    }
    private void Drain()
    {
        if (!CanWrite || pending.Count == 0) return;
        try
        {
            if (store == null) { store = new ExperimentCsvLogStore(OutputDirectory); Debug.Log("[Experiment Logger] CSV root: " + store.RootDirectory, this); }
            while (pending.Count > 0)
            {
                var entry = pending.Peek();
                if (store.Record(entry)) Debug.Log("[Experiment Logger] " + entry.EventType + " -> " + Path.Combine(store.RootDirectory, entry.SessionId), this);
                pending.Dequeue();
            }
            LastError = "";
        }
        catch (Exception error)
        {
            nextRetry = Time.unscaledTime + 5;
            if (LastError != error.Message) Debug.LogError("[Experiment Logger] CSV save failed; event retained for retry: " + error.Message, this);
            LastError = error.Message;
        }
    }
}
