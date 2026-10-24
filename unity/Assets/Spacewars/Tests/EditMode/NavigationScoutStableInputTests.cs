using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Tests.EditMode
{
    public sealed class NavigationScoutStableInputTests
    {
        private const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
        private static object Read(object value,string name)
        {if(value==null)return null;var t=value.GetType();return t.GetField(name,F)?.GetValue(value)??t.GetProperty(name,F)?.GetValue(value);}
        private static string Cursor(object value,params string[] names)
            =>value==null?"null":string.Join(",",names.Select(n=>n+"="+Read(value,n)));

        [Test,Timeout(180000)] public void RecordedScoutMoveFinishesStableGeometryThroughProductionHost()
        {
            var wall=Stopwatch.StartNew();
            string output=Path.GetFullPath(Path.Combine(ScoutWitnessEnvironment.DataPath,"../../.local/qa/scout-query-repair-01a1467d"));Directory.CreateDirectory(output);
            var input=JObject.Parse(File.ReadAllText(Path.Combine(ScoutWitnessEnvironment.DataPath,"../Tests/Fixtures/navigation-scout-stable-input.json")));
            var map=new ThreeCrossingsMap(ScoutWitnessEnvironment.Load<ThreeCrossingsProfileData>("ThreeCrossingsProfile"));
            var baseline=PlayableProfile.Create(ScoutWitnessEnvironment.Load<PlayableProfileData>("PlayableProfile"),map);
            string lab=Path.Combine(output,"lab");
            var store=new NativeBalanceStore(baseline,Path.Combine(lab,"profiles.json"),false);
            var profile=PlayableProfile.Create(store.Active.CopyData(),map,store.Active.DisplayName);
            StringAssert.Contains("spacewars-default @1",profile.DisplayName);
            File.WriteAllText(Path.Combine(output,"profile.json"),ScoutWitnessEnvironment.Json(profile.CopyData()));
            var setup=new NativeLobbyConfiguration{HasExplicitSeed=true,ExplicitSeed=(int)input["seed"]};setup.InitializeParticipants();
            for(int i=0;i<setup.Participants.Count;i++){var p=setup.Participants[i];p.Human=false;p.Team=i+1;p.Name="ИИ "+(i+1);p.Difficulty=i==0?AiDifficulty.Fighter:AiDifficulty.Veteran;}
            Assert.AreEqual(2,setup.Participants.Count);Assert.True(setup.Spectator);Assert.IsNull(setup.Validate(profile,true,true));
            var configuration=NativeLobbyMatch.Create(profile,setup);
            var domain=new PlayableDomain(profile,1,configuration);
            var authority=new PlayableAuthorityTick(domain,new AiAuthorityScheduler(Array.Empty<PlayableAiOwnerLoop>()),(int)input["seed"],configuration,asynchronousRoutes:true);
            var nav=domain.Navigation;var ledger=nav.PhysicalHostBudget;UnityHostRouteService host=null;
            long lastArrival=0,graph=0,field=0,connector=0,activation=-1;int nodes=0,candidates=64,maxTrace=1;string lastStage=null;
            long samples=(2L*(long)Math.Floor(map.HalfExtent/profile.Navigation.GridCell)+1);
            samples*=samples;
            long graphBound=6*samples+3;
            const long maxLabels=1048576;
            long fieldBound=64*(samples+1)+6*maxLabels+2;
            // Probe/select plus existing per-route reconstruction cycle guard.
            // Exact trace-length bound is tightened from the completed graph before use.
            long ConnectorBound()=>fieldBound+64*(64+samples+samples*65+16+maxLabels*maxTrace);
            TestContext.WriteLine("WITNESS_CONTRACT no AI policy/reissues after recorded120; SAME normal async tick quantum and budget="+profile.NavigationRequestsPerFrame+"; graphBound="+graphBound+" fieldBound="+fieldBound+" watchdog180000; not combined qualification");
            void Observe(bool force=false)
            {
                var root=nav.PinCommittedHostRoot();if(root==null)return;
                try{
                    var current=nav.PendingRequestFor(3);long request=current?.Request??3;
                    foreach(var pair in root.ArrivalWork){
                        var intents=(NavigationHostArrivalIntents)root.ArrivalIntentSets[pair.Key];
                        if(!intents.Requests.Any(r=>r.Entity==3&&r.Request==request))continue;
                        var work=(NavigationArrivalWork)pair.Value;var history=(NavigationHostArrivalHistory)root.ArrivalHistory[pair.Key];
                        Assert.True(work.IsImmutableCaptureRoot);Assert.True(history.IsImmutableCaptureRoot);Assert.AreEqual(work.LogicalEpoch+1,history.Count);
                        var c=work.RouteCounters;graph=c.LogicalGraphWork;field=c.LogicalFieldWork;connector=c.ConnectorWork;lastArrival=pair.Key;
                        string stage=work.WorkPhase,signature=work.WorkPhase;var scheduler=Read(work,"scheduler");var jobs=(IEnumerable)Read(scheduler,"jobs");int jobCount=0;
                        foreach(var job in jobs){
                            jobCount++;var recipe=(NavScheduledRequest)Read(job,"Request");candidates=recipe.Region.Candidates.Count;
                            Assert.LessOrEqual(candidates,64);
                            var compiler=Read(job,"Compiler")??Read(job,"CompletedCompiler");var partition=Read(compiler,"partition");
                            var g=(LayeredNavigationGraph)Read(job,"Graph");
                            if(g!=null&&nodes==0){
                                nodes=g.Nodes.Count;maxTrace=Math.Max(1,g.Arcs.Select(a=>a.Trace?.Count??1).DefaultIfEmpty(1).Max());
                                Assert.LessOrEqual(nodes,samples);Assert.AreEqual(samples+5L*nodes+3,graph,"one full partition compiler, shared by current candidate subscriptions");
                                TestContext.WriteLine("WITNESS_GRAPH tick="+domain.Tick+" N="+nodes+" E="+g.Arcs.Count+" candidates="+candidates+" maxTrace="+maxTrace+" graphWork="+graph+" compileKey="+recipe.CompileKey+" artifactKey="+recipe.ArtifactKey);
                            }
                            Assert.True(recipe.Query,"actual singleton uses explicit query recipe");Assert.AreEqual(1,jobCount,"one ordered endpoint query job");
                            signature+="/"+Read(job,"Phase")+"/"+Read(compiler,"Phase")+"/"+Read(Read(job,"Field"),"phase")+"/"+Read(Read(job,"Connector"),"phase")+"/"+Read(Read(job,"Singleton"),"phase");
                            stage+=" query="+Cursor(Read(job,"Singleton"),"phase","cursor","current","queryCandidate","queryChosenCandidate","FrontierCount","StateCount")+" job="+Read(job,"Phase")+" compiler="+Cursor(partition,"Phase","Consumed","sampled","x","z","node","direction","componentCursor")+" field="+Cursor(Read(job,"Field"),"phase","candidate","node","current","arcCursor")+" connector="+Cursor(Read(job,"Connector"),"phase","cursor","candidate","walkNode");
                        }
                        Assert.LessOrEqual(graph,graphBound);Assert.LessOrEqual(field,fieldBound);Assert.LessOrEqual(connector,ConnectorBound());
                        if(force||signature!=lastStage||domain.Tick%120==0){
                            TestContext.WriteLine("WITNESS_PROGRESS tick="+domain.Tick+" geometry="+domain.Geometry.Revision+" request="+request+" arrival="+pair.Key+" epoch="+work.LogicalEpoch+" history="+history.Count+" status="+work.Status+" jobs="+jobCount+" work="+graph+"/"+field+"/"+connector+" queue="+c.PendingSubscriptions+"/"+c.ReadyResults+" retained="+c.RetainedBytes+" "+stage);
                            lastStage=signature;
                        }
                    }
                }finally{root.Release();}
            }
            void Step()
            {
                Assert.Less(wall.ElapsedMilliseconds,180000,"existing watchdog; no timeout extension");
                host.Service(authority,profile.NavigationRequestsPerFrame);
                Assert.True(authority.TryAdvance(),"ordinary async authority must advance its clock");
                Assert.True(nav.Crowd.TryGet(3,out var scout));Assert.True(map.IsValidLocation(scout.Location,scout.Radius));
                if(domain.Tick>=121)Assert.AreEqual(6,domain.Geometry.Revision,"control input remains stable after the observed construction rebind");
                if(domain.Tick>=121)Observe();
            }
            try{
                Assert.True(nav.Crowd.TryGet(3,out var actor));Assert.AreEqual(new NavPoint(-36,-46),actor.Position);Assert.AreEqual(2,domain.Geometry.Revision);
                Assert.True(nav.Crowd.TryGet(4,out var other));Assert.AreEqual(new NavPoint(36,46),other.Position);
                host=new UnityHostRouteService();host.Service(authority,profile.NavigationRequestsPerFrame);
                foreach(var value in (JArray)input["commands"]){
                    var row=(JObject)value;long tick=(long)row["tick"];while(domain.Tick<tick)Step();Assert.AreEqual(tick,domain.Tick);
                    var ids=((JArray)row["ids"]).Select(v=>(int)v).ToArray();var target=(JArray)row["target"];
                    var command=new PlayableCommand(1,(long)row["sequence"],(string)row["owner"],(PlayableCommandKind)Enum.Parse(typeof(PlayableCommandKind),(string)row["kind"]),ids,new NavPoint((double)target[0],(double)target[1]),
                        siteId:(int)row["site"],slotId:(int)row["slot"],parentId:(int)row["parent"],buildingKind:(PlayableBuildingKind)Enum.Parse(typeof(PlayableBuildingKind),(string)row["building"]),origin:PlayableOrderOrigin.Ai,source:(string)row["source"],jobId:(long)row["actionId"],actionId:(long)row["actionId"]);
                    var receipt=authority.Apply(command);Assert.AreEqual(PlayableCommandStatus.Applied,receipt.Status,receipt.Message);Assert.AreEqual(tick,receipt.AppliedTick);
                    TestContext.WriteLine("WITNESS_APPLIED "+row.ToString(Newtonsoft.Json.Formatting.None)+" geom="+domain.Geometry.Revision);
                }
                Step();Assert.AreEqual(121,domain.Tick);Assert.AreEqual(6,domain.Geometry.Revision);
                var pending=nav.PendingRequestFor(3);Assert.NotNull(pending);Assert.AreEqual(3,pending.Request);Assert.AreEqual(1,pending.Order);Assert.AreEqual(new NavPoint(-37.58,0),pending.Goal);
                File.WriteAllText(Path.Combine(output,"geometry6.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{domain.Geometry.Revision,domain.Geometry.HalfExtent,Obstacles=domain.Geometry.Obstacles}));
                Observe(true);
                var origin=actor.Position;double Travel(){double x=actor.Position.X-origin.X,z=actor.Position.Z-origin.Z;return Math.Sqrt(x*x+z*z);}
                while(Travel()<13){
                    Step();
                    if(activation<0&&nav.InstalledExecutionFor(3)!=null){activation=domain.Tick;TestContext.WriteLine("WITNESS_ACTIVATED tick="+activation+" work="+graph+"/"+field+"/"+connector+" request="+actor.InstalledRequest);}
                    Assert.False(!nav.IsPending(3)&&!actor.Moving&&nav.InstalledExecutionFor(3)==null,"actual control order ended without activation: "+actor.Outcome);
                }
                Assert.GreaterOrEqual(activation,75);Assert.Less(activation,975,"activate before the first actual repeated Move");Assert.GreaterOrEqual(Travel(),13);Assert.AreEqual(3,actor.InstalledRequest);Assert.AreEqual(1,actor.InstalledOrder);
                TestContext.WriteLine("WITNESS_RESULT tick="+domain.Tick+" activation="+activation+" travel="+Travel()+" outcome="+actor.Outcome+" arrival="+lastArrival+" N="+nodes+" work="+graph+"/"+field+"/"+connector+" no subsequent AI reissue/builds; NOT combined fix");
            }finally{
                host?.Dispose();authority.Stop();nav.ReleaseCommittedHostRoot();domain.DestroyOwnedNavigation();
                TestContext.WriteLine("WITNESS_CLEANUP physical="+ledger.PhysicalBytes+" held="+ledger.HeldBytes+" scopes="+ledger.ActiveScopes);
                Assert.Zero(ledger.PhysicalBytes);Assert.Zero(ledger.HeldBytes);Assert.Zero(ledger.ActiveScopes);
                if(Directory.Exists(lab))Directory.Delete(lab,true);
            }
        }
    }
}
