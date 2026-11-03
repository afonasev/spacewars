using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Diagnostics;
using System.Threading;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Tests.PlayMode;
internal static class FormationAsyncDiagnostic
{
    const int Capacity=8192;const string Output=".local/recast-formation/async-diagnostic1";
    static void Check(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
    static object Field(object o,string name)=>o.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public).GetValue(o);
    sealed class Observer
    {
        readonly object[] ring=new object[Capacity];readonly PlayableAuthorityTick world;readonly CertifiedRouteKernel kernel;
        WeakReference<RecastAttachmentPreparation> previous;long seen,lastTick=-1,lastRefusals=-1;int generation,resets,disappearances;long lastWork,maxWork;
        internal Observer(PlayableAuthorityTick world,CertifiedRouteKernel kernel){this.world=world;this.kernel=kernel;kernel.BeforePrepare=Observe;}
        void Observe(){
            object context=null;var prep=Field(kernel,"recastPreparation");if(prep!=null)for(var c=Field(prep,"first");c!=null;c=Field(c,"Next"))if(((NavigationRequest[])Field(c,"Members")).Length==49){context=c;break;}
            var helper=context==null?null:Field(context,"Attachment") as RecastAttachmentPreparation;
            RecastAttachmentPreparation old=null;previous?.TryGetTarget(out old);bool changed=!ReferenceEquals(old,helper);
            if(helper!=null&&changed){generation++;resets++;previous=new WeakReference<RecastAttachmentPreparation>(helper);}else if(helper==null&&old!=null){disappearances++;previous=null;}
            long work=helper?.Work??0;if(helper!=null&&!changed&&work<lastWork)resets++;maxWork=Math.Max(maxWork,work);lastWork=work;
            long tick=world.Tick,refusals=kernel.RecastPreparationRefusals;if(tick==lastTick&&refusals==lastRefusals&&!changed)return;lastTick=tick;lastRefusals=refusals;
            var s=world.Domain.Navigation;var root=s.PinCommittedHostRoot();var bank=s.PinCreditBank();
            try{var f=root?.Frontier??default;ring[seen%Capacity]=new{Tick=tick,HelperGeneration=generation,HelperPresent=helper!=null,HelperWork=work,Required=context==null?(bool?)null:(bool)Field(context,"AttachmentRequired"),Committed=kernel.RecastPreparationCommittedWork,Refusals=refusals,PreScope=kernel.RecastPreparationPreScopeRefusals,PreFork=kernel.RecastPreparationPreForkRefusals,AdvanceEntered=kernel.RecastPreparationAdvanceEnteredRefusals,Publication=kernel.RecastPreparationPublicationRefusals,QuantumSerial=root?.QuantumSerial,Stage=f.Stage.ToString(),OriginalBudget=f.OriginalBudget,SolveLimit=f.SolveWorkLimit,SolveConsumed=f.SolveConsumed,BankCount=bank==null?(long?)null:bank[NavigationAccruedCredits.Count],Accepted=bank==null?(long?)null:bank[NavigationAccruedCredits.Accepted],Promoted=bank==null?(long?)null:bank[NavigationAccruedCredits.Promoted],Discarded=bank==null?(long?)null:bank[NavigationAccruedCredits.Discarded]};seen++;}
            finally{bank?.Release();root?.Release();}
        }
        internal object Result(){var data=new object[Math.Min(seen,Capacity)];long first=Math.Max(0,seen-Capacity);for(int i=0;i<data.Length;i++)data[i]=ring[(first+i)%Capacity];return new{Capacity,TotalSamples=seen,Dropped=Math.Max(0,seen-Capacity),HelperGenerations=generation,HelperBirthOrResetEvents=resets,DisappearanceEvents=disappearances,MaximumObservedHelperWork=maxWork,LastObservedHelperWork=lastWork,Samples=data};}
    }
    internal static void Run()
    {
        Check(!Directory.Exists(Output),"diagnostic output exists; no second producer");Directory.CreateDirectory(Output);
        var p=PlayableProfile.RecastGroupCorridorDefault;var config=RecastFormationInputs.Create(p,out var scenario,out var rejected);PlayableAuthorityTick world=null;CertifiedRouteWorker worker=null;var kernel=new CertifiedRouteKernel();Observer observer=null;Exception problem=null;object actors=null,bankEnd=null,census=null;bool ready=false,ack=false,clean=false,paidSpines=false,northBridge=false,parkedUnchanged=false,playing=false;long terminalTick=-1,issued=-1;double elapsed=0;CertifiedRoutePublication publication=null;ulong native=RecastNative.sw_recast_allocation_count();var clock=Stopwatch.StartNew();
        try{
            world=new PlayableAuthorityTick(config,805,asynchronousRoutes:true);world.EnableAccruedCredits();var s=world.Domain.Navigation;Check(s.AccruedCreditsEnabled,"actual accrued mode missing");
            var full=world.Domain.Snapshot(0,RuntimeStatus.Starting,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,config.Seed);var ids=full.Entities.Where(a=>a.Owner==PlayableOwner.Player).Select(a=>a.Id).OrderBy(x=>x).ToArray();Check(ids.Length==50&&full.Entities.Count==51&&full.Population.Living==100,"frozen actual factory/count/population changed");
            observer=new Observer(world,kernel);worker=new CertifiedRouteWorker(world.NavigationBinding,8,kernel);
            while(worker.Publication.RecastFoundationBuilds!=1&&clock.Elapsed.TotalSeconds<180){if(worker.Publication.Failure!=null)throw new InvalidOperationException(worker.Publication.Failure);Thread.Sleep(1);}Check(worker.Publication.RecastFoundationBuilds==1&&world.Tick==0,"public startup atTick0 failed");
            while(world.Tick==0&&clock.Elapsed.TotalSeconds<180){if(!world.TryAdvance())Thread.Sleep(1);}Check(world.Tick==1,"normal initial authority tick failed");var parked=s.Crowd.Units.Single(a=>a.Id==4).Position;var goal=new NavPoint(-23.2,29.4);Check(p.AuthoredMap.TryLocate(goal,p.ExplorerCollisionRadius,out var typed),"frozen goal unavailable");var receipt=world.Apply(new PlayableCommand(805,1,"player-1",PlayableCommandKind.Move,ids,goal).WithResolvedLocation(typed));Check(receipt.Status==PlayableCommandStatus.Applied,"normal Move refused: "+receipt.Message);issued=receipt.AppliedTick;
            double next=clock.Elapsed.TotalSeconds+1d/30;
            while(world.Tick-issued<2700&&clock.Elapsed.TotalSeconds<180){
                if(worker.Publication.Failure!=null)throw new InvalidOperationException(worker.Publication.Failure);
                if(s.PendingCount==0){ready=true;break;}
                double delay=next-clock.Elapsed.TotalSeconds;if(delay>0){Thread.Sleep(Math.Max(1,(int)Math.Min(5,delay*1000)));continue;}
                if(world.TryAdvance())next=Math.Max(next+1d/30,clock.Elapsed.TotalSeconds);else Thread.Sleep(1);
            }
            terminalTick=world.Tick;elapsed=clock.Elapsed.TotalSeconds;var selected=ids.Select(id=>s.Crowd.Units.Single(a=>a.Id==id)).ToArray();ready=s.PendingCount==0&&selected.All(a=>a.InstalledRequest>0&&a.GoalLocation.HasValue&&a.Outcome!=NavigationOutcome.Rejected);
            var plans=selected.Select(a=>(a.StorageOwner.InstalledExecution as NavigationInstalledExecutionOwner)?.Corridor?.Corridor?.RecastPlan).ToArray();paidSpines=plans.All(x=>x!=null);northBridge=paidSpines&&plans.SelectMany(x=>x.Spine.Legs).Any(x=>x.From.SurfaceId=="bridge/north"||x.To.SurfaceId=="bridge/north");parkedUnchanged=s.Crowd.Units.Single(a=>a.Id==4).Position.Equals(parked);playing=world.Domain.Outcome==PlayableMatchOutcome.Playing;
            actors=selected.Select(a=>new{a.Id,a.Position,a.Location,a.GoalLocation,a.Moving,a.Outcome,a.InstalledRequest}).ToArray();publication=worker.Publication;
        }catch(Exception e){problem=e;}
        finally{
            worker?.Dispose();ack=worker==null||SpinWait.SpinUntil(()=>worker.IsStopped,5000);publication=worker?.Publication??publication;
            if(ack){kernel.BeforePrepare=null;if(worker==null)kernel.Clear();}
            if(world!=null&&ack){var s=world.Domain.Navigation;var bank=s.PinCreditBank();try{bankEnd=bank==null?null:new{Count=bank[NavigationAccruedCredits.Count],Accepted=bank[NavigationAccruedCredits.Accepted],Promoted=bank[NavigationAccruedCredits.Promoted],Discarded=bank[NavigationAccruedCredits.Discarded],SaturationDrops=bank[NavigationAccruedCredits.SaturationDrops]};}finally{bank?.Release();}world.Stop();s.ReleaseCommittedHostRoot();world.Domain.DestroyOwnedNavigation();var b=s.PhysicalHostBudget;clean=b.PhysicalBytes==0&&b.HeldBytes==0&&b.ActiveScopes==0&&native==RecastNative.sw_recast_allocation_count();census=new{b.PhysicalBytes,b.HeldBytes,b.ActiveScopes,NativeBefore=native,NativeAfter=RecastNative.sw_recast_allocation_count()};}
            long classified=kernel.RecastPreparationPreScopeRefusals+kernel.RecastPreparationPreForkRefusals+kernel.RecastPreparationAdvanceEnteredRefusals+kernel.RecastPreparationPublicationRefusals;
            var result=new{Result=problem==null&&ready&&paidSpines&&northBridge&&parkedUnchanged&&playing&&ack&&clean&&classified==kernel.RecastPreparationRefusals&&publication?.RecastLongQueries==2?"PASS":"FAIL",Kind="ONE managed async+accrued diagnostic, not ordinary replay",Source=Environment.GetEnvironmentVariable("SPACEWARS_QA_SOURCE"),Seed=19092026,Generation=805,Horizon=2700,Dt=1d/30,Deadline=180,Issued=issued,TerminalTick=terminalTick,ElapsedSeconds=elapsed,Ready=ready,PaidSpines=paidSpines,NorthBridge=northBridge,ParkedUnchanged=parkedUnchanged,Playing=playing,Failure=problem?.ToString(),Actors=actors,Scenario=scenario,RejectedCandidates=rejected,Publication=publication,Committed=kernel.RecastPreparationCommittedWork,Refusals=kernel.RecastPreparationRefusals,PreScope=kernel.RecastPreparationPreScopeRefusals,PreFork=kernel.RecastPreparationPreForkRefusals,AdvanceEntered=kernel.RecastPreparationAdvanceEnteredRefusals,SourcePublication=kernel.RecastPreparationPublicationRefusals,Bank=bankEnd,WorkerACK=ack,Clean=clean,Census=census,Observations=ack?observer?.Result():null};
            File.WriteAllText(Path.Combine(Output,"observations.json"),Newtonsoft.Json.JsonConvert.SerializeObject(result));Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(new{Case="async-diagnostic-before-assert",result.Result,terminalTick,elapsed,ready,result.Committed,result.Refusals,result.PreScope,result.PreFork,result.AdvanceEntered,result.SourcePublication,ack,clean,Failure=result.Failure}));
        }
        if(problem!=null)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(problem).Throw();Check(ack&&clean,"diagnostic cleanup/ACK failed");Check(kernel.RecastPreparationRefusals==kernel.RecastPreparationPreScopeRefusals+kernel.RecastPreparationPreForkRefusals+kernel.RecastPreparationAdvanceEnteredRefusals+kernel.RecastPreparationPublicationRefusals,"diagnostic refusal labels incomplete");Check(ready,"all fifty paid Ready not reached by2700 normal-cadence ticks");Check(paidSpines&&northBridge&&parkedUnchanged&&playing,"original paidspine/northbridge/parked/outcome assertions failed");Check(publication.RecastLongQueries==2,"diagnostic added unexpected native query");
    }
}
