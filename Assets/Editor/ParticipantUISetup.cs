#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class ParticipantUISetup
{
    [MenuItem("Tools/Experiment/Update Participant Japanese UI")]
    public static void Run()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play before updating participant UI");
        UpdateFont();
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/QuestSetup.unity") throw new InvalidOperationException("Open QuestSetup first");
        foreach (var root in scene.GetRootGameObjects())
        {
            if (root.name == "TierBoard" || root.GetComponent<PrivateTierBoardController>() != null || root.GetComponent<ExperimentLobbyUI>() != null || root.GetComponent<TierZone>() != null)
                ApplyPresentation(root.transform);
            var debug = root.GetComponent<CandidateSyncTest>();
            if (debug != null)
            {
                var so = new SerializedObject(debug); var label = so.FindProperty("candidateStatusText").objectReferenceValue as TMP_Text;
                if (label != null) { label.text = ""; label.enabled = false; EditorUtility.SetDirty(label); }
            }
        }
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
    }
    public static void ApplyPresentation(Transform root)
    {
        ParticipantUIStyle.Apply(root);
        foreach (var t in root.GetComponentsInChildren<TMP_Text>(true))
        {
            if (t.transform.parent != null && t.transform.parent.name == "CardName") continue;
            // No card geometry, slots or interactors are changed.
            var zone = t.GetComponentInParent<TierZone>();
            if(zone != null && (t.text.Trim()=="U" || t.text.Trim()=="未分類" || "ABCDE".Contains(t.text.Trim()) && t.text.Trim().Length==1))
                t.text=zone.tierName=="Unclassified" ? "未分類" : zone.tierName;
            if (t.name == "TierLabel" && t.transform.parent.name != "UnclassifiedRow") t.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,160);
            if (t.name == "TierLabel" && t.transform.parent.name == "UnclassifiedRow") { t.text = "未分類"; t.fontSize = 32; }
            if (t.name == "Title" && t.transform.parent.name == "TierBoardCanvas") { t.text = t.GetComponentInParent<PrivateTierBoardController>() != null ? "独立回答" : "共同分類"; t.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,128); }
            if (t.name == "Hint" && t.transform.parent.name == "TierBoardCanvas") { t.fontSize = 42; t.textWrappingMode = TextWrappingModes.Normal; }
            if (t.name == "Title" && t.transform.parent.name == "ResultContent") t.text = ExperimentResultUI.Title;
            if (t.name == "Title" && t.transform.parent.name == "ParticipantIntroduction") t.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,152);
            if (t.name == "Description" && t.transform.parent.name == "ParticipantIntroduction") t.text = ExperimentLobbyUI.GeneralDescription;
            if (t.transform.parent.name == "CurrentCondition" && t.name == "Heading") t.text = "提案方式 ／ 独立回答 → 共同分類";
            if (t.transform.parent.name == "Participants" && t.name == "Status") t.text = "参加者の接続を待っています。";
            if (t.transform.parent.name == "Connection" && t.name == "Status") t.text = "未接続";
            if (t.transform.parent.name == "Waiting") t.text = "実験の準備をしています。";
            EditorUtility.SetDirty(t);
        }
        foreach (var result in root.GetComponentsInChildren<ExperimentResultUI>(true)) result.ConfigurePresentation();
        foreach (var ui in root.GetComponentsInChildren<FinalAgreementUI>(true))
        {
            var main = root.GetComponentsInChildren<Canvas>(true).FirstOrDefault(c=>c.name=="TierBoardCanvas");
            if(main != null)
            {
                var corners=new Vector3[4];((RectTransform)main.transform).GetWorldCorners(corners);
                float halfWidth=((RectTransform)ui.transform).rect.width*ui.transform.lossyScale.x/2;
                Vector3 position=ui.transform.position;
                float left=Vector3.Dot((corners[0]+corners[1])*.5f-main.transform.position,main.transform.right);
                float current=Vector3.Dot(position-main.transform.position,main.transform.right);
                ui.transform.position=position+main.transform.right*(left-halfWidth-.12f-current);
            }
            var so = new SerializedObject(ui); var status = (TMP_Text)so.FindProperty("statusText").objectReferenceValue;
            status.fontSize = 32; status.textWrappingMode = TextWrappingModes.Normal;
            ((RectTransform)ui.transform).SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 580);
            status.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 420);
            EditorUtility.SetDirty(status);
        }
        foreach (var ui in root.GetComponentsInChildren<IndependentAnswerSubmissionUI>(true))
        {
            var so = new SerializedObject(ui); var status = (TMP_Text)so.FindProperty("status").objectReferenceValue;
            status.fontSize = 32; status.textWrappingMode = TextWrappingModes.Normal;
            status.text = "独立回答 ／ 分類済み 0枚\n未分類のカードをすべて配置してください。";
            EditorUtility.SetDirty(status);
        }
    }
    public static void UpdateFont()
    {
        const string path = "Assets/Resources/ParticipantJapanese SDF.asset";
        string glyphs = string.Concat(new[] {"ParticipantUIStyle","ExperimentLobbyUI","IndependentAnswerSubmissionUI","PrivateTierBoardController","ReadonlyParticipantBoardView","FinalAgreementUI","ExperimentResultUI"}.Select(n=>File.ReadAllText("Assets/Scripts/"+n+".cs")));
        foreach (string original in new[] {"Assets/Fonts/IndependentAnswer/LobbyJapanese SDF.asset","Assets/Fonts/IndependentAnswer/SubmitJapanese SDF.asset","Assets/Fonts/IndependentAnswer/ResultJapanese SDF.asset","Assets/PilotCards/PilotJapanese SDF.asset"})
        { var f=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(original); if(f!=null) glyphs+=string.Concat(f.characterTable.Select(c=>(char)c.unicode)); }
        glyphs = new string(glyphs.Where(c=>!char.IsControl(c)).Distinct().OrderBy(c=>c).ToArray());
        var font=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
        var source=AssetDatabase.LoadAssetAtPath<Font>("Assets/Fonts/IndependentAnswer/NotoSansJP.ttf");
        if(font==null)
        {
            font=TMP_FontAsset.CreateFontAsset(source,48,6,UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA,2048,2048);
            font.name="ParticipantJapanese SDF"; AssetDatabase.CreateAsset(font,path); AssetDatabase.AddObjectToAsset(font.material,font);
            foreach(var atlas in font.atlasTextures) AssetDatabase.AddObjectToAsset(atlas,font);
        }
        font.ReadFontAssetDefinition();
        if(!font.HasCharacters(glyphs))
        {
            font.atlasPopulationMode=AtlasPopulationMode.Dynamic;
            typeof(TMP_FontAsset).GetProperty("SourceFont_EditorRef",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).SetValue(font,source);
            if(!font.TryAddCharacters(glyphs,out string missing)) throw new InvalidOperationException("Participant font missing: "+missing);
        }
        font.atlasPopulationMode=AtlasPopulationMode.Static; EditorUtility.SetDirty(font); AssetDatabase.SaveAssets();
    }
}
#endif
