using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Tests.PlayMode;
internal static class FormationPreflight
{
    static void Check(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
    // Read actual paid contexts while their existing worker rendezvous owns them.
    // This is tool-only observation: no new product hook, retained state or query.
    static void InstallContextObservation(CertifiedRouteKernel kernel,Action<ProbeInput> captureProbe=null)
    {
        var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public;
        object Field(object o,string name)=>o.GetType().GetField(name,flags).GetValue(o);
        var seen=new System.Collections.Generic.HashSet<long>();
        var trace=typeof(RecastGroupPreparation).GetMethod("Trace",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic);
        bool Can(ILayeredNavigationProvider provider,NavigationRequest request,NavLocation from,NavLocation to){var args=new object[]{provider,request,from,to,null};return (bool)trace.Invoke(null,args);}
        kernel.BeforePrepare=()=>{
            var preparation=Field(kernel,"recastPreparation");if(preparation==null)return;
            for(var ctx=Field(preparation,"first");ctx!=null;ctx=Field(ctx,"Next")){
                var members=(NavigationRequest[])Field(ctx,"Members");var routes=(RecastMemberRoute[])Field(ctx,"Routes");
                if(members==null||members.Length==0||routes==null||(NavSolveStatus)Field(ctx,"Result")!=NavSolveStatus.Ready||!seen.Add(members[0].Request))continue;
                var provider=(ILayeredNavigationProvider)Field(ctx,"Provider");var spine=routes.FirstOrDefault(x=>x!=null)?.Spine;
                Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(new{Case="actual-paid-context",NativeResult=Field(ctx,"Result").ToString(),Count=members.Length,RouteSuccess=routes.Count(x=>x!=null),BuildSpineNull=spine==null,Members=members.Select((r,i)=>new{r.Entity,r.Request,r.StartLocation,r.GoalLocation,r.Profile.ProfileId,r.Profile.Revision,r.Profile.Radius,r.HeldIdentity,GeometryRevision=r.Geometry.Revision,Group=r.MemberIdentity?.GroupId,RouteNull=routes[i]==null}),Spine=spine==null?null:new{spine.Binding,spine.MapBinding,spine.Fan,spine.Stops,spine.FanCenter,spine.FanRadius,Legs=spine.Legs.Select((l,n)=>new{Index=n,l.From,l.To,Kind=l.Kind.ToString(),l.PortalId,l.Cost})}}));
                if(spine==null)continue;
                if(captureProbe!=null&&members.Any(r=>r.Entity==24)){
                    var actual=members.Single(r=>r.Entity==24);Check(routes[Array.IndexOf(members,actual)]==null,"selected actual member24 is not a failed attachment");
                    var expected=File.ReadLines(".local/recast-formation/attachment-diagnostic1.log").Select(line=>{try{return Newtonsoft.Json.Linq.JObject.Parse(line);}catch{return null;}}).First(x=>x!=null&&(string)x["Case"]=="actual-null-attachment-trace"&&(int)x["Entity"]==24);
                    NavLocation Location(Newtonsoft.Json.Linq.JToken x)=>new NavLocation(new NavPoint((double)x["Position"]["X"],(double)x["Position"]["Z"]),(string)x["SurfaceId"]);
                    Check(actual.StartLocation.Value.Equals(Location(expected["StartLocation"]))&&actual.GoalLocation.Value.Equals(Location(expected["GoalLocation"])),"actual member24 frozen start/goal changed");
                    var geometry=new NavigationGeometryState{HalfExtent=actual.Geometry.HalfExtent,Revision=actual.Geometry.Revision,Obstacles=actual.Geometry.Obstacles.ToArray()};
                    var paidMap=((RecastMapCache.Pin)Field(ctx,"Map")).Map;
                    var input=new ProbeInput{Geometry=WorldWire.Pack(w=>WorldWire.Write(w,geometry)),ProfileValues=actual.Profile.CaptureWireValues(),ProfileId=actual.Profile.ProfileId,ProfileRevision=actual.Profile.Revision,MapBinding=paidMap.Geometry.Binding,Start=actual.StartLocation.Value,Goal=actual.GoalLocation.Value,Entity=actual.Entity,Request=actual.Request,HeldIdentity=actual.HeldIdentity};
                    Check(input.MapBinding==spine.MapBinding,"actual context native map/spine binding differs");captureProbe(input);
                    Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(new{Case="exact-paid-member-input-captured",input.Entity,input.Request,input.Start,input.Goal,input.ProfileId,input.ProfileRevision,input.ProfileValues,input.MapBinding,input.HeldIdentity,GeometryBytes=input.Geometry.Length,GeometrySHA=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(input.Geometry)).ToLowerInvariant()}));
                    continue;
                }
                foreach(var request in members.Where((r,i)=>routes[i]==null)){
                    var start=request.StartLocation.Value;var goal=request.GoalLocation.Value;
                    var originalJoins=spine.Stops.Where(n=>n<=spine.Fan&&Can(provider,request,start,spine.Node(n))).ToArray();int join=originalJoins.DefaultIfEmpty(-1).First();
                    var originalExits=join<0?Array.Empty<int>():spine.Stops.Where(n=>n>=Math.Max(join,spine.Fan)&&Can(provider,request,spine.Node(n),goal)).ToArray();
                    var allJoins=Enumerable.Range(0,spine.Legs.Length+1).Where(n=>n<=spine.Fan&&Can(provider,request,start,spine.Node(n))).ToArray();
                    int allJoin=allJoins.DefaultIfEmpty(-1).First();var allExits=allJoin<0?Array.Empty<int>():Enumerable.Range(0,spine.Legs.Length+1).Where(n=>n>=Math.Max(allJoin,spine.Fan)&&Can(provider,request,spine.Node(n),goal)).ToArray();
                    Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(new{Case="actual-null-attachment-trace",request.Entity,request.Request,request.StartLocation,request.GoalLocation,OriginalJoinCandidates=originalJoins,OriginalExitCandidates=originalExits,AllTypedBoundaryJoins=allJoins,AllTypedBoundaryExits=allExits,OriginalFailure=join<0?"noOriginJoin":originalExits.Length==0?"noTerminalExit":"unexpectedProjectionBoundary",WholeTraceOnly=true,NewNativeQueries=0}));
                }
            }
        };
    }
    sealed class ProbeInput
    {
        internal byte[] Geometry;internal double[] ProfileValues;internal string ProfileId,MapBinding,HeldIdentity;internal int ProfileRevision,Entity;internal long Request;internal NavLocation Start,Goal;
    }
    static void IsolatedConnectivity(ProbeInput copied,PlayableProfile profile)
    {
        var dto=WorldWire.Unpack(copied.Geometry,WorldWire.ReadNavigationGeometryState);
        var geometry=new NavGeometry(dto.HalfExtent,dto.Obstacles,dto.Revision);
        var nav=NavigationProfile.FromWireValues(copied.ProfileId,copied.ProfileRevision,copied.ProfileValues);
        Check(nav.CaptureWireValues().SequenceEqual(copied.ProfileValues),"copied profile values changed");
        var input=RecastAuthorityGeometry.Prepare(profile.AuthoredMap,geometry,nav);
        Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(new{Case="isolated-input-identity",copied.Entity,copied.Request,copied.Start,copied.Goal,CopiedMapBinding=copied.MapBinding,RebuiltMapBinding=input.Binding,MatchBinding=input.Binding==copied.MapBinding,GeometryRevision=dto.Revision,ObstacleCount=dto.Obstacles.Length,GeometrySHA=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(copied.Geometry)).ToLowerInvariant()}));
        Check(input.Binding==copied.MapBinding,"exact copied native map binding mismatch: query NotRun");
        Directory.CreateDirectory(".local/recast-formation");File.WriteAllBytes(".local/recast-formation/member24-geometry.bin",copied.Geometry);
        var budget=new NavigationRetentionBudget();Check(budget.TryOpenScope(NavigationPreparationKind.Solve,RecastFormationInputs.Generation,0,out var scope),"isolated native scope refused");ulong before=RecastNative.sw_recast_allocation_count();long faults=RecastNative.ReleaseFaults;
        try{using(var memory=RecastNative.Memory(scope.OwnedReservation()))using(var map=new RecastNavigationMap(input))using(var query=map.Query(copied.Start,copied.Goal)){
            for(int step=0;step<4096&&query.Status==RecastQueryStatus.Pending;step++)query.Step(32);
            Check(query.Status!=RecastQueryStatus.Pending,"isolated native diagnostic did not terminate");
            Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(new{Case="ONE-isolated-native-connectivity",copied.Entity,copied.Request,Status=query.Status.ToString(),query.Iterations,TypedResult=query.Status==RecastQueryStatus.Full?query.CopyTypedRoute():Array.Empty<RecastRoutePoint>(),DiagnosticQueries=1,Ordinary=false,OriginalReadyRecomputed=false,ProductionCap=16384}));
        }}finally{scope.Discard();Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(new{Case="isolated-native-final-census",budget.PhysicalBytes,budget.HeldBytes,budget.ActiveScopes,NativeBefore=before,NativeAfter=RecastNative.sw_recast_allocation_count(),ReleaseFaultsBefore=faults,ReleaseFaultsAfter=RecastNative.ReleaseFaults}));Check(budget.PhysicalBytes==0&&budget.HeldBytes==0&&budget.ActiveScopes==0&&RecastNative.sw_recast_allocation_count()==before&&RecastNative.ReleaseFaults==faults,"isolated query/map owner release failed");}
    }
    internal static void ObserveRoster()
    {
        var p=PlayableProfile.RecastGroupCorridorDefault;
        var config=RecastFormationInputs.Create(p,out var scenario,out var rejected,50,false);
        ulong native=RecastNative.sw_recast_allocation_count();PlayableAuthorityTick world=null;
        try{
            world=new PlayableAuthorityTick(config,RecastFormationInputs.Generation);
            var nav=world.Domain.Navigation;
            var full=world.Domain.Snapshot(0,RuntimeStatus.Starting,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,config.Seed);
            var view=world.Latest;var selected=full.Entities.Where(a=>scenario.Any(x=>x.Position.Equals(a.Position))).ToArray();
            var automatic=full.Entities.Where(a=>!selected.Any(x=>x.Id==a.Id)).ToArray();
            Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(new{Case="one-factory-global-observation",Seed=config.Seed,RecastFormationInputs.Generation,Tick=world.Tick,FullDomainTotal=full.Entities.Count,FullNavTotal=nav.Crowd.Units.Count,ViewerEntities=view.Entities.Count,OwnPopulation=view.Population,p.ArmyCapacity,p.ExplorerPopulationCost,ScenarioCount=scenario.Length,ScenarioIds=selected.Select(x=>x.Id),Automatic=automatic.Select(x=>new{x.Id,x.Owner,x.Kind,x.Position,x.Health,x.Moving}),Roster=config.Roster,Assignments=config.Assignments,Starts=config.Starts,world.Domain.Outcome,AIowners=world.Scheduler.Owners.Count(),MovementCommands=0,WorkerCreated=false,RejectedCandidates=rejected},Newtonsoft.Json.Formatting.Indented));
            Check(full.Entities.Count==nav.Crowd.Units.Count,"full domain/Nav population differs");
            Check(selected.Length==50&&automatic.Length==2&&!world.Scheduler.Owners.Any(),"observed factory scenario/automatic/AI identities differ");
        }finally{
            if(world!=null){world.Stop();world.Domain.Navigation.ReleaseCommittedHostRoot();world.Domain.DestroyOwnedNavigation();var b=world.Domain.Navigation.PhysicalHostBudget;Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(new{Case="global-observation-final-census",b.PhysicalBytes,b.HeldBytes,b.ActiveScopes,NativeBefore=native,NativeAfter=RecastNative.sw_recast_allocation_count()}));Check(b.PhysicalBytes==0&&b.HeldBytes==0&&b.ActiveScopes==0,"observational factory owner leak");}
            Check(RecastNative.sw_recast_allocation_count()==native,"observational factory native leak");
        }
    }
    sealed class HelperBirthObservation : IDisposable
    {
        readonly CertifiedRouteKernel kernel;readonly NavigationRetentionBudget budget;
        readonly System.Reflection.BindingFlags flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public;
        object context,query,map;long physical,held;int denials,matches;bool rollback;
        object Field(object value,string name)=>value.GetType().GetField(name,flags).GetValue(value);
        object Context49(){var prep=Field(kernel,"recastPreparation");if(prep==null)return null;for(var c=Field(prep,"first");c!=null;c=Field(c,"Next"))if(((NavigationRequest[])Field(c,"Members")).Length==49)return c;return null;}
        void Empty(object c){foreach(var name in new[]{"Provider","ResultPayload","Routes","Spine","Attachment"})Check(Field(c,name)==null,"partial Context publication after helper refusal: "+name);}
        internal HelperBirthObservation(CertifiedRouteKernel kernel,NavigationRetentionBudget budget)
        {
            this.kernel=kernel;this.budget=budget;Check(kernel.BeforePrepare==null&&budget.AllocationAdmission==null,"fault observer would replace an existing policy/hook");
            kernel.BeforePrepare=()=>{
                if(denials==0||rollback)return;
                Check(ReferenceEquals(Context49(),context),"helper refusal replaced context");Empty(context);
                Check(ReferenceEquals(Field(context,"Query"),query)&&((RecastNavigationQuery)query).Status==RecastQueryStatus.Full,"helper refusal replaced/lost nativeFull query");
                Check(ReferenceEquals(((RecastMapCache.Pin)Field(context,"Map")).Map,map)&&(NavSolveStatus)Field(context,"Result")==NavSolveStatus.Pending,"helper refusal lost map pin/pending state");
                rollback=true;Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(new{Case="helper-birth-rollback",Result="PASS",FirstRequest=((NavigationRequest[])Field(context,"Members"))[0].Request,NativeQueryIdentity=System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(query),MapBinding=((RecastMapCache.Pin)Field(context,"Map")).Map.Geometry.Binding,PhysicalAtDenial=physical,PhysicalAfterRollback=budget.PhysicalBytes,HeldAtDenial=held,HeldAfterRollback=budget.HeldBytes,AllPublicationFieldsNull=true,NativeFullQueryRetained=true}));
            };
            budget.AllocationAdmission=bytes=>{
                if(bytes!=3771304)return true;matches++;
                if(denials!=0){Check(rollback,"helper retry occurred before rollback rendezvous");return true;}
                context=Context49();Check(context!=null,"helper denial has no actual49 context");Empty(context);query=Field(context,"Query");map=((RecastMapCache.Pin)Field(context,"Map")).Map;
                Check(query!=null&&((RecastNavigationQuery)query).Status==RecastQueryStatus.Full&&(NavSolveStatus)Field(context,"Result")==NavSolveStatus.Pending,"helper denial is not nativeFull/Pending birth");
                physical=budget.PhysicalBytes;held=budget.HeldBytes;denials++;
                Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(new{Case="ONE-component-helper-birth-denial",Bytes=bytes,FalsePredicateCalls=denials,FirstRequest=((NavigationRequest[])Field(context,"Members"))[0].Request,NativeQueryIdentity=System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(query),NativeStatus="Full",MapBinding=((RecastMapCache.Pin)Field(context,"Map")).Map.Geometry.Binding,OrdinaryGame=false}));return false;
            };
        }
        internal void Verify(){Check(denials==1&&matches==2&&rollback,"helper denial/rollback/retry contract incomplete");Check(kernel.RecastLongQueries==2,"helper retry created additional native queries");Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(new{Case="helper-denial-component-qualified",FalsePredicateCalls=denials,HelperBirthAllocationAttempts=matches,kernel.RecastLongQueries,kernel.RecastAttachments,AttachmentsMeaning="attempts; not successful-members assertion",kernel.RecastIterations,Result="PASS"}));}
        public void Dispose(){budget.AllocationAdmission=null;kernel.BeforePrepare=null;}
    }
    sealed class CadenceObservation : IDisposable
    {
        readonly CertifiedRouteKernel kernel;readonly NavigationSession session;
        readonly System.Collections.Generic.Dictionary<long,long> work=new System.Collections.Generic.Dictionary<long,long>();
        long lastCommitted,lastSerial;int lastSpent;bool lastSolve;int observations,maxSlice,maxSpent;
        internal CadenceObservation(CertifiedRouteKernel k,NavigationSession s){kernel=k;session=s;Check(k.BeforePrepare==null,"cadence observer would replace hook");k.BeforePrepare=Observe;}
        void Observe(){
            long committed=kernel.RecastPreparationCommittedWork,delta=committed-lastCommitted;
            if(delta>0){Check(lastSolve&&delta<=256&&lastSpent+delta<=4096,"committed Recast slice/quantum overflow");work.TryGetValue(lastSerial,out long prior);work[lastSerial]=prior+delta;Check(work[lastSerial]<=4096,"committed quantum aggregate overflow");maxSlice=Math.Max(maxSlice,(int)delta);maxSpent=Math.Max(maxSpent,lastSpent+(int)delta);}
            var root=session.PinCommittedHostRoot();try{
                if(root!=null){var f=root.Frontier;Check(f.OriginalBudget==4,"saved actual quantum grant changed");if(f.Stage==CertifiedRouteKernel.PumpStage.Solve)Check(f.SolveWorkLimit==4096&&f.SolveConsumed<=4096,"actual Solve allowance changed");lastSolve=f.Stage==CertifiedRouteKernel.PumpStage.Solve;lastSpent=f.SolveConsumed;lastSerial=root.QuantumSerial;}
                else lastSolve=false;
            }finally{root?.Release();}
            lastCommitted=committed;observations++;
        }
        internal void Verify(){Observe();Check(work.Values.Sum()==kernel.RecastPreparationCommittedWork&&work.Count>0&&maxSlice>0,"cadence work transcript incomplete");Check(kernel.RecastPreparationRefusals==0,"unexplained cadence refusal");Check(kernel.RecastLongQueries==2,"cadence created extra native group queries");Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(new{Case="actual-profile4-prep-cadence",Result="PASS",Observations=observations,OriginalBudget=4,SolveLimit=4096,MaxCommittedSlice=maxSlice,MaxSpentInQuantum=maxSpent,PrepQuanta=work.Count,CommittedWork=work.Values.Sum(),kernel.RecastPreparationRefusals,kernel.RecastLongQueries,kernel.RecastIterations}));}
        public void Dispose(){kernel.BeforePrepare=null;}
    }
    const string RecoveryDirectory=".local/recast-formation/bent-recovery1";
    static void WriteNew(string path,byte[] bytes){using var output=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.Read);output.Write(bytes,0,bytes.Length);}
    static string Sha(byte[] bytes)=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
    static NavCorridorDescriptor Retained(NavigationSession session,int id)
    {
        var actor=session.Crowd.Units.Single(a=>a.Id==id);var owner=actor.StorageOwner.InstalledExecution as NavigationInstalledExecutionOwner;
        Check(owner?.Corridor?.Corridor!=null,"member lacks retained paid execution: "+id);return owner.Corridor.Corridor;
    }
    internal static NavigationSession FromSnapshot(NavigationSessionState state,PlayableProfile profile)
    {
        NavGeometry Geometry(NavigationGeometryState g)=>new NavGeometry(g.HalfExtent,g.Obstacles,g.Revision);
        var geometry=Geometry(state.Geometry);var navigationGeometry=state.NavigationGeometryIndex==0?geometry:Geometry(state.NavigationGeometry);
        var result=new NavigationSession(state.Generation,geometry,profile.Navigation,navigationGeometry,profile.AuthoredMap,null,null,replayOnlyProjection:true){UsesRecastGroundRoutes=true};
        try{result.ConfigureMarch(profile.GroupMarchMaximumStretch,profile.Revision);result.RestoreState(state);return result;}
        catch{result.ReleaseCommittedHostRoot();result.DestroyNavigationAuthority();throw;}
    }
    static void VerifyBentReady(NavigationSession source,PlayableProfile profile,int[] ids,byte[] bytes,string outputDirectory,long producerQueries,bool offline)
    {
        Directory.CreateDirectory(outputDirectory);using var saved=WorldWire.Unpack(bytes,WorldWire.ReadNavigationSessionState);NavigationSession restored=null;
        try{
            restored=FromSnapshot(saved,profile);
            byte[] back;using(var recaptured=restored.CaptureState())back=WorldWire.Pack(w=>WorldWire.Write(w,recaptured));
            WriteNew(Path.Combine(outputDirectory,"recapture.nav.bin"),back);
            WriteNew(Path.Combine(outputDirectory,"recapture.json"),System.Text.Encoding.UTF8.GetBytes(Newtonsoft.Json.JsonConvert.SerializeObject(new{Source=Environment.GetEnvironmentVariable("SPACEWARS_QA_SOURCE"),InputSHA256=Sha(bytes),RecaptureSHA256=Sha(back),InputBytes=bytes.Length,RecaptureBytes=back.Length,Offline=offline,ProducerQueries=producerQueries,NewQueries=0})));
            Check(bytes.SequenceEqual(back),"actual bent Ready capture/restore/recapture differs");
            Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(new{Case="persisted-bent-byteexact",Result="PASS",SHA256=Sha(bytes),Bytes=bytes.Length,Offline=offline,NewQueries=0}));
            Check(ids.Length==50&&producerQueries==2,"frozen fifty/two producer group queries changed");
            Check(Retained(source,24).RecastPlan.OriginLegs>1,"actual member24 origin is not a newly bent Ready transcript");
            var terminal=Retained(source,21);Check(terminal.Legs.Count-terminal.RecastPlan.TerminalLeg>1,"actual member21 terminal is not a newly bent Ready transcript");
            foreach(int id in ids){
                var a=Retained(source,id);var b=Retained(restored,id);var p=a.RecastPlan;var q=b.RecastPlan;
                Check(p.Join==q.Join&&p.Exit==q.Exit&&p.OriginLegs==q.OriginLegs&&p.TerminalLeg==q.TerminalLeg&&p.Spine.Binding==q.Spine.Binding&&a.Legs.Count==b.Legs.Count&&a.Endpoint.Equals(b.Endpoint),"accepted bent correspondence/slot changed: "+id);
                for(int i=0;i<a.Legs.Count;i++){var x=a.Legs[i];var y=b.Legs[i];Check(x.From.Equals(y.From)&&x.To.Equals(y.To)&&x.Kind==y.Kind&&x.PortalId==y.PortalId&&x.Cost==y.Cost,"accepted bent leg changed: "+id);}
                var m=source.GroupFor(id).Members.Single(x=>x.Entity==id);var n=restored.GroupFor(id).Members.Single(x=>x.Entity==id);
                Check(m.Slot==n.Slot&&m.Goal.Equals(n.Goal)&&Equals(m.GoalLocation,n.GoalLocation)&&Equals(m.Recast,n.Recast),"accepted member own slot/role/progress changed: "+id);
                var xActor=source.Crowd.Units.Single(u=>u.Id==id);var yActor=restored.Crowd.Units.Single(u=>u.Id==id);
                bool active=source.InstalledExecutionFor(id)!=null,activeRestored=restored.InstalledExecutionFor(id)!=null;
                Check(xActor.GoalLocation.Equals(yActor.GoalLocation)&&xActor.Position.Equals(yActor.Position)&&xActor.Moving==yActor.Moving&&xActor.Outcome==yActor.Outcome&&active==activeRestored,"saved actor/active eligibility changed: "+id);
                Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(new{Case="retained-bent-member",Id=id,xActor.Moving,xActor.Outcome,Active=active,RestoredActive=activeRestored,p.Join,p.Exit,p.OriginLegs,p.TerminalLeg,Legs=a.Legs.Count,Slot=m.Slot,Result="PASS"}));
            }
            Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(new{Case="actual-bent-Ready-roundtrip",Result="PASS",Members=ids.Length,OriginMember=24,TerminalMember=21,Bytes=bytes.Length,SHA256=Sha(bytes),ProducerQueries=producerQueries,NewQueries=0,GameReplays=0,Offline=offline}));
        }finally{if(restored!=null){restored.ReleaseCommittedHostRoot();restored.DestroyNavigationAuthority();var b=restored.PhysicalHostBudget;Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(new{Case="bent-restored-final-census",b.PhysicalBytes,b.HeldBytes,b.ActiveScopes}));Check(b.PhysicalBytes==0&&b.HeldBytes==0&&b.ActiveScopes==0,"bent restored owner leak");}}
    }
    static void BentReadyRoundtrip(NavigationSession source,PlayableProfile profile,int[] ids,long producerQueries,bool recovery)
    {
        string directory=recovery?RecoveryDirectory:".local/recast-formation/bent-ready-observer";Directory.CreateDirectory(directory);
        using var captured=source.CaptureState();var bytes=WorldWire.Pack(w=>WorldWire.Write(w,captured));
        WriteNew(Path.Combine(directory,"source.nav.bin"),bytes);
        WriteNew(Path.Combine(directory,"source.json"),System.Text.Encoding.UTF8.GetBytes(Newtonsoft.Json.JsonConvert.SerializeObject(new{Source=Environment.GetEnvironmentVariable("SPACEWARS_QA_SOURCE"),SHA256=Sha(bytes),Bytes=bytes.Length,Ids=ids,ProducerQueries=producerQueries,profile.ProfileId,profile.Revision,Map=profile.AuthoredMap.Id,MapRevision=profile.AuthoredMap.Revision,RecastFormationInputs.Seed,RecastFormationInputs.Generation,Goal=new NavPoint(-23.2,29.4),Qualification="explicit bent recovery component; ordinary0spent/1remaining; no helper faultcontrol",ProductChanged=false})));
        Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(new{Case="bent-source-persisted-before-observers",Path=Path.Combine(directory,"source.nav.bin"),SHA256=Sha(bytes),Bytes=bytes.Length,Source=Environment.GetEnvironmentVariable("SPACEWARS_QA_SOURCE")}));
        VerifyBentReady(source,profile,ids,bytes,directory,producerQueries,false);
    }
    internal static void BentOffline()
    {
        var meta=Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(Path.Combine(RecoveryDirectory,"source.json")));var bytes=File.ReadAllBytes(Path.Combine(RecoveryDirectory,"source.nav.bin"));
        Check(Sha(bytes)==(string)meta["SHA256"],"saved bent source SHA changed");var profile=PlayableProfile.RecastGroupCorridorDefault;
        Check(profile.ProfileId==(string)meta["ProfileId"]&&profile.Revision==(int)meta["Revision"],"saved bent profile changed");
        using var state=WorldWire.Unpack(bytes,WorldWire.ReadNavigationSessionState);NavigationSession source=null;ulong native=RecastNative.sw_recast_allocation_count();
        try{source=FromSnapshot(state,profile);VerifyBentReady(source,profile,meta["Ids"].ToObject<int[]>(),bytes,".local/recast-formation/bent-offline1",(long)meta["ProducerQueries"],true);}
        finally{if(source!=null){source.ReleaseCommittedHostRoot();source.DestroyNavigationAuthority();var b=source.PhysicalHostBudget;Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(new{Case="offline-bent-source-census",b.PhysicalBytes,b.HeldBytes,b.ActiveScopes,NativeBefore=native,NativeAfter=RecastNative.sw_recast_allocation_count()}));Check(b.PhysicalBytes==0&&b.HeldBytes==0&&b.ActiveScopes==0&&native==RecastNative.sw_recast_allocation_count(),"offline bent source leak");}}
    }
    internal static void Run(bool attachmentDiagnostic=false,bool connectivityProbe=false,bool recovery=false,bool cadence=false)
    {
        if(recovery)Check(!File.Exists(Path.Combine(RecoveryDirectory,"source.nav.bin")),"persisted source already exists; use offline observer, not another producer");
        HelperBirthObservation helperBirth=null;CadenceObservation cadenceObserver=null;
        var p=PlayableProfile.RecastGroupCorridorDefault;var map=(ThreeCrossingsMap)p.AuthoredMap;
        var config=RecastFormationInputs.Create(p,out var scenario,out var rejected);
        ulong native=RecastNative.sw_recast_allocation_count();PlayableAuthorityTick world=null;CertifiedRouteWorker worker=null;var kernel=new CertifiedRouteKernel();
        try{
            world=new PlayableAuthorityTick(config,RecastFormationInputs.Generation);var s=world.Domain.Navigation;
            var full=world.Domain.Snapshot(0,RuntimeStatus.Starting,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,config.Seed);
            var ownAnchor=config.Starts[config.Assignments[0]].ExplorerAnchor;
            var ownAuto=full.Entities.Single(a=>a.Owner==PlayableOwner.Player&&a.Position.Equals(ownAnchor));
            var scenarioIds=full.Entities.Where(a=>a.Owner==PlayableOwner.Player&&scenario.Any(x=>x.Position.Equals(a.Position))).Select(a=>a.Id).ToArray();
            var ids=scenarioIds.Concat(new[]{ownAuto.Id}).OrderBy(x=>x).ToArray();var parked=s.Crowd.Units.Where(a=>!ids.Contains(a.Id)).Select(a=>new{a.Id,a.Position,a.Location}).ToArray();
            Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(new{Case="reconciled-global-birth",FullDomain=full.Entities.Count,FullNav=s.Crowd.Units.Count,Viewer=world.Latest.Entities.Count,Population=world.Latest.Population,Scenario=scenario,ScenarioIds=scenarioIds,Selected=ids,OwnAutomatic=ownAuto.Id,Parked=parked,Assignments=config.Assignments,config.Roster,rejected}));
            Check(full.Entities.Count==51&&s.Crowd.Units.Count==51&&scenario.Length==49&&scenarioIds.Length==49&&ids.Length==50&&ids.Distinct().Count()==50&&parked.Length==1,"reconciled global population identity failed");
            Check(world.Latest.Population.Living==100&&world.Latest.Population.Reserved==0&&world.Latest.Population.Capacity==100&&!world.Scheduler.Owners.Any(),"reconciled own population/cap/AI mismatch");
            Check(ids.All(id=>full.Entities.Single(a=>a.Id==id).Owner==PlayableOwner.Player),"selected member is not normal command owner");
            Check(world.TryAdvance()&&world.Domain.Outcome==PlayableMatchOutcome.Playing,"normal two-team roster cannot continue before command");
            NavPoint goal=default;NavLocation typed=default;NavPoint[] slots=null;bool found=false;
            var candidateCenters=(new[]{new NavPoint(-28,38),new NavPoint(-24,26),new NavPoint(-28,24),new NavPoint(-30,20),new NavPoint(-26,30),new NavPoint(-30,28),new NavPoint(-34,22)}).Concat(from col in Enumerable.Range(0,12) from row in Enumerable.Range(0,13) select new NavPoint(-(16+col*p.Navigation.ArrivalSlotSpacing),15+row*p.Navigation.ArrivalSlotSpacing)).Distinct();
            foreach(var center in candidateCenters){
                string reason=null;NavLocation candidate=default;var trial=s.AllocateArrivalSlots(center,ids);
                if(!map.TryLocate(center,p.ExplorerCollisionRadius,out candidate)||candidate.SurfaceId!="bank/west")reason="center not west bank";
                else if(trial.Length!=50)reason="allocator did not supply fifty slots";
                else for(int i=0;i<trial.Length&&reason==null;i++){
                    if(!map.TryLocate(trial[i],p.ExplorerCollisionRadius,out var at)||at.SurfaceId!="bank/west"||!world.Domain.Geometry.IsFree(trial[i],p.ExplorerCollisionRadius))reason="slot not free typed west bank "+i;
                    else if(parked.Any(a=>RecastFormationInputs.Distance(trial[i],a.Position)<p.Navigation.ArrivalSlotSpacing))reason="slot overlaps parked actor";
                    else if(config.Starts.Any(a=>RecastFormationInputs.Distance(trial[i],a.Position)<=p.ExplorerRange+p.HeadquartersFootprintRadius+p.ExplorerCollisionRadius))reason="slot enters home combat/contact zone";
                    for(int j=0;j<i&&reason==null;j++)if(RecastFormationInputs.Distance(trial[i],trial[j])+1e-9<p.Navigation.ArrivalSlotSpacing)reason="slot spacing differs";
                }
                Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(new{Case="pure-goal-candidate",Center=center,Slots=trial,Accepted=reason==null,Reason=reason}));
                if(reason==null){goal=center;typed=candidate;slots=trial;found=true;break;}
            }
            Check(found,"no pure source-valid fifty-slot goal; no normal Move attempted");
            Check(goal.Equals(new NavPoint(-23.2,29.4)),"frozen actual goal changed; no alternate command");

#if RECAST_ATTACHMENT_PHASE_AUDIT
            RecastAttachmentPhaseAudit.Profile(p);RecastAttachmentPhaseAudit.CommandPositions(s,ids);
#endif
            var receipt=world.Apply(new PlayableCommand(RecastFormationInputs.Generation,1,"player-1",PlayableCommandKind.Move,ids,goal).WithResolvedLocation(typed));
            Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(new{Case="one-normal-component-command",receipt.Status,receipt.Message,Goal=goal}));
            Check(receipt.Status==PlayableCommandStatus.Applied,"normal component command refused: "+receipt.Status+" "+receipt.Message);
            ProbeInput copied=null;if(!attachmentDiagnostic&&!recovery&&!cadence)helperBirth=new HelperBirthObservation(kernel,s.PhysicalHostBudget);if(attachmentDiagnostic)InstallContextObservation(kernel,connectivityProbe?new Action<ProbeInput>(x=>{Check(copied==null,"isolated probe copied more than one member");copied=x;}):null);
            if(cadence)cadenceObserver=new CadenceObservation(kernel,s);
            worker=new CertifiedRouteWorker(world.NavigationBinding,8,kernel);var timer=Stopwatch.StartNew();
            while(s.PendingCount>0&&timer.Elapsed.TotalSeconds<60&&(!cadence||world.Tick<RecastFormationInputs.Horizon)){world.TryAdvance();
#if RECAST_ATTACHMENT_PHASE_AUDIT
RecastAttachmentPhaseAudit.CheckPositions(s,ids,world.Tick);
#endif
if(worker.Publication.Failure!=null)throw new InvalidOperationException(worker.Publication.Failure);System.Threading.Thread.Yield();}
            Check(s.PendingCount==0,"bounded preflight atomic arrival/realworker did not settle");Check(world.Domain.Outcome==PlayableMatchOutcome.Playing,"normal roster ended during preparation");
            if(attachmentDiagnostic){worker.Dispose();Check(System.Threading.SpinWait.SpinUntil(()=>worker.IsStopped,5000),"diagnostic worker stop ACK");using(var capture=s.CaptureState())Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(new{Case="exact-technical-failures",Failures=capture.TechnicalFailures.Select(f=>new{f.Entity,f.Request,Status=f.Status.ToString()})}));}
            if(connectivityProbe){Check(copied!=null,"exact paid member24 input was unavailable; no probe query");IsolatedConnectivity(copied,p);}
            var actors=ids.Select(id=>s.Crowd.Units.Single(a=>a.Id==id)).ToArray();Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(new{Case="actual-ready-boundary",Tick=world.Tick,s.AppliedResults,s.RejectedResults,s.PendingCount,kernel.RecastLongQueries,kernel.RecastAttachments,kernel.RecastIterations,Actors=actors.Select(a=>new{a.Id,a.Position,a.Location,a.GoalLocation,a.Moving,a.Outcome,a.InstalledRequest,a.LastFailure,a.FailedRequest}),Groups=ids.Select(id=>s.GroupFor(id)).Where(x=>x!=null).GroupBy(x=>x.GroupId).Select(x=>x.First())}));Check(actors.All(a=>a.InstalledRequest>0&&a.GoalLocation.HasValue&&a.Outcome!=NavigationOutcome.Rejected),"not all fifty actors own paid Ready decisions");
            var plans=actors.Select(a=>(a.StorageOwner.InstalledExecution as NavigationInstalledExecutionOwner)?.Corridor?.Corridor?.RecastPlan).ToArray();Check(plans.All(x=>x!=null),"member lacks paid shared-spine attachment");
            var bridges=plans.SelectMany(x=>x.Spine.Legs.Select(l=>l.From.SurfaceId).Concat(x.Spine.Legs.Select(l=>l.To.SurfaceId))).Where(x=>x.StartsWith("bridge/",StringComparison.Ordinal)).Distinct().ToArray();Check(bridges.Contains("bridge/north"),"paid path does not use actual north bridge");
            Check(parked.All(x=>s.Crowd.Units.Single(a=>a.Id==x.Id).Position.Equals(x.Position)),"component moved parked opponent");
            helperBirth?.Verify();cadenceObserver?.Verify();if(cadence)Check(world.Tick<=RecastFormationInputs.Horizon,"Ready outside frozen2700 horizon");
#if RECAST_ATTACHMENT_PHASE_AUDIT
            RecastAttachmentPhaseAudit.Ready(kernel,world.Tick);
#endif

            var counters=new{kernel.RecastLongQueries,kernel.RecastAttachments,kernel.RecastIterations,LocalAttachmentWork=((RecastGroupPreparation)typeof(CertifiedRouteKernel).GetField("recastPreparation",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(kernel))?.LocalAttachmentWork??0};
            timer.Stop();worker.Dispose();Check(System.Threading.SpinWait.SpinUntil(()=>worker.IsStopped,5000),"bent Ready capture worker stop ACK");if(!cadence)BentReadyRoundtrip(s,p,ids,counters.RecastLongQueries,recovery);
            var result=new{Result="PASS",Case="actual-factory-slot-paid-path",RecastFormationInputs.Seed,RecastFormationInputs.Generation,Goal=goal,Scenario=scenario,Ids=ids,Total=51,Qualifying=50,OwnAuto=ownAuto.Id,Parked=parked,Population=full.Population,Outcome=world.Domain.Outcome,Tick=world.Tick,Goals=actors.Select(a=>new{a.Id,a.GoalLocation,a.InstalledRequest}),Bridges=bridges,Spines=plans.Select(x=>x.Spine.Binding).Distinct().ToArray(),counters.RecastLongQueries,counters.RecastAttachments,counters.RecastIterations,counters.LocalAttachmentWork,ReadyElapsedMs=timer.Elapsed.TotalMilliseconds,Map=map.Id,map.Revision,p.ProfileId,ProfileRevision=p.Revision,Binding=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(WorldWire.Binding(p))).ToLowerInvariant()};
            var json=Newtonsoft.Json.JsonConvert.SerializeObject(result,Newtonsoft.Json.Formatting.Indented);Directory.CreateDirectory(".local/recast-formation");File.WriteAllText(
#if RECAST_ATTACHMENT_PHASE_AUDIT
Path.Combine(RecastAttachmentPhaseAudit.Output,"preflight-recipe.json"),
#else
".local/recast-formation/preflight-recipe.json",
#endif
json);Console.WriteLine(json);
        }finally{
            worker?.Dispose();if(worker!=null)Check(System.Threading.SpinWait.SpinUntil(()=>worker.IsStopped,5000),"preflight worker stop ACK");else kernel.Clear();
            helperBirth?.Dispose();cadenceObserver?.Dispose();
            if(world!=null){world.Stop();world.Domain.Navigation.ReleaseCommittedHostRoot();world.Domain.DestroyOwnedNavigation();var b=world.Domain.Navigation.PhysicalHostBudget;Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(new{Case="preflight-final-census",WorkerStopped=worker==null||worker.IsStopped,b.PhysicalBytes,b.HeldBytes,b.ActiveScopes,NativeBefore=native,NativeAfter=RecastNative.sw_recast_allocation_count()}));Check(b.PhysicalBytes==0&&b.HeldBytes==0&&b.ActiveScopes==0,"preflight physical owner leak");}
            Check(RecastNative.sw_recast_allocation_count()==native,"preflight native leak");
        }
    }
}
