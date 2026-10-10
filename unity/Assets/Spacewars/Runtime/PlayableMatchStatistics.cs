using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    public enum MatchAiDecisionKind { Opening, OpeningCompleted, OpeningAborted, Strategy }
    public enum MatchFactKind { Income, UnitCompleted, BuildingCompleted, UnitDestroyed, BuildingDestroyed, Army, HumanAction, AiDecision }
    public sealed class MatchFact
    {
        public long Tick{get;} public long Sequence{get;} public double Seconds{get;} public PlayableOwner Owner{get;}
        public MatchFactKind Kind{get;} public double Value{get;} public int EntityKind{get;} public string Name{get;}
        internal MatchFact(long tick,long sequence,double seconds,PlayableOwner owner,MatchFactKind kind,double value,int entityKind=0,string name="")
        {Tick=tick;Sequence=sequence;Seconds=seconds;Owner=owner;Kind=kind;Value=value;EntityKind=entityKind;Name=name;}
    }
    public sealed class MatchResultPlayer
    {
        public int Slot{get;} public PlayableOwner Owner{get;} public int Team{get;} public string Id{get;} public string Name{get;} public bool IsAi{get;} public string AiIdentity{get;}
        public double EarnedCredits{get;} public int UnitsBuilt{get;} public int BuildingsBuilt{get;} public int UnitsDestroyed{get;} public int BuildingsDestroyed{get;} public long Score{get;}
        internal MatchResultPlayer(int slot,PlayableOwner owner,int team,string id,string name,bool ai,string identity,IEnumerable<MatchFact> facts,double divisor)
        {Slot=slot;Owner=owner;Team=team;Id=id;Name=name;IsAi=ai;AiIdentity=identity;var f=facts.Where(x=>x.Owner==owner).ToArray();EarnedCredits=f.Where(x=>x.Kind==MatchFactKind.Income).Sum(x=>x.Value);UnitsBuilt=f.Count(x=>x.Kind==MatchFactKind.UnitCompleted);BuildingsBuilt=f.Count(x=>x.Kind==MatchFactKind.BuildingCompleted);UnitsDestroyed=f.Count(x=>x.Kind==MatchFactKind.UnitDestroyed);BuildingsDestroyed=f.Count(x=>x.Kind==MatchFactKind.BuildingDestroyed);Score=(long)Math.Floor(EarnedCredits/divisor)+(long)f.Where(x=>x.Kind==MatchFactKind.UnitCompleted||x.Kind==MatchFactKind.BuildingCompleted||x.Kind==MatchFactKind.UnitDestroyed||x.Kind==MatchFactKind.BuildingDestroyed).Sum(x=>x.Value);}
        public double Column(int index)=>index==0?EarnedCredits:index==1?UnitsBuilt:index==2?BuildingsBuilt:index==3?UnitsDestroyed:index==4?BuildingsDestroyed:Score;
    }
    public sealed class MatchResult
    {
        private readonly Dictionary<PlayableOwner,Dictionary<int,double>> income=new Dictionary<PlayableOwner,Dictionary<int,double>>();
        private readonly Dictionary<PlayableOwner,MatchFact[]> army=new Dictionary<PlayableOwner,MatchFact[]>();
        private readonly Dictionary<PlayableOwner,double[]> actions=new Dictionary<PlayableOwner,double[]>();
        public long Tick{get;} public double Duration{get;} public int Seed{get;} public string Profile{get;} public string ProfileLabel{get;} public int? WinnerTeam{get;} public bool Manual{get;}
        public IReadOnlyList<MatchResultPlayer> Players{get;} public IReadOnlyList<MatchFact> Facts{get;}
        internal MatchResult(long tick,double duration,int seed,string profile,int? winner,bool manual,MatchResultPlayer[] players,MatchFact[] facts,string profileLabel=null)
        {Tick=tick;Duration=duration;Seed=seed;Profile=profile;ProfileLabel=profileLabel??profile;WinnerTeam=winner;Manual=manual;Players=Array.AsReadOnly(players);Facts=Array.AsReadOnly(facts);foreach(var player in players){var owned=facts.Where(f=>f.Owner==player.Owner).ToArray();income[player.Owner]=owned.Where(f=>f.Kind==MatchFactKind.Income).GroupBy(f=>(int)Math.Floor(f.Seconds+1e-8)).ToDictionary(g=>g.Key,g=>g.Sum(f=>f.Value));army[player.Owner]=owned.Where(f=>f.Kind==MatchFactKind.Army).ToArray();actions[player.Owner]=owned.Where(f=>f.Kind==MatchFactKind.HumanAction).Select(f=>f.Seconds).ToArray();}}
        public IEnumerable<MatchResultPlayer> Ranked=>Players.OrderByDescending(x=>x.Score).ThenBy(x=>x.Slot);
        public bool TeamsVisible=>Players.GroupBy(x=>x.Team).Any(x=>x.Count()>1);
        public bool IsMaximum(MatchResultPlayer player,int column)=>player.Column(column)>0&&player.Column(column)==Players.Max(x=>x.Column(column));
        public double Income(PlayableOwner owner,int second)=>income.TryGetValue(owner,out var series)&&series.TryGetValue(second,out var value)?value:0;
        public double Army(PlayableOwner owner,double seconds){if(!army.TryGetValue(owner,out var series))return 0;int lo=0,hi=series.Length;while(lo<hi){int mid=(lo+hi)/2;if(series[mid].Seconds<=seconds+1e-8)lo=mid+1;else hi=mid;}return lo==0?0:series[lo-1].Value;}
        private static int Upper(double[] series,double time){int lo=0,hi=series.Length;while(lo<hi){int mid=(lo+hi)/2;if(series[mid]<=time+1e-8)lo=mid+1;else hi=mid;}return lo;}
        // Raw actions stay available independently of the product's chosen APM window.
        public double Apm(PlayableOwner owner,double seconds,double windowSeconds)
        {double start=Math.Max(0,seconds-windowSeconds),span=Math.Min(seconds,windowSeconds);if(windowSeconds<=0)throw new ArgumentOutOfRangeException(nameof(windowSeconds));return span<=0||!actions.TryGetValue(owner,out var series)?0:(Upper(series,seconds)-(start<=0?0:Upper(series,start)))*60/span;}
    }
    internal sealed partial class PlayableDomain
    {
        private readonly List<MatchFact> matchFacts=new List<MatchFact>();
        private readonly Dictionary<PlayableOwner,string> matchAiIdentities=new Dictionary<PlayableOwner,string>();
        private readonly Dictionary<PlayableOwner,string> matchAiStrategies=new Dictionary<PlayableOwner,string>();
        private long matchFactSequence; private int matchSeed; private bool matchManual; private MatchResult frozenResult;
        internal MatchResult Result=>frozenResult;
        internal void BindMatchSeed(int seed){matchSeed=seed;}
        private void Fact(PlayableOwner owner,MatchFactKind kind,double value,int entityKind=0,string name="")
        {if(frozenResult==null)matchFacts.Add(new MatchFact(Tick,++matchFactSequence,elapsed,owner,kind,value,entityKind,name));}
        internal void RecordHumanAction(string id)
        {if(Outcome==PlayableMatchOutcome.Playing&&TryOwner(id,out var owner)&&!eliminated.Contains(owner)&&(offline==null||offline.Roster[(int)owner].Control==OfflineControl.Human))Fact(owner,MatchFactKind.HumanAction,1);}
        private void RecordArmy(PlayableOwner owner)=>Fact(owner,MatchFactKind.Army,units.Values.Where(x=>x.Owner==owner).Sum(x=>PlayableUnitRules.Population(profile,x.Kind)));
        private void RecordBuildingComplete(Building b)=>Fact(b.Owner,MatchFactKind.BuildingCompleted,TerritoryRules.Cost(profile,b.Kind),(int)b.Kind,b.Kind.ToString());
        private void RecordKill(PlayableOwner? attacker,PlayableOwner victim,int id,bool building,int kind,double fallback)
        {if(attacker.HasValue&&Hostile(attacker.Value,victim))Fact(attacker.Value,building?MatchFactKind.BuildingDestroyed:MatchFactKind.UnitDestroyed,fallback,kind);}
        internal void RecordAiState(PlayableAiOwnerCheckpoint state)
        {
            if(frozenResult!=null||state==null||!TryOwner(state.OwnerId,out var owner))return;
            string identity=state.AiProfileId+"@"+state.AiRevision+":"+state.AiHash+" · "+state.Difficulty+" · personality "+state.PersonalitySeed+" · "+state.Opening?.InitialOpening;
            matchAiIdentities[owner]=identity;
            string strategy=state.Strategy?.Phase+" · "+state.Strategy?.Strategy;
            if(!matchAiStrategies.TryGetValue(owner,out var previous)||previous!=strategy)
            {
                matchAiStrategies[owner]=strategy;
                if(previous==null)Fact(owner,MatchFactKind.AiDecision,(int)state.Opening.InitialOpening,(int)MatchAiDecisionKind.Opening,identity);
                if(state.Strategy?.Strategy!=null)Fact(owner,MatchFactKind.AiDecision,(int)state.Strategy.Strategy.Value,(int)MatchAiDecisionKind.Strategy,strategy);
                else if(state.Strategy?.Phase==PlayableAiOpeningPhase.Complete)Fact(owner,MatchFactKind.AiDecision,0,(int)MatchAiDecisionKind.OpeningCompleted,strategy);
                else if(state.Strategy?.Phase==PlayableAiOpeningPhase.Aborted)Fact(owner,MatchFactKind.AiDecision,0,(int)MatchAiDecisionKind.OpeningAborted,strategy);
            }
        }
        internal void FinishManually(){if(Outcome!=PlayableMatchOutcome.Playing||frozenResult!=null)return;matchManual=true;WinnerTeam=null;Outcome=PlayableMatchOutcome.ManuallyFinished;FreezeResult();}
        private void FreezeResult()
        {
            if(frozenResult!=null)return;
            var facts=matchFacts.ToArray();var players=Owners.Select((owner,index)=>new MatchResultPlayer(index,owner,TeamOf(owner),OwnerName(owner),OwnerName(owner),offline==null?owner==PlayableOwner.Enemy:offline.Roster[index].Control==OfflineControl.Ai,matchAiIdentities.TryGetValue(owner,out var identity)?identity+(matchAiStrategies.TryGetValue(owner,out var strategy)?" · итог "+strategy:""):"",facts,profile.MatchScoreEarnedCreditsDivisor)).ToArray();
            int? winner=matchManual?null:WinnerTeam??(Outcome==PlayableMatchOutcome.PlayerWon?(int?)TeamOf(PlayableOwner.Player):Outcome==PlayableMatchOutcome.PlayerLost?(int?)TeamOf(PlayableOwner.Enemy):null);
            frozenResult=new MatchResult(Tick,elapsed,matchSeed,profile.ProfileId+"@"+profile.Revision,winner,matchManual,players,facts,profile.DisplayName+" · "+profile.Revision);
        }
        private void WriteMatchHistory(BinaryWriter w)
        {
            w.Write(matchSeed);w.Write(matchManual);w.Write(matchFactSequence);
            w.Write(matchFacts.Count);foreach(var f in matchFacts){w.Write(f.Tick);w.Write(f.Sequence);w.Write(f.Seconds);w.Write((int)f.Owner);w.Write((int)f.Kind);w.Write(f.Value);w.Write(f.EntityKind);w.Write(f.Name);}
            foreach(var map in new[]{matchAiIdentities,matchAiStrategies}){w.Write(map.Count);foreach(var row in map.OrderBy(x=>(int)x.Key)){w.Write((int)row.Key);w.Write(row.Value);}}
        }
        private void ReadMatchHistory(BinaryReader r)
        {
            matchSeed=r.ReadInt32();matchManual=WorldWire.Boolean(r);matchFactSequence=r.ReadInt64();
            int Count(){int count=r.ReadInt32();if(count<0||count>2000000)throw new ArgumentException("Invalid match history count.");return count;}
            if(matchFactSequence<0)throw new ArgumentException("Invalid match sequence.");
            int n=Count();long previous=0,previousTick=0;double previousSeconds=0;for(int i=0;i<n;i++){var f=new MatchFact(r.ReadInt64(),r.ReadInt64(),r.ReadDouble(),(PlayableOwner)r.ReadInt32(),(MatchFactKind)r.ReadInt32(),r.ReadDouble(),r.ReadInt32(),r.ReadString());if(f.Tick<previousTick||f.Tick>Tick||f.Sequence<=previous||f.Sequence>matchFactSequence||!HasOwner(f.Owner)||!Enum.IsDefined(typeof(MatchFactKind),f.Kind)||double.IsNaN(f.Value)||double.IsInfinity(f.Value)||double.IsNaN(f.Seconds)||double.IsInfinity(f.Seconds)||f.Seconds<previousSeconds||f.Seconds>elapsed+1e-8||f.Value<0||((f.Kind==MatchFactKind.UnitCompleted||f.Kind==MatchFactKind.UnitDestroyed)&&!Enum.IsDefined(typeof(PlayableEntityKind),f.EntityKind))||((f.Kind==MatchFactKind.BuildingCompleted||f.Kind==MatchFactKind.BuildingDestroyed)&&!Enum.IsDefined(typeof(PlayableBuildingKind),f.EntityKind)))throw new ArgumentException("Invalid match history fact.");if(f.Kind==MatchFactKind.AiDecision&&(!Enum.IsDefined(typeof(MatchAiDecisionKind),f.EntityKind)||f.Value!=Math.Truncate(f.Value)||(f.EntityKind==(int)MatchAiDecisionKind.Opening&&!Enum.IsDefined(typeof(PlayableAiOpening),(int)f.Value))||(f.EntityKind==(int)MatchAiDecisionKind.Strategy&&!Enum.IsDefined(typeof(PlayableAiMidgameStrategy),(int)f.Value))))throw new ArgumentException("Invalid strategic fact.");
                previous=f.Sequence;previousTick=f.Tick;previousSeconds=f.Seconds;matchFacts.Add(f);}
            foreach(var map in new[]{matchAiIdentities,matchAiStrategies}){n=Count();for(int i=0;i<n;i++){var owner=(PlayableOwner)r.ReadInt32();if(!HasOwner(owner))throw new ArgumentException("Invalid match AI owner.");map.Add(owner,r.ReadString());}}
            if(matchFactSequence!=previous||matchManual!=(Outcome==PlayableMatchOutcome.ManuallyFinished))throw new ArgumentException("Invalid match terminal/sequence binding.");
            if(Outcome!=PlayableMatchOutcome.Playing)FreezeResult();
        }
    }
}
