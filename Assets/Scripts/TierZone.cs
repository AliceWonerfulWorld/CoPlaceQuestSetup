using UnityEngine;
using System.Collections.Generic;

public class TierZone : MonoBehaviour
{
    public string tierName;
    public List<Transform> snapPoints = new List<Transform>();

    private Dictionary<Transform, GameObject> occupiedPoints = new Dictionary<Transform, GameObject>();

    private void Awake()
    {
        foreach (Transform point in snapPoints)
        {
            occupiedPoints[point] = null;
        }
    }

    public Transform GetAvailableSnapPoint(GameObject card)
    {
        // すでにこのTier内で割当て済みなら、その場所を探す
        foreach (var pair in occupiedPoints)
        {
            if (pair.Value == card)
               return pair.Key;
        }

        // 空いている場所を探す
        foreach (Transform point in snapPoints)
        {
            if (occupiedPoints[point] == null)
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
        {
            if (pair.Value == card)
            {
                target = pair.Key;
                break;
            }
        }

        if (target != null)
        {
            occupiedPoints[target] = null;
        }
    }
}
