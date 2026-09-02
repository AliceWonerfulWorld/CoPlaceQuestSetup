using UnityEngine;
using Styly.NetSync;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using TMPro;
using System.Collections.Generic;

public class CandidateSyncTest : MonoBehaviour
{
    private bool previousPrimaryButton = false;
    private bool previousSecondaryButton = false;

    [SerializeField]
    private TMP_Text candidateStatusText;

    private Dictionary<int, string> participantCandidates
        = new Dictionary<int, string>();

    private void Start()
    {
        Debug.Log("[CandidateSyncTest] START");

        NetSyncManager.Instance.OnClientVariableChanged.AddListener(
            OnClientVariableChanged
        );
    }

    private void OnDestroy()
    {
        if (NetSyncManager.Instance != null)
        {
            NetSyncManager.Instance.OnClientVariableChanged.RemoveListener(
                OnClientVariableChanged
            );
        }
    }

    private void Update()
    {
        // Editor用
        if (Keyboard.current != null)
        {
            if (Keyboard.current.aKey.wasPressedThisFrame)
            {
                Debug.Log("[INPUT] Keyboard A pressed");
                SendCandidate("Card_01", "A");
            }

            if (Keyboard.current.cKey.wasPressedThisFrame)
            {
                Debug.Log("[INPUT] Keyboard C pressed");
                SendCandidate("Card_01", "C");
            }
        }

        // Quest右コントローラー用
        var rightController = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);

        if (!rightController.isValid)
            return;

        bool primaryButton;
        if (rightController.TryGetFeatureValue(
            UnityEngine.XR.CommonUsages.primaryButton,
            out primaryButton))
        {
            if (primaryButton && !previousPrimaryButton)
            {
                Debug.Log("[INPUT] Quest A pressed");
                SendCandidate("Card_01", "A");
            }

            previousPrimaryButton = primaryButton;
        }

        bool secondaryButton;
        if (rightController.TryGetFeatureValue(
            UnityEngine.XR.CommonUsages.secondaryButton,
            out secondaryButton))
        {
            if (secondaryButton && !previousSecondaryButton)
            {
                Debug.Log("[INPUT] Quest B pressed");
                SendCandidate("Card_01", "C");
            }

            previousSecondaryButton = secondaryButton;
        }
    }

    private void SendCandidate(string cardId, string tierId)
    {
        NetSyncManager.Instance.SetClientVariable(
            $"candidate_{cardId}",
            tierId
        );

        Debug.Log(
            $"[Candidate Send] Client {NetSyncManager.Instance.ClientNo} / {cardId} / Tier {tierId}"
        );
    }

    private void OnClientVariableChanged(
        int clientNo,
        string name,
        string oldValue,
        string newValue
    )
    {
        if (!name.StartsWith("candidate_"))
            return;

        string cardId = name.Replace("candidate_", "");

        Debug.Log(
            $"[Candidate Receive] Participant {clientNo} / {cardId} / {oldValue} -> {newValue}"
        );

        participantCandidates[clientNo] =
            $"{cardId} → Tier {newValue}";

        UpdateCandidateDisplay();
    }

    private void UpdateCandidateDisplay()
    {
        if (candidateStatusText == null)
            return;

        string displayText = "";

        foreach (var candidate in participantCandidates)
        {
            displayText +=
                $"Participant {candidate.Key} : {candidate.Value}\n";
        }

        candidateStatusText.text = displayText;
    }
}