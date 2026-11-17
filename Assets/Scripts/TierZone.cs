using UnityEngine;
using System.Collections.Generic;

public class TierZone : MonoBehaviour
{
    public string tierName;
    public List<Transform> snapPoints = new List<Transform>();
    [Tooltip("Optional stable card IDs, in SnapPoint order. Keeps shared layouts consistent across clients.")]
    [SerializeField] private List<string> snapCardIds = new List<string>();
    [SerializeField] private UnityEngine.UI.Image dropHighlight;

    private readonly Dictionary<Transform, GameObject> occupiedPoints = new Dictionary<Transform, GameObject>();
    [SerializeField] private RectTransform dropArea;
    [SerializeField, Min(0)] private float frontDropDistance = 0.8f;
    [SerializeField, Min(0)] private float backDropDistance = 0.12f;
    [SerializeField, Min(0)] private float verticalMargin = 0.02f;
    private static readonly Dictionary<Transform, TierZone> activeFeedback = new Dictionary<Transform, TierZone>();
    private GameObject hoveringCard, preview;
    public bool IsHighlighted => dropHighlight != null && dropHighlight.enabled;
    public bool IsPreviewVisible => preview != null && preview.activeSelf;
    public Vector3 PreviewPosition => preview != null ? preview.transform.position : Vector3.zero;
    public RectTransform DropArea => dropArea;
    private BoxCollider dropCollider;
    private readonly Vector3[] areaCorners = new Vector3[4];
    private float WorldVerticalMargin => verticalMargin * Mathf.Abs((transform.parent != null ? transform.parent : transform).lossyScale.y);

    private void Awake()
    {
        dropCollider = GetComponent<BoxCollider>();
        RefreshDropCollider();
        foreach (Transform point in snapPoints)
            if (point != null) occupiedPoints[point] = null;
        if (dropHighlight != null) dropHighlight.enabled = false;
    }

    // Refresh on Reset after PilotCardSetController has normalized the private board scale.
    public void RefreshDropCollider()
    {
        if (dropArea == null) return;
        if (dropCollider == null) dropCollider = GetComponent<BoxCollider>();
        if (dropCollider == null) return;
        dropArea.GetWorldCorners(areaCorners);
        Vector3 min = transform.InverseTransformPoint(areaCorners[0]), max = min;
        foreach (var corner in areaCorners) { var point = transform.InverseTransformPoint(corner); min = Vector3.Min(min, point); max = Vector3.Max(max, point); }
        var scale = transform.lossyScale;
        Vector3 center = (min + max) * 0.5f;
        center.z += (backDropDistance - frontDropDistance) * 0.5f / Mathf.Abs(scale.z);
        dropCollider.center = center;
        dropCollider.size = new Vector3(max.x - min.x, max.y - min.y + WorldVerticalMargin * 2 / Mathf.Abs(scale.y), (frontDropDistance + backDropDistance) / Mathf.Abs(scale.z));
        dropCollider.isTrigger = true;
    }

    public bool ContainsDropPoint(Vector3 position, float extraVerticalMargin = 0)
    {
        if (dropCollider == null) dropCollider = GetComponent<BoxCollider>();
        if (dropCollider == null || !dropCollider.enabled || !isActiveAndEnabled) return false;
        if (dropArea != null)
        {
            if (!dropArea.gameObject.activeInHierarchy) return false;
            Vector3 local = dropArea.InverseTransformPoint(position);
            float yMargin = (WorldVerticalMargin + extraVerticalMargin) / Mathf.Abs(dropArea.lossyScale.y);
            float depth = Vector3.Dot(position - dropArea.position, dropArea.forward);
            var rect = dropArea.rect;
            return local.x >= rect.xMin && local.x <= rect.xMax && local.y >= rect.yMin - yMargin && local.y <= rect.yMax + yMargin &&
                depth >= -frontDropDistance && depth <= backDropDistance;
        }
        // Compatibility for independently constructed legacy/test zones.
        Vector3 point = transform.InverseTransformPoint(position) - dropCollider.center;
        Vector3 half = dropCollider.size * 0.5f;
        return Mathf.Abs(point.x) <= half.x && Mathf.Abs(point.y) <= half.y && Mathf.Abs(point.z) <= half.z;
    }
    public float VerticalDistance(Vector3 position) => dropArea != null
        ? Mathf.Abs(Vector3.Dot(position - dropArea.TransformPoint(dropArea.rect.center), dropArea.up))
        : Mathf.Abs(Vector3.Dot(position - transform.position, transform.up));
    public void SetHovered(GameObject card, bool hovered)
    {
        if (!hovered) { if (hoveringCard == card) ClearFeedback(); return; }
        var slot = PeekAvailableSnapPoint(card);
        if (card == null || !card.activeInHierarchy || slot == null) { if (hoveringCard == card) ClearFeedback(); return; }
        Transform board = transform.parent != null ? transform.parent : transform;
        if (activeFeedback.TryGetValue(board, out var other) && other != null)
        {
            // Two local hands can hold cards: one feedback owner per board, until that drag ends.
            if (other.hoveringCard != card) return;
            if (other != this) other.ClearFeedback();
        }
        activeFeedback[board] = this; hoveringCard = card;
        if (dropHighlight != null) dropHighlight.enabled = true;
        ShowPreview(card, slot);
    }
    public void ClearFeedback()
    {
        Transform board = transform.parent != null ? transform.parent : transform;
        if (activeFeedback.TryGetValue(board, out var active) && active == this) activeFeedback.Remove(board);
        hoveringCard = null;
        if (dropHighlight != null) dropHighlight.enabled = false;
        if (preview != null) preview.SetActive(false);
    }
    private void OnDisable() => ClearFeedback();
    private void LateUpdate() { if ((hoveringCard == null || !hoveringCard.activeInHierarchy) && (IsHighlighted || IsPreviewVisible)) ClearFeedback(); }
    private void ShowPreview(GameObject card, Transform slot)
    {
        var collider = card.GetComponent<BoxCollider>();
        if (collider == null) return;
        Vector3 size = Vector3.Scale(collider.size, card.transform.lossyScale);
        if (preview == null)
        {
            preview = new GameObject("DropSlotPreview", typeof(RectTransform), typeof(Canvas));
            preview.transform.SetParent(transform, false);
            preview.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            for (int i = 0; i < 4; i++)
            {
                var edge = (RectTransform)new GameObject("Edge" + i, typeof(RectTransform), typeof(UnityEngine.UI.Image)).transform;
                edge.SetParent(preview.transform, false);
                var image = edge.GetComponent<UnityEngine.UI.Image>(); image.raycastTarget = false; image.color = new Color(0.35f, 1f, 0.88f, 0.95f);
            }
        }
        // Exact snap center and rotation; offset towards the viewer only to avoid z-fighting.
        preview.transform.SetPositionAndRotation(slot.position - slot.forward * (Mathf.Abs(size.z) * 0.5f + 0.004f), slot.rotation);
        var scale = transform.lossyScale;
        preview.transform.localScale = new Vector3(0.001f / Mathf.Abs(scale.x), 0.001f / Mathf.Abs(scale.y), 0.001f / Mathf.Abs(scale.z));
        float w = Mathf.Abs(size.x) * 1000, h = Mathf.Abs(size.y) * 1000;
        var rect = (RectTransform)preview.transform; rect.sizeDelta = new Vector2(w, h);
        for (int i = 0; i < 4; i++)
        {
            var edge = (RectTransform)rect.GetChild(i);
            edge.sizeDelta = i < 2 ? new Vector2(w, 3) : new Vector2(3, h);
            edge.anchoredPosition = i < 2 ? new Vector2(0, (i == 0 ? 1 : -1) * (h - 3) * 0.5f) : new Vector2((i == 2 ? 1 : -1) * (w - 3) * 0.5f, 0);
        }
        preview.SetActive(true);
    }

    public Transform GetAvailableSnapPoint(GameObject card)
    {
        var point = PeekAvailableSnapPoint(card);
        if (point != null) occupiedPoints[point] = card;
        return point;
    }
    // Hover/preview must not reserve slots or change final placement state.
    public Transform PeekAvailableSnapPoint(GameObject card)
    {
        foreach (var pair in occupiedPoints)
            if (pair.Value == card && pair.Key != null) return pair.Key;

        int preferred = snapCardIds.IndexOf(card.name);
        if (preferred >= 0 && preferred < snapPoints.Count)
        {
            Transform point = snapPoints[preferred];
            if (point != null && (!occupiedPoints.TryGetValue(point, out var occupant) || occupant == null))
            {
                return point;
            }
            // Do not assign a different slot to a known card on different clients.
            return null;
        }
        foreach (Transform point in snapPoints)
        {
            if (point != null && (!occupiedPoints.TryGetValue(point, out var occupant) || occupant == null))
            {
                return point;
            }
        }
        return null;
    }

    public void ConfigureCardSlots(string[] ids)
    {
        ClearFeedback(); RefreshDropCollider(); occupiedPoints.Clear(); snapCardIds = new List<string>(ids);
        for (int i = 0; i < snapPoints.Count; i++)
        {
            var point = snapPoints[i]; if (point == null) continue;
            var pos = point.localPosition;
            pos.x = ids.Length == 3 ? -0.23f + i * 0.2666667f : (-0.73f + (i % 4) * 0.58f) / transform.localScale.x;
            // The first eight slots are a stable 4x2 grid in every Tier, including Unclassified.
            pos.y = ids.Length == 3 ? 0 : (i < 4 ? 0.115f : -0.115f) / transform.localScale.y;
            point.localPosition = pos; occupiedPoints[point] = null;
        }
        if (dropHighlight != null) dropHighlight.enabled = false;
    }

    public void ReleaseCard(GameObject card)
    {
        Transform target = null;
        foreach (var pair in occupiedPoints)
            if (pair.Value == card) { target = pair.Key; break; }
        if (target != null) occupiedPoints[target] = null;
        SetHovered(card, false);
    }
}
