#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class PilotCardReadabilitySetup
{
    [MenuItem("Tools/Experiment/Update Pilot Card Readability")]
    public static void Run()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (EditorApplication.isPlaying || scene.path != "Assets/Scenes/QuestSetup.unity") throw new InvalidOperationException("Open QuestSetup outside Play Mode first");
        var sets = UnityEngine.Object.FindFirstObjectByType<PilotCardSetController>();
        if (sets == null) throw new InvalidOperationException("Pilot card sets must be installed first");
        var font = AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>("Assets/PilotCards/PilotJapanese SDF.asset");
        const string backingPath = "Assets/PilotCards/PilotCardBacking.mat";
        var backing = AssetDatabase.LoadAssetAtPath<Material>(backingPath);
        if (backing == null)
        {
            backing = new Material(AssetDatabase.LoadAssetAtPath<Material>("Assets/PilotCards/A01.mat"));
            backing.name = "PilotCardBacking"; backing.mainTexture = null; backing.color = new Color(0.035f, 0.065f, 0.085f, 1);
            AssetDatabase.CreateAsset(backing, backingPath);
        }
        var so = new SerializedObject(sets); so.FindProperty("cardFont").objectReferenceValue = font;
        so.FindProperty("cardBacking").objectReferenceValue = backing; so.ApplyModifiedPropertiesWithoutUndo();
        var view = UnityEngine.Object.FindFirstObjectByType<ReadonlyParticipantBoardView>();
        so = new SerializedObject(view);
        so.FindProperty("boardSize").vector2Value = new Vector2(1600, 1450);
        so.FindProperty("canvasScale").floatValue = 0.001f; so.FindProperty("columns").intValue = 1;
        so.ApplyModifiedPropertiesWithoutUndo();
        var anchor = view.transform.position; anchor.y = 1.7f; view.transform.position = anchor;
        // Keep source PNGs and GUIDs untouched. Avoid NPOT rescaling of the 720x480 originals.
        foreach (string path in Directory.GetFiles("Assets/PilotCards", "*.png"))
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.npotScale = TextureImporterNPOTScale.None; importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear; importer.anisoLevel = 2;
            importer.SaveAndReimport();
        }
        AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        Debug.Log("[Pilot Readability] Configured 54x22 cm cards, bold TMP names, 4x2 slots and enlarged read-only answer boards. Quest legibility still requires hardware validation.");
    }
}
#endif
