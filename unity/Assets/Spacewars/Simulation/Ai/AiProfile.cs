using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Spacewars.Simulation.Ai
{
    public enum AiDifficulty { Recruit, Fighter, Veteran, Expert, Cheater }
    [Serializable] public sealed class AiNumericValue { public string path; public double value; }
    [Serializable] public sealed class AiProfileData
    {
        public int schemaVersion=1,revision=1;
        public string id="native-strategic-ai-v1";
        public AiNumericValue[] fields;
    }
    public sealed class AiProfile
    {
        private static readonly HashSet<string> IncomeAdditions=new HashSet<string>(StringComparer.Ordinal)
        {
            "economy.continuityEnabled","economy.earlyMinePaybackSeconds","economy.minimumIncomeToSpendRatio","economy.surplusIncomeToSpendRatio",
            "technology.minimumIncomeBuildings","technology.mixedBaseIncomeBuildings",
            "technology.rocketExplorers","technology.massAirPercent","technology.massAirMinimum","technology.massAirPerOpponent",
            "openingsMidgame.outpostHomeBuildings","openingsMidgame.outpostTwoProducerShare","openingsMidgame.noOutpostTwoProducerShare",
            "openingsMidgame.earlyMineRaidValue","openingsMidgame.openingRelayValue","openingsMidgame.midgameRelayValue"
        };
        private static readonly HashSet<string> DefenseAdditions=new HashSet<string>(StringComparer.Ordinal)
        {
            "positionDevelopment.homeCombatLimit","positionDevelopment.outpostCombatLimit",
            "positionDevelopment.repairHomeBuildings","positionDevelopment.secondRepairBases",
            "positionDevelopment.retrofitSeconds","positionDevelopment.airIntelSeconds","positionDevelopment.veteranSentryReserve"
        };
        private readonly Dictionary<string,double> values;
        public string Id {get;} public int Revision {get;} public string Hash {get;}
        // Values and identity are immutable; CopyData remains deeply detached.
        // Ledger defaults and detached owners must not rebuild/hash all fields.
        private static readonly Lazy<AiProfile> initial=new Lazy<AiProfile>(()=>new AiProfile(new AiProfileData{fields=AiProfileMetadata.Fields.Select(f=>new AiNumericValue{path=f.Path,value=f.Initial}).ToArray()}));
        public static AiProfile Initial=>initial.Value;
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
            foreach(var field in AiProfileMetadata.Fields.Where(f=>IsDefaultExtensionField(f.Path)))
                if(!values.ContainsKey(field.Path))values.Add(field.Path,field.Initial);
            if(values.Count!=AiProfileMetadata.Fields.Count)throw new ArgumentException("Incomplete AI profile.");
            foreach(var field in AiProfileMetadata.Fields.Where(f=>f.Unit=="s"&&(!f.InheritsWhenUnset||values[f.Path]!=f.Minimum)))SecondsToTicks(values[field.Path],30);
            ValidateGroups();Id=data.id;Revision=data.revision;
            Hash=Digest(Id+"\n"+Revision+"\n"+string.Join("\n",IdentityValues().OrderBy(v=>v.Key,StringComparer.Ordinal).Select(v=>v.Key+"="+v.Value.ToString("R",CultureInfo.InvariantCulture))));
        }
        // Default extensions keep the established opening seed binding and
        // saved default profile identity. Tuned values still contribute to identity.
        private IEnumerable<KeyValuePair<string,double>> IdentityValues()=>
            values.Where(v=>!IsDefaultExtensionField(v.Key)||v.Value!=AiProfileMetadata.Field(v.Key).Initial);
        internal static bool IsDefaultExtensionField(string path)=>path.StartsWith("turretBypass.",StringComparison.Ordinal)||IncomeAdditions.Contains(path)||DefenseAdditions.Contains(path)||path.StartsWith("difficulty.expert.",StringComparison.Ordinal)||path=="difficulty.cheater.incomeBonusPercent"||path=="scouting.maximumCombatHealthLossShare"||path.StartsWith("victoryConversion.",StringComparison.Ordinal);
        public double Value(string path)=>values[path];
        // The explicit legacy importer validates every historical value. Only
        // these two fields were absent; the live profile retains caller values.
        internal string BeforeExpansionSelectionHash=>Digest(Id+"\n"+Revision+"\n"+string.Join("\n",IdentityValues().Where(v=>v.Key!="economy.remoteExpansionShare"&&v.Key!="economy.enemyMainProtectedIncomeSites").OrderBy(v=>v.Key,StringComparer.Ordinal).Select(v=>v.Key+"="+v.Value.ToString("R",CultureInfo.InvariantCulture))));
        public double DifficultyValue(AiDifficulty difficulty,string name)
        {
            if(name=="opening.earlyGroundShare"||name=="opening.incomeBeforeReinforcement")
                return UsesExpertPolicy(difficulty)?Value("difficulty.expert."+name):0;
            if(name=="incomeBonusPercent")
                return UsesExpertPolicy(difficulty)?Value("difficulty."+DifficultyId(difficulty)+"."+name):0;
            if(UsesExpertPolicy(difficulty)&&values.TryGetValue("difficulty.expert."+name,out double expert)&&expert!=-1)return expert;
            return name.StartsWith("decision.",StringComparison.Ordinal)?Value(name):Value("difficulty."+DifficultyId(BehaviorDifficulty(difficulty))+"."+name);
        }
        public double IncomeMultiplier(AiDifficulty difficulty)=>1+DifficultyValue(difficulty,"incomeBonusPercent")/100d;
        public static bool UsesExpertPolicy(AiDifficulty difficulty)=>difficulty is AiDifficulty.Expert or AiDifficulty.Cheater;
        public static AiDifficulty BehaviorDifficulty(AiDifficulty difficulty)=>UsesExpertPolicy(difficulty)?AiDifficulty.Veteran:difficulty;
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
            if(Value("positionDevelopment.repairHealth")>=Value("positionDevelopment.repairReturnHealth"))throw new ArgumentException("Repair health hysteresis.");
            string prefix="openingsMidgame.";
            foreach(var group in new[]{new[]{"opening.aggressive","opening.doubleIncome","opening.greedy","opening.allIn","opening.air"},new[]{"ground.balanced","ground.tanks","ground.heavy","ground.explorers"},new[]{"combined.hawksTanks","combined.hawksExplorers","combined.balancedSmallAir","combined.heavySmallAir"},new[]{"personality.defensive","personality.aggressive","personality.harassing"},new[]{"personality.defensive","personality.aggressive"}})
                if(group.Sum(key=>Value(prefix+key))<=0)throw new ArgumentException("Empty opening/midgame weight group: "+group[0]);
            foreach(var range in new[]{new[]{"reinforcedMin","reinforcedMax"},new[]{"allInMin","allInMax"},new[]{"outpostMin","outpostMax"},new[]{"adaptation.firstMinSeconds","adaptation.firstMaxSeconds"},new[]{"airFirst","airCalm"},new[]{"airFactoryThreat","airFactoryCalm"}})
                if(Value(prefix+range[0])>Value(prefix+range[1]))throw new ArgumentException("Opening/midgame range: "+range[0]);
            foreach(var name in new[]{"decisionSeconds","reactionSeconds","estimateNoise","actionsPerDecision","scoutAssignments"})
            {
                double a=DifficultyValue(AiDifficulty.Recruit,name),b=DifficultyValue(AiDifficulty.Fighter,name),c=DifficultyValue(AiDifficulty.Veteran,name);
                bool descending=name=="decisionSeconds"||name=="reactionSeconds"||name=="estimateNoise";
                if(descending?(a<b||b<c):(a>b||b>c))throw new ArgumentException("Difficulty ordering: "+name);
                if(name.EndsWith("Seconds")&&(SecondsToTicks(a,30)<SecondsToTicks(b,30)||SecondsToTicks(b,30)<SecondsToTicks(c,30)))throw new ArgumentException("Rounded difficulty ordering.");
            }
            foreach(var difficulty in new[]{AiDifficulty.Recruit,AiDifficulty.Fighter,AiDifficulty.Veteran,AiDifficulty.Expert})
            {
                if(DifficultyValue(difficulty,"reactionSeconds")>DifficultyValue(difficulty,"control.reactionMaxSeconds")||
                    DifficultyValue(difficulty,"micro.shortStep")>DifficultyValue(difficulty,"micro.maxWithdrawal")||
                    DifficultyValue(difficulty,"micro.combatRepairHealth")>DifficultyValue(difficulty,"micro.repairReturnHealth")||
                    DifficultyValue(difficulty,"micro.idleRepairHealth")>DifficultyValue(difficulty,"micro.repairReturnHealth"))throw new ArgumentException("Combat control profile range.");
            }
            foreach(var phase in new[]{"early","mid","late"})
                if(new[]{"recon","line","support"}.Sum(r=>Value("composition."+phase+"."+r+"BudgetShare"))<=0)throw new ArgumentException("Empty composition: "+phase);
            if(new[]{"threat","economicValue","reachability","opportunity"}.Sum(r=>Value("utility.target."+r))<=0||Value("utility.scout.informationGain")<=0)throw new ArgumentException("Empty utility weights.");
        }
        internal static string Digest(string text)
        {using(var hash=SHA256.Create())return string.Concat(hash.ComputeHash(Encoding.UTF8.GetBytes(text)).Select(b=>b.ToString("x2",CultureInfo.InvariantCulture)));}
    }
}
