using System;
using System.Reflection;
using System.IO;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class NavigationActiveArrivalAuthorityTests
    {
        private static byte[] ExactWire(NavigationSessionState state){using(var output=new MemoryStream()){using(var writer=new BinaryWriter(output,System.Text.Encoding.UTF8,true))WorldWire.Write(writer,state);return output.ToArray();}}
        private const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        [Test] public void ActiveArrivalSubsetReissueCannotBypassOwnerAdmission()
        {
            var type=typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.PlayableDomain",true);
            var domain=Activator.CreateInstance(type,Hidden,null,new object[]{PlayableProfile.Default,1L,false},null);
            object Call(string name,params object[] values)=>type.GetMethod(name,Hidden).Invoke(domain,values);
            var nav=(NavigationSession)type.GetProperty("Navigation",Hidden).GetValue(domain);
            var geometry=(NavGeometry)type.GetProperty("Geometry",Hidden).GetValue(domain);
            var kernel=new CertifiedRouteKernel();
            try{
                int first=(int)Call("SpawnUnit",new NavPoint(-12,-12),PlayableOwner.Player,PlayableEntityKind.Tank);
                int second=(int)Call("SpawnUnit",new NavPoint(-12,-9),PlayableOwner.Player,PlayableEntityKind.Tank);
                Assert.That(Call("Apply",new PlayableCommand(1,1,"player-1",PlayableCommandKind.Move,new[]{first,second},new NavPoint(-10,-12)).AsHuman(),null),Is.EqualTo(PlayableCommandStatus.Applied));
                kernel.PauseAtPhysicalBoundary=()=>nav.ArrivalPhases.Length!=0;
                kernel.Pump(new PlayableRouteBinding(geometry,PlayableProfile.Default,nav.Admission,nav.RoutePort),64);
                Assert.That(nav.ArrivalPhases,Is.Not.Empty,"Actual physical arrival must be partially prepared before reissue.");
                Assert.That(nav.AllocationCount,Is.EqualTo(2));
                var budget=nav.PhysicalHostBudget;long bytes=budget.PhysicalBytes;var admission=nav.Admission;long group=nav.GroupFor(first).GroupId;int calls=0;
                budget.AllocationAdmission=_=>{calls++;return false;};
                var status=Call("Apply",new PlayableCommand(1,2,"player-1",PlayableCommandKind.Move,new[]{first},new NavPoint(-8,-12)).AsHuman(),null);
                TestContext.WriteLine("actual active arrival status="+status+" admissionCalls="+calls+" oldGroup="+group+" newGroup="+nav.GroupFor(first).GroupId+" bytes="+bytes+"->"+budget.PhysicalBytes);
                Assert.That(status,Is.EqualTo(PlayableCommandStatus.Overflow));Assert.That(calls,Is.GreaterThan(0));Assert.That(nav.GroupFor(first).GroupId,Is.EqualTo(group));Assert.That(nav.Admission,Is.SameAs(admission));Assert.That(budget.PhysicalBytes,Is.EqualTo(bytes));
            }finally{nav.PhysicalHostBudget.AllocationAdmission=null;kernel.PauseAtPhysicalBoundary=null;kernel.Clear();nav.ReleaseCommittedHostRoot();nav.DestroyNavigationAuthority();Assert.That(nav.PhysicalHostBudget.PhysicalBytes,Is.Zero);Assert.That(nav.PhysicalHostBudget.HeldBytes,Is.Zero);Assert.That(nav.PhysicalHostBudget.ActiveScopes,Is.Zero);}
        }
        [Test] public void ActualActiveArrivalContactAndSubsetReissueKeepImmutableVersionsCoherent()
        {
            var type=typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.PlayableDomain",true);
            var domain=Activator.CreateInstance(type,Hidden,null,new object[]{PlayableProfile.Default,1L,false},null);
            object Call(string name,params object[] values)=>type.GetMethod(name,Hidden).Invoke(domain,values);
            var nav=(NavigationSession)type.GetProperty("Navigation",Hidden).GetValue(domain);var geometry=(NavGeometry)type.GetProperty("Geometry",Hidden).GetValue(domain);var kernel=new CertifiedRouteKernel();NavigationAuthorityGroup oldVersion=null;
            NavigationLiveGroup Owner(long id)=>(NavigationLiveGroup)((NavigationCommandSlots<long,GroupOrderState>)typeof(NavigationSession).GetField("groups",Hidden).GetValue(nav)).OwnerFor(id);
            try{
                int first=(int)Call("SpawnUnit",new NavPoint(-12,-12),PlayableOwner.Player,PlayableEntityKind.Tank);int second=(int)Call("SpawnUnit",new NavPoint(-12,-9),PlayableOwner.Player,PlayableEntityKind.Tank);
                Assert.That(Call("Apply",new PlayableCommand(1,1,"player-1",PlayableCommandKind.Move,new[]{first,second},new NavPoint(-10,-12)).AsHuman(),null),Is.EqualTo(PlayableCommandStatus.Applied));
                kernel.PauseAtPhysicalBoundary=()=>nav.ArrivalPhases.Length!=0;kernel.Pump(new PlayableRouteBinding(geometry,PlayableProfile.Default,nav.Admission,nav.RoutePort),64);Assert.That(nav.ArrivalPhases,Is.Not.Empty);
                long original=nav.GroupFor(first).GroupId;oldVersion=Owner(original).ImmutableVersion;oldVersion.Retain();
                nav.SetFormationContact(first,true,9);
                var current=Owner(original).ImmutableVersion;Assert.That(current,Is.Not.SameAs(oldVersion));var immutable=(NavigationAuthorityMember)current.Members[0];Assert.That(immutable.Data.FormationReleased,Is.True);Assert.That(immutable.Data.ContactTick,Is.EqualTo(9));Assert.That(immutable.Data.Phase,Is.EqualTo(nav.GroupFor(first).Members[0].Phase));Assert.That(((NavigationAuthorityMember)oldVersion.Members[0]).Data.ContactTick,Is.Zero);
                Assert.That(Call("Apply",new PlayableCommand(1,2,"player-1",PlayableCommandKind.Move,new[]{first},new NavPoint(-8,-12)).AsHuman(),null),Is.EqualTo(PlayableCommandStatus.Applied));
                Assert.That(nav.GroupFor(first).GroupId,Is.Not.EqualTo(original));Assert.That(nav.GroupFor(second).GroupId,Is.EqualTo(original));Assert.That(Owner(original).ImmutableVersion.Members.Count,Is.EqualTo(1));Assert.That(Owner(nav.GroupFor(first).GroupId).ImmutableVersion.Members.Count,Is.EqualTo(1));
                oldVersion.Release();oldVersion=null; // The immutability proof is complete; both continuations have the same external pins.
                using(var saved=nav.CaptureState()){
                    var detached=(NavGeometry)typeof(NavigationSession).GetMethod("LoadGeometry",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{saved.Geometry});
                    var cold=new NavigationSession(nav.Generation,detached,PlayableProfile.Default.Navigation);cold.ConfigureMarch(PlayableProfile.Default.GroupMarchMaximumStretch,PlayableProfile.Default.Revision);
                    try{cold.RestoreState(saved);Assert.That(cold.AllocationCount,Is.EqualTo(nav.AllocationCount));Assert.That(cold.GroupFor(first).GroupId,Is.EqualTo(nav.GroupFor(first).GroupId));Assert.That(cold.GroupFor(second).GroupId,Is.EqualTo(original));
                        using(var recaptured=cold.CaptureState()){Assert.That(recaptured.HostAuthority,Is.EqualTo(saved.HostAuthority));Assert.That(recaptured.ArrivalAuthority,Is.EqualTo(saved.ArrivalAuthority));}
                        var fresh=new CertifiedRouteKernel();try{
                            kernel.PauseAtPhysicalBoundary=null;
                            for(int pump=0;pump<2;pump++){
                                kernel.Pump(new PlayableRouteBinding(nav.AuthorityGeometry,PlayableProfile.Default,nav.Admission,nav.RoutePort),64);
                                fresh.Pump(new PlayableRouteBinding(cold.AuthorityGeometry,PlayableProfile.Default,cold.Admission,cold.RoutePort),64);
                                Assert.That(kernel.CapturePumpFrontier().Stage,Is.EqualTo(CertifiedRouteKernel.PumpStage.Idle));Assert.That(fresh.CapturePumpFrontier().Stage,Is.EqualTo(CertifiedRouteKernel.PumpStage.Idle));
                                using(var a=nav.CaptureState())using(var b=cold.CaptureState()){Assert.That(ExactWire(b),Is.EqualTo(ExactWire(a)),pump==0?"Resumed suffix":"Next complete Pump");}
                            }
                            int sourceReceipts=0,coldReceipts=0;nav.RouteActivated+=_=>sourceReceipts++;cold.RouteActivated+=_=>coldReceipts++;
                            for(int tick=0;tick<300;tick++){
                                nav.PrepareDeliveryBarrier();cold.PrepareDeliveryBarrier();
                                kernel.Pump(new PlayableRouteBinding(nav.AuthorityGeometry,PlayableProfile.Default,nav.Admission,nav.RoutePort),64);fresh.Pump(new PlayableRouteBinding(cold.AuthorityGeometry,PlayableProfile.Default,cold.Admission,cold.RoutePort),64);
                                Assert.That(cold.DeliveryBarrierReady,Is.EqualTo(nav.DeliveryBarrierReady));
                                if(nav.DeliveryBarrierReady){nav.Step(1d/30);cold.Step(1d/30);}
                                using(var a=nav.CaptureState())using(var b=cold.CaptureState()){
                                    Assert.That(cold.PhysicalHostBudget.PhysicalBytes,Is.EqualTo(nav.PhysicalHostBudget.PhysicalBytes),"Exact retained owner shape at tick "+tick);
                                    Assert.That(ExactWire(b),Is.EqualTo(ExactWire(a)),"Actual continuation tick "+tick);
                                }
                            }
                            Assert.That(sourceReceipts,Is.EqualTo(2));Assert.That(coldReceipts,Is.EqualTo(sourceReceipts));
                            Assert.That(nav.Crowd.TryGet(first,out var firstActor),Is.True);Assert.That(nav.Crowd.TryGet(second,out var secondActor),Is.True);Assert.That(firstActor.Outcome,Is.EqualTo(NavigationOutcome.Arrived));Assert.That(secondActor.Outcome,Is.EqualTo(NavigationOutcome.Arrived));
                            foreach(int entity in new[]{first,second}){var live=nav.GroupFor(entity);Assert.That(((NavigationAuthorityMember)Owner(live.GroupId).ImmutableVersion.Members[0]).Data.Phase,Is.EqualTo(live.Members[0].Phase),"Actual arrived member COW");}
                            using(var before=nav.CaptureState()){
                                long bytes=nav.PhysicalHostBudget.PhysicalBytes;var previous=nav.Admission;
                                nav.PhysicalHostBudget.AllocationAdmission=_=>false;try{Assert.That(nav.TryStopGroup(new[]{first,second},false),Is.False);}finally{nav.PhysicalHostBudget.AllocationAdmission=null;}
                                Assert.That(nav.Admission,Is.SameAs(previous));Assert.That(nav.PhysicalHostBudget.PhysicalBytes,Is.EqualTo(bytes));using(var after=nav.CaptureState())Assert.That(ExactWire(after),Is.EqualTo(ExactWire(before)),"Denied Stop preserves old wire");
                            }
                            var stop=new PlayableCommand(1,3,"player-1",PlayableCommandKind.Stop,new[]{first,second}).AsHuman();long stopRevision=(long)type.GetField("nextOrderRevision",Hidden).GetValue(domain);
                            Assert.That(Call("Apply",stop,null),Is.EqualTo(PlayableCommandStatus.Applied));cold.PrepareCommandGroup(stop,stopRevision,0,(int)PlayableOwner.Player);Assert.That(cold.TryStopGroup(new[]{first,second},false),Is.True);cold.FinishCommandGroup(true,new[]{first,second});
                            Assert.That(firstActor.Outcome,Is.EqualTo(NavigationOutcome.Stopped));Assert.That(secondActor.Outcome,Is.EqualTo(NavigationOutcome.Stopped));
                            using(var a=nav.CaptureState())using(var b=cold.CaptureState())Assert.That(ExactWire(b),Is.EqualTo(ExactWire(a)),"Actual Human Stop");
                            foreach(int entity in new[]{first,second}){var live=nav.GroupFor(entity);Assert.That(((NavigationAuthorityMember)Owner(live.GroupId).ImmutableVersion.Members[0]).Data.Phase,Is.EqualTo(live.Members[0].Phase));}
                            kernel.Pump(new PlayableRouteBinding(nav.AuthorityGeometry,PlayableProfile.Default,nav.Admission,nav.RoutePort),64);fresh.Pump(new PlayableRouteBinding(cold.AuthorityGeometry,PlayableProfile.Default,cold.Admission,cold.RoutePort),64);
                            using(var a=nav.CaptureState())using(var b=cold.CaptureState())Assert.That(ExactWire(b),Is.EqualTo(ExactWire(a)),"Post Stop complete Pump");
                            using(var stopped=nav.CaptureState()){
                                var detachedAgain=(NavGeometry)typeof(NavigationSession).GetMethod("LoadGeometry",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{stopped.Geometry});var late=new NavigationSession(nav.Generation,detachedAgain,PlayableProfile.Default.Navigation);late.ConfigureMarch(PlayableProfile.Default.GroupMarchMaximumStretch,PlayableProfile.Default.Revision);var lateKernel=new CertifiedRouteKernel();
                                try{late.RestoreState(stopped);using(var recaptured=late.CaptureState())Assert.That(ExactWire(recaptured),Is.EqualTo(ExactWire(stopped)),"Cold restore after receipt/arrival/Stop");
                                    kernel.Pump(new PlayableRouteBinding(nav.AuthorityGeometry,PlayableProfile.Default,nav.Admission,nav.RoutePort),64);lateKernel.Pump(new PlayableRouteBinding(late.AuthorityGeometry,PlayableProfile.Default,late.Admission,late.RoutePort),64);using(var a=nav.CaptureState())using(var b=late.CaptureState())Assert.That(ExactWire(b),Is.EqualTo(ExactWire(a)),"Fresh kernel after post Stop restore");
                                }finally{lateKernel.Clear();late.ReleaseCommittedHostRoot();late.DestroyNavigationAuthority();Assert.That(late.PhysicalHostBudget.PhysicalBytes,Is.Zero);Assert.That(late.PhysicalHostBudget.HeldBytes,Is.Zero);Assert.That(late.PhysicalHostBudget.ActiveScopes,Is.Zero);}
                            }
                        }finally{fresh.Clear();}}
                    finally{cold.ReleaseCommittedHostRoot();cold.DestroyNavigationAuthority();Assert.That(cold.PhysicalHostBudget.PhysicalBytes,Is.Zero);}
                }
            }finally{oldVersion?.Release();kernel.PauseAtPhysicalBoundary=null;kernel.Clear();nav.ReleaseCommittedHostRoot();nav.DestroyNavigationAuthority();Assert.That(nav.PhysicalHostBudget.PhysicalBytes,Is.Zero);Assert.That(nav.PhysicalHostBudget.ActiveScopes,Is.Zero);}
        }
    }
}
