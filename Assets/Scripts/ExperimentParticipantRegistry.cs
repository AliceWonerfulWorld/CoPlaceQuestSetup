using System;
using System.Collections.Generic;
using Styly.NetSync;
using UnityEngine;

// Explicit roles, not connected-client count, determine the answering cohort.
public class ExperimentParticipantRegistry : MonoBehaviour
{
    public enum ClientRole { AutoByPlatform, Participant, Experimenter }
    public const string RoleVariable = "experimentRole";
    public const string ExpectedCountVariable = "experimentExpectedParticipantCount";
    [SerializeField] private NetSyncManager netSyncManager;
    [Header("Production Role / Development Override")]
    [SerializeField, Tooltip("Production default: AutoByPlatform (Editor = Experimenter, Player/Quest = Participant). Explicit Participant/Experimenter values are development overrides; restore Auto before production builds.")]
    private ClientRole localRole = ClientRole.AutoByPlatform;
    [SerializeField, Min(1)] private int expectedParticipantCount = 2;

    private readonly List<int> participants = new List<int>();
    private NetSyncManager subscribedManager;
    private string publishedRole;
    private string publishedExpectedCount;
    private float nextRefresh;
    public event Action OnParticipantsChanged;
    public IReadOnlyList<int> ParticipantClientNos => participants;
    public int ExpectedParticipantCount
    {
        get
        {
            var net = Network;
            if (net != null && net.IsReady)
            {
                int authority = int.MaxValue;
                foreach (int client in net.GetAliveClients(includeStealthClients: true))
                    if (net.GetClientVariable(RoleVariable, client) == ClientRole.Experimenter.ToString())
                        authority = Math.Min(authority, client);
                if (IsLocalExperimenter && net.ClientNo > 0) authority = Math.Min(authority, net.ClientNo);
                if (authority == net.ClientNo && IsLocalExperimenter) return Mathf.Max(1, expectedParticipantCount);
                if (authority != int.MaxValue && int.TryParse(net.GetClientVariable(ExpectedCountVariable, authority), out int count) && count > 0)
                    return count;
            }
            return Mathf.Max(1, expectedParticipantCount);
        }
    }
    public NetSyncManager Network => netSyncManager != null ? netSyncManager : NetSyncManager.Instance;
    public bool IsLocalParticipant => EffectiveRole == ClientRole.Participant;
    public bool IsLocalExperimenter => EffectiveRole == ClientRole.Experimenter;
    public bool IsRoleOverridden => localRole != ClientRole.AutoByPlatform;
    public bool CanUseEditorParticipantInput => Application.isEditor && localRole == ClientRole.Participant;
    public ClientRole EffectiveRole => localRole == ClientRole.AutoByPlatform
        ? (Application.isEditor ? ClientRole.Experimenter : ClientRole.Participant) : localRole;

    private ClientRole? lastReportedRole;
    private void ReportRole()
    {
        if (lastReportedRole == localRole) return;
        lastReportedRole = localRole;
        if (IsRoleOverridden) Debug.LogWarning($"[Development Role Override] {localRole} / Effective: {EffectiveRole}. Production default is AutoByPlatform.", this);
        else Debug.Log($"[Production Auto Role] {EffectiveRole}", this);
    }
    // Shared answering permission; phase-specific guards remain with each controller.
    public bool CanOperateInSession(ExperimentSessionManager session)
    {
        var net = Network;
        return IsLocalParticipant && net != null && net.IsReady && net.ClientNo > 0 &&
            IsRegisteredParticipant(net.ClientNo) && session != null && !session.IsResettingSession &&
            !string.IsNullOrEmpty(session.CurrentSessionId) && !string.IsNullOrEmpty(session.SessionStartedAtUtc);
    }
    private void Start() { ReportRole(); Subscribe(); }
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
        publishedExpectedCount = null;
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
        publishedExpectedCount = null;
        PublishRole();
        PublishExpectedCount();
        RefreshParticipants();
    }

    private void PublishExpectedCount()
    {
        if (subscribedManager == null || !subscribedManager.IsReady || !IsLocalExperimenter) return;
        string count = Mathf.Max(1, expectedParticipantCount).ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (publishedExpectedCount != count && subscribedManager.SetClientVariable(ExpectedCountVariable, count))
            publishedExpectedCount = count;
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
        ReportRole();
        Subscribe();
        if (subscribedManager == null || !subscribedManager.IsReady) { publishedRole = publishedExpectedCount = null; return; }
        PublishRole();
        PublishExpectedCount();
        if (Time.unscaledTime >= nextRefresh)
        {
            nextRefresh = Time.unscaledTime + 1;
            RefreshParticipants(); // Covers disconnects without a per-frame scene search.
        }
    }

    private void OnVariableChanged(int client, string name, string oldValue, string newValue)
    {
        if (name == RoleVariable) RefreshParticipants();
        if (name == ExpectedCountVariable) OnParticipantsChanged?.Invoke();
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
