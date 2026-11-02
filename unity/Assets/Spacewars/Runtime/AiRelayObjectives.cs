using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Runtime
{
    // Detached relay policy: public geography and owner-visible facts only. Mission
    // identity/lifetime lives in the existing serialized scout/army registry.
    internal static class AiRelayObjectives
    {
        internal static bool HasRelays(PlayableAiObservation o)=>o.PublicScoutObjectives.Any(t=>t.Role==PlayablePublicScoutObjectiveRole.Relay);
        internal static bool IsRelay(PlayableAiObservation o,int site)=>o.PublicScoutObjectives.Any(t=>t.SiteId==site&&t.Role==PlayablePublicScoutObjectiveRole.Relay);
        internal static string Claim(int site)=>"relay:"+site.ToString(System.Globalization.CultureInfo.InvariantCulture);
        internal static bool IsClaim(AiArmyState a)=>a.Objective!=null&&a.Objective.StartsWith("relay:",StringComparison.Ordinal);
        static double Distance(NavPoint a,NavPoint b)=>AiOpponentSelector.Distance(a,b);
        internal static PlayableEntitySnapshot[] Enemies(PlayableAiObservation o,PlayableProfile p,NavPoint point)=>
            o.Entities.Where(u=>u.Health>0&&o.IsHostile(u.Owner)&&Distance(u.Position,point)<=p.ExplorerVision).ToArray();
        internal static bool Needed(PlayableAiObservation o,PlayableProfile p,int site)
        {
            var s=o.Sites.FirstOrDefault(x=>x.Site.Id==site);
            if(s==null)return true; // Inspect public geography; hidden control is never assumed.
            if(Enemies(o,p,s.Site.Position).Length>0||s.Contested)return true;
            if(s.Owner.HasValue&&!o.IsHostile(s.Owner.Value))return false;
            return !s.Claimant.HasValue||s.Claimant==o.Owner||o.IsHostile(s.Claimant.Value);
        }
        internal static bool Viable(PlayableAiObservation o,PlayableProfile p,AiProfile ai,AiKnowledgeState knowledge,
            IEnumerable<PlayableEntitySnapshot> members,int site,NavPoint target)
        {
            var ours=members.ToArray();
            var point=o.Sites.FirstOrDefault(s=>s.Site.Id==site)?.Site.Position??target;
            // Include only allies actually present at this fight, never an unseen team army.
            var allies=o.Entities.Where(u=>u.Health>0&&u.Owner!=o.Owner&&!o.IsHostile(u.Owner)&&Distance(u.Position,point)<=p.RelayCaptureRadius);
            var force=ours.Concat(allies).GroupBy(u=>u.Id).Select(g=>g.First()).ToArray();
            return AiActiveStrategyPolicy.Force(force,p,ai)*ai.Value("activeStrategy.maximumEncounterRatio")>=AiActiveStrategyPolicy.Force(Enemies(o,p,point),p,ai)&&
                AiActiveStrategyPolicy.ViableApproach(o,p,ai,force,site,target,PlayableRouteTargetKind.PublicObjective,knowledge);
        }
        internal static bool Holding(PlayableAiObservation o,PlayableProfile p,int site,IEnumerable<PlayableEntitySnapshot> members)=>
            o.Sites.Any(s=>s.Site.Id==site&&members.Any(u=>Distance(u.Position,s.Site.Position)<=p.RelayCaptureRadius));
        internal static void ObserveScout(PlayableAiObservation o,AiKnowledgeState knowledge,AiArmyRegistry registry,AiProfile ai,PlayableProfile p,AiArmyState a)
        {
            var target=o.PublicScoutObjectives.FirstOrDefault(t=>t.Role==PlayablePublicScoutObjectiveRole.Relay&&Claim(t.SiteId)==a.Objective);
            var unit=o.Entities.FirstOrDefault(u=>u.Id==a.LeaderId&&u.Owner==o.Owner&&u.Health>0);
            bool holding=target!=null&&unit!=null&&Holding(o,p,target.SiteId,new[]{unit});
            if(target==null||!target.Reachable||unit==null||unit.Held||unit.OrderStamp?.Origin==PlayableOrderOrigin.Human||
                !Needed(o,p,target.SiteId)||!Viable(o,p,ai,knowledge,new[]{unit},target.SiteId,target.Approach)||
                !holding&&o.Tick-a.ProgressTick>=AiProfile.SecondsToTicks(ai.Value("armies.stallSeconds"),30))
            {registry.Disband(a.Id);return;}
            registry.UpdateMission(a.Id,AiScoutPlanner.Policy,holding?AiArmyPhase.Engaging:AiArmyPhase.Advancing,o.Tick,a.Objective,unit.Position,target.Approach,
                progressed:holding&&!o.Sites.Any(s=>s.Site.Id==target.SiteId&&s.Contested)||a.Anchor.HasValue&&!unit.Position.Equals(a.Anchor.Value));
        }
        internal static PlayableAiAction Combat(PlayableAiObservation o,PlayableProfile p,PlayableEntitySnapshot[] units,int site,long actionId)
        {
            var relay=o.Sites.FirstOrDefault(s=>s.Site.Id==site);if(relay==null)return null;
            var enemy=Enemies(o,p,relay.Site.Position).OrderBy(e=>units.Select(u=>Distance(u.Position,e.Position)).DefaultIfEmpty(double.MaxValue).Min()).ThenBy(e=>e.Id)
                .FirstOrDefault(e=>units.Any(u=>o.RouteProofs.Any(r=>r.UnitId==u.Id&&r.Kind==PlayableRouteTargetKind.VisibleEnemy&&r.TargetId==e.Id)));
            if(enemy==null)return null;
            var ids=units.Where(u=>o.RouteProofs.Any(r=>r.UnitId==u.Id&&r.Kind==PlayableRouteTargetKind.VisibleEnemy&&r.TargetId==enemy.Id)).Select(u=>u.Id).ToArray();
            return new PlayableAiAction(actionId,o.OwnerId,o.ProfileId,o.ProfileRevision,o.Generation,o.SnapshotSequence,PlayableCommandKind.Attack,ids,enemy.Position,
                siteId:site,targetId:enemy.Id,seed:o.Seed,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);
        }
        internal static bool CombatOrder(PlayableAiObservation o,PlayableProfile p,int site,PlayableEntitySnapshot unit)
        {
            var s=o.Sites.FirstOrDefault(x=>x.Site.Id==site);var stamp=unit.OrderStamp;
            return s!=null&&unit.CurrentOrder?.Kind==PlayableTacticalOrderKind.Attack&&stamp?.Origin==PlayableOrderOrigin.Ai&&stamp.Owner==o.Owner&&
                stamp.Source==PlayableAiOpeningComposition.SourceIdentity&&stamp.Generation==o.Generation&&
                Enemies(o,p,s.Site.Position).Any(e=>e.Id==unit.CurrentOrder.TargetId);
        }
        internal static PlayableAiAction PlanScout(PlayableAiObservation o,AiKnowledgeState knowledge,AiArmyRegistry registry,PlayableProfile p,AiProfile ai,int budget,int reservedActor)
        {
            if(!HasRelays(o)||budget<=0)return null;
            var claims=registry.Capture().Armies.Where(a=>a.Role==AiArmyRole.Scout&&a.Phase!=AiArmyPhase.Disbanded).ToArray();
            var own=claims.Where(a=>a.TacticalOwner==AiScoutPlanner.Policy&&IsClaim(a)).OrderBy(a=>a.Id).FirstOrDefault();
            var units=o.Entities.Where(u=>u.Owner==o.Owner&&u.Health>0&&u.Kind==PlayableEntityKind.Explorer&&u.Id!=reservedActor&&!u.Held&&
                u.OrderStamp?.Origin!=PlayableOrderOrigin.Human&&(registry.ArmyFor(u.Id)==0||own!=null&&own.Members.Contains(u.Id))).ToArray();
            if(own==null&&claims.Length>=budget)return null;
            var choices=from t in o.PublicScoutObjectives
                where t.Role==PlayablePublicScoutObjectiveRole.Relay&&t.Reachable&&Needed(o,p,t.SiteId)&&
                    (own==null||own.Objective==Claim(t.SiteId))
                from u in units
                where Viable(o,p,ai,knowledge,new[]{u},t.SiteId,t.Approach)
                from route in o.RouteProofs.Where(r=>r.UnitId==u.Id&&r.Kind==PlayableRouteTargetKind.PublicObjective&&r.TargetId==t.SiteId&&r.Target.Equals(t.Approach))
                where AiStaticDefenseSafety.Withdrawal(o,p,u,route.Path,knowledge)
                orderby Enemies(o,p,o.Sites.FirstOrDefault(s=>s.Site.Id==t.SiteId)?.Site.Position??t.Approach).Length>0 descending,
                    AiExpansionPlanner.RouteLength(route),t.SiteId,u.Id
                select new{t,u,route};
            var choice=choices.FirstOrDefault();if(choice==null)return null;
            var combat=Combat(o,p,new[]{choice.u},choice.t.SiteId,1);
            if(combat!=null)return combat;
            if(own!=null&&Holding(o,p,choice.t.SiteId,new[]{choice.u}))return null; // Presence and automatic combat finish the capture.
            return new PlayableAiAction(1,o.OwnerId,o.ProfileId,o.ProfileRevision,o.Generation,o.SnapshotSequence,
                PlayableCommandKind.AttackMove,new[]{choice.u.Id},choice.t.Approach,siteId:choice.t.SiteId,seed:o.Seed,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);
        }
    }
}
