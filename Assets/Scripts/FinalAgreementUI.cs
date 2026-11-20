using System.Text;
using TMPro;
using UnityEngine;

public class FinalAgreementUI : MonoBehaviour
{
    [SerializeField] private FinalAgreementManager agreement;
    [SerializeField] private CandidateMarkerManager participantStyles;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private TMP_Text buttonText;
    [SerializeField] private UnityEngine.UI.Button readyButton;
    [SerializeField] private Canvas canvas;
    private const float ClickCooldownSeconds = 0.5f;
    private float lastAcceptedClickTime = float.NegativeInfinity;
    private bool duplicateClickLogged;
    private string lastText;
    private void Update()
    {
        if (agreement == null) return;
        ParticipantUIStyle.Apply(transform);
        canvas.enabled = agreement.IsAgreementPhase && !agreement.IsBoardConfirmed;
        readyButton.interactable = agreement.CanToggleReady && Time.unscaledTime - lastAcceptedClickTime >= ClickCooldownSeconds;
        string label = agreement.IsBoardConfirmed ? "確定済み" : (agreement.IsLocalReady || agreement.LocalApprovalPending) ? "同意を取り消す" : "この配置に同意する";
        if (buttonText.text != label) buttonText.text = label;
        var b = new StringBuilder(agreement.IsBoardConfirmed ? "分類結果が確定しました。\n" : "最終結果への同意\n全員が同意すると結果が確定します。\n");
        b.Append("参加者：").Append(agreement.Participants.Count).Append(" / ").Append(agreement.ExpectedParticipantCount).AppendLine();
        foreach (int client in agreement.Participants)
        {
            int displayId = agreement.DisplayId(client);
            var style = participantStyles != null ? participantStyles.GetParticipantStyle(displayId) : null;
            string name = "参加者" + displayId;
            string color = ColorUtility.ToHtmlStringRGB(style != null ? style.color : Color.white);
            b.Append("<color=#").Append(color).Append('>').Append(name.Replace("<", "").Replace(">", "")).Append("</color>").Append(client == agreement.LocalClientNo ? "（あなた） " : "  ")
                .AppendLine(client == agreement.LocalClientNo && agreement.LocalApprovalPending ? "同期待ち" : agreement.IsParticipantReady(client) ? "同意済み" : "未同意");
        }
        if (!agreement.IsLocalParticipant) b.AppendLine("実験者：閲覧専用");
        if (!agreement.IsConnectionReady) b.AppendLine("未接続：接続を待っています。");
        if (!string.IsNullOrEmpty(agreement.StatusMessage)) b.AppendLine(agreement.HasUnclassifiedCards ? "未分類のカードをすべて配置してください。" : ParticipantUIStyle.AgreementMessage(agreement.StatusMessage));
        if (agreement.IsLocalReady) b.AppendLine("他の参加者の同意を待っています。");
        string text = b.ToString(); if (lastText != text) { lastText = text; statusText.text = text; }
        // Fit the panel vertically as participants are added, keeping the same readable font size.
        float height = Mathf.Max(540, 320 + agreement.Participants.Count * 58 + (!string.IsNullOrEmpty(agreement.StatusMessage) ? 100 : 0));
        var rect = (RectTransform)transform; rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
    }
    public void ResetLocalSession() { lastAcceptedClickTime = float.NegativeInfinity; duplicateClickLogged = false; lastText = null; }
    public void ToggleReady()
    {
        if (agreement == null || !agreement.CanToggleReady) return;
        // XR pointer/submit events can arrive close together. A duplicate must not
        // turn the newly accepted approval into a cancellation.
        if (Time.unscaledTime - lastAcceptedClickTime < ClickCooldownSeconds)
        {
            if (!duplicateClickLogged)
            {
                duplicateClickLogged = true;
                Debug.Log("[Final Agreement UI] Duplicate click ignored", this);
            }
            return;
        }
        lastAcceptedClickTime = Time.unscaledTime;
        duplicateClickLogged = false;
        Debug.Log($"[Final Agreement UI] {(agreement.IsLocalReady || agreement.LocalApprovalPending ? "Cancel" : "Ready")} requested / frame {Time.frameCount}", this);
        agreement.ToggleLocalReady();
    }
}
