#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Exercises the public UI commands against real session/lobby/logger components.
public static class ExperimenterUISimplificationVerification
{
    [Serializable] sealed class Report {public bool passed;public string failure="",environment="Unity 6000.3 offline preview / isolated GUID logs; not Quest hardware";public List<string> checks=new List<string>();}
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
    public static string LastReport {get;private set;}
    [MenuItem("Tools/Experiment/Verify Simplified Experimenter UI")]
    public static void Run()
    {
        var report=new Report();var owned=new List<ProposedRevealVerification.Endpoint>();
        string folder="Library/ExperimenterUISimplificationVerification/"+Guid.NewGuid().ToString("N");Directory.CreateDirectory(folder);LastReport=folder+"/result.json";
        void Check(bool value,string title){if(!value)throw new Exception(title);report.checks.Add(title);}
        try
        {
            var store=new ExperimentDataStore(folder+"/ids");
            ExperimentDataStore.Metadata Save(string type,string pair,string pid1,string pid2)
            {
                var m=new ExperimentDataStore.Metadata{sessionId=Guid.NewGuid().ToString("N"),epoch=1,condition="Normal",cardSetId="A",expectedParticipantCount=2,createdAtUtc=DateTime.UtcNow.ToString("o"),appVersion="verification",plan=new ExperimentDataPlan{experimentType=type,pairId=pair,participant1Id=pid1,participant2Id=pid2,allocationGroup="G1"}};
                m.relativePath=ExperimentDataStore.RelativePath(m);store.Save(m);return m;
            }
            Save("Main","001","P001","P002");Save("Main","003","P005","P006");Save("Pilot","002","P003","P004");
            Check(store.RebuildIndex(),"fixture index saved");File.Delete(Path.Combine(folder,"ids/session_index.csv"));
            var proposed=store.SuggestNewPair("Main",2);
            Check(proposed.pairId=="002"&&proposed.participant1Id=="P003"&&proposed.participant2Id=="P004","ID suggestions scan metadata even with missing index and gaps");
            var pilot=store.SuggestNewPair("Pilot",1);
            Check(pilot.experimentType=="Pilot"&&pilot.pairId=="001"&&pilot.participant1Id=="P001"&&pilot.participant2Id=="","Pilot and Main retain distinct cohorts; one-person suggestion has no second PID");
            Check(!File.Exists(Path.Combine(folder,"ids/session_index.csv"))&&store.ReadAll().Count==3,"suggestion never confirms or persists identifiers");
            Save("Main","0002","P0003","P0004");
            proposed=store.SuggestNewPair("Main",2);Check(proposed.pairId=="004"&&proposed.participant1Id=="P007","numeric aliases do not cause duplicate suggested IDs");
            var damaged=store.ReadAll().First();File.WriteAllText(Path.Combine(store.DirectoryFor(damaged),"session_metadata.json"),"broken");
            bool rejected=false;try{store.SuggestNewPair("Main",2);}catch{rejected=true;}Check(rejected,"unreadable authoritative metadata blocks suggestion rather than issuing guessed IDs");
            var initial=ProposedRevealVerification.CreateEndpoint(99,1,true);owned.Add(initial);ParticipantVerificationSession.Set(initial.registry,"localRole",ExperimentParticipantRegistry.ClientRole.Experimenter);ParticipantVerificationSession.Call(initial.registry,"Update");
            var initialUI=initial.session.EnsureResetUI();initialUI.Refresh();var initialCanvas=initial.scene.GetRootGameObjects().Single(o=>o.name=="ExperimenterControlPanel");
            Check(initial.session.CurrentSessionId==""&&initialUI.NextAction=="prepare"&&initialCanvas.transform.Find("Management/Prepare").GetComponent<UnityEngine.UI.Button>().interactable,"fresh empty Session ID displays an enabled preparation action");
            ExperimenterControlPanelVerification.Capture(initial,"simplified-initial");EditorSceneManager.ClosePreviewScene(initial.scene);owned.Remove(initial);
            foreach(string type in new[]{"Pilot","Main"})foreach(int count in new[]{1,2})
            {
                var people=Enumerable.Range(1,count).Select(c=>ProposedRevealVerification.CreateEndpoint(c,count,true)).ToArray();owned.AddRange(people);
                var host=ParticipantVerificationSession.Host(people,owned,ExperimentManager.ExperimentMode.Normal);var group=people.Concat(new[]{host}).ToArray();
                var logger=host.components.OfType<ExperimentLogger>().Single();var service=logger.Data;var ui=host.session.EnsureResetUI();ui.Refresh();
                var canvas=host.scene.GetRootGameObjects().Single(o=>o.name=="ExperimenterControlPanel");var basic=canvas.transform.Find("Management");
                UnityEngine.UI.Button Button(string path)=>canvas.transform.Find(path).GetComponent<UnityEngine.UI.Button>();
                string tag=type+"/"+count;
                Check(service.Draft.experimentType=="Test"&&service.Applied.plan.pairId==""&&host.lobby.IsExperimentStarted,tag+" Test runs with no IDs or bindings");
                Check(!basic.Find("ExperimentDataManagement").gameObject.activeSelf&&basic.Find("Normal").gameObject.activeSelf,tag+" Test hides pair/PID/group/order controls");
                if(type=="Pilot"&&count==1)ExperimenterControlPanelVerification.Capture(host,"simplified-test");
                var processor=Activator.CreateInstance(host.net.GetType().Assembly.GetType("Styly.NetSync.MessageProcessor",true),new object[]{false});ParticipantVerificationSession.Set(host.net,"_messageProcessor",processor);
                var map=(Dictionary<int,string>)processor.GetType().GetField("_clientNoToDeviceId",Flags).GetValue(processor);foreach(var person in people)map[person.net.ClientNo]="headset-"+person.net.ClientNo;
                Button("Management/"+type).onClick.Invoke();ui.Refresh();
                Check(service.Draft.experimentType==type&&service.Draft.pairId=="001"&&service.Draft.participant1Id=="P001"&&service.Draft.participant2Id==(count==2?"P002":""),tag+" type selection proposes editable IDs");
                Check(service.Applied.plan.experimentType=="Test"&&ui.NextAction=="running",tag+" Draft type change preserves active trial and guide");
                // Explicitly end the development trial using the existing Reset confirmation.
                ui.RequestConfirmation();Check(ui.IsConfirmationOpen,tag+" preparation requires explicit confirmation");ui.ConfirmReset();foreach(var person in people)ProposedRevealVerification.Deliver(host,person);ParticipantVerificationSession.Tick(group,16);ui.Refresh();
                Check(service.Applied.plan.experimentType==type&&service.Applied.plan.trialOrder==1&&host.session.CurrentCardSetId=="A"&&host.session.SessionMode==ExperimentManager.ExperimentMode.Normal,tag+" research preparation applies correct type/order/condition/set");
                Check(!host.lobby.StartExperiment()&&ui.NextAction=="binding",tag+" unconfirmed devices block central Start and guide to confirmation");
                void ConfirmDevices()
                {
                    ui.Refresh();
                    basic.Find("ExperimentDataManagement/Device1").GetComponent<TMP_Dropdown>().value=1;
                    Button("Management/ExperimentDataManagement/Bind1").onClick.Invoke();
                    if(count==2){basic.Find("ExperimentDataManagement/SecondBinding/Device2").GetComponent<TMP_Dropdown>().value=2;Button("Management/ExperimentDataManagement/SecondBinding/Bind2").onClick.Invoke();}
                    ui.Refresh();
                }
                if(type=="Main"&&count==2)
                {
                    ExperimenterControlPanelVerification.Capture(host,"simplified-main-unconfirmed");
                    Button("Details").onClick.Invoke();ui.Refresh();Check(canvas.transform.Find("Advanced").gameObject.activeSelf&&!basic.gameObject.activeSelf,tag+" details opens without a competing basic panel");
                    Check(canvas.transform.Find("Advanced/ManualPair").GetComponent<TMP_InputField>()!=null,tag+" manual IDs are available in details");
                    ExperimenterControlPanelVerification.Capture(host,"simplified-details");Button("Advanced/CloseDetails").onClick.Invoke();ui.Refresh();Check(basic.gameObject.activeSelf,tag+" details closes to basic flow");
                    var dropdown=basic.Find("ExperimentDataManagement/Device1").GetComponent<TMP_Dropdown>();
                    Check(dropdown.template.GetComponent<UnityEngine.UI.ScrollRect>().content!=null&&dropdown.template.GetComponentInChildren<UnityEngine.UI.Toggle>(true)!=null,tag+" device dropdown has a scroll viewport and selectable items");
                }
                ConfirmDevices();Check(host.lobby.CanStart&&ui.NextAction=="start",tag+" explicit UI confirmation enables central Start");
                if(type=="Main"&&count==2)
                {
                    host.net.OnAvatarDisconnected.Invoke(1);ui.Refresh();Check(!host.lobby.CanStart,tag+" disconnect event invalidates identity even before registry polling");ConfirmDevices();
                    map[1]="replacement-headset";service.ObserveConnections();ui.Refresh();Check(!host.lobby.CanStart&&ui.NextAction=="binding",tag+" device replacement invalidates binding");
                    map[1]="headset-1";service.ObserveConnections();Check(!host.lobby.CanStart,tag+" restoring a device key does not silently restore confirmation");ConfirmDevices();
                }
                string first=host.session.CurrentSessionId;
                ui.PerformNextAction();foreach(var person in people)ProposedRevealVerification.Deliver(host,person);ParticipantVerificationSession.Tick(group,4);ui.Refresh();Check(ui.NextAction=="running",tag+" manual primary Start guides classification");
                void Complete(bool independent)
                {
                    if(independent)
                    {
                        foreach(var person in people)foreach(string id in person.answers.AnswerCardIds)Check(person.privateBoard.TryPlaceCard(id,"B"),tag+" independent "+person.net.ClientNo+"/"+id);
                        ParticipantVerificationSession.Tick(group,3);foreach(var person in people)Check(person.submission.TrySubmit(),tag+" submit "+person.net.ClientNo);ParticipantVerificationSession.Tick(group,12);
                    }
                    foreach(string id in people[0].placements.CardIds)Check(people[0].placements.SendPlacement(id,"C"),tag+" shared "+id);
                    ParticipantVerificationSession.Tick(group,4);foreach(var person in people)person.agreement.ToggleLocalReady();ParticipantVerificationSession.Tick(group,6);ui.Refresh();
                }
                Complete(false);Check(ui.NextAction=="second"&&service.Applied.status=="completed"&&service.Applied.saveStatus=="complete",tag+" saved normal finish offers second trial and questionnaire guide");
                var oldBytes=Directory.GetFiles(logger.SessionDirectory(first),"*.csv").ToDictionary(p=>p,File.ReadAllBytes);
                // A save failure must block all continuation, even after a completed result.
                var original=typeof(ExperimentLogger).GetField("store",Flags).GetValue(logger);string bad=Path.GetFullPath(folder+"/bad-"+Guid.NewGuid().ToString("N"));File.WriteAllText(bad,"blocks directory");ParticipantVerificationSession.Set(logger,"store",new ExperimentCsvLogStore(bad));logger.RecordEvent("UI_SAVE_FAILURE",Guid.NewGuid().ToString("N"));ui.Refresh();
                Check(ui.NextAction=="retry"&&!ui.CanReset&&logger.UnsavedCount>0,tag+" failed save blocks next trial");
                if(type=="Main"&&count==2){ExperimenterControlPanelVerification.Capture(host,"simplified-save-error");Button("Collapse").onClick.Invoke();ui.Refresh();Check(canvas.transform.Find("ImportantWarning").GetComponent<TMP_Text>().text!="",tag+" save warning remains visible while collapsed");Button("Collapse").onClick.Invoke();}
                ui.PerformNextAction();Check(host.session.CurrentSessionId==first,tag+" failed retry never resets");ParticipantVerificationSession.Set(logger,"store",original);Check(logger.RetrySave(),tag+" queued save recovers");oldBytes=Directory.GetFiles(logger.SessionDirectory(first),"*.csv").ToDictionary(p=>p,File.ReadAllBytes);ui.Refresh();
                ui.PerformNextAction();Check(ui.IsConfirmationOpen&&service.Draft.trialOrder==2&&service.Draft.participant1Id=="P001"&&host.session.CurrentSessionId==first,tag+" second action inherits Draft and asks before Reset");
                ui.ConfirmReset();foreach(var person in people)ProposedRevealVerification.Deliver(host,person);ParticipantVerificationSession.Tick(group,16);ui.Refresh();
                Check(service.Applied.plan.trialOrder==2&&host.session.SessionMode==ExperimentManager.ExperimentMode.Proposed&&host.session.CurrentCardSetId=="B"&&ui.NextAction=="binding"&&!host.lobby.CanStart,tag+" second trial resets and requires fresh device confirmation");
                ConfirmDevices();ui.PerformNextAction();foreach(var person in people)ProposedRevealVerification.Deliver(host,person);ParticipantVerificationSession.Tick(group,4);Complete(true);
                Check(ui.NextAction=="pair"&&logger.AreRequiredAnswersSaved,tag+" two saved trials offer next pair with questionnaire guide");
                Check(oldBytes.All(p=>File.ReadAllBytes(p.Key).SequenceEqual(p.Value)),tag+" first-trial CSVs unchanged after second trial");
                if(type=="Main"&&count==2){ExperimenterControlPanelVerification.Capture(host,"simplified-pair-complete");ExperimenterControlPanelVerification.Capture(host,"simplified-main-1280x800",1280,800);}
                string second=host.session.CurrentSessionId;
                Button("Details").onClick.Invoke();ui.Refresh();Button("Advanced/Order1").onClick.Invoke();ui.Refresh();
                Check(service.Draft.trialOrder==1&&service.Draft.pairId=="001"&&service.Draft.participant1Id=="P001"&&host.session.CurrentSessionId==second,tag+" manual retry preserves IDs and does not reset or overwrite");
                Button("Advanced/RestoreApplied").onClick.Invoke();Button("Advanced/CloseDetails").onClick.Invoke();ui.Refresh();
                ui.PerformNextAction();ui.Refresh();
                Check(service.Draft.pairId=="002"&&service.Draft.participant1Id=="P00"+(count==2?"3":"2")&&service.Draft.trialOrder==1&&host.session.CurrentSessionId==second&&ui.NextAction=="prepare",tag+" next pair is a proposal only; no automatic Reset or Start");
                ui.PerformNextAction();Check(ui.IsConfirmationOpen,tag+" next pair still requires preparation confirmation");ui.CancelReset();
                foreach(var e in group) {EditorSceneManager.ClosePreviewScene(e.scene);owned.Remove(e);}
            }
            // A manually selected second trial alone must not imply pair completion.
            var only=ProposedRevealVerification.CreateEndpoint(1,1,true);owned.Add(only);var solo=new[]{only};var admin=ParticipantVerificationSession.Host(solo,owned,ExperimentManager.ExperimentMode.Normal);var endpoints=solo.Concat(new[]{admin}).ToArray();
            var recorder=admin.components.OfType<ExperimentLogger>().Single();recorder.Data.SetDraft(new ExperimentDataPlan{experimentType="Main",pairId="999",participant1Id="P999",allocationGroup="G2",trialOrder=2});
            ParticipantVerificationSession.Prepare(admin,solo,ExperimentManager.ExperimentMode.Normal,"B",false);
            var soloProcessor=Activator.CreateInstance(admin.net.GetType().Assembly.GetType("Styly.NetSync.MessageProcessor",true),new object[]{false});ParticipantVerificationSession.Set(admin.net,"_messageProcessor",soloProcessor);
            ((Dictionary<int,string>)soloProcessor.GetType().GetField("_clientNoToDeviceId",Flags).GetValue(soloProcessor))[1]="solo-device";
            Check(recorder.Data.ConfirmBinding("P999",1,"Operator checked participant and headset")&&admin.lobby.StartExperiment(),"manual second-only fixture starts explicitly");ProposedRevealVerification.Deliver(admin,only);ParticipantVerificationSession.Tick(endpoints,4);
            foreach(string id in only.placements.CardIds)only.placements.SendPlacement(id,"C");ParticipantVerificationSession.Tick(endpoints,4);only.agreement.ToggleLocalReady();ParticipantVerificationSession.Tick(endpoints,6);
            var review=admin.session.EnsureResetUI();review.Refresh();Check(recorder.Data.Applied.status=="completed"&&review.NextAction=="review","second trial without a saved first trial never claims two completed trials");
            report.passed=true;
        }
        catch(Exception e){report.failure=e.ToString();Debug.LogException(e);}
        finally{foreach(var e in owned)EditorSceneManager.ClosePreviewScene(e.scene);File.WriteAllText(LastReport,JsonUtility.ToJson(report,true));}
    }
}
#endif
