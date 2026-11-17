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

public static class PilotCardReadabilityVerification
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    [Serializable] private class Report { public bool passed; public List<string> checks = new List<string>(); public string failure; public string hardware = "Quest 3: NOT TESTED. Perspective captures are Editor simulations."; }
    [MenuItem("Tools/Experiment/Verify Pilot Card Readability")]
    public static void Run()
    {
        var report = new Report(); ProposedRevealVerification.Endpoint e = null;
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); report.checks.Add(name); }
        try
        {
            e = ProposedRevealVerification.CreateEndpoint(1, 2, true);
            var sets = e.components.OfType<PilotCardSetController>().Single();
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/PilotCards/PilotJapanese SDF.asset");
            var rows = File.ReadAllLines("coplace_pilot_kit/metadata/card_manifest.csv").Skip(1).Select(l => l.Split(',')).ToArray();
            foreach (string set in new[] { "A", "B", "A" })
            {
                sets.ApplySessionSet(set); e.placements.ResetLocalSession(); Call(e.privateBoard, "ResetLocalBoard");
                string[] ids = e.answers.AnswerCardIds.ToArray();
                foreach (var row in rows.Where(r => r[0] == set))
                {
                    string id = row[1], name = row[2];
                    var shared = e.placements.GetCard(id).gameObject;
                    var local = e.components.OfType<PrivateAnswerCard>().Single(c => c.CardId == id).gameObject;
                    foreach (var go in new[] { shared, local })
                    {
                        var size = Vector3.Scale(go.GetComponent<BoxCollider>().size, go.transform.lossyScale);
                        Check(Mathf.Abs(size.x - 0.54f) < 0.001f && Mathf.Abs(size.y - 0.22f) < 0.001f, set + " " + go.name + " physical collider 54x22 cm");
                        var face = go.GetComponent<PilotCardFace>(); face.SetVisible(true); var title = face.EventName;
                        Canvas.ForceUpdateCanvases(); title.ForceMeshUpdate(true, true);
                        Check(title.text == name && !title.isTextOverflowing && title.textInfo.characterInfo.Take(title.textInfo.characterCount).Count(c => c.isVisible) == name.Length,
                            set + " " + go.name + " complete event name, no truncation");
                        Check(title.fontStyle.HasFlag(FontStyles.Bold) && !title.enableAutoSizing && title.fontSize == 42, set + " " + go.name + " fixed bold TMP title");
                        Check(title.rectTransform.lossyScale.x > 0.00099f && title.rectTransform.lossyScale.x < 0.00101f, set + " " + go.name + " same world text scale");
                        var photo = go.GetComponentInChildren<UnityEngine.UI.RawImage>(true);
                        float originalAspect = photo.texture.width * photo.uvRect.width / (photo.texture.height * photo.uvRect.height);
                        float displayAspect = photo.rectTransform.rect.width / photo.rectTransform.rect.height;
                        Check(Mathf.Abs(originalAspect - displayAspect) < 0.001f, set + " " + go.name + " photograph aspect preserved");
                        Check(go.GetComponentsInChildren<UnityEngine.UI.Graphic>(true).All(g => !g.raycastTarget) && go.GetComponentsInChildren<UnityEngine.UI.GraphicRaycaster>(true).Length == 0 && go.GetComponentsInChildren<Collider>(true).Length == 1,
                            set + " " + go.name + " face does not intercept XR selection");
                    }
                    var texture = shared.GetComponent<PilotCardFace>().SourceTexture;
                    Check(texture.width == 720 && texture.height == 480, set + " " + id + " imported original 720x480 without NPOT resizing");
                }
                foreach (var zone in e.components.OfType<TierZone>())
                {
                    // Inspect all eight fixed slots even when the zone is not currently active/visible.
                    var points = zone.snapPoints.Take(8).Select(t => t.position).ToArray();
                    Check(points.Select(p => Mathf.Round(p.x * 1000)).Distinct().Count() == 4 && points.Select(p => Mathf.Round(p.y * 1000)).Distinct().Count() == 2,
                        set + " " + zone.name + " stable 4x2 grid");
                    for (int i = 0; i < points.Length; i++)
                    {
                        var p = zone.transform.InverseTransformPoint(points[i]);
                        Check(Mathf.Abs(p.x * zone.transform.lossyScale.x) + 0.27f <= 1.35f && Mathf.Abs(p.y * zone.transform.lossyScale.y) + 0.11f <= 0.23f + 0.0001f,
                            set + " " + zone.name + " slot " + i + " stays inside board row");
                        for (int j = 0; j < i; j++)
                            Check(Mathf.Abs(points[i].x - points[j].x) >= 0.5399f || Mathf.Abs(points[i].y - points[j].y) >= 0.2199f,
                                set + " " + zone.name + " nonoverlapping slots " + j + "/" + i);
                    }
                }
                Check(ids.All(id => e.components.OfType<PrivateAnswerCard>().Single(c => c.CardId == id).transform.position.y - 0.11f >= 0.2f), set + " both private rows stay above floor");
                // Read-only titles share the source names and photo crop, at a larger fixed 4x2 layout.
                foreach (var row in rows.Where(r => r[0] == set))
                {
                    var go = new GameObject("ReadonlyTest", typeof(RectTransform), typeof(Canvas)); go.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace; UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, e.scene);
                    var rect = go.GetComponent<RectTransform>(); rect.sizeDelta = new Vector2(351.5f, 103.5f); rect.localScale = Vector3.one * 0.001f;
                    var title = PilotCardFace.Populate(rect, row[1], row[2], e.placements.GetCard(row[1]).GetComponent<PilotCardFace>().SourceTexture, font, true);
                    Canvas.ForceUpdateCanvases(); title.ForceMeshUpdate(true, true);
                    Check(title.text == row[2] && !title.isTextOverflowing && title.textInfo.characterInfo.Take(title.textInfo.characterCount).Count(c => c.isVisible) == row[2].Length,
                        set + " " + row[1] + " Reveal name complete with fixed 30pt text");
                    UnityEngine.Object.DestroyImmediate(go);
                }
                Call(e.privateBoard, "SetMainHidden", false);
                foreach (var card in e.placements.CardIds.Select(e.placements.GetCard)) card.GetComponent<PilotCardFace>().SetVisible(true);
                var privateContent = (GameObject)e.privateBoard.GetType().GetField("contentRoot", Flags).GetValue(e.privateBoard);
                privateContent.SetActive(false);
                var sharedCards = ids.Select(id => e.placements.GetCard(id).gameObject).ToArray();
                Capture(e, sharedCards, set + "-Shared", 1f); Capture(e, sharedCards, set + "-Shared", 1.5f);
                Call(e.privateBoard, "SetMainHidden", true); privateContent.SetActive(true);
                var localCards = e.components.OfType<PrivateAnswerCard>().Where(c => ids.Contains(c.CardId)).Select(c => c.gameObject).ToArray();
                Capture(e, localCards, set + "-Private", 1f); Capture(e, localCards, set + "-Private", 1.5f);
                foreach (var card in sharedCards)
                    Check(!card.GetComponentInChildren<Canvas>(true).gameObject.activeSelf, set + " " + card.name + " Main face hidden during independent answering");
            }
            sets.ApplySessionSet("Development"); e.placements.ResetLocalSession(); Call(e.privateBoard, "ResetLocalBoard");
            Check(e.placements.CardIds.Count == 3 && e.placements.CardIds.All(id => Mathf.Abs(e.placements.GetCard(id).transform.lossyScale.x - 0.25f) < 0.001f), "Development returns to original 3 food cards and 25cm scale");
            report.passed = true;
        }
        catch (Exception error) { report.failure = error.ToString(); Debug.LogException(error); }
        finally
        {
            if (e != null)
            {
                foreach (var c in e.scene.GetRootGameObjects().SelectMany(o => o.GetComponentsInChildren<MonoBehaviour>(true)))
                    try { c.GetType().GetMethod("OnDisable", Flags)?.Invoke(c, null); } catch { }
                EditorSceneManager.ClosePreviewScene(e.scene);
            }
            Directory.CreateDirectory("Documentation"); File.WriteAllText("Documentation/PilotCardReadabilityVerification.json", JsonUtility.ToJson(report, true));
        }
        if (!report.passed) throw new InvalidOperationException(report.failure);
        Debug.Log("[Pilot Readability Verification] PASS " + report.checks.Count + " checks; Quest hardware not tested");
    }
    private static void Capture(ProposedRevealVerification.Endpoint e, GameObject[] cards, string label, float distance)
    {
        var go = new GameObject("ReadabilityCamera"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, e.scene);
        var camera = go.AddComponent<Camera>(); camera.enabled = false; camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(e.scene);
        Vector3 center = cards.Select(c => c.transform.position).Aggregate(Vector3.zero, (a, b) => a + b) / cards.Length;
        camera.transform.position = center + new Vector3(0, 0, -distance - 0.0156f); camera.transform.rotation = Quaternion.identity;
        camera.fieldOfView = 75; camera.nearClipPlane = 0.03f; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(0.02f, 0.03f, 0.04f);
        var render = new RenderTexture(1600, 1000, 24); var image = new Texture2D(1600, 1000, TextureFormat.RGB24, false); var previous = RenderTexture.active;
        try
        {
            camera.targetTexture = render; camera.Render(); RenderTexture.active = render; image.ReadPixels(new Rect(0, 0, 1600, 1000), 0, 0); image.Apply();
            Directory.CreateDirectory("Library/PilotReadability"); File.WriteAllBytes("Library/PilotReadability/" + label + "-" + distance.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "m.png", image.EncodeToPNG());
        }
        finally { RenderTexture.active = previous; camera.targetTexture = null; UnityEngine.Object.DestroyImmediate(image); UnityEngine.Object.DestroyImmediate(render); UnityEngine.Object.DestroyImmediate(go); }
    }
    static void Call(object target, string method, params object[] args) => target.GetType().GetMethod(method, Flags).Invoke(target, args);
}
#endif
