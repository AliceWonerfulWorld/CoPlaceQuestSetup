#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.InputSystem;

// Editor-only partial keeps keyboard testing separate from the runtime sync logic.
public partial class NormalPlacementSync
{
    private string editorSelectedCardId = "Card_01";

    private void HandleEditorTestInput()
    {
        if (!Application.isPlaying || !isActiveAndEnabled || !IsNormalMode) return;
        var keyboard = Keyboard.current;
        if (keyboard == null) return;

        string selected = null;
        if (keyboard.f6Key.wasPressedThisFrame) selected = "Card_01";
        else if (keyboard.f7Key.wasPressedThisFrame) selected = "Card_02";
        else if (keyboard.f8Key.wasPressedThisFrame) selected = "Card_03";
        if (selected != null)
        {
            editorSelectedCardId = selected;
            Debug.Log($"[Normal Editor Test] Selected {selected}");
        }

        string tier = null;
        if (keyboard.digit1Key.wasPressedThisFrame) tier = "A";
        else if (keyboard.digit2Key.wasPressedThisFrame) tier = "B";
        else if (keyboard.digit3Key.wasPressedThisFrame) tier = "C";
        else if (keyboard.digit4Key.wasPressedThisFrame) tier = "D";
        else if (keyboard.digit0Key.wasPressedThisFrame) tier = "Unclassified";
        if (tier != null) TryPlaceEditorTestCard(editorSelectedCardId, tier);
    }

    private bool TryPlaceEditorTestCard(string cardId, string tierId)
    {
        if (!isActiveAndEnabled || !IsNormalMode || subscribedManager == null || !subscribedManager.IsReady)
            return false;
        if (!cardsById.TryGetValue(cardId, out var card) || card == null ||
            !zonesByTier.TryGetValue(tierId, out var zone) || zone == null)
        {
            Debug.LogWarning($"[Normal Editor Test] Missing card or Tier: {cardId} / {tierId}", this);
            return false;
        }
        // ApplySyncedPlacement never sends. A held card or full Tier rejects the input.
        if (!card.ApplySyncedPlacement(zone))
        {
            Debug.LogWarning($"[Normal Editor Test] Placement rejected: {cardId} / {tierId}", this);
            return false;
        }
        Debug.Log(tierId == "Unclassified"
            ? $"[Normal Editor Test] {cardId} -> Unclassified"
            : $"[Normal Editor Test] {cardId} -> Tier {tierId}");
        return SendPlacement(cardId, tierId);
    }
}
#endif
