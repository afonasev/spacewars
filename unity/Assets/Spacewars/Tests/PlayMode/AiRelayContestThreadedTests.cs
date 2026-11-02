using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Tests.PlayMode
{
    public sealed class AiRelayContestThreadedTests
    {
        const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
        static object Field(object o,string name)=>o.GetType().GetField(name,F).GetValue(o);
        // Authored ownership/force fixtures; all decisions, orders, fights and subsequent
        // neutralization/capture run through real native AI + threaded navigation.
        [Test,Timeout(65000)] public void AlliedRelayIsDefendedThroughOrdinaryHost()=>RelayMissionFinishesAfterRealMovementAndCombat("ally");

        [TestCase("recapture")][TestCase("deny")][Timeout(65000)]
        public void RelayMissionFinishesAfterRealMovementAndCombat(string mode)
        {
            var p=PlayableProfile.Default;const long generation=81;const int seed=7;
            var points=new[]{new NavPoint(-24,-12),new NavPoint(24,18),new NavPoint(-24,18)};
            var starts=points.Select((v,i)=>new OfflineStart("start-"+i,i+1,v,new NavPoint(v.X+5,v.Z),pin:i+1)).ToArray();
            var sites=points.Select((v,i)=>new TerritorySite(i+1,PlayableBuildingKind.Headquarters,v,Array.Empty<TerritorySlot>()))
                .Concat(new[]{new TerritorySite(4,PlayableBuildingKind.RelayTower,new NavPoint(0,-8),Array.Empty<TerritorySlot>())}).ToArray();
            var roster=new[]{new OfflineParticipant("player-1",1,1,OfflineControl.Ai),new OfflineParticipant("enemy-1",2,2,OfflineControl.Human),new OfflineParticipant("ally",3,1,OfflineControl.Human)};
            var units=Enumerable.Range(0,3).Select(i=>new OfflineScenarioUnit(1,PlayableEntityKind.Tank,new NavPoint(-12,-12+i*3))).ToArray();
            var costs=new double[,]{{0,55,30},{55,0,55},{30,55,0}};
            var config=new OfflineMatchConfiguration(p,"relay-contest-v1","relay-flat","prepared-owned-threaded",seed,roster,starts,sites,Array.Empty<NavObstacle>(),costs,scenario:units);
            var domain=new PlayableDomain(p,generation,config);
            object Call(string method,params object[] args)=>typeof(PlayableDomain).GetMethod(method,F).Invoke(domain,args);
            var previousOwner=mode=="ally"?PlayableOwner.Third:PlayableOwner.Enemy;
            int prior=(int)Call("SpawnUnit",new NavPoint(3,-8),previousOwner,PlayableEntityKind.Explorer);
            for(int i=0;i<(mode=="deny"?30:180);i++)Call("AdvanceCapture",1d/30);
            if(mode!="deny")Call("Damage",prior,100000);
            int enemy=mode=="deny"?prior:mode=="ally"?(int)Call("SpawnUnit",new NavPoint(-3,-8),PlayableOwner.Enemy,PlayableEntityKind.Explorer):0;
            var owner=new PlayableAiOwnerLoop(p,PlayableAiOpeningComposition.Initialize(seed,"player-1"),generation);
            var opening=Field(owner,"openingExecutor");opening.GetType().GetField("phase",F).SetValue(opening,PlayableAiOpeningPhase.Complete);opening.GetType().GetField("initialForceFulfilled",F).SetValue(opening,true);
            foreach(var step in new[]{"income","factory","force","pressure"})((HashSet<string>)Field(opening,"completed")).Add(step);
            var a=new PlayableAuthorityTick(domain,new AiAuthorityScheduler(new[]{owner}),seed,config);CertifiedRouteWorker worker=null;
            try
            {
                var origin=a.ParticipantView("player-1").Entities.Where(u=>u.Owner==PlayableOwner.Player).ToDictionary(u=>u.Id,u=>u.Position);
                worker=new CertifiedRouteWorker(a.NavigationBinding,p.NavigationRequestsPerFrame);
                bool moved=false,applied=false,contested=false,neutral=false,enemyRemoved=enemy==0,finished=false;var watch=Stopwatch.StartNew();
                while(a.Tick<1800&&watch.Elapsed.TotalSeconds<55)
                {
                    if(!a.TryAdvance()){Thread.Sleep(1);continue;}Assert.IsNull(worker.Publication.Failure);
                    var view=a.ParticipantView("player-1");var relay=view.Sites.FirstOrDefault(s=>s.Site.Id==4);
                    moved|=view.Entities.Any(u=>origin.TryGetValue(u.Id,out var start)&&AiOpponentSelector.Distance(start,u.Position)>p.ExplorerCollisionRadius);
                    applied|=owner.Checkpoint.Records.Any(r=>(r.Policy=="scout"||r.Policy==AiArmyPlanner.Policy)&&(r.Kind==PlayableCommandKind.Attack||r.Kind==PlayableCommandKind.AttackMove)&&r.RuntimeStatus==PlayableCommandStatus.Applied);
                    contested|=relay?.Contested==true;neutral|=relay!=null&&!relay.Owner.HasValue;
                    // Full fixture view is evidence only, never fed back into the planner.
                    var full=domain.Snapshot(1,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,seed);
                    enemyRemoved|=enemy>0&&!full.Entities.Any(u=>u.Id==enemy);
                    // The army can intercept the enemy and leave the allied relay outside
                    // current vision. Inspect world ownership only in the witness, never AI input.
                    var outcome=mode=="ally"?full.Sites.Single(s=>s.Site.Id==4):relay;
                    finished=outcome!=null&&outcome.Owner==(mode=="ally"?PlayableOwner.Third:PlayableOwner.Player)&&enemyRemoved&&!outcome.Contested;
                    if(finished&&applied&&moved)break;Thread.Sleep(1);
                }
                TestContext.WriteLine("RELAY_CONTEST mode="+mode+" tick="+a.Tick+" moved="+moved+" applied="+applied+" contested="+contested+" neutral="+neutral+" enemyRemoved="+enemyRemoved+" finished="+finished+" mission="+owner.Checkpoint.ArmyMission?.Reason+" records="+string.Join(";",owner.Checkpoint.Records.Select(r=>r.Policy+":"+r.Kind+":"+r.RuntimeStatus+":"+r.Message)));
                Assert.True(moved&&applied&&finished,"Actual relay objective outcome, not just accepted order");
                if(mode=="recapture")Assert.True(neutral,"Hostile relay must pass through neutral");
                if(mode=="deny")Assert.True(contested,"Presence must actually interrupt hostile capture");
                if(mode!="recapture")Assert.True(enemyRemoved,"Actual opposing capturer defeated");
                var saved=a.CaptureBytes();var restored=PlayableAuthorityTick.RestoreBytes(saved,config);
                try{CollectionAssert.AreEqual(saved,restored.CaptureBytes());}finally{restored.Stop();}
            }
            finally{worker?.Dispose();if(worker!=null)Assert.True(SpinWait.SpinUntil(()=>worker.IsStopped,5000));a.Stop();}
        }
    }
}
