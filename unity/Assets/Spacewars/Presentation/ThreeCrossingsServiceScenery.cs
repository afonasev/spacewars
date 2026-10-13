using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
using UnityEngine;

namespace Spacewars.Presentation
{
    public static class ThreeCrossingsServiceScenery
    {
        static bool Overlap(NavPolygon a,NavPolygon b)=>a.MinX<b.MaxX&&a.MaxX>b.MinX&&a.MinZ<b.MaxZ&&a.MaxZ>b.MinZ;
        public static NavPolygon Envelope(Bounds bounds,Vector3 position,float scale,float clearance,PlayableProfile gameplay,float heading)
        {
            var physical=ThreeCrossingsDetails.CompoundEnvelope(bounds,position,scale,clearance,null,heading);
            var projected=ThreeCrossingsDetails.CompoundEnvelope(bounds,position,scale,clearance,gameplay,heading);
            return new NavPolygon(new[]{new NavPoint(Math.Min(physical.MinX,projected.MinX),Math.Min(physical.MinZ,projected.MinZ)),new NavPoint(Math.Max(physical.MaxX,projected.MaxX),Math.Min(physical.MinZ,projected.MinZ)),new NavPoint(Math.Max(physical.MaxX,projected.MaxX),Math.Max(physical.MaxZ,projected.MaxZ)),new NavPoint(Math.Min(physical.MinX,projected.MinX),Math.Max(physical.MaxZ,projected.MaxZ))});
        }
        public static bool VisibleFromGameplay(ThreeCrossingsMap map,PlayableProfile gameplay,Bounds bounds,Vector3 position,float scale,float heading)
        {
            var turn=Quaternion.Euler(0,heading,0);var camera=position+new Vector3((float)gameplay.CameraOffsetX,(float)gameplay.CameraHeight,(float)gameplay.CameraOffsetZ);
            // Sample lower silhouette and roof corners; interpolation resolution is
            // a visibility solver quality bound, not a gameplay/camera setting.
            foreach(float y in new[]{bounds.min.y+.25f,bounds.max.y})foreach(float x in new[]{bounds.min.x,bounds.max.x})foreach(float z in new[]{bounds.min.z,bounds.max.z})
            {
                var point=position+turn*(new Vector3(x,y,z)*scale);int samples=Mathf.CeilToInt(Vector2.Distance(new Vector2(point.x,point.z),new Vector2(camera.x,camera.z))/.3f);
                for(int i=1;i<samples;i++)
                {
                    var ray=Vector3.Lerp(point,camera,i/(float)samples);var ground=new NavPoint(ray.x,ray.z);
                    if(map.Solids.Any(s=>s.Contains(ground)&&ray.y>s.Bottom&&ray.y<s.Top))return false;
                    if(map.Supports.Any(s=>s.Bounds.Contains(ground)&&ray.y<s.HeightAt(ground)))return false;
                }
            }
            return true;
        }
        public static string Rejection(ThreeCrossingsMap map,PlayableProfile gameplay,ThreeCrossingsMaterials surfaces,Bounds bounds,Vector3 position,float heading,IEnumerable<ThreeCrossingsDetails.Placement> existing,IEnumerable<NavPolygon> occupied)
        {
            float scale=surfaces.Profile.serviceEquipmentScale;
            var envelope=Envelope(bounds,position,scale,surfaces.Art.detailClearance,gameplay,heading);
            if(occupied.Any(o=>Overlap(envelope,o)))return "occupied";
            if(existing.Any(p=>envelope.Contains(new NavPoint(p.Position.x,p.Position.z))||envelope.BoundaryDistanceSquared(new NavPoint(p.Position.x,p.Position.z))<p.Radius*p.Radius))return "existing";
            // CompoundClear applies the authoritative .3m sample reserve plus profile
            // clearance itself. Feed bare complete geometry/projection bounds so the
            // profile clearance is applied once, not twice.
            var protectedEnvelope=Envelope(bounds,position,scale,0,gameplay,heading);
            var protectedBounds=new Bounds(new Vector3((float)((protectedEnvelope.MinX+protectedEnvelope.MaxX)/2-position.x),bounds.center.y*scale,(float)((protectedEnvelope.MinZ+protectedEnvelope.MaxZ)/2-position.z)),new Vector3((float)(protectedEnvelope.MaxX-protectedEnvelope.MinX),bounds.size.y*scale,(float)(protectedEnvelope.MaxZ-protectedEnvelope.MinZ)));
            if(!ThreeCrossingsDetails.CompoundClear(map,gameplay,surfaces,protectedBounds,position,1,0))return "protected-support-projection";
            // Complete rectangle sampling against access strips; an enclosing circle
            // would reject narrow clear pockets even when every part is outside access.
            int nx=Mathf.CeilToInt(protectedBounds.size.x/.4f),nz=Mathf.CeilToInt(protectedBounds.size.z/.4f);
            for(int x=0;x<=nx;x++)for(int z=0;z<=nz;z++)
            {
                var point=new Vector2(position.x+Mathf.Lerp(protectedBounds.min.x,protectedBounds.max.x,x/(float)nx),position.z+Mathf.Lerp(protectedBounds.min.z,protectedBounds.max.z,z/(float)nz));
                if(surfaces.AccessOccupied(point,.3f))return "access";
            }
            if(!VisibleFromGameplay(map,gameplay,bounds,position,scale,heading))return "camera-occlusion";
            return null;
        }
        public static bool Clear(ThreeCrossingsMap map,PlayableProfile gameplay,ThreeCrossingsMaterials surfaces,Bounds bounds,Vector3 position,float heading,IEnumerable<ThreeCrossingsDetails.Placement> existing,IEnumerable<NavPolygon> occupied)=>Rejection(map,gameplay,surfaces,bounds,position,heading,existing,occupied)==null;
        public static List<ThreeCrossingsDetails.Placement> ReserveServicePockets(IEnumerable<ThreeCrossingsDetails.Placement> details,IEnumerable<ThreeCrossingsDetails.Placement> service,Bounds bounds,PlayableProfile gameplay,ThreeCrossingsMaterials surfaces)
        {
            var envelopes=service.Select(p=>Envelope(bounds,p.Position,p.Scale,surfaces.Art.detailClearance,gameplay,p.Rotation)).ToArray();
            // Keep every authored structure/utility/wreck. Only loose procedural scree
            // conflicting with the selected equipment bay is locally omitted.
            return details.Where(p=>p.Kind!="Boulder"||!envelopes.Any(e=>e.Contains(new NavPoint(p.Position.x,p.Position.z))||e.BoundaryDistanceSquared(new NavPoint(p.Position.x,p.Position.z))<p.Radius*p.Radius)).ToList();
        }
        public static List<ThreeCrossingsDetails.Placement> Plan(ThreeCrossingsMap map,PlayableProfile gameplay,ThreeCrossingsMaterials surfaces,IEnumerable<ThreeCrossingsDetails.Placement> existing,Bounds bounds)
        {
            var all=existing.ToArray();var result=new List<ThreeCrossingsDetails.Placement>();var occupied=new List<NavPolygon>();var art=surfaces.Profile;
            foreach(var hangar in all.Where(p=>p.Kind=="PerimeterHangar"))
            {
                if(result.Count>=art.serviceBayCount)break;
                bool placed=false;
                bool TryPosition(Vector3 position)
                {
                    position.y=(float)map.SurfaceHeight(new NavPoint(position.x,position.z));
                    // Cheap necessary center checks precede full rectangles/rays; they
                    // reject only candidates whose actual body center is protected.
                    if(!surfaces.ProtectedGround(map,gameplay,new Vector2(position.x,position.z),0))return false;
                    if(surfaces.RoadMask.GetPixelBilinear(position.x/(float)(map.HalfExtent*2)+.5f,position.z/(float)(map.HalfExtent*2)+.5f).r>.015f)return false;
                    foreach(float heading in new[]{0f,90f,180f,270f})
                    {
                        if(!Clear(map,gameplay,surfaces,bounds,position,heading,all,occupied))continue;
                        float radius=Mathf.Sqrt(Mathf.Pow(Mathf.Max(Mathf.Abs(bounds.min.x),Mathf.Abs(bounds.max.x)),2)+Mathf.Pow(Mathf.Max(Mathf.Abs(bounds.min.z),Mathf.Abs(bounds.max.z)),2))*art.serviceEquipmentScale;
                        result.Add(new ThreeCrossingsDetails.Placement(position,radius,"ServiceEquipment",heading,hangar.SiteId,art.serviceEquipmentScale));
                        occupied.Add(Envelope(bounds,position,art.serviceEquipmentScale,surfaces.Art.detailClearance,gameplay,heading));return true;
                    }
                    return false;
                }
                // Same deterministic orbit as the selected study; no simulation RNG.
                const int orbitSamples=120;
                for(int attempt=0;attempt<orbitSamples&&!placed;attempt++)
                {
                    float angle=attempt*2.4f,distance=art.serviceBayReach+Mathf.Sqrt(attempt)*art.serviceBaySpread;
                    placed=TryPosition(hangar.Position+new Vector3(Mathf.Cos(angle)*distance,0,Mathf.Sin(angle)*distance));
                }
                // A half-metre grid resolves narrow valid pockets missed by the coarse
                // orbit, within the same profile-defined extent. Search resolution is
                // a geometric solver quality bound, not an additional art control.
                float reach=art.serviceBayReach+Mathf.Sqrt(orbitSamples)*art.serviceBaySpread;
                for(float x=Mathf.Ceil((hangar.Position.x-reach)*2)*.5f;x<=hangar.Position.x+reach&&!placed;x+=.5f)for(float z=Mathf.Ceil((hangar.Position.z-reach)*2)*.5f;z<=hangar.Position.z+reach&&!placed;z+=.5f)
                    if(Mathf.Pow(x-hangar.Position.x,2)+Mathf.Pow(z-hangar.Position.z,2)<=reach*reach)placed=TryPosition(new Vector3(x,0,z));
            }
            if(art.serviceBayCount>0&&result.Count==0)throw new InvalidOperationException("No fully supported clear service equipment pocket");
            return result;
        }
    }
}
