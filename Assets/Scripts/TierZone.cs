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
    private readonly HashSet<GameObject> hoveringCards = new HashSet<GameObject>();
    private BoxCollider dropCollider;

    private void Awake()
    {
        dropCollider = GetComponent<BoxCollider>();
        foreach (Transform point in snapPoints)
            if (point != null) occupiedPoints[point] = null;
        if (dropHighlight != null) dropHighlight.enabled = false;
    }

    public bool ContainsDropPoint(Vector3 position)
    {
        if (dropCollider == null) dropCollider = GetComponent<BoxCollider>();
        if (dropCollider == null || !dropCollider.enabled || !isActiveAndEnabled) return false;
        Vector3 point = transform.InverseTransformPoint(position) - dropCollider.center;
        Vector3 half = dropCollider.size * 0.5f;
        return Mathf.Abs(point.x) <= half.x && Mathf.Abs(point.y) <= half.y && Mathf.Abs(point.z) <= half.z;
    }

    public void SetHovered(GameObject card, bool hovered)
    {
        if (hovered) hoveringCards.Add(card);
        else hoveringCards.Remove(card);
        if (dropHighlight != null) dropHighlight.enabled = hoveringCards.Count > 0;
    }

    public Transform GetAvailableSnapPoint(GameObject card)
    {
        foreach (var pair in occupiedPoints)
            if (pair.Value == card && pair.Key != null) return pair.Key;

        int preferred = snapCardIds.IndexOf(card.name);
        if (preferred >= 0 && preferred < snapPoints.Count)
        {
            Transform point = snapPoints[preferred];
            if (point != null && (!occupiedPoints.TryGetValue(point, out var occupant) || occupant == null))
            {
                occupiedPoints[point] = card;
                return point;
            }
            // Do not assign a different slot to a known card on different clients.
            return null;
        }
        foreach (Transform point in snapPoints)
        {
            if (point != null && (!occupiedPoints.TryGetValue(point, out var occupant) || occupant == null))
            {
                occupiedPoints[point] = card;
                return point;
            }
        }
        return null;
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
