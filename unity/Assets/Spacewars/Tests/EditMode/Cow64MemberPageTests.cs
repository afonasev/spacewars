using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed partial class NavigationPhysicalBodyTests
    {
        private static NavOwnedStorage CowMemberRoot(NavigationAuthorityGroup g)=>(NavOwnedStorage)typeof(NavigationAuthorityGroup).GetField("memberRoot",CostFlags).GetValue(g);
        private static int CowPageSlots(NavigationAuthorityGroup g)=>((NavPagedList<NavOwnedStorage>)CowMemberRoot(g)).PageSize;
        private static NavigationAuthorityGroup CowDecode(byte[] body,INavStorageReservation reservation)
        {
            using(var stream=new MemoryStream(body,false))using(var reader=new BinaryReader(stream)){
                var root=(NavigationAuthorityGroup)NavPhysicalBodyWire.ReadFrom(reader,reservation,NavigationHostBodyExtension.Instance);
                Assert.AreEqual(stream.Length,stream.Position);return root;
            }
        }
        private static NavigationAuthorityGroup CowSource(NavigationRetentionBudget budget,int count)
        {
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Reconstruction,1,1,out var scope));
            var members=new NavigationAuthorityMember[count];NavRetainedArray<NavOwnedStorage> array=null;NavigationAuthorityGroup root=null;bool done=false;
            try{
                var r=scope.OwnedReservation();
                for(int i=0;i<count;i++)members[i]=NavigationAuthorityMember.CopyOwned(new GroupMemberState{Entity=i+1,Slot=i,Incarnation=1,OrderRevision=1,MobilityRevision=1,Phase=GroupMemberPhase.Allocating,ContactPhase="PostMovementVisibility"},r);
                array=NavRetainedArray<NavOwnedStorage>.Copy(members.Cast<NavOwnedStorage>().ToArray(),r.TryReserve);
                root=NavigationAuthorityGroup.CopyNamesOwned(new GroupOrderState{WorldGeneration=1,GroupId=1,OrderRevision=1,CommandSequence=1,MembershipRevision=1,OwnerId="player-1",Origin=PlayableOrderOrigin.Human,Kind=PlayableCommandKind.Move},array,r);
                scope.Attach(root);scope.Commit(root,_=>{});done=true;return root;
            }finally{if(!done)root?.ReleaseStorage();array?.Release();foreach(var m in members)m?.ReleaseStorage();scope.Discard();}
        }
        private static NavigationAuthorityGroup CowWrite(NavigationRetentionBudget budget,NavigationAuthorityGroup source)
        {
            if(!budget.TryOpenScope(NavigationPreparationKind.Projection,1,2,out var scope))throw new InvalidOperationException("COW test scope refused.");
            NavigationAuthorityMember member=null;NavigationAuthorityGroup next=null;bool done=false;
            try{
                var old=(NavigationAuthorityMember)source.Members[0];var d=old.Data;
                var dto=new GroupMemberState{Entity=d.Entity,Slot=d.Slot,Incarnation=d.Incarnation,OrderRevision=d.OrderRevision,MobilityRevision=d.MobilityRevision,Phase=d.Phase,Goal=d.Goal,GoalLocation=old.GoalLocation,HasGoal=d.HasGoal,FormationReleased=d.FormationReleased,Branch=d.Branch,Reservation=d.Reservation,ContactTick=d.ContactTick+1,ContactPhase=old.ContactPhase,ProgressRequest=d.ProgressRequest,Progress=d.Progress,ProgressVersion=d.ProgressVersion,ProgressOffset=d.ProgressOffset};
                member=NavigationAuthorityMember.CopyOwned(dto,scope.OwnedReservation());next=NavigationAuthorityGroup.CopyWithMemberAndMarchOwned(source,0,member,source.Data,scope.OwnedReservation());
                scope.Attach(next);scope.Commit(next,_=>{});done=true;return next;
            }finally{if(!done)next?.ReleaseStorage();member?.ReleaseStorage();scope.Discard();}
        }
        [Test] public void Cow64BodyOwnershipRefusalAndSaveRestore()
        {
            Assert.False(ContactPublishProfile.Enabled);Assert.False(ContactPublishProfile.LegacyMemberPagesForQa);
            var coverage=new List<string>();
            foreach(var shape in new[]{"49-array","50-array","51-array","50-steady64","50-old256"}){
                int count=int.Parse(shape.Substring(0,2),CultureInfo.InvariantCulture);var budget=new NavigationRetentionBudget();NavigationAuthorityGroup source=null,next=null;
                try{
                    source=CowSource(budget,count);
                    if(shape.EndsWith("64")||shape.EndsWith("256")){
                        ContactPublishProfile.LegacyMemberPagesForQa=shape.EndsWith("256");var seeded=CowWrite(budget,source);source.ReleaseStorage();source=seeded;ContactPublishProfile.LegacyMemberPagesForQa=false;
                        Assert.AreEqual(shape.EndsWith("256")?256:64,CowPageSlots(source));
                    }
                    byte[] oldBytes=CaptureGraph(budget,source);long baseline=budget.PhysicalBytes;int refs=source.References;
                    var requests=new List<long>();budget.AllocationAdmission=bytes=>{requests.Add(bytes);return true;};next=CowWrite(budget,source);budget.AllocationAdmission=null;
                    Assert.AreEqual(count==50?64:256,CowPageSlots(next));Assert.AreEqual(source.VersionIdentity,next.MemberParentIdentity);Assert.AreEqual(0,next.ChangedMemberIndex);Assert.True(source.Data.BitwiseSame(next.Data));
                    for(int i=1;i<count;i++)Assert.AreSame(source.Members[i],next.Members[i],"Unchanged member owner aliases must survive.");
                    var oldMember=(NavigationAuthorityMember)source.Members[0];var newMember=(NavigationAuthorityMember)next.Members[0];Assert.AreNotSame(oldMember,newMember);Assert.AreNotSame(oldMember.ContactOwner,newMember.ContactOwner);Assert.AreEqual(oldMember.Data.ContactTick+1,newMember.Data.ContactTick);
                    Assert.AreSame(source.OwnerIdOwner,next.OwnerIdOwner);CollectionAssert.AreEqual(oldBytes,CaptureGraph(budget,source),"Pinned source BODY stays exact.");Assert.AreEqual(refs,source.References);
                    Assert.Contains(count==50?624L:2160L,requests);if(count==50)Assert.False(requests.Contains(2160L));
                    var body=CaptureGraph(budget,next);var cold=new NavigationRetentionBudget();Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var decode));NavigationAuthorityGroup restored=null,continued=null,hotContinued=null;
                    try{
                        restored=CowDecode(body,decode.OwnedReservation());decode.Attach(restored);decode.Commit(restored,_=>{});
                        Assert.AreEqual(CowPageSlots(next),CowPageSlots(restored));CollectionAssert.AreEqual(body,CaptureGraph(cold,restored));Assert.AreEqual(Unique(restored),cold.PhysicalBytes);
                        hotContinued=CowWrite(budget,next);continued=CowWrite(cold,restored);CollectionAssert.AreEqual(CaptureGraph(budget,hotContinued),CaptureGraph(cold,continued),"Hot/cold next COW BODY exact.");
                    }finally{hotContinued?.ReleaseStorage();continued?.ReleaseStorage();restored?.ReleaseStorage();decode.Discard();Assert.Zero(cold.PhysicalBytes);Assert.Zero(cold.HeldBytes);Assert.Zero(cold.ActiveScopes);}
                    if(shape=="50-array"){
                        var probeBudget=new NavigationRetentionBudget();Assert.True(probeBudget.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var probe));
                        var counter=new Refusal(probe,int.MaxValue);var decoded=CowDecode(body,counter);probe.Attach(decoded);probe.Commit(decoded,_=>{});int allocations=counter.Calls;decoded.ReleaseStorage();probe.Discard();Assert.Zero(probeBudget.PhysicalBytes);Assert.Zero(probeBudget.HeldBytes);Assert.Zero(probeBudget.ActiveScopes);
                        for(int nth=1;nth<=allocations;nth++){
                            var rejectedBudget=new NavigationRetentionBudget();Assert.True(rejectedBudget.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var rejected));var rejecting=new Refusal(rejected,nth);
                            try{Assert.Throws<InvalidOperationException>(()=>CowDecode(body,rejecting));Assert.AreEqual(nth,rejecting.Calls);}finally{rejected.Discard();Assert.Zero(rejectedBudget.PhysicalBytes);Assert.Zero(rejectedBudget.HeldBytes);Assert.Zero(rejectedBudget.ActiveScopes);}
                        }
                        coverage.Add("50-body-decode,"+allocations+","+body.Length+",0");TestContext.WriteLine($"COW64_DECODE all_nth={allocations} zero_release=true");
                    }
                    next.ReleaseStorage();next=null;Assert.AreEqual(baseline,budget.PhysicalBytes);
                    for(int nth=1;nth<=requests.Count;nth++){
                        int seen=0;budget.AllocationAdmission=bytes=>{Assert.AreEqual(requests[seen],bytes);return ++seen!=nth;};
                        try{Assert.Throws<InvalidOperationException>(()=>CowWrite(budget,source));}finally{budget.AllocationAdmission=null;}
                        Assert.AreEqual(nth,seen);Assert.AreEqual(baseline,budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);Assert.AreEqual(refs,source.References);CollectionAssert.AreEqual(oldBytes,CaptureGraph(budget,source));
                    }
                    coverage.Add(shape+","+requests.Count+","+body.Length+","+baseline);TestContext.WriteLine($"COW64_OWNERSHIP shape={shape} all_nth={requests.Count} body={body.Length} source_exact=true");
                }finally{ContactPublishProfile.LegacyMemberPagesForQa=false;budget.AllocationAdmission=null;next?.ReleaseStorage();source?.ReleaseStorage();Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);}
            }
            // The performance genesis intentionally starts at149 population per owner.
            // Use a fresh, explicitly bound legal producer for world restore; keep the
            // original100-cap profile unchanged in the timed performance control below.
            var restoreData=PlayableProfile.Default.CopyData();restoreData.armyCapacity=200;
            var profile=PlayableProfile.Create(restoreData,new AiTestMap());Assert.AreEqual(200,profile.ArmyCapacity);
            var setup=new NativeLobbyConfiguration{AiTestMap=true,HasExplicitSeed=true,ExplicitSeed=7};setup.InitializeParticipants();
            using(var runtime=PlayableRuntime.CreateEightPlayerPerformanceMatch(profile,1,setup,startPaused:true)){
                var config=(OfflineMatchConfiguration)typeof(PlayableRuntime).GetField("offlineConfiguration",CostFlags).GetValue(runtime);var views=runtime.SpectatorFrame(PlayableRuntime.LocalSpectatorId).Players.OrderBy(v=>(int)v.Owner).ToArray();
                foreach(bool legacy in new[]{true,false}){
                    ContactPublishProfile.LegacyMemberPagesForQa=legacy;
                    using(var s=new ContactCostSource(config,views,profile)){
                        s.Type.GetProperty("Tick",CostFlags).SetValue(s.Domain,1L);foreach(var q in s.Queries)s.Nav.SetFormationContact(q.Entity,q.Contact,1);
                        foreach(var g in s.Queries.Select(q=>q.Owner).Distinct())Assert.AreEqual(legacy?256:64,CowPageSlots(g.ImmutableVersion));
                        var saved=s.Save();var cold=PlayableDomain.RestoreConfiguredWorldBytes(saved,profile,7,"contact-publish-cost",config);var coldBudget=cold.Navigation.PhysicalHostBudget;
                        try{
                            // Old256 BODY remains256 at decode; no eager migration.
                            CollectionAssert.AreEqual(saved,cold.CaptureWorldBytes(7,"contact-publish-cost"));foreach(var g in cold.Navigation.GroupOrders){var slots=(NavigationCommandSlots<long,GroupOrderState>)typeof(NavigationSession).GetField("groups",CostFlags).GetValue(cold.Navigation);Assert.AreEqual(legacy?256:64,CowPageSlots(((NavigationLiveGroup)slots.OwnerFor(g.GroupId)).ImmutableVersion));}
                            ContactPublishProfile.LegacyMemberPagesForQa=false;
                            // Discover the actual publisher credits on an independent restored
                            // probe, including the carrier allocated after the new group exists.
                            var probeWorld=PlayableDomain.RestoreConfiguredWorldBytes(saved,profile,7,"contact-publish-cost",config);var probeBudget=probeWorld.Navigation.PhysicalHostBudget;var publisherCredits=new List<long>();
                            try{probeBudget.AllocationAdmission=bytes=>{publisherCredits.Add(bytes);return true;};probeWorld.Navigation.SetFormationContact(s.Queries[0].Entity,s.Queries[0].Contact,2);}
                            finally{probeBudget.AllocationAdmission=null;probeWorld.DestroyOwnedNavigation();Assert.Zero(probeBudget.PhysicalBytes);Assert.Zero(probeBudget.HeldBytes);Assert.Zero(probeBudget.ActiveScopes);}
                            byte[] refusalSource=s.Save();long refusalOperation=s.Operation,refusalPhysical=s.Budget.PhysicalBytes;var sourceOwners=s.Queries.Select(q=>q.Owner).Distinct().ToArray();var sourceVersions=sourceOwners.Select(o=>o.ImmutableVersion).ToArray();var sourceGenerations=sourceOwners.Select(o=>(long)CostGeneration.GetValue(o)).ToArray();
                            for(int nth=1;nth<=publisherCredits.Count;nth++){
                                int seen=0;s.Budget.AllocationAdmission=bytes=>{Assert.AreEqual(publisherCredits[seen],bytes);return ++seen!=nth;};
                                try{s.Nav.SetFormationContact(s.Queries[0].Entity,s.Queries[0].Contact,2);}finally{s.Budget.AllocationAdmission=null;}
                                Assert.AreEqual(nth,seen);Assert.AreEqual(refusalOperation,s.Operation);Assert.AreEqual(refusalPhysical,s.Budget.PhysicalBytes);Assert.Zero(s.Budget.HeldBytes);Assert.Zero(s.Budget.ActiveScopes);
                                for(int g=0;g<sourceOwners.Length;g++){Assert.AreSame(sourceVersions[g],sourceOwners[g].ImmutableVersion);Assert.AreEqual(sourceGenerations[g],(long)CostGeneration.GetValue(sourceOwners[g]));}
                            }
                            CollectionAssert.AreEqual(refusalSource,s.Save());Assert.AreEqual(refusalOperation+1,s.Operation);coverage.Add((legacy?"50-publisher-old256":"50-publisher-steady64")+","+publisherCredits.Count+","+refusalSource.Length+","+refusalPhysical);TestContext.WriteLine($"COW64_PUBLISHER source_slots={(legacy?256:64)} all_nth={publisherCredits.Count} source_exact=true zero_temporary=true");
                            s.Type.GetProperty("Tick",CostFlags).SetValue(s.Domain,2L);s.Type.GetProperty("Tick",CostFlags).SetValue(cold,2L);
                            long operation=s.Operation,coldOperation=(long)CostOperation.GetValue(cold.Navigation);
                            foreach(var q in s.Queries){s.Nav.SetFormationContact(q.Entity,q.Contact,2);cold.Navigation.SetFormationContact(q.Entity,q.Contact,2);}
                            Assert.AreEqual(operation+400,s.Operation);Assert.AreEqual(coldOperation+400,(long)CostOperation.GetValue(cold.Navigation));
                            foreach(var q in s.Queries)Assert.AreEqual(64,CowPageSlots(q.Owner.ImmutableVersion));
                            CollectionAssert.AreEqual(s.Save(),cold.CaptureWorldBytes(7,"contact-publish-cost"),"Full400 world current-format restore and next400 publications exact.");
                            TestContext.WriteLine($"COW64_WORLD restore_source_page={(legacy?256:64)} save={saved.Length} next400_exact=true no_gameplay=true");
                        }finally{cold.DestroyOwnedNavigation();Assert.Zero(coldBudget.PhysicalBytes);Assert.Zero(coldBudget.HeldBytes);Assert.Zero(coldBudget.ActiveScopes);}
                    }
                }
            }
            string output=Environment.GetEnvironmentVariable("SPACEWARS_COW64_OUTPUT");if(output!=null){Directory.CreateDirectory(output);File.WriteAllLines(Path.Combine(output,"refusal-coverage.csv"),new[]{"shape,all_nth_points,body_bytes,source_physical_bytes"}.Concat(coverage));}
            Assert.False(ContactPublishProfile.LegacyMemberPagesForQa);
        }
        private sealed class Cow64Arm
        {
            internal byte[] Initial,Full,Domain;internal GroupOrderState[] Groups;internal long[] Credits,Identities;internal int[] Order;
            internal long Physical,Operation;internal double Wall,Cpu;
        }
        [Test] public void Cow64Actual400PredecessorCandidatePredecessor()
        {
            Assert.True(PerformanceProfile.Enabled);Assert.False(ContactPublishProfile.Enabled);Assert.False(ContactPublishProfile.LegacyMemberPagesForQa);
            var profile=PlayableProfile.Create(PlayableProfile.Default.CopyData(),new AiTestMap());var setup=new NativeLobbyConfiguration{AiTestMap=true,HasExplicitSeed=true,ExplicitSeed=7};setup.InitializeParticipants();
            string output=Environment.GetEnvironmentVariable("SPACEWARS_COW64_OUTPUT");if(output!=null)Directory.CreateDirectory(output);
            using(var runtime=PlayableRuntime.CreateEightPlayerPerformanceMatch(profile,1,setup,startPaused:true)){
                var config=(OfflineMatchConfiguration)typeof(PlayableRuntime).GetField("offlineConfiguration",CostFlags).GetValue(runtime);var views=runtime.SpectatorFrame(PlayableRuntime.LocalSpectatorId).Players.OrderBy(v=>(int)v.Owner).ToArray();
                Cow64Arm Arm(string name,bool legacy){
                    ContactPublishProfile.LegacyMemberPagesForQa=legacy;
                    using(var s=new ContactCostSource(config,views,profile)){
                        var a=new Cow64Arm{Initial=s.Initial,Order=s.Queries.Select(q=>q.Entity).ToArray(),Identities=new long[1600*7]};long operation=s.Operation,versionBase=CostVersionSequence();var credits=new long[200000];int n=0,published=0;
                        s.Budget.AllocationAdmission=bytes=>{if(n==credits.Length)throw new InvalidOperationException("Fixed credit observer overflow.");credits[n++]=bytes;return true;};
                        using(PerformanceProfile.Measure(ProfileStage.ContactPublish)){}var before=ContactCostSnapshot.Read();
                        for(int tick=1;tick<=4;tick++){
                            s.Type.GetProperty("Tick",CostFlags).SetValue(s.Domain,(long)tick);
                            foreach(var q in s.Queries){long parent=q.Owner.ImmutableVersion.VersionIdentity,generation=(long)CostGeneration.GetValue(q.Owner);
                                using(PerformanceProfile.Measure(ProfileStage.ContactPublish))s.Nav.SetFormationContact(q.Entity,q.Contact,tick);
                                var v=q.Owner.ImmutableVersion;Assert.AreEqual(operation+published+1,s.Operation);Assert.AreEqual(generation+1,(long)CostGeneration.GetValue(q.Owner));Assert.AreEqual(parent,v.MemberParentIdentity);Assert.AreEqual(versionBase+published+1,v.VersionIdentity);Assert.AreEqual(legacy?256:64,CowPageSlots(v));
                                int at=published++*7;a.Identities[at]=tick;a.Identities[at+1]=q.Entity;a.Identities[at+2]=s.Operation;a.Identities[at+3]=generation+1;a.Identities[at+4]=v.VersionIdentity-versionBase;a.Identities[at+5]=v.MemberParentIdentity-versionBase;a.Identities[at+6]=q.Contact?1:0;
                            }
                        }
                        var after=ContactCostSnapshot.Read();s.Budget.AllocationAdmission=null;Assert.AreEqual(1600,after.Count[(int)ProfileStage.ContactPublish]-before.Count[(int)ProfileStage.ContactPublish]);
                        int stage=(int)ProfileStage.ContactPublish;a.Wall=(after.Wall[stage]-before.Wall[stage])*1000d/Stopwatch.Frequency;a.Cpu=(after.Cpu[stage]-before.Cpu[stage])/1000000d;a.Credits=credits.Take(n).ToArray();a.Operation=s.Operation;a.Physical=s.Budget.PhysicalBytes;a.Groups=s.Nav.GroupOrders.ToArray();
                        using(var world=((PlayableDomain)s.Domain).CaptureWorldState(7,"contact-publish-cost")){a.Domain=(byte[])world.Domain.Clone();a.Full=world.Encode();}
                        Assert.AreEqual(operation+1601,s.Operation,"1600 publications plus one existing save observer.");Assert.AreEqual(a.Physical,s.Budget.PhysicalBytes);Assert.Zero(s.Budget.HeldBytes);Assert.Zero(s.Budget.ActiveScopes);Assert.AreEqual(0d,(double)s.Type.GetField("elapsed",CostFlags).GetValue(s.Domain));
                        if(output!=null){File.WriteAllBytes(Path.Combine(output,name+".save"),a.Full);File.WriteAllBytes(Path.Combine(output,name+".domain"),a.Domain);File.WriteAllLines(Path.Combine(output,name+".credits"),a.Credits.Select(x=>x.ToString(CultureInfo.InvariantCulture)));File.WriteAllLines(Path.Combine(output,name+".identities"),a.Identities.Select(x=>x.ToString(CultureInfo.InvariantCulture)));}
                        TestContext.WriteLine(string.Format(CultureInfo.InvariantCulture,"COW64_ARM {0} slots={1} publications=1600 wall_ms={2:R} cpu_ms={3:R} operation={4} save={5} physical={6} credits={7}",name,legacy?256:64,a.Wall,a.Cpu,a.Operation,a.Full.Length,a.Physical,a.Credits.Length));return a;
                    }
                }
                var a1=Arm("A1",true);var b=Arm("B",false);var a2=Arm("A2",true);
                CollectionAssert.AreEqual(a1.Full,a2.Full);CollectionAssert.AreEqual(a1.Credits,a2.Credits);Assert.AreEqual(a1.Physical,a2.Physical);
                foreach(var a in new[]{b,a2}){
                    CollectionAssert.AreEqual(a1.Initial,a.Initial);CollectionAssert.AreEqual(a1.Domain,a.Domain);CollectionAssert.AreEqual(a1.Order,a.Order);CollectionAssert.AreEqual(a1.Identities,a.Identities);Assert.AreEqual(a1.Operation,a.Operation);
                    for(int g=0;g<8;g++){Assert.True(new NavigationAuthorityGroup.Values(a1.Groups[g]).BitwiseSame(new NavigationAuthorityGroup.Values(a.Groups[g])));Assert.AreEqual(a1.Groups[g].OwnerId,a.Groups[g].OwnerId);Assert.AreEqual(a1.Groups[g].Source,a.Groups[g].Source);Assert.AreEqual(a1.Groups[g].OriginalLocation?.SurfaceId,a.Groups[g].OriginalLocation?.SurfaceId);
                        for(int m=0;m<50;m++){var x=a1.Groups[g].Members[m];var y=a.Groups[g].Members[m];Assert.True(new NavigationAuthorityMember.Values(x).BitwiseSame(new NavigationAuthorityMember.Values(y)));Assert.AreEqual(x.ContactPhase,y.ContactPhase);Assert.AreEqual(x.GoalLocation?.SurfaceId,y.GoalLocation?.SurfaceId);}}
                }
                Assert.False(a1.Full.SequenceEqual(b.Full),"Approved physical BODY change must be visible.");Assert.Less(b.Full.Length,a1.Full.Length);Assert.Less(b.Physical,a1.Physical);Assert.AreEqual(a1.Credits.Length,b.Credits.Length);
                for(int i=0;i<a1.Credits.Length;i++)Assert.AreEqual(a1.Credits[i]==2160?624:a1.Credits[i],b.Credits[i],"Only page-size credits change on fresh fixed input.");
                if(output!=null)File.WriteAllLines(Path.Combine(output,"metrics.csv"),new[]{"arm,slots,wall_ms,cpu_ms,physical,save_bytes,credits","A1,256,"+a1.Wall.ToString("R",CultureInfo.InvariantCulture)+","+a1.Cpu.ToString("R",CultureInfo.InvariantCulture)+","+a1.Physical+","+a1.Full.Length+","+a1.Credits.Length,"B,64,"+b.Wall.ToString("R",CultureInfo.InvariantCulture)+","+b.Cpu.ToString("R",CultureInfo.InvariantCulture)+","+b.Physical+","+b.Full.Length+","+b.Credits.Length,"A2,256,"+a2.Wall.ToString("R",CultureInfo.InvariantCulture)+","+a2.Cpu.ToString("R",CultureInfo.InvariantCulture)+","+a2.Physical+","+a2.Full.Length+","+a2.Credits.Length});
                TestContext.WriteLine(string.Format(CultureInfo.InvariantCulture,"COW64_ABA B_over_A1_wall={0:R} B_over_A2_wall={1:R} B_over_A1_cpu={2:R} B_over_A2_cpu={3:R}",b.Wall/a1.Wall,b.Wall/a2.Wall,b.Cpu/a1.Cpu,b.Cpu/a2.Cpu));
            }
            Assert.False(ContactPublishProfile.LegacyMemberPagesForQa);
        }
    }
}
