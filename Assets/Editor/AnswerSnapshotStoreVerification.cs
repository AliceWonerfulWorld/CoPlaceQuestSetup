#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class AnswerSnapshotStoreVerification
{
    [Serializable] public class Report { public bool passed; public string root, failure; public List<string> checks = new List<string>(); }
    public static string LastReport { get; private set; }
    [MenuItem("Tools/Experiment/Verify Answer Snapshot Store")]
    public static void Run()
    {
        var r = new Report { root = Path.GetFullPath("Library/AnswerSnapshotStoreVerification/" + Guid.NewGuid().ToString("N")) };
        Directory.CreateDirectory(r.root); LastReport = Path.Combine(r.root, "result.json");
        void Check(bool ok, string text) { if (!ok) throw new Exception(text); r.checks.Add(text); }
        void Reject(Action action, string text) { bool rejected = false; try { action(); } catch (InvalidDataException) { rejected = true; } catch (IOException) { rejected = true; } catch (ArgumentException) { rejected = true; } Check(rejected, text); }
        try
        {
            var store = new ExperimentCsvLogStore(r.root, "store-verification");
            ExperimentCsvLogStore.Observation E(string id, string mode, string type) => new ExperimentCsvLogStore.Observation {
                SessionId = id, Epoch = 7, Condition = mode, EventType = type, EventId = id + ":" + type, CardSetId = "A",
                ObservedAtUtc = DateTimeOffset.UtcNow, MonotonicSeconds = 10 };
            string[] ids = Enumerable.Range(1, 8).Select(i => "A" + i.ToString("00")).ToArray();
            var start = E("normal", "Normal", "EXPERIMENT_STARTED"); store.Record(start);
            var final = E("normal", "Normal", "BOARD_CONFIRMED");
            final.FinalPlacements = ids.Select(id => new ExperimentCsvLogStore.FinalPlacement { CardId = id, Tier = "E" }).ToArray(); store.Record(final);
            string finalPath = Path.Combine(r.root, "normal/final_placements.csv"); byte[] before = File.ReadAllBytes(finalPath);
            Check(ExperimentCsvLogStore.ReadCsv(finalPath).Count == 9, "8 final cards persisted");
            final.ObservedAtUtc = DateTimeOffset.UtcNow.AddHours(1); Check(!store.Record(final) && before.SequenceEqual(File.ReadAllBytes(finalPath)), "duplicate final preserves exact original bytes and observed time");
            final.FinalPlacements[0].Tier = "A"; Reject(() => store.Record(final), "conflicting final cannot overwrite");
            Check(before.SequenceEqual(File.ReadAllBytes(finalPath)), "conflicting final leaves bytes intact"); final.FinalPlacements[0].Tier = "E";
            final.Epoch++; Reject(() => store.Record(final), "same Session wrong Epoch rejected"); final.Epoch--;
            final.CardSetId = "B"; Reject(() => store.Record(final), "same Session wrong Set rejected"); final.CardSetId = "A";
            final.SessionId = "other-session"; Reject(() => store.Record(final), "snapshot cannot precede matching Session Start"); final.SessionId = "normal";
            final.EventType = "NOT_CONFIRMED"; Reject(() => store.Record(final), "unconfirmed event cannot persist final"); final.EventType = "BOARD_CONFIRMED";
            var proposedStart = E("proposed", "Proposed", "EXPERIMENT_STARTED"); store.Record(proposedStart);
            var reveal = E("proposed", "Proposed", "ANSWERS_REVEALED"); reveal.SnapshotId = "reveal-1";
            reveal.IndependentAnswers = new[] { 1, 2 }.SelectMany(client => ids.Select(id => new ExperimentCsvLogStore.IndependentAnswer {
                ClientId = client, CardId = id, Tier = client == 1 ? "A" : "B", SubmissionId = "consent-" + client, SelectedAt = DateTime.UtcNow.ToString("o") })).ToArray();
            store.Record(reveal); string path = Path.Combine(r.root, "proposed/independent_answers.csv"); byte[] initial = File.ReadAllBytes(path);
            Check(ExperimentCsvLogStore.ReadCsv(path).Count == 17, "2 participants x 8 independent rows");
            Check(!store.Record(reveal) && initial.SequenceEqual(File.ReadAllBytes(path)), "duplicate Reveal preserves exact bytes");
            reveal.SnapshotId = "reveal-2"; Reject(() => store.Record(reveal), "different committed snapshot cannot overwrite"); reveal.SnapshotId = "reveal-1";
            reveal.IndependentAnswers[0].Tier = "E"; Reject(() => store.Record(reveal), "changed independent answer rejected"); reveal.IndependentAnswers[0].Tier = "A";
            var answers = reveal.IndependentAnswers; reveal.IndependentAnswers = answers.Take(15).ToArray(); Reject(() => store.Record(reveal), "incomplete participant/card matrix rejected"); reveal.IndependentAnswers = answers;
            reveal.IndependentAnswers[0].SelectedAt = "not a timestamp"; Reject(() => store.Record(reveal), "invalid selected-at rejected");
            reveal.IndependentAnswers[0].SelectedAt = ExperimentCsvLogStore.ReadCsv(path)[1][8];
            reveal.Condition = "Normal"; Reject(() => store.Record(reveal), "Normal cannot write independent answers"); reveal.Condition = "Proposed";
            Check(initial.SequenceEqual(File.ReadAllBytes(path)), "all rejected independent writes leave original bytes intact");
            var devStart = E("development", "Proposed", "EXPERIMENT_STARTED"); devStart.CardSetId = "Development"; store.Record(devStart);
            var dev = E("development", "Proposed", "ANSWERS_REVEALED"); dev.CardSetId = "Development"; dev.SnapshotId = "dev-reveal";
            dev.IndependentAnswers = answers.Take(3).ToArray(); store.Record(dev);
            Check(ExperimentCsvLogStore.ReadCsv(Path.Combine(r.root, "development/independent_answers.csv")).Count == 4, "1 participant x 3 development answers");
            Check(File.ReadAllBytes(path).Take(3).SequenceEqual(new byte[] {239,187,191}), "answer CSV UTF-8 BOM");
            r.passed = true;
        }
        catch (Exception e) { r.failure = e.ToString(); }
        finally { File.WriteAllText(LastReport, JsonUtility.ToJson(r, true)); }
        if (!r.passed) throw new Exception(r.failure);
        Debug.Log("[Answer Snapshot Store] PASS " + r.checks.Count + " / " + LastReport);
    }
}
#endif
