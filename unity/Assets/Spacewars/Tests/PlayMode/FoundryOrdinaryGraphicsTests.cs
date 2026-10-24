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
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Spacewars.Tests.PlayMode
{
    public sealed class FoundryOrdinaryGraphicsTests
    {
        private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
        private static MaterialPropertyBlock Effective(Renderer renderer,int slot=0)
        {
            var block=new MaterialPropertyBlock();renderer.GetPropertyBlock(block,slot);
            if(block.isEmpty)renderer.GetPropertyBlock(block);
            return block;
        }

        [TestCase(false)] [TestCase(true)]
        public void GraphicsRefreshPreservesRendererAndMaterialOverrides(bool materialOverride)
        {
            var host=new GameObject("Material override regression");
            using(var world=new PlayableWorld(host.transform,PlayableProfile.Default))
            try
            {
                var obj=new GameObject("Mixed override scopes");obj.transform.SetParent(world.GraphicsRoot);
                var renderer=obj.AddComponent<MeshRenderer>();
                var material=new Material(Resources.Load<Shader>("TerritoryFog"));
                try
                {
                    renderer.sharedMaterials=new[]{material,material};
                    var general=new MaterialPropertyBlock();general.SetColor("_BaseColor",Color.red);general.SetFloat("_SurfaceRole",6);general.SetVector("_FoundryPadBounds",new Vector4(1,2,3,4));renderer.SetPropertyBlock(general);
                    if(materialOverride){var specific=new MaterialPropertyBlock();specific.SetColor("_BaseColor",Color.blue);specific.SetFloat("_SurfaceRole",8);renderer.SetPropertyBlock(specific,1);}
                    for(int refresh=0;refresh<2;refresh++)
                    {
                        world.ApplyGraphics(ActorMaterialProfile.Load(),IndustrialLightingProfile.Load());
                        var inherited=new MaterialPropertyBlock();renderer.GetPropertyBlock(inherited,0);
                        Assert.True(inherited.isEmpty,"Do not introduce a per-material block that hides renderer overrides");
                        var ground=Effective(renderer);Assert.AreEqual(Color.red,ground.GetColor("_BaseColor"));Assert.AreEqual(6,ground.GetFloat("_SurfaceRole"));Assert.AreEqual(new Vector4(1,2,3,4),ground.GetVector("_FoundryPadBounds"));
                        var second=Effective(renderer,1);Assert.AreEqual(materialOverride?Color.blue:Color.red,second.GetColor("_BaseColor"));Assert.AreEqual(materialOverride?8:6,second.GetFloat("_SurfaceRole"));
                        Assert.AreEqual(IndustrialLightingProfile.Load().concreteHighlight,ground.GetVector("_IndustrialFinish").z);
                        Assert.AreEqual(ground.GetVector("_IndustrialFinish"),second.GetVector("_IndustrialFinish"));
                    }
                }
                finally{UnityEngine.Object.DestroyImmediate(material);}
            }
            finally{UnityEngine.Object.DestroyImmediate(host);}
        }

        private static void AssertTerrain(PlayableWorld world)
        {
            var renderers=world.GraphicsRoot.GetComponentsInChildren<MeshRenderer>();
            var ground=renderers.Where(r=>Effective(r).GetFloat("_SurfaceRole")==6||Effective(r).GetFloat("_SurfaceRole")==7).ToArray();
            Assert.IsNotEmpty(ground,"Ordinary match must retain textured ash/gravel roles after binding lighting");
            Assert.True(renderers.Any(r=>Effective(r).GetFloat("_SurfaceRole")==8),"Concrete role retained");
            Assert.True(renderers.Any(r=>Effective(r).GetFloat("_SurfaceRole")==10),"Basalt role retained");
            // SetColor/GetColor perform Unity's color-space round trip. Compare
            // the stored representation exactly, rather than raw authored floats.
            var expected=new MaterialPropertyBlock();expected.SetColor("_BaseColor",PlayableWorld.FoundryGround);
            foreach(var renderer in ground)
            {
                Assert.AreEqual(expected.GetColor("_BaseColor"),Effective(renderer).GetColor("_BaseColor"),renderer.name);
                Assert.AreEqual("SlagAsh",renderer.sharedMaterial.GetTexture("_EarthTex").name);
                Assert.AreEqual("SlagAsh-b",renderer.sharedMaterial.GetTexture("_EarthTexB").name);
                Assert.AreEqual("SlagAsh-c",renderer.sharedMaterial.GetTexture("_EarthTexC").name);
            }
        }

        [UnityTest] public IEnumerator LobbyMatchAndLiveGraphicsResetRetainFoundryTextures()
        {
            Assert.Contains("-noaudio",Environment.GetCommandLineArgs());Assert.Contains("-muted",Environment.GetCommandLineArgs());
            var output=Environment.GetEnvironmentVariable("SPACEWARS_FOUNDRY_ORDINARY_EVIDENCE")??Path.GetFullPath("../.local/foundry-ordinary/captures");Directory.CreateDirectory(output);
            bool addKeyboard=Keyboard.current==null,addMouse=Mouse.current==null;
            var keyboard=addKeyboard?InputSystem.AddDevice<Keyboard>():Keyboard.current;var mouse=addMouse?InputSystem.AddDevice<Mouse>():Mouse.current;
            var host=new GameObject("Foundry ordinary lobby graphics witness");var app=host.AddComponent<PlayableBootstrap>();
            T Get<T>(string n)=>(T)typeof(PlayableBootstrap).GetField(n,Flags).GetValue(app);
            void Call(string n)=>typeof(PlayableBootstrap).GetMethod(n,Flags).Invoke(app,null);
            RenderTexture target=null;Camera camera=null;
            try
            {
                double until=Time.realtimeSinceStartupAsDouble+120;
                while(Get<VisualElement>("lobbyScreen")==null&&Time.realtimeSinceStartupAsDouble<until)yield return null;
                Assert.NotNull(Get<VisualElement>("lobbyScreen"),"Menu startup");Call("ShowLobby");
                Get<VisualElement>("lobbyScreen").Q<DropdownField>("lobby-map-choice").value="Огненный разлом (6)";
                var setup=Get<NativeLobbyConfiguration>("lobbySetup");setup.HasExplicitSeed=true;setup.ExplicitSeed=7;
                Assert.AreEqual(6,setup.Participants.Count);Call("LaunchLobbyMatch");until=Time.realtimeSinceStartupAsDouble+120;
                while((Get<bool>("inLobby")||Get<bool>("preparing"))&&Time.realtimeSinceStartupAsDouble<until)yield return null;
                Assert.False(Get<bool>("inLobby"));Assert.False(Get<bool>("preparing"));
                var runtime=Get<PlayableRuntime>("runtime");Assert.NotNull(runtime);Assert.AreEqual(7,runtime.MatchSeed);
                var profile=Get<PlayableProfile>("profile");Assert.IsInstanceOf<FoundryMap>(profile.AuthoredMap);
                until=Time.realtimeSinceStartupAsDouble+30;
                while((Get<PlayableSnapshot>("view")==null||Get<PlayableSnapshot>("view").Tick<60)&&Time.realtimeSinceStartupAsDouble<until){Assert.False(runtime.IsStopped,runtime.Latest?.Failure);yield return null;}
                Assert.GreaterOrEqual(Get<PlayableSnapshot>("view").Tick,60,"Ordinary threaded match advances through app Update, without Pump or injected state");
                var world=Get<PlayableWorld>("world");AssertTerrain(world);
                camera=Get<Camera>("cameraView");target=new RenderTexture(1600,1000,24){antiAliasing=CameraAntialiasingProfile.Load().Samples};target.Create();camera.targetTexture=target;
                for(int frame=0;frame<8;frame++)yield return null;
                Save(target,Path.Combine(output,"ordinary-match.png"));
                var binding=camera.GetComponent<NativeGraphicsBinding>();
                typeof(NativeGraphicsBinding).GetMethod("Refresh",Flags).Invoke(binding,new object[]{"*"});
                for(int frame=0;frame<8;frame++)yield return null;
                AssertTerrain(world);Save(target,Path.Combine(output,"after-graphics-refresh.png"));
                var view=Get<PlayableSnapshot>("view");
                File.WriteAllText(Path.Combine(output,"capture.txt"),"Ordinary lobby → Foundry; default six participants; seed=7; tick="+view.Tick+"; paused="+view.Paused+"; map="+profile.AuthoredMap.Id+"@"+profile.AuthoredMap.Revision+"; Unity="+Application.unityVersion+"; GPU="+SystemInfo.graphicsDeviceName+"; real match camera, normal owner fog, no injected entities, no InspectionReveal, no synchronous Pump. Editor PlayMode; not a Player/device acceptance.");
            }
            finally
            {
                if(camera)camera.targetTexture=null;if(target){target.Release();UnityEngine.Object.Destroy(target);}UnityEngine.Object.Destroy(host);
                if(addKeyboard)InputSystem.RemoveDevice(keyboard);if(addMouse)InputSystem.RemoveDevice(mouse);
            }
            yield return null;
        }
        private static void Save(RenderTexture target,string path)
        {
            var active=RenderTexture.active;Texture2D image=null;
            try
            {
                RenderTexture.active=target;image=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();
                var pixels=image.GetPixels32();Assert.Less(pixels.Count(c=>c.r>200&&c.b>200&&c.g<40),16,"No shader error pink");
                File.WriteAllBytes(path,image.EncodeToPNG());
            }
            finally{RenderTexture.active=active;if(image)UnityEngine.Object.Destroy(image);}
        }
    }
}
