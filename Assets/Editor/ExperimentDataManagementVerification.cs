#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class ExperimentDataManagementVerification
{
    [Serializable] class Report {public bool passed;public List<string> checks=new List<string>();public string failure="",root;public string environment="Unity Editor offline preview / isolated GUID logs; NOT Quest hardware";}
    public static string LastReport {get;private set;}
    [MenuItem("Tools/Experiment/Verify Experiment Data Management")]
    public static void Run()
    {
        var r=new Report{root=Path.GetFullPath("Library/ExperimentDataManagementVerification/"+Guid.NewGuid().ToString("N"))};Directory.CreateDirectory(r.root);LastReport=Path.Combine(r.root,"result.json");
        void Check(bool yes,string name){if(!yes)throw new Exception(name);r.checks.Add(name);}
        void Reject(Action action,string name){bool threw=false;try{action();}catch{threw=true;}Check(threw,name);}
        var owned=new List<ProposedRevealVerification.Endpoint>();
        try
        {
            foreach(string group in new[]{"G1","G2","G3","G4"})foreach(int order in new[]{1,2})
            {
                var p=new ExperimentDataPlan{experimentType="Main",pairId="001",participant1Id="P001",participant2Id="P002",allocationGroup=group,trialOrder=order};
                Check(p.TryCondition(out var mode,out string set),group+" order "+order+" resolves");
                var wanted=new Dictionary<string,string[]>{{"G1",new[]{"Normal/A","Proposed/B"}},{"G2",new[]{"Proposed/A","Normal/B"}},{"G3",new[]{"Normal/B","Proposed/A"}},{"G4",new[]{"Proposed/B","Normal/A"}}};
                Check(mode+"/"+set==wanted[group][order-1],group+" exact allocation "+order);
                Check(p.Validate(2,mode,set)=="",group+" valid anonymous identifiers");
                Check(p.Validate(2,mode,set=="A"?"B":"A")!="",group+" rejects wrong card set");
            }
            var invalid=new ExperimentDataPlan{experimentType="Main",pairId="../x",participant1Id="P001",participant2Id="P002",allocationGroup="G1"};
            Check(invalid.Validate(2,ExperimentManager.ExperimentMode.Normal,"A")!="","Pair traversal rejected");invalid.pairId="001";invalid.participant2Id="P001";
            Check(invalid.Validate(2,ExperimentManager.ExperimentMode.Normal,"A")!="","Duplicate PID rejected");invalid.participant2Id="Alice";
            Check(invalid.Validate(2,ExperimentManager.ExperimentMode.Normal,"A")!="","Name instead of anonymous PID rejected");
            invalid.pairId="pair001";invalid.participant2Id="P002";Check(invalid.Validate(2,ExperimentManager.ExperimentMode.Normal,"A")!="","case-only Pair aliases rejected before directory creation");
            var data=new ExperimentDataStore(r.root);var old=Path.Combine(r.root,"old_session");Directory.CreateDirectory(old);File.WriteAllText(Path.Combine(old,"phase_events.csv"),"legacy untouched");var bytes=File.ReadAllBytes(Path.Combine(old,"phase_events.csv"));
            var all=new List<ExperimentDataStore.Metadata>();
            foreach(string type in new[]{"Test","Pilot","Main"})foreach(int trial in new[]{1,2})
            {
                var plan=new ExperimentDataPlan{experimentType=type,pairId="001",participant1Id="P001",participant2Id="P002",allocationGroup="G1",trialOrder=trial,notes="quoted \"note\", line\nsecond line"};plan.TryCondition(out var mode,out string set);
                var m=new ExperimentDataStore.Metadata{sessionId=Guid.NewGuid().ToString("N"),epoch=trial,condition=mode.ToString(),cardSetId=set,expectedParticipantCount=2,plan=plan,createdAtUtc=DateTime.UtcNow.ToString("o"),appVersion="verification"};m.relativePath=ExperimentDataStore.RelativePath(m);all.Add(m);data.Save(m);
                var store=new ExperimentCsvLogStore(r.root);double clock=1;
                ExperimentCsvLogStore.Observation Event(string kind)=>new ExperimentCsvLogStore.Observation{SessionId=m.sessionId,Epoch=m.epoch,Condition=kind=="EXPERIMENT_STARTED"?m.condition:"",CardSetId=m.cardSetId,EventId=m.sessionId+":"+kind,EventType=kind,ObservedAtUtc=DateTimeOffset.UtcNow,MonotonicSeconds=clock++,Metadata=m,ParticipantsAtStart=kind=="EXPERIMENT_STARTED"?"2":""};
                store.Record(Event("SESSION_CREATED"));var start=Event("EXPERIMENT_STARTED");store.Record(start);store.Record(start);
                Check(store.GetSessionDirectory(m.sessionId).StartsWith(Path.Combine(r.root,type)+Path.DirectorySeparatorChar),type+" separated output");
                Check(m.actualParticipantCount==1 && m.participantsAtStart.SequenceEqual(new[]{2}),type+" ClientNo 2 means one actual participant");
                var final=Event("BOARD_CONFIRMED");final.Condition=m.condition;final.FinalPlacements=new[]{new ExperimentCsvLogStore.FinalPlacement{CardId=set+"01",Tier="C"}};
                if(mode==ExperimentManager.ExperimentMode.Proposed)
                {
                    var reveal=Event("ANSWERS_REVEALED");reveal.Condition="Proposed";reveal.SnapshotId="snapshot";reveal.IndependentAnswers=new[]{new ExperimentCsvLogStore.IndependentAnswer{ClientId=2,CardId=set+"01",Tier="E",SelectedAt=DateTime.UtcNow.ToString("o"),SubmissionId="submission"}};store.Record(reveal);
                }
                store.Record(final);
                Check(m.status=="completed"&&m.saveStatus=="complete",type+" finalized and fully saved");
                Check(ExperimentCsvLogStore.ReadCsv(Path.Combine(data.DirectoryFor(m),"final_placements.csv"))[1][6]=="C",type+" internal Tier / CSV unchanged");
                var frozen=m.Copy();frozen.plan.notes="mutated";Reject(()=>data.Save(frozen),type+" Applied metadata immutable");
                var retry=m.Copy();retry.sessionId=Guid.NewGuid().ToString("N");retry.relativePath=ExperimentDataStore.RelativePath(retry);data.Save(retry);
                Check(retry.relativePath!=m.relativePath && File.Exists(Path.Combine(data.DirectoryFor(m),"final_placements.csv")),type+" repeated pair/order never overwrites");
                var binding=new ExperimentDataStore.Binding{sessionId=m.sessionId,epoch=m.epoch,participantId="P001",clientNo=2,deviceKey=ExperimentDataManagement.HashDevice("device-one"),confirmedAtUtc=DateTime.UtcNow.ToString("o"),observerRunId="verification",reason="operator confirmed only connected headset"};data.AppendBinding(m,binding);binding.clientNo=7;binding.confirmedAtUtc=DateTime.UtcNow.ToString("o");data.AppendBinding(m,binding);
                var mappings=ExperimentCsvLogStore.ReadCsv(Path.Combine(data.DirectoryFor(m),"participant_bindings.csv"));Check(mappings.Count==3 && mappings[1][3]=="P001" && mappings[2][4]=="7",type+" explicit reconnect mapping history joins client IDs");
            }
            Check(data.RebuildIndex(),"index rebuild succeeds");var index=ExperimentCsvLogStore.ReadCsv(Path.Combine(r.root,"session_index.csv"));Check(index.Count==13 && index.Skip(1).Select(row=>row[1]).Distinct().Count()==12,"session index deduplicates Session IDs; retains retries");
            Check(index[0].SequenceEqual(ExperimentDataStore.IndexColumns)&&index.Skip(1).All(row=>row[21].Contains("\"note\", line\nsecond")),"complete CSV schema and quoted notes");
            File.WriteAllText(Path.Combine(r.root,"session_index.csv"),"broken index");Check(data.RebuildIndex()&&ExperimentCsvLogStore.ReadCsv(Path.Combine(r.root,"session_index.csv")).Count==13,"corrupt index rebuilt from metadata");
            File.Delete(Path.Combine(r.root,"session_index.csv"));Check(data.RebuildIndex(),"missing index rebuilt");
            var corruptPath=Path.Combine(data.DirectoryFor(all[0]),"session_metadata.json");var primary=File.ReadAllBytes(corruptPath);var backup=File.ReadAllBytes(corruptPath+".bak");
            File.WriteAllText(corruptPath,"broken metadata");Check(data.Find(all[0].sessionId).plan.Signature==all[0].plan.Signature && data.RebuildIndex(),"metadata backup restores identity when primary is corrupt");
            var intactIndex=File.ReadAllBytes(Path.Combine(r.root,"session_index.csv"));File.WriteAllText(corruptPath+".bak","broken backup");
            Check(!data.RebuildIndex() && File.ReadAllBytes(Path.Combine(r.root,"session_index.csv")).SequenceEqual(intactIndex),"unrecoverable metadata never replaces index with partial results");
            File.WriteAllBytes(corruptPath,primary);File.WriteAllBytes(corruptPath+".bak",backup);Check(data.RebuildIndex(),"metadata repair restores index rebuild");
            var cohort=new ExperimentDataPlan{experimentType="Main",pairId="002",participant1Id="P001",participant2Id="P002",allocationGroup="G1"};Check(data.ValidateCohort(cohort)!="","PID reuse across another pair rejected");cohort.pairId="001";cohort.allocationGroup="G4";Check(data.ValidateCohort(cohort)!="","same pair changed group rejected");
            Check(File.ReadAllBytes(Path.Combine(old,"phase_events.csv")).SequenceEqual(bytes) && !index.Skip(1).Any(row=>row[1]=="old_session"),"legacy folder unchanged and never classified as Main");
            var interrupted=all[0].Copy();interrupted.sessionId=Guid.NewGuid().ToString("N");interrupted.relativePath=ExperimentDataStore.RelativePath(interrupted);interrupted.status="prepared";interrupted.startedAtUtc=interrupted.finishedAtUtc="";interrupted.saveStatus="pending";data.Save(interrupted);
            var abortStore=new ExperimentCsvLogStore(r.root);abortStore.Record(new ExperimentCsvLogStore.Observation{Metadata=interrupted,SessionId=interrupted.sessionId,Epoch=interrupted.epoch,EventId="abort",EventType="SESSION_ABORTED",CardSetId=interrupted.cardSetId,ObservedAtUtc=DateTimeOffset.UtcNow,MonotonicSeconds=1});Check(interrupted.status=="aborted"&&interrupted.finishedAtUtc!="","aborted status is distinct from completed");
            var bad=interrupted.Copy();bad.relativePath="../escape";Reject(()=>data.Save(bad),"tampered relative path rejected");
            var indexPath=Path.Combine(r.root,"session_index.csv");var savedIndex=File.ReadAllBytes(indexPath);File.Delete(indexPath);Directory.CreateDirectory(indexPath);Check(!data.RebuildIndex(),"index IO failure reported without deleting session data");Directory.Delete(indexPath);Check(data.RebuildIndex(),"index IO recovery succeeds");
            // Exercise real preparation, Role exclusion and direct Start guards.
            var people=new[]{ProposedRevealVerification.CreateEndpoint(1,1,true)};owned.AddRange(people);var host=ParticipantVerificationSession.Host(people,owned,ExperimentManager.ExperimentMode.Normal);
            var logger=host.components.OfType<ExperimentLogger>().Single();var service=logger.Data;
            Check(service.Draft.experimentType=="Test","fresh logger defaults to Test");
            Check(logger.SessionDirectory(host.session.CurrentSessionId).Contains(Path.DirectorySeparatorChar+"Test"+Path.DirectorySeparatorChar),"real Reset freezes new Test output path");
            var peerLogger=people[0].components.OfType<ExperimentLogger>().Single();
            Check(!peerLogger.Data.SetDraft(new ExperimentDataPlan{experimentType="Main"}) && !peerLogger.RebuildIndex() && !peerLogger.Data.ConfirmBinding("P001",1,"not authorized"),"Participant cannot edit metadata / write index / bind identities");
            var actual=service.Applied;Check(actual.actualParticipantCount==1&&actual.participantsAtStart.SequenceEqual(new[]{1})&&!actual.participantsAtStart.Contains(99),"actual start cohort excludes experimenter ClientNo 99");
            var before=service.Applied;var draft=service.Draft;draft.experimentType="Main";draft.pairId="010";draft.participant1Id="P010";draft.allocationGroup="G1";draft.trialOrder=1;service.SetDraft(draft);
            Check(service.Applied.plan.experimentType=="Test"&&before.relativePath==service.Applied.relativePath,"Draft edits never mutate current trial / path");
            Check(host.session.DraftMode==ExperimentManager.ExperimentMode.Normal && host.session.DraftCardSetId=="A"&&!host.session.SetDraft(ExperimentManager.ExperimentMode.Proposed,"B",1),"Main allocation prevents free mode / set API");
            ParticipantVerificationSession.Prepare(host,people,ExperimentManager.ExperimentMode.Normal,"A",false);
            Check(!host.lobby.StartExperiment()&&host.lobby.StartBlockReason.Contains("対応"),"direct Start rejects unconfirmed participant identity");
            Check(!service.ConfirmBinding("P010",99,"operator checked")&&!service.ConfirmBinding("P010",1,""),"Experimenter and missing manual confirmation cannot be assigned");
            var processor=Activator.CreateInstance(host.net.GetType().Assembly.GetType("Styly.NetSync.MessageProcessor",true),new object[]{false});
            ParticipantVerificationSession.Set(host.net,"_messageProcessor",processor);
            var deviceMap=(Dictionary<int,string>)processor.GetType().GetField("_clientNoToDeviceId",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(processor);deviceMap[1]="verification-headset-one";
            Check(service.ConfirmBinding("P010",1,"Only connected headset; operator checked participant"),"explicit valid participant-device confirmation accepted");
            Check(service.StartBlockReason=="" && host.lobby.CanStart,"confirmed mapping permits normal Start guard");
            deviceMap[1]="different-device";Check(service.StartBlockReason!="","device replacement invalidates prior confirmation");deviceMap[1]="verification-headset-one";
            Check(service.StartBlockReason!="" && service.ConfirmBinding("P010",1,"Operator rechecked original headset"),"returning device still requires explicit confirmation");
            string firstSession=host.session.CurrentSessionId;
            Check(host.lobby.StartExperiment(),"Main trial starts only after manual binding");foreach(var peer in people)ProposedRevealVerification.Deliver(host,peer);ParticipantVerificationSession.Tick(people.Concat(new[]{host}).ToArray(),4);
            foreach(string id in people[0].placements.CardIds)Check(people[0].placements.SendPlacement(id,"C"),"Main Normal placement "+id);
            ParticipantVerificationSession.Tick(people.Concat(new[]{host}).ToArray(),4);people[0].agreement.ToggleLocalReady();ParticipantVerificationSession.Tick(people.Concat(new[]{host}).ToArray(),6);
            Check(service.Applied.status=="completed" && service.Applied.saveStatus=="complete","Main normal completion is saved");
            Check(service.PrepareSecondDraft() && host.session.CurrentSessionId==firstSession && service.Applied.plan.trialOrder==1 && service.Draft.trialOrder==2 && service.Draft.pairId=="010" && service.Draft.participant1Id=="P010" && host.session.DraftMode==ExperimentManager.ExperimentMode.Proposed && host.session.DraftCardSetId=="B","second draft inherits pair / PID / group without starting or resetting");
            ParticipantVerificationSession.Prepare(host,people,ExperimentManager.ExperimentMode.Proposed,"B",false);
            Check(service.StartBlockReason.Contains("対応"),"new Session requires fresh mapping confirmation");Check(service.ConfirmBinding("P010",1,"Same participant and headset checked again"),"second trial explicitly re-confirms identity");
            Check(host.lobby.StartExperiment(),"Main Proposed starts with correct assigned condition");foreach(var peer in people)ProposedRevealVerification.Deliver(host,peer);ParticipantVerificationSession.Tick(people.Concat(new[]{host}).ToArray(),4);
            foreach(string id in people[0].answers.AnswerCardIds)Check(people[0].privateBoard.TryPlaceCard(id,"A"),"Main independent placement "+id);
            ParticipantVerificationSession.Tick(people.Concat(new[]{host}).ToArray(),3);Check(people[0].submission.TrySubmit(),"Main independent submission");ParticipantVerificationSession.Tick(people.Concat(new[]{host}).ToArray(),12);
            foreach(string id in people[0].placements.CardIds)Check(people[0].placements.SendPlacement(id,"C"),"Main shared placement "+id);
            ParticipantVerificationSession.Tick(people.Concat(new[]{host}).ToArray(),4);people[0].agreement.ToggleLocalReady();ParticipantVerificationSession.Tick(people.Concat(new[]{host}).ToArray(),6);
            Check(service.Applied.status=="completed" && service.Applied.saveStatus=="complete" && logger.IndependentSaveStatus=="保存済み","Main Proposed completes with saved independent and final CSVs");
            Check(!ExperimentCsvLogStore.ReadCsv(Path.Combine(logger.SessionDirectory(firstSession),"phase_events.csv")).Any(row=>row.Length>3 && row[3]=="SESSION_ABORTED"),"normal first Main trial never becomes aborted on next preparation");
            var phase=ExperimentCsvLogStore.ReadCsv(Path.Combine(logger.SessionDirectory(host.session.CurrentSessionId),"phase_events.csv"));
            var bindingRows=ExperimentCsvLogStore.ReadCsv(Path.Combine(logger.SessionDirectory(host.session.CurrentSessionId),"participant_bindings.csv"));
            Check(bindingRows.Skip(1).All(row=>row[7]==phase[1][10] && row[9]=="Participant"),"mapping and phase logs share observer run and verified Role");
            var ui=host.session.EnsureResetUI();ui.Refresh();ui.Refresh();ExperimenterControlPanelVerification.Capture(host,"data-management");
            ui.RequestConfirmation();var input=host.scene.GetRootGameObjects().Single(o=>o.name=="ExperimenterControlPanel").GetComponentsInChildren<TMP_InputField>(true).Single(f=>f.name=="Notes");input.onEndEdit.Invoke("next trial notes changed");ui.Refresh();
            Check(!ui.IsConfirmationOpen && service.Applied.plan.notes=="" && service.HasDraftChanges,"metadata input updates Draft only and cancels stale confirmation");
            r.passed=true;
        }
        catch(Exception e){r.failure=e.ToString();Debug.LogException(e);}
        finally{foreach(var e in owned)EditorSceneManager.ClosePreviewScene(e.scene);File.WriteAllText(LastReport,JsonUtility.ToJson(r,true));}
    }
}
#endif
