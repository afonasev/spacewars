using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    public sealed class UnitDestructionObservationTests
    {
        const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
        static readonly Type Domain=typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.PlayableDomain",true);
        [Test]public void LethalDamageRemovesAuthorityUnitAndPublishesItsFinalPoseOnlyToPermittedObservers()
        {
            var profile=PlayableProfile.Default;var domain=Activator.CreateInstance(Domain,Flags,null,new object[]{profile,1L,false},null);
            var position=new NavPoint(profile.Headquarters(PlayableOwner.Enemy).X-3,profile.Headquarters(PlayableOwner.Enemy).Z-3);
            int id=(int)Domain.GetMethod("SpawnUnit",Flags).Invoke(domain,new object[]{position,PlayableOwner.Enemy,PlayableEntityKind.Shkval});
            var nav=(NavigationSession)Domain.GetProperty("Navigation",Flags).GetValue(domain);Assert.IsTrue(nav.Crowd.TryGet(id,out var n));typeof(NavUnit).GetProperty("Heading").SetValue(n,.7);
            Domain.GetMethod("RefreshVision",Flags).Invoke(domain,null);
            Domain.GetMethod("Damage",Flags).Invoke(domain,new object[]{id,10000});Assert.IsFalse(nav.Crowd.TryGet(id,out _));
            PlayableSnapshot View(PlayableOwner owner)=>(PlayableSnapshot)Domain.GetMethod("PlayerSnapshot",Flags).Invoke(domain,new object[]{1L,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7,owner});
            var own=View(PlayableOwner.Enemy);var fact=own.Sounds.Single(e=>e.Kind==PlayableSoundKind.Destroyed&&e.UnitId==id);Assert.AreEqual(position,fact.Position);Assert.AreEqual(.7,fact.HullHeading);Assert.AreEqual(PlayableEntityKind.Shkval,fact.Weapon);Assert.IsFalse(fact.Building);Assert.IsFalse(own.Entities.Any(e=>e.Id==id));
            Assert.IsFalse(View(PlayableOwner.Player).Sounds.Any(e=>e.UnitId==id),"Unobserved hostile death must not disclose position or pose");
        }
    }
}
