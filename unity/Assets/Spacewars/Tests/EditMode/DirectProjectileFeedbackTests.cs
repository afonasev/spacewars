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
        [TestCase(false,PlayableEntityKind.Explorer)][TestCase(false,PlayableEntityKind.Hawk)]
        [TestCase(true,PlayableEntityKind.Explorer)][TestCase(true,PlayableEntityKind.Hawk)]
        public void SpatialShotsPassThroughFriendlyUnitAndHeadquartersToHitEnemy(bool allied,PlayableEntityKind kind)
        {
            var friendly=PlayableOwner.Player;
            if(allied){
                Nav.ReleaseCommittedHostRoot();Nav.DestroyNavigationAuthority();
                var authority=new OfflineParticipantAuthority(OfflineParticipantAuthorityTests.Config(),2);
                domain=OfflineParticipantAuthorityTests.Domain(authority);friendly=(PlayableOwner)2;
                var units=(IDictionary)Domain.GetField("units",Flags).GetValue(domain);
                foreach(int id in units.Keys.Cast<int>().ToArray()){Nav.Remove(id);units.Remove(id);}
            }
            var home=View().Buildings.Single(b=>b.Owner==PlayableOwner.Player&&b.Kind==PlayableBuildingKind.Headquarters);
            var cover=((IDictionary)Domain.GetField("buildings",Flags).GetValue(domain))[home.Id];cover.GetType().GetField("Owner").SetValue(cover,friendly);
            var from=new BallisticPoint(home.Position.X-8,.5,home.Position.Z);
            int friend=(int)Call("SpawnUnit",new NavPoint(home.Position.X-6,home.Position.Z),friendly,PlayableEntityKind.Tank);
            int target=(int)Call("SpawnUnit",new NavPoint(home.Position.X+5,home.Position.Z),PlayableOwner.Enemy,PlayableEntityKind.Tank);
            var type=Domain.GetNestedType("Projectile",BindingFlags.NonPublic);var shot=Activator.CreateInstance(type);
            void Set(string field,object value)=>type.GetField(field).SetValue(shot,value);
            Set("Id",201);Set("Faction",PlayableOwner.Player);Set("Position",from.Ground);Set("Height",from.Y);
            Set("Kind",kind);Set("Speed",20d);Set("Remaining",20d);Set("Damage",1d);Set("DirectionX",1d);Set("Spatial",true);Set("Target",target);
            ((IList)Domain.GetField("projectiles",Flags).GetValue(domain)).Add(shot);Call("RefreshVision");Call("AdvanceProjectiles",1d);
            var view=View();Assert.AreEqual(PlayableProfile.Default.TankHealth-1,view.Entities.Single(e=>e.Id==target).Health);
            Assert.AreEqual(PlayableProfile.Default.TankHealth,view.Entities.Single(e=>e.Id==friend).Health);
            Assert.AreEqual(home.Health,view.Buildings.Single(b=>b.Id==home.Id).Health);Assert.IsEmpty(view.Projectiles);
            Assert.AreEqual(target,view.Sounds.Single(e=>e.Kind==PlayableSoundKind.Impact).Contact.TargetId);
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
            foreach(var hit in hits){Assert.AreEqual(target,hit.Contact.TargetId);Assert.AreEqual(kind,hit.Weapon);Assert.AreEqual(-9,hit.Contact.From.X);Assert.True(hit.Contact.Impact.HasValue);Assert.AreEqual(1,hit.Contact.Impact.Value.Y);Assert.AreEqual(0,hit.Contact.Impact.Value.Z);Assert.Greater(hit.Contact.Impact.Value.X,-9);Assert.Less(hit.Contact.Impact.Value.X,-7);}
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
