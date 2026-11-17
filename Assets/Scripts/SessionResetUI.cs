using TMPro;
using UnityEngine;

// PC-only local presentation. The existing Session manager owns all network/reset state.
public sealed class SessionResetUI : MonoBehaviour
{
    private ExperimentSessionManager session;
    private ExperimentParticipantRegistry registry;
    private GameObject canvasRoot, controls, dialog;
    private UnityEngine.UI.Button resetButton, confirmButton;
    private TMP_Text status, prompt;
    private string confirmationSession, requestedFrom, message = "";
    private long confirmationEpoch;
    private bool awaitingCompletion;
    public bool IsConfirmationOpen { get; private set; }
    public string StatusMessage => message;
    public bool CanReset => !Application.isMobilePlatform && registry != null && registry.IsLocalExperimenter &&
        registry.Network != null && registry.Network.IsReady && session != null && !session.IsResettingSession && !awaitingCompletion;

    public void Configure(ExperimentSessionManager manager, ExperimentParticipantRegistry roles) { session = manager; registry = roles; }
    public void RequestConfirmation()
    {
        if (!CanReset || IsConfirmationOpen) return;
        confirmationSession = session.CurrentSessionId; confirmationEpoch = session.SessionEpoch;
        IsConfirmationOpen = true;
        Refresh();
    }
    public void CancelReset() { IsConfirmationOpen = false; message = "Reset cancelled."; Refresh(); }
    public void ConfirmReset()
    {
        if (!IsConfirmationOpen) return;
        IsConfirmationOpen = false;
        if (!CanReset || confirmationSession != session.CurrentSessionId || confirmationEpoch != session.SessionEpoch)
        { message = "Reset cancelled: connection, role or Session changed. Please try again."; Refresh(); return; }
        requestedFrom = session.CurrentSessionId;
        awaitingCompletion = true; // Lock immediately, before any Room echo or another click.
        if (!session.ResetSession()) { awaitingCompletion = false; message = "Reset failed: " + session.LastFailure; }
        else message = "Resetting Session... waiting for all connected clients.";
        Refresh();
    }
    private void Update() => Refresh();
    public void Refresh()
    {
        bool admin = !Application.isMobilePlatform && registry != null && registry.IsLocalExperimenter;
        if (!admin) IsConfirmationOpen = false;
        if (awaitingCompletion && session != null && !session.IsResettingSession)
        {
            awaitingCompletion = false;
            message = session.CurrentSessionId != requestedFrom ? "Reset complete. Select Mode, then Start in Lobby." : "Reset failed: " + session.LastFailure;
        }
        if (admin && session != null && session.IsResettingSession)
            message = string.IsNullOrEmpty(session.LastFailure) ? "Resetting Session... waiting for all connected clients." : "Reset pending: " + session.LastFailure;
        if (admin && awaitingCompletion && (registry.Network == null || !registry.Network.IsReady))
            message = "Reset pending: disconnected. Reconnect to receive Session acknowledgement.";
        if (IsConfirmationOpen && (!CanReset || confirmationSession != session.CurrentSessionId || confirmationEpoch != session.SessionEpoch))
        { IsConfirmationOpen = false; message = "Reset cancelled: Session or connection changed."; }
        if (admin && canvasRoot == null) Build();
        if (canvasRoot == null) return;
        canvasRoot.SetActive(admin);
        if (!admin) return;
        resetButton.interactable = CanReset && !IsConfirmationOpen;
        confirmButton.interactable = CanReset;
        status.text = message + (session != null && !string.IsNullOrEmpty(session.LogSaveFailure) ? "\nCSV save failed: " + session.LogSaveFailure : "");
        dialog.SetActive(IsConfirmationOpen);
        if (IsConfirmationOpen) prompt.text = "Reset Session?\nCurrent placements and answers will be cleared on all clients.\nSaved CSV logs will be kept.\nSession: " + (string.IsNullOrEmpty(confirmationSession) ? "(new)" : confirmationSession);
    }
    private void OnDisable() { IsConfirmationOpen = false; if (canvasRoot != null) canvasRoot.SetActive(false); }
    private void OnDestroy() { if (canvasRoot != null) { if (Application.isPlaying) Destroy(canvasRoot); else DestroyImmediate(canvasRoot); } }
    private RectTransform Rect(string name, Transform parent, Vector2 anchor, Vector2 position, Vector2 size)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false); rect.anchorMin = rect.anchorMax = rect.pivot = anchor;
        rect.anchoredPosition = position; rect.sizeDelta = size; return rect;
    }
    private TMP_Text Text(string name, Transform parent, Vector2 anchor, Vector2 position, Vector2 size, string text, float fontSize)
    {
        var label = Rect(name, parent, anchor, position, size).gameObject.AddComponent<TextMeshProUGUI>();
        label.font = TMP_Settings.defaultFontAsset; label.fontSize = fontSize; label.text = text;
        label.color = Color.white; label.raycastTarget = false; label.alignment = TextAlignmentOptions.Center;
        label.textWrappingMode = TextWrappingModes.Normal; return label;
    }
    private UnityEngine.UI.Button Button(string name, Transform parent, Vector2 position, string title, UnityEngine.Events.UnityAction action)
    {
        var rect = Rect(name, parent, new Vector2(.5f,.5f), position, new Vector2(230,52));
        var image = rect.gameObject.AddComponent<UnityEngine.UI.Image>(); image.color = new Color(.12f,.26f,.36f,1);
        var button = rect.gameObject.AddComponent<UnityEngine.UI.Button>(); button.targetGraphic = image; button.onClick.AddListener(action);
        Text("Label", rect, new Vector2(.5f,.5f), Vector2.zero, new Vector2(220,48), title, 23);
        return button;
    }
    private void Build()
    {
        canvasRoot = new GameObject("ExperimenterSessionReset", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(canvasRoot, gameObject.scene);
        var canvas = canvasRoot.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 100;
        var scaler = canvasRoot.GetComponent<UnityEngine.UI.CanvasScaler>(); scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920,1080); scaler.matchWidthOrHeight = .5f;
        controls = Rect("ResetControls", canvasRoot.transform, new Vector2(1,1), new Vector2(-20,-20), new Vector2(440,160)).gameObject;
        var bg = controls.AddComponent<UnityEngine.UI.Image>(); bg.color = new Color(.035f,.055f,.085f,.96f); bg.raycastTarget = false;
        resetButton = Button("ResetSession", controls.transform, new Vector2(0,45), "Reset Session", RequestConfirmation);
        status = Text("ResetStatus", controls.transform, new Vector2(.5f,.5f), new Vector2(0,-33), new Vector2(420,82), "", 20);
        status.enableAutoSizing = true; status.fontSizeMin = 14; status.fontSizeMax = 20;
        dialog = Rect("ResetConfirmation", canvasRoot.transform, new Vector2(.5f,.5f), Vector2.zero, new Vector2(1920,1080)).gameObject;
        var dialogRect = (RectTransform)dialog.transform; dialogRect.anchorMin = Vector2.zero; dialogRect.anchorMax = Vector2.one; dialogRect.sizeDelta = Vector2.zero;
        var blocker = dialog.AddComponent<UnityEngine.UI.Image>(); blocker.color = new Color(0,0,0,.85f);
        prompt = Text("Prompt", dialog.transform, new Vector2(.5f,.5f), new Vector2(0,65), new Vector2(820,240), "", 30);
        confirmButton = Button("ConfirmReset", dialog.transform, new Vector2(-140,-115), "Reset Session", ConfirmReset);
        Button("CancelReset", dialog.transform, new Vector2(140,-115), "Cancel", CancelReset);
        dialog.SetActive(false);
    }
}
