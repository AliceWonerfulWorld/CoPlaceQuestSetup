using TMPro;
using UnityEngine;

// Presentation only: no colliders, raycasters or network state.
public sealed class PilotCardFace : MonoBehaviour
{
    public const float Width = 0.54f, Height = 0.22f;
    // Source PNGs are 720x480. Show the photograph, excluding the baked-in ID/name/footer.
    public static readonly Rect PhotographUv = new Rect(14f / 720f, 170f / 480f, 692f / 720f, 296f / 480f);
    private GameObject face;
    public Texture SourceTexture { get; private set; }
    public TMP_Text EventName { get; private set; }
    public void Configure(string id, string displayName, Texture texture, TMP_FontAsset font)
    {
        SourceTexture = texture;
        if (face != null) { face.SetActive(false); if (Application.isPlaying) Destroy(face); else DestroyImmediate(face); }
        var rect = Rect("PilotFace", transform, Vector2.zero, new Vector2(540, 220)); face = rect.gameObject;
        rect.gameObject.AddComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        rect.localPosition = new Vector3(0, 0, -0.52f);
        var scale = transform.lossyScale;
        rect.localScale = new Vector3(0.001f / Mathf.Abs(scale.x), 0.001f / Mathf.Abs(scale.y), 0.001f / Mathf.Abs(scale.z));
        EventName = Populate(rect, id, displayName, texture, font, false);
        face.SetActive(GetComponent<Renderer>().enabled);
    }
    public void SetVisible(bool visible) { if (face != null) face.SetActive(enabled && visible); }
    private void LateUpdate() => SetVisible(GetComponent<Renderer>().enabled);
    public static TMP_Text Populate(RectTransform parent, string id, string name, Texture texture, TMP_FontAsset font, bool compact)
    {
        Vector2 size = parent.sizeDelta;
        var background = Rect("CardSurface", parent, Vector2.zero, size).gameObject.AddComponent<UnityEngine.UI.Image>();
        background.color = new Color(0.035f, 0.065f, 0.085f, 1); background.raycastTarget = false;
        float photoHeight = compact ? 30 : 107;
        float photoWidth = photoHeight * (692f / 296f);
        var photo = Rect("CardImage", parent, compact ? new Vector2(-size.x / 2 + 44, 21) : new Vector2(0, 44), new Vector2(photoWidth, photoHeight));
        var image = photo.gameObject.AddComponent<UnityEngine.UI.RawImage>(); image.texture = texture; image.uvRect = PhotographUv; image.raycastTarget = false;
        var title = Text("CardName", parent, name, font,
            compact ? new Vector2(43, 0) : new Vector2(0, -62),
            compact ? new Vector2(size.x - 100, size.y - 8) : new Vector2(510, 88), compact ? 30 : 42);
        title.fontStyle = FontStyles.Bold; title.color = Color.white;
        title.textWrappingMode = TextWrappingModes.Normal;
        var cardId = Text("CardId", parent, id, font,
            compact ? new Vector2(-size.x / 2 + 44, -25) : new Vector2(-218, 65), compact ? new Vector2(78, 30) : new Vector2(90, 36), compact ? 18 : 22);
        cardId.color = new Color(0.69f, 0.82f, 0.87f, 1);
        return title;
    }
    private static TMP_Text Text(string name, Transform parent, string value, TMP_FontAsset font, Vector2 position, Vector2 size, float pointSize)
    {
        var text = Rect(name, parent, position, size).gameObject.AddComponent<TextMeshProUGUI>();
        text.font = font; text.text = value; text.fontSize = pointSize; text.enableAutoSizing = false;
        text.alignment = TextAlignmentOptions.Center; text.raycastTarget = false;
        text.overflowMode = TextOverflowModes.Truncate;
        return text;
    }
    private static RectTransform Rect(string name, Transform parent, Vector2 position, Vector2 size)
    {
        var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
        rect.SetParent(parent, false); rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size; rect.anchoredPosition = position; return rect;
    }
}
