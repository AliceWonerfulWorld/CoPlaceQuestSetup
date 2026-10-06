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
        canvas.enabled = agreement.IsAgreementPhase && !agreement.IsBoardConfirmed;
        readyButton.interactable = agreement.CanToggleReady && Time.unscaledTime - lastAcceptedClickTime >= ClickCooldownSeconds;
        string label = agreement.IsBoardConfirmed ? "Board Confirmed" : (agreement.IsLocalReady || agreement.LocalApprovalPending) ? "Cancel Ready" : "This board is OK";
        if (buttonText.text != label) buttonText.text = label;
        var b = new StringBuilder(agreement.IsBoardConfirmed ? "FINAL BOARD CONFIRMED\n" : "FINAL AGREEMENT\n");
        b.Append("Participants: ").Append(agreement.Participants.Count).Append(" / ").Append(agreement.ExpectedParticipantCount).AppendLine();
        foreach (int client in agreement.Participants)
        {
            int displayId = agreement.DisplayId(client);
            var style = participantStyles != null ? participantStyles.GetParticipantStyle(displayId) : null;
            string name = style != null ? style.label : "P" + displayId;
            string color = ColorUtility.ToHtmlStringRGB(style != null ? style.color : Color.white);
            b.Append("<color=#").Append(color).Append('>').Append(name.Replace("<", "").Replace(">", "")).Append("</color>").Append(client == agreement.LocalClientNo ? " (You)  " : "  ")
                .AppendLine(agreement.IsParticipantReady(client) ? "Ready" : "Waiting");
        }
        if (!agreement.IsLocalParticipant) b.AppendLine("Experimenter: view only");
        if (!string.IsNullOrEmpty(agreement.StatusMessage)) b.AppendLine(agreement.StatusMessage);
        string text = b.ToString(); if (lastText != text) { lastText = text; statusText.text = text; }
        // Fit the panel vertically as participants are added, keeping the same readable font size.
        float height = Mathf.Max(430, 230 + agreement.Participants.Count * 58 + (!string.IsNullOrEmpty(agreement.StatusMessage) ? 100 : 0));
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
