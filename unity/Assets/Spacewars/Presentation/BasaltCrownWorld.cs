using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
using UnityEngine;
namespace Spacewars.Presentation
{
    public sealed partial class PlayableWorld
    {
        private void CreateBasaltCrown(BasaltCrownMap map)
        {
            var art=BasaltCrownArtProfile.Load();
            basaltTerrainMaterial=art.Material(Fog.Texture,new Vector4((float)map.HalfExtent,(float)map.HalfExtent,0,0),new Color((float)profile.FogTintR,(float)profile.FogTintG,(float)profile.FogTintB));
            var masks=BuildBasaltPaintMasks(map,art);basaltPaintMask=masks[0];basaltAccentMask=masks[1];
            basaltTerrainMaterial.SetTexture("_PaintA",basaltPaintMask);basaltTerrainMaterial.SetTexture("_PaintB",basaltAccentMask);
            var mines=BasaltCrownDetails.Mines(map,profile);
            if(mines.Length!=4)throw new System.InvalidOperationException("Basalt Crown requires four mines");
            basaltTerrainMaterial.SetVectorArray("_MineCenters",mines.Select(s=>new Vector4((float)s.Position.X,(float)s.Position.Z,0,0)).ToArray());
            var pads=BasaltCrownDetails.SitePads(map,profile,art);
            var centers=new Vector4[BasaltCrownDetails.MaxPads];var hubs=new Vector4[BasaltCrownDetails.MaxPads];
            for(int i=0;i<pads.Length;i++){centers[i]=new Vector4((float)pads[i].Center.X,(float)pads[i].Center.Z,pads[i].Radius,0);hubs[i]=new Vector4((float)pads[i].Hub.X,(float)pads[i].Hub.Z,0,0);}
            basaltTerrainMaterial.SetInt("_SitePadCount",pads.Length);basaltTerrainMaterial.SetVectorArray("_SitePads",centers);basaltTerrainMaterial.SetVectorArray("_SiteHubs",hubs);
            // Ramp precedence and flat supports come from the navigation provider.
            // Clip the underlying decks around ramps so no flat cap covers the slope.
            foreach(var support in map.Supports){
                var pieces=new List<NavObstacle>{support.Bounds};
                if(support.Id!="citadel-ramp")foreach(var ramp in map.Supports)if(ramp.Id=="citadel-ramp")pieces=Subtract(pieces,ramp.Bounds);
                foreach(var b in pieces)Prism(support.Id,b,-1,support.Height,support.HeightAt);
            }
            foreach(var rock in map.Solids)if(!double.IsNaN(rock.Top))Prism("Basalt ridge",rock,rock.Bottom,rock.Top,null,true);
            var details=BasaltCrownDetails.Plan(map,profile,art);
            foreach(var group in details.GroupBy(p=>p.Type)){
                var mesh=BasaltCrownDetails.Build(group,map);terrainMeshes.Add(mesh);
                var obj=new GameObject("Basalt details — "+group.Key);obj.transform.SetParent(root,false);
                obj.AddComponent<MeshFilter>().sharedMesh=mesh;var renderer=obj.AddComponent<MeshRenderer>();renderer.sharedMaterial=basaltTerrainMaterial;
                var properties=new MaterialPropertyBlock();properties.SetFloat("_RoadSurface",0);properties.SetFloat("_DetailKind",(int)group.Key);renderer.SetPropertyBlock(properties);
            }
            void Prism(string name,NavObstacle shape,double bottom,double top,System.Func<NavPoint,double> height,bool ridge=false){
                var mesh=ridge?TerrainMesh.ErodedRock(shape,bottom,top,art.ridgeErosion):TerrainMesh.Prism(shape,bottom,top,height);terrainMeshes.Add(mesh);
                var obj=new GameObject(name);obj.transform.SetParent(root,false);obj.AddComponent<MeshFilter>().sharedMesh=mesh;
                var renderer=obj.AddComponent<MeshRenderer>();renderer.sharedMaterial=basaltTerrainMaterial;
                var properties=new MaterialPropertyBlock();properties.SetColor("_BaseColor",Color.white);properties.SetFloat("_SurfaceRole",4);properties.SetFloat("_RoadSurface",ridge?0:1);renderer.SetPropertyBlock(properties);
            }
        }
        private Texture2D basaltPaintMask,basaltAccentMask;
        private Material basaltTerrainMaterial;
        public static Texture2D[] BuildBasaltPaintMasks(BasaltCrownMap map,BasaltCrownArtProfile art)
        {
            art.Validate();
            // Fixed bounded raster precision: finer than the authored 2m source cells.
            const int size=512;int length=size*size;float step=(float)(map.HalfExtent*2/size);
            var fields=new float[BasaltCrownArtProfile.Roles.Length][];for(int i=0;i<fields.Length;i++)fields[i]=new float[length];
            for(int i=0;i<length;i++)fields[0][i]=1;
            foreach(var patch in map.Materials){
                int role=BasaltCrownArtProfile.Role(patch.Material);var b=patch.Bounds;
                int x0=Mathf.Clamp(Mathf.CeilToInt((float)(b.MinX+map.HalfExtent)/step-.5f),0,size),x1=Mathf.Clamp(Mathf.CeilToInt((float)(b.MaxX+map.HalfExtent)/step-.5f),0,size);
                int z0=Mathf.Clamp(Mathf.CeilToInt((float)(b.MinZ+map.HalfExtent)/step-.5f),0,size),z1=Mathf.Clamp(Mathf.CeilToInt((float)(b.MaxZ+map.HalfExtent)/step-.5f),0,size);
                for(int z=z0;z<z1;z++)for(int x=x0;x<x1;x++){int i=z*size+x;fields[0][i]=0;fields[role][i]=1;}
            }
            for(int role=0;role<fields.Length;role++)fields[role]=Feather(fields[role],size,Mathf.CeilToInt((role==2?art.roadFeather:art.naturalFeather)/step));
            var a=new Color[length];var c=new Color[length];
            for(int i=0;i<length;i++){
                float total=0;foreach(var f in fields)total+=f[i];total=Mathf.Max(total,.00001f);
                a[i]=new Color(fields[0][i]/total,fields[1][i]/total,fields[2][i]/total,fields[3][i]/total);
                c[i]=new Color(fields[4][i]/total,fields[5][i]/total,fields[6][i]/total,0);
            }
            Texture2D Mask(string name,Color[] pixels){var t=new Texture2D(size,size,TextureFormat.RGBA32,false,true){name=name,wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Bilinear};t.SetPixels(pixels);t.Apply(false,false);return t;}
            return new[]{Mask("Basalt Crown ground role weights",a),Mask("Basalt Crown accent role weights",c)};
        }
        private static float[] Feather(float[] field,int size,int radius)
        {
            var temp=new float[field.Length];var result=new float[field.Length];int count=radius*2+1;
            for(int row=0;row<size;row++){
                float sum=0;for(int k=-radius;k<=radius;k++)sum+=field[row*size+Mathf.Clamp(k,0,size-1)];
                for(int x=0;x<size;x++){temp[row*size+x]=sum/count;sum+=field[row*size+Mathf.Clamp(x+radius+1,0,size-1)]-field[row*size+Mathf.Clamp(x-radius,0,size-1)];}
            }
            for(int col=0;col<size;col++){
                float sum=0;for(int k=-radius;k<=radius;k++)sum+=temp[Mathf.Clamp(k,0,size-1)*size+col];
                for(int z=0;z<size;z++){result[z*size+col]=sum/count;sum+=temp[Mathf.Clamp(z+radius+1,0,size-1)*size+col]-temp[Mathf.Clamp(z-radius,0,size-1)*size+col];}
            }
            return result;
        }
        private static List<NavObstacle> Subtract(List<NavObstacle> pieces,NavObstacle cut){
            var result=new List<NavObstacle>();
            foreach(var b in pieces){
                double x=System.Math.Max(b.MinX,cut.MinX),z=System.Math.Max(b.MinZ,cut.MinZ),xx=System.Math.Min(b.MaxX,cut.MaxX),zz=System.Math.Min(b.MaxZ,cut.MaxZ);
                if(x>=xx||z>=zz){result.Add(b);continue;}
                if(b.MinX<x)result.Add(new NavObstacle(b.MinX,b.MinZ,x,b.MaxZ));
                if(xx<b.MaxX)result.Add(new NavObstacle(xx,b.MinZ,b.MaxX,b.MaxZ));
                if(b.MinZ<z)result.Add(new NavObstacle(x,b.MinZ,xx,z));
                if(zz<b.MaxZ)result.Add(new NavObstacle(x,zz,xx,b.MaxZ));
            }
            return result;
        }
    }
}
