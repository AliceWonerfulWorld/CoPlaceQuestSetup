using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

// Explicit one-time scene builder; does not run automatically or rebuild at runtime.
public static class PrivateTierBoardSetup
{
    [MenuItem("Tools/Experiment/Add Private TierBoard")]
    public static void Create()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode before creating the board.");
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/QuestSetup.unity") throw new InvalidOperationException("Open QuestSetup first.");
        if (GameObject.Find("PrivateTierBoard") != null) throw new InvalidOperationException("PrivateTierBoard already exists; adjust it in the Inspector.");
        var main = GameObject.Find("TierBoard");
        var mainCards = new[] { "Card_01", "Card_02", "Card_03" }.Select(GameObject.Find).ToArray();
        if (main == null || mainCards.Any(c => c == null)) throw new InvalidOperationException("Main board/cards are missing.");
        var manager = UnityEngine.Object.FindFirstObjectByType<ExperimentManager>();
        var registry = UnityEngine.Object.FindFirstObjectByType<ExperimentParticipantRegistry>();
        var session = UnityEngine.Object.FindFirstObjectByType<ExperimentSessionManager>();
        var candidates = UnityEngine.Object.FindFirstObjectByType<CandidateSyncTest>();
        if (manager == null || registry == null || session == null || candidates == null) throw new InvalidOperationException("Experiment references missing.");

        var root = new GameObject("PrivateTierBoard");
        Undo.RegisterCreatedObjectUndo(root, "Add Private TierBoard");
        var controller = root.AddComponent<PrivateTierBoardController>();
        var content = new GameObject("LocalView"); content.transform.SetParent(root.transform, false);
        var view = UnityEngine.Object.Instantiate(main, content.transform);
        view.name = "PrivateBoardView";
        // Preserve the source's readable layout with a smaller local workspace.
        view.transform.localPosition = Vector3.zero; view.transform.localRotation = Quaternion.identity;
        view.transform.localScale = Vector3.one;
        root.transform.position = new Vector3(0, 1.25f, 1.25f);
        root.transform.localScale = Vector3.one * 0.6f;
        foreach (var ui in view.GetComponentsInChildren<FinalAgreementUI>(true)) UnityEngine.Object.DestroyImmediate(ui.gameObject);
        // A clone must carry no shared interaction/data/transform components.
        foreach (var component in view.GetComponentsInChildren<MonoBehaviour>(true))
            if (component != null && component.GetType().Namespace != null && component.GetType().Namespace.StartsWith("Styly.NetSync", StringComparison.Ordinal))
                UnityEngine.Object.DestroyImmediate(component);
        TMP_Text instructionText = null;
        foreach (var text in view.GetComponentsInChildren<TMP_Text>(true))
        {
            if (text.text == "TIER BOARD") { text.text = "PRIVATE ANSWER"; text.fontSize = 70; text.color = new Color(0.43f, 0.91f, 0.84f); }
            else if (text.text == "Place a card")
            {
                instructionText = text;
                text.text = "Only your answers are visible. Grab any card. Return to Unclassified to cancel.";
                text.fontSize = 30; text.enableAutoSizing = false;
                var rect = text.rectTransform;
                rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 2550);
                rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 145);
                rect.anchoredPosition += Vector2.down * 35;
            }
        }
        var zones = view.GetComponentsInChildren<TierZone>(true);
        var privateCards = new List<PrivateAnswerCard>();
        for (int i = 0; i < mainCards.Length; i++)
        {
            GameObject source = mainCards[i];
            var cardObject = UnityEngine.Object.Instantiate(source, content.transform);
            cardObject.name = "PrivateCard_" + (i + 1).ToString("00"); cardObject.tag = "Untagged";
            // Reuse only visuals, colliders, Rigidbody and XR Grab settings.
            foreach (var component in cardObject.GetComponentsInChildren<MonoBehaviour>(true))
                if (component != null && !(component is XRGrabInteractable)) UnityEngine.Object.DestroyImmediate(component);
            var grab = cardObject.GetComponent<XRGrabInteractable>();
            grab.selectEntered.RemoveAllListeners(); grab.selectExited.RemoveAllListeners();
            grab.retainTransformParent = true;
            cardObject.transform.localScale = source.transform.lossyScale / 0.6f;
            var body = cardObject.GetComponent<Rigidbody>(); body.useGravity = false; body.isKinematic = true;
            var privateCard = cardObject.AddComponent<PrivateAnswerCard>();
            Set(privateCard, "cardId", source.name); Set(privateCard, "board", controller);
            privateCards.Add(privateCard);
        }
        foreach (var zone in zones)
        {
            SetArray(zone, "snapCardIds", privateCards.Select(c => c.gameObject.name).ToArray());
            // Place the preview cards without calling runtime Awake/interaction methods.
            if (zone.tierName == "Unclassified")
                for (int i = 0; i < privateCards.Count; i++)
                    privateCards[i].transform.SetPositionAndRotation(zone.snapPoints[i].position, zone.snapPoints[i].rotation);
        }
        Set(controller, "experimentManager", manager); Set(controller, "participantRegistry", registry);
        Set(controller, "sessionManager", session); Set(controller, "candidateSync", candidates);
        Set(controller, "contentRoot", content); Set(controller, "instructions", instructionText);
        SetArray(controller, "cards", privateCards.Cast<UnityEngine.Object>().ToArray());
        SetArray(controller, "tierZones", zones.Cast<UnityEngine.Object>().ToArray());
        var mainObjects = new[] { main }.Concat(mainCards).ToArray();
        SetArray(controller, "mainRenderers", mainObjects.SelectMany(o => o.GetComponentsInChildren<Renderer>(true)).Cast<UnityEngine.Object>().ToArray());
        SetArray(controller, "mainCanvases", main.GetComponentsInChildren<Canvas>(true).Where(c => c.GetComponent<FinalAgreementUI>() == null).Cast<UnityEngine.Object>().ToArray());
        SetArray(controller, "mainColliders", mainObjects.SelectMany(o => o.GetComponentsInChildren<Collider>(true)).Cast<UnityEngine.Object>().ToArray());
        Set(candidates, "usePrivateAnswerBoard", true);
        SetArray(candidates, "answerCardIds", mainCards.Select(c => c.name).ToArray());
        ImproveReadability(root);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Selection.activeGameObject = root;
        Debug.Log("[Private Board Setup] Local view, 5 private zones and 3 private XR cards saved to QuestSetup.");
    }
    [MenuItem("Tools/Experiment/Update Private Board Readability")]
    public static void ImproveReadability()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode first.");
        var root = GameObject.Find("PrivateTierBoard");
        if (root == null) throw new InvalidOperationException("PrivateTierBoard is missing.");
        ImproveReadability(root);
        EditorSceneManager.MarkSceneDirty(root.scene); EditorSceneManager.SaveScene(root.scene);
    }
    private static void ImproveReadability(GameObject root)
    {
        TMP_Text hint = root.GetComponentsInChildren<TMP_Text>(true).First(t => t.name == "Hint");
        hint.fontSize = 50; hint.enableAutoSizing = false;
        hint.rectTransform.anchoredPosition = new Vector2(0, 1390);
        hint.rectTransform.sizeDelta = new Vector2(2480, 200);
        foreach (var card in root.GetComponentsInChildren<PrivateAnswerCard>(true))
        {
            if (card.transform.Find("CardName") != null) continue;
            var label = new GameObject("CardName", typeof(RectTransform), typeof(Canvas));
            label.transform.SetParent(card.transform, false);
            label.transform.localPosition = new Vector3(0, -0.68f, -0.56f);
            label.transform.localScale = Vector3.one * 0.004f;
            label.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            var rect = label.GetComponent<RectTransform>(); rect.sizeDelta = new Vector2(240, 52);
            var textObject = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(label.transform, false);
            var text = textObject.GetComponent<TextMeshProUGUI>();
            text.font = hint.font; text.fontSize = 38; text.text = card.CardId;
            text.alignment = TextAlignmentOptions.Center; text.color = new Color(0.82f, 0.96f, 0.93f);
            text.raycastTarget = false;
            text.rectTransform.anchorMin = Vector2.zero; text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = text.rectTransform.offsetMax = Vector2.zero;
        }
    }
    private static void Set(UnityEngine.Object target, string name, UnityEngine.Object value)
    { var so = new SerializedObject(target); so.FindProperty(name).objectReferenceValue = value; so.ApplyModifiedPropertiesWithoutUndo(); }
    private static void Set(UnityEngine.Object target, string name, string value)
    { var so = new SerializedObject(target); so.FindProperty(name).stringValue = value; so.ApplyModifiedPropertiesWithoutUndo(); }
    private static void Set(UnityEngine.Object target, string name, bool value)
    { var so = new SerializedObject(target); so.FindProperty(name).boolValue = value; so.ApplyModifiedPropertiesWithoutUndo(); }
    private static void SetArray(UnityEngine.Object target, string name, UnityEngine.Object[] values)
    {
        var so = new SerializedObject(target); var p = so.FindProperty(name); p.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        so.ApplyModifiedPropertiesWithoutUndo();
    }
    private static void SetArray(UnityEngine.Object target, string name, string[] values)
    {
        var so = new SerializedObject(target); var p = so.FindProperty(name); p.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++) p.GetArrayElementAtIndex(i).stringValue = values[i];
        so.ApplyModifiedPropertiesWithoutUndo();
    }
}
