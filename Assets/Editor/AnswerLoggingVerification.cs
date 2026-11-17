#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Real scene components with offline NetSync. Every run has its own CSV directory.
public static class AnswerLoggingVerification
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    [Serializable] public class Report { public bool passed; public string root, failure; public List<string> checks = new List<string>(); }
    public static string LastReport { get; private set; }
    [MenuItem("Tools/Experiment/Verify Answer Logging")]
    public static void Run()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Run offline verification outside Play Mode");
        var report = new Report { root = Path.GetFullPath("Library/AnswerLoggingVerification/" + Guid.NewGuid().ToString("N")) };
        Directory.CreateDirectory(report.root); LastReport = Path.Combine(report.root, "result.json");
        void Check(bool value, string name) { if (!value) throw new Exception(name); report.checks.Add(name); }
        try
        {
            foreach (int count in new[] { 2, 1 })
            {
                var endpoints = new List<ProposedRevealVerification.Endpoint>();
                try
                {
                    for (int client = 1; client <= count; client++) endpoints.Add(ProposedRevealVerification.CreateEndpoint(client, count, true));
                    var exp = ProposedRevealVerification.CreateEndpoint(99, count, true); endpoints.Add(exp);
                    var group = endpoints.ToArray(); var people = group.Take(count).ToArray();
                    Set(exp.registry, "localRole", ExperimentParticipantRegistry.ClientRole.Experimenter); Call(exp.registry, "Update");
                    foreach (var e in group) Set(e.manager, "lobby", e.lobby);
                    ExperimentLogger Log(ProposedRevealVerification.Endpoint e) => e.components.OfType<ExperimentLogger>().Single();
                    string root = Path.Combine(report.root, "participants-" + count); Set(Log(exp), "outputRootOverride", root);
                    void Deliver(ProposedRevealVerification.Endpoint from) { foreach (var to in group) if (to != from) ProposedRevealVerification.Deliver(from, to); }
                    foreach (var e in group) Deliver(e);
                    void Tick(int steps = 1)
                    {
                        for (int n = 0; n < steps; n++) foreach (var e in group)
                        {
                            Set(e.session, "nextAttempt", 0f); Call(e.registry, "Update"); e.registry.RefreshParticipants(); Call(e.session, "Update"); Call(e.lobby, "Update");
                            Call(e.manager, "Update"); Call(e.placements, "Update"); Call(e.agreement, "Update"); Set(e.shared, "nextInitializationAttempt", 0f); Call(e.shared, "Update");
                            Call(e.submission, "Update"); Call(e.privateBoard, "Update"); Call(e.answers, "LateUpdate"); Set(e.reveal, "nextAttempt", 0f); Call(e.reveal, "LateUpdate"); Call(e.agreement, "LateUpdate");
                            Call(Log(e), "Update"); Deliver(e);
                        }
                    }
                    Tick(3); var saved = new Dictionary<string, byte[]>();
                    foreach (var set in new[] { PilotCardSetController.CardSet.A, PilotCardSetController.CardSet.B, PilotCardSetController.CardSet.Development })
                    foreach (bool proposed in new[] { false, true })
                    {
                        string label = count + "/" + set + "/" + (proposed ? "Proposed" : "Normal");
                        Set(exp.components.OfType<PilotCardSetController>().Single(), "nextSessionSet", set);
                        Check(exp.session.ResetSession(), label + " Reset accepted"); Deliver(exp); Tick(14);
                        Check(group.All(e => !e.session.IsResettingSession), label + " all peers reset");
                        Check(saved.All(p => File.ReadAllBytes(p.Key).SequenceEqual(p.Value)), label + " prior CSV bytes unchanged");
                        if (proposed) exp.lobby.SelectProposed(); else exp.lobby.SelectNormal(); Deliver(exp); Tick(3);
                        Check(exp.lobby.StartExperiment(), label + " Start"); Deliver(exp); Tick(4);
                        string folder = Path.Combine(root, exp.session.CurrentSessionId); var ids = people[0].placements.CardIds.OrderBy(x => x).ToArray();
                        if (proposed)
                        {
                            foreach (var p in people) foreach (string id in ids) Check(p.privateBoard.TryPlaceCard(id, p.net.ClientNo == 1 ? "A" : "B"), label + " private " + p.net.ClientNo + "/" + id);
                            foreach (var p in people) Deliver(p); Tick(3);
                            Check(!File.Exists(Path.Combine(folder, "independent_answers.csv")), label + " no pre-Reveal answers");
                            var first = people[0]; Check(first.submission.TrySubmit(), label + " first Submit"); Deliver(first); Tick(count == 1 ? 0 : 2);
                            // Cancel before allowing the Reveal coordinator to commit (also for the single-person case).
                            Check(first.submission.TryCancel(), label + " Cancel"); Deliver(first); Set(first.submission, "nextRetry", 0f); Tick(3);
                            Check(!exp.reveal.HasRevealed, label + " Cancel prevents Reveal");
                            Check(first.submission.TrySubmit(), label + " resubmit"); Deliver(first);
                            foreach (var p in people.Skip(1)) { Check(p.submission.TrySubmit(), label + " peer Submit"); Deliver(p); }
                            bool failInitial = count == 2 && set == PilotCardSetController.CardSet.B;
                            string initialFile = Path.Combine(folder, "independent_answers.csv");
                            if (failInitial) Directory.CreateDirectory(initialFile);
                            Tick(12);
                            if (failInitial)
                            {
                                Check(!string.IsNullOrEmpty(Log(exp).LastError) && !exp.session.ResetSession(), "independent CSV failure blocks Reset");
                                Directory.Delete(initialFile);
                                Check(Log(exp).TryFlushBeforeReset(), "independent CSV retries after recovery");
                            }
                            Check(exp.reveal.HasRevealed && exp.shared.IsActive, label + " Reveal and shared ready");
                            var rows = ExperimentCsvLogStore.ReadCsv(Path.Combine(folder, "independent_answers.csv"));
                            Check(rows.Count == count * ids.Length + 1 && rows[0].SequenceEqual(ExperimentCsvLogStore.IndependentColumns), label + " complete independent CSV");
                            Check(rows.Skip(1).All(r => r[7] == (r[5] == "1" ? "A" : "B")), label + " independent tiers exact");
                            byte[] initial = File.ReadAllBytes(Path.Combine(folder, "independent_answers.csv"));
                            Call(Log(exp), "OnRevealed"); Call(Log(exp), "OnRevealed");
                            Check(initial.SequenceEqual(File.ReadAllBytes(Path.Combine(folder, "independent_answers.csv"))), label + " Reveal duplicate immutable");
                        }
                        foreach (string id in ids) Check(people[0].placements.SendPlacement(id, "E"), label + " final placement " + id);
                        Deliver(people[0]); Tick(3);
                        Check(!File.Exists(Path.Combine(folder, "final_placements.csv")), label + " no unconfirmed final CSV");
                        bool failFinal = count == 2 && set == PilotCardSetController.CardSet.A && !proposed;
                        string finalFile = Path.Combine(folder, "final_placements.csv");
                        if (failFinal) Directory.CreateDirectory(finalFile);
                        foreach (var p in people) { p.agreement.ToggleLocalReady(); Deliver(p); } Tick(6);
                        if (failFinal)
                        {
                            Check(!string.IsNullOrEmpty(Log(exp).LastError) && !exp.session.ResetSession(), "final CSV failure blocks Reset");
                            Directory.Delete(finalFile);
                            Check(Log(exp).TryFlushBeforeReset(), "final CSV retries after recovery");
                        }
                        Check(group.All(e => e.agreement.IsBoardConfirmed), label + " confirmed");
                        var final = ExperimentCsvLogStore.ReadCsv(Path.Combine(folder, "final_placements.csv"));
                        Check(final.Count == ids.Length + 1 && final.Skip(1).All(r => r[6] == "E") && final[0].SequenceEqual(ExperimentCsvLogStore.FinalColumns), label + " exact dynamic final CSV");
                        Call(Log(exp), "CaptureSnapshot");
                        Check(Log(exp).TryFlushBeforeReset(), label + " preflight complete");
                        var events = ExperimentCsvLogStore.ReadCsv(Path.Combine(folder, "phase_events.csv")).Skip(1).ToArray();
                        Check(events.Select(r => r[7]).Distinct().Count() == events.Length, label + " event IDs unique");
                        double Time(string type) => double.Parse(events.Single(r => r[3] == type)[5], System.Globalization.CultureInfo.InvariantCulture);
                        Check(Time("BOARD_CONFIRMED") >= Time("EXPERIMENT_STARTED"), label + " total duration computable");
                        if (proposed)
                        {
                            Check(events.Count(r => r[3] == "PARTICIPANT_SUBMITTED") == count + 1 && events.Count(r => r[3] == "PARTICIPANT_SUBMISSION_CANCELLED") == 1, label + " Submit Cancel resubmit history");
                            Check(Time("SHARED_CLASSIFICATION_STARTED") >= Time("ANSWERS_REVEALED") && Time("BOARD_CONFIRMED") >= Time("SHARED_CLASSIFICATION_STARTED"), label + " phase durations ordered");
                            Check(events.Where(r => r[3] == "PARTICIPANT_SUBMITTED").All(r => r[6].StartsWith("client:")), label + " connection ID explicit");
                            var initialRows = ExperimentCsvLogStore.ReadCsv(Path.Combine(folder, "independent_answers.csv")).Skip(1).ToArray();
                            Check(initialRows.All(r => events.Any(ev => ev[3] == "PARTICIPANT_SUBMITTED" && ev[6] == "client:" + r[5] && ev[7].EndsWith(":" + r[11]))), label + " Reveal rows join final valid Submit IDs");
                            if (set == PilotCardSetController.CardSet.A)
                            {
                                var committed = exp.reveal.GetRevealedSnapshot(); committed.epoch++;
                                Set(exp.reveal, "revealed", committed);
                                Check(!Log(exp).TryFlushBeforeReset(), label + " Reveal Epoch mismatch blocks preflight"); committed.epoch--;
                                string current = committed.sessionId; committed.sessionId = "wrong-session";
                                Check(!Log(exp).TryFlushBeforeReset(), label + " Reveal Session mismatch blocks preflight"); committed.sessionId = current;
                                Check(Log(exp).TryFlushBeforeReset(), label + " valid Snapshot preflight recovers");
                                // A fresh observer must label restored states instead of inventing live timing.
                                var original = Log(exp); Call(original, "OnDisable");
                                var restored = exp.manager.gameObject.AddComponent<ExperimentLogger>();
                                Set(restored, "sessionManager", exp.session); Set(restored, "participantRegistry", exp.registry); Set(restored, "lobby", exp.lobby); Set(restored, "finalAgreement", exp.agreement);
                                string restoredRoot = Path.Combine(root, "restored"); Set(restored, "outputRootOverride", restoredRoot); Call(restored, "OnEnable"); Call(restored, "Update");
                                var replay = ExperimentCsvLogStore.ReadCsv(Path.Combine(restoredRoot, exp.session.CurrentSessionId, "phase_events.csv")).Skip(1).ToArray();
                                Check(replay.All(ev => ev[11] == "retained_snapshot") && replay.Count(ev => ev[3] == "BOARD_CONFIRMED") == 1, label + " cold restoration explicitly marked");
                                Check(ExperimentCsvLogStore.ReadCsv(Path.Combine(restoredRoot, exp.session.CurrentSessionId, "independent_answers.csv")).Count == count * ids.Length + 1, label + " restored immutable answers complete");
                                Call(restored, "OnDisable"); UnityEngine.Object.DestroyImmediate(restored); Call(original, "OnEnable");
                            }

                        }
                        else Check(!File.Exists(Path.Combine(folder, "independent_answers.csv")), label + " Normal has no independent CSV");
                        foreach (string file in Directory.GetFiles(folder, "*.csv")) saved[file] = File.ReadAllBytes(file);
                    }
                    // Interrupted trials need no nonexistent final answer, but have an explicit outcome.
                    Check(exp.session.ResetSession(), "prepare aborted trial"); Deliver(exp); Tick(14);
                    exp.lobby.SelectNormal(); Deliver(exp); Tick(3); Check(exp.lobby.StartExperiment(), "start aborted trial"); Deliver(exp); Tick(3);
                    string aborted = Path.Combine(root, exp.session.CurrentSessionId);
                    Check(exp.session.ResetSession(), "unconfirmed trial can Reset"); Deliver(exp); Tick(14);
                    Check(ExperimentCsvLogStore.ReadCsv(Path.Combine(aborted, "phase_events.csv")).Any(r => r.Length > 3 && r[3] == "SESSION_ABORTED") && !File.Exists(Path.Combine(aborted, "final_placements.csv")), "aborted trial explicit without invented final");
                    var logger = Log(exp); logger.enabled = false;
                    Check(!exp.session.ResetSession(), "disabled Logger cannot bypass preflight"); logger.enabled = true; Call(logger, "OnEnable");
                    UnityEngine.Object.DestroyImmediate(logger);
                    Check(!exp.session.ResetSession(), "missing Logger cannot bypass preflight");
                }
                finally { foreach (var e in endpoints) { foreach (var c in e.components.OfType<MonoBehaviour>()) { try { c.GetType().GetMethod("OnDisable", Flags)?.Invoke(c, null); } catch { } } EditorSceneManager.ClosePreviewScene(e.scene); } }
            }
            report.passed = true;
        }
        catch (Exception error) { report.failure = error.ToString(); }
        finally { File.WriteAllText(LastReport, JsonUtility.ToJson(report, true)); }
        if (!report.passed) throw new Exception(report.failure);
        Debug.Log("[Answer Logging Verification] PASS " + report.checks.Count + " / " + LastReport);
    }
    static void Set(object target, string field, object value) => target.GetType().GetField(field, Flags).SetValue(target, value);
    static void Call(object target, string method) => target.GetType().GetMethod(method, Flags).Invoke(target, null);
}
#endif
