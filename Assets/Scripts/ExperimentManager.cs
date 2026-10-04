using System;
using Styly.NetSync;
using UnityEngine;

public class ExperimentManager : MonoBehaviour
{
   private const string ModeVariable = "experimentMode";
   private const string StateVariable = "experimentState";

   [Header("Room Mode Sync")]
   [SerializeField] private NetSyncManager netSyncManager;
    [SerializeField] private ExperimentSessionManager sessionManager;
   [SerializeField] private bool canChangeMode = true;
   private NetSyncManager subscribedManager;
   private bool started;
   private bool networkWasReady;
   private bool initializationRequested;
   private float nextInitializationAttempt;
   private string publishedState;
   public event Action<ExperimentMode> OnModeChanged;
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
       started = true;
       Subscribe();
       Debug.Log(
           $"[ExperimentManager] Start / Mode: {currentMode} / State: {currentState} / Card: {currentCardId}"
       );
   }

   private void OnEnable()
   {
       if (started) Subscribe();
   }

   private void OnDisable()
   {
       if (subscribedManager != null)
       {
           subscribedManager.OnReady.RemoveListener(OnNetworkReady);
           subscribedManager.OnGlobalVariableChanged.RemoveListener(OnGlobalVariableChanged);
       }
       subscribedManager = null;
       networkWasReady = false;
       initializationRequested = false;
       publishedState = null;
   }

   private void Subscribe()
   {
       if (subscribedManager != null) return;
       subscribedManager = netSyncManager != null ? netSyncManager : NetSyncManager.Instance;
       if (subscribedManager == null) return;
       subscribedManager.OnReady.AddListener(OnNetworkReady);
       subscribedManager.OnGlobalVariableChanged.AddListener(OnGlobalVariableChanged);
       if (subscribedManager.IsReady) OnNetworkReady();
   }

   private void Update()
   {
       Subscribe(); // Instance lookup only; no per-frame scene search.
       bool ready = subscribedManager != null && subscribedManager.IsReady;
       if (!ready)
       {
           networkWasReady = false;
           initializationRequested = false;
           publishedState = null;
           return;
       }
       if (!networkWasReady) OnNetworkReady();
       PublishState();
       if (!initializationRequested && Time.unscaledTime >= nextInitializationAttempt)
       {
           nextInitializationAttempt = Time.unscaledTime + 1;
           if (SessionVariableTransport.GetGlobalVariable(subscribedManager, sessionManager, ModeVariable) == null) RestoreRoomMode();
           else initializationRequested = true;
       }
   }

   private void OnNetworkReady()
   {
       if (subscribedManager == null || !subscribedManager.IsReady) return;
       networkWasReady = true;
       initializationRequested = false;
       publishedState = null;
       RestoreRoomMode();
       PublishState();
   }

   private void RestoreRoomMode()
   {
       if (subscribedManager == null || !subscribedManager.IsReady) return;
       string value = SessionVariableTransport.GetGlobalVariable(subscribedManager, sessionManager, ModeVariable);
       if (value == null)
       {
           // A fixed initial Room default avoids conflicting Inspector defaults
           // on simultaneous first joins. Never overwrite an existing Room value.
           if (currentState == ExperimentState.Idle && RoomClientsAreIdle())
               initializationRequested = SendMode(ExperimentMode.Proposed);
           return;
       }
       initializationRequested = true;
       ApplyRoomMode(value, true);
   }

   private void PublishState()
   {
       if ((sessionManager != null && sessionManager.IsResettingSession) || subscribedManager == null || !subscribedManager.IsReady) return;
       string value = currentState.ToString();
       if (publishedState != value && SessionVariableTransport.SetClientVariable(subscribedManager, sessionManager, StateVariable, value))
           publishedState = value;
   }

   private bool RoomClientsAreIdle()
   {
       foreach (int client in subscribedManager.GetAliveClients(includeStealthClients: true))
       {
           if (client == subscribedManager.ClientNo) continue;
           // Unknown/new peers are fail-closed until their state arrives.
           if (SessionVariableTransport.GetClientVariable(subscribedManager, sessionManager, StateVariable, client) != ExperimentState.Idle.ToString())
               return false;
       }
       return true;
   }

   private bool SendMode(ExperimentMode mode)
   {
       bool accepted = SessionVariableTransport.SetGlobalVariable(subscribedManager, sessionManager, ModeVariable, mode.ToString());
       if (accepted)
           Debug.Log($"[Experiment Mode Send] Client {subscribedManager.ClientNo} / {mode}");
       else
           Debug.LogWarning("[ExperimentManager] Room Modeの送信に失敗しました", this);
       return accepted;
   }

   private void OnGlobalVariableChanged(string name, string oldValue, string newValue)
   {
       if (name != ModeVariable || subscribedManager == null || !subscribedManager.IsReady) return;
       ApplyRoomMode(newValue, false);
   }

   private void ApplyRoomMode(string value, bool restoring)
   {
       // Accept only the two protocol strings, not numeric Enum.TryParse values.
       ExperimentMode mode;
       if (value == "Normal") mode = ExperimentMode.Normal;
       else if (value == "Proposed") mode = ExperimentMode.Proposed;
       else
       {
           Debug.LogWarning($"[ExperimentManager] Invalid Room Mode: {value}", this);
           return;
       }
       if (currentState != ExperimentState.Idle && mode != currentMode)
       {
           // No rollback write: competing clients could otherwise create a loop.
           // Resume from the Room value on returning to Idle.
           Debug.LogWarning($"[ExperimentManager] 実験中のRoom Mode変更を拒否: {currentMode} -> {mode}. Idleに戻ると再取得します", this);
           return;
       }
       if (restoring) Debug.Log($"[Experiment Mode Restore] {mode}");
       if (currentMode == mode) return;
       if (!restoring) Debug.Log($"[Experiment Mode Receive] {currentMode} -> {mode}");
       currentMode = mode;
       Debug.Log($"[Experiment Mode Apply] CurrentMode = {currentMode}");
       OnModeChanged?.Invoke(currentMode);
   }

   public void SetMode(ExperimentMode mode)
   {
        if (sessionManager != null && sessionManager.IsResettingSession) return;
        if (currentState != ExperimentState.Idle)
        {
            Debug.LogWarning(
                "[ExperimentManager] 実験中は方式を変更できません"
            );
            return;
        }

        if (!canChangeMode || !isActiveAndEnabled || !Enum.IsDefined(typeof(ExperimentMode), mode)) return;
        Subscribe();
        if (subscribedManager == null || !subscribedManager.IsReady)
        {
            Debug.LogWarning("[ExperimentManager] NetSync準備前は方式を変更できません", this);
            return;
        }
        if (!RoomClientsAreIdle())
        {
            Debug.LogWarning("[ExperimentManager] 接続中の参加者がIdleではない、または状態未取得のため方式変更を拒否しました", this);
            return;
        }
        if (SessionVariableTransport.GetGlobalVariable(subscribedManager, sessionManager, ModeVariable) == mode.ToString()) return;
        if (SendMode(mode)) initializationRequested = true;
        // CurrentMode changes only through Room receive/restore.
   }

   public void StartCard(string cardId)
   {
        if (sessionManager != null && sessionManager.IsResettingSession) return;
        currentCardId = cardId;
        currentState = ExperimentState.Answering;
        PublishState();

        Debug.Log(
            $"[ExperimentManager] Card Start / {currentCardId} / Mode: {currentMode}"
        );
   }

   public void SetState(ExperimentState newState)
   {
        if (sessionManager != null && sessionManager.IsResettingSession && newState != ExperimentState.Idle) return;
        currentState = newState;
        if (currentState == ExperimentState.Idle) RestoreRoomMode();
        PublishState();

        Debug.Log(
            $"[ExperimentManager] State changed -> {currentState}"
        );
   }

   public void RestoreSessionState()
   {
       string value = subscribedManager != null && subscribedManager.IsReady ? SessionVariableTransport.GetClientVariable(subscribedManager, sessionManager, StateVariable) : null;
       if (Enum.TryParse(value, out ExperimentState restored) && Enum.IsDefined(typeof(ExperimentState), restored)) currentState = restored;
       publishedState = null; PublishState();
   }
   public void ResetExperiment()
   {
      publishedState = null;
      currentState = ExperimentState.Idle;
      currentCardId = "Card_01";
      RestoreRoomMode();
      PublishState();

      Debug.Log(
          "[ExperimentManager] Experiment Reset"
      );
   }
}
