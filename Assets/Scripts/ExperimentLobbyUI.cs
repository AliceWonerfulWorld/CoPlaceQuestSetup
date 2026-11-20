using TMPro;
using UnityEngine;

public class ExperimentLobbyUI : MonoBehaviour
{
    public const string GeneralDescription = "画像カードをA〜Eに分類する実験です。\nカードをつかみ、分類先で離してください。";
    public const string NormalDescription = "他の参加者の配置を確認しながら、\n同じボード上で画像カードを分類します。";
    public const string ProposedDescription = "最初に他の参加者の回答を見ずに個別に分類します。\n全員の回答後に結果を比較し、\n話し合いながら最終配置を決定します。";
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
    private void Update()
    {
        if (lobby == null || experimentManager == null) return;
        if (contentRoot.activeSelf != (lobby.IsLobbyVisible && !lobby.IsExperimenter)) contentRoot.SetActive(lobby.IsLobbyVisible && !lobby.IsExperimenter);
        if (!lobby.IsLobbyVisible) return;
        experimenterControls.SetActive(false);
        ParticipantUIStyle.Apply(transform);
        var session = lobby.Session;
        bool applied = session != null && session.HasAppliedSettings;
        bool normal = applied && session.SessionMode == ExperimentManager.ExperimentMode.Normal;
        Set(modeHeading, !applied ? "実験方式は準備中です" : normal ? "通常方式 ／ 共同分類" : "提案方式 ／ 独立回答 → 共同分類");
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
    private static void Set(TMP_Text target, string text) { if (target != null && target.text != text) target.text = text; }
}
