using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Spacewars.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Profiling;
using Object = UnityEngine.Object;

namespace Spacewars.Editor
{
    public static class SentryTurretModelAssets
    {
        const string Source="Assets/Spacewars/Content/StyleA/sentry-turret.glb";
        const string Folder="Assets/Spacewars/Content/Resources/StyleA/";
        const string Prefab=Folder+"sentry-turret.prefab";
        const string Scene="Assets/Spacewars/Content/StyleA/Preview/SentryTurretPreview.unity";
        static string Evidence=>Environment.GetEnvironmentVariable("SENTRY_TURRET_OUTPUT")??Path.GetFullPath("../.local/sentry-turret/unity");
        static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
        [Serializable] sealed class Counts
        {
            public int vertices,triangles,meshes,renderers,materials,submissions;
            public long meshMemoryBytes;
            public Vector3 dimensions;
        }
        [Serializable] sealed class Report
        {
            public string result,unity,scope;
            public Counts before,after;
            public int checks,shots,turningShots,verticalShots;
            public float yawRate,rotatingSpreadMultiplier;
        }
        static Counts Measure(GameObject model)
        {
            var filters=model.GetComponentsInChildren<MeshFilter>(true);var renderers=model.GetComponentsInChildren<MeshRenderer>(true);
            var meshes=filters.Select(f=>f.sharedMesh).Distinct().ToArray();
            var bounds=renderers[0].bounds;foreach(var r in renderers.Skip(1))bounds.Encapsulate(r.bounds);
            return new Counts{vertices=filters.Sum(f=>f.sharedMesh.vertexCount),triangles=filters.Sum(f=>f.sharedMesh.triangles.Length/3),meshes=meshes.Length,
                renderers=renderers.Length,materials=renderers.SelectMany(r=>r.sharedMaterials).Distinct().Count(),submissions=renderers.Sum(r=>r.sharedMaterials.Length),
                meshMemoryBytes=meshes.Sum(m=>Profiler.GetRuntimeMemorySizeLong(m)),dimensions=bounds.size};
        }
        static void Materials(GameObject model)
        {
            foreach(var renderer in model.GetComponentsInChildren<MeshRenderer>(true))
            {
                var assigned=renderer.sharedMaterials;
                for(int i=0;i<assigned.Length;i++)
                {
                    string role=assigned[i].name;string path=Folder+"sentry-turret-"+role+".mat";
                    var material=AssetDatabase.LoadAssetAtPath<Material>(path);
                    if(!material)
                    {
                        var template=AssetDatabase.LoadAssetAtPath<Material>(Folder+"factory-"+role+".mat");
                        material=template?new Material(template):new Material(Shader.Find("Universal Render Pipeline/Lit"));
                        material.name="sentry-turret-"+role;
                        if(!template)
                        {
                            var color=role switch{
                                "concrete"=>new Color(.43f,.43f,.39f),"bore"=>new Color(.01f,.014f,.018f),
                                "system-emissive"=>new Color(.12f,.8f,.9f),_=>throw new InvalidOperationException("Unknown turret material "+role)};
                            material.SetColor("_BaseColor",color);material.SetFloat("_Smoothness",.22f);
                            if(role=="system-emissive"){material.EnableKeyword("_EMISSION");material.SetColor("_EmissionColor",color*1.4f);}
                        }
                        AssetDatabase.CreateAsset(material,path);
                    }
                    assigned[i]=material;
                }
                renderer.sharedMaterials=assigned;
            }
        }
        [MenuItem("Spacewars/Assets/Turret 3/Prepare, Verify and Render")]
        public static void PrepareAndVerify()
        {
            if(!Application.isBatchMode&&!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
            Directory.CreateDirectory(Evidence);EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            AssetDatabase.ImportAsset(Source,ImportAssetOptions.ForceSynchronousImport);
            var source=AssetDatabase.LoadAssetAtPath<GameObject>(Source);Require(source,"Turret GLB import failed");
            var model=Object.Instantiate(source);model.name="sentry-turret";
            try
            {
                Materials(model);Require(model.GetComponentsInChildren<Collider>(true).Length==0,"Unexpected gameplay collider");
                var rig=model.AddComponent<SentryTurretRig>();rig.Bind();
                Require(rig.Forward.z>.99f,"Authored turret must face +Z");
                var before=Measure(model);
                PrefabUtility.SaveAsPrefabAsset(model,Prefab);AssetDatabase.SaveAssets();model.SetActive(false);
                RenderAtRest("before-context.png");
                var roots=new[]{rig.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="modelRoot"),rig.Yaw,rig.Pitch};
                var expected=new Dictionary<string,string>();
                foreach(var motion in roots)
                {
                    var filters=model.GetComponentsInChildren<MeshFilter>(true).Where(f=>ClosestMotion(f.transform,roots)==motion).ToArray();
                    foreach(var row in StaticBuildingMeshes.CombineUnder(motion,filters,Folder,"sentry-turret-"+motion.name+"-"))expected.Add(row.Key,row.Value);
                }
                model.SetActive(true);rig.ResetAim();var after=Measure(model);
                Require(before.triangles==after.triangles&&before.vertices==after.vertices,"Consolidation changed geometry counts");
                Require(after.renderers<before.renderers&&after.submissions<before.submissions,"No measured consolidation benefit");
                Require((before.dimensions-after.dimensions).sqrMagnitude<1e-9f,"Consolidation changed bounds");
                PrefabUtility.SaveAsPrefabAsset(model,Prefab);AssetDatabase.SaveAssets();
                StaticBuildingMeshes.ValidateSaved(AssetDatabase.LoadAssetAtPath<GameObject>(Prefab),expected);
                model.SetActive(false);
                RenderAtRest("after-context.png");
                var host=new GameObject("Turret 3 preview");var preview=host.AddComponent<SentryTurretPreview>();preview.Initialize();preview.ShowControls=false;
                int checks=Verify(preview);
                preview.Initialize();Capture(preview.PreviewCamera,"01-base-context.png");
                var cam=preview.PreviewCamera;
                cam.transform.position=new Vector3(3.5f,3.4f,5);cam.transform.LookAt(new Vector3(0,1.15f,0));cam.orthographicSize=1.9f;
                Capture(cam,"02-front-detail.png");
                cam.transform.position=new Vector3(-3.8f,3.0f,-4.5f);cam.transform.LookAt(new Vector3(0,1.15f,0));
                Capture(cam,"03-rear-detail.png");
                preview.Initialize();preview.Step(4.2);Capture(preview.PreviewCamera,"04-turning-fire.png");
                preview.Initialize();preview.Step(8.7);Capture(preview.PreviewCamera,"05-vertical-fire.png");
                // Render a reproducible twelve-second actual Unity animation, no Player build needed.
                preview.Initialize();preview.PreviewCamera.transform.position=new Vector3(7,7,-11);preview.PreviewCamera.transform.LookAt(new Vector3(0,1.8f,0));preview.PreviewCamera.orthographicSize=6;
                string frames=Path.Combine(Evidence,"frames");Directory.CreateDirectory(frames);
                for(int i=0;i<240;i++){preview.Step(.05);Capture(preview.PreviewCamera,Path.Combine("frames",$"{i:0000}.png"),960,600);}
                File.WriteAllText(Path.Combine(Evidence,"verification.json"),JsonUtility.ToJson(new Report{
                    result="PASS",unity=Application.unityVersion,before=before,after=after,checks=checks,shots=preview.Shots,turningShots=preview.TurningShots,verticalShots=preview.VerticalShots,
                    yawRate=preview.Profile.yawDegreesPerSecond,rotatingSpreadMultiplier=preview.Profile.rotatingSpreadMultiplier,
                    scope="Actual isolated Editor model/animation inspection. Submissions are material-pass proxy, not measured Frame Debugger draw calls; mesh memory is Editor runtime allocation. No ordinary-match, Player/device/FPS/human acceptance."
                },true));
                Object.DestroyImmediate(host);
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                new GameObject("Turret 3 animated preview").AddComponent<SentryTurretPreview>();
                EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(),Scene);AssetDatabase.SaveAssets();
                Debug.Log("SENTRY_TURRET_VERIFIED checks="+checks+" renderers="+before.renderers+"->"+after.renderers);
            }
            finally{if(model)Object.DestroyImmediate(model);}
        }
        static Transform ClosestMotion(Transform node,Transform[] roots)
        {
            for(var p=node.parent;p;p=p.parent)if(roots.Contains(p))return p;
            throw new InvalidOperationException("Mesh has no declared motion group: "+node.name);
        }
        static void RenderAtRest(string file)
        {
            var host=new GameObject("Consolidation comparison");var preview=host.AddComponent<SentryTurretPreview>();
            try{preview.Initialize();Capture(preview.PreviewCamera,file);}finally{Object.DestroyImmediate(host);}
        }
        static int Verify(SentryTurretPreview preview)
        {
            int checks=0;void Check(bool ok,string why){Require(ok,why);checks++;}
            var p=preview.Profile;var rig=preview.Rig;
            var fixedNode=rig.GetComponentsInChildren<Transform>().Single(t=>t.name=="modelRoot");
            var fixedMatrix=fixedNode.localToWorldMatrix;
            var pivot=rig.Pitch.position;
            rig.ResetAim();rig.AimAt(pivot+Vector3.right*5,.125f,p);
            Check(Mathf.Abs(Mathf.DeltaAngle(0,rig.YawDegrees))<=p.yawDegreesPerSecond*.125f+.001f,"Yaw exceeded angular rate");
            rig.AimAt(pivot+Vector3.right*5,1,p);Check(Vector3.Dot(rig.Forward,Vector3.right)>.999f,"Yaw socket orientation wrong");
            rig.AimAt(pivot+Vector3.up*5,1,p);float yaw=rig.YawDegrees;
            Check(rig.ElevationDegrees==90&&Vector3.Dot(rig.Forward,Vector3.up)>.999f,"Weapon cannot fire vertically");
            rig.AimAt(pivot+Vector3.up*5,.1f,p);Check(Mathf.Abs(Mathf.DeltaAngle(yaw,rig.YawDegrees))<.001f,"Zenith yaw is unstable");
            Check(fixedMatrix==fixedNode.localToWorldMatrix,"Fixed foundation moved");
            rig.AimAt(pivot+Vector3.down*5,1,p);Check(rig.ElevationDegrees==-p.depressionDegrees,"Depression clamp broken");
            rig.ResetAim();rig.AimAt(pivot+Vector3.left*5,.01f,p);
            float spread=(float)Spacewars.Simulation.PlayableProfile.Default.ExplorerAssaultSpreadDeg;
            Check(rig.Turning&&Mathf.Abs(rig.Spread(spread,p)-spread*p.rotatingSpreadMultiplier)<.0001f,"Turning spread missing");
            rig.AimAt(pivot+Vector3.left*5,1,p);rig.AimAt(pivot+Vector3.left*5,.01f,p);
            Check(!rig.Turning&&rig.Spread(spread,p)==spread,"Settled spread not restored");
            // Shortest route across the wrap, no long reverse rotation.
            rig.ResetAim();rig.AimAt(pivot+new Vector3(.01f,0,-5),1,p);float a=rig.YawDegrees;
            rig.AimAt(pivot+new Vector3(-.01f,0,-5),.01f,p);Check(Mathf.Abs(Mathf.DeltaAngle(a,rig.YawDegrees))<1,"Yaw wrap took long route");
            var parameters=p.Parameters().ToArray();Check(parameters.Length==12&&parameters.Select(x=>x.Path).Distinct().Count()==12,"Incomplete preview metadata");
            foreach(var parameter in parameters)
            {
                float original=parameter.Read();parameter.Write(float.NaN);bool rejected=false;
                try{p.Validate();}catch(ArgumentException){rejected=true;}finally{parameter.Write(original);}
                Check(rejected,"NaN tuning accepted: "+parameter.Path);
                parameter.Write(parameter.Maximum+parameter.Step);rejected=false;
                try{p.Validate();}catch(ArgumentException){rejected=true;}finally{parameter.Write(original);}
                Check(rejected,"Out-of-range tuning accepted: "+parameter.Path);
            }
            preview.Initialize();preview.Step(12);
            Check(preview.TurningShots>0,"No shot during rotation");Check(preview.VerticalShots>0,"No vertical shot");
            Check(preview.ShotLog.Where(s=>s.turning).All(s=>Mathf.Abs(s.spread-spread*p.rotatingSpreadMultiplier)<.001f),"Wrong rotating shot spread");
            var log=preview.ShotLog;
            double interval=Spacewars.Simulation.PlayableProfile.Default.ExplorerBurstShotIntervalMs/1000d;
            double pause=Spacewars.Simulation.PlayableProfile.Default.ExplorerBurstPauseMs/1000d;
            for(int i=1;i<log.Count;i++)Check(Math.Abs(log[i].time-log[i-1].time-interval-(i%preview.BurstSize==0?pause:0))<1e-8,"Cadence drift");
            Check(preview.ReferenceHealth==3*Spacewars.Simulation.PlayableProfile.Default.TankHealth,"HP reference mismatch");
            return checks;
        }
        static void Capture(Camera camera,string name,int width=1600,int height=1000)
        {
            var render=new RenderTexture(width,height,24,RenderTextureFormat.ARGB32);var previous=RenderTexture.active;
            var image=new Texture2D(width,height,TextureFormat.RGB24,false);
            try{camera.targetTexture=render;camera.Render();RenderTexture.active=render;image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();File.WriteAllBytes(Path.Combine(Evidence,name),image.EncodeToPNG());}
            finally{camera.targetTexture=null;RenderTexture.active=previous;Object.DestroyImmediate(image);render.Release();Object.DestroyImmediate(render);}
        }
    }
}
