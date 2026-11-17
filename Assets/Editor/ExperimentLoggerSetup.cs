#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class ExperimentLoggerSetup
{
    [MenuItem("Tools/Experiment/Configure Experiment Logger")]
    public static void Run()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (EditorApplication.isPlaying || scene.path != "Assets/Scenes/QuestSetup.unity" || scene.isDirty)
            throw new InvalidOperationException("Open saved QuestSetup outside Play Mode first");
        var manager = UnityEngine.Object.FindFirstObjectByType<ExperimentManager>();
        var logger = manager.GetComponent<ExperimentLogger>();
        if (logger == null) logger = Undo.AddComponent<ExperimentLogger>(manager.gameObject);
        var serialized = new SerializedObject(logger);
        serialized.FindProperty("sessionManager").objectReferenceValue = UnityEngine.Object.FindFirstObjectByType<ExperimentSessionManager>();
        serialized.FindProperty("participantRegistry").objectReferenceValue = UnityEngine.Object.FindFirstObjectByType<ExperimentParticipantRegistry>();
        serialized.FindProperty("lobby").objectReferenceValue = UnityEngine.Object.FindFirstObjectByType<ExperimentLobbyController>();
        serialized.FindProperty("finalAgreement").objectReferenceValue = UnityEngine.Object.FindFirstObjectByType<FinalAgreementManager>();
        serialized.ApplyModifiedProperties();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[Experiment Logger Setup] Configured on ExperimentManager; no CSV written until Experimenter observes a Session");
    }
}
#endif
