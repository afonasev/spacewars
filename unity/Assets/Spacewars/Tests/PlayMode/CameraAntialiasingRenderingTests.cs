using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;
namespace Spacewars.Tests.PlayMode
{
    public sealed class CameraAntialiasingRenderingTests
    {
        [Test] public void ProfileRejectsInvalidModes()
        {
            var p=CameraAntialiasingProfile.Load();Assert.AreEqual(1,p.mode,"Human-selected MSAA4 default");Assert.AreEqual(2,p.revision,"Selected art revision");Assert.AreEqual(1,p.Parameters().Count());
            foreach(float value in new[]{-1f,3f,.5f,float.NaN,float.PositiveInfinity}){p.mode=value;Assert.Throws<ArgumentException>(()=>p.Validate());}
            for(int mode=0;mode<3;mode++){p.mode=mode;p.Validate();Assert.AreEqual(mode==1?4:1,p.Samples);}
        }
        private static Camera View(Transform root,RenderTexture target)
        {var c=new GameObject("Material preview camera").AddComponent<Camera>();c.transform.SetParent(root);c.targetTexture=target;c.fieldOfView=48;c.nearClipPlane=.1f;c.farClipPlane=700;c.clearFlags=CameraClearFlags.SolidColor;c.backgroundColor=new Color(.075f,.10f,.13f);return c;}
        private static Texture2D Read(RenderTexture target)
        {var old=RenderTexture.active;try{RenderTexture.active=target;var t=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);t.ReadPixels(new Rect(0,0,target.width,target.height),0,0);t.Apply();return t;}finally{RenderTexture.active=old;}}
        private static int Changed(Color32[] a,Color32[] b,int tolerance=3)
        {int n=0;for(int i=0;i<a.Length;i++)if(Math.Abs(a[i].r-b[i].r)+Math.Abs(a[i].g-b[i].g)+Math.Abs(a[i].b-b[i].b)>tolerance)n++;return n;}
        private static string Output(){var p=Environment.GetEnvironmentVariable("SPACEWARS_AA_EVIDENCE")??Path.GetFullPath("../.local/camera-antialiasing/captures");Directory.CreateDirectory(p);return p;}
        private static PlayableProfile Map(bool foundry)=>foundry?FoundryGreybox.LoadProfile():PlayableProfile.Create(JsonUtility.FromJson<PlayableProfileData>(Resources.Load<TextAsset>("PlayableProfile").text),new ThreeCrossingsMap(JsonUtility.FromJson<ThreeCrossingsProfileData>(Resources.Load<TextAsset>("ThreeCrossingsProfile").text)));

        private static RenderTexture Target(int width,int height,int samples)
        {var target=new RenderTexture(width,height,24){antiAliasing=samples};target.Create();Assert.AreEqual(samples,target.antiAliasing,"Unsupported target MSAA");return target;}
        private static void Save(RenderTexture target,string path)
        {Directory.CreateDirectory(Path.GetDirectoryName(path));var image=Read(target);try{Assert.Less(image.GetPixels32().Count(c=>c.r>200&&c.b>200&&c.g<40),16,"No error pink");File.WriteAllBytes(path,image.EncodeToPNG());}finally{UnityEngine.Object.Destroy(image);}}
        private static string Mode(int mode)=>new[]{"off","msaa4","smaa-high"}[mode];
        [UnityTest] public IEnumerator CaptureMatchedStillAndMotion()
        {
            float previousScale=Time.timeScale;Time.timeScale=0;
            try{
            foreach(bool foundry in new[]{false,true}){
                var profile=Map(foundry);var light=IndustrialLightingProfile.Load(profile.AuthoredMap?.Id);var root=new GameObject("Matched AA world");var world=new PlayableWorld(root.transform,profile);world.InspectionReveal();world.MaterialFinish=ActorMaterialProfile.Load();
                var focus=world.Point(TerritoryRules.Sites(profile)[0].Position);var camera=View(root.transform,null);
                camera.gameObject.AddComponent<ActorGlowCamera>().Configure(ActorMaterialProfile.Load());camera.gameObject.AddComponent<ContactOcclusionCamera>().Configure(ContactOcclusionProfile.Load());var aa=camera.gameObject.AddComponent<CameraAntialiasing>();
                var sun=new GameObject("Accepted sun").AddComponent<Light>();sun.transform.SetParent(root.transform);light.ApplyScene(sun);sun.shadows=LightShadows.Soft;camera.gameObject.AddComponent<VisibleShadowCoverage>().Configure(root.transform,light);
                var hq=world.Building(1,"Headquarters",true);hq.Root.position=focus;
                var ally=world.Tank(2,true);ally.Root.position=world.Point(new NavPoint(TerritoryRules.Sites(profile)[0].Position.X+5,TerritoryRules.Sites(profile)[0].Position.Z-4));
                var enemy=world.Tank(3,false);enemy.Root.position=world.Point(new NavPoint(TerritoryRules.Sites(profile)[0].Position.X-5,TerritoryRules.Sites(profile)[0].Position.Z-4));
                try{
                foreach(bool close in new[]{false,true}){
                    camera.orthographic=!close;camera.orthographicSize=(float)profile.CameraOrthoSize;
                    var aim=focus+(close?new Vector3(0,0,-2):Vector3.zero);var position=aim+(close?new Vector3(0,9.45f,-5.04f):new Vector3((float)profile.CameraOffsetX,(float)profile.CameraHeight,(float)profile.CameraOffsetZ));
                    camera.transform.position=position;camera.transform.LookAt(aim);world.FaceBuilding(hq,camera);
                    int count=root.GetComponentsInChildren<Renderer>().Length,vertices=root.GetComponentsInChildren<MeshFilter>().Sum(f=>f.sharedMesh.vertexCount);Color32[] baseline=null;
                    for(int mode=0;mode<3;mode++){
                        string prefix=(foundry?"foundry":"crossings")+"-"+(close?"close":"gameplay")+"-"+Mode(mode);var target=Target(1600,1000,mode==1?4:1);
                        try{
                            camera.targetTexture=target;aa.Configure(new CameraAntialiasingProfile{mode=mode});camera.transform.position=position;camera.transform.LookAt(aim);for(int n=0;n<8;n++)yield return null;
                            Assert.AreEqual(mode==1?4:1,CameraAntialiasingDiagnostics.Samples(camera),"Effective URP MSAA descriptor");
                            Assert.AreEqual(count,root.GetComponentsInChildren<Renderer>().Length);Assert.AreEqual(vertices,root.GetComponentsInChildren<MeshFilter>().Sum(f=>f.sharedMesh.vertexCount));
                            var image=Read(target);try{var pixels=image.GetPixels32();if(mode==0)baseline=pixels;else Assert.Greater(Changed(baseline,pixels),100,"AA must change actual output");}finally{UnityEngine.Object.Destroy(image);}
                            Save(target,Path.Combine(Output(),prefix+".png"));
                            File.AppendAllText(Path.Combine(Output(),"descriptors.txt"),prefix+" samples="+CameraAntialiasingDiagnostics.Samples(camera)+" postAA="+camera.GetUniversalAdditionalCameraData().antialiasing+" renderers="+count+" vertices="+vertices+" Unity="+Application.unityVersion+" GPU="+SystemInfo.graphicsDeviceName+"\n");
                        }finally{camera.targetTexture=null;target.Release();UnityEngine.Object.Destroy(target);}
                        target=Target(960,600,mode==1?4:1);
                        try{
                            camera.targetTexture=target;aa.Configure(new CameraAntialiasingProfile{mode=mode});
                            for(int frame=0;frame<48;frame++){
                                // Fixed 24Hz output, 1.2m/s along camera right; no wall-time dependency.
                                var offset=camera.transform.right*((frame-23.5f)*1.2f/24f);camera.transform.position=position+offset;camera.transform.LookAt(aim+offset);
                                yield return null;yield return null;
                                Save(target,Path.Combine(Output(),"motion",prefix,frame.ToString("D3")+".png"));
                            }
                        }finally{camera.targetTexture=null;target.Release();UnityEngine.Object.Destroy(target);}
                    }
                }}finally{world.Dispose();UnityEngine.Object.Destroy(root);}yield return null;
            }}finally{Time.timeScale=previousScale;}
        }
        [UnityTest] public IEnumerator IndependentModesMirrorAndConcealedEnemy()
        {
            var root=new GameObject("AA isolation world");var world=new PlayableWorld(root.transform,PlayableProfile.Default);world.InspectionReveal();world.MaterialFinish=ActorMaterialProfile.Load();var actor=world.Building(1,"ScientificCenter",false);actor.Root.position=Vector3.zero;
            var targets=new[]{Target(640,480,1),Target(640,480,1)};var cameras=new[]{View(root.transform,targets[0]),View(root.transform,targets[1])};var controls=new CameraAntialiasing[2];
            var sun=new GameObject("Accepted sun").AddComponent<Light>();sun.transform.SetParent(root.transform);IndustrialLightingProfile.Load().ApplyScene(sun);
            try{
                for(int i=0;i<2;i++){cameras[i].transform.position=new Vector3(0,5,-5);cameras[i].transform.LookAt(Vector3.zero);cameras[i].gameObject.AddComponent<ActorGlowCamera>().Configure(ActorMaterialProfile.Load(),30+i);cameras[i].gameObject.AddComponent<ContactOcclusionCamera>().Configure(ContactOcclusionProfile.Load());controls[i]=cameras[i].gameObject.AddComponent<CameraAntialiasing>();controls[i].Configure(new CameraAntialiasingProfile());}
                for(int n=0;n<8;n++)yield return null;var ordinary=Read(targets[1]);OfflineCameraMirrorFeature.Register(cameras[1]);for(int n=0;n<8;n++)yield return null;var mirror=Read(targets[1]);
                try{var a=ordinary.GetPixels32();var b=mirror.GetPixels32();long error=0;for(int y=0;y<480;y++)for(int x=0;x<640;x++){var u=a[y*640+x];var v=b[y*640+639-x];error+=Math.Abs(u.r-v.r)+Math.Abs(u.g-v.g)+Math.Abs(u.b-v.b);}Assert.Less(error/(640f*480f*3),.5,"Offline mirror equals reflected ordinary view");}finally{UnityEngine.Object.Destroy(ordinary);UnityEngine.Object.Destroy(mirror);}
                Save(targets[1],Path.Combine(Output(),"mirror-off.png"));var retained=Read(targets[0]);
                for(int mode=1;mode<3;mode++){
                    cameras[1].targetTexture=null;targets[1].Release();UnityEngine.Object.Destroy(targets[1]);targets[1]=Target(640,480,mode==1?4:1);cameras[1].targetTexture=targets[1];controls[1].Configure(new CameraAntialiasingProfile{mode=mode});
                    for(int n=0;n<8;n++)yield return null;var unchanged=Read(targets[0]);try{Assert.Zero(Changed(retained.GetPixels32(),unchanged.GetPixels32(),0),"Other camera is byte-identical");}finally{UnityEngine.Object.Destroy(unchanged);}
                    Assert.AreEqual(mode==1?4:1,CameraAntialiasingDiagnostics.Samples(cameras[1]));Save(targets[1],Path.Combine(Output(),"mirror-"+Mode(mode)+".png"));
                    actor.Root.gameObject.SetActive(false);actor.Health.parent.gameObject.SetActive(false);for(int n=0;n<8;n++)yield return null;var hidden=Read(targets[1]);world.Clear();for(int n=0;n<8;n++)yield return null;var absent=Read(targets[1]);
                    try{Assert.Zero(Changed(hidden.GetPixels32(),absent.GetPixels32(),0),"Concealed enemy equals absent, no history leak");Save(targets[1],Path.Combine(Output(),"concealed-"+Mode(mode)+".png"));}finally{UnityEngine.Object.Destroy(hidden);UnityEngine.Object.Destroy(absent);}
                    actor=world.Building(1,"ScientificCenter",false);actor.Root.position=Vector3.zero;
                }
                UnityEngine.Object.Destroy(retained);
                controls[0].enabled=false;Assert.True(cameras[0].allowMSAA);controls[0].enabled=true;Assert.False(cameras[0].allowMSAA);
            }finally{for(int i=0;i<2;i++){OfflineCameraMirrorFeature.Unregister(cameras[i]);cameras[i].targetTexture=null;targets[i].Release();UnityEngine.Object.Destroy(targets[i]);}world.Dispose();UnityEngine.Object.Destroy(root);}yield return null;
        }
    }
}
