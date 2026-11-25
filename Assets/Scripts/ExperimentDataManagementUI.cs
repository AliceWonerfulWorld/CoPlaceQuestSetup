using System;
using System.Linq;
using TMPro;
using UnityEngine;

// Owned by the existing PC-only management Canvas; no participant UI or network state.
public sealed class ExperimentDataManagementUI
{
    readonly ExperimentLogger logger;
    readonly ExperimentParticipantRegistry registry;
    readonly Transform panel;
    readonly TMP_FontAsset font;
    TMP_InputField pair,p1,p2,notes,verification;
    TMP_Text summary,error,connections;
    int client1=-1,client2=-1;
    string message="";
    public ExperimentDataManagementUI(ExperimentLogger log,ExperimentParticipantRegistry roles,Transform parent,TMP_FontAsset japanese)
    {
        logger=log;registry=roles;font=japanese;
        panel=Rect("ExperimentDataManagement",parent,new Vector2(890,-68),new Vector2(950,990));
        var bg=panel.gameObject.AddComponent<RoundedPanelGraphic>();bg.color=new Color(.035f,.055f,.085f,.98f);
        Text("Header",panel,new Vector2(20,-12),new Vector2(910,40),"実験情報・参加者ID（実験者専用）",26);
        Button("Test",new Vector2(20,-62),new Vector2(270,44),"Test：動作確認",()=>Edit(p=>p.experimentType="Test"));
        Button("Pilot",new Vector2(310,-62),new Vector2(290,44),"Pilot：予備実験",()=>SelectType("Pilot"));
        Button("Main",new Vector2(620,-62),new Vector2(300,44),"Main：本実験",()=>SelectType("Main"));
        pair=Input("Pair",new Vector2(20,-122),"ペアID（例：001）",p=>p.pairId);
        p1=Input("Participant1",new Vector2(20,-178),"参加者1 ID（例：P001）",p=>p.participant1Id);
        p2=Input("Participant2",new Vector2(480,-178),"参加者2 ID（例：P002）",p=>p.participant2Id);
        for(int i=1;i<=4;i++){string group="G"+i;Button(group,new Vector2(20+(i-1)*225,-240),new Vector2(210,42),group,()=>Edit(p=>p.allocationGroup=group));}
        Button("Order1",new Vector2(20,-292),new Vector2(285,42),"試行順序：1回目",()=>Edit(p=>p.trialOrder=1));
        Button("Order2",new Vector2(325,-292),new Vector2(285,42),"試行順序：2回目",()=>Edit(p=>p.trialOrder=2));
        notes=Input("Notes",new Vector2(20,-348),"備考（1000文字以内・氏名は入力しない）",p=>p.notes,900);
        summary=Text("AppliedAndDraft",panel,new Vector2(20,-408),new Vector2(910,120),"",19);
        Text("BindingHelp",panel,new Vector2(20,-535),new Vector2(910,82),"適用済みIDと端末の対応確認（再接続時も確認）\n対象者だけを接続・切断して一覧の変化を確認し、本人に確認して登録します。接続順では割り当てません。",18);
        verification=InputRaw("Verification",new Vector2(20,-622),"確認方法（例：P001だけ接続し本人に確認）",900);
        Button("Choose1",new Vector2(20,-680),new Vector2(460,42),"参加者1の端末を選ぶ",()=>client1=Next(client1));
        Button("Bind1",new Vector2(500,-680),new Vector2(420,42),"参加者1の対応を確認・登録",()=>Bind(1,client1));
        Button("Choose2",new Vector2(20,-730),new Vector2(460,42),"参加者2の端末を選ぶ",()=>client2=Next(client2));
        Button("Bind2",new Vector2(500,-730),new Vector2(420,42),"参加者2の対応を確認・登録",()=>Bind(2,client2));
        connections=Text("Connections",panel,new Vector2(20,-780),new Vector2(910,60),"",18);
        Button("SecondDraft",new Vector2(20,-848),new Vector2(445,44),"同じペアの2回目を次回設定へ",()=>message=logger.Data.PrepareSecondDraft()?"2回目の次回設定を用意しました。新規準備は手動で行ってください":"保存済みの1回目が必要です");
        Button("RebuildIndex",new Vector2(485,-848),new Vector2(435,44),"セッション一覧を再構築",()=>message=logger.RebuildIndex()?"セッション一覧を再構築しました":"再構築できません。メタデータと保存先を確認してください");
        error=Text("MetadataStatus",panel,new Vector2(20,-910),new Vector2(910,70),"",20);
        panel.gameObject.SetActive(false);
    }
    public void SetVisible(bool visible){panel.gameObject.SetActive(visible);if(visible)Refresh();}
    void SelectType(string type)=>Edit(p=>{p.experimentType=type;if(p.allocationGroup=="")p.allocationGroup="G1";});
    void Edit(Action<ExperimentDataPlan> action){var draft=logger.Data.Draft;action(draft);if(!logger.Data.SetDraft(draft))message="準備処理中は設定できません";}
    int Next(int selected){var clients=registry.ParticipantClientNos.ToArray();if(clients.Length==0)return -1;int index=Array.IndexOf(clients,selected);return clients[(index+1)%clients.Length];}
    void Bind(int slot,int client)
    {
        var m=logger.Data.Applied;
        if(m==null){message="先に新規セッションを準備してください";return;}
        message=logger.Data.ConfirmBinding(slot==1?m.plan.participant1Id:m.plan.participant2Id,client,verification.text)?"対応を記録しました":logger.Data.LastError;
    }
    public void Refresh()
    {
        if(!panel.gameObject.activeSelf)return;
        var d=logger.Data.Draft;var m=logger.Data.Applied;
        Sync(pair,d.pairId);Sync(p1,d.participant1Id);Sync(p2,d.participant2Id);Sync(notes,d.notes);
        summary.text="次回："+d.experimentType+" ／ ペア "+d.pairId+" ／ "+d.allocationGroup+" ／ "+d.trialOrder+"回目\n適用済み："+(m==null?"未準備":m.plan.experimentType+" ／ ペア "+m.plan.pairId+" ／ "+m.plan.allocationGroup+" ／ "+m.plan.trialOrder+"回目 ／ "+m.condition+" "+m.cardSetId)+"\n適用済みID："+(m==null?"未準備":m.plan.participant1Id+" ／ "+m.plan.participant2Id)+"\n保存先："+(m==null?"未決定":m.relativePath.Replace(m.sessionId,m.sessionId.Substring(0,Math.Min(12,m.sessionId.Length))+"…"));
        connections.text="登録："+logger.Data.BindingSummary+"\n選択：参加者1 → "+(client1<0?"未選択":client1.ToString())+" ／ 参加者2 → "+(client2<0?"未選択":client2.ToString())+"　接続端末："+string.Join("　",registry.ParticipantClientNos.Select(c=>c+"（端末 "+Short(logger.Data.DeviceKey(c))+"）"));
        error.text=message!=""?message:logger.Data.StartBlockReason!=""?logger.Data.StartBlockReason:logger.IndexWarning!=""?"一覧CSVの更新に失敗しています。再構築してください":"実験情報を確認できました";
    }
    static string Short(string value)=>value.Length>8?value.Substring(0,8):"未確認";
    static void Sync(TMP_InputField field,string value){if(!field.isFocused && field.text!=value)field.SetTextWithoutNotify(value);}
    TMP_InputField Input(string name,Vector2 pos,string hint,Func<ExperimentDataPlan,string> getter,float width=440)
    {
        var input=InputRaw(name,pos,hint,width);
        input.onEndEdit.AddListener(value=>Edit(p=>{if(name=="Pair")p.pairId=value;else if(name=="Participant1")p.participant1Id=value;else if(name=="Participant2")p.participant2Id=value;else p.notes=value;}));return input;
    }
    TMP_InputField InputRaw(string name,Vector2 pos,string hint,float width)
    {
        var rect=Rect(name,panel,pos,new Vector2(width,48));var bg=rect.gameObject.AddComponent<UnityEngine.UI.Image>();bg.color=new Color(.10f,.16f,.21f);
        string caption=name=="Pair"?"ペアID":name=="Participant1"?"参加者1 ID":name=="Participant2"?"参加者2 ID":name=="Notes"?"備考":"確認方法";
        string example=name=="Pair"?"例：001":name=="Participant1"?"例：P001":name=="Participant2"?"例：P002":name=="Notes"?"1000文字以内・氏名は入力しない":"対象者だけ接続し本人に確認";
        Text("Caption",rect,new Vector2(8,-4),new Vector2(125,40),caption,19);
        var viewport=Rect("Viewport",rect,new Vector2(140,-4),new Vector2(width-150,40));viewport.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
        var field=rect.gameObject.AddComponent<TMP_InputField>();var text=Text("Text",viewport,Vector2.zero,viewport.sizeDelta,"",21);text.richText=false;
        var placeholder=Text("Placeholder",viewport,Vector2.zero,viewport.sizeDelta,example,20);placeholder.color=new Color(.65f,.73f,.78f);
        field.textViewport=viewport;field.textComponent=text;field.placeholder=placeholder;field.targetGraphic=bg;field.characterLimit=name=="Notes"?1000:name=="Verification"?200:40;
        return field;
    }
    void Button(string name,Vector2 pos,Vector2 size,string title,UnityEngine.Events.UnityAction action)
    {var rect=Rect(name,panel,pos,size);var bg=rect.gameObject.AddComponent<RoundedPanelGraphic>();bg.color=new Color(.12f,.24f,.29f);var b=rect.gameObject.AddComponent<UnityEngine.UI.Button>();b.targetGraphic=bg;b.onClick.AddListener(action);var t=Text("Label",rect,new Vector2(6,-3),size-new Vector2(12,6),title,20);t.alignment=TextAlignmentOptions.Center;}
    TMP_Text Text(string name,Transform parent,Vector2 pos,Vector2 size,string value,float fontSize)
    {var rect=Rect(name,parent,pos,size);var t=rect.gameObject.AddComponent<TextMeshProUGUI>();t.font=font;t.richText=false;t.text=value;t.fontSize=fontSize;t.enableAutoSizing=false;t.color=Color.white;t.raycastTarget=false;t.alignment=TextAlignmentOptions.TopLeft;t.textWrappingMode=TextWrappingModes.Normal;return t;}
    static RectTransform Rect(string name,Transform parent,Vector2 pos,Vector2 size)
    {var rect=(RectTransform)new GameObject(name,typeof(RectTransform)).transform;rect.SetParent(parent,false);rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(0,1);rect.anchoredPosition=pos;rect.sizeDelta=size;return rect;}
}
