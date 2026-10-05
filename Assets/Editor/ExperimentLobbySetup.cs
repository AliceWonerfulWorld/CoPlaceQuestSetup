#if UNITY_EDITOR
using System;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class ExperimentLobbySetup
{
    private static readonly Color Accent = new Color(0.38f, 0.85f, 0.80f);
    public const string Glyphs = "CoPlace 共同VR空間における画像分類実験現在方式接続済接続待ち参加者が揃いました参加者を待っています実験者の開始をお待ちください開始の準備ができました接続を待っていますSession Resetを処理しています実験は開始済みです開始を同期していますNew Sessionを実行してくださいParticipantが揃うまでお待ちくださいNew Sessionで開始前の状態に戻してください方式の同期を待っていますParticipantの準備を待っています未作成現在状態実験を開始個別回答比較共同分類実験の準備方式を選択・●○→ / 0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ.-";
    [MenuItem("Tools/Experiment/Add Experiment Lobby")]
    public static void Run()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (EditorApplication.isPlaying || scene.path != "Assets/Scenes/QuestSetup.unity" || scene.isDirty)
            throw new InvalidOperationException("Open saved QuestSetup outside Play Mode first");
        if (GameObject.Find("ExperimentLobby") != null) throw new InvalidOperationException("Lobby already exists; adjust it in Inspector");
        var manager = UnityEngine.Object.FindFirstObjectByType<ExperimentManager>();
        var registry = UnityEngine.Object.FindFirstObjectByType<ExperimentParticipantRegistry>();
        var session = UnityEngine.Object.FindFirstObjectByType<ExperimentSessionManager>();
        var existingUI = UnityEngine.Object.FindFirstObjectByType<FinalAgreementUI>();
        if (manager == null || registry == null || session == null || existingUI == null) throw new InvalidOperationException("Existing managers/UI missing");
        var font = Font();
        var root = new GameObject("ExperimentLobby"); Undo.RegisterCreatedObjectUndo(root, "Add Experiment Lobby");
        root.transform.position = new Vector3(-0.07f, 1.65f, -1.5f);
        var lobby = root.AddComponent<ExperimentLobbyController>(); var ui = root.AddComponent<ExperimentLobbyUI>();
        Set(lobby, "experimentManager", manager); Set(lobby, "participantRegistry", registry); Set(lobby, "sessionManager", session);
        Set(manager, "lobby", lobby); Set(ui, "lobby", lobby); Set(ui, "experimentManager", manager);
        var content = Rect("LobbyContent", root.transform, Vector2.zero, new Vector2(3000, 1500));
        content.localScale = Vector3.one * 0.001f;
        var canvas = content.gameObject.AddComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace; canvas.sortingOrder = 20;
        var sourceCanvas = existingUI.GetComponent<Canvas>(); canvas.worldCamera = sourceCanvas.worldCamera;
        // Copy the installed, tested pointer raycasters (including XR). No new SDK.
        foreach (var raycaster in existingUI.GetComponents<UnityEngine.EventSystems.BaseRaycaster>())
        {
            var copy = content.gameObject.AddComponent(raycaster.GetType()); EditorUtility.CopySerialized(raycaster, copy);
        }
        if (content.GetComponents<UnityEngine.EventSystems.BaseRaycaster>().Length == 0) throw new InvalidOperationException("Source UI lacks raycasters");
        Set(ui, "contentRoot", content.gameObject);
        var intro = Rect("ParticipantIntroduction", content, Vector2.zero, new Vector2(1480, 1500));
        Surface(intro, intro.sizeDelta);
        Text("Eyebrow", intro, "EXPERIMENT INTRODUCTION", new Vector2(0, 670), new Vector2(1320, 44), 26, Accent, font);
        Text("Title", intro, "CoPlace", new Vector2(0, 570), new Vector2(1320, 132), 98, Color.white, font);
        Text("Subtitle", intro, "共同VR空間における画像分類実験", new Vector2(0, 462), new Vector2(1320, 72), 42, new Color(0.86f, 0.91f, 0.93f), font);
        Text("Description", intro, ExperimentLobbyUI.GeneralDescription, new Vector2(0, 300), new Vector2(1320, 200), 40, new Color(0.82f, 0.86f, 0.89f), font);
        var condition = Rect("CurrentCondition", intro, Vector2.zero, new Vector2(1320, 280));
        Panel("Surface", condition, Vector2.zero, condition.sizeDelta, new Color(0.11f, 0.18f, 0.20f, 0.96f));
        Set(ui, "modeHeading", Text("Heading", condition, "PROPOSED  /  個別回答 → 比較・共同分類", new Vector2(0, 90), new Vector2(1224, 56), 34, Accent, font));
        Set(ui, "modeDescription", Text("Explanation", condition, ExperimentLobbyUI.ProposedDescription, new Vector2(0, -24), new Vector2(1224, 146), 34, Color.white, font));
        var people = Rect("Participants", intro, new Vector2(-342, -300), new Vector2(636, 196));
        Panel("Surface", people, Vector2.zero, people.sizeDelta, new Color(0.10f, 0.13f, 0.15f, 0.94f));
        Set(ui, "participants", Text("Status", people, "Participants\n0 / 2 Connected\n○ 参加者を待っています", Vector2.zero, new Vector2(548, 168), 36, Color.white, font));
        var network = Rect("Connection", intro, new Vector2(342, -300), new Vector2(636, 196));
        Panel("Surface", network, Vector2.zero, network.sizeDelta, new Color(0.10f, 0.13f, 0.15f, 0.94f));
        Text("Caption", network, "CONNECTION", new Vector2(0, 60), new Vector2(548, 44), 26, new Color(0.62f, 0.71f, 0.74f), font);
        Set(ui, "connection", Text("Status", network, "○ Waiting  /  接続待ち", new Vector2(0, -20), new Vector2(548, 76), 34, Color.white, font));
        var wait = Rect("Waiting", intro, new Vector2(0, -565), new Vector2(1320, 190));
        Panel("Surface", wait, Vector2.zero, wait.sizeDelta, new Color(0.10f, 0.23f, 0.24f, 0.94f));
        var waiting = Text("Message", wait, "実験者の開始をお待ちください", Vector2.zero, new Vector2(1200, 148), 40, Accent, font);
        waiting.alignment = TextAlignmentOptions.Center; Set(ui, "waiting", waiting);

        var admin = Rect("ExperimenterControls", content, new Vector2(1120, 0), new Vector2(620, 1500));
        Surface(admin, admin.sizeDelta); Set(ui, "experimenterControls", admin.gameObject);
        Text("Eyebrow", admin, "EXPERIMENTER", new Vector2(0, 660), new Vector2(532, 50), 32, Accent, font);
        Text("Heading", admin, "実験の準備", new Vector2(0, 584), new Vector2(532, 70), 44, Color.white, font);
        Text("ModeCaption", admin, "方式を選択", new Vector2(0, 460), new Vector2(532, 46), 30, new Color(0.76f, 0.83f, 0.85f), font);
        Set(ui, "normalButton", Button("NormalButton", admin, "Normal  /  共同分類", new Vector2(0, 354), new Vector2(532, 118), font, lobby.SelectNormal));
        Set(ui, "proposedButton", Button("ProposedButton", admin, "Proposed  /  個別回答", new Vector2(0, 202), new Vector2(532, 118), font, lobby.SelectProposed));
        Set(ui, "newSessionButton", Button("NewSessionButton", admin, "New Session", new Vector2(0, 12), new Vector2(532, 118), font, lobby.NewSessionFromUI));
        Set(ui, "sessionInfo", Text("SessionInformation", admin, "Session ID\n未作成\n\n現在状態  /  Idle", new Vector2(0, -246), new Vector2(532, 294), 28, new Color(0.74f, 0.81f, 0.85f), font));
        var start = Button("StartExperimentButton", admin, "Start Experiment\n実験を開始", new Vector2(0, -570), new Vector2(532, 174), font, lobby.StartFromUI);
        var background = start.GetComponent<RoundedPanelGraphic>(); background.color = Accent;
        start.GetComponentInChildren<TMP_Text>().color = new Color(0.03f, 0.11f, 0.13f); Set(ui, "startButton", start);
        foreach (var mode in UnityEngine.Object.FindObjectsByType<ExperimentModeUI>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        { Set(mode, "participantRegistry", registry); Set(mode, "canvas", mode.GetComponent<Canvas>()); }
        admin.gameObject.SetActive(false);
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); Selection.activeGameObject = root;
        Debug.Log("[Lobby Setup] World-space Introduction + separate Experimenter controls saved to QuestSetup");
    }
    public static void RefreshExisting() { Font(); var root=GameObject.Find("ExperimentLobby"); ((RectTransform)root.transform.Find("LobbyContent")).sizeDelta = new Vector2(3000,1500); root.transform.Find("LobbyContent/ExperimenterControls").gameObject.SetActive(false); EditorSceneManager.MarkSceneDirty(root.scene); EditorSceneManager.SaveScene(root.scene); }
    private static TMP_FontAsset Font()
    {
        const string path = "Assets/Fonts/IndependentAnswer/LobbyJapanese SDF.asset";
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
        string glyphs = Glyphs + ExperimentLobbyUI.GeneralDescription + ExperimentLobbyUI.NormalDescription + ExperimentLobbyUI.ProposedDescription;
        if (font != null)
        {
            font.atlasPopulationMode = AtlasPopulationMode.Static;
            if (!font.HasCharacters(glyphs.Replace('\n', ' ')))
                throw new InvalidOperationException("Lobby font lacks required glyphs; regenerate the static font asset");
            EditorUtility.SetDirty(font); AssetDatabase.SaveAssets(); return font;
        }
        font = TMP_FontAsset.CreateFontAsset(AssetDatabase.LoadAssetAtPath<UnityEngine.Font>("Assets/Fonts/IndependentAnswer/NotoSansJP.ttf"), 72, 9,
            UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, 2048, 2048);
        font.name = "LobbyJapanese SDF";
        if (!font.TryAddCharacters(glyphs.Replace('\n', ' '), out string missing)) throw new InvalidOperationException("Lobby missing glyphs: " + missing);
        font.atlasPopulationMode = AtlasPopulationMode.Static;
        AssetDatabase.CreateAsset(font, path); AssetDatabase.AddObjectToAsset(font.material, font);
        foreach (var atlas in font.atlasTextures) AssetDatabase.AddObjectToAsset(atlas, font);
        EditorUtility.SetDirty(font); AssetDatabase.SaveAssets(); return font;
    }
    private static RectTransform Rect(string name, Transform parent, Vector2 pos, Vector2 size)
    {
        var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
        rect.SetParent(parent, false); rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = pos; rect.sizeDelta = size; return rect;
    }
    private static void Surface(RectTransform parent, Vector2 size)
    {
        Panel("Border", parent, Vector2.zero, size, new Color(0.39f, 0.59f, 0.59f, 0.46f));
        Panel("Surface", parent, Vector2.zero, size - Vector2.one * 3, new Color(0.055f, 0.070f, 0.083f, 0.96f));
    }
    private static RoundedPanelGraphic Panel(string name, Transform parent, Vector2 pos, Vector2 size, Color color)
    { var g = Rect(name, parent, pos, size).gameObject.AddComponent<RoundedPanelGraphic>(); g.color = color; g.raycastTarget = false; return g; }
    private static TMP_Text Text(string name, Transform parent, string text, Vector2 pos, Vector2 size, float fontSize, Color color, TMP_FontAsset font)
    {
        var tmp = Rect(name, parent, pos, size).gameObject.AddComponent<TextMeshProUGUI>();
        tmp.font = font; tmp.fontSize = fontSize; tmp.fontStyle = FontStyles.Bold; tmp.text = text; tmp.color = color;
        tmp.alignment = TextAlignmentOptions.MidlineLeft; tmp.raycastTarget = false; tmp.enableAutoSizing = false; return tmp;
    }
    private static UnityEngine.UI.Button Button(string name, Transform parent, string title, Vector2 pos, Vector2 size, TMP_FontAsset font, UnityEngine.Events.UnityAction action)
    {
        var panel = Panel(name, parent, pos, size, new Color(0.13f, 0.22f, 0.24f)); panel.raycastTarget = true;
        var button = panel.gameObject.AddComponent<UnityEngine.UI.Button>(); button.targetGraphic = panel;
        var colors = button.colors; colors.highlightedColor = new Color(0.77f, 0.95f, 0.95f); colors.pressedColor = new Color(0.58f, 0.82f, 0.81f);
        colors.disabledColor = new Color(0.48f, 0.53f, 0.55f, 0.65f); button.colors = colors;
        var label = Text("Label", panel.transform, title, Vector2.zero, size - new Vector2(32, 24), 34, Color.white, font);
        label.alignment = TextAlignmentOptions.Center; UnityEventTools.AddPersistentListener(button.onClick, action); return button;
    }
    private static void Set(UnityEngine.Object target, string name, UnityEngine.Object value)
    { var so = new SerializedObject(target); so.FindProperty(name).objectReferenceValue = value; so.ApplyModifiedPropertiesWithoutUndo(); }
}
#endif
