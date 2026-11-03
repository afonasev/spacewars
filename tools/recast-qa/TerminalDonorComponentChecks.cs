using System;using System.IO;using System.IO.Compression;using System.Linq;using System.Reflection;using System.Collections.Generic;using Newtonsoft.Json;using Newtonsoft.Json.Linq;using Spacewars.Runtime;using Spacewars.Simulation;
internal static class TerminalDonorComponentChecks {
    internal static void RunActualPocketCurrent(){
        var input=Environment.GetEnvironmentVariable("RECAST_DONOR_INPUT")??throw new ArgumentException("Input");var output=Environment.GetEnvironmentVariable("RECAST_DONOR_OUTPUT")??throw new ArgumentException("Output");Check(!Directory.Exists(output),"Output exists");Directory.CreateDirectory(output);
        var bytes=File.ReadAllBytes(Path.Combine(input,"terminal-nav14.bin"));using var saved=WorldWire.Unpack(bytes,WorldWire.ReadNavigationSessionState);var profile=PlayableProfile.RecastGroupCorridorDefault;NavigationSession source=null;ulong native=RecastNative.sw_recast_allocation_count();long faults=RecastNative.ReleaseFaults;
        try{source=FormationPreflight.FromSnapshot(saved,profile);using(var c=source.CaptureState())Check(bytes.SequenceEqual(WorldWire.Pack(w=>WorldWire.Write(w,c))),"Actual all-peer current did not restore exactly");
            object Row(NavUnit a)=>new{a.Id,a.Position,a.Location,a.Heading,a.Moving,a.Held,a.CanYield,a.Team,a.Outcome,a.InstalledRequest,a.InstalledOrder,a.InstalledGroupId,a.routeIndex,a.localIndex,Global=a.route.ToArray(),Local=a.localRoute.ToArray(),a.noProgressTicks,a.blockedSeconds};
            object SnapshotValue(object value){var t=value.GetType();if(t.IsPrimitive||t.IsEnum||t==typeof(string))return value;return t.GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).ToDictionary(f=>f.Name,f=>SnapshotValue(f.GetValue(value)));}
            object Groups()=>saved.Groups.SelectMany(g=>g.Members).Select(m=>source.GroupFor(m.Entity)).Where(g=>g!=null).GroupBy(g=>g.GroupId).Select(g=>g.First()).Select(g=>new{g.GroupId,g.IssuedTick,g.OrderRevision,Formation=g.FormationPlan==null?null:new{g.FormationPlan.FrontAnchor,g.FormationPlan.AnchorInitialized,g.FormationPlan.Phase,Entries=g.FormationPlan.Entries.Select(e=>new{e.Entity,e.EntryOrdinal,e.Lane,e.Stage}).ToArray()}}).ToArray();
            File.WriteAllText(Path.Combine(output,"initial.json"),JsonConvert.SerializeObject(new{Meaning="Exact restored actual current containing ALL101 saved bodies/routes/headings/counters/roles, no invented peer state or pose/order changes",Actors=source.Crowd.Units.Select(Row).ToArray(),Groups=Groups()},Formatting.Indented));
            int rootId=int.Parse(Environment.GetEnvironmentVariable("RECAST_CURRENT_ROOT")??"24");var root=source.Crowd.Units.Single(a=>a.Id==rootId);Check(root.Moving&&root.noProgressTicks>0,"Captured root is not the actual moving stalled actor");var start=root.Position;var traces=new List<object>();int limit=(int)Math.Ceiling(profile.Navigation.BlockedTimeoutSeconds*30);
            for(int step=0;step<limit;step++){
                var beforeActors=source.Crowd.Units.Select(a=>new{a.Id,a.Position,a.Location,a.Radius,a.Speed}).ToDictionary(a=>a.Id);
                NavCrowdMotion.CausalActor=rootId;source.Step(1d/30);root=source.Crowd.Units.Single(a=>a.Id==rootId);var c=NavCrowdMotion.Causal;var afterActors=source.Crowd.Units.ToArray();
                foreach(var a in afterActors){var before=beforeActors[a.Id];double x=a.Position.X-before.Position.X,z=a.Position.Z-before.Position.Z;Check(x*x+z*z<=a.Speed*a.Speed/900+1e-9,"Replay speed exceeded "+a.Id);Check(source.Crowd.Geometry.SegmentFree(before.Position,a.Position,a.Radius)&&profile.AuthoredMap.TryTraverse(before.Location,a.Position,a.Radius,out var reached)&&reached.Equals(a.Location),"Replay authority/layer sweep failed "+a.Id);}
                for(int left=0;left<afterActors.Length;left++)for(int right=0;right<left;right++){
                    var a=afterActors[left];var peer=afterActors[right];var ab=beforeActors[a.Id];var pb=beforeActors[peer.Id];double sx=ab.Position.X-pb.Position.X,sz=ab.Position.Z-pb.Position.Z,dx=a.Position.X-ab.Position.X-(peer.Position.X-pb.Position.X),dz=a.Position.Z-ab.Position.Z-(peer.Position.Z-pb.Position.Z),denom=dx*dx+dz*dz,t=denom==0?0:Math.Max(0,Math.Min(1,-(sx*dx+sz*dz)/denom));Check(Math.Pow(sx+dx*t,2)+Math.Pow(sz+dz*t,2)>=Math.Pow(a.Radius+peer.Radius,2)-1e-8,"Replay continuous peer sweep failed "+a.Id+"/"+peer.Id);
                }
                traces.Add(new{Step=step+1,source.LastNavigationStepPath,Actors=source.Crowd.Units.Select(Row).ToArray(),Groups=Groups(),Causal=SnapshotValue(c)});
                if(!root.Moving)break;
            }
            File.WriteAllText(Path.Combine(output,"replay.json"),JsonConvert.SerializeObject(new{Meaning="Bounded synchronous owning-source NavigationSession.Step replay of exact ALL-peer current. Real captured input, dynamic production peers, complete bounded continuation (not first-motion credit), no World/threaded-runtime or gameplay acceptance; no source/file pose/counter edit.",Root=rootId,SpeedAuthorityAndAllContinuousPeerSweeps="PASS each simulated step",Steps=traces.Count,Limit=limit,RootTranslated=!root.Position.Equals(start),RootDistance=Math.Sqrt(Math.Pow(root.Position.X-start.X,2)+Math.Pow(root.Position.Z-start.Z,2)),root.Outcome,root.noProgressTicks,Trace=traces},Formatting.Indented));
        }finally{if(source!=null){source.ReleaseCommittedHostRoot();source.DestroyNavigationAuthority();var b=source.PhysicalHostBudget;File.WriteAllText(Path.Combine(output,"cleanup.json"),JsonConvert.SerializeObject(new{b.PhysicalBytes,b.HeldBytes,b.ActiveScopes,NativeBefore=native,NativeAfter=RecastNative.sw_recast_allocation_count(),NativeFaults=RecastNative.ReleaseFaults-faults}));Check(b.PhysicalBytes==0&&b.HeldBytes==0&&b.ActiveScopes==0&&native==RecastNative.sw_recast_allocation_count()&&faults==RecastNative.ReleaseFaults,"Actual pocket current census mismatch");}}
    }
    internal static void RunIngressAim(){
        var input=Environment.GetEnvironmentVariable("RECAST_DONOR_INPUT")??throw new ArgumentException("Input");
        var output=Environment.GetEnvironmentVariable("RECAST_DONOR_OUTPUT")??throw new ArgumentException("Output");
        Check(!Directory.Exists(output),"Output exists");Directory.CreateDirectory(output);
        double Distance(NavPoint a,NavPoint b)=>Math.Sqrt(Math.Pow(a.X-b.X,2)+Math.Pow(a.Z-b.Z,2));
        var observation=JObject.Parse(File.ReadAllText(Path.Combine(input,"approach-observation.json")));
        var rows=observation["Rows"].Where(j=>(int)j["Actor"]==19&&(int)j["Tick"]>=4900).ToArray();Check(rows.Length>1,"Missing actual late ingress observations");
        var bytes=File.ReadAllBytes(Path.Combine(input,"active-current","terminal-nav14.bin"));using var saved=WorldWire.Unpack(bytes,WorldWire.ReadNavigationSessionState);
        NavigationSession source=null;ulong native=RecastNative.sw_recast_allocation_count();long faults=RecastNative.ReleaseFaults;
        try{source=FormationPreflight.FromSnapshot(saved,PlayableProfile.RecastGroupCorridorDefault);
            Check(source.TryOpenPhysicalHostScope(NavigationPreparationKind.Solve,805,source.Admission.PublicationIdentity,out var scope),"Scope refused");using(scope){
                var r=scope.OwnedReservation();var actor=source.Crowd.Units.Single(a=>a.Id==19);var owner=(NavigationInstalledExecutionOwner)actor.StorageOwner.InstalledExecution;var route=owner.Corridor.TypedRoute;var group=source.GroupFor(19);var plan=group.FormationPlan;var entry=plan.Entries.Single(e=>e.Entity==19);var profile=PlayableProfile.RecastGroupCorridorDefault;
                Check(RecastFormationGuide.TryLayout(route,plan.EntryPortal,plan.ExitPortal,profile.Navigation.ArrivalSlotSpacing,r,out var layout),"Actual ingress layout refused");
                var reports=new List<object>();int transverse=0,forward=0;
                foreach(var row in rows){
                    Check((long)row["InstalledRequest"]==route.Subscription.RequestSequence&&(long)row["InstalledGroupId"]==route.Subscription.GroupId&&entry.EntryOrdinal==(int)row["Role"]&&entry.Lane==(int)row["Lane"],"Actual late owning group/request/role mismatch");
                    Check((string)row["Stage"]=="Admitted"&&(int)row["RouteIndex"]==9&&(int)row["LocalLength"]==0,"Unexpected observed ingress applicability");
                    var body=row["Causal"]["Body"];var actual=P(body["Position"]);int lane=entry.Lane;double current=layout.Station(actual),limit=(double)row["Causal"]["ForwardLimit"],station=Math.Min(layout.Length+layout.Radius,Math.Max(current,limit)+7*Math.Max(1d/30,profile.Navigation.CrowdLookaheadSeconds));
                    double lateral=RecastFormationMotion.Station(actual,layout.Entry,layout.Normal);bool band=lateral>=layout.Low-1e-9&&lateral<=(layout.Low+layout.High)/2-layout.Radius+1e-9;
                    var previous=layout.Point(layout.Directional&&!band&&current<-layout.Radius?current:station,lane);var recorded=P(row["Causal"]["GuideTarget"]);Check(previous.Equals(recorded),"Baseline source did not reproduce actual guide target");
                    var strict=layout.Point(layout.Directional&&current<-layout.Radius&&Distance(actual,layout.Point(current,lane))>profile.Navigation.ArrivalTolerance?current:station,lane);
                    double chosen=RecastFormationMotion.Station(layout.Point(current,lane),layout.Entry,layout.Normal),edge=lateral<layout.Low?layout.Low:(layout.Low+layout.High)/2-layout.Radius;
                    double projected=station;if(layout.Directional&&current<-layout.Radius&&(lateral<layout.Low||lateral>(layout.Low+layout.High)/2-layout.Radius))projected=Math.Min(station,current+(-layout.Radius-current)/((edge-lateral)/(chosen-lateral)));
                    var proposed=layout.Point(projected,lane);
                    var sourceAim=RecastFormationGuide.Aim(layout,actual,station,lane);
                    if(layout.Station(previous)==current)transverse++;else forward++;
                    reports.Add(new{Tick=(int)row["Tick"],Group=(long)row["InstalledGroupId"],Request=(long)row["InstalledRequest"],Ordinal=entry.EntryOrdinal,Lane=lane,Actual=actual,ExistingTolerance=profile.Navigation.ArrivalTolerance,ChosenLaneDistance=Distance(actual,layout.Point(current,lane)),Previous=previous,Recorded=recorded,StrictRejected=strict,Proposed=proposed,Source=sourceAim,Physical=profile.AuthoredMap.TryTraverse(new NavLocation(actual,(string)row["Location"]["SurfaceId"]),proposed,actor.Radius,out _)});
                }
                Check(transverse>0&&forward>0,"Actual old observations did not exercise both discontinuous branches");
                File.WriteAllText(Path.Combine(output,"ingress-aim.json"),JsonConvert.SerializeObject(new{Meaning="Actual late causal rows with their actual recorded guide targets, bound to the saved accepted owner/group/request/frozen ordinal and lane. No late route arrays, snapshot, current, clock or gameplay fabricated. Proposed lane aim is an explicit source-bound projection; Physical is only authored terrain trace, not moving-peer or typed execution acceptance.",Rows=reports,transverse,forward,Layout=new{layout.Entry,layout.Exit,layout.Low,layout.High,layout.Radius,layout.Lanes}},Formatting.Indented));
            }
            using var recapture=source.CaptureState();Check(bytes.SequenceEqual(WorldWire.Pack(w=>WorldWire.Write(w,recapture))),"Ingress probe changed source current");
        }finally{if(source!=null){source.ReleaseCommittedHostRoot();source.DestroyNavigationAuthority();var b=source.PhysicalHostBudget;File.WriteAllText(Path.Combine(output,"cleanup.json"),JsonConvert.SerializeObject(new{b.PhysicalBytes,b.HeldBytes,b.ActiveScopes,NativeBefore=native,NativeAfter=RecastNative.sw_recast_allocation_count(),NativeFaults=RecastNative.ReleaseFaults-faults}));Check(b.PhysicalBytes==0&&b.HeldBytes==0&&b.ActiveScopes==0&&native==RecastNative.sw_recast_allocation_count()&&faults==RecastNative.ReleaseFaults,"Ingress census mismatch");}}
    }
    internal static void RunCaptured47Boundary(){
        int actorId=int.Parse(Environment.GetEnvironmentVariable("RECAST_DONOR_BOUNDARY_ACTOR")??"47");
        var input=Environment.GetEnvironmentVariable("RECAST_DONOR_INPUT")??throw new ArgumentException("Input");var output=Environment.GetEnvironmentVariable("RECAST_DONOR_OUTPUT")??throw new ArgumentException("Output");Check(!Directory.Exists(output),"Output exists");Directory.CreateDirectory(output);
        var evidence=JObject.Parse(File.ReadAllText(Path.Combine(input,Environment.GetEnvironmentVariable("RECAST_DONOR_BOUNDARY_CAPTURE")??"actor47-cold-1858.json")));var snap=evidence["Snapshot"];var observed=evidence["Row"];
        var owningFrame=evidence["Frame"]["Groups"].Single(g=>g["Bodies"].Any(b=>(int)b["Id"]==actorId&&(bool)b["Selected"]));var poses=owningFrame["Bodies"];
        var bytes=File.ReadAllBytes(Path.Combine(input,"active-current","terminal-nav14.bin"));using var saved=WorldWire.Unpack(bytes,WorldWire.ReadNavigationSessionState);NavigationSession source=null;NavPreparedCrowdStep frame=null;
        ulong native=RecastNative.sw_recast_allocation_count();long faults=RecastNative.ReleaseFaults;
        try{source=FormationPreflight.FromSnapshot(saved,PlayableProfile.RecastGroupCorridorDefault);Check(source.TryOpenPhysicalHostScope(NavigationPreparationKind.Solve,805,source.Admission.PublicationIdentity,out var scope),"Scope refused");using(scope){var r=scope.OwnedReservation();frame=NavPreparedCrowdStep.Create(source.Crowd,source.Crowd.Units.Count,r);frame.Observe();
            foreach(var u in frame.After){var p=poses.Single(j=>(int)j["Id"]==u.Id);u.Position=P(p["Position"]);u.Location=L(p["Location"]);u.Moving=(bool)p["Moving"];u.Outcome=(NavigationOutcome)(int)p["Outcome"];}
            int row=frame.Find(actorId);var actor=frame.After[row];var owner=source.Crowd.Units.Single(a=>a.Id==actorId).StorageOwner.InstalledExecution as NavigationInstalledExecutionOwner;Check(owner!=null,"Missing captured accepted owner");var route=owner.Corridor.TypedRoute;var pose=poses.Single(j=>(int)j["Id"]==actorId);var progress=pose["Progress"];
            Check((long)progress["Request"]==(long)snap["InstalledRequest"]&&route.Subscription.RequestSequence==(long)snap["InstalledRequest"],"Captured owning group/request state mismatch");
            var state=new NavRecastStepState((int)progress["Phase"],(int)progress["Cursor"],(double)progress["Station"],(double)progress["HighWater"]);
            File.WriteAllText(Path.Combine(output,"owning-progress.json"),JsonConvert.SerializeObject(new{Actor=actorId,Group=owningFrame["Group"],GroupRequest=progress["Request"],SnapshotRequest=snap["InstalledRequest"],AcceptedRequest=route.Subscription.RequestSequence,Progress=progress,Meaning="Actual selected owning group state, never the unselected other group's default DTO"},Formatting.Indented));
            actor.Heading=(double)snap["Heading"];actor.route=snap["Route"].ToObject<NavPoint[]>();actor.routeIndex=(int)snap["RouteIndex"];actor.localRoute=snap["LocalRoute"].ToObject<NavPoint[]>();actor.localIndex=(int)snap["LocalIndex"];actor.noProgressTicks=(int)snap["NoProgressTicks"];actor.blockedSeconds=(double)snap["BlockedSeconds"];
            frame.BindRecast(row,route,state);var group=source.GroupFor(actorId);var plan=group.FormationPlan;var roles=snap["Roles"];var entry=roles.Single(j=>(int)j["Entity"]==actorId);
            var profile=PlayableProfile.RecastGroupCorridorDefault;double pace=group.Members.Min(m=>source.Crowd.Units.Single(u=>u.Id==m.Entity).Speed);double reach=pace*Math.Max(1d/30,profile.Navigation.CrowdLookaheadSeconds);
            foreach(var m in group.Members){var installed=(NavigationInstalledExecutionOwner)source.Crowd.Units.Single(u=>u.Id==m.Entity).StorageOwner.InstalledExecution;reach=Math.Min(reach,RecastFormationGuide.AvailableBankReach(installed.Corridor.TypedRoute,plan.EntryPortal,plan.ExitPortal,reach));}
            Check(RecastFormationGuide.TryLayout(route,plan.EntryPortal,plan.ExitPortal,profile.Navigation.ArrivalSlotSpacing,r,out var layout,Math.Max(0,reach)),"Actual layout refused");
            int portal=RecastFormationGuide.Portal(route,plan.EntryPortal),role=(int)entry["EntryOrdinal"]/layout.Lanes,lane=(int)entry["Lane"];
            var staging=roles.Where(j=>(string)j["Stage"]=="Staging").Select(j=>(int)j["EntryOrdinal"]/layout.Lanes).ToArray();int first=staging.Length==0?role:staging.Min(),future=staging.Length==0?1:staging.Max()+1-first;
            double recordedForward=(double)observed["Causal"]["ForwardLimit"],frontier=recordedForward+(role-first)*layout.Pitch;
            bool isStaging=(string)entry["Stage"]=="Staging";
            byte capturedFlags=(byte)observed["Causal"]["HintFlags"];
            bool isRecovering=(string)entry["Stage"]=="Recovering";
            bool compareRecovering=Environment.GetEnvironmentVariable("RECAST_DONOR_BOUNDARY_RECOVERING_COMPARE")=="1";Check(!compareRecovering||isRecovering&&(capturedFlags&NavFormationFrameHint.Guide)!=0,"Recovering comparison requires an actually recorded guided bank hint");
            var derivedRecovering=isRecovering?RecastFormationGuide.RecoveringApproach(frame,row,route,portal,layout,lane,pace,1d/30):default;
            var recordedHint=new NavFormationFrameHint{Flags=capturedFlags,Target=P(observed["Causal"]["GuideTarget"]),TargetLeg=portal,ForwardLimit=recordedForward,DirectionalBankReach=(capturedFlags&NavFormationFrameHint.Guide)!=0?layout.BankReach:0};
            var hint=isStaging?RecastFormationGuide.StagingApproach(frame,row,route,portal,layout,frontier,role,lane,first,future,pace,1d/30):isRecovering&&!compareRecovering?derivedRecovering:recordedHint;frame.Formation[row]=hint;
            Check(hint.Flags==(byte)observed["Causal"]["HintFlags"]&&hint.ForwardLimit==recordedForward,"Cold actual hint did not reproduce");
            Check(hint.Target.Equals(P(observed["Causal"]["GuideTarget"])),"Cold actual guide target did not reproduce");
            string control=Environment.GetEnvironmentVariable("RECAST_DONOR_BOUNDARY_CONTROL")??"actual";
            if(control=="pre-entry-only"){hint.Flags&=unchecked((byte)~(NavFormationFrameHint.Bound|NavFormationFrameHint.Directional));frame.Formation[row]=hint;}
            else Check(control=="actual","Unknown explicit boundary counterfactual");
            if(compareRecovering){
                Check(control=="actual","Recovering source comparison cannot combine hint counterfactuals");
                object Hint(NavFormationFrameHint h)=>new{h.Flags,h.Target,h.TargetLeg,h.ForwardLimit,h.DirectionalBankReach};
                File.WriteAllText(Path.Combine(output,"recovering-policy-comparison.json"),JsonConvert.SerializeObject(new{Meaning="Recorded actual hint remains exact provenance; changed current source RecoveringApproach rederived on the SAME actual pose/route/current state. Not an actual async replay.",Recorded=Hint(hint),SourceDerived=Hint(derivedRecovering),DistanceToRecordedGuide=Math.Sqrt(Math.Pow(actor.Position.X-hint.Target.X,2)+Math.Pow(actor.Position.Z-hint.Target.Z,2)),ExistingTolerance=profile.Navigation.ArrivalTolerance,MotorDirectional=RecastFormationMotion.DirectionalAllows(derivedRecovering,route,actor.Position,actor.route[actor.routeIndex]),MotorTyped=NavRecastIntervals.CanTarget(route,state,actor.Location,actor.routeIndex,r,frame.Working,actorId)},Formatting.Indented));
                hint=derivedRecovering;frame.Formation[row]=hint;control="source-rederived-recovering";
            }
            bool compareIngress=Environment.GetEnvironmentVariable("RECAST_DONOR_BOUNDARY_INGRESS_COMPARE")=="1";
            if(compareIngress){
                Check(!isStaging&&!isRecovering,"Ingress comparison requires actual Admitted role");
                var recorded=hint;double forecast=Math.Min(layout.Length+layout.Radius,Math.Max(layout.Station(actor.Position),hint.ForwardLimit)+pace*Math.Max(1d/30,profile.Navigation.CrowdLookaheadSeconds));
                var aim=RecastFormationGuide.Aim(layout,actor.Position,forecast,lane);bool certified=NavRecastIntervals.Advance(route,state,actor.Location,aim,r,out _,frame.Working,actor.Id);
                if(Environment.GetEnvironmentVariable("RECAST_DONOR_BOUNDARY_INGRESS_BASELINE")=="1")Check(certified&&recorded.Target.Equals(aim),"Baseline owning source did not reproduce actual ingress hint");
                hint.Target=aim;hint.Flags=(byte)((hint.Flags&~NavFormationFrameHint.Guide)|(certified?NavFormationFrameHint.Guide:0));hint.DirectionalBankReach=certified?layout.BankReach:0;frame.Formation[row]=hint;
                File.WriteAllText(Path.Combine(output,"ingress-policy-comparison.json"),JsonConvert.SerializeObject(new{Meaning="Same actual saved snapshot/frame/owning request and frozen lane; recorded hint compared with explicitly rederived current-source Admitted ingress hint. No World/Step/pose/clock/publication.",Recorded=new{recorded.Flags,recorded.Target,recorded.ForwardLimit},Derived=new{hint.Flags,hint.Target,hint.ForwardLimit},Certified=certified},Formatting.Indented));
            }
            double station=Math.Min(layout.Length+layout.Radius,Math.Max(layout.Station(actor.Position),hint.ForwardLimit)+pace*Math.Max(1d/30,profile.Navigation.CrowdLookaheadSeconds));var alignment=RecastFormationGuide.Aim(layout,actor.Position,station,lane);var provider=(PartitionNavigationProvider)route.Legs[0].RecastAuthorityProvider;
            var targets=new[]{(Name:"accepted motor",Point:actor.route[actor.routeIndex]),(Name:"stationary-column alignment",Point:alignment)};
            var facts=new List<object>();foreach(var target in targets){bool geometry=source.Crowd.Geometry.SegmentFree(actor.Position,target.Point,actor.Radius,out int blocker);bool typed=NavRecastIntervals.Advance(route,state,actor.Location,target.Point,r,out _,frame.Working,actorId);bool physical=profile.AuthoredMap.TryTraverse(actor.Location,target.Point,actor.Radius,out var reached);facts.Add(new{target.Name,target.Point,Geometry=geometry,Blocker=blocker,Obstacle=blocker<0?(NavObstacle?)null:source.Crowd.Geometry.Obstacles[blocker],Typed=typed,Physical=physical,Reached=reached,Formation=RecastFormationMotion.Allows(hint,route,actor.Position,target.Point)});}
            var moves=new List<object>();foreach(var delta in new[]{new NavPoint(0,actor.Speed/30),new NavPoint(-actor.Speed/30,0),new NavPoint(actor.Speed/30,0),new NavPoint(0,-actor.Speed/30)}){var end=new NavPoint(actor.Position.X+delta.X,actor.Position.Z+delta.Z);moves.Add(new{End=end,Geometry=source.Crowd.Geometry.SegmentFree(actor.Position,end,actor.Radius),Formation=RecastFormationMotion.Allows(hint,route,actor.Position,end),ClearMotion=((INavMotionClearance)frame.Working).ClearMotion(row,end)});}
            File.WriteAllText(Path.Combine(output,"boundary.json"),JsonConvert.SerializeObject(new{Meaning="Exact saved typed owner plus detached actual snapshot/frame; explicit frame-hint counterfactual when requested; no World/Step/query or source pose mutation",Control=control,OriginalHintFlags=(byte)observed["Causal"]["HintFlags"],Tick=(int)observed["Tick"],Actor=actorId,Stage=(string)entry["Stage"],HintOrigin=compareIngress?"explicit source-rederived ingress counterfactual; actual original retained in ingress-policy-comparison.json":compareRecovering?"changed source RecoveringApproach; exact recorded counterpart in recovering-policy-comparison.json":isStaging?"recomputed StagingApproach":isRecovering?"recomputed RecoveringApproach checked against recorded flags/target/limit; bank reach derived": "actual captured flags/target/limit plus accepted-source portal/bank reach",actor.Position,actor.Heading,actor.routeIndex,Hint=new{hint.Flags,hint.Target,hint.TargetLeg,hint.ForwardLimit,hint.DirectionalBankReach},Layout=new{layout.Entry,layout.Exit,layout.Low,layout.High,layout.Lanes,layout.BankReach},Targets=facts,Moves=moves},Formatting.Indented));
            if(Environment.GetEnvironmentVariable("RECAST_DONOR_BOUNDARY_PROJECTION_COMPARE")=="1"){
                // Both paths execute the owning C# predicates on the same detached
                // captured group/request. Invalid endpoints remain ordinary denials.
                var projectionTargets=new List<NavPoint>{actor.Position,P(observed["Causal"]["Selected"]["Position"]),P(observed["Causal"]["PreFilter"]["Position"]),hint.Target};
                projectionTargets.AddRange(actor.route);projectionTargets.AddRange(actor.localRoute);
                for(int x=-1;x<=1;x++)for(int z=-1;z<=1;z++)projectionTargets.Add(new NavPoint(actor.Position.X+x*profile.Navigation.LocalGridCell,actor.Position.Z+z*profile.Navigation.LocalGridCell));
                var comparisons=new List<object>();
                foreach(var target in projectionTargets){
                    bool scalar=NavRecastIntervals.Advance(route,state,actor.Location,target,r,out var original,frame.Working,actorId,nearestPrecheck:false);
                    bool nearest=NavRecastIntervals.Advance(route,state,actor.Location,target,r,out var optimized,frame.Working,actorId,nearestPrecheck:true);
                    comparisons.Add(new{Target=target,Scalar=scalar,Nearest=nearest,ExactReceipt=original.Equals(optimized)});
                    Check(scalar==nearest&&original.Equals(optimized),"Nearest projection changed the scalar result or receipt");
                }
                File.WriteAllText(Path.Combine(output,"projection-comparison.json"),JsonConvert.SerializeObject(new{Result="PASS",Actor=actorId,Tick=(int)observed["Tick"],StableClearance=frame.Working.StableSelectClearance,ImmutableRoute=route.IsImmutableCaptureRoot,Meaning="Exact scalar/nearest receipts on retained owning pose and endpoints; no clock, movement or performance claim",Comparisons=comparisons},Formatting.Indented));
            }
            if(Environment.GetEnvironmentVariable("RECAST_DONOR_BOUNDARY_SELECT")=="1"){
                // All positions/radii are actual. Peer stationarity is an explicit
                // projection, not a fabricated claim about their missing headings.
                for(int i=0;i<frame.After.Length;i++){var u=frame.After[i];frame.Motion[i]=new NavMotionBody{Id=u.Id,Position=u.Position,Target=u.Position,Radius=u.Radius};frame.Proposals[i]=new NavMotionProposal{Position=u.Position};}
                var b=observed["Causal"]["Body"];frame.Motion[row]=new NavMotionBody{Id=actorId,Position=P(b["Position"]),Target=compareRecovering||compareIngress?((hint.Flags&NavFormationFrameHint.Guide)!=0?hint.Target:actor.route[actor.routeIndex]):P(b["Target"]),Heading=(double)b["Heading"],Radius=(double)b["Radius"],Speed=(double)b["Speed"],OwnPaceCap=(double)b["OwnPaceCap"],TurnSpeed=(double)b["TurnSpeed"],Active=true,OwnMoving=true};
                if(Environment.GetEnvironmentVariable("RECAST_DONOR_BOUNDARY_REDIRECT")=="1"){
                    var beforeHint=hint;var beforeTarget=frame.Motion[row].Target;typeof(NavCrowd).GetMethod("RedirectOccupiedExitGuides",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(frame.Working,new object[]{frame.Motion});hint=frame.Formation[row];
                    File.WriteAllText(Path.Combine(output,"consumer-handoff.json"),JsonConvert.SerializeObject(new{Meaning="Changed source consumer on exact detached captured pose/route/hint; no World/Step/clock or publication",BeforeFlags=beforeHint.Flags,AfterFlags=hint.Flags,BeforeTarget=beforeTarget,AfterTarget=frame.Motion[row].Target,FullContinuation=NavRecastIntervals.TryContinuationRejoin(route,state,actor.Location,Array.Empty<NavPoint>(),0,actor.route,actor.routeIndex,actor.GoalLocation.Value,r,out int same,frame.Working,actor.Id)&&same==actor.routeIndex},Formatting.Indented));
                }
                NavCrowdMotion.Neighbors(frame.Motion,frame.Neighbors,frame.NeighborCounts,profile.Navigation,1d/30);
                var turns=new List<object>();bool translated=false;
                object Proposal(NavMotionProposal q)=>new{q.Position,q.Velocity,q.Heading,q.Blocked,q.Overflow,q.Refuse,q.StaticNoWinner,q.JointRefused,q.IndependentTurn};
                if(Environment.GetEnvironmentVariable("RECAST_DONOR_BOUNDARY_EXPLAIN")=="1"){
                    var a=frame.Motion[row];a.Speed=a.OwnSpeed;var delta=new NavPoint(a.Target.X-a.Position.X,a.Target.Z-a.Position.Z);double distance=Math.Sqrt(delta.X*delta.X+delta.Z*delta.Z),aim=Math.Atan2(delta.Z,delta.X),dt=1d/30;
                    var riskMethod=typeof(NavCrowdMotion).GetMethods(BindingFlags.Static|BindingFlags.NonPublic).Single(m=>m.Name=="Risk"&&m.GetParameters().Length==6&&m.GetParameters()[5].ParameterType==typeof(double));var choices=new List<object>();
                    for(int direction=-2;direction<profile.Navigation.CrowdHeadingIntervals;direction++)for(int band=1;band<=profile.Navigation.CrowdSpeedBands;band++){
                        double steering=direction==-2?a.Heading:direction==-1?aim:NavCrowdMotion.Angle(aim-Math.PI+2*Math.PI*direction/profile.Navigation.CrowdHeadingIntervals),heading=NavCrowdMotion.Turn(a.Heading,steering,a.TurnSpeed*dt),speed=Math.Min(a.Speed,distance/dt)*band/profile.Navigation.CrowdSpeedBands;
                        bool aligned=Math.Abs(NavCrowdMotion.Angle(steering-heading))<=profile.Navigation.ForwardAlignmentAngle;var velocity=new NavPoint(Math.Cos(steering)*speed,Math.Sin(steering)*speed);var futurePoint=new NavPoint(a.Position.X+velocity.X*dt,a.Position.Z+velocity.Z*dt);double forecastReach=Math.Min(distance,speed*Math.Max(dt,profile.Navigation.CrowdLookaheadSeconds));var forecast=new NavPoint(a.Position.X+Math.Cos(steering)*forecastReach,a.Position.Z+Math.Sin(steering)*forecastReach);
                        double risk=(double)riskMethod.Invoke(null,new object[]{frame.Motion,row,velocity,frame.Neighbors,frame.NeighborCounts[row],profile.Navigation.CrowdLookaheadSeconds});
                        frame.Proposals[row]=new NavMotionProposal{Position=futurePoint,Velocity=velocity,Heading=steering};NavCrowdMotion.ResolveJoint(frame.Motion,frame.Proposals,dt);bool peers=!frame.Proposals[row].Position.Equals(a.Position);
                        bool terrain=((INavMotionClearance)frame.Working).ClearMotion(row,futurePoint),longTerrain=((INavMotionClearance)frame.Working).ClearMotion(row,forecast),formation=RecastFormationMotion.Allows(hint,route,a.Position,futurePoint),improves=NavCrowdMotion.ImprovesGoal(a.Position,futurePoint,actor.Goal);
                        choices.Add(new{direction,band,steering,heading,speed,aligned,Risk=risk,GoalImproves=improves,StaticDtPeers=peers,TerrainDt=terrain,TerrainForecast=longTerrain,FormationDt=formation,Future=futurePoint});
                    }
                    frame.Proposals[row]=new NavMotionProposal{Position=a.Position};File.WriteAllText(Path.Combine(output,"stationary-candidates.json"),JsonConvert.SerializeObject(new{Meaning="Diagnostic bounded steering enumeration on actual all-body positions with ALL peers explicitly stationary; source private scalar Risk and source Joint/ClearMotion/Allows predicates. Not an actual moving-peer forecast replay or gameplay.",Choices=choices},Formatting.Indented));
                }
                for(int turn=0;turn<32;turn++){
                    double heading=frame.Motion[row].Heading;
                    var next=NavCrowdMotion.Select(frame.Motion,row,frame.Neighbors,frame.NeighborCounts[row],profile.Navigation,1d/30,frame.Working,actor.Goal,true);
                    bool formation=RecastFormationMotion.Allows(hint,route,actor.Position,next.Position),typed=NavRecastIntervals.Advance(route,state,actor.Location,next.Position,r,out _,frame.Working,actorId);
                    frame.Proposals[row]=next;NavCrowdMotion.ResolveJoint(frame.Motion,frame.Proposals,1d/30);var joint=frame.Proposals[row];
                    turns.Add(new{Turn=turn,Heading=heading,Selected=Proposal(next),Joint=Proposal(joint),Formation=formation,Typed=typed});
                    if(!joint.Position.Equals(actor.Position)){translated=formation&&typed;break;}
                    if(joint.Heading.Equals(heading))break;frame.Motion[row].Heading=joint.Heading;
                }
                File.WriteAllText(Path.Combine(output,"stationary-select.json"),JsonConvert.SerializeObject(new{Meaning="Detached actual all-body positions/radii and focus target/speed/heading (explicit source-rederived hint counterfactual when requested); ALL peers explicitly projected stationary. Only focus heading advanced between Select calls, no actor translation, World/Step/clock or gameplay acceptance",Neighbors=frame.NeighborCounts[row],TranslationAvailable=translated,Turns=turns},Formatting.Indented));
            }
            if(Environment.GetEnvironmentVariable("RECAST_DONOR_BOUNDARY_REPAIR")=="1"){
                var flags=BindingFlags.Instance|BindingFlags.NonPublic;typeof(NavCrowd).GetMethod("BuildIndex",flags).Invoke(frame.Working,null);object[] args={actor,actor.route[actor.routeIndex],null};
                try{var position=actor.Position;int noProgress=actor.noProgressTicks;var endpoint=actor.GoalLocation;bool repaired=(bool)typeof(NavCrowd).GetMethod("CommitDetour",flags).Invoke(frame.Working,args);
                    Check(actor.Position.Equals(position)&&actor.noProgressTicks==noProgress&&actor.GoalLocation.Equals(endpoint)&&frame.Formation[row].Flags==hint.Flags,"Repair changed motion/progress/endpoint or the live hint");
                    if(repaired){Check(NavRecastIntervals.TryContinuationRejoin(route,state,actor.Location,actor.localRoute,actor.localIndex,actor.route,actor.routeIndex,actor.GoalLocation.Value,r,out _,frame.Working,actor.Id),"Repair lost full typed/HOLD continuation");Check(actor.localRoute.Length>0&&RecastFormationMotion.Allows(hint,route,position,actor.localRoute[0]),"First repair leg violates the original frame cap");}
                    File.WriteAllText(Path.Combine(output,"repair.json"),JsonConvert.SerializeObject(new{Meaning="Existing bounded CommitDetour on exact detached all-body projection; no production publication/clock/command",DirectionalMotorSuffix=repaired?(bool?)RecastFormationMotion.DirectionalTailAllows(hint,route,actor.localRoute[actor.localRoute.Length-1],actor.route,actor.routeIndex,actor.Goal):null,GuideDistanceBefore=Math.Sqrt(Math.Pow(position.X-hint.Target.X,2)+Math.Pow(position.Z-hint.Target.Z,2)),GuideDistanceAfter=repaired?(double?)Math.Sqrt(Math.Pow(actor.localRoute[actor.localRoute.Length-1].X-hint.Target.X,2)+Math.Pow(actor.localRoute[actor.localRoute.Length-1].Z-hint.Target.Z,2)):null,Repaired=repaired,LiveHintFlags=frame.Formation[row].Flags,FirstLegAllowed=repaired?(bool?)RecastFormationMotion.Allows(hint,route,position,actor.localRoute[0]):null,FutureEndAllowed=repaired?(bool?)RecastFormationMotion.Allows(hint,route,position,actor.localRoute[actor.localRoute.Length-1]):null,frame.Working.RepairCount,frame.Working.RepairFailed,Local=actor.localRoute,actor.localIndex,actor.routeIndex,actor.noProgressTicks,actor.blockedSeconds,Grid=args[2] is NavLocalRepairWorkspace w?new{w.Radius,w.Side,Cell=profile.Navigation.LocalGridCell,Nodes=w.Nodes.Select((n,k)=>new{Index=k,n.Seen,n.Closed,n.X,n.Z,n.Parent,n.Cost,n.Score,n.Side,Point=new NavPoint(position.X+n.X*profile.Navigation.LocalGridCell,position.Z+n.Z*profile.Navigation.LocalGridCell),n.Location}).Where(n=>n.Seen).ToArray()}:null},Formatting.Indented));}
                finally{(args[2] as NavOwnedStorage)?.Release();}
            }
            frame.Release();frame=null;using var recapture=source.CaptureState();Check(bytes.SequenceEqual(WorldWire.Pack(w=>WorldWire.Write(w,recapture))),"Cold projection changed source");
        }}finally{frame?.Release();if(source!=null){source.ReleaseCommittedHostRoot();source.DestroyNavigationAuthority();var b=source.PhysicalHostBudget;File.WriteAllText(Path.Combine(output,"cleanup.json"),JsonConvert.SerializeObject(new{b.PhysicalBytes,b.HeldBytes,b.ActiveScopes,NativeBefore=native,NativeAfter=RecastNative.sw_recast_allocation_count(),NativeFaults=RecastNative.ReleaseFaults-faults}));Check(b.PhysicalBytes==0&&b.HeldBytes==0&&b.ActiveScopes==0&&native==RecastNative.sw_recast_allocation_count()&&faults==RecastNative.ReleaseFaults,"Boundary census mismatch");}}
    }
    // Extend the saved-input component; the historical49 fixture and assertions below stay intact.
    internal static void RunCaptured50(){
        var input=Environment.GetEnvironmentVariable("RECAST_DONOR_INPUT")??throw new ArgumentException("Input directory");
        var output=Environment.GetEnvironmentVariable("RECAST_DONOR_OUTPUT")??throw new ArgumentException("Unique output");
        Check(!Directory.Exists(output),"Output exists");Directory.CreateDirectory(output);
        bool requireBaseline=Environment.GetEnvironmentVariable("RECAST_DONOR_REQUIRE_BASELINE")=="1";
        var bytes=File.ReadAllBytes(Path.Combine(input,"active-current","terminal-nav14.bin"));
        using var saved=WorldWire.Unpack(bytes,WorldWire.ReadNavigationSessionState);
        var profile=PlayableProfile.RecastGroupCorridorDefault;NavigationSession source=null;
        ulong native=RecastNative.sw_recast_allocation_count();long faults=RecastNative.ReleaseFaults;
        var results=new List<object>();
        try{
            source=FormationPreflight.FromSnapshot(saved,profile);
            for(int group=0;group<2;group++){
                var capture=JObject.Parse(File.ReadAllText(Path.Combine(input,"group-"+group,"preparation-cause.json")));
                Check((bool)capture["Complete"]&&(int)capture["PreparationDropped"]==0&&capture["PreparationError"].Type==JTokenType.Null,"Incomplete original capture");
                var ctx=capture["Source"];var original=ctx["Members"].ToArray();Check(original.Length==50,"Captured50 required");
                var owners=original.Select(j=>source.Crowd.Units.Single(a=>a.Id==(int)j["Entity"]).StorageOwner.InstalledExecution as NavigationInstalledExecutionOwner).ToArray();
                Check(owners.All(o=>o!=null),"Missing immutable request owner");var requests=owners.Select(o=>o.Request.Request).ToArray();
                for(int i=0;i<requests.Length;i++){
                    var q=requests[i];var j=original[i];
                    Check(q.Entity==(int)j["Entity"]&&q.Request==(long)j["Request"]&&q.StartLocation.Value.Equals(L(j["Start"]))&&q.GoalLocation.Value.Equals(L(j["Goal"]))&&q.HeldIdentity==(string)j["HeldIdentity"]&&q.Profile.ProfileId==(string)j["ProfileId"]&&q.Profile.Revision==(int)j["Revision"]&&q.Profile.CaptureWireValues().Select(BitConverter.DoubleToInt64Bits).SequenceEqual(j["Profile"].ToObject<double[]>().Select(BitConverter.DoubleToInt64Bits)),"Original request/profile changed");
                    var geom=JToken.Parse(JsonConvert.SerializeObject(new{q.Geometry.Revision,q.Geometry.HalfExtent,Obstacles=q.Geometry.Obstacles.ToArray()}));
                    Check(JToken.DeepEquals(geom,ctx["Geometry"]),"Original geometry changed");
                    Check(ReferenceEquals(q.Geometry,requests[0].Geometry),"Original shared geometry alias lost");
                }
                var geometry=requests[0].Geometry;var authority=RecastAuthorityGeometry.Prepare(profile.AuthoredMap,geometry,requests[0].Profile);
                var sp=ctx["Spine"];Check(authority.Binding==(string)sp["MapBinding"],"Authority map binding changed");
                var acceptedSpine=owners[0].Corridor.TypedRoute.RecastPlan.Spine;
                Check(acceptedSpine.Binding==(string)sp["Binding"]&&acceptedSpine.MapBinding==authority.Binding&&acceptedSpine.FanNode==(int)sp["Fan"]&&acceptedSpine.FanCenter.Equals(P(sp["FanCenter"]))&&acceptedSpine.FanRadius==(double)sp["FanRadius"],"Accepted spine binding changed");
                // Capture omitted Cost: recover each exact value from its matching immutable owned transcript.
                NavTypedLeg[] Recover(JToken recorded,IReadOnlyList<NavTypedLeg> owned){
                    var a=recorded.ToArray();Check(a.Length==owned.Count,"Owned transcript length changed");var legs=new NavTypedLeg[a.Length];
                    for(int i=0;i<a.Length;i++){var j=a[i];var l=owned[i];Check(l.From.Equals(L(j["From"]))&&l.To.Equals(L(j["To"]))&&l.Kind==Enum.Parse<NavTypedLegKind>((string)j["Kind"])&&l.PortalId==(string)j["PortalId"],"Captured leg does not match owned transcript at "+i);legs[i]=new NavTypedLeg(l.From,l.To,l.Cost,l.Kind,l.PortalId);}return legs;
                }
                var spineLegs=Recover(sp["Legs"],acceptedSpine.Legs);
                Check(source.TryOpenPhysicalHostScope(NavigationPreparationKind.Solve,805,source.Admission.PublicationIdentity,out var scope),"Component scope refused");
                PartitionNavigationProvider provider=null;RecastPayload payload=null;RecastAttachmentPreparation helper=null;
                var literal=new NavCrowd(geometry,requests[0].Profile,profile.AuthoredMap);
                try{using(scope){var r=scope.OwnedReservation();
                    provider=PartitionNavigationProvider.CopyOwned(new PartitionNavigationProvider(profile.AuthoredMap,geometry,requests[0].HeldIdentity),r);
                    Check(provider.Binding==(string)ctx["ProviderBinding"]&&acceptedSpine.ProviderBinding==provider.Binding,"Original provider binding changed");
                    long size=32+8L*requests.Length;
                    size=checked(size+NavStorageLayout.ObjectBytesBound(typeof(RecastSpineProposal))+88+(NavTypedLeg.StorageCharge+NavStorageLayout.ValueBytes(typeof(NavTypedLeg)))*spineLegs.Length+4L*sp["Stops"].Count()+24+2L*((string)sp["Binding"]).Length);
                    foreach(var j in ctx["Baseline"])if(j.Type!=JTokenType.Null)size=checked(size+NavStorageLayout.ObjectBytesBound(typeof(RecastMemberRoute))+64+(NavTypedLeg.StorageCharge+NavStorageLayout.ValueBytes(typeof(NavTypedLeg)))*j["Legs"].Count()+NavStorageLayout.ValueBytes(typeof(RecastRoutePoint))*j["Waypoints"].Count());
                    payload=RecastPayload.Allocate(r,size);
                    var spine=new RecastSpineProposal(spineLegs,(string)sp["Binding"],(string)sp["MapBinding"],(int)sp["Fan"],sp["Stops"].ToObject<int[]>(),P(sp["FanCenter"]),(double)sp["FanRadius"]);
                    var baseline=ctx["Baseline"].Select((j,i)=>j.Type==JTokenType.Null?null:new RecastMemberRoute(j["Waypoints"].Select(w=>new RecastRoutePoint(L(w["Location"]),(double)w["Height"],(string)w["PortalId"])).ToArray(),Recover(j["Legs"],owners[i].Corridor.TypedRoute.Legs),spine,(int)j["Join"],(int)j["Exit"],(int)j["OriginLegs"],(int)j["TerminalLeg"],true)).ToArray();payload.Value=baseline;
                    Check(baseline.Count(a=>a!=null)==(group==0?28:26),"Captured native baseline changed");
                    foreach(var q in requests)literal.Add(q.Entity,q.Start,q.Profile.Radius,q.Profile.TankSpeed,q.Profile.TankTurnSpeed,q.StartLocation);
                    helper=RecastAttachmentPreparation.Create(requests.Length,r);int quanta=0;var timer=System.Diagnostics.Stopwatch.StartNew();
                    while(!helper.Complete){long before=helper.Work;helper.Step(requests,baseline,spine,provider,authority,r,256,null);Check(helper.Work>before&&helper.Work-before<=256,"Step256 charge/progress changed");quanta++;}
                    long expected=(long)capture["Rows"].Last()["Work"];
                    var flags=BindingFlags.Instance|BindingFlags.NonPublic;var field=(RecastAttachmentField)typeof(RecastAttachmentPreparation).GetField("field",flags).GetValue(helper);
                    File.WriteAllText(Path.Combine(output,"group-"+group+"-work.json"),JsonConvert.SerializeObject(new{helper.Work,BaselineWork=expected,Quanta=quanta,Seconds=timer.Elapsed.TotalSeconds,Field=new{field.Work,field.Traces,field.Expansions},RecordedField=capture["Rows"].Last()["Field"]},Formatting.Indented));
                    var recordedField=capture["Rows"].Last()["Field"];
                    if(requireBaseline){
                        Check(field.Work==(long)recordedField["Work"]&&field.Traces==(long)recordedField["Traces"]&&field.Expansions==(long)recordedField["Expansions"],"Exact captured field work/trace/expansion changed");
                        int count=(int)typeof(RecastAttachmentField).GetField("members",flags).GetValue(field);
                        Check(((int[])typeof(RecastAttachmentField).GetField("resolved",flags).GetValue(field)).Take(count).SequenceEqual(recordedField["Resolved"].ToObject<int[]>()),"Exact resolved nodes changed");
                        Check(JToken.DeepEquals(JToken.FromObject(((NavPoint[])typeof(RecastAttachmentField).GetField("targets",flags).GetValue(field)).Take(count).ToArray()),recordedField["Targets"]),"Exact field targets changed");
                    }
                    int validations=0;var routes=new List<object>();
                    for(int i=0;i<requests.Length;i++){
                        var q=requests[i];var route=helper.WorkingRoutes[i];Check(route!=null&&route.Legs[0].From.Equals(q.StartLocation.Value)&&route.Legs.Last().To.Equals(q.GoalLocation.Value),"Incomplete exact endpoints");
                        for(int k=1;k<route.Legs.Length;k++)Check(route.Legs[k-1].To.Equals(route.Legs[k].From),"Disconnected transcript");
                        var owned=new List<NavTypedLeg>();NavRecastMemberPlan plan=null;NavTypedRoute typed=null;
                        try{
                            // Ordinary registration traces the complete waypoint edges.
                            // CanSweep uses TryGraphTrace and is a different partition predicate.
                            int transcript=0;var at=q.StartLocation.Value;var clearance=new NavClearanceProfile("ground",1,"ground",0,q.Profile.Radius);
                            foreach(var waypoint in route.Waypoints){IReadOnlyList<NavTypedLeg> trace=null;try{
                                Check(provider.TryTraceOwned(at,waypoint.Location.Position,clearance,out var reached,out trace,r)&&reached.Equals(waypoint.Location),"Normal registration waypoint rejected");
                                foreach(var leg in trace){Check(transcript<route.Legs.Length,"Normal transcript overflow");var expectedLeg=route.Legs[transcript++];Check(leg.From.Equals(expectedLeg.From)&&leg.To.Equals(expectedLeg.To)&&leg.Kind==expectedLeg.Kind&&leg.PortalId==expectedLeg.PortalId&&leg.Cost==expectedLeg.Cost,"Normal full transcript differs");}at=reached;
                            }finally{(trace as NavOwnedStorage)?.ReleaseOwner();}}
                            Check(transcript==route.Legs.Length&&at.Equals(q.GoalLocation.Value),"Normal transcript count/goal differs");
                            foreach(var leg in route.Legs){owned.Add(NavTypedLeg.CreateOwned(leg.From,leg.To,leg.Cost,leg.Kind,leg.PortalId,r,provider));if(leg.Kind==NavTypedLegKind.Surface){Check(geometry.SegmentFree(leg.From.Position,leg.To.Position,q.Profile.Radius),"Physical geometry leg rejected");validations++;}}
                            var old=owners[i].Corridor.TypedRoute;var oldPlan=old.RecastPlan;
                            plan=NavRecastMemberPlan.Create(oldPlan.Spine,route.Join,route.Exit,route.OriginLegs,route.TerminalLeg,oldPlan.GraphBinding,oldPlan.ProfileBinding,r);
                            typed=NavTypedRoute.CopySubscriptionAndCreateOwned(old.Subscription,owned,route.Legs.Sum(l=>l.Cost),r,plan);plan.Validate(typed);
                            var points=owned.Where(l=>l.Kind==NavTypedLegKind.Surface).Select(l=>l.To.Position).ToArray();
                            Check(NavRecastIntervals.TryContinuationRejoin(typed,NavRecastIntervals.Initial(typed),q.StartLocation.Value,Array.Empty<NavPoint>(),0,points,0,q.GoalLocation.Value,r,out _,literal,q.Entity),"Full typed literal continuation refused");
                            if(requireBaseline){Check(route.Legs.Length==old.Legs.Count,"Baseline route length differs");for(int k=0;k<route.Legs.Length;k++){var a=route.Legs[k];var b=old.Legs[k];Check(a.From.Equals(b.From)&&a.To.Equals(b.To)&&a.Cost==b.Cost&&a.Kind==b.Kind&&a.PortalId==b.PortalId,"Baseline transcript differs");}}
                        }finally{typed?.ReleaseStorage();plan?.Release();foreach(var l in owned)l.ReleaseStorage();}
                        routes.Add(new{q.Entity,route.Join,route.Exit,route.OriginLegs,route.TerminalLeg,Legs=route.Legs.Select(LegValue).ToArray(),route.Waypoints});
                    }
                    // Host's residual preparation grants were not recorded. Fixed256 can
                    // repeat a partially copied trace (Origin/Terminal source charges it).
                    // Compare exact field operations and full transcripts; preserve BOTH totals.
                    var item=new{Group=group,Result="PASS",Work=helper.Work,BaselineWork=expected,TotalWorkExact=helper.Work==expected,WorkerGrantSequence="NotRecorded/NotReplayed; standalone fixed256",NativeBaseline=baseline.Count(a=>a!=null),Members=requests.Length,SurfaceValidations=validations,AllFullTypedLiteral=true,ExactOriginalTranscripts=requireBaseline,ExactFieldOperations=requireBaseline,Routes=routes};results.Add(item);
                    File.WriteAllText(Path.Combine(output,"group-"+group+"-result.json"),JsonConvert.SerializeObject(item,Formatting.Indented));
                    if(!requireBaseline)Check(helper.Work<expected,"Captured work reduction falsified");
                    using(var cancelled=RecastAttachmentPreparation.Create(requests.Length,r)){
                        RecastAttachmentField active=null;
                        while(active==null||active.Work==0){cancelled.Step(requests,baseline,spine,provider,authority,r,256,null);active=(RecastAttachmentField)typeof(RecastAttachmentPreparation).GetField("field",flags).GetValue(cancelled);Check(!cancelled.Complete,"No active field for cancellation");}
                        long before=cancelled.Work;bool caught=false;try{cancelled.Step(requests,baseline,spine,provider,authority,r,256,()=>true);}catch(OperationCanceledException){caught=true;}
                        Check(caught&&cancelled.Work==before&&!cancelled.Complete,"Active field cancellation changed work/output");
                    }
                }}finally{helper?.Dispose();payload?.Dispose();provider?.StorageRoot.Release();literal.ReleaseCrowdStorage();}
            }
            using var current=source.CaptureState();Check(bytes.SequenceEqual(WorldWire.Pack(w=>WorldWire.Write(w,current))),"Immutable source changed");
            File.WriteAllText(Path.Combine(output,"result.json"),JsonConvert.SerializeObject(new{Result="PASS",Groups=2,Members=100,RequireExactBaseline=requireBaseline,ExactSourceUnchanged=true,NativeQueries=0,WorldSteps=0,Results=results.Select((_,i)=>"group-"+i+"-result.json")},Formatting.Indented));
        }finally{
            if(source!=null){source.ReleaseCommittedHostRoot();source.DestroyNavigationAuthority();var b=source.PhysicalHostBudget;var after=RecastNative.sw_recast_allocation_count();File.WriteAllText(Path.Combine(output,"cleanup.json"),JsonConvert.SerializeObject(new{b.PhysicalBytes,b.HeldBytes,b.ActiveScopes,NativeBefore=native,NativeAfter=after,NativeFaults=RecastNative.ReleaseFaults-faults}));Check(b.PhysicalBytes==0&&b.HeldBytes==0&&b.ActiveScopes==0&&native==after&&RecastNative.ReleaseFaults==faults,"Captured component lifetime/native mismatch");}
        }
    }
    static void Check(bool v,string why){if(!v)throw new InvalidOperationException(why);}
    static object FirstDifference(JToken a,JToken b,string path){
        if(a.Type!=b.Type)return new{Path=path,CurrentType=a.Type.ToString(),RecordedType=b.Type.ToString(),Current=a.ToString(Formatting.None),Recorded=b.ToString(Formatting.None)};
        if(a is JObject ao&&b is JObject bo){var ap=ao.Properties().ToArray();var bp=bo.Properties().ToArray();if(!ap.Select(x=>x.Name).SequenceEqual(bp.Select(x=>x.Name)))return new{Path=path,Reason="Object field/order differs"};foreach(var p in ap){var d=FirstDifference(p.Value,bo[p.Name],path+"."+p.Name);if(d!=null)return d;}return null;}
        if(a is JArray aa&&b is JArray ba){if(aa.Count!=ba.Count)return new{Path=path,Reason="Array length differs"};for(int i=0;i<aa.Count;i++){var d=FirstDifference(aa[i],ba[i],path+"["+i+"]");if(d!=null)return d;}return null;}
        return JToken.DeepEquals(a,b)?null:new{Path=path,CurrentType=a.Type.ToString(),RecordedType=b.Type.ToString(),Current=a.ToString(Formatting.None),Recorded=b.ToString(Formatting.None)};
    }
    static NavPoint P(JToken j)=>new NavPoint((double)j["X"],(double)j["Z"]);static NavLocation L(JToken j)=>new NavLocation(P(j["Position"]),(string)j["SurfaceId"]);
    static NavTypedLeg[] Legs(JToken values)=>values.Select(j=>new NavTypedLeg(L(j["From"]),L(j["To"]),(double)j["Cost"],Enum.Parse<NavTypedLegKind>((string)j["Kind"]),(string)j["PortalId"])).ToArray();
    static double Approach(IEnumerable<NavTypedLeg> legs,NavPoint start,string entry){double total=0;var at=start;foreach(var l in legs){double x=l.To.Position.X-at.X,z=l.To.Position.Z-at.Z;total+=Math.Sqrt(x*x+z*z);at=l.To.Position;if(l.Kind==NavTypedLegKind.Portal&&l.PortalId==entry)return total;}throw new InvalidOperationException("No actual initial entry");}
    static object LegValue(NavTypedLeg l)=>new{From=l.From,To=l.To,l.Cost,l.Kind,l.PortalId};
    internal static void Run(bool baselineOnly=false,bool evidenceOnly=false,bool sparseOnly=false){
        var input=Environment.GetEnvironmentVariable("RECAST_DONOR_INPUT")??throw new ArgumentException("Input directory");var output=Environment.GetEnvironmentVariable("RECAST_DONOR_OUTPUT")??throw new ArgumentException("Unique output");Check(!Directory.Exists(output),"Output exists");Directory.CreateDirectory(output);
        var ctx=JObject.Parse(File.ReadAllText(Path.Combine(input,"original-context-1.json")));var prior=JObject.Parse(File.ReadAllText(Path.Combine(input,"phase-audit.json")));var formation=JObject.Parse(File.ReadAllText(Path.Combine(input,"formation-observation.json")))["Mapping"];var bytes=File.ReadAllBytes(Path.Combine(input,"terminal-nav14.bin"));using var saved=WorldWire.Unpack(bytes,WorldWire.ReadNavigationSessionState);var profile=PlayableProfile.RecastGroupCorridorDefault;
        NavigationSession source=null;PartitionNavigationProvider provider=null;RecastPayload payload=null;RecastAttachmentPreparation helper=null;var rows=new List<object>();ulong native=RecastNative.sw_recast_allocation_count();long nativeFaults=RecastNative.ReleaseFaults;int validations=0;bool cancelled=false,capacity=false,refused=false;int activeEntity=-1,activeLeg=-1,activePoint=-1;string activePredicate="setup";
        try{
            source=FormationPreflight.FromSnapshot(saved,profile);var original=ctx["Members"].ToArray();var owners=original.Select(j=>source.Crowd.Units.Single(a=>a.Id==(int)j["Entity"]).StorageOwner.InstalledExecution as NavigationInstalledExecutionOwner).ToArray();Check(owners.All(o=>o!=null),"Missing immutable original request");var requests=owners.Select(o=>o.Request.Request).ToArray();Check(requests.Length==49,"Original49 required");
            for(int i=0;i<requests.Length;i++){var q=requests[i];var j=original[i];Check(q.Entity==(int)j["Entity"]&&q.Request==(long)j["Request"]&&q.StartLocation.Value.Equals(L(j["Start"]))&&q.GoalLocation.Value.Equals(L(j["Goal"]))&&q.HeldIdentity==(string)j["HeldIdentity"]&&q.Profile.ProfileId==(string)j["ProfileId"]&&q.Profile.Revision==(int)j["Revision"]&&q.Profile.CaptureWireValues().Select(BitConverter.DoubleToInt64Bits).SequenceEqual(j["Profile"].ToObject<double[]>().Select(BitConverter.DoubleToInt64Bits)),"Immutable original request/profile differs");var currentGeometry=JToken.Parse(JsonConvert.SerializeObject(new{q.Geometry.Revision,q.Geometry.HalfExtent,Obstacles=q.Geometry.Obstacles.ToArray()}));var difference=FirstDifference(currentGeometry,j["Geometry"],"Members["+i+"].Geometry");if(difference!=null)File.WriteAllText(Path.Combine(output,"geometry-first-difference.json"),JsonConvert.SerializeObject(difference,Formatting.Indented));Check(difference==null&&JToken.DeepEquals(currentGeometry,j["Geometry"]),"Original geometry differs after exact producer serialization");}
            var geometry=requests[0].Geometry;var literal=new NavCrowd(geometry,requests[0].Profile,profile.AuthoredMap);foreach(var q in requests)literal.Add(q.Entity,q.Start,q.Profile.Radius,q.Profile.TankSpeed,q.Profile.TankTurnSpeed,q.StartLocation);
            var authority=RecastAuthorityGeometry.Prepare(profile.AuthoredMap,geometry,requests[0].Profile);Check(authority.Binding==(string)ctx["Spine"]["MapBinding"],"Managed authority map binding differs");
            Check(source.TryOpenPhysicalHostScope(NavigationPreparationKind.Solve,805,source.Admission.PublicationIdentity,out var scope),"Component scope refused");using(scope){var r=scope.OwnedReservation();provider=PartitionNavigationProvider.CopyOwned(new PartitionNavigationProvider(profile.AuthoredMap,geometry,requests[0].HeldIdentity),r);Check(provider.Binding==(string)ctx["ProviderBinding"],"Provider binding differs");
                long size=32+8L*49;var sp=ctx["Spine"];size=checked(size+NavStorageLayout.ObjectBytesBound(typeof(RecastSpineProposal))+88+(NavTypedLeg.StorageCharge+NavStorageLayout.ValueBytes(typeof(NavTypedLeg)))*(long)sp["Legs"].Count()+4L*sp["Stops"].Count()+24+2L*((string)sp["Binding"]).Length);
                foreach(var j in ctx["Baseline"])if(j.Type!=JTokenType.Null)size=checked(size+NavStorageLayout.ObjectBytesBound(typeof(RecastMemberRoute))+64+(NavTypedLeg.StorageCharge+NavStorageLayout.ValueBytes(typeof(NavTypedLeg)))*(long)j["Legs"].Count()+NavStorageLayout.ValueBytes(typeof(RecastRoutePoint))*(long)j["Waypoints"].Count());
                payload=RecastPayload.Allocate(r,size);var spine=new RecastSpineProposal(Legs(sp["Legs"]),(string)sp["Binding"],(string)sp["MapBinding"],(int)sp["Fan"],sp["Stops"].ToObject<int[]>(),P(sp["FanCenter"]),(double)sp["FanRadius"]);var baseline=ctx["Baseline"].Select(j=>j.Type==JTokenType.Null?null:new RecastMemberRoute(j["Waypoints"].Select(w=>new RecastRoutePoint(L(w["Location"]),(double)w["Height"],(string)w["PortalId"])).ToArray(),Legs(j["Legs"]),spine,(int)j["Join"],(int)j["Exit"],(int)j["OriginLegs"],(int)j["TerminalLeg"],true)).ToArray();payload.Value=baseline;Check(baseline.Count(a=>a!=null)==33,"Native33/null16 baseline changed");
                if(baselineOnly){
                    var inspected=new List<object>();int surfaceCount=0,failedCount=0;var failures=new List<object>();
                    for(int i=0;i<requests.Length;i++){
                        var q=requests[i];var oldTyped=owners[i].Corridor.TypedRoute;
                        void Inspect(string set,NavTypedLeg[] legs,int origin,int terminal,int join,int exit){
                            for(int index=0;index<legs.Length;index++){
                                var leg=legs[index];if(leg.Kind!=NavTypedLegKind.Surface)continue;surfaceCount++;
                                var clearance=new NavClearanceProfile("ground",1,"ground",0,q.Profile.Radius);
                                bool segment=geometry.SegmentFree(leg.From.Position,leg.To.Position,q.Profile.Radius);
                                bool sweep=provider.CanSweep(leg.From,leg.To,clearance);
                                var matches=oldTyped.Legs.Select((l,k)=>new{Leg=l,Index=k}).Where(m=>m.Leg.From.Equals(leg.From)&&m.Leg.To.Equals(leg.To)&&m.Leg.Kind==leg.Kind&&m.Leg.PortalId==leg.PortalId&&m.Leg.Cost==leg.Cost).Select(m=>{
                                    var ap=m.Leg.RecastAuthorityProvider;var ac=new NavClearanceProfile("ground",1,"ground",(int)oldTyped.Subscription.MobilityRevision,oldTyped.RecastPlan.Spine.Radius);
                                    return new{m.Index,ProviderType=ap?.GetType().FullName,ProviderBinding=ap?.Binding,SameBinding=ap?.Binding==provider.Binding,Clearance=ac,AuthorityCanSweep=ap?.CanSweep(m.Leg.From,m.Leg.To,ac),PlanRadius=oldTyped.RecastPlan.Spine.Radius,oldTyped.Subscription.MobilityRevision};
                                }).ToArray();
                                var row=new{Set=set,q.Entity,RequestIndex=i,LegIndex=index,Role=index<origin?"origin":index>=terminal?"terminal":"spine",Join=join,Exit=exit,OriginLegs=origin,TerminalLeg=terminal,Leg=LegValue(leg),Clearance=clearance,ProfileId=q.Profile.ProfileId,ProfileRevision=q.Profile.Revision,ProfileWireCount=q.Profile.WireValueCount,ProfileWireBits=q.Profile.CaptureWireValues().Select(BitConverter.DoubleToInt64Bits).ToArray(),q.HeldIdentity,GeometryRevision=geometry.Revision,ProviderBinding=provider.Binding,ProviderType=provider.GetType().FullName,GeometrySegmentFree=segment,ProviderCanSweep=sweep,Predicate=segment&&sweep,RestoredTypedMatches=matches};
                                inspected.Add(row);if(!segment||!sweep){failedCount++;failures.Add(row);}
                            }
                        }
                        if(baseline[i]!=null){var b=baseline[i];Inspect("original-native33",b.Legs,b.OriginLegs,b.TerminalLeg,b.Join,b.Exit);}
                        var old=prior["Helpers"][0]["FinalRoutes"][i];Inspect("recorded-old49",Legs(old["Legs"]),(int)old["OriginLegs"],(int)old["TerminalLeg"],(int)old["Join"],(int)old["Exit"]);
                    }
                    // Complete census is durable before any final assertion. No helper or field is created.
                    File.WriteAllText(Path.Combine(output,"baseline-inspection.json"),JsonConvert.SerializeObject(new{Meaning="Diagnostic predicate evaluation only, not production preparation Work; no helper.Step/World/native long query",OriginalNativeRoutes=baseline.Count(a=>a!=null),RecordedOldRoutes=requests.Length,SurfaceCount=surfaceCount,FailedCount=failedCount,Failures=failures,Rows=inspected},Formatting.Indented));
                    using var restored=source.CaptureState();bool unchanged=bytes.SequenceEqual(WorldWire.Pack(w=>WorldWire.Write(w,restored)));File.WriteAllText(Path.Combine(output,"source-unchanged.json"),JsonConvert.SerializeObject(new{ExactSavedSourceUnchanged=unchanged}));Check(unchanged,"Baseline inspector mutated saved source");return;
                }
                if(!evidenceOnly){
                var fallback=RecastAttachmentPreparation.Create(49,r);try{
                    var mixed=(NavigationRequest[])requests.Clone();var oldQ=requests[1];var values=Enumerable.Range(0,NavigationProfile.CrowdWireValueCount).Select(oldQ.Profile.WireValueAt).ToArray();int changed=NavigationProfile.LegacyWireValueCount+2;values[changed]=BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(values[changed])+1);var altered=NavigationProfile.FromWireValues(oldQ.Profile.ProfileId,oldQ.Profile.Revision,values);
                    mixed[1]=new NavigationRequest(oldQ.Session,oldQ.Request,oldQ.Entity,oldQ.Order,altered,oldQ.Geometry,oldQ.Start,oldQ.Goal,oldQ.BaseGeometry,oldQ.HeldIdentity,oldQ.MemberIdentity,oldQ.StartLocation,oldQ.GoalLocation);
                    var flags=BindingFlags.Instance|BindingFlags.NonPublic;var stage=typeof(RecastAttachmentPreparation).GetField("stage",flags);var fieldInfo=typeof(RecastAttachmentPreparation).GetField("field",flags);
                    while(stage.GetValue(fallback).ToString()!="Field"){long before=fallback.Work;fallback.Step(mixed,baseline,spine,provider,authority,r,256,null);Check(fallback.Work-before<=256&&!fallback.Complete,"Fallback did not reach old field seed setup");}
                    var oldField=(RecastAttachmentField)fieldInfo.GetValue(fallback);int seedCount=(int)typeof(RecastAttachmentField).GetField("seedCount",flags).GetValue(oldField);var donorMeta=(int[])typeof(RecastAttachmentField).GetField("seedDonors",flags).GetValue(oldField);var seedStops=(int[])typeof(RecastAttachmentField).GetField("seedStops",flags).GetValue(oldField);var seeds=(NavPoint[])typeof(RecastAttachmentField).GetField("seeds",flags).GetValue(oldField);var expectedSeeds=prior["Fields"][0]["SeedInputs"].ToArray();Check(seedCount==expectedSeeds.Length&&donorMeta.Take(seedCount).All(i=>i==-1),"Binding mismatch accepted donor roots");for(int i=0;i<seedCount;i++)Check(seedStops[i]==(int)expectedSeeds[i]["Stop"]&&seeds[i].Equals(P(expectedSeeds[i]["Position"])),"Binding fallback changed original roots");
                    long atCancel=fallback.Work;try{fallback.Step(mixed,baseline,spine,provider,authority,r,256,()=>true);}catch(OperationCanceledException){cancelled=true;}Check(cancelled&&fallback.Work==atCancel&&!fallback.Complete,"Partial fallback cancellation advanced work");
                }finally{fallback.Dispose();}
                var cap=RecastAttachmentField.Create(requests[0].Profile.LocalGridCell,spine.FanRadius,49,checked(49+spine.Stops.Length),((INavStorageFacade)provider).StorageRoot,r);try{try{cap.Begin(spine.FanCenter,spine.FanRadius,requests[0].GoalLocation.Value.SurfaceId,false,1,50+spine.Stops.Length,r);}catch(ArgumentException){capacity=true;}Check(capacity&&cap.Work==0,"Derived seed capacity not guarded");cap.Begin(spine.FanCenter,spine.FanRadius,requests[0].GoalLocation.Value.SurfaceId,false,1,1,r);cap.SetSeed(0,spine.Fan,spine.Node(spine.Fan),7);cap.SetSeed(0,spine.Fan,spine.Node(spine.Fan));Check(((int[])typeof(RecastAttachmentField).GetField("seedDonors",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(cap))[0]==-1,"Normal seed reuse retained donor provenance");}finally{cap.Dispose();}
                var deny=new NavigationRetentionBudget();Check(deny.TryOpenScope(NavigationPreparationKind.Solve,805,0,out var ds),"Refusal scope");using(ds){deny.AllocationAdmission=_=>false;try{RecastAttachmentPreparation.Create(49,ds.OwnedReservation()).Dispose();}catch(InvalidOperationException e)when(e.Message=="Physical storage exceeds prepared reservation."){refused=true;}finally{deny.AllocationAdmission=null;}}Check(refused&&deny.PhysicalBytes==0&&deny.HeldBytes==0&&deny.ActiveScopes==0,"Birth refusal cleanup");
                } // Negative controls reused from exact-runtime component3 in evidence-only continuation.
                helper=RecastAttachmentPreparation.Create(49,r);while(!helper.Complete){long before=helper.Work;helper.Step(requests,baseline,spine,provider,authority,r,256,null);Check(helper.Work-before<=256,"Step256 charge exceeded");}
                activePredicate="prevalidation";
                void Require(bool ok,string why){if(ok)return;File.WriteAllText(Path.Combine(output,"validation-failure.json"),JsonConvert.SerializeObject(new{Entity=activeEntity,Leg=activeLeg,Point=activePoint,Predicate=activePredicate,Failure=why,Work=helper.Work},Formatting.Indented));throw new InvalidOperationException(why);}
                if(evidenceOnly){
                    var flags=BindingFlags.Instance|BindingFlags.NonPublic;var field=(RecastAttachmentField)typeof(RecastAttachmentPreparation).GetField("field",flags).GetValue(helper);
                    object F(string name)=>typeof(RecastAttachmentField).GetField(name,flags).GetValue(field);
                    var workspace=F("workspace");int fieldMembers=(int)F("members"),fieldSeeds=(int)F("seedCount");
                    var indices=(int[])typeof(RecastAttachmentPreparation).GetField("indices",flags).GetValue(helper);
                    var checkpoint=new{Work=helper.Work,BaselineWork=1942806,Field=new{field.Work,field.Traces,field.Expansions,field.Status,field.NodeCapacity,field.Count,Members=fieldMembers,SeedCount=fieldSeeds,Remaining=F("remainingMembers"),Open=workspace.GetType().GetField("OpenCount",flags|BindingFlags.Public).GetValue(workspace),Phase=F("phase").ToString(),SeedStops=((int[])F("seedStops")).Take(fieldSeeds).ToArray(),SeedDonors=((int[])F("seedDonors")).Take(fieldSeeds).ToArray(),Seeds=((NavPoint[])F("seeds")).Take(fieldSeeds).ToArray(),Targets=Enumerable.Range(0,fieldMembers).Select(i=>new{Index=i,Entity=requests[indices[i]].Entity,Resolved=field.Resolved(i),SourceStop=field.Resolved(i)?field.SourceStop(i):-1,SourceDonor=field.Resolved(i)?field.SourceDonor(i):-1}).ToArray(),FinePhaseCounters="NotRecorded in default symbolOFF source"},Routes=helper.WorkingRoutes.Select((route,i)=>new{requests[i].Entity,RequestIndex=i,Complete=route!=null,Route=route==null?null:new{route.Join,route.Exit,route.OriginLegs,route.TerminalLeg,Cost=route.Legs.Sum(l=>l.Cost),PriorCost=Legs(prior["Helpers"][0]["FinalRoutes"][i]["Legs"]).Sum(l=>l.Cost),Legs=route.Legs.Select(LegValue).ToArray(),Waypoints=route.Waypoints.Select(w=>new{w.Location,w.Height,w.PortalId}).ToArray()}}).ToArray(),NegativeControls=sparseOnly?"Reused component3 PASS unaffected mismatchfallback/cancel/cap/reset/birthrefusal paths; sparse selection after successful bindings only":"Reused PASS component3 exact runtime261: mismatchfallback/cancel/cap/reset/birthrefusal",Meaning="Candidate checkpoint BEFORE validation; diagnostic evaluation work separate from preparation Work"};
                    File.WriteAllText(Path.Combine(output,"candidate-prevalidation.json"),JsonConvert.SerializeObject(checkpoint,Formatting.Indented));
                    if(sparseOnly){
                        var terminalPayloads=(RecastPayload[])typeof(RecastAttachmentPreparation).GetField("terminals",flags).GetValue(helper);
                        var helperExits=(int[])typeof(RecastAttachmentPreparation).GetField("exits",flags).GetValue(helper);
                        var proofs=new List<object>();var expectedUnion=new List<int>();
                        for(int targetIndex=0;targetIndex<fieldMembers;targetIndex++){
                            var q=requests[indices[targetIndex]];var candidates=new List<(int Donor,double Distance)>();
                            for(int donor=0;donor<requests.Length;donor++){
                                int exit=helperExits[donor];var goal=requests[donor].GoalLocation.Value;var route=helper.WorkingRoutes[donor];
                                if(exit<0||terminalPayloads[donor]!=null||route!=null&&!ReferenceEquals(route.Spine,spine)||exit<spine.Fan||spine.Node(exit).SurfaceId!=goal.SurfaceId)continue;
                                IReadOnlyList<NavTypedLeg> trace=null;bool qualified=false;
                                try{qualified=geometry.SegmentFree(spine.Node(exit).Position,goal.Position,q.Profile.Radius)&&provider.TryTraceOwned(spine.Node(exit),goal.Position,new NavClearanceProfile("ground",1,"ground",0,q.Profile.Radius),out var reached,out trace,r)&&reached.Equals(goal);}finally{(trace as NavOwnedStorage)?.ReleaseOwner();}
                                if(!qualified)continue;double dx=goal.Position.X-q.Goal.X,dz=goal.Position.Z-q.Goal.Z;candidates.Add((donor,dx*dx+dz*dz));
                            }
                            var rankedDonors=candidates.OrderBy(x=>x.Distance).ThenBy(x=>requests[x.Donor].Entity).ThenBy(x=>requests[x.Donor].Request).ToArray();int winner=rankedDonors.Length==0?-1:rankedDonors[0].Donor;
                            if(winner>=0&&!expectedUnion.Contains(winner))expectedUnion.Add(winner);
                            proofs.Add(new{Target=q.Entity,TargetIndex=targetIndex,WinnerMember=winner,WinnerEntity=winner<0?-1:requests[winner].Entity,Candidates=rankedDonors.Select(x=>new{Member=x.Donor,requests[x.Donor].Entity,requests[x.Donor].Request,DistanceSquared=x.Distance}).ToArray()});
                        }
                        var oldSeeds=prior["Fields"][0]["SeedInputs"].ToArray();var seedDonors=(int[])F("seedDonors");var seedStops=(int[])F("seedStops");var seedPoints=(NavPoint[])F("seeds");
                        bool oldRetained=fieldSeeds==oldSeeds.Length+expectedUnion.Count;
                        for(int i=0;i<oldSeeds.Length&&i<fieldSeeds;i++)oldRetained&=seedDonors[i]==-1&&seedStops[i]==(int)oldSeeds[i]["Stop"]&&seedPoints[i].Equals(P(oldSeeds[i]["Position"]));
                        bool exact=oldRetained&&expectedUnion.Count<=fieldMembers;
                        for(int i=0;i<expectedUnion.Count&&oldSeeds.Length+i<fieldSeeds;i++){int donor=expectedUnion[i],at=oldSeeds.Length+i;exact&=seedDonors[at]==donor&&seedStops[at]==helperExits[donor]&&seedPoints[at].Equals(requests[donor].Goal);}
                        File.WriteAllText(Path.Combine(output,"sparse-root-proof.json"),JsonConvert.SerializeObject(new{UnresolvedCount=fieldMembers,ExpectedUnion=expectedUnion.Select(i=>new{Member=i,requests[i].Entity}).ToArray(),OriginalSpineRootsRetained=oldRetained,ExactArgminDedupRoots=exact,Proofs=proofs,Meaning="Independent diagnostic qualification/comparison work, not preparation Work"},Formatting.Indented));
                        activePredicate="exact sparse argmin roots and ALL old roots";Require(exact,"Sparse roots differ from recipient-qualified argmin union/old baseline");
                    }

                    var physical=new List<object>();int physicalFailed=0;
                    for(int i=0;i<requests.Length;i++){
                        var q=requests[i];var route=helper.WorkingRoutes[i];activeEntity=q.Entity;activePredicate="complete candidate";Require(route!=null,"Missing candidate");
                        var clearance=new NavClearanceProfile("ground",1,"ground",0,q.Profile.Radius);
                        for(int j=0;j<route.Legs.Length;j++){
                            var leg=route.Legs[j];bool certificate=provider.CertifyHistoricalLeg(leg,clearance,incidentEndpoints:true);
                            bool segment=leg.Kind!=NavTypedLegKind.Surface||geometry.SegmentFree(leg.From.Position,leg.To.Position,q.Profile.Radius);
                            bool? sweep=leg.Kind==NavTypedLegKind.Surface?provider.CanSweep(leg.From,leg.To,clearance):(bool?)null;
                            physical.Add(new{q.Entity,LegIndex=j,Role=j<route.OriginLegs?"origin":j>=route.TerminalLeg?"terminal":"spine",Leg=LegValue(leg),Clearance=clearance,GeometrySegmentFree=segment,IncidentCertificate=certificate,CanSweepDiagnostic=sweep,Passed=segment&&certificate});if(!segment||!certificate)physicalFailed++;
                        }
                    }
                    File.WriteAllText(Path.Combine(output,"physical-leg-validation.json"),JsonConvert.SerializeObject(new{Failed=physicalFailed,Rows=physical},Formatting.Indented));
                    activeEntity=activeLeg=activePoint=-1;activePredicate="ALL candidate physical certificates";Require(physicalFailed==0,"Candidate incident certificate/geometry rejected; all rows persisted");
                    var oracleRows=new List<object>();
                    void Oracle(string set,int entity,NavLocation start,IEnumerable<NavLocation> points,NavLocation goal,IReadOnlyList<NavTypedLeg> expected,NavClearanceProfile clearance){
                        int cursor=0,pointIndex=0;var at=start;activeEntity=entity;
                        foreach(var target in points){
                            activePoint=pointIndex;activeLeg=cursor;activePredicate=set+" TryTraceOwned/reached";
                            IReadOnlyList<NavTypedLeg> trace=null;
                            try{
                                bool ok=provider.TryTraceOwned(at,target.Position,clearance,out var reached,out trace,r);
                                oracleRows.Add(new{Set=set,Entity=entity,PointIndex=pointIndex,From=at,Target=target,Reached=reached,Success=ok,Trace=trace?.Select(LegValue).ToArray()});
                                File.AppendAllText(Path.Combine(output,"normal-registration-oracle.jsonl"),JsonConvert.SerializeObject(oracleRows[oracleRows.Count-1])+"\n");
                                Require(ok&&reached.Equals(target),"Normal registration waypoint lost exact clearance/endpoint");
                                foreach(var part in trace){activeLeg=cursor;activePredicate=set+" exact emitted transcript";Require(cursor<expected.Count,"Normal transcript overflow");var e=expected[cursor++];Require(e.From.Equals(part.From)&&e.To.Equals(part.To)&&e.Kind==part.Kind&&e.PortalId==part.PortalId&&e.Cost==part.Cost,"Normal transcript changed");}
                                at=reached;pointIndex++;
                            }finally{(trace as NavOwnedStorage)?.ReleaseOwner();}
                        }
                        activePredicate=set+" exact transcript count/final endpoint";Require(cursor==expected.Count&&at.Equals(goal),"Normal transcript length/final endpoint mismatch");
                    }
                    for(int i=0;i<requests.Length;i++){var q=requests[i];var route=helper.WorkingRoutes[i];Oracle("member",q.Entity,q.StartLocation.Value,route.Waypoints.Select(w=>w.Location),q.GoalLocation.Value,route.Legs,new NavClearanceProfile("ground",1,"ground",0,q.Profile.Radius));}
                    Oracle("shared-spine",-1,spine.Node(0),spine.Stops.Skip(1).Select(spine.Node),spine.Node(spine.Stops.Last()),spine.Legs,new NavClearanceProfile("ground",1,"ground",0,requests[0].Profile.Radius));
                    File.WriteAllText(Path.Combine(output,"normal-registration-summary.json"),JsonConvert.SerializeObject(new{MemberRoutes=49,SharedSpine=1,WaypointSegments=oracleRows.Count,ExactFullTranscripts=true,DiagnosticWorkNotPreparationWork=true}));
                }
                var distances=new Dictionary<int,double>();string entry=(string)formation["EntryPortal"];
                for(int i=0;i<49;i++){var route=helper.WorkingRoutes[i];var q=requests[i];activeEntity=q.Entity;activeLeg=activePoint=-1;activePredicate="typed/endpoints/origin-prefix";Require(route!=null&&route.Legs[0].From.Equals(q.StartLocation.Value)&&route.Legs.Last().To.Equals(q.GoalLocation.Value),"Complete exact route endpoints differ");for(int j=1;j<route.Legs.Length;j++){activeLeg=j;Require(route.Legs[j-1].To.Equals(route.Legs[j].From),"Disconnected literal transcript");}
                    var old=prior["Helpers"][0]["FinalRoutes"][i];var oldLegs=Legs(old["Legs"]);int firstPortal=Array.FindIndex(route.Legs,l=>l.Kind==NavTypedLegKind.Portal&&l.PortalId==entry);int oldFirst=Array.FindIndex(oldLegs,l=>l.Kind==NavTypedLegKind.Portal&&l.PortalId==entry);Require(firstPortal==oldFirst&&firstPortal>=0,"Initial approach boundary changed");for(int j=0;j<=firstPortal;j++){activeLeg=j;Require(route.Legs[j].From.Equals(oldLegs[j].From)&&route.Legs[j].To.Equals(oldLegs[j].To)&&route.Legs[j].Kind==oldLegs[j].Kind&&route.Legs[j].PortalId==oldLegs[j].PortalId&&route.Legs[j].Cost==oldLegs[j].Cost,"Actual origin-to-entry transcript changed");}distances[q.Entity]=Approach(route.Legs,q.Start,entry);
                    var owned=new List<NavTypedLeg>();NavRecastMemberPlan plan=null;NavTypedRoute typed=null;try{
                        foreach(var leg in route.Legs){owned.Add(NavTypedLeg.CreateOwned(leg.From,leg.To,leg.Cost,leg.Kind,leg.PortalId,r,provider));if(leg.Kind==NavTypedLegKind.Surface){if(!evidenceOnly)Require(geometry.SegmentFree(leg.From.Position,leg.To.Position,q.Profile.Radius)&&provider.CanSweep(leg.From,leg.To,new NavClearanceProfile("ground",1,"ground",0,q.Profile.Radius)),"Literal physical surface leg rejected");validations++;}}
                        var oldTyped=owners[i].Corridor.TypedRoute;var oldPlan=oldTyped.RecastPlan;plan=NavRecastMemberPlan.Create(oldPlan.Spine,route.Join,route.Exit,route.OriginLegs,route.TerminalLeg,oldPlan.GraphBinding,oldPlan.ProfileBinding,r);typed=NavTypedRoute.CopySubscriptionAndCreateOwned(oldTyped.Subscription,owned,route.Legs.Sum(l=>l.Cost),r,plan);plan.Validate(typed);
                        activePredicate="full current typed/literal continuation";var points=owned.Where(l=>l.Kind==NavTypedLegKind.Surface).Select(l=>l.To.Position).ToArray();Require(NavRecastIntervals.TryContinuationRejoin(typed,NavRecastIntervals.Initial(typed),q.StartLocation.Value,Array.Empty<NavPoint>(),0,points,0,q.GoalLocation.Value,r,out _,literal,q.Entity),"Complete current typed/literal suffix rejected");
                    }finally{typed?.ReleaseStorage();plan?.Release();foreach(var leg in owned)leg.ReleaseStorage();}
                    rows.Add(new{q.Entity,route.Join,route.Exit,route.OriginLegs,route.TerminalLeg,CompleteCost=route.Legs.Sum(l=>l.Cost),PriorCompleteCost=oldLegs.Sum(l=>l.Cost),Legs=route.Legs.Select(LegValue).ToArray()});
                }
                foreach(var row in formation["Rows"]){int id=(int)row["Entity"];if(distances.ContainsKey(id))continue;var actor=source.Crowd.Units.Single(a=>a.Id==id);var o=(NavigationInstalledExecutionOwner)actor.StorageOwner.InstalledExecution;distances[id]=Approach(o.Corridor.TypedRoute.Legs,o.Request.Request.Start,entry);}
                var ranked=formation["Rows"].OrderBy(j=>distances[(int)j["Entity"]]).ThenBy(j=>(int)j["Slot"]).ToArray();for(int rank=0;rank<50;rank++)Require((int)ranked[rank]["EntryOrdinal"]==rank,"Actual initial origin-derived rank changed");Require(saved.Groups.Single(g=>g.GroupId==1).FormationPlan.Seal==(string)formation["Seal"],"Existing fixed seal changed");
                using var currentState=source.CaptureState();Require(bytes.SequenceEqual(WorldWire.Pack(w=>WorldWire.Write(w,currentState))),"Component mutated saved source authority");
                File.WriteAllText(Path.Combine(output,"result.json"),JsonConvert.SerializeObject(new{Result=helper.Work<1942806?"PASS":"FAIL",Meaning="ONE savedactual49 component; no World/newnativeLongquery. Originalnative33/null16 inputs, completedoldroutes ONLYvalidationreference. Newlocalforest objective does not promise complete/globalshortest.",Work=helper.Work,BaselineWork=1942806,All49CompleteTypedAndLiteral=true,SurfaceValidationOperations=validations,ActualInitialRanks50Unchanged=true,FixedSealAndSlotsUnchanged=true,ExactSavedSourceUnchanged=true,NegativeControlsReused=evidenceOnly,NegativeControlsSource=evidenceOnly?(sparseOnly?"component3 PASS unchanged negative paths; sparse selection follows successful bindings only":"component3 exactruntime261 PASS"):"this execution",Cancelled=evidenceOnly?(bool?)null:cancelled,CapacityGuard=evidenceOnly?(bool?)null:capacity,BirthRefusal=evidenceOnly?(bool?)null:refused,Rows=rows},Formatting.Indented));
                activeEntity=activeLeg=activePoint=-1;activePredicate="work reduction against recorded 1942806";Require(helper.Work<1942806,"Actual all-qualified donor roots did not reduce recorded preparation work; falsification, no retry");
            }
        }catch(Exception error){if(evidenceOnly)File.WriteAllText(Path.Combine(output,"exception-checkpoint.json"),JsonConvert.SerializeObject(new{Entity=activeEntity,Leg=activeLeg,Point=activePoint,Predicate=activePredicate,Work=helper?.Work,Exception=error.ToString()},Formatting.Indented));throw;}finally{helper?.Dispose();payload?.Dispose();provider?.StorageRoot.Release();if(source!=null){source.ReleaseCommittedHostRoot();source.DestroyNavigationAuthority();var b=source.PhysicalHostBudget;File.WriteAllText(Path.Combine(output,"cleanup.json"),JsonConvert.SerializeObject(new{b.PhysicalBytes,b.HeldBytes,b.ActiveScopes,NativeBefore=native,NativeAfter=RecastNative.sw_recast_allocation_count(),NativeFaults=RecastNative.ReleaseFaults-nativeFaults}));Check(b.PhysicalBytes==0&&b.HeldBytes==0&&b.ActiveScopes==0&&RecastNative.sw_recast_allocation_count()==native&&RecastNative.ReleaseFaults==nativeFaults,"Component ownership/native cleanup failed");}}
    }
}
