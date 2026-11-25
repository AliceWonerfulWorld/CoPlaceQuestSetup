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
    private bool collapsed, showDetails;
    private GameObject detailRoot;
    private TMP_Text warning;
    private UnityEngine.UI.Button abortButton;
    private ExperimentDataManagementUI dataManagementUI;
    public bool IsConfirmationOpen { get; private set; }
    public string StatusMessage => message;
    private bool IsAdmin => !Application.isMobilePlatform && registry != null && registry.IsLocalExperimenter;
    public bool CanReset => IsAdmin && registry.Network != null && registry.Network.IsReady && session != null && !session.IsResettingSession && (!IsCompleted || CompletedAndSaved) && logger != null && logger.IsOperational && logger.LastError == "" && logger.UnsavedCount == 0;
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
    // A computed presentation, never a second experiment state machine.
    public string NextAction
    {
        get
        {
            if(session.IsResettingSession)return "sync";
            if(logger==null || !logger.IsOperational)return "unavailable";
            if(logger.LastError!="")return "retry";
            if(IsCompleted && !CompletedAndSaved)return "saving";
            if(!IsCompleted && lobby!=null && lobby.HasStartRequestedOrStarted)return lobby.IsExperimentStarted?"running":"starting";
            if(!session.HasAppliedSettings || !logger.IsPreparedInCurrentRun || session.HasDraftChanges || logger.Data.HasDraftChanges)return "prepare";
            if(IsCompleted)
            {
                var m=logger.Data.Applied;
                return m!=null && m.plan.experimentType!="Test"?(m.plan.trialOrder==1?"second":logger.Data.HasSavedFirstTrial()?"pair":"review"):"prepare";
            }
            if(lobby!=null && lobby.CanStart)return "start";
            if(logger.Data.Applied?.plan.experimentType!="Test" && logger.Data.StartBlockReason!="")return "binding";
            return "waiting";
        }
    }
    public void PerformNextAction()
    {
        if(!IsAdmin || IsConfirmationOpen)return;
        switch(NextAction)
        {
            case "retry": RetrySave();break;
            case "prepare": RequestConfirmation();break;
            case "second": if(logger.Data.PrepareSecondDraft())RequestConfirmation();else message="1回目の正常終了・保存完了を確認してください";break;
            case "pair": message=logger.Data.SuggestNewPair(logger.Data.Draft.experimentType)?"新しいペアのID候補です。IDと割り当てを確認して準備してください":logger.Data.LastError;break;
            case "start": lobby.StartExperiment();break;
            case "binding": var field=controls.transform.Find("ExperimentDataManagement/Device1");if(field!=null)field.GetComponent<TMP_Dropdown>().Show();message="本人と端末を照合して、下の端末選択欄から確認登録してください";break;
        }
        Refresh();
    }
    public void Refresh()
    {
        try { RefreshCore(); }
        catch(Exception e)
        {
            if(warning!=null)warning.text="実験情報を読み取れません。進行を止めて保存先・メタデータを確認してください。\n"+e.Message;
            if(resetButton!=null)resetButton.interactable=false;
            if(startButton!=null)startButton.interactable=false;
            if(abortButton!=null)abortButton.interactable=false;
            IsConfirmationOpen=false;if(dialog!=null)dialog.SetActive(false);
        }
    }
    private void RefreshCore()
    {
        if (!IsAdmin) IsConfirmationOpen = false;
        if (IsConfirmationOpen && !ConfirmationValid) { IsConfirmationOpen = false; message = "接続・セッション・設定が変わったため取り消しました"; }
        if (IsAdmin && canvasRoot == null) Build();
        if (canvasRoot == null) return;
        canvasRoot.SetActive(IsAdmin); if (!IsAdmin) return;
        if (lastRequestedSession != null && !session.IsResettingSession && session.CurrentSessionId != lastRequestedSession) { message = "新しいセッションの同期が完了しました"; lastRequestedSession = null; }
        if (displayedSession != session.CurrentSessionId) { displayedSession = session.CurrentSessionId; message = session.IsResettingSession ? "新しいセッションの通知を受信しました" : ""; }
        controls.SetActive(!collapsed && !showDetails); detailRoot.SetActive(showDetails && !collapsed);
        var d=logger!=null?logger.Data.Draft:new ExperimentDataPlan();var m=logger!=null?logger.Data.Applied:null;
        string action=NextAction;
        string title="",guide="";
        switch(action)
        {
            case "sync":title="セッションを準備しています";guide=session.PreparationStatus;break;
            case "unavailable":title="記録機能を確認してください";guide="ログ記録機能が無効です。詳細設定を確認してください";break;
            case "retry":title="保存を再試行する";guide="保存に失敗しました。次の試行へ進めません。保存先を確認して再試行してください";break;
            case "saving":title="データを保存しています";guide="結果が確定しました。保存完了までお待ちください";break;
            case "running":title="分類作業中";guide="分類作業中です。参加者全員の最終同意をお待ちください";break;
            case "starting":title="実験開始を同期しています";guide="参加者のQuestへ開始を通知しています";break;
            case "second":title="2回目の実験を準備する";guide="分類作業が終了しました。アンケートを案内してください。\nアンケート終了後、同じペアの2回目を準備してください";break;
            case "review":title="1回目の記録を確認してください";guide="2回目は終了・保存済みです。1回目の正常終了・保存記録がありません。詳細設定から不足する試行を準備してください";break;
            case "pair":title="次のペアを準備する";guide="このペアの2試行が終了しました。アンケートを案内してください。\nアンケート終了を確認してから、次のペアを準備してください";break;
            case "start":title="実験を開始する";guide="実験を開始できます。参加者への説明が済んだら開始してください";break;
            case "binding":title="参加者の端末を確認する";guide="参加者と使用端末の対応を確認してください。下の端末欄で選択・確認登録します";break;
            case "waiting":title="参加者の準備を待っています";guide=lobby!=null?lobby.StartBlockReason:"接続を確認してください";break;
            default:title="セッションを準備する";guide=d.experimentType=="Test"?"実験方式・カードセット・参加人数を選択して準備してください":"次回のID候補と割り当てを研究計画と照合し、確認して準備してください";break;
        }
        resetButton.GetComponentInChildren<TMP_Text>().text=title;
        string validation=d.Validate(session.DraftExpectedParticipants,session.DraftMode,session.DraftCardSetId);
        resetButton.interactable=!IsConfirmationOpen && (action=="start" || action=="binding" || action=="retry" && logger.IsOperational || action=="pair" && CompletedAndSaved || (action=="prepare"||action=="second") && CanReset && (action=="second"||validation==""));
        confirmButton.interactable=ConfirmationValid;
        // Kept as a hidden API-compatible control; there is only one primary action.
        startButton.interactable=lobby!=null && lobby.CanStart && !IsConfirmationOpen;
        startButton.gameObject.SetActive(false);
        abortButton.gameObject.SetActive(!IsCompleted && lobby!=null && lobby.IsExperimentStarted);
        abortButton.interactable=CanReset && !IsConfirmationOpen;
        current.text="現在の実験 ／ "+(m==null?"未準備":TypeName(m.plan.experimentType)+(m.plan.experimentType=="Test"?"":"　ペア "+m.plan.pairId+"　"+m.plan.trialOrder+"回目 / 2"))+"\n"+
            (session.HasAppliedSettings?Mode(session.SessionMode)+" ／ カードセット "+session.CurrentCardSetId+" ／ "+session.AppliedExpectedParticipants+"人":"次回設定を確認してセッションを準備してください")+"\n進行："+CurrentPhase+"　接続："+(lobby!=null && lobby.IsConnected?"接続済み":"未接続")+"　参加者 "+(lobby!=null?lobby.ConnectedParticipants:0)+" / "+(session.HasAppliedSettings?session.AppliedExpectedParticipants:session.DraftExpectedParticipants)+"人";
        status.text=guide;
        reason.text=action=="prepare" && validation!=""?validation:message;
        draft.text="次回設定（"+(session.HasDraftChanges || logger!=null && logger.Data.HasDraftChanges?"未適用の候補":"現在と一致")+"） ／ "+TypeName(d.experimentType)+"　"+Mode(session.DraftMode)+"　セット "+session.DraftCardSetId+"　"+session.DraftExpectedParticipants+"人";
        data.text=logger==null?"ログ記録機能がありません":"ログ保存："+logger.SaveStatus+" ／ 未保存 "+logger.UnsavedCount+"件"+(IsCompleted?"　最終回答："+logger.FinalSaveStatus:"");
        string problem=logger==null?"ログ記録機能がありません":!logger.IsOperational?"ログ記録機能が無効です":logger.LastError!=""?"保存に失敗しています。保存先を確認して再試行してください（詳細設定にエラー内容）":logger.IndexWarning!=""?"セッション一覧の更新失敗：ログ管理から再構築してください":"";
        if(lobby!=null && !lobby.IsConnected)problem="接続が切れています。参加者のQuestと実験者PCの接続を確認してください。\n"+problem;
        else if(lobby!=null && lobby.IsExperimentStarted && lobby.ConnectedParticipants!=session.AppliedExpectedParticipants)problem="参加者の接続台数が変わりました。接続と本人・端末の対応を確認してください。\n"+problem;
        else if(session.LastFailure!="")problem="準備・同期に問題があります。詳細設定を確認してください。\n"+problem;
        else if(lobby!=null && lobby.IsExperimentStarted && logger!=null && m!=null && m.plan.experimentType!="Test" && logger.Data.StartBlockReason.Contains("対応"))problem="参加者と端末の対応が不明です。実験を中断し、再確認してください。\n"+problem;
        warning.text=problem;
        foreach(string type in new[]{"Test","Pilot","Main"})
        {var b=controls.transform.Find(type).GetComponent<UnityEngine.UI.Button>();b.interactable=!session.IsResettingSession && !IsConfirmationOpen;b.GetComponent<RoundedPanelGraphic>().color=d.experimentType==type?ExperimenterUIColors.Accent:ExperimenterUIColors.Control;}
        foreach(string selection in new[]{"Normal","Proposed","SetA","SetB","One","Two"})
        {var child=controls.transform.Find(selection);child.gameObject.SetActive(d.experimentType=="Test");child.GetComponent<UnityEngine.UI.Button>().interactable=!session.IsResettingSession && !IsConfirmationOpen;
            bool selected=selection=="Normal"?session.DraftMode==ExperimentManager.ExperimentMode.Normal:selection=="Proposed"?session.DraftMode==ExperimentManager.ExperimentMode.Proposed:selection=="SetA"?session.DraftCardSetId=="A":selection=="SetB"?session.DraftCardSetId=="B":selection=="One"?session.DraftExpectedParticipants==1:session.DraftExpectedParticipants==2;
            child.GetComponent<RoundedPanelGraphic>().color=selected?ExperimenterUIColors.Accent:ExperimenterUIColors.Control;
        }
        dataManagementUI?.SetVisible(d.experimentType!="Test" && !collapsed && !showDetails);
        if(showDetails) dataManagementUI?.Refresh();
        details.text="適用済み Session ID："+session.CurrentSessionId+"　Epoch："+session.SessionEpoch+"\n初期化：ローカル "+session.IsLocalInitialized+"　参加者 "+session.InitializedParticipants+"人　ACK "+session.AcknowledgedCount+" / "+session.SyncTargetCount+"\n"+
            string.Join("　",registry.ParticipantClientNos.Select(c=>"ClientNo "+c+"："+(session.HasClientAcknowledged(c)?"ACK済み":"ACK待ち")+" key "+(logger!=null?logger.Data.DeviceKey(c):"")))+"\n同期："+session.PreparationStatus+"\n保存先："+(logger!=null?CurrentDirectory:"")+"\n保存エラー："+(logger!=null?logger.LastError:"")+"\n"+session.LastFailure;
        dialog.SetActive(IsConfirmationOpen);
        if (IsConfirmationOpen) prompt.text="設定を確認してセッションを準備しますか？\n次回："+TypeName(d.experimentType)+" ／ "+Mode(session.DraftMode)+" ／ セット "+session.DraftCardSetId+" ／ "+session.DraftExpectedParticipants+"人\n"+
            (d.experimentType=="Test"?"ID・端末対応登録は不要です。":"ペア "+d.pairId+"　参加者 "+d.participant1Id+" / "+d.participant2Id+"\n"+d.allocationGroup+"　"+d.trialOrder+"回目（ID・割り当てを確認してください）")+"\n保存済みデータは保持されます。"+(IsCompleted?"分類作業は正常に終了しています。":lobby!=null && lobby.IsExperimentStarted?"進行中の実験を中断します。":"")+"\n準備後も実験開始は手動です。";
    }
    static string TypeName(string type)=>type=="Main"?"本実験（Main）":type=="Pilot"?"予備実験（Pilot）":"動作確認（Test）";
    private void SelectType(string type)
    {
        if(logger==null)return;
        if(type==logger.Data.Draft.experimentType)return;
        if(type=="Test"){var d=logger.Data.Draft;d.experimentType=type;logger.Data.SetDraft(d);}
        else message=logger.Data.SuggestNewPair(type)?"IDとG1は候補です。研究計画と照合して確認してください":logger.Data.LastError;
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
        var image = rect.gameObject.AddComponent<RoundedPanelGraphic>(); image.color = primary ? ExperimenterUIColors.Accent : ExperimenterUIColors.Control;
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
        var scaler = canvasRoot.GetComponent<UnityEngine.UI.CanvasScaler>(); scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920,1080); scaler.screenMatchMode=UnityEngine.UI.CanvasScaler.ScreenMatchMode.Expand;
        controls = Rect("Management",canvasRoot.transform,new Vector2(.5f,1),new Vector2(-880,-68),new Vector2(1760,922)).gameObject;
        controls.GetComponent<RectTransform>().pivot=new Vector2(0,1);
        var bg=controls.AddComponent<RoundedPanelGraphic>();bg.color=ExperimenterUIColors.Background;
        Button("Collapse",canvasRoot.transform,new Vector2(80,-16),new Vector2(310,44),"実験者管理　表示／折り畳み",()=>collapsed=!collapsed);
        Button("Details",canvasRoot.transform,new Vector2(1390,-16),new Vector2(450,44),"詳細設定・ログ管理",()=>showDetails=!showDetails);
        Button("Test",controls.transform,new Vector2(24,-16),new Vector2(530,48),TypeName("Test"),()=>SelectType("Test"));
        Button("Pilot",controls.transform,new Vector2(614,-16),new Vector2(530,48),TypeName("Pilot"),()=>SelectType("Pilot"));
        Button("Main",controls.transform,new Vector2(1204,-16),new Vector2(530,48),TypeName("Main"),()=>SelectType("Main"));
        Panel("CurrentSection",controls.transform,new Vector2(24,-80),new Vector2(1712,132),new Color(.04f,.10f,.13f));
        current=Text("Current",controls.transform,new Vector2(44,-90),new Vector2(1672,116),"",24);
        status=Text("Preparation",controls.transform,new Vector2(24,-228),new Vector2(1712,76),"",26);
        resetButton=Button("Prepare",controls.transform,new Vector2(24,-318),new Vector2(780,60),"セッションを準備する",PerformNextAction,true);
        startButton=Button("Start",controls.transform,Vector2.zero,Vector2.one,"",()=>lobby.StartExperiment());startButton.gameObject.SetActive(false);
        abortButton=Button("Abort",controls.transform,new Vector2(1304,-318),new Vector2(430,60),"途中中断…",RequestConfirmation);
        reason=Text("StartReason",controls.transform,new Vector2(24,-386),new Vector2(1712,38),"",20);
        draft=Text("Draft",controls.transform,new Vector2(24,-426),new Vector2(1712,32),"",22);
        Button("Normal",controls.transform,new Vector2(24,-492),new Vector2(330,52),"通常方式",()=>Select(ExperimentManager.ExperimentMode.Normal));
        Button("Proposed",controls.transform,new Vector2(376,-492),new Vector2(330,52),"提案方式",()=>Select(ExperimentManager.ExperimentMode.Proposed));
        Button("SetA",controls.transform,new Vector2(24,-566),new Vector2(330,52),"カードセット A",()=>Select(set:"A"));
        Button("SetB",controls.transform,new Vector2(376,-566),new Vector2(330,52),"カードセット B",()=>Select(set:"B"));
        Button("One",controls.transform,new Vector2(24,-640),new Vector2(330,52),"参加人数 1人",()=>Select(count:1));
        Button("Two",controls.transform,new Vector2(376,-640),new Vector2(330,52),"参加人数 2人",()=>Select(count:2));
        Panel("PersistentStatusBackground",canvasRoot.transform,new Vector2(80,-986),new Vector2(1760,92),ExperimenterUIColors.Background);
        data=Text("Data",canvasRoot.transform,new Vector2(104,-992),new Vector2(1712,32),"",22);
        warning=Text("ImportantWarning",canvasRoot.transform,new Vector2(104,-1028),new Vector2(1712,50),"",18);warning.color=new Color(1,.72f,.45f);
        detailRoot=Rect("Advanced",canvasRoot.transform,new Vector2(.5f,1),new Vector2(-880,-68),new Vector2(1760,922)).gameObject;
        detailRoot.GetComponent<RectTransform>().pivot=new Vector2(0,1);var dbg=detailRoot.AddComponent<RoundedPanelGraphic>();dbg.color=ExperimenterUIColors.Background;
        Text("DetailsTitle",detailRoot.transform,new Vector2(24,-16),new Vector2(1250,44),"詳細設定・ログ管理",28);
        Button("CloseDetails",detailRoot.transform,new Vector2(1390,-16),new Vector2(346,44),"基本画面へ戻る",()=>showDetails=false);
        details=Text("DetailsText",detailRoot.transform,new Vector2(24,-84),new Vector2(1690,196),"",19);
        Button("CopySession",detailRoot.transform,new Vector2(24,-294),new Vector2(400,44),"Session IDをコピー",()=>GUIUtility.systemCopyBuffer=session.CurrentSessionId);
        Button("OpenLogs",detailRoot.transform,new Vector2(444,-294),new Vector2(400,44),"現在の保存先を開く",()=>OpenPath(CurrentDirectory));
        Button("CopyPath",detailRoot.transform,new Vector2(864,-294),new Vector2(400,44),"保存先パスをコピー",()=>GUIUtility.systemCopyBuffer=CurrentDirectory);
        Button("Retry",detailRoot.transform,new Vector2(1284,-294),new Vector2(450,44),"保存を再試行",RetrySave);
        Button("OpenAllLogs",detailRoot.transform,new Vector2(24,-354),new Vector2(530,44),"ExperimentLogs全体を開く",OpenFolder);
        Button("OpenIndex",detailRoot.transform,new Vector2(614,-354),new Vector2(530,44),"session_index.csvを開く",()=>OpenPath(System.IO.Path.Combine(logger.OutputDirectory,"session_index.csv")));
        Button("RebuildIndex",detailRoot.transform,new Vector2(1204,-354),new Vector2(530,44),"セッション一覧を再構築",()=>message=logger.RebuildIndex()?"セッション一覧を再構築しました":"再構築失敗：メタデータと保存先を確認してください");
        if(logger!=null)dataManagementUI=new ExperimentDataManagementUI(logger,registry,controls.transform,japaneseFont,detailRoot.transform);
        Button("ControlledOne",detailRoot.transform,new Vector2(24,-804),new Vector2(400,48),"次回の参加人数：1人",()=>SelectControlledCount(1));
        Button("ControlledTwo",detailRoot.transform,new Vector2(444,-804),new Vector2(400,48),"次回の参加人数：2人",()=>SelectControlledCount(2));
        Text("RetryHelp",detailRoot.transform,new Vector2(24,-866),new Vector2(1690,50),"準備元端末が戻らず同期が進まない場合は、保存状態を確認し全端末とRoomを再起動してください。",20);
        detailRoot.SetActive(false);
        dialog=Rect("Confirmation",canvasRoot.transform,new Vector2(.5f,.5f),Vector2.zero,new Vector2(1920,1080)).gameObject;
        var blocker=dialog.AddComponent<UnityEngine.UI.Image>();blocker.color=new Color(0,0,0,.96f);
        prompt=Text("Prompt",dialog.transform,new Vector2(360,-260),new Vector2(1200,360),"",28);
        confirmButton=Button("Confirm",dialog.transform,new Vector2(360,-680),new Vector2(560,64),"ID・条件を確認して準備する",ConfirmReset,true);
        Button("Cancel",dialog.transform,new Vector2(1000,-680),new Vector2(560,64),"取消",CancelReset);dialog.SetActive(false);
    }
    private string CurrentDirectory=>logger==null?"":string.IsNullOrEmpty(session.CurrentSessionId)?logger.OutputDirectory:logger.SessionDirectory(session.CurrentSessionId);
    private void OpenPath(string path)
    {if(!IsAdmin)return;try{if(System.IO.Directory.Exists(path)||System.IO.File.Exists(path))Application.OpenURL(new Uri(path).AbsoluteUri);else message="保存先は最初の保存成功時に作成されます";}catch{message="開けません。保存先パスをコピーして確認してください";}}
    private void SelectControlledCount(int count)
    {
        var d=logger.Data.Draft;
        if(session.SetDraft(session.DraftMode,session.DraftCardSetId,count))
        {if(count==1)d.participant2Id="";logger.Data.SetDraft(d);message=count==2?"参加者2のIDを基本画面で確認・入力してください":"次回を1人構成に変更しました";}
    }
}

internal static class ExperimenterUIColors
{
    public static readonly Color Background=new Color(.035f,.055f,.085f,1);
    public static readonly Color Accent=new Color(.04f,.42f,.40f);
    public static readonly Color Control=new Color(.12f,.22f,.30f);
    public static readonly Color Field=new Color(.10f,.16f,.21f);
}
