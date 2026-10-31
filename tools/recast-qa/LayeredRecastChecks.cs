using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
internal static class LayeredRecastChecks
{
    static void Require(bool value,string name){if(!value)throw new Exception(name);}
    internal static void Run(){
        Spacewars.Tests.PlayMode.RecastLayerInputs.Validate();
        var profile=NavigationProfile.Default.ForUnit(.58,NavigationProfile.Default.TankSpeed,NavigationProfile.Default.TankTurnSpeed);
        ulong baseline=RecastNative.sw_recast_allocation_count();
        foreach(var forward in new[]{true,false})foreach(var open in new[]{true,false}){
            var terrain=new StackedNavigationFixture(open,forward?NavPortalDirection.Forward:NavPortalDirection.Both);
            var source=RecastAuthorityGeometry.Prepare(terrain,new NavGeometry(8,Array.Empty<NavObstacle>(),1),profile);
            Console.WriteLine($"CASE forward={forward} open={open} radius={profile.Radius} cell={source.CellSize} links={source.LinkIds.Length} qualify={terrain.Provider.QualifyPortal("ramp",new NavClearanceProfile("ground",1,"ground",0,profile.Radius)).Status}");
            Require(source.Areas.Contains((byte)1)&&source.Areas.Contains((byte)2),"overlapping floor lost");
            Require(source.Point(new NavLocation(new NavPoint(-5,0),"lower"))[1]==0&&source.Point(new NavLocation(new NavPoint(-5,0),"upper"))[1]==4,"typed height");
            using var map=new RecastNavigationMap(source);
            if(open&&forward){foreach(var pair in new[]{(new NavLocation(new NavPoint(-5,0),"lower"),StackedNavigationFixture.Entry),(StackedNavigationFixture.Exit,new NavLocation(new NavPoint(5,0),"upper")),(StackedNavigationFixture.Entry,StackedNavigationFixture.Exit)}){using var diag=map.Query(pair.Item1,pair.Item2);while(diag.Status==RecastQueryStatus.Pending)diag.Step(64);Console.WriteLine($"DIAG {pair.Item1.SurfaceId}:{pair.Item1.Position.X}->{pair.Item2.SurfaceId}:{pair.Item2.Position.X} {diag.Status}");}}
            foreach(bool reverse in new[]{false,true}){
                var a=new NavLocation(new NavPoint(-5,0),"lower");var b=new NavLocation(new NavPoint(5,0),"upper");
                using var q=map.Query(reverse?b:a,reverse?a:b);while(q.Status==RecastQueryStatus.Pending)q.Step(64);
                bool ready=open&&(!reverse||!forward);Require(q.Status==(ready?RecastQueryStatus.Full:RecastQueryStatus.Unreachable),"directed/closed layered status "+q.Status);
                if(ready){var points=q.CopyTypedRoute();Require(points.Any(x=>x.PortalId=="ramp"),"portal provenance");var portal=points.Single(x=>x.PortalId=="ramp");Require(portal.Location.Equals(reverse?StackedNavigationFixture.Entry:StackedNavigationFixture.Exit),"canonical authored exit");}
            }
        }
        var physical=new StackedRampQualificationMap();var physicalGeometry=new NavGeometry(physical.HalfExtent,Array.Empty<NavObstacle>(),1);var input=RecastAuthorityGeometry.Prepare(physical,physicalGeometry,profile);
        var start=new NavLocation(new NavPoint(-12,0),"lower");var goal=Spacewars.Tests.PlayMode.RecastLayerInputs.Goal;
        using(var map=new RecastNavigationMap(input)){using var query=map.Query(start,goal);while(query.Status==RecastQueryStatus.Pending)query.Step(64);Require(query.Status==RecastQueryStatus.Full,"physical native corridor "+query.Status);var corridor=query.CopyTypedRoute();foreach(var point in corridor)Console.WriteLine($"PHYSICAL {point.Location.SurfaceId} ({point.Location.Position.X},{point.Location.Position.Z}) h={point.Height} portal={point.PortalId}");
            Require(corridor.Any(p=>p.PortalId=="ramp-entry")&&corridor.Any(p=>p.PortalId=="ramp-exit")&&corridor.All(p=>p.PortalId!="ramp"),"native edge was not physically expanded");
            var request=new Spacewars.Runtime.NavigationRequest(1,1,1,1,profile,physicalGeometry,start.Position,goal.Position,memberIdentity:new Spacewars.Runtime.NavigationMemberIdentity(1,0,1,1,1),startLocation:start,goalLocation:goal);
            var spine=Spacewars.Runtime.RecastGroupPreparation.BuildSpine(physical.Provider,new[]{request},corridor,input);Require(spine!=null,"no physical shared spine");var branch=Spacewars.Runtime.RecastGroupPreparation.Attach(physical.Provider,request,spine,input);Require(branch!=null,"no physical own branch");Require(branch.Legs.All(l=>l.Kind!=NavTypedLegKind.Portal||l.From.Position.Equals(l.To.Position)),"nonzero executed portal jump");Console.WriteLine($"PHYSICAL SPINE PASS legs={spine.Legs.Length}, ownbranch={branch.Legs.Length}");
        }
        using(var map=new RecastNavigationMap(input))foreach(var pair in new[]{(new NavLocation(new NavPoint(0,0),"ramp-support"),goal),(start,new NavLocation(new NavPoint(1,0),"ramp-support")),(new NavLocation(new NavPoint(-1,0),"ramp-support"),new NavLocation(new NavPoint(1,0),"ramp-support"))}){
            using var q=map.Query(pair.Item1,pair.Item2);while(q.Status==RecastQueryStatus.Pending)q.Step(64);Require(q.Status==RecastQueryStatus.Full,"typed ramp endpoint query");var points=q.CopyTypedRoute();var request=new Spacewars.Runtime.NavigationRequest(1,9,9,1,profile,physicalGeometry,pair.Item1.Position,pair.Item2.Position,memberIdentity:new Spacewars.Runtime.NavigationMemberIdentity(9,0,9,1,1),startLocation:pair.Item1,goalLocation:pair.Item2);var spine=Spacewars.Runtime.RecastGroupPreparation.BuildSpine(physical.Provider,new[]{request},points,input);Require(spine!=null&&Spacewars.Runtime.RecastGroupPreparation.Attach(physical.Provider,request,spine,input)!=null,"typed ramp actual radius attachment");if(pair.Item1.SurfaceId=="ramp-support"&&pair.Item2.SurfaceId=="ramp-support")Require(points.Length==2&&points.All(p=>p.PortalId==null),"same ramp direct sweep took a needless loop");
        }
        Console.WriteLine("RAMP ENDPOINTS PASS ramp-upper/lower-ramp/same-ramp direct, source-derived anchor attachments qualified by actual radius");
        PaidMemoryChecks.Run(input,start,goal);
        var obstacle=new NavObstacle(-.2,-.2,.2,.2);var overlay=new NavGeometry(physical.HalfExtent,new[]{obstacle},2);var visible=physical.ForGeometry(overlay,null);var held=physical.ForGeometry(overlay,"held-v1");var clearance=new NavClearanceProfile("ground",1,"ground",0,.58);
        Require(visible.Binding!=physical.Provider.Binding&&held.Binding!=visible.Binding,"overlay/HOLD binding lost");Require(!held.IsValid(new NavLocation(new NavPoint(0,0),"lower"),clearance)&&!held.IsValid(new NavLocation(new NavPoint(0,0),"upper"),clearance),"global overlay disappeared on a layer");Require(held.QualifyPortal("ramp",clearance).Status==NavConnectorStatus.Blocked,"interior connector overlay disappeared");
        Require(physical.Provider.IsValid(new NavLocation(new NavPoint(4.5,0),"lower"),clearance)&&!physical.Provider.IsValid(new NavLocation(new NavPoint(4.5,0),"upper"),clearance),"per-layer blocker flattened");
        Require(RecastAuthorityGeometry.Prepare(physical,overlay,profile).LinkIds.Length==0,"blocked public connector retained");
        Require(physical.Provider.QualifyPortal("ramp",new NavClearanceProfile("large",1,"ground",0,.8)).Status==NavConnectorStatus.Blocked&&physical.Provider.QualifyPortal("ramp",new NavClearanceProfile("air",1,"air",0,.1)).Status==NavConnectorStatus.Blocked,"clearance/mobility qualifier");
        Require(physical.ForGeometry(new NavGeometry(physical.HalfExtent,Array.Empty<NavObstacle>(),2),null).Binding!=physical.ForGeometry(new NavGeometry(physical.HalfExtent,Array.Empty<NavObstacle>(),1),null).Binding,"empty geometry revision disappeared");
        var endpoint=physical.ForGeometry(new NavGeometry(physical.HalfExtent,new[]{new NavObstacle(-2.2,-.2,-1.8,.2)},3),"held-endpoint");Require(endpoint.QualifyPortal("ramp-entry",clearance).Status==NavConnectorStatus.Blocked&&endpoint.QualifyPortal("ramp",clearance).Status==NavConnectorStatus.Blocked,"endpoint overlay was not blocked");
        Require(physical.TryTraverse(new NavLocation(new NavPoint(-4,10),"lower"),new NavPoint(0,10),.58,out var sameLower)&&sameLower.SurfaceId=="lower","independent lower sweep was blocked");Require(physical.TryTraverse(new NavLocation(new NavPoint(4,5),"upper"),new NavPoint(0,5),.58,out var sameUpper)&&sameUpper.SurfaceId=="upper","independent upper sweep was blocked");
        Console.WriteLine("OVERLAY/HOLD/per-layer blocker/clearance/mobility/emptyrevision/endpointblocker/independent sweeps PASS");
        Require(RecastNative.sw_recast_allocation_count()==baseline,"layered native leak");
        var repeated=new SourcePieces(63,true);var geometry=new NavGeometry(8,Array.Empty<NavObstacle>(),1);
        var repeatedSource=RecastAuthorityGeometry.Prepare(repeated,geometry,profile);Require(repeatedSource.Point(new NavLocation(new NavPoint(6,0),"s0"))[1]==0,"repeated semantic support selector");
        try{RecastAuthorityGeometry.Prepare(new SourcePieces(64,false),geometry,profile);throw new Exception("64 semantic surfaces accepted");}catch(InvalidOperationException e){Require(e.Message.Contains("semantic area capacity"),"wrong capacity failure");}
        Console.WriteLine("LAYERED PASS independent overlapping floors, authored portal provenance, forward/reverse/closed, narrow source-valid .58 link, repeated semantic pieces, distinct63/64 capacity, native lifetime0");
    }
    internal static void StartupDiagnostic(){var p=PlayableProfile.RecastLayerQualification;var map=(StackedRampQualificationMap)p.AuthoredMap;foreach(double radius in new[]{p.Navigation.Radius,p.ExplorerCollisionRadius,p.ShkvalCollisionRadius}){var profile=p.Navigation.ForUnit(radius,p.Navigation.TankSpeed,p.Navigation.TankTurnSpeed);var source=RecastAuthorityGeometry.Prepare(map,new NavGeometry(map.HalfExtent,Array.Empty<NavObstacle>(),1),profile);try{using var native=new RecastNavigationMap(source);Console.WriteLine($"PUBLIC radius={radius} links={source.LinkIds.Length} native=PASS");}catch(Exception e){Console.WriteLine($"PUBLIC radius={radius} links={source.LinkIds.Length} native=FAIL {e.Message}");}}}
    internal static void Session(){
        Spacewars.Tests.PlayMode.RecastLayerInputs.Validate();var p=PlayableProfile.RecastLayerQualification;var map=(StackedRampQualificationMap)p.AuthoredMap;var geometry=new NavGeometry(map.HalfExtent,Array.Empty<NavObstacle>(),1);
        var session=new Spacewars.Runtime.NavigationSession(803,geometry,p.Navigation,null,map){UsesRecastGroundRoutes=true};Spacewars.Runtime.NavigationSession restored=null;var kernel=new Spacewars.Runtime.CertifiedRouteKernel();ulong baseline=RecastNative.sw_recast_allocation_count();
        try{
            session.RouteFailed+=(request,outcome)=>Console.WriteLine($"ROUTEFAIL entity={request.Entity} outcome={outcome}");
            session.ConfigureMarch(p.GroupMarchMaximumStretch,p.Revision);session.Crowd.Add(1,new NavPoint(-12,0),p.ExplorerCollisionRadius,p.Navigation.TankSpeed,p.Navigation.TankTurnSpeed,canYield:true);session.Crowd.Add(2,new NavPoint(-15,0),p.ExplorerCollisionRadius,p.Navigation.TankSpeed,p.Navigation.TankTurnSpeed,canYield:true);
            var goal=Spacewars.Tests.PlayMode.RecastLayerInputs.Goal;var cmd=new PlayableCommand(803,1,"player-1",PlayableCommandKind.Move,new[]{1,2},goal.Position).WithResolvedLocation(goal);
            session.PrepareCommandGroup(cmd,1,0,1);var slots=session.AllocateArrivalSlots(goal.Position,new[]{1,2});Require(slots.Length==2&&slots.All(x=>map.IsValidLocation(new NavLocation(x,goal.SurfaceId),p.ExplorerCollisionRadius)),"typed own slots invalid");bool applied=session.MoveGroup(new[]{1,2},slots);session.FinishCommandGroup(applied,new[]{1,2});Require(applied,"typed group admission refused");
            var binding=new Spacewars.Runtime.PlayableRouteBinding(geometry,p,session.Admission,session.RoutePort);
            var seen=new HashSet<long>();for(int i=0;i<400&&session.AppliedResults<2;i++){
                kernel.Pump(binding,8);
                if(session.Answers.TryPeek(out var answer)&&seen.Add(answer.Request.Request)){var actor=session.Crowd.Units.Single(u=>u.Id==answer.Request.Entity);var typed=answer.StorageOwner?.CorridorOwner?.TypedRoute;Console.WriteLine($"HEAD request={answer.Request.Request} status={answer.Status} physical={(typed==null?false:session.Crowd.ValidatePhysicalTypedRoute(actor,actor.Location,answer.Request.Goal,typed,answer.Request.GoalLocation))}");if(typed!=null)foreach(var leg in typed.Legs)if(leg.Kind==NavTypedLegKind.Surface){bool sweep=map.TryTraverse(leg.From,leg.To,actor.Radius,out var end);Console.WriteLine($"LEG {leg.From.SurfaceId}:{leg.From.Position.X},{leg.From.Position.Z}->{leg.To.SurfaceId}:{leg.To.Position.X},{leg.To.Position.Z} valid={map.IsValidLocation(leg.From,actor.Radius)}/{map.IsValidLocation(leg.To,actor.Radius)} sweep={sweep} same={end.Equals(leg.To)}");}}
                session.ApplyResults();
            }
            Console.WriteLine($"SESSION applied={session.AppliedResults} requests={session.Requests.Count} queries={kernel.RecastLongQueries} attachments={kernel.RecastAttachments} corridor={session.LastCorridorFailure} result={session.LastResultBatchFailure}");foreach(var actor in session.Crowd.Units)Console.WriteLine($"ACTOR id={actor.Id} outcome={actor.Outcome} failure={actor.LastFailure}");Require(session.AppliedResults==2,"paid layered host did not install ready decisions");
            foreach(int id in new[]{1,2}){var plan=session.InstalledExecutionFor(id)?.Corridor?.RecastPlan;Require(plan!=null,"missing owned typed spine");}
            kernel.Clear();
            byte[] bytes;using(var capture=session.CaptureState())bytes=Spacewars.Runtime.WorldWire.Pack(w=>Spacewars.Runtime.WorldWire.Write(w,capture));
            using(var saved=Spacewars.Runtime.WorldWire.Unpack(bytes,Spacewars.Runtime.WorldWire.ReadNavigationSessionState)){
                restored=new Spacewars.Runtime.NavigationSession(803,geometry,p.Navigation,null,map){UsesRecastGroundRoutes=true};restored.ConfigureMarch(p.GroupMarchMaximumStretch,p.Revision);restored.RestoreState(saved);
            }
            using(var again=restored.CaptureState())Require(bytes.SequenceEqual(Spacewars.Runtime.WorldWire.Pack(w=>Spacewars.Runtime.WorldWire.Write(w,again))),"typed ready current codec differs");
            foreach(int id in new[]{1,2})Require(session.Crowd.Units.Single(u=>u.Id==id).Location.Equals(restored.Crowd.Units.Single(u=>u.Id==id).Location),"preparation moved a typed actor");
            session.Step(1d/30);restored.Step(1d/30);foreach(int id in new[]{1,2}){Require(session.Crowd.Units.Single(u=>u.Id==id).Position.Equals(restored.Crowd.Units.Single(u=>u.Id==id).Position),"first physical step differs after restore");Require(session.GroupFor(id).Members.Single(m=>m.Entity==id).Recast.Equals(restored.GroupFor(id).Members.Single(m=>m.Entity==id).Recast),"first accepted interval progress differs after restore");}
            Console.WriteLine("SESSION PASS paid projection, all own slots/ready typed decisions, exact current codec/preparation pose, one identical affected physical step; synchronous component only, actual threaded witness NotRun");
        }finally{kernel.Clear();restored?.ReleaseCommittedHostRoot();restored?.DestroyNavigationAuthority();session.ReleaseCommittedHostRoot();session.DestroyNavigationAuthority();}
        Require(RecastNative.sw_recast_allocation_count()==baseline,"component native pin leak");
    }
    sealed class SourcePieces:IPlayableTerrain{
        readonly StackedNavigationFixture fallback=new StackedNavigationFixture();
        public SourcePieces(int count,bool repeated){var values=new List<MapSupport>();for(int i=0;i<count;i++)values.Add(new MapSupport("floor",new NavObstacle(-8,-8,0,8),false,i*4,surfaceId:"s"+i));if(repeated)values.Add(new MapSupport("floor",new NavObstacle(0,-8,8,8),false,surfaceId:"s0"));Supports=values.AsReadOnly();}
        public IReadOnlyList<MapSupport> Supports{get;}public string Id=>"bounded-recast-area-data";public int Revision=>1;public int SurfaceSemanticsVersion=>1;public double HalfExtent=>8;public double DirectFireHeight=>1;public IReadOnlyList<NavSurfaceTransition> SurfaceTransitions=>Array.Empty<NavSurfaceTransition>();public IReadOnlyList<NavObstacle> Solids=>Array.Empty<NavObstacle>();public IReadOnlyList<NavObstacle> MovementBlockers=>Solids;
        public MapSupport SupportAt(NavPoint p)=>throw new NotSupportedException();public double SurfaceHeight(NavPoint p)=>throw new NotSupportedException();public NavPoint SurfaceGradient(NavPoint p)=>throw new NotSupportedException();public bool SupportsFootprint(NavPoint p,double r)=>false;public bool SupportsSweep(NavPoint a,NavPoint b,double r)=>false;public bool TryLocate(NavPoint p,double r,out NavLocation l){l=default;return false;}public bool IsValidLocation(NavLocation l,double r)=>false;public bool TryTraverse(NavLocation a,NavPoint b,double r,out NavLocation l){l=default;return false;}public bool CompatibleCombatSurface(NavLocation a,NavLocation b)=>false;public TerritorySite[] Sites(PlayableProfile p)=>Array.Empty<TerritorySite>();public NavPoint Headquarters(PlayableOwner o)=>throw new NotSupportedException();
    }
}
