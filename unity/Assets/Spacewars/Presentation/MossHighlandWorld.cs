using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
using UnityEngine;
namespace Spacewars.Presentation
{
    public sealed partial class PlayableWorld
    {
        private void CreateMossHighland(MossHighlandMap map,MossHighlandArtProfile selectedArt=null)
        {
            var art=selectedArt??MossHighlandArtProfile.Load();art.Validate();
            mossTerrainMaterial=art.Material(Fog.Texture,new Vector4((float)map.HalfExtent,(float)map.HalfExtent,0,0),new Color((float)profile.FogTintR,(float)profile.FogTintG,(float)profile.FogTintB));
            var masks=BuildMossPaintMasks(map,art);mossPaintMask=masks[0];mossAccentMask=masks[1];
            mossTerrainMaterial.SetTexture("_PaintA",mossPaintMask);mossTerrainMaterial.SetTexture("_PaintB",mossAccentMask);
            // Ramp precedence and flat supports come from the navigation provider.
            // Clip the underlying decks around ramps so no flat cap covers the slope.
            foreach(var support in map.Supports){
                var pieces=new List<NavObstacle>{support.Bounds};
                if(support.Id!="plateau-ramp")foreach(var ramp in map.Supports)if(ramp.Id=="plateau-ramp")pieces=Subtract(pieces,ramp.Bounds);
                foreach(var b in pieces)Prism(support.Id,b,-1,support.Height,support.HeightAt);
            }
            // One heightfield skin avoids overlapping roofs and coincident stratum walls.
            // It samples only original solid cells; relief retreats below their authored tops.
            var levels=new float[64,64];
            for(int z=0;z<64;z++)for(int x=0;x<64;x++){
                var at=new NavPoint(x*2-63,z*2-63);
                levels[x,z]=(float)map.Solids.Where(r=>r.Contains(at)).Select(r=>r.Top).DefaultIfEmpty(0).Max();
            }
            var ridgeVertices=new List<Vector3>();var ridgeIndices=new List<int>();
            float H(float x,float z,float level)=>level-Mathf.Min(art.ridgeErosion,level*.4f)*Mathf.PerlinNoise(x*.27f+31,z*.27f+71);
            void Tri(Vector3 a,Vector3 b,Vector3 c){int i=ridgeVertices.Count;ridgeVertices.Add(a);ridgeVertices.Add(b);ridgeVertices.Add(c);ridgeIndices.Add(i);ridgeIndices.Add(i+1);ridgeIndices.Add(i+2);}
            float Level(int x,int z)=>x<0||z<0||x>=64||z>=64?0:levels[x,z];
            for(int z=0;z<64;z++)for(int x=0;x<64;x++){
                float level=levels[x,z];if(level<=0)continue;
                float xx=x*2-64,zz=z*2-64;
                Vector3 P(float u,float v,float l)=>new Vector3(xx+u,H(xx+u,zz+v,l),zz+v);
                for(int j=0;j<4;j++)for(int i=0;i<4;i++){
                    float u=i*.5f,v=j*.5f;Tri(P(u,v,level),P(u,v+.5f,level),P(u+.5f,v+.5f,level));Tri(P(u,v,level),P(u+.5f,v+.5f,level),P(u+.5f,v,level));
                }
                void Wall(float ax,float az,float bx,float bz,float neighbor){
                    if(neighbor>=level) return;
                    for(int i=0;i<4;i++){
                        float u=i*.25f,v=(i+1)*.25f;float a=Mathf.Lerp(ax,bx,u),b=Mathf.Lerp(az,bz,u),c=Mathf.Lerp(ax,bx,v),d=Mathf.Lerp(az,bz,v);
                        Tri(P(a,b,neighbor),P(a,b,level),P(c,d,level));Tri(P(a,b,neighbor),P(c,d,level),P(c,d,neighbor));
                    }
                }
                Wall(0,0,2,0,Level(x,z-1));Wall(2,0,2,2,Level(x+1,z));Wall(2,2,0,2,Level(x,z+1));Wall(0,2,0,0,Level(x-1,z));
            }
            var ridgeMesh=new Mesh{name="Eroded geological solid",indexFormat=UnityEngine.Rendering.IndexFormat.UInt32};ridgeMesh.SetVertices(ridgeVertices);ridgeMesh.SetTriangles(ridgeIndices,0);ridgeMesh.RecalculateNormals();ridgeMesh.RecalculateBounds();terrainMeshes.Add(ridgeMesh);
            var ridgeObject=new GameObject("Moss ridge");ridgeObject.transform.SetParent(root,false);ridgeObject.AddComponent<MeshFilter>().sharedMesh=ridgeMesh;ridgeObject.AddComponent<MeshRenderer>().sharedMaterial=mossTerrainMaterial;
            foreach(var rock in map.VisualRidges)if(!double.IsNaN(rock.Top)){
                // The ridge skin is emitted once below; unions only place bounded scatter.
                // Deterministic tiny pockets remain INSIDE the original solid volume.
                // Traversable ground, complete building slots and exits stay empty.
                for(double z=rock.MinZ+1;z<rock.MaxZ-1;z+=2)for(double x=rock.MinX+1;x<rock.MaxX-1;x+=2){
                    float n=Mathf.PerlinNoise((float)x*.71f+17,(float)z*.83f+31);if(n>art.detailDensity)continue;
                    double r=art.pebbleSize*(.6+.4*n);var box=new NavObstacle(x-r,z-r,x+r,z+r);
                    if(!rock.Footprint.StrictlyContains(box.Footprint))continue;
                    if(map.Solids.Any(s=>s.Top>rock.Top&&s.Contains(new NavPoint(x,z))))continue;
                    Prism("Moss pebble",box,System.Math.Max(rock.Bottom,rock.Top-art.ridgeErosion*.9),rock.Top,null,true);
                }
            }
            void Prism(string name,NavObstacle shape,double bottom,double top,System.Func<NavPoint,double> height,bool ridge=false){
                var mesh=ridge?TerrainMesh.ErodedRock(shape,bottom,top,art.ridgeErosion):TerrainMesh.Prism(shape,bottom,top,height);
                terrainMeshes.Add(mesh);
                var obj=new GameObject(name);obj.transform.SetParent(root,false);obj.AddComponent<MeshFilter>().sharedMesh=mesh;
                var renderer=obj.AddComponent<MeshRenderer>();renderer.sharedMaterial=mossTerrainMaterial;
                var properties=new MaterialPropertyBlock();properties.SetColor("_BaseColor",Color.white);properties.SetFloat("_SurfaceRole",4);properties.SetFloat("_RoadSurface",ridge?0:1);renderer.SetPropertyBlock(properties);
            }
        }
        private Texture2D mossPaintMask,mossAccentMask;
        private Material mossTerrainMaterial;
        public static Texture2D[] BuildMossPaintMasks(MossHighlandMap map,MossHighlandArtProfile art)
        {
            art.Validate();
            // Fixed bounded raster precision: finer than the authored 2m source cells.
            const int size=512;int length=size*size;float step=(float)(map.HalfExtent*2/size);
            var fields=new float[MossHighlandArtProfile.Roles.Length][];for(int i=0;i<fields.Length;i++)fields[i]=new float[length];
            for(int i=0;i<length;i++)fields[4][i]=1;
            foreach(var patch in map.Materials){
                int role=MossHighlandArtProfile.Role(patch.Material);var b=patch.Bounds;
                int x0=Mathf.Clamp(Mathf.CeilToInt((float)(b.MinX+map.HalfExtent)/step-.5f),0,size),x1=Mathf.Clamp(Mathf.CeilToInt((float)(b.MaxX+map.HalfExtent)/step-.5f),0,size);
                int z0=Mathf.Clamp(Mathf.CeilToInt((float)(b.MinZ+map.HalfExtent)/step-.5f),0,size),z1=Mathf.Clamp(Mathf.CeilToInt((float)(b.MaxZ+map.HalfExtent)/step-.5f),0,size);
                for(int z=z0;z<z1;z++)for(int x=x0;x<x1;x++){int i=z*size+x;fields[4][i]=0;fields[role][i]=1;}
            }
            for(int role=0;role<fields.Length;role++)fields[role]=Feather(fields[role],size,role==2?0:Mathf.CeilToInt(art.naturalFeather/step));
            var a=new Color[length];var c=new Color[length];
            for(int i=0;i<length;i++){
                float total=0;foreach(var f in fields)total+=f[i];total=Mathf.Max(total,.00001f);
                a[i]=new Color(fields[0][i]/total,fields[1][i]/total,fields[2][i]/total,fields[3][i]/total);
                c[i]=new Color(fields[4][i]/total,fields[5][i]/total,fields[6][i]/total,0);
            }
            Texture2D Mask(string name,Color[] pixels){var t=new Texture2D(size,size,TextureFormat.RGBA32,false,true){name=name,wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Bilinear};t.SetPixels(pixels);t.Apply(false,false);return t;}
            return new[]{Mask("Moss Highland ground role weights",a),Mask("Moss Highland accent role weights",c)};
        }
    }
}
