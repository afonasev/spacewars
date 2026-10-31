using System;
using System.IO;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Presentation;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Newtonsoft.Json;

public sealed class TacticalQueuePreviewTests
{
    const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
    [Test] public void OrderMarkerStyleHasValidatedEditableMetadata()
    {
        var art=EnvironmentArtProfile.Load();var fields=art.Parameters().Where(f=>f.Path.Contains(".orderMarker")).ToArray();Assert.AreEqual(3,fields.Length);
        Assert.Less(art.orderMarkerRadius,12);Assert.Less(art.orderMarkerWidth,2);
        foreach(var f in fields){float original=f.Read();Assert.IsNotEmpty(f.Description);Assert.IsNotEmpty(f.Unit);Assert.Greater(f.Step,0);foreach(float bad in new[]{float.NaN,float.PositiveInfinity,f.Minimum-f.Step,f.Maximum+f.Step}){f.Write(bad);Assert.Throws<ArgumentException>(()=>art.Validate());}f.Write(original);}art.Validate();
    }
    [Test] public void VectorControlsHaveBoundsAndMetadata()
    {
        var art=EnvironmentArtProfile.Load();var fields=art.Parameters().Where(f=>f.Path.Contains(".orderConfirmation")).ToArray();Assert.AreEqual(9,fields.Length);
        foreach(var f in fields){float original=f.Read();Assert.IsNotEmpty(f.Description);Assert.IsNotEmpty(f.Unit);Assert.Greater(f.Step,0);f.Write(f.Maximum+f.Step);Assert.Throws<ArgumentException>(()=>art.Validate());f.Write(original);}art.Validate();
    }
    [UnityTest] public IEnumerator VectorFramesRenderDistinctSilhouettesConvergeAndFade()
    {
        var host=new GameObject("Vector accepted-order animation proof");var panel=ScriptableObject.CreateInstance<PanelSettings>();panel.themeStyleSheet=Resources.Load<ThemeStyleSheet>("FoundationTheme");panel.scaleMode=PanelScaleMode.ConstantPixelSize;
        var target=new RenderTexture(800,360,24);target.Create();panel.targetTexture=target;var doc=host.AddComponent<UIDocument>();doc.panelSettings=panel;
        var root=doc.rootVisualElement;root.style.width=800;root.style.height=360;root.style.backgroundColor=new Color(.07f,.09f,.11f);root.style.unityFont=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        var art=EnvironmentArtProfile.Load();var layer=new PlayableOrderMarkerLayer(location=>new Vector2((float)location.Position.X,(float)location.Position.Z),art:art);root.Add(layer);
        var times=new[]{0f,.12f,.28f,.65f};var list=new List<PlayableQueueMarker>();
        for(int row=0;row<2;row++)for(int i=0;i<times.Length;i++)list.Add(new PlayableQueueMarker(row*4+i+1,row*4+i+1,1,row==0?PlayableCommandKind.Move:PlayableCommandKind.AttackMove,new NavLocation(new NavPoint(100+i*200,110+row*140),NavLocation.FlatSurface),QueueMarkerPhase.Pending,times[i]));
        layer.Set(list);
        foreach(var text in new[]{"VECTOR / ACCEPTED MOVE","VECTOR / ACCEPTED ATTACK"}){int row=text.EndsWith("MOVE")?0:1;var label=new Label(text);label.style.position=Position.Absolute;label.style.left=20;label.style.top=20+row*140;label.style.color=Color.white;label.style.fontSize=16;root.Add(label);}
        for(int i=0;i<times.Length;i++){var label=new Label((times[i]*1000).ToString("0")+" ms");label.style.position=Position.Absolute;label.style.left=75+i*200;label.style.top=310;label.style.color=Color.white;root.Add(label);}
        Texture2D pixels=null;
        try
        {
            yield return null;yield return null;yield return null;
            var old=RenderTexture.active;RenderTexture.active=target;pixels=new Texture2D(800,360,TextureFormat.RGB24,false);pixels.ReadPixels(new Rect(0,0,800,360),0,0);pixels.Apply();RenderTexture.active=old;
            int Count(int cx,int cy,int range=45){int count=0;for(int y=cy-range;y<=cy+range;y++)for(int x=cx-range;x<=cx+range;x++){var c=pixels.GetPixel(x,359-y);if(c.g>.5f&&c.g>c.r*2||c.r>.5f&&c.r>c.g*2)count++;}return count;}
            for(int row=0;row<2;row++){for(int i=0;i<3;i++)Assert.Greater(Count(100+i*200,110+row*140),30,"visible animation frame");Assert.Zero(Count(700,110+row*140),"confirmation must fully disappear at configured duration");}
            Assert.Greater(Count(100,110,15),20,"the exact destination must be readable from the first frame, before arrows converge");
            var folder=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.local/order-marker-captures"));Directory.CreateDirectory(folder);File.WriteAllBytes(Path.Combine(folder,"vector-animation.png"),pixels.EncodeToPNG());
        }
        finally{if(pixels)UnityEngine.Object.DestroyImmediate(pixels);panel.targetTexture=null;UnityEngine.Object.DestroyImmediate(host);UnityEngine.Object.DestroyImmediate(panel);target.Release();UnityEngine.Object.DestroyImmediate(target);}
    }
    [UnityTest] public IEnumerator ActiveAndPendingMarkersRenderAsSmallHollowRings()
    {
        var host=new GameObject("Order marker style proof");var panel=ScriptableObject.CreateInstance<PanelSettings>();panel.themeStyleSheet=Resources.Load<ThemeStyleSheet>("FoundationTheme");panel.scaleMode=PanelScaleMode.ConstantPixelSize;
        var target=new RenderTexture(256,128,24);target.Create();panel.targetTexture=target;
        var doc=host.AddComponent<UIDocument>();doc.panelSettings=panel;var root=doc.rootVisualElement;root.style.width=256;root.style.height=128;root.style.backgroundColor=new Color(.18f,.22f,.25f);root.style.unityFont=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        var layer=new PlayableOrderMarkerLayer(location=>new Vector2((float)location.Position.X,64));root.Add(layer);
        var positions=new[]{48,104,160,216};layer.Set(positions.Select((x,i)=>new PlayableQueueMarker(i+1,i+1,i+1,i%2==0?PlayableCommandKind.Move:PlayableCommandKind.AttackMove,new NavLocation(new NavPoint(x,0),NavLocation.FlatSurface),i<2?QueueMarkerPhase.Active:QueueMarkerPhase.Pending)).ToArray());
        Texture2D pixels=null;
        try
        {
            yield return null;yield return null;yield return null;
            var old=RenderTexture.active;RenderTexture.active=target;pixels=new Texture2D(256,128,TextureFormat.RGB24,false);pixels.ReadPixels(new Rect(0,0,256,128),0,0);pixels.Apply();RenderTexture.active=old;
            var background=pixels.GetPixel(20,64);
            foreach(int x in positions)
            {
                // Interior sample misses the central digit, proving that no disk/background fill remains.
                var interior=pixels.GetPixel(x+5,64);Assert.Less(Vector3.Distance(new Vector3(background.r,background.g,background.b),new Vector3(interior.r,interior.g,interior.b)),.03f);
                int colored=0;for(int y=52;y<=76;y++)for(int xx=x-12;xx<=x+12;xx++){var color=pixels.GetPixel(xx,y);if(color.g>color.r*2&&color.g>.5f||color.r>color.g*2&&color.r>.5f)colored++;}
                Assert.Greater(colored,15);Assert.Less(colored,200,"Small outline must not become a filled marker");
            }
            var folder=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.local/order-marker-captures"));Directory.CreateDirectory(folder);File.WriteAllBytes(Path.Combine(folder,"hollow-style.png"),pixels.EncodeToPNG());
        }
        finally{if(pixels)UnityEngine.Object.DestroyImmediate(pixels);panel.targetTexture=null;UnityEngine.Object.DestroyImmediate(host);UnityEngine.Object.DestroyImmediate(panel);target.Release();UnityEngine.Object.DestroyImmediate(target);}
    }
    [UnityTest] public IEnumerator ProductionVariantASelectionAndAcceptedOrderExpireWithoutPermanentChain()
    {
        var profile=PlayableProfile.Default;var type=typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.PlayableDomain",true);var domain=Activator.CreateInstance(type,F,null,new object[]{profile,1L,false},null);
        object Domain(string n,params object[] a)=>type.GetMethod(n,F).Invoke(domain,a);
        var nav=(NavigationSession)type.GetProperty("Navigation",F).GetValue(domain);long seq=0;
        int Spawn(NavPoint p,PlayableOwner o)=>(int)Domain("SpawnUnit",p,o,PlayableEntityKind.Tank);
        var own=new[]{Spawn(new NavPoint(-14,-14),PlayableOwner.Player),Spawn(new NavPoint(-18,-14),PlayableOwner.Player)};var other=new[]{Spawn(new NavPoint(-10,-10),PlayableOwner.Enemy),Spawn(new NavPoint(-6,-10),PlayableOwner.Enemy)};
        void Send(string owner,int[] ids,NavPoint point,PlayableCommandKind kind,bool append=false,int target=0)=>Assert.AreEqual(PlayableCommandStatus.Applied,Domain("Apply",new PlayableCommand(1,++seq,owner,kind,ids,point,targetId:target,mode:append?PlayableOrderMode.Append:PlayableOrderMode.Replace),null));
        Send("player-1",own,new NavPoint(-8,-14),PlayableCommandKind.Move);Send("enemy-1",other,new NavPoint(-4,-10),PlayableCommandKind.Move);
        using(var route=new UnityHostRouteService()){var geometry=(NavGeometry)type.GetProperty("Geometry",F).GetValue(domain);route.Service(nav.Requests,nav.Answers,1,geometry,profile,64);nav.ApplyResults();Domain("Step",1d/30);Assert.Greater(route.NavMeshRequests+route.FlowRequests,0);}
        Send("player-1",own,new NavPoint(-4,-4),PlayableCommandKind.AttackMove,true);Send("player-1",own,default,PlayableCommandKind.Attack,true,other[0]);
        Send("enemy-1",other,new NavPoint(-16,-4),PlayableCommandKind.AttackMove,true);Send("enemy-1",other,default,PlayableCommandKind.Attack,true,own[0]);
        var folder=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.local/order-marker-captures"));Directory.CreateDirectory(folder);
        var priorLights=UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Select(x=>x.GetInstanceID()).ToArray();
        var state=new List<object>();var frames=new List<object>();
        for(int seat=0;seat<2;seat++)
        {
            var owner=(PlayableOwner)seat;var view=(PlayableSnapshot)Domain("PlayerSnapshot",1L,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7,owner);var selected=seat==0?own:other;var chain=PlayableQueueMarkers.ForSelection(view,selected);Assert.AreEqual(3,chain.Count);Assert.AreEqual(QueueMarkerPhase.Active,chain[0].Phase);Assert.True(chain.All(m=>m.Location.HasValue));
            state.Add(new{seed=7,owner=view.OwnerId,tick=view.Tick,selection=selected,markers=chain.Select(m=>new{m.Number,m.IssuanceId,m.CommandSequence,kind=m.Kind.ToString(),phase=m.Phase.ToString(),x=m.Location.Value.Position.X,z=m.Location.Value.Position.Z}),visible=view.Entities.Select(e=>new{e.Id,owner=e.Owner.ToString(),x=e.Position.X,z=e.Position.Z})});
            var go=new GameObject("queue owner placement preview");var hud=go.AddComponent<PlayableBootstrap>();hud.enabled=false;
            void Set(string n,object x)=>typeof(PlayableBootstrap).GetField(n,F).SetValue(hud,x);
            T Get<T>(string n)=>(T)typeof(PlayableBootstrap).GetField(n,F).GetValue(hud);
            object Call(string n,params object[] a)=>typeof(PlayableBootstrap).GetMethods(F).Single(m=>m.Name==n&&m.GetParameters().Length==a.Length).Invoke(hud,a);
            Set("profile",profile);Call("CreateHud");Call("CreateWorld");var camera=Get<Camera>("cameraView");camera.orthographicSize=14;camera.transform.position=new Vector3(-11,28,-35);camera.transform.LookAt(new Vector3(-11,0,-13));
            var world=Get<PlayableWorld>("world");world.Fog.Update(view.Vision,1);
            foreach(var entity in view.Entities){var actor=world.Tank(entity.Id,entity.Owner==view.Owner,entity.Kind);actor.Root.position=world.Point(entity.Position);}
            var root=Get<VisualElement>("root");var panel=go.GetComponent<UIDocument>().panelSettings;root.style.width=800;root.style.height=620;
            var texture=new RenderTexture(800,620,24);var backdrop=new RenderTexture(800,620,24);texture.Create();backdrop.Create();camera.targetTexture=backdrop;panel.targetTexture=texture;
            var scene=new VisualElement{pickingMode=PickingMode.Ignore};scene.style.position=Position.Absolute;scene.style.left=0;scene.style.top=0;scene.style.right=0;scene.style.bottom=0;scene.style.backgroundImage=new StyleBackground(Background.FromRenderTexture(backdrop));root.Insert(0,scene);
            Set("view",view);foreach(int id in selected)Get<HashSet<int>>("selection").Add(id);Call("ApplyResponsiveHud",800f);Call("UpdateHud");Get<PlayableMapTerrain>("mapTerrain").Update(world.Fog);var map=Get<PlayableMapSurface>("compactMap");map.Set(view,new HashSet<int>(selected),null);
            try
            {
                var model=Get<PlayableOrderMarkers>("orderMarkers");var layer=Get<PlayableOrderMarkerLayer>("orderMarkerLayer");var selection=Get<HashSet<int>>("selection");
                IEnumerator Frame(string name,int expected){Call("UpdateHud");Call("UpdateOrderMarkers",(object)Array.Empty<PlayableCommandReceipt>());Assert.AreEqual(expected,layer.VisibleCount);yield return null;yield return null;var previous=RenderTexture.active;RenderTexture.active=texture;var pixels=new Texture2D(800,620,TextureFormat.RGBA32,false);pixels.ReadPixels(new Rect(0,0,800,620),0,0);pixels.Apply();RenderTexture.active=previous;frames.Add(new{stage=name,seat,seed=7,owner=view.OwnerId,view.Sequence,view.Tick,view.Paused,selection=selection.ToArray(),markers=layer.VisibleCount,presentationTime=Time.unscaledTimeAsDouble});File.WriteAllBytes(Path.Combine(folder,name+"-seat-"+seat+".png"),pixels.EncodeToPNG());UnityEngine.Object.DestroyImmediate(pixels);}
                Call("SelectionMarkerEvent");yield return Frame("01-selection-before-expiry",1);
                yield return new WaitForSecondsRealtime(1.08f);yield return Frame("02-still-selected-after-expiry",0);Assert.AreEqual(2,selection.Count);
                var command=new PlayableCommand(1,++seq,view.OwnerId,PlayableCommandKind.Move,selected,new NavPoint(-8,-12),mode:PlayableOrderMode.Append);model.Submitted(view,command);var args=new object[]{command,null};var status=(PlayableCommandStatus)type.GetMethod("Apply",F).Invoke(domain,args);Assert.AreEqual(PlayableCommandStatus.Applied,status);view=(PlayableSnapshot)Domain("PlayerSnapshot",seq,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7,owner);Set("view",view);selection.Clear();map.Set(view,selection,null);
                Call("UpdateOrderMarkers",(object)new[]{new PlayableCommandReceipt(command.Sequence,view.Tick,status,(string)args[1],0,view.OwnerId)});yield return Frame("03-recent-accepted-deselected",1);
                yield return new WaitForSecondsRealtime(1.08f);yield return Frame("04-recent-expired-deselected",0);Assert.True(view.Entities.Where(e=>selected.Contains(e.Id)).All(e=>e.Queue.Deferred.Count>=3));
                int target=seat==0?other[0]:own[0];Send(view.OwnerId,selected,default,PlayableCommandKind.Attack,false,target);view=(PlayableSnapshot)Domain("PlayerSnapshot",++seq,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7,owner);Set("view",view);foreach(int id in selected)selection.Add(id);map.Set(view,selection,null);Call("SelectionMarkerEvent");yield return Frame("05-visible-direct-attack",1);
                var actor=nav.Crowd.Units.Single(u=>u.Id==target);var location=new NavPoint(26,26);typeof(NavUnit).GetProperty("Position").SetValue(actor,location);typeof(NavUnit).GetProperty("Location").SetValue(actor,new NavLocation(location,NavLocation.FlatSurface));view=(PlayableSnapshot)Domain("PlayerSnapshot",++seq,RuntimeStatus.Running,true,new PlayableRuntimeMetrics(0,0,0,0,0),null,7,owner);Set("view",view);world.Fog.Update(view.Vision,1);foreach(var id in world.Actors.Keys.Where(id=>!view.Entities.Any(e=>e.Id==id)).ToArray())world.Remove(id);Get<PlayableMapTerrain>("mapTerrain").Update(world.Fog);map.Set(view,selection,null);yield return Frame("06-hidden-living-target-paused",0);
            }
            finally{camera.targetTexture=null;panel.targetTexture=null;texture.Release();backdrop.Release();UnityEngine.Object.DestroyImmediate(texture);UnityEngine.Object.DestroyImmediate(backdrop);Set("cameraView",camera);UnityEngine.Object.DestroyImmediate(go);int target=seat==0?other[0]:own[0];var actor=nav.Crowd.Units.Single(u=>u.Id==target);var original=seat==0?new NavPoint(-10,-10):new NavPoint(-14,-14);typeof(NavUnit).GetProperty("Position").SetValue(actor,original);typeof(NavUnit).GetProperty("Location").SetValue(actor,new NavLocation(original,NavLocation.FlatSurface));}
        }
        foreach(var light in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Where(x=>!priorLights.Contains(x.GetInstanceID())))UnityEngine.Object.DestroyImmediate(light.gameObject);
        File.WriteAllText(Path.Combine(folder,"state.json"),JsonConvert.SerializeObject(new{initialAuthority=state,captures=frames},Formatting.Indented));
    }
}
