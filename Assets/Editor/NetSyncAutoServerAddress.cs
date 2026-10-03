#if UNITY_EDITOR
using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Styly.NetSync;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

[InitializeOnLoad]
internal static class NetSyncAutoServerAddress
{
    private const string ScenePath = "Assets/Scenes/QuestSetup.unity";
    // Verified in the installed STYLY NetSync NetSyncManager implementation.
    private const string ServerAddressProperty = "_serverAddress";
    private const string StartupKey = "CoPlaceQuestSetup.NetSyncAutoServerAddress.StartupUpdated";

    static NetSyncAutoServerAddress()
    {
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        if (!SessionState.GetBool(StartupKey, false))
            EditorApplication.delayCall += OnEditorStartup;
    }

    private static void OnEditorStartup()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating
            || EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer)
        {
            EditorApplication.delayCall += OnEditorStartup;
            return;
        }

        // SessionState survives script reloads, so this runs once per Editor session.
        SessionState.SetBool(StartupKey, true);
        RefreshServerAddress();
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingEditMode)
            RefreshServerAddress();
    }

    internal static void RefreshServerAddress(bool saveDirtyScene = false, string networkInterface = "en0")
    {
        if (Application.platform != RuntimePlatform.OSXEditor) return;
        if (!TryReadLanIp(networkInterface, out string address)) return;

        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        bool openedForUpdate = !scene.IsValid() || !scene.isLoaded;
        Scene previousActive = SceneManager.GetActiveScene();
        try
        {
            if (openedForUpdate)
            {
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
                {
                    Debug.LogWarning($"[NetSync Auto IP] Scene not found: {ScenePath}");
                    return;
                }
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            }

            bool alreadyDirty = scene.isDirty;
            bool changed = ApplyServerAddress(scene, address);
            // Preserve existing unsaved work at startup/Play. Builds save the target
            // scene so its serialized IP also invalidates an old incremental build.
            if ((changed && !alreadyDirty) || (saveDirtyScene && scene.isDirty))
            {
                if (!EditorSceneManager.SaveScene(scene))
                    Debug.LogWarning($"[NetSync Auto IP] Could not save {ScenePath}; save the scene before building.");
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[NetSync Auto IP] Could not update {ScenePath}: {exception.Message}");
        }
        finally
        {
            if (openedForUpdate && scene.IsValid() && scene.isLoaded)
                EditorSceneManager.CloseScene(scene, true);
            if (previousActive.IsValid() && previousActive.isLoaded)
                SceneManager.SetActiveScene(previousActive);
        }
    }

    private static bool TryReadLanIp(string networkInterface, out string address)
    {
        address = null;
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "/usr/sbin/ipconfig",
                Arguments = "getifaddr " + networkInterface,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using (var process = Process.Start(startInfo))
            {
                if (process == null) throw new InvalidOperationException("ipconfig did not start.");
                if (!process.WaitForExit(2000))
                {
                    process.Kill();
                    throw new TimeoutException("ipconfig timed out.");
                }

                string output = process.StandardOutput.ReadToEnd().Trim();
                if (process.ExitCode != 0 || !IPAddress.TryParse(output, out var ip)
                    || ip.AddressFamily != AddressFamily.InterNetwork
                    || IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.Any))
                {
                    Debug.LogWarning($"[NetSync Auto IP] Could not obtain an IPv4 address from {networkInterface}. Existing Server Address kept.");
                    return false;
                }

                address = ip.ToString();
                return true;
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[NetSync Auto IP] LAN IP lookup failed: {exception.Message}. Existing Server Address kept.");
            return false;
        }
    }

    private static bool ApplyServerAddress(Scene scene, string address)
    {
        bool foundManager = false;
        bool changed = false;
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var manager in root.GetComponentsInChildren<NetSyncManager>(true))
            {
                foundManager = true;
                var serialized = new SerializedObject(manager);
                var property = serialized.FindProperty(ServerAddressProperty);
                if (property == null || property.propertyType != SerializedPropertyType.String)
                {
                    Debug.LogWarning($"[NetSync Auto IP] NetSyncManager's string {ServerAddressProperty} field was not found. Existing settings kept.", manager);
                    continue;
                }
                if (property.stringValue == address) continue;

                string previous = property.stringValue;
                property.stringValue = address;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                if (PrefabUtility.IsPartOfPrefabInstance(manager))
                    PrefabUtility.RecordPrefabInstancePropertyModifications(manager);
                changed = true;
                Debug.Log($"[NetSync Auto IP] {previous} -> {address}", manager);
            }
        }

        if (!foundManager)
            Debug.LogWarning($"[NetSync Auto IP] No NetSyncManager found in {scene.path}. Existing settings kept.");
        if (changed) EditorSceneManager.MarkSceneDirty(scene);
        return changed;
    }
}

internal sealed class NetSyncAutoServerAddressBuildProcessor : IPreprocessBuildWithReport
{
    public int callbackOrder => -1000;

    public void OnPreprocessBuild(BuildReport report)
    {
        NetSyncAutoServerAddress.RefreshServerAddress(saveDirtyScene: true);
    }
}
#endif
