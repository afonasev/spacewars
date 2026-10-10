using System;
using UnityEditor;
using UnityEngine;

namespace Spacewars.Editor
{
    public static class RallyFlagImportProject
    {
        public static void Prepare()
        {
            const string source="Assets/Spacewars/Content/StyleA/";
            const string resources="Assets/Spacewars/Content/Resources/StyleA/";
            AssetDatabase.ImportAsset(source+"rallyFlag.glb",ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.ImportAsset(source+"rallyFlag-detail.png",ImportAssetOptions.ForceSynchronousImport);
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(source+"rallyFlag.glb");
            if(!prefab)throw new InvalidOperationException("Approved rally GLB did not import");
            var model=UnityEngine.Object.Instantiate(prefab);model.name="rallyFlag";
            try
            {
                foreach(var renderer in model.GetComponentsInChildren<Renderer>(true))
                {
                    var materials=renderer.sharedMaterials;
                    for(int i=0;i<materials.Length;i++)
                    {
                        string role=materials[i].name;
                        string path=resources+"rallyFlag-"+role+".mat";
                        var material=AssetDatabase.LoadAssetAtPath<Material>(path);
                        if(!material)
                        {
                            // Use the established native painted-metal treatment for the web model's roles.
                            var template=AssetDatabase.LoadAssetAtPath<Material>(resources+"headquarters-"+role+".mat");
                            if(!template)throw new InvalidOperationException("Missing native material role: "+role);
                            material=new Material(template){name=role};
                            if(material.HasProperty("_DetailNormalMap"))material.SetTexture("_DetailNormalMap",AssetDatabase.LoadAssetAtPath<Texture2D>(source+"rallyFlag-detail.png"));
                            AssetDatabase.CreateAsset(material,path);
                        }
                        materials[i]=material;
                    }
                    renderer.sharedMaterials=materials;
                }
                PrefabUtility.SaveAsPrefabAsset(model,resources+"rallyFlag.prefab");AssetDatabase.SaveAssets();
            }
            finally{UnityEngine.Object.DestroyImmediate(model);}
            Debug.Log("RALLY_FLAG_IMPORT_PASS");
        }
    }
}
