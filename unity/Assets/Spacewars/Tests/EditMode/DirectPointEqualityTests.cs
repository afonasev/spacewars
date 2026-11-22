using System;
using NUnit.Framework;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode { public sealed class DirectPointEqualityTests {
  [Test] public void DirectPointEqualityKeepsInheritedScalarSemantics(){
   var scalars=new[]{0d,BitConverter.Int64BitsToDouble(long.MinValue),double.Epsilon,-double.Epsilon,1d,-1d,double.MaxValue,double.MinValue,double.PositiveInfinity,double.NegativeInfinity,double.NaN,BitConverter.Int64BitsToDouble(0x7ff8000000000031L)};
   int count=0;foreach(double x in scalars)foreach(double z in scalars){var a=new NavPoint(x,z);foreach(double u in scalars)foreach(double v in scalars){var b=new NavPoint(u,v);Assert.AreEqual(((object)a).Equals((object)b),a.Equals(b),"original ValueType field equality");count++;}}
   TestContext.WriteLine("POINT_EQUALITY original_oracle_comparisons="+count);
  }
}}
