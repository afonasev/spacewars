using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Text;
using Newtonsoft.Json;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Presentation;
using UnityEngine;
using UnityEngine.TestTools;

// Diagnosis only: ordinary prefix, bounded live authority-thread observations,
// then capture of the SAME runtime. This is not the full mission qualification.
public sealed class CampaignLiveMovementDiagnosticTests
{
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    [UnityTest, Timeout(240000)] public IEnumerator ObserveFirstLiveForkStallBeforeAndAfterCapture()=>Observe(false);
    [UnityTest, Timeout(240000)] public IEnumerator OrdinaryForkProgressesWithoutCapture()=>Observe(true);
    private IEnumerator Observe(bool requireFork)
    {
        string folder=Environment.GetEnvironmentVariable("CAMPAIGN_WEST_OUTPUT");Assert.IsNotEmpty(folder);Directory.CreateDirectory(folder);
        var p=PlayableProfile.Create(PlayableProfile.Default.CopyData(),new CampaignOpeningMap());var r=new PlayableRuntime(CampaignReconOpening.Configuration(p),106);var routes=new UnityHostRouteService();long sequence=0;
        NavigationSession session=null;PropertyInfo observer=null;int armed=0,stalled=0,captured=0,steps=0,flat=0;string before=null,after=null;var ring=new Queue<string>();var previous=new Dictionary<int,NavPoint>();var gate=new object();
        object Read(string name)=>typeof(NavigationSession).GetProperty(name,Flags)?.GetValue(session);
        object Field(string name)=>typeof(NavigationSession).GetField(name,Flags).GetValue(session);
        string Groups(){var slots=Field("groups");return JsonConvert.SerializeObject(slots.GetType().GetProperty("Values",Flags).GetValue(slots),Formatting.Indented);}
        string State()=>"steps="+steps+" path="+Read("LastNavigationStepPath")+" null="+Read("LastOwnedMovementNullReason")+" detail="+Read("LastOwnedMovementNullDetail")+" traffic="+Read("LastTrafficDispatchStage")+" names="+Read("LastAnswerNamesFailure")+" result="+Read("LastResultBatchFailure")+" batchStage="+Read("LastResultBatchStage")+" batchSource="+Read("LastResultBatchSourceReason")+" batchDetail="+Read("LastResultBatchDetail")+" answers="+string.Join(";",session.Answers.CopyItems().Select(a=>a.Request.Entity+":"+a.Request.Request+":"+a.Request.Order+":"+a.Status))+" pubs="+Read("OwnedMovementPublications")+" refusal="+Read("LastOwnedMovementRefusal")+" computation="+Read("LastOwnedMovementComputation")+" actors="+string.Join(";",session.Crowd.Units.Select(u=>u.Id+":"+u.Team+":"+u.Position.X+","+u.Position.Z+":heading="+u.Heading+":moving="+u.Moving+":speed="+u.Speed+":mob="+u.MobilityRevision+":order="+u.InstalledOrder+":goal="+u.Goal.X+","+u.Goal.Z+":outcome="+u.Outcome));
        void Observe(NavigationSession s)
        {
            if(Volatile.Read(ref armed)==0)return;steps++;double delta=0;int moving=0,players=0;bool installed=false;
            foreach(var u in s.Crowd.Units.Where(u=>u.Team==1)){players++;if(u.Moving)moving++;installed|=u.InstalledOrder>=4;if(previous.TryGetValue(u.Id,out var old))delta+=Math.Abs(old.X-u.Position.X)+Math.Abs(old.Z-u.Position.Z);else delta=1;previous[u.Id]=u.Position;}
            flat=installed&&moving>0&&delta<1e-9?flat+1:0;
            lock(gate){if(steps%5==0){if(ring.Count==128)ring.Dequeue();ring.Enqueue(State());}
                if(flat>=90&&Volatile.Read(ref stalled)==0){before=State()+"\nCROWD\n"+JsonConvert.SerializeObject(s.Crowd.CaptureState(),Formatting.Indented)+"\nGROUPS\n"+Groups();Volatile.Write(ref stalled,1);}
                if(Volatile.Read(ref captured)==1&&after==null)after=State()+"\nGROUPS\n"+Groups();}
        }
        IEnumerator Wait(Func<bool> done,double seconds,string reason){double end=Time.realtimeSinceStartupAsDouble+seconds;while(!done()&&Time.realtimeSinceStartupAsDouble<end){routes.Service(r,p.NavigationRequestsPerFrame);Assert.False(r.IsStopped,r.Latest?.Failure);yield return null;}Assert.True(done(),reason+" tick="+r.Latest?.Tick);}
        void Command(PlayableCommandKind kind,NavPoint point,int target=0){var ids=r.Latest.Entities.Where(e=>e.Owner==PlayableOwner.Player).Select(e=>e.Id).ToArray();var receipt=r.TrySubmit(new PlayableCommand(r.Generation,++sequence,"player-1",kind,ids,point,targetId:target));Assert.True(receipt.Accepted);}
        try{
            yield return Wait(()=>r.IsPrepared,60,"preparation");session=(NavigationSession)typeof(NavigationRoutePort).GetField("session",Flags).GetValue(r.NavigationBinding.RoutePort);observer=typeof(NavigationSession).GetProperty("MovementDiagnosticObserver",Flags);Assert.NotNull(observer);observer.SetValue(session,(Action<NavigationSession>)Observe);
            Command(PlayableCommandKind.Move,CampaignReconOpening.Rendezvous);yield return Wait(()=>r.Latest.Mission.Reinforced,60,"reinforcement");yield return Wait(()=>r.HasCampaignCheckpoint,30,"checkpoint");
            Command(PlayableCommandKind.Move,CampaignReconOpening.Explore);yield return Wait(()=>r.Latest.Mission.MineDiscovered,90,"mine revealed");var mine=r.Latest.Buildings.Single(b=>b.SiteId==1);Command(PlayableCommandKind.Attack,mine.Position,mine.Id);yield return Wait(()=>r.Latest.Mission.MineDestroyed,120,"mine combat");
            Command(PlayableCommandKind.AttackMove,CampaignReconOpening.Fork);Volatile.Write(ref armed,1);
            yield return Wait(()=>requireFork?r.Latest.Mission.ForkRevealed:Volatile.Read(ref stalled)==1||r.Latest.Mission.ForkRevealed,90,requireFork?"ordinary fork without capture":"first stall or fork");
            lock(gate){File.WriteAllText(Path.Combine(folder,"pre-capture-trace.txt"),string.Join("\n",ring));File.WriteAllText(Path.Combine(folder,"pre-capture-state.txt"),before??"Fork reached without a 90-step flat movement interval.");}
            if(!requireFork&&Volatile.Read(ref stalled)==1){var save=r.RequestCaptureBytes();yield return Wait(()=>save.IsCompleted,30,"same runtime capture");Assert.AreEqual(System.Threading.Tasks.TaskStatus.RanToCompletion,save.Status,save.Exception?.ToString());File.WriteAllBytes(Path.Combine(folder,"live-stall.bin"),save.Result);Volatile.Write(ref captured,1);int start=Volatile.Read(ref steps);yield return Wait(()=>Volatile.Read(ref steps)>=start+300||r.Latest.Mission.ForkRevealed,30,"same runtime after capture");lock(gate){File.WriteAllText(Path.Combine(folder,"post-capture-state.txt"),after??"no next step");File.WriteAllText(Path.Combine(folder,"post-capture-trace.txt"),string.Join("\n",ring));}}
            TestContext.WriteLine((requireFork?"Ordinary fork without capture: stall=":"Diagnostic only: stall=")+stalled+" fork="+r.Latest.Mission.ForkRevealed+" tick="+r.Latest.Tick);
        }finally{if(observer!=null)observer.SetValue(session,null);lock(gate){if(requireFork){File.WriteAllText(Path.Combine(folder,"ordinary-fork-trace.txt"),string.Join("\n",ring));File.WriteAllText(Path.Combine(folder,"ordinary-fork-first-flat-state.txt"),before??"no 90-step flat interval");}}r.RequestStop();routes.Dispose();}
    }
}
