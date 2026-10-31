using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed partial class EightPlayerPerformanceScenarioTests
    {
#if UNITY_INCLUDE_TESTS
        [System.Runtime.InteropServices.DllImport("/usr/lib/libSystem.B.dylib",EntryPoint="clock_gettime_nsec_np")]
        private static extern ulong SnapshotThreadCpu(int clock);
        private static long SnapshotCpuNow()=>(long)SnapshotThreadCpu(16);
        private sealed class SnapshotPartCost
        {internal long Count,Wall,Cpu,Maximum;}
        [Test,Explicit("400-unit frozen configured-owner snapshot source CPU control; exclusive quiet only")]
        public void ReportFourHundredOwnerSnapshotPartsWithoutChangingState()
        {
            const BindingFlags hidden=BindingFlags.Instance|BindingFlags.NonPublic;
            var resource=System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(),"Assets/Spacewars/Content/Resources/PlayableProfile.json");Assert.True(System.IO.File.Exists(resource));
            var data=Newtonsoft.Json.JsonConvert.DeserializeObject<PlayableProfileData>(System.IO.File.ReadAllText(resource));
            var baseline=PlayableProfile.Create(data,new AiTestMap());
            var fixtureRoot=System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(),"../.local/cycle60"));System.IO.Directory.CreateDirectory(fixtureRoot);
            var store=new Spacewars.Presentation.NativeBalanceStore(baseline,System.IO.Path.Combine(fixtureRoot,"native-balance-cost.json"),false);
            store.Select(Spacewars.Presentation.NativeBalanceStore.NewMatchProfileId,Spacewars.Presentation.NativeBalanceStore.NewMatchProfileVersion);
            var profile=store.Active;var aiProfile=store.ActiveAiProfile;Assert.AreEqual(2,profile.Revision);
            System.IO.File.WriteAllText(System.IO.Path.Combine(fixtureRoot,"bound-profile.json"),Newtonsoft.Json.JsonConvert.SerializeObject(profile.CopyData(),Newtonsoft.Json.Formatting.Indented));
            System.IO.File.WriteAllText(System.IO.Path.Combine(fixtureRoot,"bound-ai-profile.json"),Newtonsoft.Json.JsonConvert.SerializeObject(aiProfile.CopyData(),Newtonsoft.Json.Formatting.Indented));
            long allocationStart=GC.GetAllocatedBytesForCurrentThread();var allocationProbe=new byte[4096];GC.KeepAlive(allocationProbe);bool allocationsSupported=GC.GetAllocatedBytesForCurrentThread()-allocationStart>=4096;
            var setup=new NativeLobbyConfiguration{AiTestMap=true,HasExplicitSeed=true,ExplicitSeed=7};setup.InitializeParticipants();
            OfflineMatchConfiguration config;
            using(var runtime=PlayableRuntime.CreateEightPlayerPerformanceMatch(profile,1,setup,startPaused:true,aiProfile:aiProfile)){
                Assert.AreEqual(400,runtime.DiagnosticGlobalUnits);
                config=(OfflineMatchConfiguration)typeof(PlayableRuntime).GetField("offlineConfiguration",hidden).GetValue(runtime);
            }
            var authority=new PlayableAuthorityTick(config,1,aiProfile:aiProfile);
            var domain=(PlayableDomain)typeof(PlayableAuthorityTick).GetField("domain",hidden).GetValue(authority);
            var scheduler=(AiAuthorityScheduler)typeof(PlayableAuthorityTick).GetField("scheduler",hidden).GetValue(authority);
            var metrics=new PlayableRuntimeMetrics(0,0,0,0,0);Assert.AreEqual(8,scheduler.Owners.Count);
            PlayableSnapshot Project(PlayableAiOwnerLoop owner){
                var view=domain.PlayerSnapshotForArmyAi(1,RuntimeStatus.Running,false,metrics,null,7,domain.OwnerFor(owner.OwnerId),owner.NeedsArmyRoutes(domain.Tick),owner.ArmyState);
                if(!owner.NeedsArmyRoutes(domain.Tick)&&owner.NeedsDefenseRoutes(view))view=domain.PlayerSnapshotForArmyAi(1,RuntimeStatus.Running,false,metrics,null,7,domain.OwnerFor(owner.OwnerId),true,owner.ArmyState);
                return view;
            }
            try{
                foreach(bool commanded in new[]{false,true}){
                    if(commanded)foreach(var owner in scheduler.Owners){
                        var view=domain.PlayerSnapshot(1,RuntimeStatus.Running,false,metrics,null,7,domain.OwnerFor(owner.OwnerId));
                        Assert.AreEqual(50,view.Entities.Count(e=>e.Owner==view.Owner));
                        var receipt=authority.Apply(PlayableRuntime.CreateEightPlayerMovingPerformanceCommand(view,PlayableRuntime.EightPlayerMovingPerformanceSequence(view.Owner,0)));
                        Assert.AreEqual(PlayableCommandStatus.Applied,receipt.Status,receipt.Message);
                    }
                    for(int warm=0;warm<4;warm++)foreach(var owner in scheduler.Owners)Project(owner);
                    var expected=scheduler.Owners.Select(Project).ToArray();
                    const int repeats=32;long baselineCpu=SnapshotCpuNow(),baselineWall=System.Diagnostics.Stopwatch.GetTimestamp(),baselineAllocated=GC.GetAllocatedBytesForCurrentThread();
                    for(int n=0;n<repeats;n++)foreach(var owner in scheduler.Owners)Project(owner);
                    baselineAllocated=allocationsSupported?GC.GetAllocatedBytesForCurrentThread()-baselineAllocated:-1;baselineWall=System.Diagnostics.Stopwatch.GetTimestamp()-baselineWall;baselineCpu=SnapshotCpuNow()-baselineCpu;
                    var costs=new System.Collections.Generic.Dictionary<string,SnapshotPartCost>();
                    var starts=new System.Collections.Generic.Dictionary<string,(long Wall,long Cpu)>();
                    Array.Clear(domain.SnapshotCostSampleAttemptsForTests,0,domain.SnapshotCostSampleAttemptsForTests.Length);
                    domain.SnapshotCostObserverForTests=(label,begin)=>{
                        if(begin){Assert.False(starts.ContainsKey(label));starts.Add(label,(System.Diagnostics.Stopwatch.GetTimestamp(),SnapshotCpuNow()));}
                        else{
                            long cpu=SnapshotCpuNow(),wall=System.Diagnostics.Stopwatch.GetTimestamp();var start=starts[label];starts.Remove(label);
                            if(!costs.TryGetValue(label,out var cost))costs.Add(label,cost=new SnapshotPartCost());cost.Count++;cost.Wall+=wall-start.Wall;cost.Cpu+=cpu-start.Cpu;cost.Maximum=Math.Max(cost.Maximum,wall-start.Wall);
                        }
                    };
                    long measuredCpu=SnapshotCpuNow(),measuredWall=System.Diagnostics.Stopwatch.GetTimestamp(),measuredAllocated=GC.GetAllocatedBytesForCurrentThread();
                    for(int n=0;n<repeats;n++)foreach(var owner in scheduler.Owners)Project(owner);
                    measuredAllocated=allocationsSupported?GC.GetAllocatedBytesForCurrentThread()-measuredAllocated:-1;measuredWall=System.Diagnostics.Stopwatch.GetTimestamp()-measuredWall;measuredCpu=SnapshotCpuNow()-measuredCpu;
                    domain.SnapshotCostObserverForTests=null;Assert.IsEmpty(starts);
                    var after=scheduler.Owners.Select(Project).ToArray();for(int i=0;i<expected.Length;i++)AssertSnapshotGraphEqual(expected[i],after[i],"owner"+i,0);
                    Assert.AreEqual(0,domain.Tick);Assert.AreEqual(400,domain.Navigation.Crowd.Units.Count);
                    TestContext.Out.WriteLine("SNAPSHOT_OWNER_COST commanded="+commanded+" owners="+scheduler.Owners.Count+" actors=400 tick="+domain.Tick+" repeats="+repeats+" profile="+profile.ProfileId+"@"+profile.Revision+" map="+profile.AuthoredMap.Id+" allocations_supported="+allocationsSupported+" observer_off_cpu_ms="+baselineCpu/1e6+" observer_off_wall_ms="+baselineWall*1000d/System.Diagnostics.Stopwatch.Frequency+" observer_off_allocated="+baselineAllocated+" instrumented_cpu_ms="+measuredCpu/1e6+" instrumented_wall_ms="+measuredWall*1000d/System.Diagnostics.Stopwatch.Frequency+" instrumented_allocated="+measuredAllocated);
                    var sampleLabels=new[]{"UnitVisibilitySample","UnitQueueSample","UnitHealthResearchSample","UnitWeaponResearchSample","UnitDefenseResearchSample"};
                    for(int i=0;i<sampleLabels.Length;i++)TestContext.Out.WriteLine("SNAPSHOT_SAMPLE_ATTEMPTS commanded="+commanded+" label="+sampleLabels[i]+" attempts="+domain.SnapshotCostSampleAttemptsForTests[i]+" sampling=1/32");
                    foreach(var pair in costs.OrderByDescending(x=>x.Value.Cpu))TestContext.Out.WriteLine("SNAPSHOT_SOURCE_PART commanded="+commanded+" label="+pair.Key+" calls="+pair.Value.Count+" cpu_ms="+pair.Value.Cpu/1e6+" wall_ms="+pair.Value.Wall*1000d/System.Diagnostics.Stopwatch.Frequency+" max_wall_ms="+pair.Value.Maximum*1000d/System.Diagnostics.Stopwatch.Frequency+" inclusive=true");
                    PerformanceProfile.Flush();
                }
            }finally{domain.SnapshotCostObserverForTests=null;authority.Stop();domain.DestroyOwnedNavigation();}
        }
        private static void AssertSnapshotGraphEqual(object expected,object actual,string path,int depth)
        {
            if(ReferenceEquals(expected,actual))return;Assert.Less(depth,32,path);Assert.IsNotNull(expected,path);Assert.IsNotNull(actual,path);
            Type type=expected.GetType();Assert.AreEqual(type,actual.GetType(),path);
            if(type==typeof(double)){Assert.AreEqual(BitConverter.DoubleToInt64Bits((double)expected),BitConverter.DoubleToInt64Bits((double)actual),path);return;}
            if(type==typeof(float)){Assert.AreEqual(BitConverter.SingleToInt32Bits((float)expected),BitConverter.SingleToInt32Bits((float)actual),path);return;}
            if(type.IsPrimitive||type.IsEnum||type==typeof(string)||type==typeof(decimal)){Assert.AreEqual(expected,actual,path);return;}
            if(expected is PlayableProfile||expected is NavGeometry){Assert.AreSame(expected,actual,path);return;}
            if(expected is System.Collections.IEnumerable left&&actual is System.Collections.IEnumerable right){
                var a=left.Cast<object>().ToArray();var b=right.Cast<object>().ToArray();Assert.AreEqual(a.Length,b.Length,path);
                for(int i=0;i<a.Length;i++)AssertSnapshotGraphEqual(a[i],b[i],path+"["+i+"]",depth+1);return;
            }
            foreach(var property in type.GetProperties(BindingFlags.Instance|BindingFlags.Public).Where(p=>p.CanRead&&p.GetIndexParameters().Length==0))AssertSnapshotGraphEqual(property.GetValue(expected),property.GetValue(actual),path+"."+property.Name,depth+1);
            foreach(var field in type.GetFields(BindingFlags.Instance|BindingFlags.Public))AssertSnapshotGraphEqual(field.GetValue(expected),field.GetValue(actual),path+"."+field.Name,depth+1);
        }
#endif
        [Test]
        public void PausedGenesisHasExactlyFiftyNonOverlappingUnitsForEachOfEightAiOwners()
        {
            var profile=PlayableProfile.Create(PlayableProfile.Default.CopyData(),new AiTestMap());
            var setup=new NativeLobbyConfiguration{AiTestMap=true,HasExplicitSeed=true,ExplicitSeed=7};
            setup.InitializeParticipants();
            using(var runtime=PlayableRuntime.CreateEightPlayerPerformanceMatch(profile,1,setup,startPaused:true))
            {
                var frame=runtime.SpectatorFrame(PlayableRuntime.LocalSpectatorId);
                Assert.AreEqual(8,frame.Players.Count);Assert.AreEqual(400,frame.Overview.Entities.Count);
                Assert.AreEqual(7,frame.Overview.Seed);Assert.AreEqual(0,frame.Overview.Tick);Assert.True(frame.Overview.Paused);
                foreach(var view in frame.Players)
                {
                    var own=view.Entities.Where(e=>e.Owner==view.Owner).ToArray();
                    Assert.AreEqual(50,own.Length);Assert.AreEqual(49,own.Count(e=>e.Kind==PlayableEntityKind.Tank));
                    Assert.AreEqual(1,own.Count(e=>e.Kind==PlayableEntityKind.Explorer));
                    var move=PlayableRuntime.CreateEightPlayerMovingPerformanceCommand(view,1);
                    Assert.AreEqual(PlayableCommandKind.Move,move.Kind);Assert.AreEqual(PlayableOrderOrigin.Ai,move.Origin);
                    Assert.AreEqual(view.OwnerId,move.PlayerId);Assert.AreEqual(view.Generation,move.Generation);
                    Assert.AreEqual(PlayableRuntime.MovingPerformanceSource,move.Source);Assert.Greater(move.JobId,0);Assert.AreEqual(move.JobId,move.ActionId);
                    CollectionAssert.AreEqual(own.OrderBy(e=>e.Id).Select(e=>e.Id),move.EntityIds);
                    Assert.True(profile.AuthoredMap.SupportsFootprint(move.Target,profile.Navigation.Radius));
                    Assert.AreEqual(PlayableCommandStatus.Rejected,runtime.SubmitEightPlayerMovingPerformanceEvidence(view).Status,"Scripted submission stays disabled without its explicit Player flag.");
                    var explorer=own.Single(e=>e.Kind==PlayableEntityKind.Explorer);
                    foreach(var tank in own.Where(e=>e.Kind==PlayableEntityKind.Tank)){
                        double dx=tank.Position.X-explorer.Position.X,dz=tank.Position.Z-explorer.Position.Z;
                        Assert.Less(dx*dx+dz*dz,25*25,"Loaded armies must share their own assigned start, including shuffled starts");
                    }
                    foreach(var e in own)Assert.True(profile.AuthoredMap.SupportsFootprint(e.Position,PlayableUnitRules.Radius(profile,e.Kind)));
                }
                var units=frame.Overview.Entities.ToArray();
                for(int a=0;a<units.Length;a++)for(int b=a+1;b<units.Length;b++)
                {
                    double dx=units[a].Position.X-units[b].Position.X,dz=units[a].Position.Z-units[b].Position.Z;
                    double radius=PlayableUnitRules.Radius(profile,units[a].Kind)+PlayableUnitRules.Radius(profile,units[b].Kind);
                    Assert.GreaterOrEqual(dx*dx+dz*dz,radius*radius,"QA genesis must not begin with overlapping units");
                }
                Assert.AreEqual(400,runtime.DiagnosticGlobalUnits);
                Assert.True(runtime.LobbyConfiguration.Spectator);
            }
        }
        [Test]
        public void MovingLoadCommandsApplyAfterInterleavedNativeAiSequences()
        {
            var profile=PlayableProfile.Create(PlayableProfile.Default.CopyData(),new AiTestMap());
            var setup=new NativeLobbyConfiguration{AiTestMap=true,HasExplicitSeed=true,ExplicitSeed=7};setup.InitializeParticipants();
            using(var runtime=PlayableRuntime.CreateEightPlayerPerformanceMatch(profile,1,setup,startPaused:true)){
                const BindingFlags fields=BindingFlags.Instance|BindingFlags.NonPublic;
                var config=(OfflineMatchConfiguration)typeof(PlayableRuntime).GetField("offlineConfiguration",fields).GetValue(runtime);
                var authority=new PlayableAuthorityTick(config,1);
                var domain=(PlayableDomain)typeof(PlayableAuthorityTick).GetField("domain",fields).GetValue(authority);
                try{
                    foreach(var view in runtime.SpectatorFrame(PlayableRuntime.LocalSpectatorId).Players.OrderBy(v=>(int)v.Owner)){
                        var own=view.Entities.Where(e=>e.Owner==view.Owner).Select(e=>e.Id).ToArray();
                        long native=domain.AllocateAiSequence();Assert.GreaterOrEqual(native,1L<<61);
                        var stop=new PlayableCommand(1,native,view.OwnerId,PlayableCommandKind.Stop,own,origin:PlayableOrderOrigin.Ai,source:"test:native-sequence-control",jobId:1,actionId:1);
                        var stopped=authority.Apply(stop);Assert.AreEqual(PlayableCommandStatus.Applied,stopped.Status,stopped.Message);
                        var rejected=authority.Apply(PlayableRuntime.CreateEightPlayerMovingPerformanceCommand(view,1000000+(int)view.Owner));
                        Assert.AreEqual(PlayableCommandStatus.InvalidSequence,rejected.Status,"Original rejected load remains a negative control.");
                        long sequence=PlayableRuntime.EightPlayerMovingPerformanceSequence(view.Owner,0);Assert.Greater(sequence,native);
                        var receipt=authority.Apply(PlayableRuntime.CreateEightPlayerMovingPerformanceCommand(view,sequence));
                        Assert.AreEqual(PlayableCommandStatus.Applied,receipt.Status,receipt.Message);
                        Assert.AreEqual(view.OwnerId,receipt.OwnerId);Assert.AreEqual(sequence,receipt.Sequence);
                        Assert.Greater(domain.AllocateAiSequence(),sequence,"The ordinary native allocator advances after the diagnostic command.");
                    }
                    Assert.AreEqual(400,domain.Navigation.Crowd.Units.Count);
                }finally{authority.Stop();domain.DestroyOwnedNavigation();}
            }
        }
        [Test]
        public void ReportFirstFiftyMemberArrivalPreparationWithoutAdvancingTheMatch()
        {
            var profile=PlayableProfile.Create(PlayableProfile.Default.CopyData(),new AiTestMap());
            var setup=new NativeLobbyConfiguration{AiTestMap=true,HasExplicitSeed=true,ExplicitSeed=7};setup.InitializeParticipants();
            using(var runtime=PlayableRuntime.CreateEightPlayerPerformanceMatch(profile,1,setup,startPaused:true)){
                const BindingFlags hidden=BindingFlags.Instance|BindingFlags.NonPublic;
                var config=(OfflineMatchConfiguration)typeof(PlayableRuntime).GetField("offlineConfiguration",hidden).GetValue(runtime);
                var authority=new PlayableAuthorityTick(config,1);
                var domain=(PlayableDomain)typeof(PlayableAuthorityTick).GetField("domain",hidden).GetValue(authority);
                var nav=domain.Navigation;var kernel=new CertifiedRouteKernel();
                try{
                    var view=runtime.SpectatorFrame(PlayableRuntime.LocalSpectatorId).Players.OrderBy(v=>(int)v.Owner).First();
                    var receipt=authority.Apply(PlayableRuntime.CreateEightPlayerMovingPerformanceCommand(view,PlayableRuntime.EightPlayerMovingPerformanceSequence(view.Owner,0)));
                    Assert.AreEqual(PlayableCommandStatus.Applied,receipt.Status,receipt.Message);
                    nav.PrepareTickQuantum(1,profile.NavigationRequestsPerFrame,false);
                    kernel.PauseAtPhysicalBoundary=()=>nav.ArrivalPhases.Length>0;
                    for(int n=0;n<16&&nav.ArrivalPhases.Length==0;n++)kernel.Pump(new PlayableRouteBinding(nav.AuthorityGeometry,profile,nav.Admission,nav.RoutePort),profile.NavigationRequestsPerFrame);
                    var root=nav.PinCommittedHostRoot();
                    try{
                        Assert.AreEqual(1,root.ArrivalWork.Count);
                        var work=(NavigationArrivalWork)root.ArrivalWork.First().Value;var input=work.PhysicalInput;
                        Assert.AreEqual(50,input.Members.Count);Assert.AreEqual(400,nav.Crowd.Units.Count);
                        var key=typeof(NavigationArrivalWork).GetMethod("RawWorkingKey",hidden);
                        int keys=input.Members.Select(m=>(string)key.Invoke(work,new object[]{m})).Distinct().Count();
                        TestContext.WriteLine("ARRIVAL_50_BIRTH candidates="+input.Source.Candidates.Count+" members="+input.Members.Count+" workingKeys="+keys+" occupancy="+input.Occupancy.Occupants.Count+" phase="+work.WorkPhase+" member="+work.MemberCursor+" candidateWork="+work.CandidateWorkConsumed+" occupancyWork="+work.OccupancyWorkConsumed+" epoch="+work.LogicalEpoch);
                        Assert.AreEqual(0,domain.Tick,"This diagnostic only prepares the first admitted input, without advancing the match.");
                    }finally{root?.Release();}
                    kernel.PauseAtPhysicalBoundary=null;
                    int quanta=1;
                    for(;quanta<1024&&nav.AllocationCount>0;quanta++){
                        nav.PrepareTickQuantum(quanta+1,profile.NavigationRequestsPerFrame,true);
                        kernel.Pump(new PlayableRouteBinding(nav.AuthorityGeometry,profile,nav.Admission,nav.RoutePort),profile.NavigationRequestsPerFrame);
                    }
                    root=nav.PinCommittedHostRoot();
                    try{
                        foreach(var row in root.ArrivalWork){
                            var work=(NavigationArrivalWork)row.Value;
                            TestContext.WriteLine("ARRIVAL_50_FIXED_INPUT quanta="+quanta+" allocating="+nav.AllocationCount+" status="+work.Status+" phase="+work.WorkPhase+" member="+work.MemberCursor+" candidateWork="+work.CandidateWorkConsumed+" occupancyWork="+work.OccupancyWorkConsumed+" direct="+work.DirectWorkConsumed+" epoch="+work.LogicalEpoch+" graph="+work.RouteCounters.PhysicalGraphWork+" retentionBlocked="+work.LastAdvanceRetentionBlocked);
                            Assert.AreEqual(NavArrivalStatus.Ready,work.Status,"Q2 must prepare the full original group within the existing bound");
                            Assert.True(nav.PhysicalHostBudget.TryOpenScope(NavigationPreparationKind.Solve,1,9999,out var comparison));
                            var reference=NavigationArrivalWork.CreateOwned(work.PhysicalInput,comparison.OwnedReservation());
                            typeof(NavigationArrivalWork).GetMethod("ReleaseSpatial",hidden).Invoke(reference,null);
                            typeof(NavigationArrivalWork).GetField("spatialPolicy",hidden).SetValue(reference,false);
                            typeof(NavigationArrivalWork).GetField("paidPagePolicy",hidden).SetValue(reference,false);
                            try{
                                for(long epoch=0;epoch<256&&(reference.Status==NavArrivalStatus.Pending||reference.Status==NavArrivalStatus.BudgetExhausted);epoch++)reference.Advance(epoch,10000,new NavEpochQuota(20000,20000,20000,20000,20000));
                                Assert.AreEqual(NavArrivalStatus.Ready,reference.Status);
                                CollectionAssert.AreEqual(reference.Proposals.Select(p=>p.Member.EntityId),work.Proposals.Select(p=>p.Member.EntityId));
                                CollectionAssert.AreEqual(reference.Proposals.Select(p=>p.Endpoint),work.Proposals.Select(p=>p.Endpoint),"exact frozen original50 placement endpoints");
                                TestContext.WriteLine("ARRIVAL_50_ORIGINAL_EXACT endpoints="+work.Proposals.Count+" referenceOccupancy="+reference.OccupancyWorkConsumed+" indexedOccupancy="+work.OccupancyWorkConsumed);
                            }finally{reference.ReleaseOwner();comparison.Discard();}

                        }
                        Assert.AreEqual(0,domain.Tick);
                    }finally{root?.Release();}
                }finally{kernel.PauseAtPhysicalBoundary=null;kernel.Clear();authority.Stop();domain.DestroyOwnedNavigation();Assert.Zero(nav.PhysicalHostBudget.PhysicalBytes);Assert.Zero(nav.PhysicalHostBudget.HeldBytes);Assert.Zero(nav.PhysicalHostBudget.ActiveScopes);}
            }
        }
        [Test]
        public void PerformanceGenesisRejectsAnOrdinaryHumanMatch()
        {
            var setup=new NativeLobbyConfiguration{AiTestMap=true};setup.InitializeParticipants();setup.Participants[0].Human=true;
            var profile=PlayableProfile.Create(PlayableProfile.Default.CopyData(),new AiTestMap());
            Assert.Throws<ArgumentException>(()=>PlayableRuntime.CreateEightPlayerPerformanceMatch(profile,1,setup,startPaused:true));
        }
    }
}
