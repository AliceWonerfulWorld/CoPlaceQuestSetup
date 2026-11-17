#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class PilotCardSetSetup
{
    [MenuItem("Tools/Experiment/Install Pilot Card Sets")]
    public static void Install()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (EditorApplication.isPlaying || scene.path != "Assets/Scenes/QuestSetup.unity")
            throw new InvalidOperationException("Open QuestSetup outside Play Mode first.");
        if (UnityEngine.Object.FindFirstObjectByType<PilotCardSetController>() != null)
            throw new InvalidOperationException("Pilot sets already installed; existing configuration was not overwritten.");
        var normal = UnityEngine.Object.FindFirstObjectByType<NormalPlacementSync>();
        var privateBoard = UnityEngine.Object.FindFirstObjectByType<PrivateTierBoardController>();
        var candidates = UnityEngine.Object.FindFirstObjectByType<CandidateSyncTest>();
        var readonlyBoard = UnityEngine.Object.FindFirstObjectByType<ReadonlyParticipantBoardView>();
        var session = UnityEngine.Object.FindFirstObjectByType<ExperimentSessionManager>();
        var main = Enumerable.Range(1, 3).Select(i => GameObject.Find("Card_" + i.ToString("00")).GetComponent<CardTierDetector>()).ToList();
        var privates = privateBoard.GetComponentsInChildren<PrivateAnswerCard>(true).OrderBy(c => c.CardId).ToList();
        if (privates.Count != 3 || readonlyBoard == null) throw new InvalidOperationException("Existing private/reveal board is missing.");
        var root = new GameObject("PilotCardSets");
        Undo.RegisterCreatedObjectUndo(root, "Install Pilot Card Sets");
        var controller = root.AddComponent<PilotCardSetController>();
        var dev = main.Select(c => new PilotCardSetController.Card { id = c.name, displayName = c.name,
            material = c.GetComponent<Renderer>().sharedMaterial, texture = c.GetComponent<Renderer>().sharedMaterial.mainTexture }).ToArray();
        // Extend the existing objects, retaining their XR settings and sync references.
        for (int i = 3; i < 8; i++)
        {
            var shared = UnityEngine.Object.Instantiate(main[0], main[0].transform.parent);
            shared.name = "Card_" + (i + 1).ToString("00");
            var local = UnityEngine.Object.Instantiate(privates[0], privates[0].transform.parent);
            local.name = "PrivateCard_" + (i + 1).ToString("00");
            local.ConfigureId(shared.name);
            main.Add(shared); privates.Add(local);
            shared.gameObject.SetActive(false); local.gameObject.SetActive(false);
        }
        var mainZones = ExpandBoard(GameObject.Find("TierBoard"));
        var privateZones = ExpandBoard(privateBoard.gameObject);
        foreach (var card in main) SetArray(card, "placementZones", mainZones);
        SetArray(normal, "tierZones", mainZones);
        SetArray(privateBoard, "tierZones", privateZones);
        // Include the new meshes/colliders in the existing visibility policy.
        var mainObjects = new[] { GameObject.Find("TierBoard") }.Concat(main.Select(c => c.gameObject));
        SetArray(privateBoard, "mainRenderers", mainObjects.SelectMany(o => o.GetComponentsInChildren<Renderer>(true)).ToArray());
        SetArray(privateBoard, "mainColliders", mainObjects.SelectMany(o => o.GetComponentsInChildren<Collider>(true)).ToArray());
        // The new row lives inside the existing board Canvas, requiring no new visibility controller.
        Set(controller, "normalSync", normal); Set(controller, "privateBoard", privateBoard);
        Set(controller, "candidates", candidates); Set(controller, "readonlyBoard", readonlyBoard);
        SetArray(controller, "sharedCards", main.ToArray()); SetArray(controller, "privateCards", privates.ToArray());
        SetArray(controller, "sharedZones", mainZones); SetArray(controller, "privateZones", privateZones);
        var catalog = ImportManifest(dev[0].material);
        SetCards(controller, "setA", catalog.Where(c => c.id.StartsWith("A", StringComparison.Ordinal)).ToArray());
        SetCards(controller, "setB", catalog.Where(c => c.id.StartsWith("B", StringComparison.Ordinal)).ToArray());
        SetCards(controller, "development", dev); Set(session, "cardSets", controller);
        // Keep 3 food cards as the serialized development default. Reset replaces these lists atomically.
        foreach (var zone in mainZones) zone.ConfigureCardSlots(dev.Select(c => c.id).ToArray());
        foreach (var zone in privateZones) zone.ConfigureCardSlots(privates.Take(3).Select(c => c.name).ToArray());
        // Fit eight image tiles even when every answer is in the same Tier.
        var view = new SerializedObject(readonlyBoard);
        view.FindProperty("boardSize").vector2Value = new Vector2(1600, 1450);
        view.FindProperty("canvasScale").floatValue = 0.001f;
        view.ApplyModifiedPropertiesWithoutUndo();
        RefreshPresentation();
        PilotCardReadabilitySetup.Run();
        CardDropSetup.Run();
        AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        Selection.activeGameObject = root;
        Debug.Log("[Pilot Cards Setup] A/B: 8 cards each; Development: original 3 cards. Select Next Session Set and run New Session.");
    }
    [MenuItem("Tools/Experiment/Refresh Pilot Presentation")]
    public static void RefreshPresentation()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode first");
        const string path = "Assets/PilotCards/PilotJapanese SDF.asset";
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
        if (font == null)
        {
            var source = AssetDatabase.LoadAssetAtPath<Font>("Assets/Fonts/IndependentAnswer/NotoSansJP.ttf");
            font = TMP_FontAsset.CreateFontAsset(source, 72, 9, UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, 1024, 1024);
            string glyphs = new string(Enumerable.Range(32, 95).Select(c => (char)c).ToArray()) + "—·" +
                string.Concat(File.ReadAllLines("coplace_pilot_kit/metadata/card_manifest.csv").Skip(1).Select(l => l.Split(',')[2]));
            if (!font.TryAddCharacters(glyphs, out var missing)) throw new InvalidOperationException("Missing pilot glyphs: " + missing);
            font.name = "PilotJapanese SDF"; font.atlasPopulationMode = AtlasPopulationMode.Static;
            AssetDatabase.CreateAsset(font, path); AssetDatabase.AddObjectToAsset(font.material, font);
            foreach (var atlas in font.atlasTextures) AssetDatabase.AddObjectToAsset(atlas, font);
        }
        var readonlyBoard = UnityEngine.Object.FindFirstObjectByType<ReadonlyParticipantBoardView>();
        Set(readonlyBoard, "font", font);
        var pilotSets = UnityEngine.Object.FindFirstObjectByType<PilotCardSetController>();
        if (pilotSets != null) Set(pilotSets, "cardFont", font);
        var view = new SerializedObject(readonlyBoard); view.FindProperty("canvasScale").floatValue = 0.001f; view.FindProperty("boardSize").vector2Value = new Vector2(1600, 1450); view.FindProperty("columns").intValue = 1; view.ApplyModifiedPropertiesWithoutUndo();
        foreach (var text in UnityEngine.Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (text.text == "複数人で画像カードをA〜DのTierに分類します。\n他の参加者と話し合いながら、\n最終的な配置を決定してください。") text.text = ExperimentLobbyUI.GeneralDescription;
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
    }
    private static PilotCardSetController.Card[] ImportManifest(Material template)
    {
        const string folder = "Assets/PilotCards";
        Directory.CreateDirectory(folder);
        var lines = File.ReadAllLines("coplace_pilot_kit/metadata/card_manifest.csv");
        var cards = lines.Skip(1).Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => l.Split(',')).ToArray();
        if (cards.Length != 16) throw new InvalidDataException("Expected exactly 16 manifest entries.");
        var result = new System.Collections.Generic.List<PilotCardSetController.Card>();
        foreach (var row in cards)
        {
            string id = row[1], target = folder + "/" + Path.GetFileName(row[3]);
            if (row.Length != 5 || (row[0] != "A" && row[0] != "B") || id != row[0] + (result.Count % 8 + 1).ToString("00"))
                throw new InvalidDataException("Unexpected manifest sequence: " + id);
            File.Copy("coplace_pilot_kit/" + row[3], target, true);
            AssetDatabase.ImportAsset(target, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(target);
            importer.textureType = TextureImporterType.Default; importer.maxTextureSize = 2048;
            importer.npotScale = TextureImporterNPOTScale.None; importer.wrapMode = TextureWrapMode.Clamp; importer.anisoLevel = 2;
            importer.mipmapEnabled = true; importer.textureCompression = TextureImporterCompression.Compressed;
            importer.SaveAndReimport();
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(target);
            var material = new Material(template) { name = id, mainTexture = texture };
            AssetDatabase.CreateAsset(material, folder + "/" + id + ".mat");
            result.Add(new PilotCardSetController.Card { id = id, displayName = row[2], texture = texture, material = material });
        }
        return result.ToArray();
    }
    private static TierZone[] ExpandBoard(GameObject root)
    {
        var zones = root.GetComponentsInChildren<TierZone>(true).ToList();
        var d = zones.Single(z => z.tierName == "D");
        var e = UnityEngine.Object.Instantiate(d, d.transform.parent); e.name = "Tier_E"; e.tierName = "E"; zones.Add(e);
        // Keep the existing overall vertical footprint: five Tier rows + Unclassified.
        foreach (var zone in zones)
        {
            int index = zone.tierName == "Unclassified" ? 5 : zone.tierName[0] - 'A';
            float y = 1.45f - index * 0.50f;
            zone.transform.localPosition = new Vector3(0, y, zone.transform.localPosition.z);
            zone.transform.localScale = new Vector3(2.7f, 0.46f, 0.1f);
            while (zone.snapPoints.Count < 8)
            {
                var point = UnityEngine.Object.Instantiate(zone.snapPoints[0], zone.snapPoints[0].parent);
                point.name = "SnapPoint_" + zone.tierName + "_" + (zone.snapPoints.Count + 1).ToString("00"); zone.snapPoints.Add(point);
            }
            for (int i = 0; i < 8; i++)
            {
                // Eight stable slots, allocated by ID rather than arrival order.
                var point = zone.snapPoints[i];
                var position = point.localPosition; position.x = -0.34f + i * 0.095f; point.localPosition = position;
            }
        }
        // Match the visual row positions to the physical drop zones.
        var canvas = root.GetComponentsInChildren<Canvas>(true).First(c => c.name == "TierBoardCanvas");
        var dRow = canvas.GetComponentsInChildren<RectTransform>(true).First(r => r.name == "TierDRow");
        var eRow = UnityEngine.Object.Instantiate(dRow, dRow.parent); eRow.name = "TierERow";
        foreach (var text in eRow.GetComponentsInChildren<TMP_Text>(true))
            if (text.text == "D") text.text = "E";
        var rows = new[] { "TierARow", "TierBRow", "TierCRow", "TierDRow", "TierERow", "UnclassifiedRow" };
        for (int i = 0; i < rows.Length; i++)
        {
            var row = canvas.GetComponentsInChildren<RectTransform>(true).FirstOrDefault(r => r.name == rows[i]);
            if (row == null) throw new InvalidOperationException("Missing visual row " + rows[i]);
            var pos = row.anchoredPosition; pos.y = (1.06f - i * 0.50f) * 1000; row.anchoredPosition = pos;
            row.sizeDelta = new Vector2(row.sizeDelta.x, 460);
            // Existing decorative three-card slots imply a three-card limit; hide them for the eight-card layout.
            foreach (var t in row.GetComponentsInChildren<RectTransform>(true))
                if (t.name.StartsWith("CardSlot", StringComparison.Ordinal)) t.gameObject.SetActive(false);
        }
        return zones.ToArray();
    }
    private static void Set(UnityEngine.Object target, string field, UnityEngine.Object value)
    { var so = new SerializedObject(target); so.FindProperty(field).objectReferenceValue = value; so.ApplyModifiedPropertiesWithoutUndo(); }
    private static void SetArray(UnityEngine.Object target, string field, UnityEngine.Object[] values)
    {
        var so = new SerializedObject(target); var p = so.FindProperty(field); p.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        so.ApplyModifiedPropertiesWithoutUndo();
    }
    private static void SetCards(UnityEngine.Object target, string field, PilotCardSetController.Card[] cards)
    {
        var so = new SerializedObject(target); var p = so.FindProperty(field); p.arraySize = cards.Length;
        for (int i = 0; i < cards.Length; i++)
        {
            var c = p.GetArrayElementAtIndex(i); c.FindPropertyRelative("id").stringValue = cards[i].id;
            c.FindPropertyRelative("displayName").stringValue = cards[i].displayName;
            c.FindPropertyRelative("texture").objectReferenceValue = cards[i].texture;
            c.FindPropertyRelative("material").objectReferenceValue = cards[i].material;
        }
        so.ApplyModifiedPropertiesWithoutUndo();
    }
}
#endif
