using System.Collections.Generic;
using Spacewars.Simulation;
using UnityEngine;

namespace Spacewars.Presentation
{
    public sealed partial class PlayableWorld
    {
        private Mesh[] explorerLauncherBases,explorerLauncherHeads;
        private Material[] explorerLauncherMaterials;

        private void BuildExplorerRocketLauncher(Actor actor)
        {
            foreach(var renderer in actor.Launcher.GetComponentsInChildren<Renderer>(true))
                renderer.gameObject.SetActive(false);

            if(explorerLauncherMaterials==null)
            {
                explorerLauncherMaterials=new[]
                {
                    Resources.Load<Material>("StyleA/explorer-structure"),
                    Resources.Load<Material>("StyleA/explorer-neutral-metal"),
                    Resources.Load<Material>("StyleA/explorer-team-primary")
                };
                explorerLauncherBases=new Mesh[3];
                explorerLauncherHeads=new Mesh[3];
                for(int level=0;level<3;level++)
                {
                    explorerLauncherBases[level]=BuildLauncherBase(level);
                    explorerLauncherHeads[level]=BuildLauncherHead(level);
                }
            }

            var owner=OwnerPaint?.Invoke(actor.ExplorerRocketOwner)??(actor.ExplorerRocketOwner==PlayableOwner.Player?Ally:Enemy);
            actor.ExplorerRocketMeshes=new[]
            {
                AddMesh("Rocket traverse ring and cradle",actor.Turret,explorerLauncherBases[0]),
                AddMesh("Rocket paired elevation assembly",actor.Launcher,explorerLauncherHeads[0])
            };
            actor.ExplorerRocketDetailLevel=0;

            MeshFilter AddMesh(string name,Transform parent,Mesh mesh)
            {
                var part=new GameObject(name){layer=presentationLayer};
                part.transform.SetParent(parent,false);
                var filter=part.AddComponent<MeshFilter>();
                filter.sharedMesh=mesh;
                var renderer=part.AddComponent<MeshRenderer>();
                renderer.sharedMaterials=explorerLauncherMaterials;
                for(int i=0;i<explorerLauncherMaterials.Length;i++)
                {
                    MaterialFinish?.Apply(renderer,i,owner);
                    if(i==2)
                    {
                        var paint=new MaterialPropertyBlock();
                        renderer.GetPropertyBlock(paint,i);
                        ActorMaterialProfile.RecordColorInput(renderer,i,"_BaseColor",owner);
                        paint.SetColor("_BaseColor",owner);
                        renderer.SetPropertyBlock(paint,i);
                    }
                }
                return filter;
            }
        }

        public void SelectExplorerRocketDetail(Actor actor)
        {
            if(actor.ExplorerRocketMeshes==null)return;
            int level=Mathf.Clamp(actor.DetailLod?.Level??0,0,2);
            if(level==actor.ExplorerRocketDetailLevel)return;
            actor.ExplorerRocketMeshes[0].sharedMesh=explorerLauncherBases[level];
            actor.ExplorerRocketMeshes[1].sharedMesh=explorerLauncherHeads[level];
            actor.ExplorerRocketDetailLevel=level;
        }

        private void DisposeExplorerRocketMeshes()
        {
            if(explorerLauncherBases==null)return;
            foreach(var mesh in explorerLauncherBases)ReleaseWorldObject(mesh);
            foreach(var mesh in explorerLauncherHeads)ReleaseWorldObject(mesh);
        }

        private static Mesh BuildLauncherBase(int level)
        {
            var builder=new LauncherMesh();
            int sides=level==0?16:level==1?12:8;
            builder.Tube(new Vector3(0,-.085f,0),Vector3.up,.45f,0,.074f,sides,0);
            if(level<2)builder.Tube(new Vector3(0,-.02f,0),Vector3.up,.415f,0,.026f,sides,1);
            foreach(float side in new[]{-1f,1f})
            {
                float x=side*.61f;
                builder.BeveledBox(new Vector3(x,-.039f,0),new Vector3(.135f,.245f,.27f),.038f,0);
                builder.Tube(new Vector3(x,0,0),Vector3.right,.093f,0,.147f,level==0?12:8,1);
                if(level==0)builder.Tube(new Vector3(x+side*.076f,0,0),Vector3.right,.05f,0,.016f,8,0);
            }
            return builder.ToMesh("Explorer launcher base L"+level);
        }

        private static Mesh BuildLauncherHead(int level)
        {
            var builder=new LauncherMesh();
            float elevation=(float)ExplorerRocketGeometry.PortHeight;
            float front=(float)ExplorerRocketGeometry.PortForward;
            builder.Tube(Vector3.zero,Vector3.right,.068f,0,1.23f,level==0?12:8,0);
            foreach(float side in new[]{-1f,1f})
            {
                float x=side*(float)ExplorerRocketGeometry.PodOffset;
                builder.BeveledBox(new Vector3(x,.125f,.035f),new Vector3(.145f,.29f,.165f),.036f,0);
                builder.BeveledBox(new Vector3(x,elevation,.061f),new Vector3(.365f,.25f,.455f),.065f,0);
                builder.BeveledBox(new Vector3(x,elevation+.118f,.068f),new Vector3(.238f,.042f,.325f),.018f,2);

                // The mouth is centered on the same point used by the rocket simulation.
                // A recessed black cup and shaded inner rim read as an opening at all distances.
                var mouth=new Vector3(x,elevation,front);
                int sides=level==0?16:level==1?12:8;
                builder.Tube(mouth-Vector3.forward*.033f,Vector3.forward,.119f,.081f,.066f,sides,1);
                builder.Tube(mouth-Vector3.forward*.066f,Vector3.forward,.086f,0,.016f,sides,0);

                if(level==2)continue;
                int ventCount=level==0?3:2;
                for(int i=0;i<ventCount;i++)
                    builder.BeveledBox(new Vector3(x,elevation+.083f,-.14f+i*.038f),new Vector3(.177f,.022f,.016f),.005f,1);
                if(level==0)
                    foreach(float offset in new[]{-.085f,.085f})
                    foreach(float z in new[]{-.054f,.177f})
                        builder.Tube(new Vector3(x+offset,elevation+.141f,z),Vector3.up,.014f,0,.012f,6,1);
            }
            return builder.ToMesh("Explorer launcher elevation L"+level);
        }

        private sealed class LauncherMesh
        {
            private readonly List<Vector3> vertices=new List<Vector3>();
            private readonly List<Vector3> normals=new List<Vector3>();
            private readonly List<Vector2> uv=new List<Vector2>();
            private readonly List<int>[] triangles={new List<int>(),new List<int>(),new List<int>()};

            private void Face(int slot,Vector3 normal,params Vector3[] corners)
            {
                if(Vector3.Dot(Vector3.Cross(corners[1]-corners[0],corners[2]-corners[0]),normal)<0)
                    System.Array.Reverse(corners);
                int start=vertices.Count;
                Vector3 horizontal=Vector3.Cross(Mathf.Abs(normal.y)>.95f?Vector3.forward:Vector3.up,normal).normalized;
                Vector3 vertical=Vector3.Cross(normal,horizontal);
                foreach(var corner in corners)
                {
                    vertices.Add(corner);
                    normals.Add(normal);
                    uv.Add(new Vector2(Vector3.Dot(corner,horizontal),Vector3.Dot(corner,vertical))*1.85f+Vector2.one*.5f);
                }
                for(int i=1;i<corners.Length-1;i++)
                {
                    triangles[slot].Add(start);
                    triangles[slot].Add(start+i);
                    triangles[slot].Add(start+i+1);
                }
            }

            internal void BeveledBox(Vector3 center,Vector3 size,float bevel,int slot)
            {
                var half=size*.5f;
                Vector3[] Ring(float y,float inset)
                {
                    float x=half.x-inset,z=half.z-inset,b=bevel*.55f;
                    return new[]{new Vector3(-x+b,y,-z),new Vector3(x-b,y,-z),new Vector3(x,y,-z+b),
                        new Vector3(x,y,z-b),new Vector3(x-b,y,z),new Vector3(-x+b,y,z),new Vector3(-x,y,z-b),new Vector3(-x,y,-z+b)};
                }
                float lip=Mathf.Min(bevel*.48f,half.y*.65f);
                Vector3[][] bands={Ring(-half.y,lip),Ring(-half.y+lip,0),Ring(half.y-lip,0),Ring(half.y,lip)};
                for(int band=0;band<bands.Length-1;band++)
                for(int i=0;i<8;i++)
                {
                    int j=(i+1)%8;
                    Vector3 a=bands[band][i],b=bands[band][j],c=bands[band+1][j],d=bands[band+1][i];
                    Face(slot,Vector3.Cross(d-a,b-a).normalized,center+a,center+b,center+c,center+d);
                }
                for(int i=0;i<8;i++)
                {
                    bands[0][i]+=center;
                    bands[3][i]+=center;
                }
                Face(slot,Vector3.down,bands[0]);
                Face(slot,Vector3.up,bands[3]);
            }

            internal void Tube(Vector3 center,Vector3 axis,float outer,float inner,float length,int sides,int slot)
            {
                Vector3 u=Vector3.Cross(axis,axis==Vector3.up?Vector3.forward:Vector3.up).normalized;
                Vector3 v=Vector3.Cross(axis,u);
                Vector3 end=center+axis*(length*.5f),rear=center-axis*(length*.5f);
                for(int i=0;i<sides;i++)
                {
                    float a=i*2*Mathf.PI/sides,b=(i+1)*2*Mathf.PI/sides;
                    Vector3 n=u*Mathf.Cos(a)+v*Mathf.Sin(a),m=u*Mathf.Cos(b)+v*Mathf.Sin(b);
                    var outward=(n+m).normalized;
                    Face(slot,outward,rear+n*outer,rear+m*outer,end+m*outer,end+n*outer);
                    if(inner>0)
                    {
                        Face(slot,axis,end+n*outer,end+m*outer,end+m*inner,end+n*inner);
                        Face(0,-outward,rear+n*inner,rear+m*inner,end+m*inner,end+n*inner);
                    }
                    else
                    {
                        Face(slot,axis,end,end+n*outer,end+m*outer);
                        Face(slot,-axis,rear,rear+n*outer,rear+m*outer);
                    }
                }
            }

            internal Mesh ToMesh(string name)
            {
                var mesh=new Mesh{name=name,subMeshCount=triangles.Length};
                mesh.SetVertices(vertices);
                mesh.SetNormals(normals);
                mesh.SetUVs(0,uv);
                for(int i=0;i<triangles.Length;i++)mesh.SetTriangles(triangles[i],i,false);
                mesh.RecalculateTangents();
                mesh.RecalculateBounds();
                return mesh;
            }
        }
    }
}
