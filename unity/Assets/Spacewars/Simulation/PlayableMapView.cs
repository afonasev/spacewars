using System;
using System.Collections.Generic;
using System.Linq;

namespace Spacewars.Simulation
{
    // Normalized UI coordinates: north (+Z) is always at the top, independent of camera.
    public readonly struct PlayableMapTransform
    {
        public readonly double HalfWidth,HalfDepth;
        public PlayableMapTransform(double halfWidth,double halfDepth){if(halfWidth<=0||halfDepth<=0)throw new ArgumentOutOfRangeException();HalfWidth=halfWidth;HalfDepth=halfDepth;}
        public NavPoint Project(NavPoint world)=>new NavPoint((world.X/HalfWidth+1)/2,(1-world.Z/HalfDepth)/2);
        public NavPoint Ground(NavPoint uv)=>new NavPoint((Clamp(uv.X)*2-1)*HalfWidth,(1-Clamp(uv.Z)*2)*HalfDepth);
        private static double Clamp(double v)=>Math.Max(0,Math.Min(1,v));
    }
    public enum MapMarkState { Empty, Ready, Construction, Memory, Unit }
    public sealed class PlayableMapMark
    {
        public PlayableMapMark(int id,NavPoint position,PlayableBuildingKind kind,MapMarkState state,PlayableOwner? owner,int start=0){Id=id;Position=position;Kind=kind;State=state;Owner=owner;Start=start;}
        public int Id{get;} public NavPoint Position{get;} public PlayableBuildingKind Kind{get;} public MapMarkState State{get;} public PlayableOwner? Owner{get;} public int Start{get;}
    }
    public static class PlayableMapView
    {
        // Only the safe perspective enters this projection. No domain lookup or ordinary slots.
        public static IReadOnlyList<PlayableMapMark> Marks(PlayableSnapshot view)
        {
            var result=new List<PlayableMapMark>();
            foreach(var site in view.DiscoveredSites)
            {
                var live=view.Buildings.FirstOrDefault(b=>b.SiteId==site.Id&&b.SlotId==0&&b.Phase!=ConstructionPhase.Pending);
                var memory=view.Vision?.KnownBuildings.FirstOrDefault(b=>b.Position.Equals(site.Position)&&!view.Vision.IsVisible(b.Position));
                result.Add(new PlayableMapMark(live?.Id??memory?.Id??0,site.Position,site.Kind,live!=null?(live.Phase==ConstructionPhase.Ready?MapMarkState.Ready:MapMarkState.Construction):memory!=null?MapMarkState.Memory:MapMarkState.Empty,live?.Owner??memory?.Owner,site.Kind==PlayableBuildingKind.Headquarters?site.Id:0));
            }
            foreach(var b in view.Buildings)if(b.SlotId!=0&&b.Phase!=ConstructionPhase.Pending)
                result.Add(new PlayableMapMark(b.Id,b.Position,b.Kind,b.Phase==ConstructionPhase.Ready?MapMarkState.Ready:MapMarkState.Construction,b.Owner));
            if(view.Vision!=null)foreach(var b in view.Vision.KnownBuildings)
                if(!view.Vision.IsVisible(b.Position)&&!view.DiscoveredSites.Any(s=>s.Position.Equals(b.Position)))result.Add(new PlayableMapMark(b.Id,b.Position,b.Kind,MapMarkState.Memory,b.Owner));
            foreach(var e in view.Entities)result.Add(new PlayableMapMark(e.Id,e.Position,default(PlayableBuildingKind),MapMarkState.Unit,e.Owner));
            return result.AsReadOnly();
        }
        public static int[] SelectOwnUnits(PlayableSnapshot view,NavPoint a,NavPoint b)=>view.Entities.Where(e=>e.Owner==view.Owner&&e.Position.X>=Math.Min(a.X,b.X)&&e.Position.X<=Math.Max(a.X,b.X)&&e.Position.Z>=Math.Min(a.Z,b.Z)&&e.Position.Z<=Math.Max(a.Z,b.Z)).Select(e=>e.Id).ToArray();
    }
}
