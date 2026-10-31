using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Spacewars.Simulation;
namespace Spacewars.Runtime
{
    internal sealed partial class PlayableDomain
    {
        private long nextTeamSignalId=1;
        private readonly List<PlayableTeamSignal> teamSignals=new List<PlayableTeamSignal>();
        private PlayableCommandStatus SendTeamSignal(PlayableCommand command,PlayableOwner author)
        {
            var p=command.Target;
            if(double.IsNaN(p.X)||double.IsNaN(p.Z)||double.IsInfinity(p.X)||double.IsInfinity(p.Z)||Math.Abs(p.X)>profile.ArenaHalfExtent||Math.Abs(p.Z)>profile.ArenaHalfExtent)return PlayableCommandStatus.InvalidTarget;
            var kind=command.Kind==PlayableCommandKind.SignalAttack?TeamSignalKind.Attack:command.Kind==PlayableCommandKind.SignalDefend?TeamSignalKind.Defend:TeamSignalKind.Recon;
            teamSignals.RemoveAll(s=>s.ExpiresTick<=Tick||eliminated.Contains(s.Author));
            var prior=teamSignals.FirstOrDefault(s=>s.Author==author&&s.Kind==kind&&Math.Pow(s.Point.X-p.X,2)+Math.Pow(s.Point.Z-p.Z,2)<=Math.Pow(profile.TeamSignalRepeatRadius,2));
            if(prior!=null)teamSignals.Remove(prior);
            else while(teamSignals.Count(s=>s.Author==author)>=(int)profile.TeamSignalMaximumPerAuthor){var oldest=teamSignals.Where(s=>s.Author==author).OrderBy(s=>s.CreatedTick).ThenBy(s=>s.Id).First();teamSignals.Remove(oldest);}
            teamSignals.Add(new PlayableTeamSignal(prior?.Id??nextTeamSignalId++,author,kind,p,Tick,Tick+(long)Math.Round(profile.TeamSignalLifetimeSeconds*30)));
            return PlayableCommandStatus.Applied;
        }
        internal PlayableTeamSignal[] ProjectTeamSignals(PlayableOwner owner)=>eliminated.Contains(owner)?Array.Empty<PlayableTeamSignal>():teamSignals.Where(s=>s.ExpiresTick>Tick&&!eliminated.Contains(s.Author)&&!Hostile(owner,s.Author)).ToArray();
        private void WriteTeamSignals(BinaryWriter w)
        {
            w.Write(0x53494731);w.Write(nextTeamSignalId);w.Write(teamSignals.Count);
            foreach(var s in teamSignals){w.Write(s.Id);w.Write((int)s.Author);w.Write((int)s.Kind);w.Write(s.Point.X);w.Write(s.Point.Z);w.Write(s.CreatedTick);w.Write(s.ExpiresTick);}
        }
        private void ReadTeamSignals(BinaryReader r)
        {
            // Optional extension retains old current-envelope fixtures without inventing signals.
            if(r.BaseStream.Position==r.BaseStream.Length)return;
            if(r.ReadInt32()!=0x53494731)throw new ArgumentException("Invalid team signal extension.");
            nextTeamSignalId=r.ReadInt64();int count=r.ReadInt32();if(nextTeamSignalId<1||count<0||count>64)throw new ArgumentException("Invalid team signal count.");
            for(int i=0;i<count;i++){
                long id=r.ReadInt64();var owner=(PlayableOwner)r.ReadInt32();var kind=(TeamSignalKind)r.ReadInt32();var p=new NavPoint(r.ReadDouble(),r.ReadDouble());long created=r.ReadInt64(),expires=r.ReadInt64();
                if(id<1||id>=nextTeamSignalId||teamSignals.Any(s=>s.Id==id)||!HasOwner(owner)||!Enum.IsDefined(typeof(TeamSignalKind),kind)||double.IsNaN(p.X)||double.IsNaN(p.Z)||double.IsInfinity(p.X)||double.IsInfinity(p.Z)||Math.Abs(p.X)>profile.ArenaHalfExtent||Math.Abs(p.Z)>profile.ArenaHalfExtent||created<0||created>Tick||expires<=created)throw new ArgumentException("Invalid team signal record.");
                teamSignals.Add(new PlayableTeamSignal(id,owner,kind,p,created,expires));
            }
        }
    }
}
