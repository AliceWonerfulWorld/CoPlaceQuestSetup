#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class ProposedSharedPlacementSetup
{
    [MenuItem("Tools/Experiment/Connect Proposed Shared Placement")]
    public static void Run()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode first");
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/QuestSetup.unity") throw new InvalidOperationException("Open QuestSetup first");
        var manager = UnityEngine.Object.FindFirstObjectByType<ExperimentManager>();
        var registry = UnityEngine.Object.FindFirstObjectByType<ExperimentParticipantRegistry>();
        var session = UnityEngine.Object.FindFirstObjectByType<ExperimentSessionManager>();
        var reveal = UnityEngine.Object.FindFirstObjectByType<ProposedRevealCoordinator>();
        var sync = UnityEngine.Object.FindFirstObjectByType<NormalPlacementSync>();
        var agreement = UnityEngine.Object.FindFirstObjectByType<FinalAgreementManager>();
        var privateBoard = UnityEngine.Object.FindFirstObjectByType<PrivateTierBoardController>();
        var view = UnityEngine.Object.FindFirstObjectByType<ReadonlyParticipantBoardView>();
        if (manager == null || registry == null || session == null || reveal == null || sync == null || agreement == null || privateBoard == null || view == null)
            throw new InvalidOperationException("Existing experiment references missing");
        var shared = UnityEngine.Object.FindFirstObjectByType<ProposedSharedPlacementController>();
        if (shared == null) shared = reveal.gameObject.AddComponent<ProposedSharedPlacementController>();
        Set(shared, "experimentManager", manager); Set(shared, "participantRegistry", registry);
        Set(shared, "sessionManager", session); Set(shared, "revealCoordinator", reveal);
        Set(shared, "placementSync", sync); Set(shared, "finalAgreement", agreement);
        Set(sync, "proposedSharedPlacement", shared); Set(privateBoard, "sharedPlacement", shared);

        // Main's logical root differs from its actual interaction surface. Place
        // the references beside that Canvas, opposite the Ready UI on the right.
        var main = GameObject.Find("TierBoard");
        var canvas = main.GetComponentsInChildren<Canvas>(true).Single(c => c.name == "TierBoardCanvas");
        Undo.RecordObject(view.transform, "Position Initial Answer References");
        view.transform.position = canvas.transform.position + canvas.transform.right * -2.25f + Vector3.up * 0.3f + canvas.transform.forward * 0.4f;
        view.transform.rotation = canvas.transform.rotation * Quaternion.Euler(0, -15, 0);
        var so = new SerializedObject(view); so.FindProperty("canvasScale").floatValue = 0.0009f;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        Selection.activeGameObject = view.gameObject;
        Debug.Log("[Proposed Shared Placement Setup] Main adapter + Final Agreement connected; reference UI floats left/behind Main");
    }
    private static void Set(UnityEngine.Object target, string name, UnityEngine.Object value)
    {
        var so = new SerializedObject(target); so.FindProperty(name).objectReferenceValue = value; so.ApplyModifiedPropertiesWithoutUndo();
    }
}
#endif
