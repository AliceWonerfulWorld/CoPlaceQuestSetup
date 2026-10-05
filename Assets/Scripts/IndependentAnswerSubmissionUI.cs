using TMPro;
using UnityEngine;

public class IndependentAnswerSubmissionUI : MonoBehaviour
{
    [SerializeField] private IndependentAnswerSubmission submission;
    [SerializeField] private PrivateTierBoardController privateBoard;
    [SerializeField] private ProposedRevealCoordinator reveal;
    [SerializeField] private Canvas canvas;
    [SerializeField] private UnityEngine.UI.Button button;
    [SerializeField] private TMP_Text label;
    [SerializeField] private TMP_Text status;
    private float nextClick;
    private void Update()
    {
        if (submission == null || privateBoard == null) return;
        canvas.enabled = privateBoard.IsVisible;
        bool submitted = submission.IsLocalSubmitted;
        button.interactable = Time.unscaledTime >= nextClick && (submitted ? submission.CanCancel : submission.CanSubmit);
        label.text = submitted ? "確定を取り消す" : "この回答で確定";
        status.text = submission.IsCancellationPending ? "回答確定を取り消しています..." : submitted ? "回答確定済み\n相手の回答を待っています..." :
            submission.CanSubmit ? "すべての回答がそろいました\n内容を確認して確定してください" :
            reveal != null && reveal.IsAnswerLocked ? "一斉Revealを準備しています..." : "すべてのカードを配置してください";
    }
    public void ToggleSubmission()
    {
        if (submission == null || Time.unscaledTime < nextClick) return;
        bool accepted = submission.IsLocalSubmitted ? submission.TryCancel() : submission.TrySubmit();
        if (accepted) nextClick = Time.unscaledTime + 0.5f;
    }
}
