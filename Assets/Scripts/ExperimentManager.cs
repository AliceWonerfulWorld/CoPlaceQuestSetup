using UnityEngine;

public class ExperimentManager : MonoBehaviour
{
   public enum ExperimentMode
   {
        Normal,
        Proposed
   }

   public enum ExperimentState
   {
        Idle,
        Answering,
        WaitingForAnswers,
        Revealed,
        Discussion,
        Confirmed
   }

   [Header("Experiment Settings")]
   [SerializeField]
   private ExperimentMode currentMode = ExperimentMode.Proposed;

   [SerializeField]
   private ExperimentState currentState = ExperimentState.Idle;

   [Header("Current Card")]
   [SerializeField]
   private string currentCardId = "Card_01";

   public ExperimentMode CurrentMode => currentMode;
   public ExperimentState CurrentState => currentState;
   public string CurrentCardId => currentCardId;

   private void Start()
   {
       Debug.Log(
           $"[ExperimentManager] Start / Mode: {currentMode} / State: {currentState} / Card: {currentCardId}"
       );
   }

   public void SetMode(ExperimentMode mode)
   {
        if (currentState != ExperimentState.Idle)
        {
            Debug.LogWarning(
                "[ExperimentManager] 実験中は方式を変更できません"
            );
            return;
        }

        currentMode = mode;

        Debug.Log(
            $"[ExperimentManager] Mode changed -> {currentMode}"
        );
   }

   public void StartCard(string cardId)
   {
        currentCardId = cardId;
        currentState = ExperimentState.Answering;

        Debug.Log(
            $"[ExperimentManager] Card Start / {currentCardId} / Mode: {currentMode}"
        );
   }

   public void SetState(ExperimentState newState)
   {
        currentState = newState;

        Debug.Log(
            $"[ExperimentManager] State changed -> {currentState}"
        );
   }

   public void ResetExperiment()
   {
      currentState = ExperimentState.Idle;
      currentCardId = "Card_01";

      Debug.Log(
          "[ExperimentManager] Experiment Reset"
      );
   }
}
