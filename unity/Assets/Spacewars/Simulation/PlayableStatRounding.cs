using System;
namespace Spacewars.Simulation
{
    // combat-stat-rounding-v1: apply once after all upgrade multipliers.
    public static class PlayableStatRounding
    {
        // Derived arithmetic only: bounded, thread-local and absent from saves.
        // Repeated entity projections use the same authored HP/damage values.
        private struct Entry { internal long Value,Step;internal double Result;internal bool Valid; }
        [ThreadStatic] private static Entry[] recent;
        private static double Nearest(double value,double step)
        {
            long valueBits=BitConverter.DoubleToInt64Bits(value),stepBits=BitConverter.DoubleToInt64Bits(step);
            var entries=recent??(recent=new Entry[64]);
            uint hash=unchecked((uint)(valueBits^(valueBits>>32)^stepBits^(stepBits>>32))*2654435761u);
            int slot=(int)(hash>>26);
            ref var entry=ref entries[slot];
            if(entry.Valid&&entry.Value==valueBits&&entry.Step==stepBits)return entry.Result;
            // Decimal conversion removes binary noise around human-authored decimal ties.
            // Misses and invalid inputs retain the original arithmetic and exceptions.
            double result=(double)(Math.Round((decimal)value/(decimal)step,0,MidpointRounding.AwayFromZero)*(decimal)step);
            entry=new Entry{Value=valueBits,Step=stepBits,Result=result,Valid=true};return result;
        }
        public static double MaximumHealth(PlayableProfile profile,double value)=>Math.Max(profile.MaximumHealthRoundingStep,Nearest(value,profile.MaximumHealthRoundingStep));
        public static double Damage(PlayableProfile profile,double value)=>Nearest(value,profile.DamageRoundingStep);
        // Presentation only: live entities retain fractional health and cannot show zero.
        public static double DisplayHealth(double value)=>Math.Ceiling(value);
    }
}
