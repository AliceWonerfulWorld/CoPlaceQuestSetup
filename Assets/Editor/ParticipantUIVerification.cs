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
using UnityEngine.SceneManagement;

public static class ParticipantUIVerification
{
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
    [Serializable] class Report { public bool passed;public List<string> checks=new List<string>();public string failure;public string environment="Unity Editor offline preview scenes; simulated network and input; NOT Quest hardware"; }
    static T Get<T>(object o,string n)=>(T)o.GetType().GetField(n,Flags).GetValue(o);
    [MenuItem("Tools/Experiment/Verify Participant Japanese UI")]
    public static void Run()
    {
        var r=new Report();var owned=new List<ProposedRevealVerification.Endpoint>();
        void Check(bool ok,string name){if(!ok)throw new Exception(name);r.checks.Add(name);}
        void CheckText(ProposedRevealVerification.Endpoint e,string tag)
        {
            foreach(var t in e.scene.GetRootGameObjects().SelectMany(o=>o.GetComponentsInChildren<TMP_Text>()).Where(t=>t.isActiveAndEnabled && (t.GetComponentInParent<Canvas>()==null || t.GetComponentInParent<Canvas>().enabled)))
            {
                Check(t.font!=null && t.font.HasCharacters(t.text.Replace('\n',' ')),tag+" glyph coverage "+t.name);
                t.ForceMeshUpdate();
                if(t.GetComponentInParent<PilotCardFace>()==null && t.text!="" && t.font==ParticipantUIStyle.Font)
                    Check(t.preferredHeight<=t.rectTransform.rect.height+2 && !t.isTextOverflowing,tag+" text fits "+t.name+" / "+t.transform.parent.name);
            }
        }
        try
        {
            var tierNames=new[]{"A","B","C","D","E","Unclassified"};
            var meanings=new[]{"非常に適している","やや適している","どちらともいえない","あまり適していない","まったく適していない","まだ分類されていません"};
            for(int index=0;index<tierNames.Length;index++)Check(ParticipantUIStyle.TierMeaning(tierNames[index])==meanings[index],"specified rating meaning "+tierNames[index]);
            Check(ParticipantUIStyle.SharedInstruction=="参加者同士で話し合い、新入生歓迎イベントとしての適切さを評価してください。","shared instruction matches common task");
            for(int count=1;count<=2;count++)
            {
                var people=Enumerable.Range(1,count).Select(i=>ProposedRevealVerification.CreateEndpoint(i,count,true)).ToArray();owned.AddRange(people);
                foreach(var e in people) { ParticipantVerificationSession.Set(e.manager,"lobby",e.lobby); ParticipantVerificationSession.Call(e.privateBoard,"Update"); }
                var a=people[0];
                ParticipantVerificationSession.Call(a.components.OfType<ExperimentLobbyUI>().Single(),"Update");
                CheckText(a,"Lobby / "+count);
                if(count==2) Capture(a,"lobby",a.lobby.transform,1.0f);
                var host=ParticipantVerificationSession.Host(people,owned);
                var group=people.Concat(new[]{host}).ToArray();
                var previousCsv=new Dictionary<string,byte[]>();
                foreach(string set in new[]{"A","B"}) foreach(var mode in new[]{ExperimentManager.ExperimentMode.Normal,ExperimentManager.ExperimentMode.Proposed})
                {
                    string tag=count+"/"+set+"/"+mode;
                    ParticipantVerificationSession.Prepare(host,people,mode,set,false);
                    foreach(var e in people)
                    {
                        var intro=e.components.OfType<ExperimentLobbyUI>().Single();ParticipantVerificationSession.Call(intro,"Update");
                        intro.ConfigurePresentation(true);intro.ConfigurePresentation(true);
                        var labels=intro.GetComponentsInChildren<TMP_Text>(true);
                        Check(labels.Count(t=>t.name=="TaskCriteria")==1 && labels.Count(t=>t.name=="StepsHeading")==1,tag+" repeated presentation regeneration never duplicates labels");
                        foreach(var expected in new[]{ExperimentLobbyUI.TaskTitle,ExperimentLobbyUI.GeneralDescription,ExperimentLobbyUI.JudgementGuide,ExperimentLobbyUI.JudgementNote,ExperimentLobbyUI.TaskGoal,ExperimentLobbyUI.ClassificationRules,ParticipantUIStyle.CriteriaLegend()})
                            Check(labels.Any(t=>t.text==expected),tag+" common task includes "+expected.Split('\n')[0]);
                        Check(Get<TMP_Text>(intro,"modeDescription").text==(mode==ExperimentManager.ExperimentMode.Normal?ExperimentLobbyUI.NormalDescription:ExperimentLobbyUI.ProposedDescription),tag+" applied mode selects exact neutral steps");
                        Check(!labels.Any(t=>new[]{"反映されやす","公平な話し合い","採用したい","有力候補","正解です"}.Any(word=>t.text.Contains(word))),tag+" no hypothesis or preferred answer in task UI");
                        CheckText(e,tag+" prepared Lobby");
                    }
                    var opposite=mode==ExperimentManager.ExperimentMode.Normal?ExperimentManager.ExperimentMode.Proposed:ExperimentManager.ExperimentMode.Normal;
                    host.session.SetDraft(opposite,set,count);
                    var lobbyUI=a.components.OfType<ExperimentLobbyUI>().Single();ParticipantVerificationSession.Call(lobbyUI,"Update");
                    Check(Get<TMP_Text>(lobbyUI,"modeDescription").text==(mode==ExperimentManager.ExperimentMode.Normal?ExperimentLobbyUI.NormalDescription:ExperimentLobbyUI.ProposedDescription),tag+" next Draft never replaces applied task steps");
                    host.session.SetDraft(mode,set,count);
                    if(count==2 && set=="A")Capture(a,"lobby-"+mode,a.lobby.transform,1.0f);
                    Check(host.lobby.StartExperiment(),tag+" prepared session starts without extra task confirmation");
                    foreach(var peer in people)ProposedRevealVerification.Deliver(host,peer);ParticipantVerificationSession.Tick(group,4);
                    foreach(var canvas in new[]{MainCanvas(a),PrivateCanvas(a)})
                    {
                        foreach(string tier in new[]{"A","B","C","D","E","Unclassified"})
                        {
                            var row=canvas.transform.Find(tier=="Unclassified"?"UnclassifiedRow":"Tier"+tier+"Row");
                            Check(row.Find("TierCaption").gameObject.activeSelf && row.Find("TierCaption").GetComponent<TMP_Text>().enabled && row.Find("TierCaption").GetComponent<TMP_Text>().text.Replace("\n","")==ParticipantUIStyle.TierMeaning(tier),tag+" matching "+canvas.transform.parent.name+" rating "+tier);
                        }
                    }
                    Check(MainCanvas(a).transform.Find("Hint").GetComponent<TMP_Text>().text==ParticipantUIStyle.SharedInstruction,tag+" common shared-task instruction in both modes");
                    Check(people.All(e=>!e.components.OfType<ExperimentResultUI>().Single().IsVisible),tag+" no completion panel before confirmation");
                    var management=host.session.GetComponent<SessionResetUI>(); management.Refresh();
                    Check(management.PreparationActionLabel=="実験を中断する",tag+" active experiment uses abort action");
                    Check(previousCsv.All(p=>File.ReadAllBytes(p.Key).SequenceEqual(p.Value)),tag+" previous answer CSV bytes preserved");
                    Check(people.All(e=>e.view.DisplayedParticipantCount==0 && !e.view.IsReferenceOpen && !e.view.CanToggleReference),tag+" no reference before committed Reveal");
                    Check(people.All(e=>e.scene.GetRootGameObjects().All(o=>o.name!="ExperimenterControlPanel")),tag+" management panel absent on Participant");
                    var ids=a.answers.AnswerCardIds.ToArray();Check(ids.Length==8,tag+" expected actual pilot cards");
                    if(mode==ExperimentManager.ExperimentMode.Proposed)
                    {
                        var ui=a.components.OfType<IndependentAnswerSubmissionUI>().Single();ParticipantVerificationSession.Call(ui,"Update");
                        CheckText(a,tag+" independent");
                        Check(PrivateCanvas(a).transform.Find("Hint").GetComponent<TMP_Text>().text.StartsWith(ParticipantUIStyle.IndependentInstruction),tag+" independent-task instruction uses personal evaluation");
                        Check(Get<TMP_Text>(ui,"status").text.Contains("0 / "+ids.Length),tag+" count derived from current answer IDs");
                        var privateConnection=Get<object>(a.net,"_connectionManager");privateConnection.GetType().GetMethod("Disconnect").Invoke(privateConnection,null);
                        ParticipantVerificationSession.Call(ui,"Update");Check(Get<TMP_Text>(ui,"status").text.Contains("接続を待っています"),tag+" private disconnection is explained without asking to classify again");
                        privateConnection.GetType().GetMethod("Connect").Invoke(privateConnection,new object[]{"",0,0,0,"reveal-test"});ParticipantVerificationSession.Tick(group,2);
                        if(count==2 && set=="A") Capture(a,"independent",a.privateBoard.transform,2.2f);
                        foreach(var e in people) foreach(string id in ids) Check(e.privateBoard.TryPlaceCard(id,e==a?"A":"B"),tag+" classify "+e.net.ClientNo+"/"+id);
                        ParticipantVerificationSession.Tick(group,3);
                        Check(people.All(e=>!e.reveal.HasRevealed && e.submission.CanSubmit),tag+" completing cards alone never reveals");
                        Check(a.submission.TrySubmit(),tag+" submit");ParticipantVerificationSession.Tick(group,1);
                        Check(!a.privateBoard.CanInteract && Get<TMP_Text>(ui,"label").text=="提出を取り消す",tag+" Submit locks answers and has distinct label");
                        CheckText(a,tag+" submitted");
                        if(count==2 && set=="A") Capture(a,"submitted",a.privateBoard.transform,2.2f);
                        Check(a.submission.TryCancel(),tag+" cancel before commit");ParticipantVerificationSession.Tick(group,3);
                        Check(a.privateBoard.CanInteract && a.submission.TrySubmit(),tag+" cancellation unlocks and resubmits");
                        foreach(var e in people.Skip(1)) Check(e.submission.TrySubmit(),tag+" peer Submit");
                        ParticipantVerificationSession.Tick(group,12);
                        Check(people.All(e=>e.reveal.HasRevealed && e.view.IsReferenceOpen && e.view.DisplayedParticipantCount==count && e.view.CanToggleReference),tag+" committed Reveal automatically opens once for all Participants");
                        CheckText(a,tag+" revealed");
                        Check(host.view.DisplayedParticipantCount==count && !host.view.CanToggleReference,tag+" experimenter fixed read-only view retained");
                        string snapshot=JsonUtility.ToJson(a.reveal.GetRevealedSnapshot());
                        var panel=a.view.transform.Find("ReadonlyMiniBoards");
                        Check(panel.GetComponentsInChildren<TMP_Text>(true).Any(t=>t.name=="CriteriaLegend" && t.text==ParticipantUIStyle.CriteriaLegend(count>1)),tag+" reference uses same rating legend without reducing images");
                        Check(panel.GetComponentsInChildren<UnityEngine.UI.Selectable>(true).Length==0 && panel.GetComponentsInChildren<Collider>(true).Length==0,tag+" answer content has no interactions");
                        Check(panel.GetComponentsInChildren<TMP_Text>(true).Any(t=>t.text.Contains("あなた")) && !panel.GetComponentsInChildren<TMP_Text>(true).Any(t=>t.text.Contains("Client")),tag+" local identity without technical IDs");
                        Check(!panel.GetComponentsInChildren<TMP_Text>(true).Any(t=>t.name=="CardId" || ids.Contains(t.text)),tag+" reference Card IDs never rendered");
                        var expectedVisuals=Get<ReadonlyParticipantBoardView.CardVisual[]>(a.view,"cardVisuals").ToDictionary(v=>v.cardId);
                        foreach(var image in panel.GetComponentsInChildren<UnityEngine.UI.RawImage>(true))
                        {
                            string id=image.transform.parent.name;
                            Check(expectedVisuals.ContainsKey(id) && image.texture==expectedVisuals[id].texture && image.uvRect==PilotCardFace.PhotographUv,tag+" photo matches committed card "+id);
                            Check(((RectTransform)image.transform).anchoredPosition==Vector2.zero && ((RectTransform)image.transform).rect.height<=((RectTransform)image.transform.parent).rect.height-7,tag+" centered photo uses former ID area "+id);
                        }
                        var camera=a.scene.GetRootGameObjects().SelectMany(o=>o.GetComponentsInChildren<Camera>(true)).First(c=>c.CompareTag("MainCamera"));
                        var position=a.view.transform.position;camera.transform.position+=Vector3.right*.2f;ParticipantVerificationSession.Call(a.view,"Update");
                        Check(a.view.transform.position==position,tag+" reference remains world anchored when head moves");camera.transform.position-=Vector3.right*.2f;
                        var control=a.scene.GetRootGameObjects().Single(o=>o.name=="IndependentReferenceControls");
                        var launcher=control.transform.Find("ReferenceLauncher");
                        Check(launcher.GetComponents<UnityEngine.EventSystems.BaseRaycaster>().Length>0,tag+" launcher reuses XR UI raycaster types");
                        launcher.GetComponentInChildren<UnityEngine.UI.Button>().onClick.Invoke();
                        Check(!a.view.IsReferenceOpen,tag+" actual launcher button closes locally");
                        Check(count==1 || people[1].view.IsReferenceOpen,tag+" local toggle does not affect peer");
                        a.view.Refresh();Check(!a.view.IsReferenceOpen,tag+" duplicate commit does not reopen user-closed panel");
                        ParticipantVerificationSession.Call(a.reveal,"ClearLocal");ParticipantVerificationSession.Call(a.reveal,"LateUpdate");
                        Check(!a.view.IsReferenceOpen && a.view.DisplayedRevealId==a.reveal.RevealId,tag+" reconnect same commit preserves local closed state");
                        if(count==2 && set=="A") Capture(a,"reference-hidden",MainCanvas(a).transform.parent,2.2f);
                        for(int repeat=0;repeat<3;repeat++) {Check(a.view.ToggleReference(),tag+" toggle open");Check(a.view.ToggleReference(),tag+" toggle close");} a.view.ToggleReference();
                        Check(JsonUtility.ToJson(a.reveal.GetRevealedSnapshot())==snapshot,tag+" toggles never mutate committed answers");
                        if(count==2 && set=="A") { Capture(a,"reference-open",a.view.transform,.76f); CaptureContext(a,"reference-context"); }
                    }
                    else Check(people.All(e=>!e.view.ToggleReference()),tag+" Normal has no reference operation");
                    a.agreement.ToggleLocalReady();ParticipantVerificationSession.Call(a.components.OfType<FinalAgreementUI>().Single(),"Update");
                    Check(!a.agreement.IsLocalReady && Get<TMP_Text>(a.components.OfType<FinalAgreementUI>().Single(),"statusText").text.Contains("未分類"),tag+" unclassified agreement rejection explained in Japanese");
                    foreach(string id in ids) Check(a.placements.SendPlacement(id,"C"),tag+" shared placement works with reference open "+id);
                    ParticipantVerificationSession.Tick(group,4);
                    if(count==2 && set=="A") Capture(a,mode==ExperimentManager.ExperimentMode.Normal?"shared-normal":"shared-proposed",MainCanvas(a).transform.parent,2.2f);
                    ParticipantVerificationSession.Set(a.agreement,"pendingReady","pending-ui-check");ParticipantVerificationSession.Call(a.components.OfType<FinalAgreementUI>().Single(),"Update");
                    Check(Get<TMP_Text>(a.components.OfType<FinalAgreementUI>().Single(),"statusText").text.Contains("同期待ち"),tag+" Ready send is distinct from acknowledged approval");
                    ParticipantVerificationSession.Set(a.agreement,"pendingReady",null);
                    a.agreement.ToggleLocalReady();ParticipantVerificationSession.Tick(group,2);
                    if(count==2 && set=="A") Capture(a,"agreement",a.components.OfType<FinalAgreementUI>().Single().transform,.60f);
                    foreach(var e in people.Skip(1)) e.agreement.ToggleLocalReady();ParticipantVerificationSession.Tick(group,6);
                    Check(people.All(e=>e.agreement.IsBoardConfirmed && e.components.OfType<ExperimentResultUI>().Single().IsVisible),tag+" final agreement and Japanese Result");
                    Check(people.All(e=>e.components.OfType<ExperimentResultUI>().Single().GetComponentsInChildren<UnityEngine.UI.Button>().Length==0),tag+" participant Result has no management button");
                    Check(people.All(e=>!e.view.IsReferenceOpen && !e.view.CanToggleReference && !e.view.ToggleReference()),tag+" completion closes reference and disables reopening");
                    Check(people.All(e=>!e.components.OfType<FinalAgreementUI>().Single().GetComponent<Canvas>().enabled),tag+" completion hides agreement controls");
                    var result=a.components.OfType<ExperimentResultUI>().Single();
                    var resultPanel=Get<GameObject>(result,"contentRoot");
                    var eye=a.scene.GetRootGameObjects().SelectMany(o=>o.GetComponentsInChildren<Camera>(true)).First(c=>c.CompareTag("MainCamera"));
                    Check(Mathf.Abs(Vector3.Distance(resultPanel.transform.position,eye.transform.position)-1.4f)<.01f && Vector3.Dot((resultPanel.transform.position-eye.transform.position).normalized,eye.transform.forward)>.999f,tag+" completion panel is summoned in view at 1.4m");
                    Check(!MainCanvas(a).transform.Find("Hint").GetComponent<TMP_Text>().enabled,tag+" completion hides shared task instructions");
                    var resultPosition=resultPanel.transform.position;eye.transform.position+=Vector3.right*.2f;result.Refresh();
                    Check(resultPanel.transform.position==resultPosition,tag+" completion panel remains world anchored");eye.transform.position-=Vector3.right*.2f;
                    if(count==2 && set=="A") Capture(a,"result-"+mode,resultPanel.transform,.50f);
                    var logger=host.components.OfType<ExperimentLogger>().Single();
                    string folder=Path.Combine(logger.OutputDirectory,host.session.CurrentSessionId);
                    Check(logger.FinalSaveStatus=="保存済み" && ExperimentCsvLogStore.ReadCsv(Path.Combine(folder,"final_placements.csv")).Count==ids.Length+1,tag+" final CSV saved");
                    if(mode==ExperimentManager.ExperimentMode.Proposed) Check(logger.IndependentSaveStatus=="保存済み" && ExperimentCsvLogStore.ReadCsv(Path.Combine(folder,"independent_answers.csv")).Count==ids.Length*count+1,tag+" independent CSV matrix saved");
                    management.Refresh();Check(management.CanReset && management.PreparationActionLabel=="次のセッションを準備する",tag+" normal completion offers next session");
                    if(count==1 && set=="A") ExperimenterControlPanelVerification.Capture(host,"completed-"+mode);
                    // Exercise the actual IO error + queue guard only in this test's isolated root.
                    if(count==1 && set=="A")
                    {
                        var saved=Get<HashSet<string>>(logger,"savedFinal");saved.Remove(host.session.CurrentSessionId);management.Refresh();
                        Check(!management.CanReset && management.PreparationActionLabel=="実験データを保存しています",tag+" finalized differs from final CSV saved");if(mode==ExperimentManager.ExperimentMode.Normal) ExperimenterControlPanelVerification.Capture(host,"completed-saving-simulated");saved.Add(host.session.CurrentSessionId);
                        string failureRoot=Path.GetFullPath("Library/ParticipantCompletionFailure-"+Guid.NewGuid().ToString("N"));
                        File.WriteAllText(failureRoot,"isolated test blocks output directory");
                        var originalStore=Get<object>(logger,"store");ParticipantVerificationSession.Set(logger,"store",new ExperimentCsvLogStore(failureRoot));
                        logger.RecordEvent("TEST_SAVE_FAILURE",Guid.NewGuid().ToString("N"));management.Refresh();
                        if(mode==ExperimentManager.ExperimentMode.Normal) ExperimenterControlPanelVerification.Capture(host,"completed-save-error");
                        string unchanged=host.session.CurrentSessionId;
                        Check(!management.CanReset && management.PreparationActionLabel=="保存を再試行する" && !host.session.StartNewSession() && host.session.CurrentSessionId==unchanged,tag+" save failure blocks UI and existing Reset API");
                        File.Delete(failureRoot);Check(logger.RetrySave() && host.session.CurrentSessionId==unchanged,tag+" queued save recovery never issues new Session");
                        ParticipantVerificationSession.Set(logger,"store",originalStore);management.Refresh();
                        Check(management.CanReset && management.PreparationActionLabel=="次のセッションを準備する",tag+" save recovery restores next preparation");
                    }
                    foreach(string file in Directory.GetFiles(folder,"*.csv")) previousCsv[file]=File.ReadAllBytes(file);
                    foreach(var e in people) foreach(var t in e.scene.GetRootGameObjects().SelectMany(o=>o.GetComponentsInChildren<TMP_Text>()).Where(t=>t.isActiveAndEnabled && (t.GetComponentInParent<Canvas>()==null || t.GetComponentInParent<Canvas>().enabled)))
                    {
                        Check(t.font!=null && t.font.HasCharacters(t.text.Replace('\n',' ')),tag+" glyph coverage "+t.name);
                        t.ForceMeshUpdate();
                        if (t.GetComponentInParent<PilotCardFace>() == null && t.text != "" && t.font == ParticipantUIStyle.Font)
                            Check(t.preferredHeight <= t.rectTransform.rect.height + 2 && !t.isTextOverflowing, tag+" text fits "+t.name+" / "+t.transform.parent.name);
                    }
                    string completedSession=host.session.CurrentSessionId;
                    management.RequestConfirmation();
                    Check(management.IsConfirmationOpen && !Get<TMP_Text>(management,"prompt").text.Contains("中断"),tag+" normal next-session dialog is not an abort");
                    management.ConfirmReset();foreach(var peer in people) ProposedRevealVerification.Deliver(host,peer);ParticipantVerificationSession.Tick(group,16);
                    Check(host.session.CurrentSessionId!=completedSession && !host.session.IsResettingSession && people.All(e=>!e.components.OfType<ExperimentResultUI>().Single().IsVisible && !e.view.IsReferenceOpen),tag+" next-session Reset clears finished UI and reference");
                    Check(MainCanvas(a).transform.Find("Hint").GetComponent<TMP_Text>().enabled,tag+" Reset restores task instructions for next trial");
                    Check(!ExperimentCsvLogStore.ReadCsv(Path.Combine(folder,"phase_events.csv")).Skip(1).Any(row=>row[3]=="SESSION_ABORTED"),tag+" normal completion never records SESSION_ABORTED");
                }
                ParticipantVerificationSession.Prepare(host,people,ExperimentManager.ExperimentMode.Normal,"A",false);
                Check(people.All(e=>!e.view.IsReferenceOpen && e.view.DisplayedParticipantCount==0),"Reset removes prior panel and answers / "+count);
                if(count==2) Capture(a,"lobby-ready",a.lobby.transform,1.0f);
                var connection=Get<object>(a.net,"_connectionManager");connection.GetType().GetMethod("Disconnect").Invoke(connection,null);
                ParticipantVerificationSession.Call(a.components.OfType<ExperimentLobbyUI>().Single(),"Update");
                Check(a.components.OfType<ExperimentLobbyUI>().Single().GetComponentsInChildren<TMP_Text>().Any(t=>t.text=="未接続"),"Disconnected Lobby is Japanese / "+count);
            }
            r.passed=true;
        }
        catch(Exception e){r.failure=e.ToString();Debug.LogException(e);}
        finally {foreach(var e in owned) EditorSceneManager.ClosePreviewScene(e.scene);Directory.CreateDirectory("Documentation");File.WriteAllText("Documentation/ParticipantUIVerification.json",JsonUtility.ToJson(r,true));}
    }
    static Canvas PrivateCanvas(ProposedRevealVerification.Endpoint e)=>e.privateBoard.GetComponentsInChildren<Canvas>(true).Single(c=>c.name=="TierBoardCanvas");
    static Canvas MainCanvas(ProposedRevealVerification.Endpoint e)=>e.scene.GetRootGameObjects().Single(o=>o.name=="TierBoard").GetComponentsInChildren<Canvas>(true).Single(c=>c.name=="TierBoardCanvas");
    private static void CaptureContext(ProposedRevealVerification.Endpoint e,string name)
    {
        var xr=e.scene.GetRootGameObjects().SelectMany(o=>o.GetComponentsInChildren<Camera>(true)).First(c=>c.CompareTag("MainCamera"));
        var obj=new GameObject("ParticipantContextCamera");SceneManager.MoveGameObjectToScene(obj,e.scene);var cam=obj.AddComponent<Camera>();cam.enabled=false;
        cam.overrideSceneCullingMask=EditorSceneManager.GetSceneCullingMask(e.scene);cam.transform.SetPositionAndRotation(xr.transform.position,xr.transform.rotation*Quaternion.Euler(0,20,0));cam.fieldOfView=75;
        cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.018f,.03f,.044f);
        var rt=new RenderTexture(1800,1200,24);var tex=new Texture2D(1800,1200,TextureFormat.RGB24,false);var old=RenderTexture.active;
        try{cam.targetTexture=rt;Canvas.ForceUpdateCanvases();cam.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,1800,1200),0,0);tex.Apply();Directory.CreateDirectory("Documentation/ParticipantUI");File.WriteAllBytes("Documentation/ParticipantUI/"+name+".png",tex.EncodeToPNG());}
        finally{RenderTexture.active=old;cam.targetTexture=null;UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(tex);UnityEngine.Object.DestroyImmediate(obj);}
    }
    public static void Capture(ProposedRevealVerification.Endpoint e,string name,Transform focus,float size)
    {
        var obj=new GameObject("ParticipantPreviewCamera");SceneManager.MoveGameObjectToScene(obj,e.scene);var cam=obj.AddComponent<Camera>();cam.enabled=false;
        cam.overrideSceneCullingMask=EditorSceneManager.GetSceneCullingMask(e.scene);cam.orthographic=true;cam.orthographicSize=size;
        Vector3 center=focus.position;
        var corners=new Vector3[4];bool any=false;Bounds bounds=new Bounds(center,Vector3.zero);
        foreach(var canvas in focus.GetComponentsInChildren<Canvas>())
            if(canvas.enabled) { ((RectTransform)canvas.transform).GetWorldCorners(corners); foreach(var corner in corners) { if(!any) {bounds=new Bounds(corner,Vector3.zero);any=true;} else bounds.Encapsulate(corner); } }
        if(focus==e.view.transform && e.view.IsReferenceOpen)
        {
            var close=e.scene.GetRootGameObjects().FirstOrDefault(o=>o.name=="IndependentReferenceControls")?.transform.Find("ReferenceClose") as RectTransform;
            if(close!=null && close.gameObject.activeInHierarchy){close.GetWorldCorners(corners);foreach(var corner in corners)bounds.Encapsulate(corner);}
        }
        if(any) { center=bounds.center; size=Mathf.Max(size,bounds.size.y*.55f,bounds.size.x/1.5f*.55f); }
        cam.orthographicSize=size;
        cam.transform.SetPositionAndRotation(center-focus.forward*4,focus.rotation);cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.018f,.03f,.044f);
        var disabled=e.scene.GetRootGameObjects().SelectMany(o=>o.GetComponentsInChildren<Canvas>()).Where(c=>c.enabled && !c.transform.IsChildOf(focus) && c.transform!=focus && !(focus==e.view.transform && c.name=="ReferenceClose") && !(focus==MainCanvas(e).transform.parent && (c.name=="ReferenceLauncher" || c.GetComponentInParent<PilotCardFace>()!=null))).ToArray();
        foreach(var c in disabled) c.enabled=false;
        var rt=new RenderTexture(1800,1200,24);var image=new Texture2D(1800,1200,TextureFormat.RGB24,false);var previous=RenderTexture.active;
        try {cam.targetTexture=rt;Canvas.ForceUpdateCanvases();cam.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,1800,1200),0,0);image.Apply();Directory.CreateDirectory("Documentation/ParticipantUI");File.WriteAllBytes("Documentation/ParticipantUI/"+name+".png",image.EncodeToPNG());}
        finally {foreach(var c in disabled) if(c!=null)c.enabled=true;RenderTexture.active=previous;cam.targetTexture=null;UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(obj);}
    }
}
#endif
