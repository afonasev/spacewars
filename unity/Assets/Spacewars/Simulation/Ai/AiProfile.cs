using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Spacewars.Simulation.Ai
{
    public enum AiDifficulty { Recruit, Fighter, Veteran }
    [Serializable] public sealed class AiNumericValue { public string path; public double value; }
    [Serializable] public sealed class AiProfileData
    {
        public int schemaVersion=1,revision=1;
        public string id="native-strategic-ai-v1";
        public AiNumericValue[] fields;
    }
    public sealed class AiProfile
    {
        private readonly Dictionary<string,double> values;
        public string Id {get;} public int Revision {get;} public string Hash {get;}
        public static AiProfile Initial=>new AiProfile(new AiProfileData{fields=AiProfileMetadata.Fields.Select(f=>new AiNumericValue{path=f.Path,value=f.Initial}).ToArray()});
        public AiProfile(AiProfileData data)
        {
            if(data==null||data.schemaVersion!=1||string.IsNullOrWhiteSpace(data.id)||data.revision<1||data.fields==null)throw new ArgumentException("Invalid native AI profile identity.");
            if(data.fields.Any(f=>f==null||f.path==null)||data.fields.Select(f=>f.path).Distinct(StringComparer.Ordinal).Count()!=data.fields.Length)throw new ArgumentException("Duplicate/missing AI field.");
            values=new Dictionary<string,double>(StringComparer.Ordinal);
            foreach(var f in data.fields)
            {
                if(f==null||f.path==null||values.ContainsKey(f.path))throw new ArgumentException("Duplicate/missing AI field.");
                var descriptor=AiProfileMetadata.Fields.SingleOrDefault(x=>x.Path==f.path);
                if(descriptor==null)throw new ArgumentException("Unknown AI field: "+f.path);
                descriptor.Validate(f.value);values.Add(f.path,f.value);
            }
            if(values.Count!=AiProfileMetadata.Fields.Count)throw new ArgumentException("Incomplete AI profile.");
            foreach(var field in AiProfileMetadata.Fields.Where(f=>f.Unit=="s"))SecondsToTicks(values[field.Path],30);
            ValidateGroups();Id=data.id;Revision=data.revision;
            Hash=Digest(Id+"\n"+Revision+"\n"+string.Join("\n",values.OrderBy(v=>v.Key,StringComparer.Ordinal).Select(v=>v.Key+"="+v.Value.ToString("R",CultureInfo.InvariantCulture))));
        }
        public double Value(string path)=>values[path];
        public double DifficultyValue(AiDifficulty difficulty,string name)=>Value("difficulty."+DifficultyId(difficulty)+"."+name);
        public static string DifficultyId(AiDifficulty difficulty)
        {if(!Enum.IsDefined(typeof(AiDifficulty),difficulty))throw new ArgumentException("Unknown difficulty.");return difficulty.ToString().ToLowerInvariant();}
        public static long SecondsToTicks(double seconds,double tickRate)
        {
            if(double.IsNaN(seconds)||double.IsInfinity(seconds)||seconds<0||double.IsNaN(tickRate)||double.IsInfinity(tickRate)||tickRate<=0||seconds*tickRate>=long.MaxValue)throw new ArgumentOutOfRangeException(nameof(seconds));
            return seconds==0?0:Math.Max(1,(long)Math.Ceiling(seconds*tickRate));
        }
        public AiProfileData CopyData()=>new AiProfileData{id=Id,revision=Revision,fields=values.OrderBy(v=>v.Key,StringComparer.Ordinal).Select(v=>new AiNumericValue{path=v.Key,value=v.Value}).ToArray()};
        private void ValidateGroups()
        {
            foreach(var name in new[]{"decisionSeconds","reactionSeconds","estimateNoise","actionsPerDecision","scoutAssignments"})
            {
                double a=DifficultyValue(AiDifficulty.Recruit,name),b=DifficultyValue(AiDifficulty.Fighter,name),c=DifficultyValue(AiDifficulty.Veteran,name);
                bool descending=name=="decisionSeconds"||name=="reactionSeconds"||name=="estimateNoise";
                if(descending?(a<b||b<c):(a>b||b>c))throw new ArgumentException("Difficulty ordering: "+name);
                if(name.EndsWith("Seconds")&&(SecondsToTicks(a,30)<SecondsToTicks(b,30)||SecondsToTicks(b,30)<SecondsToTicks(c,30)))throw new ArgumentException("Rounded difficulty ordering.");
            }
            foreach(var phase in new[]{"early","mid","late"})
                if(new[]{"recon","line","support"}.Sum(r=>Value("composition."+phase+"."+r+"BudgetShare"))<=0)throw new ArgumentException("Empty composition: "+phase);
            if(new[]{"threat","economicValue","reachability","opportunity"}.Sum(r=>Value("utility.target."+r))<=0||Value("utility.scout.informationGain")<=0)throw new ArgumentException("Empty utility weights.");
        }
        internal static string Digest(string text)
        {using(var hash=SHA256.Create())return string.Concat(hash.ComputeHash(Encoding.UTF8.GetBytes(text)).Select(b=>b.ToString("x2",CultureInfo.InvariantCulture)));}
    }
}
