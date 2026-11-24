using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;

// Local presentation of the committed Reveal snapshot. No colliders, grab,
// network transforms, raycasters or mutable answer data on the generated UI.
public class ReadonlyParticipantBoardView : MonoBehaviour
{
    [Serializable] public class CardVisual { public string cardId, displayName; public Texture texture; }
    [SerializeField] private ProposedRevealCoordinator revealCoordinator;
    [SerializeField] private TMP_FontAsset font;
    [SerializeField] private CardVisual[] cardVisuals;
    [Header("World-space layout (root Transform sets position / rotation)")]
    [SerializeField, Min(1)] private int columns = 2;
    [SerializeField, Min(0.0001f)] private float canvasScale = 0.0011f;
    [SerializeField] private Vector2 boardSize = new Vector2(720, 840);
    [SerializeField] private Vector2 spacing = new Vector2(110, 100);
    [SerializeField] private Color accent = new Color(0.38f, 0.85f, 0.80f, 1);
    [Header("Participant local reference panel")]
    [SerializeField] private float referenceDistance = 1.4f;
    [SerializeField] private Vector2 referenceOffset = new Vector2(1.0f, -0.1f);
    private ExperimentParticipantRegistry registry;
    private ExperimentSessionManager session;
    private Camera xrCamera;
    private ExperimentManager experiment;
    private bool ParticipantFinished => registry != null && registry.IsLocalParticipant && experiment != null && experiment.CurrentState == ExperimentManager.ExperimentState.Confirmed;
    private GameObject referenceControls;
    private Transform closeControl;
    private TMP_Text toggleLabel;
    private Vector3 fixedPosition;
    private Quaternion fixedRotation;
    private bool layoutCaptured, subscribed, preferredOpen;
    private string preferenceSession, preferenceReveal;
    private long preferenceEpoch;
    public bool IsReferenceOpen { get; private set; }
    private bool HasCurrentContent => content != null && !ParticipantFinished && session != null && !session.IsResettingSession && session.CurrentMode == ExperimentManager.ExperimentMode.Proposed &&
        preferenceSession == session.CurrentSessionId && preferenceEpoch == session.SessionEpoch && revealCoordinator != null && revealCoordinator.HasRevealed && revealCoordinator.RevealId == displayedId;
    public bool CanToggleReference => registry != null && registry.IsLocalParticipant && HasCurrentContent;
    private readonly Dictionary<string, CardVisual> visuals = new Dictionary<string, CardVisual>();
    private GameObject content;
    private string displayedId;
    public int DisplayedParticipantCount { get; private set; }
    public string DisplayedRevealId => displayedId;
    public void ConfigureCards(CardVisual[] values)
    {
        Clear(); cardVisuals = values; visuals.Clear(); Awake();
    }
    private void Awake()
    {
        if (!layoutCaptured) { fixedPosition = transform.position; fixedRotation = transform.rotation; layoutCaptured = true; }
        var all = gameObject.scene.GetRootGameObjects();
        registry = all.SelectMany(o=>o.GetComponentsInChildren<ExperimentParticipantRegistry>(true)).SingleOrDefault();
        experiment = all.SelectMany(o=>o.GetComponentsInChildren<ExperimentManager>(true)).SingleOrDefault();
        session = all.SelectMany(o=>o.GetComponentsInChildren<ExperimentSessionManager>(true)).SingleOrDefault();
        xrCamera = all.SelectMany(o=>o.GetComponentsInChildren<Camera>(true)).FirstOrDefault(c=>c.CompareTag("MainCamera"));
        if (cardVisuals != null) foreach (var v in cardVisuals) if (v != null && !string.IsNullOrEmpty(v.cardId)) visuals[v.cardId] = v;
    }
    private void OnEnable() { if (revealCoordinator != null && !subscribed) { revealCoordinator.OnRevealChanged += Refresh; subscribed = true; } }
    private void OnDisable() { if (revealCoordinator != null && subscribed) revealCoordinator.OnRevealChanged -= Refresh; subscribed = false; Clear(); }
    private void Start() => Refresh();
    private void Update() { if (displayedId != null && !HasCurrentContent) Clear(); if (referenceControls != null) referenceControls.SetActive(CanToggleReference); }
    private void OnDestroy() { if (referenceControls != null) { if (Application.isPlaying) Destroy(referenceControls); else DestroyImmediate(referenceControls); } }
    private void Clear()
    {
        if (content != null)
        {
            content.SetActive(false);
            if (Application.isPlaying) Destroy(content); else DestroyImmediate(content);
        }
        content = null; displayedId = null; DisplayedParticipantCount = 0; IsReferenceOpen = false;
        if (session == null || session.IsResettingSession || session.CurrentMode != ExperimentManager.ExperimentMode.Proposed || preferenceSession != session.CurrentSessionId || preferenceEpoch != session.SessionEpoch)
        { preferenceSession = preferenceReveal = null; preferredOpen = false; }
        if (referenceControls != null) referenceControls.SetActive(false);
        if (closeControl != null) closeControl.gameObject.SetActive(false);
    }
    private ProposedRevealCoordinator.Snapshot CurrentSnapshot()
    {
        var s = revealCoordinator != null && revealCoordinator.CanDisplayReveal ? revealCoordinator.GetRevealedSnapshot() : null;
        return !ParticipantFinished && s != null && session != null && !session.IsResettingSession && session.CurrentMode == ExperimentManager.ExperimentMode.Proposed &&
            s.sessionId == session.CurrentSessionId && s.epoch == session.SessionEpoch ? s : null;
    }
    public void Refresh()
    {
        if (registry == null || session == null) Awake();
        var snapshot = CurrentSnapshot();
        if (snapshot == null) { Clear(); return; }
        if (displayedId == snapshot.id) return;
        bool open = preferenceSession != snapshot.sessionId || preferenceEpoch != snapshot.epoch || preferenceReveal != snapshot.id || preferredOpen;
        Clear();
        preferenceSession = snapshot.sessionId; preferenceEpoch = snapshot.epoch; preferenceReveal = snapshot.id; preferredOpen = open;
        font = ParticipantUIStyle.Font != null ? ParticipantUIStyle.Font : font;
        if (registry != null && registry.IsLocalParticipant) boardSize = new Vector2(720,1080);
        transform.SetPositionAndRotation(fixedPosition, fixedRotation);
        content = new GameObject("ReadonlyMiniBoards"); content.transform.SetParent(transform, false);
        content.SetActive(false); // All boards become visible together, after construction.
        int count = snapshot.participants.Length, cols = registry != null && registry.IsLocalParticipant ? Mathf.Min(2,count) : Mathf.Min(Mathf.Max(1, columns), count);
        int rows = Mathf.CeilToInt((float)count / cols);
        for (int i = 0; i < count; i++)
        {
            int row = i / cols, col = i % cols, rowCount = Mathf.Min(cols, count - row * cols);
            var p = snapshot.participants[i];
            var board = Rect("P" + p.displayId + "_InitialAnswer", content.transform, Vector2.zero, boardSize);
            board.gameObject.AddComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            board.localScale = Vector3.one * canvasScale;
            board.localPosition = new Vector3((col - (rowCount - 1) * 0.5f) * (boardSize.x + spacing.x) * canvasScale,
                ((rows - 1) * 0.5f - row) * (boardSize.y + spacing.y) * canvasScale, 0);
            BuildBoard(board, p, snapshot.cardIds.Length);
        }
        BuildCriteria(content.transform,cols,rows);
        displayedId = snapshot.id; DisplayedParticipantCount = count;
        content.SetActive(open);
        IsReferenceOpen = open;
        if (registry != null && registry.IsLocalParticipant) { EnsureControls(); if (open) PlaceInView(); else { if (closeControl != null) closeControl.gameObject.SetActive(false); if (toggleLabel != null) toggleLabel.text = "独立回答を表示"; } }

        Debug.Log($"[Readonly Reveal Boards] Displayed {count} Participant boards / {snapshot.id}", this);
    }
    public bool ToggleReference()
    {
        if (!CanToggleReference || content == null) return false;
        IsReferenceOpen = !IsReferenceOpen; preferredOpen = IsReferenceOpen; content.SetActive(IsReferenceOpen);
        if (IsReferenceOpen) PlaceInView();
        if (closeControl != null) closeControl.gameObject.SetActive(IsReferenceOpen);
        if (toggleLabel != null) toggleLabel.text = IsReferenceOpen ? "独立回答を閉じる" : "独立回答を表示";
        return true;
    }
    private void PlaceInView()
    {
        if (xrCamera == null) return;
        Vector3 forward = Vector3.ProjectOnPlane(xrCamera.transform.forward, Vector3.up).normalized;
        if (forward.sqrMagnitude < .01f) forward = Vector3.forward;
        Quaternion rotation = Quaternion.LookRotation(forward,Vector3.up);
        Vector3 right=rotation*Vector3.right;
        float horizontal=referenceOffset.x;
        var board=gameObject.scene.GetRootGameObjects().FirstOrDefault(o=>o.name=="TierBoard");
        var main=board != null ? board.GetComponentsInChildren<Canvas>(true).FirstOrDefault(c=>c.name=="TierBoardCanvas") : null;
        if(main != null)
        {
            var corners=new Vector3[4];((RectTransform)main.transform).GetWorldCorners(corners);
            float edge=float.NegativeInfinity;
            foreach(var corner in corners)
            { var delta=corner-xrCamera.transform.position;float depth=Vector3.Dot(delta,forward);if(depth>.1f)edge=Mathf.Max(edge,Vector3.Dot(delta,right)*referenceDistance/depth); }
            if(!float.IsNegativeInfinity(edge))
                horizontal=Mathf.Max(horizontal,edge + (Mathf.Min(2,DisplayedParticipantCount)*boardSize.x + (Mathf.Min(2,DisplayedParticipantCount)-1)*spacing.x)*canvasScale/2 + .08f);
        }
        Vector3 position=xrCamera.transform.position + forward * referenceDistance + right * horizontal + Vector3.up * referenceOffset.y;
        rotation=Quaternion.LookRotation(Vector3.ProjectOnPlane(position-xrCamera.transform.position,Vector3.up).normalized,Vector3.up);
        transform.SetPositionAndRotation(position,rotation);
        if (closeControl != null) { closeControl.position = transform.position + rotation * new Vector3(0,.70f,0); closeControl.rotation = rotation; closeControl.gameObject.SetActive(true); }
        if (toggleLabel != null) toggleLabel.text = "独立回答を閉じる";
    }
    private void EnsureControls()
    {
        if (referenceControls != null) { referenceControls.SetActive(true); return; }
        var source = gameObject.scene.GetRootGameObjects().SelectMany(o=>o.GetComponentsInChildren<FinalAgreementUI>(true)).SingleOrDefault();
        if (source == null) return;
        referenceControls = new GameObject("IndependentReferenceControls");
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(referenceControls,gameObject.scene);
        var launcher = ControlCanvas("ReferenceLauncher", referenceControls.transform, source);
        launcher.SetPositionAndRotation(source.transform.position + source.transform.up * .55f,source.transform.rotation);
        toggleLabel = ReferenceButton(launcher, "独立回答を閉じる");
        closeControl = ControlCanvas("ReferenceClose",referenceControls.transform,source);
        ReferenceButton(closeControl,"独立回答を閉じる");
    }
    private Transform ControlCanvas(string name, Transform parent, FinalAgreementUI source)
    {
        var rect=Rect(name,parent,Vector2.zero,new Vector2(650,100)); rect.localScale=Vector3.one*.001f;
        var c=rect.gameObject.AddComponent<Canvas>();c.renderMode=RenderMode.WorldSpace;c.worldCamera=xrCamera;
        foreach(var raycaster in source.GetComponents<UnityEngine.EventSystems.BaseRaycaster>()) rect.gameObject.AddComponent(raycaster.GetType());
        return rect;
    }
    private TMP_Text ReferenceButton(Transform parent,string title)
    {
        var rect=Rect("ToggleReference",parent,Vector2.zero,new Vector2(650,100));
        var graphic=rect.gameObject.AddComponent<RoundedPanelGraphic>();graphic.color=new Color(.08f,.24f,.27f);graphic.raycastTarget=true;
        var button=rect.gameObject.AddComponent<UnityEngine.UI.Button>();button.targetGraphic=graphic;button.onClick.AddListener(()=>ToggleReference());
        var text=Label("Label",rect,title,new Vector2(-300,0),new Vector2(600,80),38,Color.white);text.alignment=TextAlignmentOptions.Center;
        return text;
    }
    private void BuildCriteria(Transform parent,int cols,int rows)
    {
        // Outside the existing answer boards: all card rows and photograph sizes stay intact.
        float width=cols*boardSize.x+(cols-1)*spacing.x;
        float height=cols>1?240:310;
        var legend=Rect("TaskCriteria",parent,Vector2.zero,new Vector2(width,height));
        legend.gameObject.AddComponent<Canvas>().renderMode=RenderMode.WorldSpace;
        legend.localScale=Vector3.one*canvasScale;
        legend.localPosition=new Vector3(0,-(rows*boardSize.y+(rows-1)*spacing.y)*canvasScale/2-(height/2+25)*canvasScale,0);
        Panel("Surface",legend,Vector2.zero,legend.sizeDelta,new Color(.035f,.065f,.085f,.97f));
        var label=Label("CriteriaLegend",legend,ParticipantUIStyle.CriteriaLegend(cols>1),new Vector2(-width/2+32,0),new Vector2(width-64,height-24),30,Color.white);
        label.textWrappingMode=TextWrappingModes.Normal;
    }
    private void BuildBoard(RectTransform board, ProposedRevealCoordinator.Participant participant, int cardCount)
    {
        var styles = gameObject.scene.GetRootGameObjects().SelectMany(o=>o.GetComponentsInChildren<CandidateMarkerManager>(true)).FirstOrDefault();
        Color identity = styles != null ? styles.GetParticipantStyle(participant.displayId).color : accent;
        Panel("Border", board, Vector2.zero, boardSize, new Color(identity.r, identity.g, identity.b, .8f));
        Panel("Surface", board, Vector2.zero, boardSize - Vector2.one * 3, new Color(0.035f, 0.065f, 0.085f, 0.94f));
        float left = -boardSize.x / 2 + 32, top = boardSize.y / 2;
        Label("Eyebrow", board, "公開された独立回答", new Vector2(left, top - 40), new Vector2(boardSize.x - 64, 40), 24, accent);
        Label("Header", board, "参加者" + participant.displayId + "の独立回答", new Vector2(left, top - 92), new Vector2(boardSize.x - 64, 72), 46, Color.white);
        Label("Identity", board, (registry != null && registry.Network != null && participant.clientNo == registry.Network.ClientNo ? "あなた ／ " : "") + "閲覧専用",
            new Vector2(left, top - 139), new Vector2(boardSize.x - 64, 40), 25, new Color(0.64f, 0.76f, 0.80f));
        float rowHeight = (boardSize.y - 234) / 5;
        for (int tierIndex = 0; tierIndex < 5; tierIndex++)
        {
            string tier = new[] { "A", "B", "C", "D", "E" }[tierIndex];
            var row = Rect("Tier_" + tier, board, new Vector2(0, top - 190 - rowHeight * (tierIndex + 0.5f)), new Vector2(boardSize.x - 56, rowHeight - 10));
            Panel("RowSurface", row, Vector2.zero, row.sizeDelta, new Color(0.09f, 0.15f, 0.18f, 0.86f));
            Label("TierLabel", row, tier, new Vector2(-row.sizeDelta.x / 2 + 21, 0), new Vector2(48, 72), 46, accent);
            var cards = Rect("Cards", row, new Vector2(38, 0), new Vector2(row.sizeDelta.x - 102, row.sizeDelta.y - 14));
            int inTier = 0;
            int tileColumns = Mathf.Min(4, Mathf.Max(1,cardCount));
            int tileRows = Mathf.Max(2,Mathf.CeilToInt((float)cardCount/tileColumns));
            float tileWidth = (cards.sizeDelta.x - 12 * (tileColumns-1)) / tileColumns;
            float tileHeight = (cards.sizeDelta.y - 12 * (tileRows-1)) / tileRows;
            foreach (var answer in participant.answers)
                if (answer.tier == tier)
                {
                    var tile = BuildCard(cards, answer.cardId, tileWidth, tileHeight);
                    tile.anchoredPosition = new Vector2(-cards.sizeDelta.x / 2 + tileWidth / 2 + (inTier % tileColumns) * (tileWidth + 12),
                        cards.sizeDelta.y / 2 - tileHeight / 2 - (inTier / tileColumns) * (tileHeight + 12));
                    inTier++;
                }
            if (inTier == 0) Label("Empty", row, "—", new Vector2(-row.sizeDelta.x / 2 + 110, 0), new Vector2(80, 50), 30, new Color(0.36f, 0.48f, 0.53f));
        }
        Label("Readonly", board, "閲覧専用 ／ 分類済み " + cardCount + " / " + cardCount + "枚",
            new Vector2(left, -top + 30), new Vector2(boardSize.x - 64, 36), 24, new Color(0.64f, 0.76f, 0.80f));
    }
    private RectTransform BuildCard(RectTransform parent, string cardId, float width, float height)
    {
        var card = Rect(cardId, parent, Vector2.zero, new Vector2(width, height));
        visuals.TryGetValue(cardId, out var visual);
        bool pilot = cardId.StartsWith("A", StringComparison.Ordinal) || cardId.StartsWith("B", StringComparison.Ordinal);
        if (pilot && registry != null && registry.IsLocalParticipant)
        {
            Panel("CardSurface",card,Vector2.zero,card.sizeDelta,new Color(.025f,.045f,.06f));
            float h=Mathf.Min(height-8,(width-8)/(692f/296f));
            var image=Rect("CardImage",card,Vector2.zero,new Vector2(h*692f/296f,h)).gameObject.AddComponent<UnityEngine.UI.RawImage>();
            image.texture=visual != null ? visual.texture : null;image.uvRect=PilotCardFace.PhotographUv;image.raycastTarget=false;
            // Card ID remains the internal object/data key, never participant-facing text.
        }
        else if (pilot)
            PilotCardFace.Populate(card, cardId, visual != null ? visual.displayName : cardId, visual != null ? visual.texture : null, font, true);
        else
        {
            // Preserve the development image content, without the pilot-only photograph crop.
            var image = Rect("CardImage", card, Vector2.zero, new Vector2(height - 8, height - 8)).gameObject.AddComponent<UnityEngine.UI.RawImage>();
            image.texture = visual != null ? visual.texture : null; image.raycastTarget = false;
        }
        return card;
    }
    private RectTransform Panel(string name, Transform parent, Vector2 pos, Vector2 size, Color color)
    {
        var rect = Rect(name, parent, pos, size);
        rect.gameObject.AddComponent<CanvasRenderer>();
        var graphic = rect.gameObject.AddComponent<RoundedPanelGraphic>(); graphic.color = color; graphic.raycastTarget = false;
        return rect;
    }
    private TMP_Text Label(string name, Transform parent, string text, Vector2 pos, Vector2 size, float fontSize, Color color)
    {
        var rect = Rect(name, parent, pos, size);
        rect.pivot = new Vector2(0, 0.5f);
        var label = rect.gameObject.AddComponent<TextMeshProUGUI>(); label.font = font; label.text = text;
        label.fontSize = fontSize; label.enableAutoSizing = false; label.color = color; label.alignment = TextAlignmentOptions.MidlineLeft;
        label.raycastTarget = false; label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Ellipsis;
        return label;
    }
    private static RectTransform Rect(string name, Transform parent, Vector2 pos, Vector2 size)
    {
        var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
        rect.SetParent(parent, false); rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size; rect.anchoredPosition = pos; return rect;
    }
}
