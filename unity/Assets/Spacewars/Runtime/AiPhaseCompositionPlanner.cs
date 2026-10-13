using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Runtime
{
    public enum AiGamePhase { Early, Mid, Late }
    public sealed class AiPhaseCompositionState
    {
        internal AiPhaseCompositionState(AiGamePhase phase,bool captured,double income,long entered,long observed)
        {Phase=phase;StartingIncomeCaptured=captured;StartingIncome=income;EnteredTick=entered;ObservedTick=observed;}
        public AiGamePhase Phase{get;} public bool StartingIncomeCaptured{get;} public double StartingIncome{get;}
        public long EnteredTick{get;} public long ObservedTick{get;}
    }
    public sealed class AiCompositionAssessment
    {
        internal AiCompositionAssessment(AiGamePhase phase,double[] shares,double[] value,int support,bool cover,IEnumerable<AiEconomyCandidate> candidates,string reason)
        {Phase=phase;BudgetShares=Array.AsReadOnly(shares);CommittedRoleCredits=Array.AsReadOnly(value);SupportCount=support;ViableCover=cover;Candidates=Array.AsReadOnly(candidates.ToArray());Reason=reason;}
        public AiGamePhase Phase{get;} // recon, line, support; credit value, never head-count shares.
        public IReadOnlyList<double> BudgetShares{get;} public IReadOnlyList<double> CommittedRoleCredits{get;}
        public int SupportCount{get;} public bool ViableCover{get;} public IReadOnlyList<AiEconomyCandidate> Candidates{get;} public string Reason{get;}
    }
    // Only detached owner observations, registered gameplay mechanics and the shared ledger.
    // Observe owns phase progress; Assess proposes ordinary commands without reservations/RNG.
    public sealed class AiPhaseCompositionPlanner
    {
        private readonly string owner;private readonly long generation;private readonly AiRosterCatalog roster;
        private AiGamePhase phase;private bool captured;private double startingIncome;private long entered,observed;
        public AiPhaseCompositionPlanner(string owner,long generation,AiRosterCatalog roster=null)
        {if(string.IsNullOrWhiteSpace(owner)||generation<1)throw new ArgumentException("Composition binding");this.owner=owner;this.generation=generation;this.roster=roster??AiRosterCatalog.Initial;}
        private void Bind(PlayableAiObservation o)
        {if(o==null||o.OwnerId!=owner||o.Generation!=generation)throw new ArgumentException("Composition observation binding");}
        public AiPhaseCompositionState Capture()=>new AiPhaseCompositionState(phase,captured,startingIncome,entered,observed);
        public bool ViableCover(PlayableAiObservation o,PlayableProfile p,AiProfile ai)
        {
            var fighters=o.Entities.Where(u=>u.Owner==o.Owner&&u.Health>0&&roster.For(u.Kind).lineWeight>0&&string.IsNullOrWhiteSpace(roster.For(u.Kind).exclusionReason)).ToArray();
            double reference=p.TankWeaponDamage/p.TankWeaponReloadSeconds*ai.Value("utility.forceHorizonSeconds")*ai.Value("armies.minimumUnits");
            return AiDefensePlanner.Force(fighters,p,ai,roster)>=reference;
        }
        public void Observe(PlayableAiObservation o,PlayableProfile p,AiProfile ai)
        {
            Bind(o);if(o.Tick<observed)throw new ArgumentException("Composition clock regressed");observed=o.Tick;
            if(!captured){captured=true;startingIncome=double.IsNaN(o.IncomePerSecond)||double.IsInfinity(o.IncomePerSecond)?0:Math.Max(0,o.IncomePerSecond);}
            if(o.Tick-entered<AiProfile.SecondsToTicks(ai.Value("phase.minimumResidenceSeconds"),30))return;
            bool Ratio(string path)=>startingIncome>0&&o.IncomePerSecond>=startingIncome*ai.Value(path);
            if(phase==AiGamePhase.Early&&(Ratio("phase.midIncomeStartingRatio")||o.Tick>=AiProfile.SecondsToTicks(ai.Value("phase.midFallbackSeconds"),30)))
            {phase=AiGamePhase.Mid;entered=o.Tick;}
            else if(phase==AiGamePhase.Mid&&(Ratio("phase.lateIncomeStartingRatio")&&o.OwnerResearch.Any(r=>r.Complete)&&ViableCover(o,p,ai)||o.Tick>=AiProfile.SecondsToTicks(ai.Value("phase.lateFallbackSeconds"),30)))
            {phase=AiGamePhase.Late;entered=o.Tick;}
        }
        private static double[] Weights(AiRosterDescriptorData d)
        {double sum=d.reconWeight+d.lineWeight+d.supportWeight;return sum<=0?new double[3]:new[]{d.reconWeight/sum,d.lineWeight/sum,d.supportWeight/sum};}
        private static void Normalize(double[] a){double sum=a.Sum();if(sum>0)for(int i=0;i<a.Length;i++)a[i]/=sum;}
        public AiCompositionAssessment Assess(PlayableAiObservation o,PlayableProfile p,AiProfile ai,AiBudgetState ledger=null,int reconCap=1,IEnumerable<int> startupProducers=null,bool usefulScoutDemand=false)
        {
            Bind(o);if(o.Tick<observed)throw new ArgumentException("Composition assessment clock regressed");if(ledger!=null&&(ledger.OwnerId!=owner||ledger.Generation!=generation))throw new ArgumentException("Composition ledger binding");
            var descriptors=roster.CopyData().Where(d=>string.IsNullOrWhiteSpace(d.exclusionReason)).ToArray();
            var value=new double[3];int support=0,recon=0;
            void Count(PlayableEntityKind kind,double credits,bool countMinimum=true)
            {var d=roster.For(kind);var weights=Weights(d);for(int i=0;i<3;i++)value[i]+=credits*weights[i];if(countMinimum&&d.supportWeight>0)support++;if(d.reconWeight>0)recon++;}
            foreach(var u in o.Entities.Where(u=>u.Owner==o.Owner&&u.Health>0))Count(u.Kind,roster.CreditCost(u.Kind,p));
            foreach(var q in o.Buildings.Where(b=>b.Owner==o.Owner&&b.Health>0&&b.PrivateState!=null).SelectMany(b=>b.PrivateState.Orders))Count(q.Kind,q.PaidCost);
            // Paid history is settlement history, not another unit. Accepted delayed commands
            // are absent from gameplay queues until delivery and are counted exactly once here.
            foreach(var e in ledger?.Unpaid??Array.Empty<AiBudgetEntry>())
            {var d=roster.Production(e.Action.Kind);if(d!=null)Count(d.kind,e.Amount,false);}
            bool cover=ViableCover(o,p,ai);long available=ledger==null?o.Credits:Math.Max(0,(long)ledger.Liquid-ledger.SafetyReserve-ledger.Reserved.Sum(e=>(long)e.Amount)-ledger.Unpaid.Sum(e=>(long)e.Amount));
            var startup=new HashSet<int>(startupProducers??Array.Empty<int>());var normal=new List<Tuple<AiRosterDescriptorData,PlayableAiAction>>();var launch=new List<AiEconomyCandidate>();
            PlayableAiAction Action(AiRosterDescriptorData d,int producer)=>new PlayableAiAction(1,o.OwnerId,o.ProfileId,o.ProfileRevision,o.Generation,o.SnapshotSequence,d.productionCommand,new[]{producer},unitKind:d.kind,seed:o.Seed,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);
            foreach(var d in descriptors)
                foreach(var b in AiEconomyAdmission.Producers(o,d))
                {
                    var a=Action(d,b.Id);string rejection=AiEconomyAdmission.Reject(o,p,a,true,roster);if(rejection!=null)continue;
                    if(startup.Contains(b.Id))
                    {if(d.kind==PlayableEntityKind.Tank)launch.Add(new AiEconomyCandidate("production",a,null,1));continue;}
                    if(d.supportWeight>0&&!cover||d.reconWeight>0&&(recon>=reconCap||d.lineWeight+d.supportWeight==0&&!usefulScoutDemand)||roster.CreditCost(d.kind,p)>available)continue;
                    normal.Add(Tuple.Create(d,a));
                }
            bool Combat(AiRosterDescriptorData d)=>d.lineWeight+d.supportWeight>0&&d.targetTags.Any(t=>t=="ground"||t=="building")&&PlayableUnitRules.DamagePerSecond(p,d.kind,o.OwnerResearch.Any(r=>r.Kind==PlayableResearchKind.ExplorerAssaultGuns&&r.Complete))>0;
            string prefix="composition."+phase.ToString().ToLowerInvariant()+".";
            var shares=new[]{ai.Value(prefix+"reconBudgetShare"),ai.Value(prefix+"lineBudgetShare"),ai.Value(prefix+"supportBudgetShare")};Normalize(shares);
            var availableRoles=new bool[3];foreach(var d in normal.Select(x=>x.Item1)){var w=Weights(d);for(int i=0;i<3;i++)availableRoles[i]|=w[i]>0;}
            // Availability transfer is separate from the bounded visible-counter correction.
            double unavailable=0;for(int i=0;i<3;i++)if(!availableRoles[i]){unavailable+=shares[i];shares[i]=0;}
            // A spare scout cap is not a combat assignment. Weapon capability AND a
            // descriptor wired to existing native combat roles are required; no new raids.
            var combatRoles=new bool[3];foreach(var d in normal.Select(x=>x.Item1).Where(Combat)){var w=Weights(d);for(int i=0;i<3;i++)combatRoles[i]|=w[i]>0;}
            double combatShare=Enumerable.Range(0,3).Where(i=>combatRoles[i]).Sum(i=>shares[i]);
            if(combatShare>0)for(int i=0;i<3;i++)if(combatRoles[i])shares[i]+=unavailable*shares[i]/combatShare;
            // Missing combat capability leaves money unallocated rather than gifting it
            // to reconnaissance. Genuine scouting may still spend its original role share.

            Tuple<AiRosterDescriptorData,PlayableAiAction> fallback=null;
            if(shares.Sum()==0||unavailable>0&&combatShare==0)
            {
                fallback=normal.Where(x=>Combat(x.Item1)).OrderBy(x=>roster.CreditCost(x.Item1.kind,p)).ThenBy(x=>(int)x.Item1.kind).ThenBy(x=>x.Item2.EntityIds[0]).FirstOrDefault();
                if(fallback!=null)
                {
                    if(shares.Sum()==0)shares=Weights(fallback.Item1);
                    else
                    {
                        // A valid zero combat-share profile still transfers unavailable
                        // mass to the cheapest compatible combat role. Retain the original
                        // recon share; never normalize that mass into pure reconnaissance.
                        double combatWeight=fallback.Item1.lineWeight+fallback.Item1.supportWeight;
                        shares[1]+=unavailable*fallback.Item1.lineWeight/combatWeight;
                        shares[2]+=unavailable*fallback.Item1.supportWeight/combatWeight;
                    }
                }
            }
            var response=new double[3];bool targets=false;
            foreach(var tag in new[]{"ground","building"})
            {
                var health=tag=="ground"?o.Entities.Where(u=>o.IsHostile(u.Owner)&&u.Health>0).OrderBy(u=>u.Id).Select(u=>(double)u.Health):o.Buildings.Where(b=>o.IsHostile(b.Owner)&&b.Health>0).OrderBy(b=>b.Id).Select(b=>(double)b.Health);
                foreach(var hp in health)
                {
                    targets=true;
                    foreach(var d in normal.Select(x=>x.Item1).GroupBy(d=>d.kind).Select(g=>g.First()).Where(d=>Combat(d)&&d.targetTags.Contains(tag)))
                    {
                        double effectiveness=Math.Min(hp,PlayableUnitRules.DamagePerSecond(p,d.kind,o.OwnerResearch.Any(r=>r.Kind==PlayableResearchKind.ExplorerAssaultGuns&&r.Complete))*ai.Value("utility.forceHorizonSeconds"))/Math.Max(1,roster.CreditCost(d.kind,p));
                        var weights=Weights(d);for(int i=0;i<3;i++)response[i]+=effectiveness*weights[i];
                    }
                }
            }
            if(targets&&response.Sum()>0)
            {
                Normalize(response);double transfer=Enumerable.Range(0,3).Sum(i=>Math.Max(0,response[i]-shares[i]));
                double fraction=transfer==0?0:Math.Min(1,ai.Value("composition.counterMaximumBudgetTransfer")/transfer);
                for(int i=0;i<3;i++)shares[i]+=fraction*(response[i]-shares[i]);
            }
            var minimum=normal.Where(x=>x.Item1.supportWeight>0&&support<ai.Value(prefix+"minimumSupport")).OrderBy(x=>roster.CreditCost(x.Item1.kind,p)).ThenBy(x=>(int)x.Item1.kind).ThenBy(x=>x.Item2.EntityIds[0]).FirstOrDefault();
            // Compare roles over one affordable purchase, with the same credit horizon
            // for every descriptor; otherwise a costly unit inflates its own deficit.
            double total=value.Sum()+(normal.Count==0?0:normal.Min(x=>roster.CreditCost(x.Item1.kind,p)));
            double Deficit(AiRosterDescriptorData d)
            {var w=Weights(d);return Enumerable.Range(0,3).Sum(i=>w[i]*(shares[i]*total-value[i]));}
            var chosen=minimum??fallback??normal.Where(x=>Weights(x.Item1).Where((weight,i)=>weight>0&&shares[i]>0).Any()&&Deficit(x.Item1)>0).OrderByDescending(x=>Deficit(x.Item1)).ThenBy(x=>roster.CreditCost(x.Item1.kind,p)).ThenBy(x=>(int)x.Item1.kind).ThenBy(x=>x.Item2.EntityIds[0]).FirstOrDefault();
            if(chosen!=null)launch.Add(new AiEconomyCandidate("production",chosen.Item2,null,minimum!=null?1:0));
            string reason=chosen==null&&launch.Count==0?"composition blocked: no affordable legal combat producer/unlock/population; support requires viable cover":minimum!=null?"minimum support deficit":"role credit deficit";
            return new AiCompositionAssessment(phase,shares,value,support,cover,launch,reason);
        }
        internal void WriteState(BinaryWriter w)
        {WorldWire.String(w,owner);w.Write(generation);w.Write((int)phase);w.Write(captured);WorldWire.Number(w,startingIncome);w.Write(entered);w.Write(observed);}
        internal void ReadState(BinaryReader r,long tick)
        {
            AiStateWire.Require(WorldWire.String(r)==owner&&r.ReadInt64()==generation,"composition owner/generation");phase=WorldWire.EnumValue<AiGamePhase>(r);captured=WorldWire.Boolean(r);startingIncome=WorldWire.Number(r);entered=r.ReadInt64();observed=r.ReadInt64();
            AiStateWire.Require(startingIncome>=0&&entered>=0&&entered<=observed&&observed<=tick&&(captured||startingIncome==0&&phase==AiGamePhase.Early&&entered==0&&observed==0)&&(phase==AiGamePhase.Early?entered==0:entered>0),"composition phase/reference/clocks");
        }
    }
}
