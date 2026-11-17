using UnityEngine;

// The sole geometric decision for both release and local hover feedback.
public static class CardDropTargetResolver
{
    public const float BoundaryHysteresis = 0.015f;
    public static TierZone Resolve(TierZone[] zones, GameObject card, Vector3 position, TierZone previous = null)
    {
        if (zones == null) return null;
        TierZone best = null; float distance = float.PositiveInfinity; bool previousInBoard = false;
        foreach (var zone in zones)
        {
            if (zone == null) continue;
            if (zone == previous) previousInBoard = true;
            if (!zone.ContainsDropPoint(position) || (card != null && zone.PeekAvailableSnapPoint(card) == null)) continue;
            float d = zone.VerticalDistance(position);
            if (d < distance - 0.000001f || (Mathf.Abs(d - distance) <= 0.000001f && best != null && string.CompareOrdinal(zone.tierName, best.tierName) < 0))
            { best = zone; distance = d; }
        }
        // Retain a previous target only while a valid current region exists. Never attract an outside drop.
        if (best != null && previousInBoard && previous != best && previous.ContainsDropPoint(position, BoundaryHysteresis) &&
            (card == null || previous.PeekAvailableSnapPoint(card) != null) && previous.VerticalDistance(position) <= distance + BoundaryHysteresis * 2)
            return previous;
        return best;
    }
}
