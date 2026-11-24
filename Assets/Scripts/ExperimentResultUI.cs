using TMPro;
using UnityEngine;

// Presentation only: the existing Main board and confirmed snapshot remain the result.
[DefaultExecutionOrder(400)]
public class ExperimentResultUI : MonoBehaviour
{
    public const string Title = "分類作業が終了しました";
    public const string Message = "分類結果が確定しました。\nお疲れさまでした。";
    public const string Waiting = "実験者からの次の案内をお待ちください。";
    [SerializeField] private ExperimentManager experimentManager;
    [SerializeField] private ExperimentSessionManager sessionManager;
    [SerializeField] private ExperimentParticipantRegistry participantRegistry;
    [SerializeField] private GameObject contentRoot;
    [SerializeField] private GameObject experimenterControls;
    [SerializeField] private TMP_Text sessionInfo;
    [SerializeField] private UnityEngine.UI.Button newSessionButton;
    public bool IsVisible => contentRoot != null && contentRoot.activeSelf;

    private bool presentationConfigured;
    private Camera participantCamera;
    private TMP_Text taskHint;
    private bool hintWasEnabled;

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
    private void Hide() { if (contentRoot != null) contentRoot.SetActive(false); SetTaskHintVisible(true); }
    private void LateUpdate() => Refresh();
    public void Refresh()
    {
        ParticipantUIStyle.Apply(transform);
        bool visible = (participantRegistry == null || !participantRegistry.IsLocalExperimenter) && experimentManager != null &&
            experimentManager.CurrentState == ExperimentManager.ExperimentState.Confirmed &&
            (sessionManager == null || !sessionManager.IsResettingSession);
        if (contentRoot == null) return;
        SetTaskHintVisible(!visible);
        if (visible)
        {
            ConfigurePresentation();
            if (!contentRoot.activeSelf) PlaceInView();
        }
        if (contentRoot.activeSelf != visible) contentRoot.SetActive(visible);
        if (!visible) return;
        if (experimenterControls != null) experimenterControls.SetActive(false);
    }
    // Reuse the existing result Canvas. A single world-space placement on entry,
    // never a head-following HUD and never an additional completion condition.
    public void ConfigurePresentation()
    {
        if (presentationConfigured || contentRoot == null) return;
        presentationConfigured = true;
        var panel = contentRoot.GetComponent<RectTransform>();
        panel.sizeDelta = new Vector2(1400,550);
        foreach (var name in new[] { "Border", "Surface" })
        {
            var child = panel.Find(name) as RectTransform;
            if (child != null) child.sizeDelta = panel.sizeDelta - Vector2.one * (name == "Surface" ? 3 : 0);
        }
        SetLabel("Title",Title,170,130,66);
        SetLabel("ConfirmedMessage",Message,15,150,44);
        SetLabel("WaitingMessage",Waiting,-175,130,38);
        var canvas = contentRoot.GetComponent<Canvas>();
        if (canvas != null) canvas.sortingOrder = 200;
    }
    private void SetTaskHintVisible(bool visible)
    {
        if (taskHint == null && transform.parent != null)
            foreach (var label in transform.parent.GetComponentsInChildren<TMP_Text>(true))
                if (label.name == "Hint" && label.transform.parent.name == "TierBoardCanvas")
                { taskHint = label; hintWasEnabled = label.enabled; break; }
        if (taskHint != null) taskHint.enabled = visible && hintWasEnabled;
    }
    private void SetLabel(string name,string text,float y,float height,float size)
    {
        var child = contentRoot.transform.Find(name);
        var label = child != null ? child.GetComponent<TMP_Text>() : null;
        if (label == null) return;
        label.text = text; label.fontSize = size; label.enableAutoSizing = false;
        label.alignment = TextAlignmentOptions.Center; label.textWrappingMode = TextWrappingModes.Normal;
        label.rectTransform.anchoredPosition = new Vector2(0,y);
        label.rectTransform.sizeDelta = new Vector2(1260,height);
    }
    private void PlaceInView()
    {
        if (participantCamera == null)
            foreach (var root in gameObject.scene.GetRootGameObjects())
                foreach (var camera in root.GetComponentsInChildren<Camera>(true))
                    if (camera.CompareTag("MainCamera")) participantCamera = camera;
        if (participantCamera == null) return; // Existing board-relative placement is the fallback.
        var forward = participantCamera.transform.forward.normalized;
        if (forward.sqrMagnitude < .01f) forward = Vector3.forward;
        contentRoot.transform.SetPositionAndRotation(participantCamera.transform.position + forward * 1.4f,
            Quaternion.LookRotation(forward,Vector3.up));
    }
    public void NewSessionFromUI()
    {
        if (!IsVisible || participantRegistry == null || !participantRegistry.IsLocalExperimenter || sessionManager == null) return;
        sessionManager.RequestResetFromUI();
    }
}
