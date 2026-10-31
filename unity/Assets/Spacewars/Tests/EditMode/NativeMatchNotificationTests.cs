using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    public sealed class NativeMatchNotificationTests
    {
        const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
        static readonly Type Domain=typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.PlayableDomain",true);
        object domain;NativeMatchNotifications n;PlayableProfile p;
        object Call(string name,params object[] args){var m=Domain.GetMethod(name,F);var parameters=m.GetParameters();return m.Invoke(domain,parameters.Select((parameter,i)=>i<args.Length?args[i]:parameter.DefaultValue).ToArray());}
        PlayableSnapshot View(PlayableOwner owner=PlayableOwner.Player)=>(PlayableSnapshot)Call("PlayerSnapshot",1L,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7,owner);
        PlayableMatchEvent E(long id,long tick,double x,MatchEventKind kind=MatchEventKind.UnitsAttacked,PlayableOwner owner=PlayableOwner.Player)=>new PlayableMatchEvent(id,tick,owner,kind,new NavPoint(x,0));
        void Feed(double now,params PlayableMatchEvent[] events)=>n.Update(View().WithMatchEvents(events),false,now,p);
        [SetUp] public void Setup(){p=PlayableProfile.Default;domain=Activator.CreateInstance(Domain,F,null,new object[]{p,1L},null);n=new NativeMatchNotifications();View();}
        [Test] public void LethalAndPositiveDamageAreOwnFactsWithoutSurvivingVision()
        {
            var own=View().Entities.First(e=>e.Owner==PlayableOwner.Player);Call("Damage",own.Id,1);Call("Damage",own.Id,100000);
            Assert.AreEqual(2,View().MatchEvents.Count);Assert.True(View().MatchEvents.All(e=>e.Battle));Assert.IsEmpty(View(PlayableOwner.Enemy).MatchEvents);
            Assert.False(View().Entities.Any(e=>e.Id==own.Id));Assert.AreEqual(own.Position,View().MatchEvents.Last().Point);
        }
        [Test] public void ContinuousBattleIsSilentRemoteBattleAndReopenedFrontNotify()
        {
            Feed(0,E(1,0,0));Assert.AreEqual(1,n.Count);
            Feed(.1,E(2,20,1));Assert.AreEqual(1,n.Count);
            Feed(.2,E(3,40,40));Assert.AreEqual(2,n.Count);
            Feed(.3,E(4,60,2));Assert.AreEqual(2,n.Count);Assert.AreEqual(40,n.Latest.Point.X);
            Feed(.4,E(5,300,2));Assert.AreEqual(3,n.Count);
        }
        [Test] public void LatestCompletionReplacesBattleAndHistoryNavigatesForExactlyFiveSeconds()
        {
            Feed(0,E(1,0,0));Feed(1,E(2,1,20,MatchEventKind.UnitReady));Feed(2,E(3,2,40,MatchEventKind.ResearchCompleted));
            Assert.AreEqual(20,n.Focus(true,4,p).Value.X);Assert.AreEqual(0,n.Focus(true,4,p).Value.X);Assert.AreEqual(0,n.Focus(true,4,p).Value.X);
            Assert.AreEqual(40,n.Focus(false,4,p).Value.X);
            Assert.AreEqual("Исследование завершено",n.Visible(4.99,p).Text);Assert.Null(n.Visible(5,p));
            Assert.AreEqual(20,n.Focus(true,5,p).Value.X);Assert.Null(n.Focus(false,7,p));
        }
        [Test] public void OwnSignalsAreMarksOnlyAndAllyRefreshWithSameIdentityIsNewNotification()
        {
            var own=new PlayableTeamSignal(1,PlayableOwner.Player,TeamSignalKind.Attack,new NavPoint(1,0),0,150);
            var ally=new PlayableTeamSignal(2,(PlayableOwner)2,TeamSignalKind.Defend,new NavPoint(10,0),0,150);
            n.Update(View().WithTeamSignals(new[]{own,ally}),false,0,p);Assert.AreEqual(1,n.Count);Assert.AreEqual(10,n.Latest.Point.X);
            n.Update(View().WithTeamSignals(new[]{own,ally}),false,1,p);Assert.AreEqual(1,n.Count);
            var refreshed=new PlayableTeamSignal(2,(PlayableOwner)2,TeamSignalKind.Defend,new NavPoint(11,0),5,155);
            n.Update(View().WithTeamSignals(new[]{own,refreshed}),false,2,p);Assert.AreEqual(2,n.Count);Assert.AreEqual(11,n.Latest.Point.X);
        }
        [Test] public void OverviewContainsBattlesOnlyAndPerspectiveChangeDoesNotReplayOldFacts()
        {
            Call("Sound",PlayableSoundKind.ProductionComplete,new NavPoint(-20,0),PlayableEntityKind.Tank,(PlayableOwner?)PlayableOwner.Player,true);
            Call("MatchEvent",MatchEventKind.UnitsAttacked,PlayableOwner.Enemy,new NavPoint(20,0));
            var frame=(PlayableSpectatorFrame)Call("SpectatorSnapshot",1L,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7);
            Assert.AreEqual(1,frame.Overview.MatchEvents.Count);Assert.True(frame.Overview.MatchEvents.All(e=>e.Battle));
            n.Update(frame.Overview,true,0,p);Assert.AreEqual(1,n.Count);n.Update(frame.Perspective(PlayableOwner.Player),false,1,p);Assert.Zero(n.Count);
            n.Update(frame.Perspective(PlayableOwner.Player).WithMatchEvents(new[]{E(3,1,0,MatchEventKind.BuildingCompleted)}),false,2,p);Assert.AreEqual("Здание построено",n.Latest.Text);
        }
        [Test] public void CompletionFactsDoNotMutateCheckpointAndAiProjectionRemainsEmpty()
        {
            const string source="native-domain-world-v1:c77962dd:source:96a32f63:ai-release-c569e3a03045";
            var before=(byte[])Call("CaptureWorldBytes",7,source);
            foreach(var kind in new[]{PlayableSoundKind.ConstructionComplete,PlayableSoundKind.ProductionComplete,PlayableSoundKind.ResearchComplete})Call("Sound",kind,new NavPoint(-20,0),PlayableEntityKind.Tank,(PlayableOwner?)PlayableOwner.Player,true);
            Assert.AreEqual(3,View().MatchEvents.Count);CollectionAssert.AreEqual(before,(byte[])Call("CaptureWorldBytes",7,source));
            var ai=(PlayableSnapshot)Call("PlayerSnapshotForAi",1L,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7,PlayableOwner.Player,true);Assert.IsEmpty(ai.MatchEvents);
        }
        [Test] public void NotificationProfileHasValidatedMetadataAndEnforcesHumanTimeCaps()
        {
            foreach(var field in PlayableProfileMetadata.Fields.Where(f=>f.Path.StartsWith("presentation.notifications.")))Assert.Greater(field.Maximum,field.Minimum);
            Assert.AreEqual(5,PlayableProfileMetadata.Fields.Count(f=>f.Path.StartsWith("presentation.notifications.")));
            var data=p.CopyData();Assert.Throws<ArgumentOutOfRangeException>(()=>PlayableProfileMetadata.Fields.Single(f=>f.Path=="presentation.notifications.seconds").Write(data,4));
        }
    }
}
