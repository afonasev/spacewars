using System;
using System.Collections.Generic;
using Spacewars.Simulation;
namespace Spacewars.Presentation
{
    // Anonymous immutable values, never actor identity, collection order or frame time.
    public static class GroundTrailDensity
    {
        public static int Divisor(PlayableSnapshot view,ModelDetailProfile profile)
        {
            int count=VisibleMovingGroundCount(view);
            return count>=profile.trailLargeCrowdThreshold?(int)profile.trailLargeCrowdDivisor:count>=profile.trailCrowdThreshold?(int)profile.trailCrowdDivisor:1;
        }
        internal static ulong SourceSeed(PlayableGroundTrail sample)
        {
            ulong hash=0x9e3779b97f4a7c15UL;
            hash=Mix(hash,(ulong)BitConverter.DoubleToInt64Bits(sample.From.X));hash=Mix(hash,(ulong)BitConverter.DoubleToInt64Bits(sample.From.Z));
            hash=Mix(hash,(ulong)BitConverter.DoubleToInt64Bits(sample.To.X));hash=Mix(hash,(ulong)BitConverter.DoubleToInt64Bits(sample.To.Z));
            hash=Mix(hash,(ulong)BitConverter.DoubleToInt64Bits(sample.Heading));hash=Mix(hash,(ulong)BitConverter.DoubleToInt64Bits(sample.Distance));
            hash=Mix(hash,(ulong)sample.Tick);hash=Mix(hash,(ulong)sample.Kind);
            return hash;
        }
        static ulong Mix(ulong hash,ulong value)
        {
            // Fixed integer dispersion constants are a technical invariant, not art tuning.
            unchecked{hash+=value+0x9e3779b97f4a7c15UL;hash=(hash^(hash>>30))*0xbf58476d1ce4e5b9UL;hash=(hash^(hash>>27))*0x94d049bb133111ebUL;return hash^(hash>>31);}
        }
        public static int VisibleMovingGroundCount(PlayableSnapshot view)
        {
            int count=0;
            for(int i=0;i<view.Entities.Count;i++){
                var actor=view.Entities[i];
                if(actor.Moving&&actor.Kind!=PlayableEntityKind.Hawk&&(view.Vision==null||view.Vision.IsVisible(actor.Position)))count++;
            }
            return count;
        }
    }
    // Choose vehicles from visible anonymous data, then keep the choice for every
    // new impression from that vehicle. Historical decisions survive count/stop
    // and fog changes; omitted history never reappears as a cold mesh rebuild.
    public sealed class GroundTrailVehicleSelection
    {
        sealed class Vehicle{internal ulong Seed;internal long Tick;}
        readonly Dictionary<PlayableGroundTrail,Vehicle> vehicles=new Dictionary<PlayableGroundTrail,Vehicle>(GroundTrailJournal.BySource);
        readonly Dictionary<PlayableGroundTrail,bool> choices=new Dictionary<PlayableGroundTrail,bool>();
        readonly List<PlayableGroundTrail> retired=new List<PlayableGroundTrail>();
        long generation=-1,expiry=long.MaxValue;int lifetime=-1;
        public void Clear(){vehicles.Clear();choices.Clear();retired.Clear();generation=-1;expiry=long.MaxValue;lifetime=-1;}
        public void Begin(long gen,long tick,int ticks)
        {
            if(generation!=gen){Clear();generation=gen;}
            if(lifetime==ticks&&tick<expiry)return;lifetime=ticks;expiry=long.MaxValue;
            foreach(var row in choices){if(tick-row.Key.Tick>=ticks)retired.Add(row.Key);else expiry=Math.Min(expiry,row.Key.Tick+ticks);}
            foreach(var key in retired)choices.Remove(key);retired.Clear();
            foreach(var row in vehicles)if(tick-row.Value.Tick>=ticks)retired.Add(row.Key);
            foreach(var key in retired)vehicles.Remove(key);retired.Clear();
        }
        public bool Keep(PlayableGroundTrail sample,int divisor)
        {
            if(!GroundTrailJournal.HasSource(sample))return true;
            if(choices.TryGetValue(sample,out bool found))return found;
            if(choices.Count>=GroundTrailJournal.Capacity)return true; // Same defensive ceiling as the journal; preserve geometry on overflow.
            if(!vehicles.TryGetValue(sample,out var vehicle)){
                vehicle=new Vehicle{Seed=GroundTrailDensity.SourceSeed(sample),Tick=sample.Tick};vehicles.Add(sample,vehicle);
            }
            vehicle.Tick=Math.Max(vehicle.Tick,sample.Tick);
            bool keep=divisor<=1||vehicle.Seed%(uint)divisor==0;choices.Add(sample,keep);expiry=Math.Min(expiry,sample.Tick+lifetime);return keep;
        }
        public int SampleCount=>choices.Count;
    }
}
