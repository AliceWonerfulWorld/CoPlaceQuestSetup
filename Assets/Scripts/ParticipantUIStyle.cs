using System.Runtime.CompilerServices;
using TMPro;
using UnityEngine;

// Presentation only: protocol values and study conditions are unchanged.
public static class ParticipantUIStyle
{
    private static TMP_FontAsset font;
    private static readonly ConditionalWeakTable<Transform, object> styled = new ConditionalWeakTable<Transform, object>();
    public static TMP_FontAsset Font => font != null ? font : (font = Resources.Load<TMP_FontAsset>("ParticipantJapanese SDF"));
    public const string SharedInstruction = "参加者同士で話し合い、最終的な分類結果を決めてください。";
    public static void Apply(Transform root)
    {
        if (root == null || Font == null || styled.TryGetValue(root, out _)) return;
        foreach (var text in root.GetComponentsInChildren<TMP_Text>(true))
        {
            if (text.GetComponentInParent<PilotCardFace>() != null || text.transform.parent != null && text.transform.parent.name == "CardName") continue;
            text.font = Font; text.text = Translate(text.text); text.enableAutoSizing = false;
        }
        styled.Add(root, new object());
    }
    public static string Translate(string text)
    {
        switch (text.Trim())
        {
            case "TIER BOARD": return "共同分類";
            case "Place a card": return SharedInstruction;
            case "PRIVATE ANSWER": return "独立回答";
            case "Only your answers are visible. Grab any card. Return to Unclassified to cancel.": return "画像カードを自分の考えに基づいて分類してください。";
            case "EXPERIMENT INTRODUCTION": return "実験の説明";
            case "CONNECTION": return "接続状態";
            case "TIER": return "分類先";
            case "RETURN": case "Unclassified": case "U": return "未分類";
            case "FINAL RESULT": return ExperimentResultUI.Title;
            case "FINAL AGREEMENT": return "最終結果への同意";
            case "This board is OK": return "この配置に同意する";
            case "この回答で確定": return "回答を提出";
            case "確定を取り消す": return "提出を取り消す";
            default: return text;
        }
    }
    public static string AgreementMessage(string message)
    {
        switch (message)
        {
            case "Unclassified cards remain. Place every card in A-E.": return "未分類のカードをすべて配置してください。";
            case "Release all cards before confirming": return "カードを離してください。";
            case "Waiting for placement synchronization":
            case "Unclassified cards remain or placement synchronization is pending": return "配置の同期を待っています。";
            case "Ready send failed": return "同意を送信できません。接続を確認してください。";
            case "Board confirmed": return "分類結果が確定しました。";
            case "Board result exceeds NetSync variable limit": return "結果の同期を完了できません。実験者にお知らせください。";
            case "": return "";
            default: return "同期状態を確認しています。改善しない場合は実験者にお知らせください。";
        }
    }
}
