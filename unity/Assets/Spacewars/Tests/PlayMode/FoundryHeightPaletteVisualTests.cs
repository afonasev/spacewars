using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

public sealed class FoundryHeightPaletteVisualTests
{
    // Explicit static visual inspection of production meshes/shader and UI Toolkit painter.
    // No AI match, gameplay witness or Player is started by this fixture.
    [UnityTest] public IEnumerator CaptureWorldAndCompactMap()
    {
        string output=Environment.GetEnvironmentVariable("FOUNDRY_HEIGHT_EVIDENCE");
        Assert.IsNotEmpty(output,"Explicit evidence directory required");
        CollectionAssert.Contains(Environment.GetCommandLineArgs(),"-noaudio");
        CollectionAssert.Contains(Environment.GetCommandLineArgs(),"-muted");
        Directory.CreateDirectory(output);
        var profile=FoundryGreybox.LoadProfile();var map=(FoundryMap)profile.AuthoredMap;
        var host=new GameObject("Foundry height palette static inspection");
        var panel=ScriptableObject.CreateInstance<PanelSettings>();
        var minimapTarget=new RenderTexture(180,180,24);minimapTarget.Create();
        var worldTarget=new RenderTexture(1200,1000,24){antiAliasing=CameraAntialiasingProfile.Load().Samples};worldTarget.Create();
        var oldAmbient=RenderSettings.ambientLight;var oldMode=RenderSettings.ambientMode;
        var world=new PlayableWorld(host.transform,profile);
        var terrain=new PlayableMapTerrain(profile);
        try
        {
            RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.52f,.55f,.60f);
            var sun=new GameObject("Fixed Foundry inspection sun").AddComponent<Light>();sun.transform.SetParent(host.transform);sun.type=LightType.Directional;sun.intensity=1.8f;sun.transform.rotation=Quaternion.Euler(50,-35,0);
            var camera=new GameObject("Fixed Foundry inspection camera").AddComponent<Camera>();camera.transform.SetParent(host.transform);camera.orthographic=true;camera.nearClipPlane=.1f;camera.farClipPlane=900;camera.backgroundColor=new Color(.045f,.065f,.08f);camera.clearFlags=CameraClearFlags.SolidColor;camera.targetTexture=worldTarget;
            world.InspectionReveal();
            Color Paint(PlayableOwner? owner){if(!owner.HasValue)return Color.gray;ColorUtility.TryParseHtmlString(NativeLobbyConfiguration.ColorHex[(int)owner.Value],out var c);return c;}
            foreach(var site in map.Sites(profile).Where(s=>s.Kind==PlayableBuildingKind.Headquarters))
            {
                var actor=world.Building(site.Id,site.Kind.ToString(),site.Id<=3);actor.Root.position=world.Point(site.Position);actor.Root.rotation=Quaternion.Euler(0,site.Id<=3?0:180,0);PlayableWorld.PaintOwner(actor,Paint((PlayableOwner)(site.Id-1)));
            }
            terrain.ShowPublicTerrain();panel.targetTexture=minimapTarget;panel.scaleMode=PanelScaleMode.ConstantPixelSize;panel.scale=1;
            var ui=new GameObject("Actual compact map painter");ui.transform.SetParent(host.transform);var document=ui.AddComponent<UIDocument>();document.panelSettings=panel;
            var root=document.rootVisualElement;root.style.width=180;root.style.height=180;
            var surface=new PlayableMapSurface(profile,terrain.Texture){OwnerPaint=Paint};surface.style.width=180;surface.style.height=180;root.Add(surface);
            var sites=map.Sites(profile);surface.SetPublic(sites.Select(s=>new PlayableMapMark(s.Id,s.Position,s.Kind,MapMarkState.Ready,null)).ToArray(),profile.ArenaHalfExtent);
            for(int frame=0;frame<8;frame++)yield return null;
            Assert.AreEqual(180,surface.contentRect.width);Assert.AreEqual(180,surface.contentRect.height);
            Save(minimapTarget,Path.Combine(output,"minimap-neutral-180.png"));
            surface.SetPublic(sites.Select(s=>new PlayableMapMark(s.Id,s.Position,s.Kind,MapMarkState.Ready,s.Id<=6?(PlayableOwner?)(s.Id-1):null)).ToArray(),profile.ArenaHalfExtent);
            for(int frame=0;frame<4;frame++)yield return null;
            Save(minimapTarget,Path.Combine(output,"minimap-occupied-180.png"));
            void Shot(string name,Vector3 position,Quaternion rotation,float size){camera.transform.SetPositionAndRotation(position,rotation);camera.orthographicSize=size;camera.Render();Save(worldTarget,Path.Combine(output,name+".png"));}
            Shot("world-overview",new Vector3(0,340,0),Quaternion.Euler(90,0,0),150);
            Shot("world-oblique",new Vector3(0,300,-195),Quaternion.Euler(62,0,0),160);
            Shot("ramp-close",new Vector3(-52,80,-30),Quaternion.Euler(55,0,0),50);
            world.InspectionHide();Shot("world-hidden",new Vector3(0,340,0),Quaternion.Euler(90,0,0),150);
            File.WriteAllText(Path.Combine(output,"capture.txt"),"Editor PlayMode static diagnostic; production world + UI Toolkit map painter; native 180x180 public map with neutral/six owned HQ marks. Fixed lighting/cameras, authored six diagnostic HQ models; not an ordinary match/Player/device/human acceptance. No simulation/AI. map="+map.Id+"@"+map.Revision+"; source supplied by manifest; Unity="+Application.unityVersion);
        }
        finally
        {
            RenderSettings.ambientLight=oldAmbient;RenderSettings.ambientMode=oldMode;
            terrain.Dispose();world.Dispose();
            UnityEngine.Object.DestroyImmediate(host);panel.targetTexture=null;UnityEngine.Object.DestroyImmediate(panel);minimapTarget.Release();worldTarget.Release();UnityEngine.Object.DestroyImmediate(minimapTarget);UnityEngine.Object.DestroyImmediate(worldTarget);
        }
        yield return null;
    }
    private static void Save(RenderTexture target,string path)
    {
        var prior=RenderTexture.active;RenderTexture.active=target;var png=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);
        try{png.ReadPixels(new Rect(0,0,target.width,target.height),0,0);png.Apply();File.WriteAllBytes(path,png.EncodeToPNG());}
        finally{RenderTexture.active=prior;UnityEngine.Object.DestroyImmediate(png);}
    }
}
