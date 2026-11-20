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

public static class ExperimenterControlPanelVerification
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    [Serializable] private class Report { public bool passed; public List<string> checks = new List<string>(); public string failure; public string environment = "Editor preview scenes / offline transport / isolated GUID log roots; no Quest hardware"; }
    private static void Set(object o, string n, object v) => o.GetType().GetField(n, Flags).SetValue(o,v);
    private static void Call(object o, string n) => o.GetType().GetMethod(n, Flags).Invoke(o,null);
    public static void Capture(ProposedRevealVerification.Endpoint e, string name, int width = 1920, int height = 1080)
    {
        if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
        var ui=e.session.EnsureResetUI(); ui.Refresh();
        var root=e.scene.GetRootGameObjects().Single(o=>o.name=="ExperimenterControlPanel");
        var canvas=root.GetComponent<Canvas>();
        var cameraObject=new GameObject("ManagementPreviewCamera"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraObject,e.scene);
        var camera=cameraObject.AddComponent<Camera>(); camera.enabled=false;
        camera.overrideSceneCullingMask=EditorSceneManager.GetSceneCullingMask(e.scene);
        camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=new Color(.018f,.03f,.044f);
        camera.cullingMask=1<<5;
        foreach(var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer=5;
        canvas.renderMode=RenderMode.ScreenSpaceCamera; canvas.worldCamera=camera; canvas.planeDistance=1;
        var rt=new RenderTexture(width,height,24); var image=new Texture2D(width,height,TextureFormat.RGB24,false);
        var previous=RenderTexture.active;
        try
        {
            camera.targetTexture=rt; Canvas.ForceUpdateCanvases();
            typeof(UnityEngine.UI.CanvasScaler).GetMethod("Handle",Flags).Invoke(root.GetComponent<UnityEngine.UI.CanvasScaler>(),null); Canvas.ForceUpdateCanvases();
            foreach(var label in root.GetComponentsInChildren<TMP_Text>()) { label.ForceMeshUpdate(); if (label.isTextOverflowing || label.preferredHeight > label.rectTransform.rect.height + 1) throw new Exception("Management text overflow: "+label.name); }
            camera.Render(); RenderTexture.active=rt; image.ReadPixels(new Rect(0,0,width,height),0,0); image.Apply();
            Directory.CreateDirectory("Documentation/ExperimenterUI"); File.WriteAllBytes("Documentation/ExperimenterUI/"+name+".png",image.EncodeToPNG());
        }
        finally { canvas.renderMode=RenderMode.ScreenSpaceOverlay; canvas.worldCamera=null; RenderTexture.active=previous; camera.targetTexture=null; UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(image); UnityEngine.Object.DestroyImmediate(cameraObject); }
    }
    [MenuItem("Tools/Experiment/Verify Management Session Safety")]
    public static void Run()
    {
        var r = new Report(); var endpoints = new List<ProposedRevealVerification.Endpoint>();
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); r.checks.Add(name); }
        try
        {
            var a = ProposedRevealVerification.CreateEndpoint(1,2,true); endpoints.Add(a);
            var b = ProposedRevealVerification.CreateEndpoint(2,2,true); endpoints.Add(b);
            var exp = ProposedRevealVerification.CreateEndpoint(99,2,true); endpoints.Add(exp);
            Set(exp.registry,"localRole",ExperimentParticipantRegistry.ClientRole.Experimenter);
            foreach (var e in endpoints) Set(e.manager,"lobby",e.lobby);
            void Deliver(ProposedRevealVerification.Endpoint from) { foreach(var to in endpoints) if (from != to) ProposedRevealVerification.Deliver(from,to); }
            void Tick(int frames = 12)
            {
                for (int i=0;i<frames;i++) foreach(var e in endpoints)
                {
                    Call(e.registry,"Update"); e.registry.RefreshParticipants(); Set(e.session,"nextAttempt",0f); Call(e.session,"Update");
                    Call(e.manager,"Update"); Call(e.lobby,"Update"); Deliver(e);
                }
            }
            Tick();
            var logger = exp.components.OfType<ExperimentLogger>().Single();
            Check(!exp.lobby.StartExperiment(),"Old/unprepared session rejects direct Start API");
            Check(!a.session.SetDraft(ExperimentManager.ExperimentMode.Normal,"B",1) && !a.session.StartNewSession() && !a.lobby.StartExperiment(),"Participant management APIs reject commands");
            var ui=exp.session.EnsureResetUI(); ui.Refresh();
            Check(exp.session.EnsureResetUI()==ui,"Management owner is reused");
            ui.RequestConfirmation(); ui.CancelReset(); Check(!ui.IsConfirmationOpen && exp.session.SessionEpoch==0,"Cancel keeps session unchanged");
            ui.RequestConfirmation(); exp.session.SetDraft(ExperimentManager.ExperimentMode.Normal,"B",2); ui.ConfirmReset();
            Check(!exp.session.IsResettingSession,"Changed Draft cancels outstanding confirmation");
            foreach (int count in new[]{1,2,1,2})
            foreach (string set in new[]{"A","B","A"})
            foreach (var mode in new[]{ExperimentManager.ExperimentMode.Normal,ExperimentManager.ExperimentMode.Proposed})
            {
                string label=mode+"/"+set+"/"+count;
                Check(exp.session.SetDraft(mode,set,count),label+" Draft selected");
                Check(exp.session.StartNewSession(),label+" request accepted");
                Check(!exp.session.StartNewSession() && !exp.lobby.StartExperiment(),label+" double reset and pending Start blocked");
                if (r.checks.Count < 15) { Set(exp.session,"resetDeadline",Time.unscaledTime - 1); Check(exp.session.PreparationStatus.Contains("タイムアウト"), "Timeout never completes preparation"); Capture(exp,"preparing"); }
                Deliver(exp); Tick();
                Check(endpoints.All(e=>!e.session.IsResettingSession && e.session.HasAppliedSettings && e.session.SessionMode==mode && e.manager.CurrentMode==mode && e.session.CurrentCardSetId==set && e.registry.ExpectedParticipantCount==count),label+" settings apply identically before ACK completion");
                Check(exp.session.AcknowledgedCount==3 && exp.session.InitializedParticipants==2,label+" participant ACK distinct from all-peer ACK");
                Check(logger.IsPreparedInCurrentRun && logger.HasMonotonicTiming,label+" fresh Logger run has monotonic origin");
                Check(exp.lobby.CanStart==(count==2),label+" exact participant count gate");
            }
            Check(exp.session.SetDraft(ExperimentManager.ExperimentMode.Normal,"A",2),"Normal settings selected");
            Check(exp.session.StartNewSession(),"Prepare Normal"); Deliver(exp); Tick();
            Check(exp.lobby.CanStart,"Prepared synchronized session can start");
            Capture(exp,"normal"); Capture(exp,"normal-1280x720",1280,720); Capture(exp,"normal-1280x800",1280,800);
            exp.Peer(2,ExperimentParticipantRegistry.RoleVariable,"Experimenter"); exp.registry.RefreshParticipants();
            Check(!exp.lobby.StartExperiment(),"Missing required participant blocks direct Start"); Deliver(b); Tick();
            string id=exp.session.CurrentSessionId;
            exp.Peer(1,ExperimentSessionManager.AckVariable,"old-session"); Check(!exp.lobby.StartExperiment(),"Stale actual Session ID ACK blocks direct Start");
            Deliver(a); Tick();
            exp.Peer(1,ExperimentSessionManager.SettingsAckVariable,"wrong-settings"); Check(!exp.lobby.StartExperiment(),"Mismatched applied settings blocks direct Start");
            Deliver(a); Tick();
            exp.session.SetDraft(ExperimentManager.ExperimentMode.Proposed,"B",1); Check(!exp.lobby.StartExperiment(),"Unapplied Draft blocks direct Start");
            exp.session.SetDraft(ExperimentManager.ExperimentMode.Normal,"A",2);
            Check(exp.lobby.StartExperiment() && !exp.lobby.StartExperiment(),"Start accepted once"); Deliver(exp); Tick();
            ui.Refresh(); Check(((TMP_Text)typeof(SessionResetUI).GetField("current",Flags).GetValue(ui)).text.Contains("共同分類中"),"Normal active Idle is displayed as shared classification");
            exp.session.SetDraft(ExperimentManager.ExperimentMode.Proposed,"B",1); Tick();
            Check(endpoints.All(e=>e.session.CurrentSessionId==id && e.session.CurrentCardSetId=="A" && e.manager.CurrentMode==ExperimentManager.ExperimentMode.Normal && e.registry.ExpectedParticipantCount==2),"Draft during experiment does not change applied conditions");
            var retained=exp.net.GetGlobalVariable(ExperimentSessionManager.ControlVariable);
            Call(logger,"OnDisable"); Check(!logger.IsPreparedInCurrentRun,"Logger disable invalidates preparation proof"); Call(logger,"OnEnable");
            Check(!logger.IsPreparedInCurrentRun && exp.session.CurrentSessionId==id && exp.lobby.IsExperimentStarted,"Restored trial is preserved without automatic Reset");
            Check(exp.session.StartNewSession(),"Explicit abort uses existing Reset"); Deliver(exp); Tick();
            Check(!File.Exists(Path.Combine(logger.OutputDirectory,id,"final_placements.csv")),"Unconfirmed answers are not invented");
            Check(ExperimentCsvLogStore.ReadCsv(Path.Combine(logger.OutputDirectory,id,"phase_events.csv")).Skip(1).Any(row=>row[3]=="SESSION_ABORTED"),"Aborted trial is recorded");
            exp.session.SetDraft(ExperimentManager.ExperimentMode.Normal,"A",2); Check(exp.session.StartNewSession(),"Prepare for logger guard checks"); Deliver(exp); Tick();
            Call(logger,"OnDisable"); Check(!exp.lobby.StartExperiment(),"Inactive Logger blocks Start"); Call(logger,"OnEnable");
            Check(!exp.lobby.StartExperiment(),"Reenabled Logger requires new preparation");
            Check(exp.session.StartNewSession(),"Prepare after logger continuity loss"); Deliver(exp); Tick(); Check(exp.lobby.CanStart,"New preparation restores ability to start");
            // Force IO failure in this test's own root only.
            string root=Path.GetFullPath("Library/ManagementVerification/Failure-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Path.GetDirectoryName(root)); File.WriteAllText(root,"file blocks directory");
            Set(logger,"store",new ExperimentCsvLogStore(root)); logger.RecordEvent("TEST_SAVE_FAILURE",Guid.NewGuid().ToString("N"));
            Capture(exp,"error");
            string before=exp.session.CurrentSessionId;
            Check(logger.UnsavedCount>0 && !exp.session.StartNewSession() && exp.session.CurrentSessionId==before,"Save failure prevents Reset without issuing Session");
            File.Delete(root); Check(logger.RetrySave(),"Existing queue retries after IO recovery");
            Check(exp.session.CurrentSessionId==before,"Save retry does not create Session");
            ui.Refresh(); var texts=exp.scene.GetRootGameObjects().Where(o=>o.name=="ExperimenterControlPanel").SelectMany(o=>o.GetComponentsInChildren<TMP_Text>(true));
            Check(texts.All(t=>t.font!=null && t.font.HasCharacters(t.text.Replace('\n',' '))),"Management static font covers rendered text");
            Check(exp.scene.GetRootGameObjects().Count(o=>o.name=="ExperimenterControlPanel")==1,"One management Canvas per scene");
            Check(a.scene.GetRootGameObjects().All(o=>o.name!="ExperimenterControlPanel"),"Participant has no management Canvas");
            ui.RequestConfirmation();
            Check(exp.session.StartNewSession(), "API preparation invalidates outstanding dialog"); ui.Refresh();
            Check(!ui.IsConfirmationOpen, "Session/reset change cancels confirmation"); Deliver(exp); Tick();
            Check(exp.lobby.CanStart, "Logger-missing test begins with an otherwise startable session");
            UnityEngine.Object.DestroyImmediate(logger);
            Check(!exp.lobby.StartExperiment() && exp.lobby.StartBlockReason.Contains("Logger"), "Missing Logger blocks direct Start");
            string legacyId="legacy"+Guid.NewGuid().ToString("N");
            string legacy="{\"id\":\""+legacyId+"\",\"epoch\":"+(exp.session.SessionEpoch+1)+",\"owner\":99,\"phase\":\"active\",\"mode\":\"Normal\",\"cardSetId\":\"A\",\"activatedAt\":\""+DateTime.UtcNow.ToString("o")+"\"}";
            exp.Peer(99,ExperimentSessionManager.AckVariable,legacyId); exp.Global(ExperimentSessionManager.ControlVariable,legacy); Call(exp.session,"Update");
            Check(!exp.session.HasAppliedSettings && !exp.lobby.StartExperiment(),"Legacy control without settings cannot infer applied count or Start");
            Check(exp.session.CurrentSessionId==legacyId && exp.net.GetGlobalVariable(ExperimentSessionManager.ControlVariable)==legacy,"Restored legacy control never issues automatic Reset");
            Set(a.registry,"localRole",ExperimentParticipantRegistry.ClientRole.Experimenter); Call(a.registry,"Update");
            a.session.SetDraft(ExperimentManager.ExperimentMode.Normal,"A",2);
            Set(a.components.OfType<PilotCardSetController>().Single(),"setA",new PilotCardSetController.Card[0]);
            Check(a.session.StartNewSession(),"Initialization-failure scenario accepts request before applying assets");
            ProposedRevealVerification.Deliver(a,b); Call(a.session,"Update"); Call(b.session,"Update");
            string failedId=a.session.CurrentSessionId;
            Check(a.session.IsResettingSession && a.session.PreparationStatus.Contains("準備失敗") && !a.session.StartNewSession() && a.session.CurrentSessionId==failedId,"Actual invalid card assets fail initialization without forced new GUID");
            var avatar=b.net.GetType().GetField("_avatarManager",Flags).GetValue(b.net);
            var peers=(System.Collections.IDictionary)avatar.GetType().GetField("_connectedPeers",Flags).GetValue(avatar); peers.Remove(1);
            Check(b.session.PreparationStatus.Contains("準備元端末が未接続") && b.session.IsResettingSession,"Owner disconnect remains blocked with actionable status");
            r.passed=true;
        }
        catch(Exception e) { r.failure=e.ToString(); Debug.LogException(e); }
        finally { foreach(var e in endpoints) EditorSceneManager.ClosePreviewScene(e.scene); Directory.CreateDirectory("Documentation"); File.WriteAllText("Documentation/ExperimenterControlPanelVerification.json",JsonUtility.ToJson(r,true)); }
    }
}
#endif
