using System;
using System.Collections.Generic;
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
        // Negative contract control only. Neither arm publishes a replacement.
        [Test] public void ContactPhaseReuseChangesPinnedOwnershipAndRefusalOnActual400()
        {
            const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
            var profile=PlayableProfile.Create(PlayableProfile.Default.CopyData(),new AiTestMap());
            var setup=new NativeLobbyConfiguration{AiTestMap=true,HasExplicitSeed=true,ExplicitSeed=7};setup.InitializeParticipants();
            using(var runtime=PlayableRuntime.CreateEightPlayerPerformanceMatch(profile,1,setup,startPaused:true)){
                var config=(OfflineMatchConfiguration)typeof(PlayableRuntime).GetField("offlineConfiguration",flags).GetValue(runtime);
                var authority=new PlayableAuthorityTick(config,1);
                var domain=typeof(PlayableAuthorityTick).GetField("domain",flags).GetValue(authority);var type=domain.GetType();
                var nav=(NavigationSession)type.GetProperty("Navigation",flags).GetValue(domain);
                object Call(string name,params object[] args)=>type.GetMethod(name,flags).Invoke(domain,args);
                var budget=nav.PhysicalHostBudget;NavigationAuthorityMember[] source=null;
                try{
                    foreach(var view in runtime.SpectatorFrame(PlayableRuntime.LocalSpectatorId).Players.OrderBy(v=>(int)v.Owner))
                        Assert.AreEqual(PlayableCommandStatus.Applied,authority.Apply(PlayableRuntime.CreateEightPlayerMovingPerformanceCommand(view,PlayableRuntime.EightPlayerMovingPerformanceSequence(view.Owner,0))).Status);
                    Assert.AreEqual(400,nav.Crowd.Units.Count);Assert.AreEqual(8,nav.GroupOrders.Count);Assert.True(nav.GroupOrders.All(g=>g.Members.Length==50));
                    Call("RefreshVision");type.GetProperty("Tick",flags).SetValue(domain,1L);Call("RefreshFormationContacts");
                    var groups=nav.GroupOrders.ToArray();var dto=groups.SelectMany(g=>g.Members).ToArray();
                    Assert.AreEqual(400,dto.Length);Assert.True(dto.All(m=>m.ContactTick==1&&m.ContactPhase=="PostMovementVisibility"));
                    var slots=(NavigationCommandSlots<long,GroupOrderState>)typeof(NavigationSession).GetField("groups",flags).GetValue(nav);
                    var owners=groups.Select(g=>(NavigationLiveGroup)slots.OwnerFor(g.GroupId)).ToArray();
                    var versions=owners.Select(o=>o.ImmutableVersion).ToArray();
                    var generations=owners.Select(o=>(long)typeof(NavigationLiveGroup).GetField("generation",flags).GetValue(o)).ToArray();
                    source=versions.SelectMany(v=>v.Members.Cast<NavigationAuthorityMember>()).ToArray();
                    foreach(var member in source)member.Retain();
                    var references=source.Select(m=>m.References).ToArray();
                    foreach(var value in dto)value.ContactTick=2;
                    byte[] Save()=>(byte[])Call("CaptureWorldBytes",7,"contact-publish-boundary");
                    long Operation()=>(long)typeof(NavigationSession).GetField("authorityOperationIdentity",flags).GetValue(nav);
                    var save=Save();long operation=Operation(),physical=budget.PhysicalBytes,held=budget.HeldBytes;int scopes=budget.ActiveScopes;
                    Assert.Zero(scopes);Assert.IsNull(budget.AllocationAdmission);
                    void SourceExact(){
                        Assert.AreEqual(operation,Operation(),"Component arms must never publish to the source.");
                        for(int g=0;g<8;g++){Assert.AreSame(versions[g],owners[g].ImmutableVersion);Assert.AreEqual(generations[g],(long)typeof(NavigationLiveGroup).GetField("generation",flags).GetValue(owners[g]));}
                        CollectionAssert.AreEqual(references,source.Select(m=>m.References));
                        Assert.AreEqual(physical,budget.PhysicalBytes);Assert.AreEqual(held,budget.HeldBytes);Assert.AreEqual(scopes,budget.ActiveScopes);
                        Assert.AreEqual(0d,(double)type.GetField("elapsed",flags).GetValue(domain));
                    }
                    byte[] Arm(bool reuse,out long[] trace,out long delta){
                        var requests=new List<long>();NavigationRetentionBudget.Scope scope=null;NavRetainedArray<NavOwnedStorage> root=null;
                        var next=new NavigationAuthorityMember[400];
                        try{
                            budget.AllocationAdmission=bytes=>{requests.Add(bytes);return true;};
                            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Projection,nav.Generation,2,out scope));
                            var reservation=scope.OwnedReservation();var roots=new NavOwnedStorage[800];
                            for(int i=0;i<400;i++){
                                next[i]=reuse?NavigationAuthorityMember.CopyWithValuesOwned(source[i],new NavigationAuthorityMember.Values(dto[i]),reservation):NavigationAuthorityMember.CopyOwned(dto[i],reservation);
                                Assert.True(next[i].Data.BitwiseSame(new NavigationAuthorityMember.Values(dto[i])));Assert.AreEqual(dto[i].ContactPhase,next[i].ContactPhase);
                                Assert.AreEqual(dto[i].GoalLocation?.SurfaceId,next[i].GoalLocation?.SurfaceId);
                                Assert.AreEqual(reuse,ReferenceEquals(source[i].ContactOwner,next[i].ContactOwner));
                                roots[2*i]=source[i];roots[2*i+1]=next[i];
                            }
                            root=NavRetainedArray<NavOwnedStorage>.Copy(roots,reservation.TryReserve);
                            scope.Attach(root);scope.Commit(root,_=>{});budget.AllocationAdmission=null;
                            delta=budget.PhysicalBytes-physical;trace=requests.ToArray();return CaptureGraph(budget,root);
                        }finally{
                            budget.AllocationAdmission=null;root?.Release();foreach(var member in next)member?.ReleaseStorage();scope?.Discard();
                        }
                    }
                    var a1=Arm(false,out var ta1,out long da1);SourceExact();
                    var b=Arm(true,out var tb,out long db);SourceExact();
                    var a2=Arm(false,out var ta2,out long da2);SourceExact();
                    CollectionAssert.AreEqual(a1,a2);CollectionAssert.AreEqual(ta1,ta2);Assert.AreEqual(da1,da2);
                    Assert.False(a1.SequenceEqual(b),"Pinned BODY must expose distinct versus shared contact owners.");
                    Assert.False(ta1.SequenceEqual(tb),"Fresh text and scratch admission must not disappear silently.");Assert.Greater(da1,db);
                    long denied=ta1.Except(tb).First();
                    bool Refuses(bool reuse){
                        Assert.True(budget.TryOpenScope(NavigationPreparationKind.Projection,nav.Generation,2,out var scope));NavigationAuthorityMember member=null;
                        try{
                            budget.AllocationAdmission=bytes=>bytes!=denied;
                            try{member=reuse?NavigationAuthorityMember.CopyWithValuesOwned(source[0],new NavigationAuthorityMember.Values(dto[0]),scope.OwnedReservation()):NavigationAuthorityMember.CopyOwned(dto[0],scope.OwnedReservation());return false;}
                            catch(InvalidOperationException){return true;}
                        }finally{budget.AllocationAdmission=null;member?.ReleaseStorage();scope.Discard();}
                    }
                    Assert.True(Refuses(false),"A must reject a fresh-text-specific credit request.");SourceExact();
                    Assert.False(Refuses(true),"B bypasses that credit request, proving refusal inequivalence.");SourceExact();
                    // World capture itself performs one authority publication. Keep it outside
                    // the operation-invariant component interval; its complete bytes stay exact.
                    CollectionAssert.AreEqual(save,Save());Assert.AreEqual(operation+1,Operation(),"One existing save-capture publication, not a component mutation.");
                    for(int g=0;g<8;g++){Assert.AreSame(versions[g],owners[g].ImmutableVersion);Assert.AreEqual(generations[g],(long)typeof(NavigationLiveGroup).GetField("generation",flags).GetValue(owners[g]));}
                    CollectionAssert.AreEqual(references,source.Select(m=>m.References));Assert.AreEqual(physical,budget.PhysicalBytes);Assert.AreEqual(held,budget.HeldBytes);Assert.AreEqual(scopes,budget.ActiveScopes);
                    TestContext.WriteLine($"CONTACT_PUBLISH_NEGATIVE members=400 groups=8 seed=7 A1_BODY={a1.Length} B_BODY={b.Length} A2_BODY={a2.Length} A_delta={da1} B_delta={db} A_requests={ta1.Length} B_requests={tb.Length} denied_bytes={denied} A_refused=true B_refused=false source_save={save.Length} operation={operation}");
                    string output=Environment.GetEnvironmentVariable("SPACEWARS_CONTACT_BOUNDARY_OUTPUT");
                    if(output!=null){Directory.CreateDirectory(output);File.WriteAllBytes(Path.Combine(output,"A1.body"),a1);File.WriteAllBytes(Path.Combine(output,"B.body"),b);File.WriteAllBytes(Path.Combine(output,"A2.body"),a2);File.WriteAllLines(Path.Combine(output,"A1.credits"),ta1.Select(n=>n.ToString()));File.WriteAllLines(Path.Combine(output,"B.credits"),tb.Select(n=>n.ToString()));File.WriteAllLines(Path.Combine(output,"A2.credits"),ta2.Select(n=>n.ToString()));File.WriteAllBytes(Path.Combine(output,"source.save"),save);}
                }finally{
                    budget.AllocationAdmission=null;if(source!=null)foreach(var member in source)member.Release();authority.Stop();Call("DestroyOwnedNavigation");Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
                }
            }
        }
    }
}
