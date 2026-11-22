using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Runtime
{
    internal sealed class AiVictoryBid
    {
        internal int Team, Target;
        internal PlayableRouteTargetKind Kind;
        internal long Tick;
        internal double Force, AntiAir, Fortifications, Eta;
        internal (int Id, double Force, bool Air)[] Contacts;
        // A dispatch uses only the current owner-local assessment. The team
        // board and save wire carry the numeric bid, not executable recipients.
        internal NavPoint Destination;
        internal int[] Recipients = Array.Empty<int>();
        internal bool RememberedCenter;
        internal bool KnownHeadquarters;
        internal double Resistance => Fortifications + Contacts.Sum(c=>c.Force);

        internal static double TankForce(PlayableProfile p, AiProfile ai) =>
            PlayableUnitRules.DamagePerSecond(p,PlayableEntityKind.Tank)*ai.Value("utility.forceHorizonSeconds");

        internal bool Superior(AiProfile ai, double minimum) =>
            Force>=minimum && Force>=Resistance*ai.Value("victoryConversion.advantageRatio") &&
            AntiAir>=Contacts.Where(c=>c.Air).Sum(c=>c.Force);

        internal static bool CoalitionSuperior(IEnumerable<AiVictoryBid> bids,AiProfile profile,double minimum)
        {
            var coalition=bids.ToArray();
            var contacts=coalition.SelectMany(b=>b.Contacts).GroupBy(c=>c.Id).ToArray();
            double force=coalition.Sum(b=>b.Force);
            double resistance=coalition.Max(b=>b.Fortifications)+contacts.Sum(c=>c.Max(row=>row.Force));
            return force>=minimum&&force>=resistance*profile.Value("victoryConversion.advantageRatio")&&
                coalition.Sum(b=>b.AntiAir)>=contacts.Sum(c=>c.Where(row=>row.Air).Select(row=>row.Force).DefaultIfEmpty(0).Max());
        }

        internal static AiVictoryBid[] Assess(PlayableAiObservation o, PlayableProfile p, AiProfile ai,
            AiKnowledgeState knowledge, PlayableEntitySnapshot[] available, int pursuing, IReadOnlyDictionary<string,long> fortifications)
        {
            if(available.Length==0)return Array.Empty<AiVictoryBid>();
            var actors=available.ToDictionary(u=>u.Id);
            var centers=o.Buildings.Where(b=>o.IsHostile(b.Owner)&&b.Health>0&&TerritoryRules.Center(b.Kind)).ToDictionary(b=>b.Id);
            var areas=o.Intel.Areas.ToDictionary(a=>a.Envelope.AreaId);
            var objectiveSites=o.PublicScoutObjectives.Where(g=>g.Reachable&&
                (g.Role==PlayablePublicScoutObjectiveRole.PossibleEnemyStart||g.Role==PlayablePublicScoutObjectiveRole.ExpansionOutpost))
                .Select(g=>g.SiteId).ToHashSet();
            var rememberedCenters=knowledge.Contacts.Where(c=>c.Building&&o.IsHostile(c.Owner)&&
                TerritoryRules.Center((PlayableBuildingKind)c.Kind)&&(pursuing<0||AiOpponentSelector.Team(o,c.Owner)==pursuing))
                .OrderByDescending(c=>c.LastSeenTick).ThenBy(c=>c.Id).ToArray();
            var rememberedSites=new Dictionary<int,AiKnownContact>();
            foreach(var siteId in objectiveSites)
            {
                if(!areas.TryGetValue(siteId,out var area))continue;
                var known=rememberedCenters.FirstOrDefault(c=>area.Envelope.Contains(c.Position));
                if(known!=null)rememberedSites.Add(siteId,known);
            }
            var contacts=knowledge.Contacts.Where(c=>!c.Building&&!c.Visible&&
                o.Tick-c.LastSeenTick<=AiProfile.SecondsToTicks(ai.Value("scouting.contactHalfLifeSeconds"),30)).ToArray();
            bool twoTeams=o.Participants.Select(x=>x.Team).Distinct().Count()<=2;
            int TeamFor(PlayableRouteProof r)
            {
                if(r.Kind==PlayableRouteTargetKind.VisibleEnemy&&centers.TryGetValue(r.TargetId,out var center))
                    return AiOpponentSelector.Team(o,center.Owner);
                if(r.Kind!=PlayableRouteTargetKind.PublicObjective||!objectiveSites.Contains(r.TargetId))return -1;
                // A visible anchor gets an entity Attack only after its own
                // admission. An occluded, observed center can qualify the public
                // approach. A possible start alone cannot start an episode.
                if(o.Sites.Any(s=>s.Site.Id==r.TargetId&&(s.Owner.HasValue&&!o.IsHostile(s.Owner.Value)||
                    o.Vision?.IsVisible(s.Site.Position)==true)))return -1;
                if(areas.TryGetValue(r.TargetId,out var area)&&(area.Full||
                    centers.Values.Any(b=>area.Envelope.Contains(b.Position))))return -1;
                if(rememberedSites.TryGetValue(r.TargetId,out var known))
                    return AiOpponentSelector.Team(o,known.Owner);
                bool recentlyEmpty=knowledge.Areas.Any(a=>a.AreaId==r.TargetId&&a.EmptyAtLastCoverage&&!a.RevisitDue);
                return twoTeams&&!recentlyEmpty?pursuing:-1;
            }

            var routes=o.RouteProofs.Where(r=>actors.ContainsKey(r.UnitId))
                .Select(r=>(Route:r,Team:TeamFor(r))).Where(x=>x.Team>=0);
            return routes.GroupBy(x=>(x.Team,x.Route.Kind,x.Route.TargetId,Destination:x.Route.Kind==PlayableRouteTargetKind.VisibleEnemy?x.Route.Target:x.Route.Goal)).Select(g=>
            {
                var legs=g.Select(x=>x.Route).GroupBy(r=>r.UnitId).Select(x=>x.First()).ToArray();
                var target=legs[0].Target;
                var enemy=o.Entities.Where(e=>o.IsHostile(e.Owner)&&e.Health>0&&AiOpponentSelector.Team(o,e.Owner)==g.Key.Team)
                    .Select(e=>(e.Id,Force:Power(e,p,ai),Air:e.Kind==PlayableEntityKind.Hawk));
                var known=contacts.Where(c=>AiOpponentSelector.Team(o,c.Owner)==g.Key.Team)
                    .Select(c=>(c.Id,Force:PlayableUnitRules.DamagePerSecond(p,(PlayableEntityKind)c.Kind)*
                        ai.Value("utility.forceHorizonSeconds")*Math.Min(1,(double)c.HealthAtLastSeen/PlayableUnitRules.Health(p,(PlayableEntityKind)c.Kind))*c.Confidence,
                        Air:c.Kind==(int)PlayableEntityKind.Hawk));
                double Arrival(PlayableRouteProof r)
                {
                    var u=actors[r.UnitId];
                    var range=r.Kind==PlayableRouteTargetKind.VisibleEnemy?PlayableUnitRules.Range(p,u.Kind,u.Upgraded):0;
                    double firingLead=Math.Max(0,range-AiOpponentSelector.Distance(r.Goal,r.Target));
                    return Math.Max(0,AiExpansionPlanner.RouteLength(r)-firingLead)/PlayableUnitRules.Speed(p,u.Kind,u.Upgraded);
                }
                // A fast forward unit does not bring home production or units
                // on long detours into its combat window. Count what can fight
                // together on this approach and use the very same legs to issue.
                double radius=ai.Value("safeReinforcements.coreRadius");
                double window=ai.Value("victoryConversion.sustainSeconds");
                var approaches=legs.Select(r=>(Unit:actors[r.UnitId],Eta:Arrival(r),Force:Power(actors[r.UnitId],p,ai))).ToArray();
                var enemyContacts=enemy.Concat(known).Where(c=>c.Force>0).GroupBy(c=>c.Id).Select(c=>c.First()).OrderBy(c=>c.Id).ToArray();
                var cohort=approaches.Where(a=>AiRosterCatalog.Initial.LineWeight(a.Unit.Kind)>0)
                    .Select(anchor=>approaches.Where(a=>Math.Abs(anchor.Eta-a.Eta)<=window&&
                        AiOpponentSelector.Distance(anchor.Unit.Position,a.Unit.Position)<=radius).ToArray())
                    .Where(group=>group.Count(a=>AiRosterCatalog.Initial.LineWeight(a.Unit.Kind)>0)>=ai.Value("armies.minimumUnits"))
                    .OrderByDescending(group=>group.Sum(a=>a.Force)).ThenBy(group=>group.Max(a=>a.Eta)).FirstOrDefault();
                if(cohort==null)return null;
                var force=cohort.Select(a=>a.Unit).ToArray();
                AiKnownContact remembered=null;
                if(g.Key.Kind==PlayableRouteTargetKind.PublicObjective&&rememberedSites.TryGetValue(g.Key.TargetId,out var knownCenter))
                    remembered=knownCenter;
                return new AiVictoryBid{Team=g.Key.Team,Target=g.Key.TargetId,Kind=g.Key.Kind,Tick=o.Tick,
                    Destination=g.Key.Destination,Recipients=force.Select(u=>u.Id).ToArray(),RememberedCenter=remembered!=null,
                    KnownHeadquarters=remembered?.Kind==(int)PlayableBuildingKind.Headquarters||
                        g.Key.Kind==PlayableRouteTargetKind.VisibleEnemy&&centers[g.Key.TargetId].Kind==PlayableBuildingKind.Headquarters,
                    Force=cohort.Sum(a=>a.Force),Eta=cohort.Max(a=>a.Eta),
                    Fortifications=AiActiveStrategyPolicy.FortificationPower(o,p,ai,force,remembered?.Id??g.Key.TargetId,
                        remembered?.Position??target,knowledge,fortifications),
                    AntiAir=cohort.Where(a=>AiRosterCatalog.Initial.Targets(a.Unit.Kind,"air")).Sum(a=>a.Force),
                    Contacts=enemyContacts};
            }).Where(b=>b!=null).GroupBy(b=>(b.Team,b.Kind,b.Target)).Select(g=>g.OrderByDescending(b=>b.Force).ThenBy(b=>b.Eta).First())
              .OrderBy(b=>b.Team).ThenBy(b=>b.Kind).ThenBy(b=>b.Target).ToArray();
        }

        private static double Power(PlayableEntitySnapshot unit,PlayableProfile p,AiProfile ai)=>
            PlayableUnitRules.DamagePerSecond(p,unit.Kind,unit.Upgraded)*ai.Value("utility.forceHorizonSeconds")*
            Math.Min(1,PlayableUnitRules.HealthRatio(p,unit));

        internal void Write(BinaryWriter w)
        {
            w.Write(Team);w.Write(Target);w.Write((int)Kind);w.Write(Tick);
            WorldWire.Number(w,Force);WorldWire.Number(w,Fortifications);WorldWire.Number(w,Eta);WorldWire.Number(w,AntiAir);
            WorldWire.Array(w,Contacts,c=>{w.Write(c.Id);WorldWire.Number(w,c.Force);w.Write(c.Air);});
        }
        internal static AiVictoryBid Read(BinaryReader r, long tick)
        {
            var b=new AiVictoryBid{Team=r.ReadInt32(),Target=r.ReadInt32(),Kind=WorldWire.EnumValue<PlayableRouteTargetKind>(r),
                Tick=r.ReadInt64(),Force=WorldWire.Number(r),Fortifications=WorldWire.Number(r),Eta=WorldWire.Number(r),
                AntiAir=WorldWire.Number(r),Contacts=WorldWire.Array(r,()=>(r.ReadInt32(),WorldWire.Number(r),WorldWire.Boolean(r)))};
            AiStateWire.Require(b.Team>=0&&b.Target>0&&b.Tick>=0&&b.Tick<=tick&&b.Force>0&&b.Fortifications>=0&&b.Eta>=0&&b.AntiAir>=0&&b.AntiAir<=b.Force&&
                (b.Kind==PlayableRouteTargetKind.VisibleEnemy||b.Kind==PlayableRouteTargetKind.PublicObjective)&&
                b.Contacts.All(c=>c.Id>0&&c.Force>0)&&b.Contacts.Select(c=>c.Id).SequenceEqual(b.Contacts.Select(c=>c.Id).Distinct().OrderBy(id=>id)),"victory bid");
            return b;
        }
    }

    // Owner-local facts plus a time-bounded projection from the team's board.
    internal sealed class AiVictoryEpisode
    {
        internal int Team=-1;
        internal long Since=-1, Roll=-1, Attempt=-1, Ready=-1, Departure=-1, Arrival=-1;
        internal int Target;
        internal PlayableRouteTargetKind Kind;
        internal AiVictoryBid[] Bids=Array.Empty<AiVictoryBid>();
        internal bool Pursuing=>Departure>=0;

        internal bool WaitingToDepart(AiVictoryBid bid,long tick)=>
            Team>=0&&(!Pursuing||bid!=null&&tick<Departure);

        internal AiVictoryBid Review(PlayableAiObservation o, PlayableProfile p, AiProfile ai, AiPersonality personality,
            AiVictoryBid[] bids, AiTeamAssignment assignment, bool hasAlliedBot, bool eligible)
        {
            Bids=bids;
            double minimum=AiVictoryBid.TankForce(p,ai)*ai.Value("victoryConversion.minimumTankEquivalent");
            var shared=assignment?.Victory;
            bool Valid(AiVictoryBid b)=>b.Superior(ai,minimum)||shared!=null&&shared.Team==b.Team&&
                shared.Target==b.Target&&shared.Kind==b.Kind&&
                AiVictoryBid.CoalitionSuperior(shared.Partners.Append(b),ai,minimum);
            var bid=bids.Where(b=>(Team<0||b.Team==Team)&&(Ready>=0||b.Kind==PlayableRouteTargetKind.VisibleEnemy||b.RememberedCenter)&&Valid(b))
                .OrderByDescending(b=>b.KnownHeadquarters)
                .ThenByDescending(b=>Departure>=0&&b.Target==Target&&b.Kind==Kind&&Kind==PlayableRouteTargetKind.VisibleEnemy)
                .ThenByDescending(b=>b.Kind==PlayableRouteTargetKind.VisibleEnemy)
                .ThenByDescending(b=>b.RememberedCenter)
                .ThenByDescending(b=>shared!=null&&shared.Team==b.Team&&shared.Target==b.Target&&shared.Kind==b.Kind)
                .ThenByDescending(b=>b.Target==Target&&b.Kind==Kind)
                .ThenBy(b=>b.Eta).ThenBy(b=>b.Target).FirstOrDefault();
            if(!eligible||bid==null)
            {
                // Once committed, keep looking for centers during defense, recovery
                // and fog. Every new attack still needs a currently admissible bid.
                // Other opponents can qualify if the old team offers no viable lead.
                if(!Pursuing||eligible&&bids.Any(b=>b.Team!=Team&&(b.Kind==PlayableRouteTargetKind.VisibleEnemy||b.RememberedCenter)&&Valid(b)))
                    Reset();
                if(!eligible)Bids=Array.Empty<AiVictoryBid>();
                return null;
            }
            if(Team<0){Team=bid.Team;Since=o.Tick;}
            if(o.Tick-Since<Ticks(ai,"sustainSeconds"))return null;
            if(Roll<0)
            {
                var draw=assignment?.VictoryDraws!=null&&assignment.VictoryDraws.TryGetValue(Team,out var published)
                    ?published:(Since:Since,Roll:-1L);
                string identity=hasAlliedBot?"team-"+o.Team:o.OwnerId;
                Roll=draw.Roll>=0?draw.Roll:AiRandom.Key(o.Seed,identity,ai.Hash,"victory-conversion-"+Team,draw.Since);
                bool early=Roll/4294967296d<ai.Value("victoryConversion.early"+personality);
                Attempt=early?o.Tick:Since+Ticks(ai,"sustainSeconds")+Ticks(ai,"accumulationSeconds");
            }
            if(Ready<0)
            {
                if(o.Tick<Attempt)return null;
                Ready=o.Tick;
            }
            if(Target!=bid.Target||Kind!=bid.Kind){Target=bid.Target;Kind=bid.Kind;Departure=Arrival=-1;}
            if(Departure<0)
            {
                long deadline=Ready+AiProfile.SecondsToTicks(ai.Value("team.maximumAssemblyWaitSeconds"),30);
                if(shared!=null&&shared.Target==Target&&shared.Kind==Kind&&shared.Arrival>=0)
                {
                    Arrival=shared.Arrival;
                    Departure=Math.Min(deadline,Math.Max(o.Tick,Arrival-AiProfile.SecondsToTicks(bid.Eta,30)));
                }
                else if(!hasAlliedBot||o.Tick>=deadline)Departure=o.Tick;
            }
            return bid;
        }

        private static long Ticks(AiProfile ai,string key)=>AiProfile.SecondsToTicks(ai.Value("victoryConversion."+key),30);
        private void Reset()
        {Team=-1;Target=0;Kind=default;Since=Roll=Attempt=Ready=Departure=Arrival=-1;}

        internal void Write(BinaryWriter w)
        {
            w.Write(Team);w.Write(Target);w.Write((int)Kind);w.Write(Since);w.Write(Roll);w.Write(Attempt);w.Write(Ready);w.Write(Departure);w.Write(Arrival);
            WorldWire.Array(w,Bids,b=>b.Write(w));
        }
        internal void Read(BinaryReader r,long tick)
        {
            Team=r.ReadInt32();Target=r.ReadInt32();Kind=WorldWire.EnumValue<PlayableRouteTargetKind>(r);
            Since=r.ReadInt64();Roll=r.ReadInt64();Attempt=r.ReadInt64();Ready=r.ReadInt64();Departure=r.ReadInt64();Arrival=r.ReadInt64();
            Bids=WorldWire.Array(r,()=>AiVictoryBid.Read(r,tick));
            AiStateWire.Require(Team>=-1&&Since>=-1&&Since<=tick&&(Team>=0)==(Since>=0)&&Roll>=-1&&Roll<=uint.MaxValue&&
                Attempt>=-1&&(Attempt>=0)==(Roll>=0)&&(Attempt<0||Attempt>=Since)&&
                Ready>=-1&&Ready<=tick&&(Ready<0||Roll>=0&&Ready>=Attempt)&&Target>=0&&(Target>0)==(Ready>=0)&&
                Departure>=-1&&(Departure<0||Departure>=Ready&&Ready>=0)&&Arrival>=-1&&(Arrival<0||Departure>=0)&&
                Bids.Select(b=>(b.Team,b.Kind,b.Target)).Distinct().Count()==Bids.Length,"victory episode");
        }
    }
}
