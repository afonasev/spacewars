using System;
using System.Linq;
using System.Reflection;
using System.IO;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Tests.PlayMode;
internal static class FormationServiceChecks
{
    static void Check(bool ok,string text){if(!ok)throw new InvalidOperationException(text);}
    static object Field(object o,string name)=>o.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public).GetValue(o);
    static RecastGroupPreparation Prep(CertifiedRouteKernel k)=>(RecastGroupPreparation)Field(k,"recastPreparation");
    static object Context(CertifiedRouteKernel k){var p=Prep(k);if(p==null)return null;for(var c=Field(p,"first");c!=null;c=Field(c,"Next"))if(((NavigationRequest[])Field(c,"Members")).Length==49)return c;return null;}
    static CertifiedRouteKernel.PumpFrontier Frame(NavigationSession s){var root=s.PinCommittedHostRoot();try{return root.Frontier;}finally{root.Release();}}
    internal static void Run()
    {
        var p=PlayableProfile.RecastGroupCorridorDefault;var config=RecastFormationInputs.Create(p,out _,out _);PlayableAuthorityTick world=null;var kernel=new CertifiedRouteKernel();NavigationSession restored=null;CertifiedRouteKernel fresh=null;ulong native=RecastNative.sw_recast_allocation_count();
        try{
            world=new PlayableAuthorityTick(config,RecastFormationInputs.Generation);var s=world.Domain.Navigation;
            var ids=world.Domain.Snapshot(0,RuntimeStatus.Starting,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,config.Seed).Entities.Where(a=>a.Owner==PlayableOwner.Player).Select(a=>a.Id).OrderBy(x=>x).ToArray();Check(ids.Length==50,"smallcase requires actual fifty factory");
            var goal=new NavPoint(-23.2,29.4);Check(p.AuthoredMap.TryLocate(goal,p.ExplorerCollisionRadius,out var at),"frozen goal unavailable");Check(world.Apply(new PlayableCommand(805,1,"player-1",PlayableCommandKind.Move,ids,goal).WithResolvedLocation(at)).Status==PlayableCommandStatus.Applied,"normal Move refused");
            kernel.PauseAtPhysicalBoundary=()=>kernel.CapturePumpFrontier().Stage==CertifiedRouteKernel.PumpStage.Solve&&Context(kernel)!=null&&((bool)Field(Context(kernel),"AttachmentRequired"));
            for(int i=0;i<1000;i++){world.TryAdvance();kernel.Pump(world.NavigationBinding,8);if(kernel.CapturePumpFrontier().Stage==CertifiedRouteKernel.PumpStage.Solve&&Context(kernel)!=null&&(bool)Field(Context(kernel),"AttachmentRequired"))break;}
            kernel.PauseAtPhysicalBoundary=null;var context=Context(kernel);Check(context!=null,"actual native context not reached");var baseline=Field(context,"Routes");var provider=Field(context,"Provider");var spine=Field(context,"Spine");var map=((RecastMapCache.Pin)Field(context,"Map")).Map;
            var start=Frame(s);Check(start.OriginalBudget==4&&start.SolveWorkLimit==4096&&start.SolveConsumed==0,"actual profile quantum is not4/4096");Check(kernel.RecastLongQueries==2,"smallcase introduced extra group query");
            int Slice(string label,int allowance){
                var before=Frame(s);long refusals=kernel.RecastPreparationRefusals,progressBefore=kernel.ProgressSequence;int? used=null;var after=before;
                try{used=kernel.AdvancePhysicalRecastPreparation(world.NavigationBinding,allowance);}
                finally{after=Frame(s);Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(new{Case="service-small-slice-before-assert",Label=label,Source=Environment.GetEnvironmentVariable("SPACEWARS_QA_SOURCE"),ReturnedUsed=used,Status=!used.HasValue?"Threw":used>0?"Committed":kernel.RecastPreparationRefusals>refusals?"Refused":kernel.Continue?.Invoke()==false?"Cancelled":"NoWork",BeforeStage=before.Stage.ToString(),AfterStage=after.Stage.ToString(),OriginalBudget=after.OriginalBudget,Limit=after.SolveWorkLimit,BeforeSpent=before.SolveConsumed,AfterSpent=after.SolveConsumed,ProgressBefore=progressBefore,ProgressAfter=kernel.ProgressSequence,RefusalsBefore=refusals,RefusalsAfter=kernel.RecastPreparationRefusals}));}
                Check(after.OriginalBudget==4&&after.SolveWorkLimit==4096&&after.SolveConsumed<=4096,"slice changed aggregate allowance");
                Check(used>=0&&used<=256&&after.SolveConsumed-before.SolveConsumed==used,"actual slice/debit mismatch");return used.Value;
            }
            long expectedSpent=start.SolveConsumed;int firstUsed=Slice("productive-first",256);Check(firstUsed>0,"productive slice missing");expectedSpent+=firstUsed;var first=Frame(s);Check(first.SolveConsumed==expectedSpent&&ReferenceEquals(Field(context,"Routes"),baseline),"private slice escaped baseline or debit missing");
            var helper=(RecastAttachmentPreparation)Field(context,"Attachment");Check(!ReferenceEquals(helper.WorkingRoutes,baseline)&&helper.StorageBytes==3771304,"workingroutes ownership/layout mismatch");
            long progress=kernel.ProgressSequence;Check(Slice("no-work",0)==0&&Frame(s).Same(first)&&kernel.ProgressSequence==progress,"no-work changed quota/progress");
            long retainedWork=helper.Work;var retainedRoutes=helper.WorkingRoutes;
            s.PhysicalHostBudget.AllocationAdmission=bytes=>bytes!=NavigationRetentionBudget.ScopeBytes;
            try{Check(Slice("scope-denial",256)==0,"scope denial reported useful progress");}finally{s.PhysicalHostBudget.AllocationAdmission=null;}
            Check(ReferenceEquals(Field(context,"Attachment"),helper)&&helper.Work==retainedWork&&ReferenceEquals(helper.WorkingRoutes,retainedRoutes)&&(bool)Field(context,"AttachmentRequired")&&Frame(s).Same(first),"scope denial discarded committed helper/work or changed published quota");
            Check(ReferenceEquals(Field(context,"Routes"),baseline)&&ReferenceEquals(Field(context,"Provider"),provider)&&ReferenceEquals(Field(context,"Spine"),spine)&&ReferenceEquals(((RecastMapCache.Pin)Field(context,"Map")).Map,map),"scope denial lost native/direct baseline");
            int retryUsed=Slice("after-scope-refusal",256);Check(retryUsed>0,"retained helper retry missing");expectedSpent+=retryUsed;Check(ReferenceEquals(Field(context,"Attachment"),helper)&&helper.Work==retainedWork+retryUsed&&ReferenceEquals(helper.WorkingRoutes,retainedRoutes)&&Frame(s).SolveConsumed==expectedSpent,"retry did not extend same committed helper/work with exact debit");
            Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(new{Case="pre-work-refusal-retention",SameHelper=true,SameWorkingRoutes=true,BeforeWork=retainedWork,AfterWork=helper.Work,RetryUsed=retryUsed,PublishedSpent=expectedSpent}));
            kernel.BeforePrepare=()=>{
                // A real same-value COW publication changes the expected source
                // identity, without manufacturing work/progress or a fake host.
                var old=s.PinCommittedHostRoot();NavigationHostRoot next=null;NavigationRetentionBudget.Scope scope=null;
                try{Check(s.TryOpenPhysicalHostScope(NavigationPreparationKind.Solve,old.Epoch,s.Admission.PublicationIdentity,out scope),"fault COW scope refused");next=old.ForkOwned(scope.OwnedReservation());next.SealPrepared();scope.Attach(next);s.PublishPhysicalHostRoot(scope,old,next,()=>{});}finally{next?.ReleaseStorage();scope?.Discard();old.Release();}
            };
            try{Check(Slice("source-publication-refusal",256)==0,"lost-source private work was committed");}finally{kernel.BeforePrepare=null;}
            Check(Frame(s).SolveConsumed==expectedSpent&&Field(context,"Attachment")==null&&ReferenceEquals(Field(context,"Routes"),baseline),"publication refusal retained unpaid route/work");
            int traceRefusals=0;s.PhysicalHostBudget.AllocationAdmission=bytes=>{
                if(!new System.Diagnostics.StackTrace().GetFrames().Any(f=>f.GetMethod().DeclaringType==typeof(TerrainLocations)&&f.GetMethod().Name=="TraceCore"))return true;
                traceRefusals++;return false;
            };
            try{Check(Slice("trace-denial",256)==0&&traceRefusals==1,"actual trace denial boundary not reached/exactly once");}finally{s.PhysicalHostBudget.AllocationAdmission=null;}
            Check(Frame(s).SolveConsumed==expectedSpent&&Field(context,"Attachment")==null&&ReferenceEquals(Field(context,"Routes"),baseline),"trace refusal retained private cursor/route or reset quota");
            int restartedUsed=Slice("after-trace-refusal",256);Check(restartedUsed>0,"restart after trace refusal failed");expectedSpent+=restartedUsed;kernel.Continue=()=>false;
            try{Check(Slice("cancel",256)==0,"cancel reported useful work");}finally{kernel.Continue=null;}
            Check(Frame(s).SolveConsumed==expectedSpent&&Field(context,"Attachment")==null&&(bool)Field(context,"AttachmentRequired"),"cancel reset spent quota or kept derivative");
            using var captured=s.CaptureState();var bytes=WorldWire.Pack(w=>WorldWire.Write(w,captured));File.WriteAllBytes(".local/recast-formation/service-small-pending.bin",bytes);
            using var saved=WorldWire.Unpack(bytes,WorldWire.ReadNavigationSessionState);restored=FormationPreflight.FromSnapshot(saved,p);var cold=Frame(restored);Check(cold.OriginalBudget==4&&cold.SolveWorkLimit==4096&&cold.SolveConsumed==expectedSpent,"pending restore reset quota");
            fresh=new CertifiedRouteKernel{PhysicalOperationsPerPump=1};var g=saved.NavigationGeometry;var geometry=new NavGeometry(g.HalfExtent,g.Obstacles,g.Revision);fresh.Pump(new PlayableRouteBinding(geometry,p,restored.Admission,restored.RoutePort),8);
            cold=Frame(restored);Check(cold.OriginalBudget==4&&cold.SolveWorkLimit==4096&&cold.SolveConsumed==expectedSpent,"caller8 changed resumed quantum allowance");
            Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(new{Case="actual-Recast-service-small",Result="PASS",OriginalBudget=4,SolveLimit=4096,Slice=256,Spent=expectedSpent,PrivateRoutesPublished=false,ScopeRefusal=true,SourcePublicationRefusal=true,TraceRefusal=traceRefusals,Cancellation=true,ColdCallerBudget=8,NativeQueries=kernel.RecastLongQueries,ColdNativeQueries=fresh.RecastLongQueries,kernel.RecastPreparationCommittedWork,kernel.RecastPreparationRefusals}));
        }finally{
            fresh?.Clear();if(restored!=null){restored.ReleaseCommittedHostRoot();restored.DestroyNavigationAuthority();var b=restored.PhysicalHostBudget;Check(b.PhysicalBytes==0&&b.HeldBytes==0&&b.ActiveScopes==0,"cold smallcase leaked");}
            kernel.Clear();if(world!=null){world.Stop();var s=world.Domain.Navigation;s.ReleaseCommittedHostRoot();world.Domain.DestroyOwnedNavigation();var b=s.PhysicalHostBudget;Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(new{Case="service-small-final-census",b.PhysicalBytes,b.HeldBytes,b.ActiveScopes,NativeBefore=native,NativeAfter=RecastNative.sw_recast_allocation_count()}));Check(b.PhysicalBytes==0&&b.HeldBytes==0&&b.ActiveScopes==0,"small service owner leak");}Check(native==RecastNative.sw_recast_allocation_count(),"small service native leak");
        }
    }
}
