#if UNITY_EDITOR
using System;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class ExperimentResultSetup
{
    private static readonly Color Accent = new Color(0.38f, 0.85f, 0.80f);
    [MenuItem("Tools/Experiment/Add Result Waiting UI")]
    public static void Run()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (EditorApplication.isPlaying || scene.path != "Assets/Scenes/QuestSetup.unity")
            throw new InvalidOperationException("Open QuestSetup outside Play Mode first");
        if (UnityEngine.Object.FindFirstObjectByType<ExperimentResultUI>() != null)
            throw new InvalidOperationException("Result UI already exists");
        var manager = UnityEngine.Object.FindFirstObjectByType<ExperimentManager>();
        var session = UnityEngine.Object.FindFirstObjectByType<ExperimentSessionManager>();
        var registry = UnityEngine.Object.FindFirstObjectByType<ExperimentParticipantRegistry>();
        var agreement = UnityEngine.Object.FindFirstObjectByType<FinalAgreementUI>();
        var main = GameObject.Find("TierBoard");
        if (manager == null || session == null || registry == null || agreement == null || main == null)
            throw new InvalidOperationException("Existing experiment references missing");
        var board = main.GetComponentsInChildren<Canvas>(true).Single(c => c.name == "TierBoardCanvas");
        var font = ResultFont();
        var root = new GameObject("ExperimentResult");
        Undo.RegisterCreatedObjectUndo(root, "Add Result Waiting UI");
        root.transform.SetParent(main.transform, false);
        var corners = new Vector3[4]; ((RectTransform)board.transform).GetWorldCorners(corners);
        root.transform.position = (corners[1] + corners[2]) * 0.5f + board.transform.up * 0.23f - board.transform.forward * 0.025f;
        root.transform.rotation = board.transform.rotation;
        var ui = root.AddComponent<ExperimentResultUI>();
        Set(ui, "experimentManager", manager); Set(ui, "sessionManager", session); Set(ui, "participantRegistry", registry);
        var content = Rect("ResultContent", root.transform, Vector2.zero, new Vector2(1660, 350));
        content.localScale = Vector3.one * 0.001f;
        var canvas = content.gameObject.AddComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = agreement.GetComponent<Canvas>().worldCamera;
        foreach (var source in agreement.GetComponents<UnityEngine.EventSystems.BaseRaycaster>())
        {
            var copy = content.gameObject.AddComponent(source.GetType()); EditorUtility.CopySerialized(source, copy);
        }
        Set(ui, "contentRoot", content.gameObject);
        Surface(content);
        Label("Title", content, ExperimentResultUI.Title, new Vector2(0, 112), new Vector2(1532, 50), 32, Accent, font);
        Label("ConfirmedMessage", content, ExperimentResultUI.Message, new Vector2(0, 42), new Vector2(1532, 72), 52, Color.white, font);
        Label("WaitingMessage", content, ExperimentResultUI.Waiting, new Vector2(0, -78), new Vector2(1532, 130), 38, new Color(0.80f, 0.88f, 0.90f), font);
        var admin = Rect("ExperimenterControls", content, new Vector2(0, 320), new Vector2(1660, 230));
        Surface(admin); Set(ui, "experimenterControls", admin.gameObject);
        Set(ui, "sessionInfo", Label("SessionInformation", admin, "", new Vector2(-240, 0), new Vector2(1030, 180), 28, Color.white, font));
        var panel = Panel("NewSessionButton", admin, new Vector2(550, 0), new Vector2(450, 116), new Color(0.13f, 0.25f, 0.27f));
        panel.raycastTarget = true;
        var button = panel.gameObject.AddComponent<UnityEngine.UI.Button>(); button.targetGraphic = panel;
        var label = Label("Label", panel.transform, "New Session / Reset", Vector2.zero, new Vector2(410, 82), 32, Accent, font);
        label.alignment = TextAlignmentOptions.Center;
        UnityEventTools.AddPersistentListener(button.onClick, ui.NewSessionFromUI); Set(ui, "newSessionButton", button);
        admin.gameObject.SetActive(false); content.gameObject.SetActive(false);
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        Debug.Log("[Result UI Setup] Above existing Main board; Confirmed / Waiting with manual Experimenter reset");
    }
    private static TMP_FontAsset ResultFont()
    {
        const string path = "Assets/Fonts/IndependentAnswer/ResultJapanese SDF.asset";
        string glyphs = ExperimentResultUI.Title + ExperimentResultUI.Message + ExperimentResultUI.Waiting +
            "Mode: Normal Proposed Confirmed Session ID: 待機中 — New Session / Resetで次の実験を準備0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
        if (font != null) return font;
        var source = AssetDatabase.LoadAssetAtPath<Font>("Assets/Fonts/IndependentAnswer/NotoSansJP.ttf");
        if (source == null) throw new InvalidOperationException("Japanese font source missing");
        font = TMP_FontAsset.CreateFontAsset(source, 72, 9, UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, 1024, 1024);
        font.name = "ResultJapanese SDF";
        if (!font.TryAddCharacters(glyphs.Replace('\n', ' '), out var missing)) throw new InvalidOperationException("Result missing glyphs: " + missing);
        font.atlasPopulationMode = AtlasPopulationMode.Static;
        AssetDatabase.CreateAsset(font, path); AssetDatabase.AddObjectToAsset(font.material, font);
        foreach (var atlas in font.atlasTextures) AssetDatabase.AddObjectToAsset(atlas, font);
        EditorUtility.SetDirty(font); AssetDatabase.SaveAssets(); return font;
    }
    private static RectTransform Rect(string name, Transform parent, Vector2 position, Vector2 size)
    {
        var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
        rect.SetParent(parent, false); rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * 0.5f;
        rect.sizeDelta = size; rect.anchoredPosition = position; return rect;
    }
    private static void Surface(RectTransform rect)
    {
        Panel("Border", rect, Vector2.zero, rect.sizeDelta, new Color(Accent.r, Accent.g, Accent.b, 0.4f));
        Panel("Surface", rect, Vector2.zero, rect.sizeDelta - Vector2.one * 3, new Color(0.035f, 0.065f, 0.085f, 0.94f));
    }
    private static RoundedPanelGraphic Panel(string name, Transform parent, Vector2 position, Vector2 size, Color color)
    {
        var panel = Rect(name, parent, position, size).gameObject.AddComponent<RoundedPanelGraphic>();
        panel.color = color; panel.raycastTarget = false; return panel;
    }
    private static TMP_Text Label(string name, Transform parent, string text, Vector2 position, Vector2 size, float fontSize, Color color, TMP_FontAsset font)
    {
        var label = Rect(name, parent, position, size).gameObject.AddComponent<TextMeshProUGUI>();
        label.font = font; label.fontSize = fontSize; label.text = text; label.color = color;
        label.alignment = TextAlignmentOptions.MidlineLeft; label.raycastTarget = false; return label;
    }
    private static void Set(UnityEngine.Object target, string name, UnityEngine.Object value)
    {
        var so = new SerializedObject(target); so.FindProperty(name).objectReferenceValue = value; so.ApplyModifiedPropertiesWithoutUndo();
    }
}
#endif
