using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
namespace Spacewars.Tests.PlayMode
{
    public sealed class CameraAntialiasingUiTests
    {
        private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
        private static T Get<T>(object obj,string name)=>(T)obj.GetType().GetField(name,Flags).GetValue(obj);
        private static void Call(object obj,string name,params object[] values)=>obj.GetType().GetMethod(name,Flags).Invoke(obj,values);
        private static RenderTexture Target(int width,int height,int samples=1){var t=new RenderTexture(width,height,24){antiAliasing=samples};t.Create();return t;}
        private static void Save(RenderTexture target,string name)
        {var previous=RenderTexture.active;var image=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);try{RenderTexture.active=target;image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();var folder=Environment.GetEnvironmentVariable("SPACEWARS_AA_EVIDENCE")??Path.GetFullPath("../.local/aa-captures");Directory.CreateDirectory(folder);File.WriteAllBytes(Path.Combine(folder,name+".png"),image.EncodeToPNG());}finally{RenderTexture.active=previous;UnityEngine.Object.Destroy(image);}}
        [UnityTest] public IEnumerator OrdinaryBootstrapUiOutsideAa()
        {
            var host=new GameObject("AA ordinary HUD");var hud=host.AddComponent<PlayableBootstrap>();hud.enabled=false;typeof(PlayableBootstrap).GetField("profile",Flags).SetValue(hud,PlayableProfile.Default);Call(hud,"CreateHud");Call(hud,"CreateWorld");
            var root=Get<VisualElement>(hud,"root");var camera=Get<Camera>(hud,"cameraView");var world=Get<PlayableWorld>(hud,"world");world.InspectionReveal();world.Building(1,"Headquarters",true).Root.position=Vector3.zero;world.Tank(2,true).Root.position=new Vector3(5,0,-4);
            for(int n=0;n<8;n++)yield return null;Assert.AreEqual(1,camera.GetComponent<CameraAntialiasing>().Profile.mode,"Selected ordinary default");Assert.AreEqual(4,CameraAntialiasingDiagnostics.Samples(camera),"Ordinary screen target MSAA4");
            var panel=host.GetComponent<UIDocument>().panelSettings;var target=Target(1280,800);panel.targetTexture=target;root.style.width=1280;root.style.height=800;Get<VisualElement>(hud,"modal").style.display=DisplayStyle.None;
            var scene=new VisualElement{pickingMode=PickingMode.Ignore};scene.style.position=Position.Absolute;scene.style.left=0;scene.style.right=0;scene.style.top=0;scene.style.bottom=0;root.Insert(0,scene);
            try{
                for(int mode=0;mode<3;mode++){
                    var backdrop=Target(1280,800,mode==1?4:1);try{
                        camera.targetTexture=backdrop;camera.GetComponent<CameraAntialiasing>().Configure(new CameraAntialiasingProfile{mode=mode});scene.style.backgroundImage=new StyleBackground(Background.FromRenderTexture(backdrop));
                        for(int n=0;n<8;n++)yield return null;Assert.AreEqual(mode==1?4:1,CameraAntialiasingDiagnostics.Samples(camera));Assert.Greater(root.Query<Label>().ToList().Count(l=>l.worldBound.width>0),2);Save(target,"ui-ordinary-"+mode);
                    }finally{scene.style.backgroundImage=StyleKeyword.None;camera.targetTexture=null;backdrop.Release();UnityEngine.Object.Destroy(backdrop);}
                }
            }finally{panel.targetTexture=null;target.Release();UnityEngine.Object.Destroy(target);UnityEngine.Object.Destroy(host);}yield return null;
        }
        [UnityTest] public IEnumerator RealTwoLocalBootstrapUiAndDescriptors()
        {
            var host=new GameObject("AA local HUD");var hud=host.AddComponent<OfflineTwoLocalBootstrap>();hud.enabled=false;Call(hud,"Start");
            var views=Get<Array>(hud,"views").Cast<object>().ToArray();var cameras=views.Select(v=>Get<Camera>(v,"Camera")).ToArray();var runtime=Get<PlayableRuntime>(hud,"runtime");Call(hud,"Render",runtime.OfflineFrame);
            for(int n=0;n<8;n++)yield return null;foreach(var camera in cameras){Assert.AreEqual(1,camera.GetComponent<CameraAntialiasing>().Profile.mode,"Selected local default");Assert.AreEqual(4,CameraAntialiasingDiagnostics.Samples(camera),"Local screen target MSAA4");}
            var root=Get<VisualElement>(hud,"root");Get<VisualElement>(hud,"lobby").style.display=DisplayStyle.None;var panel=host.GetComponent<UIDocument>().panelSettings;var target=Target(1600,800);panel.targetTexture=target;root.style.width=1600;root.style.height=800;
            try{
                for(int mode=0;mode<3;mode++){
                    var textures=new RenderTexture[2];var scenes=new VisualElement[2];
                    try{
                        for(int seat=0;seat<2;seat++){
                            var camera=cameras[seat];textures[seat]=Target(1600,800,mode==1?4:1);camera.targetTexture=textures[seat];camera.GetComponent<CameraAntialiasing>().Configure(new CameraAntialiasingProfile{mode=mode});Assert.True(OfflineCameraMirrorFeature.IsRegistered(camera));
                            var texture=textures[seat];float u=seat*.5f;var scene=scenes[seat]=new VisualElement{pickingMode=PickingMode.Ignore};scene.style.position=Position.Absolute;scene.style.left=seat*800;scene.style.width=800;scene.style.height=800;
                            scene.generateVisualContent+=ctx=>{var mesh=ctx.Allocate(4,6,texture);mesh.SetNextVertex(new Vertex{position=new Vector3(0,800,Vertex.nearZ),tint=Color.white,uv=new Vector2(u,0)});mesh.SetNextVertex(new Vertex{position=new Vector3(0,0,Vertex.nearZ),tint=Color.white,uv=new Vector2(u,1)});mesh.SetNextVertex(new Vertex{position=new Vector3(800,0,Vertex.nearZ),tint=Color.white,uv=new Vector2(u+.5f,1)});mesh.SetNextVertex(new Vertex{position=new Vector3(800,800,Vertex.nearZ),tint=Color.white,uv=new Vector2(u+.5f,0)});foreach(ushort index in new ushort[]{0,1,2,2,3,0})mesh.SetNextIndex(index);};root.Insert(0,scene);
                        }
                        for(int n=0;n<8;n++)yield return null;foreach(var camera in cameras)Assert.AreEqual(mode==1?4:1,CameraAntialiasingDiagnostics.Samples(camera));Assert.Greater(root.Query<Label>().ToList().Count(l=>l.worldBound.width>0),2);Save(target,"ui-two-local-"+mode);
                    }finally{for(int i=0;i<2;i++){scenes[i]?.RemoveFromHierarchy();cameras[i].targetTexture=null;if(textures[i]){textures[i].Release();UnityEngine.Object.Destroy(textures[i]);}}}
                }
            }finally{panel.targetTexture=null;target.Release();UnityEngine.Object.Destroy(target);runtime.RequestStop();UnityEngine.Object.Destroy(host);}yield return null;
        }
    }
}
