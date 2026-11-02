using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Runtime.CompilerServices;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Runtime
{
    // Only owner/team observations and public map geometry enter this planner.
    // Persistent choices, timers, knowledge and pending actions belong to their existing authorities.
    public static class AiOrbitalPolicy
    {
        public const string Policy="orbital";
        // Profiles are immutable; avoid reflecting hundreds of gameplay fields per candidate.
        private static readonly ConditionalWeakTable<PlayableProfile,PlayableProfileData> settings=new ConditionalWeakTable<PlayableProfile,PlayableProfileData>();
        private static PlayableProfileData Data(PlayableProfile p)=>settings.GetValue(p,key=>key.CopyData());
        internal static double ScanRadius(PlayableProfile p)=>Data(p).orbitalScanRadius;
        private sealed class Opportunity
        {
            internal int Ability,Entity;internal NavPoint Point;internal double Utility;
        }
        private static double V(AiProfile ai,string key)=>ai.Value("orbital."+key);
        private static double D(NavPoint a,NavPoint b)=>AiOpponentSelector.Distance(a,b);
        private static bool Visible(PlayableAiObservation o,NavPoint p)=>o.Vision?.IsVisible(p)==true;
        public static bool IsHack(PlayableAiAction a)=>a.Kind==PlayableCommandKind.UseOrbital&&a.TargetId==OrbitalAbilities.ControlHack;
        private static bool Ready(PlayableAiObservation o,int id)=>o.Orbital.CommonRemaining<=0&&o.Orbital.Abilities.Any(a=>a.Id==id&&a.Remaining<=0);
        private static double Health(PlayableEntitySnapshot u)=>u.ExactHealth??u.Health;
        private static double Health(PlayableBuildingSnapshot b)=>b.ExactHealth??b.Health;
        public static bool HackValid(PlayableAiObservation o,PlayableProfile p,int id)
        {
            var data=Data(p);double threshold=data.orbitalHackHealthPercent/100;
            var u=o.Entities.FirstOrDefault(e=>e.Id==id&&o.IsHostile(e.Owner)&&Visible(o,e.Position));
            if(u!=null)return Health(u)>0&&Health(u)<PlayableUnitRules.MaximumHealth(p,u)*threshold;
            var b=o.Buildings.FirstOrDefault(e=>e.Id==id&&e.Kind==PlayableBuildingKind.Mine&&o.IsHostile(e.Owner)&&Visible(o,e.Position));
            return b!=null&&Health(b)>0&&b.MaxHealth>0&&Health(b)<b.MaxHealth*threshold;
        }
        private static PlayableAiAction Action(PlayableAiObservation o,string source,PlayableCommandKind kind,int ability=0,int[] ids=null,NavPoint point=default(NavPoint))
            // Stateless local ID; the authority assigns a globally unique action ID before scheduling.
            =>new PlayableAiAction(1,o.OwnerId,o.ProfileId,o.ProfileRevision,o.Generation,o.SnapshotSequence,kind,ids,point,targetId:ability,seed:o.Seed,sourceIdentity:source);
        public static PlayableAiAction Plan(PlayableAiObservation o,PlayableProfile p,AiProfile ai,AiKnowledgeState knowledge,AiPersonality personality,IEnumerable<PlayableAiTraceRecord> records,string source,Func<NavPoint,long> lastScan=null)
        {
            if(o.Orbital.Level==0)return null;
            var opportunities=(o.Orbital.PendingLevel==1||o.Orbital.PendingLevel==2||o.Orbital.CommonRemaining<=0&&o.Orbital.Abilities.Any(a=>a.Remaining<=0&&OrbitalAbilities.Tier(a.Id)==1))?
                Opportunities(o,p,ai,knowledge,lastScan).ToArray():Array.Empty<Opportunity>();
            if(o.Orbital.PendingLevel>0)
            {
                // Keep the five permanent slots sequential, including future effect placeholders.
                // Selection is harmless; no placeholder is ever activated.
                int choice=o.Orbital.Options.OrderByDescending(id=>ChoiceScore(o,p,ai,personality,opportunities,id)).ThenBy(id=>id).FirstOrDefault();
                if(choice!=0)return Action(o,source,PlayableCommandKind.ChooseOrbital,choice);
            }
            var best=opportunities.Where(c=>Ready(o,c.Ability)&&c.Utility>=V(ai,"minimumUtility"))
                .OrderByDescending(c=>c.Utility).ThenBy(c=>c.Ability).ThenBy(c=>c.Entity).ThenBy(c=>c.Point.X).ThenBy(c=>c.Point.Z).FirstOrDefault();
            if(best!=null)return Action(o,source,PlayableCommandKind.UseOrbital,best.Ability,best.Entity==0?null:new[]{best.Entity},best.Point);
            // A hack resets orders. Give that acquired actor a first useful ordinary order;
            // subsequent grouping, repair and fighting remain with the existing planners.
            var acquired=o.Entities.Where(u=>u.Owner==o.Owner&&u.Health>0&&u.CurrentOrder==null&&u.OrderStamp?.Origin==PlayableOrderOrigin.Ai&&
                records.Any(r=>r.Policy==Policy&&r.Kind==PlayableCommandKind.UseOrbital&&r.Status==PlayableAiDeliveryStatus.Applied&&r.ActionId==u.OrderStamp.ActionId)).OrderBy(u=>u.Id).FirstOrDefault();
            if(acquired==null)return null;
            var anchor=o.Entities.Where(u=>u.Id!=acquired.Id&&u.Owner==o.Owner&&u.Health>0).OrderBy(u=>D(u.Position,acquired.Position)).ThenBy(u=>u.Id).FirstOrDefault();
            if(anchor!=null)return Action(o,source,PlayableCommandKind.Follow,anchor.Id,new[]{acquired.Id},anchor.Position);
            var home=o.Buildings.Where(b=>b.Owner==o.Owner&&b.Health>0&&(b.Kind==PlayableBuildingKind.Headquarters||b.Kind==PlayableBuildingKind.Outpost)).OrderBy(b=>D(b.Position,acquired.Position)).FirstOrDefault();
            return home==null?null:Action(o,source,PlayableCommandKind.Move,ids:new[]{acquired.Id},point:home.Rally);
        }
        private static double ChoiceScore(PlayableAiObservation o,PlayableProfile p,AiProfile ai,AiPersonality personality,Opportunity[] options,int id)
        {
            double score=options.Where(c=>c.Ability==id).Select(c=>c.Utility).DefaultIfEmpty(0).Max();
            if(id==1)score+=V(ai,"scanUtility")*(o.Entities.Any(u=>u.Owner==o.Owner&&u.Kind==PlayableEntityKind.Shkval)?1:0);
            if(id==2&&o.Entities.Any(u=>o.IsHostile(u.Owner))&&!o.Entities.Any(u=>o.IsHostile(u.Owner)&&u.Kind!=PlayableEntityKind.Hawk))score=0;
            if(id==OrbitalAbilities.ControlHack)score+=o.Entities.Where(u=>o.IsHostile(u.Owner)&&u.Health>0).Select(u=>PlayableUnitRules.Cost(p,u.Kind)*V(ai,"hackValueScale")*V(ai,"hackSurvivalFloor")).DefaultIfEmpty(0).Max();
            if(personality==AiPersonality.Defensive&&id==2||personality==AiPersonality.Aggressive&&id==OrbitalAbilities.ControlHack||personality==AiPersonality.Harassing&&id==1)score+=V(ai,"personalityBonus");
            return score+V(ai,"choiceVariation")*(AiRandom.Key(o.Seed,o.OwnerId,ai.Hash,"orbital-choice-"+id,o.Orbital.PendingLevel)/(double)uint.MaxValue);
        }
        public static bool CanDeliver(PlayableAiObservation o,PlayableProfile p,AiProfile ai,AiKnowledgeState knowledge,PlayableAiAction action,Func<NavPoint,long> lastScan=null)
        {
            if(action.Kind==PlayableCommandKind.ChooseOrbital)return o.Orbital.Options.Contains(action.TargetId);
            if(!Ready(o,action.TargetId))return false;
            if(IsHack(action))return action.EntityIds.Count==1&&HackValid(o,p,action.EntityIds[0]);
            if(action.TargetId==1)return !Visible(o,action.Target)&&ScanUtility(o,p,ai,knowledge,action.Target,lastScan)>0;
            if(action.TargetId==2)return MineAllowed(o,p,ai,action.Target)&&MineWorthReplacing(o,p,ai,action.Target);
            return false;
        }
        private static IEnumerable<Opportunity> Opportunities(PlayableAiObservation o,PlayableProfile p,AiProfile ai,AiKnowledgeState knowledge,Func<NavPoint,long> lastScan)
        {
            foreach(var u in o.Entities.Where(u=>HackValid(o,p,u.Id)))
            {
                double ratio=PlayableUnitRules.HealthRatio(p,u);
                double localEnemies=o.Entities.Count(e=>o.IsHostile(e.Owner)&&e.Id!=u.Id&&D(e.Position,u.Position)<=PlayableUnitRules.Range(p,e.Kind));
                double localFriends=o.Entities.Count(e=>!o.IsHostile(e.Owner)&&D(e.Position,u.Position)<=PlayableUnitRules.Vision(p,e.Kind));
                double survival=(ratio+V(ai,"hackSurvivalFloor"))/(1+Math.Max(0,localEnemies-localFriends));
                bool urgent=o.OwnCenterDamage.Any(h=>h.AttackerId==u.Id);
                yield return new Opportunity{Ability=OrbitalAbilities.ControlHack,Entity=u.Id,Point=u.Position,Utility=PlayableUnitRules.Cost(p,u.Kind)*V(ai,"hackValueScale")*survival+(urgent?V(ai,"hackThreatBonus"):0)};
            }
            foreach(var b in o.Buildings.Where(b=>b.Kind==PlayableBuildingKind.Mine&&HackValid(o,p,b.Id)))
            {
                double hostile=o.Entities.Count(u=>o.IsHostile(u.Owner)&&D(u.Position,b.Position)<=PlayableUnitRules.Vision(p,u.Kind));
                double friendly=o.Entities.Count(u=>!o.IsHostile(u.Owner)&&D(u.Position,b.Position)<=PlayableUnitRules.Vision(p,u.Kind));
                yield return new Opportunity{Ability=OrbitalAbilities.ControlHack,Entity=b.Id,Point=b.Position,Utility=TerritoryRules.Cost(p,b.Kind)*V(ai,"hackValueScale")/(1+Math.Max(0,hostile-friendly))};
            }
            var scans=o.PublicScoutObjectives.Where(t=>t.Reachable).Select(t=>ScanCenter(o,t))
                .Concat(knowledge.Contacts.Where(c=>!c.Visible).Select(c=>c.Position))
                .Concat(o.Entities.Where(u=>u.Owner==o.Owner&&u.CurrentOrder!=null&&(u.CurrentOrder.Kind==PlayableTacticalOrderKind.AttackMove||u.CurrentOrder.Kind==PlayableTacticalOrderKind.Attack)).Select(u=>u.CurrentOrder.Destination));
            foreach(var point in scans.Distinct())
            {
                double utility=ScanUtility(o,p,ai,knowledge,point,lastScan);
                if(utility>0)yield return new Opportunity{Ability=1,Point=point,Utility=utility};
            }
            // Route vertices are public/observed paths on the correct surface. Enemy points
            // are visible ground contacts; never extrapolate a private enemy order.
            var minePoints=o.RouteProofs.SelectMany(r=>r.Path).Concat(o.PublicScoutObjectives.Where(t=>t.Reachable).Select(t=>t.Approach))
                .Concat(o.Entities.Where(u=>o.IsHostile(u.Owner)&&u.Kind!=PlayableEntityKind.Hawk&&u.Health>0).Select(u=>u.Position));
            foreach(var point in minePoints.Distinct())
                if(MineAllowed(o,p,ai,point)&&MineWorthReplacing(o,p,ai,point))
                    yield return new Opportunity{Ability=2,Point=point,Utility=MineUtility(o,p,ai,point)};
        }
        private static NavPoint ScanCenter(PlayableAiObservation o,PlayablePublicScoutObjective t)
            =>o.Intel.Areas.FirstOrDefault(a=>a.Envelope.AreaId==t.SiteId)?.Envelope.Placement.FirstOrDefault().Position??t.Approach;
        private static double ScanUtility(PlayableAiObservation o,PlayableProfile p,AiProfile ai,AiKnowledgeState knowledge,NavPoint point,Func<NavPoint,long> lastScan)
        {
            if(Visible(o,point)||Math.Abs(point.X)>p.ArenaHalfExtent||Math.Abs(point.Z)>p.ArenaHalfExtent)return 0;
            var objective=o.PublicScoutObjectives.FirstOrDefault(t=>D(ScanCenter(o,t),point)<=Data(p).orbitalScanRadius);
            long prior=lastScan?.Invoke(point)??-1;
            bool newContact=knowledge.Contacts.Any(c=>c.LastSeenTick>prior&&D(c.Position,point)<=Data(p).orbitalScanRadius);
            if(prior>=0&&o.Tick-prior<AiProfile.SecondsToTicks(ai.Value("scouting.revisitSeconds"),30)&&!newContact)return 0;
            if(objective!=null&&knowledge.Areas.Any(a=>a.AreaId==objective.SiteId&&!a.RevisitDue))return 0;
            bool engagement=o.Entities.Any(u=>u.Owner==o.Owner&&u.CurrentOrder!=null&&
                (u.CurrentOrder.Kind==PlayableTacticalOrderKind.AttackMove||u.CurrentOrder.Kind==PlayableTacticalOrderKind.Attack)&&D(u.CurrentOrder.Destination,point)<=Data(p).orbitalScanRadius);
            double confidence=knowledge.Contacts.Where(c=>D(c.Position,point)<=Data(p).orbitalScanRadius&&!c.Visible).Select(c=>c.Confidence).DefaultIfEmpty(objective!=null?1:0).Max();
            bool baseSearch=objective?.Role==PlayablePublicScoutObjectiveRole.PossibleEnemyStart&&!knowledge.Contacts.Any(c=>c.Building&&c.Kind==(int)PlayableBuildingKind.Headquarters);
            return V(ai,"scanUtility")*confidence+(engagement?V(ai,"engagementBonus"):0)+(baseSearch?V(ai,"baseSearchBonus"):0);
        }
        private static bool MineAllowed(PlayableAiObservation o,PlayableProfile p,AiProfile ai,NavPoint point)
        {
            var d=Data(p);double blast=d.orbitalMineBlastRadius+V(ai,"mineSafetyMargin");
            if(!Visible(o,point)||Math.Abs(point.X)>p.ArenaHalfExtent||Math.Abs(point.Z)>p.ArenaHalfExtent||p.AuthoredMap!=null&&!p.AuthoredMap.SupportsFootprint(point,d.orbitalMineModelRadius))return false;
            if(o.Mines.Any(m=>!o.IsHostile(m.Owner)&&D(m.Point,point)<Math.Max(V(ai,"mineSpacing"),m.BlastRadius+d.orbitalMineBlastRadius)))return false;
            if(o.Entities.Any(u=>!o.IsHostile(u.Owner)&&u.Health>0&&(D(u.Position,point)<=blast+PlayableUnitRules.Radius(p,u.Kind)||u.CurrentOrder!=null&&SegmentDistance(point,u.Position,u.CurrentOrder.Destination)<=blast+PlayableUnitRules.Radius(p,u.Kind))))return false;
            if(o.Buildings.Any(b=>b.Health>0&&D(b.Position,point)<=TerritoryRules.Radius(p,b.Kind)+(!o.IsHostile(b.Owner)?blast:d.orbitalMineModelRadius)))return false;
            return true;
        }
        private static double MineUtility(PlayableAiObservation o,PlayableProfile p,AiProfile ai,NavPoint point)
        {
            double radius=V(ai,"mineGuardRadius");
            double guard=o.Buildings.Where(b=>!o.IsHostile(b.Owner)&&b.Health>0).Select(b=>Math.Max(0,1-D(b.Position,point)/radius)).DefaultIfEmpty(0).Max();
            double ground=o.Entities.Where(u=>o.IsHostile(u.Owner)&&u.Health>0&&u.Kind!=PlayableEntityKind.Hawk).Select(u=>Math.Max(0,1-D(u.Position,point)/radius)).DefaultIfEmpty(0).Max();
            double survey=o.PublicScoutObjectives.Where(t=>t.Reachable).Select(t=>Math.Max(0,1-D(t.Approach,point)/radius)).DefaultIfEmpty(0).Max();
            // A mine already supplies its own view, so evaluate coverage against other
            // sources only when judging a new point; utility of the old mine stays comparable.
            return V(ai,"mineUtility")*Math.Max(guard,survey)+V(ai,"mineContactBonus")*ground;
        }
        private static bool MineWorthReplacing(PlayableAiObservation o,PlayableProfile p,AiProfile ai,NavPoint point)
        {
            double utility=MineUtility(o,p,ai,point);if(utility<V(ai,"minimumUtility"))return false;
            var owned=o.Mines.Where(m=>m.Owner==o.Owner).OrderBy(m=>m.Id).ToArray();
            return owned.Length<Data(p).orbitalMineCapacity||utility>MineUtility(o,p,ai,owned[0].Point)+V(ai,"replacementGain");
        }
        private static double SegmentDistance(NavPoint p,NavPoint a,NavPoint b)
        {
            double x=b.X-a.X,z=b.Z-a.Z,length=x*x+z*z;
            double t=length==0?0:Math.Max(0,Math.Min(1,((p.X-a.X)*x+(p.Z-a.Z)*z)/length));
            return D(p,new NavPoint(a.X+t*x,a.Z+t*z));
        }
    }
    public sealed partial class AiDecisionArbiter
    {
        private const string OrbitalScanPrefix="orbital-scan:";
        internal void RememberOrbitalScan(NavPoint point,long tick)
        {
            string key=OrbitalScanPrefix+point.X.ToString("R",CultureInfo.InvariantCulture)+":"+point.Z.ToString("R",CultureInfo.InvariantCulture);
            history[key]=new History{FirstTick=tick,Conditions="observed"};
        }
        internal long LastOrbitalScan(NavPoint point,double radius)
        {
            long latest=-1;
            foreach(var pair in history.Where(x=>x.Key.StartsWith(OrbitalScanPrefix,StringComparison.Ordinal)))
                if(TryScanPoint(pair.Key,out var p)&&AiOpponentSelector.Distance(point,p)<=radius)latest=Math.Max(latest,pair.Value.FirstTick);
            return latest;
        }
        private static bool TryScanPoint(string key,out NavPoint point)
        {
            point=default;var parts=key.Substring(OrbitalScanPrefix.Length).Split(':');
            if(parts.Length!=2||!double.TryParse(parts[0],NumberStyles.Float,CultureInfo.InvariantCulture,out double x)||!double.TryParse(parts[1],NumberStyles.Float,CultureInfo.InvariantCulture,out double z)||double.IsNaN(x)||double.IsNaN(z)||double.IsInfinity(x)||double.IsInfinity(z))return false;
            point=new NavPoint(x,z);return true;
        }
    }

}
