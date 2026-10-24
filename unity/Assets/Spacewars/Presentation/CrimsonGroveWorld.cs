using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
using UnityEngine;
namespace Spacewars.Presentation
{
    public sealed partial class PlayableWorld
    {
        private Material groveMaterial;private Texture2D groveSurfaceMask;
        private System.Collections.IEnumerator CreateCrimsonGrove(CrimsonGroveMap map)
        {
            var art=CrimsonGroveArtProfile.Load();var budget=new LoadingFrameBudget();
            groveMaterial=new Material(Resources.Load<Shader>("CrimsonGroveSurface")){name=art.id};
            groveMaterial.SetTexture("_FogMask",Fog.Texture);groveMaterial.SetVector("_FogBounds",new Vector4((float)map.HalfExtent,(float)map.HalfExtent,0,0));
            groveMaterial.SetColor("_FogColor",new Color((float)profile.FogTintR,(float)profile.FogTintG,(float)profile.FogTintB));
            EnvironmentArt.BindFamily(groveMaterial,"Earth","Environment/NaturalFrontier/Earth",new Vector3(.13535270f,.15587468f,.14469715f));
            EnvironmentArt.BindFamily(groveMaterial,"Rock","Environment/NaturalFrontier/Rock",new Vector3(.13677501f,.13289979f,.14225178f));
            groveMaterial.SetVector("_Detail",new Vector4(art.textureTile,art.patchSize,.06f,art.detailContrast));
            EnvironmentArt.BindFamily(groveMaterial,"Concrete","Environment/NaturalFrontier/Concrete",new Vector3(.38497328f,.37142344f,.37949659f));
            groveMaterial.SetColor("_RoadTint",Color.HSVToRGB(art.rockHue,art.rockSaturation,art.roadValue));
            groveMaterial.SetVector("_GroundFinish",new Vector4(art.macroScale,art.macroContrast,art.roadValue,art.footDustReach));
            groveMaterial.SetVector("_RockFinish",new Vector4(art.rockStrata,art.rockFractures,art.rockFractureScale,art.normalSlope));
            groveMaterial.SetVector("_PadFinish",new Vector4(art.padJointSize,art.padWear,art.padValue,0));
            groveMaterial.SetColor("_MineralTint",art.Rock);
            groveMaterial.SetVector("_Geology",new Vector4(art.bedrockCoverage,art.mineralSheetContrast,0,0));
            groveMaterial.SetColor("_TerraceTint",art.Terrace);groveMaterial.SetFloat("_TerraceHeight",(float)map.TerraceHeight);
            groveMaterial.SetVector("_Deposits",new Vector4(art.roadBurial,art.roadDepositScale,art.pebbleSize,art.gravelCoverage));
            // Distance raster precision is a fixed presentation data contract, not gameplay tuning.
            const int resolution=512;groveSurfaceMask=new Texture2D(resolution,resolution,TextureFormat.RGBA32,false,true){name="Crimson Grove ground transitions",wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Bilinear};
            var pixels=new Color[resolution*resolution];var routes=CrimsonGroveSurfaces.Routes(map);
            for(int z=0;z<resolution;z++){
                for(int x=0;x<resolution;x++){
                    var p=new NavPoint(((x+.5)/resolution*2-1)*map.HalfExtent,((z+.5)/resolution*2-1)*map.HalfExtent);double distance=double.MaxValue;
                    foreach(var rock in map.Solids)distance=Math.Min(distance,Math.Sqrt(rock.Footprint.DistanceSquared(p)));
                    double road=CrimsonGroveSurfaces.RouteDistance(p,routes);float variation=.8f+.4f*Mathf.PerlinNoise((float)p.X/7+11,(float)p.Z/7+29);
                    float wear=1-Mathf.SmoothStep(0,1,(float)road/(art.roadWidth*variation*.5f));pixels[z*resolution+x]=new Color(Mathf.Clamp01((float)distance/8),wear,0,1);
                }
                if(budget.Expired){yield return null;budget.Restart();}
            }
            groveSurfaceMask.SetPixels(pixels);groveSurfaceMask.Apply();groveMaterial.SetTexture("_SurfaceMask",groveSurfaceMask);
            foreach(var support in map.Supports){
                GroveMesh(support.SurfaceId,CrimsonGroveSurfaces.SupportTop(support),art.Ground,0);
                GroveMesh(support.SurfaceId+" layered side faces",CrimsonGroveSurfaces.SupportSides(support,art),art.Rock,1);
                if(budget.Expired){yield return null;budget.Restart();}
            }
            foreach(var authoredRock in map.Solids){
                var rock=map.Border.Contains(authoredRock)?CrimsonGroveSurfaces.TrimBorder(map,authoredRock,art):authoredRock;
                GroveMesh("Layered cool stone ridge",CrimsonGroveSurfaces.LayeredRock(rock,art,out var cap),art.Rock,1);
                // Pure decoration: crystals and rock fragments stay inside the blocked cap, never on roads or pads.
                var crowns=new List<CombineInstance>();var random=new System.Random((int)(rock.MinX*73+rock.MinZ*127));
                for(double z=rock.MinZ+art.treeSpacing/2;z<rock.MaxZ;z+=art.treeSpacing)for(double x=rock.MinX+art.treeSpacing/2;x<rock.MaxX;x+=art.treeSpacing){
                    double px=x+(random.NextDouble()-.5)*art.treeSpacing*.6,pz=z+(random.NextDouble()-.5)*art.treeSpacing*.6;
                    if(Math.Abs(px)>map.HalfExtent||Math.Abs(pz)>map.HalfExtent)continue;
                    var p=new NavPoint(px,pz);float radius=art.crownRadius*(.75f+(float)random.NextDouble()*.25f);
                    if(!cap.Footprint.Contains(p)||cap.Footprint.BoundaryDistanceSquared(p)<radius*radius)continue;
                    float height=art.treeHeight*(.8f+(float)random.NextDouble()*.2f);
                    var pos=new Vector3((float)px,CrimsonGroveSurfaces.CrestHeight(rock,art,(float)px,(float)pz),(float)pz);
                    crowns.Add(new CombineInstance{mesh=GroveCrystal(radius,height,art,random),transform=Matrix4x4.Translate(pos)});
                }
                GroveCombined("Faceted crimson crystal clusters",crowns,art.Leaves);
                var rubble=new List<CombineInstance>();
                for(double z=rock.MinZ;z<rock.MaxZ;z+=art.rockChipSpacing)for(double x=rock.MinX;x<rock.MaxX;x+=art.rockChipSpacing){
                    float px=(float)(x+random.NextDouble()*art.rockChipSpacing),pz=(float)(z+random.NextDouble()*art.rockChipSpacing);
                    float size=art.rockChipSize*(.5f+(float)random.NextDouble()*.5f);var point=new NavPoint(px,pz);
                    if(!cap.Footprint.Contains(point)||cap.Footprint.BoundaryDistanceSquared(point)<size*size)continue;
                    rubble.Add(new CombineInstance{mesh=GroveRockChip(size,random),transform=Matrix4x4.TRS(new Vector3(px,CrimsonGroveSurfaces.CrestHeight(rock,art,px,pz)-size*.15f,pz),Quaternion.Euler(0,(float)random.NextDouble()*360,0),Vector3.one)});
                }
                GroveCombined("Broken cliff cap fragments",rubble,art.Rock,1);
                if(budget.Expired){yield return null;budget.Restart();}
            }
            foreach(var site in map.Sites(profile).Where(p=>p.Kind==PlayableBuildingKind.Headquarters||p.Kind==PlayableBuildingKind.Outpost)){
                var pad=CrimsonGroveSurfaces.Pad(site,profile,art);
                // Millimetre layer separation avoids z-fighting; there is no raised collision or kerb.
                GroveMesh("Complete construction apron "+site.Id,CrimsonGroveSurfaces.PrefabApron(pad,site,map,art),new Color(art.padValue*.94f,art.padValue*.97f,art.padValue),3,site.Position);
            }
        }
        private void GroveMesh(string name,Mesh mesh,Color color,int role,NavPoint centre=default(NavPoint)){
            terrainMeshes.Add(mesh);var obj=new GameObject(name);obj.transform.SetParent(root,false);obj.AddComponent<MeshFilter>().sharedMesh=mesh;var renderer=obj.AddComponent<MeshRenderer>();renderer.sharedMaterial=groveMaterial;
            var block=new MaterialPropertyBlock();block.SetColor("_BaseColor",color);block.SetFloat("_Role",role);block.SetVector("_PadOrigin",new Vector4((float)centre.X,(float)centre.Z,0,0));renderer.SetPropertyBlock(block);
        }
        private void GroveCombined(string name,List<CombineInstance> parts,Color color,int role=2){if(parts.Count==0)return;var mesh=new Mesh{indexFormat=UnityEngine.Rendering.IndexFormat.UInt32};mesh.CombineMeshes(parts.ToArray(),true,true);foreach(var part in parts)ReleaseWorldObject(part.mesh);GroveMesh(name,mesh,color,role);}
        private static Mesh GroveRockChip(float radius,System.Random random){
            // Fixed octagonal fracture topology; profile controls the visible scale/density.
            var vertices=new List<Vector3>();var triangles=new List<int>();var rings=new Vector3[16];
            for(int i=0;i<8;i++){
                float a=i*Mathf.PI/4,r=radius*(.65f+(float)random.NextDouble()*.35f);
                rings[i]=new Vector3(Mathf.Sin(a)*r,0,Mathf.Cos(a)*r);
                rings[i+8]=new Vector3(Mathf.Sin(a)*r*.65f,radius*(.4f+(float)random.NextDouble()*.4f),Mathf.Cos(a)*r*.65f);
            }
            void Tri(Vector3 a,Vector3 b,Vector3 c){int i=vertices.Count;vertices.Add(a);vertices.Add(b);vertices.Add(c);triangles.AddRange(new[]{i,i+1,i+2});}
            var peak=new Vector3(radius*.15f,radius*.7f,0);
            for(int i=0;i<8;i++){int j=(i+1)%8;Tri(rings[i],rings[j],rings[i+8]);Tri(rings[j],rings[j+8],rings[i+8]);Tri(rings[i+8],rings[j+8],peak);}
            var mesh=new Mesh{name="Angular mineral fragment"};mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
        }
        public static Mesh GroveCrystal(float radius,float height,CrimsonGroveArtProfile art,System.Random random){
            // Six prism faces and a split pointed termination are crystal topology.
            // Every shard fits in the cluster clearance already reserved on the blocked cap.
            var vertices=new List<Vector3>();var triangles=new List<int>();var colors=new List<Color>();
            float R(float a,float b)=>Mathf.Lerp(a,b,(float)random.NextDouble());
            void Tri(Vector3 a,Vector3 b,Vector3 c,Color ca,Color cb,Color cc){int i=vertices.Count;vertices.Add(a);vertices.Add(b);vertices.Add(c);colors.Add(ca);colors.Add(cb);colors.Add(cc);triangles.AddRange(new[]{i,i+1,i+2});}
            for(int shard=0;shard<Mathf.RoundToInt(art.crystalShards);shard++){
                float a=R(0,Mathf.PI*2),orbit=shard==0?0:R(.3f,.68f)*radius;
                float h=height*(shard==0?R(.85f,1):R(.28f,.75f));
                float width=radius*(shard==0?R(.2f,.27f):R(.11f,.19f));
                float lean=shard==0?0:R(.2f,1)*art.crystalLean;
                var rotation=Quaternion.AngleAxis(lean,new Vector3(Mathf.Cos(a),0,-Mathf.Sin(a)));
                float reach=orbit+width+Mathf.Sin(lean*Mathf.Deg2Rad)*h;
                float fit=Mathf.Min(1,radius/Mathf.Max(radius,reach));
                var origin=new Vector3(Mathf.Sin(a)*orbit,0,Mathf.Cos(a)*orbit)*fit;
                Vector3 P(float angle,float y,float r){var v=rotation*new Vector3(Mathf.Sin(angle)*r,y,Mathf.Cos(angle)*r)*fit;return origin+v;}
                var dark=new Color(.32f,.22f,.26f,1);var mid=new Color(R(.65f,.95f),R(.55f,.75f),R(.58f,.8f),1);var light=new Color(1.25f,.9f,.94f,1);
                var tip=P(a+.3f,h,0);
                for(int face=0;face<6;face++){
                    float u=a+face*Mathf.PI/3,v=a+(face+1)*Mathf.PI/3;
                    var b0=P(u,0,width*.7f);var b1=P(v,0,width*.7f);
                    var t0=P(u,h*.72f,width);var t1=P(v,h*.72f,width);
                    Tri(b0,t1,t0,dark,mid,mid);Tri(b0,b1,t1,dark,dark,mid);Tri(t0,t1,tip,mid,mid,light);
                }
            }
            var mesh=new Mesh{name="Multi shard faceted ruby cluster"};mesh.SetVertices(vertices);mesh.SetColors(colors);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
        }
    }
}
