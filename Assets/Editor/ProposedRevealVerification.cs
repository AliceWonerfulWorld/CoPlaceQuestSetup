#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Styly.NetSync;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Multiple isolated endpoints using the installed NetSync offline transport.
// Deliver simulates server delivery; all decisions/UI use production components.
public static class ProposedRevealVerification
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    [Serializable] private class Report { public bool passed; public string[] checks; public string failure; }
    public sealed class Endpoint
    {
        public Scene scene;
        public NetSyncManager net;
        public object variables;
        public ExperimentManager manager;
        public ExperimentParticipantRegistry registry;
        public ExperimentSessionManager session;
        public CandidateSyncTest answers;
        public PrivateTierBoardController privateBoard;
        public ProposedRevealCoordinator reveal;
        public ReadonlyParticipantBoardView view;
        public NormalPlacementSync placements;
        public FinalAgreementManager agreement;
        public ProposedSharedPlacementController shared;
        public IndependentAnswerSubmission submission;
        public Component[] components;
        public void Peer(int client, string key, string value) => variables.GetType().GetMethod("SetClientVariable").Invoke(variables, new object[] { key, value, client, "reveal-test" });
        public void Global(string key, string value) => variables.GetType().GetMethod("SetGlobalVariable").Invoke(variables, new object[] { key, value, "reveal-test" });
    }
    [MenuItem("Tools/Experiment/Verify Proposed Readonly Reveal")]
    public static void Run()
    {
        var report = new Report(); var checks = new List<string>(); var endpoints = new List<Endpoint>();
        void Check(bool value, string name) { if (!value) throw new Exception(name); checks.Add(name); }
        try
        {
            Endpoint[] Create(int count)
            {
                var result = new Endpoint[count];
                for (int i = 0; i < count; i++) { result[i] = CreateEndpoint(i + 1, count); endpoints.Add(result[i]); }
                return result;
            }
            void Tick(Endpoint[] group, int frames = 1)
            {
                for (int frame = 0; frame < frames; frame++)
                {
                    // Manual preview ticks do not advance Unity's clock. Remove
                    // only the one-second retry throttle, preserving the barrier.
                    foreach (var e in group) { Set(e.reveal, "nextAttempt", 0f); Call(e.submission, "Update"); Call(e.answers, "LateUpdate"); Call(e.reveal, "LateUpdate"); }
                    foreach (var from in group) foreach (var to in group) if (from != to) Deliver(from, to);
                    foreach (var e in group) Call(e.privateBoard, "Update");
                }
            }
            var pair = Create(2); var p1 = pair[0]; var p2 = pair[1];
            var initialMain = pair.Select(e => e.components.OfType<CardTierDetector>().Select(c => (c.transform.position, c.CurrentTier)).ToArray()).ToArray();
            Check(p1.privateBoard.TryPlaceCard("Card_01", "A") && p1.privateBoard.TryPlaceCard("Card_02", "B") && p1.privateBoard.TryPlaceCard("Card_03", "C"), "P1 answers all cards A/B/C");
            Check(p2.privateBoard.TryPlaceCard("Card_01", "D") && p2.privateBoard.TryPlaceCard("Card_02", "B"), "P2 answers D/B with Card_03 unanswered");
            Tick(pair, 3);
            Check(pair.All(e => !e.reveal.HasRevealed && !e.reveal.AreAllAnswersComplete() && e.view.DisplayedParticipantCount == 0), "One unanswered card prevents every endpoint from Revealing");
            Check(pair.All(e => ((TMP_Text)Get(e.answers, "candidateStatusText")).text == "" &&
                ((IDictionary)Get(e.components.OfType<CandidateMarkerManager>().Single(), "markers")).Count == 0), "No legacy markers/status/mini boards disclose tiers before Reveal");
            Check(p1.submission.CanSubmit && !p2.submission.CanSubmit && !p2.submission.TrySubmit(), "Only a Participant with all valid answers can Submit");
            Check(p1.privateBoard.TryPlaceCard("Card_01", "Unclassified"), "Completed local answer can cancel before Reveal");
            Check(p2.privateBoard.TryPlaceCard("Card_03", "A"), "P2 answers last card"); Tick(pair, 3);
            Check(pair.All(e => !e.reveal.HasRevealed), "Cancellation keeps the all-card barrier false");
            Check(p1.privateBoard.TryPlaceCard("Card_01", "A"), "Cancelled answer can answer again");
            foreach (var from in pair) foreach (var to in pair) if (from != to) Deliver(from, to);
            Tick(pair, 3);
            Check(pair.All(e => e.reveal.AreAllAnswersComplete() && !e.reveal.HasRevealed && !e.reveal.IsAnswerLocked && e.view.DisplayedParticipantCount == 0), "All answered without Submit never starts Reveal");
            Check(p1.submission.TrySubmit(), "P1 independently submits"); Tick(pair, 2);
            Check(!p1.privateBoard.CanInteract && !p1.privateBoard.TryPlaceCard("Card_01", "D") && !p1.answers.TrySetCandidate("Card_01", "Unclassified", out _), "Submit locks XR/Editor and candidate cancellation data API");
            Check(pair.All(e => !e.reveal.HasRevealed) && p2.privateBoard.CanInteract, "Only P1 Submitted never Reveals and P2 remains editable");
            Call(p1.submission, "ClearLocal");
            Check(p1.submission.IsLocalSubmitted && !p1.privateBoard.CanInteract, "Reconnect restores submitted consent and private lock from existing Client Variables");
            Check(p1.submission.TryCancel(), "Participant can cancel consent before Reveal"); Tick(pair, 2);
            Check(p1.privateBoard.CanInteract && !p1.submission.IsLocalSubmitted && pair.All(e => !e.reveal.HasRevealed), "Cancellation echo restores editing without revealing");
            Check(p1.submission.TrySubmit() && p2.submission.TrySubmit(), "Both Participants explicitly Submit");
            foreach (var from in pair) foreach (var to in pair) if (from != to) Deliver(from, to);
            Set(p1.reveal, "nextAttempt", 0f); Call(p1.reveal, "LateUpdate"); Deliver(p1, p2);
            Call(p1.reveal, "LateUpdate");
            Check(p1.reveal.IsAnswerLocked && !p1.reveal.HasRevealed && p1.view.DisplayedParticipantCount == 0, "Preparation locks accepted answers but exposes no boards before peer ACK");
            Tick(pair, 5);
            Check(pair.All(e => e.reveal.HasRevealed && e.manager.CurrentState == ExperimentManager.ExperimentState.Revealed), "Shared commit moves both endpoints to Revealed");
            Check(p1.reveal.GetRevealedSnapshot().id == p2.reveal.GetRevealedSnapshot().id && pair.All(e => e.view.DisplayedParticipantCount == 2), "Both endpoints show both boards from the same Reveal ID");
            Capture(p1);
            foreach (var e in pair)
            {
                var s = e.reveal.GetRevealedSnapshot();
                Check(s.participants[0].answers.Select(a => a.tier).SequenceEqual(new[] { "A", "B", "C" }) &&
                    s.participants[1].answers.Select(a => a.tier).SequenceEqual(new[] { "D", "B", "A" }), "Committed snapshot preserves P1 A/B/C and P2 D/B/A on client " + e.net.ClientNo);
                foreach (var p in s.participants)
                    foreach (var a in p.answers)
                        Check(e.view.transform.Find("ReadonlyMiniBoards/P" + p.displayId + "_InitialAnswer/Tier_" + a.tier + "/Cards/" + a.cardId) != null,
                            "Correct UI Tier parent: client " + e.net.ClientNo + " / P" + p.displayId + " / " + a.cardId);
                Check(!e.submission.CanCancel && !e.submission.TryCancel(), "Submit cancellation rejected after Reveal on client " + e.net.ClientNo);
                Check(!e.privateBoard.CanInteract && !e.privateBoard.TryPlaceCard("Card_01", "D"), "Private edits rejected after Reveal on client " + e.net.ClientNo);
                Check(e.view.GetComponentsInChildren<Collider>(true).Length == 0 &&
                    e.view.GetComponentsInChildren<UnityEngine.UI.Selectable>(true).Length == 0 &&
                    e.view.GetComponentsInChildren<UnityEngine.UI.GraphicRaycaster>(true).Length == 0 &&
                    e.view.GetComponentsInChildren<UnityEngine.UI.Graphic>(true).All(g => !g.raycastTarget), "Mini boards are strictly readonly on client " + e.net.ClientNo);
                Check(e.view.GetComponentsInChildren<UnityEngine.UI.RawImage>(true).Count(i => i.texture != null) == 6,
                    "All six card visuals reuse existing images on client " + e.net.ClientNo);
            }
            Check(pair.Select((e, p) => e.components.OfType<CardTierDetector>().Select((c, i) => c.transform.position == initialMain[p][i].position && c.CurrentTier == initialMain[p][i].CurrentTier).All(v => v)).All(v => v), "Private + Reveal leave Main positions and tiers unchanged");
            var copy = p1.reveal.GetRevealedSnapshot(); copy.participants[0].answers[0].tier = "D";
            Check(p1.reveal.GetRevealedSnapshot().participants[0].answers[0].tier == "A", "Public snapshot copies cannot mutate committed answers");
            p1.Peer(2, "candidate_Card_01", SessionVariableTransport.Encode(p1.session, "C"));
            Call(p1.reveal, "LateUpdate");
            Check(p1.reveal.GetRevealedSnapshot().participants[1].answers[0].tier == "D", "Later answer packets do not mutate the initial Reveal snapshot");
            Call(p1.reveal, "ClearLocal"); Call(p1.reveal, "LateUpdate");
            Check(p1.reveal.HasRevealed && p1.view.DisplayedParticipantCount == 2 && p1.reveal.GetRevealedSnapshot().participants[1].answers[0].tier == "D", "Reconnect restores committed boards independently of mutable candidate data");
            string oldCommit = p1.net.GetGlobalVariable(ProposedRevealCoordinator.RevealVariable);
            Set(p1.registry, "localRole", ExperimentParticipantRegistry.ClientRole.Experimenter); Call(p1.registry, "Update");
            Check(p1.session.StartNewSession(), "Experimenter can New Session after Reveal"); Deliver(p1, p2);
            for (int i = 0; i < 6; i++)
            {
                foreach (var e in pair)
                {
                    // Session Reset requires every live client (including the
                    // passive simulated Experimenter) to acknowledge reset.
                    if (e.session.CurrentSessionId != "") e.Peer(99, ExperimentSessionManager.AckVariable, e.session.CurrentSessionId);
                    Set(e.session, "nextAttempt", 0f); Call(e.session, "Update");
                }
                foreach (var from in pair) foreach (var to in pair) if (from != to) Deliver(from, to);
            }
            Check(pair.All(e => !e.session.IsResettingSession && e.session.SessionEpoch == 1 && e.manager.CurrentState == ExperimentManager.ExperimentState.Idle), "Both endpoints reset Epoch and ExperimentState");
            Check(pair.All(e => !e.reveal.HasRevealed && e.view.DisplayedParticipantCount == 0 && e.components.OfType<PrivateAnswerCard>().All(c => c.CurrentTier == "Unclassified")), "Session reset removes every Mini Board and resets Private positions");
            Check(pair.All(e => e.answers.GetParticipantCandidates(e.net.ClientNo).Values.All(a => !a.Answered && a.Tier == "Unclassified" && a.SelectedAt == "")), "Session reset clears candidate / answered / selectedAt on both clients");
            Check(pair.All(e => !e.submission.IsLocalSubmitted && !e.submission.IsLocalLocked && SessionVariableTransport.GetClientVariable(e.net, e.session, IndependentAnswerSubmission.SubmittedVariable) == "false"), "Session Reset clears independent Submit and private locks");
            p1.Global(ProposedRevealCoordinator.RevealVariable, oldCommit); Call(p1.reveal, "LateUpdate");
            Check(!p1.reveal.HasRevealed && p1.view.DisplayedParticipantCount == 0, "Delayed previous-session Reveal cannot recreate old boards");
            var trio = Create(3);
            for (int p = 0; p < 3; p++) for (int c = 0; c < 3; c++)
                if (p != 2 || c != 2) Check(trio[p].privateBoard.TryPlaceCard("Card_0" + (c + 1), new[] { "A", "B", "D" }[p]), "Three-participant setup answer P" + (p + 1) + " / card " + (c + 1));
            Tick(trio, 3);
            Check(trio.All(e => !e.reveal.HasRevealed), "P3's missing card prevents three-participant Reveal");
            Check(trio[2].privateBoard.TryPlaceCard("Card_03", "D"), "P3 answers last card"); Tick(trio, 2);
            Check(trio.All(e => !e.reveal.HasRevealed), "Three completed participants still require explicit Submit");
            foreach (var e in trio) Check(e.submission.TrySubmit(), "Submit participant " + e.net.ClientNo); Tick(trio, 6);
            Check(trio.All(e => e.reveal.HasRevealed && e.view.DisplayedParticipantCount == 3), "Dynamic three-participant Reveal and Grid succeed");
            Check(trio.All(e => e.reveal.GetRevealedSnapshot().participants.Length == 3) &&
                trio[0].net.GetGlobalVariable(ProposedRevealCoordinator.RevealVariable).Length <= 1024, "Compressed three-participant payload respects actual NetSync 1024-character limit");
            Check(trio.All(e => e.reveal.GetRevealedSnapshot().participants.All(p => p.clientNo != 99)), "Experimenter client 99 is excluded from completion and UI");
            var race = Create(2);
            for (int p = 0; p < 2; p++) foreach (string id in race[p].answers.AnswerCardIds) race[p].privateBoard.TryPlaceCard(id, "A");
            foreach (var e in race) e.submission.TrySubmit();
            Deliver(race[1], race[0]); Set(race[0].reveal, "nextAttempt", 0f); Call(race[0].reveal, "LateUpdate"); Deliver(race[0], race[1]);
            Check(race[1].submission.TryCancel(), "In-flight Submit cancellation revokes consent before ACK"); Call(race[1].submission, "Update");
            Check(race[1].privateBoard.TryPlaceCard("Card_03", "Unclassified"), "In-flight cancellation before preparation ACK is accepted");
            Tick(race, 3);
            Check(race.All(e => !e.reveal.HasRevealed && !e.reveal.IsAnswerLocked && e.view.DisplayedParticipantCount == 0), "In-flight cancellation invalidates preparation and unlocks everyone without leaking");
            race[1].privateBoard.TryPlaceCard("Card_03", "D"); race[1].submission.TrySubmit(); Deliver(race[1], race[0]);
            Set(race[0].reveal, "nextAttempt", 0f); Call(race[0].reveal, "LateUpdate");
            Call(race[0].reveal, "LateUpdate");
            Set(race[0].reveal, "preparationStarted", Time.unscaledTime - 30);
            Call(race[0].reveal, "LateUpdate"); Deliver(race[0], race[1]); Call(race[1].reveal, "LateUpdate");
            Check(race.All(e => !e.reveal.IsAnswerLocked && !e.reveal.HasRevealed), "Missing ACK timeout clears preparation and unlocks answers");
            Tick(race, 6);
            Check(race.All(e => e.reveal.HasRevealed && e.reveal.GetRevealedSnapshot().participants[1].answers[2].tier == "D"), "Retry Reveals the updated snapshot after cancellation and timeout");
            Check(p1.view.GetComponentsInChildren<RoundedPanelGraphic>(true).Length == 0, "Reset leaves no active rounded Mini Board graphics");
            report.passed = true;
        }
        catch (Exception error) { report.failure = error.ToString(); Debug.LogException(error); }
        finally
        {
            foreach (var e in endpoints) EditorSceneManager.ClosePreviewScene(e.scene);
            report.checks = checks.ToArray(); Directory.CreateDirectory("Library/ProposedRevealVerification");
            File.WriteAllText("Library/ProposedRevealVerification/result.json", JsonUtility.ToJson(report, true));
            Debug.Log($"[Proposed Reveal Verification] {(report.passed ? "PASS" : "FAIL")} / {checks.Count} checks");
        }
    }
    public static Endpoint CreateEndpoint(int client, int count)
    {
        var e = new Endpoint { scene = EditorSceneManager.OpenPreviewScene("Assets/Scenes/QuestSetup.unity") };
        e.components = e.scene.GetRootGameObjects().SelectMany(o => o.GetComponentsInChildren<Component>(true)).ToArray();
        T One<T>() where T : Component => e.components.OfType<T>().Single();
        e.net = One<NetSyncManager>(); Set(e.net, "_offlineMode", true); Set(e.net, "_clientNo", client);
        var asm = typeof(NetSyncManager).Assembly;
        var connection = Activator.CreateInstance(asm.GetType("Styly.NetSync.OfflineConnectionManager", true), true);
        connection.GetType().GetMethod("Connect").Invoke(connection, new object[] { "", 0, 0, 0, "reveal-test" });
        Set(e.net, "_connectionManager", connection);
        e.variables = Activator.CreateInstance(asm.GetType("Styly.NetSync.NetworkVariableManager", true), Flags, null, new object[] { connection, "reveal-test", e.net }, null);
        Set(e.net, "_networkVariableManager", e.variables); e.variables.GetType().GetMethod("MarkInitialSyncComplete").Invoke(e.variables, null);
        foreach (string name in new[] { "OnGlobalVariableChanged", "OnClientVariableChanged" })
        {
            var info = e.variables.GetType().GetEvent(name);
            info.AddEventHandler(e.variables, Delegate.CreateDelegate(info.EventHandlerType, e.net, typeof(NetSyncManager).GetMethod(name + "Handler", Flags)));
        }
        var avatars = Activator.CreateInstance(asm.GetType("Styly.NetSync.AvatarManager", true), Flags, null, new object[] { false }, null);
        Set(e.net, "_avatarManager", avatars);
        var peers = (IDictionary)Get(avatars, "_connectedPeers"); for (int p = 1; p <= count; p++) peers[p] = null; peers[99] = null;
        for (int p = 1; p <= count; p++) e.Peer(p, ExperimentParticipantRegistry.RoleVariable, "Participant");
        e.Peer(99, ExperimentParticipantRegistry.RoleVariable, "Experimenter");
        e.manager = One<ExperimentManager>(); e.registry = One<ExperimentParticipantRegistry>(); e.session = One<ExperimentSessionManager>();
        e.answers = One<CandidateSyncTest>(); e.privateBoard = One<PrivateTierBoardController>();
        e.reveal = One<ProposedRevealCoordinator>(); e.view = One<ReadonlyParticipantBoardView>();
        e.placements = One<NormalPlacementSync>(); e.agreement = One<FinalAgreementManager>(); e.shared = One<ProposedSharedPlacementController>(); e.submission = One<IndependentAnswerSubmission>();
        Set(e.registry, "localRole", ExperimentParticipantRegistry.ClientRole.Participant); Set(e.registry, "expectedParticipantCount", count);
        Set(e.manager, "currentMode", ExperimentManager.ExperimentMode.Proposed); Set(e.manager, "currentState", ExperimentManager.ExperimentState.Idle);
        foreach (var z in e.components.OfType<TierZone>()) Call(z, "Awake");
        foreach (var c in e.components.OfType<CardTierDetector>()) Call(c, "Awake");
        foreach (var c in e.components.OfType<PrivateAnswerCard>()) Call(c, "Awake");
        Call(One<NormalPlacementSync>(), "Awake"); Call(e.privateBoard, "Awake"); Call(e.view, "Awake");
        Call(e.registry, "Start"); Call(e.manager, "Start"); Call(e.answers, "Start"); Call(e.session, "Start");
        Call(e.submission, "OnEnable"); Call(e.placements, "Start"); Call(e.agreement, "Start"); Call(e.shared, "OnEnable");
        Call(e.privateBoard, "OnEnable"); Call(e.privateBoard, "Start"); Call(e.privateBoard, "Update");
        Call(e.reveal, "OnEnable"); Call(e.view, "OnEnable"); Call(e.view, "Start");
        return e;
    }
    public static void Deliver(Endpoint from, Endpoint to)
    {
        foreach (var v in from.net.GetAllGlobalVariables()) to.Global(v.Key, v.Value);
        foreach (var v in from.net.GetAllClientVariables(from.net.ClientNo)) to.Peer(from.net.ClientNo, v.Key, v.Value);
    }
    private static void Capture(Endpoint e)
    {
        var go = new GameObject("ReadonlyRevealPreviewCamera"); SceneManager.MoveGameObjectToScene(go, e.scene);
        var camera = go.AddComponent<Camera>(); camera.enabled = false;
        camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(e.scene);
        camera.transform.position = new Vector3(0, 1.45f, -0.5f); camera.transform.rotation = Quaternion.identity;
        camera.fieldOfView = 37; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(0.018f, 0.030f, 0.044f);
        camera.cullingMask = 1 << 5; // UI only; isolate visual QA from the room.
        foreach (var t in e.view.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 5;
        foreach (var g in e.view.GetComponentsInChildren<RoundedPanelGraphic>(true))
        { g.SetAllDirty(); g.Rebuild(UnityEngine.UI.CanvasUpdate.PreRender); }
        Canvas.ForceUpdateCanvases();
        var diagnostics = new System.Text.StringBuilder();
        foreach (var g in e.view.GetComponentsInChildren<RoundedPanelGraphic>(true))
        {
            var mesh = g.canvasRenderer.GetMesh();
            diagnostics.AppendLine(g.name + " active=" + g.isActiveAndEnabled + " cull=" + g.canvasRenderer.cull + " verts=" + (mesh != null ? mesh.vertexCount : 0) + " legacy=" + typeof(UnityEngine.UI.Graphic).GetProperty("useLegacyMeshGeneration", Flags).GetValue(g) + " size=" + g.rectTransform.rect);
        }
        Directory.CreateDirectory("Library/ProposedRevealVerification"); File.WriteAllText("Library/ProposedRevealVerification/graphics.txt", diagnostics.ToString());
        var render = new RenderTexture(1800, 1200, 24); var image = new Texture2D(1800, 1200, TextureFormat.RGB24, false);
        var previous = RenderTexture.active;
        try
        {
            camera.targetTexture = render; camera.Render(); RenderTexture.active = render;
            image.ReadPixels(new Rect(0, 0, render.width, render.height), 0, 0); image.Apply();
            Directory.CreateDirectory("Library/ProposedRevealVerification"); File.WriteAllBytes("Library/ProposedRevealVerification/ReadonlyBoards.png", image.EncodeToPNG());
        }
        finally { RenderTexture.active = previous; camera.targetTexture = null; UnityEngine.Object.DestroyImmediate(image); UnityEngine.Object.DestroyImmediate(render); UnityEngine.Object.DestroyImmediate(go); }
    }
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, Flags).SetValue(target, value);
    private static object Get(object target, string name) => target.GetType().GetField(name, Flags).GetValue(target);
    private static void Call(object target, string name) => target.GetType().GetMethod(name, Flags).Invoke(target, null);
}
#endif
