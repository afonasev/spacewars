using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using System.Text;
using System.IO;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Presentation;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class CampaignEastCombatDiagnosticTests
{
    const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
    static object Get(object x,string field)=>x.GetType().GetField(field,F).GetValue(x);
    static void Set(object x,string field,object value)=>x.GetType().GetField(field,F).SetValue(x,value);
    static object Call(object x,string method,params object[] args)=>x.GetType().GetMethod(method,F).Invoke(x,args);
    [UnityTest] public IEnumerator ObserveReconstructedEastTwoSurvivorAttack()=>Observe(false);
    [UnityTest] public IEnumerator ObserveReconstructedEastTwoSurvivorCloseMove()=>Observe(true);
    static IEnumerator Observe(bool closeMove)
    {
        string folder=Environment.GetEnvironmentVariable("CAMPAIGN_COMBAT_TRACE");Assert.IsNotEmpty(folder);Directory.CreateDirectory(folder);
        var p=PlayableProfile.Create(PlayableProfile.Default.CopyData(),new CampaignOpeningMap());var a=CampaignReconOpening.Configuration(p);
        var scenario=a.ScenarioUnits.Select((u,i)=>new OfflineScenarioUnit(u.LogicalPlayer,u.Kind,i==0?new NavPoint(61.9455011625458,67.891004260792):i==2?new NavPoint(61.6808661635457,65.3267638580756):u.Position,u.Heading,u.Upgraded)).ToArray();
        var config=new OfflineMatchConfiguration(p,a.SourceIdentity+"-reconstructed-east-combat",a.MapIdentity,a.RouteProvenance,a.Seed,a.Roster.ToArray(),a.Starts.ToArray(),a.Sites.ToArray(),a.Obstacles.ToArray(),new double[3,3],scenario:scenario,terrain:p.AuthoredMap.Id,scenarioBuildings:a.ScenarioBuildings.ToArray(),missionId:CampaignReconOpening.Id);
        byte[] genesis;var setup=new PlayableRuntime(config,106,startPaused:true);
        try{
            double ready=Time.realtimeSinceStartupAsDouble+60;while(!setup.IsPrepared&&Time.realtimeSinceStartupAsDouble<ready){Assert.False(setup.IsStopped,setup.Latest?.Failure);yield return null;}Assert.True(setup.IsPrepared);Assert.True(setup.Latest.Paused);
            // Use the runtime's stationary capture callback and real transport codec.
            Action prepare=()=>{var d=Get(setup,"domain");Set(d,"campaignReinforced",true);Call(d,"TransferCampaignSquad",1,3);Set(d,"campaignStartedRadio",true);Call(d,"CampaignRadioEmitOnce","mission-start");Call(d,"CampaignRadioEmitOnce","reinforcement-3");Call(d,"Damage",11,10000);Call(d,"Damage",13,10000);Call(d,"Damage",10,16);Call(d,"Damage",12,2);};
            Set(setup,"BeforeCaptureSerialization",prepare);var capture=setup.RequestCaptureBytes();double captured=Time.realtimeSinceStartupAsDouble+20;while(!capture.IsCompleted&&Time.realtimeSinceStartupAsDouble<captured)yield return null;Assert.AreEqual(System.Threading.Tasks.TaskStatus.RanToCompletion,capture.Status,capture.Exception?.ToString());genesis=capture.Result;File.WriteAllBytes(Path.Combine(folder,"reconstructed-input.bin"),genesis);
        }finally{Set(setup,"BeforeCaptureSerialization",null);setup.RequestStop();}
        double stopped=Time.realtimeSinceStartupAsDouble+10;while(!setup.IsStopped&&Time.realtimeSinceStartupAsDouble<stopped)yield return null;Assert.True(setup.IsStopped);
        File.WriteAllText(Path.Combine(folder,"tactic.txt"),closeMove?"Ordinary Move to visible ground (57,80), explorer moving fire; no repeated commands.":"Original explicit Attack tank18; no repeated commands.");
        var r=PlayableRuntime.RestoreBytes(genesis,config);var routes=new UnityHostRouteService();object domain=null;var lines=new ConcurrentQueue<string>();var output=new StringBuilder("Causal reconstruction, NOT exact replay. IDs10/12/18 and recorded positions/HP; prior reload/turret/burst/queue/projectile state UNKNOWN and neutral here. Authored east guard/geometry retained.\n");
        try{
            double deadline=Time.realtimeSinceStartupAsDouble+60;while(!r.IsPrepared&&Time.realtimeSinceStartupAsDouble<deadline){routes.Service(r,p.NavigationRequestsPerFrame);Assert.False(r.IsStopped,r.Latest?.Failure);yield return null;}Assert.True(r.IsPrepared);
            CollectionAssert.AreEqual(new[]{10,12},r.Latest.Entities.Where(e=>e.Owner==PlayableOwner.Player).Select(e=>e.Id).OrderBy(x=>x));Assert.AreEqual(100,r.ParticipantView("corporation").Entities.Single(e=>e.Id==18).Health);
            domain=Get(r,"domain");var units=(IDictionary)Get(domain,"units");var nav=(NavigationSession)domain.GetType().GetProperty("Navigation",F).GetValue(domain);var projectiles=(IEnumerable)Get(domain,"projectiles");var seen=new HashSet<int>();
            Action observe=()=>{
                long tick=(long)domain.GetType().GetProperty("Tick",F).GetValue(domain);var text=new StringBuilder("tick="+tick);
                foreach(int id in new[]{10,12,18}){if(!units.Contains(id)){text.Append(" id="+id+":dead");continue;}var u=units[id];Assert.True(nav.Crowd.TryGet(id,out var actor));var target=new NavPoint(57,82);double dx=actor.Position.X-target.X,dz=actor.Position.Z-target.Z;bool clear=(bool)Call(domain,"GroundFireClear",actor.Position,target,id==18?p.TankProjectileCollisionRadius:0d);text.Append(" id="+id+" hp="+Get(u,"Health")+" pos="+actor.Position.X+","+actor.Position.Z+" moving="+actor.Moving+" held="+actor.Held+" pending="+nav.IsPending(id)+" installed="+actor.InstalledRequest+" target="+Get(u,"Target")+" order="+((PlayableTacticalOrderSnapshot)Get(u,"CurrentOrder"))?.Kind+" dToTank="+Math.Sqrt(dx*dx+dz*dz)+" clear="+clear+" reload="+Get(u,"Reload")+" burst="+Get(u,"BurstRemaining")+" burstSeq="+Get(u,"BurstSequence"));}
                foreach(var shot in projectiles){int id=(int)Get(shot,"Id");if(seen.Add(id))text.Append(" shot="+id+"/owner="+Get(shot,"Owner")+"/target="+Get(shot,"Target"));}lines.Enqueue(text.ToString());
            };
            Set(domain,"CombatStepForQa",observe);r.RequestPause(false);var receipt=r.TrySubmit(new PlayableCommand(r.Generation,17,"player-1",closeMove?PlayableCommandKind.Move:PlayableCommandKind.Attack,new[]{10,12},closeMove?new NavPoint(57,80):new NavPoint(57,82),targetId:closeMove?0:18));Assert.True(receipt.Accepted,"reconstructed Attack ingress="+receipt.Status);
            double end=Time.realtimeSinceStartupAsDouble+30,next=0;
            while(r.Latest.Outcome==PlayableMatchOutcome.Playing&&Time.realtimeSinceStartupAsDouble<end){routes.Service(r,p.NavigationRequestsPerFrame);Assert.False(r.IsStopped,r.Latest?.Failure);while(lines.TryDequeue(out var line))output.AppendLine(line);if(Time.realtimeSinceStartupAsDouble>=next){File.WriteAllText(Path.Combine(folder,"combat-trace.txt"),output.ToString());next=Time.realtimeSinceStartupAsDouble+1;}yield return null;}
            Assert.AreEqual(PlayableCommandStatus.Applied,r.ReceiptsAfter(0).Single(x=>x.Sequence==17&&x.Status!=PlayableCommandStatus.Accepted).Status);output.AppendLine("OUTCOME="+r.Latest.Outcome+" tick="+r.Latest.Tick+" tankHP="+r.ParticipantView("corporation").Entities.FirstOrDefault(e=>e.Id==18)?.Health);Debug.Log(output.ToString().Split('\n').LastOrDefault(x=>x.StartsWith("OUTCOME=")));
        }finally{if(domain!=null)Set(domain,"CombatStepForQa",null);r.RequestStop();routes.Dispose();while(lines.TryDequeue(out var line))output.AppendLine(line);File.WriteAllText(Path.Combine(folder,"combat-trace.txt"),output.ToString());}
    }
}
