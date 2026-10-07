using TMPro;
using UnityEngine;

public class ExperimentModeUI : MonoBehaviour
{
    [SerializeField] private ExperimentManager experimentManager;
    [SerializeField] private TMP_Text modeText;
    [SerializeField] private ExperimentParticipantRegistry participantRegistry;
    [SerializeField] private Canvas canvas;
    // Lobby now owns Mode controls; the legacy canvas stays hidden in the production flow.
    private void Update() { if (canvas != null) canvas.enabled = false; }

    private void OnEnable()
    {
        if (experimentManager != null) experimentManager.OnModeChanged += OnModeChanged;
        UpdateModeText();
    }

    private void OnDisable()
    {
        if (experimentManager != null) experimentManager.OnModeChanged -= OnModeChanged;
    }

    private void OnModeChanged(ExperimentManager.ExperimentMode mode)
    {
        UpdateModeText();
    }

    public void SetNormalMode()
    {
        SetMode(ExperimentManager.ExperimentMode.Normal);
    }

    public void SetProposedMode()
    {
        SetMode(ExperimentManager.ExperimentMode.Proposed);
    }

    private void SetMode(ExperimentManager.ExperimentMode mode)
    {
        if (experimentManager == null)
        {
            Debug.LogWarning("[ExperimentModeUI] ExperimentManager is not assigned.", this);
            return;
        }

        experimentManager.SetMode(mode);
        // Keeps the current confirmed display for rejected/pending requests.
        UpdateModeText();
    }

    public void UpdateModeText()
    {
        if (modeText != null && experimentManager != null)
            modeText.text = $"Current Mode: {experimentManager.CurrentMode}";
    }
}
