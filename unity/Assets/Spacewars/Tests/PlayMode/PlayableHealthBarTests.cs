using System.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;

public sealed class PlayableHealthBarTests
{
    private System.Action restorePreferences;
    [SetUp] public void PreserveOriginalDisplayMode(){restorePreferences=NativePreferenceWriter.Capture();NativeUserSettings.HideDistantHealth=false;}
    [TearDown] public void RestoreDisplayMode(){restorePreferences();PlayerPrefs.Save();}

    [System.Runtime.InteropServices.DllImport("/usr/lib/libSystem.B.dylib")]
    private static extern ulong clock_gettime_nsec_np(int clock);
    private static long HealthCpuNow()=>System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX)?(long)clock_gettime_nsec_np(16):-1;
    [Test,Explicit("Fixed-input authored-model CPU cost control; run alone through exclusive Unity runner")]
    public void FourHundredAuthoredModelsExposeOriginalHealthUpdateCost()
    {
        const int count=400,frames=24;var host=new GameObject("400 health cost control");var cameraObject=new GameObject("Health cost camera");
        var camera=cameraObject.AddComponent<Camera>();var target=new RenderTexture(3456,2160,0);
        camera.targetTexture=target;camera.orthographic=true;camera.orthographicSize=144;camera.farClipPlane=1728;
        camera.transform.position=new Vector3(0,104,-148);camera.transform.LookAt(Vector3.zero);
        var world=new PlayableWorld(host.transform,PlayableProfile.Default);var actors=new PlayableWorld.Actor[count];var positions=new Vector3[count];
        try{
            for(int i=0;i<count;i++){
                int cohort=i/50,local=i%50;float angle=cohort*Mathf.PI/4;
                actors[i]=world.Tank(10000+i,true,local==0?PlayableEntityKind.Explorer:PlayableEntityKind.Tank);
                positions[i]=new Vector3(Mathf.Cos(angle)*88+local%7*2.2f,0,Mathf.Sin(angle)*88+local/7*2.2f);
                actors[i].Root.position=positions[i];
            }
            // Stabilize model membership and the existing LOD hysteresis first.
            for(int warm=0;warm<3;warm++)foreach(var actor in actors)PlayableWorld.UpdateHealth(actor,camera,1,true);
            int meshes=actors.Sum(a=>a.Hull.GetComponentsInChildren<MeshRenderer>(true).Length);
            for(int moving=0;moving<2;moving++){
                long ticks=0,cpu=0,allocated=0;
                for(int frame=0;frame<frames;frame++){
                    float offset=moving==0?0:frame*.02f;
                    for(int i=0;i<count;i++){var actor=actors[i];actor.Root.position=positions[i]+new Vector3(offset,0,0);actor.Hull.localRotation=Quaternion.Euler(0,moving==0?0:frame*.1f,0);}
                    long bytes=System.GC.GetAllocatedBytesForCurrentThread(),start=System.Diagnostics.Stopwatch.GetTimestamp(),cpuStart=HealthCpuNow();
                    foreach(var actor in actors)PlayableWorld.UpdateHealth(actor,camera,1,true);
                    long cpuEnd=HealthCpuNow();ticks+=System.Diagnostics.Stopwatch.GetTimestamp()-start;
                    if(cpuStart>=0)cpu+=cpuEnd-cpuStart;allocated+=System.GC.GetAllocatedBytesForCurrentThread()-bytes;
                }
                double wallMs=ticks*1000d/System.Diagnostics.Stopwatch.Frequency;
                TestContext.Out.WriteLine("HEALTH_COST_CONTROL mode="+(moving==0?"unchanged":"changed-pose")+" actors="+count+" modelMeshRenderers="+meshes+" frames="+frames+" wall_ms="+wallMs.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+" thread_cpu_ms="+(cpu/1000000d).ToString("R",System.Globalization.CultureInfo.InvariantCulture)+" allocated="+allocated+" camera=3456x2160/ortho144/0,104,-148");
                foreach(var actor in actors){Assert.True(actor.Health.parent.gameObject.activeSelf);Assert.That(actor.Health.localScale.x,Is.GreaterThan(0));}
            }
            var expected=HealthPublicState(actors);
            foreach(var actor in actors)PlayableWorld.UpdateHealth(actor,camera,1,true);
            CollectionAssert.AreEqual(expected,HealthPublicState(actors),"Unchanged original authored model health state is exactly repeatable after pose update");
        }finally{camera.targetTexture=null;world.Dispose();Object.DestroyImmediate(host);Object.DestroyImmediate(cameraObject);Object.DestroyImmediate(target);}
    }
    private static byte[] HealthPublicState(PlayableWorld.Actor[] actors)
    {
        using(var stream=new System.IO.MemoryStream())using(var writer=new System.IO.BinaryWriter(stream)){
            foreach(var actor in actors){
                var root=actor.Health.parent;writer.Write(root.gameObject.activeSelf);
                foreach(var transform in new[]{root}.Concat(root.Cast<Transform>())){
                    var p=transform.localPosition;var q=transform.localRotation;var s=transform.localScale;
                    writer.Write(p.x);writer.Write(p.y);writer.Write(p.z);writer.Write(q.x);writer.Write(q.y);writer.Write(q.z);writer.Write(q.w);writer.Write(s.x);writer.Write(s.y);writer.Write(s.z);writer.Write(transform.gameObject.activeSelf);
                }
            }return stream.ToArray();
        }
    }
    [TestCase(0f,0.2f,45f,0f)]
    [TestCase(73f,0.6f,65f,120f)]
    [TestCase(180f,1f,30f,240f)]
    public void BarsStayScreenHorizontalAndOnlyRightEdgeDepletes(float heading,float construction,float pitch,float yaw)
    {
        var host=new GameObject("Health test");
        var cameraObject=new GameObject("Camera");
        var camera=cameraObject.AddComponent<Camera>();
        var world=new PlayableWorld(host.transform,PlayableProfile.Default);
        try
        {
            camera.orthographic=true;camera.orthographicSize=25;
            camera.transform.rotation=Quaternion.Euler(pitch,yaw,13);
            camera.transform.position=new Vector3(0,3.8f,0)-camera.transform.forward*40;
            foreach(var actor in new[]{world.Tank(1,true),world.Building(2,"Factory",false)})
            {
                foreach(var renderer in actor.Health.parent.GetComponentsInChildren<Renderer>()){Assert.AreEqual(UnityEngine.Rendering.ShadowCastingMode.Off,renderer.shadowCastingMode);Assert.False(renderer.receiveShadows);}
                var outline=actor.Health.parent.Find("Health outline");Assert.NotNull(outline);
                Assert.That(outline.localScale.x,Is.EqualTo(PlayableProfile.Default.HealthBarWidth+2*PlayableProfile.Default.HealthBarOutlineWidth).Within(.0001));
                Assert.That(outline.localScale.y,Is.EqualTo(PlayableProfile.Default.HealthBarHeight+2*PlayableProfile.Default.HealthBarOutlineWidth).Within(.0001));
                var color=new MaterialPropertyBlock();outline.GetComponent<Renderer>().GetPropertyBlock(color);Assert.AreEqual(Color.black,color.GetColor("_BaseColor"));
                actor.Root.rotation=Quaternion.Euler(0,heading,0);
                actor.Root.localScale=new Vector3(1,construction,1);
                PlayableWorld.UpdateHealth(actor,camera,1);
                Assert.AreEqual(PlayableProfile.Default.HealthBarCells-1,actor.Health.parent.Cast<Transform>().Count(t=>t.name.StartsWith("Health divider ")));
                var up=camera.transform.up;var absoluteUp=new Vector3(Mathf.Abs(up.x),Mathf.Abs(up.y),Mathf.Abs(up.z));
                float modelTop=actor.Hull.GetComponentsInChildren<MeshRenderer>(true).Where(r=>r.enabled&&r.gameObject.activeInHierarchy).Max(r=>Vector3.Dot(up,r.bounds.center-actor.Root.position)+Vector3.Dot(absoluteUp,r.bounds.extents));
                var intended=actor.Root.position+up*(modelTop+(float)(PlayableProfile.Default.HealthBarModelGap+PlayableProfile.Default.HealthBarHeight/2+PlayableProfile.Default.HealthBarOutlineWidth));
                float pixelHeight=actor.Health.localScale.y*camera.pixelHeight/(2*camera.orthographicSize);
                float intendedY=camera.WorldToScreenPoint(intended).y;
                Assert.That(camera.WorldToScreenPoint(actor.Health.parent.position).y,Is.EqualTo(Mathf.Round(intendedY-pixelHeight/2)+pixelHeight/2).Within(.001f));
                Vector3 fullLeft=ScreenEdge(actor.Health,camera,-.5f),fullRight=ScreenEdge(actor.Health,camera,.5f);
                Assert.That(fullLeft.y,Is.EqualTo(fullRight.y).Within(.001f));
                PlayableWorld.UpdateHealth(actor,camera,.25f);
                Vector3 left=ScreenEdge(actor.Health,camera,-.5f),right=ScreenEdge(actor.Health,camera,.5f);
                Assert.That(left.x,Is.EqualTo(fullLeft.x).Within(.001f));
                Assert.That(left.y,Is.EqualTo(fullLeft.y).Within(.001f));
                Assert.That(right.y,Is.EqualTo(left.y).Within(.001f));
                Assert.That(right.x-left.x,Is.EqualTo((fullRight.x-fullLeft.x)*.25f).Within(.001f));
                PlayableWorld.UpdateHealth(actor,camera,0);Assert.False(actor.Health.gameObject.activeSelf);
                PlayableWorld.UpdateHealth(actor,camera,2);Assert.True(actor.Health.gameObject.activeSelf);
                Assert.That(actor.Health.localScale.x,Is.EqualTo(actor.Health.parent.Find("Health background").localScale.x).Within(.0001));
            }
        }
        finally{world.Dispose();Object.DestroyImmediate(host);Object.DestroyImmediate(cameraObject);}
    }
    [TestCase(2.135f)] [TestCase(9.6f)] [TestCase(25.6f)]
    public void EveryVisibleCellAndGapHasEqualIntegerPixelSize(float zoom)
    {
        var host=new GameObject("Equal health pixels");var cameraObject=new GameObject("Pixel camera");
        var camera=cameraObject.AddComponent<Camera>();var target=new RenderTexture(1280,720,24);
        camera.targetTexture=target;camera.orthographic=true;camera.orthographicSize=zoom;camera.transform.position=new Vector3(0,26,-37);camera.transform.LookAt(Vector3.zero);
        var world=new PlayableWorld(host.transform,PlayableProfile.Default);
        try
        {
            foreach(var actor in new[]{world.Tank(1,true),world.Building(2,"Factory",true)})
            foreach(float offset in new[]{.137f,.291f,.83f})
            {
                actor.Root.position=new Vector3(offset,offset/2,0);PlayableWorld.UpdateHealth(actor,camera,1,true);
                var background=actor.Health.parent.Find("Health background");
                var dividers=actor.Health.parent.Cast<Transform>().Where(t=>t.name.StartsWith("Health divider ")).OrderBy(t=>t.localPosition.x).ToArray();
                float left=ScreenEdge(background,camera,-.5f).x,previous=left,cellPixels=-1,gapPixels=-1;
                Assert.That(left,Is.EqualTo(Mathf.Round(left)).Within(.001f));
                foreach(var divider in dividers)
                {
                    float start=ScreenEdge(divider,camera,-.5f).x,end=ScreenEdge(divider,camera,.5f).x;
                    if(cellPixels<0){cellPixels=start-previous;gapPixels=end-start;}
                    Assert.That(start-previous,Is.EqualTo(cellPixels).Within(.001f),"All interiors, including first and last, must be equal");
                    Assert.That(end-start,Is.EqualTo(gapPixels).Within(.001f));
                    Assert.That(start,Is.EqualTo(Mathf.Round(start)).Within(.001f));Assert.That(end,Is.EqualTo(Mathf.Round(end)).Within(.001f));previous=end;
                }
                Assert.That(ScreenEdge(background,camera,.5f).x-previous,Is.EqualTo(cellPixels).Within(.001f),"Last cell cannot gain half a gap");
                Assert.That(cellPixels,Is.EqualTo(Mathf.Round(cellPixels)).Within(.001f));Assert.That(cellPixels,Is.GreaterThanOrEqualTo(.999f));
                Assert.That(gapPixels,Is.EqualTo(Mathf.Round(gapPixels)).Within(.001f));Assert.That(gapPixels,Is.GreaterThanOrEqualTo(.999f));
                PlayableWorld.UpdateHealth(actor,camera,.67f,true);
                Assert.That(ScreenEdge(actor.Health,camera,-.5f).x,Is.EqualTo(left).Within(.001f));
                Assert.That(actor.Health.localScale.x/background.localScale.x,Is.EqualTo(.67f).Within(.00001f));
            }
        }
        finally{camera.targetTexture=null;world.Dispose();Object.DestroyImmediate(host);Object.DestroyImmediate(cameraObject);Object.DestroyImmediate(target);}
    }
    [Test] public void HealthAndBuildingOutlineControlsValidateWithLabMetadata()
    {
        foreach(var field in PlayableProfileMetadata.Fields.Where(f=>f.Path.StartsWith("presentation.healthBar.")||f.Path.StartsWith("presentation.buildingSelection.")))
        {
            var data=PlayableProfile.Default.CopyData();Assert.IsNotEmpty(field.Description);Assert.IsNotEmpty(field.Unit);Assert.Greater(field.Step,0);
            foreach(double bad in new[]{double.NaN,double.PositiveInfinity,field.Minimum-field.Step,field.Maximum+field.Step})
            {Assert.Throws<System.ArgumentOutOfRangeException>(()=>field.Write(data,bad));
                var member=typeof(PlayableProfileData).GetField(field.FieldName);
                if(member.FieldType==typeof(int)){if(double.IsNaN(bad)||double.IsInfinity(bad))continue;member.SetValue(data,(int)bad);}else member.SetValue(data,bad);
                Assert.Throws<System.ArgumentException>(()=>PlayableProfile.Validate(data));}
        }
    }
    [Test] public void PackagedMatchCameraKeepsStartAndExpandsBothLimits()
    {
        var profile=PlayableProfile.Create(JsonUtility.FromJson<PlayableProfileData>(Resources.Load<TextAsset>("PlayableProfile").text));
        Assert.AreEqual(9.6,profile.CameraOrthoSize);Assert.AreEqual(2.135,profile.CameraMinZoom);Assert.AreEqual(25.6,profile.CameraMaxZoom);
        var package=Newtonsoft.Json.Linq.JObject.Parse(Resources.Load<TextAsset>("NativeLabCatalog").text);
        var directory=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"health-camera-"+System.Guid.NewGuid().ToString("N"));
        try
        {
            var store=new NativeBalanceStore(profile,System.IO.Path.Combine(directory,"profiles.json"),false,package);
            store.Select(NativeBalanceStore.NewMatchProfileId,NativeBalanceStore.NewMatchProfileVersion);
            Assert.AreEqual(profile.CameraOrthoSize,store.Active.CameraOrthoSize);
            Assert.AreEqual(profile.CameraMinZoom,store.Active.CameraMinZoom);Assert.AreEqual(profile.CameraMaxZoom,store.Active.CameraMaxZoom);
            Assert.AreEqual(1.4,store.Active.HealthBarWidth);Assert.AreEqual(.075,store.Active.HealthBarHeight);Assert.AreEqual(10,store.Active.HealthBarCells);
        }
        finally{if(System.IO.Directory.Exists(directory))System.IO.Directory.Delete(directory,true);}
    }
    [UnityEngine.TestTools.UnityTest] public System.Collections.IEnumerator CompactBarsRenderAboveUnitsAndBuildings()
    {
        var profile=PlayableProfile.Default;var host=new GameObject("Compact bar inspection");
        var world=new PlayableWorld(host.transform,profile);var cameraObject=new GameObject("Inspection camera");
        var camera=cameraObject.AddComponent<Camera>();var texture=new RenderTexture(1600,1000,24);var pixels=new Texture2D(1600,1000,TextureFormat.RGB24,false);
        var ambient=RenderSettings.ambientLight;
        try
        {
            world.InspectionReveal();RenderSettings.ambientLight=new Color(.65f,.65f,.66f);
            camera.orthographic=true;camera.nearClipPlane=.1f;camera.farClipPlane=700;camera.targetTexture=texture;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.075f,.1f,.13f);
            var light=new GameObject("Inspection light").AddComponent<Light>();light.transform.SetParent(host.transform);light.type=LightType.Directional;light.intensity=1.5f;light.transform.rotation=Quaternion.Euler(50,-30,0);
            var actors=new[]{world.Tank(1,true),world.Tank(2,true,PlayableEntityKind.Explorer),world.Tank(3,false,PlayableEntityKind.Shkval),world.Tank(4,true,PlayableEntityKind.Hawk),world.Building(5,"Factory",true),world.Building(6,"Headquarters",false),world.Building(7,"SentryTurret",true),world.Building(8,"RepairTower",true)};
            for(int i=0;i<actors.Length;i++)actors[i].Root.position=new Vector3((i%4-1.5f)*5,0,i<4?-4:4);
            var focus=new Vector3(0,1,0);camera.transform.position=focus+new Vector3((float)profile.CameraOffsetX,(float)profile.CameraHeight,(float)profile.CameraOffsetZ);camera.transform.LookAt(focus);
            var output=System.Environment.GetEnvironmentVariable("SPACEWARS_HEALTH_OUTPUT");
            foreach(var zoom in new[]{(name:"start",size:profile.CameraOrthoSize),(name:"far",size:profile.CameraMaxZoom),(name:"close",size:profile.CameraMinZoom),(name:"unit-full",size:profile.CameraMinZoom),(name:"building-full",size:profile.CameraMinZoom)})
            {
                camera.orthographicSize=(float)zoom.size;
                if(zoom.name=="close"||zoom.name.EndsWith("-full")){focus=actors[zoom.name=="building-full"?4:0].Root.position+Vector3.up;camera.transform.position=focus+new Vector3((float)profile.CameraOffsetX,(float)profile.CameraHeight,(float)profile.CameraOffsetZ);camera.transform.LookAt(focus);}
                for(int frame=0;frame<3;frame++){for(int i=0;i<actors.Length;i++)PlayableWorld.UpdateHealth(actors[i],camera,zoom.name.EndsWith("-full")?1f:i%2==0?.67f:1f,true);yield return null;}
                camera.Render();var previous=RenderTexture.active;
                try{RenderTexture.active=texture;pixels.ReadPixels(new Rect(0,0,1600,1000),0,0);pixels.Apply();}
                finally{RenderTexture.active=previous;}
                if(!string.IsNullOrEmpty(output)){System.IO.Directory.CreateDirectory(output);System.IO.File.WriteAllBytes(System.IO.Path.Combine(output,"health-bars-"+zoom.name+".png"),pixels.EncodeToPNG());}
                Assert.That(pixels.GetPixels32().Count(p=>p.r>200&&p.b>200&&p.g<40),Is.LessThan(10),"Shader error material");
            }
        }
        finally{RenderSettings.ambientLight=ambient;camera.targetTexture=null;world.Dispose();Object.DestroyImmediate(host);Object.DestroyImmediate(cameraObject);Object.DestroyImmediate(texture);Object.DestroyImmediate(pixels);}
    }
    private static Vector3 ScreenEdge(Transform fill,Camera camera,float x)=>camera.WorldToScreenPoint(fill.TransformPoint(new Vector3(x,0,0)));
}
