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
            public Placement(Vector3 p,float r,string kind,float rotation){Position=p;Radius=r;Kind=kind;Rotation=rotation;}
        }
        public static List<Placement> Plan(ThreeCrossingsMap map,PlayableProfile gameplay,ThreeCrossingsMaterials surfaces)
        {
            var art=surfaces.Art;var result=new List<Placement>();var sites=TerritoryRules.Sites(gameplay).ToArray();
            bool Clear(float x,float z,float radius)
            {
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
                    if(surfaces.RoadMask.GetPixelBilinear((x+Mathf.Cos(a)*r)/(float)(map.HalfExtent*2)+.5f,(z+Mathf.Sin(a)*r)/(float)(map.HalfExtent*2)+.5f).r>.015f)return false;
                }
                return true;
            }
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
        public static Mesh Build(IEnumerable<Placement> placements,EnvironmentArtProfile art,bool stones)
        {
            var b=new EnvironmentDetails.Builder();var cache=new Dictionary<string,Mesh>();int index=0;
            foreach(var p in placements)
            {
                if((p.Kind=="Boulder")!=stones)continue;
                string key=stones?"Boulder"+(index++%3):p.Kind;
                if(!cache.TryGetValue(key,out var mesh)){mesh=stones?EnvironmentDetails.Boulder(index%3):EnvironmentDetails.Service(p.Kind);cache[key]=mesh;}
                float size=stones?p.Radius:art.serviceScale;
                b.Append(mesh,Matrix4x4.TRS(p.Position,Quaternion.Euler(0,p.Rotation,0),Vector3.one*size));
            }
            foreach(var mesh in cache.Values)UnityEngine.Object.Destroy(mesh);
            return b.Mesh(stones?"Clustered geological debris":"Outpost service infrastructure");
        }
    }
}
