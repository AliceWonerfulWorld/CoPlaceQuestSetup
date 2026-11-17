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
    private readonly string runId;
    private readonly Dictionary<string, double?> origins = new Dictionary<string, double?>();
    private static readonly UTF8Encoding Utf8Bom = new UTF8Encoding(true);
    public string RootDirectory => root;

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
        string folder = Path.Combine(root, entry.SessionId);
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
        if (!origins.TryGetValue(entry.SessionId, out double? origin))
        {
            // A restarted logger has no trustworthy monotonic origin for an existing Session.
            origin = rows.Count == 0 ? entry.MonotonicSeconds : (double?)null;
            origins.Add(entry.SessionId, origin);
        }
        bool duplicate = rows.Any(r => r[7] == entry.EventId);
        string condition = rows.Select(r => r[2]).FirstOrDefault(c => !string.IsNullOrEmpty(c)) ?? "";
        if (!string.IsNullOrEmpty(entry.Condition))
        {
            if (entry.EventType != "EXPERIMENT_STARTED") throw new ArgumentException("Only a confirmed Experiment Start may set Condition");
            if (condition != "" && condition != entry.Condition) throw new InvalidDataException("Condition changed within a Session");
            condition = entry.Condition;
        }
        if (!duplicate)
        {
            string elapsed = origin.HasValue ? Math.Max(0, entry.MonotonicSeconds - origin.Value).ToString("F6", CultureInfo.InvariantCulture) : "";
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
