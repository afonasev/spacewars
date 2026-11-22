using System;
using System.IO;
using System.Linq;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Spacewars.Editor
{
    public static class CerberusModelReview
    {
        // Off-screen studio using the same actor creation, materials and rigs as a match.
        public static void Capture()
        {
            string output=Path.GetFullPath("../.local/cerberus/unity");
            Directory.CreateDirectory(output);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var scene=new GameObject("Studio");
            var camera=new GameObject("Camera").AddComponent<Camera>();
            camera.clearFlags=CameraClearFlags.SolidColor;
            camera.backgroundColor=new Color(.20f,.225f,.25f);
            camera.orthographic=true;
            camera.orthographicSize=1.43f;
            camera.transform.position=new Vector3(0,3.2f,5);
            camera.transform.LookAt(new Vector3(0,.24f,0));
            RenderSettings.ambientMode=AmbientMode.Flat;
            RenderSettings.ambientLight=new Color(.66f,.70f,.75f);
            var key=new GameObject("Key").AddComponent<Light>();
            key.type=LightType.Directional;key.intensity=1.7f;
            key.transform.rotation=Quaternion.Euler(45,160,0);
            var fill=new GameObject("Fill").AddComponent<Light>();
            fill.type=LightType.Directional;fill.intensity=.55f;
            fill.transform.rotation=Quaternion.Euler(35,-40,0);
            var profile=PlayableProfile.Create(JsonUtility.FromJson<PlayableProfileData>(Resources.Load<TextAsset>("PlayableProfile").text));
            var balance=NativeBalanceStore.ForApplication(profile,Path.GetFullPath("../.local/cerberus/model-lab.json"));
            balance.ResetRelease();
            profile=balance.Active;
            Debug.Log($"Cerberus model review profile {balance.SelectedId} v{balance.SelectedVersion}");
            var world=new PlayableWorld(scene.transform,profile,staged:true);
            var cerberus=world.Tank(1,true,PlayableEntityKind.Cerberus);
            var vepr=world.Tank(2,true,PlayableEntityKind.Tank);
            foreach(var actor in new[]{cerberus,vepr})
            {
                actor.Health.parent.gameObject.SetActive(false);
                actor.Selection.SetActive(false);
                actor.Hull.localRotation=Quaternion.Euler(0,27,0);
                PlayableWorld.PaintOwner(actor,new Color(.10f,.34f,.58f));
            }
            cerberus.Root.position=Vector3.left*.95f;vepr.Root.position=Vector3.right*.99f;
            var rt=new RenderTexture(1280,720,24,RenderTextureFormat.ARGB32){antiAliasing=4};
            var pixels=new Texture2D(rt.width,rt.height,TextureFormat.RGBA32,false);
            void Save(string name)
            {
                camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;
                pixels.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);pixels.Apply();
                File.WriteAllBytes(Path.Combine(output,name+".png"),pixels.EncodeToPNG());
                RenderTexture.active=null;
            }
            try
            {
                for(int tier=0;tier<3;tier++)
                {
                    cerberus.DetailLod.SetDetail(tier);
                    if(vepr.DetailLod)vepr.DetailLod.SetDetail(tier);
                    Save("cerberus-vepr-lod"+tier);
                }
                string Counts(Renderer[] renderers)
                {
                    int triangles=0,vertices=0,submissions=0;
                    foreach(var renderer in renderers)
                    {
                        Mesh mesh=renderer is SkinnedMeshRenderer skin?skin.sharedMesh:renderer.GetComponent<MeshFilter>()?.sharedMesh;
                        if(!mesh)continue;
                        vertices+=mesh.vertexCount;
                        for(int sub=0;sub<mesh.subMeshCount;sub++){triangles+=(int)mesh.GetIndexCount(sub)/3;submissions++;}
                    }
                    return $"{vertices},{triangles},{renderers.Length},{submissions}";
                }
                using(var report=new StreamWriter(Path.Combine(output,"runtime-meshes.csv")))
                {
                    report.WriteLine("model,tier,vertices,triangles,renderers,submeshes");
                    for(int tier=0;tier<3;tier++)
                    {
                        cerberus.DetailLod.SetDetail(tier);
                        report.WriteLine($"Cerberus,{tier},"+Counts(cerberus.Hull.GetComponentsInChildren<Renderer>()));
                    }
                }
                vepr.Root.gameObject.SetActive(false);
                cerberus.DetailLod.SetDetail(0);
                cerberus.Root.position=Vector3.zero;
                camera.orthographicSize=.92f;
                Save("cerberus-close");
                cerberus.Hull.localRotation=Quaternion.identity;
                camera.transform.position=Vector3.up*5;camera.transform.rotation=Quaternion.Euler(90,0,0);
                camera.orthographicSize=1.65f;Save("cerberus-top");
                camera.transform.position=new Vector3(4,.49f,0);camera.transform.LookAt(new Vector3(0,.34f,0));
                Save("cerberus-side");
                camera.transform.position=new Vector3(0,3.2f,5);
                camera.transform.LookAt(new Vector3(0,.24f,0));
                camera.orthographicSize=1.5f;
                cerberus.Hull.localRotation=Quaternion.Euler(0,55,0);
                var red=new Color(.65f,.12f,.08f);
                PlayableWorld.PaintOwner(cerberus,red);
                var pose=new CerberusWeaponPose(.28,-.22,2.0,2.5,.92,.31);
                var sample=new PlayableEntitySnapshot(1,PlayableOwner.Player,PlayableEntityKind.Cerberus,
                    default,400,false,0,0,0,flames:pose);
                cerberus.Cerberus.Display(sample,profile.CerberusAngle,red);
                // Show the entire effect volume, including both endpoints.
                var bounds=cerberus.Hull.GetComponentsInChildren<Renderer>()[0].bounds;
                foreach(var renderer in cerberus.Hull.GetComponentsInChildren<Renderer>())
                    bounds.Encapsulate(renderer.bounds);
                var direction=camera.transform.forward;
                camera.transform.position=bounds.center-direction*8;
                camera.orthographicSize=bounds.extents.magnitude*1.05f;
                Save("cerberus-firing");
                var blue=new Color(.10f,.34f,.58f);
                PlayableWorld.PaintOwner(cerberus,blue);
                cerberus.Cerberus.Display(sample,profile.CerberusAngle,blue);
                Save("cerberus-firing-blue");
                PlayableWorld.PaintOwner(cerberus,red);
                cerberus.Cerberus.Display(sample,profile.CerberusAngle,red);
                // Keep the camera fixed relative to the hull for the moving-track comparison.
                var tracks=cerberus.Hull.GetComponentsInChildren<SkinnedMeshRenderer>();
                Vector3[][] BakedTracks()
                {
                    return tracks.Select(track=>
                    {
                        var mesh=new Mesh();
                        track.BakeMesh(mesh);
                        var positions=mesh.vertices;
                        UnityEngine.Object.DestroyImmediate(mesh);
                        return positions;
                    }).ToArray();
                }
                var before=BakedTracks();
                var advance=cerberus.Hull.forward*.12f;
                cerberus.Root.position+=advance;
                camera.transform.position+=advance;
                cerberus.Cerberus.Display(sample,profile.CerberusAngle,red);
                var after=BakedTracks();
                if(tracks.Length!=2)throw new InvalidOperationException($"Expected two active tracks, found {tracks.Length}.");
                for(int i=0;i<tracks.Length;i++)
                {
                    float displacement=before[i].Zip(after[i],Vector3.Distance).Max();
                    Debug.Log($"Cerberus track {tracks[i].name}: {tracks[i].bones.Length} bones; max local vertex travel {displacement:F4}");
                    if(displacement<=.01f)throw new InvalidOperationException("Track mesh did not follow its animated bones.");
                    // Multiple renders here share one Editor frame.
                    tracks[i].forceMatrixRecalculationPerRender=true;
                }
                Save("cerberus-running-gear");
                camera.orthographicSize=1.14f;
                cerberus.Root.gameObject.SetActive(false);
                var wreck=CerberusPresentation.Create(scene.transform,true);
                wreck.transform.localScale=cerberus.Hull.localScale;
                wreck.transform.rotation=Quaternion.Euler(0,27,0);
                camera.transform.position=new Vector3(0,3.2f,5);
                camera.transform.LookAt(new Vector3(0,.24f,0));
                for(int tier=0;tier<3;tier++)
                {
                    wreck.GetComponent<ModelDetailLod>().SetDetail(tier);Save("wreck-lod"+tier);
                }
            }
            finally
            {
                camera.targetTexture=null;UnityEngine.Object.DestroyImmediate(rt);
                UnityEngine.Object.DestroyImmediate(pixels);
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            }
        }
    }
}
