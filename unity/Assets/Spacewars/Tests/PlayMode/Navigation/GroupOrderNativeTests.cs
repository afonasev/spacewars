using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.TestTools;
using UnityEngine;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Presentation;
namespace Spacewars.Tests.PlayMode
{
    public sealed class GroupOrderNativeTests
    {
        [UnityTest] public IEnumerator FlatMoveReceiptUsesActualHostActivationArrivalAndOwnerProjection()
        {
            var p=PlayableProfile.Default;var type=typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.PlayableDomain",true);
            const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
            var domain=Activator.CreateInstance(type,flags,null,new object[]{p,1L,false},null);
            object Call(string name,params object[] args)=>type.GetMethod(name,flags).Invoke(domain,args);
            var nav=(NavigationSession)type.GetProperty("Navigation",flags).GetValue(domain);
            var geometry=(NavGeometry)type.GetProperty("Geometry",flags).GetValue(domain);
            int id=(int)Call("SpawnUnit",new NavPoint(-12,-12),PlayableOwner.Player,PlayableEntityKind.Tank);
            var goal=new NavPoint(-10,-12);
            Assert.AreEqual(PlayableCommandStatus.Applied,(PlayableCommandStatus)Call("Apply",new PlayableCommand(1,1,"player-1",PlayableCommandKind.Move,new[]{id},goal).AsHuman(),null));
            Assert.Null(Call("SpatialCompletion",id,PlayableOwner.Player));
            using(var host=new UnityHostRouteService()){
                for(int tick=0;tick<300&&Call("SpatialCompletion",id,PlayableOwner.Player)==null;tick++){
                    host.Service(nav.Requests,nav.Answers,1,geometry,p,64);Call("Step",1d/30);yield return null;
                }
                Assert.Greater(host.NavMeshRequests+host.FlowRequests,0);
            }
            var receipt=(PlayableSpatialCompletion)Call("SpatialCompletion",id,PlayableOwner.Player);
            Assert.NotNull(receipt);Assert.AreEqual(goal,receipt.OriginalTarget);Assert.AreEqual(1,receipt.Order.Sequence);
            Assert.AreEqual(PlayableSpatialCompletion.FlatSurface,receipt.SurfaceId);
            Assert.Null(Call("SpatialCompletion",id,PlayableOwner.Enemy));
            Assert.AreEqual(PlayableCommandStatus.Applied,(PlayableCommandStatus)Call("Apply",new PlayableCommand(1,2,"player-1",PlayableCommandKind.Stop,new[]{id}).AsHuman(),null));
            Call("Step",1d/30);Assert.Null(Call("SpatialCompletion",id,PlayableOwner.Player));
        }
        [UnityTest] public IEnumerator AuthoredBridgeAndFoundryRampReceiptsUseActualUnityHostRoute()
        {
            foreach(bool foundry in new[]{false,true}){
                var p=foundry?PlayableProfile.Create(PlayableProfile.Default.CopyData(),new FoundryMap(new FoundryProfileData())):PlayableProfile.ThreeCrossingsDefault;
                var type=typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.PlayableDomain",true);var flags=BindingFlags.Instance|BindingFlags.NonPublic;
                var domain=Activator.CreateInstance(type,flags,null,new object[]{p,1L,false},null);object Call(string name,params object[] args)=>type.GetMethod(name,flags).Invoke(domain,args);
                var nav=(NavigationSession)type.GetProperty("Navigation",flags).GetValue(domain);var geometry=(NavGeometry)type.GetProperty("Geometry",flags).GetValue(domain);
                var from=foundry?new NavPoint(0,35):new NavPoint(-20,0);var goal=foundry?new NavPoint(77,35):new NavPoint(20,0);
                int id=(int)Call("SpawnUnit",from,PlayableOwner.Player,PlayableEntityKind.Tank);Assert.AreEqual(PlayableCommandStatus.Applied,Call("Apply",new PlayableCommand(1,1,"player-1",PlayableCommandKind.Move,new[]{id},goal).AsHuman(),null));
                var anchor=nav.GroupFor(id).OriginalLocation.Value;bool sawIntermediate=false;
                using(var host=new UnityHostRouteService()){
                    for(int tick=0;tick<1500&&Call("SpatialCompletion",id,PlayableOwner.Player)==null;tick++){
                        host.Service(nav.Requests,nav.Answers,1,geometry,p,64);Call("Step",1d/30);
                        var actor=nav.Crowd.Units.Single(u=>u.Id==id);Assert.True(p.AuthoredMap.IsValidLocation(actor.Location,actor.Radius));
                        if(foundry?actor.Location.SurfaceId.StartsWith("flank-ramp/"):actor.Location.SurfaceId=="bridge/central")sawIntermediate=true;
                        if(tick%20==0)yield return null;
                    }
                    Assert.Greater(host.NavMeshRequests+host.FlowRequests,0);
                }
                var receipt=(PlayableSpatialCompletion)Call("SpatialCompletion",id,PlayableOwner.Player);Assert.NotNull(receipt);Assert.True(sawIntermediate);Assert.AreEqual(anchor,receipt.OriginalLocation);Assert.AreNotEqual("flat",receipt.SurfaceId);
            }
        }
        [UnityTest] public IEnumerator AiAdapterAuthoredMoveCompletesThroughOrdinaryRuntimeAndOwnerObservation()
        {
            var p=PlayableProfile.ThreeCrossingsDefault;
            using(var runtime=new PlayableRuntime(p,51,7,autonomousOwnerAi:false,autonomousEnemyAi:false,startPaused:true))
            using(var host=new UnityHostRouteService()){
                try{
                    double startup=Time.realtimeSinceStartupAsDouble+3;while((runtime.Latest==null||runtime.Latest.Sequence==0)&&Time.realtimeSinceStartupAsDouble<startup)yield return new WaitForSecondsRealtime(.01f);
                    var adapter=new PlayableAiAdapter(runtime);var observation=adapter.Observe();var actor=observation.Entities.First(e=>e.Owner==PlayableOwner.Player);var goal=new NavPoint(actor.Position.X+2,actor.Position.Z);
                    Assert.True(runtime.NavigationBinding.Geometry.IsFree(goal,PlayableUnitRules.Radius(p,actor.Kind)));Assert.True(p.AuthoredMap.TryTraverse(actor.Location.Value,goal,PlayableUnitRules.Radius(p,actor.Kind),out var assigned));
                    runtime.RequestPause(false);double resume=Time.realtimeSinceStartupAsDouble+3;while(runtime.Latest.Paused&&Time.realtimeSinceStartupAsDouble<resume)yield return new WaitForSecondsRealtime(.01f);Assert.False(runtime.Latest.Paused);
                    var current=adapter.Observe();Assert.AreEqual(PlayableAiDeliveryStatus.Scheduled,adapter.Schedule(current,new PlayableAiAction(31,"player-1",current.ProfileId,current.ProfileRevision,current.Generation,current.SnapshotSequence,PlayableCommandKind.Move,new[]{actor.Id},goal),0));adapter.Pump();
                    PlayableSpatialCompletion receipt=null;
                    long issuedTick=runtime.Latest.Tick;double deadline=Time.realtimeSinceStartupAsDouble+12;
                    while(receipt==null&&Time.realtimeSinceStartupAsDouble<deadline){
                        host.Service(runtime.Requests,runtime.Answers,runtime.Latest.Generation,runtime.NavigationBinding.Geometry,p,64);adapter.Pump();
                        Assert.False(adapter.Trace.Any(t=>t.ActionId==31&&(t.Status==PlayableAiDeliveryStatus.Rejected||t.Status==PlayableAiDeliveryStatus.Cancelled)),"Applied command required: "+string.Join(";",adapter.Trace.Select(t=>t.Status)));
                        receipt=adapter.Observe().Entities.Single(e=>e.Id==actor.Id).Completion;yield return new WaitForSecondsRealtime(.01f);
                    }
                    Assert.NotNull(receipt);Assert.Greater(runtime.Latest.Tick,issuedTick);Assert.AreEqual(assigned,receipt.AssignedLocation);Assert.AreEqual(PlayableOrderOrigin.Ai,receipt.Order.Origin);Assert.AreEqual(31,receipt.Order.ActionId);Assert.AreEqual(goal,receipt.OriginalLocation.Position);Assert.AreNotEqual("flat",receipt.SurfaceId);
                    Assert.True(adapter.Trace.Any(t=>t.ActionId==31&&t.Status==PlayableAiDeliveryStatus.Applied));Assert.Greater(host.NavMeshRequests+host.FlowRequests,0);Assert.IsNull(runtime.Latest.Failure);
                }finally{runtime.RequestStop();}
                double stop=Time.realtimeSinceStartupAsDouble+3;while(!runtime.IsStopped&&Time.realtimeSinceStartupAsDouble<stop)yield return new WaitForSecondsRealtime(.01f);Assert.True(runtime.IsStopped);
            }
        }
        [UnityTest] public IEnumerator S09S10FiftyMixedMembersUseActualCommandAndHostRouteWithSubsetHold()
        {
            var p=PlayableProfile.Default;var type=typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.PlayableDomain",true);
            const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
            var domain=Activator.CreateInstance(type,flags,null,new object[]{p,1L,false,true,null},null);
            object Call(string name,params object[] args)=>type.GetMethod(name,flags).Invoke(domain,args);
            foreach(var owner in new[]{PlayableOwner.Player,PlayableOwner.Enemy})Call("AddBuilding",owner,PlayableBuildingKind.Headquarters,p.Headquarters(owner),true);
            Call("RebuildGeometry");
            var nav=(NavigationSession)type.GetProperty("Navigation",flags).GetValue(domain);var geometry=(NavGeometry)type.GetProperty("Geometry",flags).GetValue(domain);
            var ids=new List<int>();
            for(double z=-20;z<=20&&ids.Count<50;z+=3.2)for(double x=-20;x<=20&&ids.Count<50;x+=3.2){
                var at=new NavPoint(x,z);var kind=(PlayableEntityKind)(ids.Count%3);double radius=PlayableUnitRules.Radius(p,kind);
                if(!geometry.IsFree(at,radius))continue;
                ids.Add((int)Call("SpawnUnit",at,PlayableOwner.Player,kind));
            }
            Assert.AreEqual(50,ids.Count);var goal=new NavPoint(12,-15);
            var command=new PlayableCommand(1,1,"player-1",PlayableCommandKind.Move,ids.AsEnumerable().Reverse().ToArray(),goal).AsHuman();
            Assert.AreEqual(PlayableCommandStatus.Applied,(PlayableCommandStatus)Call("Apply",command,null));var group=nav.GroupFor(ids[0]);Assert.AreEqual(50,group.Members.Length);
            int held=ids[0];Assert.AreEqual(PlayableCommandStatus.Applied,(PlayableCommandStatus)Call("Apply",new PlayableCommand(1,2,"player-1",PlayableCommandKind.Stop,new[]{held}).AsHuman(),null));
            Assert.AreEqual(49,nav.GroupFor(ids[1]).Members.Length);var heldAt=nav.Crowd.Units.Single(u=>u.Id==held).Position;
            using(var host=new UnityHostRouteService()){
                host.Service(nav.Requests,nav.Answers,1,geometry,p,64);nav.ApplyResults();
                Assert.AreEqual(1,nav.RejectedResults,"Superseded subset job cannot apply; unaffected group jobs remain valid.");Assert.AreEqual(0,nav.PendingCount);Assert.Greater(nav.AppliedResults,0);
                Assert.AreEqual(50,host.NavMeshRequests+host.FlowRequests);
                // A later HOLD is a real topology change and reissues the remaining
                // member connectors without replacing their common accepted order.
                Assert.AreEqual(PlayableCommandStatus.Applied,(PlayableCommandStatus)Call("Apply",new PlayableCommand(1,3,"player-1",PlayableCommandKind.Hold,new[]{held}).AsHuman(),null));
                for(int tick=0;tick<30;tick++){Call("Step",1d/30);host.Service(nav.Requests,nav.Answers,1,geometry,p,64);yield return null;}
            }
            var heldUnit=nav.Crowd.Units.Single(u=>u.Id==held);Assert.True(heldUnit.Held);Assert.AreEqual(heldAt,heldUnit.Position);
            Assert.AreEqual(group.GroupId,nav.GroupFor(ids[1]).GroupId);Assert.AreEqual(49,nav.GroupFor(ids[1]).Members.Length);Assert.AreEqual(0,nav.PendingCount);
            Assert.False(nav.TryForwardResume(1,held,group.GroupId,group.OrderRevision,group.Members[0].Incarnation,out _,out _));
        }
        [UnityTest] public IEnumerator AuthoredMixedFifoExecutesLiveAttackThroughActualHostAndRestoredQueue()
        {
            var p=PlayableProfile.ThreeCrossingsDefault;var type=typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.PlayableDomain",true);const BindingFlags f=BindingFlags.Instance|BindingFlags.NonPublic;
            var d=Activator.CreateInstance(type,f,null,new object[]{p,1L,false},null);object Call(string name,params object[] args)=>type.GetMethod(name,f).Invoke(d,args);
            var nav=(NavigationSession)type.GetProperty("Navigation",f).GetValue(d);
            int id=(int)Call("SpawnUnit",new NavPoint(-20,0),PlayableOwner.Player,PlayableEntityKind.Tank),enemy=(int)Call("SpawnUnit",new NavPoint(-10,0),PlayableOwner.Enemy,PlayableEntityKind.Tank);
            Call("SpawnUnit",new NavPoint(-14,12),PlayableOwner.Player,PlayableEntityKind.Explorer);Call("Damage",enemy,99);
            Assert.AreEqual(PlayableCommandStatus.Applied,Call("Apply",new PlayableCommand(1,1,"player-1",PlayableCommandKind.Move,new[]{id},new NavPoint(-22,0)).AsHuman(),null));
            Assert.AreEqual(PlayableCommandStatus.Applied,Call("Apply",new PlayableCommand(1,2,"player-1",PlayableCommandKind.AttackMove,new[]{id},new NavPoint(-24,0),mode:PlayableOrderMode.Append).AsHuman(),null));
            Assert.AreEqual(PlayableCommandStatus.Applied,Call("Apply",new PlayableCommand(1,3,"player-1",PlayableCommandKind.Attack,new[]{id},targetId:enemy,mode:PlayableOrderMode.Append).AsHuman(),null));
            var bytes=(byte[])Call("CaptureWorldBytes",7,"host-fifo");d=type.GetMethod("RestoreWorldBytes",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{bytes,p,7,"host-fifo"});nav=(NavigationSession)type.GetProperty("Navigation",f).GetValue(d);
            bool liveAttack=false;PlayableUnitQueueSnapshot q=null;
            using(var host=new UnityHostRouteService()){
                for(int k=0;k<1400;k++){
                    host.Service(nav.Requests,nav.Answers,nav.Generation,(NavGeometry)type.GetProperty("Geometry",f).GetValue(d),p,64);Call("Step",1d/30);
                    q=(PlayableUnitQueueSnapshot)Call("TacticalQueueProjection",id,PlayableOwner.Player);if(q.Active?.Kind==PlayableCommandKind.Attack&&q.Active.VisibleTargetId==enemy)liveAttack=true;
                    if(q.Completion?.Order.Sequence==3)break;if(k%15==0)yield return null;
                }
                Assert.Greater(host.NavMeshRequests+host.FlowRequests,0);
            }
            Assert.True(liveAttack,"Direct Attack must execute against a still-live visible target, not only skip a dead head.");Assert.NotNull(q.Completion);Assert.AreEqual(3,q.Completion.Order.Sequence);Assert.Zero(q.Deferred.Count);
        }
        [UnityTest] public IEnumerator RuntimePauseCaptureRestoresNonemptyTacticalQueueWithoutAdvancingIt()
        {
            var p=PlayableProfile.ThreeCrossingsDefault;byte[] saved=null;long tick=0;int id=0;
            using(var r=new PlayableRuntime(p,81,7,autonomousOwnerAi:false,autonomousEnemyAi:false,startPaused:true))using(var host=new UnityHostRouteService()){
                double until=Time.realtimeSinceStartupAsDouble+5;while((r.Latest==null||r.Latest.Sequence==0)&&Time.realtimeSinceStartupAsDouble<until)yield return new WaitForSecondsRealtime(.01f);Assert.NotNull(r.Latest);
                var unit=r.Latest.Entities.First(u=>u.Owner==PlayableOwner.Player);id=unit.Id;var goal=new NavPoint(unit.Position.X-5,unit.Position.Z);
                r.RequestPause(false);until=Time.realtimeSinceStartupAsDouble+5;while(r.Latest.Paused&&Time.realtimeSinceStartupAsDouble<until)yield return new WaitForSecondsRealtime(.01f);
                Assert.True(r.TrySubmit(new PlayableCommand(81,1,"player-1",PlayableCommandKind.Move,new[]{id},goal)).Accepted);Assert.True(r.TrySubmit(new PlayableCommand(81,2,"player-1",PlayableCommandKind.AttackMove,new[]{id},unit.Position,mode:PlayableOrderMode.Append)).Accepted);
                until=Time.realtimeSinceStartupAsDouble+8;while(!r.ReceiptsAfter(0).Any(c=>c.Sequence==2&&c.Status==PlayableCommandStatus.Applied)&&Time.realtimeSinceStartupAsDouble<until){host.Service(r.Requests,r.Answers,r.Generation,r.NavigationBinding.Geometry,p,64);yield return new WaitForSecondsRealtime(.01f);}
                Assert.True(r.ReceiptsAfter(0).Any(c=>c.Sequence==2&&c.Status==PlayableCommandStatus.Applied));r.RequestPause(true);until=Time.realtimeSinceStartupAsDouble+5;while(!r.Latest.Paused&&Time.realtimeSinceStartupAsDouble<until)yield return new WaitForSecondsRealtime(.01f);Assert.True(r.Latest.Paused);
                Assert.AreEqual(1,r.Latest.Entities.Single(u=>u.Id==id).Queue.Deferred.Count);tick=r.Latest.Tick;var capture=r.RequestCaptureBytes();until=Time.realtimeSinceStartupAsDouble+5;while(!capture.IsCompleted&&Time.realtimeSinceStartupAsDouble<until)yield return new WaitForSecondsRealtime(.01f);Assert.True(capture.IsCompleted);Assert.False(capture.IsFaulted,capture.Exception?.ToString());saved=capture.Result;r.RequestStop();
            }
            using(var restored=PlayableRuntime.RestoreBytes(saved,p,7,autonomousOwnerAi:false,autonomousEnemyAi:false)){
                double until=Time.realtimeSinceStartupAsDouble+5;while((restored.Latest==null||restored.Latest.Sequence==0)&&Time.realtimeSinceStartupAsDouble<until)yield return new WaitForSecondsRealtime(.01f);
                Assert.True(restored.Latest.Paused);Assert.AreEqual(tick,restored.Latest.Tick);Assert.AreEqual(1,restored.Latest.Entities.Single(u=>u.Id==id).Queue.Deferred.Count);yield return new WaitForSecondsRealtime(.1f);Assert.AreEqual(tick,restored.Latest.Tick);restored.RequestStop();
            }
        }
    }
}
