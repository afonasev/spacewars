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
        private PlayableSpectatorFrame spectatorFrame;
        private double spectatorSpeed=1;
        private bool AuthorizesSpectator(string id)=>id!=null&&(localSpectator?id==LocalSpectatorId:offlineConfiguration?.Spectators.Contains(id)==true);
        public PlayableSpectatorFrame SpectatorFrame(string id)
        {
            if(!AuthorizesSpectator(id))throw new ArgumentException("Not a registered spectator.",nameof(id));
            return Volatile.Read(ref spectatorFrame);
        }
        public void SetSpectatorSpeed(string id,double speed)
        {
            if(!AuthorizesSpectator(id))throw new ArgumentException("Not a registered spectator.",nameof(id));
            if(speed!=.5&&speed!=1&&speed!=2&&speed!=4)throw new ArgumentOutOfRangeException(nameof(speed));
            if(Volatile.Read(ref spectatorSpeed)!=speed){Volatile.Write(ref spectatorSpeed,speed);PresentationChanged();Signal();}
        }
        private void PublishSpectatorFrame(RuntimeStatus status,string failure,bool? framePaused=null)
        {
            if(!localSpectator&&(offlineConfiguration==null||offlineConfiguration.Spectators.Count==0))return;
            Volatile.Write(ref spectatorFrame,domain.SpectatorSnapshot(snapshotSequence,status,framePaused??(Volatile.Read(ref paused)!=0),Metrics(lastCpu,0),failure,seed));
        }
    }
}
