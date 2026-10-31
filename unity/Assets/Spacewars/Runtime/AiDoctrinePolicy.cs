using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;
namespace Spacewars.Runtime
{
    public enum AiDoctrine { Balanced, Tanks, Heavy, Explorers, HawksTanks, HawksExplorers, BalancedSmallAir, HeavySmallAir, Air }
    public sealed partial class AiDoctrineState
    {
        public bool Chosen,AirOpening,Mixed,EnemyAirSeen;
        public AiDoctrine Doctrine;
        public long ChosenTick;
    }
    // Durable owner choice. Production follows live+queued headcounts; it never destroys the previous army.
    public sealed partial class AiDoctrinePolicy
    {
        private readonly PlayableAiOpeningCompositionState opening;
        private readonly long generation;
        private bool chosen,airOpening,mixed,enemyAirSeen;
        private bool factoryAllowed=true;
        internal Func<int,bool> UtilityAvailable{get;set;}
        internal Func<int,bool> CombatCountable{get;set;}
        private AiDoctrine doctrine;
        private long chosenTick;
        public AiDoctrinePolicy(PlayableAiOpeningCompositionState opening,long generation){this.opening=opening;this.generation=generation;}
        public bool Ready=>chosen;
        private static double V(AiProfile p,string key)=>p.Value("openingsMidgame."+key);
        private double Roll(string key,long ordinal=0)=>AiRandom.Key(opening.MatchSeed,opening.OwnerId,opening.ProfileIdentity,key,ordinal)/4294967296d;
        private AiDoctrine Pick(AiProfile p,bool combined,string key,long ordinal=0)
        {
            var paths=combined?new[]{"combined.hawksTanks","combined.hawksExplorers","combined.balancedSmallAir","combined.heavySmallAir"}:new[]{"ground.balanced","ground.tanks","ground.heavy","ground.explorers"};
            double pick=Roll(key,ordinal)*paths.Sum(x=>V(p,x)),sum=0;
            for(int i=0;i<paths.Length;i++){sum+=V(p,paths[i]);if(pick<sum)return (AiDoctrine)(i+(combined?4:0));}
            return combined?AiDoctrine.HeavySmallAir:AiDoctrine.Explorers;
        }
        public AiDoctrineState Capture()=>BindPersonality(new AiDoctrineState{Chosen=chosen,AirOpening=airOpening,Mixed=mixed,EnemyAirSeen=enemyAirSeen,Doctrine=doctrine,ChosenTick=chosenTick});
        private static bool Alive(PlayableBuildingSnapshot b)=>(b.Health>0||b.Phase==ConstructionPhase.Pending||b.Phase==ConstructionPhase.Constructing)&&b.PrivateState?.Lifecycle?.Selling!=true;
        public void Observe(PlayableAiObservation o,PlayableProfile p,AiProfile ai,AiOpeningExecutor executor,AiDifficulty difficulty)
        {
            if(o.OwnerId!=opening.OwnerId||o.Generation!=generation)throw new ArgumentException("Doctrine observation binding");
            factoryAllowed=executor.AllowsPostAirFactory(o,ai);
            ObservePersonality(o,p,ai,difficulty);
            enemyAirSeen|=o.Entities.Any(u=>o.IsHostile(u.Owner)&&u.Health>0&&u.Kind==PlayableEntityKind.Hawk);
            bool allies=o.Participants.Any(x=>x.Team==o.Team&&x.Id!=o.OwnerId);
            if(!chosen&&executor.Capture().InitialForceFulfilled)
            {
                chosen=true;chosenTick=o.Tick;airOpening=executor.Air;
                mixed=airOpening?(!allies||Roll("team-air-only")>=V(ai,"teamAirOnly")):Roll("ground-stay")>=V(ai,"groundStay");
                doctrine=airOpening&&!mixed?AiDoctrine.Air:Pick(ai,mixed,"first-doctrine");
            }
            if(chosen&&airOpening&&!mixed)
            {
                bool groundSupport=o.Buildings.Any(b=>b.Owner!=o.Owner&&!o.IsHostile(b.Owner)&&b.Kind==PlayableBuildingKind.Factory&&Alive(b));
                if(!groundSupport&&o.Tick-chosenTick>=AiProfile.SecondsToTicks(V(ai,"allyWait"),30))
                {mixed=true;doctrine=Pick(ai,true,"ground-support-lost");}
            }
            ObserveAdaptation(o,p,ai);
        }
        public double[] Targets(PlayableAiObservation o,AiProfile ai)
        {
            double[] ground=doctrine==AiDoctrine.Balanced||doctrine==AiDoctrine.BalancedSmallAir?new[]{V(ai,"balanced.tank"),V(ai,"balanced.explorer"),V(ai,"balanced.shkval"),0d}:
                doctrine==AiDoctrine.Heavy||doctrine==AiDoctrine.HeavySmallAir?new[]{V(ai,"heavy.tank"),0d,V(ai,"heavy.shkval"),0d}:
                doctrine==AiDoctrine.Explorers||doctrine==AiDoctrine.HawksExplorers?new[]{0d,1d,0d,0d}:new[]{1d,0d,0d,0d};
            if(doctrine==AiDoctrine.Air)ground=new[]{0d,0d,0d,1d};
            if(doctrine==AiDoctrine.HawksTanks||doctrine==AiDoctrine.HawksExplorers)ground[3]=V(ai,"airCore");
            if(doctrine==AiDoctrine.BalancedSmallAir||doctrine==AiDoctrine.HeavySmallAir)ground[3]=V(ai,"smallAir");
            if(enemyAirSeen&&ground[0]>0)ground[1]=Math.Max(ground[1],ground[0]*V(ai,"antiAir.explorer")/V(ai,"antiAir.tank"));
            return ground;
        }
        internal PlayableEntityKind GroundProductionKind(PlayableAiObservation o,AiProfile ai)
        {
            var kinds=new[]{PlayableEntityKind.Tank,PlayableEntityKind.Explorer,PlayableEntityKind.Shkval};var target=Targets(o,ai);
            return Enumerable.Range(0,kinds.Length).Where(i=>target[i]>0).OrderBy(i=>(o.Entities.Count(u=>u.Owner==o.Owner&&u.Health>0&&u.Kind==kinds[i]&&(CombatCountable==null||CombatCountable(u.Id)))+o.Buildings.Where(b=>b.Owner==o.Owner&&b.PrivateState!=null).SelectMany(b=>b.PrivateState.Orders).Count(q=>q.Kind==kinds[i]))/target[i]).ThenBy(i=>i).Select(i=>kinds[i]).DefaultIfEmpty(PlayableEntityKind.Tank).First();
        }
        public AiCompositionAssessment Assess(PlayableAiObservation o,PlayableProfile p,AiProfile ai,AiBudgetState ledger,int scoutBudget=0,bool usefulScouting=false)
        {
            var kinds=new[]{PlayableEntityKind.Tank,PlayableEntityKind.Explorer,PlayableEntityKind.Shkval,PlayableEntityKind.Hawk};var target=Targets(o,ai);var counts=new double[4];
            for(int i=0;i<kinds.Length;i++)
            {
                var k=kinds[i];counts[i]=o.Entities.Count(u=>u.Owner==o.Owner&&u.Health>0&&u.Kind==k&&(CombatCountable==null||CombatCountable(u.Id)))+o.Buildings.Where(b=>b.Owner==o.Owner&&Alive(b)&&b.PrivateState!=null).SelectMany(b=>b.PrivateState.Orders).Count(q=>q.Kind==k)+
                    (ledger?.Unpaid??Array.Empty<AiBudgetEntry>()).Count(e=>e.Action.UnitKind==k&&PlayableUnitRules.ProductionKind(e.Action.Kind)==k);
            }
            long available=ledger==null?o.Credits:Math.Max(0,(long)ledger.Liquid-ledger.SafetyReserve-ledger.Reserved.Sum(e=>(long)e.Amount)-ledger.Unpaid.Sum(e=>(long)e.Amount));
            var candidates=new List<AiEconomyCandidate>();
            foreach(var held in ledger?.Reserved??Array.Empty<AiBudgetEntry>())
            {
                if(held.Purpose!="factory-startup"||!PlayableUnitRules.ProductionKind(held.Action.Kind).HasValue)continue;
                if(AiEconomyAdmission.Reject(o,p,held.Action,true)==null)
                    candidates.Add(new AiEconomyCandidate("production",new PlayableAiAction(1,o.OwnerId,o.ProfileId,o.ProfileRevision,o.Generation,o.SnapshotSequence,held.Action.Kind,held.Action.CopyEntityIds(),unitKind:held.Action.UnitKind,seed:o.Seed,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity),null,1));
            }
            if(candidates.Count>0)return new AiCompositionAssessment(AiGamePhase.Mid,new[]{0d,1d,0d},new double[3],(int)counts[2],counts[0]+counts[1]>0,candidates,"finish accepted typed startup cycles");
            int utilityExplorers=o.Entities.Count(u=>u.Owner==o.Owner&&u.Health>0&&u.Kind==PlayableEntityKind.Explorer&&(UtilityAvailable==null||UtilityAvailable(u.Id)))+o.Buildings.Where(b=>b.Owner==o.Owner&&b.PrivateState!=null).SelectMany(b=>b.PrivateState.Orders).Count(q=>q.Kind==PlayableEntityKind.Explorer)+(ledger?.Unpaid??Array.Empty<AiBudgetEntry>()).Count(e=>e.Action.Kind==PlayableCommandKind.QueueExplorer);
            if(usefulScouting&&utilityExplorers<scoutBudget&&available>=PlayableUnitRules.Cost(p,PlayableEntityKind.Explorer))
            {
                foreach(var b in AiProductionDemand.Lines(o))
                {
                    var a=new PlayableAiAction(1,o.OwnerId,o.ProfileId,o.ProfileRevision,o.Generation,o.SnapshotSequence,PlayableCommandKind.QueueExplorer,new[]{b.Id},unitKind:PlayableEntityKind.Explorer,seed:o.Seed,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);
                    if(AiEconomyAdmission.Reject(o,p,a,true)==null)candidates.Add(new AiEconomyCandidate("production",a,null,1));
                }
                if(candidates.Count>0)return new AiCompositionAssessment(AiGamePhase.Mid,new[]{0d,1d,0d},new double[3],(int)counts[2],counts[0]+counts[1]>0,candidates,"bounded useful reconnaissance replacement");
            }
            foreach(int i in Enumerable.Range(0,4).Where(i=>target[i]>0).OrderBy(i=>counts[i]/target[i]).ThenBy(i=>counts[i]).ThenBy(i=>i))
            {
                var k=kinds[i];var typed=new List<AiEconomyCandidate>();
                foreach(var b in AiEconomyAdmission.Producers(o,AiRosterCatalog.Initial.For(k)))
                {
                    var a=new PlayableAiAction(1,o.OwnerId,o.ProfileId,o.ProfileRevision,o.Generation,o.SnapshotSequence,PlayableUnitRules.ProductionCommand(k),new[]{b.Id},unitKind:k,seed:o.Seed,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);
                    var rejection=AiEconomyAdmission.Reject(o,p,a,true);
                    // A missing/busy/unavailable line may yield to another type. A
                    // ready legal line short only of money keeps the typed deficit:
                    // buying a cheaper unit each income period would starve it forever.
                    if(rejection==null||rejection=="insufficient credits")typed.Add(new AiEconomyCandidate("production",a,rejection,1));
                }
                if(typed.Count==0)continue;
                if(PlayableUnitRules.Cost(p,k)>available)
                    return new AiCompositionAssessment(AiGamePhase.Mid,new[]{0d,1d,0d},new double[3],(int)counts[2],counts[0]+counts[1]>0,
                        typed.Select(c=>new AiEconomyCandidate(c.Policy,c.Action,"saving for doctrine "+k,c.Priority)),"saving for doctrine "+k);
                candidates.AddRange(typed.Where(c=>c.Legal));
                // One desired headcount deficit per decision; unavailable producers do not freeze another line.
                if(candidates.Count>0)break;
            }
            return new AiCompositionAssessment(AiGamePhase.Mid,new[]{0d,1d,0d},new double[3],(int)counts[2],counts[0]+counts[1]>0,candidates,"doctrine "+doctrine+" headcount deficit");
        }
        public IEnumerable<AiEconomyCandidate> ProducerRequests(PlayableAiObservation o,PlayableProfile p)
        {
            if(!chosen)yield break;
            foreach(var kind in new[]{PlayableBuildingKind.Factory,PlayableBuildingKind.Hangar})
            {
                bool dueFactory=doctrine==AiDoctrine.Air&&nextCompositionChange>=0&&o.Tick>=nextCompositionChange&&pendingDoctrine!=doctrine&&!CriticalDefense(o,p)&&SpendableCredits>=TerritoryRules.Cost(p,PlayableBuildingKind.Factory);
                bool needed=kind==PlayableBuildingKind.Factory?doctrine!=AiDoctrine.Air||dueFactory:mixed||doctrine==AiDoctrine.Air;
                if(!needed||kind==PlayableBuildingKind.Factory&&!factoryAllowed||o.Buildings.Any(b=>b.Owner==o.Owner&&b.Kind==kind&&Alive(b)))continue;
                foreach(var s in o.Sites.Where(s=>s.Owner==o.Owner&&s.Ready&&!s.Contested))foreach(var slot in s.Site.Slots)
                {var a=new PlayableAiAction(1,o.OwnerId,o.ProfileId,o.ProfileRevision,o.Generation,o.SnapshotSequence,PlayableCommandKind.BuildAt,siteId:s.Site.Id,slotId:slot.Id,parentId:s.CenterId,buildingKind:kind,seed:o.Seed,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);yield return new AiEconomyCandidate("economy",a,AiEconomyAdmission.Reject(o,p,a,true),1);}
            }
        }
        public int ResearchRank(PlayableResearchKind kind)
        {
            if(!chosen)return int.MaxValue;
            bool heavy=doctrine==AiDoctrine.Heavy||doctrine==AiDoctrine.HeavySmallAir;
            bool tanks=doctrine==AiDoctrine.Tanks||doctrine==AiDoctrine.HawksTanks;
            bool explorers=doctrine==AiDoctrine.Explorers||doctrine==AiDoctrine.HawksExplorers;
            var order=heavy?new[]{PlayableResearchKind.ImprovedDefensiveSystems,PlayableResearchKind.ImprovedWeaponSystems,PlayableResearchKind.TankChassis}:
                tanks?new[]{PlayableResearchKind.TankChassis,PlayableResearchKind.ImprovedWeaponSystems,PlayableResearchKind.ImprovedDefensiveSystems}:
                explorers?new[]{PlayableResearchKind.ExplorerAssaultGuns,PlayableResearchKind.ImprovedWeaponSystems,PlayableResearchKind.ImprovedDefensiveSystems}:
                new[]{PlayableResearchKind.ImprovedWeaponSystems,PlayableResearchKind.ImprovedDefensiveSystems};
            int rank=Array.IndexOf(order,kind);return rank<0?order.Length:rank;
        }
        internal void WriteState(BinaryWriter w){w.Write(factoryAllowed);w.Write(chosen);w.Write(airOpening);w.Write(mixed);w.Write(enemyAirSeen);w.Write((int)doctrine);w.Write(chosenTick);WritePersonality(w);}
        internal void ReadState(BinaryReader r,long tick){factoryAllowed=WorldWire.Boolean(r);chosen=WorldWire.Boolean(r);airOpening=WorldWire.Boolean(r);mixed=WorldWire.Boolean(r);enemyAirSeen=WorldWire.Boolean(r);doctrine=WorldWire.EnumValue<AiDoctrine>(r);chosenTick=r.ReadInt64();AiStateWire.Require(chosenTick>=0&&chosenTick<=tick&&(chosen||chosenTick==0),"doctrine clocks");ReadPersonality(r,tick);}
    }
}
