using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Presentation
{
    // Ephemeral AI announcements reuse the manual signals' DTO, lifetime, cap, alpha and artwork.
    // Negative IDs separate these presentation-only marks from positive authority signal IDs.
    public sealed class NativeTeamSignals
    {
        private readonly List<PlayableTeamSignal> active=new List<PlayableTeamSignal>();
        private readonly int[] teams;
        public NativeTeamSignals(NativeLobbyConfiguration roster){var config=(roster??new NativeLobbyConfiguration()).Copy();config.InitializeParticipants();teams=config.Participants.Select(p=>p.Team).ToArray();}
        public void Publish(long id,int author,NavPoint point,TeamSignalKind kind,long tick,PlayableProfile profile)
        {
            if(active.Any(s=>s.Id==-id))return;
            Advance(tick);
            var own=active.Where(s=>(int)s.Author==author).OrderBy(s=>s.CreatedTick).ToArray();
            foreach(var old in own.Take(Math.Max(0,own.Length-(int)profile.TeamSignalMaximumPerAuthor+1)))active.Remove(old);
            active.Add(new PlayableTeamSignal(-id,(PlayableOwner)author,kind,point,tick,tick+(long)Math.Round(profile.TeamSignalLifetimeSeconds*30)));
        }
        public void Advance(long tick)=>active.RemoveAll(s=>tick>=s.ExpiresTick);
        public PlayableTeamSignal[] Visible(int team,bool spectator,long tick)
        {Advance(tick);return active.Where(s=>spectator||teams[(int)s.Author]==team).ToArray();}
    }
}
