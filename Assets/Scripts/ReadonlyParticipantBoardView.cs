using System;
using System.Collections.Generic;
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
    private readonly Dictionary<string, CardVisual> visuals = new Dictionary<string, CardVisual>();
    private GameObject content;
    private string displayedId;
    public int DisplayedParticipantCount { get; private set; }
    public string DisplayedRevealId => displayedId;
    private void Awake()
    {
        if (cardVisuals != null) foreach (var v in cardVisuals) if (v != null && !string.IsNullOrEmpty(v.cardId)) visuals[v.cardId] = v;
    }
    private void OnEnable() { if (revealCoordinator != null) revealCoordinator.OnRevealChanged += Refresh; }
    private void OnDisable() { if (revealCoordinator != null) revealCoordinator.OnRevealChanged -= Refresh; Clear(); }
    private void Start() => Refresh();
    private void Clear()
    {
        if (content != null)
        {
            content.SetActive(false);
            if (Application.isPlaying) Destroy(content); else DestroyImmediate(content);
        }
        content = null; displayedId = null; DisplayedParticipantCount = 0;
    }
    public void Refresh()
    {
        var snapshot = revealCoordinator != null && revealCoordinator.CanDisplayReveal ? revealCoordinator.GetRevealedSnapshot() : null;
        if (snapshot == null) { Clear(); return; }
        if (displayedId == snapshot.id) return;
        Clear();
        content = new GameObject("ReadonlyMiniBoards"); content.transform.SetParent(transform, false);
        content.SetActive(false); // All boards become visible together, after construction.
        int count = snapshot.participants.Length, cols = Mathf.Min(Mathf.Max(1, columns), count);
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
        displayedId = snapshot.id; DisplayedParticipantCount = count;
        content.SetActive(true);
        Debug.Log($"[Readonly Reveal Boards] Displayed {count} Participant boards / {snapshot.id}", this);
    }
    private void BuildBoard(RectTransform board, ProposedRevealCoordinator.Participant participant, int cardCount)
    {
        Panel("Border", board, Vector2.zero, boardSize, new Color(accent.r, accent.g, accent.b, 0.38f));
        Panel("Surface", board, Vector2.zero, boardSize - Vector2.one * 3, new Color(0.035f, 0.065f, 0.085f, 0.94f));
        float left = -boardSize.x / 2 + 32, top = boardSize.y / 2;
        Label("Eyebrow", board, "INDEPENDENT ANSWER  /  REVEALED", new Vector2(left, top - 40), new Vector2(boardSize.x - 64, 32), 24, accent);
        Label("Header", board, "P" + participant.displayId + "  INITIAL ANSWER", new Vector2(left, top - 92), new Vector2(boardSize.x - 64, 58), 46, Color.white);
        Label("Identity", board, "Participant " + participant.displayId + "  ·  Client " + participant.clientNo,
            new Vector2(left, top - 139), new Vector2(boardSize.x - 64, 32), 25, new Color(0.64f, 0.76f, 0.80f));
        float rowHeight = (boardSize.y - 234) / 4;
        for (int tierIndex = 0; tierIndex < 4; tierIndex++)
        {
            string tier = new[] { "A", "B", "C", "D" }[tierIndex];
            var row = Rect("Tier_" + tier, board, new Vector2(0, top - 190 - rowHeight * (tierIndex + 0.5f)), new Vector2(boardSize.x - 56, rowHeight - 10));
            Panel("RowSurface", row, Vector2.zero, row.sizeDelta, new Color(0.09f, 0.15f, 0.18f, 0.86f));
            Label("TierLabel", row, tier, new Vector2(-row.sizeDelta.x / 2 + 21, 0), new Vector2(48, 64), 46, accent);
            var cards = Rect("Cards", row, new Vector2(38, 0), new Vector2(row.sizeDelta.x - 102, row.sizeDelta.y - 14));
            var layout = cards.gameObject.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
            layout.spacing = 12; layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlHeight = layout.childControlWidth = false;
            layout.childForceExpandHeight = layout.childForceExpandWidth = false;
            int inTier = 0;
            float tileWidth = Mathf.Min(162, (cards.sizeDelta.x - 12 * (cardCount - 1)) / Mathf.Max(1, cardCount));
            foreach (var answer in participant.answers)
                if (answer.tier == tier) { BuildCard(cards, answer.cardId, tileWidth, cards.sizeDelta.y); inTier++; }
            if (inTier == 0) Label("Empty", row, "—", new Vector2(-row.sizeDelta.x / 2 + 110, 0), new Vector2(80, 40), 30, new Color(0.36f, 0.48f, 0.53f));
        }
        Label("Readonly", board, "READ ONLY  ·  " + cardCount + " / " + cardCount + " ANSWERED",
            new Vector2(left, -top + 30), new Vector2(boardSize.x - 64, 36), 24, new Color(0.64f, 0.76f, 0.80f));
    }
    private void BuildCard(RectTransform parent, string cardId, float width, float height)
    {
        var card = Rect(cardId, parent, Vector2.zero, new Vector2(width, height));
        Panel("CardSurface", card, Vector2.zero, card.sizeDelta, new Color(0.14f, 0.23f, 0.26f, 1));
        visuals.TryGetValue(cardId, out var visual);
        var imageRect = Rect("CardImage", card, new Vector2(0, 16), new Vector2(width - 18, height - 47));
        var image = imageRect.gameObject.AddComponent<UnityEngine.UI.RawImage>();
        image.texture = visual != null ? visual.texture : null; image.raycastTarget = false;
        image.color = image.texture != null ? Color.white : new Color(0.22f, 0.34f, 0.38f);
        if (image.texture != null)
        {
            float ratio = (float)image.texture.width / image.texture.height;
            float imageHeight = Mathf.Min(imageRect.sizeDelta.y, imageRect.sizeDelta.x / ratio);
            imageRect.sizeDelta = new Vector2(imageHeight * ratio, imageHeight);
        }
        string name = visual != null && !string.IsNullOrEmpty(visual.displayName) ? visual.displayName : cardId;
        var label = Label("CardName", card, name, new Vector2(-width / 2 + 6, -height / 2 + 20), new Vector2(width - 12, 32), 25, Color.white);
        label.alignment = TextAlignmentOptions.Center;
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
        label.fontSize = fontSize; label.color = color; label.alignment = TextAlignmentOptions.MidlineLeft;
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
