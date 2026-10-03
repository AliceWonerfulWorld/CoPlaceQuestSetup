using TMPro;
using UnityEngine;

public class ExperimentModeUI : MonoBehaviour
{
    [SerializeField] private ExperimentManager experimentManager;
    [SerializeField] private TMP_Text modeText;

    private void OnEnable()
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
        // Read the actual mode even when SetMode rejects a change outside Idle.
        UpdateModeText();
    }

    public void UpdateModeText()
    {
        if (modeText != null && experimentManager != null)
            modeText.text = $"Current Mode: {experimentManager.CurrentMode}";
    }
}
