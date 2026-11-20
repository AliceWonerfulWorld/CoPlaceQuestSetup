#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

// Production release/hover handlers, simulated XR events, real NetSync offline transport.
// This does not emulate physical Quest tracking or the controller input device.
public static class CardDropVerification
{
    const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    [Serializable] private class Report { public bool passed; public List<string> checks = new List<string>(); public string failure; public string hardware = "Quest 3 / physical Ray or Direct input: NOT TESTED"; }
    [MenuItem("Tools/Experiment/Verify Card Drop Targets")]
    public static void Run()
    {
        var report = new Report(); var endpoints = new List<ProposedRevealVerification.Endpoint>();
        void Check(bool ok, string text) { if (!ok) throw new Exception(text); report.checks.Add(text); }
        try
        {
            var a = ProposedRevealVerification.CreateEndpoint(1, 2, true); endpoints.Add(a);
            var b = ProposedRevealVerification.CreateEndpoint(2, 2, true); endpoints.Add(b);
            var exp = ProposedRevealVerification.CreateEndpoint(99, 2, true); endpoints.Add(exp);
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
                    Call(e.lobby, "Update"); Call(e.components.OfType<ExperimentLobbyUI>().Single(), "Update");
                    foreach (var ui in e.components.OfType<ExperimentModeUI>()) Call(ui, "Update");
                    Call(e.components.OfType<FinalAgreementUI>().Single(), "Update"); Call(e.manager, "Update"); Call(e.placements, "Update"); Call(e.agreement, "Update");
                    Set(e.shared, "nextInitializationAttempt", 0f); Call(e.shared, "Update");
                    Call(e.submission, "Update"); Call(e.privateBoard, "Update"); Call(e.answers, "LateUpdate");
                    Set(e.reveal, "nextAttempt", 0f); Call(e.reveal, "LateUpdate"); Call(e.agreement, "LateUpdate"); Deliver(e);
                }
            }
            TierZone[] Zones(ProposedRevealVerification.Endpoint e, bool own) => own ? (TierZone[])Get(e.privateBoard, "tierZones") : (TierZone[])Get(e.placements, "tierZones");
            Component Card(ProposedRevealVerification.Endpoint e, string id, bool own) => own ? (Component)e.components.OfType<PrivateAnswerCard>().Single(c => c.CardId == id) : e.placements.GetCard(id);
            TierZone Target(Component card) => card is PrivateAnswerCard p ? p.PreviewDropTarget : ((CardTierDetector)card).PreviewDropTarget;
            string Tier(Component card) => card is PrivateAnswerCard p ? p.CurrentTier : ((CardTierDetector)card).CurrentTier;
            Vector3 Center(TierZone zone) => zone.DropArea.TransformPoint(zone.DropArea.rect.center);
            void Hover(Component card, Vector3 position) { card.transform.position = position; Call(card, "UpdateDragFeedback", true); }
            void End(Component card, bool canceled = false) => Call(card, "OnReleased", new SelectExitEventArgs { isCanceled = canceled });
            int Reservations(TierZone zone) => ((Dictionary<Transform, GameObject>)Get(zone, "occupiedPoints")).Values.Count(v => v != null);
            void VerifyGeometry(bool own, string label)
            {
                var zones = Zones(a, own); var card = Card(a, a.answers.AnswerCardIds[0], own);
                var highlights = zones.Select(z => Get(z, "dropHighlight")).ToArray();
                Check(highlights.Distinct().Count() == 6, label + " every row, including E, owns a distinct highlight");
                foreach (var zone in zones)
                {
                    var center = Center(zone); var area = zone.DropArea;
                    Check(CardDropTargetResolver.Resolve(zones, card.gameObject, center - area.forward * 0.6f) == zone, label + " " + zone.tierName + " accepts a card 60cm in front of visible row");
                    Check(CardDropTargetResolver.Resolve(zones, card.gameObject, center - area.forward * 0.801f) == null &&
                          CardDropTargetResolver.Resolve(zones, card.gameObject, center + area.forward * 0.121f) == null, label + " " + zone.tierName + " rejects front/back outside depth limits");
                    float halfWidth = area.rect.width * Mathf.Abs(area.lossyScale.x) / 2;
                    Check(CardDropTargetResolver.Resolve(zones, card.gameObject, center + area.right * (halfWidth - 0.001f)) == zone &&
                          CardDropTargetResolver.Resolve(zones, card.gameObject, center + area.right * (halfWidth + 0.001f)) == null, label + " " + zone.tierName + " visual right edge matches drop bounds");
                    var collider = zone.GetComponent<BoxCollider>();
                    Vector3 point = zone.transform.InverseTransformPoint(center - area.forward * 0.6f) - collider.center;
                    Check(Mathf.Abs(point.x) <= collider.size.x / 2 && Mathf.Abs(point.y) <= collider.size.y / 2 && Mathf.Abs(point.z) <= collider.size.z / 2, label + " " + zone.tierName + " runtime collider matches enlarged depth");
                    var graphic = (UnityEngine.UI.Image)Get(zone, "dropHighlight");
                    Check(!graphic.raycastTarget && graphic.transform.parent == area && graphic.rectTransform.rect.height <= area.rect.height, label + " " + zone.tierName + " highlight is inside its own visual row and noninteractive");
                }
                var ordered = zones.OrderByDescending(z => Center(z).y).ToArray();
                for (int i = 0; i < ordered.Length - 1; i++)
                {
                    var upper = ordered[i]; var lower = ordered[i + 1]; Vector3 middle = (Center(upper) + Center(lower)) / 2;
                    Check(CardDropTargetResolver.Resolve(zones, card.gameObject, middle + Vector3.up * 0.02f) == upper &&
                          CardDropTargetResolver.Resolve(zones, card.gameObject, middle - Vector3.up * 0.02f) == lower, label + " boundary " + upper.tierName + "/" + lower.tierName + " chooses expected side");
                    Hover(card, Center(upper)); Hover(card, middle - Vector3.up * 0.005f);
                    Check(Target(card) == upper, label + " boundary " + upper.tierName + "/" + lower.tierName + " ignores 5mm jitter");
                    Hover(card, middle - Vector3.up * 0.02f);
                    Check(Target(card) == lower && lower.IsHighlighted && !upper.IsHighlighted && zones.Count(z => z.IsHighlighted) == 1, label + " boundary " + upper.tierName + "/" + lower.tierName + " switches one highlight after crossing 2cm");
                    End(card, true);
                }
                Hover(card, Center(ordered[0]) + Vector3.up * 0.3f);
                Check(Target(card) == null && zones.All(z => !z.IsHighlighted && !z.IsPreviewVisible), label + " outside outer edge clears feedback without attraction"); End(card, true);
            }
            void Drop(Component card, TierZone zone, string label)
            {
                Call(card, "OnGrabbed", new SelectEnterEventArgs());
                string beforeGlobal = a.net.GetGlobalVariable("normalPlacement_" + card.gameObject.name);
                string privateId = card is PrivateAnswerCard ownCard ? ownCard.CardId : null;
                string beforeCandidate = privateId != null ? a.net.GetClientVariable("candidate_" + privateId) : null;
                int before = Reservations(zone);
                Hover(card, Center(zone) - zone.DropArea.forward * 0.6f);
                var expected = zone.PeekAvailableSnapPoint(card.gameObject);
                Check(Target(card) == zone && zone.IsHighlighted && zone.IsPreviewVisible, label + " hover predicts " + zone.tierName);
                Check(Reservations(zone) == before && a.net.GetGlobalVariable("normalPlacement_" + card.gameObject.name) == beforeGlobal &&
                    (privateId == null || a.net.GetClientVariable("candidate_" + privateId) == beforeCandidate), label + " hover/preview makes no slot reservation or placement write");
                Check(Vector3.ProjectOnPlane(zone.PreviewPosition - expected.position, expected.forward).sqrMagnitude < 0.000001f, label + " preview footprint equals snap slot");
                End(card);
                Check(Tier(card) == zone.tierName && Vector3.Distance(card.transform.position, expected.position) < 0.0001f, label + " released card matches highlighted Tier and preview slot");
                Check(!zone.IsHighlighted && !zone.IsPreviewVisible && Target(card) == null, label + " release clears highlight and preview");
            }
            Tick(3);
            foreach (string set in new[] { "A", "B" })
            foreach (bool proposed in new[] { false, true })
            {
                string label = set + "/" + (proposed ? "Proposed" : "Normal");
                var sets = exp.components.OfType<PilotCardSetController>().Single(); exp.session.SetDraft(proposed ? ExperimentManager.ExperimentMode.Proposed : ExperimentManager.ExperimentMode.Normal, set, 2);
                Check(exp.session.StartNewSession(), label + " Reset accepted"); Deliver(exp); Tick(14);
                Check(group.All(e => !e.session.IsResettingSession && e.components.OfType<TierZone>().All(z => !z.IsHighlighted && !z.IsPreviewVisible)), label + " Reset clears all local feedback");
                if (proposed) exp.lobby.SelectProposed(); else exp.lobby.SelectNormal(); Deliver(exp); Tick(3);
                Check(exp.lobby.StartExperiment(), label + " Start accepted"); Deliver(exp); Tick(4);
                var ids = a.answers.AnswerCardIds.ToArray(); var zones = Zones(a, proposed);
                var first = Card(a, ids[0], proposed);
                VerifyGeometry(proposed, label);
                var second = Card(a, ids[1], proposed);
                var upperZone = zones.Single(z => z.tierName == "A"); var lowerZone = zones.Single(z => z.tierName == "B");
                Hover(first, Center(upperZone)); Hover(second, Center(lowerZone));
                Check(zones.Count(z => z.IsHighlighted) == 1 && upperZone.IsHighlighted, label + " simultaneous local drags keep a single feedback owner");
                Call(first, "UpdateDragFeedback", false); Call(second, "UpdateDragFeedback", true);
                Check(zones.Count(z => z.IsHighlighted) == 1 && lowerZone.IsHighlighted, label + " second local drag acquires feedback after first ends");
                End(first, true); End(second, true);
                var blockedZone = zones.Single(z => z.tierName == "C");
                var blocker = new GameObject("OccupiedTestSlot"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(blocker, a.scene);
                blockedZone.GetAvailableSnapPoint(blocker);
                Hover(first, Center(blockedZone));
                Check(Target(first) == null && !blockedZone.IsHighlighted && !blockedZone.IsPreviewVisible, label + " unavailable fixed slot has no misleading target or preview");
                End(first, true); blockedZone.ReleaseCard(blocker); UnityEngine.Object.DestroyImmediate(blocker);
                foreach (string tier in new[] { "A", "B", "C", "D", "E" })
                    for (int i = 0; i < ids.Length; i++) Drop(Card(a, ids[i], proposed), zones.Single(z => z.tierName == tier), label + " " + ids[i]);
                Deliver(a); Tick(3);
                Check(proposed ? ids.All(id => a.answers.TryGetParticipantAnswer(1, id, out var answer) && answer.Tier == "E") :
                    group.All(e => ids.All(id => e.placements.GetCard(id).CurrentTier == "E")), label + " eight sequential moves/re-moves preserve answers or synchronized placements");
                Vector3 original = first.transform.position; string originalTier = Tier(first);
                Call(first, "OnGrabbed", new SelectEnterEventArgs()); Hover(first, Center(zones[0]) + Vector3.right * 4); End(first);
                Check(first.transform.position == original && Tier(first) == originalTier && zones.All(z => !z.IsHighlighted), label + " invalid drop restores original slot without changing Tier");
                Call(first, "OnGrabbed", new SelectEnterEventArgs()); Hover(first, Center(zones.Single(z => z.tierName == "A"))); End(first, true);
                Check(first.transform.position == original && Tier(first) == originalTier && zones.All(z => !z.IsHighlighted && !z.IsPreviewVisible), label + " canceled XR selection restores placement and clears feedback");
                Hover(first, Center(zones.Single(z => z.tierName == "C"))); Call(first, "OnDisable");
                Check(zones.All(z => !z.IsHighlighted && !z.IsPreviewVisible), label + " component disable clears feedback"); End(first, true);
                Hover(first, Center(zones.Single(z => z.tierName == "C"))); Call(first, "OnApplicationFocus", false);
                Check(zones.All(z => !z.IsHighlighted && !z.IsPreviewVisible), label + " application focus loss clears feedback"); End(first, true);
                Hover(first, Center(zones.Single(z => z.tierName == "C"))); Call(first, "OnApplicationPause", true);
                Check(zones.All(z => !z.IsHighlighted && !z.IsPreviewVisible), label + " application pause clears feedback"); End(first, true);
                var denied = Card(exp, ids[0], proposed); Hover(denied, Center(Zones(exp, proposed)[0]));
                Check(Target(denied) == null && Zones(exp, proposed).All(z => !z.IsHighlighted), label + " Experimenter cannot show participant drag feedback");
                if (proposed)
                {
                    Hover(first, Center(zones.Single(z => z.tierName == "C")));
                    Check(a.submission.TrySubmit(), label + " Submit accepted"); Deliver(a); Tick(2); Call(first, "UpdateDragFeedback", true);
                    Check(Target(first) == null && zones.All(z => !z.IsHighlighted && !z.IsPreviewVisible) && !a.reveal.HasRevealed, label + " Submit clears feedback and blocks further hover before Reveal");
                    Check(a.submission.TryCancel(), label + " Cancel Submit accepted"); Deliver(a); Tick(2);
                    Hover(first, Center(zones.Single(z => z.tierName == "C")));
                    Check(Target(first) != null, label + " Cancel Submit restores drag feedback"); End(first, true);
                    foreach (string id in ids) Check(b.privateBoard.TryPlaceCard(id, "B"), label + " peer independently answers " + id);
                    Deliver(b); Tick(2); Check(a.submission.TrySubmit() && b.submission.TrySubmit(), label + " both Submit"); Deliver(a); Deliver(b); Tick(10);
                    Check(group.All(e => e.reveal.HasRevealed && e.shared.IsActive && e.view.DisplayedParticipantCount == 2), label + " simultaneous Reveal and shared board still work");
                    Call(first, "UpdateDragFeedback", true);
                    Check(Target(first) == null && Zones(a, true).All(z => !z.IsHighlighted), label + " independent board remains noninteractive after Reveal");
                    zones = Zones(a, false);
                    foreach (string id in ids) Drop(Card(a, id, false), zones.Single(z => z.tierName == "E"), label + " shared " + id);
                    Deliver(a); Tick(3);
                }
                var sharedFirst = Card(a, ids[0], false); var sharedZones = Zones(a, false);
                Hover(sharedFirst, Center(sharedZones.Single(z => z.tierName == "C")));
                Capture(a, set + (proposed ? "-Proposed" : "-Normal"));
                a.agreement.ToggleLocalReady(); Deliver(a); Tick(2); Call(sharedFirst, "UpdateDragFeedback", true);
                Check(Target(sharedFirst) == null && sharedZones.All(z => !z.IsHighlighted && !z.IsPreviewVisible), label + " local Final Agreement ready suppresses feedback"); End(sharedFirst, true);
                b.agreement.ToggleLocalReady(); Deliver(b); Tick(5);
                Check(group.All(e => e.agreement.IsBoardConfirmed), label + " Final Agreement still confirms eight cards");
            }
            // Reset while a live local drag has feedback, rather than only after confirmation.
            exp.session.SetDraft(ExperimentManager.ExperimentMode.Normal, "A", 2);
            Check(exp.session.StartNewSession(), "live-drag Reset preparation"); Deliver(exp); Tick(14); exp.lobby.SelectNormal(); Deliver(exp); Tick(3);
            Check(exp.lobby.StartExperiment(), "live-drag Reset Start"); Deliver(exp); Tick(4);
            var moving = Card(a, a.answers.AnswerCardIds[0], false); Hover(moving, Center(Zones(a, false).Single(z => z.tierName == "D")));
            Check(Zones(a, false).Any(z => z.IsHighlighted), "live drag has feedback before Reset");
            Check(exp.session.StartNewSession(), "live-drag Reset request"); Deliver(exp); Tick(14);
            Check(group.All(e => e.components.OfType<TierZone>().All(z => !z.IsHighlighted && !z.IsPreviewVisible) && e.placements.CardIds.All(id => e.placements.GetCard(id).CurrentTier == "Unclassified")), "Reset during drag clears preview/highlight and restores all eight cards");
            report.passed = true;
        }
        catch (Exception ex) { report.failure = ex.ToString(); Debug.LogException(ex); }
        finally
        {
            foreach (var e in endpoints)
            {
                foreach (var c in e.scene.GetRootGameObjects().SelectMany(o => o.GetComponentsInChildren<MonoBehaviour>(true)))
                    try { c.GetType().GetMethod("OnDisable", Flags)?.Invoke(c, null); } catch { }
                EditorSceneManager.ClosePreviewScene(e.scene);
            }
            Directory.CreateDirectory("Documentation"); File.WriteAllText("Documentation/CardDropVerification.json", JsonUtility.ToJson(report, true));
        }
        if (!report.passed) throw new InvalidOperationException(report.failure);
        Debug.Log("[Card Drop Verification] PASS " + report.checks.Count + " checks (simulated events / offline transport, NOT Quest hardware)");
    }
    private static void Capture(ProposedRevealVerification.Endpoint e, string label)
    {
        var go = new GameObject("DropPreviewCamera"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, e.scene);
        var camera = go.AddComponent<Camera>(); camera.enabled = false; camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(e.scene);
        camera.transform.position = new Vector3(0, 1.7f, -4); camera.transform.rotation = Quaternion.identity; camera.orthographic = true; camera.orthographicSize = 1.9f;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(0.02f, 0.03f, 0.04f);
        var render = new RenderTexture(1400, 1400, 24); var image = new Texture2D(1400, 1400, TextureFormat.RGB24, false); var previous = RenderTexture.active;
        try { camera.targetTexture = render; camera.Render(); RenderTexture.active = render; image.ReadPixels(new Rect(0, 0, 1400, 1400), 0, 0); image.Apply(); Directory.CreateDirectory("Library/CardDropVerification"); File.WriteAllBytes("Library/CardDropVerification/" + label + ".png", image.EncodeToPNG()); }
        finally { RenderTexture.active = previous; camera.targetTexture = null; UnityEngine.Object.DestroyImmediate(image); UnityEngine.Object.DestroyImmediate(render); UnityEngine.Object.DestroyImmediate(go); }
    }
    static object Get(object o, string name) => o.GetType().GetField(name, Flags).GetValue(o);
    static void Set(object o, string name, object value) => o.GetType().GetField(name, Flags).SetValue(o, value);
    static void Call(object o, string name, params object[] args) => o.GetType().GetMethod(name, Flags).Invoke(o, args);
}
#endif
