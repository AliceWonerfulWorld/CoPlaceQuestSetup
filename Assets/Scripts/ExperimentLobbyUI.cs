using TMPro;
using UnityEngine;

public class ExperimentLobbyUI : MonoBehaviour
{
    public const string TaskTitle = "新入生歓迎イベントを企画しよう";
    public const string GeneralDescription = "あなたたちは、大学のサークルの新入生歓迎イベントを企画するメンバーです。\n\n各イベント候補について、新入生歓迎イベントとしてどの程度適しているかを考え、A〜Eの5段階に分類してください。";
    public const string JudgementGuide = "・新入生が参加しやすいか\n・参加者同士が交流しやすいか\n・楽しめるイベントか";
    public const string JudgementNote = "これらは判断の参考となる観点です。どの観点を重視するかは、自分の考えに基づいて判断してください。";
    public const string TaskGoal = "この課題に正解はありません。\n最終的には、参加者全員が同意できる分類結果を作成してください。";
    public const string ClassificationRules = "すべての候補をA〜Eに分類します。\n同じ段階に複数枚を配置できます。枚数制限はありません。\nすべての段階を使う必要はありません。\nカード間の厳密な順位付けは不要です。\n未分類への移動や再分類もできます。";
    public const string NormalDescription = "1. イベント候補を確認する。\n2. 参加者同士で話し合う。\n3. すべてのイベント候補を共同でA〜Eに分類する。\n4. 最終的な分類結果に全員が同意する。";
    public const string ProposedDescription = "1. 各自の考えに基づいてイベント候補を分類する。\n2. 独立回答を提出する。\n3. 全員の独立回答が公開される。\n4. 公開された回答も参照しながら話し合い、共同で分類する。\n5. 最終的な分類結果に全員が同意する。";
    [SerializeField] private ExperimentLobbyController lobby;
    [SerializeField] private ExperimentManager experimentManager;
    [SerializeField] private GameObject contentRoot;
    [SerializeField] private GameObject experimenterControls;
    [SerializeField] private TMP_Text modeHeading;
    [SerializeField] private TMP_Text modeDescription;
    [SerializeField] private TMP_Text connection;
    [SerializeField] private TMP_Text participants;
    [SerializeField] private TMP_Text waiting;
    [SerializeField] private TMP_Text sessionInfo;
    [SerializeField] private UnityEngine.UI.Button normalButton, proposedButton, newSessionButton, startButton;
    private bool presentationConfigured;
    private void Update()
    {
        if (lobby == null || experimentManager == null) return;
        if (contentRoot.activeSelf != (lobby.IsLobbyVisible && !lobby.IsExperimenter)) contentRoot.SetActive(lobby.IsLobbyVisible && !lobby.IsExperimenter);
        if (!lobby.IsLobbyVisible) return;
        experimenterControls.SetActive(false);
        ParticipantUIStyle.Apply(transform);
        ConfigurePresentation();
        var session = lobby.Session;
        bool applied = session != null && session.HasAppliedSettings;
        bool normal = applied && session.SessionMode == ExperimentManager.ExperimentMode.Normal;
        Set(modeHeading, !applied ? "実験方式は準備中です" : normal ? "現在の方式：通常方式 ／ 共同分類" : "現在の方式：提案方式 ／ 独立回答 → 共同分類");
        Set(modeDescription, !applied ? "実験者が実験の準備をしています。" : normal ? NormalDescription : ProposedDescription);
        bool all = lobby.IsConnected && lobby.ConnectedParticipants == lobby.ExpectedParticipants;
        Set(connection, lobby.IsConnected ? "接続済み" : "未接続");
        Set(participants, $"参加者の接続：{lobby.ConnectedParticipants} / {lobby.ExpectedParticipants}人\n" +
            (lobby.IsConnected && session != null ? "初期化完了：" + session.InitializedParticipants + "人" : "初期化を待っています"));
        Set(waiting, lobby.IsExperimenter ? (lobby.CanStart ? "開始の準備ができました" : lobby.StartBlockReason) :
            !lobby.IsConnected ? "接続を待っています。" : lobby.IsResetting || session == null || !session.HasAppliedSettings || !session.AreConnectedClientsInitialized ? "実験の準備をしています。" : !all ? (lobby.ConnectedParticipants < lobby.ExpectedParticipants ? "他の参加者の接続を待っています。" : "参加者の接続を確認しています。実験者の案内をお待ちください。") : "実験者が実験を開始するまでお待ちください。");
        if (lobby.IsExperimenter)
        {
            Set(sessionInfo, "Session ID\n" + (string.IsNullOrEmpty(lobby.SessionId) ? "未作成" : lobby.SessionId) + "\n\n現在状態  /  " +
                (lobby.IsResetting ? "Resetting" : lobby.CurrentState));
            normalButton.interactable = lobby.CanChangeMode && !normal;
            proposedButton.interactable = lobby.CanChangeMode && normal;
            newSessionButton.interactable = lobby.CanNewSession;
            startButton.interactable = lobby.CanStart;
        }
    }
    // Reuse the existing Lobby Canvas and status fields. Text-only children are
    // idempotent; no extra interaction, timing boundary or confirmation is added.
    public void ConfigurePresentation(bool force = false)
    {
        if ((!force && presentationConfigured) || contentRoot == null) return;
        var intro = contentRoot.transform.Find("ParticipantIntroduction") as RectTransform;
        if (intro == null) return;
        presentationConfigured = true;
        ((RectTransform)contentRoot.transform).sizeDelta = new Vector2(3000,1940);
        intro.sizeDelta = new Vector2(2800,1940);
        foreach (var name in new[] { "Border", "Surface" })
        { var rect=intro.Find(name) as RectTransform; if(rect!=null) rect.sizeDelta=intro.sizeDelta-Vector2.one*(name=="Surface"?3:0); }
        Label(intro,"Eyebrow","実験課題",-680,850,1240,60,34);
        Label(intro,"Title",TaskTitle,-680,740,1240,140,56);
        Label(intro,"Subtitle","CoPlace ／ 共同VR空間でのイベント分類",-680,625,1240,80,34);
        Label(intro,"Description",GeneralDescription,-680,430,1240,290,38);
        Label(intro,"JudgementHeading","判断する際の参考",-680,225,1240,65,42);
        Label(intro,"JudgementGuide",JudgementGuide,-680,80,1240,200,38);
        Label(intro,"JudgementNote",JudgementNote,-680,-135,1240,230,38);
        Label(intro,"TaskGoal",TaskGoal,-680,-365,1240,210,38);
        Label(intro,"ClassificationRules",ClassificationRules,-680,-680,1240,320,36);
        Label(intro,"CriteriaHeading","A〜Eの評価基準",680,850,1240,65,42);
        Label(intro,"TaskCriteria",ParticipantUIStyle.CriteriaLegend(),680,605,1240,370,40);
        Label(intro,"UnclassifiedNote","未分類：まだ分類されていません。\nCは評価の一つで、未分類とは異なります。",680,345,1240,130,36);
        var condition=intro.Find("CurrentCondition") as RectTransform;
        condition.anchoredPosition=new Vector2(680,-35); condition.sizeDelta=new Vector2(1240,580);
        var surface=condition.Find("Surface") as RectTransform;if(surface!=null)surface.sizeDelta=condition.sizeDelta;
        Arrange(modeHeading,new Vector2(0,230),new Vector2(1140,100),36);
        Label(condition,"StepsHeading","実験手順",0,135,1140,60,34);
        Arrange(modeDescription,new Vector2(0,-85),new Vector2(1140,370),36);
        Label(intro,"BasicOperation","カードをつかみ、分類先で離してください。",680,-395,1240,100,36);
        MoveSection(intro,"Participants",new Vector2(357,-550),new Vector2(594,170));
        MoveSection(intro,"Connection",new Vector2(1003,-550),new Vector2(594,170));
        Arrange(participants,Vector2.zero,new Vector2(540,150),32);
        Arrange(connection,new Vector2(0,-23),new Vector2(540,80),34);
        var caption=intro.Find("Connection/Caption").GetComponent<TMP_Text>();Arrange(caption,new Vector2(0,47),new Vector2(540,50),28);
        MoveSection(intro,"Waiting",new Vector2(680,-785),new Vector2(1240,230));
        Arrange(waiting,Vector2.zero,new Vector2(1140,190),38);
    }
    private static void Arrange(TMP_Text text,Vector2 pos,Vector2 size,float fontSize)
    {
        if(text==null)return;
        text.rectTransform.anchoredPosition=pos;text.rectTransform.sizeDelta=size;
        text.font=ParticipantUIStyle.Font;text.fontSize=fontSize;text.enableAutoSizing=false;
        text.textWrappingMode=TextWrappingModes.Normal;text.overflowMode=TextOverflowModes.Overflow;
    }
    private static void MoveSection(Transform intro,string name,Vector2 pos,Vector2 size)
    {
        var rect=intro.Find(name) as RectTransform;rect.anchoredPosition=pos;rect.sizeDelta=size;
        var surface=rect.Find("Surface") as RectTransform;if(surface!=null)surface.sizeDelta=size;
    }
    private static void Label(Transform parent,string name,string value,float x,float y,float width,float height,float size)
    {
        var child=parent.Find(name);
        if(child==null)child=new GameObject(name,typeof(RectTransform),typeof(TextMeshProUGUI)).transform;
        child.SetParent(parent,false);var rect=(RectTransform)child;
        rect.anchorMin=rect.anchorMax=rect.pivot=Vector2.one*.5f;
        var text=child.GetComponent<TMP_Text>();Arrange(text,new Vector2(x,y),new Vector2(width,height),size);
        text.text=value;text.raycastTarget=false;text.alignment=TextAlignmentOptions.MidlineLeft;
        text.color=name.EndsWith("Heading") || name=="Eyebrow" ? new Color(.38f,.85f,.80f) : Color.white;
    }
    private static void Set(TMP_Text target, string text) { if (target != null && target.text != text) target.text = text; }
}
