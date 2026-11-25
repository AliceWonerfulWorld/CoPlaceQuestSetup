using System;
using System.Linq;
using TMPro;
using UnityEngine;

// PC-only inputs; all changes remain Draft until the existing Reset confirmation.
public sealed class ExperimentDataManagementUI
{
    readonly ExperimentLogger logger;
    readonly ExperimentParticipantRegistry registry;
    readonly Transform panel, advanced;
    readonly TMP_FontAsset font;
    readonly TMP_InputField pair,p1,p2,manualPair,manual1,manual2,notes,verification;
    readonly TMP_Dropdown device1,device2,method;
    readonly TMP_Text summary,connections,error;
    readonly UnityEngine.UI.Button bind1,bind2;
    readonly UnityEngine.UI.Button[] groups=new UnityEngine.UI.Button[4];
    readonly GameObject secondRow;
    int[] clients=new int[0];
    string[] keys=new string[0];
    string displayedSession="",message="";
    public ExperimentDataManagementUI(ExperimentLogger log,ExperimentParticipantRegistry roles,Transform parent,TMP_FontAsset japanese,Transform detailParent=null)
    {
        logger=log;registry=roles;font=japanese;
        panel=Rect("ExperimentDataManagement",parent,new Vector2(24,-454),new Vector2(1712,470));
        advanced=detailParent??panel;
        pair=Input("Pair",panel,new Vector2(0,-38),"ペアID",440,p=>p.pairId,(p,v)=>p.pairId=v);
        p1=Input("Participant1",panel,new Vector2(464,-38),"参加者1",440,p=>p.participant1Id,(p,v)=>p.participant1Id=v);
        p2=Input("Participant2",panel,new Vector2(928,-38),"参加者2",440,p=>p.participant2Id,(p,v)=>p.participant2Id=v);
        summary=Text("AllocationHelp",panel,Vector2.zero,new Vector2(1690,32),"",22);
        string[] labels={"G1　通常 A → 提案 B","G2　提案 A → 通常 B","G3　通常 B → 提案 A","G4　提案 B → 通常 A"};
        for(int i=0;i<4;i++){string g="G"+(i+1);groups[i]=Button("G"+(i+1),panel,new Vector2(i*420,-96),new Vector2(404,52),labels[i],()=>Edit(p=>p.allocationGroup=g));}
        Text("BindingHelp",panel,new Vector2(0,-168),new Vector2(1690,58),"使用端末の確認（適用済みの参加者）　接続順では割り当てません。\n本人と端末を照合し、端末を選んで確認登録してください。2回目・再接続時も確認が必要です。",20);
        connections=Text("Connections",panel,new Vector2(0,-232),new Vector2(1690,36),"",21);
        device1=Dropdown("Device1",panel,new Vector2(0,-276),new Vector2(470,48),new[]{"端末を選択"});
        secondRow=Rect("SecondBinding",panel,new Vector2(870,-276),new Vector2(820,48)).gameObject;
        device2=Dropdown("Device2",secondRow.transform,Vector2.zero,new Vector2(470,48),new[]{"端末を選択"});
        bind1=Button("Bind1",panel,new Vector2(490,-276),new Vector2(330,48),"参加者1を確認・登録",()=>Bind(1));
        bind2=Button("Bind2",secondRow.transform,new Vector2(490,0),new Vector2(330,48),"参加者2を確認・登録",()=>Bind(2));
        method=Dropdown("VerificationMethod",panel,new Vector2(0,-340),new Vector2(820,48),new[]{"本人に端末を提示してもらい確認","対象者だけ接続・切断して本人に確認","その他（詳細設定に確認方法を入力）"});
        error=Text("MetadataStatus",panel,new Vector2(0,-404),new Vector2(1690,64),"",20);
        // Manual override/retry tools stay with technical details, not the main flow.
        manualPair=Input("ManualPair",advanced,new Vector2(24,-430),"ペアID",440,p=>p.pairId,(p,v)=>p.pairId=v);
        manual1=Input("ManualParticipant1",advanced,new Vector2(488,-430),"参加者1",440,p=>p.participant1Id,(p,v)=>p.participant1Id=v);
        manual2=Input("ManualParticipant2",advanced,new Vector2(952,-430),"参加者2",440,p=>p.participant2Id,(p,v)=>p.participant2Id=v);
        for(int i=0;i<4;i++){string g="G"+(i+1);Button("Manual"+g,advanced,new Vector2(24+i*420,-490),new Vector2(404,48),labels[i],()=>Edit(p=>p.allocationGroup=g));}
        notes=Input("Notes",advanced,new Vector2(24,-550),"備考",1640,p=>p.notes,(p,v)=>p.notes=v);
        verification=InputRaw("Verification",advanced,new Vector2(24,-610),"その他の確認方法",1640,200);
        Button("Order1",advanced,new Vector2(24,-670),new Vector2(300,48),"手動：1回目を再試行",()=>Retry(1));
        Button("Order2",advanced,new Vector2(340,-670),new Vector2(300,48),"手動：2回目を再試行",()=>Retry(2));
        Button("RestoreApplied",advanced,new Vector2(656,-670),new Vector2(440,48),"現在のペア設定を次回へ戻す",()=>{var m=logger.Data.Applied;if(m!=null)Edit(p=>Copy(m.plan,p));});
        Text("ManualHelp",advanced,new Vector2(24,-730),new Vector2(1640,60),"ID・割り当ては次回設定だけを変更します。再試行も確認ダイアログを経て別Session IDに保存します。\n備考に氏名を入力しないでください。アンケート終了は実験者が確認してください。",20);
    }
    static void Copy(ExperimentDataPlan from,ExperimentDataPlan to)
    {to.experimentType=from.experimentType;to.pairId=from.pairId;to.participant1Id=from.participant1Id;to.participant2Id=from.participant2Id;to.allocationGroup=from.allocationGroup;to.trialOrder=from.trialOrder;to.notes=from.notes;}
    void Retry(int order){var m=logger.Data.Applied;if(m==null)return;Edit(p=>{Copy(m.plan,p);p.trialOrder=order;});}
    void Edit(Action<ExperimentDataPlan> action){var d=logger.Data.Draft;action(d);message=logger.Data.SetDraft(d)?"次回設定を変更しました。適用前に確認してください":"同期中は変更できません";}
    public void SetVisible(bool visible){panel.gameObject.SetActive(visible);if(visible)Refresh();}
    void Bind(int slot)
    {
        var m=logger.Data.Applied;var dropdown=slot==1?device1:device2;int index=dropdown.value-1;
        if(m==null || index<0 || index>=clients.Length){message="先にセッションを準備し、端末を選択してください";return;}
        if(keys[index]!=logger.Data.DeviceKey(clients[index])){message="端末識別が変わりました。再選択して確認してください";return;}
        string proof=method.value==2?verification.text:method.options[method.value].text;
        message=logger.Data.ConfirmBinding(slot==1?m.plan.participant1Id:m.plan.participant2Id,clients[index],proof)?"本人・端末の対応を記録しました":logger.Data.LastError;
    }
    public void Refresh()
    {
        var d=logger.Data.Draft;var m=logger.Data.Applied;
        bool settingsEnabled=!logger.GetComponent<ExperimentSessionManager>().IsResettingSession;
        foreach(var field in new[]{pair,p1,p2,manualPair,manual1,manual2,notes})field.interactable=settingsEnabled;
        foreach(var button in groups)button.interactable=settingsEnabled;
        Sync(pair,d.pairId);Sync(p1,d.participant1Id);Sync(p2,d.participant2Id);Sync(notes,d.notes);Sync(manualPair,d.pairId);Sync(manual1,d.participant1Id);Sync(manual2,d.participant2Id);
        p2.gameObject.SetActive(logger.GetComponent<ExperimentSessionManager>().DraftExpectedParticipants==2);
        summary.text="次回候補："+d.trialOrder+"回目 ／ "+d.allocationGroup+"　割り当て一覧：1回目 → 2回目（研究計画と照合して確認）";
        for(int i=0;i<4;i++)groups[i].GetComponent<RoundedPanelGraphic>().color=d.allocationGroup=="G"+(i+1)?ExperimenterUIColors.Accent:ExperimenterUIColors.Control;
        var next=registry.ParticipantClientNos.ToArray();var nextKeys=next.Select(logger.Data.DeviceKey).ToArray();
        if(!clients.SequenceEqual(next) || !keys.SequenceEqual(nextKeys) || displayedSession!=m?.sessionId)
        {
            clients=next;keys=nextKeys;displayedSession=m?.sessionId;message="";
            foreach(var dropdown in new[]{device1,device2}){dropdown.Hide();dropdown.ClearOptions();dropdown.AddOptions(new[]{"端末を選択（本人確認後）"}.Concat(clients.Select((c,i)=>"接続端末 "+(char)('A'+i))).ToList());dropdown.SetValueWithoutNotify(0);dropdown.RefreshShownValue();}
        }
        string Status(string id)
        {return id+"："+(clients.Any(c=>logger.Data.TryResolveParticipant(c,out string pid)&&pid==id)?"確認済み":"未確認");}
        bool controlled=m!=null && m.plan.experimentType!="Test";
        connections.text=controlled?Status(m.plan.participant1Id)+(m.expectedParticipantCount==2?"　／　"+Status(m.plan.participant2Id):"")+"　接続端末："+clients.Length+"台":"セッション準備後に参加者の端末を確認します";
        secondRow.SetActive(controlled && m.expectedParticipantCount==2);
        bind1.GetComponentInChildren<TMP_Text>().text=(controlled?m.plan.participant1Id:"参加者1")+"を確認・登録";
        bind2.GetComponentInChildren<TMP_Text>().text=(controlled?m.plan.participant2Id:"参加者2")+"を確認・登録";
        bool editable=controlled && !logger.GetComponent<ExperimentSessionManager>().IsResettingSession && !logger.GetComponent<ExperimentSessionManager>().Lobby.HasStartRequestedOrStarted;
        bind1.interactable=editable&&device1.value>0;bind2.interactable=editable&&device2.value>0;
        error.text=message;
    }
    static void Sync(TMP_InputField field,string value){if(!field.isFocused && field.text!=value)field.SetTextWithoutNotify(value);}
    TMP_InputField Input(string name,Transform parent,Vector2 pos,string title,float width,Func<ExperimentDataPlan,string> getter,Action<ExperimentDataPlan,string> setter)
    {var field=InputRaw(name,parent,pos,title,width,name=="Notes"?1000:40);field.onEndEdit.AddListener(v=>Edit(p=>setter(p,v)));return field;}
    TMP_InputField InputRaw(string name,Transform parent,Vector2 pos,string title,float width,int limit)
    {
        var rect=Rect(name,parent,pos,new Vector2(width,48));var bg=rect.gameObject.AddComponent<UnityEngine.UI.Image>();bg.color=ExperimenterUIColors.Field;
        float caption=title.Length>6?260:140;Text("Caption",rect,new Vector2(12,-8),new Vector2(caption-20,32),title,20);
        var viewport=Rect("Viewport",rect,new Vector2(caption,-4),new Vector2(width-caption-12,40));viewport.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
        var field=rect.gameObject.AddComponent<TMP_InputField>();var text=Text("Text",viewport,Vector2.zero,viewport.sizeDelta,"",21);text.richText=false;
        field.textViewport=viewport;field.textComponent=text;field.targetGraphic=bg;field.characterLimit=limit;return field;
    }
    TMP_Dropdown Dropdown(string name,Transform parent,Vector2 pos,Vector2 size,string[] options)
    {
        var root=Rect(name,parent,pos,size);var bg=root.gameObject.AddComponent<UnityEngine.UI.Image>();bg.color=ExperimenterUIColors.Field;
        var drop=root.gameObject.AddComponent<TMP_Dropdown>();drop.targetGraphic=bg;
        drop.captionText=Text("Selected",root,new Vector2(12,-6),size-new Vector2(50,12),"",20);
        Text("Arrow",root,new Vector2(size.x-34,-6),new Vector2(26,36),"▼",20);
        var template=Rect("Template",root,new Vector2(0,-size.y),new Vector2(size.x,192));var surface=template.gameObject.AddComponent<UnityEngine.UI.Image>();surface.color=new Color(.08f,.13f,.18f);
        var scroll=template.gameObject.AddComponent<UnityEngine.UI.ScrollRect>();scroll.horizontal=false;scroll.movementType=UnityEngine.UI.ScrollRect.MovementType.Clamped;scroll.scrollSensitivity=30;
        var viewport=Rect("Viewport",template,Vector2.zero,template.sizeDelta);viewport.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();var hit=viewport.gameObject.AddComponent<UnityEngine.UI.Image>();hit.color=Color.clear;
        var content=Rect("Content",viewport,Vector2.zero,new Vector2(size.x,48));scroll.viewport=viewport;scroll.content=content;
        var item=Rect("Item",content,Vector2.zero,new Vector2(size.x,48));var itemBg=item.gameObject.AddComponent<UnityEngine.UI.Image>();itemBg.color=ExperimenterUIColors.Control;
        var toggle=item.gameObject.AddComponent<UnityEngine.UI.Toggle>();toggle.targetGraphic=itemBg;
        var check=Rect("Check",item,new Vector2(4,-14),new Vector2(12,20)).gameObject.AddComponent<UnityEngine.UI.Image>();check.color=new Color(.35f,.86f,.79f);toggle.graphic=check;
        drop.itemText=Text("ItemLabel",item,new Vector2(24,-6),new Vector2(size.x-36,36),"",20);
        drop.template=template;drop.AddOptions(options.ToList());template.gameObject.SetActive(false);return drop;
    }
    UnityEngine.UI.Button Button(string name,Transform parent,Vector2 pos,Vector2 size,string title,UnityEngine.Events.UnityAction action)
    {var rect=Rect(name,parent,pos,size);var bg=rect.gameObject.AddComponent<RoundedPanelGraphic>();bg.color=ExperimenterUIColors.Control;var b=rect.gameObject.AddComponent<UnityEngine.UI.Button>();b.targetGraphic=bg;b.onClick.AddListener(action);var t=Text("Label",rect,new Vector2(8,-4),size-new Vector2(16,8),title,20);t.alignment=TextAlignmentOptions.Center;return b;}
    TMP_Text Text(string name,Transform parent,Vector2 pos,Vector2 size,string value,float fontSize)
    {var rect=Rect(name,parent,pos,size);var t=rect.gameObject.AddComponent<TextMeshProUGUI>();t.font=font;t.richText=false;t.text=value;t.fontSize=fontSize;t.color=Color.white;t.raycastTarget=false;t.alignment=TextAlignmentOptions.TopLeft;t.textWrappingMode=TextWrappingModes.Normal;return t;}
    static RectTransform Rect(string name,Transform parent,Vector2 pos,Vector2 size)
    {var rect=(RectTransform)new GameObject(name,typeof(RectTransform)).transform;rect.SetParent(parent,false);rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(0,1);rect.anchoredPosition=pos;rect.sizeDelta=size;return rect;}
}
