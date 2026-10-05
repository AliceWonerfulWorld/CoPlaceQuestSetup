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

public static class IndependentAnswerSubmissionVerification
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    [Serializable] private class Report { public bool passed; public string[] checks; public string failure; }
    [MenuItem("Tools/Experiment/Verify Independent Submit UI")]
    public static void Run()
    {
        var r = new Report(); var checks = new List<string>(); ProposedRevealVerification.Endpoint a = null, b = null;
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); checks.Add(name); }
        try
        {
            a = ProposedRevealVerification.CreateEndpoint(1, 2); b = ProposedRevealVerification.CreateEndpoint(2, 2);
            var ui = a.components.OfType<IndependentAnswerSubmissionUI>().Single();
            var button = (UnityEngine.UI.Button)Get(ui, "button"); var status = (TMP_Text)Get(ui, "status"); var label = (TMP_Text)Get(ui, "label");
            TickUI(a, ui);
            Check(!button.interactable && status.text == "すべてのカードを配置してください", "Unanswered state disables Japanese Submit button");
            Check(button.onClick.GetPersistentEventCount() == 1 && button.onClick.GetPersistentTarget(0) == ui,
                "Submit callback is separate from Final Agreement Ready");
            Check(ui.GetComponents<UnityEngine.EventSystems.BaseRaycaster>().Length > 0 && status.font.HasCharacters(status.text),
                "World Space raycaster and embedded Japanese glyphs exist");
            foreach (string id in a.answers.AnswerCardIds) a.privateBoard.TryPlaceCard(id, "A");
            TickUI(a, ui); Check(button.interactable && label.text == "この回答で確定", "All valid local answers enable Submit");
            string time = SessionVariableTransport.GetClientVariable(a.net, a.session, "selectedAt_Card_01");
            a.Peer(1, "selectedAt_Card_01", SessionVariableTransport.Encode(a.session, "not-a-timestamp"));
            TickUI(a, ui); Check(!button.interactable && !a.submission.TrySubmit(), "Invalid selectedAt blocks Submit");
            a.Peer(1, "selectedAt_Card_01", SessionVariableTransport.Encode(a.session, time));
            TickUI(a, ui); button.onClick.SetPersistentListenerState(0, UnityEngine.Events.UnityEventCallState.EditorAndRuntime); button.onClick.Invoke(); TickUI(a, ui);
            Check(a.submission.IsLocalSubmitted && !a.privateBoard.CanInteract && label.text == "確定を取り消す" && status.text.Contains("回答確定済み"),
                "Actual button callback submits and updates local readonly/waiting UI");
            Check(!a.answers.TrySetCandidate("Card_01", "D", out _) && a.components.OfType<PrivateAnswerCard>().All(c => !c.Process(null, null)),
                "Submit blocks candidate data edits and XR select filters");
            Check(SessionVariableTransport.GetClientVariable(a.net, a.session, FinalAgreementManager.ReadyVariable) != "true" && !a.agreement.IsLocalReady,
                "Independent Submit never makes Final Agreement Ready");
            Capture(a, ui);
            Set(ui, "nextClick", 0f); Call(ui, "Update"); Check(button.interactable, "Submitted Participant can cancel while peer has not submitted");
            button.onClick.Invoke(); TickUI(a, ui);
            Check(!a.submission.IsLocalSubmitted && a.privateBoard.CanInteract && button.interactable, "Actual cancel button restores editing after echo");
            Check(a.privateBoard.TryPlaceCard("Card_01", "C") && a.submission.TrySubmit(), "Cancelled Participant can change answer and submit again");
            Call(a.submission, "ClearLocal"); TickUI(a, ui);
            Check(a.submission.IsLocalSubmitted && !a.privateBoard.CanInteract, "Submitted UI/interaction state restores from server variables on reconnect");
            ProposedRevealVerification.Deliver(a, b);
            foreach (string id in b.answers.AnswerCardIds) b.privateBoard.TryPlaceCard(id, "B");
            Check(b.submission.TrySubmit(), "Peer explicitly submits completed answers");
            for (int i = 0; i < 8; i++)
            {
                foreach (var e in new[] { a, b }) { Set(e.reveal, "nextAttempt", 0f); Call(e.submission, "Update"); Call(e.reveal, "LateUpdate"); }
                ProposedRevealVerification.Deliver(a, b); ProposedRevealVerification.Deliver(b, a);
            }
            TickUI(a, ui);
            Check(a.reveal.HasRevealed && b.reveal.HasRevealed && !a.submission.TryCancel() && !((Canvas)Get(ui, "canvas")).enabled,
                "All submitted commits common Reveal, hides Submit panel and rejects cancel");
            r.passed = true;
        }
        catch (Exception e) { r.failure = e.ToString(); Debug.LogException(e); }
        finally
        {
            if (a != null) EditorSceneManager.ClosePreviewScene(a.scene); if (b != null) EditorSceneManager.ClosePreviewScene(b.scene);
            r.checks = checks.ToArray(); Directory.CreateDirectory("Library/IndependentAnswerSubmissionVerification");
            File.WriteAllText("Library/IndependentAnswerSubmissionVerification/result.json", JsonUtility.ToJson(r, true));
            Debug.Log($"[Independent Submit Verification] {(r.passed ? "PASS" : "FAIL")} / {checks.Count}");
        }
    }
    static void TickUI(ProposedRevealVerification.Endpoint e, IndependentAnswerSubmissionUI ui)
    { Call(e.components.OfType<FinalAgreementUI>().Single(), "Update"); Call(e.submission, "Update"); Call(e.privateBoard, "Update"); Set(ui, "nextClick", 0f); Call(ui, "Update"); }
    static void Capture(ProposedRevealVerification.Endpoint e, IndependentAnswerSubmissionUI ui)
    {
        var main = e.privateBoard.GetComponentsInChildren<Canvas>(true).Single(c => c.name == "TierBoardCanvas");
        var go = new GameObject("PrivateSubmitPreviewCamera"); SceneManager.MoveGameObjectToScene(go, e.scene);
        var cam = go.AddComponent<Camera>(); cam.enabled = false; cam.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(e.scene);
        cam.transform.SetPositionAndRotation(main.transform.position + main.transform.right * 0.25f - main.transform.forward * 4, main.transform.rotation);
        cam.orthographic = true; cam.orthographicSize = 1.35f; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.018f, 0.030f, 0.044f);
        Canvas.ForceUpdateCanvases(); var render = new RenderTexture(1800, 1200, 24); var tex = new Texture2D(1800, 1200, TextureFormat.RGB24, false); var old = RenderTexture.active;
        try
        {
            cam.targetTexture = render; cam.Render(); RenderTexture.active = render; tex.ReadPixels(new Rect(0, 0, 1800, 1200), 0, 0); tex.Apply();
            Directory.CreateDirectory("Library/IndependentAnswerSubmissionVerification"); File.WriteAllBytes("Library/IndependentAnswerSubmissionVerification/Submitted.png", tex.EncodeToPNG());
        }
        finally { RenderTexture.active = old; cam.targetTexture = null; UnityEngine.Object.DestroyImmediate(tex); UnityEngine.Object.DestroyImmediate(render); UnityEngine.Object.DestroyImmediate(go); }
    }
    static object Get(object obj, string name) => obj.GetType().GetField(name, Flags).GetValue(obj);
    static void Set(object obj, string name, object value) => obj.GetType().GetField(name, Flags).SetValue(obj, value);
    static void Call(object obj, string name) => obj.GetType().GetMethod(name, Flags).Invoke(obj, null);
}
#endif
