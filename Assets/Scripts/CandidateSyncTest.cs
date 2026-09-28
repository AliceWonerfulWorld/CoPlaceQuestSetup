using UnityEngine;
using Styly.NetSync;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using TMPro;
using System.Collections.Generic;
using System;

public class CandidateSyncTest : MonoBehaviour
{
    private bool previousPrimaryButton = false;
    private bool previousSecondaryButton = false;

    [SerializeField]
    private TMP_Text candidateStatusText;

    [SerializeField]
    private CandidateMarkerManager candidateMarkerManager;

    private Dictionary<string, string> participantCandidates
        = new Dictionary<string, string>();

    private Dictionary<string, bool> participantAnswered
        = new Dictionary<string, bool>();

    private Dictionary<string, string> participantSelectedTimes
        = new Dictionary<string, string>();

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

    public void SendCandidate(string cardId, string tierId)
    {
        string selectedAt = DateTime.UtcNow.ToString("o");

        NetSyncManager.Instance.SetClientVariable(
            $"candidate_{cardId}",
            tierId
        );

        NetSyncManager.Instance.SetClientVariable(
            $"answered_{cardId}",
            "true"
        );

        NetSyncManager.Instance.SetClientVariable(
            $"selectedAt_{cardId}",
            selectedAt
        );

        Debug.Log(
            $"[Candidate Send] Client {NetSyncManager.Instance.ClientNo} / {cardId} / Tier {tierId} / Answered true / SelectedAt {selectedAt}"
        );
    }

    public void CancelCandidate(string cardId)
    {
        NetSyncManager.Instance.SetClientVariable(
            $"candidate_{cardId}",
            "Unclassified"
        );

        NetSyncManager.Instance.SetClientVariable(
            $"answered_{cardId}",
            "false"
        );

        NetSyncManager.Instance.SetClientVariable(
            $"selectedAt_{cardId}",
            ""
        );

        Debug.Log(
            $"[Candidate Cancel] Client {NetSyncManager.Instance.ClientNo} / {cardId} / Answered false"
        );
    }

    private void OnClientVariableChanged(
        int clientNo,
        string name,
        string oldValue,
        string newValue
    )
    {
       // 配置候補
       if (name.StartsWith("candidate_"))
       {
           string cardId = name.Replace("candidate_", "");
           string key = $"{clientNo}_{cardId}";

           participantCandidates[key] = newValue;

           Debug.Log(
               $"[Candidate Receive] Participant {clientNo} / {cardId} / {oldValue} -> {newValue}"
           );

           bool answered = false;

           if (participantAnswered.ContainsKey(key))
           {
               answered = participantAnswered[key];
           }

           if (candidateMarkerManager != null)
           {
               candidateMarkerManager.UpdateMarker(
                   clientNo,
                   cardId,
                   newValue,
                   answered
               );
           } 

           UpdateCandidateDisplay();
           return;
       }

       // 回答状態
       if (name.StartsWith("answered_"))
       {
           string cardId = name.Replace("answered_","");
           string key = $"{clientNo}_{cardId}";

           bool answered = newValue == "true";

           participantAnswered[key] = answered;

           if (candidateMarkerManager != null && 
               participantCandidates.ContainsKey(key))
            {
                candidateMarkerManager.UpdateMarker(
                    clientNo,
                    cardId,
                    participantCandidates[key],
                    answered
                );
            }

           Debug.Log(
                $"[Answered Receive] Participant {clientNo} / {cardId} / Answered: {answered}"
           );

           UpdateCandidateDisplay();
       }

       if (name.StartsWith("selectedAt_"))
       {
           string cardId = name.Replace("selectedAt_", "");
           string key = $"{clientNo}_{cardId}";

           participantSelectedTimes[key] = newValue;

           Debug.Log(
               $"[SelectedAt Receive] Participant {clientNo} / {cardId} / SelectedAt: {newValue}"
           );

           UpdateCandidateDisplay();
       }
    }

    private void UpdateCandidateDisplay()
    {
        if (candidateStatusText == null)
            return;

        string displayText = "";

        foreach (var candidate in participantCandidates)
        {
            string key = candidate.Key;
            string tier = candidate.Value;

            string[] parts = key.Split('_');

            if (parts.Length < 3)
                continue;
            
            string participantId = parts[0];
            string cardId = $"{parts[1]}_{parts[2]}";
            bool answered = false;

            if (participantAnswered.ContainsKey(key))
            {
                answered = participantAnswered[key];
            }

            string selectedAt = "-";

            if (participantSelectedTimes.ContainsKey(key) &&
                !string.IsNullOrEmpty(participantSelectedTimes[key]))
            {
                selectedAt = participantSelectedTimes[key];
            }

            displayText +=
                $"Participant {participantId} : {cardId} -> Tier {tier} / Answered: {answered} / SelectedAt: {selectedAt}\n";
        }

        candidateStatusText.text = displayText;
    }
}