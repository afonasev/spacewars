using System;
using System.Threading.Tasks;
using NUnit.Framework;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class StatRoundingMemoTests
    {
        static double Original(double value,double step)=>(double)(Math.Round((decimal)value/(decimal)step,0,MidpointRounding.AwayFromZero)*(decimal)step);
        static void Bits(double expected,double actual)=>Assert.AreEqual(BitConverter.DoubleToInt64Bits(expected),BitConverter.DoubleToInt64Bits(actual));
        [TestCase(1,.01),TestCase(5,.1),TestCase(7,.03),TestCase(50,1)]
        public void HitsEvictionsAndProfileChangesRetainDecimalRoundingBits(double healthStep,double damageStep)
        {
            var data=PlayableProfile.Default.CopyData();data.maximumHealthRoundingStep=healthStep;data.damageRoundingStep=damageStep;var profile=PlayableProfile.Create(data);
            var other=PlayableProfile.Default;var random=new Random(838049288);
            void Check(PlayableProfile p,double value){Bits(Original(value,p.DamageRoundingStep),PlayableStatRounding.Damage(p,value));Bits(Math.Max(p.MaximumHealthRoundingStep,Original(value,p.MaximumHealthRoundingStep)),PlayableStatRounding.MaximumHealth(p,value));}
            for(int i=0;i<4096;i++){
                double value=i%3==0?(i+.5)*damageStep:random.NextDouble()*2000-100;
                Check(profile,value);Check(profile,value);Check(other,value);Check(profile,value);
                long bits=BitConverter.DoubleToInt64Bits(Math.Abs(value));Check(profile,BitConverter.Int64BitsToDouble(bits+1));Check(profile,BitConverter.Int64BitsToDouble(bits-1));
            }
            foreach(var value in new[]{0d,-0d,138d,156d,80.5,312.5,437.5,2.34,2.35,2.3,9.25}){Check(profile,value);Check(other,value);}
        }
        [Test] public void IndependentThreadsAndInvalidInputsKeepOriginalResultsAndExceptions()
        {
            var profile=PlayableProfile.Default;
            Parallel.For(0,4,lane=>{
                for(int i=0;i<1024;i++){double value=(i%17+lane*.1)*2.35;Bits(Original(value,profile.DamageRoundingStep),PlayableStatRounding.Damage(profile,value));}
                foreach(var value in new[]{double.NaN,double.PositiveInfinity,double.NegativeInfinity,double.MaxValue})Assert.Throws<OverflowException>(()=>PlayableStatRounding.Damage(profile,value));
                Bits(Original(2.35,profile.DamageRoundingStep),PlayableStatRounding.Damage(profile,2.35));
            });
        }
    }
}
