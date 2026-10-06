using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    public sealed class PlayableFollowProfileTests
    {
        internal static PlayableProfileData Data()=>(PlayableProfileData)typeof(PlayableProfile).GetMethod("DefaultData",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);
        [TestCase(0,.25)][TestCase(2,0)][TestCase(10.1,.25)][TestCase(2,1.05)][TestCase(2.05,.25)][TestCase(2,.075)][TestCase(double.NaN,.25)][TestCase(2,double.PositiveInfinity)]
        public void MissingInvalidOrOffStepFollowValuesFailClosed(double distance,double tolerance)
        {var d=Data();d.followDistance=distance;d.followArrivalTolerance=tolerance;Assert.Throws<ArgumentException>(()=>PlayableProfile.Create(d));}
        [TestCase(true)][TestCase(false)]
        public void EachEffectiveFollowScalarInvalidatesDetachedWorldBeforeHydration(bool distance)
        {
            var original=PlayableProfile.Default;
            // Use the public authority capture API through an isolated flat configuration.
            var config=OfflineParticipantAuthorityTests.Config(2);var a=new OfflineParticipantAuthority(config,77);var bytes=a.CaptureBytes();
            var d=Data();if(distance)d.followDistance+=.1;else d.followArrivalTolerance+=.05;
            var binding=typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.WorldWire").GetMethod("Binding",BindingFlags.Static|BindingFlags.NonPublic);
            var oldBytes=(byte[])binding.Invoke(null,new object[]{original});var next=(byte[])binding.Invoke(null,new object[]{PlayableProfile.Create(d)});Assert.False(oldBytes.SequenceEqual(next));
            // Replace only the profile in an otherwise identical immutable configuration.
            var costs=new double[config.Starts.Count,config.Starts.Count];for(int i=0;i<config.Starts.Count;i++)for(int j=0;j<config.Starts.Count;j++)costs[i,j]=config.RouteCost(i,j);
            var changed=new OfflineMatchConfiguration(PlayableProfile.Create(d),config.SourceIdentity,config.MapIdentity,config.RouteProvenance,config.Seed,config.Roster.ToArray(),config.Starts.ToArray(),config.Sites.ToArray(),config.Obstacles.ToArray(),costs,config.Spectators.ToArray());
            Assert.Throws<ArgumentException>(()=>OfflineParticipantAuthority.Restore(bytes,changed));
        }
        [Test] public void SourceDefaultsAndMetadataRemainExplicit()
        {Assert.AreEqual(2,PlayableProfile.Default.FollowDistance);Assert.AreEqual(.25,PlayableProfile.Default.FollowArrivalTolerance);Assert.AreEqual(2,PlayableProfileMetadata.Fields.Count(f=>f.Path.StartsWith("system.follow.")));}
    }
}
