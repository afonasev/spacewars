using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Spacewars.Tests.PlayMode
{
    public sealed class RelayFogMapPresentationTests
    {
        [UnityTest] public IEnumerator PublicRelaysAndAllSiteMarkersUseOnlyObservedControl()
        {
            var profile=PlayableProfile.ThreeCrossingsDefault;
            var sites=TerritoryRules.Sites(profile).ToArray();var relay=sites.First(s=>s.Kind==PlayableBuildingKind.RelayTower);
            var vision=new PlayableVision(0,profile.ArenaHalfExtent,profile.ArenaHalfExtent,profile.VisionCellSize,profile.FogEdgeFeather);
            var host=new GameObject("Relay fog rendering");var uiHost=new GameObject("Relay fog map comparison");
            var cameraHost=new GameObject("Relay fog camera");var camera=cameraHost.AddComponent<Camera>();
            var panel=ScriptableObject.CreateInstance<PanelSettings>();
            var world=new PlayableWorld(host.transform,profile);var textures=new List<RenderTexture>();var terrains=new List<PlayableMapTerrain>();
            var output=Environment.GetEnvironmentVariable("RELAY_FOG_EVIDENCE")??Path.GetFullPath("../.local/relay-fog-minimap-knowledge/screenshots");Directory.CreateDirectory(output);
            try
            {
                var doc=uiHost.AddComponent<UIDocument>();panel.themeStyleSheet=Resources.Load<ThemeStyleSheet>("FoundationTheme");panel.scaleMode=PanelScaleMode.ConstantPixelSize;
                var target=new RenderTexture(1280,680,24);target.Create();textures.Add(target);panel.targetTexture=target;doc.panelSettings=panel;
                var root=doc.rootVisualElement;root.style.backgroundColor=new Color(.035f,.05f,.065f);root.style.unityFont=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                var title=new Label("Вышки связи и точки карты · Unity Play Mode · seed 71");title.style.fontSize=21;title.style.color=Color.white;title.style.marginLeft=16;title.style.marginTop=12;root.Add(title);
                var row=new VisualElement();row.style.flexDirection=FlexDirection.Row;row.style.marginTop=16;root.Add(row);
                string[] captions={"Не разведана","Наблюдали свой контроль","Скрытый захват врагом","Повторная разведка"};
                string[] notes={"Нейтральная до разведки","Видимый владелец","Последний известный владелец","Новый владелец после разведки"};
                for(int stage=0;stage<4;stage++)
                {
                    bool visible=stage==1||stage==3;var control=stage<2?PlayableOwner.Player:PlayableOwner.Enemy;
                    vision.Refresh(visible?new[]{new VisionSource(relay.Position,9)}:Array.Empty<VisionSource>(),Array.Empty<KnownBuilding>());
                    var state=new TerritorySiteSnapshot(relay,control,control,1,false,0,true,stage*5);
                    vision.ObserveRelays(new[]{state});
                    var view=new PlayableSnapshot(profile.ProfileId,profile.Revision,1,71,stage+1,stage+1,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,0,null,Array.Empty<PlayableEntitySnapshot>(),Array.Empty<PlayableBuildingSnapshot>(),Array.Empty<PlayableProjectileSnapshot>(),null,null,
                        sites:visible?new[]{state}:Array.Empty<TerritorySiteSnapshot>(),vision:vision.Snapshot(),discoveredSites:stage==0?Array.Empty<TerritorySite>():new[]{relay},publicSites:sites,activeProfile:profile);
                    world.Fog.Update(view.Vision,100);world.RenderSites(view,camera);
                    var model=host.GetComponentsInChildren<RelayTowerPresentation>().Single(r=>r.name=="Relay site "+relay.Id);
                    Assert.True(model.gameObject.activeInHierarchy,"Permanent tower remains visible through fog");
                    Assert.AreEqual(stage!=0,model.Controlled);
                    var point=world.Point(relay.Position);camera.orthographic=true;camera.orthographicSize=6;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.035f,.05f,.065f);
                    camera.transform.position=point+new Vector3(9,12,-14);camera.transform.LookAt(point+Vector3.up*2);
                    var scene=new RenderTexture(300,310,24);scene.Create();textures.Add(scene);camera.targetTexture=scene;camera.Render();
                    var column=new VisualElement();column.style.width=320;column.style.alignItems=Align.Center;row.Add(column);
                    var caption=new Label(captions[stage]);caption.style.fontSize=17;caption.style.color=Color.white;column.Add(caption);
                    var image=new Image{image=scene,scaleMode=ScaleMode.ScaleToFit};image.style.width=300;image.style.height=310;column.Add(image);
                    var terrain=new PlayableMapTerrain(profile);terrains.Add(terrain);terrain.Update(world.Fog);
                    var map=new PlayableMapSurface(profile,terrain.Texture);map.style.width=180;map.style.height=180;map.Set(view,new HashSet<int>(),null);column.Add(map);
                    var marks=(IReadOnlyList<PlayableMapMark>)typeof(PlayableMapSurface).GetField("marks",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(map);
                    Assert.AreEqual(sites.Length,marks.Count,"Every public site uses its original geometric marker");
                    Assert.AreEqual(stage==0?(PlayableOwner?)null:stage==3?PlayableOwner.Enemy:PlayableOwner.Player,marks.Single(m=>m.Id==-relay.Id).Owner);
                    var note=new Label(notes[stage]);note.style.color=new Color(.7f,.8f,.85f);note.style.fontSize=13;note.style.marginTop=10;column.Add(note);
                    Save(scene,Path.Combine(output,"relay-fog-stage-"+stage+".png"));
                }
                for(int i=0;i<8;i++)yield return null;
                Save(target,Path.Combine(output,"relay-fog-and-public-map-sites.png"));
            }
            finally
            {
                camera.targetTexture=null;panel.targetTexture=null;world.Dispose();foreach(var terrain in terrains)terrain.Dispose();
                UnityEngine.Object.DestroyImmediate(host);UnityEngine.Object.DestroyImmediate(uiHost);UnityEngine.Object.DestroyImmediate(cameraHost);UnityEngine.Object.DestroyImmediate(panel);
                foreach(var texture in textures){texture.Release();UnityEngine.Object.DestroyImmediate(texture);}
            }
        }
        static void Save(RenderTexture target,string path)
        {
            var prior=RenderTexture.active;RenderTexture.active=target;var image=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);
            try{image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();Assert.Greater(image.GetPixels32().Where((_,i)=>i%71==0).Select(c=>(int)c.r<<16|(int)c.g<<8|c.b).Distinct().Count(),8);File.WriteAllBytes(path,image.EncodeToPNG());}
            finally{RenderTexture.active=prior;UnityEngine.Object.DestroyImmediate(image);}
        }
    }
}
