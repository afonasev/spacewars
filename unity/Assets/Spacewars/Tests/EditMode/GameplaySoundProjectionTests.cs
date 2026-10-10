using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class GameplaySoundProjectionTests
    {
        private static readonly Type Domain=typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.PlayableDomain",true);
        private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
        private object domain;
        private object Call(string name,params object[] args)=>Domain.GetMethod(name,Flags).Invoke(domain,args);
        private PlayableSnapshot View(PlayableOwner owner=PlayableOwner.Player)=>(PlayableSnapshot)Call("PlayerSnapshot",1L,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7,owner);
        [SetUp] public void Setup(){domain=Activator.CreateInstance(Domain,Flags,null,new object[]{PlayableProfile.Default,1L},null);View();}
        [Test] public void HiddenEventStaysHiddenWhenVisionLaterRevealsIt()
        {
            var point=new NavPoint(25,20);
            Call("Sound",PlayableSoundKind.Impact,point,PlayableEntityKind.Tank,(PlayableOwner?)null,false);
            Assert.IsEmpty(View().Sounds);
            Call("SpawnPlayer",point,point);
            Assert.IsEmpty(View().Sounds,"Event-time eligibility cannot be gained later.");
        }
        [Test] public void OwnUnitDestructionIsCausalAndDoesNotRequireSurvivingVision()
        {
            var unit=View().Entities.First(e=>e.Owner==PlayableOwner.Player);
            Call("Damage",unit.Id,100000);
            Assert.IsFalse(View().Entities.Any(e=>e.Id==unit.Id));
            Assert.AreEqual(1,View().Sounds.Count(e=>e.Kind==PlayableSoundKind.Destroyed));
        }
        [Test] public void JournalCountIsBoundedAndAiProjectionCarriesNoAudio()
        {
            for(int i=0;i<600;i++)Call("Sound",PlayableSoundKind.Shot,new NavPoint(-20,0),PlayableEntityKind.Tank,(PlayableOwner?)PlayableOwner.Player,false);
            Assert.AreEqual(256,View().Sounds.Count);
            var ai=(PlayableSnapshot)Call("PlayerSnapshotForAi",1L,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7,PlayableOwner.Player,true);
            Assert.IsEmpty(ai.Sounds);
        }
        [Test] public void SoundJournalDoesNotEnterWorldCheckpointAndRestoreStartsEmpty()
        {
            const string source="native-domain-world-v1:c77962dd:source:96a32f63:ai-release-c569e3a03045";
            var before=(byte[])Call("CaptureWorldBytes",7,source);var authority=PlayableWorldRestoreTests.Facts(domain,true);
            Call("Sound",PlayableSoundKind.Shot,new NavPoint(-20,0),PlayableEntityKind.Tank,(PlayableOwner?)PlayableOwner.Player,false);
            var after=(byte[])Call("CaptureWorldBytes",7,source);CollectionAssert.AreEqual(before,after);
            Assert.AreEqual(authority,PlayableWorldRestoreTests.Facts(domain,true));Assert.IsNotEmpty(View().Sounds);
            domain=Domain.GetMethod("RestoreWorldBytes",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{after,PlayableProfile.Default,7,source});
            Assert.IsEmpty(View().Sounds);
        }
        [TestCase(PlayableEntityKind.Tank)][TestCase(PlayableEntityKind.Explorer)][TestCase(PlayableEntityKind.Shkval)]
        public void EveryWeaponPublishesActualShotAndImpact(PlayableEntityKind kind)
        {
            var nav=(NavigationSession)Domain.GetProperty("Navigation",Flags).GetValue(domain);
            var units=(IDictionary)Domain.GetField("units",Flags).GetValue(domain);
            foreach(int id in units.Keys.Cast<int>().ToArray()){nav.Remove(id);units.Remove(id);}
            int own=(int)Call("SpawnUnit",new NavPoint(-12,0),PlayableOwner.Player,kind);
            int enemy=(int)Call("SpawnUnit",new NavPoint(-5,0),PlayableOwner.Enemy,PlayableEntityKind.Tank);
            units[enemy].GetType().GetField("Reload").SetValue(units[enemy],999d);
            Call("RefreshVision");
            Assert.AreEqual(PlayableCommandStatus.Applied,Call("Apply",new PlayableCommand(1,1,"player-1",PlayableCommandKind.Attack,new[]{own},targetId:enemy),null));
            for(int i=0;i<120;i++){Call("RefreshVision");Call("AdvanceCombat",1d/30);Call("AdvanceProjectiles",1d/30);}
            Assert.IsTrue(View().Sounds.Any(e=>e.Kind==PlayableSoundKind.Shot&&e.Weapon==kind));
            Assert.IsTrue(View().Sounds.Any(e=>e.Kind==PlayableSoundKind.Impact&&e.Weapon==kind));
        }
        [Test] public void OrdinaryProjectileHitPublishesImpactBeforeRemovingProjectile()
        {
            var own=View().Entities.First(e=>e.Owner==PlayableOwner.Player);
            var list=(IList)Domain.GetField("projectiles",Flags).GetValue(domain);
            var type=Domain.GetNestedType("Projectile",BindingFlags.NonPublic);var p=Activator.CreateInstance(type);
            void Set(string k,object value)=>type.GetField(k).SetValue(p,value);
            Set("Id",999);Set("Faction",PlayableOwner.Enemy);Set("Position",own.Position);Set("Height",.5d);Set("Speed",1d);Set("Remaining",10d);Set("Damage",1);Set("Kind",PlayableEntityKind.Tank);Set("DirectionX",1d);
            list.Add(p);Call("AdvanceProjectiles",.033d);
            Assert.IsEmpty(list);Assert.IsTrue(View().Sounds.Any(e=>e.Kind==PlayableSoundKind.Impact));
        }
    }
}
