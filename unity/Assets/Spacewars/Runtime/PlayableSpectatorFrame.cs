using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    // Dedicated viewer capability. Never attached to Latest, owner Views or AI observations.
    public sealed class PlayableSpectatorFrame
    {
        public PlayableSnapshot Overview { get; }
        private readonly long publishedAt=System.Diagnostics.Stopwatch.GetTimestamp();
        public double AgeSeconds=>Math.Max(0,(System.Diagnostics.Stopwatch.GetTimestamp()-publishedAt)/(double)System.Diagnostics.Stopwatch.Frequency);
        public IReadOnlyList<PlayableSnapshot> Players { get; }
        internal PlayableSpectatorFrame(PlayableSnapshot overview,PlayableSnapshot[] players)
        {
            if(players.Length<2||players.Length>8||players.Any(p=>p.Generation!=overview.Generation||p.Sequence!=overview.Sequence||p.Tick!=overview.Tick))throw new ArgumentException("Incoherent spectator frame.");
            Overview=overview;Players=Array.AsReadOnly((PlayableSnapshot[])players.Clone());
        }
        private PlayableSnapshot previousOverview;
        private IReadOnlyList<PlayableSnapshot> previousPlayers;
        // Retain only the preceding publication's views, never its history chain.
        internal PlayableSpectatorFrame(PlayableSpectatorFrame current,PlayableSpectatorFrame previous)
        {
            Overview=current.Overview;Players=current.Players;
            if(previous!=null&&previous.Overview.Generation==Overview.Generation&&previous.Overview.Tick==Overview.Tick-1){
                previousOverview=previous.Overview;previousPlayers=previous.Players;
            }else if(previous!=null&&previous.Overview.Generation==Overview.Generation&&previous.Overview.Tick==Overview.Tick){
                previousOverview=previous.previousOverview;previousPlayers=previous.previousPlayers;
            }
        }
        public PlayableSnapshot PreviousPerspective(PlayableOwner? owner)=>owner.HasValue?previousPlayers?.FirstOrDefault(p=>p.Owner==owner.Value):previousOverview;
        public PlayableSnapshot Perspective(PlayableOwner? owner)=>owner.HasValue?Players.Single(p=>p.Owner==owner.Value):Overview;
    }
}
