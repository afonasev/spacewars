using System;
namespace Spacewars.Simulation
{
    public enum TeamSignalKind { Attack, Defend, Recon }
    // Immutable positional annotation: no hidden entity identifier or target tracking.
    public sealed class PlayableTeamSignal
    {
        public long Id{get;} public PlayableOwner Author{get;} public TeamSignalKind Kind{get;} public NavPoint Point{get;}
        public long CreatedTick{get;} public long ExpiresTick{get;}
        public PlayableTeamSignal(long id,PlayableOwner author,TeamSignalKind kind,NavPoint point,long created,long expires)
        {Id=id;Author=author;Kind=kind;Point=point;CreatedTick=created;ExpiresTick=expires;}
        public double Alpha(double tick,PlayableProfile profile)
        {
            double age=Math.Max(0,(tick-CreatedTick)/30d),remaining=(ExpiresTick-tick)/30d;
            if(remaining<=0)return 0;
            double pulse=profile.TeamSignalMinimumAlpha+(1-profile.TeamSignalMinimumAlpha)*(.5+.5*Math.Cos(age*Math.PI*2/profile.TeamSignalPulseSeconds));
            return pulse*Math.Min(1,remaining/profile.TeamSignalFadeSeconds);
        }
    }
}
