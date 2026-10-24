using System;
using System.Collections.Generic;
using UnityEngine;

namespace Spacewars.Presentation
{
    // Presentation-only ray/triangle intersection. Never adds colliders or changes hit tests.
    internal sealed class DirectContactSurface
    {
        private sealed class Geometry { internal Vector3[] Vertices;internal int[] Triangles; }
        private readonly Dictionary<Mesh,Geometry> geometry=new Dictionary<Mesh,Geometry>();
        internal void Clear()=>geometry.Clear();
        internal Vector3 Resolve(Transform hull,Vector3 from,Vector3 fallback)
        {
            if(!hull)return fallback;
            var parts=hull.GetComponentsInChildren<MeshFilter>();
            var bounds=new Bounds();bool any=false;
            foreach(var part in parts)if(part.TryGetComponent<Renderer>(out var renderer)&&renderer.enabled)
            {if(!any){bounds=renderer.bounds;any=true;}else bounds.Encapsulate(renderer.bounds);}
            if(!any)return fallback;
            // Physical shots use an abstract collision-height plane. Finish the visual
            // segment at the actual armour rather than letting it pass above a low hull.
            var direction=(bounds.center-from).normalized;
            if(direction.sqrMagnitude<Mathf.Epsilon)return bounds.center;
            float best=float.PositiveInfinity;Vector3 point=fallback;
            foreach(var part in parts)
            {
                var mesh=part.sharedMesh;if(!mesh||!mesh.isReadable||!part.TryGetComponent<Renderer>(out var renderer)||!renderer.enabled)continue;
                var localFrom=part.transform.InverseTransformPoint(from);
                var localDirection=part.transform.InverseTransformVector(direction);
                if(!mesh.bounds.IntersectRay(new Ray(localFrom,localDirection)))continue;
                if(!geometry.TryGetValue(mesh,out var data))
                {data=new Geometry{Vertices=mesh.vertices,Triangles=mesh.triangles};geometry.Add(mesh,data);}
                var vertices=data.Vertices;var triangles=data.Triangles;
                for(int i=0;i<triangles.Length;i+=3)
                {
                    Vector3 a=vertices[triangles[i]],b=vertices[triangles[i+1]],c=vertices[triangles[i+2]];
                    var e1=b-a;var e2=c-a;var cross=Vector3.Cross(localDirection,e2);float det=Vector3.Dot(e1,cross);
                    if(Math.Abs(det)<1e-8f)continue;
                    float inv=1/det;var offset=localFrom-a;float u=Vector3.Dot(offset,cross)*inv;if(u<0||u>1)continue;
                    var q=Vector3.Cross(offset,e1);float v=Vector3.Dot(localDirection,q)*inv;if(v<0||u+v>1)continue;
                    float t=Vector3.Dot(e2,q)*inv;if(t<0)continue;
                    var hit=part.transform.TransformPoint(localFrom+localDirection*t);float distance=Vector3.Distance(from,hit);
                    if(distance<best){best=distance;point=hit;}
                }
            }
            // Non-readable content still gets a visible surface endpoint; authority is unchanged.
            if(float.IsPositiveInfinity(best)&&bounds.IntersectRay(new Ray(from,direction),out var near))point=from+direction*near;
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
