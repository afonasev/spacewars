using System;
using System.IO;
using System.Linq;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace Spacewars.Editor
{
    public static class FactoryModelAssets
    {
        const string Source = "Assets/Spacewars/Content/StyleA/factory-armored.glb";
        const string Folder = "Assets/Spacewars/Content/Resources/StyleA/";
        const string ScenePath = "Assets/Spacewars/Content/StyleA/Preview/FactoryArmoredContext.unity";
        static readonly Color Owner = new Color(.19f, .72f, .77f);
        static string Evidence => Path.GetFullPath("../design/factory-armored/previews");

        [MenuItem("Spacewars/Assets/Factory A/Prepare and Render Context")]
        public static void Prepare()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Directory.CreateDirectory(Evidence);
            AssetDatabase.ImportAsset(Source, ImportAssetOptions.ForceSynchronousImport);
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(Source);
            if (!source) throw new InvalidOperationException("Factory GLB import failed");
            var model = Object.Instantiate(source); model.name = "factory-armored";
            try
            {
                foreach (var r in model.GetComponentsInChildren<Renderer>(true))
                {
                    var materials = r.sharedMaterials;
                    for (int i = 0; i < materials.Length; i++)
                    {
                        string role = materials[i].name;
                        string path = Folder + "factory-armored-" + role + ".mat";
                        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                        if (!material)
                        {
                            material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "factory-armored-" + role };
                            Color color = role switch
                            {
                                "team-primary" => Owner,
                                "armor" => new Color(.43f, .46f, .45f),
                                "structure" => new Color(.07f, .085f, .10f),
                                "neutral-metal" => new Color(.27f, .31f, .33f),
                                "rubber" => new Color(.025f, .03f, .035f),
                                "warning" => Owner,
                                "work-emissive" => Owner,
                                "lamp-emissive" => new Color(1, .65f, .28f),
                                _ => throw new InvalidOperationException("Unknown material role " + role)
                            };
                            material.SetColor("_BaseColor", color);
                            material.SetFloat("_Metallic", role == "neutral-metal" ? .45f : .12f);
                            material.SetFloat("_Smoothness", .28f);
                            if (role.EndsWith("emissive")) { material.EnableKeyword("_EMISSION"); material.SetColor("_EmissionColor", role == "work-emissive" ? Color.black : color * 1.5f); }
                            AssetDatabase.CreateAsset(material, path);
                        }
                        // Reuse the actual current building surface stack, including albedo,
                        // normal/detail normal and packed mask maps, on the newly authored UVs.
                        string templateRole = role == "armor" ? "team-primary" : role;
                        if (templateRole == "team-primary" || templateRole == "neutral-metal" || templateRole == "structure")
                        {
                            var template = AssetDatabase.LoadAssetAtPath<Material>(Folder + "factory-" + templateRole + ".mat");
                            Require(template, "Missing current building material " + templateRole);
                            material.CopyPropertiesFromMaterial(template);
                            material.name = "factory-armored-" + role;
                            if (role == "armor")
                            {
                                material.SetColor("_BaseColor", new Color(.70f,.73f,.72f));
                                var finish=ActorMaterialProfile.Load();
                                material.SetFloat("_Metallic",finish.armorMetallic);
                                material.SetFloat("_Smoothness",finish.armorSmoothness);
                            }
                            Require(material.GetTexture("_BaseMap") == template.GetTexture("_BaseMap"), "Surface texture mismatch");
                            EditorUtility.SetDirty(material);
                        }
                        // Exterior lamps and concealed process flashes both follow the owner hue.
                        if (role == "lamp-emissive")
                        {
                            material.name = "factory-armored-lamp-team-emissive";
                            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
                            material.EnableKeyword("_EMISSION"); EditorUtility.SetDirty(material);
                        }
                        if (role == "work-emissive")
                        {
                            material.SetColor("_BaseColor", Owner);
                            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
                            material.EnableKeyword("_EMISSION"); EditorUtility.SetDirty(material);
                        }
                        if (role == "warning")
                        {
                            material.name = "factory-armored-threshold-team-primary";
                            material.SetColor("_BaseColor", Owner); EditorUtility.SetDirty(material);
                        }
                        materials[i] = material;
                    }
                    r.sharedMaterials = materials;
                }
                ValidateModel(model);
                model.AddComponent<FactoryModelPresentation>();
                PrefabUtility.SaveAsPrefabAsset(model, Folder + "factory-armored.prefab");
                AssetDatabase.SaveAssets();
            }
            finally { Object.DestroyImmediate(model); }
            Render();
        }

        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        static void ValidateModel(GameObject model)
        {
            Require(model.GetComponentsInChildren<Collider>(true).Length == 0, "Visual candidate cannot add collision");
            var nodes = model.GetComponentsInChildren<Transform>(true);
            foreach (string name in Enumerable.Range(0,6).Select(i=>"doorSection"+i).Concat(new[]{"fan0","fan1","smoke0","smoke1","workGlow0","workGlow1","workGlow2","gateClearanceTop","gateFloor","gateLeft","gateRight","gateRollerStore"}))
                Require(nodes.Count(n=>n.name==name)==1,"Missing/duplicate articulated node "+name);
            var meshes = model.GetComponentsInChildren<MeshFilter>(true);
            Require(meshes.Length > 0, "No mesh");
            foreach (var m in meshes) Require(m.sharedMesh && m.sharedMesh.vertexCount>0 && m.sharedMesh.uv.Length==m.sharedMesh.vertexCount && float.IsFinite(m.sharedMesh.bounds.size.sqrMagnitude),"Invalid mesh/UV "+m.name);
            var renderers=model.GetComponentsInChildren<Renderer>();var bounds=renderers[0].bounds;
            foreach(var r in renderers.Skip(1))bounds.Encapsulate(r.bounds);
            Require(Mathf.Abs(bounds.size.y-2.289068f)<.003f,"Source height mismatch: "+bounds);
            Require(Mathf.Abs(bounds.size.x-2.978701f)<.003f&&bounds.size.z<3.5f,"Candidate exceeds expected visual envelope");
            // Opaque inner roof is part of shell-structure; the source has no production vehicle.
            Require(nodes.Any(n=>n.name=="shell-structure") && nodes.Any(n=>n.name=="shell-armor"),"Opaque shell missing");
            File.WriteAllText(Path.Combine(Evidence,"unity-import.json"),JsonUtility.ToJson(new ImportReport{unity=Application.unityVersion,meshes=meshes.Length,triangles=meshes.Sum(m=>m.sharedMesh.triangles.Length/3),dimensions=bounds.size,result="PASS"},true));
        }
        [Serializable] class ImportReport { public string unity,result;public int meshes,triangles;public Vector3 dimensions; }

        static GameObject Spawn(string key, Vector3 position, float scale, Color owner)
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Folder+key+".prefab");
            Require(prefab,"Missing current asset "+key);
            var go=(GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.name=key;go.transform.position=position;go.transform.localScale=Vector3.one*scale;
            Paint(go,owner);return go;
        }
        [Serializable] class ClearanceReport { public string result,unit;public float unitHeight,clearHeight,roofGap,sideGap,interiorGap; }
        static Bounds MeshBounds(GameObject go)
        {
            var renderers=go.GetComponentsInChildren<MeshRenderer>();var bounds=renderers[0].bounds;
            foreach(var r in renderers.Skip(1))bounds.Encapsulate(r.bounds);
            return bounds;
        }
        static float BayCeiling(GameObject factory, Vector3 floor, float halfWidth)
        {
            float minimum=float.PositiveInfinity;
            var surfaces=factory.GetComponentsInChildren<MeshFilter>()
                .Where(m=>m.transform.parent.name=="modelRoot").ToArray();
            // Vertical rays over the unit's swept corridor find the first actual overhead
            // triangle above the floor, even if it would intersect the unit's roof.
            for(int x=0;x<5;x++) for(int z=0;z<9;z++)
            {
                Vector2 point=new Vector2(Mathf.Lerp(-halfWidth,halfWidth,x/4f),Mathf.Lerp(factory.transform.position.z-.65f,floor.z,z/8f));
                float ceiling=float.PositiveInfinity;
                foreach(var mesh in surfaces)
                {
                    var vertices=mesh.sharedMesh.vertices.Select(v=>mesh.transform.TransformPoint(v)).ToArray();
                    var indices=mesh.sharedMesh.triangles;
                    for(int i=0;i<indices.Length;i+=3)
                    {
                        var a=vertices[indices[i]];var b=vertices[indices[i+1]];var c=vertices[indices[i+2]];
                        float denominator=(b.z-c.z)*(a.x-c.x)+(c.x-b.x)*(a.z-c.z);
                        if(Mathf.Abs(denominator)<.000001f)continue;
                        float u=((b.z-c.z)*(point.x-c.x)+(c.x-b.x)*(point.y-c.z))/denominator;
                        float v=((c.z-a.z)*(point.x-c.x)+(a.x-c.x)*(point.y-c.z))/denominator;
                        if(u<0||v<0||u+v>1)continue;
                        float y=u*a.y+v*b.y+(1-u-v)*c.y;
                        if(y>floor.y+.03f)ceiling=Mathf.Min(ceiling,y);
                    }
                }
                Require(float.IsFinite(ceiling),"Missing roof above clearance corridor");
                minimum=Mathf.Min(minimum,ceiling);
            }
            return minimum;
        }
        static void Paint(GameObject go,Color owner)
        {
            var finish=ActorMaterialProfile.Load();
            foreach(var r in go.GetComponentsInChildren<Renderer>(true))
                for(int i=0;i<r.sharedMaterials.Length;i++)finish.Apply(r,i,owner);
        }
        static Camera Setup()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var ground=GameObject.CreatePrimitive(PrimitiveType.Plane);ground.name="Current ground texture / asset context";
            Object.DestroyImmediate(ground.GetComponent<Collider>());ground.transform.localScale=Vector3.one*4;
            ground.transform.position=new Vector3(0,-.015f,0);
            const string path=Folder+"factory-preview-ground.mat";
            var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!mat){mat=new Material(Shader.Find("Universal Render Pipeline/Lit"));mat.SetColor("_BaseColor",new Color(.37f,.40f,.36f));mat.SetFloat("_Smoothness",.1f);
                var earth=Resources.Load<Texture2D>("Environment/NaturalFrontier/Earth");if(earth){mat.SetTexture("_BaseMap",earth);mat.SetTextureScale("_BaseMap",new Vector2(12,12));}AssetDatabase.CreateAsset(mat,path);}
            ground.GetComponent<Renderer>().sharedMaterial=mat;
            RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.52f,.55f,.60f);
            var key=new GameObject("Preview daylight").AddComponent<Light>();key.type=LightType.Directional;key.intensity=1.8f;key.shadows=LightShadows.Soft;key.transform.rotation=Quaternion.Euler(50,-35,0);
            var fill=new GameObject("Preview fill").AddComponent<Light>();fill.type=LightType.Directional;fill.intensity=.45f;fill.transform.rotation=Quaternion.Euler(35,140,0);
            var c=new GameObject("Factory context camera").AddComponent<Camera>();c.orthographic=true;c.clearFlags=CameraClearFlags.SolidColor;c.backgroundColor=new Color(.20f,.23f,.25f);c.nearClipPlane=.1f;c.farClipPlane=100;
            c.GetUniversalAdditionalCameraData().renderPostProcessing=false;return c;
        }
        static void Shot(Camera camera,string name,Vector3 position,Vector3 target,float size,int width=1600,int height=1000)
        {
            camera.transform.position=position;camera.transform.LookAt(target);camera.orthographicSize=size;
            var rt=new RenderTexture(width,height,24,RenderTextureFormat.ARGB32);var image=new Texture2D(width,height,TextureFormat.RGB24,false);var old=RenderTexture.active;
            try{camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();File.WriteAllBytes(Path.Combine(Evidence,name+".png"),image.EncodeToPNG());}
            finally{camera.targetTexture=null;RenderTexture.active=old;rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(image);}
        }
        static void Render()
        {
            var p=PlayableProfile.Default;
            float factoryScale=(float)(p.FactoryModelHeightMeters*p.FactoryModelScale/2.06);
            var camera=Setup();
            Spawn("headquarters",new Vector3(-4,0,-3),(float)(p.HeadquartersModelHeightMeters*p.HeadquartersModelScale/4.2),Owner);
            Spawn("factory",new Vector3(0,0,-3),factoryScale,Owner);
            Spawn("refinery",new Vector3(4,0,-3),(float)(p.RefineryModelHeightMeters*p.RefineryModelScale/2.545),Owner);
            var candidate=Spawn("factory-armored",new Vector3(0,0,1.2f),factoryScale,Owner);
            var tank=Spawn("tank",new Vector3(-2.3f,0,3.4f),(float)(p.TankModelRadius*p.TankModelScale/1.585),Owner);
            var explorer=Spawn("explorer",new Vector3(.1f,0,4.2f),(float)(p.ExplorerModelRadius*p.ExplorerModelScale/1.0773),Owner);
            var shkval=Spawn("shkval",new Vector3(2.2f,0,3.4f),(float)(p.ShkvalModelRadius*p.ShkvalModelScale/2.05),Owner);
            var effect=candidate.GetComponent<FactoryModelPresentation>();effect.animatePreview=true;
            camera.transform.position=new Vector3(10,13,18);camera.transform.LookAt(new Vector3(0,.6f,0));camera.orthographicSize=6.7f;
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(),ScenePath);AssetDatabase.SaveAssets();
            effect.Sample(FactoryModelPresentation.Phase.Working,0,1.4f);
            Require(effect.SmokeParticles>0,"Working smoke failed to emit");
            Shot(camera,"context-working",new Vector3(10,13,18),new Vector3(0,.6f,0),6.7f);
            Shot(camera,"context-top",new Vector3(0,18,11),new Vector3(0,.3f,0),6.5f);
            var lightBlock=new MaterialPropertyBlock();
            var workLights=candidate.GetComponentsInChildren<Renderer>(true).Where(r=>r.name.StartsWith("workGlow")).ToArray();
            Require(workLights.Any(r=>{r.GetPropertyBlock(lightBlock,0);return lightBlock.GetColor("_EmissionColor").maxColorComponent>.1f;}),"Working flashes missing from material slot");
            // Same composition makes working vs idle differences reviewable.
            effect.Sample(FactoryModelPresentation.Phase.Idle,0,2);
            Require(effect.SmokeParticles==0,"Idle smoke must be empty in clean sample");
            Require(workLights.All(r=>{r.GetPropertyBlock(lightBlock,0);return lightBlock.GetColor("_EmissionColor").maxColorComponent==0;}),"Idle work lights must be dark");
            Shot(camera,"context-idle",new Vector3(10,13,18),new Vector3(0,.6f,0),6.7f);
            // Close views of the actual prefab and its state-controlled geometry.
            foreach(var state in new[]{FactoryModelPresentation.Phase.Idle,FactoryModelPresentation.Phase.Working,FactoryModelPresentation.Phase.Rollout})
            {
                effect.Sample(state,state==FactoryModelPresentation.Phase.Rollout?1:0,1.4f);
                var sections=candidate.GetComponentsInChildren<Transform>(true).Where(t=>t.name.StartsWith("doorSection")).ToArray();
                Require(sections.All(t=>t.gameObject.activeSelf==(state!=FactoryModelPresentation.Phase.Rollout)),"Door state mismatch");
                Shot(camera,"factory-"+state.ToString().ToLowerInvariant(),new Vector3(4.5f,4.5f,7.4f),new Vector3(0,.8f,1.2f),2.25f);
            }
            // Measure the current unit at its unchanged gameplay scale against actual
            // authored portal markers. Place wheels on the raised bay floor, not the ground.
            var markers=candidate.GetComponentsInChildren<Transform>(true);
            Vector3 floor=markers.Single(t=>t.name=="gateFloor").position;
            float top=markers.Single(t=>t.name=="gateClearanceTop").position.y;
            float left=markers.Single(t=>t.name=="gateLeft").position.x;
            float right=markers.Single(t=>t.name=="gateRight").position.x;
            var unitBounds=MeshBounds(shkval);
            Debug.Log("FACTORY_CLEARANCE_MEASURE unit="+unitBounds+" left="+left+" right="+right+" floor="+floor+" top="+top);
            float unitHeight=unitBounds.size.y;
            float clearHeight=top-floor.y;
            Require(clearHeight-unitHeight>=.20f,"Shkval roof clearance below 0.20m: "+(clearHeight-unitHeight));
            Require(Mathf.Abs(right-left)-unitBounds.size.x>=.40f,"Shkval side clearance too small");
            // Verify the actual underside along the complete bay, including the baffle.
            float interiorCeiling=BayCeiling(candidate, floor, unitBounds.extents.x);
            Require(interiorCeiling-floor.y-unitHeight>=.20f,"Interior ceiling clearance too small");
            File.WriteAllText(Path.Combine(Evidence,"shkval-clearance.json"),JsonUtility.ToJson(new ClearanceReport{unitHeight=unitHeight,clearHeight=clearHeight,roofGap=clearHeight-unitHeight,sideGap=(Mathf.Abs(right-left)-unitBounds.size.x)*.5f,interiorGap=interiorCeiling-floor.y-unitHeight,result="PASS"},true));
            // F2: all currently factory-produced units must keep the same 20 cm
            // headroom and 20 cm per-side clearance used for Shkval in F1.
            foreach(var unit in new[]{tank,explorer,shkval})
            {
                var bounds=MeshBounds(unit);
                float gap=clearHeight-bounds.size.y;
                float side=(Mathf.Abs(right-left)-bounds.size.x)*.5f;
                float interior=BayCeiling(candidate,floor,bounds.extents.x)-floor.y-bounds.size.y;
                Require(gap>=.20f&&side>=.20f&&interior>=.20f,"Factory clearance failed for "+unit.name+": head="+gap+" side="+side+" interior="+interior);
                File.WriteAllText(Path.Combine(Evidence,unit.name+"-clearance.json"),JsonUtility.ToJson(new ClearanceReport{unit=unit.name,unitHeight=bounds.size.y,clearHeight=clearHeight,roofGap=gap,sideGap=side,interiorGap=interior,result="PASS"},true));
            }
            explorer.transform.position=new Vector3(2.2f,0,4.5f);
            var oldUnitPosition=shkval.transform.position;
            float floorOffset=floor.y-unitBounds.min.y;
            shkval.transform.position=new Vector3(0,oldUnitPosition.y+floorOffset,floor.z+.12f);
            effect.Sample(FactoryModelPresentation.Phase.Rollout,1,1.4f);
            Shot(camera,"shkval-clearance",new Vector3(3.8f,2.8f,8),new Vector3(0,1.0f,1.9f),2.05f);
            Shot(camera,"shkval-clearance-front",new Vector3(0,1.05f,8),new Vector3(0,1.05f,1.2f),1.65f);
            // Native clearance views for the two shorter units, at unchanged scales.
            shkval.SetActive(false);
            foreach(var unit in new[]{tank,explorer})
            {
                var oldPosition=unit.transform.position;
                var bounds=MeshBounds(unit);
                unit.transform.position=new Vector3(0,oldPosition.y+floor.y-bounds.min.y,floor.z+.12f);
                Shot(camera,unit.name+"-clearance",new Vector3(3.8f,2.8f,8),new Vector3(0,1.0f,1.9f),2.05f);
                unit.transform.position=oldPosition;
            }
            shkval.SetActive(true);
            shkval.transform.position=oldUnitPosition;
            // A working order must override an accidental requested door opening.
            effect.Sample(FactoryModelPresentation.Phase.Working,1,1.4f);
            Require(candidate.GetComponentsInChildren<Transform>(true).Where(t=>t.name.StartsWith("doorSection")).All(t=>t.gameObject.activeSelf),"Production exposed interior");
            if (Environment.GetEnvironmentVariable("FACTORY_PREVIEW_VIDEO") == "1")
            {
                Directory.CreateDirectory(Path.Combine(Evidence,"frames"));
                // Ten-second presentation demonstration, sampled at 12 fps; no gameplay simulation.
                effect.Sample(FactoryModelPresentation.Phase.Idle,0,0);
                for (int frame=0;frame<120;frame++)
                {
                    float t=frame/12f;
                    var phase=t<2?FactoryModelPresentation.Phase.Idle:t<7?FactoryModelPresentation.Phase.Working:FactoryModelPresentation.Phase.Rollout;
                    float opening=t<7?0:t<8?t-7:t<9?1:10-t;
                    if (t<8) shkval.transform.position=new Vector3(0,floorOffset,1.2f);
                    else
                    {
                        float progress=Mathf.Clamp01(t-8);
                        float z=Mathf.Lerp(1.2f,4.5f,progress);
                        float y=floorOffset*(1-Mathf.InverseLerp(floor.z,floor.z+.65f,z));
                        shkval.transform.position=new Vector3(0,y,z);
                    }
                    effect.StepPreview(phase,opening,t,1f/12f);
                    Shot(camera,"frames/"+frame.ToString("D4"),new Vector3(4.5f,5.7f,8.4f),new Vector3(0,.7f,1.9f),3.1f,960,600);
                }
            }
            camera=Setup();
            var colors=new[]{Owner,new Color(.93f,.25f,.16f),new Color(.92f,.68f,.13f),new Color(.62f,.25f,.82f)};
            for(int i=0;i<4;i++)
            {
                float x=(i-1.5f)*3.5f;
                Spawn("factory-armored",new Vector3(x,0,0),factoryScale,colors[i]).GetComponent<FactoryModelPresentation>().Sample(FactoryModelPresentation.Phase.Idle,0,1);
                Spawn("shkval",new Vector3(x,0,2.2f),(float)(p.ShkvalModelRadius*p.ShkvalModelScale/2.05),colors[i]);
            }
            Shot(camera,"player-colors",new Vector3(5,9,15),new Vector3(0,.7f,0),4.9f);
            File.WriteAllText(Path.Combine(Evidence,"state-validation.txt"),"PASS: named nodes, finite geometry/envelope/UVs, shared building texture maps, tank/Explorer/Shkval portal and interior clearance, no colliders, working smoke/flashes, idle no smoke/no emission, closed production door, open rollout door.\nNative Editor asset preview; not ordinary-match, Player, device, performance or human acceptance.\nCurrent PlayableProfile scales used without overrides.\n");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            Debug.Log("FACTORY_ARMORED_PREVIEW_PASS");
        }
    }
}
