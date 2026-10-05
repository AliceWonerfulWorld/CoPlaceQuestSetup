using TMPro;
using UnityEngine;

public class ExperimentLobbyUI : MonoBehaviour
{
    public const string GeneralDescription = "複数人で画像カードをA〜DのTierに分類します。\n他の参加者と話し合いながら、\n最終的な配置を決定してください。";
    public const string NormalDescription = "他の参加者の配置を確認しながら、\n同じTierBoard上で画像カードを分類します。";
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
        if (contentRoot.activeSelf != lobby.IsLobbyVisible) contentRoot.SetActive(lobby.IsLobbyVisible);
        if (!lobby.IsLobbyVisible) return;
        experimenterControls.SetActive(lobby.IsExperimenter);
        bool normal = experimentManager.CurrentMode == ExperimentManager.ExperimentMode.Normal;
        Set(modeHeading, normal ? "NORMAL  /  共同分類" : "PROPOSED  /  個別回答 → 比較・共同分類");
        Set(modeDescription, normal ? NormalDescription : ProposedDescription);
        bool all = lobby.IsConnected && lobby.ConnectedParticipants == lobby.ExpectedParticipants;
        Set(connection, lobby.IsConnected ? "● Connected  /  接続済み" : "○ Waiting  /  接続待ち");
        Set(participants, $"Participants\n{lobby.ConnectedParticipants} / {lobby.ExpectedParticipants} Connected\n" +
            (all ? "● 参加者が揃いました" : "○ 参加者を待っています"));
        Set(waiting, lobby.IsExperimenter ? (lobby.CanStart ? "開始の準備ができました" : lobby.StartBlockReason) :
            lobby.IsResetting ? "Session Resetを処理しています\n実験者の開始をお待ちください" : "実験者の開始をお待ちください");
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
