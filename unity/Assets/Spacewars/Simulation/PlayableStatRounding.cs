using System;
namespace Spacewars.Simulation
{
    // combat-stat-rounding-v1: apply once after all upgrade multipliers.
    public static class PlayableStatRounding
    {
        private static double Nearest(double value,double step)
        {
            // Decimal conversion removes binary noise around human-authored decimal ties.
            return (double)(Math.Round((decimal)value/(decimal)step,0,MidpointRounding.AwayFromZero)*(decimal)step);
        }
        public static double MaximumHealth(PlayableProfile profile,double value)=>Math.Max(profile.MaximumHealthRoundingStep,Nearest(value,profile.MaximumHealthRoundingStep));
        public static double Damage(PlayableProfile profile,double value)=>Nearest(value,profile.DamageRoundingStep);
        // Presentation only: live entities retain fractional health and cannot show zero.
        public static double DisplayHealth(double value)=>Math.Ceiling(value);
    }
}
