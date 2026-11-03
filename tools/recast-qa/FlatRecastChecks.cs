using System;
using System.Linq;
using System.Threading;
using Spacewars.Runtime;
using Spacewars.Simulation;

internal static class FlatRecastChecks
{
    internal static void InspectSavedWorld(){
        var input=Environment.GetEnvironmentVariable("NATIVE_DEFAULT_WORLD_INPUT")??throw new ArgumentException("Saved world required");var output=Environment.GetEnvironmentVariable("NATIVE_DEFAULT_WORLD_OUTPUT")??throw new ArgumentException("Unique output required");Require(!System.IO.Directory.Exists(output),"Output exists");System.IO.Directory.CreateDirectory(output);
        var bytes=System.IO.File.ReadAllBytes(input);using var world=PlayableWorldState.Decode(bytes);using var saved=WorldWire.Unpack(world.Navigation,WorldWire.ReadNavigationSessionState);
        var budget=new NavigationRetentionBudget();NavigationHostRoot host=null;NavigationAuthorityOwners authority=null;NavigationTrafficAuthority traffic=null;NavPagedList<long> bank=null;NavigationMotionSources motion=null;ulong native=RecastNative.sw_recast_allocation_count();
        try{
            var method=typeof(NavigationSession).GetMethods(System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic).Single(m=>m.Name=="DecodeDetachedHostAuthority"&&m.GetParameters().Length==9);object[] args={saved.HostAuthority,budget,null,null,null,0L,null,null,null};method.Invoke(null,args);host=(NavigationHostRoot)args[3];authority=(NavigationAuthorityOwners)args[4];traffic=(NavigationTrafficAuthority)args[6];bank=(NavPagedList<long>)args[7];motion=(NavigationMotionSources)args[8];
            var requests=authority.RequestCatalog.OfType<NavigationHostRequest>().Select(x=>x.Request).ToArray();
            var rows=requests.Select(q=>new{q.Session,q.Request,q.Entity,q.Order,q.StartLocation,q.GoalLocation,q.HeldIdentity,q.MemberIdentity,Profile=new{q.Profile.ProfileId,q.Profile.Revision,Values=q.Profile.CaptureWireValues()},Geometry=new{q.Geometry.Revision,q.Geometry.HalfExtent,Obstacles=q.Geometry.Obstacles.ToArray()},BaseGeometry=new{q.BaseGeometry.Revision,q.BaseGeometry.HalfExtent,Obstacles=q.BaseGeometry.Obstacles.ToArray()}}).ToArray();
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(output,"navigation-current.bin"),world.Navigation);
            System.IO.File.WriteAllText(System.IO.Path.Combine(output,"readback.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{Meaning="Readonly owning physical current graph decode; no World/Step/query/pose changes; failure13 itself was not captured",WorldTick=world.Tick,saved.Generation,Requests=rows,Request1Retained=requests.Any(q=>q.Request==1&&q.Entity==3),Actors=saved.Crowd.Units,Groups=saved.Groups,saved.AnswerMailbox,saved.BarrierAnswers,saved.ProbeAnswers,saved.PendingRequestIndices,saved.TechnicalFailures,saved.AppliedResults,saved.RejectedResults},Newtonsoft.Json.Formatting.Indented));
            Console.WriteLine("COLD saved graph requests="+requests.Length+" humanRequest1="+requests.Any(q=>q.Request==1&&q.Entity==3));
        }finally{motion?.Release();bank?.Release();traffic?.Release();host?.Release();authority?.Release();System.IO.File.WriteAllText(System.IO.Path.Combine(output,"cleanup.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{budget.PhysicalBytes,budget.HeldBytes,budget.ActiveScopes,NativeBefore=native,NativeAfter=RecastNative.sw_recast_allocation_count()}));Require(budget.PhysicalBytes==0&&budget.HeldBytes==0&&budget.ActiveScopes==0&&native==RecastNative.sw_recast_allocation_count(),"Cold world graph ownership mismatch");}
    }
    internal static void CapturedAuthoredRequest()
    {
        var path=Environment.GetEnvironmentVariable("NATIVE_DEFAULT_REQUEST_INPUT")??throw new ArgumentException("Owning failed request required");var output=Environment.GetEnvironmentVariable("NATIVE_DEFAULT_REQUEST_OUTPUT")??throw new ArgumentException("Unique output required");Require(!System.IO.Directory.Exists(output),"Preserve prior replay");System.IO.Directory.CreateDirectory(output);
        var q=Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(path));
        NavPoint Point(Newtonsoft.Json.Linq.JToken p)=>new NavPoint((double)p["X"],(double)p["Z"]);
        NavLocation Location(Newtonsoft.Json.Linq.JToken p)=>new NavLocation(Point(p["Position"]),(string)p["SurfaceId"]);
        NavObstacle Obstacle(Newtonsoft.Json.Linq.JToken p){
            if(p["Polygon"].Type!=Newtonsoft.Json.Linq.JTokenType.Null)return new NavObstacle(new NavPolygon(p["Polygon"]["Vertices"].Select(Point)),(double)p["Bottom"],(double)p["Top"]);
            double radius=(double)p["CircleRadius"];return radius>0?NavObstacle.RestoreCircle((double)p["MinX"],(double)p["MinZ"],(double)p["MaxX"],(double)p["MaxZ"],radius):new NavObstacle((double)p["MinX"],(double)p["MinZ"],(double)p["MaxX"],(double)p["MaxZ"]);
        }
        NavGeometry Geometry(Newtonsoft.Json.Linq.JToken g)=>new NavGeometry((double)g["HalfExtent"],g["Obstacles"].Select(Obstacle).ToArray(),(int)g["Revision"]);
        var geometry=Geometry(q["Geometry"]);var baseGeometry=Geometry(q["BaseGeometry"]);var profile=NavigationProfile.FromWireValues((string)q["Profile"]["ProfileId"],(int)q["Profile"]["Revision"],q["Profile"]["Values"].Select(v=>(double)v).ToArray());
        var member=q["MemberIdentity"];var start=Location(q["StartLocation"]);var goal=Location(q["GoalLocation"]);
        var request=new NavigationRequest((long)q["Session"],(long)q["Request"],(int)q["Entity"],(long)q["Order"],profile,geometry,start.Position,goal.Position,baseGeometry,(string)q["HeldIdentity"],new NavigationMemberIdentity((long)member["Incarnation"],(long)member["MobilityRevision"],(long)member["GroupId"],(long)member["OrderRevision"],(long)member["MembershipRevision"]),start,goal);
        IPlayableTerrain terrain; if(Environment.GetEnvironmentVariable("NATIVE_DEFAULT_REQUEST_MAP")=="BasaltCrown")terrain=new BasaltCrownMap();else terrain=new ThreeCrossingsMap(Newtonsoft.Json.JsonConvert.DeserializeObject<ThreeCrossingsProfileData>(System.IO.File.ReadAllText("unity/Assets/Spacewars/Content/Resources/ThreeCrossingsProfile.json")));var provider=new PartitionNavigationProvider(terrain,geometry,request.HeldIdentity);var clearance=new NavClearanceProfile("ground",1,"ground",0,profile.Radius);
        ulong before=RecastNative.sw_recast_allocation_count();
        try{var input=RecastAuthorityGeometry.Prepare(terrain,geometry,profile);using var map=new RecastNavigationMap(input);using var query=map.Query(start,goal);int calls=0;while(query.Status==RecastQueryStatus.Pending&&calls++<4096)query.Step(256);
            var corridor=query.Status==RecastQueryStatus.Full?query.CopyTypedRoute():Array.Empty<RecastRoutePoint>();
            if(Environment.GetEnvironmentVariable("NATIVE_DEFAULT_PAID_SPINE")=="1")corridor=PaidSpine(provider,request,input,corridor,output);
            var spine=RecastGroupPreparation.BuildSpine(provider,new[]{request},corridor,input);var route=spine==null?null:RecastGroupPreparation.Attach(provider,request,spine,input);
            var trace=new System.Collections.Generic.List<object>();var last=start;foreach(var point in corridor){bool physical=geometry.SegmentFree(last.Position,point.Location.Position,profile.Radius);bool accepted=provider.TryTrace(last,point.Location.Position,clearance,out var reached,out var legs)&&reached.Equals(point.Location);double? height=null;string heightFailure=null;try{height=input.Point(point.Location)[1];}catch(ArgumentException e){heightFailure=e.Message;}bool heightOk=height.HasValue&&Math.Abs(height.Value-point.Height)<=2*input.CellHeight+1e-6;trace.Add(new{From=last,Target=point.Location,physical,accepted,heightOk,Height=height,heightFailure,NativeHeight=point.Height,Reached=reached});if(physical&&accepted&&heightOk)last=point.Location;}
            System.IO.File.WriteAllText(System.IO.Path.Combine(output,"replay.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{Meaning="Exact owning failure request/profile/geometry; unchanged current map resource; standalone Native and production BuildSpine/Attach; not host/gameplay acceptance",InputSha=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.IO.File.ReadAllBytes(path))).ToLowerInvariant(),Map=terrain.Id,terrain.Revision,provider.Binding,NativeStatus=query.Status.ToString(),query.Iterations,Corridor=corridor,Trace=trace,LastAccepted=last,SpineReady=spine!=null,BranchReady=route!=null,Spine=spine,Branch=route},Newtonsoft.Json.Formatting.Indented));Console.WriteLine($"CAPTURED authored q{request.Request}/entity{request.Entity} native={query.Status} corners={corridor.Length} spine={spine!=null} branch={route!=null} last={last.SurfaceId}:{last.Position.X},{last.Position.Z}");
        }finally{Require(RecastNative.sw_recast_allocation_count()==before&&RecastNative.ReleaseFaults==0,"Captured request native lifetime leak");}
    }
    private static RecastRoutePoint[] PaidSpine(PartitionNavigationProvider provider,NavigationRequest request,RecastAuthorityGeometry geometry,RecastRoutePoint[] raw,string output)
    {
        var budget=new NavigationRetentionBudget();Require(budget.TryOpenScope(NavigationPreparationKind.Admission,request.Session,0,out var scope),"paid spine scope refused");RecastPayload source=null;RecastSpinePreparation preparation=null;PartitionNavigationProvider owned=null;
        try{var r=scope.OwnedReservation();owned=PartitionNavigationProvider.CopyOwned(provider,r);Require(owned.SupportLawCount==provider.SupportLawCount,"owned support law count changed");for(int i=0;i<owned.SupportLawCount;i++){var a=provider.SupportLawAt(i);var b=owned.SupportLawAt(i);Require(a.Id==b.Id&&a.SurfaceId==b.SurfaceId&&a.Height==b.Height&&a.Gradient.Equals(b.Gradient)&&a.Origin.Equals(b.Origin)&&a.IsBridge==b.IsBridge&&a.Bounds.GeometryEquals(b.Bounds),"owned exact support law changed");}source=RecastPayload.Allocate(r,checked(32L+raw.Length*NavStorageLayout.ValueBytes(typeof(RecastRoutePoint))));source.Value=raw;preparation=RecastSpinePreparation.Create(source,r);int calls=0,max=0;
            while(preparation.Status==RecastSpinePreparation.Result.Pending&&calls++<100000){long before=preparation.Work;preparation.Step(owned,request,geometry,r,256);int used=checked((int)(preparation.Work-before));Require(used<=256,"paid spine exceeded quantum");max=Math.Max(max,used);}
            var done=preparation.Completed?.Value as RecastRoutePoint[];var copy=done?.ToArray()??Array.Empty<RecastRoutePoint>();System.IO.File.WriteAllText(System.IO.Path.Combine(output,"paid-spine.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{Source="S2 a72544f87 unchanged RecastSpinePreparation+RecastLocalAttachment only; no field/formation/host changes",Status=preparation.Status.ToString(),preparation.Work,preparation.HelperWork,Calls=calls,MaximumQuantum=max,Points=copy},Newtonsoft.Json.Formatting.Indented));Console.WriteLine($"PAID spine {preparation.Status} work={preparation.Work} helper={preparation.HelperWork} calls={calls} max={max} points={copy.Length}");return copy;
        }finally{preparation?.Dispose();source?.Dispose();((INavStorageFacade)owned)?.StorageRoot.Release();scope.Discard();Require(budget.PhysicalBytes==0&&budget.HeldBytes==0&&budget.ActiveScopes==0,"paid spine ownership leak");}
    }
    private static void Require(bool value,string message){if(!value)throw new Exception(message);}
    private static void Close(NavigationSession session,CertifiedRouteWorker worker)
    {
        worker?.Dispose();if(worker!=null)Require(SpinWait.SpinUntil(()=>worker.IsStopped,5000),"flat worker stop ACK");
        session.ReleaseCommittedHostRoot();session.DestroyNavigationAuthority();
        Require(session.PhysicalHostBudget.PhysicalBytes==0&&session.PhysicalHostBudget.HeldBytes==0&&session.PhysicalHostBudget.ActiveScopes==0,"flat physical owner leak");
    }
    internal static PlayableProfile Profile()
    {var data=PlayableProfile.Default.CopyData();data.groundRouteBackend=1;return PlayableProfile.Create(data);}
    internal static void Run(bool publication=false,PlayableCommandKind kind=PlayableCommandKind.Move,bool combatContact=false)
    {
        var profile=Profile();Require(profile.AuthoredMap==null,"flat test substituted an authored map");
        var geometry=new NavGeometry(profile.ArenaHalfExtent,new[]{new NavObstacle(-1,-5,1,5)},1);
        ulong before=RecastNative.sw_recast_allocation_count();
        var session=new NavigationSession(901,geometry,profile.Navigation){UsesRecastGroundRoutes=true};
        session.ConfigureMarch(profile.GroupMarchMaximumStretch,profile.Revision);
        CertifiedRouteWorker worker=null;NavigationSession restored=null;byte[] image=null;MotionPublicationChecks audit=null;
        try{
            session.Crowd.Add(1,new NavPoint(-12,-2),profile.TankCollisionRadius,profile.Navigation.TankSpeed,profile.Navigation.TankTurnSpeed,canYield:true);
            session.Crowd.Add(2,new NavPoint(-12,2),profile.TankCollisionRadius,profile.Navigation.TankSpeed,profile.Navigation.TankTurnSpeed,canYield:true);
            if(publication){session.Crowd.Add(3,new NavPoint(-30,30),profile.TankCollisionRadius,profile.Navigation.TankSpeed,profile.Navigation.TankTurnSpeed,canYield:true);audit=new MotionPublicationChecks(session);}
            session.RouteFailed+=(request,outcome)=>Console.WriteLine("FLAT route failure entity="+request.Entity+" outcome="+outcome);
            var command=new PlayableCommand(901,1,"player-1",kind,new[]{1,2},new NavPoint(12,0)).WithResolvedLocation(new NavLocation(new NavPoint(12,0),NavLocation.FlatSurface));
            session.PrepareCommandGroup(command,1,0,1);
            bool moved=session.MoveGroup(new[]{1,2},new[]{new NavPoint(12,-2),new NavPoint(12,2)});session.FinishCommandGroup(moved,new[]{1,2});
            Require(moved,"flat real group command refused");
            if(combatContact)foreach(int id in new[]{1,2})session.SetFormationContact(id,true,0);
            worker=new CertifiedRouteWorker(new PlayableRouteBinding(geometry,profile,session.Admission,session.RoutePort),8);
            bool settled=SpinWait.SpinUntil(()=>{if(publication)session.Step(1d/30);else session.ApplyResults();return session.AppliedResults==2||session.PendingCount==0||worker.Publication.Failure!=null;},15000);
            Console.WriteLine("FLAT source result applied="+session.AppliedResults+" pending="+session.PendingCount+" rejected="+session.RejectedResults+" host="+Newtonsoft.Json.JsonConvert.SerializeObject(worker.Publication)+" corridor="+session.LastCorridorFailure+" result="+session.LastResultBatchFailure);
            if(!settled)Console.WriteLine("MOTION timeout prepared="+audit?.Prepared+" commits="+audit?.Committed+" physical="+session.PhysicalHostBudget.PhysicalBytes+" held="+session.PhysicalHostBudget.HeldBytes+" scopes="+session.PhysicalHostBudget.ActiveScopes+" movement="+session.LastOwnedMovementRefusalMessage+" path="+session.LastNavigationStepPath);
            Require(settled,"flat threaded route timeout");
            Require(worker.Publication.Failure==null,worker.Publication.Failure);
            Require(session.AppliedResults==2,"flat typed routes missing");
            Require(worker.Publication.RecastFoundationBuilds==1&&worker.Publication.RecastLongQueries==1,"flat foundation/subgroup reuse failed");
            Require(session.Admission.AuthoredTerrain==null&&session.Admission.SurfaceProviderId==NavLocation.FlatSurface,"flat admission binding changed");
            if(audit!=null){var old=session.Crowd.Units.Select(a=>a.CopyMovementHeader()).ToArray();audit.RefuseNext=true;session.Step(1d/30);Require(audit.Refused==1,"refusal not exercised");for(int i=0;i<old.Length;i++)Require(old[i].SameMovementValues(session.Crowd.Units[i]),"refused preadmission changed authority");}
            var origins=session.Crowd.Units.Select(a=>a.Position).ToArray();
            for(int tick=0;tick<90;tick++){session.ApplyResults();session.Step(1d/30);}
            Require(session.Crowd.Units.Where((a,i)=>!a.Position.Equals(origins[i])).Count()==2,"flat accepted routes did not move");
            Require(session.Crowd.Units.All(a=>a.Location.SurfaceId==NavLocation.FlatSurface&&geometry.IsFree(a.Position,a.Radius)),"flat support/footprint changed");
            if(combatContact)Require(session.GroupFor(1).Members.All(m=>m.FormationReleased),"physical cursor binding reacquired formation pacing");
            if(audit!=null){Require(audit.Ready>0&&audit.Committed>0&&audit.Composed>0&&audit.FirstTurningReady==2&&audit.FirstTranslatingReady==2,"owning source/first turning and translating receivers not exercised");Console.WriteLine("MOTION actual transcript/full suffix/atomic publication/refusal/reentry PASS kind="+kind+" ready="+audit.Ready+" committed="+audit.Committed+" composed="+audit.Composed+" first-turn="+audit.FirstTurningReady+" first-translation="+audit.FirstTranslatingReady);audit.VerifyTranscriptCodec();audit.VerifySessionSources(profile,geometry);audit.Dispose();}
            using(var state=session.CaptureState())image=WorldWire.Pack(w=>WorldWire.Write(w,state));
            restored=new NavigationSession(901,geometry,profile.Navigation){UsesRecastGroundRoutes=true};
            restored.ConfigureMarch(profile.GroupMarchMaximumStretch,profile.Revision);
            using(var state=WorldWire.Unpack(image,WorldWire.ReadNavigationSessionState))restored.RestoreState(state);
            using(var state=restored.CaptureState())Require(image.SequenceEqual(WorldWire.Pack(w=>WorldWire.Write(w,state))),"flat current restore changed authoritative bytes");
            foreach(var actor in session.Crowd.Units){var copy=restored.Crowd.Units.Single(a=>a.Id==actor.Id);Require(actor.Position.Equals(copy.Position)&&actor.Location.Equals(copy.Location)&&actor.Heading==copy.Heading&&actor.InstalledRequest==copy.InstalledRequest,"flat current actor identity/pose changed");}
            Console.WriteLine("FLAT actual null AuthoredMap / real threaded group Ready / movement around blocker / current exact restore PASS bytes="+image.Length+" foundation="+worker.Publication.RecastFoundationBuilds+" queries="+worker.Publication.RecastLongQueries);
        }catch(Exception error){Console.WriteLine("FLAT primary failure: "+error);throw;}finally{audit?.Dispose();if(restored!=null)Close(restored,null);Close(session,worker);}
        Require(RecastNative.sw_recast_allocation_count()==before&&RecastNative.ReleaseFaults==0,"flat native lifetime leak/fault");
        Console.WriteLine("FLAT stop ACK / physical scopes and native allocations returned to baseline PASS");
    }
}
