using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Tests.EditMode
{
    public sealed class NavigationThreeTankQueryTests
    {
        private const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
        private static object Read(object v,string n)=>v?.GetType().GetField(n,F)?.GetValue(v)??v?.GetType().GetProperty(n,F)?.GetValue(v);
        [Test,Timeout(180000)] public void OrdinaryThreeTankAttackMoveUsesOneGraphAndMovesEveryMember()=>Run(false);
        [Test,Timeout(180000)] public void GroupQuerySecondMemberColdResumeKeepsExactNextAndOriginalRequests()=>Run(true);
        private void Run(bool cold)
        {
            var map=new ThreeCrossingsMap(ScoutWitnessEnvironment.Load<ThreeCrossingsProfileData>("ThreeCrossingsProfile"));
            var profile=PlayableProfile.Create(ScoutWitnessEnvironment.Load<PlayableProfileData>("PlayableProfile"),map);
            const int seed=-515260688;var setup=new NativeLobbyConfiguration{HasExplicitSeed=true,ExplicitSeed=seed};setup.InitializeParticipants();
            for(int i=0;i<setup.Participants.Count;i++){var p=setup.Participants[i];p.Human=false;p.Team=i+1;p.Name="ИИ "+(i+1);p.Difficulty=i==0?AiDifficulty.Fighter:AiDifficulty.Veteran;}
            Assert.AreEqual(2,setup.Participants.Count);Assert.IsNull(setup.Validate(profile,true,true));
            var configuration=NativeLobbyMatch.Create(profile,setup);var domain=new PlayableDomain(profile,1,configuration);var nav=domain.Navigation;
            var authority=new PlayableAuthorityTick(domain,new AiAuthorityScheduler(Array.Empty<PlayableAiOwnerLoop>()),seed,configuration,asynchronousRoutes:true);
            var budget=nav.PhysicalHostBudget;var ids=new List<int>();var starts=new Dictionary<int,NavPoint>();var activated=new Dictionary<int,long>();
            bool coldDone=false;long observedGroup=0;var originalRequests=new Dictionary<int,long>();var originalOrders=new Dictionary<int,long>();
            var requestFacts=new long[1024,4];int requestCount=0;var cancellationFacts=new long[1024,2];int cancellationCount=0;
            void BindObservers(){nav.RouteRequested+=r=>{int at=requestCount++;if(at<1024){requestFacts[at,0]=r.Entity;requestFacts[at,1]=r.Request;requestFacts[at,2]=r.Order;requestFacts[at,3]=domain.Tick;}};nav.MovementCancelled+=id=>{int at=cancellationCount++;if(at<1024){cancellationFacts[at,0]=id;cancellationFacts[at,1]=domain.Tick;}};}
            byte[] Bytes(){using(var state=domain.CaptureWorldState(seed,"three-tank-query-v1"))return state.Encode();}
            var compilerOwners=new HashSet<object>();var graphs=new HashSet<object>();var compileKeys=new HashSet<string>();long graphWork=0,connectorWork=0;int maxJobs=0;
            var host=new UnityHostRouteService();BindObservers();try{
                double radius=PlayableUnitRules.Radius(profile,PlayableEntityKind.Tank);
                // Real authored placement predicates and live actors decide the
                // setup; no unit is forced into an occupied/unsupported point.
                foreach(int z in new[]{-40,-36,-32})foreach(int x in new[]{-38,-34,-30}){
                    var p=new NavPoint(x,z);if(ids.Count==3)break;
                    if(!domain.Geometry.IsFree(p,radius)||!map.TryLocate(p,radius,out _)||nav.Crowd.Units.Any(u=>Math.Pow(u.Position.X-p.X,2)+Math.Pow(u.Position.Z-p.Z,2)<Math.Pow(4*Math.Max(radius,u.Radius),2)))continue;
                    int id=(int)typeof(PlayableDomain).GetMethod("SpawnUnit",F).Invoke(domain,new object[]{p,PlayableOwner.Player,PlayableEntityKind.Tank});ids.Add(id);starts.Add(id,p);
                }
                Assert.AreEqual(3,ids.Count,"three actual valid Tank placements");
                var goal=new[]{new NavPoint(36,46),new NavPoint(32,46),new NavPoint(30,46),new NavPoint(28,46)}.First(p=>domain.Geometry.IsFree(p,radius)&&map.TryLocate(p,radius,out _));
                Assert.True(domain.Geometry.IsFree(goal,radius));Assert.True(map.TryLocate(goal,radius,out var goalLocation));Assert.True(map.IsValidLocation(goalLocation,radius));
                var receipt=authority.Apply(new PlayableCommand(1,1,"player-1",PlayableCommandKind.AttackMove,ids.ToArray(),goal,origin:PlayableOrderOrigin.Human));
                Assert.AreEqual(PlayableCommandStatus.Applied,receipt.Status,receipt.Message);
                var pending=ids.Select(id=>nav.PendingRequestFor(id)).ToArray();Assert.True(pending.All(p=>p!=null));
                Assert.AreEqual(1,pending.Select(p=>p.MemberIdentity.GroupId).Distinct().Count());Assert.AreEqual(1,pending.Select(p=>p.Order).Distinct().Count());observedGroup=pending[0].MemberIdentity.GroupId;
                foreach(var p in pending){originalRequests.Add(p.Entity,p.Request);originalOrders.Add(p.Entity,p.Order);}
                while(ids.Any(id=>Distance(nav.Crowd.Units.Single(u=>u.Id==id).Position,starts[id])<13)){
                    Assert.Less(domain.Tick,6000,"bounded actual group result; no policy reissues in this control");
                    host.Service(authority,profile.NavigationRequestsPerFrame);Assert.True(authority.TryAdvance());
                    foreach(int id in ids){Assert.True(nav.Crowd.TryGet(id,out var u));Assert.True(map.IsValidLocation(u.Location,u.Radius));if(nav.InstalledExecutionFor(id)!=null&&!activated.ContainsKey(id)){Assert.AreEqual(originalRequests[id],u.InstalledRequest,"original accepted request reaches actual install");Assert.AreEqual(originalOrders[id],u.InstalledOrder);activated.Add(id,domain.Tick);}}
                    bool coldBoundary=false;var root=nav.PinCommittedHostRoot();if(root!=null)try{foreach(var pair in root.ArrivalWork){
                        var work=(NavigationArrivalWork)pair.Value;var c=work.RouteCounters;graphWork=Math.Max(graphWork,c.LogicalGraphWork);connectorWork=Math.Max(connectorWork,c.ConnectorWork);
                        int jobs=0;foreach(var job in (IEnumerable)Read(Read(work,"scheduler"),"jobs")){
                            jobs++;var request=(NavScheduledRequest)Read(job,"Request");Assert.True(request.Query);Assert.True(request.GroupQuery,"entity="+request.Subscription.EntityId+" tick="+domain.Tick+" members="+((Array)Read(work,"members")).Length+" group="+request.Subscription.GroupId);AssertFullPreparedRegion(work,request);compileKeys.Add(request.CompileKey);
                            var compiler=Read(job,"Compiler")??Read(job,"CompletedCompiler");if(compiler!=null&&Read(job,"Graph")!=null)compilerOwners.Add(compiler);
                            var graph=Read(job,"Graph");if(graph!=null)graphs.Add(graph);
                            if(cold&&!coldDone&&work.MemberCursor==1&&Read(job,"Singleton")!=null){
                                var scheduler=Read(work,"scheduler");Assert.AreSame(graph,Read(scheduler,"cachedGraph"));Assert.AreSame(Read(job,"CompletedCompiler"),Read(scheduler,"cachedGraphCompiler"));Assert.AreSame(Read(scheduler,"artifactOwner"),Read(job,"GraphProviderOwner"));Assert.AreNotSame(request,Read(job,"GraphProviderOwner"));coldBoundary=true;
                            }
                        }maxJobs=Math.Max(maxJobs,jobs);Assert.LessOrEqual(jobs,1,"ordered member/endpoint query, never 61 parallel jobs");
                    }}finally{root.Release();}
                    if(coldBoundary){
                        byte[] prefix=Bytes();host.Service(authority,profile.NavigationRequestsPerFrame);Assert.True(authority.TryAdvance());byte[] expected=Bytes();
                        host.Dispose();authority.Stop();nav.ReleaseCommittedHostRoot();domain.DestroyOwnedNavigation();Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
                        domain=PlayableDomain.RestoreConfiguredWorldBytes(prefix,profile,seed,"three-tank-query-v1",configuration);nav=domain.Navigation;budget=nav.PhysicalHostBudget;authority=new PlayableAuthorityTick(domain,new AiAuthorityScheduler(Array.Empty<PlayableAiOwnerLoop>()),seed,configuration,asynchronousRoutes:true);host=new UnityHostRouteService();BindObservers();
                        CollectionAssert.AreEqual(prefix,Bytes(),"new group BODY/recipe preserve exact prefix");host.Service(authority,profile.NavigationRequestsPerFrame);Assert.True(authority.TryAdvance());CollectionAssert.AreEqual(expected,Bytes(),"exact actual next Pump + world tick across member graph borrowing");
                        Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(nav),budget.PhysicalBytes);compilerOwners.Clear();graphs.Clear();compileKeys.Clear();coldDone=true;
                    }
                }
                TestContext.WriteLine("THREE_TANK_OBSERVED tick="+domain.Tick+" graphOwners="+graphs.Count+" compilers="+compilerOwners.Count+" keys="+compileKeys.Count+" activated="+activated.Count+" work="+graphWork+"/"+connectorWork);
                Assert.AreEqual(cold,coldDone);Assert.LessOrEqual(requestCount,1024);Assert.LessOrEqual(cancellationCount,1024);Assert.AreEqual(3,activated.Count);Assert.AreEqual(1,compileKeys.Count);Assert.AreEqual(1,graphs.Count,"borrow the actual certified graph owner");Assert.AreEqual(1,compilerOwners.Count,"retain the actual completed compiler owner");Assert.AreEqual(1,maxJobs);
                foreach(int id in ids){var u=nav.Crowd.Units.Single(v=>v.Id==id);Assert.GreaterOrEqual(Distance(u.Position,starts[id]),13);Assert.NotNull(nav.InstalledExecutionFor(id));}
                TestContext.WriteLine("THREE_TANK_REQUEST_FACTS "+string.Join(";",Enumerable.Range(0,requestCount).Select(i=>requestFacts[i,0]+":"+requestFacts[i,1]+":"+requestFacts[i,2]+"@"+requestFacts[i,3]))+" cancels="+string.Join(";",Enumerable.Range(0,cancellationCount).Select(i=>cancellationFacts[i,0]+"@"+cancellationFacts[i,1]))+" original="+string.Join(",",originalRequests.OrderBy(p=>p.Key).Select(p=>p.Key+":"+p.Value)));
                TestContext.WriteLine("THREE_TANK_RESULT cold="+coldDone+" tick="+domain.Tick+" activations="+string.Join(",",activated.OrderBy(p=>p.Key).Select(p=>p.Key+":"+p.Value))+" graphOwners="+graphs.Count+" compilerOwners="+compilerOwners.Count+" compileKeys="+compileKeys.Count+" graphWork="+graphWork+" connectorWork="+connectorWork+" goal="+goal.X+","+goal.Z+" sameGroup="+observedGroup+" no AI reissues; not A3 trunk/combined");
            }catch(Exception e){TestContext.WriteLine("THREE_TANK_BODY_ERROR "+e);throw;}finally{host.Dispose();authority.Stop();nav.ReleaseCommittedHostRoot();domain.DestroyOwnedNavigation();Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);}
        }
        private static void AssertFullPreparedRegion(NavigationArrivalWork work,NavScheduledRequest request)
        {
            // Pages are partitioned by surface and may legitimately contain one
            // candidate. Compare the exact complete prepared page, never assume
            // that the Scout's 61-candidate page describes every group target.
            var members=(NavArrivalMember[])Read(work,"members");
            var key=(string)work.GetType().GetMethod("WorkingKey",F).Invoke(work,new object[]{members[work.MemberCursor]});
            var index=Read(work,"preparedClearances");var prepared=index.GetType().GetProperty("Item",F).GetValue(index,new object[]{new NavNameKey(key)});
            var regions=(IReadOnlyList<NavTerminalRegion>)Read(prepared,"Regions");
            Assert.AreSame(regions[(int)Read(work,"pageIndex")],request.Region,"full approved prepared terminal page, no candidate pruning");
            Assert.GreaterOrEqual(request.Region.Candidates.Count,1);Assert.LessOrEqual(request.Region.Candidates.Count,64);
        }
        private static double Distance(NavPoint a,NavPoint b)=>Math.Sqrt(Math.Pow(a.X-b.X,2)+Math.Pow(a.Z-b.Z,2));
    }
}
