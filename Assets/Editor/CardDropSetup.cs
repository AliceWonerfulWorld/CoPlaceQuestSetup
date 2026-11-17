#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class CardDropSetup
{
    [MenuItem("Tools/Experiment/Update Card Drop Targets")]
    public static void Run()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (EditorApplication.isPlaying || scene.path != "Assets/Scenes/QuestSetup.unity") throw new InvalidOperationException("Open QuestSetup outside Play Mode first");
        foreach (var zone in UnityEngine.Object.FindObjectsByType<TierZone>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var canvas = zone.transform.parent.GetComponentsInChildren<Canvas>(true).Single(c => c.name == "TierBoardCanvas");
            string rowName = zone.tierName == "Unclassified" ? "UnclassifiedRow" : "Tier" + zone.tierName + "Row";
            var row = canvas.GetComponentsInChildren<RectTransform>(true).Single(r => r.name == rowName);
            // Legacy visuals kept their 600px children after the rows were reduced to 460px.
            foreach (var name in new[] { "Outline", "Surface", "Interior", "DropHighlight" })
            {
                var rect = (RectTransform)row.Find(name);
                if (rect == null) throw new InvalidOperationException("Missing row graphic: " + rowName + "/" + name);
                float inset = name == "Outline" ? 0 : name == "Surface" ? 1.5f : name == "Interior" ? 4 : 6;
                rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.one * inset; rect.offsetMax = Vector2.one * -inset;
            }
            var badge = row.Find("TierBadge") as RectTransform;
            if (badge != null) badge.sizeDelta = new Vector2(badge.sizeDelta.x, Mathf.Min(badge.sizeDelta.y, row.rect.height - 32));
            var highlight = row.Find("DropHighlight").GetComponent<UnityEngine.UI.Image>();
            highlight.sprite = null; highlight.type = UnityEngine.UI.Image.Type.Simple;
            highlight.color = new Color(0.22f, 0.95f, 0.78f, 0.22f); highlight.raycastTarget = false; highlight.enabled = false;
            highlight.transform.SetSiblingIndex(row.Find("Interior").GetSiblingIndex() + 1);
            var so = new SerializedObject(zone);
            so.FindProperty("dropArea").objectReferenceValue = row;
            so.FindProperty("dropHighlight").objectReferenceValue = highlight;
            so.FindProperty("frontDropDistance").floatValue = 0.8f;
            so.FindProperty("backDropDistance").floatValue = 0.12f;
            so.FindProperty("verticalMargin").floatValue = 0.02f;
            so.ApplyModifiedPropertiesWithoutUndo();
            if (Mathf.Abs(Vector3.Dot(row.forward, zone.transform.forward)) < 0.999f) throw new InvalidOperationException("Tier and row planes must be parallel");
            zone.RefreshDropCollider();
            var collider = zone.GetComponent<BoxCollider>();
            EditorUtility.SetDirty(zone); EditorUtility.SetDirty(collider);
        }
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        Debug.Log("[Card Drop Setup] 12 visual row references/colliders matched; Tier E has its own highlight; 80cm front / 12cm back / 2cm row margins. Local snap previews enabled.");
    }
}
#endif
