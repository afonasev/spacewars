using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
using UnityEngine;

namespace Spacewars.Presentation
{
    [Serializable] public sealed class NullDockMachineMetrics
    {
        public string name;
        public int[] vertices,triangles;
        public long[] meshBytes;
    }

    public sealed partial class PlayableWorld
    {
        public readonly List<NullDockMachineMetrics> DockMachineMetrics=new List<NullDockMachineMetrics>();
        private static readonly Color FieldGraphite=new Color(.13f,.17f,.21f),FieldViolet=new Color(.39f,.23f,.48f);

        private void CreateZeroFieldMachine(NullDockMap map,int index,NullDockArtProfile art)
        {
            string name=index==2?"Null field magnetic stand":"Null field cryogenic module "+(index+1);
            var tiers=new Mesh[3];
            for(int level=0;level<3;level++){
                // Close is the authored source. Only these three NEW machines get reduced tiers;
                // the existing station, decks, rails and minor modules stay on their original path.
                var batch=new DockBatch(level==0?DockMetrics:new NullDockMeshMetrics(),level);
                ZeroFieldMachine(batch,map.Modules[index],index,(float)map.ModuleHeight,art);
                tiers[level]=batch.Finish();tiers[level].name=name+" "+new[]{"close","normal","far"}[level];
                if(level>0){tiers[level].bounds=tiers[0].bounds;terrainMeshes.Add(tiers[level]);}
            }
            DockEmit(name,tiers[0],dockMaterial);
            var obj=root.Find(name).gameObject;var renderer=obj.GetComponent<MeshRenderer>();
            var detail=obj.AddComponent<ModelDetailLod>();
            detail.Configure(new Renderer[]{renderer},new[]{tiers[0]},new[]{tiers[1]},new[]{tiers[2]},tiers[0].bounds);
            sceneryDetail.Add(detail);
            DockMachineMetrics.Add(new NullDockMachineMetrics{name=name,
                vertices=tiers.Select(m=>m.vertexCount).ToArray(),triangles=tiers.Select(m=>m.triangles.Length/3).ToArray(),
                meshBytes=tiers.Select(m=>UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(m)).ToArray()});
        }

        private static void ZeroFieldMachine(DockBatch b,NavObstacle solid,int index,float height,NullDockArtProfile art)
        {
            // Authored dimensions are fixed mechanical proportions inside A04's existing solids.
            // They cannot change lane width or balance; height follows the existing map profile.
            var center=new Vector3((float)(solid.MinX+solid.MaxX)*.5f,0,(float)(solid.MinZ+solid.MaxZ)*.5f);
            NavObstacle Inset(float k)=>new NavObstacle(new NavPolygon(solid.Footprint.Vertices.Select(p=>new NavPoint(center.x+(p.X-center.x)*k,center.z+(p.Z-center.z)*k))));
            b.LocalTransform=Matrix4x4.Scale(new Vector3(1,height/6,1));
            b.Add(TerrainMesh.Prism(solid,0,.30),FieldGraphite,4);
            b.Add(TerrainMesh.Prism(Inset(.98f),.30,.65),Steel,4);
            b.Add(TerrainMesh.Prism(Inset(.94f),.65,2.65),FieldGraphite,4);
            b.Add(TerrainMesh.Prism(Inset(.95f),2.65,2.95),art.Ceramic,8);
            b.Add(TerrainMesh.Prism(Inset(.89f),2.95,3.12),FieldGraphite,4);
            // Recessed service panels articulate the load-bearing base at normal camera distance.
            // The already-minimal plinth stays exact in every tier; tiny fasteners are close-only.
            var vertices=solid.Footprint.Vertices;
            for(int edge=0;edge<vertices.Count;edge++){
                var a=V(vertices[edge],0);var end=V(vertices[(edge+1)%vertices.Count],0);var along=(end-a).normalized;
                var inward=(center-(a+end)*.5f).normalized;var outward=new Vector3(along.z,0,-along.x);
                if(Vector3.Dot(outward,inward)>0)outward=-outward;
                var facing=Quaternion.LookRotation(outward);float span=Vector3.Distance(a,end);
                float spacing=b.DetailLevel==2?6.5f:4.0f;
                for(float d=2;d<span-1.8f;d+=spacing){
                    // Mount on the inset wall, with clearance inside the authoritative outer polygon.
                    var at=center+(a+along*d-center)*.94f+outward*.10f;
                    b.Box(at+Vector3.up*1.75f,new Vector3(2.65f,1.30f,.12f),art.Ceramic*.72f,8,facing);
                    b.Box(at+Vector3.up*1.75f+outward*.08f,new Vector3(2.15f,.12f,.06f),FieldGraphite,4,facing);
                    b.Box(at+Vector3.up*2.42f,new Vector3(2.7f,.12f,.28f),Steel,4,facing);
                    if(b.DetailLevel==0)foreach(int side in new[]{-1,1})
                        b.Box(at+along*(side*1.04f)+Vector3.up*2.18f+outward*.09f,new Vector3(.12f,.12f,.06f),DarkSteel,4,facing);
                }
            }
            var q=Quaternion.Euler(0,index==2?26.565051f:index==0?135:-45,0);
            Vector3 P(float x,float y,float z)=>center+q*new Vector3(x,y,z);
            void Box(float x,float y,float z,Vector3 size,Color color,int role=4)=>b.Box(P(x,y,z),size,color,role,q);
            void Armor(float x,float y,float z,Vector3 size)=>DockBeveledBox(b,P(x,y,z),size,.22f,art.Ceramic,q,8);

            if(index==2){
                // The upper half of the core is visible between three independently braced collars.
                // Elliptic sections share analytic normals at all tessellation levels.
                FieldArc(b,P(0,3.22f,0),q,1.68f,1.62f,1.43f,1.39f,26.8f,0,Mathf.PI,art.Core,7);
                for(int side=-1;side<=1;side+=2){
                    Armor(side*3.15f,3.48f,0,new Vector3(.85f,.66f,28.4f));
                    b.Cylinder(P(side*2.74f,3.30f,-13.4f),P(side*2.74f,3.30f,13.4f),.15f,FieldViolet,4);
                }
                foreach(float z in new[]{-9.2f,0,9.2f}){
                    // Two ceramic shell segments leave a deliberate dark cap seam.
                    FieldArc(b,P(0,3.12f,z),q,2.67f,2.63f,2.12f,2.08f,2.65f,0,Mathf.PI*.47f,art.Ceramic,8);
                    FieldArc(b,P(0,3.12f,z),q,2.67f,2.63f,2.12f,2.08f,2.65f,Mathf.PI*.53f,Mathf.PI,art.Ceramic,8);
                    Box(0,5.55f,z,new Vector3(.56f,.36f,2.15f),FieldGraphite);
                    Box(0,5.75f,z,new Vector3(.25f,.06f,1.25f),FieldViolet);
                    for(int side=-1;side<=1;side+=2){
                        Armor(side*2.9f,3.8f,z,new Vector3(.95f,1.45f,3.0f));
                        Box(side*3.42f,3.8f,z,new Vector3(.12f,.6f,1.1f),FieldViolet);
                        if(b.DetailLevel<2)foreach(int end in new[]{-1,1})
                            Box(side*3.48f,4.13f,z+end*.88f,new Vector3(.08f,.12f,.15f),Steel);
                    }
                }
                foreach(int end in new[]{-1,1}){
                    Armor(0,3.68f,end*14.9f,new Vector3(5.6f,2.4f,2.8f));
                    Box(0,4.91f,end*14.9f,new Vector3(2.6f,.08f,1.4f),FieldGraphite);
                    Box(0,4.96f,end*14.9f,new Vector3(1.7f,.04f,.28f),FieldViolet);
                    Box(0,3.4f,end*16.34f,new Vector3(2.4f,.66f,.10f),DarkSteel);
                    if(b.DetailLevel<2)for(int vent=-3;vent<=3;vent++)
                        Box(vent*.28f,4.97f,end*14.35f,new Vector3(.12f,.05f,.36f),Steel);
                }
            }else{
                // Paired longitudinal refrigeration beds replace the generic fan roof.
                Armor(0,3.45f,0,new Vector3(12.0f,.7f,21.0f));
                foreach(int side in new[]{-1,1}){
                    Box(side*2.95f,3.94f,0,new Vector3(4.85f,.30f,18.8f),FieldGraphite);
                    int fins=b.DetailLevel==0?28:b.DetailLevel==1?14:7;
                    for(int k=0;k<fins;k++){
                        float z=-9.1f+(k+.5f)*18.2f/fins;
                        Box(side*2.95f,4.43f,z,new Vector3(4.5f,.75f,18.2f/fins*.27f),Steel);
                    }
                    // All tiers retain the same silhouette rails and end headers.
                    Armor(side*5.62f,4.29f,0,new Vector3(.68f,1.25f,18.4f));
                    b.Cylinder(P(side*6.48f,3.52f,-7.8f),P(side*6.48f,3.52f,7.8f),.24f,FieldViolet,4);
                    foreach(int end in new[]{-1,1}){
                        Armor(side*2.95f,4.02f,end*10.1f,new Vector3(4.9f,1.25f,1.45f));
                        Box(side*2.95f,4.67f,end*10.1f,new Vector3(1.1f,.06f,.35f),Cyan*.55f,5);
                        if(b.DetailLevel<2)foreach(int socket in new[]{-1,1}){
                            Box(side*2.95f+socket*1.45f,4.68f,end*10.1f,new Vector3(.5f,.07f,.68f),FieldGraphite);
                            Box(side*2.95f+socket*1.45f,4.73f,end*10.1f,new Vector3(.22f,.04f,.40f),Steel);
                        }
                        b.Cylinder(P(side*6.48f,3.52f,end*7.8f),P(side*5.9f,3.52f,end*8.2f),.32f,Steel,4);
                    }
                    int clips=b.DetailLevel==0?5:b.DetailLevel==1?3:2;
                    for(int k=0;k<clips;k++)Box(side*6.48f,3.55f,Mathf.Lerp(-7,7,(float)k/(clips-1)),new Vector3(.65f,.65f,.28f),FieldGraphite);
                }
                Armor(0,4.35f,0,new Vector3(.7f,1.35f,20.4f));
            }
            b.LocalTransform=Matrix4x4.identity;
        }

        private static void FieldArc(DockBatch b,Vector3 at,Quaternion q,float rx,float ry,float ix,float iy,float length,float start,float end,Color color,int role)
        {
            int segments=b.DetailLevel==0?24:b.DetailLevel==1?12:6;
            var vertices=new List<Vector3>();var normals=new List<Vector3>();var uv=new List<Vector2>();var triangles=new List<int>();
            Vector3 P(float x,float y,float z)=>new Vector3(x,y,z);
            void Quad(Vector3 a,Vector3 bb,Vector3 c,Vector3 d,Vector3 na,Vector3 nb,Vector3 nc,Vector3 nd){
                // Analytic normals keep the same smooth shading as tessellation decreases.
                int n=vertices.Count;vertices.AddRange(new[]{a,bb,c,d});normals.AddRange(new[]{na,nb,nc,nd});
                uv.AddRange(new[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up});
                bool reverse=Vector3.Dot(Vector3.Cross(bb-a,c-a),na+nb+nc)<0;
                triangles.AddRange(reverse?new[]{n,n+2,n+1,n,n+3,n+2}:new[]{n,n+1,n+2,n,n+2,n+3});
            }
            float half=length*.5f;
            for(int k=0;k<segments;k++){
                float a=Mathf.Lerp(start,end,(float)k/segments),z=Mathf.Lerp(start,end,(float)(k+1)/segments);
                Vector3 O(float angle,float depth)=>P(rx*Mathf.Cos(angle),ry*Mathf.Sin(angle),depth);
                Vector3 I(float angle,float depth)=>P(ix*Mathf.Cos(angle),iy*Mathf.Sin(angle),depth);
                Vector3 N(float angle,float x,float y)=>P(Mathf.Cos(angle)/x,Mathf.Sin(angle)/y,0).normalized;
                var na=N(a,rx,ry);var nz=N(z,rx,ry);
                Quad(O(a,-half),O(z,-half),O(z,half),O(a,half),na,nz,nz,na);
                na=-N(a,ix,iy);nz=-N(z,ix,iy);
                Quad(I(a,-half),I(z,-half),I(z,half),I(a,half),na,nz,nz,na);
                foreach(int side in new[]{-1,1}){var normal=Vector3.forward*side;Quad(O(a,half*side),I(a,half*side),I(z,half*side),O(z,half*side),normal,normal,normal,normal);}
            }
            foreach(float angle in new[]{start,end}){
                var normal=P(-Mathf.Sin(angle),Mathf.Cos(angle),0)*(angle==start?-1:1);
                Quad(P(rx*Mathf.Cos(angle),ry*Mathf.Sin(angle),-half),P(ix*Mathf.Cos(angle),iy*Mathf.Sin(angle),-half),P(ix*Mathf.Cos(angle),iy*Mathf.Sin(angle),half),P(rx*Mathf.Cos(angle),ry*Mathf.Sin(angle),half),normal,normal,normal,normal);
            }
            var mesh=new Mesh();mesh.SetVertices(vertices);mesh.SetNormals(normals);mesh.SetUVs(0,uv);mesh.SetTriangles(triangles,0);mesh.RecalculateBounds();
            b.Add(mesh,color,role,Matrix4x4.TRS(at,q,Vector3.one));
        }
    }
}
