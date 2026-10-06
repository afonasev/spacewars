using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Presentation;
using UnityEngine;
namespace Spacewars.Tests.PlayMode
{
    public sealed class PlayableRallyNativeTests
    {
        [Test] public void ShippedThreeCrossingsNativeRouterAdmitsTypedBridgeAndRejectsWaterPreservingOld()
        {
            var map=new ThreeCrossingsMap(JsonUtility.FromJson<ThreeCrossingsProfileData>(Resources.Load<TextAsset>("ThreeCrossingsProfile").text));
            var p=PlayableProfile.Create(JsonUtility.FromJson<PlayableProfileData>(Resources.Load<TextAsset>("PlayableProfile").text),map);
            var type=typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.PlayableDomain",true);const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
            var d=Activator.CreateInstance(type,flags,null,new object[]{p,1L,false},null);
            object Call(string name,params object[] args)=>type.GetMethod(name,flags).Invoke(d,args);
            PlayableSnapshot View()=>(PlayableSnapshot)Call("PlayerSnapshot",1L,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,19092026,PlayableOwner.Player);
            Call("AddCredits",PlayableOwner.Player,10000d);Call("BuildAt",1,2,PlayableBuildingKind.Factory,1,PlayableOwner.Player,null);Call("AdvanceFoundations");Call("AdvanceBuildings",100d);
            int factory=View().Buildings.Single(b=>b.Kind==PlayableBuildingKind.Factory).Id;var nav=(NavigationSession)type.GetProperty("Navigation",flags).GetValue(d);
            using(var native=new UnityNavigationRouter((NavGeometry)type.GetProperty("Geometry",flags).GetValue(d),p.Navigation)){
                var target=new NavPoint(0,-38);Assert.AreEqual(PlayableCommandStatus.Accepted,Call("Apply",new PlayableCommand(1,1,"player-1",PlayableCommandKind.SetRally,new[]{factory},target),null));
                for(int step=0;step<100&&View().Buildings.Single(b=>b.Id==factory).PrivateState.PendingRally.HasValue;step++){
                    Call("AdvanceRally");while(nav.Requests.TryDequeue(out var r)){
                        var route=r.Profile.Radius==p.Navigation.Radius?native.FindPath(r.Start,r.Goal):new SharedFlowRouter(r.Geometry,r.Profile).FindPath(r.Start,r.Goal);
                        if(route.Length>0){var at=r.Start;foreach(var q in route){Assert.True(map.SupportsSweep(at,q,r.Profile.Radius),"Actual bridge/bank/ramp full-radius support");at=q;}}
                        nav.Answers.TryEnqueue(new NavigationAnswer(r,route));nav.ApplyResults();Call("AdvanceRally");
                    }
                }
                var admitted=View().Buildings.Single(b=>b.Id==factory).PrivateState;Assert.True(admitted.HasRally);Assert.AreEqual(target,admitted.Rally);Assert.False(admitted.PendingRally.HasValue);
                var water=new NavPoint(0,20);Assert.AreEqual(PlayableCommandStatus.Accepted,Call("Apply",new PlayableCommand(1,2,"player-1",PlayableCommandKind.SetRally,new[]{factory},water),null));Call("AdvanceRally");
                var rejected=View().Buildings.Single(b=>b.Id==factory).PrivateState;Assert.AreEqual(target,rejected.Rally);Assert.False(rejected.PendingRally.HasValue);
                Assert.AreEqual(PlayableCommandStatus.InvalidTarget,((PlayableCommandReceipt[])Call("DrainRallyReceipts")).Last().Status);
            }
        }
        [Test] public void PendingIntentJsonRoundTripRetainsCoordinatesSequenceAndGenerationWithoutJob()
        {
            var intent=new PlayableRallyIntentState{Generation=7,Sequence=23,Building=11,Owner=PlayableOwner.Player,TargetX=-7.125,TargetZ=38};
            var restored=JsonUtility.FromJson<PlayableRallyIntentState>(JsonUtility.ToJson(intent));Assert.AreEqual(intent.Generation,restored.Generation);Assert.AreEqual(intent.Sequence,restored.Sequence);Assert.AreEqual(intent.Building,restored.Building);Assert.AreEqual(intent.Owner,restored.Owner);Assert.AreEqual(intent.TargetX,restored.TargetX);Assert.AreEqual(intent.TargetZ,restored.TargetZ);
        }
    }
}
