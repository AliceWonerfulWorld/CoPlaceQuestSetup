#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Reuses the installed NetSync/production-component endpoints from Reveal QA.
public static class ProposedSharedPlacementVerification
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    [Serializable] private class Report { public bool passed; public string[] checks; public string failure; }
    [MenuItem("Tools/Experiment/Verify Proposed Shared Placement")]
    public static void Run()
    {
        var report = new Report(); var checks = new List<string>();
        var endpoints = new List<ProposedRevealVerification.Endpoint>();
        void Check(bool result, string name) { if (!result) throw new Exception(name); checks.Add(name); }
        try
        {
            var a = ProposedRevealVerification.CreateEndpoint(1, 2); endpoints.Add(a);
            var b = ProposedRevealVerification.CreateEndpoint(2, 2); endpoints.Add(b);
            var pair = new[] { a, b };
            void Deliver(ProposedRevealVerification.Endpoint from, ProposedRevealVerification.Endpoint to) => ProposedRevealVerification.Deliver(from, to);
            void Tick(int frames = 1)
            {
                for (int frame = 0; frame < frames; frame++)
                {
                    foreach (var e in pair)
                    {
                        Call(e.submission, "Update"); Call(e.placements, "Update"); Call(e.agreement, "Update");
                        Set(e.shared, "nextInitializationAttempt", 0f); Call(e.shared, "Update");
                        Call(e.privateBoard, "Update"); Call(e.components.OfType<FinalAgreementUI>().Single(), "Update");
                        Call(e.answers, "LateUpdate"); Set(e.reveal, "nextAttempt", 0f); Call(e.reveal, "LateUpdate");
                        Call(e.agreement, "LateUpdate");
                    }
                    Deliver(a, b); Deliver(b, a);
                }
            }
            CardTierDetector[] Main(ProposedRevealVerification.Endpoint e) => e.components.OfType<CardTierDetector>().OrderBy(c => c.name).ToArray();
            // Seed a pre-existing Normal board in the same Session to prove the
            // Proposed handover starts empty instead of inheriting/copying it.
            foreach (var e in pair) Set(e.manager, "currentMode", ExperimentManager.ExperimentMode.Normal);
            Check(a.placements.SendPlacement("Card_01", "D") && a.placements.SendPlacement("Card_02", "A") && a.placements.SendPlacement("Card_03", "B"), "Existing Normal board can be populated before Proposed");
            Deliver(a, b); Call(a.agreement, "Update"); Call(b.agreement, "Update");
            a.agreement.ToggleLocalReady(); Deliver(a, b);
            Check(a.agreement.IsLocalReady, "Old Normal readiness exists before handover");
            a.net.SetGlobalVariable("experimentMode", "Proposed"); Deliver(a, b); Tick(2);
            Check(pair.All(e => !e.shared.IsActive && !e.placements.SendPlacement("Card_01", "C") && !Main(e)[0].Process(null, null)), "Main send and XR selection are blocked before Reveal");
            Check(pair.All(e => !e.agreement.CanToggleReady), "Final Agreement disabled during Private phase");
            Check(pair.All(e => Main(e).All(c => !c.GetComponent<Renderer>().enabled && !c.GetComponent<Collider>().enabled)), "Main visuals/colliders remain hidden before Reveal");
            foreach (var e in pair)
            {
                string[] tiers = e == a ? new[] { "A", "B", "C" } : new[] { "D", "B", "A" };
                for (int i = 0; i < 3; i++) Check(e.privateBoard.TryPlaceCard("Card_0" + (i + 1), tiers[i]), "Private answer client " + e.net.ClientNo + " / Card_0" + (i + 1));
            }
            foreach (var e in pair) Check(e.submission.TrySubmit(), "Explicit independent Submit before shared placement / client " + e.net.ClientNo);
            Tick(8);
            Check(pair.All(e => e.reveal.HasRevealed && e.shared.IsActive && e.manager.CurrentState == ExperimentManager.ExperimentState.Discussion), "Reveal opens Proposed Discussion/shared placement on both endpoints");
            Check(pair.All(e => Main(e).All(c => c.CurrentTier == "Unclassified") &&
                e.placements.CardIds.All(id => SessionVariableTransport.GetGlobalVariable(e.net, e.session, "normalPlacement_" + id) == "Unclassified")), "All Main cards start Unclassified despite old Normal and Private answers");
            Check(pair.All(e => Main(e).All(c => c.GetComponent<Renderer>().enabled && c.GetComponent<Collider>().enabled && c.Process(null, null))), "Main visuals/colliders/XR selection enabled after initialization echoes");
            Check(pair.All(e => !e.privateBoard.IsVisible && !e.privateBoard.CanInteract && e.view.DisplayedParticipantCount == 2), "Private hidden/locked while both readonly reference panels remain visible");
            Check(pair.All(e => !e.agreement.IsLocalReady && !e.agreement.IsBoardConfirmed), "Old readiness and Board Confirmed are cleared for new shared phase");
            Check(pair.All(e => e.view.GetComponentsInChildren<Collider>(true).Length == 0 &&
                e.view.GetComponentsInChildren<UnityEngine.UI.Selectable>(true).Length == 0 &&
                e.view.GetComponentsInChildren<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>(true).Length == 0), "References are readonly world-space UI with no physical interaction");
            string initial = JsonUtility.ToJson(a.reveal.GetRevealedSnapshot());
            Check(a.placements.SendPlacement("Card_03", "D"), "Editor-side shared Card_03 placement accepted first"); Deliver(a, b); Tick();
            Check(Main(b)[2].CurrentTier == "D", "Editor -> peer Main placement applies");
            Check(b.placements.SendPlacement("Card_01", "B"), "Peer-side shared Card_01 placement accepted"); Deliver(b, a); Tick();
            Check(Main(a)[0].CurrentTier == "B", "Peer -> Editor Main placement applies");
            Check(b.placements.SendPlacement("Card_03", "Unclassified"), "Shared card can return to Unclassified"); Deliver(b, a); Tick();
            Check(pair.All(e => Main(e)[2].CurrentTier == "Unclassified"), "Shared Unclassified synchronizes to both endpoints");
            Check(b.placements.SendPlacement("Card_03", "D") && a.placements.SendPlacement("Card_02", "A"), "Shared cards can be placed again in arbitrary order");
            // Each latest global write is delivered before another writer sends.
            // Restore b's Card_03 write then a's Card_02 write without stale export.
            Deliver(b, a); a.placements.SendPlacement("Card_02", "A"); Deliver(a, b); Tick(2);
            Check(pair.All(e => Main(e).Select(c => c.CurrentTier).SequenceEqual(new[] { "B", "A", "D" })), "All shared cards agree on B/A/D");
            Check(JsonUtility.ToJson(a.reveal.GetRevealedSnapshot()) == initial && JsonUtility.ToJson(b.reveal.GetRevealedSnapshot()) == initial,
                "Shared changes never modify initial answer snapshots");
            // Exercise the same keyboard placement helper as F6/F7/F8 + digits.
            var keyboard = typeof(NormalPlacementSync).GetMethod("TryPlaceEditorTestCard", Flags);
            Check((bool)keyboard.Invoke(a.placements, new object[] { "Card_02", "C" }), "Editor shared keyboard placement path supports Proposed"); Deliver(a, b); Tick(2);
            a.agreement.ToggleLocalReady(); Deliver(a, b); Tick();
            Check(a.agreement.IsLocalReady && !Main(a)[0].Process(null, null) && !a.placements.SendPlacement("Card_01", "A"), "Ready Participant cannot Grab or move Main");
            Check(b.placements.SendPlacement("Card_01", "C"), "Other Participant can still change shared board"); Deliver(b, a); Tick(2);
            Check(pair.All(e => !e.agreement.IsLocalReady), "Board change invalidates/clears readiness on every Participant");
            Check(Main(a)[0].CurrentTier == "C", "Remote placement still updates a previously Ready Participant");
            a.agreement.ToggleLocalReady(); Deliver(a, b); b.agreement.ToggleLocalReady(); Deliver(b, a); Tick(3);
            Check(pair.All(e => e.agreement.IsBoardConfirmed && e.manager.CurrentState == ExperimentManager.ExperimentState.Confirmed &&
                e.placements.CardIds.All(id => e.placements.IsCardConfirmed(id))), "All Participant Ready -> Proposed Board Confirmed on both endpoints");
            Check(pair.All(e => !e.placements.SendPlacement("Card_01", "B") && !Main(e)[0].Process(null, null)), "Confirmed shared board rejects further placement and XR Grab");
            Check(pair.All(e => e.view.DisplayedParticipantCount == 2 && JsonUtility.ToJson(e.reveal.GetRevealedSnapshot()) == initial), "Initial answer references remain visible and unchanged after final confirmation");
            Capture(a);
            Call(a.shared, "ClearLocal"); Call(a.reveal, "ClearLocal"); Call(a.reveal, "LateUpdate"); Call(a.shared, "Update"); Call(a.privateBoard, "Update");
            Check(a.shared.IsActive && a.agreement.IsBoardConfirmed && Main(a).Select(c => c.CurrentTier).SequenceEqual(new[] { "C", "C", "D" }) &&
                a.view.DisplayedParticipantCount == 2, "Reconnect restores final shared board, confirmation and initial references without resetting Main");
            Check(a.manager.CurrentState == ExperimentManager.ExperimentState.Confirmed, "Reveal reconnect does not regress Confirmed to Revealed");
            Set(a.registry, "localRole", ExperimentParticipantRegistry.ClientRole.Experimenter); Call(a.registry, "Update");
            Check(!a.placements.CanLocalEditSharedBoard && !Main(a)[0].Process(null, null), "Experimenter can view references/Main but cannot edit Proposed Main");
            Check(a.session.StartNewSession(), "Experimenter can reset after Proposed final confirmation"); Deliver(a, b);
            for (int i = 0; i < 8; i++)
            {
                foreach (var e in pair)
                {
                    if (e.session.CurrentSessionId != "") e.Peer(99, ExperimentSessionManager.AckVariable, e.session.CurrentSessionId);
                    Set(e.session, "nextAttempt", 0f); Call(e.session, "Update");
                }
                Deliver(a, b); Deliver(b, a);
            }
            Tick(2);
            Check(pair.All(e => !e.session.IsResettingSession && e.session.SessionEpoch == 1 && !e.shared.IsActive &&
                !e.reveal.HasRevealed && e.view.DisplayedParticipantCount == 0 && e.manager.CurrentState == ExperimentManager.ExperimentState.Idle), "Session Reset clears shared phase, Reveal references and State on both endpoints");
            Check(pair.All(e => Main(e).All(c => c.CurrentTier == "Unclassified") && e.components.OfType<PrivateAnswerCard>().All(c => c.CurrentTier == "Unclassified") &&
                !e.agreement.IsBoardConfirmed && !e.agreement.IsLocalReady), "Session Reset clears Main, Private, Ready and Board Confirmed");
            Set(a.registry, "localRole", ExperimentParticipantRegistry.ClientRole.Participant); Call(a.registry, "Update"); Deliver(a, b);
            a.net.SetGlobalVariable("experimentMode", "Normal"); Deliver(a, b); Tick();
            Check(pair.All(e => e.placements.IsNormalMode && !e.privateBoard.IsVisible && e.view.DisplayedParticipantCount == 0 && Main(e).All(c => c.GetComponent<Renderer>().enabled)), "Normal restores Main with no Private or Reveal UI");
            Check(a.placements.SendPlacement("Card_01", "A") && a.placements.SendPlacement("Card_02", "B") && a.placements.SendPlacement("Card_03", "C"), "Normal placement still writes all cards after Proposed reset");
            Deliver(a, b); Tick(2); a.agreement.ToggleLocalReady(); Deliver(a, b); b.agreement.ToggleLocalReady(); Deliver(b, a); Tick(3);
            Check(pair.All(e => e.agreement.IsBoardConfirmed && e.manager.CurrentState == ExperimentManager.ExperimentState.Confirmed), "Normal two-participant Ready / Final Agreement / Board Confirmed regression passes");
            report.passed = true;
        }
        catch (Exception error) { report.failure = error.ToString(); Debug.LogException(error); }
        finally
        {
            foreach (var e in endpoints) EditorSceneManager.ClosePreviewScene(e.scene);
            report.checks = checks.ToArray(); Directory.CreateDirectory("Library/ProposedSharedPlacementVerification");
            File.WriteAllText("Library/ProposedSharedPlacementVerification/result.json", JsonUtility.ToJson(report, true));
            Debug.Log($"[Proposed Shared Placement Verification] {(report.passed ? "PASS" : "FAIL")} / {checks.Count} checks");
        }
    }
    private static void Capture(ProposedRevealVerification.Endpoint e)
    {
        var go = new GameObject("SharedPlacementPreviewCamera"); SceneManager.MoveGameObjectToScene(go, e.scene);
        var camera = go.AddComponent<Camera>(); camera.enabled = false; camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(e.scene);
        camera.transform.position = new Vector3(-0.25f, 1.98f, -5); camera.transform.rotation = Quaternion.identity;
        camera.orthographic = true; camera.orthographicSize = 2.35f; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(0.018f, 0.030f, 0.044f);
        Canvas.ForceUpdateCanvases();
        var render = new RenderTexture(1800, 1200, 24); var image = new Texture2D(1800, 1200, TextureFormat.RGB24, false); var previous = RenderTexture.active;
        try
        {
            camera.targetTexture = render; camera.Render(); RenderTexture.active = render;
            image.ReadPixels(new Rect(0, 0, render.width, render.height), 0, 0); image.Apply();
            Directory.CreateDirectory("Library/ProposedSharedPlacementVerification"); File.WriteAllBytes("Library/ProposedSharedPlacementVerification/SharedPlacement.png", image.EncodeToPNG());
        }
        finally { RenderTexture.active = previous; camera.targetTexture = null; UnityEngine.Object.DestroyImmediate(image); UnityEngine.Object.DestroyImmediate(render); UnityEngine.Object.DestroyImmediate(go); }
    }
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, Flags).SetValue(target, value);
    private static void Call(object target, string name) => target.GetType().GetMethod(name, Flags).Invoke(target, null);
}
#endif
