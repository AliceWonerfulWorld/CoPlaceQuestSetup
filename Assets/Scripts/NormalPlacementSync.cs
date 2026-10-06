using System;
using System.Collections.Generic;
using Styly.NetSync;
using UnityEngine;

public partial class NormalPlacementSync : MonoBehaviour
{
    private const string VariablePrefix = "normalPlacement_";

    [SerializeField] private ExperimentManager experimentManager;
    [SerializeField] private NetSyncManager netSyncManager;
    [SerializeField] private ExperimentSessionManager sessionManager;
    [SerializeField] private ProposedSharedPlacementController proposedSharedPlacement;
    [SerializeField] private CardTierDetector[] cards;
    [SerializeField] private TierZone[] tierZones;

    private readonly Dictionary<string, CardTierDetector> cardsById = new Dictionary<string, CardTierDetector>();
    private readonly Dictionary<string, TierZone> zonesByTier = new Dictionary<string, TierZone>();
    private readonly Dictionary<string, TierZone> pendingPlacements = new Dictionary<string, TierZone>();
    private NetSyncManager subscribedManager;
    private bool started;
    private bool wasNormalAndReady;

    public bool IsResettingSession => sessionManager != null && sessionManager.IsResettingSession;
    public bool HasUnclassifiedZone => zonesByTier.ContainsKey("Unclassified");
    public bool IsNormalMode => experimentManager != null
        && experimentManager.CurrentMode == ExperimentManager.ExperimentMode.Normal;
    public bool IsSharedPlacementPhase => experimentManager != null && experimentManager.CanRunExperiment &&
        (IsNormalMode || (proposedSharedPlacement != null && proposedSharedPlacement.IsActive));
    public string SharedAgreementId => proposedSharedPlacement != null ? proposedSharedPlacement.AgreementId : null;
    public bool CanLocalEditSharedBoard => IsSharedPlacementPhase && !IsResettingSession &&
        experimentManager.CurrentState != ExperimentManager.ExperimentState.Confirmed &&
        (IsNormalMode || (participantRegistry != null && participantRegistry.IsLocalParticipant &&
        subscribedManager != null && participantRegistry.IsRegisteredParticipant(subscribedManager.ClientNo)));

    private void Awake()
    {
        foreach (var card in cards ?? Array.Empty<CardTierDetector>())
        {
            if (card == null) continue;
            if (!cardsById.TryAdd(card.gameObject.name, card))
                Debug.LogError($"[Normal Placement] Duplicate card ID: {card.gameObject.name}", this);
        }

        foreach (var zone in tierZones ?? Array.Empty<TierZone>())
        {
            if (zone == null || string.IsNullOrEmpty(zone.tierName)) continue;
            if (!zonesByTier.TryAdd(zone.tierName, zone))
                Debug.LogError($"[Normal Placement] Duplicate Tier: {zone.tierName}", this);
        }
    }

    private void Start()
    {
        started = true;
        Subscribe();
    }

    private void OnEnable()
    {
        if (started) Subscribe();
    }

    private void OnDisable()
    {
        Unsubscribe();
        pendingPlacements.Clear();
        wasNormalAndReady = false;
    }

    private void Subscribe()
    {
        if (subscribedManager != null) return;
        subscribedManager = netSyncManager != null ? netSyncManager : NetSyncManager.Instance;
        if (subscribedManager == null) return;

        subscribedManager.OnGlobalVariableChanged.AddListener(OnGlobalVariableChanged);
        subscribedManager.OnReady.AddListener(OnNetworkReady);
        OnNetworkReady();
    }

    private void Unsubscribe()
    {
        if (subscribedManager != null)
        {
            subscribedManager.OnGlobalVariableChanged.RemoveListener(OnGlobalVariableChanged);
            subscribedManager.OnReady.RemoveListener(OnNetworkReady);
        }
        subscribedManager = null;
    }

    private void Update()
    {
        // Also handles late NetSync initialization without searching the scene each frame.
        Subscribe();
        if (IsResettingSession) return;
        RefreshConfirmationLocks();
        bool normalAndReady = IsSharedPlacementPhase && subscribedManager != null && subscribedManager.IsReady;
        if (!normalAndReady)
        {
            pendingPlacements.Clear();
            wasNormalAndReady = false;
            return;
        }

        if (!wasNormalAndReady) ReadSharedPlacements();
        wasNormalAndReady = true;
#if UNITY_EDITOR
        HandleEditorTestInput();
#endif

        if (pendingPlacements.Count == 0) return;
        foreach (var entry in cardsById)
        {
            if (pendingPlacements.TryGetValue(entry.Key, out var zone)
                && entry.Value != null && entry.Value.ApplySyncedPlacement(zone))
                pendingPlacements.Remove(entry.Key);
        }
    }

    private void OnNetworkReady()
    {
        if (IsResettingSession || !IsSharedPlacementPhase || subscribedManager == null || !subscribedManager.IsReady) return;
        ReadSharedPlacements();
        wasNormalAndReady = true;
    }

    private void ReadSharedPlacements()
    {
        // GetGlobalVariable may only be called after NetSync's initial sync is ready.
        // Do not publish defaults: a joining client must not overwrite the Room's cards.
        foreach (var entry in cardsById)
        {
            string value = subscribedManager.GetGlobalVariable(VariablePrefix + entry.Key);
            if (value != null) OnGlobalVariableChanged(VariablePrefix + entry.Key, null, value);
        }
    }

    public void ResetLocalSession()
    {
        SetBoardConfirmation(new Dictionary<string, string>(), false);
        pendingPlacements.Clear(); wasNormalAndReady = false;
        if (!zonesByTier.TryGetValue("Unclassified", out var zone)) throw new InvalidOperationException("Unclassified TierZone missing");
        foreach (var card in cardsById.Values) card.ResetSessionPlacement(zone);
    }
    public void RestoreSessionPlacements() { Subscribe(); if (IsSharedPlacementPhase && subscribedManager != null && subscribedManager.IsReady) ReadSharedPlacements(); }

    public bool SendPlacement(string cardId, string tierId)
    {
        if (IsResettingSession || !isActiveAndEnabled || !CanLocalEditSharedBoard) return false;
        if (RejectLocalInteraction(cardId)) return false;
        if (string.IsNullOrEmpty(cardId) || !cardsById.ContainsKey(cardId)
            || string.IsNullOrEmpty(tierId) || !zonesByTier.ContainsKey(tierId))
        {
            Debug.LogWarning($"[Normal Placement] Unknown card or Tier: {cardId} / {tierId}", this);
            return false;
        }

        Subscribe();
        if (subscribedManager == null || !subscribedManager.IsReady)
        {
            Debug.LogWarning($"[Normal Placement] NetSync is not ready; placement not sent: {cardId} / {tierId}", this);
            return false;
        }

        if (finalAgreement != null && !finalAgreement.PreparePlacementChange(cardId, tierId)) return false;
        if (!SessionVariableTransport.SetGlobalVariable(subscribedManager, sessionManager, VariablePrefix + cardId, tierId))
        {
            Debug.LogWarning($"[Normal Placement] Placement send failed: {cardId} / {tierId}", this);
            return false;
        }

        // A local release is a newer intent than a remote update deferred while grabbed.
        pendingPlacements.Remove(cardId);
        Debug.Log($"[Normal Placement Send] Client {subscribedManager.ClientNo} / {cardId} / Tier {tierId}");
        return true;
    }

    private void OnGlobalVariableChanged(string name, string oldValue, string newValue)
    {
        if (IsResettingSession || !IsSharedPlacementPhase || string.IsNullOrEmpty(name)
            || !name.StartsWith(VariablePrefix, StringComparison.Ordinal)) return;

        newValue = SessionVariableTransport.Decode(sessionManager, newValue);
        if (newValue == null) return;
        string cardId = name.Substring(VariablePrefix.Length);
        if (!cardsById.TryGetValue(cardId, out var card) || card == null) return;
        if (RejectConfirmedPlacement(cardId)) return;
        if (string.IsNullOrEmpty(newValue) || !zonesByTier.TryGetValue(newValue, out var zone))
        {
            Debug.LogWarning($"[Normal Placement] Invalid shared Tier: {cardId} / {newValue}", this);
            return;
        }

        Debug.Log($"[Normal Placement Receive] {cardId} / Tier {newValue}");
        pendingPlacements[cardId] = zone;
        if (subscribedManager != null && subscribedManager.IsReady && card.ApplySyncedPlacement(zone))
            pendingPlacements.Remove(cardId);
    }
}
