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

    [SerializeField]
    private List<TierMarkerPoint> tierMarkerPoints;

    [SerializeField]
    private GameObject participant1MarkerPrefab;

    [SerializeField]
    private GameObject participant2MarkerPrefab;

    private Dictionary<string, GameObject> markers
        = new Dictionary<string, GameObject>();
    private readonly Dictionary<string, int> markerDisplayIds = new Dictionary<string, int>();

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
            (markers[key] == null || !markerDisplayIds.TryGetValue(key, out int oldDisplayId) || oldDisplayId != displayId))
            ClearMarker(participantId, cardId);
        if (markers.ContainsKey(key))
        {
            markers[key].transform.position = targetPoint.position;
            markers[key].transform.rotation = targetPoint.rotation;
            return;
        }

        GameObject prefab =
            displayId == 1
                ? participant1MarkerPrefab
                : participant2MarkerPrefab;

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
    }

    public void ClearMarker(int participantId, string cardId)
    {
        string key = $"{participantId}_{cardId}";
        if (!markers.TryGetValue(key, out var marker)) return;
        if (marker != null) RemoveMarker(marker);
        markers.Remove(key);
        markerDisplayIds.Remove(key);
    }

    public void ClearAllMarkers()
    {
        foreach (var marker in markers.Values)
            if (marker != null) RemoveMarker(marker);
        markers.Clear();
        markerDisplayIds.Clear();
    }

    private void RemoveMarker(GameObject marker)
    {
        marker.SetActive(false);
        if (Application.isPlaying) Destroy(marker);
        else DestroyImmediate(marker);
    }

    private Transform GetMarkerPoint(string tierId)
    {
        foreach (var point in tierMarkerPoints)
        {
            if (point.tierName == tierId)
                return point.markerPoint;
        }

        return null;
    }
}
