using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Runtime
{
    [Serializable] public sealed class AiOpeningExecutionState
    {
        public string Template{get;internal set;} public PlayableAiOpeningPhase Phase{get;internal set;}
        public long StartedTick{get;internal set;}=-1; public long ProgressTick{get;internal set;}=-1;
        public int Replans{get;internal set;} public string Reason{get;internal set;}="not started";
        public bool InitialForceFulfilled{get;internal set;}
        public int ProtectedCapturerId{get;internal set;}
        public IReadOnlyList<string> Completed{get;internal set;}=Array.Empty<string>();
        public string Fallback=>Phase==PlayableAiOpeningPhase.Aborted?"ordinary-macro-army":null;
    }
    internal sealed partial class PlayableDomain
    {
        internal Func<PlayableOwner,int> ProtectedAiCapturer;
        internal string FinancialOwnerId(PlayableOwner owner)=>offline!=null&&((int)owner)<offline.Roster.Count?offline.Roster[(int)owner].Id:owner==PlayableOwner.Player?PlayerId:"enemy-1";
        private bool IsProtectedAiCapturer(int id,PlayableOwner owner)=>ProtectedAiCapturer?.Invoke(owner)==id;
    }
    // Produces ordinary economy/army requests. World facts, not command receipts, fulfil production.
    public sealed class AiOpeningExecutor
    {
        private readonly PlayableAiOpeningCompositionState opening;
        private readonly long generation;
        private readonly HashSet<string> completed=new HashSet<string>();
        private long started=-1,progress=-1,secondIncome=-1;
        private bool producerChosen,air,initialForceFulfilled;
        private int requiredForce,refineries,outpostThreshold,capturer;
        private bool teamResolved;
        private string teamAirWinner;
        private bool mixedForce;
        private bool capturerReleased,greedyAir,reinforceChosen,reinforce,reinforcementFulfilled;
        private int reinforcementTarget;
        internal long SpendableCredits{get;set;}
        internal Func<int,bool> CombatAvailable{get;set;}
        internal bool AggressivePersonality{get;set;}
        internal ISet<string> KnownGreedyAllies{get;set;}
        internal int ProtectedUnit=>capturerReleased?0:capturer;
        private PlayableEntityKind forceKind=PlayableEntityKind.Tank;
        private PlayableAiOpeningPhase phase=PlayableAiOpeningPhase.Active;
        private string reason="not started";
        public AiOpeningExecutor(PlayableAiOpeningCompositionState opening,long generation){this.opening=opening;this.generation=generation;}
        private double Sample(string key)=>AiRandom.Key(opening.MatchSeed,opening.OwnerId,opening.ProfileIdentity,key,0)/4294967296d;
        private static double V(AiProfile p,string key)=>p.Value("openingsMidgame."+key);
        private int Range(AiProfile p,string min,string max,string key)=>(int)V(p,min)+(int)Math.Floor(Sample(key)*(V(p,max)-V(p,min)+1));
        public AiScoutPlan ScoutPlan=>AiScoutPlan.Standard;
        public bool AllowsExpansion=>true;
        internal bool AllowsOutposts=>refineries>=outpostThreshold&&outpostThreshold>0;
        public bool AllowsPressure=>completed.Contains("force")||phase!=PlayableAiOpeningPhase.Active;
        internal bool Air=>air;
        internal bool ProducerChosen=>producerChosen;
        internal int RequiredForce=>requiredForce;
        internal bool HasContinuationCommitment(AiProfile p)=>reinforceChosen&&refineries<V(p,"doubleIncome");
        private bool ReinforcementPending(PlayableAiObservation o)=>reinforceChosen&&reinforce&&!reinforcementFulfilled;
        internal PlayableBuildingKind Producer=>air?PlayableBuildingKind.Hangar:PlayableBuildingKind.Factory;
        public AiOpeningExecutionState Capture()=>new AiOpeningExecutionState{Template=AiOpeningCatalog.For(opening.Opening).Id,Phase=phase,StartedTick=started,ProgressTick=progress,InitialForceFulfilled=initialForceFulfilled,ProtectedCapturerId=ProtectedUnit,Reason=reason,Completed=completed.OrderBy(x=>x).ToArray()};
        private static bool Live(PlayableBuildingSnapshot b)=>(b.Health>0||b.Phase==ConstructionPhase.Pending||b.Phase==ConstructionPhase.Constructing)&&b.PrivateState?.Lifecycle?.Selling!=true;
        private static int Income(PlayableAiObservation o)=>o.Buildings.Count(b=>b.Owner==o.Owner&&Live(b)&&b.Phase==ConstructionPhase.Ready&&(b.Kind==PlayableBuildingKind.Refinery||b.Kind==PlayableBuildingKind.Mine));
        // Paid foundations cover construction targets, but provide neither income nor readiness.
        private static int CommittedIncome(PlayableAiObservation o)=>o.Buildings.Count(b=>b.Owner==o.Owner&&Live(b)&&(b.Kind==PlayableBuildingKind.Refinery||b.Kind==PlayableBuildingKind.Mine));
        private static bool Ready(PlayableAiObservation o,PlayableBuildingKind k)=>o.Buildings.Any(b=>b.Owner==o.Owner&&Live(b)&&b.Phase==ConstructionPhase.Ready&&b.Kind==k);
        internal static bool ReplacementAvailable(PlayableAiObservation o,PlayableProfile p,long spendable)
        {
            if(spendable<PlayableUnitRules.Cost(p,PlayableEntityKind.Explorer))return false;
            return o.Buildings.Where(b=>b.Owner==o.Owner&&b.Kind==PlayableBuildingKind.Factory).Any(b=>
            {
                var a=new PlayableAiAction(1,o.OwnerId,o.ProfileId,o.ProfileRevision,o.Generation,o.SnapshotSequence,PlayableCommandKind.QueueExplorer,new[]{b.Id},unitKind:PlayableEntityKind.Explorer,seed:o.Seed,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);
                return AiEconomyAdmission.Reject(o,p,a)==null;
            });
        }
        internal static string TeamAirOwner(PlayableAiObservation o,AiProfile p,string genesisBinding=null)
        {
            var team=o.Participants.Where(x=>x.Team==o.Team).OrderBy(x=>x.Id,StringComparer.Ordinal).ToArray();
            if(team.Length<2)return null;
            var bots=team.Where(x=>x.Control==OfflineControl.Ai).ToArray();if(bots.Length==0)return null;
            // Other team sizes continue a single team-wide draw using the largest supported policy.
            string identity=string.Join("|",team.Select(x=>x.Id));
            double roll=AiRandom.Key(o.Seed,identity,genesisBinding??PlayableAiOpeningComposition.ProfileBinding(p),"team-air",0)/4294967296d;
            if(roll>=V(p,team.Length==2?"teamAir2":"teamAir3"))return null;
            uint pick=AiRandom.Key(o.Seed,identity,genesisBinding??PlayableAiOpeningComposition.ProfileBinding(p),"team-air-owner",0);
            return bots[pick%bots.Length].Id;
        }
        public void Observe(PlayableAiObservation o,AiKnowledgeState knowledge,AiArmyDeployment deployment,AiProfile p)
        {
            if(o.OwnerId!=opening.OwnerId||o.Generation!=generation)throw new ArgumentException("Opening observation binding");
            if(started<0)
            {
                started=progress=o.Tick;outpostThreshold=Range(p,"outpostMin","outpostMax","outpost-threshold");
                if(!teamResolved){teamAirWinner=TeamAirOwner(o,p,opening.ProfileIdentity);teamResolved=true;}
                bool airIntent=opening.Opening==PlayableAiOpening.Air||teamAirWinner==o.OwnerId;
                if(airIntent)capturer=o.Entities.Where(u=>u.Owner==o.Owner&&u.Health>0&&u.Kind==PlayableEntityKind.Explorer).OrderBy(u=>u.Id).Select(u=>u.Id).FirstOrDefault();
                greedyAir=!o.Participants.Any(x=>x.Team==o.Team&&x.Id!=o.OwnerId)&&opening.Opening==PlayableAiOpening.Air&&Sample("solo-air-income")<V(p,"greedyAirShare");
                forceKind=Sample("opening-force-kind")<V(p,"openingExplorerShare")?PlayableEntityKind.Explorer:PlayableEntityKind.Tank;
                mixedForce=Sample("mixed-opening")<V(p,"openingExplorerShare")||opening.Opening==PlayableAiOpening.ExplorerAllIn&&forceKind==PlayableEntityKind.Tank;
            }
            // Relay openings use the first unit for useful field work, including contest combat.
            if(AiRelayObjectives.HasRelays(o))capturerReleased=true;
            if(capturer>0&&!capturerReleased&&ReplacementAvailable(o,PlayableProfileForObservation??PlayableProfile.ThreeCrossingsDefault,SpendableCredits))capturerReleased=true;
            refineries=Income(o);
            int requested=RequiredHomeRefineryCount(p);
            if(refineries>=V(p,"doubleIncome")&&secondIncome<0)secondIncome=o.Tick;
            if(!producerChosen&&CommittedIncome(o)>=requested)
            {
                var allies=o.Participants.Select((x,i)=>new{x,i}).Where(x=>x.x.Team==o.Team&&x.x.Id!=o.OwnerId).ToArray();
                if(allies.Length==0)
                {air=opening.Opening==PlayableAiOpening.Air;producerChosen=true;}
                else
                {
                    string selected=teamAirWinner;
                    bool ownGreedy=opening.Opening==PlayableAiOpening.GreedySafe;
                    bool Has(int index,PlayableBuildingKind kind)=>o.Buildings.Any(b=>(int)b.Owner==index&&b.Kind==kind&&Live(b));
                    bool groundHalf=allies.Count(a=>Has(a.i,PlayableBuildingKind.Factory))*2>=allies.Length;
                    bool otherAir=allies.Any(a=>Has(a.i,PlayableBuildingKind.Hangar));
                    bool greedySupport=allies.All(a=>Has(a.i,PlayableBuildingKind.Factory)||!Has(a.i,PlayableBuildingKind.Hangar)&&
                        (a.x.Control==OfflineControl.Ai&&KnownGreedyAllies?.Contains(a.x.Id)==true||a.x.Control==OfflineControl.Human&&o.Buildings.Count(b=>(int)b.Owner==a.i&&Live(b)&&(b.Kind==PlayableBuildingKind.Refinery||b.Kind==PlayableBuildingKind.Mine))>=V(p,"greedyIncome")));
                    bool paidOtherBotAir=allies.Any(a=>a.x.Control==OfflineControl.Ai&&Has(a.i,PlayableBuildingKind.Hangar));
                    air=selected==o.OwnerId&&!paidOtherBotAir&&(groundHalf||greedySupport);
                    bool wait=selected==o.OwnerId&&!air&&!otherAir&&(secondIncome<0||o.Tick-secondIncome<AiProfile.SecondsToTicks(V(p,"allyWait"),30));
                    producerChosen=!wait;
                }
                if(producerChosen&&!air)capturerReleased=true;
                if(producerChosen)requiredForce=air?(int)V(p,"airCalm"):
                    opening.Opening==PlayableAiOpening.BlindRush?(int)V(p,"aggressiveFirst"):
                    opening.Opening==PlayableAiOpening.ExplorerAllIn?Range(p,"allInMin","allInMax","all-in-size"):Range(p,"reinforcedMin","reinforcedMax","opening-size");
            }
            if(refineries>=requested)completed.Add("income");
            if(completed.Contains("income")&&producerChosen&&Ready(o,Producer))completed.Add("factory");
            int protect=ProtectedUnit;
            if(air&&producerChosen)
            {
                var hawks=o.Entities.Where(u=>u.Owner==o.Owner&&u.Health>0&&u.Kind==PlayableEntityKind.Hawk).Select(u=>u.Id).ToArray();
                bool suitable=o.RouteProofs.Where(r=>hawks.Contains(r.UnitId)&&r.Kind==PlayableRouteTargetKind.VisibleEnemy&&
                    (o.Entities.Any(u=>u.Id==r.TargetId&&o.IsHostile(u.Owner)&&u.Health>0)||o.Buildings.Any(b=>b.Id==r.TargetId&&o.IsHostile(b.Owner)&&b.Health>0))).GroupBy(r=>r.TargetId).Any(g=>g.Select(r=>r.UnitId).Distinct().Count()>=V(p,"airFirst"));
                requiredForce=(int)V(p,suitable?"airFirst":"airCalm");
            }
            var wave=DesiredForce(p,requiredForce);
            if(producerChosen&&completed.Contains("factory")&&wave.All(x=>AvailableFighters(o,x.Key)>=x.Value))
            {initialForceFulfilled=true;completed.Add("force");}
            if(initialForceFulfilled&&!air&&!reinforceChosen&&opening.Opening==PlayableAiOpening.BlindRush)
            {
                reinforceChosen=true;reinforcementTarget=Range(p,"reinforcedMin","reinforcedMax","aggressive-reinforcement-size");
                bool contact=o.Entities.Any(u=>o.IsHostile(u.Owner)&&u.Health>0);
                reinforce=AggressivePersonality||o.OwnCenterDamage.Count>0||contact&&o.Entities.Count(u=>u.Owner==o.Owner&&u.Health>0)>=o.Entities.Count(u=>o.IsHostile(u.Owner)&&u.Health>0)||Sample("aggressive-continuation")<V(p,"reinforceShare");
            }
            if(reinforceChosen&&reinforce&&!reinforcementFulfilled&&DesiredForce(p,reinforcementTarget).All(x=>AvailableFighters(o,x.Key)>=x.Value))reinforcementFulfilled=true;
            if(deployment!=null&&initialForceFulfilled){completed.Add("pressure");phase=PlayableAiOpeningPhase.Complete;}
            reason=producerChosen?(air?"air producer selected":"ground producer selected"):"waiting for income/allied visible production";
        }
        internal PlayableProfile PlayableProfileForObservation{get;set;}
        internal int RequiredHomeRefineryCount(AiProfile p)=>opening.Opening==PlayableAiOpening.GreedySafe||greedyAir?(int)V(p,"greedyIncome"):teamAirWinner==opening.OwnerId||opening.Opening==PlayableAiOpening.Safe||opening.Opening==PlayableAiOpening.Air?(int)V(p,"doubleIncome"):1;
        // A new strategic savings clock starts only after the actual chosen
        // home producer and required income are ready (Hangar for Air).
        internal bool CanFundIncomeCapture(PlayableAiObservation o,AiProfile p)=>HomeEconomyReady(o,p);
        internal bool HomeEconomyReady(PlayableAiObservation o,AiProfile p)=>producerChosen&&Ready(o,Producer)&&Income(o)>=RequiredHomeRefineryCount(p);
        public bool MissingEssentialEconomy(PlayableAiObservation o,AiProfile p)=>!HomeEconomyReady(o,p)||!initialForceFulfilled||ReinforcementPending(o);
        internal bool InitialGrowthCommitmentPending(PlayableAiObservation o,AiProfile p)=>HomeEconomyReady(o,p)&&(!initialForceFulfilled||ReinforcementPending(o));
        private int AvailableFighters(PlayableAiObservation o,PlayableEntityKind kind)=>o.Entities.Count(u=>u.Owner==o.Owner&&u.Health>0&&u.Kind==kind&&u.Id!=ProtectedUnit&&(CombatAvailable==null||CombatAvailable(u.Id)));
        internal IReadOnlyDictionary<PlayableEntityKind,int> DesiredForce(AiProfile p,int size)
        {
            var result=new Dictionary<PlayableEntityKind,int>();
            if(air){result[PlayableEntityKind.Hawk]=size;return result;}
            if(mixedForce&&size>1)
            {int explorers=Math.Max(1,Math.Min(size-1,(int)Math.Round(size*V(p,"openingExplorerShare"))));result[PlayableEntityKind.Explorer]=explorers;result[PlayableEntityKind.Tank]=size-explorers;}
            else result[forceKind]=size;
            return result;
        }
        internal int[] InitialMembers(PlayableAiObservation o,AiProfile p)
        {
            if(phase!=PlayableAiOpeningPhase.Active)return null;
            return DesiredForce(p,requiredForce).SelectMany(x=>o.Entities.Where(u=>u.Owner==o.Owner&&u.Health>0&&u.Kind==x.Key&&u.Id!=ProtectedUnit&&(CombatAvailable==null||CombatAvailable(u.Id))).OrderBy(u=>u.Id).Take(x.Value).Select(u=>u.Id)).OrderBy(x=>x).ToArray();
        }
        private int Committed(PlayableAiObservation o,PlayableEntityKind kind,AiBudgetState budget=null)=>AvailableFighters(o,kind)+o.Buildings.Where(b=>b.Owner==o.Owner&&b.PrivateState!=null).SelectMany(b=>b.PrivateState.Orders).Count(q=>q.Kind==kind)+(budget?.Unpaid??Array.Empty<AiBudgetEntry>()).Count(e=>e.Action.Kind==PlayableUnitRules.ProductionCommand(kind));
        internal int InitialForceFundingProtection(PlayableAiObservation o,PlayableProfile p,AiProfile ai,AiBudgetState budget)
        {
            if(!InitialGrowthCommitmentPending(o,ai))return 0;
            return DesiredForce(ai,ReinforcementPending(o)?reinforcementTarget:requiredForce).Sum(x=>Math.Max(0,Math.Max(0,x.Value-Committed(o,x.Key,budget))*PlayableUnitRules.Cost(p,x.Key)-(budget?.Reserved??Array.Empty<AiBudgetEntry>()).Where(e=>e.Purpose=="factory-startup"&&e.Action.UnitKind==x.Key).Sum(e=>e.Amount)));
        }
        internal int HomeEconomyQuote(PlayableAiObservation o,PlayableProfile p,AiProfile ai,IEnumerable<AiEconomyCandidate> normal)=>MacroRequests(o,p,ai,normal).Where(c=>c.Policy=="economy"&&(c.Legal||c.Reason=="insufficient credits")).Select(c=>TerritoryRules.Cost(p,c.Action.BuildingKind)).DefaultIfEmpty(0).Min();
        internal int HomeEconomyFundingProtection(PlayableAiObservation o,PlayableProfile p,AiProfile ai,AiBudgetState ledger)
        {
            var goals=MacroRequests(o,p,ai,Array.Empty<AiEconomyCandidate>()).Where(c=>c.Action.Kind==PlayableCommandKind.BuildAt&&(c.Legal||c.Reason=="insufficient credits")).GroupBy(c=>c.Action.BuildingKind);
            int quote=0;
            foreach(var g in goals)
            {
                // World foundations already suppress a request. Accepted unpaid builds and
                // true building holds cover only the matching goal; startup cycles cover units.
                if((ledger?.Unpaid??Array.Empty<AiBudgetEntry>()).Any(e=>e.Action.Kind==PlayableCommandKind.BuildAt&&e.Action.BuildingKind==g.Key))continue;
                int held=(ledger?.Reserved??Array.Empty<AiBudgetEntry>()).Where(e=>e.Purpose!="factory-startup"&&e.Action.Kind==PlayableCommandKind.BuildAt&&e.Action.BuildingKind==g.Key).Sum(e=>e.Amount);
                quote+=Math.Max(0,TerritoryRules.Cost(p,g.Key)-held);
            }
            return quote;
        }
        public void ObserveReceipt(PlayableAiAction action,PlayableAiTraceRecord receipt){} // Pressure completion uses a deployed army, never a receipt alone.
        internal bool AllowsPostAirFactory(PlayableAiObservation o,AiProfile ai)
        {
            bool solo=!o.Participants.Any(x=>x.Team==o.Team&&x.Id!=o.OwnerId);
            bool capturer=o.Entities.Any(u=>u.Owner==o.Owner&&u.Kind==PlayableEntityKind.Explorer&&u.Health>0);
            bool threat=o.Entities.Any(u=>o.IsHostile(u.Owner)&&u.Health>0)||o.OwnCenterDamage.Count>0;
            return !air||!solo||!capturer||Income(o)>=V(ai,threat?"airFactoryThreat":"airFactoryCalm");
        }
        public IReadOnlyList<AiEconomyCandidate> MacroRequests(PlayableAiObservation o,PlayableProfile p,AiProfile ai,IEnumerable<AiEconomyCandidate> normal,bool initialGrowthCommitmentPending=false,IEnumerable<AiEconomyCandidate> doctrineProducers=null)
        {
            var result=new List<AiEconomyCandidate>();
            void Build(PlayableBuildingKind k)
            {
                foreach(var site in o.Sites.Where(s=>s.Owner==o.Owner&&s.Ready&&!s.Contested).OrderBy(s=>s.Site.Id==o.HomeSiteId?0:1).ThenBy(s=>s.Site.Id))
                foreach(var slot in site.Site.Slots.OrderBy(s=>s.Id))
                {
                    var a=new PlayableAiAction(1,o.OwnerId,o.ProfileId,o.ProfileRevision,o.Generation,o.SnapshotSequence,PlayableCommandKind.BuildAt,siteId:site.Site.Id,slotId:slot.Id,parentId:site.CenterId,buildingKind:k,seed:o.Seed,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);
                    result.Add(new AiEconomyCandidate("economy",a,AiEconomyAdmission.Reject(o,p,a,true),1));
                }
            }
            int requested=RequiredHomeRefineryCount(ai);
            int committedIncome=CommittedIncome(o);
            if(committedIncome<requested)Build(PlayableBuildingKind.Refinery);
            if(producerChosen&&!o.Buildings.Any(b=>b.Owner==o.Owner&&b.Kind==Producer&&Live(b)))Build(Producer);
            bool lostCapturer=!o.Entities.Any(u=>u.Owner==o.Owner&&u.Health>0&&u.Kind==PlayableEntityKind.Explorer);
            bool solo=!o.Participants.Any(x=>x.Team==o.Team&&x.Id!=o.OwnerId);
            bool threat=o.Entities.Any(u=>o.IsHostile(u.Owner)&&u.Health>0)||o.OwnCenterDamage.Count>0;
            bool groundNeeded=air&&(lostCapturer||solo&&Income(o)>=V(ai,threat?"airFactoryThreat":"airFactoryCalm"));
            if(groundNeeded&&!o.Buildings.Any(b=>b.Owner==o.Owner&&b.Kind==PlayableBuildingKind.Factory&&Live(b)))Build(PlayableBuildingKind.Factory);
            int freeSlots=o.Sites.Where(s=>s.Owner==o.Owner&&s.Ready&&!s.Contested).Sum(s=>s.Site.Slots.Count(slot=>!o.Buildings.Any(b=>b.SiteId==s.Site.Id&&b.SlotId==slot.Id)));
            // Keep the final real slot for the mandatory Factory. The fourth calm income
            // can come from an existing Mine; never fill every slot with Refinery.
            bool reserveFactory=air&&solo&&!o.Buildings.Any(b=>b.Owner==o.Owner&&b.Kind==PlayableBuildingKind.Factory&&Live(b));
            if(air&&solo&&initialForceFulfilled&&committedIncome<V(ai,threat?"airFactoryThreat":"airFactoryCalm")&&freeSlots>1)Build(PlayableBuildingKind.Refinery);
            if((!initialForceFulfilled||ReinforcementPending(o))&&producerChosen)
            {
                var goal=DesiredForce(ai,ReinforcementPending(o)?reinforcementTarget:requiredForce);
                foreach(var wanted in goal.Where(x=>Committed(o,x.Key)<x.Value).OrderBy(x=>(double)Committed(o,x.Key)/Math.Max(1,x.Value)).ThenBy(x=>x.Key))
                {
                    var kind=wanted.Key;
                    foreach(var b in AiEconomyAdmission.Producers(o,AiRosterCatalog.Initial.For(kind)))
                    {
                        var a=new PlayableAiAction(1,o.OwnerId,o.ProfileId,o.ProfileRevision,o.Generation,o.SnapshotSequence,PlayableUnitRules.ProductionCommand(kind),new[]{b.Id},unitKind:kind,seed:o.Seed,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);
                        result.Add(new AiEconomyCandidate("production",a,AiEconomyAdmission.Reject(o,p,a,true),1));
                    }
                    if(result.Any(c=>c.Policy=="production"&&c.Legal))break;
                }
            }
            else if(initialForceFulfilled&&reinforceChosen&&Income(o)<V(ai,"doubleIncome")&&(!reinforce||DesiredForce(ai,reinforcementTarget).All(x=>AvailableFighters(o,x.Key)>=x.Value)))
            {if(committedIncome<V(ai,"doubleIncome"))Build(PlayableBuildingKind.Refinery);}
            else if(initialForceFulfilled&&!reserveFactory)result.AddRange(normal.Where(c=>c.Policy=="production"||c.Action.BuildingKind!=PlayableBuildingKind.Factory&&c.Action.BuildingKind!=PlayableBuildingKind.Hangar));
            // Recover capturers through a real Factory. Existing research/repair/infrastructure is retained after the opening force.
            if(lostCapturer&&Ready(o,PlayableBuildingKind.Factory))
                foreach(var b in AiProductionDemand.Lines(o))
                {var a=new PlayableAiAction(1,o.OwnerId,o.ProfileId,o.ProfileRevision,o.Generation,o.SnapshotSequence,PlayableCommandKind.QueueExplorer,new[]{b.Id},unitKind:PlayableEntityKind.Explorer,seed:o.Seed,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);result.Add(new AiEconomyCandidate("production",a,AiEconomyAdmission.Reject(o,p,a,true),1));}
            // Opening recovery owns an overlapping producer obligation. Doctrine contributes
            // complementary requests before canonical request admission, not a second intent.
            // The existing identity retains different types, sites and slots; opening priority wins.
            if(doctrineProducers!=null)result.AddRange(doctrineProducers);
            return result.GroupBy(c=>AiEconomyAdmission.IntentId(c.Policy,c.Action)).Select(g=>g.First()).ToArray();
        }
        internal void WriteState(BinaryWriter w)
        {w.Write(teamResolved);WorldWire.String(w,teamAirWinner);w.Write(mixedForce);w.Write(reinforcementFulfilled);w.Write(greedyAir);w.Write(reinforceChosen);w.Write(reinforce);w.Write(reinforcementTarget);w.Write(capturer);w.Write(capturerReleased);w.Write(started);w.Write(progress);w.Write(secondIncome);w.Write(producerChosen);w.Write(air);w.Write(initialForceFulfilled);w.Write(requiredForce);w.Write(refineries);w.Write(outpostThreshold);w.Write((int)forceKind);w.Write((int)phase);WorldWire.String(w,reason);WorldWire.Array(w,completed.OrderBy(x=>x).ToArray(),x=>WorldWire.String(w,x));}
        internal void ReadState(BinaryReader r,long tick)
        {teamResolved=WorldWire.Boolean(r);teamAirWinner=WorldWire.String(r);mixedForce=WorldWire.Boolean(r);reinforcementFulfilled=WorldWire.Boolean(r);greedyAir=WorldWire.Boolean(r);reinforceChosen=WorldWire.Boolean(r);reinforce=WorldWire.Boolean(r);reinforcementTarget=r.ReadInt32();capturer=r.ReadInt32();capturerReleased=WorldWire.Boolean(r);started=r.ReadInt64();progress=r.ReadInt64();secondIncome=r.ReadInt64();producerChosen=WorldWire.Boolean(r);air=WorldWire.Boolean(r);initialForceFulfilled=WorldWire.Boolean(r);requiredForce=r.ReadInt32();refineries=r.ReadInt32();outpostThreshold=r.ReadInt32();forceKind=WorldWire.EnumValue<PlayableEntityKind>(r);phase=WorldWire.EnumValue<PlayableAiOpeningPhase>(r);reason=WorldWire.String(r);foreach(var x in WorldWire.Array(r,()=>WorldWire.String(r)))AiStateWire.Require(new[]{"income","factory","force","pressure"}.Contains(x)&&completed.Add(x),"opening milestone");AiStateWire.Require(started>=-1&&started<=tick&&secondIncome>=-1&&secondIncome<=tick&&requiredForce>=0&&refineries>=0&&outpostThreshold>=0&&reason!=null,"opening clocks/values");
            AiStateWire.Require((!completed.Contains("factory")||completed.Contains("income"))&&(!completed.Contains("force")||completed.Contains("factory")&&initialForceFulfilled)&&(!completed.Contains("pressure")||completed.Contains("force"))&&(phase!=PlayableAiOpeningPhase.Complete||completed.Count==4),"opening milestone dependency state");}
    }
}
