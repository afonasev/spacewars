using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;
namespace Spacewars.Runtime
{
    internal sealed class AiTeamAssignment
    {
        internal int TargetId,SecondaryTargetId; internal bool Helping; internal long WaitUntilTick;
        internal int RelaySite; internal long RelayDeparture;
        internal HashSet<string> GreedyAllies=new HashSet<string>(StringComparer.Ordinal);
        internal AiVictoryCoordination Victory;
        internal IReadOnlyDictionary<int,(long Since,long Roll)> VictoryDraws;
        internal string Reason; internal HashSet<int> DeniedSites=new HashSet<int>();
    }
    internal sealed class AiVictoryCoordination
    {
        internal int Team, Target;
        internal PlayableRouteTargetKind Kind;
        internal long Arrival;
        internal AiVictoryBid[] Partners;
    }
    // A phase-local projection of durable owner commitments. Rebuilt before ANY
    // owner reviews; no hidden world, pooled money, human intentions or commands.
    // Persisted owner clocks bound team commitments, so restore,
    // pause and restart need no second mutable copy of the same commitments.
    internal sealed class AiTeamPublicationObservation
    {
        // Closed private input for non-selected owner publications only. The
        // original defense policy still runs on that owner's worker/profile.
        internal readonly PlayableSnapshot DefenseSnapshot;
        internal AiTeamPublicationObservation(PlayableSnapshot snapshot){DefenseSnapshot=snapshot;}
    }
    internal sealed class AiTeamPublication
    {
        internal string Owner; internal PlayableAiOpening Opening; internal int Team; internal long Tick;
        internal AiArmyMissionState Mission; internal AiOpponentAssessment[] Targets;
        internal int ExpansionSite; internal long ExpansionExpires;
        internal AiVictoryEpisode Victory; internal double MinimumVictoryForce;
        internal int[] RequestedSites; internal int[] ThreatTargets=Array.Empty<int>(); internal bool HomeThreatened;
        internal AiRelayBid[] RelayBids=Array.Empty<AiRelayBid>();
        internal double RelaySeconds;
    }
    internal static class AiTeamCoordinator
    {
        internal static Dictionary<string,AiTeamAssignment> Resolve(IEnumerable<AiTeamPublication> input,AiProfile profile)
        {
            var all=input.OrderBy(p=>p.Owner,StringComparer.Ordinal).ToArray();
            var result=all.ToDictionary(p=>p.Owner,p=>new AiTeamAssignment{Reason="independent pressure"},StringComparer.Ordinal);
            foreach(var team in all.GroupBy(p=>p.Team))
            {
                var members=team.ToArray();
                foreach(var p in members)result[p.Owner].GreedyAllies=new HashSet<string>(members.Where(m=>m.Owner!=p.Owner&&m.Opening==PlayableAiOpening.GreedySafe).Select(m=>m.Owner),StringComparer.Ordinal);
                if(members.Length<2)continue;
                ResolveVictory(members,result,profile);
                ResolveRelays(members,result,profile);
                var claims=new Dictionary<int,string>();
                foreach(var p in members.Where(p=>p.ExpansionSite>0&&p.ExpansionExpires>p.Tick).OrderBy(p=>p.Owner,StringComparer.Ordinal))
                    if(!claims.ContainsKey(p.ExpansionSite))claims.Add(p.ExpansionSite,p.Owner);
                var assigned=new HashSet<string>(claims.Values,StringComparer.Ordinal);
                // One new objective per owner, retaining alternative sites for teammates.
                var bids=members.Where(p=>!assigned.Contains(p.Owner)).SelectMany(p=>p.RequestedSites.Select((site,rank)=>new{p.Owner,Site=site,Rank=rank}))
                    .OrderBy(b=>b.Rank).ThenBy(b=>b.Owner,StringComparer.Ordinal).ThenBy(b=>b.Site);
                foreach(var bid in bids)if(!assigned.Contains(bid.Owner)&&!claims.ContainsKey(bid.Site)){claims.Add(bid.Site,bid.Owner);assigned.Add(bid.Owner);}
                foreach(var claim in claims)foreach(var p in members.Where(p=>p.Owner!=claim.Value))result[p.Owner].DeniedSites.Add(claim.Key);
                // Prefer an already published objective with actual progress; otherwise
                // choose a commonly reachable target from independent owner assessments.
                var helpTargets=new HashSet<int>(members.Where(p=>p.HomeThreatened).SelectMany(p=>p.ThreatTargets));
                foreach(var p in members.Where(p=>members.Any(m=>m.Owner!=p.Owner&&m.HomeThreatened)))result[p.Owner].Reason="allied threat unreachable; retain useful owner task";
                var objectives=members.SelectMany(p=>p.Targets).GroupBy(a=>a.TargetId)
                    .Where(g=>members.Count(p=>p.Targets.Any(t=>t.TargetId==g.Key))>1)
                    .OrderByDescending(g=>helpTargets.Contains(g.Key))
                    .ThenByDescending(g=>members.Any(p=>p.Mission.ArmyId>0&&p.Mission.TargetKind==PlayableRouteTargetKind.VisibleEnemy&&p.Mission.TargetId==g.Key&&p.Tick-p.Mission.ProgressTick<AiProfile.SecondsToTicks(profile.Value("team.claimTtlSeconds"),30)))
                    .ThenByDescending(g=>g.Count()).ThenByDescending(g=>g.Sum(t=>t.Score)).ThenBy(g=>g.Key).ToArray();
                var objective=objectives.FirstOrDefault();if(objective==null)continue;
                var leader=members.Where(p=>p.Mission.ArmyId>0&&p.Mission.TargetId==objective.Key&&p.Mission.TargetKind==PlayableRouteTargetKind.VisibleEnemy&&p.Tick-p.Mission.ProgressTick<AiProfile.SecondsToTicks(profile.Value("team.claimTtlSeconds"),30)).OrderBy(p=>p.Mission.StartedTick).ThenBy(p=>p.Owner,StringComparer.Ordinal).FirstOrDefault();
                foreach(var p in members.Where(p=>p.Targets.Any(t=>t.TargetId==objective.Key)))
                {
                    var a=result[p.Owner];a.TargetId=objective.Key;a.SecondaryTargetId=p.Targets.Where(t=>t.TargetId!=objective.Key&&helpTargets.Contains(t.TargetId)).OrderByDescending(t=>t.Score).Select(t=>t.TargetId).FirstOrDefault();a.Helping=members.Any(m=>m.Owner!=p.Owner&&m.HomeThreatened&&m.ThreatTargets.Contains(objective.Key));a.Reason=p.HomeThreatened?"own survival takes priority":a.Helping?"allied support with home reserve":"shared pressure";
                    if(leader!=null&&leader.Owner!=p.Owner&&p.Mission.ArmyId==0)
                    {
                        double eta=leader.Targets.FirstOrDefault(t=>t.TargetId==objective.Key)?.ArrivalSeconds??0;
                        double own=p.Targets.Single(t=>t.TargetId==objective.Key).ArrivalSeconds;
                        long deadline=leader.Mission.StartedTick+AiProfile.SecondsToTicks(profile.Value("team.maximumAssemblyWaitSeconds"),30);
                        a.WaitUntilTick=Math.Min(deadline,p.Tick+AiProfile.SecondsToTicks(Math.Max(0,eta-own),30));
                        if(p.Tick>=deadline){a.WaitUntilTick=0;a.Reason="assembly deadline; useful pressure fallback";}
                    }
                }
            }
            return result;
        }

        private static void ResolveRelays(AiTeamPublication[] members,Dictionary<string,AiTeamAssignment> result,AiProfile profile)
        {
            long ttl=AiProfile.SecondsToTicks(profile.Value("team.claimTtlSeconds"),30);
            var bids=members.Where(p=>!p.HomeThreatened&&p.RelaySeconds>0).SelectMany(p=>p.RelayBids
                .Where(b=>p.Tick-b.Tick<=ttl).Select(b=>(Member:p,Bid:b))).ToArray();
            var choice=bids.GroupBy(x=>x.Bid.Site).Where(g=>g.Select(x=>x.Member.Owner).Distinct().Count()>1)
                .OrderByDescending(g=>g.Sum(x=>x.Bid.Force)*profile.Value("activeStrategy.maximumEncounterRatio")>=g.Max(x=>x.Bid.Resistance))
                .ThenByDescending(g=>g.All(x=>x.Bid.Arrival+x.Bid.Clearance+x.Bid.Capture<=x.Member.RelaySeconds))
                .ThenByDescending(g=>g.Any(x=>x.Bid.Breaks)).ThenByDescending(g=>g.Count())
                .ThenBy(g=>g.Max(x=>x.Bid.Arrival+x.Bid.Clearance+x.Bid.Capture)).ThenBy(g=>g.Key).FirstOrDefault();
            if(choice==null)return;
            long arrival=choice.Max(x=>x.Bid.Tick+AiProfile.SecondsToTicks(x.Bid.Arrival,30));
            long wait=AiProfile.SecondsToTicks(profile.Value("team.maximumAssemblyWaitSeconds"),30);
            foreach(var x in choice)
            {
                var assignment=result[x.Member.Owner];assignment.RelaySite=choice.Key;
                long deadline=x.Member.Tick+AiProfile.SecondsToTicks(Math.Max(0,x.Member.RelaySeconds-x.Bid.Arrival-x.Bid.Clearance-x.Bid.Capture),30);
                assignment.RelayDeparture=Math.Min(x.Bid.Started+wait,Math.Min(deadline,
                    arrival-AiProfile.SecondsToTicks(x.Bid.Arrival,30)));
            }
        }

        private static void ResolveVictory(AiTeamPublication[] members, Dictionary<string,AiTeamAssignment> result,AiProfile profile)
        {
            var safe=members.Where(p=>!p.HomeThreatened&&p.Victory!=null).ToArray();
            var draws=safe.Where(p=>p.Victory.Team>=0).GroupBy(p=>p.Victory.Team).ToDictionary(g=>g.Key,g=>{
                var episodes=g.Select(p=>p.Victory).OrderBy(e=>e.Since).ToArray();
                return (episodes[0].Since,Roll:episodes.Where(e=>e.Roll>=0).Select(e=>e.Roll).DefaultIfEmpty(-1).First());
            });
            foreach(var member in members)result[member.Owner].VictoryDraws=draws;

            var ttl=AiProfile.SecondsToTicks(profile.Value("team.claimTtlSeconds"),30);
            var bids=safe.SelectMany(p=>p.Victory.Bids.Where(b=>p.Tick-b.Tick<=ttl&&
                (p.Victory.Team<0||p.Victory.Team==b.Team)).Select(b=>(Owner:p,Bid:b))).ToArray();
            var choice=bids.GroupBy(x=>(x.Bid.Team,x.Bid.Kind,x.Bid.Target))
                .Where(g=>g.Count()>1&&AiVictoryBid.CoalitionSuperior(g.Select(x=>x.Bid),profile,g.Max(x=>x.Owner.MinimumVictoryForce)))
                .OrderByDescending(g=>g.Count(x=>x.Owner.Victory.Target==g.Key.Target&&x.Owner.Victory.Kind==g.Key.Kind))
                .ThenByDescending(g=>g.Key.Kind==PlayableRouteTargetKind.VisibleEnemy)
                .ThenByDescending(g=>g.Count()).ThenBy(g=>g.Max(x=>x.Bid.Eta)).ThenBy(g=>g.Key.Target)
                .Select(g=>g.ToArray()).FirstOrDefault();
            bool sharedTarget=choice!=null;
            if(!sharedTarget)
            {
                // Distinct directions count only when each owner can actually
                // support its own attack. Never credit a distant partner's guns here.
                choice=bids.Where(x=>x.Bid.Superior(profile,x.Owner.MinimumVictoryForce))
                    .GroupBy(x=>x.Bid.Team).Select(g=>g.GroupBy(x=>x.Owner.Owner).Select(front=>front
                        .OrderByDescending(x=>x.Owner.Victory.Target==x.Bid.Target&&x.Owner.Victory.Kind==x.Bid.Kind)
                        .ThenByDescending(x=>x.Bid.Kind==PlayableRouteTargetKind.VisibleEnemy)
                        .ThenBy(x=>x.Bid.Eta).ThenBy(x=>x.Bid.Target).First()).ToArray())
                    .Where(g=>g.Length>1).OrderByDescending(g=>g.Length).ThenBy(g=>g.Max(x=>x.Bid.Eta)).FirstOrDefault();
            }
            if(choice==null)return;
            long arrival=-1;
            var ready=choice.Where(x=>x.Owner.Victory.Ready>=0&&x.Owner.Victory.Target==x.Bid.Target&&x.Owner.Victory.Kind==x.Bid.Kind).ToArray();
            if(ready.Length>1)
            {
                arrival=ready.Select(x=>x.Owner.Victory.Arrival).Where(t=>t>=0).DefaultIfEmpty(-1).Min();
                if(arrival<0)arrival=ready.Max(x=>x.Owner.Tick+AiProfile.SecondsToTicks(x.Bid.Eta,30));
            }
            foreach(var x in choice)result[x.Owner.Owner].Victory=new AiVictoryCoordination{
                Team=x.Bid.Team,Target=x.Bid.Target,Kind=x.Bid.Kind,Arrival=arrival,
                Partners=sharedTarget?choice.Where(other=>other.Owner!=x.Owner).Select(other=>other.Bid).ToArray():Array.Empty<AiVictoryBid>()};
        }
    }
}
