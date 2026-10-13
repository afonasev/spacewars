using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
namespace Spacewars.Editor
{
    public static class UnitWreckAssets
    {
        [Serializable] class Catalog{public Model[] models;}
        [Serializable] class Model{public string name;public Role[] materials;}
        [Serializable] class Role{public string name,texture;public float[] baseColor;public float metallic,roughness;}
        public static void Prepare()
        {
            const string source="Assets/Spacewars/Content/Wrecks/",output="Assets/Spacewars/Content/Resources/Wrecks/";
            Directory.CreateDirectory(output);AssetDatabase.Refresh();
            var catalog=JsonUtility.FromJson<Catalog>(File.ReadAllText(source+"materials.json"));
            foreach(var row in catalog.models)
            {
                var imported=AssetDatabase.LoadAssetAtPath<GameObject>(source+row.name+".glb");if(!imported)throw new InvalidOperationException("Wreck GLB import failed: "+row.name);
                var host=new GameObject(row.name);UnityEngine.Object.Instantiate(imported,host.transform,false).name="modelRoot";
                foreach(var r in host.GetComponentsInChildren<MeshRenderer>(true))r.sharedMaterials=r.sharedMaterials.Select(m=>{
                    var role=row.materials.Single(a=>a.name==m.name);string path=output+row.name+"-"+role.name+".mat";
                    var material=AssetDatabase.LoadAssetAtPath<Material>(path);if(!material){material=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(material,path);}
                    var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(source+"Textures/"+role.texture);if(!texture)throw new InvalidOperationException("Missing approved charred texture");
                    material.name=role.name;material.SetColor("_BaseColor",new Color(role.baseColor[0],role.baseColor[1],role.baseColor[2],role.baseColor[3]));material.SetTexture("_BaseMap",texture);
                    material.SetFloat("_Metallic",role.metallic);material.SetFloat("_Smoothness",1-role.roughness);material.SetFloat("_Cull",0);material.SetColor("_EmissionColor",Color.black);material.DisableKeyword("_EMISSION");EditorUtility.SetDirty(material);return material;
                }).ToArray();
                if(host.GetComponentsInChildren<Collider>(true).Length!=0)throw new InvalidOperationException("Wrecks must have no collision");
                PrefabUtility.SaveAsPrefabAsset(host,output+row.name+".prefab");UnityEngine.Object.DestroyImmediate(host);
            }
            AssetDatabase.SaveAssets();Debug.Log("UNIT_WRECK_IMPORT_PASS nine unchanged approved meshes and charred textures");
        }
    }
}
