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
