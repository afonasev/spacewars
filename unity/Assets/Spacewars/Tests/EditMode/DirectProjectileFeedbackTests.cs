using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class DirectProjectileFeedbackTests
    {
        static readonly Type Domain=typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.PlayableDomain",true);
        const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
        object domain;
        NavigationSession Nav=>(NavigationSession)Domain.GetProperty("Navigation",Flags).GetValue(domain);
        object Call(string name,params object[] args)=>Domain.GetMethod(name,Flags).Invoke(domain,args);
        PlayableSnapshot View(PlayableOwner owner=PlayableOwner.Player)=>(PlayableSnapshot)Call("PlayerSnapshot",1L,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7,owner);
        [SetUp] public void Setup()
        {
            domain=Activator.CreateInstance(Domain,Flags,null,new object[]{PlayableProfile.Default,1L},null);
            var units=(IDictionary)Domain.GetField("units",Flags).GetValue(domain);
            foreach(int id in units.Keys.Cast<int>().ToArray()){Nav.Remove(id);units.Remove(id);}
        }
        [TearDown] public void Cleanup(){Nav.ReleaseCommittedHostRoot();Nav.DestroyNavigationAuthority();}
        void Shot(int id,PlayableEntityKind kind,double z=0)
        {
            var type=Domain.GetNestedType("Projectile",BindingFlags.NonPublic);var p=Activator.CreateInstance(type);
            void Set(string name,object value)=>type.GetField(name).SetValue(p,value);
            Set("Id",id);Set("Faction",PlayableOwner.Player);Set("Position",new NavPoint(-9,z));Set("Height",1d);
            Set("Kind",kind);Set("Speed",15d);Set("Remaining",10d);Set("Damage",1d);Set("DirectionX",1d);
            ((IList)Domain.GetField("projectiles",Flags).GetValue(domain)).Add(p);
        }
        [TestCase(PlayableEntityKind.Tank)][TestCase(PlayableEntityKind.Explorer)]
        public void EveryActualHitCarriesOneTerminalContactAndUnchangedDamage(PlayableEntityKind kind)
        {
            Call("SpawnUnit",new NavPoint(-12,0),PlayableOwner.Player,kind);
            int target=(int)Call("SpawnUnit",new NavPoint(-7,0),PlayableOwner.Enemy,PlayableEntityKind.Tank);
            Call("RefreshVision");
            for(int id=101;id<=103;id++)Shot(id,kind);
            Shot(104,kind,3); // Physical miss must not produce a target impact.
            Call("AdvanceProjectiles",.2d);
            var view=View();var hits=view.Sounds.Where(e=>e.Kind==PlayableSoundKind.Impact).ToArray();
            Assert.AreEqual(3,hits.Length);Assert.AreEqual(3,hits.Select(e=>e.Contact.ProjectileId).Distinct().Count());
            foreach(var hit in hits){Assert.AreEqual(target,hit.Contact.TargetId);Assert.AreEqual(kind,hit.Weapon);Assert.AreEqual(-9,hit.Contact.From.X);}
            Assert.AreEqual(PlayableProfile.Default.TankHealth-3,view.Entities.Single(e=>e.Id==target).Health);
            Assert.AreEqual(104,view.Projectiles.Single().Id);
            var hidden=View(PlayableOwner.Enemy); // target owns vision at the contact; both projections preserve the same event identity.
            CollectionAssert.AreEqual(hits.Select(e=>e.Id),hidden.Sounds.Where(e=>e.Contact!=null).Select(e=>e.Id));
        }
        [Test] public void DirectContactsCannotAcquireEventTimeVisibilityLater()
        {
            var shotType=Domain.GetNestedType("Projectile",BindingFlags.NonPublic);var shot=Activator.CreateInstance(shotType);
            shotType.GetField("Kind").SetValue(shot,PlayableEntityKind.Explorer);
            shotType.GetField("Id").SetValue(shot,100);
            Call("DirectImpactSound",shot,42,new NavPoint(25,20),false);
            Assert.IsEmpty(View().Sounds);
            Call("SpawnUnit",new NavPoint(25,20),PlayableOwner.Player,PlayableEntityKind.Explorer);Call("RefreshVision");
            Assert.IsEmpty(View().Sounds.Where(e=>e.Contact!=null));
        }
        [Test] public void ContactFactsDoNotChangeCheckpointBytes()
        {
            const string source="native-domain-world-v1:c77962dd:source:96a32f63:ai-release-c569e3a03045";
            // Sound publication asks each owner's vision. Prime that existing lazy
            // state before comparing bytes so only the transient journal changes.
            Call("RefreshVision");View();View(PlayableOwner.Enemy);
            var before=(byte[])Call("CaptureWorldBytes",7,source);
            var shot=Activator.CreateInstance(Domain.GetNestedType("Projectile",BindingFlags.NonPublic));
            Call("DirectImpactSound",shot,42,new NavPoint(25,20),false);
            var after=(byte[])Call("CaptureWorldBytes",7,source);CollectionAssert.AreEqual(before,after);
        }
        [Test] public void DirectFeedbackControlsAreNamedAndRangeValidated()
        {
            var data=(PlayableProfileData)typeof(PlayableProfile).GetMethod("DefaultData",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);
            data.explorerImpactScale=double.NaN;Assert.Throws<ArgumentException>(()=>PlayableProfile.Create(data));
            data.explorerImpactScale=.4;data.directImpactRadius=0;Assert.Throws<ArgumentException>(()=>PlayableProfile.Create(data));
            data.directImpactRadius=.18;data.directImpactSparkWidth=double.PositiveInfinity;Assert.Throws<ArgumentException>(()=>PlayableProfile.Create(data));
            data.directImpactSparkWidth=.035;data.directImpactSmokeOpacity=.6;Assert.Throws<ArgumentException>(()=>PlayableProfile.Create(data));
            Assert.Less(PlayableProfile.Default.ExplorerImpactScale,1);
        }
    }
}
