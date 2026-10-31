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
        [Test] public void Q9GroupOwnershipAllNthRollbackAndChargedPeak()
        {
            Contact30Source(true,(config,views,profile)=>{
                long[] credits;long peak,preparationPeak=0;
                using(var s=new ContactCostSource(config,views,profile)){
                    var list=new List<long>();s.Budget.AllocationAdmission=b=>{list.Add(b);preparationPeak=Math.Max(preparationPeak,checked(s.Budget.RetainedBytes+b));return true;};var owner=s.Queries[0].Owner;var old=owner.ImmutableVersion;old.Retain();long gen=(long)CostGeneration.GetValue(owner),op=s.Operation,v=CostVersionSequence();
                    try{
                        Assert.True(s.Nav.TryOwnedGroupContacts(old.Data.GroupId,id=>true,1));s.Budget.AllocationAdmission=null;
                        Assert.AreEqual(op+1,s.Operation);Assert.AreEqual(gen+1,(long)CostGeneration.GetValue(owner));Assert.AreEqual(v+1,CostVersionSequence());
                        Assert.AreEqual(old.VersionIdentity,owner.ImmutableVersion.MemberParentIdentity);Assert.AreEqual(-2,owner.ImmutableVersion.ChangedMemberIndex);
                        for(int i=0;i<50;i++){
                            var before=(NavigationAuthorityMember)old.Members[i];var after=(NavigationAuthorityMember)owner.ImmutableVersion.Members[i];
                            Assert.AreNotSame(before,after);Assert.False(before.Data.FormationReleased);Assert.True(after.Data.FormationReleased);Assert.AreEqual(1,after.Data.ContactTick);
                            Assert.AreEqual("PostMovementVisibility",after.ContactPhase);if(before.ContactOwner!=null)Assert.AreNotSame(before.ContactOwner,after.ContactOwner);
                        }
                        peak=s.Budget.PeakRetainedBytes;credits=list.ToArray();Assert.Zero(s.Budget.HeldBytes);Assert.Zero(s.Budget.ActiveScopes);
                        Assert.LessOrEqual(peak,LayeredNavigationScheduler.MaxRetainedBytes);
                    }finally{old.Release();}
                }
                using(var s=new ContactCostSource(config,views,profile)){
                    var owner=s.Queries[0].Owner;var old=owner.ImmutableVersion;long op=s.Operation,gen=(long)CostGeneration.GetValue(owner),physical=s.Budget.PhysicalBytes;
                    for(int nth=1;nth<=credits.Length;nth++){
                        int seen=0;s.Budget.AllocationAdmission=b=>{Assert.AreEqual(credits[seen],b);return ++seen!=nth;};
                        try{Assert.False(s.Nav.TryOwnedGroupContacts(old.Data.GroupId,id=>true,1));}finally{s.Budget.AllocationAdmission=null;}
                        Assert.AreEqual(nth,seen);Assert.AreSame(old,owner.ImmutableVersion);Assert.AreEqual(op,s.Operation);Assert.AreEqual(gen,(long)CostGeneration.GetValue(owner));
                        Assert.AreEqual(physical,s.Budget.PhysicalBytes);Assert.Zero(s.Budget.HeldBytes);Assert.Zero(s.Budget.ActiveScopes);
                    }
                    Assert.True(s.Nav.TryOwnedGroupContacts(old.Data.GroupId,id=>true,1));
                }
                TestContext.WriteLine("Q9_ALL_NTH="+credits.Length+" actualPreparationPeak="+preparationPeak+" setupInclusivePeak="+peak+" caps="+LayeredNavigationScheduler.MaxRetainedBytes);
            });
        }
        [Test] public void Q9StaleCancelCurrentHotColdNextGroupAndContacts()
        {
            Contact30Source(true,(config,views,profile)=>{
                using(var s=new ContactCostSource(config,views,profile)){
                    s.Type.GetProperty("Tick",CostFlags).SetValue(s.Domain,1L);
                    var q=s.Queries[0];var original=q.Owner.ImmutableVersion;int seen=0;
                    Assert.False(s.Nav.TryOwnedGroupContacts(original.Data.GroupId,id=>{if(++seen==1)s.Nav.SetFormationContact(q.Entity,true,1);return false;},2));
                    var changed=q.Owner.ImmutableVersion;Assert.AreNotSame(original,changed);Assert.True(((NavigationAuthorityMember)changed.Members[0]).Data.FormationReleased);
                    seen=0;Assert.False(s.Nav.TryOwnedGroupContacts(changed.Data.GroupId,id=>{if(++seen==1)((NavigationRetentionBudget.Scope)typeof(NavigationRetentionBudget).GetField("activeHead",CostFlags).GetValue(s.Budget)).RequestCancel();return false;},2));Assert.AreSame(changed,q.Owner.ImmutableVersion);
                    Assert.Zero(s.Budget.HeldBytes);Assert.Zero(s.Budget.ActiveScopes);
                    var saved=s.Save();var cold=PlayableDomain.RestoreConfiguredWorldBytes(saved,profile,7,"contact-publish-cost",config);var budget=cold.Navigation.PhysicalHostBudget;
                    try{
                        CollectionAssert.AreEqual(saved,cold.CaptureWorldBytes(7,"contact-publish-cost"));
                        foreach(long tick in new long[]{2,3}){
                            Contact30Phase(s,(int)tick);s.Type.GetProperty("Tick",CostFlags).SetValue(cold,tick);s.Type.GetMethod("RefreshFormationContacts",CostFlags).Invoke(cold,null);
                            CollectionAssert.AreEqual(s.Save(),cold.CaptureWorldBytes(7,"contact-publish-cost"));
                        }
                        var next=s.Nav.GroupOrders.First();Assert.True(s.Nav.TryOwnedGroupContacts(next.GroupId,id=>true,4));Assert.True(cold.Navigation.TryOwnedGroupContacts(next.GroupId,id=>true,4));
                        Assert.False(s.Nav.GroupOrders.Single(g=>g.GroupId==next.GroupId).MarchInitialized);Assert.AreEqual(0,s.Nav.GroupOrders.Single(g=>g.GroupId==next.GroupId).MarchAnchor);
                        s.Type.GetProperty("Tick",CostFlags).SetValue(s.Domain,4L);s.Type.GetProperty("Tick",CostFlags).SetValue(cold,4L);
                        CollectionAssert.AreEqual(s.Save(),cold.CaptureWorldBytes(7,"contact-publish-cost"));
                    }finally{cold.DestroyOwnedNavigation();Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);}
                }
            });
        }
        [Test] public void Q9WholeContactsFixed400Control()
        {
            Assert.True(PerformanceProfile.Enabled);ContactPhaseQa.Reset();
            string output=Environment.GetEnvironmentVariable("SPACEWARS_Q9_OUTPUT");if(output!=null)Directory.CreateDirectory(output);
            Contact30Source(false,(config,views,profile)=>{
                byte[] Run(string name,bool batch){
                    using(var s=new ContactCostSource(config,views,profile)){
                        var method=s.Type.GetMethod(batch?"RefreshFormationContacts":"RefreshFormationContactsMemberControlForQa",CostFlags);
                        long op=s.Operation,versions=CostVersionSequence();var owners=s.Queries.Select(q=>q.Owner).Distinct().ToArray();var generations=owners.Select(o=>(long)CostGeneration.GetValue(o)).ToArray();
                        var credits=new List<long>();s.Budget.AllocationAdmission=b=>{credits.Add(b);return true;};using(PerformanceProfile.Measure(ProfileStage.Contacts)){}var before=ContactCostSnapshot.Read();
                        for(int tick=1;tick<=4;tick++){s.Type.GetProperty("Tick",CostFlags).SetValue(s.Domain,(long)tick);method.Invoke(s.Domain,null);}
                        var after=ContactCostSnapshot.Read();s.Budget.AllocationAdmission=null;long writes=batch?32:1600;
                        Assert.AreEqual(op+writes,s.Operation);Assert.AreEqual(versions+writes,CostVersionSequence());for(int i=0;i<8;i++)Assert.AreEqual(generations[i]+(batch?4:200),(long)CostGeneration.GetValue(owners[i]));
                        foreach(var query in s.Queries){var group=query.Owner.ImmutableVersion;var member=group.Members.Cast<NavigationAuthorityMember>().Single(m=>m.Data.Entity==query.Entity);Assert.AreEqual(query.Contact,member.Data.FormationReleased);Assert.AreEqual(4,member.Data.ContactTick);}
                        Assert.Zero(s.Budget.HeldBytes);Assert.Zero(s.Budget.ActiveScopes);int k=(int)ProfileStage.Contacts;double wall=(after.Wall[k]-before.Wall[k])*1000d/Stopwatch.Frequency,cpu=(after.Cpu[k]-before.Cpu[k])/1000000d;
                        byte[] domain;using(var world=((PlayableDomain)s.Domain).CaptureWorldState(7,"contact-publish-cost")){domain=(byte[])world.Domain.Clone();if(output!=null)File.WriteAllBytes(Path.Combine(output,name+".save"),world.Encode());}
                        string metric=string.Format(CultureInfo.InvariantCulture,"{0},writes={1},wallMs={2:R},cpuMs={3:R},peak={4},physical={5},credits={6}",name,writes,wall,cpu,s.Budget.PeakRetainedBytes,s.Budget.PhysicalBytes,credits.Count);TestContext.WriteLine(metric);
                        if(output!=null){File.WriteAllText(Path.Combine(output,name+".metrics"),metric);File.WriteAllBytes(Path.Combine(output,name+".domain"),domain);File.WriteAllLines(Path.Combine(output,name+".credits"),credits.Select(x=>x.ToString()));}
                        return domain;
                    }
                }
                var a=Run("A-member",false);var b=Run("B-group",true);CollectionAssert.AreEqual(a,b,"Physical identity/refusal traces deliberately differ; logical domain values must stay exact.");
            });
        }
    }
}
