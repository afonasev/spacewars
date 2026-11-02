using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Input;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

public sealed class SelectedTowerRangeTests
{
    private static PlayableBuildingSnapshot Tower(int id,PlayableBuildingKind kind,NavPoint point,ConstructionPhase phase=ConstructionPhase.Ready,PlayableOwner owner=PlayableOwner.Player,int health=300)
        =>new PlayableBuildingSnapshot(id,owner,kind,point,health,phase==ConstructionPhase.Ready?1:.5,0,0,default,phase:phase);
    private static PlayableSnapshot Frame(PlayableProfile profile,params PlayableBuildingSnapshot[] towers)
        =>new PlayableSnapshot(profile.ProfileId,profile.Revision,1,7,1,1,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,1000,null,Array.Empty<PlayableEntitySnapshot>(),towers,Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,activeProfile:profile);
    private static MaterialPropertyBlock Properties(PlayableWorld world){var b=new MaterialPropertyBlock();world.TowerRangeVisual.GetComponent<MeshRenderer>().GetPropertyBlock(b);return b;}

    [UnityTest] public IEnumerator RelaySelectionUsesActualCaptureRadiusAndOffersNoActions()
    {
        var host=new GameObject("Selected relay inspection");var app=host.AddComponent<PlayableBootstrap>();app.enabled=false;
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        void Set(string name,object value)=>typeof(PlayableBootstrap).GetField(name,flags).SetValue(app,value);
        T Get<T>(string name)=>(T)typeof(PlayableBootstrap).GetField(name,flags).GetValue(app);
        object Call(string name,params object[] args)=>typeof(PlayableBootstrap).GetMethods(flags).Single(m=>m.Name==name&&m.GetParameters().Length==args.Length).Invoke(app,args);
        var profile=PlayableProfile.ThreeCrossingsDefault;var site=TerritoryRules.Sites(profile).Single(v=>v.Id==7);
        PlayableSnapshot RelayFrame(PlayableOwner? owner,PlayableProfile p=null,bool known=true){p=p??profile;return new PlayableSnapshot(p.ProfileId,p.Revision,1,7112026,1,1,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,500,null,Array.Empty<PlayableEntitySnapshot>(),Array.Empty<PlayableBuildingSnapshot>(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,sites:known?new[]{new TerritorySiteSnapshot(site,owner,owner,owner.HasValue?1:0,false,0,owner.HasValue)}:Array.Empty<TerritorySiteSnapshot>(),activeProfile:p,discoveredSites:new[]{site});}
        var oldAmbient=RenderSettings.ambientLight;GameObject lightHost=null;RenderTexture scene=null,target=null;VisualElement backdrop=null;PanelSettings panel=null;
        IEnumerator Capture(string name){
            var output=Environment.GetEnvironmentVariable("RELAY_UI_EVIDENCE");if(string.IsNullOrEmpty(output))yield break;
            var camera=Get<Camera>("cameraView");camera.targetTexture=scene;camera.Render();camera.targetTexture=null;
            for(int i=0;i<8;i++)yield return null;
            var previous=RenderTexture.active;RenderTexture.active=target;var png=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);
            try{png.ReadPixels(new Rect(0,0,target.width,target.height),0,0);png.Apply();Directory.CreateDirectory(output);File.WriteAllBytes(Path.Combine(output,name+".png"),png.EncodeToPNG());}
            finally{RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(png);}
        }
        try
        {
            Set("profile",profile);Call("CreateWorld");Call("CreateHud");Set("input",host.AddComponent<PlayableInput>());Set("view",RelayFrame(null));
            var world=Get<PlayableWorld>("world");world.Fog.ShowPublic();var camera=Get<Camera>("cameraView");camera.orthographicSize=9;camera.transform.position=new Vector3(-32,13,-15);camera.transform.LookAt(new Vector3(-40,1.5f,0));
            lightHost=new GameObject("Relay range daylight");var light=lightHost.AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.3f;light.transform.rotation=Quaternion.Euler(50,-30,0);RenderSettings.ambientLight=new Color(.6f,.65f,.7f);
            scene=new RenderTexture(1280,800,24);target=new RenderTexture(1280,800,24);scene.Create();target.Create();panel=host.GetComponent<UIDocument>().panelSettings;panel.targetTexture=target;
            backdrop=new VisualElement{pickingMode=PickingMode.Ignore};backdrop.style.position=Position.Absolute;backdrop.style.left=backdrop.style.right=backdrop.style.top=backdrop.style.bottom=0;backdrop.style.backgroundImage=new StyleBackground(Background.FromRenderTexture(scene));Get<VisualElement>("root").Insert(0,backdrop);
            Call("Render");Call("UpdateSiteHud");Call("UpdateBattleHud",new object[]{null});Assert.IsNull(world.TowerRangeVisual);yield return Capture("relay-unselected");
            var point=(Vector2)camera.WorldToScreenPoint(world.Point(site.Position));Call("Select",point,point,false);Assert.AreEqual(7,Get<int>("selectedSite"),"actual ground selection hook");
            foreach(PlayableOwner? owner in new PlayableOwner?[]{null,PlayableOwner.Player,PlayableOwner.Enemy,PlayableOwner.Third})
            {
                Set("view",RelayFrame(owner));Call("Render");Call("UpdateSiteHud");Call("UpdateBattleHud",new object[]{null});
                Assert.AreEqual(7,world.RangeRelaySiteId);Assert.AreEqual(0,world.RangeTowerId);Assert.True(world.TowerRangeVisual.activeSelf);
                Assert.That(Properties(world).GetVector("_Zone").w,Is.EqualTo(profile.RelayCaptureRadius).Within(.0001));
                Assert.IsEmpty((OfflinePadAction[])Call("CurrentBattleActions"));
                foreach(string field in new[]{"repairBuilding","sellBuilding","battleRing","armyRegion"})Assert.AreEqual(DisplayStyle.None,Get<VisualElement>(field).style.display.value,field+" stays absent for a relay");
                if(!owner.HasValue)yield return Capture("relay-selected-radius");
            }
            var changed=profile.CopyData();changed.relayCaptureRadius=4.2;var adjusted=PlayableProfile.Create(changed,profile.AuthoredMap);Set("view",RelayFrame(null,adjusted));Call("Render");Assert.That(Properties(world).GetVector("_Zone").w,Is.EqualTo(adjusted.RelayCaptureRadius).Within(.0001));
            Set("selectedSite",0);Call("Render");Assert.False(world.TowerRangeVisual.activeSelf);
            Set("selectedSite",7);Set("view",RelayFrame(null,known:false));Call("Render");Assert.False(world.TowerRangeVisual.activeSelf);Assert.AreEqual(0,world.RangeRelaySiteId);
            Set("view",RelayFrame(null));Set("inLobby",true);Call("Render");Assert.False(world.TowerRangeVisual.activeSelf);
        }
        finally{if(panel)panel.targetTexture=null;backdrop?.RemoveFromHierarchy();if(scene){scene.Release();UnityEngine.Object.DestroyImmediate(scene);}if(target){target.Release();UnityEngine.Object.DestroyImmediate(target);}if(lightHost)UnityEngine.Object.DestroyImmediate(lightHost);UnityEngine.Object.DestroyImmediate(host);RenderSettings.ambientLight=oldAmbient;}
    }

    [Test] public void SelectionLifecycleUsesActualRangesReadyStateAndIndependentLayers()
    {
        var host=new GameObject("Tower coverage lifecycle");var otherHost=new GameObject("Other local world");var profile=PlayableProfile.Default;
        var world=new PlayableWorld(host.transform,profile);var other=new PlayableWorld(otherHost.transform,profile);
        try
        {
            world.SetPresentationLayer(8);other.SetPresentationLayer(9);
            var repair=Tower(10,PlayableBuildingKind.RepairTower,new NavPoint(-8,0));var sentry=Tower(20,PlayableBuildingKind.SentryTurret,new NavPoint(0,0));var gauss=Tower(30,PlayableBuildingKind.GaussTower,new NavPoint(8,0));
            var frame=Frame(profile,gauss,sentry,repair);var selection=new HashSet<int>();
            world.RenderTowerRange(frame,selection);Assert.IsNull(world.TowerRangeVisual,"No allocation while no tower is selected");
            selection.Add(30);world.RenderTowerRange(frame,selection);Assert.AreEqual(30,world.RangeTowerId);Assert.AreEqual((float)profile.GaussRange,Properties(world).GetVector("_Zone").w);
            var visual=world.TowerRangeVisual;var mesh=visual.GetComponent<MeshFilter>().sharedMesh;var material=visual.GetComponent<Renderer>().sharedMaterial;
            Assert.AreEqual(8,visual.layer);Assert.IsNull(visual.GetComponent<Collider>());Assert.AreEqual(UnityEngine.Rendering.ShadowCastingMode.Off,visual.GetComponent<MeshRenderer>().shadowCastingMode);Assert.False(visual.GetComponent<MeshRenderer>().receiveShadows);
            selection.UnionWith(new[]{10,20});world.RenderTowerRange(frame,selection,20);Assert.AreEqual(20,world.RangeTowerId);Assert.That(Properties(world).GetVector("_Zone").w,Is.EqualTo(profile.ExplorerRange*profile.TurretRangeMultiplier).Within(.0001));
            var amber=Properties(world).GetColor("_Color");world.RenderTowerRange(frame,selection);Assert.AreEqual(10,world.RangeTowerId);Assert.Greater(Properties(world).GetColor("_Color").g,Properties(world).GetColor("_Color").r);Assert.Greater(amber.r,amber.b);
            Assert.AreSame(mesh,visual.GetComponent<MeshFilter>().sharedMesh);Assert.AreSame(material,visual.GetComponent<Renderer>().sharedMaterial,"Switching towers reuses terrain and material");
            other.RenderTowerRange(frame,new HashSet<int>());Assert.IsNull(other.TowerRangeVisual);
            other.RenderTowerRange(frame,new HashSet<int>{20});Assert.AreEqual(9,other.TowerRangeVisual.layer);Assert.AreNotSame(material,other.TowerRangeVisual.GetComponent<Renderer>().sharedMaterial);
            foreach(var disabled in new[]{Frame(profile),Frame(profile,Tower(10,PlayableBuildingKind.RepairTower,repair.Position,ConstructionPhase.Constructing)),Frame(profile,Tower(10,PlayableBuildingKind.Factory,repair.Position)),Frame(profile,Tower(10,PlayableBuildingKind.RepairTower,repair.Position,owner:PlayableOwner.Enemy)),Frame(profile,Tower(10,PlayableBuildingKind.RepairTower,repair.Position,health:0))})
            {world.RenderTowerRange(disabled,new HashSet<int>{10});Assert.False(visual.activeSelf);Assert.AreEqual(0,world.RangeTowerId);}
            world.RenderTowerRange(frame,selection,visible:false);Assert.False(visual.activeSelf);
            var data=profile.CopyData();data.turretRange*=1.2;data.towerRangeFillOpacity=.15;var changed=PlayableProfile.Create(data);
            world.RenderTowerRange(Frame(changed,repair),new HashSet<int>{10});Assert.That(Properties(world).GetVector("_Zone").w,Is.EqualTo(changed.TurretRange).Within(.0001));Assert.AreEqual(.15f,Properties(world).GetVector("_Appearance").x);
            world.Rebind(changed);Assert.IsNull(world.TowerRangeVisual);Assert.AreEqual(0,world.RangeTowerId);
            world.RenderTowerRange(Frame(changed,repair),new HashSet<int>{10});world.Clear();Assert.IsNull(world.TowerRangeVisual);
        }
        finally{world.Dispose();other.Dispose();UnityEngine.Object.DestroyImmediate(host);UnityEngine.Object.DestroyImmediate(otherHost);}
    }

    [UnityTest] public IEnumerator RenderedCoverageFollowsAuthoredBaseAndRamp()
    {
        var profile=PlayableProfile.ThreeCrossingsDefault;var terrain=profile.AuthoredMap;
        var site=TerritoryRules.Sites(profile).First(s=>s.Kind==PlayableBuildingKind.Headquarters);
        var ramps=terrain.Supports.Where(s=>Math.Abs(s.Gradient.X)+Math.Abs(s.Gradient.Z)>0).ToArray();
        double RampDistance(NavPoint p)=>ramps.Min(r=>{double x=(r.Bounds.MinX+r.Bounds.MaxX)/2,z=(r.Bounds.MinZ+r.Bounds.MaxZ)/2;return (p.X-x)*(p.X-x)+(p.Z-z)*(p.Z-z);});
        var slot=site.DefenseSlots(profile).OrderBy(s=>RampDistance(s.Position)).First();
        var host=new GameObject("Authored tower range terrain");var cameraHost=new GameObject("Authored range camera");var lightHost=new GameObject("Authored range light");var oldAmbient=RenderSettings.ambientLight;
        var world=new PlayableWorld(host.transform,profile);world.SetPresentationLayer(10);world.Fog.ShowPublic();
        var light=lightHost.AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.3f;light.transform.rotation=Quaternion.Euler(50,-30,0);RenderSettings.ambientLight=new Color(.6f,.65f,.7f);
        var camera=cameraHost.AddComponent<Camera>();camera.cullingMask=1<<10;camera.orthographic=true;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.05f,.07f,.09f);camera.orthographicSize=(float)PlayableWorld.TowerActionRange(profile,PlayableBuildingKind.RepairTower)*1.4f;
        var target=new RenderTexture(1280,800,24);target.antiAliasing=4;target.Create();camera.targetTexture=target;
        try
        {
            var baseActor=world.Building(1,"Headquarters",true);baseActor.Root.position=world.Point(site.Position);baseActor.Health.parent.gameObject.SetActive(false);
            var tower=Tower(2,PlayableBuildingKind.RepairTower,slot.Position);var actor=world.Building(2,"RepairTower",true);actor.Root.position=world.Point(slot.Position);actor.Selection.SetActive(true);actor.Health.parent.gameObject.SetActive(false);
            var selected=new HashSet<int>{2};world.RenderTowerRange(Frame(profile,tower),selected);
            var surface=world.TowerRangeVisual.GetComponent<MeshFilter>().sharedMesh;var verts=surface.vertices;var faces=surface.triangles;
            Assert.Greater(verts.Select(v=>v.y).Distinct().Count(),1,"Authored fixture has at least two distinct floors");
            Assert.True(Enumerable.Range(0,faces.Length/3).Any(i=>Mathf.Abs(verts[faces[i*3]].y-verts[faces[i*3+1]].y)+Mathf.Abs(verts[faces[i*3]].y-verts[faces[i*3+2]].y)>.0001f),"A real ramp triangle connects different endpoint heights without an invented intermediate level");
            var centre=world.Point(slot.Position);camera.transform.position=centre+new Vector3(0,24,-24);camera.transform.LookAt(centre);
            for(int i=0;i<2;i++)yield return null;
            foreach(bool visible in new[]{true,false})
            {
                world.RenderTowerRange(Frame(profile,tower),visible?selected:new HashSet<int>());camera.Render();
                var previous=RenderTexture.active;RenderTexture.active=target;var png=new Texture2D(1280,800,TextureFormat.RGB24,false);
                try{png.ReadPixels(new Rect(0,0,1280,800),0,0);png.Apply();var output=Environment.GetEnvironmentVariable("TOWER_RANGE_EVIDENCE");if(!string.IsNullOrEmpty(output)){Directory.CreateDirectory(output);File.WriteAllBytes(Path.Combine(output,visible?"repair-authored-ramp-selected.png":"repair-authored-ramp-deselected.png"),png.EncodeToPNG());}}
                finally{RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(png);}
            }
        }
        finally{camera.targetTexture=null;target.Release();UnityEngine.Object.DestroyImmediate(target);world.Dispose();UnityEngine.Object.DestroyImmediate(host);UnityEngine.Object.DestroyImmediate(cameraHost);UnityEngine.Object.DestroyImmediate(lightHost);RenderSettings.ambientLight=oldAmbient;}
    }

    [Test] public void ReadabilityFieldsAreLabRegisteredAndValidated()
    {
        var fields=PlayableProfileMetadata.Fields.Where(f=>f.Path.StartsWith("presentation.towerRange.")).ToArray();Assert.AreEqual(10,fields.Length);var data=PlayableProfile.Default.CopyData();
        foreach(var f in fields){Assert.IsNotEmpty(f.Group);Assert.IsNotEmpty(f.Label);Assert.IsNotEmpty(f.Description);Assert.IsNotEmpty(f.Unit);Assert.Greater(f.Step,0);Assert.That(f.Read(data),Is.InRange(f.Minimum,f.Maximum));Assert.Throws<ArgumentOutOfRangeException>(()=>f.Write(data,double.NaN));}
        data.towerRangeFillOpacity=.9;Assert.Throws<ArgumentException>(()=>PlayableProfile.Create(data));
        var legacyJson=System.Text.RegularExpressions.Regex.Replace(JsonUtility.ToJson(PlayableProfile.Default.CopyData()),",\\s*\"towerRange[^\"]+\"\\s*:\\s*[-+0-9.eE]+","");
        StringAssert.DoesNotContain("towerRange",legacyJson);
        var restored=PlayableProfile.Create(JsonUtility.FromJson<PlayableProfileData>(legacyJson));Assert.AreEqual(.08,restored.TowerRangeFillOpacity,"Older profile JSON receives the approved visual defaults");
    }

    [Test] public void SurfaceMeshUsesExactSupportPlanesAndDoesNotBridgeVoids()
    {
        foreach(var map in new IPlayableTerrain[]{ThreeCrossingsMap.Default,new FoundryMap(new FoundryProfileData())})
        {
            var profile=PlayableProfile.Create(PlayableProfile.Default.CopyData(),map);var mesh=PlayableWorld.BuildTowerRangeSurface(profile);
            try
            {
                var vertices=mesh.vertices;var triangles=mesh.triangles;int offset=0,triangleOffset=0;Assert.True(map.Supports.Any(s=>Math.Abs(s.Gradient.X)+Math.Abs(s.Gradient.Z)>0),"Fixture must contain a real ramp");
                foreach(var support in map.Supports)
                {
                    int count=support.Bounds.Polygon?.Vertices.Count??4,indices=support.Bounds.Polygon?.Triangles.Count??6;
                    for(int i=0;i<count;i++){var v=vertices[offset+i];Assert.That(v.y,Is.EqualTo(support.HeightAt(new NavPoint(v.x,v.z))).Within(.0001));Assert.LessOrEqual(support.Bounds.DistanceSquared(new NavPoint(v.x,v.z)),1e-8,support.Id+" float vertex remains on the authored support boundary");}
                    for(int i=0;i<indices;i++)Assert.That(triangles[triangleOffset+i],Is.InRange(offset,offset+count-1),"Triangles cannot connect unrelated supports across cliffs/voids");
                    offset+=count;triangleOffset+=indices;
                }
                Assert.AreEqual(vertices.Length,offset);Assert.AreEqual(triangles.Length,triangleOffset);
            }
            finally{UnityEngine.Object.DestroyImmediate(mesh);}
        }
    }

    [UnityTest] public IEnumerator ActualSelectionHookShowsAndClearsRadius()
    {
        var host=new GameObject("Ordinary tower range selection hook");var app=host.AddComponent<PlayableBootstrap>();app.enabled=false;var flags=BindingFlags.Instance|BindingFlags.NonPublic;
        void Set(string name,object value)=>typeof(PlayableBootstrap).GetField(name,flags).SetValue(app,value);
        T Get<T>(string name)=>(T)typeof(PlayableBootstrap).GetField(name,flags).GetValue(app);
        object Call(string name,params object[] args)=>typeof(PlayableBootstrap).GetMethods(flags).Single(m=>m.Name==name&&m.GetParameters().Length==args.Length).Invoke(app,args);
        try
        {
            var profile=PlayableProfile.Default;Set("profile",profile);Call("CreateWorld");Call("CreateHud");Set("input",host.AddComponent<PlayableInput>());
            var tower=Tower(70,PlayableBuildingKind.RepairTower,new NavPoint(0,0));Set("view",Frame(profile,tower));var world=Get<PlayableWorld>("world");world.Fog.ShowPublic();var camera=Get<Camera>("cameraView");camera.transform.position=new Vector3(0,20,-18);camera.transform.LookAt(Vector3.zero);
            Call("Render");var screen=(Vector2)camera.WorldToScreenPoint(world.Point(tower.Position)+Vector3.up);Call("Select",screen,screen,false);Call("Render");Assert.AreEqual(70,world.RangeTowerId);Assert.True(world.TowerRangeVisual.activeSelf);
            Get<HashSet<int>>("selection").Clear();Call("Render");Assert.False(world.TowerRangeVisual.activeSelf);
            Get<HashSet<int>>("selection").Add(70);Set("inLobby",true);Call("Render");Assert.False(world.TowerRangeVisual.activeSelf);Set("inLobby",false);Set("preparing",true);Call("Render");Assert.False(world.TowerRangeVisual.activeSelf);
            yield return null;
        }
        finally{UnityEngine.Object.DestroyImmediate(host);}
    }

    [UnityTest] public IEnumerator RenderedZonesUseFunctionalColoursDisappearAndRespectFogAndLayer()
    {
        var host=new GameObject("Tower coverage render");var cameraHost=new GameObject("Coverage camera");var lightHost=new GameObject("Coverage lighting");var oldAmbient=RenderSettings.ambientLight;
        var profile=PlayableProfile.Default;var world=new PlayableWorld(host.transform,profile);var camera=cameraHost.AddComponent<Camera>();var light=lightHost.AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.2f;light.transform.rotation=Quaternion.Euler(50,-30,0);RenderSettings.ambientLight=new Color(.6f,.65f,.7f);
        var target=new RenderTexture(1280,800,24);target.antiAliasing=4;target.Create();camera.targetTexture=target;camera.orthographic=true;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.05f,.07f,.09f);
        Texture2D Read(){var previous=RenderTexture.active;RenderTexture.active=target;var t=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);t.ReadPixels(new Rect(0,0,target.width,target.height),0,0);t.Apply();RenderTexture.active=previous;return t;}
        int Count(Texture2D t,bool repair)=>t.GetPixels().Count(c=>repair?c.g>.4f&&c.g>c.r*1.5f&&c.g>c.b*1.3f:c.r>.5f&&c.r>c.g*1.12f&&c.g>c.b*1.4f);
        void Save(Texture2D t,string name){var path=Environment.GetEnvironmentVariable("TOWER_RANGE_EVIDENCE");if(string.IsNullOrEmpty(path))return;Directory.CreateDirectory(path);File.WriteAllBytes(Path.Combine(path,name+".png"),t.EncodeToPNG());}
        try
        {
            world.Fog.ShowPublic();world.SetPresentationLayer(8);camera.cullingMask=1<<8;
            foreach(var kind in new[]{PlayableBuildingKind.RepairTower,PlayableBuildingKind.SentryTurret,PlayableBuildingKind.GaussTower})
            {
                var tower=Tower(1,kind,new NavPoint(0,0));world.Clear();yield return null;world.Fog.ShowPublic();var actor=world.Building(1,kind.ToString(),true);actor.Root.position=world.Point(tower.Position);actor.Health.parent.gameObject.SetActive(false);PlayableWorld.PaintOwner(actor,Color.cyan);
                var frame=Frame(profile,tower);var selection=new HashSet<int>{1};world.RenderTowerRange(frame,selection);actor.Selection.SetActive(true);
                float range=(float)PlayableWorld.TowerActionRange(profile,kind);camera.transform.position=new Vector3(0,range*1.6f,-range*1.2f);camera.transform.LookAt(Vector3.zero);
                foreach(float zoom in new[]{1.3f,.85f})
                {
                    camera.orthographicSize=range*zoom;camera.Render();var selected=Read();Assert.Greater(Count(selected,kind==PlayableBuildingKind.RepairTower),120,"Range edge must be visible in actual render "+kind);Save(selected,kind+(zoom>1?"-normal":"-close"));UnityEngine.Object.DestroyImmediate(selected);
                }
                selection.Clear();world.RenderTowerRange(frame,selection);actor.Selection.SetActive(false);camera.Render();var clear=Read();Assert.Less(Count(clear,kind==PlayableBuildingKind.RepairTower),30,"Deselection removes coverage");Save(clear,kind+"-deselected");UnityEngine.Object.DestroyImmediate(clear);
                selection.Add(1);world.RenderTowerRange(frame,selection);world.Fog.Reset();camera.Render();var hidden=Read();Assert.Less(Count(hidden,kind==PlayableBuildingKind.RepairTower),30,"Unexplored fog hides coverage");UnityEngine.Object.DestroyImmediate(hidden);
                world.Fog.ShowPublic();camera.cullingMask=1<<9;camera.Render();var other=Read();Assert.Less(Count(other,kind==PlayableBuildingKind.RepairTower),30,"Other local camera cannot see this world's coverage");UnityEngine.Object.DestroyImmediate(other);camera.cullingMask=1<<8;
            }
        }
        finally{camera.targetTexture=null;target.Release();UnityEngine.Object.DestroyImmediate(target);world.Dispose();UnityEngine.Object.DestroyImmediate(host);UnityEngine.Object.DestroyImmediate(cameraHost);UnityEngine.Object.DestroyImmediate(lightHost);RenderSettings.ambientLight=oldAmbient;}
    }
}
