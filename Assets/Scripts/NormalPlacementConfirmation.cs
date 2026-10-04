using System;
using System.Collections.Generic;
using UnityEngine;

// One atomic Room value per card: A/B/C/D = confirmed Tier; Unconfirmed = reset.
public partial class NormalPlacementSync
{
    private const string ConfirmationPrefix = "normalConfirmation_";
    private const string UnconfirmedValue = "Unconfirmed";
    [SerializeField] private ExperimentParticipantRegistry participantRegistry;
    private readonly Dictionary<string, string> finalTiers = new Dictionary<string, string>();
    public event Action<string, string> OnCardConfirmed;
    public event Action<string> OnCardConfirmationReset;
    public bool IsCardConfirmed(string cardId) => cardId != null && finalTiers.ContainsKey(cardId);
    public string GetFinalTier(string cardId) => cardId != null && finalTiers.TryGetValue(cardId, out var tier) ? tier : null;
    private static bool IsFinalTier(string tier) => tier == "A" || tier == "B" || tier == "C" || tier == "D";

    public bool ConfirmCurrentCard() => ConfirmCard(experimentManager != null ? experimentManager.CurrentCardId : null);

    public bool ConfirmCard(string cardId)
    {
        Debug.Log($"[Normal Confirm Request] {cardId}");
        Subscribe();
        if (!isActiveAndEnabled || !IsNormalMode || participantRegistry == null || !participantRegistry.IsLocalExperimenter ||
            subscribedManager == null || !subscribedManager.IsReady)
            return ConfirmFailed(cardId, "requires Normal, a ready connection and Experimenter role");
        if (cardId == null || !cardsById.TryGetValue(cardId, out var card) || card == null)
            return ConfirmFailed(cardId, "card not found");
        if (IsCardConfirmed(cardId)) return ConfirmFailed(cardId, "already confirmed");
        string tier = subscribedManager.GetGlobalVariable(VariablePrefix + cardId);
        // Never confirm an unsent/local-only or still-held placement.
        if (!IsFinalTier(tier) || tier != card.CurrentTier || !card.CanConfirmNormalPlacement)
            return ConfirmFailed(cardId, $"is {card.CurrentTier}, held, or waiting for Room placement synchronization");
        if (!subscribedManager.SetGlobalVariable(ConfirmationPrefix + cardId, tier))
            return ConfirmFailed(cardId, "Room write failed");
        Debug.Log($"[Normal Confirm] {cardId} -> Tier {tier}");
        return true;
    }

    private bool ConfirmFailed(string cardId, string reason)
    {
        Debug.LogWarning($"[Normal Confirm Failed] {cardId} {reason}", this);
        return false;
    }

    // Explicit, role-guarded Room reset entry point for a future reset UI.
    public bool ResetConfirmation(string cardId)
    {
        Subscribe();
        if (!isActiveAndEnabled || !IsNormalMode || participantRegistry == null || !participantRegistry.IsLocalExperimenter ||
            subscribedManager == null || !subscribedManager.IsReady || cardId == null || !cardsById.ContainsKey(cardId)) return false;
        // Replace any stale placement first. The card stays locked until reset arrives.
        string tier = GetFinalTier(cardId);
        if (tier != null && !subscribedManager.SetGlobalVariable(VariablePrefix + cardId, tier)) return false;
        return subscribedManager.SetGlobalVariable(ConfirmationPrefix + cardId, UnconfirmedValue);
    }

    private void ReadConfirmations()
    {
        finalTiers.Clear();
        foreach (var entry in cardsById)
        {
            string value = subscribedManager.GetGlobalVariable(ConfirmationPrefix + entry.Key);
            if (value != null) ReceiveConfirmation(ConfirmationPrefix + entry.Key, value);
        }
    }

    private bool ReceiveConfirmation(string name, string value)
    {
        if (string.IsNullOrEmpty(name) || !name.StartsWith(ConfirmationPrefix, StringComparison.Ordinal)) return false;
        string cardId = name.Substring(ConfirmationPrefix.Length);
        if (!cardsById.ContainsKey(cardId)) return true;
        if (value == UnconfirmedValue)
        {
            if (finalTiers.Remove(cardId))
            {
                if (cardsById[cardId] != null) cardsById[cardId].SetNormalConfirmationLock(false, null);
                if (IsNormalMode && experimentManager.CurrentCardId == cardId)
                    experimentManager.SetState(ExperimentManager.ExperimentState.Idle);
                OnCardConfirmationReset?.Invoke(cardId);
            }
            return true;
        }
        if (!IsFinalTier(value) || !zonesByTier.ContainsKey(value))
        {
            Debug.LogWarning($"[Normal Confirm Failed] Invalid Room confirmation: {cardId} / {value}", this);
            return true;
        }
        bool changed = GetFinalTier(cardId) != value;
        finalTiers[cardId] = value;
        pendingPlacements.Remove(cardId);
        RefreshConfirmationLocks();
        if (changed)
        {
            Debug.Log($"[Normal Confirm Receive] {cardId} -> Tier {value}");
            OnCardConfirmed?.Invoke(cardId, value);
        }
        return true;
    }

    private void RefreshConfirmationLocks()
    {
        foreach (var entry in cardsById)
        {
            if (entry.Value == null) continue;
            bool locked = IsNormalMode && finalTiers.TryGetValue(entry.Key, out var tier);
            entry.Value.SetNormalConfirmationLock(locked, locked ? zonesByTier[GetFinalTier(entry.Key)] : null);
        }
        if (IsNormalMode && IsCardConfirmed(experimentManager.CurrentCardId) &&
            experimentManager.CurrentState != ExperimentManager.ExperimentState.Confirmed)
            experimentManager.SetState(ExperimentManager.ExperimentState.Confirmed);
    }

    public bool RejectConfirmedPlacement(string cardId)
    {
        if (!IsNormalMode || !IsCardConfirmed(cardId)) return false;
        Debug.LogWarning($"[Normal Placement Rejected] {cardId} is confirmed at Tier {GetFinalTier(cardId)}", this);
        if (cardsById.TryGetValue(cardId, out var card) && card != null)
            card.SetNormalConfirmationLock(true, zonesByTier[GetFinalTier(cardId)]);
        return true;
    }
}
