using System;
using System.Linq;
using TMPro;
using UnityEngine;

// One scene-local owner; command safety remains in the session/lobby/logger APIs.
[DisallowMultipleComponent]
public sealed class SessionResetUI : MonoBehaviour
{
    [SerializeField] private TMP_FontAsset japaneseFont;
    private ExperimentSessionManager session;
    private ExperimentParticipantRegistry registry;
    private ExperimentLobbyController lobby;
    private ExperimentLogger logger;
    private FinalAgreementManager agreement;
    private bool IsCompleted => agreement != null && agreement.IsBoardConfirmed;
    private bool SaveFailed => IsCompleted && (logger == null || !logger.IsOperational || !string.IsNullOrEmpty(logger.LastError));
    private bool CompletedAndSaved => IsCompleted && logger != null && logger.IsOperational && !logger.IsSaving && logger.UnsavedCount == 0 && string.IsNullOrEmpty(logger.LastError) && logger.AreRequiredAnswersSaved;
    public string PreparationActionLabel => IsCompleted ? (SaveFailed ? "保存を再試行する" : CompletedAndSaved ? "次のセッションを準備する" : "実験データを保存しています") : lobby != null && lobby.IsExperimentStarted ? "実験を中断する" : "新規セッションを準備する";
    private GameObject canvasRoot, controls, dialog;
    private UnityEngine.UI.Button resetButton, confirmButton, startButton;
    private TMP_Text status, prompt, current, data, draft, reason, details;
    private string displayedSession, lastRequestedSession, confirmationSession, confirmationConnections, confirmationDraft, message = "";
    private long confirmationEpoch;
    private bool collapsed, showDetails, showDataManagement;
    private ExperimentDataManagementUI dataManagementUI;
    public bool IsConfirmationOpen { get; private set; }
    public string StatusMessage => message;
    private bool IsAdmin => !Application.isMobilePlatform && registry != null && registry.IsLocalExperimenter;
    public bool CanReset => IsAdmin && registry.Network != null && registry.Network.IsReady && session != null && !session.IsResettingSession && (!IsCompleted || CompletedAndSaved);
    private string Connections => registry.Network == null || !registry.Network.IsReady ? "" : string.Join(",", registry.Network.GetAliveClients(includeStealthClients: true).OrderBy(x => x).Select(x => x + ":" + registry.Network.GetClientVariable(ExperimentParticipantRegistry.RoleVariable, x)));
    private string DraftKey => session.DraftMode + ":" + session.DraftCardSetId + ":" + session.DraftExpectedParticipants + ":" + logger?.Data.DraftSignature;
    public void Configure(ExperimentSessionManager manager, ExperimentParticipantRegistry roles)
    { session = manager; registry = roles; lobby = manager.Lobby; logger = manager.GetComponent<ExperimentLogger>(); agreement = manager.gameObject.scene.GetRootGameObjects().SelectMany(o=>o.GetComponentsInChildren<FinalAgreementManager>(true)).SingleOrDefault(); }
    public void RequestConfirmation()
    {
        if (!CanReset || IsConfirmationOpen) return;
        confirmationSession = session.CurrentSessionId; confirmationEpoch = session.SessionEpoch;
        confirmationConnections = Connections; confirmationDraft = DraftKey; IsConfirmationOpen = true; Refresh();
    }
    public void CancelReset() { IsConfirmationOpen = false; message = "新規準備を取り消しました"; Refresh(); }
    private bool ConfirmationValid => CanReset && confirmationSession == session.CurrentSessionId && confirmationEpoch == session.SessionEpoch && confirmationConnections == Connections && confirmationDraft == DraftKey;
    public void ConfirmReset()
    {
        if (!IsConfirmationOpen) return;
        bool valid = ConfirmationValid; IsConfirmationOpen = false;
        if (!valid) { message = "接続・セッション・設定が変わったため取り消しました"; Refresh(); return; }
        message = "前回の実験データを確認・保存中";
        if (!session.ResetSession()) message = "準備要求に失敗しました。保存状態と詳細を確認してください";
        else { lastRequestedSession = confirmationSession; message = "準備要求を受け付けました"; }
        Refresh();
    }
    private void Update() => Refresh();
    public void Refresh()
    {
        if (!IsAdmin) IsConfirmationOpen = false;
        if (IsConfirmationOpen && !ConfirmationValid) { IsConfirmationOpen = false; message = "接続・セッション・設定が変わったため取り消しました"; }
        if (IsAdmin && canvasRoot == null) Build();
        if (canvasRoot == null) return;
        canvasRoot.SetActive(IsAdmin); if (!IsAdmin) return;
        if (lastRequestedSession != null && !session.IsResettingSession && session.CurrentSessionId != lastRequestedSession) { message = "新しいセッションの同期が完了しました"; lastRequestedSession = null; }
        if (displayedSession != session.CurrentSessionId) { displayedSession = session.CurrentSessionId; message = session.IsResettingSession ? "新しいセッションの通知を受信しました" : ""; }
        controls.SetActive(!collapsed);
        resetButton.interactable = (CanReset || (IsAdmin && SaveFailed && logger != null && logger.IsOperational && !session.IsResettingSession)) && !IsConfirmationOpen; confirmButton.interactable = CanReset;
        resetButton.GetComponentInChildren<TMP_Text>().text = PreparationActionLabel;
        foreach(string selection in new[]{"Normal","Proposed","SetA","SetB"})
        {var child=controls.transform.Find(selection);if(child!=null)child.GetComponent<UnityEngine.UI.Button>().interactable=logger!=null && !logger.Data.IsControlled && !session.IsResettingSession;}
        startButton.interactable = lobby != null && lobby.CanStart && !IsConfirmationOpen;
        current.text = "現在のセッション（適用済み・読取専用）\n" +
            (session.HasAppliedSettings ? Mode(session.SessionMode) + " ／ カードセット " + session.CurrentCardSetId + " ／ 必要 " + session.AppliedExpectedParticipants + "人" : "適用済み設定なし：新規セッションの準備が必要です") +
            "\nセッションID：" + Short(session.CurrentSessionId) + "　Epoch：" + session.SessionEpoch +
            "\n接続：" + (registry.Network != null && registry.Network.IsReady ? "接続済み" : "未接続") + "　参加者：" + (lobby != null ? lobby.ConnectedParticipants : 0) + "人" +
            "\nフェーズ：" + CurrentPhase + "　" + (IsCompleted ? "分類作業終了" : lobby != null && lobby.IsExperimentStarted ? "実験進行中" : "開始前") +
            "\n初期化：ローカル " + (session.IsLocalInitialized ? "完了" : "待機中") + " ／ 参加者 " + session.InitializedParticipants + "人　全端末ACK：" + session.AcknowledgedCount + " / " + session.SyncTargetCount +
            "\n" + string.Join("　", registry.ParticipantClientNos.Select(x => "参加者 " + x + "：接続済み・" + (session.HasClientAcknowledged(x) ? "初期化済み" : "初期化待ち")));
        status.text = (IsCompleted ? "分類作業は終了しました：" + PreparationActionLabel : session.PreparationStatus) + "\n" + message;
        draft.text = "次回の実験設定 ／ " + (logger!=null?logger.Data.Draft.experimentType:"Test") + "（現在条件は変更されません）\n" + Mode(session.DraftMode) + " ／ カードセット " + session.DraftCardSetId + " ／ " + session.DraftExpectedParticipants + "人\n" + (session.HasDraftChanges || logger!=null && logger.Data.HasDraftChanges ? "未適用の変更あり：新規準備が必要です" : "現在の設定と一致しています");
        reason.text = IsCompleted ? (CompletedAndSaved ? "保存済みデータを保持して次回を準備できます" : "保存状態を確認してください。次回準備は保存完了後に行えます") : lobby == null ? "開始判定を取得できません" : lobby.CanStart ? "実験を開始できます" : lobby.StartBlockReason;
        data.text = logger == null ? "実験データ：Loggerがありません" : "実験データ ／ CSV：" + logger.SaveStatus + "　未保存：" + logger.UnsavedCount + "件\n最終回答：" + logger.FinalSaveStatus + "\n独立回答：" + logger.IndependentSaveStatus + "\n計時：" + (logger.HasMonotonicTiming ? "単調時計で計測可能" : "継続性未確認：新規準備が必要です");
        dataManagementUI?.SetVisible(showDataManagement && !showDetails && !collapsed);
        details.text = showDetails ? "詳細（技術情報）\n準備元が同じ接続IDで復帰できない場合は、ログ保存を確認して全端末とRoomを再起動してください。\n" + session.LastFailure + "\n" + (logger != null ? logger.LastError + "\n保存先：" + logger.OutputDirectory : "") : "";
        dialog.SetActive(IsConfirmationOpen);
        if (IsConfirmationOpen) prompt.text = "新規セッションを準備しますか？\n現在のセッションID：" + (string.IsNullOrEmpty(confirmationSession) ? "未作成" : confirmationSession) + "\n現在：" + CurrentPhase + "\n保存済みデータは保持されます。" + (IsCompleted ? "分類作業は正常に終了しています。" : lobby != null && lobby.IsExperimentStarted ? "進行中の実験を中断します。" : "") + "\n次回：" + Mode(session.DraftMode) + " ／ カードセット " + session.DraftCardSetId + " ／ " + session.DraftExpectedParticipants + "人\n実験情報：" + logger?.Data.Draft.experimentType + " ／ ペア " + logger?.Data.Draft.pairId + " ／ " + logger?.Data.Draft.allocationGroup + " ／ " + logger?.Data.Draft.trialOrder + "回目";
    }
    private string CurrentPhase => lobby != null && lobby.IsExperimentStarted && lobby.CurrentState == "Idle"
        ? (session.SessionMode == ExperimentManager.ExperimentMode.Normal ? "共同分類中" : "独立回答中") : Phase(lobby != null ? lobby.CurrentState : "");
    public static string Mode(ExperimentManager.ExperimentMode mode) => mode == ExperimentManager.ExperimentMode.Normal ? "通常方式" : "提案方式";
    public static string Phase(string value) { switch (value) { case "Idle": return "待機中"; case "Answering": return "回答中"; case "WaitingForAnswers": return "回答待ち"; case "Revealed": return "回答公開済み"; case "Discussion": return "共同分類中"; case "Confirmed": return "確定済み"; default: return "未確認"; } }
    private static string Short(string value) => string.IsNullOrEmpty(value) ? "未作成" : value.Substring(0, Math.Min(12, value.Length)) + "…";
    private void Select(ExperimentManager.ExperimentMode? mode = null, string set = null, int? count = null)
    { if(!session.SetDraft(mode ?? session.DraftMode,set ?? session.DraftCardSetId,count ?? session.DraftExpectedParticipants))message="Pilot／Mainの方式・セットは割当と試行順序から決まります"; }
    private void RetrySave() { if (IsAdmin && logger != null) { message = logger.RetrySave() ? "保存の再試行が完了しました" : "保存できませんでした。詳細を確認してください"; Refresh(); } }
    private void OpenFolder() { if (!IsAdmin || logger == null) return; try { if (System.IO.Directory.Exists(logger.OutputDirectory)) Application.OpenURL(new Uri(logger.OutputDirectory + System.IO.Path.DirectorySeparatorChar).AbsoluteUri); else message = "保存先は最初の保存成功時に作成されます"; } catch { message = "保存先を開けません。パスをコピーして確認してください"; } }
    private void OnDisable() { IsConfirmationOpen = false; if (canvasRoot != null) canvasRoot.SetActive(false); }
    private void OnDestroy() { if (canvasRoot != null) { if (Application.isPlaying) Destroy(canvasRoot); else DestroyImmediate(canvasRoot); } }
    private RectTransform Rect(string name, Transform parent, Vector2 anchor, Vector2 position, Vector2 size)
    { var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); rect.SetParent(parent, false); rect.anchorMin = rect.anchorMax = rect.pivot = anchor; rect.anchoredPosition = position; rect.sizeDelta = size; return rect; }
    private void Panel(string name, Transform parent, Vector2 position, Vector2 size, Color color)
    { var graphic = Rect(name, parent, new Vector2(0,1), position, size).gameObject.AddComponent<RoundedPanelGraphic>(); graphic.color = color; graphic.raycastTarget = false; }
    private TMP_Text Text(string name, Transform parent, Vector2 position, Vector2 size, string text, float fontSize)
    {
        var label = Rect(name, parent, new Vector2(0,1), position, size).gameObject.AddComponent<TextMeshProUGUI>();
        label.font = japaneseFont; label.fontSize = fontSize; label.text = text; label.color = Color.white; label.raycastTarget = false;
        label.alignment = TextAlignmentOptions.TopLeft; label.textWrappingMode = TextWrappingModes.Normal; return label;
    }
    private UnityEngine.UI.Button Button(string name, Transform parent, Vector2 position, Vector2 size, string title, UnityEngine.Events.UnityAction action, bool primary = false)
    {
        var rect = Rect(name, parent, new Vector2(0,1), position, size);
        var image = rect.gameObject.AddComponent<RoundedPanelGraphic>(); image.color = primary ? new Color(.04f,.42f,.40f) : new Color(.12f,.22f,.30f);
        var button = rect.gameObject.AddComponent<UnityEngine.UI.Button>(); button.targetGraphic = image; button.onClick.AddListener(action);
        var label = Text("Label", rect, new Vector2(8,-5), size - new Vector2(16,10), title, 20); label.alignment = TextAlignmentOptions.Center;
        return button;
    }
    private void Build()
    {
        if (canvasRoot != null) return;
        if (japaneseFont == null) japaneseFont = Resources.Load<TMP_FontAsset>("ExperimenterJapanese SDF");
        canvasRoot = new GameObject("ExperimenterControlPanel", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(canvasRoot, gameObject.scene);
        var canvas = canvasRoot.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 100;
        var scaler = canvasRoot.GetComponent<UnityEngine.UI.CanvasScaler>(); scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920,1080); scaler.matchWidthOrHeight = 1;
        controls = Rect("Management", canvasRoot.transform, new Vector2(0,1), new Vector2(16,-68), new Vector2(850,990)).gameObject;
        var bg = controls.AddComponent<RoundedPanelGraphic>(); bg.color = new Color(.035f,.055f,.085f,.97f);
        if(logger!=null) { dataManagementUI=new ExperimentDataManagementUI(logger,registry,canvasRoot.transform,japaneseFont); Button("EditMetadata",canvasRoot.transform,new Vector2(340,-16),new Vector2(330,44),"実験情報・参加者ID",()=>{showDataManagement=!showDataManagement;showDetails=false;}); }
        Button("Collapse", canvasRoot.transform, new Vector2(16,-16), new Vector2(310,44), "実験者管理　表示／折り畳み", () => collapsed = !collapsed);
        Panel("CurrentSection", controls.transform, new Vector2(10,-8), new Vector2(830,280), new Color(.04f,.10f,.13f));
        Panel("DraftSection", controls.transform, new Vector2(10,-380), new Vector2(830,210), new Color(.065f,.085f,.14f));
        current = Text("Current", controls.transform, new Vector2(20,-16), new Vector2(810,225), "", 20);
        Button("CopySession", controls.transform, new Vector2(20,-245), new Vector2(240,42), "セッションIDをコピー", () => GUIUtility.systemCopyBuffer = session.CurrentSessionId);
        status = Text("Preparation", controls.transform, new Vector2(20,-300), new Vector2(810,76), "", 22);
        draft = Text("Draft", controls.transform, new Vector2(20,-385), new Vector2(810,100), "", 22);
        Button("Normal", controls.transform, new Vector2(20,-490), new Vector2(180,44), "通常方式", () => Select(ExperimentManager.ExperimentMode.Normal));
        Button("Proposed", controls.transform, new Vector2(210,-490), new Vector2(180,44), "提案方式", () => Select(ExperimentManager.ExperimentMode.Proposed));
        Button("SetA", controls.transform, new Vector2(400,-490), new Vector2(200,44), "カードセット A", () => Select(set: "A"));
        Button("SetB", controls.transform, new Vector2(610,-490), new Vector2(200,44), "カードセット B", () => Select(set: "B"));
        Button("One", controls.transform, new Vector2(20,-542), new Vector2(260,44), "1人（動作確認）", () => Select(count: 1));
        Button("Two", controls.transform, new Vector2(290,-542), new Vector2(260,44), "2人（評価実験）", () => Select(count: 2));
        resetButton = Button("Prepare", controls.transform, new Vector2(20,-602), new Vector2(480,50), "新規セッションを準備する", () => { if (SaveFailed) RetrySave(); else RequestConfirmation(); });
        startButton = Button("Start", controls.transform, new Vector2(520,-602), new Vector2(290,50), "実験開始", () => { if (lobby != null) lobby.StartExperiment(); }, true);
        reason = Text("StartReason", controls.transform, new Vector2(20,-662), new Vector2(810,60), "", 22);
        data = Text("Data", controls.transform, new Vector2(20,-725), new Vector2(810,132), "", 21);
        Button("OpenLogs", controls.transform, new Vector2(20,-865), new Vector2(250,44), "ログ保存先を開く", OpenFolder);
        Button("CopyPath", controls.transform, new Vector2(280,-865), new Vector2(250,44), "保存先パスをコピー", () => { if (logger != null) GUIUtility.systemCopyBuffer = logger.OutputDirectory; });
        Button("Retry", controls.transform, new Vector2(540,-865), new Vector2(270,44), "保存を再試行", RetrySave);
        Button("Details", controls.transform, new Vector2(20,-925), new Vector2(250,44), "詳細情報　表示／非表示", () => showDetails = !showDetails);
        details = Text("DetailsText", canvasRoot.transform, new Vector2(890,-68), new Vector2(900,950), "", 22);
        dialog = Rect("Confirmation", canvasRoot.transform, new Vector2(.5f,.5f), Vector2.zero, new Vector2(1920,1080)).gameObject;
        var blocker = dialog.AddComponent<UnityEngine.UI.Image>(); blocker.color = new Color(0,0,0,.94f);
        prompt = Text("Prompt", dialog.transform, new Vector2(460,-300), new Vector2(1000,330), "", 28);
        confirmButton = Button("Confirm", dialog.transform, new Vector2(460,-660), new Vector2(450,64), "確認して新規セッションを準備", ConfirmReset, true);
        Button("Cancel", dialog.transform, new Vector2(960,-660), new Vector2(450,64), "取消", CancelReset); dialog.SetActive(false);
    }
}
