using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Presentation;
using UnityEngine;
using UnityEngine.TestTools;
namespace Spacewars.Tests.PlayMode
{
    public sealed class EarlyScoutRuntimePerformanceTests
    {
        const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
        [UnityTest,Timeout(240000)] public IEnumerator FixedHumanMoveOnStaticMap()=>Run(false);
        [UnityTest,Timeout(240000)] public IEnumerator OrdinaryFirstAiExplorer()=>Run(true);
        private IEnumerator Run(bool nativeAi)
        {
            var p=(PlayableProfile)typeof(PlayableBootstrap).GetMethod("LoadNormalProfile",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);
            var setup=new NativeLobbyConfiguration{HasExplicitSeed=true,ExplicitSeed=7};setup.InitializeParticipants();
            var basis=NativeLobbyMatch.Create(p,setup);var config=basis;
            if(!nativeAi){var costs=new double[basis.Starts.Count,basis.Starts.Count];for(int i=0;i<basis.Starts.Count;i++)for(int j=0;j<basis.Starts.Count;j++)costs[i,j]=basis.RouteCost(i,j);
                config=new OfflineMatchConfiguration(p,basis.SourceIdentity,basis.MapIdentity,basis.RouteProvenance,basis.Seed,basis.Roster.Select(x=>new OfflineParticipant(x.Id,x.LogicalPlayer,x.Team,OfflineControl.Human)).ToArray(),basis.Starts.ToArray(),basis.Sites.ToArray(),basis.Obstacles.ToArray(),costs,basis.Spectators.ToArray(),basis.ScenarioUnits.ToArray(),p.AuthoredMap.Id,basis.ScenarioBuildings.ToArray());}
            var runtime=new PlayableRuntime(config,nativeAi?182:181,startPaused:true);var probe=runtime.EnableEarlyScoutPerformanceProbe();var host=new UnityHostRouteService();
            var samples=new List<object>();var frames=new List<double>();var cpu=new List<double>();var events=new object[8192];int eventCount=0;long lastTick=-1;
            double began=0;var clock=Stopwatch.StartNew();var starting=new Dictionary<int,NavPoint>();long firstMotion=-1,geometryChanges=0;bool submitted=false;int previousGeometry=-1;long firstTick=0;
            try{
                while(!runtime.IsPrepared&&clock.Elapsed.TotalSeconds<90){host.Service(runtime,p.NavigationRequestsPerFrame);Assert.IsNull(runtime.Latest.Failure);yield return null;}
                Assert.True(runtime.IsPrepared,"Ordinary runtime preparation deadline; no synchronous Pump substitute");
                var d=(PlayableDomain)typeof(PlayableRuntime).GetField("domain",F).GetValue(runtime);
                void Event(string kind,int entity,long request,long order,NavPoint start,NavPoint goal){int i=Interlocked.Increment(ref eventCount)-1;if(i<events.Length)events[i]=new{kind,entity,request,order,start,goal,tick=d.Tick,geometry=d.Geometry.Revision,wall=clock.Elapsed.TotalSeconds-began};}
                d.Navigation.RouteRequested+=q=>Event("request",q.Entity,q.Request,q.Order,q.Start,q.Goal);
                d.Navigation.RouteActivated+=q=>Event("install",q.Entity,q.Request,q.Order,q.Start,q.Goal);
                d.Navigation.RouteFailed+=(q,o)=>Event("failure:"+o,q.Entity,q.Request,q.Order,q.Start,q.Goal);
                d.Navigation.MovementCancelled+=id=>Event("cancel",id,0,0,default,default);
                foreach(var seat in config.Roster)foreach(var e in runtime.ParticipantView(seat.Id).Entities.Where(e=>(int)e.Owner==seat.LogicalPlayer-1))starting[e.Id]=e.Position;
                began=clock.Elapsed.TotalSeconds;firstTick=runtime.Latest.Tick;previousGeometry=d.Geometry.Revision;double previous=began,nextSample=0;
                runtime.RequestPause(false);
                while(clock.Elapsed.TotalSeconds-began<40){
                    host.Service(runtime,p.NavigationRequestsPerFrame);Assert.IsNull(runtime.Latest.Failure);Assert.IsNull(host.CertifiedPublication?.Failure);
                    double now=clock.Elapsed.TotalSeconds,elapsed=now-began;frames.Add((now-previous)*1000);previous=now;var v=runtime.Latest;
                    if(v.Tick!=lastTick){cpu.Add(v.Metrics.TickCpuMilliseconds);lastTick=v.Tick;if(previousGeometry!=d.Geometry.Revision){geometryChanges++;previousGeometry=d.Geometry.Revision;}}
                    if(!nativeAi&&!submitted&&elapsed>=10){var own=runtime.ParticipantView(config.Roster[0].Id);var actor=own.Entities.First(e=>e.Owner==own.Owner&&e.Kind==PlayableEntityKind.Explorer);var goal=own.PublicScoutObjectives.Where(o=>o.Reachable&&o.SiteId!=own.HomeSiteId).OrderBy(o=>Math.Pow(o.Approach.X-actor.Position.X,2)+Math.Pow(o.Approach.Z-actor.Position.Z,2)).First();
                        Assert.True(runtime.TrySubmit(new PlayableCommand(runtime.Generation,1,own.OwnerId,PlayableCommandKind.Move,new[]{actor.Id},goal.Approach)).Accepted);submitted=true;}
                    if(elapsed>=nextSample){nextSample=elapsed+.1;var actors=config.Roster.Select(seat=>runtime.ParticipantView(seat.Id)).SelectMany(pv=>pv.Entities.Where(e=>e.Owner==pv.Owner)).ToArray();
                        if(firstMotion<0&&actors.Any(e=>starting.TryGetValue(e.Id,out var origin)&&!origin.Equals(e.Position)))firstMotion=v.Tick;
                        samples.Add(new{elapsed,tick=v.Tick,geometry=d.Geometry.Revision,viewGeometry=v.Geometry.Revision,units=runtime.DiagnosticGlobalUnits,buildings=runtime.DiagnosticGlobalBuildings,cpu=v.Metrics.TickCpuMilliseconds,interval=v.Metrics.TickIntervalMilliseconds,pending=v.Metrics.NavigationPending,missed=v.Metrics.MissedDeadlines,maximum=v.Metrics.MaximumTickCpu,barrierPolls=runtime.BarrierPolls,barrierReuse=runtime.BarrierPayloadReuses,barrierPublications=runtime.BarrierPayloadPublications,payloadMs=runtime.PayloadBuildElapsedMilliseconds,heap=GC.GetTotalMemory(false),gc0=GC.CollectionCount(0),pump=host.CertifiedPublication?.Pumps,actors=actors.Select(e=>new{e.Id,e.Owner,e.Kind,e.Position,e.Moving,e.NavigationOutcome,order=e.CurrentOrder?.CommandSequence,stamp=e.OrderStamp?.Sequence,target=e.CurrentOrder?.Destination}).ToArray()});}
                    yield return null;
                }
                double end=clock.Elapsed.TotalSeconds;long endTick=runtime.Latest.Tick;var metrics=runtime.Latest.Metrics;runtime.RequestStop();host.Dispose();
                while(!runtime.IsStopped&&clock.Elapsed.TotalSeconds-end<20)yield return null;Assert.True(runtime.IsStopped);Assert.IsNull(runtime.Latest.Failure);
                double Q(List<double> a,double q){var x=a.OrderBy(t=>t).ToArray();return x.Length==0?0:x[(int)Math.Ceiling((x.Length-1)*q)];}
                var report=new{scenario=nativeAi?"ordinary-native-ai":"fixed-human-static",seed=7,map=p.AuthoredMap.Id,profile=p.ProfileId,p.Revision,wall=end-began,startTick=firstTick,endTick,rawSpeed=(endTick-firstTick)/30d/(end-began),firstMotion,geometryChanges,requests=events.Take(Math.Min(eventCount,events.Length)).Count(x=>x!=null&&x.ToString().Contains("kind = request")),eventCount,dropped=Math.Max(0,eventCount-events.Length),tickCpuP95=Q(cpu,.95),tickCpuP99=Q(cpu,.99),metrics.MaximumTickCpu,metrics.MissedDeadlines,frameP95=Q(frames,.95),frameP99=Q(frames,.99),frameMax=Q(frames,1),framesOver100=frames.Count(x=>x>100),device=SystemInfo.deviceModel,cpu=SystemInfo.processorType,quality=QualitySettings.names[QualitySettings.GetQualityLevel()],observation="Editor no world rendering; runtime pacing independent of coroutine",journal=PlayableRuntime.JournalFactory!=null,probe=probe.Capture(),samples,events=events.Take(Math.Min(eventCount,events.Length)).ToArray()};
                string dir=Environment.GetEnvironmentVariable("SPACEWARS_EARLY_SCOUT_OUTPUT");Assert.IsNotEmpty(dir);Directory.CreateDirectory(dir);File.WriteAllText(Path.Combine(dir,nativeAi?"ordinary-ai.json":"human-static.json"),Newtonsoft.Json.JsonConvert.SerializeObject(report,Newtonsoft.Json.Formatting.Indented));
                TestContext.WriteLine($"EARLY_SCOUT mode={nativeAi} speed={(endTick-firstTick)/30d/(end-began):R} tickP99={Q(cpu,.99):R} tickMax={metrics.MaximumTickCpu:R} events={eventCount} geometryChanges={geometryChanges} firstMotion={firstMotion}");
                Assert.GreaterOrEqual(firstMotion,0,"Original explorers must actually move");Assert.Zero(Math.Max(0,eventCount-events.Length));if(!nativeAi)Assert.Zero(geometryChanges,"Fixed control must have no physical changes");
            }finally{runtime.RequestStop();host.Dispose();Assert.True(SpinWait.SpinUntil(()=>runtime.IsStopped,20000));runtime.Dispose();}
        }
    }
}
