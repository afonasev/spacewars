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
using UnityEngine;
using UnityEngine.TestTools;
namespace Spacewars.Tests.PlayMode
{
    public sealed class LocalWreckNavigationTests
    {
        const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
        [Test] public void BundledNormalProfileLoadsWithValidatedLocalWreckQuotas()
        {
            var method=typeof(Spacewars.Presentation.PlayableBootstrap).GetMethod("LoadNormalProfile",BindingFlags.Static|BindingFlags.NonPublic);
            Assert.NotNull(method);var p=(PlayableProfile)method.Invoke(null,null);
            Assert.AreEqual(16,p.WreckReconcileSlotsPerTick);Assert.AreEqual(1024,p.WreckReconcileCellsPerTick);Assert.AreEqual(2,p.WreckReconcileQueriesPerTick);Assert.AreEqual(30,p.WreckResponseTicks);
            Assert.True(p.UsesRecastGroundRoutes);Assert.NotNull(p.AuthoredMap);
        }
        [UnityTest,Timeout(180000)] public IEnumerator MovingArmySurvivesLocalBurstThroughOrdinaryHost()
        {
            var p=PlayableProfile.Create(PlayableProfile.Default.CopyData(),new ProvingGroundMap());
            var d=new PlayableDomain(p,81,false);CertifiedRouteWorker worker=null;
            object Call(string name,params object[] args)=>typeof(PlayableDomain).GetMethod(name,F).Invoke(d,args);
            var frames=new List<double>();var ticks=new List<double>();var events=new List<double>();
            var far=new List<int>();var starts=new Dictionary<int,NavPoint>();var installed=new Dictionary<int,long>();
            int resets=0,requests=0;bool measuring=false,created=false,cleared=false;long eventTick=-1,nearResponseTick=-1;
            try{
                var victims=new int[6];for(int i=0;i<6;i++)victims[i]=(int)Call("SpawnUnit",new NavPoint(0,-4*i),PlayableOwner.Player,PlayableEntityKind.Tank);
                for(int i=0;i<32;i++){var point=new NavPoint(-22-i/8*3,8+i%8*3);int id=(int)Call("SpawnUnit",point,PlayableOwner.Player,PlayableEntityKind.Tank);far.Add(id);starts.Add(id,point);}
                int near=(int)Call("SpawnUnit",new NavPoint(-12,0),PlayableOwner.Player,PlayableEntityKind.Tank);
                var authority=new PlayableAuthorityTick(d,new AiAuthorityScheduler(Array.Empty<PlayableAiOwnerLoop>()),7);
                worker=new CertifiedRouteWorker(authority.NavigationBinding,p.NavigationRequestsPerFrame);
                Assert.True(SpinWait.SpinUntil(()=>worker.IsWaiting||worker.Publication.Failure!=null,5000));Assert.IsNull(worker.Publication.Failure);
                long sequence=0;foreach(int id in far)Assert.AreEqual(PlayableCommandStatus.Applied,authority.Apply(new PlayableCommand(81,++sequence,"player-1",PlayableCommandKind.Move,new[]{id},new NavPoint(22,starts[id].Z))).Status);
                Assert.AreEqual(PlayableCommandStatus.Applied,authority.Apply(new PlayableCommand(81,++sequence,"player-1",PlayableCommandKind.Move,new[]{near},new NavPoint(12,0))).Status);
                d.Navigation.RouteActivated+=r=>{if(created&&r.Entity==near&&nearResponseTick<0)nearResponseTick=d.Tick;};
                d.Navigation.RouteRequested+=r=>{if(measuring)requests++;};
                var clock=Stopwatch.StartNew();double previous=Time.realtimeSinceStartupAsDouble;
                while(d.Tick<480&&clock.Elapsed.TotalSeconds<160){
                    double now=Time.realtimeSinceStartupAsDouble;if(measuring)frames.Add((now-previous)*1000);previous=now;
                    var timer=Stopwatch.StartNew();bool advanced=authority.TryAdvance();timer.Stop();if(measuring&&advanced)ticks.Add(timer.Elapsed.TotalMilliseconds);
                    Assert.IsNull(worker.Publication.Failure);
                    if(!created&&far.All(id=>d.Navigation.Crowd.TryGet(id,out var a)&&a.Moving&&!a.Position.Equals(starts[id]))){
                        foreach(int id in far){d.Navigation.Crowd.TryGet(id,out var a);installed[id]=a.InstalledRequest;}
                        measuring=true;eventTick=d.Tick;created=true;
                        foreach(int id in victims){timer.Restart();Call("Damage",id,int.MaxValue);timer.Stop();events.Add(timer.Elapsed.TotalMilliseconds);}
                        foreach(int id in far){d.Navigation.Crowd.TryGet(id,out var a);if(a.InstalledRequest!=installed[id]||!a.Moving)resets++;}
                        Assert.False(d.Geometry.IsFree(default,0),"Blocking is authoritative in the death event.");
                    }
                    if(created&&!cleared&&d.Tick>=eventTick+90){
                        timer.Restart();Assert.True((bool)Call("TryDestroyWrecks",victims,PlayableEntityKind.Tank));timer.Stop();events.Add(timer.Elapsed.TotalMilliseconds);cleared=true;
                        Assert.True(d.Geometry.IsFree(default,0));
                    }
                    yield return null;
                }
                double Q(List<double> xs,double q){var x=xs.OrderBy(v=>v).ToArray();return x.Length==0?0:x[(int)Math.Ceiling((x.Length-1)*q)];}
                int progressed=far.Count(id=>d.Navigation.Crowd.TryGet(id,out var a)&&a.Position.X>starts[id].X+10);
                d.Navigation.Crowd.TryGet(near,out var nearby);
                var report=new{scenario="local-wreck-burst-v1",seed=7,actors=39,tick=d.Tick,eventTick,created,cleared,progressed,farCount=far.Count,nearX=nearby.Position.X,nearResponseTick,responseTicks=nearResponseTick-eventTick,resets,requests,p95=Q(frames,.95),p99=Q(frames,.99),max=Q(frames,1),long50=frames.Count(v=>v>50),tickP99=Q(ticks,.99),tickMax=Q(ticks,1),eventMax=Q(events,1),events,simulationSpeed=d.Tick/30d/clock.Elapsed.TotalSeconds,device=SystemInfo.deviceModel,cpu=SystemInfo.processorType,quality=QualitySettings.names[QualitySettings.GetQualityLevel()]};
                PerformanceProfile.Flush();
                TestContext.WriteLine("LOCAL_WRECK "+JsonUtility.ToJson(new Result{summary=Newtonsoft.Json.JsonConvert.SerializeObject(report)}));
                string output=Environment.GetEnvironmentVariable("SPACEWARS_WRECK_REPORT");if(!string.IsNullOrEmpty(output))File.WriteAllText(output,Newtonsoft.Json.JsonConvert.SerializeObject(report,Newtonsoft.Json.Formatting.Indented));
                Assert.True(created&&cleared);Assert.AreEqual(far.Count,progressed);Assert.Greater(nearby.Position.X,8);Assert.AreEqual(480,d.Tick);
                if(Array.IndexOf(Environment.GetCommandLineArgs(),"-wreckCandidate")>=0){Assert.Zero(resets,"Unrelated installed routes must survive.");Assert.GreaterOrEqual(nearResponseTick,eventTick);Assert.LessOrEqual(nearResponseTick-eventTick,p.WreckResponseTicks,"Local response target in simulation ticks");Assert.LessOrEqual(Q(events,1),50,"Event wall budget ms");Assert.LessOrEqual(Q(frames,.99),50,"Frame p99 ms");Assert.LessOrEqual(Q(frames,1),150,"Frame maximum ms");}
            }finally{worker?.Dispose();if(worker!=null)Assert.True(SpinWait.SpinUntil(()=>worker.IsStopped,5000));d.Navigation.ReleaseCommittedHostRoot();d.DestroyOwnedNavigation();Assert.Zero(d.Navigation.PhysicalHostBudget.PhysicalBytes);Assert.Zero(d.Navigation.PhysicalHostBudget.HeldBytes);}
        }
        [Serializable] private sealed class Result {public string summary;}
    }
}
