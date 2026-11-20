#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Real production components + NetSync offline transport. No live Room / Quest involved.
public static class PilotCardSetVerification
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    [Serializable] private class Report { public bool passed; public List<string> checks = new List<string>(); public string failure; public string testKind = "Unity Editor preview scenes; offline transport; simulated delivery (not Quest hardware)"; }
    [MenuItem("Tools/Experiment/Verify Pilot Card Sets")]
    public static void Run()
    {
        var report = new Report(); var endpoints = new List<ProposedRevealVerification.Endpoint>();
        void Check(bool ok, string text) { if (!ok) throw new Exception(text); report.checks.Add(text); }
        try
        {
            // Source images are copied verbatim; balancing categories are never imported as card content.
            foreach (var line in File.ReadAllLines("coplace_pilot_kit/metadata/card_manifest.csv").Skip(1).Where(l => l.Length > 0))
            {
                var row = line.Split(','); string path = "Assets/PilotCards/" + Path.GetFileName(row[3]);
                Check(File.ReadAllBytes("coplace_pilot_kit/" + row[3]).SequenceEqual(File.ReadAllBytes(path)), "Manifest image bytes match " + row[1]);
            }
            var a = ProposedRevealVerification.CreateEndpoint(1, 2, true); endpoints.Add(a);
            var b = ProposedRevealVerification.CreateEndpoint(2, 2, true); endpoints.Add(b);
            var exp = ProposedRevealVerification.CreateEndpoint(99, 2, true); endpoints.Add(exp);
            var group = endpoints.ToArray();
            PilotCardSetController Sets(ProposedRevealVerification.Endpoint e) => e.components.OfType<PilotCardSetController>().Single();
            Set(exp.registry, "localRole", ExperimentParticipantRegistry.ClientRole.Experimenter); Call(exp.registry, "Update");
            foreach (var e in group) Set(e.manager, "lobby", e.lobby);
            void Deliver(ProposedRevealVerification.Endpoint from) { foreach (var to in group) if (from != to) ProposedRevealVerification.Deliver(from, to); }
            foreach (var e in group) Deliver(e);
            void Tick(int count = 1)
            {
                for (int i = 0; i < count; i++) foreach (var e in group)
                {
                    Set(e.session, "nextAttempt", 0f); Call(e.registry, "Update"); e.registry.RefreshParticipants(); Call(e.session, "Update");
                    Call(e.lobby, "Update"); Call(e.components.OfType<ExperimentLobbyUI>().Single(), "Update"); Call(e.manager, "Update"); Call(e.placements, "Update"); Call(e.agreement, "Update");
                    Set(e.shared, "nextInitializationAttempt", 0f); Call(e.shared, "Update");
                    Call(e.submission, "Update"); Call(e.privateBoard, "Update"); Call(e.answers, "LateUpdate"); e.session.EnsureResetUI().Refresh();
                    Set(e.reveal, "nextAttempt", 0f); Call(e.reveal, "LateUpdate"); Call(e.agreement, "LateUpdate");
                    Deliver(e);
                }
            }
            string logRoot = Path.GetFullPath("Library/PilotCardVerification/Logs/" + Guid.NewGuid().ToString("N"));
            var logger = exp.components.OfType<ExperimentLogger>().Single();
            Set(logger, "outputRootOverride", logRoot); Call(logger, "OnEnable"); Call(logger, "Start");
            var font = AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>("Assets/PilotCards/PilotJapanese SDF.asset");
            Check(font != null && font.HasCharacters("P1 INITIAL ANSWER READ ONLY ABCDE 0123456789"), "Reveal font contains UI ASCII glyphs");
            foreach (var line in File.ReadAllLines("coplace_pilot_kit/metadata/card_manifest.csv").Skip(1).Where(l => l.Length > 0))
                Check(font.HasCharacters(line.Split(',')[2]), "Reveal font contains display name " + line.Split(',')[1]);
            Tick(3);
            var savedCsv = new Dictionary<string, byte[]>();
            foreach (var set in new[] { PilotCardSetController.CardSet.A, PilotCardSetController.CardSet.B, PilotCardSetController.CardSet.Development })
            foreach (bool proposed in new[] { false, true })
            {
                string label = set + "/" + (proposed ? "Proposed" : "Normal");
                Check(exp.session.SetDraft(proposed ? ExperimentManager.ExperimentMode.Proposed : ExperimentManager.ExperimentMode.Normal, set.ToString(), 2), label + " Draft selected");
                // Deliberately conflicting Participant Inspector selection must have no effect.
                Set(Sets(a), "nextSessionSet", set == PilotCardSetController.CardSet.A ? PilotCardSetController.CardSet.B : PilotCardSetController.CardSet.A);
                var reset = exp.session.EnsureResetUI();
                ((UnityEngine.UI.Button)reset.GetType().GetField("resetButton", Flags).GetValue(reset)).onClick.Invoke();
                Check(reset.IsConfirmationOpen && !exp.session.IsResettingSession, label + " UI confirmation preserves current Session");
                ((UnityEngine.UI.Button)reset.GetType().GetField("confirmButton", Flags).GetValue(reset)).onClick.Invoke(); reset.ConfirmReset();
                Check(exp.session.IsResettingSession && !reset.CanReset, label + " UI Reset accepted once and locks pending ACK"); Deliver(exp); Tick(14);
                reset.Refresh(); Check(reset.CanReset && reset.StatusMessage.Contains("同期が完了"), label + " UI reports all-client Reset completion");
                Check(savedCsv.All(pair => File.ReadAllBytes(pair.Key).SequenceEqual(pair.Value)), label + " previous Session CSV bytes preserved after Reset");
                Check(group.All(e => !e.session.IsResettingSession && e.session.CurrentCardSetId == set.ToString() && Sets(e).ActiveSetId == set.ToString()), label + " common set applied before reset ACK");
                string prefix = set == PilotCardSetController.CardSet.Development ? "Card_" : set.ToString();
                int count = set == PilotCardSetController.CardSet.Development ? 3 : 8;
                var ids = Enumerable.Range(1, count).Select(i => prefix + i.ToString("00")).ToArray();
                Check(group.All(e => e.answers.AnswerCardIds.SequenceEqual(ids) && e.placements.CardIds.OrderBy(x => x).SequenceEqual(ids)), label + " unique IDs / exact active card count");
                Check(group.All(e => !e.submission.IsLocalSubmitted && !e.reveal.HasRevealed && !e.agreement.IsBoardConfirmed && e.placements.CardIds.All(id => e.placements.GetCard(id).CurrentTier == "Unclassified")), label + " reset clears placement / Submit / Reveal / final state");
                Check(group.All(e => e.answers.GetParticipantCandidates(1).Values.All(answer => !answer.Answered) && e.answers.GetParticipantCandidates(2).Values.All(answer => !answer.Answered)), label + " independent answers cleared");
                if (proposed) exp.lobby.SelectProposed(); else exp.lobby.SelectNormal(); Deliver(exp); Tick(3);
                Check(exp.lobby.StartExperiment(), label + " start accepted"); Deliver(exp); Tick(4);
                Check(group.All(e => e.lobby.IsExperimentStarted), label + " start shared by 3 endpoints");
                Set(Sets(exp), "nextSessionSet", set == PilotCardSetController.CardSet.A ? PilotCardSetController.CardSet.B : PilotCardSetController.CardSet.A); Tick(2);
                Check(group.All(e => Sets(e).ActiveSetId == set.ToString()), label + " active set ignores Inspector changes");
                if (proposed)
                {
                    foreach (string id in ids) Check(a.privateBoard.TryPlaceCard(id, "E"), label + " P1 answers " + id + " E");
                    foreach (string id in ids.Take(count - 1)) Check(b.privateBoard.TryPlaceCard(id, "A"), label + " P2 answers " + id);
                    Deliver(a); Deliver(b); Tick(3);
                    Check(!b.submission.CanSubmit && !group.Any(e => e.reveal.HasRevealed), label + " one missing card blocks Submit / Reveal");
                    Check(b.privateBoard.TryPlaceCard(ids.Last(), "A"), label + " P2 last answer"); Deliver(b); Tick(3);
                    Check(a.submission.TrySubmit(), label + " P1 Submit"); Deliver(a); Tick(2);
                    Check(!a.privateBoard.TryPlaceCard(ids[0], "B") && !group.Any(e => e.reveal.HasRevealed), label + " submitted answers locked / no premature Reveal");
                    Check(b.submission.TrySubmit(), label + " P2 Submit"); Deliver(b); Tick(10);
                    Check(group.All(e => e.reveal.HasRevealed && e.view.DisplayedParticipantCount == 2 && e.shared.IsActive), label + " simultaneous Reveal + shared classification");
                    foreach (var e in group)
                    {
                        var snapshot = e.reveal.GetRevealedSnapshot();
                        Check(snapshot.cardIds.SequenceEqual(ids) && snapshot.participants[0].answers.All(answer => answer.tier == "E") && snapshot.participants[1].answers.All(answer => answer.tier == "A"), label + " committed answers preserved client " + e.net.ClientNo);
                        Check(e.view.GetComponentsInChildren<UnityEngine.UI.RawImage>(true).Count(image => image.texture != null) == 2 * count, label + " Reveal displays all images client " + e.net.ClientNo);
                        foreach (string id in ids) Check(e.view.transform.Find("ReadonlyMiniBoards/P1_InitialAnswer/Tier_E/Cards/" + id) != null, label + " Tier E reveal tile " + id);
                    }
                }
                // Every card in one Tier verifies the full eight-slot capacity and final fingerprint.
                foreach (string id in ids) Check(a.placements.SendPlacement(id, "E"), label + " shared placement " + id);
                Deliver(a); Tick(4);
                Check(group.All(e => ids.All(id => e.placements.GetCard(id).CurrentTier == "E")), label + " all placements synchronized");
                foreach (var e in group)
                {
                    var positions = ids.Select(id => e.placements.GetCard(id).transform.position).ToArray();
                    Check(positions.Distinct().Count() == count, label + " unique snap positions client " + e.net.ClientNo);
                    foreach (string id in ids)
                    {
                        var card = e.placements.GetCard(id); var local = e.components.OfType<PrivateAnswerCard>().Single(c => c.CardId == id);
                        Texture DisplayTexture(GameObject go) => go.GetComponent<PilotCardFace>() != null && go.GetComponent<PilotCardFace>().enabled
                            ? go.GetComponent<PilotCardFace>().SourceTexture : go.GetComponent<Renderer>().sharedMaterial.mainTexture;
                        Check(DisplayTexture(card.gameObject) != null && DisplayTexture(card.gameObject) == DisplayTexture(local.gameObject), label + " Main/Private texture correspondence " + id);
                    }
                }
                a.agreement.ToggleLocalReady(); Deliver(a); b.agreement.ToggleLocalReady(); Deliver(b); Tick(5);
                Check(group.All(e => e.agreement.IsBoardConfirmed && ids.All(id => e.placements.GetFinalTier(id) == "E")), label + " all-card final agreement");
                // Reconnect reads the same control, keeping set and confirmed placements.
                Call(b.session, "OnReady"); Tick(3);
                Check(b.agreement.IsBoardConfirmed && Sets(b).ActiveSetId == set.ToString(), label + " reconnect preserves active set and final result");
                string setLog = Path.Combine(logRoot, exp.session.CurrentSessionId, "card_set.csv");
                Check(File.Exists(setLog) && ExperimentCsvLogStore.ReadCsv(setLog)[1][1] == set.ToString(), label + " logs immutable card set in additive sidecar");
                Check(ExperimentCsvLogStore.ReadCsv(Path.Combine(logRoot, exp.session.CurrentSessionId, "phase_events.csv"))[0].SequenceEqual(ExperimentCsvLogStore.EventColumns), label + " existing phase CSV schema preserved");
                foreach (string file in Directory.GetFiles(Path.Combine(logRoot, exp.session.CurrentSessionId), "*.csv")) savedCsv[file] = File.ReadAllBytes(file);
                Capture(a, label.Replace('/', '-'));
            }
            report.passed = true;
        }
        catch (Exception error) { report.failure = error.ToString(); Debug.LogException(error); }
        finally
        {
            foreach (var e in endpoints)
            {
                foreach (var c in e.components.OfType<MonoBehaviour>())
                    try { c.GetType().GetMethod("OnDisable", Flags)?.Invoke(c, null); } catch { }
                EditorSceneManager.ClosePreviewScene(e.scene);
            }
            Directory.CreateDirectory("Documentation"); File.WriteAllText("Documentation/PilotCardSetVerification.json", JsonUtility.ToJson(report, true));
        }
        if (!report.passed) throw new InvalidOperationException(report.failure);
        Debug.Log("[Pilot Card Verification] PASS " + report.checks.Count + " checks (offline Editor, not Quest)");
    }
    private static void Capture(ProposedRevealVerification.Endpoint e, string label)
    {
        var go = new GameObject("PilotPreviewCamera"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, e.scene);
        var camera = go.AddComponent<Camera>(); camera.enabled = false; camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(e.scene);
        camera.transform.position = new Vector3(-0.7f, 1.7f, -3.5f); camera.transform.rotation = Quaternion.identity;
        camera.orthographic = true; camera.orthographicSize = 2.4f; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(0.02f, 0.03f, 0.04f);
        var render = new RenderTexture(1600, 1200, 24); var image = new Texture2D(1600, 1200, TextureFormat.RGB24, false);
        var previous = RenderTexture.active;
        try { camera.targetTexture = render; camera.Render(); RenderTexture.active = render; image.ReadPixels(new Rect(0, 0, 1600, 1200), 0, 0); image.Apply(); Directory.CreateDirectory("Library/PilotCardVerification"); File.WriteAllBytes("Library/PilotCardVerification/" + label + ".png", image.EncodeToPNG()); }
        finally { RenderTexture.active = previous; camera.targetTexture = null; UnityEngine.Object.DestroyImmediate(image); UnityEngine.Object.DestroyImmediate(render); UnityEngine.Object.DestroyImmediate(go); }
    }
    static void Set(object target, string field, object value) => target.GetType().GetField(field, Flags).SetValue(target, value);
    static void Call(object target, string method) => target.GetType().GetMethod(method, Flags).Invoke(target, null);
}
#endif
