using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Simulation;
using Spacewars.Runtime;
namespace Spacewars.Tests.EditMode
{
    public sealed class OrbitalEscalationTests
    {
        [Test] public void HackCooldownIsIndependentAndHasValidatedLaboratoryMetadata()
        {
            var data=PlayableProfile.Default.CopyData();data.orbitalHackSeconds=47;data.orbitalBasicSeconds=23;
            var profile=PlayableProfile.Create(data);var state=new OrbitalEscalationState();state.Damage(250,data);Assert.True(state.Choose(OrbitalAbilities.ControlHack));Assert.True(state.Use(OrbitalAbilities.ControlHack,data));
            Assert.AreEqual(47,state.Snapshot(data).Abilities.Single().Duration);Assert.AreEqual(47,state.Snapshot(data).Abilities.Single().Remaining);Assert.AreEqual(23,OrbitalAbilities.Duration(1,data));Assert.AreEqual(23,OrbitalAbilities.Duration(2,data));Assert.AreEqual(data.orbitalCommonSeconds,state.CommonRemaining);
            var field=PlayableProfileMetadata.Fields.Single(f=>f.FieldName=="orbitalHackSeconds");Assert.AreEqual("с",field.Unit);Assert.AreEqual(47,field.Read(profile.CopyData()));
            data.orbitalHackSeconds=0;Assert.Throws<ArgumentException>(()=>PlayableProfile.Create(data));
        }
        [Test] public void ThresholdsResetCarryFractionalExcessAndCapAtMax()
        {
            var p=new PlayableProfileData();var s=new OrbitalEscalationState();
            s.Damage(249.5,p);Assert.AreEqual(0,s.Level);s.Damage(20.75,p);Assert.AreEqual(1,s.Level);Assert.AreEqual(20.25,s.Progress);
            s.Damage(479.75,p);Assert.AreEqual(2,s.Level);Assert.AreEqual(0,s.Progress);
            s.Damage(7000,p);Assert.AreEqual(5,s.Level);Assert.AreEqual(0,s.Progress);Assert.AreEqual(p.orbitalThresholdMax,s.Snapshot(p).Threshold);
            s.Damage(7999.75,p);Assert.AreEqual(5,s.Level);Assert.AreEqual(7999.75,s.Progress);s.Damage(.25,p);Assert.AreEqual(6,s.Level);Assert.Zero(s.Progress);s.Damage(9999,p);Assert.Zero(s.Progress);
        }
        [Test] public void MaxThresholdIsIndependentOfPreviousLevelAndKeepsFractionalProgress()
        {
            var p=PlayableProfile.Default.CopyData();p.orbitalThresholdMax=12345.6;var state=new OrbitalEscalationState();
            state.Damage(p.orbitalThreshold1+p.orbitalThreshold2+p.orbitalThreshold3+p.orbitalThreshold4+p.orbitalThreshold5,p);Assert.AreEqual(5,state.Level);
            p.orbitalThreshold5=1;Assert.AreEqual(12345.6,state.Snapshot(p).Threshold);
            state.Damage(12345.5,p);Assert.AreEqual(5,state.Level);Assert.AreEqual(12345.5,state.Progress);
            state.Damage(.1,p);Assert.AreEqual(6,state.Level);Assert.Zero(state.Progress);
        }
        [Test] public void AbsoluteMaxMetadataUsesHpAndValidatesItsIndependentRange()
        {
            var f=PlayableProfileMetadata.Fields.Single(field=>field.FieldName=="orbitalThresholdMax");Assert.AreEqual("HP",f.Unit);Assert.That(f.Label,Does.Contain("V").And.Contain("MAX"));
            Assert.False(PlayableProfileMetadata.Fields.Any(field=>field.FieldName=="orbitalMaxThresholdMultiplier"));
            foreach(double invalid in new[]{0d,1000000.1,double.NaN,double.PositiveInfinity}){
                var data=PlayableProfile.Default.CopyData();data.orbitalThresholdMax=invalid;Assert.Throws<ArgumentException>(()=>PlayableProfile.Create(data));
            }
            var valid=PlayableProfile.Default.CopyData();valid.orbitalThresholdMax=45.1;Assert.AreEqual(45.1,OrbitalAbilities.Threshold(5,PlayableProfile.Create(valid).CopyData()));
        }
        [Test] public void PendingChoicesAreOrderedUniqueAndPermanent()
        {
            var p=new PlayableProfileData();var s=new OrbitalEscalationState();s.Damage(15750,p);
            Assert.AreEqual(1,s.PendingLevel);Assert.False(s.Choose(3));Assert.True(s.Choose(2));
            CollectionAssert.AreEqual(new[]{1,8},s.Options());Assert.False(s.Choose(2));Assert.True(s.Choose(8));
            CollectionAssert.AreEqual(new[]{3,4,5},s.Options());Assert.True(s.Choose(4));CollectionAssert.AreEqual(new[]{3,5},s.Options());Assert.False(s.Choose(4));Assert.True(s.Choose(3));Assert.True(s.Choose(7));Assert.Zero(s.PendingLevel);Assert.False(s.Choose(1));
            CollectionAssert.AreEqual(new[]{2,8,4,3,7},s.Snapshot(p).Abilities.Select(a=>a.Id));
        }
        [Test] public void IndependentAbilityTimersSharedIntervalAndOnlyMaxAcceleratesRecovery()
        {
            var p=new PlayableProfileData();var s=new OrbitalEscalationState();s.Damage(7750,p);foreach(int id in new[]{1,2,3,4,6})Assert.True(s.Choose(id));
            Assert.True(s.Use(1,p));Assert.False(s.Use(2,p));s.Advance(10,p);Assert.True(s.Use(2,p));Assert.AreEqual(20,s.Recovery(0));Assert.AreEqual(30,s.Recovery(1));
            s.Advance(5,p);Assert.AreEqual(15,s.Recovery(0));Assert.AreEqual(25,s.Recovery(1));Assert.AreEqual(15,s.Snapshot(p).Abilities[0].Remaining,"V still recovers at normal rate");
            s.Damage(8000,p);Assert.AreEqual(6,s.Level);Assert.AreEqual(7.5,s.Snapshot(p).Abilities[0].Remaining);
            s.Advance(2,p);Assert.AreEqual(11,s.Recovery(0));Assert.AreEqual(21,s.Recovery(1));Assert.AreEqual(3,s.CommonRemaining,"common interval is not accelerated");
            s.Advance(3,p);Assert.True(s.Use(6,p));Assert.AreEqual(45,s.Snapshot(p).Abilities.Last().Remaining);
        }
        [Test] public void RestorePreservesPendingChoicesAndFiveExactCooldownSlots()
        {
            var p=new PlayableProfileData();var s=new OrbitalEscalationState();s.Damage(1900.25,p);s.Choose(1);s.Choose(2);s.Use(2,p);s.Advance(.125,p);
            var restore=OrbitalEscalationState.Restore(s.Level,s.Progress,s.CommonRemaining,Enumerable.Range(0,5).Select(s.Choice).ToArray(),Enumerable.Range(0,5).Select(s.Recovery).ToArray(),p);
            Assert.AreEqual(3,restore.PendingLevel);Assert.AreEqual(s.Progress,restore.Progress);Assert.AreEqual(s.CommonRemaining,restore.CommonRemaining);Assert.AreEqual(s.Recovery(1),restore.Recovery(1));CollectionAssert.AreEqual(new[]{3,4,5},restore.Options());
            Assert.Throws<ArgumentException>(()=>OrbitalEscalationState.Restore(3,0,0,new[]{1,1,3,0,0},new double[5],p));
            s.Damage(20000,p);foreach(int id in new[]{3,4,6})Assert.True(s.Choose(id));s.Advance(10,p);Assert.True(s.Use(6,p));
            restore=OrbitalEscalationState.Restore(s.Level,s.Progress,s.CommonRemaining,Enumerable.Range(0,5).Select(s.Choice).ToArray(),Enumerable.Range(0,5).Select(s.Recovery).ToArray(),p);
            for(int i=0;i<5;i++){Assert.AreEqual(s.Choice(i),restore.Choice(i));Assert.AreEqual(s.Recovery(i),restore.Recovery(i));}Assert.AreEqual(6,restore.Level);
        }
        [Test] public void AlliedOwnersDoNotContributeAndViewsRemainPersonal()
        {
            var profile=PlayableProfile.Create(PlayableProfile.Default.CopyData(),new AiTestMap());var map=(AiTestMap)profile.AuthoredMap;
            var config=map.Configuration(profile,7,new[]{new OfflineParticipant("player-1",1,1,OfflineControl.Human),new OfflineParticipant("player-2",2,1,OfflineControl.Human),new OfflineParticipant("player-3",3,2,OfflineControl.Human)});
            var type=typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.PlayableDomain");const BindingFlags f=BindingFlags.NonPublic|BindingFlags.Instance;
            var domain=Activator.CreateInstance(type,f,null,new object[]{profile,1L,config},null);
            var credit=type.GetMethod("CreditOrbital",f);credit.Invoke(domain,new object[]{(PlayableOwner?)PlayableOwner.Player,PlayableOwner.Enemy,300d});
            var view=type.GetMethod("OrbitalView",f);Assert.AreEqual(0,((OrbitalEscalationSnapshot)view.Invoke(domain,new object[]{PlayableOwner.Player})).Level);
            credit.Invoke(domain,new object[]{(PlayableOwner?)PlayableOwner.Player,(PlayableOwner)2,300d});
            Assert.AreEqual(1,((OrbitalEscalationSnapshot)view.Invoke(domain,new object[]{PlayableOwner.Player})).Level);Assert.AreEqual(0,((OrbitalEscalationSnapshot)view.Invoke(domain,new object[]{PlayableOwner.Enemy})).Level);
        }
        [Test] public void DomainCreditsActualHostileDamageAndRejectsInvalidUse()
        {
            var type=typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.PlayableDomain");const BindingFlags f=BindingFlags.NonPublic|BindingFlags.Instance;
            var domain=Activator.CreateInstance(type,f,null,new object[]{PlayableProfile.Default,1L,false},null);
            PlayableSnapshot View()=>(PlayableSnapshot)type.GetMethod("Snapshot",f).Invoke(domain,new object[]{1L,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7});
            var snap=View();var own=snap.Entities.First(e=>e.Owner==PlayableOwner.Player);var enemy=snap.Entities.First(e=>e.Owner==PlayableOwner.Enemy);
            var damage=type.GetMethod("DamageWithOwner",f);
            damage.Invoke(domain,new object[]{own.Id,10d,own.Id,(PlayableOwner?)PlayableOwner.Player});Assert.AreEqual(0,View().Orbital.Progress);
            damage.Invoke(domain,new object[]{enemy.Id,.5d,own.Id,(PlayableOwner?)PlayableOwner.Player});Assert.AreEqual(.5d,View().Orbital.Progress);
            damage.Invoke(domain,new object[]{enemy.Id,10000d,own.Id,(PlayableOwner?)PlayableOwner.Player});Assert.AreEqual(enemy.Health,View().Orbital.Progress,1e-9,"overkill counts only live health");
            var hq=snap.Buildings.First(b=>b.Owner==PlayableOwner.Enemy);damage.Invoke(domain,new object[]{hq.Id,260d,0,(PlayableOwner?)PlayableOwner.Player});Assert.AreEqual(1,View().Orbital.Level);
            var apply=type.GetMethod("Apply",f);PlayableCommandStatus Cmd(PlayableCommandKind kind,int id,string owner="player-1")=>(PlayableCommandStatus)apply.Invoke(domain,new object[]{new PlayableCommand(1,10,owner,kind,null,targetId:id),null});
            Assert.AreEqual(PlayableCommandStatus.Rejected,Cmd(PlayableCommandKind.UseOrbital,1));Assert.AreEqual(PlayableCommandStatus.Applied,Cmd(PlayableCommandKind.ChooseOrbital,1));Assert.AreEqual(PlayableCommandStatus.Applied,Cmd(PlayableCommandKind.UseOrbital,1));Assert.AreEqual(PlayableCommandStatus.Rejected,Cmd(PlayableCommandKind.UseOrbital,1));Assert.AreEqual(30,View().Orbital.Abilities[0].Remaining);
            type.GetMethod("AdvanceOrbital",f).Invoke(domain,new object[]{.125d});
            var before=View().Orbital;
            var bytes=(byte[])type.GetMethod("CaptureWorldBytes",f).Invoke(domain,new object[]{7,"orbital-escalation-test"});
            var restored=type.GetMethod("RestoreWorldBytes",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{bytes,PlayableProfile.Default,7,"orbital-escalation-test"});
            var after=((PlayableSnapshot)type.GetMethod("Snapshot",f).Invoke(restored,new object[]{1L,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7})).Orbital;
            Assert.AreEqual(before.Level,after.Level);Assert.AreEqual(before.Progress,after.Progress);Assert.AreEqual(before.CommonRemaining,after.CommonRemaining);Assert.AreEqual(before.Abilities[0].Recovery,after.Abilities[0].Recovery);
            CollectionAssert.AreEqual(bytes,(byte[])type.GetMethod("CaptureWorldBytes",f).Invoke(restored,new object[]{7,"orbital-escalation-test"}));
        }
    }
}
