using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;
namespace Spacewars.Runtime
{
    internal sealed class AiTeamAssignment
    {
        internal int TargetId; internal bool Helping; internal long WaitUntilTick;
        internal string Reason; internal HashSet<int> DeniedSites=new HashSet<int>();
    }
    // A phase-local projection of durable owner commitments. Rebuilt before ANY
    // owner reviews; no hidden world, pooled money, human intentions or commands.
    // Persisted mission/expansion clocks are its sole TTL source, so restore,
    // pause and restart need no second mutable copy of the same commitments.
    internal sealed class AiTeamPublication
    {
        internal string Owner; internal int Team; internal long Tick;
        internal AiArmyMissionState Mission; internal AiOpponentAssessment[] Targets;
        internal int ExpansionSite; internal long ExpansionExpires;
        internal int[] RequestedSites; internal int[] ThreatTargets=Array.Empty<int>(); internal bool HomeThreatened;
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
                if(members.Length<2)continue;
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
                    var a=result[p.Owner];a.TargetId=objective.Key;a.Helping=members.Any(m=>m.Owner!=p.Owner&&m.HomeThreatened&&m.ThreatTargets.Contains(objective.Key));a.Reason=p.HomeThreatened?"own survival takes priority":a.Helping?"allied support with home reserve":"shared pressure";
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
    }
}
