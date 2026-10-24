using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class NavigationCommandMailboxRaceTests
    {
        const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
        static byte[] Bytes(NavigationSession s){using(var state=s.CaptureOwnedState())return WorldWire.Pack(w=>WorldWire.Write(w,state));}
        static void Transfer(NavigationSession s,CertifiedRouteKernel kernel,NavGeometry geometry,PlayableProfile profile)
        {
            long revision=s.Requests.Revision;Exception failure=null;
            var worker=new Thread(()=>{try{kernel.Pump(new PlayableRouteBinding(geometry,profile,s.Admission,s.RoutePort),4);}catch(Exception e){failure=e;}}){IsBackground=true};
            worker.Start();Assert.True(worker.Join(10000),"Worker transfer must run outside the transport gate");Assert.IsNull(failure);Assert.Greater(s.Requests.Revision,revision,"Actual worker must transfer a queued request");
        }
        static void Destroy(NavigationSession s,CertifiedRouteKernel kernel)
        {
            s.BeforeCommandMailboxSnapshotForQa=null;kernel.Clear();s.ReleaseCommittedHostRoot();s.DestroyNavigationAuthority();
            Assert.Zero(s.PhysicalHostBudget.PhysicalBytes);Assert.Zero(s.PhysicalHostBudget.HeldBytes);Assert.Zero(s.PhysicalHostBudget.ActiveScopes);
        }
        [TestCase(false,false)][TestCase(true,false)][TestCase(false,true)][TestCase(true,true)]
        public void MoveRejectsWorkerDriftAndPreservesInvariantIdentity(bool group,bool invariant)
        {
            var geometry=new NavGeometry(20,Array.Empty<NavObstacle>(),1);var s=new NavigationSession(1,geometry,PlayableProfile.Default.Navigation);var kernel=new CertifiedRouteKernel();
            var fault=new InvalidOperationException("unrelated move preparation fault");byte[] expected=null;int hits=0;
            bool Move()=>group?s.MoveGroup(new[]{1,2},new[]{new NavPoint(6,-3),new NavPoint(6,3)}):s.Move(1,new NavPoint(6,-3));
            try{
                s.Crowd.Add(1,new NavPoint(-5,-3));s.Crowd.Add(2,new NavPoint(-5,3));Assert.True(s.MoveGroup(new[]{1,2},new[]{new NavPoint(5,-3),new NavPoint(5,3)}));expected=Bytes(s);
                s.BeforeCommandMailboxSnapshotForQa=kind=>{Assert.AreEqual(NavigationSession.CommandMailboxKind.Movement,kind);hits++;s.BeforeCommandMailboxSnapshotForQa=null;if(invariant)throw fault;Transfer(s,kernel,geometry,PlayableProfile.Default);expected=Bytes(s);};
                if(invariant)Assert.AreSame(fault,Assert.Throws<InvalidOperationException>(()=>Move()));else Assert.False(Move());
                Assert.AreEqual(1,hits);CollectionAssert.AreEqual(expected,Bytes(s));Assert.Zero(s.PhysicalHostBudget.HeldBytes);Assert.Zero(s.PhysicalHostBudget.ActiveScopes);
                Assert.True(Move());var prefix=Bytes(s);var cold=new NavigationSession(1,geometry,PlayableProfile.Default.Navigation);var coldKernel=new CertifiedRouteKernel();
                try{cold.RestoreState(WorldWire.Unpack(prefix,WorldWire.ReadNavigationSessionState));CollectionAssert.AreEqual(prefix,Bytes(cold));
                    for(int tick=0;tick<30;tick++){kernel.Pump(new PlayableRouteBinding(geometry,PlayableProfile.Default,s.Admission,s.RoutePort),4);s.Step(1d/30);coldKernel.Pump(new PlayableRouteBinding(geometry,PlayableProfile.Default,cold.Admission,cold.RoutePort),4);cold.Step(1d/30);}CollectionAssert.AreEqual(Bytes(s),Bytes(cold));
                }finally{Destroy(cold,coldKernel);}
            }finally{Destroy(s,kernel);}
        }
        [TestCase(false)][TestCase(true)] public void MetadataRefusalPreservesLegacyExceptionContractAndNeighbor(bool invariant)
        {
            var geometry=new NavGeometry(20,Array.Empty<NavObstacle>(),1);var s=new NavigationSession(1,geometry,PlayableProfile.Default.Navigation);var kernel=new CertifiedRouteKernel();
            var fault=new InvalidOperationException("unrelated metadata preparation fault");int hits=0;NavigationRequest[] queued=null;string neighbor=null;
            void Prepare()=>s.PrepareCommandGroup(new PlayableCommand(1,2,"player-1",PlayableCommandKind.Attack,new[]{2},targetId:99),2,2,1);
            try{
                s.Crowd.Add(1,new NavPoint(-5,-3));s.Crowd.Add(2,new NavPoint(-5,3));Assert.True(s.Move(1,new NavPoint(5,-3)));Prepare();
                s.BeforeCommandMailboxSnapshotForQa=kind=>{Assert.AreEqual(NavigationSession.CommandMailboxKind.Metadata,kind);hits++;s.BeforeCommandMailboxSnapshotForQa=null;if(!invariant){
                    // An open metadata command prevents host ingress. A bound
                    // producer can still append; exercise that real mailbox API.
                    var request=s.Requests.CopyItems().Single();Exception failure=null;long revision=s.Requests.Revision;
                    var producer=new Thread(()=>{try{Assert.True(s.Requests.TryEnqueue(request));}catch(Exception e){failure=e;}}){IsBackground=true};producer.Start();Assert.True(producer.Join(10000));Assert.IsNull(failure);Assert.Greater(s.Requests.Revision,revision);
                }queued=s.Requests.CopyItems();neighbor=Newtonsoft.Json.JsonConvert.SerializeObject(s.GroupFor(1));if(invariant)throw fault;};
                var error=Assert.Throws<InvalidOperationException>(()=>s.FinishCommandGroup(true,new[]{2}));if(invariant)Assert.AreSame(fault,error);else Assert.IsInstanceOf<NavigationCommandRefused>(error.InnerException);
                Assert.AreEqual(1,hits);CollectionAssert.AreEqual(queued,s.Requests.CopyItems());Assert.AreEqual(neighbor,Newtonsoft.Json.JsonConvert.SerializeObject(s.GroupFor(1)));Assert.IsNull(s.GroupFor(2));Assert.Zero(s.PhysicalHostBudget.HeldBytes);Assert.Zero(s.PhysicalHostBudget.ActiveScopes);
                Prepare();s.FinishCommandGroup(true,new[]{2});Assert.AreEqual(2,s.GroupFor(2).CommandSequence);Assert.AreEqual(neighbor,Newtonsoft.Json.JsonConvert.SerializeObject(s.GroupFor(1)));
            }finally{Destroy(s,kernel);}
        }
        [TestCase(false,false)][TestCase(true,false)][TestCase(false,true)][TestCase(true,true)]
        public void ArrivalSnapshotsRefuseWorkerDriftRetryAndDoNotSwallowInvariants(bool mixed,bool invariant)
        {
            var domain=(PlayableDomain)Activator.CreateInstance(typeof(PlayableDomain),F,null,new object[]{PlayableProfile.Default,1L,false},null);
            object Call(string name,params object[] args)=>typeof(PlayableDomain).GetMethod(name,F).Invoke(domain,args);
            var s=(NavigationSession)typeof(PlayableDomain).GetProperty("Navigation",F).GetValue(domain);var kernel=new CertifiedRouteKernel();var geometry=(NavGeometry)typeof(PlayableDomain).GetProperty("Geometry",F).GetValue(domain);
            int Spawn(double x,double z)=>(int)Call("SpawnUnit",new NavPoint(x,z),PlayableOwner.Player,PlayableEntityKind.Tank);
            void Pump()=>kernel.Pump(new PlayableRouteBinding(geometry,PlayableProfile.Default,s.Admission,s.RoutePort),4);
            try{
                int a=Spawn(-18,-14),b=Spawn(-18,-8),neighbor=Spawn(-26,-22);int[] movers=mixed?new[]{a,b}:new[]{a};
                Assert.AreEqual(PlayableCommandStatus.Applied,Call("Apply",new PlayableCommand(1,1,"player-1",PlayableCommandKind.Move,movers,new NavPoint(-10,-10),origin:PlayableOrderOrigin.Human),null));
                for(int i=0;i<20000&&!s.HasReadyArrival;i++)Pump();Assert.True(s.HasReadyArrival);
                var tables=(NavigationSession.NavigationLiveArrivalTables)typeof(NavigationSession).GetField("liveArrivalTables",F).GetValue(s);var attempt=tables.Attempts[tables.SortedId(0)];
                if(mixed){Assert.AreEqual(2,attempt.Work.Proposals.Count);var endpoint=attempt.Work.Proposals.OrderBy(p=>p.Member.RequestSequence).Last().Endpoint;Spawn(endpoint.Position.X,endpoint.Position.Z);}
                Assert.True(s.Move(neighbor,new NavPoint(-20,-22)));Assert.AreEqual(1,s.Requests.Count);
                byte[] expected=Bytes(s);int hits=0;var fault=new InvalidOperationException("unrelated arrival preparation fault");
                s.BeforeCommandMailboxSnapshotForQa=kind=>{Assert.AreEqual(mixed?NavigationSession.CommandMailboxKind.MixedArrival:NavigationSession.CommandMailboxKind.Arrival,kind);hits++;s.BeforeCommandMailboxSnapshotForQa=null;if(invariant)throw fault;Transfer(s,kernel,geometry,PlayableProfile.Default);expected=Bytes(s);};
                var method=typeof(NavigationSession).GetMethod(mixed?"TryRetryOwnedArrival":"TryPublishOwnedArrival",F);
                if(invariant){var error=Assert.Throws<TargetInvocationException>(()=>method.Invoke(s,new object[]{attempt}));Assert.AreSame(fault,error.InnerException);}else Assert.AreEqual(false,method.Invoke(s,new object[]{attempt}));
                Assert.AreEqual(1,hits);Assert.False(attempt.Published);CollectionAssert.AreEqual(expected,Bytes(s));Assert.Zero(s.PhysicalHostBudget.ActiveScopes);Assert.Zero(s.PhysicalHostBudget.HeldBytes);
                Assert.AreEqual(true,method.Invoke(s,new object[]{attempt}));Assert.True(attempt.Published);Assert.Greater(s.Requests.Count,0);Assert.True(s.IsPending(neighbor));
                var world=(byte[])Call("CaptureWorldBytes",7,"mailbox-race");var prefix=Bytes(s);
                var coldDomain=(PlayableDomain)typeof(PlayableDomain).GetMethod("RestoreWorldBytes",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{world,PlayableProfile.Default,7,"mailbox-race"});
                var cold=(NavigationSession)typeof(PlayableDomain).GetProperty("Navigation",F).GetValue(coldDomain);var coldGeometry=(NavGeometry)typeof(PlayableDomain).GetProperty("Geometry",F).GetValue(coldDomain);var coldKernel=new CertifiedRouteKernel();
                try{CollectionAssert.AreEqual(prefix,Bytes(cold));for(int tick=0;tick<8;tick++){Pump();coldKernel.Pump(new PlayableRouteBinding(coldGeometry,PlayableProfile.Default,cold.Admission,cold.RoutePort),4);}CollectionAssert.AreEqual(Bytes(s),Bytes(cold));}
                finally{coldKernel.Clear();cold.ReleaseCommittedHostRoot();coldDomain.DestroyOwnedNavigation();Assert.Zero(cold.PhysicalHostBudget.PhysicalBytes);Assert.Zero(cold.PhysicalHostBudget.HeldBytes);Assert.Zero(cold.PhysicalHostBudget.ActiveScopes);}
            }finally{s.BeforeCommandMailboxSnapshotForQa=null;kernel.Clear();s.ReleaseCommittedHostRoot();domain.DestroyOwnedNavigation();Assert.Zero(s.PhysicalHostBudget.PhysicalBytes);Assert.Zero(s.PhysicalHostBudget.HeldBytes);Assert.Zero(s.PhysicalHostBudget.ActiveScopes);}
        }
    }
}
