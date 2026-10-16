using System.Collections.Generic;
using Spacewars.Simulation;
using UnityEngine;

namespace Spacewars.Presentation
{
    public sealed partial class PlayableWorld
    {
        private void CreateMossBorders(MossHighlandMap map,MossHighlandArtProfile art)
        {
            foreach(var mountain in map.BorderMountains)Add(mountain,false);
            foreach(var river in map.BorderRivers)Add(river,true);
            void Add(NavObstacle shape,bool river)
            {
                var polygon=shape.Footprint;
                var vertices=new List<Vector3>();var indices=new List<int>();var shore=new List<Vector2>();
                float Distance(Vector2 p)=>(float)System.Math.Sqrt(polygon.BoundaryDistanceSquared(new NavPoint(p.x,p.y)));
                Vector3 Point(Vector2 p)
                {
                    float d=Distance(p),height;
                    if(river)height=.025f+.38f*Mathf.SmoothStep(0,1,d/.3f)*(1-Mathf.SmoothStep(0,1,d/1.2f));
                    else {
                        // Authored peak anchors shape the approved four corner masses.
                        // Their relief stays below the authoritative 8m combat envelope.
                        var q=new Vector2(Mathf.Abs(p.x),Mathf.Abs(p.y));
                        float Peak(float x,float z,float radius,float h)=>h*Mathf.Max(0,1-Vector2.Distance(q,new Vector2(x,z))/radius);
                        float ridge=Mathf.Max(Peak(61,59,11,7.8f),Peak(56,54,7,6.7f));
                        ridge=Mathf.Max(ridge,Peak(61,45,10,6.8f));ridge=Mathf.Max(ridge,Peak(45,61,11,7.2f));
                        ridge=Mathf.Max(ridge,Peak(63,34,7,3.5f));ridge=Mathf.Max(ridge,Peak(34,63,7,3.8f));
                        float fracture=.78f+.22f*Mathf.PerlinNoise(p.x*.45f+37,p.y*.45f+91);
                        height=Mathf.Min((float)shape.Top,ridge*fracture)*art.mountainRelief;
                        // The outer square clips a landform rather than forcing a second
                        // inward slope. Only its walkable-facing edges taper to ground.
                        float inner=float.PositiveInfinity;
                        for(int i=0;i<polygon.Vertices.Count;i++){
                            var a=polygon.Vertices[i];var b=polygon.Vertices[(i+1)%polygon.Vertices.Count];
                            if(System.Math.Abs(a.X)==64&&a.X==b.X||System.Math.Abs(a.Z)==64&&a.Z==b.Z)continue;
                            var u=new Vector2((float)a.X,(float)a.Z);var v=new Vector2((float)b.X,(float)b.Z);
                            float t=Mathf.Clamp01(Vector2.Dot(p-u,v-u)/(v-u).sqrMagnitude);
                            inner=Mathf.Min(inner,Vector2.Distance(p,Vector2.Lerp(u,v,t)));
                        }
                        height*=Mathf.SmoothStep(0,1,inner/1.25f);
                    }
                    return new Vector3(p.x,height,p.y);
                }
                void Triangle(Vector2 a,Vector2 b,Vector2 c,int depth)
                {
                    // Uniform shared subdivision retains the exact polygon boundary;
                    // tessellation and shore relief are presentation quality constants.
                    if(depth<4){var ab=(a+b)*.5f;var bc=(b+c)*.5f;var ca=(c+a)*.5f;
                        Triangle(a,ab,ca,depth+1);Triangle(ab,b,bc,depth+1);Triangle(ca,bc,c,depth+1);Triangle(ab,bc,ca,depth+1);return;}
                    int start=vertices.Count;
                    foreach(var p in new[]{a,c,b}){vertices.Add(Point(p));shore.Add(new Vector2(Distance(p),0));indices.Add(start++);}
                }
                Vector2 Xz(int i){var p=polygon.Vertices[i];return new Vector2((float)p.X,(float)p.Z);}
                for(int i=0;i<polygon.Triangles.Count;i+=3)Triangle(Xz(polygon.Triangles[i]),Xz(polygon.Triangles[i+1]),Xz(polygon.Triangles[i+2]),0);
                var mesh=new Mesh{name=river?"Authored river and bank":"Authored mountain shoulder",indexFormat=UnityEngine.Rendering.IndexFormat.UInt32};
                mesh.SetVertices(vertices);mesh.SetTriangles(indices,0);mesh.SetUVs(0,shore);mesh.RecalculateNormals();mesh.RecalculateBounds();terrainMeshes.Add(mesh);
                var obj=new GameObject(river?"Moss border river":"Moss border mountain");obj.transform.SetParent(root,false);
                obj.AddComponent<MeshFilter>().sharedMesh=mesh;var renderer=obj.AddComponent<MeshRenderer>();renderer.sharedMaterial=mossTerrainMaterial;
                var properties=new MaterialPropertyBlock();properties.SetFloat("_BorderSurface",river?2:1);renderer.SetPropertyBlock(properties);
                if(river)renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }
    }
}
