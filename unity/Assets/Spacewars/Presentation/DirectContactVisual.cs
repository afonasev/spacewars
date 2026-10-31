using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Spacewars.Presentation
{
    // Presentation-only ray/triangle intersection. Never adds colliders or changes hit tests.
    internal sealed class DirectContactSurface
    {
        private sealed class Geometry
        {
            internal readonly Vector3[] Vertices;
            internal readonly int[] Triangles;
            readonly int[] order;
            readonly Node[] nodes;
            struct Node { internal Bounds Bounds;internal int Start,Count,Left,Right; }
            sealed class CenterOrder : IComparer<int>
            {
                readonly Vector3[] centers;readonly int axis;
                internal CenterOrder(Vector3[] centers,int axis){this.centers=centers;this.axis=axis;}
                public int Compare(int a,int b){int comparison=centers[a][axis].CompareTo(centers[b][axis]);return comparison!=0?comparison:a.CompareTo(b);}
            }
            internal Geometry(Mesh mesh)
            {
                Vertices=mesh.vertices;Triangles=mesh.triangles;
                int count=Triangles.Length/3;order=new int[count];var bounds=new Bounds[count];var centers=new Vector3[count];
                for(int i=0;i<count;i++){
                    order[i]=i;var a=Vertices[Triangles[i*3]];var b=Vertices[Triangles[i*3+1]];var c=Vertices[Triangles[i*3+2]];
                    var min=Vector3.Min(a,Vector3.Min(b,c));var max=Vector3.Max(a,Vector3.Max(b,c));
                    // Broad phase is deliberately conservative, including planar/grazing faces.
                    // Padding only admits extra exact tests; it never shifts a contact point.
                    float padding=Mathf.Max(1e-5f,Mathf.Max(min.magnitude,max.magnitude)*1e-5f);
                    bounds[i].SetMinMax(min-Vector3.one*padding,max+Vector3.one*padding);centers[i]=(min+max)*.5f;
                }
                var list=new List<Node>();var comparers=new[]{new CenterOrder(centers,0),new CenterOrder(centers,1),new CenterOrder(centers,2)};
                if(count>0)Build(0,count,bounds,comparers,list);nodes=list.ToArray();
            }
            int Build(int start,int count,Bounds[] bounds,CenterOrder[] comparers,List<Node> list)
            {
                var box=bounds[order[start]];for(int i=start+1;i<start+count;i++)box.Encapsulate(bounds[order[i]]);
                int index=list.Count;list.Add(default);
                // Eight triangles is an internal search leaf size; it cannot affect output fidelity.
                if(count<=8){list[index]=new Node{Bounds=box,Start=start,Count=count};return index;}
                var size=box.size;int axis=size.x>=size.y&&size.x>=size.z?0:size.y>=size.z?1:2;
                Array.Sort(order,start,count,comparers[axis]);int half=count/2;
                int left=Build(start,half,bounds,comparers,list),right=Build(start+half,count-half,bounds,comparers,list);
                list[index]=new Node{Bounds=box,Left=left,Right=right};return index;
            }
            internal void Candidates(Ray ray,List<int> result)
            {
                result.Clear();if(nodes.Length>0)Visit(0,ray,result);
                // Preserve the original nearest-hit/tie order and exact triangle arithmetic.
                result.Sort();
            }
            void Visit(int index,Ray ray,List<int> result)
            {
                var node=nodes[index];if(!node.Bounds.IntersectRay(ray))return;
                if(node.Count>0){for(int i=node.Start;i<node.Start+node.Count;i++)result.Add(order[i]*3);}
                else{Visit(node.Left,ray,result);Visit(node.Right,ray,result);}
            }
        }
        private readonly Dictionary<Mesh,Geometry> geometry=new Dictionary<Mesh,Geometry>();
        readonly List<int> candidates=new List<int>();
        internal void Clear(){geometry.Clear();candidates.Clear();}
        internal System.Collections.IEnumerator Prepare(IEnumerable<Transform> hulls)
        {
            var meshes=new HashSet<Mesh>();
            foreach(var hull in hulls)if(hull)foreach(var part in hull.GetComponentsInChildren<MeshFilter>(true))
                if(part.sharedMesh&&part.sharedMesh.isReadable)meshes.Add(part.sharedMesh);
            foreach(var mesh in meshes){if(!geometry.ContainsKey(mesh))geometry.Add(mesh,new Geometry(mesh));yield return null;}
        }
        internal Vector3 Resolve(Transform hull,Vector3 from,Vector3 fallback)
        {
            if(!hull)return fallback;
            return Resolve(hull.GetComponentsInChildren<MeshFilter>(),from,fallback,true,false)??fallback;
        }
        internal Vector3 ResolveRocket(Transform hull,Vector3 from,Vector3 fallback)
        {
            if(!hull)return fallback;
            var parts=hull.GetComponentsInChildren<MeshFilter>();
            return Resolve(parts,from,fallback,true,false)??Resolve(parts,from,fallback,false,true)??fallback;
        }
        internal Vector3 ResolveObstacle(Transform root,IEnumerable<Transform> actors,Vector3 from,Vector3 fallback,float extension)
        {
            var excluded=actors.ToArray();
            var parts=root.GetComponentsInChildren<MeshFilter>().Where(p=>!excluded.Any(a=>p.transform.IsChildOf(a))).ToArray();
            return Resolve(parts,from,fallback,true,false,Vector3.Distance(from,fallback)+extension)??fallback;
        }
        private Vector3? Resolve(MeshFilter[] parts,Vector3 from,Vector3 fallback,bool alongFlight,bool boundsFallback,float maximumDistance=float.PositiveInfinity)
        {
            var bounds=new Bounds();bool any=false;
            foreach(var part in parts)if(part.TryGetComponent<Renderer>(out var renderer)&&renderer.enabled)
            {if(!any){bounds=renderer.bounds;any=true;}else bounds.Encapsulate(renderer.bounds);}
            if(!any)return fallback;
            // Unguided contacts stay on the incoming ray. Only the Hawk-specific
            // fallback may seek the model centre.
            var direction=((alongFlight?fallback:bounds.center)-from).normalized;
            if(direction.sqrMagnitude<Mathf.Epsilon)return alongFlight?fallback:bounds.center;
            float best=float.PositiveInfinity;Vector3 point=fallback;
            foreach(var part in parts)
            {
                var mesh=part.sharedMesh;if(!mesh||!mesh.isReadable||!part.TryGetComponent<Renderer>(out var renderer)||!renderer.enabled)continue;
                var localFrom=part.transform.InverseTransformPoint(from);
                var localDirection=part.transform.InverseTransformVector(direction);
                if(!mesh.bounds.IntersectRay(new Ray(localFrom,localDirection)))continue;
                if(!geometry.TryGetValue(mesh,out var data))
                {data=new Geometry(mesh);geometry.Add(mesh,data);}
                var vertices=data.Vertices;var triangles=data.Triangles;
                data.Candidates(new Ray(localFrom,localDirection),candidates);
                foreach(int i in candidates)
                {
                    Vector3 a=vertices[triangles[i]],b=vertices[triangles[i+1]],c=vertices[triangles[i+2]];
                    var e1=b-a;var e2=c-a;var cross=Vector3.Cross(localDirection,e2);float det=Vector3.Dot(e1,cross);
                    if(Math.Abs(det)<1e-8f)continue;
                    float inv=1/det;var offset=localFrom-a;float u=Vector3.Dot(offset,cross)*inv;if(u<0||u>1)continue;
                    var q=Vector3.Cross(offset,e1);float v=Vector3.Dot(localDirection,q)*inv;if(v<0||u+v>1)continue;
                    float t=Vector3.Dot(e2,q)*inv;if(t<0)continue;
                    var hit=part.transform.TransformPoint(localFrom+localDirection*t);float distance=Vector3.Distance(from,hit);
                    if(distance<=maximumDistance&&distance<best){best=distance;point=hit;}
                }
            }
            // Non-readable content still gets a visible surface endpoint; authority is unchanged.
            if(float.IsPositiveInfinity(best))
            {
                if(boundsFallback&&bounds.IntersectRay(new Ray(from,direction),out var near))return from+direction*near;
                return null;
            }
            return point;
        }
    }
    internal sealed class DirectContactVisual
    {
        internal GameObject Root,Trail;
        internal Transform Target;
        internal Vector3 LocalPoint,Point;
        internal long Tick,PresentedTick;
        internal float Scale;
    }
}
