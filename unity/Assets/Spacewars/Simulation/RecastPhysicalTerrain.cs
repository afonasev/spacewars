using System;
using System.Collections.Generic;
using System.Linq;
namespace Spacewars.Simulation
{
    // Immutable physical companion to an authored layered provider. Native graph
    // edges can span a ramp; executed legs always describe its real support.
    internal interface IRecastPhysicalTerrain : ILayeredPlayableTerrain
    {
        MapSupport PhysicalSupportAt(NavLocation location);
        bool NativePortal(string id);
        bool NativeSupport(MapSupport support);
        NavLocation NativeEndpoint(NavLocation endpoint,NavLocation other);
        IReadOnlyList<NavTypedLeg> NativeAttachment(NavLocation from,NavLocation to);
        IReadOnlyList<NavTypedLeg> ExpandPortal(string id,bool forward);
        bool TraverseAccepted(NavLocation from,NavPoint to,double radius,NavTypedRoute route,int cursor,out NavLocation reached);
        bool TracePhysical(NavLocation from,NavPoint to,double radius,ILayeredNavigationProvider canonical,NavTypedRoute route,int cursor,INavStorageReservation reservation,out NavLocation reached,out IReadOnlyList<NavTypedLeg> trace);
    }

    internal static class RecastLayerTrace
    {
        internal static bool MatchPortal(NavTypedRoute route,ref int cursor,string id,string from,string to){
            int next=cursor;while(next<route.Legs.Count&&route.Legs[next].Kind!=NavTypedLegKind.Portal)next++;
            if(next<route.Legs.Count){var p=route.Legs[next];if(p.PortalId==id&&p.From.SurfaceId==from&&p.To.SurfaceId==to){cursor=Math.Min(route.Legs.Count-1,next+1);return true;}}
            int previous=Math.Min(cursor-1,route.Legs.Count-1);while(previous>=0&&route.Legs[previous].Kind!=NavTypedLegKind.Portal)previous--;
            if(previous>=0){var p=route.Legs[previous];if(p.PortalId==id&&p.To.SurfaceId==from&&p.From.SurfaceId==to){cursor=Math.Max(0,previous-1);return true;}}
            return false;
        }

        internal static bool Try(ILayeredNavigationProvider provider,NavLocation from,NavLocation target,NavClearanceProfile clearance,out IReadOnlyList<NavTypedLeg> trace,INavStorageReservation reservation=null)
        {
            trace=null;if(!provider.IsValid(from,clearance)||!provider.IsValid(target,clearance))return false;
            NavPagedList<NavTypedLeg> owned=null;List<NavTypedLeg> detached=null;
            void Add(NavLocation a,NavLocation b,double cost,NavTypedLegKind kind,string portal){if(reservation!=null){var value=NavTypedLeg.CreateOwned(a,b,cost,kind,portal,reservation,provider);try{owned.Add(value);}finally{value.ReleaseStorage();}}else detached.Add(new NavTypedLeg(a,b,cost,kind,portal));}
            try{
                if(reservation!=null)owned=NavPagedList<NavTypedLeg>.Create(reservation.TryReserve);else detached=new List<NavTypedLeg>();
                if(provider.CanSweep(from,target,clearance))Add(from,target,LayeredNavigationProvider.Distance(from.Position,target.Position)*provider.SurfaceCost(from.SurfaceId),NavTypedLegKind.Surface,null);
                else{
                    NavLayerPortal selected=null;bool forward=false;
                    foreach(var portal in provider.Portals){bool f=portal.Entry.SurfaceId==from.SurfaceId&&portal.Exit.SurfaceId==target.SurfaceId;bool rev=portal.Exit.SurfaceId==from.SurfaceId&&portal.Entry.SurfaceId==target.SurfaceId&&portal.Direction==NavPortalDirection.Both;
                        if(!f&&!rev)continue;var entry=f?portal.Entry:portal.Exit;var exit=f?portal.Exit:portal.Entry;
                        // This trace projects an explicit accepted connector. A
                        // nonzero-XZ backend edge must be expanded before here.
                        if(!entry.Position.Equals(exit.Position)||!portal.Open||!portal.MobilityClasses.Contains(clearance.MobilityClass)||!portal.Corridor.Contains(entry.Position,clearance.Radius)||!provider.CanSweep(from,entry,clearance)||!provider.CanSweep(exit,target,clearance))continue;
                        if(selected!=null)return false;selected=portal;forward=f;
                    }
                    if(selected==null)return false;var a=forward?selected.Entry:selected.Exit;var b=forward?selected.Exit:selected.Entry;
                    Add(from,a,LayeredNavigationProvider.Distance(from.Position,a.Position)*provider.SurfaceCost(from.SurfaceId),NavTypedLegKind.Surface,null);
                    Add(a,b,selected.TraversalCost,NavTypedLegKind.Portal,selected.Id);
                    Add(b,target,LayeredNavigationProvider.Distance(b.Position,target.Position)*provider.SurfaceCost(target.SurfaceId),NavTypedLegKind.Surface,null);
                }
                if(owned!=null){owned.Seal();trace=owned;owned=null;}else trace=detached;return true;
            }finally{owned?.Release();}
        }
    }

    // Explicit QA domain entry; absent from the production map/lobby catalog.
    // The narrow ramp retains the original Stacked entry/exit, heights and width.
    public sealed class StackedRampQualificationMap : IPlayableTerrain, ILayeredPlayableTerrain, IRecastPhysicalTerrain
    {
        public string Id=>"stacked-ramp-qualification-v1";public int Revision=>1;public int SurfaceSemanticsVersion=>1;public double HalfExtent=>64;public double DirectFireHeight=>1;
        public LayeredNavigationProvider Provider{get;}public ILayeredNavigationProvider LayeredNavigation=>Provider;
        public IReadOnlyList<MapSupport> Supports{get;}public IReadOnlyList<NavObstacle> Solids=>Array.Empty<NavObstacle>();public IReadOnlyList<NavObstacle> MovementBlockers=>Solids;public IReadOnlyList<NavSurfaceTransition> SurfaceTransitions=>Array.Empty<NavSurfaceTransition>();
        public StackedRampQualificationMap(){
            var supports=new[]{new MapSupport("lower",new NavObstacle(-64,-64,64,64),false,surfaceId:"lower"),new MapSupport("upper",new NavObstacle(-8,-8,8,8),false,4,surfaceId:"upper"),new MapSupport("ramp-low-landing",new NavObstacle(-3,-.75,-2,.75),false,surfaceId:"ramp-support"),new MapSupport("ramp-plane",new NavObstacle(-2,-.75,2,.75),false,0,new NavPoint(1,0),new NavPoint(-2,0),"ramp-support"),new MapSupport("ramp-high-landing",new NavObstacle(2,-.75,3,.75),false,4,surfaceId:"ramp-support")};
            Supports=Array.AsReadOnly(supports);
            NavTraversalArea Area(double a,double b,double c,double d,params NavObstacle[] blockers)=>new NavTraversalArea(new NavObstacle(a,b,c,d).Footprint,64,blockers);
            var corridor=Area(-3,-.75,3,.75);
            var surfaces=new[]{new NavLayerSurface("lower","lower-combat",0,1,Area(-64,-64,64,64)),new NavLayerSurface("upper","upper-combat",4,2,Area(-8,-8,8,8,new NavObstacle(4,-1,5,1))),new NavLayerSurface("ramp-support","ramp-combat",0,1,corridor)};
            var low=new NavLocation(new NavPoint(-2,0),"ramp-support");var high=new NavLocation(new NavPoint(2,0),"ramp-support");
            var portals=new[]{new NavLayerPortal("ramp",StackedNavigationFixture.Entry,StackedNavigationFixture.Exit,NavPortalDirection.Both,true,6,corridor,new[]{"ground"}),new NavLayerPortal("ramp-entry",StackedNavigationFixture.Entry,low,NavPortalDirection.Both,true,1,corridor,new[]{"ground"}),new NavLayerPortal("ramp-exit",high,StackedNavigationFixture.Exit,NavPortalDirection.Both,true,1,corridor,new[]{"ground"})};
            string binding=NavContract.Digest(w=>{w.Write(Id);w.Write(Revision);w.Write("ramp-height:x+2;-2<=x<2;flat-landings;right-continuous-gradient-v1");foreach(var s in supports){w.Write(s.Id);w.Write(s.SurfaceId);NavContract.Polygon(w,s.Bounds.Footprint);w.Write(s.Height);NavContract.Point(w,s.Gradient);NavContract.Point(w,s.Origin);}foreach(var p in portals)p.Write(w);});
            Provider=new LayeredNavigationProvider(Id,Revision,SurfaceSemanticsVersion,Revision,binding,surfaces,portals);
        }
        internal LayeredNavigationProvider ForGeometry(NavGeometry geometry,string hold){
            if(geometry==null||geometry.HalfExtent!=HalfExtent)throw new ArgumentException("Physical provider geometry mismatch.");
            if(geometry.Obstacles.Count==0&&hold==null&&geometry.Revision==Revision)return Provider;
            string binding=NavContract.Digest(w=>{w.Write(Provider.Binding);w.Write(geometry.Revision);w.Write(hold??"");w.Write(geometry.Obstacles.Count);foreach(var o in geometry.Obstacles){w.Write(o.MinX);w.Write(o.MinZ);w.Write(o.MaxX);w.Write(o.MaxZ);w.Write(o.CircleRadius);w.Write(o.PhysicalLayer);w.Write(o.Bottom);w.Write(o.Top);w.Write(o.Polygon!=null);if(o.Polygon!=null)NavContract.Polygon(w,o.Polygon);}});
            var surfaces=Provider.Surfaces.Select(s=>new NavLayerSurface(s.Id,s.CombatFamily,s.Height,s.DistanceCost,new NavTraversalArea(s.Area.Boundary,HalfExtent,s.Area.Blockers.Concat(geometry.Obstacles.Where(o=>o.PhysicalLayer<0||o.PhysicalLayer<Provider.Surfaces.Count&&Provider.Surfaces[o.PhysicalLayer].Id==s.Id))))).ToArray();
            var portals=Provider.Portals.Select(p=>new NavLayerPortal(p.Id,p.Entry,p.Exit,p.Direction,p.Open,p.TraversalCost,new NavTraversalArea(p.Corridor.Boundary,HalfExtent,p.Corridor.Blockers.Concat(geometry.Obstacles.Where(o=>o.PhysicalLayer<0||o.PhysicalLayer<Provider.Surfaces.Count&&(Provider.Surfaces[o.PhysicalLayer].Id==p.Entry.SurfaceId||Provider.Surfaces[o.PhysicalLayer].Id==p.Exit.SurfaceId)))),p.MobilityClasses)).ToArray();
            return new LayeredNavigationProvider(Id,Revision,SurfaceSemanticsVersion,geometry.Revision,binding,surfaces,portals);
        }
        bool IRecastPhysicalTerrain.NativeSupport(MapSupport support)=>support.SurfaceId!="ramp-support";
        NavLocation IRecastPhysicalTerrain.NativeEndpoint(NavLocation endpoint,NavLocation other){
            if(endpoint.SurfaceId!="ramp-support")return endpoint;
            // An explicit authored branch, not a nearest-floor guess. The ramp
            // is a physical connector and need not survive raster erosion.
            return other.SurfaceId=="upper"?StackedNavigationFixture.Exit:StackedNavigationFixture.Entry;
        }
        IReadOnlyList<NavTypedLeg> IRecastPhysicalTerrain.NativeAttachment(NavLocation from,NavLocation to){
            if(from.Equals(to))return Array.Empty<NavTypedLeg>();
            var clearance=new NavClearanceProfile("ground",1,"ground",0,0);
            if(!RecastLayerTrace.Try(Provider,from,to,clearance,out var legs))throw new ArgumentException("No explicit physical native anchor attachment.");return legs;
        }
        bool IRecastPhysicalTerrain.NativePortal(string id)=>id=="ramp";
        IReadOnlyList<NavTypedLeg> IRecastPhysicalTerrain.ExpandPortal(string id,bool forward){
            if(id!="ramp")throw new ArgumentException("Unknown physical backend portal.");
            var low=Provider.Portals.Single(p=>p.Id=="ramp-entry");var high=Provider.Portals.Single(p=>p.Id=="ramp-exit");
            return forward?new[]{new NavTypedLeg(low.Entry,low.Exit,low.TraversalCost,NavTypedLegKind.Portal,low.Id),new NavTypedLeg(low.Exit,high.Entry,4,NavTypedLegKind.Surface,null),new NavTypedLeg(high.Entry,high.Exit,high.TraversalCost,NavTypedLegKind.Portal,high.Id)}:new[]{new NavTypedLeg(high.Exit,high.Entry,high.TraversalCost,NavTypedLegKind.Portal,high.Id),new NavTypedLeg(high.Entry,low.Exit,4,NavTypedLegKind.Surface,null),new NavTypedLeg(low.Exit,low.Entry,low.TraversalCost,NavTypedLegKind.Portal,low.Id)};
        }
        private MapSupport Piece(NavLocation location){
            if(location.SurfaceId=="lower")return Supports[0];if(location.SurfaceId=="upper")return Supports[1];if(location.SurfaceId!="ramp-support")throw new ArgumentException("Unknown physical surface.");
            return location.Position.X < -2?Supports[2]:location.Position.X>=2?Supports[4]:Supports[3];
        }
        public double Height(NavLocation location){if(!IsValidLocation(location,0))throw new ArgumentException("Unsupported physical location.");return Piece(location).HeightAt(location.Position);}
        public bool IsValidLocation(NavLocation l,double r)=>Provider.IsValid(l,new NavClearanceProfile("ground",1,"ground",0,r))&&Piece(l).Contains(l.Position);
        public bool TryLocate(NavPoint p,double r,out NavLocation l)=>Provider.TryLocate(p,new NavClearanceProfile("ground",1,"ground",0,r),out l);
        public bool TryTraverse(NavLocation from,NavPoint to,double radius,out NavLocation location)=>Trace(from,to,radius,null,null,0,from.SurfaceId,out location);
        public bool TryTraverse(NavLocation from,NavLocation to,double radius,out NavLocation location)=>Trace(from,to.Position,radius,null,null,0,to.SurfaceId,out location)&&location.SurfaceId==to.SurfaceId;
        bool IRecastPhysicalTerrain.TraverseAccepted(NavLocation from,NavPoint to,double radius,NavTypedRoute route,int cursor,out NavLocation reached)=>Trace(from,to,radius,null,route,cursor,null,out reached);
        private bool Trace(NavLocation from,NavPoint to,double radius,Action<NavTypedLeg> emit,NavTypedRoute accepted,int cursor,string desiredSurface,out NavLocation reached){
            reached=default;var clearance=new NavClearanceProfile("ground",1,"ground",0,radius);if(!IsValidLocation(from,radius))return false;var at=from;double dx=to.X-from.Position.X;
            for(int i=0;i<3;i++){
                if(accepted!=null){
                    int nextPortal=cursor;while(nextPortal<accepted.Legs.Count&&accepted.Legs[nextPortal].Kind!=NavTypedLegKind.Portal)nextPortal++;
                    if(nextPortal<accepted.Legs.Count){var leg=accepted.Legs[nextPortal];var candidate=new NavLocation(to,leg.To.SurfaceId);var landing=new NavLocation(at.Position,leg.To.SurfaceId);var portal=Provider.Portals.Single(p=>p.Id==leg.PortalId);
                        bool acceptedForward=leg.From.SurfaceId==portal.Entry.SurfaceId&&leg.To.SurfaceId==portal.Exit.SurfaceId;
                        bool acceptedReverse=leg.From.SurfaceId==portal.Exit.SurfaceId&&leg.To.SurfaceId==portal.Entry.SurfaceId&&portal.Direction==NavPortalDirection.Both;
                        // A crossing is any qualified point on this authored X
                        // plane, not only its center Z. No epsilon or floor guess.
                        if((!to.Equals(at.Position)||to.Equals(accepted.Subscription.AssignedEndpoint.Position)&&leg.To.SurfaceId==accepted.Subscription.AssignedEndpoint.SurfaceId)&&at.SurfaceId==leg.From.SurfaceId&&at.Position.X==leg.From.Position.X&&leg.From.Position.Equals(leg.To.Position)&&
                            (acceptedForward||acceptedReverse)&&at.Position.X==(acceptedForward?portal.Entry:portal.Exit).Position.X&&portal.Open&&portal.Corridor.Contains(at.Position,radius)&&IsValidLocation(landing,radius)&&Provider.CanSweep(landing,candidate,clearance)&&Math.Abs(Height(at)-Height(landing))<=1e-9){
                            emit?.Invoke(new NavTypedLeg(at,at,0,NavTypedLegKind.Surface,null));emit?.Invoke(new NavTypedLeg(at,landing,leg.Cost,NavTypedLegKind.Portal,leg.PortalId));at=landing;cursor=Math.Min(accepted.Legs.Count-1,nextPortal+1);continue;
                        }
                    }
                }
                if(accepted==null&&desiredSurface!=null&&desiredSurface!=at.SurfaceId){
                    NavLayerPortal crossing=null;NavLocation destination=default;
                    foreach(var candidatePortal in Provider.Portals){bool goForward=at.SurfaceId==candidatePortal.Entry.SurfaceId&&at.Position.X==candidatePortal.Entry.Position.X&&candidatePortal.Exit.SurfaceId==desiredSurface;bool goReverse=at.SurfaceId==candidatePortal.Exit.SurfaceId&&at.Position.X==candidatePortal.Exit.Position.X&&candidatePortal.Entry.SurfaceId==desiredSurface&&candidatePortal.Direction==NavPortalDirection.Both;if(!goForward&&!goReverse||!candidatePortal.Entry.Position.Equals(candidatePortal.Exit.Position)||!candidatePortal.Open)continue;var landing=new NavLocation(at.Position,(goForward?candidatePortal.Exit:candidatePortal.Entry).SurfaceId);if(!candidatePortal.Corridor.Contains(at.Position,radius)||!IsValidLocation(landing,radius)||!Provider.CanSweep(landing,new NavLocation(to,landing.SurfaceId),clearance))continue;if(crossing!=null)return false;crossing=candidatePortal;destination=landing;}
                    if(crossing!=null){if(Math.Abs(Height(at)-Height(destination))>1e-9)return false;emit?.Invoke(new NavTypedLeg(at,destination,crossing.TraversalCost,NavTypedLegKind.Portal,crossing.Id));at=destination;continue;}
                }
                double x=0;string id=null;
                if(dx>0&&at.SurfaceId=="lower"&&at.Position.X<-2&&(to.X>-2||desiredSurface=="ramp-support"&&to.X==-2)){x=-2;id="ramp-entry";}
                else if(dx>0&&at.SurfaceId=="ramp-support"&&at.Position.X<2&&(to.X>2||desiredSurface=="upper"&&to.X==2)){x=2;id="ramp-exit";}
                else if(dx<0&&at.SurfaceId=="upper"&&at.Position.X>2&&(to.X<2||desiredSurface=="ramp-support"&&to.X==2)){x=2;id="ramp-exit";}
                else if(dx<0&&at.SurfaceId=="ramp-support"&&at.Position.X>-2&&(to.X<-2||desiredSurface=="lower"&&to.X==-2)){x=-2;id="ramp-entry";}
                if(id==null&&accepted==null&&desiredSurface!=at.SurfaceId){
                    if(at.Position.X==-2&&at.SurfaceId=="lower"&&desiredSurface=="ramp-support"&&dx>0){x=-2;id="ramp-entry";}
                    else if(at.Position.X==2&&at.SurfaceId=="upper"&&desiredSurface=="ramp-support"&&dx<0){x=2;id="ramp-exit";}
                }
                if(id==null||accepted==null&&desiredSurface==at.SurfaceId)break;double t=(x-from.Position.X)/dx;var point=new NavPoint(x,from.Position.Z+t*(to.Z-from.Position.Z));var p=Provider.Portals.Single(v=>v.Id==id);bool forward=at.SurfaceId==p.Entry.SurfaceId;var end=new NavLocation(point,at.SurfaceId);var next=new NavLocation(point,(forward?p.Exit:p.Entry).SurfaceId);
                if(!p.Corridor.Contains(point,radius))break;
                if(accepted!=null&&!RecastLayerTrace.MatchPortal(accepted,ref cursor,p.Id,end.SurfaceId,next.SurfaceId))break;
                if(!p.Open||!Provider.CanSweep(at,end,clearance)||!IsValidLocation(next,radius)||Math.Abs(Height(end)-Height(next))>1e-9)return false;
                emit?.Invoke(new NavTypedLeg(at,end,LayeredNavigationProvider.Distance(at.Position,end.Position)*Provider.SurfaceCost(at.SurfaceId),NavTypedLegKind.Surface,null));emit?.Invoke(new NavTypedLeg(end,next,p.TraversalCost,NavTypedLegKind.Portal,p.Id));at=next;
            }
            var target=new NavLocation(to,at.SurfaceId);if(!Provider.CanSweep(at,target,clearance)||!IsValidLocation(target,radius))return false;emit?.Invoke(new NavTypedLeg(at,target,LayeredNavigationProvider.Distance(at.Position,to)*Provider.SurfaceCost(at.SurfaceId),NavTypedLegKind.Surface,null));reached=target;return true;
        }
        bool IRecastPhysicalTerrain.TracePhysical(NavLocation from,NavPoint to,double radius,ILayeredNavigationProvider canonical,NavTypedRoute route,int cursor,INavStorageReservation r,out NavLocation reached,out IReadOnlyList<NavTypedLeg> trace){
            trace=null;var list=NavPagedList<NavTypedLeg>.Create(r.TryReserve);try{bool ok=Trace(from,to,radius,leg=>{var value=NavTypedLeg.CreateOwned(leg.From,leg.To,leg.Cost,leg.Kind,leg.PortalId,r,canonical);try{list.Add(value);}finally{value.ReleaseStorage();}},route,cursor,null,out reached);if(!ok)return false;list.Seal();trace=list;list=null;return true;}finally{list?.Release();}
        }
        public bool CompatibleCombatSurface(NavLocation a,NavLocation b)=>Provider.CompatibleCombatSurface(a,b);
        MapSupport IRecastPhysicalTerrain.PhysicalSupportAt(NavLocation location)=>IsValidLocation(location,0)?Piece(location):null;
        public MapSupport SupportAt(NavPoint p){if(!TryLocate(p,0,out var l))throw new ArgumentException("Explicit surface required.");return Piece(l);}
        public double SurfaceHeight(NavPoint p)=>SupportAt(p).HeightAt(p);public NavPoint SurfaceGradient(NavPoint p)=>SupportAt(p).Gradient;
        public bool SupportsFootprint(NavPoint p,double r)=>TryLocate(p,r,out _);public bool SupportsSweep(NavPoint a,NavPoint b,double r)=>TryLocate(a,r,out var l)&&TryTraverse(l,b,r,out _);
        public NavPoint Headquarters(PlayableOwner o)=>o==PlayableOwner.Player?new NavPoint(-32,-32):o==PlayableOwner.Enemy?new NavPoint(32,32):throw new ArgumentOutOfRangeException(nameof(o));
        public TerritorySite[] Sites(PlayableProfile p)=>new[]{new TerritorySite(1,PlayableBuildingKind.Headquarters,Headquarters(PlayableOwner.Player),p),new TerritorySite(2,PlayableBuildingKind.Headquarters,Headquarters(PlayableOwner.Enemy),p)};
    }
}
