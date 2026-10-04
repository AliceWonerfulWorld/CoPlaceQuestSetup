#if UNITY_EDITOR
using System;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class ProposedRevealSetup
{
    [MenuItem("Tools/Experiment/Add Proposed Readonly Reveal")]
    public static void Run()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode first");
        var manager = UnityEngine.Object.FindFirstObjectByType<ExperimentManager>();
        var registry = UnityEngine.Object.FindFirstObjectByType<ExperimentParticipantRegistry>();
        var session = UnityEngine.Object.FindFirstObjectByType<ExperimentSessionManager>();
        var sync = UnityEngine.Object.FindFirstObjectByType<CandidateSyncTest>();
        var privateBoard = UnityEngine.Object.FindFirstObjectByType<PrivateTierBoardController>();
        var marker = UnityEngine.Object.FindFirstObjectByType<CandidateMarkerManager>();
        if (manager == null || registry == null || session == null || sync == null || privateBoard == null || marker == null)
            throw new InvalidOperationException("Existing experiment components missing");
        var coordinator = UnityEngine.Object.FindFirstObjectByType<ProposedRevealCoordinator>();
        if (coordinator == null)
        {
            var root = new GameObject("ProposedReveal"); Undo.RegisterCreatedObjectUndo(root, "Add Proposed Reveal");
            coordinator = root.AddComponent<ProposedRevealCoordinator>();
        }
        Set(coordinator, "experimentManager", manager); Set(coordinator, "participantRegistry", registry);
        Set(coordinator, "sessionManager", session); Set(coordinator, "candidateSync", sync);
        Set(sync, "revealCoordinator", coordinator); Set(privateBoard, "revealCoordinator", coordinator);
        var view = UnityEngine.Object.FindFirstObjectByType<ReadonlyParticipantBoardView>();
        if (view == null)
        {
            var root = new GameObject("ParticipantAnswerReveal"); Undo.RegisterCreatedObjectUndo(root, "Add Readonly Boards");
            root.transform.position = new Vector3(0, 1.45f, 1.6f);
            view = root.AddComponent<ReadonlyParticipantBoardView>();
        }
        Set(view, "revealCoordinator", coordinator);
        Set(view, "font", privateBoard.GetComponentsInChildren<TMP_Text>(true).First(t => t.font != null).font);
        var source = new SerializedObject(marker).FindProperty("cardImages");
        var target = new SerializedObject(view); var array = target.FindProperty("cardVisuals"); array.arraySize = source.arraySize;
        for (int i = 0; i < array.arraySize; i++)
        {
            var from = source.GetArrayElementAtIndex(i); var to = array.GetArrayElementAtIndex(i);
            string id = from.FindPropertyRelative("cardId").stringValue;
            to.FindPropertyRelative("cardId").stringValue = id; to.FindPropertyRelative("displayName").stringValue = id;
            to.FindPropertyRelative("texture").objectReferenceValue = from.FindPropertyRelative("cardImage").objectReferenceValue;
        }
        target.ApplyModifiedPropertiesWithoutUndo();
        EditorSceneManager.MarkSceneDirty(manager.gameObject.scene); EditorSceneManager.SaveScene(manager.gameObject.scene);
        Selection.activeGameObject = view.gameObject;
        Debug.Log("[Proposed Reveal Setup] Coordinator + dynamic Readonly Mini Board anchor saved");
    }
    private static void Set(UnityEngine.Object target, string name, UnityEngine.Object value)
    {
        var so = new SerializedObject(target); so.FindProperty(name).objectReferenceValue = value; so.ApplyModifiedPropertiesWithoutUndo();
    }
}
#endif
