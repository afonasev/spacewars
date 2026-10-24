using System;
namespace Spacewars.Simulation
{
    public sealed partial class PartitionNavigationProvider
    {
        internal int MonotonePartCount=>locations.Supports.Count+locations.Transitions.Count+Seams.Count+locations.Geometry.Obstacles.Count;
        internal bool MonotoneHeaderTo(PartitionNavigationProvider next)=>next!=null&&terrainId==next.terrainId&&terrainRevision==next.terrainRevision&&surfaceSemanticsVersion==next.surfaceSemanticsVersion&&holdBinding==next.holdBinding&&HalfExtent==next.HalfExtent&&locations.Supports.Count==next.locations.Supports.Count&&locations.Transitions.Count==next.locations.Transitions.Count&&Seams.Count==next.Seams.Count&&locations.Geometry.HalfExtent==next.locations.Geometry.HalfExtent&&locations.Geometry.Obstacles.Count<=next.locations.Geometry.Obstacles.Count;
        // One bounded authored declaration/obstacle comparison per paid step.
        // Strict prefix is sufficient for additive obstacles; unproven reorder
        // takes cold fallback rather than assuming geometric equivalence.
        internal bool MonotonePartTo(PartitionNavigationProvider next,int index)
        {
            if(!MonotoneHeaderTo(next)||index<0||index>=MonotonePartCount)return false;
            if(index<locations.Supports.Count){var a=locations.Supports[index];var b=next.locations.Supports[index];return a.Id==b.Id&&a.SurfaceId==b.SurfaceId&&a.Height==b.Height&&a.Gradient.Equals(b.Gradient)&&a.Origin.Equals(b.Origin)&&a.IsBridge==b.IsBridge&&SamePolygon(a.Bounds.Footprint,b.Bounds.Footprint);}
            index-=locations.Supports.Count;
            if(index<locations.Transitions.Count){var a=locations.Transitions[index];var b=next.locations.Transitions[index];return a.Id==b.Id&&a.From==b.From&&a.To==b.To;}
            index-=locations.Transitions.Count;
            if(index<Seams.Count){var a=Seams[index];var b=next.Seams[index];return a.Id==b.Id&&a.FromSurface==b.FromSurface&&a.ToSurface==b.ToSurface&&a.From.Equals(b.From)&&a.To.Equals(b.To)&&a.Allowed==b.Allowed;}
            index-=Seams.Count;var x=locations.Geometry.Obstacles[index];var y=next.locations.Geometry.Obstacles[index];return x.GeometryEquals(y);
        }
        private static bool SamePolygon(NavPolygon a,NavPolygon b)
        {if(a==null||b==null)return a==null&&b==null;if(a.Vertices.Count!=b.Vertices.Count)return false;for(int i=0;i<a.Vertices.Count;i++)if(!a.Vertices[i].Equals(b.Vertices[i]))return false;return true;}
        internal bool CertifyHistoricalLeg(NavTypedLeg old,NavClearanceProfile clearance,bool incidentEndpoints=false)
        {
            var from=OwnedCheckpointLocation(old.From);var to=OwnedCheckpointLocation(old.To);
            if(!IsValid(from,clearance)||!IsValid(to,clearance))return false;
            if(old.Kind==NavTypedLegKind.Surface)return old.PortalId==null&&(incidentEndpoints?locations.CertifyIncidentSurfaceLeg(from,to,clearance.Radius):CanSweep(from,to,clearance))&&old.Cost==LayeredNavigationProvider.Distance(from.Position,to.Position)*SurfaceCost(from.SurfaceId);
            if(old.Kind!=NavTypedLegKind.Portal||!from.Position.Equals(to.Position)||old.Cost!=0)return false;
            // Supports/transitions and all original seams were certified equal.
            // The historical zero-displacement seam proof is retained; only
            // current footprint validity can be restricted by new blockers.
            foreach(var seam in Seams)if(seam.Id==old.PortalId)return seam.Allowed&&((seam.FromSurface==from.SurfaceId&&seam.ToSurface==to.SurfaceId)||(seam.ToSurface==from.SurfaceId&&seam.FromSurface==to.SurfaceId));return false;
        }
    }
}
