using System.IO;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEditor;
using UnityEngine;

namespace Spacewars.Editor
{
    public static class EnvironmentArtAssets
    {
        [MenuItem("Spacewars/Environment/Export Natural Frontier Materials")]
        public static void Export()
        {
            const string path="Assets/Spacewars/Content/Environment/NaturalFrontier/Materials";
            Directory.CreateDirectory(path);AssetDatabase.Refresh();
            var profile=EnvironmentArtProfile.Load();
            var shader=Resources.Load<Shader>("TerritoryFog");
            var roles=new[]{"Earth","Concrete","Steel","Rock","Water"};
            for(int i=0;i<roles.Length;i++)
            {
                string assetPath=path+"/"+roles[i]+".mat";
                var material=AssetDatabase.LoadAssetAtPath<Material>(assetPath);
                if(material==null){material=new Material(shader);AssetDatabase.CreateAsset(material,assetPath);}
                EnvironmentArt.Apply(material,profile);material.SetFloat("_SurfaceRole",i+1);
                // Reusable assets start revealed, with no map-specific road or shore mask.
                material.SetTexture("_FogMask",Texture2D.blackTexture);material.SetTexture("_RoadMask",Texture2D.blackTexture);material.SetTexture("_ShoreMask",Texture2D.whiteTexture);material.SetTexture("_RockMask",Texture2D.whiteTexture);
                EditorUtility.SetDirty(material);
            }
            // Reusable, centred modules use the same bounded rock generator as map dressing.
            const string modules="Assets/Spacewars/Content/Environment/NaturalFrontier/Modules";
            Directory.CreateDirectory(modules);AssetDatabase.Refresh();
            var rockMaterial=AssetDatabase.LoadAssetAtPath<Material>(path+"/Rock.mat");
            foreach(var variant in new[]{"BasaltSpur","LayeredOutcrop","WeatheredSlab"})
            {
                float length=variant=="BasaltSpur"?5:variant=="LayeredOutcrop"?8:6;
                float width=variant=="BasaltSpur"?2:variant=="LayeredOutcrop"?5:4;
                float height=variant=="BasaltSpur"?4:variant=="LayeredOutcrop"?3:1;
                var footprint=new NavPolygon(new[]{new NavPoint(-length*.5,-width*.3),new NavPoint(-length*.2,-width*.5),new NavPoint(length*.4,-width*.4),new NavPoint(length*.5,width*.1),new NavPoint(length*.2,width*.5),new NavPoint(-length*.4,width*.4)});
                var mesh=TerrainMesh.ErodedRock(new NavObstacle(footprint),0,height,profile.erosionDepth);
                string meshPath=modules+"/"+variant+".asset";
                var existing=AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
                if(existing==null)AssetDatabase.CreateAsset(mesh,meshPath);
                else {EditorUtility.CopySerialized(mesh,existing);Object.DestroyImmediate(mesh);mesh=existing;}
                var obj=new GameObject(variant);obj.AddComponent<MeshFilter>().sharedMesh=mesh;obj.AddComponent<MeshRenderer>().sharedMaterial=rockMaterial;
                PrefabUtility.SaveAsPrefabAsset(obj,modules+"/"+variant+".prefab");Object.DestroyImmediate(obj);
            }
            foreach(bool stones in new[]{true,false})
            {
                string materialPath=path+(stones?"/Debris.mat":"/Service.mat");
                var detailMaterial=AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if(detailMaterial==null){detailMaterial=new Material(shader);AssetDatabase.CreateAsset(detailMaterial,materialPath);}
                EnvironmentArt.Apply(detailMaterial,profile);detailMaterial.SetFloat("_SurfaceRole",stones?4:3);detailMaterial.SetFloat("_Dressing",stones?1:2);
                detailMaterial.SetTexture("_WeatheringMask",Texture2D.blackTexture);EditorUtility.SetDirty(detailMaterial);
                foreach(string name in stones?new[]{"FracturedBoulder","ScreeFragment","BuriedStone"}:new[]{"VentilationBlock","CableCabinet","PumpNode"})
                {
                    var mesh=stones?EnvironmentDetails.Boulder(name=="FracturedBoulder"?0:name=="ScreeFragment"?1:2):EnvironmentDetails.Service(name);
                    string meshPath=modules+"/"+name+".asset";var existing=AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
                    if(existing==null)AssetDatabase.CreateAsset(mesh,meshPath);
                    else{EditorUtility.CopySerialized(mesh,existing);Object.DestroyImmediate(mesh);mesh=existing;}
                    var obj=new GameObject(name);obj.AddComponent<MeshFilter>().sharedMesh=mesh;obj.AddComponent<MeshRenderer>().sharedMaterial=detailMaterial;
                    PrefabUtility.SaveAsPrefabAsset(obj,modules+"/"+name+".prefab");Object.DestroyImmediate(obj);
                }
            }
            AssetDatabase.SaveAssets();Debug.Log("ENVIRONMENT_MATERIAL_EXPORT_PASS");
        }
        public static void BuildCandidate(){Export();PlayableProject.Build();}
    }
}
