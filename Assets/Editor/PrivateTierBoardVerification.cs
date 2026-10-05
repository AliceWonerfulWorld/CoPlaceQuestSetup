#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Styly.NetSync;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Runs the actual components/API in an isolated preview scene, with NetSync's
// own offline transport. Does not change the open scene, roles or live Room.
public static class PrivateTierBoardVerification
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    [Serializable] private class Report { public bool passed; public string[] checks; public string failure; }
    [MenuItem("Tools/Experiment/Verify Private TierBoard")]
    public static void Run()
    {
        var report = new Report(); var checks = new List<string>();
        var scene = EditorSceneManager.OpenPreviewScene("Assets/Scenes/QuestSetup.unity");
        try
        {
            var components = scene.GetRootGameObjects().SelectMany(o => o.GetComponentsInChildren<Component>(true)).ToArray();
            T One<T>() where T : Component => components.OfType<T>().Single();
            var net = One<NetSyncManager>();
            Set(net, "_offlineMode", true); Set(net, "_clientNo", 1);
            var asm = typeof(NetSyncManager).Assembly;
            var connectionType = asm.GetType("Styly.NetSync.OfflineConnectionManager", true);
            var connection = Activator.CreateInstance(connectionType, true);
            connectionType.GetMethod("Connect").Invoke(connection, new object[] { "", 0, 0, 0, "private-board-test" });
            Set(net, "_connectionManager", connection);
            var variablesType = asm.GetType("Styly.NetSync.NetworkVariableManager", true);
            var variables = Activator.CreateInstance(variablesType, Flags, null, new object[] { connection, "private-board-test", net }, null);
            Set(net, "_networkVariableManager", variables);
            variablesType.GetMethod("MarkInitialSyncComplete").Invoke(variables, null);
            foreach (string eventName in new[] { "OnGlobalVariableChanged", "OnClientVariableChanged" })
            {
                var info = variablesType.GetEvent(eventName);
                info.AddEventHandler(variables, Delegate.CreateDelegate(info.EventHandlerType, net,
                    typeof(NetSyncManager).GetMethod(eventName + "Handler", Flags)));
            }
            void Peer(int client, string key, string value) => variablesType.GetMethod("SetClientVariable").Invoke(variables,
                new object[] { key, value, client, "private-board-test" });
            var manager = One<ExperimentManager>(); var registry = One<ExperimentParticipantRegistry>();
            var session = One<ExperimentSessionManager>(); var sync = One<CandidateSyncTest>();
            var normal = One<NormalPlacementSync>(); var board = One<PrivateTierBoardController>();
            Set(registry, "localRole", ExperimentParticipantRegistry.ClientRole.Participant);
            Set(manager, "lobby", null); // Isolate the existing post-Start flow.
            Set(manager, "currentMode", ExperimentManager.ExperimentMode.Proposed);
            Set(manager, "currentState", ExperimentManager.ExperimentState.Idle);
            foreach (var zone in components.OfType<TierZone>()) Call(zone, "Awake");
            foreach (var card in components.OfType<CardTierDetector>()) Call(card, "Awake");
            foreach (var card in components.OfType<PrivateAnswerCard>()) Call(card, "Awake");
            Call(normal, "Awake"); Call(board, "Awake");
            Call(registry, "Start"); Peer(2, ExperimentParticipantRegistry.RoleVariable, "Participant"); registry.RefreshParticipants();
            Call(manager, "Start"); Call(sync, "Start"); Call(session, "Start"); Call(board, "OnEnable"); Call(board, "Start"); Call(board, "Update");
            // One offline client is alive; this makes legacy per-card completion
            // eligible so the no-premature-Reveal test exercises that exact case.
            Set(registry, "expectedParticipantCount", 1);
            var mains = components.OfType<CardTierDetector>().ToArray();
            var initial = mains.Select(c => (c.transform.position, c.CurrentTier)).ToArray();
            var privateCards = components.OfType<PrivateAnswerCard>().OrderBy(c => c.CardId).ToArray();
            void Check(bool result, string name) { if (!result) throw new Exception(name); checks.Add(name); }
            Check(privateCards.Length == 3 && components.OfType<TierZone>().Count() == 10, "Three private cards / five independent private zones");
            Check(privateCards.All(c => c.GetComponent<CardTierDetector>() == null &&
                c.GetComponents<MonoBehaviour>().All(m => m.GetType().Namespace == null || !m.GetType().Namespace.StartsWith("Styly.NetSync"))), "Private cards contain no Main/NetSync behaviour");
            Check(board.CanInteract && board.IsVisible, "Proposed Participant can interact");
            Check(board.TryPlaceCard("Card_03", "C") && board.TryPlaceCard("Card_01", "A") && board.TryPlaceCard("Card_02", "B"), "Answer all cards in arbitrary order");
            Check(manager.CurrentCardId == "Card_01", "Private input does not depend on or change CurrentCardId");
            for (int i = 0; i < 3; i++)
            {
                string id = "Card_0" + (i + 1); string tier = new[] { "D", "C", "A" }[i];
                Peer(2, "candidate_" + id, SessionVariableTransport.Encode(session, tier));
                Peer(2, "selectedAt_" + id, SessionVariableTransport.Encode(session, DateTime.UtcNow.ToString("o")));
                Peer(2, "answered_" + id, SessionVariableTransport.Encode(session, "true"));
            }
            Call(sync, "LateUpdate"); Call(board, "Update");
            Check(sync.GetParticipantCandidates(1).Values.Select(a => a.Tier).SequenceEqual(new[] { "A", "B", "C" }) &&
                sync.GetParticipantCandidates(2).Values.Select(a => a.Tier).SequenceEqual(new[] { "D", "C", "A" }), "Participant snapshots preserve different answers");
            Check(privateCards.Select(c => c.CurrentTier).SequenceEqual(new[] { "A", "B", "C" }), "Private view restores only client 1");
            Check(manager.CurrentState == ExperimentManager.ExperimentState.WaitingForAnswers &&
                privateCards.All(c => !sync.IsCardRevealed(c.CardId)), "All answers remain editable / no premature single-card Reveal");
            Check(((TMP_Text)Get(sync, "candidateStatusText")).text == "" &&
                ((System.Collections.IDictionary)Get(One<CandidateMarkerManager>(), "markers")).Count == 0, "Legacy status and markers expose no answers in private phase");
            Check(board.TryPlaceCard("Card_01", "C"), "Answered card can move again");
            Check(sync.TryGetParticipantAnswer(1, "Card_01", out var changed) && changed.Tier == "C" && changed.Answered && changed.SelectedAt != "", "Changed answer keeps UTC selectedAt");
            Check(board.TryPlaceCard("Card_01", "Unclassified") && sync.TryGetParticipantAnswer(1, "Card_01", out var cancelled) &&
                !cancelled.Answered && cancelled.SelectedAt == "", "Unclassified cancels and clears selectedAt");
            Check(board.TryPlaceCard("Card_01", "A"), "Cancelled card can answer again");
            Check(mains.Select((c, i) => c.transform.position == initial[i].position && c.CurrentTier == initial[i].CurrentTier).All(v => v) &&
                !net.GetAllGlobalVariables().Keys.Any(k => k.StartsWith("normalPlacement_")), "Private answers never move or publish Main placements");
            foreach (var state in new[] { ExperimentManager.ExperimentState.Revealed, ExperimentManager.ExperimentState.Discussion, ExperimentManager.ExperimentState.Confirmed })
            {
                Set(manager, "currentState", state); Call(board, "Update");
                Check(!board.CanInteract && !board.TryPlaceCard("Card_02", "D"), "Input rejected in " + state);
            }
            Set(manager, "currentState", ExperimentManager.ExperimentState.Idle);
            Set(registry, "localRole", ExperimentParticipantRegistry.ClientRole.Experimenter); Call(board, "Update");
            Check(!board.IsVisible && !board.CanInteract, "Experimenter sees no private board");
            Set(manager, "lobby", null); // Isolate the existing post-Start flow.
            Set(manager, "currentMode", ExperimentManager.ExperimentMode.Normal); Call(board, "Update");
            Check(!board.IsVisible && !board.CanInteract && mains.All(c => c.GetComponent<Renderer>().enabled), "Normal restores Main visuals and disables Private input");
            Set(manager, "lobby", null); // Isolate the existing post-Start flow.
            Set(manager, "currentMode", ExperimentManager.ExperimentMode.Proposed);
            Set(registry, "localRole", ExperimentParticipantRegistry.ClientRole.Participant); Call(board, "Update");
            // Move only the local view away from the server snapshot, then simulate OnReady.
            var localZones = (TierZone[])Get(board, "tierZones");
            privateCards[0].ApplyPlacement(localZones.Single(z => z.tierName == "D"));
            Call(sync, "OnReady"); Set(board, "wasReady", false); Call(board, "Update");
            Check(privateCards[0].CurrentTier == "A", "Reconnect restores authoritative own candidate");
            Set(registry, "localRole", ExperimentParticipantRegistry.ClientRole.Experimenter); Call(registry, "Update");
            Check(session.StartNewSession(), "New Session accepted by Experimenter");
            Call(session, "Update");
            // The simulated peer acknowledges its reset after publishing its defaults.
            foreach (string id in sync.AnswerCardIds)
            {
                Peer(2, "candidate_" + id, SessionVariableTransport.Encode(session, "Unclassified"));
                Peer(2, "answered_" + id, SessionVariableTransport.Encode(session, "false"));
                Peer(2, "selectedAt_" + id, SessionVariableTransport.Encode(session, ""));
            }
            Peer(2, ExperimentSessionManager.AckVariable, session.CurrentSessionId);
            Call(session, "Update"); Call(session, "Update"); Call(board, "Update");
            Check(!session.IsResettingSession && session.SessionEpoch == 1 && privateCards.All(c => c.CurrentTier == "Unclassified") &&
                sync.GetParticipantCandidates(1).Values.All(a => !a.Answered && a.Tier == "Unclassified" && a.SelectedAt == ""), "Session epoch / data / local view reset together");
            Set(registry, "localRole", ExperimentParticipantRegistry.ClientRole.Participant); Call(registry, "Update"); registry.RefreshParticipants();
            net.SetGlobalVariable("experimentMode", "Normal"); Call(board, "Update"); Call(normal, "Start");
            var agreement = One<FinalAgreementManager>(); Call(agreement, "Start");
            Check(normal.SendPlacement("Card_01", "A") && normal.SendPlacement("Card_02", "B") && normal.SendPlacement("Card_03", "C"),
                "Normal shared placement writes still succeed after private Session Reset");
            Check(mains.All(c => c.CurrentTier == new Dictionary<string, string> { ["Card_01"] = "A", ["Card_02"] = "B", ["Card_03"] = "C" }[c.name]),
                "Normal receives and applies its authoritative shared placement echoes");
            Call(agreement, "Update"); agreement.ToggleLocalReady(); Call(agreement, "LateUpdate");
            Check(agreement.IsBoardConfirmed && manager.CurrentState == ExperimentManager.ExperimentState.Confirmed &&
                mains.All(c => normal.IsCardConfirmed(c.name)), "Normal Ready / Final Agreement / Board Confirmed remain functional");
            report.passed = true;
        }
        catch (Exception error) { report.failure = error.ToString(); Debug.LogException(error); }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
            report.checks = checks.ToArray(); Directory.CreateDirectory("Library/PrivateBoardVerification");
            File.WriteAllText("Library/PrivateBoardVerification/result.json", JsonUtility.ToJson(report, true));
            Debug.Log($"[Private Board Verification] {(report.passed ? "PASS" : "FAIL")} / {checks.Count} checks");
        }
    }
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, Flags).SetValue(target, value);
    private static object Get(object target, string name) => target.GetType().GetField(name, Flags).GetValue(target);
    private static void Call(object target, string name) => target.GetType().GetMethod(name, Flags).Invoke(target, null);
}
#endif
