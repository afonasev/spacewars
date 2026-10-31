using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Presentation;

namespace Spacewars.Tests.PlayMode
{
    public sealed class Oct28MixedGroupTests
    {
        [Test,Timeout(65000)] public void StraightNorthMixedGroupAndSoloControlMoveSafelyAndArriveWithoutRepeatedSteeringReversals()=>Run(false);
        [Test,Timeout(65000)] public void DenseStraightNorthMixedGroupAndSoloControlMoveSafelyAndArriveWithoutRepeatedSteeringReversals()=>Run(true);
        private void Run(bool dense)
        {
            const int seed=7;const long generation=81;var p=PlayableProfile.Default;
            var starts=new[]{new OfflineStart("west",1,new NavPoint(-25,-25),new NavPoint(-25,-20),pin:1),new OfflineStart("east",2,new NavPoint(25,-25),new NavPoint(25,-20),pin:2)};
            var sites=starts.Select((s,i)=>new TerritorySite(i+1,PlayableBuildingKind.Headquarters,s.Position,Array.Empty<TerritorySlot>())).ToArray();
            var configuration=new OfflineMatchConfiguration(p,"oct28-straight-north","clear-north","explicit-flat-no-obstacles",seed,new[]{new OfflineParticipant("player-1",1,1,OfflineControl.Human),new OfflineParticipant("enemy-1",2,2,OfflineControl.Human)},starts,sites,Array.Empty<NavObstacle>(),new double[,]{{0,50},{50,0}});
            var d=new PlayableDomain(p,generation,configuration);CertifiedRouteWorker worker=null;
            var method=typeof(PlayableDomain).GetMethod("SpawnUnit",BindingFlags.Instance|BindingFlags.NonPublic);
            int Spawn(double x,double z,PlayableEntityKind kind)=>(int)method.Invoke(d,new object[]{new NavPoint(x,z),PlayableOwner.Player,kind});
            PlayableSnapshot View()=>d.Snapshot(1,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,seed);
            try{
                var mixed=dense?Enumerable.Range(0,18).Select(i=>Spawn((i%3-1)*2,-3-(i/3)*2,i%2==0?PlayableEntityKind.Tank:PlayableEntityKind.Explorer)).ToArray():new[]{Spawn(-2,-3,PlayableEntityKind.Tank),Spawn(0,-3,PlayableEntityKind.Tank),Spawn(2,-3,PlayableEntityKind.Tank),Spawn(-1,-6,PlayableEntityKind.Explorer),Spawn(1,-6,PlayableEntityKind.Explorer)};
                int solo=Spawn(18,-6,PlayableEntityKind.Explorer);var ids=mixed.Concat(new[]{solo}).ToArray();
                foreach(int id in ids){Assert.True(d.Navigation.Crowd.TryGet(id,out var actor));typeof(NavUnit).GetProperty("Heading").SetValue(actor,Math.PI/2);}
                var initial=View().Entities.Where(e=>ids.Contains(e.Id)).ToDictionary(e=>e.Id);
                foreach(var e in initial.Values)Assert.True(d.Geometry.IsFree(e.Position,PlayableUnitRules.Radius(p,e.Kind)),"Authored clear lane before command");
                var saved=d.CaptureWorldBytes(seed,"oct28-straight-north");var restored=PlayableDomain.RestoreConfiguredWorldBytes(saved,p,seed,"oct28-straight-north",configuration);
                try{CollectionAssert.AreEqual(saved,restored.CaptureWorldBytes(seed,"oct28-straight-north"));}finally{restored.DestroyOwnedNavigation();}
                var authority=new PlayableAuthorityTick(d,new AiAuthorityScheduler(Array.Empty<PlayableAiOwnerLoop>()),seed);
                worker=new CertifiedRouteWorker(authority.NavigationBinding,p.NavigationRequestsPerFrame);
                Assert.AreEqual(PlayableCommandStatus.Applied,authority.Apply(new PlayableCommand(generation,1,"player-1",PlayableCommandKind.Move,mixed,new NavPoint(0,24))).Status);
                Assert.AreEqual(PlayableCommandStatus.Applied,authority.Apply(new PlayableCommand(generation,2,"player-1",PlayableCommandKind.Move,new[]{solo},new NavPoint(18,24))).Status);
                var reversals=ids.ToDictionary(id=>id,id=>0);var signs=ids.ToDictionary(id=>id,id=>0);
                var displayReversals=ids.ToDictionary(id=>id,id=>0);var displaySigns=ids.ToDictionary(id=>id,id=>0);
                var previous=initial;var motion=new PlayableUnitMotion();var clock=Stopwatch.StartNew();bool moved=false,arrived=false;
                var rows=new List<string>{"tick,id,x,z,heading,displayHeading"};
                while(d.Tick<1200&&clock.Elapsed.TotalSeconds<55){
                    if(!authority.TryAdvance()){Thread.Sleep(1);continue;}Assert.IsNull(worker.Publication.Failure);
                    var view=View();var current=view.Entities.Where(e=>ids.Contains(e.Id)).ToDictionary(e=>e.Id);motion.BeginFrame(view);
                    moved|=ids.All(id=>current[id].Position.Z>initial[id].Position.Z+p.ExplorerCollisionRadius);
                    foreach(int id in ids){var e=current[id];var pose=motion.Sample(e,d.Tick/30d);pose=motion.Sample(e,d.Tick/30d+1d/60d);
                        rows.Add(FormattableString.Invariant($"{d.Tick},{id},{e.Position.X:R},{e.Position.Z:R},{e.HullHeading:R},{pose.Hull:R}"));
                        Assert.True(d.Geometry.IsFree(e.Position,PlayableUnitRules.Radius(p,e.Kind)));
                        if(e.Position.Z>initial[id].Position.Z+5&&e.Position.Z<19&&e.Moving){
                            int s=Math.Abs(Math.Cos(e.HullHeading))<1e-3?0:Math.Sign(Math.Cos(e.HullHeading));
                            if(s!=0){if(signs[id]!=0&&s!=signs[id])reversals[id]++;signs[id]=s;}
                            int ds=Math.Abs(Math.Cos(pose.Hull))<1e-3?0:Math.Sign(Math.Cos(pose.Hull));
                            if(ds!=0){if(displaySigns[id]!=0&&ds!=displaySigns[id])displayReversals[id]++;displaySigns[id]=ds;}
                        }
                    }
                    foreach(int a in ids)foreach(int b in ids.Where(b=>b>a)){
                        var before=new NavPoint(previous[a].Position.X-previous[b].Position.X,previous[a].Position.Z-previous[b].Position.Z);
                        var after=new NavPoint(current[a].Position.X-current[b].Position.X,current[a].Position.Z-current[b].Position.Z);
                        double radius=PlayableUnitRules.Radius(p,current[a].Kind)+PlayableUnitRules.Radius(p,current[b].Kind);
                        double dx=after.X-before.X,dz=after.Z-before.Z,q=dx*dx+dz*dz;
                        double t=q==0?0:Math.Max(0,Math.Min(1,-(before.X*dx+before.Z*dz)/q));
                        double sx=before.X+dx*t,sz=before.Z+dz*t;
                        Assert.GreaterOrEqual(sx*sx+sz*sz,radius*radius-1e-9,"Full-step peer sweep");
                    }
                    previous=current;arrived=ids.All(id=>current[id].NavigationOutcome==NavigationOutcome.Arrived);if(arrived)break;Thread.Sleep(1);
                }
                var output=System.IO.Path.GetFullPath(System.IO.Path.Combine(UnityEngine.Application.dataPath,dense?"../../.local/oct28/r3-dense":"../../.local/oct28/r3-baseline"));System.IO.Directory.CreateDirectory(output);System.IO.File.WriteAllLines(output+"/trajectory.csv",rows);
                TestContext.WriteLine($"MIXED_NORTH seed={seed} tick={d.Tick} moved={moved} arrived={arrived} pumps={worker.Publication.Pumps}");
                foreach(int id in ids)TestContext.WriteLine($"id={id} kind={initial[id].Kind} authorityReversals={reversals[id]} displayReversals={displayReversals[id]}");
                Assert.True(moved&&arrived,"Both the mixed group and solo control must move and arrive through the real host");
                foreach(int id in ids.Where(id=>initial[id].Kind==PlayableEntityKind.Explorer))Assert.LessOrEqual(reversals[id],4,"Repeated left-right steering on the clear straight segment: "+id);
                Assert.Greater(worker.Publication.Pumps,0);
            }finally{worker?.Dispose();if(worker!=null)Assert.True(SpinWait.SpinUntil(()=>worker.IsStopped,5000));d.Navigation.ReleaseCommittedHostRoot();d.DestroyOwnedNavigation();Assert.Zero(d.Navigation.PhysicalHostBudget.PhysicalBytes);Assert.Zero(d.Navigation.PhysicalHostBudget.HeldBytes);}
        }
    }
}
