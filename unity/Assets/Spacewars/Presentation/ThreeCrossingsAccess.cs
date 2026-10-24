using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
using UnityEngine;

namespace Spacewars.Presentation
{
    public readonly struct HangarAccess
    {
        public readonly int Group;
        public readonly Vector2 Gate,Direction;
        public Vector2 Right=>new Vector2(-Direction.y,Direction.x);
        public readonly Vector2[] Points;
        public HangarAccess(int group,Vector2 gate,Vector2 direction,Vector2[] points){Group=group;Gate=gate;Direction=direction;Points=points;}
        public IEnumerable<ThreeCrossingsMaterials.Segment> Segments
        {
            get{double phase=0;for(int i=1;i<Points.Length;i++)
            {var a=Points[i-1];var b=Points[i];yield return new ThreeCrossingsMaterials.Segment(new NavPoint(a.x,a.y),new NavPoint(b.x,b.y),phase);phase+=Vector2.Distance(a,b);}}
        }
    }
    public sealed partial class ThreeCrossingsMaterials
    {
        internal readonly Dictionary<string,int> AccessSearchStats=new Dictionary<string,int>();
        static Vector2 Closest(Vector2 p,Segment s)
        {
            var a=new Vector2((float)s.A.X,(float)s.A.Z);var b=new Vector2((float)s.B.X,(float)s.B.Z);var d=b-a;
            return a+d*Mathf.Clamp01(Vector2.Dot(p-a,d)/d.sqrMagnitude);
        }
        public bool ProtectedGround(ThreeCrossingsMap map,PlayableProfile gameplay,Vector2 point,float radius)
            =>ProtectedGround(map,gameplay,point,radius,TerritoryRules.Sites(gameplay));
        private bool ProtectedGround(ThreeCrossingsMap map,PlayableProfile gameplay,Vector2 point,float radius,TerritorySite[] sites)
        {
            var p=new NavPoint(point.x,point.y);float reserve=radius+Art.detailClearance;
            if(!map.SupportsFootprint(p,reserve)||Math.Abs(map.SurfaceHeight(p))>.05)return false;
            foreach(var platform in map.Supports.Where(s=>s.Id.StartsWith("platform-")))if(platform.Bounds.Contains(p)||platform.Bounds.BoundaryDistanceSquared(p)<reserve*reserve)return false;
            foreach(var site in sites)
            {
                if(Vector2.Distance(point,new Vector2((float)site.Position.X,(float)site.Position.Z))<TerritoryRules.Radius(gameplay,site.Kind)+reserve)return false;
                foreach(var slot in site.Slots)if(Vector2.Distance(point,new Vector2((float)slot.Position.X,(float)slot.Position.Z))<gameplay.OrdinaryPadRadius+reserve)return false;
            }
            return ConstructionMask.GetPixelBilinear(point.x/(float)(map.HalfExtent*2)+.5f,point.y/(float)(map.HalfExtent*2)+.5f).g<.01f;
        }
        public bool AccessOccupied(Vector2 point,float radius)
        {
            foreach(var access in HangarAccesses)
            {
                // The loading rectangle ends at the gate; roads may join existing roads.
                float across=Vector2.Dot(point-access.Gate,access.Right),along=Vector2.Dot(point-access.Gate,access.Direction);
                if(Mathf.Abs(across)<Profile.hangarApronWidth/2+radius+Art.detailClearance&&along>-radius-Art.detailClearance&&along<Profile.hangarApronLength+radius+Art.detailClearance)return true;
                foreach(var s in access.Segments)if(Vector2.Distance(point,Closest(point,s))<Profile.hangarAccessWidth/2+radius+Art.detailClearance)return true;
            }
            return false;
        }
        public bool TryHangarAccess(ThreeCrossingsMap map,PlayableProfile gameplay,ThreeCrossingsDetails.Placement hangar,Bounds bounds,IEnumerable<ThreeCrossingsDetails.Placement> placed,out HangarAccess access)
        {
            access=default;var items=placed.ToArray();var sites=TerritoryRules.Sites(gameplay);
            bool Fail(string reason){string key=hangar.SiteId+":"+reason;AccessSearchStats[key]=AccessSearchStats.TryGetValue(key,out var n)?n+1:1;return false;}
            var turn=Quaternion.Euler(0,hangar.Rotation,0);var localGate=turn*new Vector3(0,0,bounds.min.z*hangar.Scale);var localForward=turn*Vector3.back;
            var direction=new Vector2(localForward.x,localForward.z);var right=new Vector2(-direction.y,direction.x);
            var gate=new Vector2(hangar.Position.x+localGate.x,hangar.Position.z+localGate.z);
            var start=gate+direction*Profile.hangarApronLength;float radius=Profile.hangarAccessWidth/2;
            var boxes=new Dictionary<int,NavPolygon>();
            for(int i=0;i<items.Length;i++)if(items[i].Kind.StartsWith("Perimeter"))
            {var mesh=CorporateOutpostModules.Compound(items[i].Kind);boxes[i]=ThreeCrossingsDetails.CompoundEnvelope(mesh.bounds,items[i].Position,items[i].Scale,0,null,items[i].Rotation);UnityEngine.Object.Destroy(mesh);}
            var own=ThreeCrossingsDetails.CompoundEnvelope(bounds,hangar.Position,hangar.Scale,0,null,hangar.Rotation);
            bool Clear(Vector2 p,float r)
            {
                if(!ProtectedGround(map,gameplay,p,r,sites))return false;
                for(int index=0;index<items.Length;index++)
                {var item=items[index];
                    if(item.Kind.StartsWith("Perimeter"))
                    {
                        var box=boxes[index];var point=new NavPoint(p.x,p.y);
                        if(box.Contains(point)||box.BoundaryDistanceSquared(point)<Math.Pow(r+Art.detailClearance,2))return false;
                    }
                    else if(Vector2.Distance(p,new Vector2(item.Position.x,item.Position.z))<item.Radius+r+Art.detailClearance)return false;
                }
                return true;
            }
            // Entire apron rectangle is sampled, preserving the original terrain/site clearance.
            // Fixed half-metre resolution is a placement solver invariant, not an art control.
            int apronX=Mathf.CeilToInt(Profile.hangarApronWidth/.25f),apronZ=Mathf.CeilToInt(Profile.hangarApronLength/.25f);
            for(int ix=0;ix<=apronX;ix++)for(int iz=0;iz<=apronZ;iz++)
                {
                var point=gate+right*Mathf.Lerp(-Profile.hangarApronWidth/2,Profile.hangarApronWidth/2,ix/(float)apronX)+direction*(Profile.hangarApronLength*iz/apronZ);
                if(!Clear(point,0))return Fail("apron");
            }

            bool Line(Vector2 a,Vector2 b)
            {
                if(!map.SupportsSweep(new NavPoint(a.x,a.y),new NavPoint(b.x,b.y),radius+Art.detailClearance))return false;
                int n=Mathf.CeilToInt(Vector2.Distance(a,b)/.5f);
                for(int i=0;i<=n;i++)
                {var p=Vector2.Lerp(a,b,n==0?0:i/(float)n);var point=new NavPoint(p.x,p.y);
                    if(!Clear(p,radius)||own.Contains(point)||own.BoundaryDistanceSquared(point)<Math.Pow(radius+Art.detailClearance,2))return false;}

                return true;
            }
            if(!Clear(start,radius))return Fail("start");
            var roads=Segments(Profile).ToArray();
            // A nearest projection can lie inside a protected base/ramp. Include supported
            // points along each road rather than incorrectly rejecting the entire segment.
            var candidates=new List<Vector2>();
            foreach(var road in roads)
            {
                candidates.Add(Closest(start,road));var a=new Vector2((float)road.A.X,(float)road.A.Z);var b=new Vector2((float)road.B.X,(float)road.B.Z);
                int count=Mathf.CeilToInt(Vector2.Distance(a,b)/2);
                for(int i=0;i<=count;i++)candidates.Add(Vector2.Lerp(a,b,i/(float)count));
            }
            var targets=candidates.Distinct().Where(p=>Vector2.Distance(start,p)<Profile.hangarAccessReach&&Clear(p,radius)).OrderBy(p=>Vector2.Distance(start,p)).ToArray();
            foreach(var target in targets)if(Profile.hangarApronLength+Vector2.Distance(start,target)<=Profile.hangarAccessReach&&Line(start,target)){access=new HangarAccess(hangar.SiteId,gate,direction,new[]{gate,start,target});return true;}
            // Breadth-first local supported lattice, then line-of-sight simplification.
            // Fixed one-metre nodes/search budget only resolve geometry; reach is an art control.
            var queue=new Queue<Vector2Int>();var parents=new Dictionary<Vector2Int,Vector2Int>();var origin=Vector2Int.zero;queue.Enqueue(origin);parents[origin]=origin;
            Vector2 At(Vector2Int node)=>start+new Vector2(node.x,node.y);
            while(queue.Count>0&&parents.Count<5000)
            {
                var node=queue.Dequeue();var p=At(node);
                foreach(var target in targets)
                {
                    if(Vector2.Distance(p,target)>2||!Line(p,target))continue;
                    var reverse=new List<Vector2>{target,p};var current=node;
                    while(current!=origin){current=parents[current];reverse.Add(At(current));}reverse.Reverse();
                    var path=new List<Vector2>{gate,start};int index=0;
                    while(index<reverse.Count-1){int next=reverse.Count-1;while(next>index+1&&!Line(reverse[index],reverse[next]))next--;path.Add(reverse[next]);index=next;}
                    float length=0;for(int i=1;i<path.Count;i++)length+=Vector2.Distance(path[i-1],path[i]);
                    if(length>Profile.hangarAccessReach)continue;
                    access=new HangarAccess(hangar.SiteId,gate,direction,path.ToArray());return true;
                }
                foreach(var step in new[]{Vector2Int.left,Vector2Int.right,Vector2Int.up,Vector2Int.down})
                {
                    var next=node+step;if(parents.ContainsKey(next)||next.sqrMagnitude>Profile.hangarAccessReach*Profile.hangarAccessReach||!Line(p,At(next)))continue;
                    parents[next]=node;queue.Enqueue(next);
                }
            }
            return Fail("route");
        }
        public void PaintHangarAccesses(ThreeCrossingsMap map,Material material)=>LoadingPreparation.Drain(PrepareHangarAccesses(map,material));
        public System.Collections.IEnumerator PrepareHangarAccesses(ThreeCrossingsMap map,Material material)
        {
            var budget=new LoadingFrameBudget();
            var roads=RoadMask.GetPixels();var construction=ConstructionMask.GetPixels();float extent=(float)map.HalfExtent*2;
            for(int y=0;y<MaskSize;y++)for(int x=0;x<MaskSize;x++)
            {
                if(x==0&&budget.Expired){yield return null;budget.Restart();}
                var p=new Vector2(((x+.5f)/MaskSize-.5f)*extent,((y+.5f)/MaskSize-.5f)*extent);int index=y*MaskSize+x;
                foreach(var access in HangarAccesses)
                {
                    float across=Vector2.Dot(p-access.Gate,access.Right),along=Vector2.Dot(p-access.Gate,access.Direction);
                    float apron=Mathf.Min(Profile.hangarApronWidth/2-Mathf.Abs(across),Mathf.Min(along,Profile.hangarApronLength-along));
                    float fill=Mathf.SmoothStep(0,1,Mathf.Clamp01(apron/.35f));
                    if(fill>0)
                    {
                        var c=construction[index];c.g=Mathf.Max(c.g,fill);c.b=Mathf.Max(c.b,fill*(1-Mathf.SmoothStep(0,.5f,apron)));
                        c.a=Mathf.Max(c.a,fill*Mathf.Exp(-Mathf.Pow((Mathf.Abs(across)-Profile.truckWheelSpacing/2)/Profile.truckTrackWidth,2)));construction[index]=c;
                    }
                    foreach(var s in access.Segments)
                    {
                        var nearest=Closest(p,s);float spur=Mathf.SmoothStep(0,1,Mathf.Clamp01((Profile.hangarAccessWidth/2-Vector2.Distance(p,nearest))/.5f));
                        // Stop paint at the gate, rather than allowing a round end-cap under the hangar.
                        if(s.A.X==access.Gate.x&&s.A.Z==access.Gate.y&&along<0)spur=0;
                        roads[index].r=Mathf.Max(roads[index].r,spur);
                    }
                }
            }
            RoadMask.SetPixels(roads);RoadMask.Apply(false,false);ConstructionMask.SetPixels(construction);ConstructionMask.Apply(false,false);
            material.SetTexture("_RoadMask",RoadMask);material.SetTexture("_ConstructionMask",ConstructionMask);
            yield break;
        }
    }
}
