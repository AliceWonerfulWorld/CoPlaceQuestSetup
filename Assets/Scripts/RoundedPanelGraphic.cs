using UnityEngine;

// A lightweight uGUI rounded rectangle; no texture, material or per-frame work.
[ExecuteAlways]
[RequireComponent(typeof(CanvasRenderer))]
public class RoundedPanelGraphic : UnityEngine.UI.MaskableGraphic
{
    [SerializeField, Min(0)] private float radius = 18;
    protected RoundedPanelGraphic() { useLegacyMeshGeneration = false; }
    protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper mesh)
    {
        mesh.Clear();
        Rect r = rectTransform.rect;
        float corner = Mathf.Min(radius, Mathf.Min(r.width, r.height) * 0.5f);
        mesh.AddVert(r.center, color, Vector2.zero);
        const int steps = 8;
        for (int quadrant = 0; quadrant < 4; quadrant++)
        {
            var center = new Vector2(quadrant == 0 || quadrant == 3 ? r.xMax - corner : r.xMin + corner,
                quadrant < 2 ? r.yMax - corner : r.yMin + corner);
            for (int i = 0; i <= steps; i++)
            {
                float angle = (quadrant * 90 + i * 90f / steps) * Mathf.Deg2Rad;
                mesh.AddVert(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * corner, color, Vector2.zero);
            }
        }
        int count = 4 * (steps + 1);
        for (int i = 0; i < count; i++) mesh.AddTriangle(0, (i + 1) % count + 1, i + 1);
    }
}
