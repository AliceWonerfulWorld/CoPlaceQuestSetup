using System;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

// PC-only study identifiers. Never sent to participants or used as protocol IDs.
[Serializable]
public sealed class ExperimentDataPlan
{
    public string experimentType = "Test", pairId = "", participant1Id = "", participant2Id = "", allocationGroup = "", notes = "";
    public int trialOrder = 1;
    public ExperimentDataPlan Copy() => JsonUtility.FromJson<ExperimentDataPlan>(JsonUtility.ToJson(this));
    public string Signature => JsonUtility.ToJson(this);
    public static bool SafeId(string value) => value != null && Regex.IsMatch(value,"^[A-Za-z0-9][A-Za-z0-9_-]{0,39}$");
    public static bool ParticipantId(string value) => value != null && Regex.IsMatch(value,"^P[0-9]{3,12}$");
    public bool TryCondition(out ExperimentManager.ExperimentMode mode,out string set)
    {
        mode=ExperimentManager.ExperimentMode.Normal;set="A";
        if(trialOrder<1 || trialOrder>2)return false;
        switch(allocationGroup)
        {
            case "G1": mode=trialOrder==1?ExperimentManager.ExperimentMode.Normal:ExperimentManager.ExperimentMode.Proposed;set=trialOrder==1?"A":"B";return true;
            case "G2": mode=trialOrder==1?ExperimentManager.ExperimentMode.Proposed:ExperimentManager.ExperimentMode.Normal;set=trialOrder==1?"A":"B";return true;
            case "G3": mode=trialOrder==1?ExperimentManager.ExperimentMode.Normal:ExperimentManager.ExperimentMode.Proposed;set=trialOrder==1?"B":"A";return true;
            case "G4": mode=trialOrder==1?ExperimentManager.ExperimentMode.Proposed:ExperimentManager.ExperimentMode.Normal;set=trialOrder==1?"B":"A";return true;
            default:return false;
        }
    }
    public string Validate(int count,ExperimentManager.ExperimentMode mode,string set)
    {
        if(experimentType!="Test" && experimentType!="Pilot" && experimentType!="Main")return "実験区分を選択してください";
        if(notes==null || notes.Length>1000 || notes.Any(c=>char.IsControl(c)&&c!='\n'&&c!='\r'&&c!='\t'))return "備考は1000文字以内で入力してください";
        if(experimentType=="Test")return "";
        if(!SafeId(pairId))return "ペアIDは半角英数字・ハイフン・下線で入力してください";
        if(pairId!=pairId.ToUpperInvariant())return "ペアIDの英字は大文字に統一してください（例：PAIR001）";
        if(count<1 || count>2)return "Pilot／Mainの参加者数は1人または2人にしてください";
        if(!ParticipantId(participant1Id) || (count==2 && !ParticipantId(participant2Id)))return "参加者IDはP001のような匿名番号で入力してください";
        if(count==1 && participant2Id!="")return "1人構成では参加者2のIDを空にしてください";
        if(count==2 && participant1Id==participant2Id)return "参加者IDが重複しています";
        if(!TryCondition(out var expectedMode,out var expectedSet))return "割当G1〜G4と試行順序1／2を選択してください";
        if(mode!=expectedMode || set!=expectedSet)return "方式・セットが割当と一致していません";
        return "";
    }
}
