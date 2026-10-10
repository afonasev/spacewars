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
        public IReadOnlyList<PlayableSnapshot> Players { get; }
        internal PlayableSpectatorFrame(PlayableSnapshot overview,PlayableSnapshot[] players)
        {
            if(players.Length<2||players.Length>8||players.Any(p=>p.Generation!=overview.Generation||p.Sequence!=overview.Sequence||p.Tick!=overview.Tick))throw new ArgumentException("Incoherent spectator frame.");
            Overview=overview;Players=Array.AsReadOnly((PlayableSnapshot[])players.Clone());
        }
        public PlayableSnapshot Perspective(PlayableOwner? owner)=>owner.HasValue?Players.Single(p=>p.Owner==owner.Value):Overview;
    }
}
