using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using System.Reflection;
namespace Spacewars.Tests.PlayMode
{
    public sealed class GroundTrailDensityRenderingTests
    {
        [Test] public void VehicleTrailControlsAreAvailableInGraphicsLabWithCountUnits()
        {
            string output=Environment.GetEnvironmentVariable("SPACEWARS_TRAIL_DENSITY_OUTPUT");Assert.IsNotEmpty(output);Directory.CreateDirectory(output);
            var root=new VisualElement();var navigation=new NativeMenuNavigation(root);
            try{
                var store=new NativeBalanceStore(PlayableProfile.Default,Path.Combine(output,"lab-controls.json"));store.Copy("Whole vehicle tracks");var view=new NativeBalanceView(root,navigation,store,()=>{});
                var flags=BindingFlags.Instance|BindingFlags.NonPublic;typeof(NativeBalanceView).GetField("section",flags).SetValue(view,"Графика");typeof(NativeBalanceView).GetField("entity",flags).SetValue(view,"Следы техники");typeof(NativeBalanceView).GetMethod("RenderFields",flags).Invoke(view,null);
                var threshold=root.Q<TextField>("visual.modelDetail.trailCrowdThreshold");Assert.NotNull(threshold);Assert.AreEqual("20",threshold.value);Assert.False(threshold.isReadOnly);Assert.True(threshold.tooltip.Contains("юн."));Assert.False(threshold.tooltip.Contains("%"));
                Assert.AreEqual("4",root.Q<TextField>("visual.modelDetail.trailLargeCrowdDivisor").value);
                Assert.AreEqual("18",root.Q<TextField>("visual.modelDetail.trailHideZoom").value);
            }finally{navigation.Dispose();root.Clear();}
        }
        // Controlled visual fixture only: no game camera or authoritative journal changes.
        public static PlayableGroundTrail[] Facts(){var journal=new GroundTrailJournal();for(int step=0;step<60;step++)for(int actor=0;actor<50;actor++)journal.Observe(actor+1,(PlayableEntityKind)(actor%3),new NavPoint(-49+step*.2,-23+actor*.9),new NavPoint(-49+(step+1)*.2,-23+actor*.9),0,90+step*3);return journal.Visible(270);}

        static PlayableSnapshot Frame(PlayableProfile p,long sequence,PlayableGroundTrail[] facts)=>new PlayableSnapshot(p.ProfileId,p.Revision,1,7319,sequence,270,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,0,null,Array.Empty<PlayableEntitySnapshot>(),Array.Empty<PlayableBuildingSnapshot>(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,groundTrails:facts);
        [UnityTest] public IEnumerator CompareFullHalfAndQuarterOnThreeCrossings()
        {
            string output=Environment.GetEnvironmentVariable("SPACEWARS_TRAIL_DENSITY_OUTPUT");Assert.IsNotEmpty(output,"owned output required");Directory.CreateDirectory(output);
            bool enabled=NativeGraphicsSettings.GroundTrailsEnabled;int quality=QualitySettings.GetQualityLevel();var ambient=RenderSettings.ambientLight;var ambientMode=RenderSettings.ambientMode;
            float shadow=GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset initial?initial.shadowDistance:0;
            var cache=PreparedMapCache.DirectoryOverride;PreparedMapCache.DirectoryOverride=Path.Combine(output,"prepared-cache");
            var host=new GameObject("trail density visual comparison");PlayableWorld world=null;Camera camera=null;RenderTexture target=null;Texture2D pixels=null;
            try{
                QualitySettings.SetQualityLevel(5,true);NativeGraphicsSettings.GroundTrailsEnabled=true;
                var p=PlayableProfile.ThreeCrossingsDefault;world=new PlayableWorld(host.transform,p,staged:true);yield return world.Preparation;world.Fog.ShowPublic();
                var lighting=IndustrialLightingProfile.Load(p.AuthoredMap.Id);var sun=new GameObject("comparison sun").AddComponent<Light>();sun.transform.SetParent(host.transform);lighting.ApplyScene(sun);
                camera=new GameObject("comparison camera").AddComponent<Camera>();camera.transform.SetParent(host.transform);camera.orthographic=true;camera.orthographicSize=20;camera.nearClipPlane=.1f;camera.farClipPlane=700;
                var focus=world.Point(new NavPoint(-43,-1));camera.transform.position=focus+new Vector3(9,25,-21);camera.transform.LookAt(focus);camera.backgroundColor=new Color(.10f,.12f,.14f);camera.clearFlags=CameraClearFlags.SolidColor;
                camera.gameObject.AddComponent<VisibleShadowCoverage>().Configure(host.transform,lighting);camera.gameObject.AddComponent<ContactOcclusionCamera>().Configure(ContactOcclusionProfile.Load());camera.gameObject.AddComponent<ActorGlowCamera>().Configure(ActorMaterialProfile.Load());
                camera.gameObject.AddComponent<CameraAntialiasing>().Configure(new CameraAntialiasingProfile{mode=1});
                target=new RenderTexture(3456,2160,24,RenderTextureFormat.DefaultHDR){antiAliasing=4};target.Create();camera.targetTexture=target;pixels=new Texture2D(3456,2160,TextureFormat.RGB24,false);
                Assert.AreEqual(5,QualitySettings.GetQualityLevel());
                var facts=Facts();var counts=new int[3];Color32[] full=null,quarter=null;
                for(int i=0;i<3;i++){
                    int divisor=1<<i;var selection=new GroundTrailVehicleSelection();selection.Begin(1,270,300);var selected=facts.Where(f=>selection.Keep(f,divisor)).ToArray();world.RenderGroundTrails(Frame(p,i+1,selected));counts[i]=world.GroundTrailQuadCount;
                    for(int frame=0;frame<8;frame++)yield return null;Assert.AreEqual(4,CameraAntialiasingDiagnostics.Samples(camera),"Observed URP descriptor after rendering");var prior=RenderTexture.active;RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,3456,2160),0,0);pixels.Apply();RenderTexture.active=prior;
                    File.WriteAllBytes(Path.Combine(output,divisor==1?"full.png":divisor==2?"half.png":"quarter.png"),pixels.EncodeToPNG());if(i==0)full=pixels.GetPixels32();if(i==2)quarter=pixels.GetPixels32();
                }
                int changed=0;for(int i=0;i<full.Length;i++)if(Math.Abs(full[i].r-quarter[i].r)+Math.Abs(full[i].g-quarter[i].g)+Math.Abs(full[i].b-quarter[i].b)>15)changed++;
                File.WriteAllText(Path.Combine(output,"visual.txt"),"ThreeCrossings; seed7319; controlled50-lane native-renderer fixture, not match/FPS acceptance;3456x2160/MSAA4/quality5; whole-vehicle full/half/quarter quads="+string.Join(",",counts)+"; changedPixels="+changed+"\n");
                // Same distant viewpoint before/after; the production camera gate is the only difference.
                camera.orthographicSize=25.6f;world.RenderGroundTrails(Frame(p,5,facts));
                Color32[] distantFull=null;
                for(int shot=0;shot<2;shot++){
                    if(shot==1)world.RenderGroundTrails(Frame(p,5,facts),camera);
                    for(int frame=0;frame<8;frame++)yield return null;
                    var prior=RenderTexture.active;RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,3456,2160),0,0);pixels.Apply();RenderTexture.active=prior;
                    File.WriteAllBytes(Path.Combine(output,shot==0?"far-before.png":"far-hidden.png"),pixels.EncodeToPNG());
                    if(shot==0)distantFull=pixels.GetPixels32();
                }
                var distantHidden=pixels.GetPixels32();int farChanged=0;for(int i=0;i<distantFull.Length;i++)if(Math.Abs(distantFull[i].r-distantHidden[i].r)+Math.Abs(distantFull[i].g-distantHidden[i].g)+Math.Abs(distantFull[i].b-distantHidden[i].b)>15)farChanged++;
                Assert.Greater(farChanged,1000,"actual distant image must show trails removed");
                Assert.Greater(counts[0],1000);Assert.Less(counts[1],counts[0]*.65);Assert.Less(counts[2],counts[0]*.4);Assert.Greater(changed,1000,"density must visibly change the actual ground image");
            }finally{
                PreparedMapCache.DirectoryOverride=cache;NativeGraphicsSettings.GroundTrailsEnabled=enabled;QualitySettings.SetQualityLevel(quality,true);RenderSettings.ambientLight=ambient;RenderSettings.ambientMode=ambientMode;
                if(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset pipeline)pipeline.shadowDistance=shadow;
                if(camera)camera.targetTexture=null;world?.Dispose();if(target){target.Release();UnityEngine.Object.DestroyImmediate(target);}if(pixels)UnityEngine.Object.DestroyImmediate(pixels);if(host)UnityEngine.Object.DestroyImmediate(host);
            }
        }
    }
}
