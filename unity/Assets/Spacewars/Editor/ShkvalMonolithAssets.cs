using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;
namespace Spacewars.Editor
{
 public static class ShkvalMonolithAssets
 {
  const string Folder="Assets/Spacewars/Content/Resources/StyleA/";
  public static void Prepare()
  {
   foreach(var key in new[]{"shkval","shkval-guidance-3"})
   {
    string path="Assets/Spacewars/Content/StyleA/"+key+".glb";
    AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
    var source=AssetDatabase.LoadAssetAtPath<GameObject>(path);if(!source)throw new Exception("Missing Shkval GLB");
    var model=new GameObject(key);var authored=Object.Instantiate(source);authored.name="modelRoot";authored.transform.SetParent(model.transform,false);
    try{
     foreach(var r in model.GetComponentsInChildren<Renderer>(true))r.sharedMaterials=r.sharedMaterials.Select(Material).ToArray();
     var nodes=model.GetComponentsInChildren<Transform>(true);
     foreach(var name in new[]{"modelRoot","turretYaw","launcherPitch","launchCarriage"})if(nodes.Count(t=>t.name==name)!=1)throw new Exception("Invalid pivot "+name);
     if(nodes.Count(t=>t.name.StartsWith("wheel")&&t.childCount>0)!=6)throw new Exception("Six articulated wheels required");
     if(nodes.Count(t=>t.name.StartsWith("muzzle"))!=4)throw new Exception("Four muzzle anchors required");
     if(model.GetComponentsInChildren<Collider>(true).Length!=0)throw new Exception("Model cannot add collision");
     PrefabUtility.SaveAsPrefabAsset(model,Folder+key+".prefab");
    }finally{Object.DestroyImmediate(model);}
   }
   UnitModelResources.ReuseExactVariantMeshes();
   AssetDatabase.SaveAssets();Debug.Log("SHKVAL_MONOLITH_IMPORT_PASS");
  }
  static Material Material(Material source)
  {
   string role=source.name;
   var path=Folder+"shkval-"+role+".mat";
   var m=AssetDatabase.LoadAssetAtPath<Material>(path);
   if(!m){m=new Material(Shader.Find("Universal Render Pipeline/Lit")){name=role};AssetDatabase.CreateAsset(m,path);}
   m.name=role;
   // Open bore liners and both cabin sides must render from the cavity-facing side.
   // Match the established Shkval double-sided surface contract.
   m.SetFloat("_Cull",0);m.doubleSidedGI=true;
   if(role=="team-primary"||role=="neutral-metal"||role=="structure"||role=="system-emissive")
   {
    var shared=AssetDatabase.LoadAssetAtPath<Material>(Folder+"explorer-"+role+".mat");
    if(!shared)throw new Exception("Missing production surface "+role);
    m.CopyPropertiesFromMaterial(shared);m.name=role;EditorUtility.SetDirty(m);return m;
   }
   Color color;float metallic,smoothness;
   switch(role){
    case "team-primary":color=new Color(.065f,.265f,.32f);metallic=.12f;smoothness=.28f;break;
    case "neutral-metal":color=new Color(.52f,.57f,.60f);metallic=.4f;smoothness=.42f;break;
    case "structure":color=new Color(.065f,.085f,.10f);metallic=.3f;smoothness=.25f;break;
    case "rubber":color=new Color(.027f,.034f,.039f);metallic=0;smoothness=.12f;break;
    case "canopy":color=new Color(.055f,.115f,.16f);metallic=.35f;smoothness=.75f;break;
    case "system-emissive":color=new Color(.06f,.75f,.95f);metallic=.2f;smoothness=.24f;break;
    default:throw new Exception("Unknown Shkval role "+role);
   }
   m.SetColor("_BaseColor",color);m.SetColor("_Color",color);m.SetFloat("_Metallic",metallic);m.SetFloat("_Smoothness",smoothness);
   if(role=="system-emissive"){m.EnableKeyword("_EMISSION");m.SetColor("_EmissionColor",color*2);}
   EditorUtility.SetDirty(m);
   return m;
  }
 }
}
