#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

// Real preparation + Start, isolated preview scenes and GUID log roots.
public static class ParticipantVerificationSession
{
    private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    public static void Set(object o,string n,object v)=>o.GetType().GetField(n,Flags).SetValue(o,v);
    public static void Call(object o,string n)=>o.GetType().GetMethod(n,Flags)?.Invoke(o,null);
    public static ProposedRevealVerification.Endpoint Host(ProposedRevealVerification.Endpoint[] people, List<ProposedRevealVerification.Endpoint> owned, ExperimentManager.ExperimentMode mode=ExperimentManager.ExperimentMode.Proposed)
    {
        var host=ProposedRevealVerification.CreateEndpoint(99,people.Length,true); owned.Add(host);
        Set(host.registry,"localRole",ExperimentParticipantRegistry.ClientRole.Experimenter);
        foreach(var e in people.Concat(new[]{host})) { Set(e.manager,"lobby",e.lobby); Call(e.registry,"Update"); }
        Prepare(host,people,mode,"Development",true); return host;
    }
    public static void Prepare(ProposedRevealVerification.Endpoint host,ProposedRevealVerification.Endpoint[] people,ExperimentManager.ExperimentMode mode,string set,bool start)
    {
        var group=people.Concat(new[]{host}).ToArray(); Tick(group,2);
        if(!host.session.SetDraft(mode,set,people.Length) || !host.session.StartNewSession()) throw new Exception("Fixture preparation rejected: "+host.session.LastFailure);
        foreach(var peer in people) ProposedRevealVerification.Deliver(host,peer);
        Tick(group,16);
        if(group.Any(e=>e.session.IsResettingSession)) throw new Exception("Fixture reset did not finish");
        if(start) { if(!host.lobby.StartExperiment()) throw new Exception("Fixture Start rejected: "+host.lobby.StartBlockReason); foreach(var peer in people) ProposedRevealVerification.Deliver(host,peer); Tick(group,4); }
    }
    public static void Tick(ProposedRevealVerification.Endpoint[] group,int frames=1)
    {
        for(int frame=0;frame<frames;frame++) foreach(var e in group)
        {
            Set(e.session,"nextAttempt",0f); Call(e.registry,"Update");e.registry.RefreshParticipants();Call(e.session,"Update");Call(e.lobby,"Update");Call(e.manager,"Update");
            Call(e.placements,"Update");Call(e.agreement,"Update");Set(e.shared,"nextInitializationAttempt",0f);Call(e.shared,"Update");
            Call(e.submission,"Update");Call(e.privateBoard,"Update");Call(e.answers,"LateUpdate");Set(e.reveal,"nextAttempt",0f);Call(e.reveal,"LateUpdate");Call(e.agreement,"LateUpdate");
            foreach(var ui in e.components.OfType<ExperimentModeUI>()) Call(ui,"Update");
            foreach(var ui in e.components.OfType<IndependentAnswerSubmissionUI>()) Call(ui,"Update");
            foreach(var ui in e.components.OfType<FinalAgreementUI>()) Call(ui,"Update");
            foreach(var ui in e.components.OfType<ExperimentLobbyUI>()) Call(ui,"Update");
            foreach(var ui in e.components.OfType<ExperimentResultUI>()) ui.Refresh();
            foreach(var to in group) if(e!=to) ProposedRevealVerification.Deliver(e,to);
        }
    }
}
#endif
