using System;
using System.IO;
using System.Linq;
using Spacewars.Presentation;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Spacewars.Editor
{
    // Isolated candidate and review scene; production Explorer bindings stay unchanged.
    public static class ExplorerTwinCannonAssets
    {
        const string Content="Assets/Spacewars/Content/StyleA/";
        const string Resources="Assets/Spacewars/Content/Resources/StyleA/";
        const string Scene=Content+"Preview/ExplorerTwinCannon.unity";
        const string Evidence="../.local/explorer-twin-cannon";
        public static void Build()
        {
            Directory.CreateDirectory(Evidence);
            string source=Content+"explorer-twin-cannon.glb";
            AssetDatabase.ImportAsset(source,ImportAssetOptions.ForceSynchronousImport);
            var model=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(source));
            model.name="explorer-twin-cannon";
            try
            {
                foreach(var r in model.GetComponentsInChildren<Renderer>())
                {
                    var materials=r.sharedMaterials;
                    for(int i=0;i<materials.Length;i++)
                    {
                        string role=materials[i].name.Split('.')[0];
                        var material=AssetDatabase.LoadAssetAtPath<Material>(Resources+"explorer-"+role+".mat");
                        // Gun armor UVs now use the original hull paint swatch, so both
                        // share the exact same production material, textures and finish.
                        if(role=="weapon-steel"||role=="weapon-bore")
                        {
                            string path=Resources+"explorer-twin-"+role+".mat";
                            material=AssetDatabase.LoadAssetAtPath<Material>(path);
                            if(!material){material=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(material,path);}
                            material.name="explorer-twin-"+role;
                            material.SetColor("_BaseColor",role=="weapon-steel"?new Color(.52f,.58f,.62f):new Color(.008f,.012f,.016f));
                            material.SetFloat("_Metallic",role=="weapon-steel"?.7f:.05f);
                            material.SetFloat("_Smoothness",role=="weapon-steel"?.5f:.15f);
                            EditorUtility.SetDirty(material);
                        }
                        if(!material)throw new Exception("Missing production material "+role);
                        materials[i]=material;
                    }
                    r.sharedMaterials=materials;
                }
                var nodes=model.GetComponentsInChildren<Transform>();
                Transform Node(string name)=>nodes.Single(t=>t.name==name);
                var yaw=Node("turretYaw");var pitch=Node("weaponPitch");var muzzle=Node("muzzle");
                var hullPaint=Node("modelRoot:team-primary").GetComponent<Renderer>().sharedMaterial;
                foreach(var armor in nodes.Where(t=>t.name.StartsWith("Faceted side armor")||t.name.StartsWith("Top armor ridge")))
                    if(armor.GetComponent<Renderer>().sharedMaterial!=hullPaint)throw new Exception("Gun armor does not share hull material");
                if(pitch.parent!=yaw||muzzle.parent!=pitch||Node("muzzleLeft").parent!=pitch||Node("muzzleRight").parent!=pitch)throw new Exception("Invalid gun hierarchy");
                var bearing=Node("Wide turret bearing");var rest=bearing.position;
                pitch.localRotation=Quaternion.Euler(-90,0,0);
                if(Vector3.Dot(muzzle.forward,Vector3.up)<.999f||(bearing.position-rest).sqrMagnitude>1e-8f)throw new Exception("Elevation moved bearing or failed vertical aim");
                pitch.localRotation=Quaternion.identity;
                foreach(float angle in new[]{0f,90f,180f,270f,360f}){yaw.localRotation=Quaternion.Euler(0,angle,0);if(!float.IsFinite(muzzle.position.sqrMagnitude))throw new Exception("Invalid yaw");}
                yaw.localRotation=Quaternion.identity;
                int tris=0;
                foreach(var filter in model.GetComponentsInChildren<MeshFilter>())
                {
                    var mesh=filter.sharedMesh;tris+=mesh.triangles.Length/3;
                    if(mesh.vertexCount!=mesh.normals.Length||mesh.vertexCount!=mesh.uv.Length)throw new Exception("Missing vertex attributes");
                    if(mesh.vertices.Any(v=>!float.IsFinite(v.sqrMagnitude)))throw new Exception("Non-finite geometry");
                }
                if(tris>30000||model.GetComponentsInChildren<Collider>().Length!=0)throw new Exception("Invalid geometry budget/colliders");
                PrefabUtility.SaveAsPrefabAsset(model,Resources+"explorer-twin-cannon.prefab");
                File.WriteAllText(Evidence+"/import.txt","PASS UV/normals, finite vertices, yaw, vertical pitch, stationary bearing, muzzle hierarchy; triangles="+tris+"\n");
            }
            finally{UnityEngine.Object.DestroyImmediate(model);}
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            new GameObject("Explorer weapon review only").AddComponent<ExplorerTwinCannonPreview>();
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(),Scene);
            var product=PlayerSettings.productName;var id=PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Standalone);
            try
            {
                PlayerSettings.productName="Spacewars Explorer Weapon Review";
                PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone,"local.spacewars.explorerweaponreview");
                AssetDatabase.SaveAssets();
                var result=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{Scene},locationPathName=Evidence+"/ExplorerWeaponReview.app",target=BuildTarget.StandaloneOSX,options=BuildOptions.Development});
                if(result.summary.result!=BuildResult.Succeeded)throw new Exception(result.summary.result.ToString());
                Debug.Log("EXPLORER_TWIN_CANNON_BUILD_PASS");
            }
            finally{PlayerSettings.productName=product;PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone,id);AssetDatabase.SaveAssets();}
        }
    }
}
