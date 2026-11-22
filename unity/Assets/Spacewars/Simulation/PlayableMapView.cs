using System;
using System.Collections.Generic;
using System.Linq;

namespace Spacewars.Simulation
{
    // Normalized UI coordinates; an optional camera bearing aligns map up with screen up.
    public readonly struct PlayableMapTransform
    {
        public readonly double HalfWidth,HalfDepth;
        private readonly double cosine,sine,screenHalfWidth,screenHalfDepth;
        public PlayableMapTransform(double halfWidth,double halfDepth,double? cameraYawDegrees=null)
        {
            if(halfWidth<=0||halfDepth<=0)throw new ArgumentOutOfRangeException();
            HalfWidth=halfWidth;HalfDepth=halfDepth;
            double angle=(cameraYawDegrees??0)*Math.PI/180;
            cosine=Math.Cos(angle);sine=Math.Sin(angle);
            // Fit the whole rotated map without clipping its corners or distorting bearings.
            double extent=Math.Max(Math.Abs(cosine)*halfWidth+Math.Abs(sine)*halfDepth,
                Math.Abs(sine)*halfWidth+Math.Abs(cosine)*halfDepth);
            screenHalfWidth=cameraYawDegrees.HasValue?extent:halfWidth;
            screenHalfDepth=cameraYawDegrees.HasValue?extent:halfDepth;
        }
        public NavPoint Project(NavPoint world)=>new NavPoint(
            .5+(cosine*world.X-sine*world.Z)/(2*screenHalfWidth),
            .5-(sine*world.X+cosine*world.Z)/(2*screenHalfDepth));
        public NavPoint Ground(NavPoint uv)
        {
            double right=(uv.X*2-1)*screenHalfWidth,up=(1-uv.Z*2)*screenHalfDepth;
            return new NavPoint(ClampWorld(cosine*right+sine*up,HalfWidth),
                ClampWorld(-sine*right+cosine*up,HalfDepth));
        }
        private static double ClampWorld(double value,double extent)=>Math.Max(-extent,Math.Min(extent,value));
    }
    public enum MapMarkState { Empty, Ready, Construction, Memory, Unit, OrbitalMine }
    public sealed class PlayableMapMark
    {
        public PlayableMapMark(int id,NavPoint position,PlayableBuildingKind kind,MapMarkState state,PlayableOwner? owner,int start=0){Id=id;Position=position;Kind=kind;State=state;Owner=owner;Start=start;}
        public int Id{get;} public NavPoint Position{get;} public PlayableBuildingKind Kind{get;} public MapMarkState State{get;} public PlayableOwner? Owner{get;} public int Start{get;}
    }
    public static class PlayableMapView
    {
        // Project the perspective and its public relay colors without a domain lookup.
        public static IReadOnlyList<PlayableMapMark> Marks(PlayableSnapshot view,bool showOrbitalMines=false)
        {
            var result=new List<PlayableMapMark>();
            foreach(var site in view.PublicSites)
            {
                if(site.Kind==PlayableBuildingKind.RelayTower)
                {
                    var publicControl=view.MapRelayControl.FirstOrDefault(r=>r.SiteId==site.Id);
                    var liveRelay=view.Sites.FirstOrDefault(s=>s.Site.Id==site.Id);
                    var observed=view.Vision?.KnownRelays.FirstOrDefault(r=>r.SiteId==site.Id);
                    var owner=publicControl!=null?publicControl.Owner:liveRelay!=null?liveRelay.Owner:observed?.Owner;
                    var state=publicControl==null&&liveRelay==null&&observed!=null?MapMarkState.Memory:
                        owner.HasValue?MapMarkState.Ready:MapMarkState.Empty;
                    result.Add(new PlayableMapMark(-site.Id,site.Position,site.Kind,state,owner));
                    continue;
                }
                var live=view.Buildings.FirstOrDefault(b=>b.SiteId==site.Id&&b.SlotId==0&&b.Phase!=ConstructionPhase.Pending);
                var memory=view.Vision?.KnownBuildings.FirstOrDefault(b=>b.Position.Equals(site.Position)&&!view.Vision.IsVisible(b.Position));
                result.Add(new PlayableMapMark(live?.Id??memory?.Id??-site.Id,site.Position,site.Kind,live!=null?(live.Phase==ConstructionPhase.Ready?MapMarkState.Ready:MapMarkState.Construction):memory!=null?MapMarkState.Memory:MapMarkState.Empty,live?.Owner??memory?.Owner,site.Kind==PlayableBuildingKind.Headquarters?site.Id:0));
            }
            foreach(var b in view.Buildings)if(b.SlotId!=0&&b.Phase!=ConstructionPhase.Pending)
                result.Add(new PlayableMapMark(b.Id,b.Position,b.Kind,b.Phase==ConstructionPhase.Ready?MapMarkState.Ready:MapMarkState.Construction,b.Owner));
            if(view.Vision!=null)foreach(var b in view.Vision.KnownBuildings)
                if(!view.Vision.IsVisible(b.Position)&&!view.PublicSites.Any(s=>s.Position.Equals(b.Position)))result.Add(new PlayableMapMark(b.Id,b.Position,b.Kind,MapMarkState.Memory,b.Owner));
            foreach(var e in view.Entities)result.Add(new PlayableMapMark(e.Id,e.Position,default(PlayableBuildingKind),MapMarkState.Unit,e.Owner));
            if(showOrbitalMines)foreach(var mine in view.Mines)
                result.Add(new PlayableMapMark(mine.Id,mine.Point,default,MapMarkState.OrbitalMine,mine.Owner));
            return result.AsReadOnly();
        }
        public static int[] SelectOwnUnits(PlayableSnapshot view,NavPoint a,NavPoint b)=>view.Entities.Where(e=>e.Owner==view.Owner&&e.Position.X>=Math.Min(a.X,b.X)&&e.Position.X<=Math.Max(a.X,b.X)&&e.Position.Z>=Math.Min(a.Z,b.Z)&&e.Position.Z<=Math.Max(a.Z,b.Z)).Select(e=>e.Id).ToArray();
    }
}
