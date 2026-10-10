using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.IO;
using NUnit.Framework;
using Spacewars.Input;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Presentation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
namespace Spacewars.Input.Tests
{
    public sealed class OfflineOrderMarkerTests : InputTestFixture
    {
        const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
        [Serializable]sealed class Palette {public string[] colors;}
        [Serializable]sealed class CaptureView {public string owner;public int[] selected;public int markers;}
        [Serializable]sealed class CaptureStage {public string stage;public int seed;public long tick;public double time;public CaptureView[] views;}
        GameObject go;OfflineTwoLocalBootstrap hud;
        void Set(string n,object value)=>typeof(OfflineTwoLocalBootstrap).GetField(n,F).SetValue(hud,value);
        T Get<T>(string n)=>(T)typeof(OfflineTwoLocalBootstrap).GetField(n,F).GetValue(hud);
        object Call(string n,params object[] args)=>typeof(OfflineTwoLocalBootstrap).GetMethods(F).Single(m=>m.Name==n&&m.GetParameters().Length==args.Length).Invoke(hud,args);
        static T V<T>(object v,string n)=>(T)v.GetType().GetField(n).GetValue(v);
        [SetUp]public override void Setup(){base.Setup();go=new GameObject("actual offline marker renderer");hud=go.AddComponent<OfflineTwoLocalBootstrap>();hud.enabled=false;}
        [TearDown]public override void TearDown(){UnityEngine.Object.DestroyImmediate(go);base.TearDown();}
        [UnityTest]public IEnumerator ActualTwoViewportMapsUseOwnEventsExpireAndRespectMirroredCamera()
        {
            var profile=PlayableProfile.Default;var starts=new[]{new OfflineStart("left",1,new NavPoint(-24,-24),new NavPoint(-10,-4),pin:1),new OfflineStart("right",2,new NavPoint(24,24),new NavPoint(2,-4),pin:2)};
            var config=new OfflineMatchConfiguration(profile,"two-seat-marker-fixture","flat-markers","authored-test-costs",7,new[]{new OfflineParticipant("owner-11",1,1,OfflineControl.Human),new OfflineParticipant("owner-28",2,2,OfflineControl.Human)},starts,starts.Select(s=>new TerritorySite(s.SiteId,PlayableBuildingKind.Headquarters,s.Position,Array.Empty<TerritorySlot>())).ToArray(),Array.Empty<NavObstacle>(),new double[,]{{0,1},{1,0}});
            using(var runtime=new PlayableRuntime(config,1,true))using(var routes=new UnityHostRouteService())
            {
                Set("profile",profile);Set("runtime",runtime);var padProfile=JsonUtility.FromJson<NativeLocalInputProfile>(Resources.Load<TextAsset>("NativeLocalInputProfile").text);Set("padProfile",padProfile);Set("ownerPaint",JsonUtility.FromJson<Palette>(Resources.Load<TextAsset>("NativeLocalPresentationProfile").text).colors.Select(text=>{Assert.True(ColorUtility.TryParseHtmlString(text,out var color));return color;}).ToArray());
                var panel=ScriptableObject.CreateInstance<PanelSettings>();panel.themeStyleSheet=Resources.Load<ThemeStyleSheet>("FoundationTheme");panel.scaleMode=PanelScaleMode.ConstantPixelSize;panel.clearColor=true;panel.colorClearValue=new Color(.04f,.07f,.09f,1);var doc=go.AddComponent<UIDocument>();doc.panelSettings=panel;var root=doc.rootVisualElement;root.style.width=1600;root.style.height=620;root.style.unityFont=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");root.style.color=Color.white;Set("root",root);
                var input=go.AddComponent<PlayableInput>();input.enabled=false;Set("keyboard",input);Set("assignedKeyboard",InputSystem.AddDevice<Keyboard>());Set("assignedMouse",InputSystem.AddDevice<Mouse>());Set("assignedPad",InputSystem.AddDevice<Gamepad>());
                Call("CreateViewport",0);Call("CreateViewport",1);Call("TryBind");var session=Get<OfflineLocalSession>("session");Assert.NotNull(session);var viewports=Get<Array>("views").Cast<object>().ToArray();
                var terrain=new PlayableMapTerrain(profile);Set("sharedTerrain",terrain);var shared=new PlayableMapSurface(profile,terrain.Texture);Set("sharedMap",shared);shared.style.position=Position.Absolute;shared.style.width=180;shared.style.height=180;shared.style.left=710;shared.style.bottom=0;root.Add(shared);
                var target=new RenderTexture(1600,620,24);target.Create();panel.targetTexture=target;var worlds=new RenderTexture[2];var cameras=viewports.Select(v=>V<Camera>(v,"Camera")).ToArray();
                var sun=new GameObject("owned local marker light").AddComponent<Light>();sun.type=LightType.Directional;sun.intensity=1.4f;sun.transform.rotation=Quaternion.Euler(48,-30,0);
                for(int i=0;i<2;i++){
                    worlds[i]=new RenderTexture(1600,620,24);worlds[i].Create();cameras[i].targetTexture=worlds[i];var texture=worlds[i];float u=i*.5f;var scene=new VisualElement();scene.style.position=Position.Absolute;scene.style.left=i*800;scene.style.width=800;scene.style.height=620;
                    scene.generateVisualContent+=ctx=>{var mesh=ctx.Allocate(4,6,texture);mesh.SetNextVertex(new Vertex{position=new Vector3(0,620,Vertex.nearZ),tint=Color.white,uv=new Vector2(u,0)});mesh.SetNextVertex(new Vertex{position=new Vector3(0,0,Vertex.nearZ),tint=Color.white,uv=new Vector2(u,1)});mesh.SetNextVertex(new Vertex{position=new Vector3(800,0,Vertex.nearZ),tint=Color.white,uv=new Vector2(u+.5f,1)});mesh.SetNextVertex(new Vertex{position=new Vector3(800,620,Vertex.nearZ),tint=Color.white,uv=new Vector2(u+.5f,0)});foreach(ushort index in new ushort[]{0,1,2,2,3,0})mesh.SetNextIndex(index);};root.Insert(0,scene);
                    var mapPanel=V<VisualElement>(viewports[i],"MapPanel");mapPanel.style.display=DisplayStyle.Flex;mapPanel.style.left=i*800+18;mapPanel.style.marginLeft=0;mapPanel.style.top=StyleKeyword.Auto;mapPanel.style.bottom=14;mapPanel.style.width=180;mapPanel.style.height=180;
                }
                void Render(){session.ReadFrame();Call("Render",session.Frame);}
                void Resume(){foreach(var seat in session.Seats){session.SampleDevice(seat.Id,true,true);session.Ready(seat.Id);}Assert.True(session.Resume());}
                var folder=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.local/order-marker-captures"));Directory.CreateDirectory(folder);var captures=new List<CaptureStage>();var pixelSites=new Vector2[2];
                IEnumerator Frame(string name,int expected){Render();foreach(var v in viewports){Assert.AreEqual(expected,V<PlayableOrderMarkerLayer>(v,"OrderLayer").VisibleCount);Assert.AreEqual(expected,V<PlayableMapSurface>(v,"Map").Q<PlayableOrderMarkerLayer>().VisibleCount);}Assert.Zero(shared.Q<PlayableOrderMarkerLayer>().VisibleCount,"public union map must not reveal private orders");yield return null;yield return null;var previous=RenderTexture.active;RenderTexture.active=target;var pixels=new Texture2D(1600,620,TextureFormat.RGBA32,false);pixels.ReadPixels(new Rect(0,0,1600,620),0,0);pixels.Apply();RenderTexture.active=previous;for(int seat=0;seat<2;seat++){if(expected>0){var v=viewports[seat];var marker=V<PlayableOrderMarkers>(v,"Orders").Visible(V<PlayableSnapshot>(v,"View"),V<HashSet<int>>(v,"Selection"),Time.unscaledTimeAsDouble).Single();pixelSites[seat]=PlayableOrderMarkerLayer.WorldPoint(root,cameras[seat],profile,marker.Location.Value,true).Value;}int green=0;var point=pixelSites[seat];for(int y=-15;y<=15;y++)for(int x=-15;x<=15;x++){int px=Mathf.RoundToInt(point.x)+x,py=619-Mathf.RoundToInt(point.y)+y;if(px>=0&&px<1600&&py>=0&&py<620){var color=pixels.GetPixel(px,py);if(color.g>.8f&&color.r<.2f&&color.b<.2f)green++;}}if(expected==0)Assert.Zero(green,"expired world marker pixels must disappear for seat"+seat);else Assert.Greater(green,20,"actual world marker must be drawn for seat"+seat);}File.WriteAllBytes(Path.Combine(folder,name+".png"),pixels.EncodeToPNG());UnityEngine.Object.DestroyImmediate(pixels);captures.Add(new CaptureStage{stage=name,seed=7,tick=session.Frame.Tick,time=Time.unscaledTimeAsDouble,views=viewports.Select(v=>new CaptureView{owner=V<PlayableSnapshot>(v,"View").OwnerId,selected=V<HashSet<int>>(v,"Selection").ToArray(),markers=V<PlayableOrderMarkerLayer>(v,"OrderLayer").VisibleCount}).ToArray()});}
                try
                {
                    Resume();Render();for(int i=0;i<2;i++){var v=viewports[i];var own=V<PlayableSnapshot>(v,"View").Entities.Single(e=>e.Owner==V<PlayableSnapshot>(v,"View").Owner);V<HashSet<int>>(v,"Selection").Add(own.Id);Call("Submit",i,PlayableCommandKind.Move,new NavPoint(own.Position.X+2,own.Position.Z-6),0,PlayableOrderMode.Replace);}
                    double until=Time.realtimeSinceStartupAsDouble+5;while(!session.Frame.Views.Values.All(v=>v.Entities.Single(e=>e.Owner==v.Owner).Queue?.Active!=null)&&Time.realtimeSinceStartupAsDouble<until){routes.Service(runtime.Requests,runtime.Answers,1,runtime.NavigationBinding.Geometry,profile,64);Render();yield return null;}Assert.True(session.Frame.Views.Values.All(v=>v.Entities.Single(e=>e.Owner==v.Owner).Queue?.Active!=null));session.Pause();yield return null;Render();
                    for(int i=0;i<2;i++){Call("SelectionMarkerEvent",i);var v=viewports[i];var view=V<PlayableSnapshot>(v,"View");var selected=V<HashSet<int>>(v,"Selection");var ownId=view.Entities.Single(e=>e.Owner==view.Owner).Id;selected.Add(viewports[1-i].GetType().GetField("Selection").GetValue(viewports[1-i]) is HashSet<int> other?other.First():0);Call("SelectionMarkerEvent",i);Assert.AreEqual(1,V<PlayableOrderMarkers>(v,"Orders").Visible(view,selected,Time.unscaledTimeAsDouble).Count);selected.RemoveWhere(id=>id!=ownId);var location=view.Entities.Single(e=>e.Id==ownId).Queue.Active.Anchor.Value;var actual=PlayableOrderMarkerLayer.WorldPoint(root,cameras[i],profile,location,true).Value;var projected=cameras[i].WorldToViewportPoint(new Vector3((float)location.Position.X,.1f,(float)location.Position.Z));Assert.That(actual.x,Is.EqualTo((i*.5f+(1-projected.x)*.5f)*root.contentRect.width).Within(.01));}
                    yield return Frame("07-production-two-owner-selection",1);yield return new WaitForSecondsRealtime(1.08f);yield return Frame("08-production-two-owner-expired-still-selected",0);
                    Resume();Render();for(int i=0;i<2;i++){var v=viewports[i];var own=V<PlayableSnapshot>(v,"View").Entities.Single(e=>e.Owner==V<PlayableSnapshot>(v,"View").Owner);Call("Submit",i,PlayableCommandKind.Move,new NavPoint(own.Position.X+3,own.Position.Z-5),0,PlayableOrderMode.Append);V<HashSet<int>>(v,"Selection").Clear();Call("SelectionMarkerEvent",i);}
                    until=Time.realtimeSinceStartupAsDouble+3;while(viewports.Any(v=>V<PlayableOrderMarkerLayer>(v,"OrderLayer").VisibleCount!=1)&&Time.realtimeSinceStartupAsDouble<until){Render();yield return null;}yield return Frame("09-production-two-owner-recent-deselected",1);yield return new WaitForSecondsRealtime(1.08f);yield return Frame("10-production-two-owner-recent-expired",0);Assert.IsNull(runtime.Latest.Failure);AssertCapture("07-production-two-owner-selection",1,1);AssertCapture("08-production-two-owner-expired-still-selected",1,0);AssertCapture("09-production-two-owner-recent-deselected",0,1);AssertCapture("10-production-two-owner-recent-expired",0,0);File.WriteAllText(Path.Combine(folder,"two-viewport-state.json"),Newtonsoft.Json.JsonConvert.SerializeObject(captures,Newtonsoft.Json.Formatting.Indented));
                void AssertCapture(string stage,int selected,int markers){var capture=captures.Single(c=>c.stage==stage);Assert.AreEqual(2,capture.views.Length,stage);foreach(var view in capture.views){Assert.AreEqual(selected,view.selected.Length,stage+" selected snapshot");Assert.AreEqual(markers,view.markers,stage+" marker snapshot");}}
                }
                finally{foreach(var camera in cameras)camera.targetTexture=null;panel.targetTexture=null;foreach(var texture in worlds){texture.Release();UnityEngine.Object.DestroyImmediate(texture);}target.Release();UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(sun.gameObject);runtime.RequestStop();}
                double end=Time.realtimeSinceStartupAsDouble+3;while(!runtime.IsStopped&&Time.realtimeSinceStartupAsDouble<end)yield return null;Assert.True(runtime.IsStopped);
            }
        }
    }
}
