using System;
using System.Diagnostics;
using System.Linq;
using Spacewars.Simulation;
using Spacewars.Runtime;
class Program{
static int Main(string[] args){try{return Run(args);}catch(Exception error){Console.Error.WriteLine(error);return 1;}}
static int Run(string[] args){
if(args.Contains("--air-motion")){AirMotionChecks.Run();return 0;}
if(args.Contains("--normal-request-captured")){FlatRecastChecks.CapturedAuthoredRequest();return 0;}
if(args.Contains("--normal-world-cold-inspect")){FlatRecastChecks.InspectSavedWorld();return 0;}
if(args.Contains("--motion-publication")){FlatRecastChecks.Run(true);FlatRecastChecks.Run(true,PlayableCommandKind.Attack);FlatRecastChecks.Run(true,PlayableCommandKind.Attack,true);return 0;}
if(args.Contains("--flat-recast")){FlatRecastChecks.Run();return 0;}
if(args.Contains("--binding-world")){S1ClosureChecks.BindingWorld();return 0;}
if(args.Contains("--binding-audit")){S1ClosureChecks.BindingAudit();return 0;}
if(args.Contains("--s1-closure")){S1ClosureChecks.Run();return 0;}
if(args.Contains("--s1-c")){S1ClosureChecks.Capacity();return 0;}
if(args.Contains("--s1-a")){S1ClosureChecks.MovingPeer();return 0;}
if(args.Contains("--s1-b")){S1ClosureChecks.Passage();return 0;}
if(args.Contains("--s1-d")){S1ClosureChecks.Queue();return 0;}
if(args.Contains("--s1-metrics")){S1ClosureChecks.Metrics();return 0;}
if(args.Contains("--guard-actual-save")){LiveGuardChecks.ActualSavedNavigation();return 0;}
if(args.Contains("--guard-exit-trace")){LiveGuardChecks.HeldRemainingIntent(true,false,true);return 0;}
if(args.Contains("--guard-exit-offset")){LiveGuardChecks.ExactSeamGoal(false,true);return 0;}
if(args.Contains("--guard-offcenter")){LiveGuardChecks.HeldRemainingIntent(true,true);return 0;}
if(args.Contains("--guard-occupancy")){LiveGuardChecks.HeldRemainingIntent(true);return 0;}
if(args.Contains("--guard-held-remaining")){LiveGuardChecks.HeldRemainingIntent();return 0;}
if(args.Contains("--guard-inputs")){Spacewars.Tests.PlayMode.RecastLayerInputs.ValidateGuard();return 0;}
if(args.Contains("--guard-pending-geometry")){LiveGuardChecks.PendingSubset(true);return 0;}
if(args.Contains("--guard-pending")){LiveGuardChecks.PendingSubset();return 0;}
if(args.Contains("--guard-near-peer")){LiveGuardChecks.NearPeerContact();return 0;}
if(args.Contains("--guard-partial")){LiveGuardChecks.PartialContract();return 0;}
if(args.Contains("--guard-hold-seam")){LiveGuardChecks.ExactSeamGoal(true);return 0;}
if(args.Contains("--guard-seam")){LiveGuardChecks.ExactSeamGoal();return 0;}
if(args.Contains("--layer-inputs")){Spacewars.Tests.PlayMode.RecastLayerInputs.Validate();return 0;}
if(args.Contains("--layer-startup-diagnostic")){LayeredRecastChecks.StartupDiagnostic();return 0;}
if(args.Contains("--layer-session")){LayeredRecastChecks.Session();return 0;}
if(args.Contains("--layered")){LayeredRecastChecks.Run();return 0;}
if(args.Contains("--mixed-inputs")){Spacewars.Tests.PlayMode.RecastStartupMixedInputs.Validate();return 0;}
if(args.Contains("--cache-ownership-negative")){StartupCacheChecks.RunCacheNegative();return 0;}
if(args.Contains("--startup-cache")){StartupCacheChecks.Run();return 0;}
var terrain=ThreeCrossingsMap.Default;
var geometry=new NavGeometry(terrain.HalfExtent,terrain.MovementBlockers.ToArray(),terrain.Revision);
var profile=NavigationProfile.Default;
try{RecastAuthorityGeometry.Prepare(terrain,geometry,profile,()=>true);throw new Exception("cancel ignored");}catch(OperationCanceledException){}var sw=Stopwatch.StartNew();
var input=RecastAuthorityGeometry.Prepare(terrain,geometry,profile);Console.WriteLine($"PREP triangles={input.TriangleCount} ms={sw.ElapsedMilliseconds} binding={input.Binding}");
try{using var cancelledMap=new RecastNavigationMap(input,()=>true);throw new Exception("bake cancel ignored");}catch(OperationCanceledException){}
using(var map=new RecastNavigationMap(input)){
Console.WriteLine($"BAKE ms={sw.ElapsedMilliseconds}");
var a=new NavPoint(-31,-46);var b=new NavPoint(-46,-46);
if(!terrain.TryLocate(a,profile.Radius,out var from)||!terrain.TryLocate(b,profile.Radius,out var to))throw new Exception("bad fixture");
using var q=map.Query(from,to);for(int i=0;i<4096&&q.Status==RecastQueryStatus.Pending;i++)q.Step(32);
Console.WriteLine($"QUERY {from.SurfaceId}->{to.SurfaceId} status={q.Status} iterations={q.Iterations}");
if(q.Status!=RecastQueryStatus.Full)return 1;
PaidMemoryChecks.Run(input,from,to);
var provider=new PartitionNavigationProvider(terrain,geometry,null);
var first=new NavigationRequest(1,1,1,1,profile,geometry,a,b,memberIdentity:new NavigationMemberIdentity(1,0,1,1,1),startLocation:from,goalLocation:to);
var second=new NavigationRequest(1,2,2,1,profile,geometry,a,b,memberIdentity:new NavigationMemberIdentity(2,0,1,1,1),startLocation:from,goalLocation:to);
var spine=RecastGroupPreparation.BuildSpine(provider,new[]{first,second},q.CopyTypedRoute(),input);
if(spine==null)throw new Exception("no typed shared spine");
foreach(var member in new[]{first,second}){var plan=RecastGroupPreparation.Attach(provider,member,spine,input);if(plan==null||plan.Spine!=spine||plan.TerminalLeg!=plan.OriginLegs+plan.Exit-plan.Join)throw new Exception("lost shared attachment metadata");}
var fakeWrong=new[]{q.CopyTypedRoute()[0],new RecastRoutePoint(new NavLocation(b,"bank/east"),0)};
if(RecastGroupPreparation.BuildSpine(provider,new[]{first,second},fakeWrong,input)!=null)throw new Exception("wrong-layer native endpoint accepted");
if(spine.Legs.Any(l=>l.To.SurfaceId=="deck/-1/-1"&&Math.Abs(l.To.Position.X+34.400001525878906)<1e-6))throw new Exception("raster support mismatch was relabelled or retained");
Console.WriteLine($"SPINE typed={spine.Legs.Length} fan={spine.Fan} accepted member correspondence PASS");
var at=from;foreach(var waypoint in q.CopyTypedRoute()){
var point=waypoint.Location.Position;
Console.WriteLine($"  {point.X:R},{point.Z:R} y={waypoint.Height:R} surface={waypoint.Location.SurfaceId}");
if(!geometry.SegmentFree(at.Position,point,profile.Radius)||!terrain.TryTraverse(at,point,profile.Radius,out var next))throw new Exception("authority rejects route segment");at=next;}
if(!terrain.TryTraverse(at,b,profile.Radius,out at)||at.SurfaceId!=to.SurfaceId)throw new Exception("authority endpoint mismatch");
}
using(var cache=new RecastMapCache()){
var cold=cache.Acquire(input);var warm=cache.Acquire(RecastAuthorityGeometry.Prepare(terrain,geometry,profile));
if(!ReferenceEquals(cold,warm)||cache.ColdBuilds!=1||cache.WarmHits!=1)throw new Exception("immutable map reuse failed");
terrain.TryLocate(new NavPoint(-31,-46),profile.Radius,out var a);terrain.TryLocate(new NavPoint(-46,-46),profile.Radius,out var b);
using(var pinned=cold.Query(a,b)){
var revised=RecastAuthorityGeometry.Prepare(terrain,new NavGeometry(geometry.HalfExtent,geometry.Obstacles.ToArray(),geometry.Revision+1),profile);
var replacement=cache.Acquire(revised);if(ReferenceEquals(cold,replacement)||cache.ColdBuilds!=2)throw new Exception("geometry version not invalidated");
while(pinned.Status==RecastQueryStatus.Pending)pinned.Step(32);if(pinned.Status!=RecastQueryStatus.Full)throw new Exception("immutable replacement invalidated a pinned query");
}
Console.WriteLine($"CACHE cold={cache.ColdBuilds} warm={cache.WarmHits} old query lifetime PASS");
}
if(RecastNative.sw_recast_live_maps()!=0||RecastNative.sw_recast_live_queries()!=0)throw new Exception("native leak");return 0;
}}
