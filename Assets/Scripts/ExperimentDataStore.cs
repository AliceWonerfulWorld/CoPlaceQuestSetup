using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

// Additional sidecars only. Existing CSV schemas and legacy folders are untouched.
public sealed class ExperimentDataStore
{
    [Serializable] public sealed class Metadata
    {
        public int schemaVersion=1;
        public string sessionId,condition,cardSetId,createdAtUtc,relativePath,appVersion;
        public long epoch;
        public int expectedParticipantCount,actualParticipantCount;
        public ExperimentDataPlan plan=new ExperimentDataPlan();
        public string startedAtUtc="",finishedAtUtc="",status="preparing",saveStatus="pending",exclusionReason="";
        public int[] participantsAtStart;
        public Metadata Copy()=>JsonUtility.FromJson<Metadata>(JsonUtility.ToJson(this));
    }
    [Serializable] public sealed class Binding
    {
        public string sessionId,participantId,deviceKey,confirmedAtUtc,observerRunId,reason;
        public int clientNo; public long epoch; public string role="Participant";
    }
    public static readonly string[] IndexColumns={"schema_version","session_id","session_epoch","experiment_type","pair_id","participant_1_id","participant_2_id","allocation_group","trial_order","condition","card_set_id","expected_participant_count","actual_participant_count","created_at_utc","started_at_utc","finished_at_utc","status","save_status","log_path","app_version","exclusion_reason","notes"};
    public static readonly string[] BindingColumns={"schema_version","session_id","session_epoch","participant_id","participant_client_id","device_key","confirmed_at_utc","observer_run_id","reason","role_at_confirmation"};
    readonly string root;
    public string IndexError {get;private set;}="";
    public ExperimentDataStore(string rootDirectory){root=Path.GetFullPath(rootDirectory);}
    public static string RelativePath(Metadata m)
    {
        if(!ExperimentDataPlan.SafeId(m.sessionId))throw new InvalidDataException("Invalid Session ID");
        if(m.plan.experimentType!="Test" && m.plan.experimentType!="Pilot" && m.plan.experimentType!="Main")throw new InvalidDataException("Invalid experiment type");
        if((m.condition!="Normal"&&m.condition!="Proposed") || (m.cardSetId!="A"&&m.cardSetId!="B"&&m.cardSetId!="Development"))throw new InvalidDataException("Invalid condition / set");
        if(m.plan.experimentType=="Test")return Path.Combine("Test",DateTimeOffset.Parse(m.createdAtUtc,CultureInfo.InvariantCulture).UtcDateTime.ToString("yyyyMMdd_HHmmss_fff",CultureInfo.InvariantCulture)+"_"+m.condition+"_"+m.cardSetId+"_"+m.sessionId);
        if(!ExperimentDataPlan.SafeId(m.plan.pairId))throw new InvalidDataException("Invalid Pair ID");
        return Path.Combine(m.plan.experimentType,"Pair_"+m.plan.pairId,m.plan.trialOrder.ToString("D2",CultureInfo.InvariantCulture)+"_"+m.condition+"_"+m.cardSetId+"_"+m.sessionId);
    }
    public string DirectoryFor(Metadata m)
    {
        if(m==null || m.schemaVersion!=1 || m.epoch<=0 || m.relativePath!=RelativePath(m))throw new InvalidDataException("Invalid session metadata path");
        if(m.expectedParticipantCount<1 || m.plan.Validate(m.expectedParticipantCount,(ExperimentManager.ExperimentMode)Enum.Parse(typeof(ExperimentManager.ExperimentMode),m.condition),m.cardSetId)!="")throw new InvalidDataException("Invalid study identifiers / allocation");
        var path=Path.GetFullPath(Path.Combine(root,m.relativePath));
        if(!path.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.Ordinal))throw new InvalidDataException("Path escaped log root");
        for(var part=path;part!=root;part=Path.GetDirectoryName(part))
            if((Directory.Exists(part)||File.Exists(part)) && (File.GetAttributes(part)&FileAttributes.ReparsePoint)!=0)throw new IOException("Symbolic links are not allowed in session paths");
        return path;
    }
    static bool SameIdentity(Metadata a,Metadata b)=>a.sessionId==b.sessionId && a.epoch==b.epoch && a.condition==b.condition && a.cardSetId==b.cardSetId && a.createdAtUtc==b.createdAtUtc && a.relativePath==b.relativePath && a.expectedParticipantCount==b.expectedParticipantCount && a.appVersion==b.appVersion && a.plan.Signature==b.plan.Signature;
    public void Save(Metadata m)
    {
        string folder=DirectoryFor(m),path=Path.Combine(folder,"session_metadata.json");
        if(File.Exists(path)||File.Exists(path+".bak"))
        {
            var existing=Load(path);
            if(!SameIdentity(existing,m))throw new InvalidDataException("Immutable session identity conflicts with metadata");
        }
        if(!File.Exists(path) && !File.Exists(path+".bak") && ReadAll().Any(x=>x.sessionId==m.sessionId))throw new InvalidDataException("Session ID already has another directory");
        Directory.CreateDirectory(folder);AtomicText(path,JsonUtility.ToJson(m,true));
    }
    public Metadata Find(string id)
    {
        var found=ReadAll().Where(m=>m.sessionId==id).ToArray();
        if(found.Length>1)throw new InvalidDataException("Duplicate Session ID in metadata");
        return found.SingleOrDefault();
    }
    Metadata Load(string path)
    {
        Metadata Parse(string file)
        {
            var m=JsonUtility.FromJson<Metadata>(File.ReadAllText(file,Encoding.UTF8));
            if(m==null || Path.GetFullPath(Path.GetDirectoryName(path))!=DirectoryFor(m))throw new InvalidDataException("Metadata location mismatch");
            return m;
        }
        try{return Parse(path);}catch{if(!File.Exists(path+".bak"))throw;return Parse(path+".bak");}
    }
    public List<Metadata> ReadAll()
    {
        var result=new List<Metadata>();
        foreach(string type in new[]{"Test","Pilot","Main"})
        {
            var folder=Path.Combine(root,type);if(!Directory.Exists(folder))continue;
            // Do not traverse links or scan/migrate legacy Session-ID directories.
            var pending=new Stack<string>();pending.Push(folder);
            while(pending.Count>0)
            {
                string dir=pending.Pop();if((File.GetAttributes(dir)&FileAttributes.ReparsePoint)!=0)throw new IOException("Linked metadata directory");
                if(File.Exists(Path.Combine(dir,"session_metadata.json")) || File.Exists(Path.Combine(dir,"session_metadata.json.bak")))result.Add(Load(Path.Combine(dir,"session_metadata.json")));
                foreach(string child in Directory.GetDirectories(dir))pending.Push(child);
            }
        }
        if(result.Select(m=>m.sessionId).Distinct().Count()!=result.Count)throw new InvalidDataException("Duplicate Session IDs");
        return result;
    }
    public string ValidateCohort(ExperimentDataPlan plan)
    {
        if(plan.experimentType=="Test")return "";
        foreach(var m in ReadAll().Where(m=>m.plan.experimentType==plan.experimentType))
        {
            var a=new[]{m.plan.participant1Id,m.plan.participant2Id}.Where(x=>!string.IsNullOrEmpty(x)).OrderBy(x=>x).ToArray();
            var b=new[]{plan.participant1Id,plan.participant2Id}.Where(x=>!string.IsNullOrEmpty(x)).OrderBy(x=>x).ToArray();
            if(m.plan.pairId==plan.pairId && (!a.SequenceEqual(b)||m.plan.allocationGroup!=plan.allocationGroup))return "同じペアIDの参加者・割当が過去の記録と異なります";
            if(m.plan.pairId!=plan.pairId && a.Intersect(b).Any())return "参加者IDが別ペアで使用されています";
        }
        return "";
    }
    public bool RebuildIndex()
    {
        try
        {
            var rows=new List<string[]>{IndexColumns};
            foreach(var m in ReadAll().OrderBy(m=>m.createdAtUtc,StringComparer.Ordinal).ThenBy(m=>m.sessionId,StringComparer.Ordinal))
                rows.Add(new[]{"1",m.sessionId,m.epoch.ToString(CultureInfo.InvariantCulture),m.plan.experimentType,m.plan.pairId,m.plan.participant1Id,m.plan.participant2Id,m.plan.allocationGroup,m.plan.trialOrder.ToString(CultureInfo.InvariantCulture),m.condition,m.cardSetId,m.expectedParticipantCount.ToString(CultureInfo.InvariantCulture),m.startedAtUtc==""?"":m.actualParticipantCount.ToString(CultureInfo.InvariantCulture),m.createdAtUtc,m.startedAtUtc,m.finishedAtUtc,m.status,m.saveStatus,m.relativePath,m.appVersion,m.exclusionReason,m.plan.notes});
            Directory.CreateDirectory(root);WriteCsv(Path.Combine(root,"session_index.csv"),rows);IndexError="";return true;
        }
        catch(Exception e){IndexError=e.Message;return false;}
    }
    public void AppendBinding(Metadata m,Binding b)
    {
        if(b==null || b.sessionId!=m.sessionId || b.epoch!=m.epoch || b.clientNo<=0 || b.participantId!="" && (!new[]{m.plan.participant1Id,m.plan.participant2Id}.Contains(b.participantId)||!ExperimentDataPlan.ParticipantId(b.participantId)||b.role!="Participant"))throw new InvalidDataException("Invalid participant binding");
        var path=Path.Combine(DirectoryFor(m),"participant_bindings.csv");var rows=File.Exists(path)?ExperimentCsvLogStore.ReadCsv(path):new List<string[]>{BindingColumns};
        if(rows.Count==0 || !rows[0].SequenceEqual(BindingColumns))throw new InvalidDataException("Unsupported binding file");
        string[] row={"1",b.sessionId,b.epoch.ToString(CultureInfo.InvariantCulture),b.participantId,b.clientNo.ToString(CultureInfo.InvariantCulture),b.deviceKey,b.confirmedAtUtc,b.observerRunId,b.reason,b.role};
        if(rows.Skip(1).Any(r=>r.SequenceEqual(row) || b.participantId=="" && r.Length==BindingColumns.Length && r[1]==b.sessionId && r[4]==b.clientNo.ToString(CultureInfo.InvariantCulture) && r[5]==b.deviceKey && r[8]==b.reason))return;
        rows.Add(row);WriteCsv(path,rows);
    }
    public static void WriteCsv(string path,IEnumerable<string[]> rows)=>AtomicText(path,string.Concat(rows.Select(row=>string.Join(",",row.Select(v=>"\""+(v??"").Replace("\"","\"\"")+"\""))+"\r\n")));
    public static void AtomicText(string path,string value)
    {
        string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            using(var stream=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            {using(var writer=new StreamWriter(stream,new UTF8Encoding(true),1024,true)){writer.Write(value);writer.Flush();}stream.Flush(true);}
            if(File.Exists(path))File.Replace(temp,path,path+".bak");else File.Move(temp,path);
        }
        finally{if(File.Exists(temp))File.Delete(temp);}
    }
}
