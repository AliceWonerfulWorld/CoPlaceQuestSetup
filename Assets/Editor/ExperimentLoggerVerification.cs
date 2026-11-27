#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class ExperimentLoggerVerification
{
    [Serializable] private class Report { public bool passed; public string[] checks; public string failure; public string outputDirectory; }
    [MenuItem("Tools/Experiment/Verify Experiment Logger")]
    public static void Run()
    {
        var report = new Report(); var checks = new List<string>();
        string root = Path.GetFullPath("Library/ExperimentLoggerVerification/run-" + Guid.NewGuid().ToString("N"));
        report.outputDirectory = root;
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks.Add(message); }
        try
        {
            var store = new ExperimentCsvLogStore(Path.Combine(root, "store"), "run-1");
            var firstUtc = DateTimeOffset.Parse("2026-10-15T00:00:00Z", CultureInfo.InvariantCulture);
            ExperimentCsvLogStore.Observation E(string type, double seconds, string id = null, string participant = "", string condition = "", string session = "normal-session")
                => new ExperimentCsvLogStore.Observation { SessionId = session, Epoch = 1, EventType = type,
                    EventId = id ?? session + ":" + type, ParticipantId = participant, Condition = condition,
                    ObserverClientId = "99", ObservedAtUtc = firstUtc.AddSeconds(seconds), MonotonicSeconds = 1000 + seconds,
                    ParticipantsAtStart = type == "EXPERIMENT_STARTED" ? "1;2" : "" };
            var originalCulture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                Check(store.Record(E("SESSION_CREATED", 0)), "Session creation creates both CSVs");
                store.Record(E("SESSION_RESET_STARTED", 0));
                store.Record(E("SESSION_RESET_COMPLETED", 2));
                string eventPath = Path.Combine(store.RootDirectory, "normal-session", "phase_events.csv");
                Check(ExperimentCsvLogStore.ReadCsv(eventPath).Skip(1).All(r => r[2] == ""), "Condition remains unknown before Start");
                store.Record(E("EXPERIMENT_STARTED", 5, condition: "Normal"));
                store.Record(E("BOARD_CONFIRMED", 45));
                var rows = ExperimentCsvLogStore.ReadCsv(eventPath);
                Check(rows.Count == 6 && rows.Skip(1).All(r => r[1] == "normal-session" && r[2] == "Normal"), "Start backfills the final Normal Condition without losing setup events");
                Check(rows[4][5] == "5.000000" && rows[5][5] == "45.000000", "Elapsed uses invariant decimal point and local monotonic origin");
                Check(!store.Record(E("BOARD_CONFIRMED", 99)) && ExperimentCsvLogStore.ReadCsv(eventPath).Count == 6, "Repeated Event ID preserves original Confirmed time and row count");
                var summary = ExperimentCsvLogStore.ReadCsv(Path.Combine(store.RootDirectory, "normal-session", "session.csv"));
                Check(summary.Count == 2 && summary[1][2] == "Normal" && summary[1][5] == rows[4][4] && summary[1][6] == rows[5][4], "Session summary contains observed Start and Confirmed timestamps");
                string complex = "参加者,日本語\"引用\"\r\n次の行";
                store.Record(E("FUTURE_OPERATION", 46, "event,日本語\n01", complex));
                Check(ExperimentCsvLogStore.ReadCsv(eventPath).Last()[6] == complex && ExperimentCsvLogStore.ReadCsv(eventPath).Last()[7] == "event,日本語\n01", "Japanese, comma, quotation and embedded CR/LF round-trip as one CSV record");
                Check(File.ReadAllBytes(eventPath).Take(3).SequenceEqual(new byte[] { 239, 187, 191 }), "UTF-8 BOM is present for Excel");
                var clockJump = E("CLOCK_CHECK", 47); clockJump.ObservedAtUtc = firstUtc.AddHours(-3);
                store.Record(clockJump);
                Check(ExperimentCsvLogStore.ReadCsv(eventPath).Last()[5] == "47.000000", "UTC wall-clock jumps do not change elapsed duration");
                var reopened = new ExperimentCsvLogStore(store.RootDirectory, "run-2");
                Check(!reopened.Record(E("BOARD_CONFIRMED", 100)), "Event ID deduplication survives logger restart");
                reopened.Record(E("AFTER_RESTART", 101));
                Check(ExperimentCsvLogStore.ReadCsv(eventPath).Last()[5] == "", "Restarted logger leaves untrustworthy Session elapsed blank");
                File.Delete(Path.Combine(store.RootDirectory, "normal-session", "session.csv"));
                reopened.Record(E("BOARD_CONFIRMED", 102));
                Check(File.Exists(Path.Combine(store.RootDirectory, "normal-session", "session.csv")), "Duplicate notification repairs a missing summary from durable event rows");
                store.Record(E("SESSION_CREATED", 50, session: "next-session"));
                Check(Directory.Exists(Path.Combine(store.RootDirectory, "next-session")) && ExperimentCsvLogStore.ReadCsv(eventPath).Count == 9, "New Session uses a separate folder and retains old Session records");
                bool rejected = false;
                try { store.Record(E("SESSION_CREATED", 1, session: "../escape")); } catch (ArgumentException) { rejected = true; }
                Check(rejected, "Session ID cannot escape the output directory");
                rejected = false;
                try { store.Record(E("EXPERIMENT_STARTED", 6, "other-start", condition: "Proposed")); } catch (InvalidDataException) { rejected = true; }
                Check(rejected, "A second conflicting Condition cannot relabel an existing Session");
                string blocked = Path.Combine(root, "blocked-root"); File.WriteAllText(blocked, "not a directory");
                var retryStore = new ExperimentCsvLogStore(blocked);
                rejected = false;
                try { retryStore.Record(E("SESSION_CREATED", 0)); } catch (IOException) { rejected = true; }
                File.Delete(blocked); retryStore.Record(E("SESSION_CREATED", 0));
                Check(rejected && ExperimentCsvLogStore.ReadCsv(Path.Combine(blocked, "normal-session", "phase_events.csv")).Count == 2,
                    "IO failure is reportable and retry saves exactly one event");
            }
            finally { CultureInfo.CurrentCulture = originalCulture; }

            var cards = new ExperimentCsvLogStore(Path.Combine(root, "operations"), "operations-run");
            foreach (string mode in new[] { "Normal", "Proposed" })
            {
                ExperimentCsvLogStore.Observation CardEvent(string type, int seconds, string id = null)
                {
                    var entry = E(type, seconds, id, session: mode + "-operations");
                    entry.CardSetId = "A";
                    return entry;
                }
                var start = CardEvent("EXPERIMENT_STARTED", 0);
                start.Condition = mode;
                cards.Record(start);
                string path = Path.Combine(cards.GetSessionDirectory(start.SessionId), "card_operations.csv");
                Check(!File.Exists(path), mode + " existing phase-only logs do not acquire a card log");
                var initialize = CardEvent("CARD_LOG_INITIALIZED", 1);
                initialize.InitializeCardLog = true;
                cards.Record(initialize);
                Check(ExperimentCsvLogStore.ReadCsv(path).Count == 1, mode + " zero operations have a header");
                void Placement(int second, string participant, int client, string from, string to)
                {
                    var entry = CardEvent("CARD_PLACEMENT_COMMITTED", second, "operation-" + second);
                    entry.ParticipantId = participant;
                    entry.CardOperation = new ExperimentCsvLogStore.CardOperation {
                        Client = client, Card = "A01", FromTier = from, ToTier = to
                    };
                    if (second == 3)
                    {
                        var before = File.ReadAllBytes(path);
                        string blockedWrite = path + ".tmp";
                        Directory.CreateDirectory(blockedWrite);
                        bool failed = false;
                        try { cards.Record(entry); }
                        catch (IOException) { failed = true; }
                        catch (UnauthorizedAccessException) { failed = true; }
                        finally { Directory.Delete(blockedWrite); }
                        Check(failed && before.SequenceEqual(File.ReadAllBytes(path)), mode + " failed write preserves prior operations");
                    }
                    Check(cards.Record(entry) && !cards.Record(entry), mode + " operation is idempotent at " + second);
                }
                Placement(2, "P01", 1, "Unclassified", "A");
                Placement(3, "P02", 2, "A", "B");
                Placement(4, "", 3, "B", "B");
                var cardRows = ExperimentCsvLogStore.ReadCsv(path);
                Check(cardRows.Count == 4 && cardRows[1][9] == "P01" && cardRows[1][12] == "Unclassified" &&
                    cardRows[2][10] == "2" && cardRows[2][13] == "B" && cardRows[3][9] == "" &&
                    cardRows[3][12] == cardRows[3][13] && cardRows[3][15] == "4.000000",
                    mode + " stores identities, tier transitions, same-tier and shared monotonic time");
            }

            string integration = Path.Combine(root, "integration");
            ExperimentResultVerification.RunWithLogs(integration);
            var flow = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText("Library/ExperimentResultVerification/result.json"));
            Check((bool)flow["passed"], "All existing Normal / Proposed / Role / Reset / Result checks pass with Logger attached: " + (string)flow["failure"]);
            Check(!Directory.Exists(Path.Combine(integration, "client-1")) && !Directory.Exists(Path.Combine(integration, "client-2")), "Participant endpoints write no CSV files");
            var folders = Directory.GetFiles(Path.Combine(integration, "client-99"),"phase_events.csv",SearchOption.AllDirectories).Select(Path.GetDirectoryName).ToArray();
            Check(folders.Length == 3, "Experimenter writes separate folders for all three Sessions including the final unstarted Session");
            var observedCards = folders.Select(folder => Path.Combine(folder, "card_operations.csv"))
                .Where(File.Exists).Select(ExperimentCsvLogStore.ReadCsv).ToArray();
            Check(observedCards.Length == 2 && observedCards.All(rows => rows.Count == 4 &&
                rows.Skip(1).Select(r => r[6]).Distinct().Count() == 3 &&
                rows.Skip(1).All(r => r[9] == "" && r[10] == (r[3] == "Normal" ? "1" : "2"))),
                "Only participant placement updates are observed once on the PC in both modes");
            var csvs = folders.Select(f => ExperimentCsvLogStore.ReadCsv(Path.Combine(f, "phase_events.csv"))).ToArray();
            var startedSessions = csvs.Where(rows => rows.Any(r => r[3] == "EXPERIMENT_STARTED")).ToArray();
            Check(startedSessions.Length == 2 && startedSessions.Select(rows => rows[1][2]).OrderBy(x => x).SequenceEqual(new[] { "Normal", "Proposed" }), "Actual Start Mode distinguishes Normal and Proposed even when Mode was selected after Session creation");
            foreach (var rows in startedSessions)
            {
                string condition = rows[1][2];
                var expectedEvents = new List<string> { "BOARD_CONFIRMED", "CARD_LOG_INITIALIZED", "EXPERIMENT_STARTED", "SESSION_CREATED", "SESSION_RESET_COMPLETED", "SESSION_RESET_STARTED" };
                if (condition == "Proposed") expectedEvents.AddRange(new[] { "PARTICIPANT_SUBMITTED", "PARTICIPANT_SUBMITTED", "ANSWERS_REVEALED", "SHARED_CLASSIFICATION_STARTED" });
                Check(rows.Skip(1).Select(r => r[3]).OrderBy(x => x).SequenceEqual(expectedEvents.OrderBy(x => x)), condition + ": phase events are saved exactly once including reconnect");
                Check(rows.Skip(1).All(r => r[1] == rows[1][1] && r[2] == condition && (r[6] == "" || r[6].StartsWith("client:")) && r[9] == "99"), condition + ": Session, Condition, observer and non-participant phase semantics are consistent");
                Check(rows.Skip(1).Select(r => r[7]).Distinct().Count() == expectedEvents.Count, condition + ": Event IDs are unique");
                var parsedElapsed = rows.Skip(1).Select(r => double.Parse(r[5], CultureInfo.InvariantCulture)).ToArray();
                Check(parsedElapsed.SequenceEqual(parsedElapsed.OrderBy(x => x)), condition + ": elapsed times are nondecreasing");
            }
            Check(csvs.Single(rows => !rows.Any(r => r[3] == "EXPERIMENT_STARTED")).Skip(1).All(r => r[2] == ""), "An unstarted Session never invents a Condition");
            // Keep small, real CSV outputs for inspection and Python/Excel verification.
            string sample = Path.Combine(root, "samples");
            foreach (var rows in startedSessions)
            {
                string folder = Path.Combine(sample, rows[1][2]); Directory.CreateDirectory(folder);
                string source = folders.Single(f => (Path.GetFileName(f) == rows[1][1] || Path.GetFileName(f).EndsWith("_"+rows[1][1],StringComparison.Ordinal)));
                foreach (string file in new[] { "session.csv", "phase_events.csv", "card_operations.csv" }) File.Copy(Path.Combine(source, file), Path.Combine(folder, file));
            }
            report.passed = true;
        }
        catch (Exception error) { report.failure = error.ToString(); Debug.LogException(error); }
        finally
        {
            report.checks = checks.ToArray(); Directory.CreateDirectory("Library/ExperimentLoggerVerification");
            File.WriteAllText("Library/ExperimentLoggerVerification/result.json", JsonUtility.ToJson(report, true));
            Debug.Log("[Experiment Logger Verification] " + (report.passed ? "PASS" : "FAIL") + " / " + checks.Count + " / " + root);
        }
    }
}
#endif
