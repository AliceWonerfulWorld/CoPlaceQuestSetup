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

    public void UpdateMarker(
        int participantId,
        string cardId,
        string tierId,
        bool answered)
    {
        string key = $"{participantId}_{cardId}";

        // 未回答・Unclassifiedなら表示しない
        if (!answered || tierId == "Unclassified")
        {
            if (markers.ContainsKey(key))
            {
                Destroy(markers[key]);
                markers.Remove(key);
            }

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
        if (markers.ContainsKey(key))
        {
            markers[key].transform.position = targetPoint.position;
            markers[key].transform.rotation = targetPoint.rotation;
            return;
        }

        GameObject prefab =
            participantId == 1
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
            targetPoint.rotation
        );

        marker.name = $"Marker_P{participantId}_{cardId}";

        markers[key] = marker;
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