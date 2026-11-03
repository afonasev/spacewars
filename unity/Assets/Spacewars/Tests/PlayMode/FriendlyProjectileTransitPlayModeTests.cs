using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using UnityEngine.TestTools;

public sealed class FriendlyProjectileTransitPlayModeTests
{
    const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
    [UnityTest] public IEnumerator SentryHitsEnemyAcrossItsOwnHeadquarters()
    {
        var profile=PlayableProfile.Default;var domain=new PlayableDomain(profile,810,false);
        object Call(string method,params object[] args)=>typeof(PlayableDomain).GetMethod(method,F).Invoke(domain,args);
        PlayableSnapshot View()=>domain.Snapshot(1,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7);
        try
        {
            var units=(IDictionary)typeof(PlayableDomain).GetField("units",F).GetValue(domain);
            foreach(int id in units.Keys.Cast<int>().ToArray()){domain.Navigation.Remove(id);units.Remove(id);}
            var home=View().Buildings.Single(b=>b.Owner==PlayableOwner.Player&&b.Kind==PlayableBuildingKind.Headquarters);
            Call("AddBuilding",PlayableOwner.Player,PlayableBuildingKind.SentryTurret,new NavPoint(home.Position.X-5,home.Position.Z),true);
            var tower=View().Buildings.Single(b=>b.Kind==PlayableBuildingKind.SentryTurret);
            int target=(int)Call("SpawnUnit",new NavPoint(home.Position.X+5,home.Position.Z),PlayableOwner.Enemy,PlayableEntityKind.Tank);
            Call("RefreshVision");bool fired=false;
            for(int tick=0;tick<90;tick++)
            {
                Call("AdvanceTurrets",1d/30);fired|=View().Projectiles.Any(p=>p.OwnerId==tower.Id);Call("AdvanceProjectiles",1d/30);
                if(View().Entities.Single(e=>e.Id==target).Health<profile.TankHealth)break;
                if(tick%15==0)yield return null;
            }
            Assert.True(fired,"The real sentry must emit a projectile");
            Assert.Less(View().Entities.Single(e=>e.Id==target).Health,profile.TankHealth,"Shot must pass through own HQ to the enemy");
            Assert.AreEqual(home.Health,View().Buildings.Single(b=>b.Id==home.Id).Health);
            Assert.AreEqual(tower.Health,View().Buildings.Single(b=>b.Id==tower.Id).Health);
        }
        finally{domain.Navigation.ReleaseCommittedHostRoot();domain.DestroyOwnedNavigation();}
    }
}
