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
    private CandidateSyncTest answers;
    private float nextClick;
    private void Awake() { answers = privateBoard != null ? privateBoard.Answers : null; ParticipantUIStyle.Apply(transform); }
    private void Update()
    {
        if (submission == null || privateBoard == null) return;
        canvas.enabled = privateBoard.IsVisible;
        if (answers == null) answers = privateBoard.Answers;
        bool submitted = submission.IsLocalSubmitted;
        button.interactable = Time.unscaledTime >= nextClick && (submitted ? submission.CanCancel : submission.CanSubmit);
        label.text = submitted ? "提出を取り消す" : "回答を提出";
        int total = answers != null ? answers.AnswerCardIds.Count : 0, completed = 0;
        if (answers != null) foreach (string id in answers.AnswerCardIds)
            if (answers.TryGetParticipantAnswer(answers.LocalClientNo, id, out var answer) && answer.Answered) completed++;
        string state = answers == null || !answers.IsDataReady ? "接続を待っています。" : submission.IsCancellationPending ? "提出を取り消しています。" :
            reveal != null && reveal.IsAnswerLocked ? "全員の回答を公開する準備をしています。" :
            submitted ? "回答を提出しました。\n全員の提出がそろうまでお待ちください。" :
            submission.CanSubmit ? "分類できました。内容を確認して回答を提出してください。" :
            privateBoard.HasHeldCard ? "カードを離してください。" : completed == total && total > 0 ? "回答の同期を待っています。" : "未分類のカードをすべて配置してください。";
        status.text = "独立回答 ／ 分類済み " + completed + " / " + total + "枚\n" + state;

    }
    public void ToggleSubmission()
    {
        if (submission == null || Time.unscaledTime < nextClick) return;
        bool accepted = submission.IsLocalSubmitted ? submission.TryCancel() : submission.TrySubmit();
        if (accepted) nextClick = Time.unscaledTime + 0.5f;
    }
}
