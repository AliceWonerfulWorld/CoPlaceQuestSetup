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

public static class ExperimentResultVerification
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    [Serializable] private class Report { public bool passed; public string[] checks; public string failure; }
    [MenuItem("Tools/Experiment/Verify Result Waiting Flows")]
    public static void Run()
    {
        var r = new Report(); var checks = new List<string>(); var endpoints = new List<ProposedRevealVerification.Endpoint>();
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); checks.Add(name); }
        try
        {
            var a = ProposedRevealVerification.CreateEndpoint(1, 2); endpoints.Add(a);
            var b = ProposedRevealVerification.CreateEndpoint(2, 2); endpoints.Add(b);
            var exp = ProposedRevealVerification.CreateEndpoint(99, 2); endpoints.Add(exp);
            var group = endpoints.ToArray();
            Set(exp.registry, "localRole", ExperimentParticipantRegistry.ClientRole.Experimenter); Call(exp.registry, "Update");
            foreach (var e in group) Set(e.manager, "lobby", e.lobby);
            void Deliver(ProposedRevealVerification.Endpoint from) { foreach (var to in group) if (from != to) ProposedRevealVerification.Deliver(from, to); }
            foreach (var e in group) Deliver(e);
            void Tick(int count = 1)
            {
                for (int i = 0; i < count; i++) foreach (var e in group)
                {
                    Set(e.session, "nextAttempt", 0f); Call(e.registry, "Update"); e.registry.RefreshParticipants(); Call(e.session, "Update");
                    Call(e.lobby, "Update"); Call(e.manager, "Update"); Call(e.placements, "Update"); Call(e.agreement, "Update");
                    Set(e.shared, "nextInitializationAttempt", 0f); Call(e.shared, "Update");
                    Call(e.submission, "Update"); Call(e.privateBoard, "Update");
                    foreach (var ui in e.components.OfType<ExperimentModeUI>()) Call(ui, "Update");
                    Call(e.components.OfType<ExperimentLobbyUI>().Single(), "Update"); Call(e.components.OfType<FinalAgreementUI>().Single(), "Update");
                    Call(e.answers, "LateUpdate"); Set(e.reveal, "nextAttempt", 0f); Call(e.reveal, "LateUpdate"); Call(e.agreement, "LateUpdate");
                    e.components.OfType<ExperimentResultUI>().Single().Refresh();
                    Deliver(e);
                }
            }
            ExperimentLobbyUI UI(ProposedRevealVerification.Endpoint e) => e.components.OfType<ExperimentLobbyUI>().Single();
            ExperimentResultUI Result(ProposedRevealVerification.Endpoint e) => e.components.OfType<ExperimentResultUI>().Single();
            CardTierDetector[] Main(ProposedRevealVerification.Endpoint e) => e.components.OfType<CardTierDetector>().OrderBy(c => c.name).ToArray();
            Tick(3);
            Set(exp.registry, "expectedParticipantCount", 3); Tick(2);
            Check(group.All(e => e.registry.ExpectedParticipantCount == 3) &&
                ((TMP_Text)Get(UI(a), "participants")).text.Contains("2 / 3 Connected"),
                "Experimenter Inspector count change synchronizes Participant count display and registry requirement");
            Set(a.registry, "expectedParticipantCount", 8); Tick(2);
            Check(a.registry.ExpectedParticipantCount == 3, "Participant local Inspector cannot override Experimenter expected count");
            Set(a.registry, "expectedParticipantCount", 2); Set(exp.registry, "expectedParticipantCount", 2); Tick(2);
            Check(group.All(e => e.lobby.IsLobbyVisible && !e.manager.CanRunExperiment), "Boot displays Lobby and blocks experiment activation on all roles");
            Check(a.lobby.ConnectedParticipants == 2 && exp.lobby.ConnectedParticipants == 2 && !a.lobby.IsExperimenter && exp.lobby.IsExperimenter,
                "Role-based participant count excludes Experimenter and uses registry expectation");
            Check(!((GameObject)Get(UI(a), "experimenterControls")).activeSelf && ((GameObject)Get(UI(exp), "experimenterControls")).activeSelf,
                "Participant has no management panel; Experimenter sees controls");
            Check(((GameObject)Get(UI(a), "contentRoot")).GetComponentsInChildren<UnityEngine.UI.Button>().Length == 0,
                "Participant visible Lobby contains no Start/Mode/Reset buttons");
            Check(group.All(e => !e.privateBoard.CanInteract && !e.privateBoard.IsVisible && !e.placements.SendPlacement("Card_01", "A") && !Main(e)[0].Process(null, null)),
                "Lobby blocks Private, Main send and XR Grab");
            Check(group.All(e => !e.agreement.CanToggleReady && e.view.DisplayedParticipantCount == 0 &&
                Main(e).All(c => !c.GetComponent<Renderer>().enabled && !c.GetComponent<Collider>().enabled)), "Lobby hides Main/Reveal and disables Final Agreement");
            Check(group.All(e => e.components.OfType<ExperimentModeUI>().All(x => !((Canvas)Get(x, "canvas")).enabled)), "Legacy Mode management canvas is hidden during Lobby");
            Check(!a.lobby.StartExperiment() && !exp.lobby.CanStart && exp.lobby.StartBlockReason.Contains("New Session"), "Participant Start rejected; Experimenter requires a valid New Session");
            string modeBefore = exp.manager.CurrentMode.ToString(); a.lobby.SelectNormal(); Deliver(a); Tick();
            Check(exp.manager.CurrentMode.ToString() == modeBefore, "Participant cannot change mode via Lobby API");
            exp.lobby.SelectNormal(); Deliver(exp); Tick(2);
            Check(group.All(e => e.manager.CurrentMode == ExperimentManager.ExperimentMode.Normal &&
                ((TMP_Text)Get(UI(e), "modeDescription")).text == ExperimentLobbyUI.NormalDescription), "Normal mode and Japanese explanation update on every endpoint");
            exp.lobby.NewSessionFromUI(); Deliver(exp); Tick(12);
            Check(group.All(e => !e.session.IsResettingSession && e.session.SessionEpoch == 1 && e.lobby.IsLobbyVisible), "New Session completes existing Reset ACK while keeping every client in Lobby");
            Check(exp.lobby.CanStart && !a.lobby.CanStart && !b.lobby.CanStart, "Only Experimenter can Start when Session and cohort are ready");
            exp.Peer(2, ExperimentParticipantRegistry.RoleVariable, "Experimenter"); exp.registry.RefreshParticipants(); Call(UI(exp), "Update");
            Check(exp.lobby.ConnectedParticipants == 1 && !exp.lobby.CanStart && !((UnityEngine.UI.Button)Get(UI(exp), "startButton")).interactable,
                "Missing Participant disables Start button");
            exp.Peer(2, ExperimentParticipantRegistry.RoleVariable, "Participant"); exp.registry.RefreshParticipants(); Tick();
            foreach (var e in group)
                Check(UI(e).GetComponentsInChildren<TMP_Text>(true).All(t => t.font.HasCharacters(t.text)), "All Lobby text has Japanese/ASCII glyphs on client " + e.net.ClientNo);
            Capture(a, "Participant.png"); Capture(exp, "Experimenter.png");
            string session = exp.session.CurrentSessionId;
            var startButton = (UnityEngine.UI.Button)Get(UI(exp), "startButton");
            startButton.onClick.SetPersistentListenerState(0, UnityEngine.Events.UnityEventCallState.EditorAndRuntime); startButton.onClick.Invoke(); Deliver(exp); Tick(3);
            Check(group.All(e => e.lobby.IsExperimentStarted && !((GameObject)Get(UI(e), "contentRoot")).activeSelf), "Start button publishes a common marker and closes all Lobbies");
            Check(group.All(e => e.session.CurrentSessionId == session && e.session.SessionEpoch == 1), "Start never creates or resets Session automatically");
            Check(a.placements.IsSharedPlacementPhase && b.placements.IsSharedPlacementPhase && Main(a).All(c => c.GetComponent<Renderer>().enabled) &&
                group.All(e => !e.privateBoard.IsVisible && e.view.DisplayedParticipantCount == 0), "Normal Start restores Main and keeps Private/Reveal hidden");
            Check(a.placements.SendPlacement("Card_03", "A") && a.placements.SendPlacement("Card_01", "B") && a.placements.SendPlacement("Card_02", "D"), "Normal placement works in arbitrary order after Lobby Start");
            Deliver(a); Tick(2); Check(Main(b).Select(c => c.CurrentTier).SequenceEqual(new[] { "B", "D", "A" }), "Normal placements synchronize across Participant endpoints after Start");
            a.agreement.ToggleLocalReady(); Deliver(a); b.agreement.ToggleLocalReady(); Deliver(b); Tick(4);
            Check(group.All(e => e.agreement.IsBoardConfirmed), "Normal Final Agreement still confirms the shared Board");
            Check(group.All(e => Result(e).IsVisible && !e.agreement.CanToggleReady && !e.placements.CanLocalEditSharedBoard &&
                !e.components.OfType<FinalAgreementUI>().Single().GetComponent<Canvas>().enabled && Main(e).All(c => !c.Process(null, null))),
                "Normal Confirmed shows Result and disables Ready / placement / Grab");
            Check(group.All(e => e.view.DisplayedParticipantCount == 0) &&
                !((GameObject)Get(Result(a), "experimenterControls")).activeSelf && ((GameObject)Get(Result(exp), "experimenterControls")).activeSelf,
                "Normal has no Initial Answer; only Experimenter sees Result session controls");
            foreach (var e in group)
                Check(Result(e).GetComponentsInChildren<TMP_Text>(true).All(t => t.font.HasCharacters(t.text)), "Result font contains every displayed glyph / " + e.net.ClientNo);
            var finalPositions = Main(a).Select(c => c.transform.position).ToArray(); Tick(3);
            Check(Main(a).Select(c => c.transform.position).SequenceEqual(finalPositions), "Result waiting preserves Main card positions");
            CaptureResult(a, "NormalParticipant.png"); CaptureResult(exp, "NormalExperimenter.png");
            Call(b.lobby, "ClearLocal"); Call(b.lobby, "Update");
            Check(b.lobby.IsExperimentStarted && b.manager.CurrentState == ExperimentManager.ExperimentState.Confirmed, "Same Session reconnect restores Start without resetting confirmed flow");
            Result(exp).NewSessionFromUI();
            Check(!Result(exp).IsVisible, "Manual Result New Session hides Result immediately");
            Deliver(exp); Tick(12);
            Check(group.All(e => e.lobby.IsLobbyVisible && !e.agreement.IsBoardConfirmed && !e.submission.IsLocalSubmitted && e.manager.CurrentState == ExperimentManager.ExperimentState.Idle),
                "Reset returns to Lobby and clears Ready/Confirmed/Submit/State");
            Check(group.All(e => !Result(e).IsVisible), "Normal Reset clears Result on all endpoints");
            exp.lobby.SelectProposed(); Deliver(exp); Tick(2);
            Check(group.All(e => e.manager.CurrentMode == ExperimentManager.ExperimentMode.Proposed &&
                ((TMP_Text)Get(UI(e), "modeDescription")).text == ExperimentLobbyUI.ProposedDescription), "Proposed mode updates Japanese introduction on every endpoint");
            Check(exp.lobby.StartExperiment(), "Experimenter can Start Proposed from valid current Session"); Deliver(exp); Tick(3);
            Check(a.privateBoard.IsVisible && a.privateBoard.CanInteract && b.privateBoard.CanInteract && !exp.privateBoard.IsVisible &&
                group.All(e => Main(e).All(c => !c.GetComponent<Renderer>().enabled)), "Proposed Start enables only Participant Private boards and keeps Main hidden");
            foreach (var e in new[] { a, b }) for (int c = 0; c < 3; c++) e.privateBoard.TryPlaceCard("Card_0" + (c + 1), e == a ? new[] { "A", "B", "C" }[c] : new[] { "D", "B", "A" }[c]);
            Tick(3); Check(group.All(e => !e.reveal.HasRevealed), "Proposed retains explicit Submit requirement after Lobby");
            Check(a.submission.TrySubmit() && b.submission.TrySubmit(), "Participants submit Private answers after Start"); Tick(12);
            Check(group.All(e => e.reveal.HasRevealed && e.shared.IsActive && e.view.DisplayedParticipantCount == 2 && !e.lobby.IsLobbyVisible),
                "Proposed Submit / ACK Reveal / readonly references / Main shared phase all work after Lobby");
            Check(b.placements.SendPlacement("Card_03", "C") && b.placements.SendPlacement("Card_01", "D") && b.placements.SendPlacement("Card_02", "A"), "Proposed Main accepts shared final placements");
            Deliver(b); Tick(2); Check(Main(a).Select(c => c.CurrentTier).SequenceEqual(new[] { "D", "A", "C" }), "Proposed Main placements synchronize after Reveal");
            a.agreement.ToggleLocalReady(); Deliver(a); b.agreement.ToggleLocalReady(); Deliver(b); Tick(4);
            Check(group.All(e => e.agreement.IsBoardConfirmed && e.view.DisplayedParticipantCount == 2), "Proposed Final Agreement preserves readonly initial reference UI");
            Check(group.All(e => Result(e).IsVisible && !e.agreement.CanToggleReady && !e.placements.CanLocalEditSharedBoard && Main(e).All(c => !c.Process(null, null))),
                "Proposed Confirmed shows Result alongside readonly Initial Answers and locked Main");
            CaptureResult(a, "ProposedParticipant.png");
            string previousStart = exp.net.GetGlobalVariable(ExperimentLobbyController.StartVariable);
            exp.lobby.NewSessionFromUI(); Deliver(exp); Tick(12);
            Check(group.All(e => e.lobby.IsLobbyVisible && e.view.DisplayedParticipantCount == 0 && Main(e).All(c => c.CurrentTier == "Unclassified")),
                "Reset after Proposed removes references, clears Main and returns all clients to Lobby");
            Check(group.All(e => !Result(e).IsVisible), "Proposed Reset removes Result on every endpoint");
            exp.Global(ExperimentLobbyController.StartVariable, previousStart); Deliver(exp); Tick(2);
            Check(group.All(e => e.lobby.IsLobbyVisible && !e.manager.CanRunExperiment), "Delayed previous-epoch Start cannot bypass Lobby after Reset");
            var connection = Get(a.net, "_connectionManager"); connection.GetType().GetMethod("Disconnect").Invoke(connection, null); Call(UI(a), "Update");
            Check(!a.lobby.IsConnected && ((TMP_Text)Get(UI(a), "connection")).text.Contains("Waiting") && a.lobby.ConnectedParticipants == 0,
                "Disconnected Lobby shows textual Waiting and zero connected count");
            r.passed = true;
        }
        catch (Exception error) { r.failure = error.ToString(); Debug.LogException(error); }
        finally
        {
            foreach (var e in endpoints) EditorSceneManager.ClosePreviewScene(e.scene);
            r.checks = checks.ToArray(); Directory.CreateDirectory("Library/ExperimentResultVerification");
            File.WriteAllText("Library/ExperimentResultVerification/result.json", JsonUtility.ToJson(r, true));
            Debug.Log($"[Result Verification] {(r.passed ? "PASS" : "FAIL")} / {checks.Count}");
        }
    }
    private static void CaptureResult(ProposedRevealVerification.Endpoint e, string name)
    {
        var result = e.components.OfType<ExperimentResultUI>().Single();
        var board = e.components.OfType<Canvas>().Single(c => c.name == "TierBoardCanvas" && c.transform.IsChildOf(result.transform.parent));
        var go = new GameObject("ResultPreviewCamera"); SceneManager.MoveGameObjectToScene(go, e.scene);
        var cam = go.AddComponent<Camera>(); cam.enabled = false;
        cam.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(e.scene);
        cam.transform.position = board.transform.position - board.transform.forward * 6 + board.transform.up * 0.4f;
        cam.transform.rotation = board.transform.rotation; cam.orthographic = true; cam.orthographicSize = 2;
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.018f, 0.027f, 0.038f);
        foreach (var graphic in result.GetComponentsInChildren<RoundedPanelGraphic>())
        { graphic.SetAllDirty(); graphic.Rebuild(UnityEngine.UI.CanvasUpdate.PreRender); }
        foreach (var text in result.GetComponentsInChildren<TMP_Text>()) text.ForceMeshUpdate();
        Canvas.ForceUpdateCanvases();
        var render = new RenderTexture(2200, 1400, 24); var tex = new Texture2D(2200, 1400, TextureFormat.RGB24, false); var old = RenderTexture.active;
        try
        {
            cam.targetTexture = render; cam.Render(); RenderTexture.active = render;
            tex.ReadPixels(new Rect(0, 0, 2200, 1400), 0, 0); tex.Apply();
            Directory.CreateDirectory("Library/ExperimentResultVerification");
            File.WriteAllBytes("Library/ExperimentResultVerification/" + name, tex.EncodeToPNG());
        }
        finally { RenderTexture.active = old; cam.targetTexture = null; UnityEngine.Object.DestroyImmediate(tex); UnityEngine.Object.DestroyImmediate(render); UnityEngine.Object.DestroyImmediate(go); }
    }
    private static void Capture(ProposedRevealVerification.Endpoint e, string name)
    {
        var go = new GameObject("LobbyPreviewCamera"); SceneManager.MoveGameObjectToScene(go, e.scene);
        var cam = go.AddComponent<Camera>(); cam.enabled = false; cam.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(e.scene);
        var pos = e.lobby.transform.position; cam.transform.position = pos + new Vector3(e.lobby.IsExperimenter ? 0.34f : 0, 0, -2); cam.transform.rotation = e.lobby.transform.rotation;
        cam.orthographic = true; cam.orthographicSize = 0.94f; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.018f, 0.027f, 0.038f);
        Canvas.ForceUpdateCanvases(); var render = new RenderTexture(1800, 1200, 24); var tex = new Texture2D(1800, 1200, TextureFormat.RGB24, false); var old = RenderTexture.active;
        try
        {
            cam.targetTexture = render; cam.Render(); RenderTexture.active = render; tex.ReadPixels(new Rect(0, 0, 1800, 1200), 0, 0); tex.Apply();
            Directory.CreateDirectory("Library/ExperimentResultVerification"); File.WriteAllBytes("Library/ExperimentResultVerification/" + name, tex.EncodeToPNG());
        }
        finally { RenderTexture.active = old; cam.targetTexture = null; UnityEngine.Object.DestroyImmediate(tex); UnityEngine.Object.DestroyImmediate(render); UnityEngine.Object.DestroyImmediate(go); }
    }
    private static object Get(object obj, string name) => obj.GetType().GetField(name, Flags).GetValue(obj);
    private static void Set(object obj, string name, object value) => obj.GetType().GetField(name, Flags).SetValue(obj, value);
    private static void Call(object obj, string name) => obj.GetType().GetMethod(name, Flags).Invoke(obj, null);
}
#endif
