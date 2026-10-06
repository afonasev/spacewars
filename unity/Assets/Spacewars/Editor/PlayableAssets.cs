using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Spacewars.Editor
{
    // Content adaptation only: geometry and named pivots come unchanged from the web GLBs.
    public static class PlayableAssets
    {
        [Serializable] private class Catalog { public Model[] models; }
        [Serializable] private class Model { public string name; public Role[] materials; }
        [Serializable] private class Role { public string name; public float[] baseColor,emission; public float metallic,roughness; }
        private const string Source="Assets/Spacewars/Content/StyleA/", Output="Assets/Spacewars/Content/Resources/StyleA/";
        public static void Prepare()
        {
            Directory.CreateDirectory(Output);AssetDatabase.Refresh();
            var data=JsonUtility.FromJson<Catalog>(File.ReadAllText(Source+"materials.json"));
            var orm=Texture("style-a-painted-metal-orm",false,true);
            var normal=Texture("style-a-painted-metal-normal",false,false,true);
            foreach(var model in data.models)
            {
                var source=AssetDatabase.LoadAssetAtPath<GameObject>(Source+model.name+".glb");
                if(!source)throw new InvalidOperationException("GLB failed to import: "+model.name);
                var instance=new GameObject(model.name);
                var importedRoot=UnityEngine.Object.Instantiate(source,instance.transform,false);
                // glTFast flattens the single-node scene; restore its authored node name.
                // An outer wrapper keeps imported ground offset inside the uniform scale.
                importedRoot.name="modelRoot";
                var detail=Texture((model.name=="shkval-guidance-3"?"shkval":model.name)+"-detail",false,false,true,true);
                foreach(var renderer in instance.GetComponentsInChildren<MeshRenderer>(true))
                {
                    renderer.sharedMaterials=renderer.sharedMaterials.Select(imported=>{
                        var matches=model.materials.Where(r=>r.name==imported.name).ToArray();
                        // Original upgraded Explorer GLB contains two distinct neutral-metal roles.
                        // glTFast preserves metallicFactor/roughnessFactor; do not collapse by name.
                        var role=matches.Length==1?matches[0]:matches.Single(r=>Math.Abs(r.metallic-imported.GetFloat("metallicFactor"))<1e-5&&Math.Abs(r.roughness-imported.GetFloat("roughnessFactor"))<1e-5);
                        string roleKey=role.name+(matches.Length>1?"-"+Array.IndexOf(model.materials,role):"");
                        string path=Output+model.name+"-"+roleKey+".mat";
                        var material=AssetDatabase.LoadAssetAtPath<Material>(path);
                        if(!material){material=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(material,path);}
                        material.name=role.name;material.SetColor("_BaseColor",new Color(role.baseColor[0],role.baseColor[1],role.baseColor[2],role.baseColor[3]));
                        material.SetFloat("_Cull",0);material.SetFloat("_Metallic",role.metallic);material.SetFloat("_Smoothness",1-role.roughness);
                        var emission=new Color(role.emission[0],role.emission[1],role.emission[2]);material.SetColor("_EmissionColor",emission);
                        if(role.name=="foundation-concrete")
                        {
                            material.SetColor("_BaseColor",new Color(70/255f,85/255f,91/255f));material.SetFloat("_Metallic",.03f);material.SetFloat("_Smoothness",.06f);
                            emission=new Color(10/255f,14/255f,16/255f)*.06f;material.SetColor("_EmissionColor",emission);
                        }
                        if(emission.maxColorComponent>0)
                        {
                            // URP Lit derives _EMISSION from GI flags when it validates material keywords.
                            // Keep the saved asset and future Prepare runs consistent through import/build.
                            material.globalIlluminationFlags=MaterialGlobalIlluminationFlags.BakedEmissive;
                            material.EnableKeyword("_EMISSION");
                        }
                        if(role.name=="structure"||role.name=="neutral-metal"||role.name=="team-primary")
                        {
                            material.SetTexture("_BaseMap",Texture("style-a-painted-metal-"+(role.name=="structure"?"structure-base-color":"base-color"),true));
                            material.SetTexture("_BumpMap",normal);material.SetFloat("_BumpScale",.16f);material.EnableKeyword("_NORMALMAP");
                            // Original bump detail is imported as a secondary tangent normal for URP.
                            material.SetTexture("_DetailNormalMap",detail);material.SetFloat("_DetailNormalMapScale",1);material.SetFloat("_DetailAlbedoMapScale",0);material.EnableKeyword("_DETAIL_SCALED");
                            var pixels=orm.GetPixels();for(int i=0;i<pixels.Length;i++){var p=pixels[i];pixels[i]=new Color(p.b*role.metallic,p.r,0,1-p.g*role.roughness);}
                            var packed=new Texture2D(orm.width,orm.height,TextureFormat.RGBA32,true,true);packed.SetPixels(pixels);packed.Apply();
                            string mapPath=Output+model.name+"-"+roleKey+"-mask.png";File.WriteAllBytes(mapPath,packed.EncodeToPNG());UnityEngine.Object.DestroyImmediate(packed);
                            AssetDatabase.ImportAsset(mapPath);var importer=(TextureImporter)AssetImporter.GetAtPath(mapPath);importer.sRGBTexture=false;importer.SaveAndReimport();
                            material.SetTexture("_MetallicGlossMap",AssetDatabase.LoadAssetAtPath<Texture2D>(mapPath));material.SetFloat("_Smoothness",1);material.EnableKeyword("_METALLICSPECGLOSSMAP");
                            material.SetTexture("_OcclusionMap",AssetDatabase.LoadAssetAtPath<Texture2D>(mapPath));material.SetFloat("_OcclusionStrength",1);material.EnableKeyword("_OCCLUSIONMAP");
                        }
                        EditorUtility.SetDirty(material);return material;
                    }).ToArray();
                }
                if(!instance.GetComponentsInChildren<Transform>(true).Any(t=>t.name=="modelRoot"))throw new InvalidOperationException("Missing modelRoot: "+string.Join(",",instance.GetComponentsInChildren<Transform>(true).Select(t=>t.name)));
                if(model.name=="tank"&&!instance.GetComponentsInChildren<Transform>(true).Any(t=>t.name=="turretYaw"))throw new InvalidOperationException("Missing turretYaw");
                PrefabUtility.SaveAsPrefabAsset(instance,Output+model.name+".prefab");UnityEngine.Object.DestroyImmediate(instance);
            }
            AssetDatabase.SaveAssets();Debug.Log("PLAYABLE_STYLE_A_PASS approved original models with URP materials");
        }
        private static Texture2D Texture(string name,bool srgb,bool readable=false,bool normal=false,bool height=false)
        {
            string path=Source+name+".png";var importer=(TextureImporter)AssetImporter.GetAtPath(path);
            importer.sRGBTexture=srgb;importer.isReadable=readable;
            if(normal){importer.textureType=TextureImporterType.NormalMap;importer.convertToNormalmap=height;if(height)importer.heightmapScale=.008f;}
            importer.SaveAndReimport();return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
    }
}
