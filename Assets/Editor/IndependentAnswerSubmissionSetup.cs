#if UNITY_EDITOR
using System;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class IndependentAnswerSubmissionSetup
{
    [MenuItem("Tools/Experiment/Add Independent Answer Submit")]
    public static void Run()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (EditorApplication.isPlaying || scene.path != "Assets/Scenes/QuestSetup.unity" || scene.isDirty)
            throw new InvalidOperationException("Open saved QuestSetup outside Play Mode first");
        var board = UnityEngine.Object.FindFirstObjectByType<PrivateTierBoardController>();
        var answers = UnityEngine.Object.FindFirstObjectByType<CandidateSyncTest>();
        var reveal = UnityEngine.Object.FindFirstObjectByType<ProposedRevealCoordinator>();
        var registry = UnityEngine.Object.FindFirstObjectByType<ExperimentParticipantRegistry>();
        var session = UnityEngine.Object.FindFirstObjectByType<ExperimentSessionManager>();
        var source = UnityEngine.Object.FindFirstObjectByType<FinalAgreementUI>();
        if (board == null || answers == null || reveal == null || registry == null || session == null || source == null)
            throw new InvalidOperationException("Existing experiment/UI references missing");
        var submission = board.GetComponent<IndependentAnswerSubmission>();
        if (submission == null) submission = board.gameObject.AddComponent<IndependentAnswerSubmission>();
        Set(submission, "candidateSync", answers); Set(submission, "participantRegistry", registry);
        Set(submission, "sessionManager", session); Set(submission, "revealCoordinator", reveal); Set(submission, "privateBoard", board);
        Set(board, "independentSubmission", submission); Set(answers, "independentSubmission", submission); Set(reveal, "independentSubmission", submission);

        const string fontPath = "Assets/Fonts/IndependentAnswer/SubmitJapanese SDF.asset";
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(fontPath);
        if (font == null)
        {
            font = TMP_FontAsset.CreateFontAsset(AssetDatabase.LoadAssetAtPath<Font>("Assets/Fonts/IndependentAnswer/NotoSansJP.ttf"),
                90, 9, UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, 1024, 1024);
            font.name = "SubmitJapanese SDF";
            if (!font.TryAddCharacters("この回答で確定確定を取り消す回答確定済み相手の回答を待っています...すべての回答がそろいました内容を確認して確定してください一斉Revealを準備しています...すべてのカードを配置してください回答確定を取り消しています...", out string missing))
                throw new InvalidOperationException("Missing Submit UI glyphs: " + missing);
            font.atlasPopulationMode = AtlasPopulationMode.Static;
            AssetDatabase.CreateAsset(font, fontPath); AssetDatabase.AddObjectToAsset(font.material, font);
            foreach (var atlas in font.atlasTextures) AssetDatabase.AddObjectToAsset(atlas, font);
            EditorUtility.SetDirty(font); AssetDatabase.SaveAssets();
        }
        var content = board.transform.Find("LocalView");
        var ui = content.GetComponentInChildren<IndependentAnswerSubmissionUI>(true);
        if (ui == null)
        {
            // Reuse the tested Final Agreement panel style/raycasters, but remove
            // all Ready behaviour and callbacks. Consent is a separate component.
            var panel = UnityEngine.Object.Instantiate(source.gameObject, content);
            panel.name = "IndependentAnswerSubmitCanvas";
            var old = panel.GetComponent<FinalAgreementUI>(); var oldSo = new SerializedObject(old);
            var status = (TMP_Text)oldSo.FindProperty("statusText").objectReferenceValue;
            var label = (TMP_Text)oldSo.FindProperty("buttonText").objectReferenceValue;
            var button = (UnityEngine.UI.Button)oldSo.FindProperty("readyButton").objectReferenceValue;
            var canvas = (Canvas)oldSo.FindProperty("canvas").objectReferenceValue;
            UnityEngine.Object.DestroyImmediate(old);
            ui = panel.AddComponent<IndependentAnswerSubmissionUI>();
            Set(ui, "submission", submission); Set(ui, "privateBoard", board); Set(ui, "reveal", reveal);
            Set(ui, "canvas", canvas); Set(ui, "button", button); Set(ui, "label", label); Set(ui, "status", status);
            button.onClick.RemoveAllListeners();
            for (int i = button.onClick.GetPersistentEventCount() - 1; i >= 0; i--) UnityEditor.Events.UnityEventTools.RemovePersistentListener(button.onClick, i);
            UnityEditor.Events.UnityEventTools.AddPersistentListener(button.onClick, ui.ToggleSubmission);
            foreach (var text in new[] { status, label })
            { text.font = font; text.fontSize = 42; text.fontStyle = FontStyles.Bold; text.raycastTarget = false; }
            status.text = "すべてのカードを配置してください"; label.text = "この回答で確定"; button.interactable = false;
            ((RectTransform)panel.transform).sizeDelta = new Vector2(920, 430);
            var privateCanvas = content.GetComponentsInChildren<Canvas>(true).Single(c => c.name == "TierBoardCanvas");
            panel.transform.SetPositionAndRotation(privateCanvas.transform.TransformPoint(new Vector3(2350, 0, -80)), privateCanvas.transform.rotation);
            panel.transform.localScale = Vector3.one * 0.0015f;
            if (panel.GetComponents<UnityEngine.EventSystems.BaseRaycaster>().Length == 0) throw new InvalidOperationException("Copied Submit Canvas has no raycaster");
        }
        var boardCanvas = content.GetComponentsInChildren<Canvas>(true).Single(c => c.name == "TierBoardCanvas");
        var panelRect = (RectTransform)ui.transform;
        panelRect.anchoredPosition3D = content.InverseTransformPoint(boardCanvas.transform.TransformPoint(new Vector3(2350, 0, -80)));
        panelRect.localRotation = Quaternion.Inverse(content.rotation) * boardCanvas.transform.rotation;
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        Debug.Log("[Independent Submit Setup] Private consent + Japanese Submit/Cancel UI saved");
    }
    private static void Set(UnityEngine.Object obj, string name, UnityEngine.Object value)
    { var so = new SerializedObject(obj); so.FindProperty(name).objectReferenceValue = value; so.ApplyModifiedPropertiesWithoutUndo(); }
}
#endif
