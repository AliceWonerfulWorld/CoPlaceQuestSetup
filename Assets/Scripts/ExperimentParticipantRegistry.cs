using System;
using System.Collections.Generic;
using Styly.NetSync;
using UnityEngine;

// Explicit roles, not connected-client count, determine the answering cohort.
public class ExperimentParticipantRegistry : MonoBehaviour
{
    public enum ClientRole { AutoByPlatform, Participant, Experimenter }
    public const string RoleVariable = "experimentRole";
    [SerializeField] private NetSyncManager netSyncManager;
    [SerializeField, Tooltip("Auto: Editor = Experimenter, Player/Quest = Participant. Use Participant for Editor participant tests.")]
    private ClientRole localRole = ClientRole.AutoByPlatform;
    [SerializeField, Min(1)] private int expectedParticipantCount = 2;

    private readonly List<int> participants = new List<int>();
    private NetSyncManager subscribedManager;
    private string publishedRole;
    private float nextRefresh;
    public event Action OnParticipantsChanged;
    public IReadOnlyList<int> ParticipantClientNos => participants;
    public int ExpectedParticipantCount => Mathf.Max(1, expectedParticipantCount);
    public NetSyncManager Network => netSyncManager != null ? netSyncManager : NetSyncManager.Instance;
    public bool IsLocalParticipant => EffectiveRole == ClientRole.Participant;
    public bool IsLocalExperimenter => EffectiveRole == ClientRole.Experimenter;
    private ClientRole EffectiveRole => localRole == ClientRole.AutoByPlatform
        ? (Application.isEditor ? ClientRole.Experimenter : ClientRole.Participant) : localRole;

    private void Start() { Subscribe(); }
    private void OnEnable() { Subscribe(); }
    private void OnDisable()
    {
        if (subscribedManager != null)
        {
            subscribedManager.OnReady.RemoveListener(OnReady);
            subscribedManager.OnClientVariableChanged.RemoveListener(OnVariableChanged);
        }
        subscribedManager = null;
        publishedRole = null;
    }

    private void Subscribe()
    {
        if (subscribedManager != null) return;
        subscribedManager = Network;
        if (subscribedManager == null) return;
        subscribedManager.OnReady.AddListener(OnReady);
        subscribedManager.OnClientVariableChanged.AddListener(OnVariableChanged);
        if (subscribedManager.IsReady) OnReady();
    }

    private void OnReady()
    {
        publishedRole = null;
        PublishRole();
        RefreshParticipants();
    }

    private void PublishRole()
    {
        if (subscribedManager == null || !subscribedManager.IsReady) return;
        string role = EffectiveRole.ToString();
        if (publishedRole != role && subscribedManager.SetClientVariable(RoleVariable, role))
            publishedRole = role;
    }

    private void Update()
    {
        Subscribe();
        if (subscribedManager == null || !subscribedManager.IsReady) { publishedRole = null; return; }
        PublishRole();
        if (Time.unscaledTime >= nextRefresh)
        {
            nextRefresh = Time.unscaledTime + 1;
            RefreshParticipants(); // Covers disconnects without a per-frame scene search.
        }
    }

    private void OnVariableChanged(int client, string name, string oldValue, string newValue)
    {
        if (name == RoleVariable) RefreshParticipants();
    }

    public void RefreshParticipants()
    {
        var net = Network;
        if (net == null || !net.IsReady) return;
        var clients = new HashSet<int>(net.GetAliveClients(includeStealthClients: true));
        if (net.ClientNo > 0) clients.Add(net.ClientNo);
        var next = new List<int>();
        foreach (int client in clients)
            if (net.GetClientVariable(RoleVariable, client) == ClientRole.Participant.ToString()) next.Add(client);
        next.Sort();
        bool changed = next.Count != participants.Count;
        for (int i = 0; !changed && i < next.Count; i++) changed = next[i] != participants[i];
        if (!changed) return;
        participants.Clear();
        participants.AddRange(next);
        if (participants.Count > ExpectedParticipantCount)
            Debug.LogWarning($"[Participants] {participants.Count} registered, expected {ExpectedParticipantCount}. Reveal remains blocked until roles are corrected.", this);
        OnParticipantsChanged?.Invoke();
    }

    public bool IsRegisteredParticipant(int clientNo) => participants.Contains(clientNo);
}
