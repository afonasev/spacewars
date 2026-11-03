using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.Rendering;
namespace Spacewars.Presentation
{
    [Serializable] public sealed class NullDockMeshMetrics
    {
        public int primitiveMeshes,sourceVertices,sourceTriangles,combinedMeshes,activeVertices,activeTriangles,materials;
        public long sourceMeshBytes,activeMeshBytes;
    }
    public sealed partial class PlayableWorld
    {
        private const float DockBaseSiteRadius=11.5f; // Existing authored ring radius; disk shares its inner boundary.
        private Material dockMaterial,dockSpaceMaterial;
        public NullDockMeshMetrics DockMetrics{get;private set;}
        private IEnumerator CreateNullDock(NullDockMap map,NullDockArtProfile artOverride=null)
        {
            var art=artOverride??NullDockArtProfile.Load();art.Validate();var budget=new LoadingFrameBudget();DockMetrics=new NullDockMeshMetrics();
            dockMaterial=new Material(Resources.Load<Shader>("NullDockSurface"));dockMaterial.SetTexture("_FogMask",Fog.Texture);
            dockMaterial.SetVector("_FogBounds",new Vector4(96,96,0,0));dockMaterial.SetColor("_FogColor",new Color((float)profile.FogTintR,(float)profile.FogTintG,(float)profile.FogTintB));
            dockMaterial.SetVector("_Finish",new Vector4(art.panelSize,art.panelJoint,art.textureTile,art.textureContrast));dockMaterial.SetFloat("_PatchSize",art.patchSize);dockMaterial.SetFloat("_RouteWidth",art.routeWidth);dockMaterial.SetFloat("_Emission",art.emission);dockMaterial.SetFloat("_CoreEmission",art.coreEmission);
            // Static zoning follows A04 support boundaries, never units, ownership or live threat state.
            var home=map.Supports.Single(s=>s.Id=="H-A").Bounds;
            var north=map.Supports.Single(s=>s.Id=="F-N").Bounds;
            var south=map.Supports.Single(s=>s.Id=="F-S").Bounds;
            dockMaterial.SetVector("_ThreatBounds",new Vector4((float)-home.MaxX,(float)north.MinZ,(float)-south.MinX,(float)-home.MinZ));
            // Palette vectors carry sRGB, matching vertex colors; shader converts both exactly once.
            dockMaterial.SetVector("_BattleColor",art.Battle);dockMaterial.SetVector("_ContestedColor",art.Contested);dockMaterial.SetVector("_RearColor",art.Rear);
            var baseSites=map.Sites(profile).Where(s=>s.Kind!=PlayableBuildingKind.Mine).ToArray();
            Vector4 Pair(int a,int b)=>new Vector4((float)baseSites[a].Position.X,(float)baseSites[a].Position.Z,(float)baseSites[b].Position.X,(float)baseSites[b].Position.Z);
            dockMaterial.SetVector("_BaseSitesA",Pair(0,1));dockMaterial.SetVector("_BaseSitesB",Pair(2,3));
            dockMaterial.SetVector("_SiteDisk",new Vector4(DockBaseSiteRadius-.2f,art.siteDiskLift,0,0));
            foreach(var suffix in new[]{"","B","C"})dockMaterial.SetTexture("_Steel"+suffix,Resources.Load<Texture2D>("Environment/NullDock/Steel"+(suffix==""?"":"-"+suffix.ToLowerInvariant())));
            foreach(var suffix in new[]{"","B","C"})dockMaterial.SetTexture("_Panel"+suffix,Resources.Load<Texture2D>("Environment/NullDock/Steel"+(suffix==""?"":"-"+suffix.ToLowerInvariant())));
            var batch=new DockBatch(DockMetrics);
            foreach(var support in map.Supports){
                if(support.Id.StartsWith("R"))batch.Add(NullDockRampMesh.Create(support,-art.hullDepth,art.Upper));
                else batch.Add(TerrainMesh.Prism(support.Bounds,-art.hullDepth,support.Height,support.HeightAt),support.Id=="station-deck"?art.Deck:art.Upper,support.Id=="station-deck"?0:1);
                if(budget.Expired){yield return null;budget.Restart();}
            }
            // Semantic panel regions are constructive seams, while sibling albedo selection is continuous and irregular.
            foreach(var support in map.Supports.Where(s=>s.Id.StartsWith("H-")||s.Id.StartsWith("F-"))){
                var b=support.Bounds;var inset=new NavObstacle(b.MinX+1.2,b.MinZ+1.2,b.MaxX-1.2,b.MaxZ-1.2);
                batch.Add(TerrainMesh.Prism(inset,0,0,p=>support.HeightAt(p)+.018),support.Id.StartsWith("F-")?art.Outpost:art.Headquarters,1);
                DockRailEdges(batch,map,support,art);
            }
            DockExteriorHull(batch,map,art);
            for(int i=3;i<map.Modules.Count;i++){
                DockDetailedModule(batch,map.Modules[i],i,(float)map.ModuleHeight,art);
                if(budget.Expired){yield return null;budget.Restart();}
            }
            DockDeckDetails(batch,map,art);
            foreach(var site in map.Sites(profile)){
                float h=(float)map.SurfaceHeight(site.Position);var at=V(site.Position,h+.03f);
                float radius=site.Kind==PlayableBuildingKind.RelayTower?1.8f:site.Kind==PlayableBuildingKind.Mine?3.3f:DockBaseSiteRadius;
                batch.Ring(at,radius,radius-.2f,new Color(.66f,.48f,.21f),4);
                if(site.Kind==PlayableBuildingKind.Mine){
                    // Ore handling trim is flush, so neither the captured building nor units are obstructed.
                    batch.Ring(at+Vector3.up*.01f,radius-.55f,radius-.75f,Steel,4);
                    for(int k=0;k<4;k++){float angle=k*Mathf.PI*.5f;var p=at+new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle))*radius;batch.Box(p,new Vector3(.9f,.035f,.4f),Amber,6,Quaternion.Euler(0,-k*90,0));}
                }
            }
            foreach(var spec in new[]{new Vector4(36,22,28,8),new Vector4(156,138,28,8),new Vector4(116,9,32,8),new Vector4(76,151,32,8)}){
                var at=map.Point(spec.x,spec.y);float h=(float)map.SurfaceHeight(at);var bounds=new NavObstacle(at.X-spec.z/2,at.Z-spec.w/2,at.X+spec.z/2,at.Z+spec.w/2);
                batch.Add(TerrainMesh.Prism(bounds,h+.015,h+.02),new Color(.14f,.20f,.23f),3);
                batch.Box(V(at,h+.025f),new Vector3(spec.z,.02f,.15f),new Color(.65f,.36f,.10f),4);
            }
            // Exposed radiator fins and docking spars only project outward into unsupported space.
            foreach(var position in new[]{map.Point(61,14),map.Point(131,146)}){
                float sign=position.Z>0?1:-1;var at=V(position,-1.8f);
                batch.Box(at+new Vector3(0,0,sign*4),new Vector3(28,.6f,7),DarkSteel,4);
                for(int k=-12;k<=12;k+=2)batch.Box(at+new Vector3(k,-.3f,sign*5),new Vector3(.35f,1.4f,9),new Color(.25f,.31f,.35f),4);
                batch.Beam(at+new Vector3(-15,0,0),at+new Vector3(-15,0,sign*14),.6f,.8f,Steel,4);
                batch.Beam(at+new Vector3(15,0,0),at+new Vector3(15,0,sign*14),.6f,.8f,Steel,4);
            }
            foreach(int side in new[]{-1,1}){
                var at=new Vector3(side*6,0,-side*74);var end=at+new Vector3(side*20,art.craneHeight,0);var top=at+Vector3.up*art.craneHeight;
                batch.Box(at+new Vector3(-side*3,-1,0),new Vector3(7,1.2f,5),DarkSteel,4);
                foreach(int z in new[]{-1,1}){
                    batch.Beam(at+new Vector3(0,0,z*1.7f),top+new Vector3(0,0,z*.7f),.45f,.45f,new Color(.58f,.29f,.07f),4);
                    batch.Beam(top+new Vector3(0,0,z*.7f),end+new Vector3(0,0,z*.7f),.35f,.45f,new Color(.62f,.35f,.08f),4);
                    batch.Cylinder(at+new Vector3(0,art.craneHeight+2,z*.5f),end+new Vector3(0,0,z*.5f),.065f,Steel,4);
                }
                batch.Box(top+new Vector3(-side*1.5f,-.7f,0),new Vector3(3,2,2.5f),new Color(.39f,.23f,.08f),2);
                batch.Box(top+new Vector3(-side*1.5f,-.4f,-1.27f),new Vector3(2,.9f,.06f),new Color(.06f,.20f,.25f),4);
                batch.Cylinder(end,end-Vector3.up*5,.055f,Steel,4);batch.Box(end-Vector3.up*5,new Vector3(.9f,.7f,.9f),Amber,6);
                batch.Box(top+Vector3.up*1.25f,new Vector3(.4f,.2f,.4f),Amber,5);
            }
            DockCraneDetails(batch,art);
            var mesh=batch.Finish();DockEmit("Station hull, decks and equipment",mesh,dockMaterial);
            DockMachineMetrics.Clear();
            for(int i=0;i<3;i++){CreateZeroFieldMachine(map,i,art);if(budget.Expired){yield return null;budget.Restart();}}
            dockSpaceMaterial=new Material(Resources.Load<Shader>("NullDockSpace"));dockSpaceMaterial.SetVector("_Planet",new Vector4(art.planetX,art.planetZ,art.planetRadius,0));
            var backdrop=TerrainMesh.Prism(new NavObstacle(-700,-700,700,700),-42,-41);FinalProceduralMesh.IndexOwned(backdrop);DockEmit("Orbital backdrop",backdrop,dockSpaceMaterial);
            var backdropRenderer=root.Find("Orbital backdrop").GetComponent<MeshRenderer>();backdropRenderer.shadowCastingMode=ShadowCastingMode.Off;backdropRenderer.receiveShadows=false;
            DockMetrics.materials=2;
        }
        private static readonly Color Steel=new Color(.45f,.50f,.53f),DarkSteel=new Color(.12f,.18f,.22f),Copper=new Color(.47f,.28f,.15f),Cyan=new Color(.10f,.66f,.76f),Amber=new Color(.95f,.56f,.13f);
        private static Vector3 V(NavPoint p,float y)=>new Vector3((float)p.X,y,(float)p.Z);
        private static void DockRailEdges(DockBatch batch,NullDockMap map,MapSupport support,NullDockArtProfile art)
        { DockPlatformFascia(batch,map,support,art); }
        private void DockEmit(string name,Mesh mesh,Material source){terrainMeshes.Add(mesh);var obj=new GameObject(name);obj.transform.SetParent(root,false);obj.AddComponent<MeshFilter>().sharedMesh=mesh;obj.AddComponent<MeshRenderer>().sharedMaterial=source;}
        private sealed class DockBatch
        {
            private readonly List<Mesh> meshes=new List<Mesh>();private readonly List<CombineInstance> parts=new List<CombineInstance>();private readonly NullDockMeshMetrics metrics;
            public Matrix4x4 LocalTransform=Matrix4x4.identity;
            public readonly int DetailLevel;
            public DockBatch(NullDockMeshMetrics metrics,int detailLevel=0){this.metrics=metrics;DetailLevel=detailLevel;}
            public void Add(Mesh mesh,Color color,int role,Matrix4x4? transform=null){
                mesh.SetColors(Enumerable.Repeat(color,mesh.vertexCount).ToList());mesh.SetUVs(1,Enumerable.Repeat(new Vector2(role,0),mesh.vertexCount).ToList());
                Add(mesh,transform);
            }
            public void Add(Mesh mesh,Matrix4x4? transform=null){
                metrics.primitiveMeshes++;metrics.sourceVertices+=mesh.vertexCount;metrics.sourceTriangles+=mesh.triangles.Length/3;metrics.sourceMeshBytes+=UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(mesh);
                meshes.Add(mesh);parts.Add(new CombineInstance{mesh=mesh,transform=LocalTransform*(transform??Matrix4x4.identity)});
            }
            public void Box(Vector3 center,Vector3 size,Color color,int role,Quaternion? rotation=null){var m=TerrainMesh.Prism(new NavObstacle(-.5,-.5,.5,.5),-.5,.5);Add(m,color,role,Matrix4x4.TRS(center,rotation??Quaternion.identity,size));}
            public void Beam(Vector3 a,Vector3 b,float width,float height,Color color,int role){var d=b-a;if(d.sqrMagnitude<.000001f)return;Box((a+b)*.5f,new Vector3(width,height,d.magnitude),color,role,Quaternion.LookRotation(d));}
            public void Cylinder(Vector3 a,Vector3 b,float radius,Color color,int role){
                // Existing station uses exact twelve-sided pipes; only new modeled batches request reduced tiers.
                int sides=DetailLevel==0?12:DetailLevel==1?8:6;
                var ring=Enumerable.Range(0,sides).Select(i=>new NavPoint(Math.Cos(i*Math.PI*2/sides)*radius,Math.Sin(i*Math.PI*2/sides)*radius));
                var mesh=TerrainMesh.Prism(new NavObstacle(new NavPolygon(ring)),0,Vector3.Distance(a,b));
                Add(mesh,color,role,Matrix4x4.TRS(a,Quaternion.FromToRotation(Vector3.up,b-a),Vector3.one));
            }
            public void Ring(Vector3 center,float outer,float inner,Color color,int role){
                var vertices=new List<Vector3>();var triangles=new List<int>();const int count=64;
                for(int i=0;i<count;i++){float a=i*Mathf.PI*2/count,b=(i+1)*Mathf.PI*2/count;int n=vertices.Count;vertices.Add(new Vector3(Mathf.Cos(a)*inner,0,Mathf.Sin(a)*inner));vertices.Add(new Vector3(Mathf.Cos(a)*outer,0,Mathf.Sin(a)*outer));vertices.Add(new Vector3(Mathf.Cos(b)*outer,0,Mathf.Sin(b)*outer));vertices.Add(new Vector3(Mathf.Cos(b)*inner,0,Mathf.Sin(b)*inner));triangles.AddRange(new[]{n,n+2,n+1,n,n+3,n+2});}
                var mesh=new Mesh();mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();Add(mesh,color,role,Matrix4x4.Translate(center));
            }
            public Mesh Finish(){
                var mesh=new Mesh{name="Null Dock combined static mesh",indexFormat=IndexFormat.UInt32};mesh.CombineMeshes(parts.ToArray(),true,true);FinalProceduralMesh.IndexOwned(mesh);
                metrics.combinedMeshes++;metrics.activeVertices+=mesh.vertexCount;metrics.activeTriangles+=mesh.triangles.Length/3;metrics.activeMeshBytes+=UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(mesh);
                foreach(var source in meshes)ReleaseWorldObject(source);meshes.Clear();parts.Clear();return mesh;
            }
        }
    }
}
