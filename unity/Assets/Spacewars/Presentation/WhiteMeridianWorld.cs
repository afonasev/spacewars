using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
using UnityEngine;
namespace Spacewars.Presentation
{
    public sealed partial class PlayableWorld
    {
        private Material meridianMaterial;
        private WhiteMeridianArtProfile meridianArt;
        private WhiteMeridianArtProfile MeridianArt=>meridianArt??(meridianArt=WhiteMeridianArtProfile.Load());
        private IEnumerator CreateWhiteMeridian(WhiteMeridianMap map)
        {
            var art=MeridianArt;var budget=new LoadingFrameBudget();
            meridianMaterial=art.Material(Fog.Texture,new Vector4((float)map.HalfExtent,(float)map.HalfExtent,0,0),new Color((float)profile.FogTintR,(float)profile.FogTintG,(float)profile.FogTintB));
            meridianRoadMask=MeridianRoadMask(map,art);meridianMaterial.SetTexture("_RoadMask",meridianRoadMask);
            meridianMaterial.SetVectorArray("_Mines",map.Sites(profile).Where(s=>s.Kind==PlayableBuildingKind.Mine).Select(s=>new Vector4((float)s.Position.X,(float)s.Position.Z,0,0)).ToArray());
            // Pigment follows the current section envelopes, including authored spacing/scale.
            // Three entries are the map's structural invariant, not designer art controls.
            meridianMaterial.SetVectorArray("_ScorchSections",map.Wreck.Select(s=>new Vector4(
                (float)((s.MinX+s.MaxX)*.5),(float)((s.MinZ+s.MaxZ)*.5),
                (float)((s.MaxX-s.MinX)*.5),(float)((s.MaxZ-s.MinZ)*.5))).ToArray());
            var basin=map.Supports.First(s=>s.Id=="salt-basin");
            Emit("Meridian basin",TerrainMesh.Prism(basin.Bounds,-1,basin.Height,basin.HeightAt),0,Color.white);
            foreach(int side in new[]{-1,1}){
                Emit("Meridian connected shelf and ramps "+side,MeridianConnectedTerrain(map,art,side),0,Color.white);
                if(budget.Expired){yield return null;budget.Restart();}
            }
            foreach(var site in map.Sites(profile).Where(s=>s.Kind!=PlayableBuildingKind.Mine))CreateMeridianFoundation(map,site,art);
            foreach(var rock in MeridianRockGroups(map)){
                if(Math.Abs(rock.MinX)>30*map.Scale&&Math.Abs(rock.MaxX)>30*map.Scale&&rock.MinZ<0&&rock.MaxZ>0&&Math.Min(Math.Abs(rock.MinX),Math.Abs(rock.MaxX))<40*map.Scale)
                    CreateMeridianCrashShoulder(rock,map,art);
                else Emit("Gypsum ridge",MeridianRock(rock,map,art),1,Color.white);
                if(budget.Expired){yield return null;budget.Restart();}
            }
            CreateMeridianDecor(map,art);
            for(int i=0;i<map.Wreck.Count;i++){CreateMeridianWreck(map.Wreck[i],i,art);if(budget.Expired){yield return null;budget.Restart();}}
        }
        private void MeridianMesh(string name,Mesh mesh,int kind,Color color,Vector4? siteBounds=null)
        {
            terrainMeshes.Add(mesh);var obj=new GameObject(name);obj.transform.SetParent(root,false);obj.AddComponent<MeshFilter>().sharedMesh=mesh;var renderer=obj.AddComponent<MeshRenderer>();renderer.sharedMaterial=meridianMaterial;
            var properties=new MaterialPropertyBlock();properties.SetFloat("_Kind",kind);properties.SetColor("_BaseColor",color);if(siteBounds.HasValue)properties.SetVector("_SiteBounds",siteBounds.Value);renderer.SetPropertyBlock(properties);
        }
        private void Emit(string name,Mesh mesh,int kind,Color color,Vector4? siteBounds=null)=>MeridianMesh(name,mesh,kind,color,siteBounds);
    }
}
