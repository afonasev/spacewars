using System;
using System.Linq;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    // Translates editable UI identity into the existing deterministic participant authority.
    internal static class NativeLobbyMatch
    {
        internal static OfflineMatchConfiguration Create(PlayableProfile profile, NativeLobbyConfiguration setup)
        {
            var authored=CreateAuthored(profile,setup);
            // Only ordinary lobby matches replace authored pins. Campaigns and direct
            // diagnostic configurations keep their intentional start bindings.
            var assignment=OfflineStartAssignment.ResolveLobby(authored.Starts,authored.Roster,authored.Seed);
            var starts=authored.Starts.Select((s,i)=>{
                int player=Array.IndexOf(assignment,i);
                return new OfflineStart(s.Id,s.SiteId,s.Position,s.ExplorerAnchor,s.Heading,
                    player<0?(int?)null:authored.Roster[player].LogicalPlayer);
            }).ToArray();
            var costs=new double[starts.Length,starts.Length];
            for(int a=0;a<starts.Length;a++)for(int b=0;b<starts.Length;b++)costs[a,b]=authored.RouteCost(a,b);
            return new OfflineMatchConfiguration(profile,authored.SourceIdentity,authored.MapIdentity,authored.RouteProvenance,
                authored.Seed,authored.Roster.ToArray(),starts,authored.Sites.ToArray(),authored.Obstacles.ToArray(),costs,terrain:profile.AuthoredMap.Id);
        }
        private static OfflineMatchConfiguration CreateAuthored(PlayableProfile profile, NativeLobbyConfiguration setup)
        {
            int seed=setup.ResolveSeed();
            var roster=setup.Participants.Select((p,i)=>new OfflineParticipant(i==0?"player-1":"foundry-"+(i+1),i+1,p.Team,p.Human?OfflineControl.Human:OfflineControl.Ai)).ToArray();
            if(profile.AuthoredMap is CrimsonGroveMap grove)return grove.Configuration(profile,seed,roster);
            if(profile.AuthoredMap is WhiteMeridianMap meridian)return meridian.Configuration(profile,seed,roster);
            if(profile.AuthoredMap is MossHighlandMap moss)return moss.Configuration(profile,seed,roster);
            if(profile.AuthoredMap is BasaltCrownMap basalt)return basalt.Configuration(profile,seed,roster);
            if(profile.AuthoredMap is AiTestMap testMap)return testMap.Configuration(profile,seed,roster);
            if(profile.AuthoredMap is FoundryMap foundry)
            {
                var authored=foundry.Configuration(profile,seed);
                var costs=new double[authored.Starts.Count,authored.Starts.Count];
                for(int a=0;a<authored.Starts.Count;a++)for(int b=0;b<authored.Starts.Count;b++)costs[a,b]=authored.RouteCost(a,b);
                return new OfflineMatchConfiguration(profile,authored.SourceIdentity,authored.MapIdentity,authored.RouteProvenance,seed,roster,authored.Starts.ToArray(),authored.Sites.ToArray(),authored.Obstacles.ToArray(),costs,terrain:profile.AuthoredMap.Id);
            }
            var sites=profile.AuthoredMap.Sites(profile).ToArray();
            var starts=sites.Where(s=>s.Kind==PlayableBuildingKind.Headquarters).Select((s,i)=>new OfflineStart("start-"+(i+1),s.Id,s.Position,new NavPoint(s.Position.X+(i==0?10:-10),s.Position.Z),i==0?0:Math.PI,i+1)).ToArray();
            // Starts are pinned by the authored map; cross-team placement does not use distance ranking.
            var routes=new double[starts.Length,starts.Length];
            return new OfflineMatchConfiguration(profile,"native-lobby-v1",profile.AuthoredMap.Id,"native-authored-starts-v1",seed,roster,starts,sites,profile.AuthoredMap.MovementBlockers.ToArray(),routes,terrain:profile.AuthoredMap.Id);
        }
    }
}
