using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

// No Unity/network dependencies. phase_events.csv is the source of truth;
// session.csv is a recoverable summary. Stage 1 has only a few rows per Session.
public sealed class ExperimentCsvLogStore
{
    public sealed class Observation
    {
        public string SessionId, EventType, EventId, ParticipantId = "", Condition = "";
        public string CardSetId = "";
        public string ObserverClientId = "", ObservationKind = "live", ParticipantsAtStart = "";
        public long Epoch;
        public DateTimeOffset ObservedAtUtc;
        public double MonotonicSeconds;
        public FinalPlacement[] FinalPlacements;
        public IndependentAnswer[] IndependentAnswers;
        public string SnapshotId;
        public bool InitializeCardLog;
        public CardOperation CardOperation;
        public ExperimentDataStore.Metadata Metadata;
    }
    public sealed class CardOperation { public int Client; public string Card, FromTier, ToTier; }
    private static readonly string[] CardColumns = {
        "schema_version", "session_id", "session_epoch", "condition", "card_set_id", "phase",
        "event_id", "operation_id", "event_type", "participant_id", "participant_client_id", "card_id",
        "from_tier", "to_tier", "observed_time_utc", "session_elapsed_seconds", "observer_run_id"
    };
    internal static bool IsPlacementTier(string tier) => tier == "Unclassified" || IsTier(tier);

    private bool SaveCardLog(string folder, Observation entry, string condition, string elapsed)
    {
        if (condition != "Normal" && condition != "Proposed" || entry.Epoch <= 0 || string.IsNullOrEmpty(entry.CardSetId))
            throw new InvalidDataException("Missing card log session metadata");
        string epoch = entry.Epoch.ToString(CultureInfo.InvariantCulture);
        string path = Path.Combine(folder, "card_operations.csv");
        bool exists = File.Exists(path);
        var rows = exists ? ReadCsv(path) : new List<string[]> { CardColumns };
        if (rows.Count == 0 || !rows[0].SequenceEqual(CardColumns) || rows.Skip(1).Any(r =>
            r.Length != CardColumns.Length || r[1] != entry.SessionId || r[2] != epoch ||
            r[3] != condition || r[4] != entry.CardSetId))
            throw new InvalidDataException("Invalid card operation log: " + path);

        var operation = entry.CardOperation;
        if (operation == null)
        {
            if (!exists) WriteAtomic(path, rows);
            return !exists;
        }

        if (!exists || operation.Client <= 0 || string.IsNullOrEmpty(operation.Card) || !IsPlacementTier(operation.ToTier) ||
            (operation.FromTier != "" && !IsPlacementTier(operation.FromTier)))
            throw new InvalidDataException("Invalid card placement observation");
        string eventId = entry.EventId + ":" + entry.EventType;
        if (rows.Skip(1).Any(r => r[6] == eventId)) return false;
        rows.Add(new[] {
            "1", entry.SessionId, epoch, condition, entry.CardSetId, "shared_classification",
            eventId, entry.EventId, entry.EventType, entry.ParticipantId ?? "",
            operation.Client.ToString(CultureInfo.InvariantCulture), operation.Card, operation.FromTier, operation.ToTier,
            entry.ObservedAtUtc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture), elapsed, runId
        });
        WriteAtomic(path, rows);
        return true;
    }
    public sealed class FinalPlacement { public string CardId, Tier; }
    public sealed class IndependentAnswer { public int ClientId; public string CardId, Tier, SelectedAt, SubmissionId; }
    public static readonly string[] FinalColumns = { "schema_version", "session_id", "session_epoch", "condition", "card_set_id", "card_id", "final_tier", "observed_time_utc" };
    public static readonly string[] IndependentColumns = { "schema_version", "session_id", "session_epoch", "condition", "card_set_id", "participant_client_id", "card_id", "initial_tier", "selected_at_utc", "observed_time_utc", "reveal_snapshot_id", "submission_id" };
    private static bool IsTier(string tier) => tier == "A" || tier == "B" || tier == "C" || tier == "D" || tier == "E";
    private void SaveSnapshots(Observation e)
    {
        if (e.FinalPlacements == null && e.IndependentAnswers == null) return;
        if (e.Epoch <= 0 || string.IsNullOrEmpty(e.CardSetId) || (e.Condition != "Normal" && e.Condition != "Proposed")) throw new InvalidDataException("Snapshot metadata missing");
        string[] Prefix() => new[] { "1", e.SessionId, e.Epoch.ToString(CultureInfo.InvariantCulture), e.Condition, e.CardSetId };
        string observed = e.ObservedAtUtc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
        if (e.FinalPlacements != null)
        {
            if (e.EventType != "BOARD_CONFIRMED" || e.FinalPlacements.Length == 0 || e.FinalPlacements.Any(r => r == null || string.IsNullOrEmpty(r.CardId) || !IsTier(r.Tier)) || e.FinalPlacements.Select(r => r.CardId).Distinct().Count() != e.FinalPlacements.Length) throw new InvalidDataException("Invalid final placements");
            var rows = e.FinalPlacements.OrderBy(r => r.CardId, StringComparer.Ordinal).Select(r => Prefix().Concat(new[] { r.CardId, r.Tier, observed }).ToArray());
            SaveImmutable(Path.Combine(GetSessionDirectory(e.SessionId), "final_placements.csv"), FinalColumns, rows, 7);
        }
        if (e.IndependentAnswers != null)
        {
            if (e.Condition != "Proposed" || e.EventType != "ANSWERS_REVEALED" || string.IsNullOrEmpty(e.SnapshotId) || e.IndependentAnswers.Length == 0 ||
                e.IndependentAnswers.Any(r => r == null || r.ClientId <= 0 || string.IsNullOrEmpty(r.CardId) || string.IsNullOrEmpty(r.SubmissionId) || !IsTier(r.Tier) ||
                    !DateTime.TryParseExact(r.SelectedAt, "o", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var time) || time.Kind != DateTimeKind.Utc) ||
                e.IndependentAnswers.Select(r => r.ClientId + ":" + r.CardId).Distinct().Count() != e.IndependentAnswers.Length)
                throw new InvalidDataException("Invalid revealed answers");
            var cards = new HashSet<string>(e.IndependentAnswers.Where(r => r.ClientId == e.IndependentAnswers[0].ClientId).Select(r => r.CardId));
            if (e.IndependentAnswers.GroupBy(r => r.ClientId).Any(g => !cards.SetEquals(g.Select(r => r.CardId)) || g.Select(r => r.SubmissionId).Distinct().Count() != 1)) throw new InvalidDataException("Incomplete independent answer matrix");
            var rows = e.IndependentAnswers.OrderBy(r => r.ClientId).ThenBy(r => r.CardId, StringComparer.Ordinal).Select(r => Prefix().Concat(new[] {
                r.ClientId.ToString(CultureInfo.InvariantCulture), r.CardId, r.Tier, r.SelectedAt, observed, e.SnapshotId, r.SubmissionId }).ToArray());
            SaveImmutable(Path.Combine(GetSessionDirectory(e.SessionId), "independent_answers.csv"), IndependentColumns, rows, 9);
        }
    }
    // Ignore only the observation time on duplicate restoration. Every answer/identity must agree.
    private static void SaveImmutable(string path, string[] columns, IEnumerable<string[]> values, int timeColumn)
    {
        var wanted = new[] { columns }.Concat(values).ToList();
        if (File.Exists(path))
        {
            var saved = ReadCsv(path);
            if (saved.Count != wanted.Count || !saved[0].SequenceEqual(columns) || saved.Skip(1).Where((r, i) =>
                r.Length != columns.Length || !r.Where((v, c) => c != timeColumn).SequenceEqual(wanted[i + 1].Where((v, c) => c != timeColumn))).Any())
                throw new InvalidDataException("Immutable snapshot conflicts with saved CSV: " + path);
            return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        // Never replace an existing result, including a concurrent writer's result.
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { WriteAtomic(temporary, wanted); File.Move(temporary, path); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }

    }

    public static readonly string[] EventColumns = {
        "schema_version", "session_id", "condition", "event_type", "event_time_utc",
        "session_elapsed_seconds", "participant_id", "event_id", "session_epoch",
        "observer_client_id", "observer_run_id", "observation_kind", "time_basis", "participants_at_start"
    };
    public static readonly string[] SessionColumns = {
        "schema_version", "session_id", "condition", "session_epoch", "first_observed_utc",
        "experiment_started_observed_utc", "board_confirmed_observed_utc",
        "reset_started_observed_utc", "reset_completed_observed_utc", "participants_at_start", "time_basis"
    };
    private const string TimeBasis = "experimenter_observation_utc; elapsed=local_monotonic_since_first_session_observation";
    private readonly string root;
    private readonly Dictionary<string,ExperimentDataStore.Metadata> metadata = new Dictionary<string,ExperimentDataStore.Metadata>();
    public string IndexError {get;private set;} = "";
    public bool RebuildIndex(){var data=new ExperimentDataStore(root);bool ok=data.RebuildIndex();IndexError=data.IndexError;return ok;}
    public string GetSessionDirectory(string id)
    {
        if(string.IsNullOrEmpty(id)||id.Length>128||id.Any(c=>!(char.IsLetterOrDigit(c)&&c<128||c=='-'||c=='_')))throw new ArgumentException("Invalid session directory ID");
        if(!metadata.TryGetValue(id,out var m)){m=new ExperimentDataStore(root).Find(id);if(m!=null)metadata[id]=m;}
        return m!=null?new ExperimentDataStore(root).DirectoryFor(m):Path.Combine(root,id);
    }
    private readonly string runId;
    private readonly Dictionary<string, double?> origins = new Dictionary<string, double?>();
    private static readonly UTF8Encoding Utf8Bom = new UTF8Encoding(true);
    public string RootDirectory => root;
    public bool HasMonotonicOrigin(string sessionId) => sessionId != null && origins.TryGetValue(sessionId, out var origin) && origin.HasValue;

    public ExperimentCsvLogStore(string rootDirectory, string observerRunId = null)
    {
        root = Path.GetFullPath(rootDirectory);
        runId = observerRunId ?? Guid.NewGuid().ToString("N");
    }

    // Called on the Unity main thread. Stable IDs also deduplicate after reopening files.
    // Throws on IO failure; the adapter retains the observation and retries without stopping the experiment.
    public bool Record(Observation entry)
    {
        Validate(entry);
        var data = new ExperimentDataStore(root);
        if(entry.Metadata!=null)
        {
            if(entry.Metadata.sessionId!=entry.SessionId || entry.Metadata.epoch!=entry.Epoch || entry.Metadata.cardSetId!=entry.CardSetId)throw new InvalidDataException("Observation metadata mismatch");
            metadata[entry.SessionId]=entry.Metadata;data.Save(entry.Metadata);
        }
        string folder = GetSessionDirectory(entry.SessionId);
        string eventPath = Path.Combine(folder, "phase_events.csv");
        var rows = new List<string[]>();
        if (File.Exists(eventPath))
        {
            rows = ReadCsv(eventPath);
            if (rows.Count == 0 || !rows[0].SequenceEqual(EventColumns) || rows.Skip(1).Any(r => r.Length != EventColumns.Length))
                throw new InvalidDataException("Unsupported or incomplete phase_events.csv: " + eventPath);
            rows.RemoveAt(0);
            if (rows.Any(r => r[1] != entry.SessionId)) throw new InvalidDataException("Mixed Session IDs in " + eventPath);
        }
        if (rows.Any(r => r[8] != entry.Epoch.ToString(CultureInfo.InvariantCulture))) throw new InvalidDataException("Session Epoch mismatch");
        string setPathBeforeWrite = Path.Combine(folder, "card_set.csv");
        if (File.Exists(setPathBeforeWrite) && !string.IsNullOrEmpty(entry.CardSetId))
        {
            var savedSet = ReadCsv(setPathBeforeWrite);
            if (savedSet.Count != 2 || savedSet[1].Length != 2 || savedSet[1][0] != entry.SessionId || savedSet[1][1] != entry.CardSetId) throw new InvalidDataException("Card set changed within Session");
        }
        if (!origins.TryGetValue(entry.SessionId, out double? origin))
        {
            // A restarted logger has no trustworthy monotonic origin for an existing Session.
            origin = rows.Count == 0 ? entry.MonotonicSeconds : (double?)null;
            origins.Add(entry.SessionId, origin);
        }
        bool phaseDuplicate = rows.Any(r => r[7] == entry.EventId);
        bool duplicate = phaseDuplicate;
        string condition = rows.Select(r => r[2]).FirstOrDefault(c => !string.IsNullOrEmpty(c)) ?? "";
        if (!string.IsNullOrEmpty(entry.Condition))
        {
            if (entry.EventType != "EXPERIMENT_STARTED" && entry.FinalPlacements == null && entry.IndependentAnswers == null) throw new ArgumentException("Only a confirmed Experiment Start may set Condition");
            if (entry.EventType != "EXPERIMENT_STARTED" && condition != entry.Condition) throw new InvalidDataException("Snapshot requires matching saved Experiment Start");
            if (condition != "" && condition != entry.Condition) throw new InvalidDataException("Condition changed within a Session");
            condition = entry.Condition;
        }
        // Persist immutable answers before acknowledging their phase event as saved.
        if ((entry.FinalPlacements != null || entry.IndependentAnswers != null) && condition != entry.Condition) throw new InvalidDataException("Snapshot Condition mismatch");
        SaveSnapshots(entry);
        string elapsed = origin.HasValue ? Math.Max(0, entry.MonotonicSeconds - origin.Value).ToString("F6", CultureInfo.InvariantCulture) : "";
        if (entry.InitializeCardLog || entry.CardOperation != null)
            duplicate = !SaveCardLog(folder, entry, condition, elapsed);
        if (!phaseDuplicate && entry.CardOperation == null)
        {
            rows.Add(new[] { "1", entry.SessionId, condition, entry.EventType,
                entry.ObservedAtUtc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture), elapsed,
                entry.ParticipantId ?? "", entry.EventId, entry.Epoch.ToString(CultureInfo.InvariantCulture),
                entry.ObserverClientId ?? "", runId, entry.ObservationKind ?? "live", TimeBasis, entry.ParticipantsAtStart ?? "" });
        }
        // Start fixes the Condition of earlier setup/reset observations, preserving their IDs/times.
        foreach (var row in rows) row[2] = condition;
        Directory.CreateDirectory(folder);
        WriteAtomic(eventPath, new[] { EventColumns }.Concat(rows));
        // Rebuild even for duplicates: retry also repairs a failed/missing summary write.
        string TimeOf(string type) => rows.FirstOrDefault(r => r[3] == type)?[4] ?? "";
        var summary = new[] { "1", entry.SessionId, condition, rows[0][8], rows[0][4],
            TimeOf("EXPERIMENT_STARTED"), TimeOf("BOARD_CONFIRMED"), TimeOf("SESSION_RESET_STARTED"),
            TimeOf("SESSION_RESET_COMPLETED"), rows.FirstOrDefault(r => r[3] == "EXPERIMENT_STARTED")?[13] ?? "", TimeBasis };
        WriteAtomic(Path.Combine(folder, "session.csv"), new[] { SessionColumns, summary });
        // Additive sidecar: stage-1 files and column positions remain unchanged.
        if (!string.IsNullOrEmpty(entry.CardSetId))
        {
            string setPath = Path.Combine(folder, "card_set.csv");
            if (File.Exists(setPath))
            {
                var setRows = ReadCsv(setPath);
                if (setRows.Count != 2 || setRows[1].Length != 2 || setRows[1][0] != entry.SessionId || setRows[1][1] != entry.CardSetId)
                    throw new InvalidDataException("Card set changed within Session " + entry.SessionId);
            }
            else WriteAtomic(setPath, new[] { new[] { "session_id", "card_set_id" }, new[] { entry.SessionId, entry.CardSetId } });
        }
        if(metadata.TryGetValue(entry.SessionId,out var m))
        {
            m.startedAtUtc=TimeOf("EXPERIMENT_STARTED");
            var start=rows.FirstOrDefault(r=>r[3]=="EXPERIMENT_STARTED");
            m.participantsAtStart=start==null?null:start[13].Split(new[]{';'},StringSplitOptions.RemoveEmptyEntries).Select(v=>int.Parse(v,CultureInfo.InvariantCulture)).Distinct().ToArray();
            m.actualParticipantCount=m.participantsAtStart?.Length??0;
            m.finishedAtUtc=TimeOf("BOARD_CONFIRMED");if(m.finishedAtUtc=="")m.finishedAtUtc=TimeOf("SESSION_ABORTED");
            m.status=TimeOf("SESSION_ABORTED")!=""?"aborted":TimeOf("BOARD_CONFIRMED")!=""?"completed":m.startedAtUtc!=""?"in_progress":TimeOf("SESSION_RESET_COMPLETED")!=""?"prepared":"preparing";
            bool answers=File.Exists(Path.Combine(folder,"final_placements.csv"))&&(m.condition!="Proposed"||File.Exists(Path.Combine(folder,"independent_answers.csv")));
            bool cardLogSaved = !rows.Any(r => r[3] == "CARD_LOG_INITIALIZED") ||
                File.Exists(Path.Combine(folder, "card_operations.csv"));
            m.saveStatus = (m.status == "completed" && !answers) || !cardLogSaved ? "pending" : "complete";
            data.Save(m);data.RebuildIndex();IndexError=data.IndexError;
        }
        return !duplicate;
    }

    private static void Validate(Observation e)
    {
        if (e == null || string.IsNullOrEmpty(e.SessionId) || e.SessionId.Length > 128 ||
            e.SessionId.Any(c => !(c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c >= '0' && c <= '9' || c == '-' || c == '_')))
            throw new ArgumentException("Invalid Session ID for a log folder");
        if (string.IsNullOrEmpty(e.EventId) || string.IsNullOrEmpty(e.EventType) ||
            double.IsNaN(e.MonotonicSeconds) || double.IsInfinity(e.MonotonicSeconds) || e.ObservedAtUtc == default)
            throw new ArgumentException("Event ID, type and an observed timestamp are required");
        if (e.Condition != "" && e.Condition != "Normal" && e.Condition != "Proposed") throw new ArgumentException("Invalid Condition");
    }

    private static string Escape(string value) => "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";
    private static void WriteAtomic(string path, IEnumerable<string[]> rows)
    {
        string temporary = path + ".tmp";
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            using (var writer = new StreamWriter(stream, Utf8Bom, 1024, true))
                foreach (var row in rows) writer.Write(string.Join(",", row.Select(Escape)) + "\r\n");
            stream.Flush(true);
        }
        if (File.Exists(path)) File.Replace(temporary, path, null);
        else File.Move(temporary, path);
    }

    // RFC 4180 quoted fields, including embedded CR/LF and escaped quotes.
    public static List<string[]> ReadCsv(string path)
    {
        string text = File.ReadAllText(path, Encoding.UTF8);
        var rows = new List<string[]>(); var fields = new List<string>(); var field = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '"')
            {
                if (quoted && i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                else quoted = !quoted;
            }
            else if (!quoted && c == ',') { fields.Add(field.ToString()); field.Clear(); }
            else if (!quoted && (c == '\r' || c == '\n'))
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                fields.Add(field.ToString()); field.Clear(); rows.Add(fields.ToArray()); fields.Clear();
            }
            else field.Append(c);
        }
        if (quoted) throw new InvalidDataException("Incomplete quoted CSV field in " + path);
        if (field.Length > 0 || fields.Count > 0) { fields.Add(field.ToString()); rows.Add(fields.ToArray()); }
        return rows;
    }
}
