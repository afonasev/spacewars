using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    public sealed class MarchReplanPublicationTests
    {
        private const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
        private static byte[] Bytes(NavigationSession session)
        {using(var state=session.CaptureOwnedState())using(var bytes=WorldWire.PackOwned(w=>WorldWire.Write(w,state),state.CaptureBudget))return bytes.TransferPublic();}
        [TestCase(false)][TestCase(true)] public void NewInstalledRequestPreservesCumulativeOriginAndExactRestore(bool singleReady)
        {
            var p=PlayableProfile.Default;var geometry=new NavGeometry(30,Array.Empty<NavObstacle>(),1);var session=new NavigationSession(1,geometry,p.Navigation);var kernel=new CertifiedRouteKernel();NavigationSession cold=null;
            var binding=new PlayableRouteBinding(geometry,p,session.Admission,session.RoutePort);
            try{
                session.ConfigureMarch(.5,p.Revision);session.Crowd.Add(1,new NavPoint(-10,-2),.72,2,4.5);session.Crowd.Add(2,new NavPoint(-10,2),.72,4,4.5);
                Assert.True(session.MoveGroup(new[]{1,2},new[]{new NavPoint(10,-2),new NavPoint(10,2)}));
                for(int pump=0;pump<2000&&session.AppliedResults<2;pump++){kernel.Pump(binding,8);session.ApplyResults();}Assert.AreEqual(2,session.AppliedResults);
                for(int tick=0;tick<10;tick++)session.Step(1d/30);
                var old=session.GroupFor(1).Members.Single(m=>m.Entity==1).Copy();var oldGroup=session.GroupFor(1).CopyHeader();Assert.Greater(old.Progress,0);Assert.Greater(old.ProgressRequest,0);Assert.AreEqual(2,oldGroup.MarchVersion);
                if(!singleReady)session.SetFormationContact(1,true,10);
                Assert.True(session.Move(1,new NavPoint(12,-2)));if(!singleReady)Assert.True(session.Move(2,new NavPoint(12,2)));int answerCount=singleReady?1:2;for(int pump=0;pump<2000&&session.Answers.Count<answerCount;pump++)kernel.Pump(binding,8);Assert.AreEqual(answerCount,session.Answers.Count);
                var position=session.Crowd.Units.Single(u=>u.Id==1).Position;int ready=session.OwnedReadyPublications,batch=session.OwnedResultBatchPublications;
                if(singleReady){
                    var args=new object[]{null,true,false};Assert.True((bool)typeof(NavigationSession).GetMethod("TryPrepareAnswerNamesBatch",F).Invoke(session,args));
                    using((IDisposable)args[0])Assert.AreEqual(true,typeof(NavigationSession).GetMethod("TryPublishOwnedReady",F).Invoke(session,new object[]{args[0],0d,null}));
                    Assert.AreEqual(ready+1,session.OwnedReadyPublications);
                }else{
                    var args=new object[]{null,false,false};Assert.True((bool)typeof(NavigationSession).GetMethod("TryPrepareAnswerNamesBatch",F).Invoke(session,args));
                    using((IDisposable)args[0])Assert.AreEqual(true,typeof(NavigationSession).GetMethod("TryPublishOwnedResultBatch",F).Invoke(session,new object[]{args[0],1d/30,true,false,null,false}));
                    Assert.Greater(session.OwnedResultBatchPublications,batch);
                }
                var group=session.GroupFor(1);var member=group.Members.Single(m=>m.Entity==1);var installed=session.InstalledExecutionFor(1);
                if(singleReady)Assert.AreEqual(position,session.Crowd.Units.Single(u=>u.Id==1).Position,"Capture boundary is before any next movement step");Assert.AreEqual(oldGroup.GroupId,group.GroupId);Assert.AreEqual(old.OrderRevision,member.OrderRevision);Assert.AreNotEqual(old.ProgressRequest,installed.Corridor.Request);Assert.Zero(member.ProgressRequest);Assert.AreEqual(old.Progress,member.Progress);Assert.AreEqual(old.Progress,member.ProgressOffset);if(singleReady){Assert.AreEqual(oldGroup.MarchAnchor,group.MarchAnchor);Assert.AreEqual(oldGroup.MarchInitialized,group.MarchInitialized);}
                TestContext.WriteLine("Q1_REAL_PUBLICATION "+Newtonsoft.Json.JsonConvert.SerializeObject(new{singleReady,group.GroupId,member.Entity,oldRequest=old.ProgressRequest,member.ProgressRequest,installedRequest=installed.Corridor.Request,member.Progress,member.ProgressOffset,group.MarchAnchor}));
                var bytes=Bytes(session);cold=new NavigationSession(1,geometry,p.Navigation);cold.ConfigureMarch(.5,p.Revision);cold.RestoreState(WorldWire.Unpack(bytes,WorldWire.ReadNavigationSessionState));CollectionAssert.AreEqual(bytes,Bytes(cold));
                session.ApplyResults();cold.ApplyResults();Assert.Zero(session.GroupFor(1).Members.Single(m=>m.Entity==1).ProgressRequest);Assert.AreEqual(old.Progress,session.GroupFor(1).Members.Single(m=>m.Entity==1).ProgressOffset,"No duplicate/new result may rebase cumulative distance again");
                if(!singleReady){session.SetFormationContact(1,false,11);cold.SetFormationContact(1,false,11);}
                for(int tick=0;tick<3;tick++){session.Step(1d/30);cold.Step(1d/30);CollectionAssert.AreEqual(Bytes(session),Bytes(cold),"exact current continuation tick "+tick);}
                Assert.AreEqual(installed.Corridor.Request,session.GroupFor(1).Members.Single(m=>m.Entity==1).ProgressRequest);
            }finally{kernel.Clear();session.ReleaseCommittedHostRoot();session.DestroyNavigationAuthority();if(cold!=null){cold.ReleaseCommittedHostRoot();cold.DestroyNavigationAuthority();}}
        }
    }
}
