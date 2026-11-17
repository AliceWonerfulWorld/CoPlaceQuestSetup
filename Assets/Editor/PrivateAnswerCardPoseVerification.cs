#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

// Captures native Unity errors too: checking only the final pose missed the startup failure.
public static class PrivateAnswerCardPoseVerification
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    [Serializable] private class Report { public bool passed; public List<string> checks = new List<string>(); public List<string> errors = new List<string>(); public string failure; }
    [MenuItem("Tools/Experiment/Verify Private Card Initial Pose")]
    public static void Run()
    {
        var report = new Report(); var scene = EditorSceneManager.NewPreviewScene();
        void Log(string message, string stack, LogType type) { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) report.errors.Add(message + "\n" + stack); }
        void Check(bool ok, string text) { if (!ok) throw new Exception(text); report.checks.Add(text); }
        Application.logMessageReceived += Log;
        try
        {
            PrivateAnswerCard Create(string name, Vector3 position, Quaternion rotation, bool active = true)
            {
                var go = new GameObject(name); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene); go.SetActive(active);
                go.transform.SetPositionAndRotation(position, rotation);
                var body = go.AddComponent<Rigidbody>(); body.useGravity = false; body.isKinematic = true;
                go.AddComponent<BoxCollider>(); go.AddComponent<XRGrabInteractable>();
                var card = go.AddComponent<PrivateAnswerCard>(); Call(card, "Awake"); return card;
            }
            TierZone Zone(string name, bool slot = true)
            {
                var go = new GameObject(name); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);
                go.AddComponent<BoxCollider>(); var zone = go.AddComponent<TierZone>(); zone.tierName = name;
                if (slot)
                {
                    var point = new GameObject("Snap").transform; point.SetParent(go.transform);
                    point.SetPositionAndRotation(new Vector3(3.5f, 1.8f, -1.25f), Quaternion.Euler(11, 42, -7)); zone.snapPoints.Add(point);
                }
                return zone;
            }
            bool At(PrivateAnswerCard card, Vector3 position, Quaternion rotation)
            {
                var body = card.GetComponent<Rigidbody>(); var q = body.rotation;
                float norm = q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w;
                return Vector3.Distance(body.position, position) < 0.0001f && Vector3.Distance(card.transform.position, position) < 0.0001f &&
                    Quaternion.Angle(q, rotation) < 0.01f && Quaternion.Angle(card.transform.rotation, rotation) < 0.01f && Mathf.Abs(norm - 1) < 0.0001f && body.isKinematic;
            }
            Vector3 initial = new Vector3(2.75f, 1.5f, -3.2f); Quaternion orientation = Quaternion.Euler(17, 61, -23);
            var startup = Create("Startup", initial, orientation);
            Check(Quaternion.Angle((Quaternion)Get(startup, "grabStartRotation"), orientation) < 0.01f && (Vector3)Get(startup, "grabStartPosition") == initial,
                "Awake initializes fallback to the actual non-origin pose before any grab");
            startup.SetInteractionEnabled(false);
            Check(At(startup, initial, orientation), "Disabling interaction before first placement retains position and unit rotation");
            Check(report.errors.Count == 0, "Initial disable emits no native Rigidbody quaternion errors");

            var first = Create("FirstPlacement", initial, orientation); var zone = Zone("Unclassified");
            // Recreate the old uninitialized fallback independently of the new Awake safeguard.
            Set(first, "grabStartPosition", Vector3.zero); Set(first, "grabStartRotation", new Quaternion(0, 0, 0, 0));
            Check(first.ApplyPlacement(zone, true), "Forced initial placement accepts the explicit Unclassified slot");
            Check(At(first, zone.snapPoints[0].position, zone.snapPoints[0].rotation), "Forced initial placement reaches the explicit slot with a valid rotation");
            Check(report.errors.Count == 0, "Forced initial placement never restores or reads the invalid legacy fallback");
            Check(!first.GetComponent<XRGrabInteractable>().enabled, "Initial placement preserves the board interaction lock");
            var next = Zone("A"); next.snapPoints[0].SetPositionAndRotation(new Vector3(-2, 2.2f, 4), Quaternion.Euler(-9, 73, 14));
            Check(first.ApplyPlacement(next, true) && At(first, next.snapPoints[0].position, next.snapPoints[0].rotation), "Reset/forced reposition moves directly to the next slot");
            var occupied = (Dictionary<Transform, GameObject>)Get(zone, "occupiedPoints");
            Check(occupied[zone.snapPoints[0]] == null, "Forced reposition releases the previous slot");

            var inactive = Create("InitiallyHidden", initial, orientation, false); var hiddenZone = Zone("HiddenUnclassified");
            Check(inactive.ApplyPlacement(hiddenZone, true) && Vector3.Distance(inactive.transform.position, hiddenZone.snapPoints[0].position) < 0.0001f,
                "Initially hidden card receives its explicit snap Transform without a fallback restore");
            inactive.gameObject.SetActive(true);
            Check(At(inactive, hiddenZone.snapPoints[0].position, hiddenZone.snapPoints[0].rotation), "Activating an initialized hidden card synchronizes the Rigidbody to the snap pose");
            Check(report.errors.Count == 0, "Hidden-card initialization and activation emit no Unity errors");

            var failed = Create("UnavailableFirstSlot", initial, orientation); var empty = Zone("Empty", false);
            Check(!failed.ApplyPlacement(empty, true) && At(failed, initial, orientation), "Unavailable first slot safely preserves the captured initial pose");
            Check(failed.GetComponent<XRGrabInteractable>().enabled, "Rejected forced placement restores the previous interaction enable state");
            Check(report.errors.Count == 0, "Failed placement emits no quaternion errors");

            var canceled = Create("CancelBeforePlacement", initial, orientation);
            canceled.transform.SetPositionAndRotation(initial + Vector3.one, Quaternion.Euler(3, 9, 2));
            Call(canceled, "OnReleased", new SelectExitEventArgs { isCanceled = true });
            Check(At(canceled, initial, orientation), "Cancel without a prior Tier restores the real initial pose, not the origin");
            Call(canceled, "OnGrabbed", new SelectEnterEventArgs());
            canceled.transform.SetPositionAndRotation(initial + Vector3.right * 2, Quaternion.Euler(0, 120, 0));
            Call(canceled, "OnReleased", new SelectExitEventArgs { isCanceled = true });
            Check(At(canceled, initial, orientation), "Actual grab/cancel fallback still restores the captured grab pose");
            Check(report.errors.Count == 0, "All isolated startup/reset/cancel scenarios emit no Unity errors");

            // Exercise the actual scene startup plus A/B/Development and both modes; collect logs throughout.
            PilotCardSetVerification.Run();
            Check(report.errors.Count == 0, "Real-scene startup and existing pilot flow checks emit no Unity errors");
            report.passed = true;
        }
        catch (Exception error) { report.failure = error.ToString(); }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene); Application.logMessageReceived -= Log;
            Directory.CreateDirectory("Documentation"); File.WriteAllText("Documentation/PrivateAnswerCardPoseVerification.json", JsonUtility.ToJson(report, true));
        }
        if (!report.passed) throw new InvalidOperationException(report.failure + "\n" + string.Join("\n", report.errors));
        Debug.Log("[Private Card Pose Verification] PASS " + report.checks.Count + " checks, 0 Unity errors (Editor; not Quest hardware)");
    }
    static object Get(object target, string field) => target.GetType().GetField(field, Flags).GetValue(target);
    static void Set(object target, string field, object value) => target.GetType().GetField(field, Flags).SetValue(target, value);
    static void Call(object target, string method, params object[] args) => target.GetType().GetMethod(method, Flags).Invoke(target, args);
}
#endif
