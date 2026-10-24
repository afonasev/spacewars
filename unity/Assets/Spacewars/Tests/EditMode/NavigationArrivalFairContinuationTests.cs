using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    public sealed class NavigationArrivalFairContinuationTests
    {
        private const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        private sealed class World:IDisposable
        {
            internal OfflineMatchConfiguration Config;private readonly bool heavy;
            private long sequence;
            internal PlayableAuthorityTick A;internal CertifiedRouteKernel K=new CertifiedRouteKernel();internal int[] Ids;
            internal NavigationSession N=>A.Domain.Navigation;
            internal World(int count=2,bool heavy=false){
                this.heavy=heavy;OfflineMatchConfiguration basis;
                if(heavy){var setup=new NativeLobbyConfiguration{HasExplicitSeed=true,ExplicitSeed=19092026};setup.InitializeParticipants();foreach(var p in setup.Participants)p.Human=true;basis=NativeLobbyMatch.Create(PlayableProfile.ThreeCrossingsDefault,setup);}else basis=OfflineParticipantAuthorityTests.Config(2);
                var costs=new double[basis.Starts.Count,basis.Starts.Count];for(int i=0;i<basis.Starts.Count;i++)for(int j=0;j<basis.Starts.Count;j++)costs[i,j]=basis.RouteCost(i,j);
                var points=heavy?new[]{new NavPoint(-18,-1.8),new NavPoint(-18,1.8),new NavPoint(-24,0),new NavPoint(-24,-4)}:Enumerable.Range(0,count).Select(i=>new NavPoint(-18-4*i,-14)).ToArray();
                var units=points.Take(count).Select(at=>new OfflineScenarioUnit(basis.Roster[0].LogicalPlayer,PlayableEntityKind.Tank,at,0)).ToArray();
                Config=new OfflineMatchConfiguration(basis.Profile,basis.SourceIdentity,basis.MapIdentity,basis.RouteProvenance,basis.Seed,basis.Roster.ToArray(),basis.Starts.ToArray(),basis.Sites.ToArray(),basis.Obstacles.ToArray(),costs,basis.Spectators.ToArray(),units,heavy?basis.Profile.AuthoredMap.Id:"flat-ground-v1",basis.ScenarioBuildings.ToArray());
                A=new PlayableAuthorityTick(Config,71);Ids=A.Latest.Entities.Where(e=>e.Kind==PlayableEntityKind.Tank).Select(e=>e.Id).ToArray();Assert.AreEqual(count,Ids.Length);
            }
            internal void Move(int index){Assert.True(N.Crowd.TryGet(Ids[index],out var unit));var goal=heavy?new NavPoint(36,46):new NavPoint(unit.Position.X,unit.Position.Z+4);Assert.AreEqual(PlayableCommandStatus.Applied,A.Apply(new PlayableCommand(71,++sequence,Config.Roster[0].Id,PlayableCommandKind.Move,new[]{unit.Id},goal).AsHuman()).Status);Assert.NotNull(N.PendingRequestFor(unit.Id));Assert.Greater(N.AllocationCount,0,"actual command must enter physical arrival admission");}
            internal void Begin(){Assert.False(A.TryAdvance());}
            internal void Finish(){NavigationArrivalFairContinuationTests.Finish(A,K);}
            internal void Consume(){Assert.True(A.TryAdvance());}
            public void Dispose(){Cleanup(A,K);}
        }
        private static void Finish(PlayableAuthorityTick a,CertifiedRouteKernel k){for(int i=0;i<4096;i++){var r=a.Domain.Navigation.PinCommittedHostRoot();try{if(r.QuantumCompleted)return;}finally{r.Release();}k.Pump(a.NavigationBinding,4);}Assert.Fail("original quantum must finish without another grant");}
        private static void Cleanup(PlayableAuthorityTick a,CertifiedRouteKernel k){k.Clear();a.Stop();a.Domain.DestroyOwnedNavigation();Assert.Zero(a.Domain.Navigation.PhysicalHostBudget.PhysicalBytes);Assert.Zero(a.Domain.Navigation.PhysicalHostBudget.HeldBytes);Assert.Zero(a.Domain.Navigation.PhysicalHostBudget.ActiveScopes);}
        private static long Cursor(World w){var r=w.N.PinCommittedHostRoot();try{return r.LastServedArrival;}finally{r.Release();}}
        [TestCase(false)][TestCase(true)] public void PolicyBeforeFirstTickSurvivesFreshBytesAndTheExactFirstQuantum(bool legacy)
        {
            using var w=new World();w.Move(0);w.Move(1);w.A.CaptureBytes();
            if(legacy){var old=w.N.PinCommittedHostRoot();try{typeof(NavigationHostRoot).GetProperty("fairArrivalPolicy",Hidden).SetValue(old,false);}finally{old.Release();}}
            var saved=w.A.CaptureBytes();
            var root=w.N.PinCommittedHostRoot();try{Assert.AreEqual(legacy?NavPhysicalStorageKind.HostRoot:NavPhysicalStorageKind.HostRootFairArrivalV1,root.PhysicalTag.Storage);Assert.False(root.TickQuantum);Assert.AreEqual(376,NavStorageLayout.ObjectBytesBound(typeof(NavigationHostRoot)));}finally{root.Release();}
            var other=PlayableAuthorityTick.RestoreBytes(saved,w.Config);var fresh=new CertifiedRouteKernel{PhysicalOperationsPerPump=1};
            try{CollectionAssert.AreEqual(saved,other.CaptureBytes(),"immediate before-first-tick fresh bytes");w.Begin();Assert.False(other.TryAdvance());w.Finish();Finish(other,fresh);CollectionAssert.AreEqual(w.A.CaptureBytes(),other.CaptureBytes(),"exact first quantum");Assert.AreEqual(legacy?0:1,Cursor(w));}
            finally{Cleanup(other,fresh);}
        }
        [Test] public void BirthAndSpentGrantColdRestorePreserveOnePreparationAndOneService()
        {
            using var w=new World();w.Move(0);w.Move(1);w.Begin();w.K.PhysicalOperationsPerPump=1;
            for(int i=0;i<128;i++){var root=w.N.PinCommittedHostRoot();bool at;try{at=root.Frontier.Stage==CertifiedRouteKernel.PumpStage.ArrivalAdvance;}finally{root.Release();}if(at)break;w.K.Pump(w.A.NavigationBinding,4);}
            Assert.True(w.N.PreparePhysicalArrivalAttempt());Assert.False(w.N.PreparePhysicalArrivalAttempt(),"at most one successful birth in this quantum");
            var before=w.N.PinCommittedHostRoot();try{Assert.AreEqual(1,before.Frontier.PhaseUsed);Assert.AreEqual(1,before.ArrivalWork.Count);}finally{before.Release();}
            var saved=w.A.CaptureBytes();var other=PlayableAuthorityTick.RestoreBytes(saved,w.Config);var fresh=new CertifiedRouteKernel{PhysicalOperationsPerPump=1};
            try{
                CollectionAssert.AreEqual(saved,other.CaptureBytes());w.K.Pump(w.A.NavigationBinding,99);fresh.Pump(other.NavigationBinding,1);CollectionAssert.AreEqual(w.A.CaptureBytes(),other.CaptureBytes(),"saved quantum budget and exact next service");
                var r=w.N.PinCommittedHostRoot();try{Assert.AreEqual(CertifiedRouteKernel.PumpStage.PreFlush,r.Frontier.Stage);Assert.AreEqual(1,r.LastServedArrival);Assert.AreEqual(1,r.ArrivalRoundEnd);Assert.AreEqual(1,((NavigationHostArrivalHistory)r.ArrivalHistory[1]).Count);}finally{r.Release();}
                var spent=w.A.CaptureBytes();var third=PlayableAuthorityTick.RestoreBytes(spent,w.Config);var thirdKernel=new CertifiedRouteKernel();
                try{CollectionAssert.AreEqual(spent,third.CaptureBytes());w.Finish();Finish(other,fresh);Finish(third,thirdKernel);CollectionAssert.AreEqual(w.A.CaptureBytes(),other.CaptureBytes());CollectionAssert.AreEqual(w.A.CaptureBytes(),third.CaptureBytes());}
                finally{Cleanup(third,thirdKernel);}
            }finally{Cleanup(other,fresh);}
        }
        [Test] public void ContinuousNewAdmissionsCannotKeepTheOldPendingAttemptOutsideTheNextRound()
        {
            using var w=new World(4,heavy:true);var seen=new long[4];
            for(int i=0;i<4;i++){w.Move(i);w.Begin();w.Finish();seen[i]=Cursor(w);if(i<3)w.Consume();}
            CollectionAssert.AreEqual(new long[]{1,1,2,1},seen,"new IDs wait behind the fixed round end");
            var r=w.N.PinCommittedHostRoot();try{Assert.AreEqual(4,r.ArrivalRoundEnd);Assert.AreEqual(3,((NavigationHostArrivalHistory)r.ArrivalHistory[1]).Count,"old work received another grant while new commands kept arriving");}finally{r.Release();}
        }
        [Test] public void CancelledServedAttemptAndReissueKeepTheOpenRoundAndExactColdSuffix()
        {
            using var w=new World(heavy:true);w.Move(0);w.Move(1);w.Begin();w.Finish();w.Consume();w.Begin();w.Finish();w.Consume();
            var previous=w.N.PendingRequestFor(w.Ids[0]);var otherRequest=w.N.PendingRequestFor(w.Ids[1]);Assert.NotNull(previous);Assert.NotNull(otherRequest);
            w.N.Stop(w.Ids[0],false);w.Move(0);Assert.AreNotEqual(previous.Request,w.N.PendingRequestFor(w.Ids[0]).Request);Assert.AreEqual(otherRequest.Request,w.N.PendingRequestFor(w.Ids[1]).Request);
            w.Begin();w.Finish();var root=w.N.PinCommittedHostRoot();try{Assert.AreEqual(2,root.LastServedArrival);Assert.AreEqual(2,root.ArrivalRoundEnd);Assert.AreEqual(1,((NavigationHostArrivalHistory)root.ArrivalHistory[2]).Count);}finally{root.Release();}
            var saved=w.A.CaptureBytes();var other=PlayableAuthorityTick.RestoreBytes(saved,w.Config);var fresh=new CertifiedRouteKernel{PhysicalOperationsPerPump=1};
            try{CollectionAssert.AreEqual(saved,other.CaptureBytes());w.Consume();Assert.True(other.TryAdvance());w.Begin();Assert.False(other.TryAdvance());w.Finish();Finish(other,fresh);CollectionAssert.AreEqual(w.A.CaptureBytes(),other.CaptureBytes(),"cancelled cursor tombstone and new attempt retain exact next round");Assert.AreEqual(2,Cursor(w));}
            finally{Cleanup(other,fresh);}
        }
        [Test] public void RefusedForkAfterScopeAdmissionSpendsOnlyThatTurnAndNextAttemptStillRuns()
        {
            using var w=new World(heavy:true);w.Move(0);w.Move(1);w.Begin();w.Finish();w.Consume();w.Begin();w.Finish();w.Consume();w.Begin();
            var before=w.N.PinCommittedHostRoot();NavigationArrivalWork second;try{second=(NavigationArrivalWork)before.ArrivalWork[2];second.Retain();Assert.AreEqual(0,((NavigationHostArrivalHistory)before.ArrivalHistory[2]).Count);}finally{before.Release();}
            int denied=0;w.N.PhysicalHostBudget.AllocationAdmission=bytes=>{if(bytes==NavigationRetentionBudget.ScopeBytes)return true;bool compute=new StackTrace().GetFrames().Any(f=>f.GetMethod().Name=="TryAdvancePhysicalArrivalAuthority");if(compute)denied++;return !compute;};
            try{w.Finish();Assert.Greater(denied,0);var r=w.N.PinCommittedHostRoot();try{Assert.AreEqual(2,r.LastServedArrival);Assert.AreSame(second,r.ArrivalWork[2]);Assert.AreEqual(0,((NavigationHostArrivalHistory)r.ArrivalHistory[2]).Count);}finally{r.Release();}}
            finally{w.N.PhysicalHostBudget.AllocationAdmission=null;second.Release();}
            w.Consume();w.Begin();w.Finish();Assert.AreEqual(1,Cursor(w));
        }
    }
}
