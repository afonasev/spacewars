using System;

namespace Spacewars.Presentation
{
    [Serializable]
    public sealed class AdaptiveMusicProfile
    {
        public string id="spacewars-music-v1";
        public double fadeSeconds=3,attackSeconds=2,releaseSeconds=12,combatHoldSeconds=6;
        public double silenceSeconds=3,calmBedGain=.55,battleBedGain=.85,battlePulseGain=.85;
        public double minimumThemeSeconds=120;
        public int fullIntensityEvents=6;
        public void Validate()
        {
            if(id!="spacewars-music-v1"||!Range(fadeSeconds,.1,8)||!Range(attackSeconds,.1,10)||
                !Range(releaseSeconds,1,30)||!Range(combatHoldSeconds,0,20)||!Range(silenceSeconds,0,8)||
                !Range(calmBedGain,0,1)||!Range(battleBedGain,0,1)||!Range(battlePulseGain,0,1)||
                !Range(minimumThemeSeconds,10,600)||fullIntensityEvents<1||fullIntensityEvents>64)
                throw new ArgumentException("Invalid adaptive music profile.");
        }
        private static bool Range(double value,double low,double high)=>!double.IsNaN(value)&&!double.IsInfinity(value)&&value>=low&&value<=high;
    }

    public enum MusicPhase { Playing, FadingOut, Gap, FadingIn }

    // Audio transport and simulation clocks remain independent. Advance is frozen on pause.
    public sealed class AdaptiveMusicState
    {
        public const double MaximumAudibleGapSeconds=10; // Explicit user contract.
        private readonly AdaptiveMusicProfile profile;
        private readonly int[] order;
        private int cursor;
        private double clock,phaseTime,themeTime,lastCombat=-1000,heldIntensity;
        public int Theme{get;private set;}
        public double Intensity{get;private set;}
        public double Envelope{get;private set;}
        public MusicPhase Phase{get;private set;}
        public int Serial{get;private set;}
        public double ThemeElapsed=>themeTime;
        public int NextTheme=>order[(cursor+1)%order.Length];
        public void AlignTransport(double elapsed)
        {themeTime=Math.Max(0,elapsed);phaseTime=themeTime;Envelope=Math.Min(1,phaseTime/profile.fadeSeconds);Phase=Envelope>=1?MusicPhase.Playing:MusicPhase.FadingIn;}

        public AdaptiveMusicState(AdaptiveMusicProfile profile,int themes,int seed)
        {
            profile.Validate();if(themes<2)throw new ArgumentException("At least two match themes required.");
            this.profile=profile;order=new int[themes];for(int i=0;i<themes;i++)order[i]=i;
            // Local soundtrack shuffle never uses the simulation RNG.
            var random=new Random(seed);for(int i=themes-1;i>0;i--){int other=random.Next(i+1);int t=order[i];order[i]=order[other];order[other]=t;}
            Theme=order[0];Phase=MusicPhase.FadingIn;Envelope=0;
        }

        public void Advance(double seconds,int events,bool paused,double themeLength)
        {
            if(paused||seconds<=0)return;
            if(double.IsNaN(seconds)||double.IsInfinity(seconds)||events<0)throw new ArgumentException("Invalid music observation.");
            clock+=seconds;themeTime+=seconds;phaseTime+=seconds;
            if(events>0){lastCombat=clock;heldIntensity=Math.Max(heldIntensity,Math.Min(1,.35+(double)events/profile.fullIntensityEvents));}
            double target=clock-lastCombat<=profile.combatHoldSeconds?heldIntensity:0;
            if(target==0)heldIntensity=0;
            double rate=target>Intensity?profile.attackSeconds:profile.releaseSeconds;
            Intensity=Move(Intensity,target,seconds/rate);
            if(Phase==MusicPhase.FadingOut&&target>0)
            {
                // A renewed fight cancels rotation without a jump in the current envelope.
                Phase=MusicPhase.FadingIn;phaseTime=Envelope*profile.fadeSeconds+seconds;
            }
            if(Phase==MusicPhase.Playing&&themeTime>=Math.Max(profile.minimumThemeSeconds,themeLength)&&target==0&&Intensity<=.05)
            {Phase=MusicPhase.FadingOut;phaseTime=0;}
            if(Phase==MusicPhase.FadingOut)
            {
                Envelope=Math.Max(0,1-phaseTime/profile.fadeSeconds);
                if(phaseTime>=profile.fadeSeconds){Phase=MusicPhase.Gap;phaseTime-=profile.fadeSeconds;Envelope=0;}
            }
            if(Phase==MusicPhase.Gap&&(target>0||phaseTime>=profile.silenceSeconds))
            {
                // Consume elapsed gap time, including a long frame, rather than adding another wait.
                phaseTime=0; // Fade begins at the actual transport start, not at an expired frame time.
                cursor=(cursor+1)%order.Length;Theme=order[cursor];Serial++;themeTime=phaseTime;
                Phase=MusicPhase.FadingIn;Envelope=0;
            }
            if(Phase==MusicPhase.FadingIn)
            {
                Envelope=Math.Min(1,phaseTime/profile.fadeSeconds);
                if(phaseTime>=profile.fadeSeconds){Phase=MusicPhase.Playing;Envelope=1;phaseTime=0;}
            }
            else if(Phase==MusicPhase.Playing)Envelope=1;
        }
        private static double Move(double value,double target,double amount)=>value<target?Math.Min(target,value+amount):Math.Max(target,value-amount);
    }
}
