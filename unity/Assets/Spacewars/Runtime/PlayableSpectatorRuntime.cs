using System;
using System.Linq;
using System.Threading;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    public sealed partial class PlayableRuntime
    {
        public const string LocalSpectatorId="local-spectator";
        private readonly bool localSpectator;
        private double spectatorSpeed=1;
        public double TargetTicksPerSecond=>Volatile.Read(ref spectatorSpeed)/TickSeconds;
        private bool AuthorizesSpectator(string id)=>id!=null&&(localSpectator?id==LocalSpectatorId:offlineConfiguration?.Spectators.Contains(id)==true);
        public PlayableSpectatorFrame SpectatorFrame(string id)
        {
            if(!AuthorizesSpectator(id))throw new ArgumentException("Not a registered spectator.",nameof(id));
            return Volatile.Read(ref publishedViews)?.Spectator;
        }
        public void SetSpectatorSpeed(string id,double speed)
        {
            if(!AuthorizesSpectator(id))throw new ArgumentException("Not a registered spectator.",nameof(id));
            if(speed!=.5&&speed!=1&&speed!=2&&speed!=4)throw new ArgumentOutOfRangeException(nameof(speed));
            if(Volatile.Read(ref spectatorSpeed)!=speed){Volatile.Write(ref spectatorSpeed,speed);PresentationChanged();Signal();}
        }
    }
}
