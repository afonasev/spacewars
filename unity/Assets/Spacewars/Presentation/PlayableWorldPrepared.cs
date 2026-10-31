using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using Spacewars.Simulation;
using UnityEngine;
namespace Spacewars.Presentation
{
    public sealed partial class PlayableWorld
    {
        PreparedSceneInstance preparedScene;
        public bool PreparationCacheHit{get;private set;}
        static readonly string[] PreparedMaterials={"material","dockMaterial","dockSpaceMaterial","groveMaterial","meridianMaterial","mossTerrainMaterial","basaltTerrainMaterial"};
        static readonly string[] PreparedTextures={"groveSurfaceMask","meridianRoadMask","mossPaintMask","mossAccentMask","mossTransitionMask","basaltPaintMask","basaltAccentMask","foundryRoadMask","foundryLavaHeat","foundryRouteMask"};
        static FieldInfo PreparedField(string name)=>typeof(PlayableWorld).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic);
        IEnumerator PrepareTerrainAndDetail(MossHighlandArtProfile art,NullDockArtProfile dockArt)
        {
            yield return PrepareWreckResources();
            string group="World-"+PreparedMapCache.MapKey(profile.AuthoredMap);
            string key=PreparedMapCache.Key(profile.AuthoredMap,profile,new{moss=art==null?null:JsonUtility.ToJson(art),dock=dockArt==null?null:JsonUtility.ToJson(dockArt),lighting=JsonUtility.ToJson(IndustrialLightingProfile.Load(profile.AuthoredMap?.Id))});
            var bytes=PreparedMapCache.Read(group,key);PreparedScene data=null;
            if(bytes!=null){try{data=PreparedSceneCodec.Decode(bytes);}catch(Exception e)when(e is IOException||e is InvalidDataException||e is FormatException||e is ArgumentException){Debug.LogWarning("Discarded prepared scene: "+e.Message);}}
            bytes=null; // Decoded buffers are independently owned; release the envelope immediately.
            if(data!=null){
                preparedScene=new PreparedSceneInstance();yield return preparedScene.Restore(data,root,material,Fog.Texture);
                foreach(var binding in data.materialFields??Array.Empty<PreparedBinding>())if(binding.name!="material")PreparedField(binding.name).SetValue(this,preparedScene.Materials[binding.index]);
                foreach(var binding in data.textureFields??Array.Empty<PreparedBinding>())PreparedField(binding.name).SetValue(this,preparedScene.Textures[binding.index]);
                if(data.hasCrossings)surfaceMaterials=ThreeCrossingsMaterials.RestorePrepared(data.crossings,preparedScene.Textures,profile);
                if(!string.IsNullOrEmpty(data.dockMetrics))DockMetrics=JsonUtility.FromJson<NullDockMeshMetrics>(data.dockMetrics);
                if(data.gate>=0)campaignGateVisual=preparedScene.Nodes[data.gate].gameObject;
                sceneryDetail.AddRange((data.worldLods??Array.Empty<int>()).Select(i=>preparedScene.Nodes[i].GetComponent<ModelDetailLod>()));PreparationCacheHit=true;data=null;yield break;
            }
            PreparedMapCache.PrimeResources(group);
            yield return GenerateTerrainAndDetail(art,dockArt);
            try{
                data=PreparedSceneCodec.Capture(root,material,Fog.Texture,(scene,textureId,materialId)=>{
                    scene.worldLods=sceneryDetail.Select(l=>Array.FindIndex(root.GetComponentsInChildren<Transform>(true),t=>t==l.transform)).ToArray();
                    scene.materialFields=PreparedMaterials.Select(name=>(name,value:PreparedField(name).GetValue(this) as Material)).Where(v=>v.value).Select(v=>new PreparedBinding{name=v.name,index=materialId(v.value)}).ToArray();
                    scene.textureFields=PreparedTextures.Select(name=>(name,value:PreparedField(name).GetValue(this) as Texture2D)).Where(v=>v.value).Select(v=>new PreparedBinding{name=v.name,index=textureId(v.value)}).ToArray();
                    if(surfaceMaterials!=null){scene.hasCrossings=true;scene.crossings=new PreparedSurface{profile=PreparedSceneCodec.Json(surfaceMaterials.Profile),art=PreparedSceneCodec.Json(surfaceMaterials.Art),textures=new[]{surfaceMaterials.RoadMask,surfaceMaterials.ShoreMask,surfaceMaterials.RockMask,surfaceMaterials.ConstructionMask,surfaceMaterials.WeatheringMask}.Select(t=>textureId(t)).ToArray(),mines=surfaceMaterials.MineAccesses.Select(a=>new PreparedMineAccess{site=a.Site.Id,cx=a.Connection.X,cz=a.Connection.Z,ex=a.Entry.X,ez=a.Entry.Z}).ToArray(),accesses=surfaceMaterials.HangarAccesses.Select(a=>new PreparedAccess{group=a.Group,gate=a.Gate,direction=a.Direction,points=a.Points}).ToArray()};}
                    if(DockMetrics!=null)scene.dockMetrics=JsonUtility.ToJson(DockMetrics);
                    if(campaignGateVisual)scene.gate=Array.FindIndex(root.GetComponentsInChildren<Transform>(true),t=>t.gameObject==campaignGateVisual);
                });
                PreparedMapCache.Write(group,key,PreparedSceneCodec.Encode(key,data));
            }catch(Exception e)when(e is IOException||e is InvalidDataException||e is FormatException||e is ArgumentException||e is UnityException){Debug.LogWarning("Environment prepared without persistent cache: "+e.Message);}
            finally{data=null;foreach(string name in PreparedTextures)PreparedMapCache.ForgetPixels(PreparedField(name).GetValue(this) as Texture2D);}
        }
        void ForgetPreparedFields()
        {
            foreach(string name in PreparedMaterials)if(name!="material")PreparedField(name).SetValue(this,null);
            foreach(string name in PreparedTextures)PreparedField(name).SetValue(this,null);
            surfaceMaterials=null;terrainMeshes.Clear();sceneryDetail.Clear();
        }
    }
}
