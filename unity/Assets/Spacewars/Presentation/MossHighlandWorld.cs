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
            var masks=BuildMossPaintMasks(map,art,profile);mossPaintMask=masks[0];mossAccentMask=masks[1];mossTransitionMask=masks[2];
            mossTerrainMaterial.SetTexture("_PaintA",mossPaintMask);mossTerrainMaterial.SetTexture("_PaintB",mossAccentMask);mossTerrainMaterial.SetTexture("_Transitions",mossTransitionMask);
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
                levels[x,z]=(float)map.OriginalRidges.Where(r=>r.Contains(at)).Select(r=>r.Top).DefaultIfEmpty(0).Max();
            }
            var strata=levels.Cast<float>().Distinct().OrderBy(v=>v).ToArray();
            var ridgeVertices=new List<Vector3>();var ridgeIndices=new List<int>();
            float H(float x,float z,float level)=>level-Mathf.Min(art.ridgeErosion,level*.4f)*Mathf.PerlinNoise(x*.27f+31,z*.27f+71);
            void Tri(Vector3 a,Vector3 b,Vector3 c){int i=ridgeVertices.Count;ridgeVertices.Add(a);ridgeVertices.Add(b);ridgeVertices.Add(c);ridgeIndices.Add(i);ridgeIndices.Add(i+1);ridgeIndices.Add(i+2);}
            float Level(int x,int z)=>x<0||z<0||x>=64||z>=64?0:levels[x,z];
            // The accepted inward-only treatment applies to every authored ridge.
            bool chipped=art.cliffEdgeInset!=0||art.cliffChipDepth!=0;
            Vector3 Skin(float wx,float wz,float level){
                var original=new Vector3(wx,H(wx,wz,level),wz);
                if(level<=0||!chipped)return original;
                // Shared world nodes query the same four quadrants at this terrace height.
                // Only exposed boundaries retreat; all offsets point into the solid union.
                Vector2 inward=Vector2.zero;int occupied=0;
                for(int dz=-1;dz<=1;dz+=2)for(int dx=-1;dx<=1;dx+=2){
                    if(Level(Mathf.FloorToInt((wx+64+dx*.01f)/2),Mathf.FloorToInt((wz+64+dz*.01f)/2))<level)continue;
                    inward+=new Vector2(dx,dz);occupied++;
                }
                if(occupied==0||occupied==4||inward==Vector2.zero)return original;
                inward.Normalize();
                float fracture=Mathf.PerlinNoise(wx/art.cliffFractureScale+123,wz/art.cliffFractureScale+47);
                float cut=Mathf.Lerp(.15f,1,fracture);
                // < half the roof subdivision prevents an exposed node crossing its next row.
                original.x+=inward.x*art.cliffEdgeInset*cut;
                original.z+=inward.y*art.cliffEdgeInset*cut;
                original.y-=Mathf.Min(art.cliffChipDepth*cut,level*.2f);
                return original;
            }
            for(int z=0;z<64;z++)for(int x=0;x<64;x++){
                float level=levels[x,z];if(level<=0)continue;
                float xx=x*2-64,zz=z*2-64;
                Vector3 P(float u,float v,float l)=>Skin(xx+u,zz+v,l);
                for(int j=0;j<4;j++)for(int i=0;i<4;i++){
                    float u=i*.5f,v=j*.5f;Tri(P(u,v,level),P(u,v+.5f,level),P(u+.5f,v+.5f,level));Tri(P(u,v,level),P(u+.5f,v+.5f,level),P(u+.5f,v,level));
                }
                void Wall(float ax,float az,float bx,float bz,float neighbor){
                    if(neighbor>=level) return;
                    for(int i=0;i<4;i++){
                        float u=i*.25f,v=(i+1)*.25f;float a=Mathf.Lerp(ax,bx,u),b=Mathf.Lerp(az,bz,u),c=Mathf.Lerp(ax,bx,v),d=Mathf.Lerp(az,bz,v);
                        if(!chipped){
                            Tri(P(a,b,neighbor),P(a,b,level),P(c,d,level));Tri(P(a,b,neighbor),P(c,d,level),P(c,d,neighbor));continue;
                        }
                        // Every adjoining wall uses the same authored height rings. A long
                        // face cannot bypass a lower terrace's corner and leave an open slit.
                        float lower=neighbor;
                        foreach(float upper in strata){
                            if(upper<=neighbor||upper>level)continue;
                            var lowA=P(a,b,lower);var lowB=P(c,d,lower);var topA=P(a,b,upper);var topB=P(c,d,upper);
                            Vector3 Shoulder(Vector3 low,Vector3 top,float wx,float wz){
                                float t=Mathf.Lerp(.55f,.8f,Mathf.PerlinNoise(wx/art.cliffFractureScale+71,wz/art.cliffFractureScale+19));
                                var mid=Vector3.Lerp(low,top,.2f);mid.y=Mathf.Lerp(low.y,top.y,t);return mid;
                            }
                            var midA=Shoulder(lowA,topA,xx+a,zz+b);var midB=Shoulder(lowB,topB,xx+c,zz+d);
                            Tri(lowA,midA,midB);Tri(lowA,midB,lowB);Tri(midA,topA,topB);Tri(midA,topB,midB);
                            lower=upper;
                        }
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
            CreateMossBorders(map,art);
            void Prism(string name,NavObstacle shape,double bottom,double top,System.Func<NavPoint,double> height,bool ridge=false){
                var mesh=ridge?TerrainMesh.ErodedRock(shape,bottom,top,art.ridgeErosion):TerrainMesh.Prism(shape,bottom,top,height);
                terrainMeshes.Add(mesh);
                var obj=new GameObject(name);obj.transform.SetParent(root,false);obj.AddComponent<MeshFilter>().sharedMesh=mesh;
                var renderer=obj.AddComponent<MeshRenderer>();renderer.sharedMaterial=mossTerrainMaterial;
                var properties=new MaterialPropertyBlock();properties.SetColor("_BaseColor",Color.white);properties.SetFloat("_SurfaceRole",4);properties.SetFloat("_RoadSurface",ridge?0:1);renderer.SetPropertyBlock(properties);
            }
        }
        private Texture2D mossPaintMask,mossAccentMask,mossTransitionMask;
        private Material mossTerrainMaterial;
        // Compact presentation envelope follows every native building slot, including
        // the widest permitted building. It never modifies supports, slots or navigation.
        public static NavObstacle MossApronBounds(TerritorySite site,PlayableProfile p,MossHighlandArtProfile art)
        {
            double center=TerritoryRules.Radius(p,site.Kind);
            double minX=site.Position.X-center,maxX=site.Position.X+center,minZ=site.Position.Z-center,maxZ=site.Position.Z+center;
            void Include(NavPoint point,double radius){minX=System.Math.Min(minX,point.X-radius);maxX=System.Math.Max(maxX,point.X+radius);minZ=System.Math.Min(minZ,point.Z-radius);maxZ=System.Math.Max(maxZ,point.Z+radius);}
            double building=System.Math.Max(p.ScienceFootprintRadius,System.Math.Max(p.FactoryFootprintRadius,p.RefineryFootprintRadius));
            foreach(var slot in site.Slots)Include(slot.Position,building);
            // Grow opposite sides equally around the main building, never around
            // the asymmetric bounds of a three- or five-slot ring.
            double halfX=System.Math.Max(site.Position.X-minX,maxX-site.Position.X)+art.apronMargin;
            double halfZ=System.Math.Max(site.Position.Z-minZ,maxZ-site.Position.Z)+art.apronMargin;
            return new NavObstacle(site.Position.X-halfX,site.Position.Z-halfZ,site.Position.X+halfX,site.Position.Z+halfZ);
        }
        public static Texture2D[] BuildMossPaintMasks(MossHighlandMap map,MossHighlandArtProfile art,PlayableProfile gameplay)
        {
            art.Validate();
            // Fixed bounded raster precision: finer than the authored 2m source cells.
            const int size=512;int length=size*size;float step=(float)(map.HalfExtent*2/size);
            var fields=new float[MossHighlandArtProfile.Roles.Length][];for(int i=0;i<fields.Length;i++)fields[i]=new float[length];
            for(int i=0;i<length;i++)fields[4][i]=1;
            foreach(var patch in map.Materials){
                int role=MossHighlandArtProfile.Role(patch.Material);var b=patch.Bounds;
                // Current complete sites are the only authority for construction paint.
                // Remove retained source decks, including vacated side-outpost positions.
                if(role==2)role=4;
                int x0=Mathf.Clamp(Mathf.CeilToInt((float)(b.MinX+map.HalfExtent)/step-.5f),0,size),x1=Mathf.Clamp(Mathf.CeilToInt((float)(b.MaxX+map.HalfExtent)/step-.5f),0,size);
                int z0=Mathf.Clamp(Mathf.CeilToInt((float)(b.MinZ+map.HalfExtent)/step-.5f),0,size),z1=Mathf.Clamp(Mathf.CeilToInt((float)(b.MaxZ+map.HalfExtent)/step-.5f),0,size);
                for(int z=z0;z<z1;z++)for(int x=x0;x<x1;x++){int i=z*size+x;fields[4][i]=0;fields[role][i]=1;}
            }
            foreach(var site in map.Sites(gameplay).Where(s=>TerritoryRules.Center(s.Kind))){
                var bounds=MossApronBounds(site,gameplay,art);
                for(int z=0;z<size;z++)for(int x=0;x<size;x++){
                    var point=new NavPoint((x+.5)*step-map.HalfExtent,(z+.5)*step-map.HalfExtent);
                    if(!bounds.Contains(point))continue;
                    // Mirror ridge notches too, so clipping cannot shift the visible center.
                    bool blocked=false;
                    foreach(int sx in new[]{-1,1})foreach(int sz in new[]{-1,1}){
                        var mirrored=new NavPoint(site.Position.X+sx*(point.X-site.Position.X),site.Position.Z+sz*(point.Z-site.Position.Z));
                        blocked|=map.Solids.Any(s=>s.Contains(mirrored));
                    }
                    fields[2][z*size+x]=blocked?0:1;
                }
            }
            for(int role=0;role<fields.Length;role++)fields[role]=Feather(fields[role],size,role==2?0:Mathf.CeilToInt(art.naturalFeather/step));
            // Independent presentation distances; never enter gameplay or role normalization.
            var transitions=new Color[length];
            var centres=map.Sites(gameplay).Where(s=>TerritoryRules.Center(s.Kind)).ToArray();
            var aprons=centres.Select(s=>MossApronBounds(s,gameplay,art)).ToArray();
            var ramps=map.Supports.Where(s=>s.Id=="plateau-ramp").ToArray();
            float Distance(NavObstacle b,double x,double z)=>(float)System.Math.Sqrt(b.DistanceSquared(new NavPoint(x,z)));
            for(int z=0;z<size;z++)for(int x=0;x<size;x++){
                int i=z*size+x;double xx=(x+.5)*step-map.HalfExtent,zz=(z+.5)*step-map.HalfExtent;
                float rock=art.screeReach,apron=art.apronEdgeReach;
                foreach(var b in map.Solids)rock=Mathf.Min(rock,Distance(b,xx,zz));
                foreach(var b in aprons)apron=Mathf.Min(apron,Distance(b,xx,zz));
                float rampPaint=0,siteRole=0;
                foreach(var ramp in ramps){
                    var bounds=ramp.Bounds;bool alongX=ramp.Gradient.X!=0;
                    double along=alongX?xx:zz,cross=System.Math.Abs(alongX?zz:xx);
                    double low=(alongX?bounds.MinX:bounds.MinZ)-art.rampApproach,high=(alongX?bounds.MaxX:bounds.MaxZ)+art.rampApproach;
                    float endFade=Mathf.SmoothStep(0,1,(float)System.Math.Min(along-low,high-along));
                    rampPaint=Mathf.Max(rampPaint,endFade*(1-Mathf.SmoothStep(0,1,(float)cross-3)));
                }
                for(int j=0;j<aprons.Length;j++)if(aprons[j].Contains(new NavPoint(xx,zz)))siteRole=centres[j].Kind==PlayableBuildingKind.Headquarters?1f/3:centres[j].Id==13?1:2f/3;
                // Inside solid roofs is not a foot; concrete retains exact precedence.
                transitions[i]=new Color(rock>0?1-Mathf.SmoothStep(0,1,rock/art.screeReach):0,
                    rock>0?1-Mathf.SmoothStep(0,1,apron/art.apronEdgeReach):0,rampPaint,siteRole);
            }
            var a=new Color[length];var c=new Color[length];
            for(int i=0;i<length;i++){
                // Authored construction has precedence over feathered natural roles.
                // Prevent soft natural weights from blurring flush engineered edges.
                if(fields[2][i]>.5f){a[i]=new Color(0,0,1,0);c[i]=Color.clear;continue;}
                float total=0;foreach(var f in fields)total+=f[i];total=Mathf.Max(total,.00001f);
                a[i]=new Color(fields[0][i]/total,fields[1][i]/total,fields[2][i]/total,fields[3][i]/total);
                c[i]=new Color(fields[4][i]/total,fields[5][i]/total,fields[6][i]/total,0);
            }
            Texture2D Mask(string name,Color[] pixels){var t=new Texture2D(size,size,TextureFormat.RGBA32,false,true){name=name,wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Bilinear};t.SetPixels(pixels);t.Apply(false,false);return t;}
            return new[]{Mask("Moss Highland ground role weights",a),Mask("Moss Highland accent role weights",c),Mask("Moss Highland transition distances",transitions)};
        }
    }
}
