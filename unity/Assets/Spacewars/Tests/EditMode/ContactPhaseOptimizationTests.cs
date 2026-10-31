using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    public sealed partial class NavigationPhysicalBodyTests
    {
        static void Contact30Source(bool valid,Action<OfflineMatchConfiguration,PlayableSnapshot[],PlayableProfile> action)
        {
            var data=PlayableProfile.Default.CopyData();if(valid)data.armyCapacity=200;
            var profile=PlayableProfile.Create(data,new AiTestMap());var setup=new NativeLobbyConfiguration{AiTestMap=true,HasExplicitSeed=true,ExplicitSeed=7};setup.InitializeParticipants();
            using(var runtime=PlayableRuntime.CreateEightPlayerPerformanceMatch(profile,1,setup,startPaused:true)){
                var config=(OfflineMatchConfiguration)typeof(PlayableRuntime).GetField("offlineConfiguration",CostFlags).GetValue(runtime);
                action(config,runtime.SpectatorFrame(PlayableRuntime.LocalSpectatorId).Players.OrderBy(v=>(int)v.Owner).ToArray(),profile);
            }
        }
        static NavigationAdmissionStorage Contact30Admission(NavigationSession nav)=>(NavigationAdmissionStorage)typeof(NavigationSession).GetField("admissionOwner",CostFlags).GetValue(nav);
        static void Contact30Phase(ContactCostSource s,int tick){s.Type.GetProperty("Tick",CostFlags).SetValue(s.Domain,(long)tick);s.Type.GetMethod("RefreshFormationContacts",CostFlags).Invoke(s.Domain,null);}
        [TestCase("group")]
        [TestCase("member")]
        [TestCase("profiled")]
        public void ContactIngressDefersUntilHostOwnsPendingThenPublishesAndMoves(string path)
        {
            var geometry=new NavGeometry(20,Array.Empty<NavObstacle>(),1);
            var nav=new NavigationSession(1,geometry,PlayableProfile.Default.Navigation);
            var budget=nav.PhysicalHostBudget;bool profiled=ContactPublishProfile.Enabled;
            try{
                nav.Crowd.Add(1,new NavPoint(-5,0));
                Assert.True(nav.MoveGroup(new[]{1},new[]{new NavPoint(4,0)}));
                // Real compatibility ingress, nested exactly as ordinary movement
                // can enqueue a request before the host registers its owned copy.
                var batch=typeof(NavigationSession).GetMethod("AdmissionBatch",CostFlags,null,new[]{typeof(Action)},null);
                batch.Invoke(nav,new object[]{(Action)(()=>Assert.True(nav.Move(1,new NavPoint(5,0))))});
                var raw=nav.PendingRequestFor(1);Assert.NotNull(raw);Assert.Null(raw.StorageOwner);
                long group=nav.GroupFor(1).GroupId;long contact=nav.GroupFor(1).Members[0].ContactTick;
                ContactPublishProfile.Enabled=path=="profiled";
                if(path=="group")Assert.False(nav.TryOwnedGroupContacts(group,_=>false,7));
                else nav.SetFormationContact(1,false,7);
                Assert.AreEqual(contact,nav.GroupFor(1).Members[0].ContactTick,"Raw ingress defers the owned contact write");
                Assert.AreSame(raw,nav.PendingRequestFor(1));Assert.Null(raw.StorageOwner,"Never forge ownership on raw ingress");
                nav.Step(1.0/30); // Raw ingress must also survive the ordinary pre-host movement phase.
                using(var host=new Spacewars.Presentation.UnityHostRouteService()){
                    for(int i=0;i<100&&nav.PendingCount>0;i++){
                        host.Service(new PlayableRouteBinding(geometry,PlayableProfile.Default,nav.Admission,nav.RoutePort),4);
                        nav.ApplyResults();
                    }
                    Assert.Zero(nav.PendingCount,"Actual host must register and complete ingress");
                    Assert.Greater(nav.AppliedResults,0);Assert.Null(raw.StorageOwner);
                    if(path=="group")Assert.True(nav.TryOwnedGroupContacts(group,_=>false,8));
                    else nav.SetFormationContact(1,false,8);
                    Assert.AreEqual(8,nav.GroupFor(1).Members[0].ContactTick);
                    Assert.NotNull(Contact30Admission(nav));
                    for(int i=0;i<60;i++)nav.Step(1.0/30);
                    Assert.Greater(nav.Crowd.Units.Single().Position.X,-4,"Contact admission must preserve physical progress");
                }
            }finally{
                ContactPublishProfile.Enabled=profiled;nav.ReleaseCommittedHostRoot();nav.DestroyNavigationAuthority();
                Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
            }
        }
        [Test] public void Contact30PublishedRootProjectionProbeMembershipAndStaleCancellation()
        {
            ContactPhaseQa.Reset();
            try{
                // Explicit endpoints produce real pending requests immediately. The
                // ordinary400 common-goal genesis only has allocating members so far.
                var pendingNav=new NavigationSession(1,new NavGeometry(20,Array.Empty<NavObstacle>(),1),PlayableProfile.Default.Navigation);
                var pendingBudget=pendingNav.PhysicalHostBudget;NavigationAdmissionStorage pinned=null;
                try{
                    pendingNav.Crowd.Add(1,new NavPoint(-5,-1));pendingNav.Crowd.Add(2,new NavPoint(-5,1));
                    Assert.True(pendingNav.MoveGroup(new[]{1,2},new[]{new NavPoint(5,-1),new NavPoint(5,1)}));
                    pinned=Contact30Admission(pendingNav);pinned.Retain();Assert.AreEqual(2,pinned.Requests.Count);
                    var slots=(NavigationCommandSlots<long,GroupOrderState>)typeof(NavigationSession).GetField("groups",CostFlags).GetValue(pendingNav);
                    var live=(NavigationLiveGroup)slots.OwnerFor(pendingNav.GroupFor(1).GroupId);var version=live.ImmutableVersion;
                    var group=(NavigationAdmissionGroupStorage)pinned.Groups[0];Assert.True(group.MatchesPublishedReplacement(version));Assert.True(group.MatchesProjection(version,pinned.Requests));
                    foreach(var member in group.Value.Members){
                        Assert.NotNull(member.PendingRequest);var changed=pinned.Requests.Where(r=>((NavigationHostRequest)r).Request.Entity!=member.Entity).ToArray();
                        Assert.False(group.MatchesProjection(version,changed),"Changed request root must be fully checked");Assert.True(group.MatchesPublishedReplacement(version),"Pinned member remains bound to pinned request root");
                    }
                    Assert.NotNull(pendingNav.Probe(100,1,PlayableProfile.Default.Navigation,new NavPoint(-5,0),new NavPoint(5,0)));
                    Assert.AreEqual(3,Contact30Admission(pendingNav).Requests.Count);Assert.AreEqual(2,pinned.Requests.Count);
                    Assert.True(Contact30Admission(pendingNav).MatchesGroupReplacement(version));Assert.True(pinned.MatchesGroupReplacement(version));
                    pendingNav.CancelProbe(100);Assert.AreEqual(2,Contact30Admission(pendingNav).Requests.Count);
                    pendingNav.Stop(1,false);
                    var remaining=pendingNav.GroupFor(2);var latestSlots=(NavigationCommandSlots<long,GroupOrderState>)typeof(NavigationSession).GetField("groups",CostFlags).GetValue(pendingNav);
                    var latest=(NavigationLiveGroup)latestSlots.OwnerFor(remaining.GroupId);
                    Assert.False(pinned.MatchesGroupReplacement(latest.ImmutableVersion),"Use the current owned replacement, never a retired owner's released version");
                    Assert.AreEqual(2,pinned.Requests.Count);Assert.True(group.Value.Members.All(m=>m.PendingRequest!=null));
                }finally{pinned?.Release();pendingNav.ReleaseCommittedHostRoot();pendingNav.DestroyNavigationAuthority();Assert.Zero(pendingBudget.PhysicalBytes);Assert.Zero(pendingBudget.HeldBytes);Assert.Zero(pendingBudget.ActiveScopes);}
                Contact30Source(true,(config,views,profile)=>{
                using(var s=new ContactCostSource(config,views,profile)){
                    var root=Contact30Admission(s.Nav);root.Retain();
                    try{
                        foreach(var owner in s.Queries.Select(q=>q.Owner).Distinct()){
                            var version=owner.ImmutableVersion;var group=root.Groups.Cast<NavigationAdmissionGroupStorage>().Single(g=>g.Value.GroupId==version.Data.GroupId);
                            Assert.True(group.MatchesProjection(version,root.Requests));Assert.True(group.MatchesPublishedReplacement(version));
                            Assert.True(group.Value.Members.All(m=>m.PendingRequest==null),"Fresh ordinary400 genesis is still allocating, not a fabricated request fixture.");
                        }
                        int oldRequests=root.Requests.Count;var q=s.Queries[0];Assert.NotNull(s.Nav.Probe(100,1,profile.Navigation,q.Owner.ImmutableVersion.Data.OriginalGoal,new NavPoint(0,0)));
                        var withProbe=Contact30Admission(s.Nav);Assert.AreEqual(oldRequests+1,withProbe.Requests.Count);Assert.AreEqual(oldRequests,root.Requests.Count);
                        foreach(var owner in s.Queries.Select(x=>x.Owner).Distinct())Assert.AreEqual(root.MatchesGroupReplacement(owner.ImmutableVersion),withProbe.MatchesGroupReplacement(owner.ImmutableVersion));
                        s.Nav.CancelProbe(100);Assert.AreEqual(oldRequests,Contact30Admission(s.Nav).Requests.Count);
                        var current=q.Owner.ImmutableVersion;var published=root.Groups.Cast<NavigationAdmissionGroupStorage>().Single(g=>g.Value.GroupId==current.Data.GroupId);
                        Assert.True(s.Budget.TryOpenScope(NavigationPreparationKind.Admission,1,0,out var scope));
                        using(scope){
                            var input=current.Members.ToArray();var swap=input[0];input[0]=input[1];input[1]=swap;
                            var array=NavRetainedArray<NavOwnedStorage>.Copy(input,scope.OwnedReservation().TryReserve);
                            try{Assert.Throws<ArgumentException>(()=>NavigationAuthorityGroup.CreateOwned(current.Data,current.OwnerIdOwner,current.SourceOwner,current.OriginalSelector,array,scope.OwnedReservation()));}finally{array.Release();}
                            var dto=s.Nav.GroupOrders.First(g=>g.GroupId==current.Data.GroupId).CopyHeader();dto.MembershipRevision++;
                            var changed=NavigationAuthorityGroup.CopyWithValuesOwned(current,new NavigationAuthorityGroup.Values(dto),scope.OwnedReservation());
                            try{Assert.False(published.MatchesProjection(changed,root.Requests));Assert.False(published.MatchesPublishedReplacement(changed));}finally{changed.ReleaseStorage();}
                        }
                        // Prepared write guards still reject stale observations and cancelled scopes.
                        foreach(bool cancel in new[]{false,true}){
                            NavigationLiveGroupObservation observation=default;s.Nav.RunHostStep(()=>observation=q.Owner.ObserveForCopy(s.Nav.ObserveLiveWriteToken()));
                            try{
                                Assert.True(s.Budget.TryOpenScope(NavigationPreparationKind.Admission,1,0,out var prepare));using(prepare){
                                    var member=NavigationAuthorityMember.CopyOwned(s.Nav.GroupOrders.First(g=>g.GroupId==current.Data.GroupId).Members[0],prepare.OwnedReservation());
                                    var version=NavigationAuthorityGroup.CopyWithMemberOwned(observation.Version,0,member,prepare.OwnedReservation());member.ReleaseStorage();
                                    var write=NavigationPreparedGroupWrite.CreateOwned(observation,version,0,prepare);version.ReleaseStorage();prepare.Attach(write);
                                    try{
                                        if(cancel){prepare.RequestCancel();Assert.Throws<OperationCanceledException>(()=>prepare.Publish(write,_=>s.Nav.RunHostStep(()=>write.ValidateInstall(prepare,s.Nav.ObserveLiveWriteToken()))));}
                                        else{s.Nav.SetFormationContact(q.Entity,false,1);Assert.Throws<InvalidOperationException>(()=>s.Nav.RunHostStep(()=>write.ValidateInstall(prepare,s.Nav.ObserveLiveWriteToken())));}
                                    }finally{write.ReleaseStorage();}
                                }
                            }finally{observation.ReleasePins();}
                        }
                        Assert.Zero(s.Budget.HeldBytes);Assert.Zero(s.Budget.ActiveScopes);
                    }finally{root.Release();}
                }
            });}finally{ContactPhaseQa.Reset();}
        }
        [Test] public void Contact30ValidHotColdWholePhaseAndOrderedPublisherRefusals()
        {
            ContactPhaseQa.Reset();
            try{Contact30Source(true,(config,views,profile)=>{
                using(var s=new ContactCostSource(config,views,profile)){
                    var saved=s.Save();var cold=PlayableDomain.RestoreConfiguredWorldBytes(saved,profile,7,"contact-publish-cost",config);var cb=cold.Navigation.PhysicalHostBudget;
                    try{
                        CollectionAssert.AreEqual(saved,cold.CaptureWorldBytes(7,"contact-publish-cost"));
                        s.Type.GetMethod("RefreshVision",CostFlags).Invoke(s.Domain,null);s.Type.GetMethod("RefreshVision",CostFlags).Invoke(cold,null);
                        Contact30Phase(s,1);s.Type.GetProperty("Tick",CostFlags).SetValue(cold,1L);s.Type.GetMethod("RefreshFormationContacts",CostFlags).Invoke(cold,null);
                        CollectionAssert.AreEqual(s.Save(),cold.CaptureWorldBytes(7,"contact-publish-cost"));
                    }finally{cold.DestroyOwnedNavigation();Assert.Zero(cb.PhysicalBytes);Assert.Zero(cb.HeldBytes);Assert.Zero(cb.ActiveScopes);}
                }
                long[][] credits=new long[2][];long[][] versions=new long[2][];
                for(int mode=0;mode<2;mode++){
                    ContactPhaseQa.LegacyAdmission=mode==0;
                    using(var s=new ContactCostSource(config,views,profile)){
                        var list=new List<long>();s.Budget.AllocationAdmission=b=>{list.Add(b);return true;};var q=s.Queries[0];s.Nav.SetFormationContact(q.Entity,q.Contact,1);s.Budget.AllocationAdmission=null;credits[mode]=list.ToArray();
                    }
                    using(var s=new ContactCostSource(config,views,profile)){
                        var saved=s.Save();long op=s.Operation,physical=s.Budget.PhysicalBytes;var q=s.Queries[0];var version=q.Owner.ImmutableVersion;long gen=(long)CostGeneration.GetValue(q.Owner);versions[mode]=new long[credits[mode].Length];
                        for(int nth=1;nth<=credits[mode].Length;nth++){
                            int seen=0;long v=CostVersionSequence();s.Budget.AllocationAdmission=b=>{Assert.AreEqual(credits[mode][seen],b);return ++seen!=nth;};
                            try{s.Nav.SetFormationContact(q.Entity,q.Contact,1);}finally{s.Budget.AllocationAdmission=null;}
                            Assert.AreEqual(nth,seen);Assert.AreEqual(op,s.Operation);Assert.AreSame(version,q.Owner.ImmutableVersion);Assert.AreEqual(gen,(long)CostGeneration.GetValue(q.Owner));Assert.AreEqual(physical,s.Budget.PhysicalBytes);Assert.Zero(s.Budget.HeldBytes);Assert.Zero(s.Budget.ActiveScopes);versions[mode][nth-1]=CostVersionSequence()-v;
                        }
                        CollectionAssert.AreEqual(saved,s.Save());Assert.AreEqual(op+1,s.Operation);
                    }
                }
                CollectionAssert.AreEqual(credits[0],credits[1]);CollectionAssert.AreEqual(versions[0],versions[1]);TestContext.WriteLine("CONTACT30_REFUSAL allNth="+credits[0].Length+" orderedCreditsAndVersionConsumptionExact=true");
            });}finally{ContactPhaseQa.Reset();}
        }
        private sealed class Contact30Arm
        {
            internal byte[] Initial,Full,Domain;internal long[] Credits,Identity;internal long Operation,Physical;internal double HeapDelta;internal int Requests,Allocating;internal readonly List<string> Metrics=new List<string>();
        }
        [Test] public void Contact30WholePhaseFixed400PredecessorCandidatePredecessor()
        {
            Assert.True(PerformanceProfile.Enabled);Assert.False(ContactPublishProfile.Enabled);ContactPhaseQa.Reset();
            string output=Environment.GetEnvironmentVariable("SPACEWARS_CONTACT30_OUTPUT");if(output!=null)Directory.CreateDirectory(output);
            long allocBefore=GC.GetAllocatedBytesForCurrentThread();var positive=new byte[1048576];long allocPositive=GC.GetAllocatedBytesForCurrentThread()-allocBefore;positive[0]=1;
            try{Contact30Source(false,(config,views,profile)=>{
                Contact30Arm Arm(string name,bool predecessor){
                    ContactPhaseQa.Reset();ContactPhaseQa.FullScanDetection=ContactPhaseQa.LegacyAdmission=predecessor;
                    using(var s=new ContactCostSource(config,views,profile)){
                        var allocating=typeof(NavigationSession).GetField("allocating",CostFlags).GetValue(s.Nav);
                        var arm=new Contact30Arm{Initial=s.Initial,Identity=new long[1600*9],Requests=s.Nav.Admission.Requests.Count,Allocating=(int)allocating.GetType().GetProperty("Count",CostFlags).GetValue(allocating)};var credit=new long[200000];int n=0,pub=0;long versionBase=CostVersionSequence(),operation=s.Operation;var queries=s.Queries.ToDictionary(q=>q.Entity);var owners=s.Queries.Select(q=>q.Owner).Distinct().ToArray();var generations=owners.Select(o=>(long)CostGeneration.GetValue(o)).ToArray();var parents=owners.Select(o=>o.ImmutableVersion.VersionIdentity-versionBase).ToArray();
                        s.Budget.AllocationAdmission=bytes=>{if(n==credit.Length)throw new InvalidOperationException("Fixed allocation observer overflow");credit[n++]=bytes;return true;};
                        ContactPhaseQa.AfterPublish=(id,contact)=>{
                            if(pub==1600)throw new InvalidOperationException("Fixed identity observer overflow");var q=queries[id];var v=q.Owner.ImmutableVersion;int at=pub++*9;
                            arm.Identity[at]=id;arm.Identity[at+1]=contact?1:0;arm.Identity[at+2]=s.Operation;arm.Identity[at+3]=(long)CostGeneration.GetValue(q.Owner);arm.Identity[at+4]=v.VersionIdentity-versionBase;arm.Identity[at+5]=v.MemberParentIdentity-versionBase;arm.Identity[at+6]=v.ChangedMemberIndex;arm.Identity[at+7]=((NavigationAuthorityMember)v.Members[v.ChangedMemberIndex]).Data.ContactTick;arm.Identity[at+8]=q.Contact?1:0;
                        };
                        foreach(ProfileStage stage in Enum.GetValues(typeof(ProfileStage)))if(stage!=ProfileStage.Count)using(PerformanceProfile.Measure(stage)){}
                        ContactPublishProfile.Enabled=true;var before=ContactCostSnapshot.Read();long heap=GC.GetTotalMemory(false);
                        for(int tick=1;tick<=4;tick++)Contact30Phase(s,tick);
                        arm.HeapDelta=GC.GetTotalMemory(false)-heap;var after=ContactCostSnapshot.Read();ContactPublishProfile.Enabled=false;ContactPhaseQa.AfterPublish=null;s.Budget.AllocationAdmission=null;
                        Assert.AreEqual(1600,pub);Assert.AreEqual(operation+1600,s.Operation);Assert.Zero(s.Budget.HeldBytes);Assert.Zero(s.Budget.ActiveScopes);
                        for(int i=0;i<1600;i++){
                            int at=i*9;var q=s.Queries[i%400];int owner=Array.IndexOf(owners,q.Owner),index=Array.FindIndex(s.Nav.GroupOrders.Single(g=>g.GroupId==q.Owner.ImmutableVersion.Data.GroupId).Members,m=>m.Entity==q.Entity);
                            Assert.AreEqual(q.Entity,arm.Identity[at]);Assert.AreEqual(arm.Identity[at+8],arm.Identity[at+1]);Assert.AreEqual(operation+i+1,arm.Identity[at+2]);Assert.AreEqual(++generations[owner],arm.Identity[at+3]);Assert.AreEqual(parents[owner],arm.Identity[at+5]);parents[owner]=arm.Identity[at+4];Assert.AreEqual(i+1,arm.Identity[at+4]);Assert.AreEqual(index,arm.Identity[at+6]);Assert.AreEqual(i/400+1,arm.Identity[at+7]);

                        }
                        for(int i=0;i<owners.Length;i++)Assert.AreEqual(generations[i],(long)CostGeneration.GetValue(owners[i]));
                        arm.Operation=s.Operation;arm.Physical=s.Budget.PhysicalBytes;arm.Credits=credit.Take(n).ToArray();
                        using(var world=((PlayableDomain)s.Domain).CaptureWorldState(7,"contact-publish-cost")){arm.Domain=(byte[])world.Domain.Clone();arm.Full=world.Encode();}
                        Assert.AreEqual(operation+1601,s.Operation);Assert.AreEqual(arm.Physical,s.Budget.PhysicalBytes);Assert.AreEqual(0d,(double)s.Type.GetField("elapsed",CostFlags).GetValue(s.Domain));
                        foreach(var stage in new[]{ProfileStage.Contacts,ProfileStage.ContactDetect,ProfileStage.ContactPublish,ProfileStage.ContactAdmissionCheck,ProfileStage.ContactAdmissionPrepare}){
                            int k=(int)stage;long calls=after.Count[k]-before.Count[k];if(stage==ProfileStage.Contacts)Assert.AreEqual(4,calls);else Assert.AreEqual(1600,calls);
                            double wall=(after.Wall[k]-before.Wall[k])*1000d/Stopwatch.Frequency,cpu=(after.Cpu[k]-before.Cpu[k])/1000000d;arm.Metrics.Add(string.Format(CultureInfo.InvariantCulture,"{0},{1},{2},{3:R},{4:R}",name,stage,calls,wall,cpu));
                        }
                        if(output!=null){File.WriteAllBytes(Path.Combine(output,name+".save"),arm.Full);File.WriteAllBytes(Path.Combine(output,name+".domain"),arm.Domain);File.WriteAllLines(Path.Combine(output,name+".credits"),arm.Credits.Select(x=>x.ToString()));File.WriteAllLines(Path.Combine(output,name+".identity"),arm.Identity.Select(x=>x.ToString()));}
                        TestContext.WriteLine("CONTACT30_ARM "+name+" publications="+pub+" admission_request_root="+arm.Requests+" allocating="+arm.Allocating+" physical="+arm.Physical+" requests="+n+" retained_heap_delta_NOT_ALLOCATED_BYTES="+arm.HeapDelta);return arm;
                    }
                }
                var a1=Arm("A1",true);var b=Arm("B",false);var a2=Arm("A2",true);
                foreach(var arm in new[]{b,a2}){CollectionAssert.AreEqual(a1.Initial,arm.Initial);CollectionAssert.AreEqual(a1.Full,arm.Full);CollectionAssert.AreEqual(a1.Domain,arm.Domain);CollectionAssert.AreEqual(a1.Credits,arm.Credits);CollectionAssert.AreEqual(a1.Identity,arm.Identity);Assert.AreEqual(a1.Physical,arm.Physical);Assert.AreEqual(a1.Operation,arm.Operation);Assert.AreEqual(a1.Requests,arm.Requests);Assert.AreEqual(a1.Allocating,arm.Allocating);}
                if(output!=null){File.WriteAllLines(Path.Combine(output,"metrics.csv"),new[]{"arm,stage,calls,wall_ms,cpu_ms"}.Concat(a1.Metrics).Concat(b.Metrics).Concat(a2.Metrics));File.WriteAllText(Path.Combine(output,"managed-metric.txt"),"GC.GetAllocatedBytes positive1MiB control="+allocPositive+"; zero control means unavailable, never zero allocation. Derived index uses reusable managed dictionaries/arrays/list/sort. Retained heap delta is not allocated bytes.\n");}
            });}finally{ContactPublishProfile.Enabled=false;ContactPhaseQa.Reset();GC.KeepAlive(positive);}
        }
    }
}
