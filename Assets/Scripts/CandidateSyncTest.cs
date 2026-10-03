using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using Styly.NetSync;
using UnityEngine;
#if UNITY_EDITOR
using UnityEngine.InputSystem;
#endif

public class CandidateSyncTest : MonoBehaviour
{
    [SerializeField] private TMP_Text candidateStatusText;
    [SerializeField] private CandidateMarkerManager candidateMarkerManager;
    [SerializeField] private ExperimentManager experimentManager;
    [SerializeField] private ExperimentParticipantRegistry participantRegistry;
    [SerializeField] private NetSyncManager netSyncManager;
    private readonly Dictionary<string, string> participantCandidates = new Dictionary<string, string>();
    private readonly Dictionary<string, bool> participantAnswered = new Dictionary<string, bool>();
    private readonly Dictionary<string, string> participantSelectedTimes = new Dictionary<string, string>();
    private readonly Dictionary<string, List<RevealedCandidate>> revealedCards = new Dictionary<string, List<RevealedCandidate>>();
    private readonly HashSet<string> dirtyCards = new HashSet<string>();
    private readonly Dictionary<string, string> checkLogs = new Dictionary<string, string>();
    private readonly Dictionary<string, string> visibilityLogs = new Dictionary<string, string>();
    private readonly HashSet<string> rejectedChanges = new HashSet<string>();
    private NetSyncManager subscribedManager;
    private bool started, networkWasReady;
    private string previousCurrentCard;
    private class RevealedCandidate { public int clientNo, displayId; public string tier, selectedAt; }
    private bool IsProposed => experimentManager != null && experimentManager.CurrentMode == ExperimentManager.ExperimentMode.Proposed;
    private NetSyncManager Network => netSyncManager != null ? netSyncManager : NetSyncManager.Instance;
    public bool IsCardRevealed(string cardId) => IsProposed && revealedCards.ContainsKey(cardId);
    private static bool IsValidTier(string tier) => tier == "A" || tier == "B" || tier == "C" || tier == "D";
    private static string Key(int client, string card) => $"{client}_{card}";

    private void Start() { started = true; Subscribe(); }
    private void OnEnable() { if (started) Subscribe(); }
    private void OnDisable()
    {
        if (subscribedManager != null)
        {
            subscribedManager.OnClientVariableChanged.RemoveListener(OnClientVariableChanged);
            subscribedManager.OnReady.RemoveListener(OnReady);
        }
        if (participantRegistry != null) participantRegistry.OnParticipantsChanged -= QueueAllCards;
        if (experimentManager != null) experimentManager.OnModeChanged -= OnModeChanged;
        subscribedManager = null;
        networkWasReady = false;
        if (candidateMarkerManager != null) candidateMarkerManager.ClearAllMarkers();
    }
    private void Subscribe()
    {
        if (subscribedManager != null) return;
        subscribedManager = Network;
        if (subscribedManager == null) return;
        subscribedManager.OnClientVariableChanged.AddListener(OnClientVariableChanged);
        subscribedManager.OnReady.AddListener(OnReady);
        if (participantRegistry != null) participantRegistry.OnParticipantsChanged += QueueAllCards;
        if (experimentManager != null) experimentManager.OnModeChanged += OnModeChanged;
        if (subscribedManager.IsReady) OnReady();
    }
    private void OnReady()
    {
        if (subscribedManager == null || !subscribedManager.IsReady) return;
        networkWasReady = true;
        participantCandidates.Clear(); participantAnswered.Clear(); participantSelectedTimes.Clear();
        revealedCards.Clear(); checkLogs.Clear(); visibilityLogs.Clear(); rejectedChanges.Clear();
        if (candidateMarkerManager != null) candidateMarkerManager.ClearAllMarkers();
        var clients = new HashSet<int>(subscribedManager.GetAliveClients(includeStealthClients: true));
        clients.Add(subscribedManager.ClientNo);
        foreach (int client in clients)
            foreach (var variable in subscribedManager.GetAllClientVariables(client)) StoreVariable(client, variable.Key, variable.Value);
        QueueAllCards();
    }
    private void Update()
    {
        Subscribe();
        if (subscribedManager == null || !subscribedManager.IsReady) networkWasReady = false;
        else if (!networkWasReady) OnReady();
#if UNITY_EDITOR
        HandleEditorTestInput();
#endif
    }

#if UNITY_EDITOR
    private void HandleEditorTestInput()
    {
        // Keep experimenter shortcuts silent and leave Player/Quest input to
        // CardTierDetector's real Grab/Release path.
        if (!Application.isPlaying || !IsProposed || participantRegistry == null ||
            !participantRegistry.IsLocalParticipant) return;
        var keyboard = Keyboard.current;
        if (keyboard == null) return;

        string tier = null;
        if (keyboard.digit1Key.wasPressedThisFrame) tier = "A";
        else if (keyboard.digit2Key.wasPressedThisFrame) tier = "B";
        else if (keyboard.digit3Key.wasPressedThisFrame) tier = "C";
        else if (keyboard.digit4Key.wasPressedThisFrame) tier = "D";
        else if (keyboard.digit0Key.wasPressedThisFrame)
        {
            Debug.Log("[Editor Test Cancel] Card_01");
            CancelCandidate("Card_01");
            return;
        }
        if (tier == null) return;
        Debug.Log($"[Editor Test Answer] Card_01 -> Tier {tier}");
        SendCandidate("Card_01", tier);
    }
#endif
    private bool CanSend(string cardId)
    {
        if (!IsProposed || !isActiveAndEnabled || string.IsNullOrEmpty(cardId)) return false;
        Subscribe();
        if (Network == null || !Network.IsReady || participantRegistry == null || !participantRegistry.IsLocalParticipant)
        {
            Debug.LogWarning("[Candidate] ReadyなParticipantのみ回答できます", this);
            return false;
        }
        participantRegistry.RefreshParticipants();
        if (!participantRegistry.IsRegisteredParticipant(Network.ClientNo))
        {
            Debug.LogWarning("[Candidate] 参加者登録の同期完了を待ってください", this);
            return false;
        }
        if (IsCardRevealed(cardId))
        {
            Debug.LogWarning($"[Candidate] Reveal後の変更・取消は禁止: {cardId}", this);
            return false;
        }
        return true;
    }
    public void SendCandidate(string cardId, string tierId)
    {
        if (tierId == "Unclassified") { CancelCandidate(cardId); return; }
        if (!IsValidTier(tierId) || !CanSend(cardId)) return;
        string selectedAt = DateTime.UtcNow.ToString("o");
        // Commit answered last; LateUpdate waits for the complete received payload.
        bool candidate = Network.SetClientVariable($"candidate_{cardId}", tierId);
        bool time = Network.SetClientVariable($"selectedAt_{cardId}", selectedAt);
        bool answered = candidate && time && Network.SetClientVariable($"answered_{cardId}", "true");
        if (!answered) { Debug.LogWarning($"[Candidate] Send failed: {cardId}", this); return; }
        if (experimentManager.CurrentState == ExperimentManager.ExperimentState.Idle) experimentManager.StartCard(cardId);
        Debug.Log($"[Candidate Send] Client {Network.ClientNo} / {cardId} / Tier {tierId} / Answered true / SelectedAt {selectedAt}");
    }
    public void CancelCandidate(string cardId)
    {
        if (!CanSend(cardId)) return;
        bool answered = Network.SetClientVariable($"answered_{cardId}", "false");
        bool candidate = Network.SetClientVariable($"candidate_{cardId}", "Unclassified");
        bool time = Network.SetClientVariable($"selectedAt_{cardId}", "");
        if (!answered || !candidate || !time) Debug.LogWarning($"[Candidate] Cancel send failed: {cardId}", this);
        Debug.Log($"[Candidate Cancel] Client {Network.ClientNo} / {cardId} / Answered false");
    }
    private void OnClientVariableChanged(int client, string name, string oldValue, string newValue)
    {
        StoreVariable(client, name, newValue); // Cache only. No remote marker is created here.
    }
    private void StoreVariable(int client, string name, string value)
    {
        if (string.IsNullOrEmpty(name)) return;
        string card;
        if (name.StartsWith("candidate_", StringComparison.Ordinal))
        {
            card = name.Substring("candidate_".Length);
            // Keep the key on deletion so its existing marker is also cleared.
            participantCandidates[Key(client, card)] = value;
        }
        else if (name.StartsWith("answered_", StringComparison.Ordinal))
        {
            card = name.Substring("answered_".Length);
            participantAnswered[Key(client, card)] = value == "true";
        }
        else if (name.StartsWith("selectedAt_", StringComparison.Ordinal))
        {
            card = name.Substring("selectedAt_".Length);
            participantSelectedTimes[Key(client, card)] = value;
        }
        else return;
        if (!string.IsNullOrEmpty(card)) dirtyCards.Add(card);
    }
    private void QueueAllCards()
    {
        foreach (string key in participantCandidates.Keys) dirtyCards.Add(key.Substring(key.IndexOf('_') + 1));
        foreach (string key in participantAnswered.Keys) dirtyCards.Add(key.Substring(key.IndexOf('_') + 1));
        foreach (string card in revealedCards.Keys) dirtyCards.Add(card);
        if (experimentManager != null) dirtyCards.Add(experimentManager.CurrentCardId);
    }
    private void OnModeChanged(ExperimentManager.ExperimentMode mode)
    {
        if (mode != ExperimentManager.ExperimentMode.Proposed)
        {
            if (candidateMarkerManager != null) candidateMarkerManager.ClearAllMarkers();
            if (candidateStatusText != null) candidateStatusText.text = "";
        }
        else QueueAllCards();
    }
    private void LateUpdate()
    {
        if (!IsProposed || subscribedManager == null || !subscribedManager.IsReady || participantRegistry == null) return;
        participantRegistry.RefreshParticipants();
        if (previousCurrentCard != experimentManager.CurrentCardId)
        {
            previousCurrentCard = experimentManager.CurrentCardId;
            dirtyCards.Add(previousCurrentCard);
        }
        if (dirtyCards.Count > 0)
        {
            var cards = new List<string>(dirtyCards);
            dirtyCards.Clear();
            foreach (string card in cards) ProcessCard(card);
            UpdateCandidateDisplay();
        }
        UpdateExperimentState();
    }
    private void ProcessCard(string card)
    {
        var participants = participantRegistry.ParticipantClientNos;
        int answeredCount = 0;
        bool complete = participants.Count == participantRegistry.ExpectedParticipantCount;
        // Read the completed package cache after all events in this frame.
        // A snapshot can dispatch candidate/answered/selectedAt in any order.
        foreach (int client in participants)
        {
            string key = Key(client, card);
            string tier = subscribedManager.GetClientVariable("candidate_" + card, client);
            string time = subscribedManager.GetClientVariable("selectedAt_" + card, client);
            bool answered = subscribedManager.GetClientVariable("answered_" + card, client) == "true";
            participantCandidates[key] = tier;
            participantAnswered[key] = answered;
            participantSelectedTimes[key] = time;
            if (answered) answeredCount++;
            complete &= answered && IsValidTier(tier) && !string.IsNullOrEmpty(time);
        }
        if (!revealedCards.ContainsKey(card))
        {
            string status = $"{answeredCount}/{participantRegistry.ExpectedParticipantCount}/{participants.Count}/{complete}";
            if (!checkLogs.TryGetValue(card, out var previous) || previous != status)
            {
                checkLogs[card] = status;
                Debug.Log($"[Reveal Check] {card} / {answeredCount} of {participantRegistry.ExpectedParticipantCount} answered");
            }
            if (complete)
            {
                Debug.Log($"[Reveal Ready] {card} / {answeredCount} of {participantRegistry.ExpectedParticipantCount} answered");
                var snapshot = new List<RevealedCandidate>();
                for (int i = 0; i < participants.Count; i++)
                {
                    string key = Key(participants[i], card);
                    snapshot.Add(new RevealedCandidate { clientNo = participants[i], displayId = i + 1,
                        tier = participantCandidates[key], selectedAt = participantSelectedTimes[key] });
                }
                revealedCards[card] = snapshot;
                Debug.Log($"[Reveal] {card}");
                foreach (var entry in snapshot) Debug.Log($"[Reveal Marker] P{entry.displayId} / {card} / Tier {entry.tier}");
            }
        }
        if (revealedCards.TryGetValue(card, out var revealed))
        {
            foreach (var entry in revealed)
            {
                string key = Key(entry.clientNo, card);
                if (participantCandidates.TryGetValue(key, out var tier) &&
                    (tier != entry.tier || !participantAnswered.TryGetValue(key, out bool answered) || !answered) && rejectedChanges.Add(key))
                    Debug.LogWarning($"[Candidate] Reveal後の受信変更を表示へ適用しません: {key}", this);
                if (candidateMarkerManager != null) candidateMarkerManager.UpdateMarker(entry.clientNo, card, entry.tier, true, entry.displayId);
            }
            return;
        }
        foreach (var entry in participantCandidates)
        {
            int separator = entry.Key.IndexOf('_');
            if (entry.Key.Substring(separator + 1) != card || !int.TryParse(entry.Key.Substring(0, separator), out int client)) continue;
            bool self = client == subscribedManager.ClientNo && participantRegistry.IsRegisteredParticipant(client);
            bool visible = self && participantAnswered.TryGetValue(entry.Key, out bool answered) && answered && IsValidTier(entry.Value);
            int displayId = 0;
            for (int i = 0; i < participants.Count; i++) if (participants[i] == client) displayId = i + 1;
            if (candidateMarkerManager != null)
            {
                if (visible) candidateMarkerManager.UpdateMarker(client, card, entry.Value, true, displayId);
                else candidateMarkerManager.ClearMarker(client, card);
            }
            string visibility = $"{visible}/{entry.Value}";
            if (!visibilityLogs.TryGetValue(entry.Key, out var last) || last != visibility)
            {
                visibilityLogs[entry.Key] = visibility;
                if (IsValidTier(entry.Value)) Debug.Log(visible
                    ? $"[Candidate Self Visible] P{displayId} / {card} / Tier {entry.Value}"
                    : $"[Candidate Hidden] P{displayId} (Client {client}) / {card} / Tier {entry.Value}");
            }
        }
    }
    private void UpdateExperimentState()
    {
        string card = experimentManager.CurrentCardId;
        var current = experimentManager.CurrentState;
        if (current == ExperimentManager.ExperimentState.Discussion || current == ExperimentManager.ExperimentState.Confirmed) return;
        ExperimentManager.ExperimentState next;
        if (revealedCards.ContainsKey(card)) next = ExperimentManager.ExperimentState.Revealed;
        else
        {
            bool any = false;
            foreach (int client in participantRegistry.ParticipantClientNos) any |= participantAnswered.TryGetValue(Key(client, card), out bool answered) && answered;
            if (!any && current == ExperimentManager.ExperimentState.Idle) return;
            bool selfAnswered = participantRegistry.IsRegisteredParticipant(subscribedManager.ClientNo) &&
                participantAnswered.TryGetValue(Key(subscribedManager.ClientNo, card), out bool self) && self;
            next = selfAnswered ? ExperimentManager.ExperimentState.WaitingForAnswers : ExperimentManager.ExperimentState.Answering;
        }
        if (current != next) experimentManager.SetState(next);
    }
    private void UpdateCandidateDisplay()
    {
        if (candidateStatusText == null) return;
        var display = new StringBuilder();
        foreach (var card in revealedCards)
            foreach (var entry in card.Value) display.AppendLine($"Participant {entry.displayId} : {card.Key} -> Tier {entry.tier} / Answered: True / SelectedAt: {entry.selectedAt}");
        if (participantRegistry.IsRegisteredParticipant(subscribedManager.ClientNo))
            foreach (var entry in participantCandidates)
            {
                string prefix = subscribedManager.ClientNo + "_";
                if (!entry.Key.StartsWith(prefix, StringComparison.Ordinal)) continue;
                string card = entry.Key.Substring(prefix.Length);
                if (revealedCards.ContainsKey(card)) continue;
                bool answered = participantAnswered.TryGetValue(entry.Key, out bool value) && value;
                if (answered && IsValidTier(entry.Value)) display.AppendLine($"Self : {card} -> Tier {entry.Value} / Answered: True");
            }
        candidateStatusText.text = display.ToString();
    }
}
