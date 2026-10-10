using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Presentation;
using Newtonsoft.Json;

namespace Spacewars.Editor
{
    // Observation fixtures: future cohesion/traffic thresholds are NOT baseline assertions.
    public static class GroupNavigationBaseline
    {
        [Serializable] public sealed class Row
        {
            public string scenario, variant, host, profile; public int repetition, seed=19092026, tickHz=30;
            public int count,ticks,arrived,blocked,unreachable,moving,pending,rejected,applied,repairs,repairFailed,maxNoProgress,peakQueue;
            public int navMeshRequests,flowRequests,navMeshBuilds,peakFlowCacheFields,firstMotionTick,crossedNorthBridge; public long flowBuilds,flowHits,flowCells,retainedManagedBytes,neighborCandidates,peakFlowCacheCells;
            public double[] startXs,startZs,goalXs,goalZs; public int[] moverIds;
            public double processCpuMs,initialRouteMs,warmRouteMs,commandToFirstMotionMs,maxTravelSpread,routeMs,routeCallP95Ms,routeCallMaxMs,tickP95Ms,tickP99Ms,tickMaxMs, maxSpeedSpread;
        }
        private static double Quantile(List<double> values,double q) {values.Sort();return values.Count==0?0:values[Math.Max(0,(int)Math.Ceiling(q*values.Count)-1)];}
        private static void Require(bool condition,string message="Baseline invariant failed") {if(!condition)throw new InvalidOperationException(message);}
        public static void Execute()
        {
            try
            {
                foreach(var scenario in new[]{"S01","S02","S04","S08","S18"})
                {
                    string[] variants=scenario=="S01"?new[]{"direct","bent","unreachable"}:scenario=="S04"?new[]{"narrow-flat","wide-flat","authored-bridge"}:new[]{scenario=="S02"?"mixed":scenario=="S08"?"occupied":"replace-restart"};
                    foreach(string variant in variants)foreach(int count in scenario=="S02"?new[]{2,10,25,50}:new[]{scenario=="S01"?1:scenario=="S08"?100:50})
                        for(int repetition=0;repetition<3;repetition++)Run(scenario,variant,count,scenario=="S04"||scenario=="S08"?2700:scenario=="S18"?300:1800,repetition);
                }
                EditorApplication.Exit(0);
            }
            catch(Exception error) {UnityEngine.Debug.LogException(error);EditorApplication.Exit(1);}
        }
        public static void ExecutePreflight()
        {
            try {Run("S04","authored-bridge",50,0,0,true);EditorApplication.Exit(0);}
            catch(Exception error) {UnityEngine.Debug.LogException(error);EditorApplication.Exit(1);}
        }
        private static int CheckBridge(NavigationSession nav,PlayableProfile p,int[] ids,NavPoint[] goals)
        {
            var map=(ThreeCrossingsMap)p.AuthoredMap;int crossed=0;
            for(int i=0;i<ids.Length;i++)
            {
                Require(nav.Crowd.TryGet(ids[i],out var unit));
                Require(unit.Position.X < -map.RiverHalfWidth-unit.Radius && goals[i].X > map.RiverHalfWidth+unit.Radius,"All 50 start/goal pairs must occupy opposite banks");
                Require(unit.Moving&&unit.Route.Count>0,"Preflight requires a complete checked route for every member");
                var previous=unit.Position;bool crosses=false;
                foreach(var point in unit.Route)
                {
                    if(previous.X<0&&point.X>=0)
                    {
                        double z=previous.Z+(point.Z-previous.Z)*(-previous.X)/(point.X-previous.X);
                        Require(Math.Abs(z-map.CrossingZ)<=map.SideBridgeWidth/2,"Route must cross intended north bridge");crosses=true;
                    }
                    previous=point;
                }
                Require(crosses,"No intended bridge crossing in route");crossed++;
            }
            return crossed;
        }
        private static void Run(string scenario,string variant,int count,int deadline,int repetition,bool preflightOnly=false)
        {
            var cpuBefore=preflightOnly?TimeSpan.Zero:Process.GetCurrentProcess().TotalProcessorTime;
            var p=variant=="authored-bridge"?PlayableProfile.ThreeCrossingsDefault:PlayableProfile.Default;var obstacles=new List<NavObstacle>();
            if(variant=="bent"||scenario=="S18")obstacles.Add(new NavObstacle(-3,-12,3,12));
            if(variant=="unreachable")obstacles.Add(new NavObstacle(-2,-50,2,50));
            if(scenario=="S04"&&variant!="authored-bridge") {double width=variant=="narrow-flat"?3:12;obstacles.Add(new NavObstacle(-4,-50,4,-width/2));obstacles.Add(new NavObstacle(-4,width/2,4,50));}
            var geometry=variant=="authored-bridge"?new NavGeometry(p.AuthoredMap.HalfExtent,PlayableMap.StaticObstacles(p),1):new NavGeometry(50,obstacles.ToArray(),1);var nav=new NavigationSession(71,geometry,p.Navigation);
            var row=new Row{scenario=scenario,variant=variant,count=count,repetition=repetition,host=Environment.OSVersion+" / CPUs="+Environment.ProcessorCount,profile=p.Navigation.ProfileId+":"+p.Navigation.Revision};
            var ids=Enumerable.Range(1,count).ToArray();
            if(variant=="authored-bridge")
            {
                // Bounded fixture staging stencil; authoritative geometry chooses legal cells.
                int placed=0;
                for(int z=0;z<7&&placed<count;z++)for(int x=0;x<12&&placed<count;x++)
                {
                    var point=new NavPoint(-54+x*3.6,35+z*3.6);
                    if(nav.Crowd.CanPlace(point,p.TankCollisionRadius))nav.Crowd.Add(ids[placed++],point,p.TankCollisionRadius,p.TankSpeed,p.TankTurnSpeed);
                }
                Require(placed==count,"Authored bridge staging must fit all 50 actors");
            }
            else for(int i=0;i<count;i++) {bool explorer=(scenario=="S02"||scenario=="S18")&&i%2==1;nav.Crowd.Add(ids[i],new NavPoint(-36+(i/10)*3.6,-16.2+(i%10)*3.6),explorer?p.ExplorerCollisionRadius:p.TankCollisionRadius,explorer?p.ExplorerSpeed:p.TankSpeed,explorer?p.ExplorerTurnSpeed:p.TankTurnSpeed);}
            // Two independent current orders; second observes parked first group and reservations.
            if(scenario=="S08")for(int i=0;i<50;i++){nav.Crowd.Remove(ids[i]);nav.Crowd.Add(ids[i],new NavPoint(18+(i/10)*3.6,-16.2+(i%10)*3.6));}
            int[] movers=scenario=="S08"?ids.Skip(50).ToArray():ids;
            var slots=nav.AllocateArrivalSlots(new NavPoint(24,variant=="authored-bridge"?38:0),movers);Require(movers.Length==slots.Length,"Arrival slot allocation incomplete");
            var starts=nav.Crowd.Units.Select(u=>u.Position).ToArray();row.startXs=starts.Select(v=>v.X).ToArray();row.startZs=starts.Select(v=>v.Z).ToArray();row.goalXs=slots.Select(v=>v.X).ToArray();row.goalZs=slots.Select(v=>v.Z).ToArray();row.moverIds=movers;var commandClock=preflightOnly?null:Stopwatch.StartNew();
            Require(nav.MoveGroup(movers,slots));row.peakQueue=nav.Requests.Count;
            long memoryBefore=preflightOnly?0:GC.GetTotalMemory(true);var samples=new List<double>();var routeSamples=new List<double>();
            using(var host=new UnityHostRouteService())
            {
                void Service() {var sw=preflightOnly?null:Stopwatch.StartNew();host.Service(nav.Requests,nav.Answers,nav.Generation,geometry,p,64);if(sw!=null){double elapsed=sw.Elapsed.TotalMilliseconds;row.routeMs+=elapsed;routeSamples.Add(elapsed);}nav.ApplyResults();}
                while(nav.Requests.Count>0)Service();row.initialRouteMs=row.routeMs;
                if(variant=="authored-bridge")row.crossedNorthBridge=CheckBridge(nav,p,movers,slots);
                if(preflightOnly)
                {
                    var preflightOutput=Environment.GetEnvironmentVariable("SPACEWARS_A0_EVIDENCE");Require(!string.IsNullOrEmpty(preflightOutput));Directory.CreateDirectory(preflightOutput);
                    File.WriteAllText(Path.Combine(preflightOutput,"preflight.json"),JsonConvert.SerializeObject(new{scenario,variant,count,map=p.AuthoredMap.Id,mapRevision=p.AuthoredMap.Revision,row.moverIds,row.startXs,row.startZs,row.goalXs,row.goalZs,row.crossedNorthBridge,result="Passed; geometry/placement/routes only, no timings"},Formatting.Indented));return;
                }
                if(scenario=="S18")
                {
                    // Late answers remain proposals, and restart drops all pending identity.
                    for(int n=0;n<12;n++){Require(nav.MoveGroup(ids,slots));foreach(int id in ids)nav.Stop(id,false);row.peakQueue=Math.Max(row.peakQueue,nav.Requests.Count);while(nav.Requests.Count>0)Service();}
                }
                for(int t=0;t<deadline;t++)
                {
                    var sw=Stopwatch.StartNew();nav.Step(1d/30);samples.Add(sw.Elapsed.TotalMilliseconds);row.neighborCandidates+=nav.Crowd.NeighborCandidates;row.ticks=t+1;
                    var travel=nav.Crowd.Units.Select((u,i)=>Math.Sqrt(Math.Pow(u.Position.X-starts[i].X,2)+Math.Pow(u.Position.Z-starts[i].Z,2))).ToArray();
                    row.maxTravelSpread=Math.Max(row.maxTravelSpread,travel.Max()-travel.Min());
                    if(row.firstMotionTick==0&&travel.Max()>0){row.firstMotionTick=t+1;row.commandToFirstMotionMs=commandClock.Elapsed.TotalMilliseconds;}
                    foreach(var u in nav.Crowd.Units)Require(geometry.IsFree(u.Position,u.Radius),"Footprint must remain legal");
                    if(nav.Crowd.Units.All(u=>!u.Moving)&&nav.PendingCount==0)break;
                }
                row.navMeshRequests=host.NavMeshRequests;row.flowRequests=host.FlowRequests;row.navMeshBuilds=host.NavMeshBuilds;
                row.peakFlowCacheFields=host.PeakFlowCacheFields;row.peakFlowCacheCells=host.PeakFlowCacheCells;row.flowBuilds=host.FlowRebuilds;row.flowHits=host.FlowCacheHits;row.flowCells=host.FlowExpandedCells;
                row.arrived=nav.Crowd.Units.Count(u=>u.Outcome==NavigationOutcome.Arrived);row.blocked=nav.Crowd.Units.Count(u=>u.Outcome==NavigationOutcome.Blocked);row.unreachable=nav.UnreachableResults;
                row.moving=nav.Crowd.Units.Count(u=>u.Moving);row.pending=nav.PendingCount;row.applied=nav.AppliedResults;row.rejected=nav.RejectedResults;
                row.repairs=nav.Crowd.RepairCount;row.repairFailed=nav.Crowd.RepairFailed;row.maxNoProgress=nav.Crowd.MaxNoProgressTicks;
                row.maxSpeedSpread=nav.Crowd.Units.Max(u=>u.Speed)-nav.Crowd.Units.Min(u=>u.Speed);
            }
            row.warmRouteMs=row.routeMs-row.initialRouteMs;
            row.processCpuMs=(Process.GetCurrentProcess().TotalProcessorTime-cpuBefore).TotalMilliseconds;
            row.retainedManagedBytes=GC.GetTotalMemory(true)-memoryBefore;
            row.routeCallP95Ms=Quantile(routeSamples,.95);row.routeCallMaxMs=routeSamples.Count==0?0:routeSamples.Max();row.tickP95Ms=Quantile(samples,.95);row.tickP99Ms=Quantile(samples,.99);row.tickMaxMs=samples.Count==0?0:samples.Max();
            if(scenario=="S18") {Require(row.pending==0);Require(row.moving==0);Require(row.rejected>0);var restarted=new NavigationSession(72,geometry,p.Navigation);restarted.Crowd.Add(1,new NavPoint(-20,0));restarted.Answers.TryEnqueue(new NavigationAnswer(new NavigationRequest(71,999,1,1,p.Navigation,geometry,new NavPoint(-20,0),new NavPoint(20,0)),new[]{new NavPoint(20,0)}));restarted.ApplyResults();Require(restarted.RejectedResults==1);Require(restarted.PendingCount==0);}
            string output=Environment.GetEnvironmentVariable("SPACEWARS_A0_EVIDENCE");
            Require(!string.IsNullOrEmpty(output),"Explicit durable output path required");Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output,scenario+"-"+variant+"-"+count+"-"+repetition+".json"),JsonConvert.SerializeObject(row,Formatting.Indented));
            UnityEngine.Debug.Log(JsonConvert.SerializeObject(row));
        }
    }
}
