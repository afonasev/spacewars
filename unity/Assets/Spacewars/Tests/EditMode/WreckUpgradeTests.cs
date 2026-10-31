using System;
using System.Collections;
using System.Linq;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    public sealed class WreckUpgradeTests
    {
        [Test] public void PreviousWreckSectionRemainsReadableWithItsHistoricalBaseAppearance()
        {
            var domain=new PlayableDomain(PlayableProfile.Default,1,false);const BindingFlags hidden=BindingFlags.Instance|BindingFlags.NonPublic;
            object Call(string name,params object[] args)=>typeof(PlayableDomain).GetMethod(name,hidden).Invoke(domain,args);
            try
            {
                int id=(int)Call("SpawnUnit",default(NavPoint),PlayableOwner.Player,PlayableEntityKind.Tank);Call("Damage",id,int.MaxValue);
                byte[] current;using(var stream=new MemoryStream()){using(var writer=new BinaryWriter(stream,System.Text.Encoding.UTF8,true))Call("WriteWrecks",writer);current=stream.ToArray();}
                // WRK1 has the same single-wreck fields and no trailing appearance byte.
                byte[] previous=current.Take(current.Length-1).ToArray();BitConverter.GetBytes(0x57524B31).CopyTo(previous,0);
                ((IDictionary)typeof(PlayableDomain).GetField("wrecks",hidden).GetValue(domain)).Clear();
                using(var stream=new MemoryStream(previous))using(var reader=new BinaryReader(stream)){Call("ReadWrecks",reader);Assert.AreEqual(stream.Length,stream.Position);}
                var wreck=domain.Snapshot(1,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7).Wrecks.Single();
                Assert.AreEqual(id,wreck.Id);Assert.False(wreck.Upgraded);Assert.True(wreck.Blocks(0));
            }
            finally{domain.DestroyOwnedNavigation();}
        }
        [TestCase(PlayableEntityKind.Tank)] [TestCase(PlayableEntityKind.Explorer)]
        [TestCase(PlayableEntityKind.Shkval)] [TestCase(PlayableEntityKind.Hawk)]
        public void DeathCapturesUpgradeAndRetainsItThroughSaveHitAndLandingFallback(PlayableEntityKind kind)
        {
            foreach(bool upgraded in new[]{false,true})
            {
                var profile=PlayableProfile.Default;var domain=new PlayableDomain(profile,1,false);
                const BindingFlags hidden=BindingFlags.Instance|BindingFlags.NonPublic;
                object Call(string name,params object[] args)=>typeof(PlayableDomain).GetMethod(name,hidden).Invoke(domain,args);
                PlayableSnapshot View()=>domain.Snapshot(1,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7);
                try
                {
                    if(upgraded&&kind==PlayableEntityKind.Hawk)
                    {
                        Call("AddCredits",PlayableOwner.Player,10000d);
                        Assert.AreEqual(PlayableCommandStatus.Applied,Call("BuildAt",1,5,PlayableBuildingKind.Factory,1,PlayableOwner.Player,null));
            Call("AdvanceFoundations");Call("AdvanceBuildings",100d);
            Assert.AreEqual(PlayableCommandStatus.Applied,Call("BuildAt",1,2,PlayableBuildingKind.ScientificCenter,1,PlayableOwner.Player,null));
                        Call("AdvanceFoundations");Call("AdvanceBuildings",100d);
                        int center=View().Buildings.Single(b=>b.Owner==PlayableOwner.Player&&b.Kind==PlayableBuildingKind.ScientificCenter).Id;
                        Call("RestoreResearchState",new PlayableResearchSaveState{Orders=new[]{new PlayableResearchSaveOrder{Id=1,Owner=(int)PlayableOwner.Player,Kind=PlayableResearchKind.HawkSalvoSystems,CenterId=center,Complete=true,PaidCost=300,Duration=30,Elapsed=30}}});
                    }
                    int id=(int)Call("SpawnUnit",default(NavPoint),PlayableOwner.Player,kind);
                    if(kind!=PlayableEntityKind.Hawk)
                    {
                        var units=(IDictionary)typeof(PlayableDomain).GetField("units",hidden).GetValue(domain);var unit=units[id];unit.GetType().GetField("AuthoredUpgrade").SetValue(unit,(bool?)upgraded);
                    }
                    Assert.AreEqual(upgraded,View().Entities.Single(e=>e.Id==id).Upgraded);
                    Call("Damage",id,int.MaxValue);var original=View().Wrecks.Single();Assert.AreEqual(upgraded,original.Upgraded);
                    Assert.AreEqual(upgraded,original.Destroy(0,PlayableEntityKind.Shkval).Upgraded);
                    Assert.AreEqual(upgraded,original.WithoutLanding(original.ExpireTick,2).Upgraded);
                    var bytes=domain.CaptureWorldBytes(7,"wreck-upgrade");var restored=PlayableDomain.RestoreWorldBytes(bytes,profile,7,"wreck-upgrade");
                    try
                    {
                        CollectionAssert.AreEqual(bytes,restored.CaptureWorldBytes(7,"wreck-upgrade"));
                        var wreck=restored.Snapshot(1,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7).Wrecks.Single();
                        Assert.AreEqual(upgraded,wreck.Upgraded);Assert.AreEqual(original.Variant,wreck.Variant);Assert.AreEqual(original.Heading,wreck.Heading);Assert.AreEqual(original.Burial,wreck.Burial);Assert.AreEqual(original.ExpireTick,wreck.ExpireTick);Assert.AreEqual(original.Radius,wreck.Radius);
                    }
                    finally{restored.DestroyOwnedNavigation();}
                }
                finally{domain.DestroyOwnedNavigation();}
            }
        }
    }
}
