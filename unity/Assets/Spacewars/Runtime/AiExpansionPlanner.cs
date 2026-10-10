using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Runtime
{
    public sealed class AiExpansionState
    {
        internal AiExpansionState(int site,int actor,long created,long expires,long progress,string phase,string reason)
        {SiteId=site;ActorId=actor;CreatedTick=created;ExpiresTick=expires;ProgressTick=progress;Phase=phase;Reason=reason;}
        public int SiteId{get;} public int ActorId{get;} public long CreatedTick{get;} public long ExpiresTick{get;} public long ProgressTick{get;}
        public string Phase{get;} public string Reason{get;}
    }
    // Detached safe expansion. A selected fork owns one site and one Explorer until
    // observed settlement/loss/expiry. Neither command acceptance nor a receipt creates territory or income.
    public sealed class AiExpansionPlanner
    {
        private readonly string ownerId;
        private int siteId,actorId;
        private long created,expires,progress,pendingId,pendingGeneration,nextId=1;
        private double capture,distance=double.MaxValue,initialIncome;
        private string phase="idle",reason="no safe known expansion";
        private Dictionary<int,string> failed=new Dictionary<int,string>();
        private string condition;
        public AiExpansionPlanner(string ownerId){this.ownerId=ownerId??throw new ArgumentNullException(nameof(ownerId));}
        internal AiExpansionPlanner Fork(){var copy=(AiExpansionPlanner)MemberwiseClone();copy.failed=new Dictionary<int,string>(failed);return copy;}
        public AiExpansionState Capture()=>new AiExpansionState(siteId,actorId,created,expires,progress,phase,reason);
        public bool ClaimsActor(int id)=>siteId!=0&&actorId==id;
        public bool ClaimsSite(int id)=>siteId!=0&&siteId==id;
        internal long PendingId=>pendingId;
        internal long PendingGeneration=>pendingGeneration;
        private static double Distance(NavPoint a,NavPoint b)=>Math.Sqrt((a.X-b.X)*(a.X-b.X)+(a.Z-b.Z)*(a.Z-b.Z));
        private static string Condition(TerritorySiteSnapshot s,PlayableEntitySnapshot actor,PlayableAiObservation o,PlayableProfile p,AiProfile ai)
        {
            if(s==null)return "unknown-site";
            if(actor==null)return "lost-actor";
            double range=PlayableUnitRules.Vision(p,actor.Kind);
            var hostileUnits=o.Entities.Where(e=>o.IsHostile(e.Owner)&&e.Health>0&&(Distance(e.Position,s.Site.Position)<=range||Distance(e.Position,actor.Position)<=range)).OrderBy(e=>e.Id).Select(e=>"u"+e.Id);
            var hostileBuildings=o.Buildings.Where(b=>o.IsHostile(b.Owner)&&b.Health>0&&(Distance(b.Position,s.Site.Position)<=range||Distance(b.Position,actor.Position)<=range)).OrderBy(b=>b.Id).Select(b=>"b"+b.Id);
            return s.Owner+":"+s.Claimant+":"+s.CenterId+":"+s.Contested+":"+actor.Id+":"+string.Join(",",hostileUnits.Concat(hostileBuildings))+":"+(o.Credits>=TerritoryRules.Cost(p,s.Site.Kind))+":"+p.ProfileId+"@"+p.Revision+":"+ai.Hash;
        }
        public static PlayableRouteRequest[] RouteRequests(PlayableAiObservation o,PlayableProfile profile)=>
            RouteRequests(o.Entities,o.Sites,o.PublicScoutObjectives,o.Owner,o.Generation,o.Tick,profile);
        // Both entrypoints accept only already fog-filtered detached data. No domain or hidden occupancy.
        internal static PlayableRouteRequest[] RouteRequests(IEnumerable<PlayableEntitySnapshot> entities,IEnumerable<TerritorySiteSnapshot> sites,
            IEnumerable<PlayablePublicScoutObjective> objectives,PlayableOwner owner,long generation,long tick,PlayableProfile profile)=>
            entities.Where(u=>u.Owner==owner&&u.Kind==PlayableEntityKind.Explorer&&u.Health>0).OrderBy(u=>u.Id)
            .SelectMany(u=>objectives.Where(g=>g.Role!=PlayablePublicScoutObjectiveRole.PossibleEnemyStart&&g.Reachable&&
                sites.Any(s=>s.Site.Id==g.SiteId&&s.CenterId==0&&!s.Contested&&(!s.Owner.HasValue||s.Owner==owner)&&(!s.Claimant.HasValue||s.Claimant==owner)))
                .OrderBy(g=>g.SiteId).Select(g=>new PlayableRouteRequest(u.Id,PlayableRouteTargetKind.PublicObjective,g.SiteId,generation,tick,u.Position,PlayableUnitRules.Radius(profile,u.Kind),g.Approach))).ToArray();
        private static bool Safe(PlayableAiObservation o,PlayableProfile p,AiProfile ai,TerritorySiteSnapshot s,PlayableEntitySnapshot actor,PlayableRouteProof route)
        {
            var points=route==null?new[]{s.Site.Position,actor.Position}:route.Path.Concat(new[]{s.Site.Position,actor.Position}).ToArray();
            var range=PlayableUnitRules.Vision(p,actor.Kind);
            bool danger=o.Entities.Where(e=>o.IsHostile(e.Owner)&&e.Health>0&&points.Any(x=>Distance(x,e.Position)<=range)).Any()||
                o.Buildings.Where(b=>o.IsHostile(b.Owner)&&b.Health>0&&points.Any(x=>Distance(x,b.Position)<=range)).Any();
            // E4 has no escort/force evaluator. Any known local hostile requires S3 escort.
            return !s.Contested&&!danger;
        }
        public void Observe(PlayableAiObservation o,PlayableProfile p,AiProfile ai)
        {
            if(o.OwnerId!=ownerId)throw new ArgumentException("Foreign expansion observation.");
            if(siteId==0)return;
            var actor=o.Entities.FirstOrDefault(u=>u.Id==actorId&&u.Owner==o.Owner&&u.Health>0);
            var site=o.Sites.FirstOrDefault(s=>s.Site.Id==siteId);
            condition=Condition(site,actor,o,p,ai);
            if(o.Tick>=expires||actor==null||site==null||site.Owner.HasValue&&site.Owner!=o.Owner||site.Claimant.HasValue&&site.Claimant!=o.Owner||!Safe(o,p,ai,site,actor,null))
            {Release(o.Tick>=expires?"commitment expired":actor==null?"actor lost":"site lost/unknown/unsafe",true);return;}
            double current=Distance(actor.Position,site.Site.Position);
            var center=o.Buildings.FirstOrDefault(b=>b.Id==site.CenterId&&b.Owner==o.Owner&&b.Health>0);
            string next=center!=null?(center.Phase==ConstructionPhase.Ready?"income":"construction"):site.Progress>=1&&site.Claimant==o.Owner?"build":"capture";
            if(next=="income"&&phase!="income")initialIncome=o.SettledIncome;
            if(current<distance||site.Progress>capture||next!=phase)progress=o.Tick;
            distance=current;capture=site.Progress;phase=next;
            // A full observed settlement must cover the current total rate including this
            // live center; a smaller HQ-only credit cannot finish expansion.
            if(center?.Phase==ConstructionPhase.Ready&&center.PrivateState?.Lifecycle?.Selling!=true&&TerritoryRules.Income(p,center.Kind)>0&&
                o.IncomePerSecond>0&&o.SettledIncome-initialIncome>=o.IncomePerSecond)
                Release("observed ready income center and settled owner income",false);
        }
        public PlayableAiAction TryPlan(PlayableAiObservation o,PlayableProfile p,AiProfile ai)
        {
            if(o.OwnerId!=ownerId)throw new ArgumentException("Foreign expansion observation.");
            if(pendingId!=0)return null;
            bool newClaim=siteId==0;
            TerritorySiteSnapshot site=null;PlayableEntitySnapshot actor=null;PlayablePublicScoutObjective objective=null;
            if(siteId==0)
            {
                var choices=from s in o.Sites
                    where (s.Site.Kind==PlayableBuildingKind.Mine||s.Site.Kind==PlayableBuildingKind.Outpost)&&TerritoryRules.Income(p,s.Site.Kind)>0&&s.CenterId==0&&!s.Contested&&(!s.Owner.HasValue||s.Owner==o.Owner)&&(!s.Claimant.HasValue||s.Claimant==o.Owner)&&o.Credits>=TerritoryRules.Cost(p,s.Site.Kind)
                    from u in o.Entities
                    where u.Owner==o.Owner&&u.Kind==PlayableEntityKind.Explorer&&u.Health>0&&!u.Moving&&u.CurrentOrder==null
                    let g=o.PublicScoutObjectives.FirstOrDefault(g=>g.SiteId==s.Site.Id&&g.Reachable&&g.Role!=PlayablePublicScoutObjectiveRole.PossibleEnemyStart)
                    let route=o.RouteProofs.FirstOrDefault(r=>r.UnitId==u.Id&&r.TargetId==s.Site.Id&&r.Kind==PlayableRouteTargetKind.PublicObjective)
                    where g!=null&&route!=null&&Safe(o,p,ai,s,u,route)&&(!failed.TryGetValue(s.Site.Id,out var f)||f!=Condition(s,u,o,p,ai))
                    let length=RouteLength(route)
                    orderby TerritoryRules.Cost(p,s.Site.Kind)/(TerritoryRules.Income(p,s.Site.Kind)/p.IncomePeriodSeconds),length,s.Site.Id,u.Id
                    select new{s,u,g};
                var best=choices.FirstOrDefault();if(best==null)return null;
                site=best.s;actor=best.u;objective=best.g;
                siteId=site.Site.Id;actorId=actor.Id;pendingActionBuildingKind=site.Site.Kind;created=progress=o.Tick;
                expires=checked(o.Tick+AiProfile.SecondsToTicks(ai.Value("economy.reservationExpirySeconds"),30));
                capture=site.Progress;distance=Distance(actor.Position,site.Site.Position);initialIncome=o.SettledIncome;
                phase="capture";reason="safe known reachable income expansion";condition=Condition(site,actor,o,p,ai);
            }
            else
            {site=o.Sites.FirstOrDefault(s=>s.Site.Id==siteId);actor=o.Entities.FirstOrDefault(u=>u.Id==actorId&&u.Owner==o.Owner&&u.Health>0);objective=o.PublicScoutObjectives.FirstOrDefault(g=>g.SiteId==siteId);}
            if(site==null||actor==null||objective==null)return null;
            PlayableCommandKind kind;NavPoint target=default;int[] units=null;
            if(site.CenterId!=0)return null;
            if(site.Progress>=1&&site.Claimant==o.Owner){kind=PlayableCommandKind.BuildAt;units=new[]{actorId};if(o.Credits<TerritoryRules.Cost(p,site.Site.Kind))return null;}
            else
            {
                // Holding within capture radius is progress through gameplay, not an order to resend.
                if(!newClaim&&Distance(actor.Position,site.Site.Position)<=TerritoryRules.CaptureRadius(p,site.Site.Kind))return null;
                if(actor.Moving||actor.CurrentOrder!=null&&actor.CurrentOrder.Kind==PlayableTacticalOrderKind.Move&&actor.CurrentOrder.Destination.Equals(objective.Approach))return null;
                if(!o.RouteProofs.Any(r=>r.UnitId==actorId&&r.TargetId==siteId&&r.Kind==PlayableRouteTargetKind.PublicObjective))return null;
                kind=PlayableCommandKind.Move;target=objective.Approach;units=new[]{actorId};
            }
            pendingId=nextId++;pendingGeneration=o.Generation;
            return new PlayableAiAction(pendingId,o.OwnerId,o.ProfileId,o.ProfileRevision,o.Generation,o.SnapshotSequence,kind,units,target,siteId:siteId,buildingKind:site.Site.Kind,seed:o.Seed,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);
        }
        private static double RouteLength(PlayableRouteProof r){double total=0;var from=r.Origin;foreach(var point in r.Path){total+=Distance(from,point);from=point;}return total;}
        public void ObserveReceipt(PlayableAiTraceRecord r)
        {
            if(r==null||r.OwnerId!=ownerId||r.SourceIdentity!=PlayableAiOpeningComposition.SourceIdentity||r.ActionId!=pendingId||!AiStateWire.CallbackGenerationMatches(r,pendingGeneration))return;
            if(r.Status==PlayableAiDeliveryStatus.Scheduled||r.Status==PlayableAiDeliveryStatus.Accepted)return;
            pendingId=0;pendingGeneration=0;
            if(r.Status==PlayableAiDeliveryStatus.Applied&&r.Kind==PlayableCommandKind.BuildAt)phase="construction";
            if(r.Status!=PlayableAiDeliveryStatus.Applied&&r.Status!=PlayableAiDeliveryStatus.Cancelled)Release("terminal "+r.Status,true);
        }
        internal void Release(string why,bool failure=false)
        {if(failure&&siteId!=0)failed[siteId]=condition;siteId=actorId=0;created=expires=progress=0;capture=initialIncome=0;distance=double.MaxValue;phase="idle";reason=why;condition=null;}
        internal void Rebind(long tick,AiProfile ai){if(siteId!=0)expires=Math.Min(expires,checked(created+AiProfile.SecondsToTicks(ai.Value("economy.reservationExpirySeconds"),30)));}
        internal const string FundingPurpose="expansion-build";
        internal static bool IsFunding(AiBudgetEntry entry)=>entry.Purpose==FundingPurpose;
        private string FundingId=>FundingPurpose+":"+siteId+":"+actorId;
        private bool FundingTarget(PlayableAiAction a)=>siteId!=0&&a.Kind==PlayableCommandKind.BuildAt&&a.SiteId==siteId&&a.SlotId==0&&a.ParentId==0&&
            (a.BuildingKind==PlayableBuildingKind.Mine||a.BuildingKind==PlayableBuildingKind.Outpost)&&a.PlayerId==ownerId&&a.EntityIds.SequenceEqual(new[]{actorId});
        internal bool HasFundingFor(PlayableAiAction a,AiBudgetLedger budget)=>budget.Capture().Reserved.Any(e=>IsFunding(e)&&e.Action.SiteId==a.SiteId&&e.Action.PlayerId==a.PlayerId&&e.Action.Generation==a.Generation&&e.Action.BuildingKind==a.BuildingKind&&e.Action.EntityIds.SequenceEqual(a.EntityIds));
        internal string ReservationFor(PlayableAiAction action,AiBudgetLedger budget)=>FundingTarget(action)&&budget.ReservationEntry(FundingId)!=null?FundingId:null;
        internal bool FundTargetValid(AiBudgetEntry e,PlayableAiObservation o)=>e.Id==FundingId&&FundingTarget(e.Action)&&o.Tick<expires&&
            o.Entities.Any(u=>u.Id==actorId&&u.Owner==o.Owner&&u.Kind==PlayableEntityKind.Explorer&&u.Health>0)&&
            o.Sites.Any(s=>s.Site.Id==siteId&&s.Site.Kind==e.Action.BuildingKind&&s.CenterId==0&&!s.Contested&&(!s.Owner.HasValue||s.Owner==o.Owner)&&(!s.Claimant.HasValue||s.Claimant==o.Owner));
        private AiBudgetEntry Fund(PlayableAiAction desired,PlayableProfile p)=>new AiBudgetEntry(FundingId,FundingPurpose,desired,TerritoryRules.Cost(p,desired.BuildingKind),1,created,expires,
            "actual build admission / actor-site loss / rejected capture / survival preemption / expiry",new[]{"slot:"+siteId+":0"},p.ProfileId+"@"+p.Revision);
        internal void BeginFunding(AiIntent intent,AiBudgetLedger budget,PlayableProfile p)
        {
            if(intent.Action.Kind!=PlayableCommandKind.Move||intent.StartupFund==0)return;
            var a=intent.Action;
            var desired=new PlayableAiAction(a.ActionId,ownerId,p.ProfileId,p.Revision,a.Generation,a.SnapshotSequence,PlayableCommandKind.BuildAt,new[]{actorId},siteId:siteId,buildingKind:a.BuildingKind,seed:a.Seed,sourceIdentity:a.SourceIdentity);
            AiStateWire.Require(intent.StartupFund==TerritoryRules.Cost(p,desired.BuildingKind)&&FundingTarget(desired),"selected expansion funding target");
            if(!budget.Reserve(Fund(desired,p)))throw new InvalidOperationException("Selected expansion fund no longer fits owner ledger.");
        }
        internal void UseFunding(AiIntent intent,AiBudgetLedger budget)
        {
            if(intent.ReservationId==null)return;
            var held=budget.ReservationEntry(intent.ReservationId);
            AiStateWire.Require(intent.ReservationId==FundingId&&held!=null&&IsFunding(held)&&FundingTarget(intent.Action)&&
                FundingTarget(held.Action)&&held.Action.BuildingKind==intent.Action.BuildingKind&&held.Amount==intent.Credits,"expansion fund admission/target/amount");
            if(!budget.Release(held.Id))throw new InvalidOperationException("Expansion admission lost its held fund.");
        }
        internal void TerminalFunding(AiIntent intent,PlayableAiDeliveryStatus status,AiBudgetLedger budget,PlayableProfile p)
        {
            // A pause/profile/human cancellation returned the unpaid build allocation,
            // not a gameplay refund. Reprotect the same valid future commitment.
            if(status==PlayableAiDeliveryStatus.Cancelled&&FundingTarget(intent.Action)&&budget.ReservationEntry(FundingId)==null)
                if(!budget.Reserve(Fund(intent.Action,p)))Release("cancelled build funding no longer fits",true);
        }
        internal void ReconcileFunding(AiBudgetLedger budget,PlayableProfile p,long tick,IEnumerable<AiIntent> pending=null)
        {
            foreach(var held in budget.Capture().Reserved.Where(IsFunding).ToArray())
                if(siteId==0||held.Id!=FundingId||!FundingTarget(held.Action)||phase=="construction"||phase=="income"||tick>=expires)
                    budget.Release(held.Id);
            if(siteId!=0&&(phase=="capture"||phase=="build")&&
                !(pending??Array.Empty<AiIntent>()).Any(i=>i.Policy=="expansion"&&FundingTarget(i.Action))&&
                budget.ReservationEntry(FundingId)==null&&TerritoryRules.Cost(p,
                    // An active claim's desired type is retained in the selected Move or
                    // future fund; unbacked rebinds release it instead of inventing funding.
                    pendingActionBuildingKind)>0)
                Release("future expansion funding lost or expired",true);
        }
        // Desired kind is serialized already in the ledger when funded; retaining this
        // private type also handles a genuinely free capture and cancelled immediate build.
        private PlayableBuildingKind pendingActionBuildingKind;
        internal void ValidateFunding(AiBudgetLedger budget,IReadOnlyList<AiIntent> pending,PlayableProfile p,long generation)
        {
            var held=budget.Capture().Reserved.Where(IsFunding).ToArray();
            AiStateWire.Require(held.Length<=1&&held.All(e=>e.Id==FundingId&&FundingTarget(e.Action)&&e.Action.Generation==generation&&
                e.Action.BuildingKind==pendingActionBuildingKind&&e.Amount==TerritoryRules.Cost(p,e.Action.BuildingKind)&&e.Terms==p.ProfileId+"@"+p.Revision&&
                e.CreatedTick==created&&e.ExpiresTick==expires&&e.Claims.SequenceEqual(new[]{"slot:"+siteId+":0"})),"expansion held commitment binding");
            AiStateWire.Require(pending.Where(i=>i.ReservationId!=null&&i.Policy=="expansion").All(i=>i.ReservationId==FundingId&&FundingTarget(i.Action)&&i.StartupFund==0&&i.Credits==TerritoryRules.Cost(p,i.Action.BuildingKind)),"expansion reservation pending binding");
            AiStateWire.Require(siteId==0||phase=="construction"||phase=="income"||TerritoryRules.Cost(p,pendingActionBuildingKind)==0||held.Length==1||pending.Any(i=>i.Policy=="expansion"&&FundingTarget(i.Action)),"unfunded live expansion commitment");
        }
        internal void WriteState(BinaryWriter w)
        {
            w.Write((int)pendingActionBuildingKind);w.Write(siteId);w.Write(actorId);w.Write(created);w.Write(expires);w.Write(progress);w.Write(pendingId);w.Write(pendingGeneration);w.Write(nextId);
            w.Write(capture);w.Write(distance);w.Write(initialIncome);WorldWire.String(w,phase);WorldWire.String(w,reason);WorldWire.String(w,condition);
            WorldWire.Array(w,failed.OrderBy(x=>x.Key).ToArray(),x=>{w.Write(x.Key);WorldWire.String(w,x.Value);});
        }
        internal void ReadState(BinaryReader r,long tick,long generation)
        {
            pendingActionBuildingKind=WorldWire.EnumValue<PlayableBuildingKind>(r);siteId=r.ReadInt32();actorId=r.ReadInt32();created=r.ReadInt64();expires=r.ReadInt64();progress=r.ReadInt64();pendingId=r.ReadInt64();pendingGeneration=r.ReadInt64();nextId=r.ReadInt64();
            capture=WorldWire.Number(r);distance=WorldWire.Number(r);initialIncome=WorldWire.Number(r);phase=WorldWire.String(r);reason=WorldWire.String(r);condition=WorldWire.String(r);
            failed=WorldWire.Array(r,()=>new KeyValuePair<int,string>(r.ReadInt32(),WorldWire.String(r))).ToDictionary(x=>x.Key,x=>x.Value);
            AiStateWire.Require((siteId==0||pendingActionBuildingKind==PlayableBuildingKind.Mine||pendingActionBuildingKind==PlayableBuildingKind.Outpost)&&siteId>=0&&actorId>=0&&(siteId==0)==(actorId==0)&&created>=0&&created<=tick&&progress>=created&&progress<=tick&&
                (siteId==0?expires==0:expires>created)&&capture>=0&&capture<=1&&distance>=0&&initialIncome>=0&&nextId>pendingId&&pendingId>=0&&
                (pendingId==0?pendingGeneration==0:pendingGeneration==generation)&&failed.All(x=>x.Key>0&&x.Value!=null)&&
                (phase=="idle"||phase=="capture"||phase=="build"||phase=="construction"||phase=="income")&&reason!=null&&(siteId==0||condition!=null),"expansion state/clocks/callback");
        }
    }
}
