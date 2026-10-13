using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
using UnityEngine;

namespace Spacewars.Presentation
{
    // Map-specific placement adapter; modules/materials remain reusable.
    public static class ThreeCrossingsDetails
    {
        public readonly struct Placement
        {
            public readonly Vector3 Position;
            public readonly float Radius;
            public readonly string Kind;
            public readonly float Rotation;
            public readonly int SiteId;
            public readonly float Scale;
            public Placement(Vector3 p,float r,string kind,float rotation,int siteId=0,float scale=1){Position=p;Radius=r;Kind=kind;Rotation=rotation;SiteId=siteId;Scale=scale;}
        }
        private static bool PointClear(ThreeCrossingsMap map,PlayableProfile gameplay,ThreeCrossingsMaterials surfaces,TerritorySite[] sites,float x,float z,float radius)
        {
            var art=surfaces.Art;
            var p=new NavPoint(x,z);
            if(!map.SupportsFootprint(p,radius+art.detailClearance))return false;
            if(map.Supports.Any(s=>s.Id.StartsWith("platform-")&& (s.Bounds.Contains(p)||s.Bounds.BoundaryDistanceSquared(p)<Math.Pow(radius+art.detailClearance,2))))return false;
            foreach(var s in sites)
            {
                double reserve=TerritoryRules.Radius(gameplay,s.Kind)+radius+art.detailClearance;
                if(Math.Pow(x-s.Position.X,2)+Math.Pow(z-s.Position.Z,2)<reserve*reserve)return false;
                foreach(var slot in s.Slots)if(Math.Pow(x-slot.Position.X,2)+Math.Pow(z-slot.Position.Z,2)<Math.Pow(gameplay.OrdinaryPadRadius+radius+art.detailClearance,2))return false;
            }
            // Sample a conservative enclosing ring, including road feather and bridge flares.
            for(int i=0;i<9;i++)
            {
                float a=i*Mathf.PI/4,r=i==8?0:radius+art.detailClearance;
                float u=(x+Mathf.Cos(a)*r)/(float)(map.HalfExtent*2)+.5f,v=(z+Mathf.Sin(a)*r)/(float)(map.HalfExtent*2)+.5f;
                if(surfaces.RoadMask.GetPixelBilinear(u,v).r>.015f||surfaces.ConstructionMask.GetPixelBilinear(u,v).g>.01f)return false;
            }
            return true;
        }
        public static bool TractorFootprintClear(ThreeCrossingsMap map,PlayableProfile gameplay,ThreeCrossingsMaterials surfaces,TerritorySite[] sites,Bounds tractorBounds,float x,float z,float heading)
        {

            // Conservatively sample the complete oriented bounding rectangle, including
            // empty space between axles. A circular proxy would reject safe long pockets.
            var rotation=Quaternion.Euler(0,heading,0);
            int nx=Mathf.CeilToInt(tractorBounds.size.x*surfaces.Profile.tractorScale/.4f),nz=Mathf.CeilToInt(tractorBounds.size.z*surfaces.Profile.tractorScale/.4f);
            for(int ix=0;ix<=nx;ix++)for(int iz=0;iz<=nz;iz++)
            {
                var local=new Vector3(Mathf.Lerp(tractorBounds.min.x,tractorBounds.max.x,ix/(float)nx),0,Mathf.Lerp(tractorBounds.min.z,tractorBounds.max.z,iz/(float)nz))*surfaces.Profile.tractorScale;
                var offset=rotation*local;if(!PointClear(map,gameplay,surfaces,sites,x+offset.x,z+offset.z,.3f))return false;
            }
            return true;
        }
        public static NavPolygon CompoundEnvelope(Bounds bounds,Vector3 position,float scale,float clearance,PlayableProfile camera=null,float rotation=0)
        {
            // Cardinal orientations preserve the full authored rectangle, never a circular proxy.
            var turn=Quaternion.Euler(0,rotation,0);var corners=new[]{new Vector3(bounds.min.x,0,bounds.min.z),new Vector3(bounds.min.x,0,bounds.max.z),new Vector3(bounds.max.x,0,bounds.min.z),new Vector3(bounds.max.x,0,bounds.max.z)}.Select(v=>turn*v).ToArray();
            bounds=new Bounds(new Vector3((corners.Min(v=>v.x)+corners.Max(v=>v.x))/2,bounds.center.y,(corners.Min(v=>v.z)+corners.Max(v=>v.z))/2),new Vector3(corners.Max(v=>v.x)-corners.Min(v=>v.x),bounds.size.y,corners.Max(v=>v.z)-corners.Min(v=>v.z)));
            float minX=position.x+bounds.min.x*scale-clearance,maxX=position.x+bounds.max.x*scale+clearance;
            float minZ=position.z+bounds.min.z*scale-clearance,maxZ=position.z+bounds.max.z*scale+clearance;
            if(camera!=null)
            {
                float low=position.y+bounds.min.y*scale,high=position.y+bounds.max.y*scale;
                float dx=-(float)(camera.CameraOffsetX/camera.CameraHeight),dz=-(float)(camera.CameraOffsetZ/camera.CameraHeight);
                minX+=Mathf.Min(low*dx,high*dx);maxX+=Mathf.Max(low*dx,high*dx);
                minZ+=Mathf.Min(low*dz,high*dz);maxZ+=Mathf.Max(low*dz,high*dz);
            }
            return new NavPolygon(new[]{new NavPoint(minX,minZ),new NavPoint(maxX,minZ),new NavPoint(maxX,maxZ),new NavPoint(minX,maxZ)});
        }
        public static bool CompoundClear(ThreeCrossingsMap map,PlayableProfile gameplay,ThreeCrossingsMaterials surfaces,Bounds bounds,Vector3 position,float scale,float rotation=0)
        {
            var sites=TerritoryRules.Sites(gameplay).ToArray();
            var rotationMatrix=Quaternion.Euler(0,rotation,0);
            int nx=Mathf.CeilToInt(bounds.size.x*scale/.4f),nz=Mathf.CeilToInt(bounds.size.z*scale/.4f);
            for(int ix=0;ix<=nx;ix++)for(int iz=0;iz<=nz;iz++)
            {
                var offset=rotationMatrix*new Vector3(Mathf.Lerp(bounds.min.x,bounds.max.x,ix/(float)nx)*scale,0,Mathf.Lerp(bounds.min.z,bounds.max.z,iz/(float)nz)*scale);
                float x=position.x+offset.x,z=position.z+offset.z;
                if(!PointClear(map,gameplay,surfaces,sites,x,z,.3f)||Math.Abs(map.SurfaceHeight(new NavPoint(x,z))-position.y)>.05)return false;
            }
            return true;
        }
        static bool Overlaps(NavPolygon a,NavPolygon b)=>a.MinX<b.MaxX&&a.MaxX>b.MinX&&a.MinZ<b.MaxZ&&a.MaxZ>b.MinZ;
        public static List<Placement> Plan(ThreeCrossingsMap map,PlayableProfile gameplay,ThreeCrossingsMaterials surfaces)
        {
            var art=surfaces.Art;var result=new List<Placement>();var sites=TerritoryRules.Sites(gameplay).ToArray();
            surfaces.HangarAccesses.Clear();surfaces.AccessSearchStats.Clear();
            bool Clear(float x,float z,float radius)=>PointClear(map,gameplay,surfaces,sites,x,z,radius)&&!surfaces.AccessOccupied(new Vector2(x,z),radius);

            // Authored service anchors live here, outside the reusable asset kit.
            foreach(var anchor in new[]{new Vector3(-54,0,-29),new Vector3(54,0,30),new Vector3(-12,0,16)})
            {
                string kind=anchor.z==16?"PumpNode":anchor.x<0?"VentilationBlock":"CableCabinet";
                for(int attempt=0;attempt<80;attempt++)
                {
                    float angle=attempt*2.4f,distance=Mathf.Sqrt(attempt)*.55f;
                    float x=anchor.x+Mathf.Cos(angle)*distance,z=anchor.z+Mathf.Sin(angle)*distance;
                    float radius=1.9f*art.serviceScale;
                    if(!Clear(x,z,radius))continue;
                    result.Add(new Placement(new Vector3(x,0,z),radius,kind,anchor.x<0?25:-155));break;
                }
            }
            var tractorMesh=HeavyTractorWreck.Build();var tractorBounds=tractorMesh.bounds;
            float tractorRadius=0;foreach(var v in tractorMesh.vertices)tractorRadius=Mathf.Max(tractorRadius,new Vector2(v.x,v.z).magnitude*surfaces.Profile.tractorScale);
            UnityEngine.Object.Destroy(tractorMesh);
            bool TractorClear(float x,float z,float heading)=>TractorFootprintClear(map,gameplay,surfaces,sites,tractorBounds,x,z,heading);

            foreach(var anchor in new[]{new Vector3(-54,165,26),new Vector3(54,345,-26)})
            {
                bool placed=false;
                // Authored poses were proven by a global scan of this exact predicate.
                // Local fallback preserves geometry and all safety reserves.
                for(int attempt=0;attempt<500;attempt++)
                {
                    float angle=attempt*2.4f,distance=Mathf.Sqrt(attempt)*.3f;
                    float x=anchor.x+Mathf.Cos(angle)*distance,z=anchor.z+Mathf.Sin(angle)*distance;
                    if(!TractorClear(x,z,anchor.y)||result.Any(p=>Vector2.Distance(new Vector2(x,z),new Vector2(p.Position.x,p.Position.z))<p.Radius+tractorRadius))continue;
                    result.Add(new Placement(new Vector3(x,(float)map.SurfaceHeight(new NavPoint(x,z)),z),tractorRadius,"HeavyTractorWreck",anchor.y));placed=true;break;
                }
                if(!placed)throw new InvalidOperationException("No safe heavy tractor pocket near "+anchor);
            }
            var compoundKinds=new[]{"PerimeterHangar","PerimeterWorkshop","PerimeterAnnex"};
            var compoundBounds=new Dictionary<string,Bounds>();
            foreach(var kind in compoundKinds){var mesh=CorporateOutpostModules.Compound(kind);compoundBounds[kind]=mesh.bounds;UnityEngine.Object.Destroy(mesh);}
            var boxCache=new Dictionary<Placement,NavPolygon>();
            NavPolygon Box(Placement p)
            {if(!boxCache.TryGetValue(p,out var box)){box=CompoundEnvelope(compoundBounds[p.Kind],p.Position,p.Scale,0,null,p.Rotation);boxCache[p]=box;}return box;}
            bool SceneryConflict(Vector3 position,float radius)
            {
                var point=new NavPoint(position.x,position.z);
                return result.Any(q=>
                {
                    if(!q.Kind.StartsWith("Perimeter"))return Vector2.Distance(new Vector2(position.x,position.z),new Vector2(q.Position.x,q.Position.z))<q.Radius+radius+art.detailClearance;
                    var box=Box(q);
                    return box.Contains(point)||box.BoundaryDistanceSquared(point)<Math.Pow(radius+art.detailClearance,2);
                });
            }
            // Cache only authoritative terrain/site/old-road validity. Dynamic compound
            // and access reservations are evaluated during each complete-layout probe.
            var mineCandidates=new Dictionary<int,Dictionary<string,List<Placement>>>();
            foreach(var access in surfaces.MineAccesses)
            {
                var site=access.Site;var origin=new Vector2((float)site.Position.X,(float)site.Position.Z);
                float bearing=Mathf.Atan2((float)access.Entry.X-origin.x,(float)access.Entry.Z-origin.y)*Mathf.Rad2Deg;
                var choices=new Dictionary<string,List<Placement>>();mineCandidates[site.Id]=choices;
                foreach(var kind in new[]{"CargoModule","ExtractionSkid"})
                {
                    var mesh=CorporateOutpostModules.Build(kind,surfaces.Profile.mineUtilityScale);float radius=CorporateOutpostModules.Radius(mesh,surfaces.Profile.industrialScale);UnityEngine.Object.Destroy(mesh);
                    var candidates=new List<Placement>();choices[kind]=candidates;
                    for(int ring=0;ring<=12;ring++)for(int sample=0;sample<72;sample++)
                    {
                        float distance=Mathf.Lerp(surfaces.Profile.mineYardRadius+radius+art.detailClearance,surfaces.Profile.industrialReach,ring/12f);
                        float angle=(bearing+180+sample*137.50776f)*Mathf.Deg2Rad;
                        float x=origin.x+Mathf.Sin(angle)*distance,z=origin.y+Mathf.Cos(angle)*distance;
                        if(!PointClear(map,gameplay,surfaces,sites,x,z,radius))continue;
                        candidates.Add(new Placement(new Vector3(x,(float)map.SurfaceHeight(new NavPoint(x,z)),z),radius,kind,bearing,site.Id,surfaces.Profile.industrialScale));
                    }
                }
            }
            bool MineLayout(bool retain)
            {
                int before=result.Count;
                foreach(var choices in mineCandidates.Values)
                {
                    bool Valid(Placement p)=>!surfaces.AccessOccupied(new Vector2(p.Position.x,p.Position.z),p.Radius)&&!SceneryConflict(p.Position,p.Radius);
                    var cargo=choices["CargoModule"].Where(Valid).ToArray();var equipment=choices["ExtractionSkid"].Where(Valid).ToArray();bool placed=false;
                    foreach(var c in cargo)
                    {
                        foreach(var e in equipment)
                        {
                            if(Vector3.Distance(c.Position,e.Position)<c.Radius+e.Radius+art.detailClearance)continue;
                            if(retain){result.Add(c);result.Add(e);}placed=true;break;
                        }
                        if(placed)break;
                    }
                    if(!placed){if(retain)result.RemoveRange(before,result.Count-before);return false;}
                }
                return true;
            }
            int groupIndex=0;
            foreach(var anchor in new[]{new Vector3(-54,0,-26),new Vector3(-30,0,56),new Vector3(30,0,-56),new Vector3(54,0,26)})
            {
                float variation=groupIndex==0?0:groupIndex==1?1:groupIndex==2?-.5f:.35f;
                float size=surfaces.Profile.edgeCompoundScale*(1+variation*surfaces.Profile.edgeCompoundVariation);
                // Solve the entire compound together. Reserving a driveway must not
                // strand its workshop/annex in an otherwise valid but too-tight pocket.
                bool PlacePart(int part,Vector3 groupAnchor)
                {
                    if(part==compoundKinds.Length)return true;
                    string kind=compoundKinds[part];var bounds=compoundBounds[kind];
                    for(int attempt=0;attempt<12000;attempt++)
                    {
                        int sample=attempt/4;float heading=(attempt%4)*90;
                        float angle=sample*2.4f,distance=Mathf.Sqrt(sample)*.3f;
                        if(part>0&&distance>surfaces.Profile.industrialReach)break;
                        var position=new Vector3(groupAnchor.x+Mathf.Cos(angle)*distance,0,groupAnchor.z+Mathf.Sin(angle)*distance);
                        if(Mathf.Max(Mathf.Abs(position.x),Mathf.Abs(position.z))<map.HalfExtent*.65f||!CompoundClear(map,gameplay,surfaces,bounds,position,size,heading))continue;
                        var envelope=CompoundEnvelope(bounds,position,size,art.detailClearance,null,heading);
                        bool accessConflict=false;
                        for(float x=(float)envelope.MinX;x<=envelope.MaxX;x+=.4f)for(float z=(float)envelope.MinZ;z<=envelope.MaxZ;z+=.4f)
                            if(surfaces.AccessOccupied(new Vector2(x,z),0))accessConflict=true;
                        if(accessConflict)continue;
                        if(result.Any(p=>p.Kind.StartsWith("Perimeter")?
                            Overlaps(envelope,Box(p)):
                            envelope.Contains(new NavPoint(p.Position.x,p.Position.z))||envelope.BoundaryDistanceSquared(new NavPoint(p.Position.x,p.Position.z))<Math.Pow(p.Radius+art.detailClearance,2)))continue;
                        // Bounds diagonal encloses the complete rotated footprint.
                        float radius=new Vector2(Mathf.Max(Mathf.Abs(bounds.min.x),Mathf.Abs(bounds.max.x)),Mathf.Max(Mathf.Abs(bounds.min.z),Mathf.Abs(bounds.max.z))).magnitude*size;
                        var candidate=new Placement(position,radius,kind,heading,-1-groupIndex,size);
                        if(part==0)
                        {if(!surfaces.TryHangarAccess(map,gameplay,candidate,bounds,result,out var access))continue;surfaces.HangarAccesses.Add(access);}
                        result.Add(candidate);
                        // Keep enough real protected space for every existing mine module.
                        // Reject this compound pose before it consumes an irreplaceable pocket.
                        if(!MineLayout(false))
                        {result.RemoveAt(result.Count-1);if(part==0)surfaces.HangarAccesses.RemoveAt(surfaces.HangarAccesses.Count-1);continue;}
                        if(PlacePart(part+1,part==0?position:groupAnchor))return true;
                        result.RemoveAt(result.Count-1);
                        if(part==0)surfaces.HangarAccesses.RemoveAt(surfaces.HangarAccesses.Count-1);
                    }
                    return false;
                }
                if(!PlacePart(0,anchor))throw new InvalidOperationException("No fully serviced ground compound at "+anchor+"; access attempts "+string.Join(",",surfaces.AccessSearchStats.Select(p=>p.Key+"="+p.Value)));
                groupIndex++;
            }
            if(!MineLayout(true))throw new InvalidOperationException("Industrial layout lost its protected service pockets");
            var rocks=map.Solids.Where(s=>s.Top>=map.WaterDepth&&s.Bottom<=0).ToArray();
            var random=new System.Random(307); // Stable authored dressing seed; never reads simulation RNG.
            float Next()=> (float)random.NextDouble();
            var clusters=new List<Vector2>();
            float FootDistance(float x,float z)
            {
                var point=new NavPoint(x,z);
                return (float)Math.Sqrt(rocks.Min(r=>r.Contains(point)?0:r.BoundaryDistanceSquared(point)));
            }
            bool Place(float x,float z,float radius)
            {
                if(!Clear(x,z,radius))return false;
                if(result.Any(p=>Vector2.Distance(new Vector2(p.Position.x,p.Position.z),new Vector2(x,z))<p.Radius+radius))return false;
                result.Add(new Placement(new Vector3(x,0,z),radius,"Boulder",Next()*360));return true;
            }
            // Fixed search budgets and random seed only resolve the authored decoration;
            // spatial scale, density and stone size remain controlled by the art profile.
            for(int i=0;i<9000;i++)
            {
                float x=(Next()*2-1)*(float)map.HalfExtent,z=(Next()*2-1)*(float)map.HalfExtent;
                float distance=FootDistance(x,z);
                if(distance<art.boulderRadius||distance>art.screeReach*.5f)continue;
                var center=new Vector2(x,z);
                if(clusters.Any(c=>Vector2.Distance(c,center)<art.screeClusterSpacing))continue;
                if(Next()>art.screeDensity||!Place(x,z,art.boulderRadius))continue;
                clusters.Add(center);
                // Larger fragments first, then a denser fan of smaller pieces. No uniform
                // background scatter: every small stone belongs to a selected cliff foot.
                for(int j=0;j<160;j++)
                {
                    float angle=Next()*Mathf.PI*2,spread=Mathf.Sqrt(Next())*art.screeReach;
                    float px=x+Mathf.Cos(angle)*spread,pz=z+Mathf.Sin(angle)*spread;
                    float foot=FootDistance(px,pz);
                    if(foot<=0||foot>art.screeReach||Next()>art.screeDensity*(1-spread/art.screeReach))continue;
                    float hierarchy=j<24?Mathf.Lerp(.5f,.85f,Next()):Mathf.Lerp(0,.4f,Next());
                    float radius=Mathf.Lerp(art.pebbleRadius,art.boulderRadius,hierarchy)*(1-.25f*spread/art.screeReach);
                    Place(px,pz,radius);
                }
            }
            return result;
        }
        public static Mesh Build(IEnumerable<Placement> placements,EnvironmentArtProfile art,bool stones,bool tractors=false,float tractorScale=1,bool corporate=false,float industrialScale=1,bool compounds=false,float mineUtilityScale=0)
        {
            var b=new EnvironmentDetails.Builder();var cache=new Dictionary<string,Mesh>();int index=0;
            foreach(var p in placements)
            {
                if((p.Kind=="Boulder")!=stones||(p.Kind=="HeavyTractorWreck")!=tractors||CorporateOutpostModules.IsModule(p.Kind)!=corporate||p.Kind.StartsWith("Perimeter")!=compounds)continue;
                string key=stones?"Boulder"+(index++%3):p.Kind;
                if(!cache.TryGetValue(key,out var mesh)){mesh=stones?EnvironmentDetails.Boulder(index%3):tractors?HeavyTractorWreck.Build():corporate?CorporateOutpostModules.Build(p.Kind,mineUtilityScale):compounds?CorporateOutpostModules.Compound(p.Kind):CorporateOutpostModules.Utility(p.Kind);cache[key]=mesh;}
                float size=stones?p.Radius:tractors?tractorScale:corporate?industrialScale:compounds?p.Scale:art.serviceScale;
                b.Append(mesh,Matrix4x4.TRS(p.Position,Quaternion.Euler(0,p.Rotation,0),Vector3.one*size));
            }
            foreach(var mesh in cache.Values)UnityEngine.Object.Destroy(mesh);
            return b.Mesh(stones?"Clustered geological debris":"Outpost service infrastructure");
        }
    }
}
