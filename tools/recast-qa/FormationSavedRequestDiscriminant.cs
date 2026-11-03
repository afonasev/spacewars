using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Spacewars.Runtime;
using Spacewars.Simulation;

// One explicitly selected, output-only diagnostic. Never entered by ordinary QA.
internal static class FormationSavedRequestDiscriminant
{
    static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    static string Sha(byte[] bytes)=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
    static void Save(string directory,string name,object value)=>File.WriteAllText(Path.Combine(directory,name),JsonConvert.SerializeObject(value,Formatting.Indented));
    static bool Equal(object a,object b)=>JToken.DeepEquals(JToken.FromObject(a),JToken.FromObject(b));
    static NavigationGeometryState Geometry(NavGeometry value)=>new NavigationGeometryState{HalfExtent=value.HalfExtent,Revision=value.Revision,Obstacles=value.Obstacles.ToArray()};

    internal static void Run(bool savedCorridor=false,bool physicalFactsOnly=false,bool paidSpine=false)
    {
        var input=Environment.GetEnvironmentVariable("RECAST_CONTINUATION_INPUT")??throw new ArgumentException("Input required");
        var output=Environment.GetEnvironmentVariable("RECAST_CONTINUATION_OUTPUT")??throw new ArgumentException("Output required");
        Require(!Directory.Exists(output),"Output already exists: never repeat a spent query");Directory.CreateDirectory(output);
        var bytes=File.ReadAllBytes(Path.Combine(input,"terminal-nav14.bin"));
        Require(bytes.Length==270652&&Sha(bytes)=="0053828cc28cf636db4c6701cfbf8bd53db025d19ba799909e9a2ba7a279a751","Actual terminal input changed");
        using var saved=WorldWire.Unpack(bytes,WorldWire.ReadNavigationSessionState);
        var profile=PlayableProfile.RecastGroupCorridorDefault;
        Require(profile.AuthoredMap.Revision==5,"Map revision changed");
        NavigationSession session=null;NavigationHostRoot root=null;int queries=0;
        ulong nativeBefore=RecastNative.sw_recast_allocation_count();long faultsBefore=RecastNative.ReleaseFaults;
        try{
            session=FormationPreflight.FromSnapshot(saved,profile);
            using(var restored=session.CaptureState())Require(bytes.SequenceEqual(WorldWire.Pack(w=>WorldWire.Write(w,restored))),"Normal restore is not exact");
            Require(saved.Crowd.Units.Length==101&&saved.Requests.Length==2,"Actual actor/request census changed");
            var dto=saved.Requests.Single(q=>q.Entity==4);root=session.PinCommittedHostRoot();
            var row=root.Rows.Values.Concat(root.Delivered.Values).OfType<NavigationHostRow>().Single(q=>q.Request.Entity==4);var request=row.Request;
            Require(dto.Session==805&&dto.Request==1&&dto.Order==1&&dto.Entity==4&&dto.UsesCurrentGeometry&&dto.UsesCurrentBaseGeometry&&dto.GeometryIndex==0&&dto.BaseGeometryIndex==0,"Original request identity/index changed");
            Require(request.Session==dto.Session&&request.Request==dto.Request&&request.Order==dto.Order&&request.Entity==dto.Entity&&Equal(request.MemberIdentity,dto.MemberIdentity)&&request.Start.Equals(dto.Start)&&request.Goal.Equals(dto.Goal)&&request.StartLocation.Equals(dto.StartLocation)&&request.GoalLocation.Equals(dto.GoalLocation),"Saved DTO vs restored host input mismatch");
            Require(request.StartLocation.Value.Equals(new NavLocation(new NavPoint(36,-46),"deck/1/-1"))&&request.GoalLocation.Value.Equals(new NavLocation(new NavPoint(34,58.2),"bank/east")),"Original typed endpoints changed");
            Require(request.Profile.ProfileId==dto.ProfileId&&request.Profile.Revision==dto.ProfileRevision&&request.Profile.CaptureWireValues().SequenceEqual(dto.ProfileValues)&&dto.ProfileId=="unity-owner-research-queue-u6-v1"&&dto.ProfileRevision==1&&dto.ProfileValues.SequenceEqual(new double[]{56,85,-40,.3,3.6,5,.2,.58,7,7.5,.8,.15,3.6,.08}),"Profile binding changed");
            Require(request.HeldIdentity==dto.HeldIdentity&&dto.HeldIdentity==""&&Equal(Geometry(request.Geometry),dto.Geometry)&&Equal(Geometry(request.BaseGeometry),dto.BaseGeometry)&&Equal(dto.Geometry,saved.Geometry)&&Equal(dto.BaseGeometry,saved.Geometry)&&request.Geometry.Revision==2&&request.Geometry.HalfExtent==64&&request.Geometry.Obstacles.Count==79,"Geometry/Held binding changed");
            Require(request.CertifiedProviderBinding==dto.CertifiedProviderBinding&&row.Provider.Binding==dto.CertifiedProviderBinding&&dto.CertifiedProviderBinding=="1493993adb1e390bcf214aefea2bf6407bf129f62b44c48340fea9a5d1522d28"&&row.Scheduled.Profile.ExactProfileBinding=="1b234b7ab95010fc8be4f9747dcf93f236e4b09a6f9c79e0aedc1477103723b5","Provider/scheduled profile binding changed");
            Require(session.TechnicalFailureFor(4)==NavSolveStatus.BlockedConnector&&row.Answer?.Status==NavSolveStatus.BlockedConnector&&row.Solved==null,"Actual failure row changed");
            Save(output,"input-guards.json",new{Result="PASS",InputSHA256=Sha(bytes),Bytes=bytes.Length,OriginalDTO=dto,RestoredGeometry=Geometry(request.Geometry),RestoredBaseGeometry=Geometry(request.BaseGeometry),Provider=row.Provider.Binding,ScheduledProfile=row.Scheduled.Profile.ExactProfileBinding,MapId=profile.AuthoredMap.Id,MapRevision=profile.AuthoredMap.Revision,HistoricalNativeCorridor="ABSENT",Meaning="Exact retained original request, not reconstructed from terminal poses"});
            if(physicalFactsOnly){
                ChordFacts(output,row.Provider,request);
                using var verified=session.CaptureState();Require(bytes.SequenceEqual(WorldWire.Pack(w=>WorldWire.Write(w,verified))),"Physical predicate diagnostic changed saved bytes");
                Save(output,"result.json",new{Result="OBSERVED physical chord facts",ExactBeforeAfterSavedBytes=true,NativeQueries=0,HelperCreated=false,HelperSteps=0,WorldCreated=false,Steps=0});return;
            }
            Require(session.TryOpenPhysicalHostScope(NavigationPreparationKind.Solve,805,session.Admission.PublicationIdentity,out var scope),"Paid diagnostic scope refused");
            using(scope){
                var reservation=scope.OwnedReservation();
                using var cache=new RecastMapCache();
                using var map=cache.Acquire(profile.AuthoredMap,request.Geometry,request.Profile,request.HeldIdentity,reservation,null);
                var geometry=map.Map.Geometry;
                Save(output,"map-binding.json",new{geometry.Binding,geometry.TriangleCount,geometry.CellSize,geometry.CellHeight,geometry.Radius,MapId=profile.AuthoredMap.Id,MapRevision=profile.AuthoredMap.Revision,NativeStart=geometry.NativeEndpoint(request.StartLocation.Value,request.GoalLocation.Value),NativeGoal=geometry.NativeEndpoint(request.GoalLocation.Value,request.StartLocation.Value),GeometrySHA256=Sha(WorldWire.Pack(w=>WorldWire.Write(w,Geometry(request.Geometry)))),QueryCountBefore=queries,Meaning="Current derived map from exact original host inputs through production paid cache; historical native map/corridor bytes absent"});
                using(var memory=RecastNative.Memory(reservation)){
                    RecastRoutePoint[] corridor;
                    if(savedCorridor){
                        var path=Environment.GetEnvironmentVariable("RECAST_SAVED_CORRIDOR_INPUT")??throw new ArgumentException("Saved corridor required");var raw=File.ReadAllBytes(path);var document=JObject.Parse(System.Text.Encoding.UTF8.GetString(raw));
                        Require((string)document["HistoricalNativeCorridor"]=="ABSENT"&&(string)document["Status"]=="Full"&&(int)document["QueryNumber"]==1&&(string)document["MapBinding"]==geometry.Binding,"Saved derived corridor binding mismatch");
                        corridor=document["TypedCorridor"].ToObject<RecastRoutePoint[]>();Require(corridor.Length==37,"Saved derived corridor count changed");
                        Save(output,"saved-corridor-input.json",new{Path=path,SHA256=Sha(raw),NativeQueries=0,MapBinding=geometry.Binding,TypedCorridor=corridor,Meaning="Reuse ONLY persisted ONE-derived-query output; no new query"});
                    }else{
                        Save(output,"query-entry.json",new{QueryNumber=1,Meaning="ONE NEW current derived query; historical native corridor ABSENT"});queries++;
                        using var query=map.Map.Query(request.StartLocation.Value,request.GoalLocation.Value);
                        while(query.Status==RecastQueryStatus.Pending)query.Step(32);
                        corridor=query.Status==RecastQueryStatus.Full?query.CopyTypedRoute():Array.Empty<RecastRoutePoint>();
                        // Persist the untouched typed output before production interpretation/assertions.
                        Save(output,"current-derived-native-corridor.json",new{Meaning="NEW current derived query on exact saved request; NOT historical native transcript",HistoricalNativeCorridor="ABSENT",QueryNumber=queries,Status=query.Status.ToString(),query.Iterations,MapBinding=geometry.Binding,TypedCorridor=corridor});
                        Require(query.Status==RecastQueryStatus.Full,"Derived native result is not Full; no spine/attachment interpretation");
                    }
                    long scratch=checked(8192L+8L*65536*(NavStorageLayout.ValueBytes(typeof(NavTypedLeg))+NavStorageLayout.ValueBytes(typeof(RecastRoutePoint)))+128L);
                    using(var workspace=NavStorageScratch.Reserve(reservation,scratch)){
                        ILayeredNavigationProvider provider=null;RecastPayload completion=null;
                        try{
                            if(row.Provider is PartitionNavigationProvider partition)provider=PartitionNavigationProvider.CopyOwned(partition,reservation);
                            else if(row.Provider is LayeredNavigationProvider layered)provider=LayeredNavigationProvider.CopyOwned(layered,reservation);
                            else throw new InvalidOperationException("Unexpected owning provider type");
                            if(paidSpine)completion=PaidCompletion(output,provider,request,corridor,geometry,reservation);
                            var effective=completion==null?corridor:(RecastRoutePoint[])completion.Value;
                            var spine=RecastGroupPreparation.BuildSpine(provider,new[]{request},effective,geometry);
                            var attached=spine==null?null:RecastGroupPreparation.Attach(provider,request,spine,geometry);
                            Save(output,"production-stages.json",new{BuildSpineNull=spine==null,DirectAttachInvoked=spine!=null,DirectAttachNull=spine==null?(bool?)null:attached==null,Spine=spine==null?null:new{spine.Binding,spine.MapBinding,spine.Fan,spine.Stops,spine.FanCenter,spine.FanRadius,spine.Legs},Attached=attached==null?null:new{attached.Waypoints,attached.Legs},AttachmentFieldSolve=false,Steps=0,WorldCreated=false,WorkerCreated=false,NativeQueries=queries});
                            if(savedCorridor)VerifyRepair(output,provider,request,corridor,geometry,spine,attached,reservation,paidSpine);
                            if(!savedCorridor){
                            var method=typeof(RecastGroupPreparation).GetMethod("Trace",BindingFlags.Static|BindingFlags.NonPublic);
                            bool Trace(NavLocation from,NavLocation to){var args=new object[]{provider,request,from,to,null};return (bool)method.Invoke(null,args);}
                            var cursor=request.StartLocation.Value;var observations=new List<object>();var accepted=new List<NavLocation>{cursor};
                            for(int i=0;i<corridor.Length;i++){
                                var corner=corridor[i];bool trace=Trace(cursor,corner.Location);double authorityHeight=geometry.Point(corner.Location)[1];bool height=Math.Abs(authorityHeight-corner.Height)<=2*geometry.CellHeight+1e-6;
                                var support=profile.AuthoredMap.SupportAt(corner.Location.Position);
                                var clearance=new NavClearanceProfile("ground",1,"ground",0,request.Profile.Radius);
                                NavLocation reached=default;IReadOnlyList<NavTypedLeg> physicalLegs=null;
                                bool physical=provider is PartitionNavigationProvider p?p.TryTrace(cursor,corner.Location.Position,clearance,out reached,out physicalLegs):RecastLayerTrace.Try(provider,cursor,corner.Location,clearance,out physicalLegs);
                                observations.Add(new{Index=i,From=cursor,Corner=corner,ProductionTrace=trace,GeometrySegment=request.Geometry.SegmentFree(cursor.Position,corner.Location.Position,request.Profile.Radius),PhysicalTrace=physical,PhysicalReached=reached,PhysicalLegs=physicalLegs,AuthorityHeight=authorityHeight,HeightDelta=Math.Abs(authorityHeight-corner.Height),HeightBound=2*geometry.CellHeight+1e-6,HeightAccepted=height,PhysicalSupportSurface=support?.SurfaceId,PhysicalSupportHeight=support?.HeightAt(corner.Location.Position),Accepted=trace&&height});
                                if(trace&&height&&!cursor.Equals(corner.Location)){cursor=corner.Location;accepted.Add(cursor);}
                            }
                            var joins=spine==null?null:spine.Stops.Where(n=>n<=spine.Fan).Select(n=>new{Node=n,Location=spine.Node(n),Trace=Trace(request.StartLocation.Value,spine.Node(n))}).ToArray();
                            Save(output,"corner-trace-facts.json",new{Meaning="Source interpretation of SAME persisted derived corridor using exact production Trace; no extra query/search/field solve",Corners=observations,Accepted=accepted,TerminalCornerAccepted=cursor.Equals(corridor[corridor.Length-1].Location),GoalTailSameSurface=cursor.SurfaceId==request.GoalLocation.Value.SurfaceId,GoalTailTrace=Trace(cursor,request.GoalLocation.Value),OriginJoins=joins});
                            }
                        }finally{completion?.Dispose();(provider as INavStorageFacade)?.StorageRoot.Release();}
                    }
                }
            }
            using(var recapture=session.CaptureState())Require(bytes.SequenceEqual(WorldWire.Pack(w=>WorldWire.Write(w,recapture))),"Diagnostic changed authoritative saved bytes");
            Save(output,"result.json",new{Result=savedCorridor?"PASS saved-corridor affected checks":"OBSERVED",ExactBeforeAfterSavedBytes=true,NativeQueries=queries,Steps=0,WorldCreated=false,WorkerCreated=false,HistoricalNativeCorridor="ABSENT",OrdinaryAuthorized=false,Next="ROOT source/recipe review before ordinary"});
        }finally{
            root?.Release();
            if(session!=null){
                try{using var final=session.CaptureState();Save(output,"final-current-audit.json",new{Exact=bytes.SequenceEqual(WorldWire.Pack(w=>WorldWire.Write(w,final))),Meaning="Actual final capture including failure paths"});}catch(Exception error){Save(output,"final-current-audit.json",new{Result="NotRun",Error=error.ToString()});}
                session.ReleaseCommittedHostRoot();session.DestroyNavigationAuthority();var budget=session.PhysicalHostBudget;var nativeAfter=RecastNative.sw_recast_allocation_count();var faultsAfter=RecastNative.ReleaseFaults;Save(output,"cleanup.json",new{budget.PhysicalBytes,budget.HeldBytes,budget.ActiveScopes,NativeBefore=nativeBefore,NativeAfter=nativeAfter,ReleaseFaultsBefore=faultsBefore,ReleaseFaultsAfter=faultsAfter,NativeQueriesAttempted=queries});Require(budget.PhysicalBytes==0&&budget.HeldBytes==0&&budget.ActiveScopes==0&&nativeBefore==nativeAfter&&faultsBefore==faultsAfter,"Diagnostic ownership/native cleanup mismatch");}
        }
    }
    static void ChordFacts(string output,ILayeredNavigationProvider provider,NavigationRequest request)
    {
        var path=Environment.GetEnvironmentVariable("RECAST_SAVED_CORRIDOR_INPUT")??throw new ArgumentException("Saved corridor required");var raw=File.ReadAllBytes(path);
        Require(Sha(raw)=="dc1433434e9b24ac5b0eeb1da4ac395276a341ddb7b4b498f6d95ce0f91d834d","Exact saved corridor changed");
        var corridor=JObject.Parse(System.Text.Encoding.UTF8.GetString(raw))["TypedCorridor"].ToObject<RecastRoutePoint[]>();Require(corridor.Length==37,"Saved corner count changed");
        var from=corridor[29].Location;var to=corridor[30].Location;var clearance=new NavClearanceProfile("ground",1,"ground",0,request.Profile.Radius);var physical=(PartitionNavigationProvider)provider;
        bool free=request.Geometry.SegmentFree(from.Position,to.Position,request.Profile.Radius,out int blocker);
        bool trace=physical.TryTrace(from,to.Position,clearance,out var reached,out var legs);
        var obstacle=blocker<0?(NavObstacle?)null:request.Geometry.Obstacles[blocker];
        Save(output,"physical-chord-facts.json",new{InputSHA256=Sha(raw),FromIndex=29,ToIndex=30,From=from,To=to,request.Profile.Radius,FromGeometryFree=request.Geometry.IsFree(from.Position,request.Profile.Radius),ToGeometryFree=request.Geometry.IsFree(to.Position,request.Profile.Radius),FromProviderValid=physical.IsValid(from,clearance),ToProviderValid=physical.IsValid(to,clearance),GeometrySegmentFree=free,ActualGeometryBlockerIndex=blocker,Blocker=obstacle,PhysicalTrace=trace,Reached=reached,Legs=legs,request.Profile.LocalDetourDistance,request.Profile.LocalGridCell,DerivedWorkspaceRadius=Math.Ceiling(request.Profile.LocalDetourDistance/request.Profile.LocalGridCell),NativeQueries=0,HelperCreated=false,HelperSteps=0,Meaning="Owning source predicates only; no connector search, movement, native query or product repair"});
    }
    sealed class ProbeRefusal:Exception{}
    sealed class RefusingReservation:INavStorageReservation,INavStorageScratchAllocator,INavStorageLedgerIdentity,INavStorageLocalBirthAllocator,INavStoragePreparationOwner
    {
        readonly INavStorageReservation inner;readonly Func<bool> refuse;
        internal RefusingReservation(INavStorageReservation inner,Func<bool> refuse){this.inner=inner;this.refuse=refuse;}
        public bool TryReserve(long bytes){if(refuse())throw new ProbeRefusal();return inner.TryReserve(bytes);}
        public void Register(NavOwnedStorage storage)=>((INavStorageAllocator)inner).Register(storage);
        public object Identity=>((INavStorageLedgerIdentity)inner).Identity;
        public bool Owns(NavOwnedStorage storage)=>((INavStorageLedgerIdentity)inner).Owns(storage);
        public object PreparationOwner=>((INavStoragePreparationOwner)inner).PreparationOwner;
        public bool TryRegisterLocalBirth(NavOwnedStorage storage)=>((INavStorageLocalBirthAllocator)inner).TryRegisterLocalBirth(storage);
        public IDisposable ClaimScratch(long bytes)=>((INavStorageScratchAllocator)inner).ClaimScratch(bytes);
    }
    static RecastPayload Complete(ILayeredNavigationProvider provider,NavigationRequest request,RecastRoutePoint[] corridor,RecastAuthorityGeometry geometry,INavStorageReservation r,out object metrics)
    {
        using var raw=RecastPayload.Allocate(r,checked(32L+corridor.Length*NavStorageLayout.ValueBytes(typeof(RecastRoutePoint))));raw.Value=(RecastRoutePoint[])corridor.Clone();
        using var prep=RecastSpinePreparation.Create(raw,r);var quanta=new List<long>();
        while(prep.Status==RecastSpinePreparation.Result.Pending){long before=prep.Work;prep.Step(provider,request,geometry,r,256);long used=prep.Work-before;quanta.Add(used);Require(used>0&&used<=256,"Preparation exceeded or did not consume its actual work allowance");}
        metrics=new{Status=prep.Status.ToString(),prep.Work,prep.HelperWork,Quanta=quanta};var result=prep.Completed;result?.Retain();return result;
    }
    static RecastPayload PaidCompletion(string output,ILayeredNavigationProvider provider,NavigationRequest request,RecastRoutePoint[] corridor,RecastAuthorityGeometry geometry,INavStorageReservation r)
    {
        var original=JsonConvert.SerializeObject(corridor);var controls=new List<object>();
        using(var raw=RecastPayload.Allocate(r,checked(32L+corridor.Length*NavStorageLayout.ValueBytes(typeof(RecastRoutePoint))))){
            raw.Value=(RecastRoutePoint[])corridor.Clone();
            using(var cancelled=RecastSpinePreparation.Create(raw,r)){
                while(cancelled.Status==RecastSpinePreparation.Result.Pending&&!cancelled.HelperActive){long before=cancelled.Work;cancelled.Step(provider,request,geometry,r,256);Require(cancelled.Work-before<=256,"Cancellation prefix exceeded allowance");}
                bool caught=false;try{cancelled.Step(provider,request,geometry,r,256,()=>true);}catch(OperationCanceledException){caught=true;}
                controls.Add(new{Case="cancel-active-existing-helper",Cancelled=caught,CompleteOutput=cancelled.Completed!=null,cancelled.Work});Save(output,"paid-preparation-controls.json",controls);Require(caught&&cancelled.Completed==null,"Cancellation exposed a complete result");
            }
            using(var refused=RecastSpinePreparation.Create(raw,r)){
                var helper=typeof(RecastSpinePreparation).GetField("helper",BindingFlags.Instance|BindingFlags.NonPublic);
                var refusal=new RefusingReservation(r,()=>helper.GetValue(refused)!=null);bool caught=false;
                try{while(refused.Status==RecastSpinePreparation.Result.Pending){long before=refused.Work;refused.Step(provider,request,geometry,refusal,256);Require(refused.Work-before<=256,"Refusal prefix exceeded allowance");}}
                catch(ProbeRefusal){caught=true;}
                controls.Add(new{Case="paid-refusal-after-helper-birth-before-name-copy",Refused=caught,CompleteOutput=refused.Completed!=null,refused.Work});Save(output,"paid-preparation-controls.json",controls);Require(caught&&refused.Completed==null,"Refusal exposed a complete result");
            }
        }
        var result=Complete(provider,request,corridor,geometry,r,out var metrics);Save(output,"paid-preparation-completion.json",metrics);
        Require(original==JsonConvert.SerializeObject(corridor),"Immutable native corridor changed during restart");
        Require(result!=null,"Paid spine completion failed on original exact persisted corridor");
        Save(output,"authority-completed-corridor.json",new{Meaning="Separate complete authority corridor, immutable raw native transcript retained",TypedCorridor=result.Value,NativeQueries=0});return result;
    }
    static void VerifyRepair(string output,ILayeredNavigationProvider provider,NavigationRequest request,RecastRoutePoint[] corridor,RecastAuthorityGeometry geometry,RecastSpineProposal spine,RecastMemberRoute attached,INavStorageReservation reservation,bool paidSpine=false)
    {
        // Preserve predicate facts before the success assertion, including FAIL.
        var traceMethod=typeof(RecastGroupPreparation).GetMethod("Trace",BindingFlags.Static|BindingFlags.NonPublic);
        var cursor=request.StartLocation.Value;var facts=new List<object>();
        var physical=(PartitionNavigationProvider)provider;var physicalClearance=new NavClearanceProfile("ground",1,"ground",0,request.Profile.Radius);
        for(int i=0;i<corridor.Length;i++){
            var raw=corridor[i];var args=new object[]{provider,request,cursor,raw.Location,null};bool strictTrace=(bool)traceMethod.Invoke(null,args);
            double rawAuthority=geometry.Point(raw.Location)[1];bool strictHeight=Math.Abs(rawAuthority-raw.Height)<=2*geometry.CellHeight+1e-6;
            bool segment=request.Geometry.SegmentFree(cursor.Position,raw.Location.Position,request.Profile.Radius);
            bool trace=physical.TryTrace(cursor,raw.Location.Position,physicalClearance,out var reached,out _);bool valid=trace&&physical.IsValid(reached,physicalClearance);
            double? reachedHeight=trace?(double?)geometry.Point(reached)[1]:null;bool height=reachedHeight.HasValue&&Math.Abs(reachedHeight.Value-raw.Height)<=2*geometry.CellHeight+1e-6;
            bool eligible=i>0&&i<corridor.Length-1&&raw.PortalId==null&&!raw.Location.Position.Equals(request.StartLocation.Value.Position)&&!raw.Location.Position.Equals(request.GoalLocation.Value.Position);
            bool strict=strictTrace&&strictHeight;bool fallback=!strict&&eligible&&segment&&trace&&valid&&reached.Position.Equals(raw.Location.Position)&&height;
            facts.Add(new{Index=i,From=cursor,Raw=raw,StrictTrace=strictTrace,StrictHeight=strictHeight,Segment=segment,PhysicalTrace=trace,Reached=reached,ReachedValid=valid,ReachedHeight=reachedHeight,ReachedHeightAccepted=height,FallbackEligible=eligible,InterpretedFallbackAccepted=fallback});
            if(strict)cursor=raw.Location;else if(fallback)cursor=reached;
        }
        Save(output,"repair-predicate-facts.json",new{Meaning="Read-only source interpretation of same persisted corridor; production stage saved separately",Facts=facts,InterpretedTerminal=cursor,TerminalRaw=corridor[corridor.Length-1].Location});
        Require(spine!=null&&attached!=null,"Same persisted corridor still fails production spine/direct attachment");
        var immutable=JsonConvert.SerializeObject(corridor);
        var method=typeof(NavigationHostRoot).GetMethod("TraceRecastOwned",BindingFlags.Static|BindingFlags.NonPublic);
        var clearance=new NavClearanceProfile("ground",1,"ground",0,request.Profile.Radius);
        bool Same(NavTypedLeg a,NavTypedLeg b)=>a.From.Equals(b.From)&&a.To.Equals(b.To)&&a.Kind==b.Kind&&a.PortalId==b.PortalId&&a.Cost==b.Cost;
        int Compose(NavLocation start,IEnumerable<NavLocation> stops,NavTypedLeg[] expected,out NavLocation end){
            int index=0;end=start;
            foreach(var target in stops){var args=new object[]{provider,end,target,clearance,reservation,null,null};
                Require((bool)method.Invoke(null,args),"Production owned full transcript trace failed");var reached=(NavLocation)args[5];var trace=(IReadOnlyList<NavTypedLeg>)args[6];
                try{foreach(var leg in trace){Require(index<expected.Length&&Same(leg,expected[index]),"Production typed transcript changed");index++;}}
                finally{(trace as NavOwnedStorage)?.ReleaseOwner();}end=reached;
            }
            Require(index==expected.Length,"Incomplete production typed transcript");return index;
        }
        int branchLegs=Compose(request.StartLocation.Value,attached.Waypoints.Select(x=>x.Location),attached.Legs,out var final);
        Require(final.Equals(request.GoalLocation.Value),"Exact assigned goal changed");
        int spineLegs=Compose(spine.Node(0),spine.Stops.Skip(1).Select(spine.Node),spine.Legs,out _);
        var cases=new List<object>();
        void Negative(string name,Action<RecastRoutePoint[]> mutate){var candidate=(RecastRoutePoint[])corridor.Clone();mutate(candidate);bool rejected;
            if(paidSpine){using var completed=Complete(provider,request,candidate,geometry,reservation,out var metrics);rejected=completed==null;cases.Add(new{Name=name,SpineRejected=rejected,Metrics=metrics});}
            else{rejected=RecastGroupPreparation.BuildSpine(provider,new[]{request},candidate,geometry)==null;cases.Add(new{Name=name,SpineRejected=rejected});}
            Save(output,"negative-guards.json",cases);Require(rejected,"Unsafe corner negative accepted: "+name);}
        Negative("blocked-essential-corner",a=>a[8]=a[9]);
        Negative("false-height-at-essential-corner",a=>a[8]=new RecastRoutePoint(a[8].Location,a[8].Height+2*geometry.CellHeight+1e-6));
        Negative("unknown-native-support",a=>a[8]=new RecastRoutePoint(new NavLocation(a[8].Location.Position,"diagnostic/unknown-support"),a[8].Height));
        Negative("portal-corner-not-corrected",a=>a[8]=new RecastRoutePoint(a[8].Location,a[8].Height,"diagnostic/portal"));
        Negative("wrong-terminal-support-not-corrected",a=>a[a.Length-1]=new RecastRoutePoint(new NavLocation(a[a.Length-1].Location.Position,"bank/west"),a[a.Length-1].Height));
        Require(JsonConvert.SerializeObject(corridor)==immutable,"Raw persisted corridor mutated");
        Require(spine.Legs.Any(l=>l.To.Equals(new NavLocation(corridor[8].Location.Position,"ramp/1/-1/z"))),"Original essential turn was not retained on authority support");
        Save(output,"full-typed-composition.json",new{Result="PASS",BranchLegs=branchLegs,SpineLegs=spineLegs,ExactFinalGoal=final,RawCorridorImmutable=true,NativeQueries=0,NegativeCases=cases.Count,Method="Same production NavigationHostRoot.TraceRecastOwned and exact From/To/Kind/PortalId/Cost composition; no host registration/publication"});
    }
}
