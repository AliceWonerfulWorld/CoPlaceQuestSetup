#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using TMPro;
using UnityEditor;
using UnityEngine;

// Only builds the dedicated static font. Does not rerun scene setup.
public static class ExperimenterControlPanelSetup
{
    [MenuItem("Tools/Experiment/Update Management Japanese Font")]
    public static void UpdateFont()
    {
        const string path = "Assets/Resources/ExperimenterJapanese SDF.asset";
        Directory.CreateDirectory("Assets/Resources"); AssetDatabase.Refresh();
        string[] sources = { "SessionResetUI", "ExperimentSessionManager", "ExperimentLobbyController", "ExperimentLogger", "ExperimentDataManagementUI", "ExperimentDataPlan", "ExperimentDataManagement" };
        string glyphs = string.Concat(sources.Select(n => File.ReadAllText("Assets/Scripts/" + n + ".cs")));
        glyphs += string.Concat(Enumerable.Range(32,95).Select(x => (char)x));
        glyphs = new string(glyphs.Where(c => !char.IsControl(c)).Distinct().OrderBy(c => c).ToArray());
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
        if (font == null)
        {
            font = TMP_FontAsset.CreateFontAsset(AssetDatabase.LoadAssetAtPath<Font>("Assets/Fonts/IndependentAnswer/NotoSansJP.ttf"), 48, 6, UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, 2048, 2048);
            font.name = "ExperimenterJapanese SDF";
            AssetDatabase.CreateAsset(font, path); AssetDatabase.AddObjectToAsset(font.material, font);
            foreach (var atlas in font.atlasTextures) AssetDatabase.AddObjectToAsset(atlas, font);
        }
        font.ReadFontAssetDefinition();
        if (font.HasCharacters(glyphs)) { font.atlasPopulationMode = AtlasPopulationMode.Static; EditorUtility.SetDirty(font); AssetDatabase.SaveAssets(); return; }
        font.atlasPopulationMode = AtlasPopulationMode.Dynamic;
        typeof(TMP_FontAsset).GetProperty("SourceFont_EditorRef", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(font, AssetDatabase.LoadAssetAtPath<Font>("Assets/Fonts/IndependentAnswer/NotoSansJP.ttf"));
        if (!font.TryAddCharacters(glyphs, out string missing)) throw new InvalidOperationException("Management glyphs missing: " + missing);
        font.atlasPopulationMode = AtlasPopulationMode.Static;
        EditorUtility.SetDirty(font); AssetDatabase.SaveAssets();
    }
}
#endif
