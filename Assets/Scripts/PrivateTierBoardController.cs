using System.Collections.Generic;
using TMPro;
using UnityEngine;
#if UNITY_EDITOR
using UnityEngine.InputSystem;
#endif

// Lives outside contentRoot so hiding the view never disables restoration/reset.
[DefaultExecutionOrder(100)]
public class PrivateTierBoardController : MonoBehaviour
{
    [SerializeField] private ExperimentManager experimentManager;
    [SerializeField] private ExperimentParticipantRegistry participantRegistry;
    [SerializeField] private ExperimentSessionManager sessionManager;
    [SerializeField] private CandidateSyncTest candidateSync;
    [SerializeField] private ProposedRevealCoordinator revealCoordinator;
    [SerializeField] private ProposedSharedPlacementController sharedPlacement;
    [SerializeField] private IndependentAnswerSubmission independentSubmission;
    public CandidateSyncTest Answers => candidateSync;
    public bool HasHeldCard { get { foreach (var card in cards) if (card != null && card.IsHeld) return true; return false; } }
    [SerializeField] private GameObject contentRoot;
    [SerializeField] private PrivateAnswerCard[] cards;
    [SerializeField] private TierZone[] tierZones;
    [SerializeField] private TMP_Text instructions;
    [SerializeField] private Renderer[] mainRenderers;
    [SerializeField] private Canvas[] mainCanvases;
    [SerializeField] private Collider[] mainColliders;
    private readonly Dictionary<string, PrivateAnswerCard> cardsById = new Dictionary<string, PrivateAnswerCard>();
    private readonly Dictionary<string, TierZone> zones = new Dictionary<string, TierZone>();
    private readonly Dictionary<string, CandidateSyncTest.ParticipantAnswer> pending = new Dictionary<string, CandidateSyncTest.ParticipantAnswer>();
    private readonly Dictionary<string, string> observedAnswers = new Dictionary<string, string>();
    private bool wasReady, initialized, mainHidden;
    private bool[] mainRendererStates, mainCanvasStates, mainColliderStates;
#if UNITY_EDITOR
    private string selectedCardId = "Card_01";
#endif
    public bool IsVisible => experimentManager != null && experimentManager.CanRunExperiment && participantRegistry != null && participantRegistry.IsLocalParticipant &&
        experimentManager.CurrentMode == ExperimentManager.ExperimentMode.Proposed && !IsResetting &&
        (revealCoordinator == null || !revealCoordinator.HasRevealed);
    private bool IsResetting => sessionManager != null && sessionManager.IsResettingSession;
    public bool CanInteract => isActiveAndEnabled && IsVisible && candidateSync != null && candidateSync.IsDataReady &&
        candidateSync.IsIndependentAnswerPhase && participantRegistry.CanOperateInSession(sessionManager) &&
        (revealCoordinator == null || !revealCoordinator.IsAnswerLocked) &&
        (independentSubmission == null || !independentSubmission.IsLocalLocked);

    public void ConfigureCards(PrivateAnswerCard[] activeCards)
    {
        cards = activeCards; cardsById.Clear(); pending.Clear(); observedAnswers.Clear();
        foreach (var card in cards) cardsById.Add(card.CardId, card);
#if UNITY_EDITOR
        selectedCardId = cards.Length > 0 ? cards[0].CardId : "";
#endif
    }
    private void Awake()
    {
        foreach (var card in cards) if (card != null) cardsById.Add(card.CardId, card);
        foreach (var zone in tierZones) if (zone != null) zones.Add(zone.tierName, zone);
        mainRendererStates = new bool[mainRenderers.Length]; mainCanvasStates = new bool[mainCanvases.Length];
        mainColliderStates = new bool[mainColliders.Length];
    }
    private void OnEnable()
    {
        if (sessionManager != null)
        {
            sessionManager.OnSessionResetStarted += OnReset;
            sessionManager.OnSessionStarted += OnStarted;
        }
    }
    private void Start() { ResetLocalBoard(); initialized = true; RefreshView(); }
    private void OnDisable()
    {
        if (sessionManager != null)
        {
            sessionManager.OnSessionResetStarted -= OnReset;
            sessionManager.OnSessionStarted -= OnStarted;
        }
        foreach (var card in cards) if (card != null) card.SetInteractionEnabled(false);
        if (contentRoot != null) contentRoot.SetActive(false);
        SetMainHidden(false);
        wasReady = false;
    }
    private void OnReset(string id) { ResetLocalBoard(); wasReady = false; RefreshView(); }
    private void OnStarted(string id) { pending.Clear(); observedAnswers.Clear(); wasReady = false; }
    private void ResetLocalBoard()
    {
        pending.Clear(); observedAnswers.Clear();
#if UNITY_EDITOR
        selectedCardId = cards.Length > 0 ? cards[0].CardId : "";
#endif
        if (!zones.TryGetValue("Unclassified", out var zone)) return;
        foreach (var card in cards) if (card != null) card.ApplyPlacement(zone, true);
    }
    private void Update()
    {
        if (!initialized) return;
        RefreshView();
        bool ready = candidateSync != null && candidateSync.IsDataReady;
        if (!ready) { wasReady = false; return; }
        if (!wasReady)
        {
            // Reconnect restores the server snapshot, not an unacknowledged local move.
            pending.Clear(); observedAnswers.Clear(); wasReady = true;
        }
        if (participantRegistry != null && participantRegistry.IsLocalParticipant) RestoreOwnAnswers();
#if UNITY_EDITOR
        HandleEditorInput();
#endif
    }
    private void RefreshView()
    {
        if (contentRoot != null && contentRoot.activeSelf != IsVisible) contentRoot.SetActive(IsVisible);
        foreach (var card in cards) if (card != null) card.SetInteractionEnabled(CanInteract);
        // Leave Main components alive for NetSync/Final Agreement/Session Reset.
        SetMainHidden(experimentManager != null && (!experimentManager.CanRunExperiment ||
            (experimentManager.CurrentMode == ExperimentManager.ExperimentMode.Proposed && (sharedPlacement == null || !sharedPlacement.IsActive))));
        if (instructions != null)
        {
            string text = revealCoordinator != null && revealCoordinator.IsAnswerLocked && !revealCoordinator.HasRevealed ? "全員の回答を公開する準備をしています。" :
                independentSubmission != null && independentSubmission.IsLocalLocked ? "提出した回答は操作できません。提出取消で変更できます。" :
                CanInteract ? ParticipantUIStyle.IndependentInstruction + "\n未分類に戻すと、そのカードの回答を取り消せます。" : "接続と実験の準備を待っています。";
            if (instructions.text != text) instructions.text = text;
        }
    }
    private void SetMainHidden(bool hidden)
    {
        if (hidden == mainHidden) return;
        for (int i = 0; i < mainRenderers.Length; i++) if (mainRenderers[i] != null)
        { if (hidden) mainRendererStates[i] = mainRenderers[i].enabled; mainRenderers[i].enabled = hidden ? false : mainRendererStates[i];
            var face = mainRenderers[i].GetComponent<PilotCardFace>(); if (face != null) face.SetVisible(mainRenderers[i].enabled); }
        for (int i = 0; i < mainCanvases.Length; i++) if (mainCanvases[i] != null)
        { if (hidden) mainCanvasStates[i] = mainCanvases[i].enabled; mainCanvases[i].enabled = hidden ? false : mainCanvasStates[i]; }
        for (int i = 0; i < mainColliders.Length; i++) if (mainColliders[i] != null)
        { if (hidden) mainColliderStates[i] = mainColliders[i].enabled; mainColliders[i].enabled = hidden ? false : mainColliderStates[i]; }
        mainHidden = hidden;
    }
    public TierZone FindDropZone(Vector3 position) => CardDropTargetResolver.Resolve(tierZones, null, position);
    public TierZone ResolveDropTarget(GameObject card, TierZone previous) => CanInteract
        ? CardDropTargetResolver.Resolve(tierZones, card, card.transform.position, previous) : null;
    public bool TryPlaceCard(string cardId, string tier)
    {
        if (!CanInteract) return Reject("Not in an enabled Participant private answer phase");
        if (!cardsById.TryGetValue(cardId, out var card) || !zones.TryGetValue(tier, out var zone)) return Reject("Unknown card or Tier");
        // A selected XR card is allowed to finish its release; keyboard moves are filtered below.
        string previous = card.CurrentTier;
        if (card.IsHeld) return Reject("Release the selected card first");
        if (!card.CanPlace(zone) || zone.PeekAvailableSnapPoint(card.gameObject) == null) return Reject("Tier has no available SnapPoint");
        if (!candidateSync.TrySetCandidate(cardId, tier, out var submitted))
        { if (previous != tier) zone.ReleaseCard(card.gameObject); return Reject("Candidate send rejected"); }
        if (!card.ApplyPlacement(zone)) return Reject("Card is held or target SnapPoint is unavailable");
        pending[cardId] = submitted;
        Debug.Log(tier == "Unclassified" ? $"[Private Answer Cancelled] {cardId} -> Unclassified" :
            previous != "Unclassified" && previous != tier ? $"[Private Answer Changed] {cardId}: {previous} -> {tier}" :
            $"[Private Answer] {cardId} -> Tier {tier}", this);
        return true;
    }
    private bool Reject(string reason) { Debug.LogWarning("[Private Answer Rejected] Reason: " + reason, this); return false; }
    private void RestoreOwnAnswers()
    {
        foreach (var pair in cardsById)
        {
            if (!candidateSync.TryGetParticipantAnswer(candidateSync.LocalClientNo, pair.Key, out var answer)) continue;
            if (pending.TryGetValue(pair.Key, out var requested))
            {
                if (answer.Tier != requested.Tier || answer.Answered != requested.Answered || answer.SelectedAt != requested.SelectedAt) continue;
                pending.Remove(pair.Key);
            }
            string tier = answer.Answered ? answer.Tier : "Unclassified";
            if (observedAnswers.TryGetValue(pair.Key, out var previous) && previous == tier && pair.Value.CurrentTier == tier) continue;
            if (zones.TryGetValue(tier, out var zone) && pair.Value.ApplyPlacement(zone))
            {
                observedAnswers[pair.Key] = tier;
                Debug.Log($"[Private Board Restore] {pair.Key} -> {tier}", this);
            }
        }
    }
#if UNITY_EDITOR
    private void HandleEditorInput()
    {
        if (!Application.isPlaying || !CanInteract || !participantRegistry.CanUseEditorParticipantInput) return;
        var keyboard = Keyboard.current; if (keyboard == null) return;
        if (keyboard.f6Key.wasPressedThisFrame || keyboard.qKey.wasPressedThisFrame) selectedCardId = cards[0].CardId;
        else if (keyboard.f7Key.wasPressedThisFrame || keyboard.wKey.wasPressedThisFrame) selectedCardId = cards[1].CardId;
        else if (keyboard.f8Key.wasPressedThisFrame || keyboard.eKey.wasPressedThisFrame) selectedCardId = cards[2].CardId;
        for (int i = 0; i < cards.Length && i < 8; i++)
            if (keyboard[(UnityEngine.InputSystem.Key)((int)UnityEngine.InputSystem.Key.F1 + i)].wasPressedThisFrame) selectedCardId = cards[i].CardId;
        string tier = null;
        if (keyboard.digit1Key.wasPressedThisFrame) tier = "A";
        else if (keyboard.digit2Key.wasPressedThisFrame) tier = "B";
        else if (keyboard.digit3Key.wasPressedThisFrame) tier = "C";
        else if (keyboard.digit4Key.wasPressedThisFrame) tier = "D";
        else if (keyboard.digit5Key.wasPressedThisFrame) tier = "E";
        else if (keyboard.digit0Key.wasPressedThisFrame) tier = "Unclassified";
        if (tier != null && cardsById.TryGetValue(selectedCardId, out var card))
        { if (card.IsHeld) Reject("Release the selected card first"); else TryPlaceCard(selectedCardId, tier); }
    }
#endif
}
