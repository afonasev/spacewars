using System;
using System.Collections;
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
        private const BindingFlags CostFlags=BindingFlags.Instance|BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public;
        private static readonly FieldInfo CostOperation=typeof(NavigationSession).GetField("authorityOperationIdentity",CostFlags);
        private static readonly FieldInfo CostGeneration=typeof(NavigationLiveGroup).GetField("generation",CostFlags);
        private static readonly FieldInfo CostVersions=typeof(NavigationAuthorityGroup).GetField("versionSequence",CostFlags);
        private static long CostVersionSequence()=>(long)CostVersions.GetValue(null);
        private sealed class ContactCostQuery {internal int Entity;internal bool Contact;internal NavigationLiveGroup Owner;}
        private sealed class ContactCostSource : IDisposable
        {
            internal readonly PlayableAuthorityTick Authority;internal readonly object Domain;internal readonly Type Type;
            internal readonly NavigationSession Nav;internal readonly NavigationRetentionBudget Budget;
            internal readonly ContactCostQuery[] Queries;internal readonly byte[] Initial;
            internal long Operation=>(long)CostOperation.GetValue(Nav);
            internal byte[] Save()=>(byte[])Type.GetMethod("CaptureWorldBytes",CostFlags).Invoke(Domain,new object[]{7,"contact-publish-cost"});
            internal ContactCostSource(OfflineMatchConfiguration config,PlayableSnapshot[] views,PlayableProfile profile)
            {
                Authority=new PlayableAuthorityTick(config,1);Domain=typeof(PlayableAuthorityTick).GetField("domain",CostFlags).GetValue(Authority);Type=Domain.GetType();
                Nav=(NavigationSession)Type.GetProperty("Navigation",CostFlags).GetValue(Domain);Budget=Nav.PhysicalHostBudget;
                foreach(var view in views)Assert.AreEqual(PlayableCommandStatus.Applied,Authority.Apply(PlayableRuntime.CreateEightPlayerMovingPerformanceCommand(view,PlayableRuntime.EightPlayerMovingPerformanceSequence(view.Owner,0))).Status);
                Assert.AreEqual(400,Nav.Crowd.Units.Count);Assert.AreEqual(8,Nav.GroupOrders.Count);Assert.True(Nav.GroupOrders.All(g=>g.Members.Length==50));
                Type.GetMethod("RefreshVision",CostFlags).Invoke(Domain,null);
                var detect=(Func<PlayableOwner,NavPoint,double,double,bool>)Delegate.CreateDelegate(typeof(Func<PlayableOwner,NavPoint,double,double,bool>),Domain,Type.GetMethod("FormationContact",CostFlags));
                var map=Type.GetField("units",CostFlags).GetValue(Domain);var values=(IEnumerable)map.GetType().GetProperty("Values",CostFlags).GetValue(map);
                var slots=(NavigationCommandSlots<long,GroupOrderState>)typeof(NavigationSession).GetField("groups",CostFlags).GetValue(Nav);
                var owners=Nav.GroupOrders.SelectMany(g=>g.Members.Select(m=>new {m.Entity,g.GroupId})).ToDictionary(m=>m.Entity,m=>(NavigationLiveGroup)slots.OwnerFor(m.GroupId));
                var queries=new List<ContactCostQuery>();
                foreach(object unit in values){var t=unit.GetType();int id=(int)t.GetField("Id",CostFlags).GetValue(unit);var owner=(PlayableOwner)t.GetField("Owner",CostFlags).GetValue(unit);var kind=(PlayableEntityKind)t.GetField("Kind",CostFlags).GetValue(unit);
                    Assert.True(kind==PlayableEntityKind.Explorer||kind==PlayableEntityKind.Tank);Assert.True(Nav.Crowd.TryGet(id,out var actor));
                    queries.Add(new ContactCostQuery{Entity=id,Contact=detect(owner,actor.Position,PlayableUnitRules.Range(profile,kind,false),kind==PlayableEntityKind.Explorer?0:profile.TankProjectileCollisionRadius),Owner=owners[id]});
                }
                Queries=queries.ToArray();Assert.AreEqual(400,Queries.Length);Assert.AreEqual(400,Queries.Select(q=>q.Entity).Distinct().Count());
                Initial=Save();Assert.Zero(Budget.HeldBytes);Assert.Zero(Budget.ActiveScopes);Assert.IsNull(Budget.AllocationAdmission);
            }
            public void Dispose(){ContactPublishProfile.Enabled=false;ContactPublishProfile.LegacyMemberPagesForQa=false;Budget.AllocationAdmission=null;Authority.Stop();Type.GetMethod("DestroyOwnedNavigation",CostFlags).Invoke(Domain,null);Assert.Zero(Budget.PhysicalBytes);Assert.Zero(Budget.HeldBytes);Assert.Zero(Budget.ActiveScopes);}
        }
        private sealed class ContactCostSnapshot
        {
            internal long[] Count,Wall,Cpu;
            internal static ContactCostSnapshot Read(){
                var a=typeof(PerformanceProfile).GetField("current",CostFlags).GetValue(null);Assert.NotNull(a);var t=a.GetType();
                return new ContactCostSnapshot{Count=(long[])((long[])t.GetField("Count",CostFlags).GetValue(a)).Clone(),Wall=(long[])((long[])t.GetField("Wall",CostFlags).GetValue(a)).Clone(),Cpu=(long[])((long[])t.GetField("Cpu",CostFlags).GetValue(a)).Clone()};
            }
        }
        private sealed class ContactCostArm
        {
            internal byte[] Initial,Final;internal long[] Credits,Identity;internal int[] Queries;
            internal int FirstCredits;internal long Physical,Operation,VersionBase;internal readonly List<string> Metrics=new List<string>();
        }
        [Test] public void ReportFixed400ContactPublishStagesAndRefusal()
        {
            Assert.True(PerformanceProfile.Enabled,"This is the one explicitly profiled exclusive component control.");
            Assert.True(System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX),"CPU units below use the existing Darwin thread CPU clock.");
            Assert.False(ContactPublishProfile.Enabled,"Default opt-in absent.");
            var profile=PlayableProfile.Create(PlayableProfile.Default.CopyData(),new AiTestMap());var setup=new NativeLobbyConfiguration{AiTestMap=true,HasExplicitSeed=true,ExplicitSeed=7};setup.InitializeParticipants();
            // Initialize diagnostic counters before snapshots; no authoritative operation.
            foreach(ProfileStage stage in Enum.GetValues(typeof(ProfileStage)))if(stage!=ProfileStage.Count)using(PerformanceProfile.Measure(stage)){}
            string output=Environment.GetEnvironmentVariable("SPACEWARS_CONTACT_COST_OUTPUT");if(output!=null)Directory.CreateDirectory(output);
            using(var runtime=PlayableRuntime.CreateEightPlayerPerformanceMatch(profile,1,setup,startPaused:true)){
                var config=(OfflineMatchConfiguration)typeof(PlayableRuntime).GetField("offlineConfiguration",CostFlags).GetValue(runtime);
                var views=runtime.SpectatorFrame(PlayableRuntime.LocalSpectatorId).Players.OrderBy(v=>(int)v.Owner).ToArray();
                ContactCostArm Arm(string name,bool enabled){
                    ContactPublishProfile.Enabled=false;
                    using(var s=new ContactCostSource(config,views,profile)){
                        var result=new ContactCostArm{Initial=s.Initial,Queries=s.Queries.Select(q=>q.Entity).ToArray(),Identity=new long[1600*7]};
                        var trace=new long[200000];int traceCount=0,publication=0;long operation=s.Operation;var owners=s.Queries.Select(q=>q.Owner).Distinct().ToArray();
                        var generations=owners.Select(o=>(long)CostGeneration.GetValue(o)).ToArray();result.VersionBase=CostVersionSequence();
                        s.Budget.AllocationAdmission=bytes=>{if(traceCount==trace.Length)throw new InvalidOperationException("Fixed diagnostic credit trace overflow.");trace[traceCount++]=bytes;return true;};
                        var before=ContactCostSnapshot.Read();ContactPublishProfile.Enabled=enabled;
                        for(int tick=1;tick<=4;tick++){
                            s.Type.GetProperty("Tick",CostFlags).SetValue(s.Domain,(long)tick);
                            foreach(var q in s.Queries){
                                long previousGeneration=(long)CostGeneration.GetValue(q.Owner),parent=q.Owner.ImmutableVersion.VersionIdentity;
                                using(PerformanceProfile.Measure(ProfileStage.ContactPublish))s.Nav.SetFormationContact(q.Entity,q.Contact,tick);
                                long current=s.Operation;Assert.AreEqual(operation+publication+1,current);Assert.AreEqual(previousGeneration+1,(long)CostGeneration.GetValue(q.Owner));
                                var version=q.Owner.ImmutableVersion;Assert.AreEqual(parent,version.MemberParentIdentity);Assert.AreEqual(result.VersionBase+publication+1,version.VersionIdentity);
                                int at=publication*7;result.Identity[at]=tick;result.Identity[at+1]=q.Entity;result.Identity[at+2]=current;result.Identity[at+3]=previousGeneration+1;result.Identity[at+4]=version.VersionIdentity-result.VersionBase;result.Identity[at+5]=version.MemberParentIdentity-result.VersionBase;result.Identity[at+6]=q.Contact?1:0;
                                publication++;if(publication==1)result.FirstCredits=traceCount;
                            }
                        }
                        ContactPublishProfile.Enabled=false;var after=ContactCostSnapshot.Read();s.Budget.AllocationAdmission=null;
                        Assert.AreEqual(1600,publication);Assert.AreEqual(operation+1600,s.Operation);
                        for(int i=0;i<owners.Length;i++)Assert.AreEqual(generations[i]+200,(long)CostGeneration.GetValue(owners[i]));
                        result.Operation=s.Operation;result.Physical=s.Budget.PhysicalBytes;result.Credits=trace.Take(traceCount).ToArray();
                        result.Final=s.Save();Assert.AreEqual(result.Operation+1,s.Operation,"Existing save observer performs one authority publication.");Assert.AreEqual(result.Physical,s.Budget.PhysicalBytes);Assert.Zero(s.Budget.HeldBytes);Assert.Zero(s.Budget.ActiveScopes);
                        for(int i=0;i<owners.Length;i++)Assert.AreEqual(generations[i]+200,(long)CostGeneration.GetValue(owners[i]));
                        Assert.AreEqual(0d,(double)s.Type.GetField("elapsed",CostFlags).GetValue(s.Domain));
                        var finalMembers=s.Nav.GroupOrders.SelectMany(g=>g.Members).ToArray();Assert.True(finalMembers.All(m=>m.ContactTick==4&&m.ContactPhase=="PostMovementVisibility"));
                        for(int i=0;i<(int)ProfileStage.Count;i++){
                            long count=after.Count[i]-before.Count[i];if(count==0)continue;
                            double wall=(after.Wall[i]-before.Wall[i])*1000d/Stopwatch.Frequency,cpu=(after.Cpu[i]-before.Cpu[i])/1000000d;
                            result.Metrics.Add(string.Format(CultureInfo.InvariantCulture,"{0},{1},{2},{3:R},{4:R}",name,(ProfileStage)i,count,wall,cpu));
                        }
                        Assert.AreEqual(1600,after.Count[(int)ProfileStage.ContactPublish]-before.Count[(int)ProfileStage.ContactPublish]);
                        Assert.AreEqual(enabled?1600:0,after.Count[(int)ProfileStage.ContactOwnedTotal]-before.Count[(int)ProfileStage.ContactOwnedTotal]);
                        if(output!=null){File.WriteAllBytes(Path.Combine(output,name+".save"),result.Final);File.WriteAllLines(Path.Combine(output,name+".credits"),result.Credits.Select(v=>v.ToString(CultureInfo.InvariantCulture)));File.WriteAllLines(Path.Combine(output,name+".identity"),result.Identity.Select(v=>v.ToString(CultureInfo.InvariantCulture)));}
                        TestContext.WriteLine($"CONTACT_COST_ARM {name} enabled={enabled} publications={publication} operation={result.Operation} save={result.Final.Length} physical={result.Physical} credits={traceCount} first_credits={result.FirstCredits} version_base={result.VersionBase}");return result;
                    }
                }
                var a1=Arm("A1",false);var b=Arm("B",true);var a2=Arm("A2",false);
                foreach(var arm in new[]{b,a2}){
                    CollectionAssert.AreEqual(a1.Initial,arm.Initial);CollectionAssert.AreEqual(a1.Final,arm.Final);CollectionAssert.AreEqual(a1.Credits,arm.Credits);CollectionAssert.AreEqual(a1.Queries,arm.Queries);CollectionAssert.AreEqual(a1.Identity,arm.Identity);Assert.AreEqual(a1.Operation,arm.Operation);Assert.AreEqual(a1.Physical,arm.Physical);Assert.AreEqual(a1.FirstCredits,arm.FirstCredits);
                }
                long[] Refusals(bool enabled){
                    ContactPublishProfile.Enabled=false;
                    using(var s=new ContactCostSource(config,views,profile)){
                        CollectionAssert.AreEqual(a1.Initial,s.Initial);var q=s.Queries[0];long operation=s.Operation,physical=s.Budget.PhysicalBytes;
                        var version=q.Owner.ImmutableVersion;long generation=(long)CostGeneration.GetValue(q.Owner);var versions=new long[a1.FirstCredits];
                        for(int nth=1;nth<=a1.FirstCredits;nth++){
                            var requests=new List<long>();long v=CostVersionSequence();ContactPublishProfile.Enabled=enabled;
                            s.Budget.AllocationAdmission=bytes=>{requests.Add(bytes);return requests.Count!=nth;};
                            try{s.Nav.SetFormationContact(q.Entity,q.Contact,1);}finally{s.Budget.AllocationAdmission=null;ContactPublishProfile.Enabled=false;}
                            Assert.AreEqual(nth,requests.Count);CollectionAssert.AreEqual(a1.Credits.Take(nth),requests);Assert.AreEqual(operation,s.Operation);Assert.AreSame(version,q.Owner.ImmutableVersion);Assert.AreEqual(generation,(long)CostGeneration.GetValue(q.Owner));Assert.AreEqual(physical,s.Budget.PhysicalBytes);Assert.Zero(s.Budget.HeldBytes);Assert.Zero(s.Budget.ActiveScopes);versions[nth-1]=CostVersionSequence()-v;
                        }
                        CollectionAssert.AreEqual(s.Initial,s.Save());Assert.AreEqual(operation+1,s.Operation);Assert.AreSame(version,q.Owner.ImmutableVersion);Assert.AreEqual(generation,(long)CostGeneration.GetValue(q.Owner));Assert.AreEqual(physical,s.Budget.PhysicalBytes);
                        TestContext.WriteLine($"CONTACT_COST_REFUSAL enabled={enabled} points={a1.FirstCredits} source_exact=true zero_temporary=true");return versions;
                    }
                }
                var off=Refusals(false);var on=Refusals(true);CollectionAssert.AreEqual(off,on,"Refused preparations consume the same fresh global version identities; never reset the counter.");
                if(output!=null){File.WriteAllLines(Path.Combine(output,"stages.csv"),new[]{"arm,stage,calls,wall_ms,cpu_ms"}.Concat(a1.Metrics).Concat(b.Metrics).Concat(a2.Metrics));File.WriteAllBytes(Path.Combine(output,"initial.save"),a1.Initial);File.WriteAllLines(Path.Combine(output,"refusal-version-deltas.txt"),off.Select(v=>v.ToString()));}
            }
            Assert.False(ContactPublishProfile.Enabled);
        }
    }
}
