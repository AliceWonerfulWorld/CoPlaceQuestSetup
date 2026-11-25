var checks=new System.Collections.Generic.List<string>();
void Check(bool yes,string title){if(!yes)throw new System.Exception(title);checks.Add(title);}
var ui=UnityEngine.Object.FindFirstObjectByType<SessionResetUI>();
var canvas=UnityEngine.GameObject.Find("ExperimenterControlPanel");
var dropdown=canvas.transform.Find("Management/ExperimentDataManagement/VerificationMethod").GetComponent<TMPro.TMP_Dropdown>();
Check(UnityEngine.Object.FindFirstObjectByType<Styly.NetSync.NetSyncManager>().IsOfflineMode,"temporary scene is offline");
Check(ui.NextAction=="prepare" && !ui.IsConfirmationOpen,"Draft suggestion does not Reset or Start");
Check(UnityEngine.Object.FindObjectsByType<UnityEngine.EventSystems.EventSystem>(UnityEngine.FindObjectsSortMode.None).Length==1,"one EventSystem");
Check(UnityEngine.Object.FindFirstObjectByType<UnityEngine.InputSystem.UI.InputSystemUIInputModule>()!=null,"Input System UI input module present");
dropdown.Show();Check(dropdown.IsExpanded,"confirmation method dropdown opens in Play Mode");
var list=dropdown.transform.Find("Dropdown List");
var toggles=list.GetComponentsInChildren<UnityEngine.UI.Toggle>().Where(t=>t.gameObject.activeInHierarchy).ToArray();
Check(toggles.Length==3,"three preset confirmation methods are selectable");toggles[1].isOn=true;
Check(dropdown.value==1,"selecting a preset updates the value");dropdown.Hide();
var device=canvas.transform.Find("Management/ExperimentDataManagement/Device1").GetComponent<TMPro.TMP_Dropdown>();device.Show();Check(device.IsExpanded,"device dropdown opens in Play Mode");device.Hide();
UnityEngine.Canvas.ForceUpdateCanvases();
foreach(var text in canvas.GetComponentsInChildren<TMPro.TMP_Text>()) {text.ForceMeshUpdate();Check(text.rectTransform.rect.width>0&&text.rectTransform.rect.height>0,"positive text bounds "+text.transform.parent.name+"/"+text.name);Check(!text.isTextOverflowing&&text.preferredHeight<=text.rectTransform.rect.height+1,"no text clipping "+text.transform.parent.name+"/"+text.name);}
var buttons=canvas.GetComponentsInChildren<UnityEngine.UI.Button>().Where(b=>b.gameObject.activeInHierarchy && b.name!="Blocker").ToArray();
foreach(var a in buttons)foreach(var b in buttons)if(a!=b&&a.transform.parent==b.transform.parent) {var ar=(UnityEngine.RectTransform)a.transform;var br=(UnityEngine.RectTransform)b.transform;var ra=new UnityEngine.Rect(ar.anchoredPosition.x,ar.anchoredPosition.y-ar.rect.height,ar.rect.width,ar.rect.height);var rb=new UnityEngine.Rect(br.anchoredPosition.x,br.anchoredPosition.y-br.rect.height,br.rect.width,br.rect.height);Check(!ra.Overlaps(rb),"no overlapping buttons "+a.name+"/"+b.name);}
var data=new {passed=true,environment="Unity 6000.3.17f1 Play Mode / temporary offline copy / no Quest hardware",checks};
System.IO.Directory.CreateDirectory("docs/experimenter-ui-simplification-verification");System.IO.File.WriteAllText("docs/experimenter-ui-simplification-verification/runtime.json",Newtonsoft.Json.JsonConvert.SerializeObject(data,Newtonsoft.Json.Formatting.Indented));
return new {passed=true,checks=checks.Count};
