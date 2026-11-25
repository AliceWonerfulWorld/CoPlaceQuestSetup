using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

// Scene-local PC adapter. Study identities stay off the network.
public sealed class ExperimentDataManagement
{
    readonly ExperimentSessionManager session;
    readonly ExperimentParticipantRegistry registry;
    readonly Func<string> root;
    readonly string run;
    ExperimentDataPlan draft=new ExperimentDataPlan();
    readonly Dictionary<string,ExperimentDataStore.Metadata> metadata=new Dictionary<string,ExperimentDataStore.Metadata>();
    readonly Dictionary<int,ExperimentDataStore.Binding> bindings=new Dictionary<int,ExperimentDataStore.Binding>();
    string bindingSession;
    public ExperimentDataManagement(ExperimentSessionManager s,ExperimentParticipantRegistry r,Func<string> path,string observerRunId=null){session=s;registry=r;root=path;run=observerRunId??Guid.NewGuid().ToString("N");}
    bool IsAdmin=>registry!=null && registry.IsLocalExperimenter && !Application.isMobilePlatform;
    public ExperimentDataPlan Draft=>draft.Copy();
    public ExperimentDataStore.Metadata Applied=>Get(session.CurrentSessionId)?.Copy();
    public string DraftSignature=>draft.Signature;
    public bool IsControlled=>draft.experimentType!="Test";
    public bool HasDraftChanges {get{try{return Get(session.CurrentSessionId)?.plan.Signature!=draft.Signature;}catch{return true;}}}
    public string LastError {get;private set;}="";
    public string BindingSummary
    {get{EnsureBindingSession();return string.Join("　",bindings.Values.Select(b=>b.participantId+" → 接続 "+b.clientNo+(registry.IsRegisteredParticipant(b.clientNo)&&b.deviceKey==DeviceKey(b.clientNo)?"（確認済み）":"（再確認が必要）")));}}
    public bool SetDraft(ExperimentDataPlan value)
    {
        if(!IsAdmin || session.IsResettingSession || value==null)return false;
        if(value.experimentType!="Test"&&value.experimentType!="Pilot"&&value.experimentType!="Main")return false;
        draft=value.Copy();
        if(IsControlled && draft.TryCondition(out var mode,out string set))session.SetDraft(mode,set,session.DraftExpectedParticipants);
        return true;
    }
    public bool CanSelect(ExperimentManager.ExperimentMode mode,string set)=>!IsControlled || draft.TryCondition(out var m,out string s)&&mode==m&&set==s;
    public string ValidatePreparation()
    {
        var error=draft.Validate(session.DraftExpectedParticipants,session.DraftMode,session.DraftCardSetId);
        if(error!="")return error;
        try{return new ExperimentDataStore(root()).ValidateCohort(draft);}catch{return "メタデータを確認できません。保存先を確認してください";}
    }
    internal void Prepare(string id,long epoch,string utc)
    {
        var m=new ExperimentDataStore.Metadata{sessionId=id,epoch=epoch,createdAtUtc=utc,condition=session.DraftMode.ToString(),cardSetId=session.DraftCardSetId,expectedParticipantCount=session.DraftExpectedParticipants,plan=draft.Copy(),appVersion=Application.version};
        m.relativePath=ExperimentDataStore.RelativePath(m);metadata[id]=m;
    }
    internal void PersistPreparation(string id){var m=Get(id);if(m==null)throw new InvalidOperationException("Missing prepared metadata");var store=new ExperimentDataStore(root());store.Save(m);store.RebuildIndex();}
    internal void CancelPreparation(string id)=>metadata.Remove(id);
    internal ExperimentDataStore.Metadata Get(string id)
    {
        if(string.IsNullOrEmpty(id))return null;
        if(metadata.TryGetValue(id,out var known))return known;
        var loaded=new ExperimentDataStore(root()).Find(id);if(loaded!=null)metadata[id]=loaded;return loaded;
    }
    public bool TryResolveParticipant(int client,out string participantId)
    {
        EnsureBindingSession();participantId="";
        if(!registry.IsRegisteredParticipant(client) || registry.Network==null || registry.Network.GetClientVariable(ExperimentParticipantRegistry.RoleVariable,client)!="Participant")return false;
        if(!bindings.TryGetValue(client,out var binding)||DeviceKey(client)==""||binding.deviceKey!=DeviceKey(client))return false;
        participantId=binding.participantId;return true;
    }
    public string StartBlockReason
    {
        get
        {
            try
            {
                var m=Get(session.CurrentSessionId);
                if(m==null)return "新規セッションの準備が必要です（実験情報がありません）";
                if(m.plan.Signature!=draft.Signature)return "次回の実験情報が未適用です。新規セッションを準備してください";
                if(m.plan.experimentType=="Test")return "";
                EnsureBindingSession();
                var active=registry.ParticipantClientNos.ToArray();
                foreach(int client in active)
                    if(!bindings.TryGetValue(client,out var b) || DeviceKey(client)=="" || b.deviceKey!=DeviceKey(client))return "参加者IDと接続端末の対応を確認してください";
                var wanted=new[]{m.plan.participant1Id,m.plan.participant2Id}.Where(x=>x!="").OrderBy(x=>x);
                if(!bindings.Where(p=>active.Contains(p.Key)).Select(p=>p.Value.participantId).OrderBy(x=>x).SequenceEqual(wanted))return "参加者IDの対応が不足または重複しています";
                return "";
            }
            catch{return "実験情報を読み取れません。保存先を確認してください";}
        }
    }
    public static string HashDevice(string value)
    {if(string.IsNullOrEmpty(value))return "";using(var hash=SHA256.Create())return string.Concat(hash.ComputeHash(Encoding.UTF8.GetBytes(value)).Select(b=>b.ToString("x2")));}
    public string DeviceKey(int client)=>HashDevice(registry.Network?.GetDeviceIdByClientNo(client));
    void EnsureBindingSession(){if(bindingSession!=session.CurrentSessionId){bindings.Clear();bindingSession=session.CurrentSessionId;}}
    public bool ConfirmBinding(string participantId,int client,string confirmationReason)
    {
        LastError="";
        try
        {
            if(!IsAdmin || session.IsResettingSession || session.Lobby==null)throw new InvalidOperationException("接続と準備完了を確認してください");
            var m=Get(session.CurrentSessionId);EnsureBindingSession();
            if(m==null || !new[]{m.plan.participant1Id,m.plan.participant2Id}.Contains(participantId) || !ExperimentDataPlan.ParticipantId(participantId))throw new InvalidOperationException("適用済みの参加者IDを選択してください");
            if(!registry.IsRegisteredParticipant(client) || registry.Network.GetClientVariable(ExperimentParticipantRegistry.RoleVariable,client)!="Participant" || DeviceKey(client)=="")throw new InvalidOperationException("参加者端末の接続・端末識別を確認してください");
            if(string.IsNullOrWhiteSpace(confirmationReason)||confirmationReason.Length>200)throw new InvalidOperationException("端末を確認した方法を入力してください");
            // One PID per current connection; re-confirmation is explicit, never inferred.
            if(bindings.Any(p=>p.Key!=client && registry.IsRegisteredParticipant(p.Key) && p.Value.participantId==participantId))throw new InvalidOperationException("同じ参加者IDが別の接続に登録されています");
            var b=new ExperimentDataStore.Binding{sessionId=m.sessionId,epoch=m.epoch,participantId=participantId,clientNo=client,deviceKey=DeviceKey(client),confirmedAtUtc=DateTime.UtcNow.ToString("o"),observerRunId=run,reason=confirmationReason};
            new ExperimentDataStore(root()).AppendBinding(m,b);bindings[client]=b;return true;
        }
        catch(Exception e){LastError=e.Message;return false;}
    }
    internal void RecordCohort(ExperimentDataStore.Metadata m,int[] clients)
    {
        if(!IsAdmin)return;
        EnsureBindingSession();
        var store=new ExperimentDataStore(root());
        foreach(int client in clients)
        {
            if(TryResolveParticipant(client,out _))continue;
            store.AppendBinding(m,new ExperimentDataStore.Binding{sessionId=m.sessionId,epoch=m.epoch,participantId="",clientNo=client,deviceKey=DeviceKey(client),confirmedAtUtc=DateTime.UtcNow.ToString("o"),observerRunId=run,role=registry.Network?.GetClientVariable(ExperimentParticipantRegistry.RoleVariable,client)??"unknown",reason="unassigned_at_start; do not infer participant identity"});
        }
    }
    public bool PrepareSecondDraft()
    {
        var m=Applied;if(!IsAdmin || m==null || m.status!="completed" || m.saveStatus!="complete" || m.plan.experimentType=="Test" || m.plan.trialOrder!=1)return false;
        var next=m.plan.Copy();next.trialOrder=2;if(!SetDraft(next))return false;next.TryCondition(out var mode,out string set);return session.SetDraft(mode,set,m.expectedParticipantCount);
    }
}
