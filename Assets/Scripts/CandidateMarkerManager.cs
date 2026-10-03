using UnityEngine;
using System.Collections.Generic;

public class CandidateMarkerManager : MonoBehaviour
{
    [System.Serializable]
    public class TierMarkerPoint
    {
        public string tierName;
        public Transform markerPoint;
    }

    [System.Serializable]
    public class CardImageBinding
    {
        public string cardId;
        public Texture cardImage;
    }
    [System.Serializable]
    public class ParticipantStyle
    {
        public string label;
        public Color color = new Color(0.28f, 0.65f, 1f);
    }

    [Header("Participant Styles (registration order)")]
    [SerializeField] private List<ParticipantStyle> participantStyles = new List<ParticipantStyle>
    {
        new ParticipantStyle { label = "P1", color = new Color(0.28f, 0.65f, 1f) },
        new ParticipantStyle { label = "P2", color = new Color(1f, 0.60f, 0.30f) }
    };
    [Header("Candidate Card Presentation")]
    [SerializeField] private List<CardImageBinding> cardImages = new List<CardImageBinding>();
    [SerializeField, Min(1)] private int columns = 4;
    [SerializeField] private Vector2 cardWorldSize = new Vector2(0.344f, 0.272f);
    [SerializeField] private Vector2 cardSpacing = new Vector2(0.036f, 0.016f);

    [SerializeField]
    private List<TierMarkerPoint> tierMarkerPoints;

    [SerializeField]
    private GameObject candidateCardPrefab;

    private Dictionary<string, GameObject> markers
        = new Dictionary<string, GameObject>();
    private readonly Dictionary<string, int> markerDisplayIds = new Dictionary<string, int>();
    private readonly Dictionary<string, string> markerTiers = new Dictionary<string, string>();

    public void UpdateMarker(
        int participantId,
        string cardId,
        string tierId,
        bool answered,
        int participantDisplayId = 0)
    {
        string key = $"{participantId}_{cardId}";
        int displayId = participantDisplayId > 0 ? participantDisplayId : participantId;

        // 未回答・Unclassifiedなら表示しない
        if (!answered || tierId == "Unclassified")
        {
            ClearMarker(participantId, cardId);

            return;
        }

        Transform targetPoint = GetMarkerPoint(tierId);

        if (targetPoint == null)
        {
            Debug.LogWarning(
                $"[Marker] Tier {tierId} のMarkerPointが見つかりません"
            );
            return;
        }

        // 既存マーカーがあれば移動
        if (markers.ContainsKey(key) &&
            markers[key] == null)
            ClearMarker(participantId, cardId);
        if (markers.ContainsKey(key))
        {
            string oldTier = markerTiers[key];
            markerTiers[key] = tierId;
            markerDisplayIds[key] = displayId;
            SetCardContent(markers[key], displayId, cardId);
            if (oldTier != tierId) LayoutTier(oldTier);
            LayoutTier(tierId);
            return;
        }

        GameObject prefab = candidateCardPrefab;

        if (prefab == null)
        {
            Debug.LogWarning(
                $"[Marker] Participant {participantId} のPrefabがありません"
            );
            return;
        }

        GameObject marker = Instantiate(
            prefab,
            targetPoint.position,
            targetPoint.rotation,
            transform
        );

        marker.name = $"Marker_P{participantId}_{cardId}";

        markers[key] = marker;
        markerDisplayIds[key] = displayId;
        markerTiers[key] = tierId;
        SetCardContent(marker, displayId, cardId);
        LayoutTier(tierId);
    }

    public void ClearMarker(int participantId, string cardId)
    {
        string key = $"{participantId}_{cardId}";
        if (!markers.TryGetValue(key, out var marker)) return;
        markerTiers.TryGetValue(key, out string oldTier);
        if (marker != null) RemoveMarker(marker);
        markers.Remove(key);
        markerDisplayIds.Remove(key);
        markerTiers.Remove(key);
        LayoutTier(oldTier);
    }

    public void ClearAllMarkers()
    {
        foreach (var marker in markers.Values)
            if (marker != null) RemoveMarker(marker);
        markers.Clear();
        markerDisplayIds.Clear();
        markerTiers.Clear();
    }

    private void RemoveMarker(GameObject marker)
    {
        marker.SetActive(false);
        if (Application.isPlaying) Destroy(marker);
        else DestroyImmediate(marker);
    }

    private Transform GetMarkerPoint(string tierId)
    {
        if (tierMarkerPoints == null) return null;
        foreach (var point in tierMarkerPoints)
        {
            if (point.tierName == tierId)
                return point.markerPoint;
        }

        return null;
    }

    private void SetCardContent(GameObject marker, int displayId, string cardId)
    {
        var view = marker.GetComponent<CandidateCardView>();
        if (view == null) return;
        Texture image = null;
        foreach (var binding in cardImages)
            if (binding.cardId == cardId) { image = binding.cardImage; break; }
        var style = GetParticipantStyle(displayId);
        view.SetContent(style.label, style.color, cardId, image);
    }

    // Extra participants keep their own label and safely reuse the configured palette.
    public ParticipantStyle GetParticipantStyle(int displayId)
    {
        int index = Mathf.Max(0, displayId - 1);
        ParticipantStyle configured = participantStyles != null && index < participantStyles.Count
            ? participantStyles[index] : null;
        ParticipantStyle palette = participantStyles != null && participantStyles.Count > 0
            ? participantStyles[index % participantStyles.Count] : null;
        return new ParticipantStyle
        {
            label = configured != null && !string.IsNullOrWhiteSpace(configured.label)
                ? configured.label : $"P{Mathf.Max(1, displayId)}",
            color = configured != null ? configured.color : palette != null ? palette.color : new Color(0.28f, 0.65f, 1f)
        };
    }

    private void LayoutTier(string tierId)
    {
        Transform origin = GetMarkerPoint(tierId);
        if (origin == null) return;
        var keys = new List<string>();
        foreach (var entry in markerTiers)
            if (entry.Value == tierId && markers.TryGetValue(entry.Key, out var marker) && marker != null) keys.Add(entry.Key);
        keys.Sort((left, right) =>
        {
            int cardOrder = string.CompareOrdinal(left.Substring(left.IndexOf('_') + 1), right.Substring(right.IndexOf('_') + 1));
            if (cardOrder != 0) return cardOrder;
            int participantOrder = markerDisplayIds[left].CompareTo(markerDisplayIds[right]);
            return participantOrder != 0 ? participantOrder : string.CompareOrdinal(left, right);
        });
        int columnCount = Mathf.Max(1, columns);
        int rows = Mathf.CeilToInt((float)keys.Count / columnCount);
        for (int i = 0; i < keys.Count; i++)
        {
            int row = i / columnCount;
            int rowCount = Mathf.Min(columnCount, keys.Count - row * columnCount);
            float x = (i % columnCount - (rowCount - 1) * 0.5f) * (cardWorldSize.x + cardSpacing.x);
            float y = ((rows - 1) * 0.5f - row) * (cardWorldSize.y + cardSpacing.y);
            markers[keys[i]].transform.SetPositionAndRotation(origin.position + origin.rotation * new Vector3(x, y, 0), origin.rotation);
        }
    }
}
