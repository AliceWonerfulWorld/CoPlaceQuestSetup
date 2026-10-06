using TMPro;
using UnityEngine;

// Presentation only: the existing Main board and confirmed snapshot remain the result.
[DefaultExecutionOrder(400)]
public class ExperimentResultUI : MonoBehaviour
{
    public const string Title = "FINAL RESULT";
    public const string Message = "配置が確定しました";
    public const string Waiting = "実験は終了しました\n実験者の指示があるまでお待ちください";
    [SerializeField] private ExperimentManager experimentManager;
    [SerializeField] private ExperimentSessionManager sessionManager;
    [SerializeField] private ExperimentParticipantRegistry participantRegistry;
    [SerializeField] private GameObject contentRoot;
    [SerializeField] private GameObject experimenterControls;
    [SerializeField] private TMP_Text sessionInfo;
    [SerializeField] private UnityEngine.UI.Button newSessionButton;
    public bool IsVisible => contentRoot != null && contentRoot.activeSelf;

    private void OnEnable()
    {
        if (sessionManager != null) sessionManager.OnSessionResetStarted += OnReset;
        Refresh();
    }
    private void OnDisable()
    {
        if (sessionManager != null) sessionManager.OnSessionResetStarted -= OnReset;
        Hide();
    }
    private void OnReset(string id) => Hide();
    private void Hide() { if (contentRoot != null) contentRoot.SetActive(false); }
    private void LateUpdate() => Refresh();
    public void Refresh()
    {
        bool visible = experimentManager != null &&
            experimentManager.CurrentState == ExperimentManager.ExperimentState.Confirmed &&
            (sessionManager == null || !sessionManager.IsResettingSession);
        if (contentRoot == null) return;
        if (contentRoot.activeSelf != visible) contentRoot.SetActive(visible);
        if (!visible) return;
        bool admin = participantRegistry != null && participantRegistry.IsLocalExperimenter;
        if (experimenterControls != null) experimenterControls.SetActive(admin);
        if (!admin) return;
        string id = sessionManager != null ? sessionManager.CurrentSessionId : "";
        string text = $"Mode: {experimentManager.CurrentMode}  /  Confirmed\nSession ID: {id}\n待機中 — New Session / Resetで次の実験を準備";
        if (sessionInfo != null && sessionInfo.text != text) sessionInfo.text = text;
        if (newSessionButton != null) newSessionButton.interactable = sessionManager != null &&
            !sessionManager.IsResettingSession && participantRegistry.Network != null && participantRegistry.Network.IsReady;
    }
    public void NewSessionFromUI()
    {
        if (!IsVisible || participantRegistry == null || !participantRegistry.IsLocalExperimenter || sessionManager == null) return;
        if (sessionManager.StartNewSession()) Hide();
    }
}
