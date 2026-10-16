using System;

namespace Spacewars.Runtime
{
    // Wall-clock pacing only: authority ticks still advance exactly 1/30 simulation seconds.
    internal sealed class PlayableRuntimePacing
    {
        private readonly double tickSeconds;
        private readonly bool catchUp;
        private double next,rate=1;
        private bool paused;
        public PlayableRuntimePacing(double tickSeconds,bool catchUp){this.tickSeconds=tickSeconds;this.catchUp=catchUp;}
        public double WaitSeconds(double now,double speed,bool isPaused)
        {
            // A new spectator rate or pause state starts a fresh schedule. Old fast-rate
            // debt must never keep the game racing after selecting a slower rate/resuming.
            if(catchUp&&(speed!=rate||isPaused!=paused))next=now;
            rate=speed;paused=isPaused;
            return next-now;
        }
        public void Complete(double now)
        {
            next+=tickSeconds/rate;
            // Spectators recover time spent in long AI ticks instead of silently losing
            // the requested multiplier. Participant sessions retain their original pacing.
            // Stop/pause commands are still admitted before every fixed authority tick.
            if(!catchUp||paused)next=Math.Max(next,now);
        }
    }
}
