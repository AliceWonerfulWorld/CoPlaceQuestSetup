using System;
using System.Collections.Generic;
using UnityEngine;

// Compatibility getters retained for log collection. Confirmation now belongs to FinalAgreementManager.
public partial class NormalPlacementSync
{
    [SerializeField] private ExperimentParticipantRegistry participantRegistry;
    [SerializeField] private FinalAgreementManager finalAgreement;
    private readonly Dictionary<string, string> finalTiers = new Dictionary<string, string>();
    public event Action<string, string> OnCardConfirmed;
    public event Action<string> OnCardConfirmationReset;
    public IReadOnlyCollection<string> CardIds => cardsById.Keys;
    public CardTierDetector GetCard(string cardId) => cardId != null && cardsById.TryGetValue(cardId, out var card) ? card : null;
    public bool IsCardConfirmed(string cardId) => cardId != null && finalTiers.ContainsKey(cardId);
    public string GetFinalTier(string cardId) => cardId != null && finalTiers.TryGetValue(cardId, out var tier) ? tier : null;
    public bool IsLocalInteractionLocked => IsSharedPlacementPhase && finalAgreement != null && finalAgreement.IsLocalInteractionLocked;

    [Obsolete("Use Participant agreement via FinalAgreementManager.ToggleLocalReady().")]
    public bool ConfirmCurrentCard() => ConfirmCard(experimentManager != null ? experimentManager.CurrentCardId : null);
    [Obsolete("Experimenter card confirmation is replaced by Participant board agreement.")]
    public bool ConfirmCard(string cardId)
    {
        Debug.LogWarning("[Normal Confirm Failed] Use Participant Final Agreement; Experimenter confirmation is disabled", this);
        return false;
    }
    [Obsolete("Reset the complete Board agreement, not an individual final result.")]
    public bool ResetConfirmation(string cardId) => finalAgreement != null && finalAgreement.ResetBoardAgreement();

    public void SetBoardConfirmation(IReadOnlyDictionary<string, string> placements, bool confirmed)
    {
        var previous = new Dictionary<string, string>(finalTiers);
        finalTiers.Clear();
        if (confirmed) foreach (var pair in placements) if (cardsById.ContainsKey(pair.Key) && zonesByTier.ContainsKey(pair.Value)) finalTiers.Add(pair.Key, pair.Value);
        pendingPlacements.Clear(); RefreshConfirmationLocks();
        foreach (var pair in finalTiers) if (!previous.TryGetValue(pair.Key, out var tier) || tier != pair.Value) OnCardConfirmed?.Invoke(pair.Key, pair.Value);
        foreach (var pair in previous) if (!finalTiers.ContainsKey(pair.Key)) OnCardConfirmationReset?.Invoke(pair.Key);
    }
    private void RefreshConfirmationLocks()
    {
        foreach (var entry in cardsById)
        {
            if (entry.Value == null) continue;
            bool locked = IsSharedPlacementPhase && IsCardConfirmed(entry.Key);
            entry.Value.SetNormalConfirmationLock(locked, locked ? zonesByTier[GetFinalTier(entry.Key)] : null);
        }
        if (IsSharedPlacementPhase && finalTiers.Count > 0 && experimentManager.CurrentState != ExperimentManager.ExperimentState.Confirmed)
            experimentManager.SetState(ExperimentManager.ExperimentState.Confirmed);
    }
    public bool RejectConfirmedPlacement(string cardId)
    {
        if (!IsSharedPlacementPhase || !IsCardConfirmed(cardId)) return false;
        Debug.LogWarning($"[Normal Placement Rejected] {cardId} is confirmed at Tier {GetFinalTier(cardId)}", this);
        if (GetCard(cardId) != null) GetCard(cardId).SetNormalConfirmationLock(true, zonesByTier[GetFinalTier(cardId)]);
        return true;
    }
    public bool RejectLocalInteraction(string cardId)
    {
        if (IsResettingSession) return true;
        if (!CanLocalEditSharedBoard) return true;
        if (RejectConfirmedPlacement(cardId)) return true;
        if (!IsLocalInteractionLocked) return false;
        Debug.LogWarning("[Card Interaction Rejected] Local participant is already ready", this);
        // Remote updates still use ApplySyncedPlacement and are allowed while locally Ready.
        string tier = subscribedManager != null && subscribedManager.IsReady ? SessionVariableTransport.GetGlobalVariable(subscribedManager, sessionManager, VariablePrefix + cardId) : null;
        if (tier != null && zonesByTier.TryGetValue(tier, out var zone) && GetCard(cardId) != null) GetCard(cardId).ApplySyncedPlacement(zone);
        return true;
    }
}
