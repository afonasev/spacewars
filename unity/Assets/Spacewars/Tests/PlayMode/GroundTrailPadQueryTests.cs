using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;
using UnityEngine;
namespace Spacewars.Tests.PlayMode
{
    public sealed class GroundTrailPadQueryTests
    {
        const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        sealed class Scene:IDisposable
        {
            internal readonly GameObject Host=new GameObject("trail pad query control");
            internal readonly PlayableProfile Profile=PlayableProfile.Create(PlayableProfile.Default.CopyData(),new AiTestMap());
            internal readonly PlayableWorld World;internal readonly PlayableSnapshot Basis;
            internal Scene(){
                var setup=new NativeLobbyConfiguration{AiTestMap=true,HumanPresent=false,AiPresent=true,HasExplicitSeed=true,ExplicitSeed=7};setup.InitializeParticipants();
                var runtime=PlayableRuntime.CreateEightPlayerPerformanceMatch(Profile,1,setup,startPaused:true);
                try{Basis=runtime.SpectatorFrame(PlayableRuntime.LocalSpectatorId).Overview;}
                finally{runtime.Dispose();Assert.True(SpinWait.SpinUntil(()=>runtime.IsStopped,5000),"Owned paused authority must stop before control");}
                World=new PlayableWorld(Host.transform,Profile);World.Fog.ShowPublic();World.RenderSites(Basis);
            }
            internal PlayableSnapshot Frame(long sequence,PlayableGroundTrail[] trails,TeamVisionSnapshot vision=null)=>new PlayableSnapshot(Profile.ProfileId,Profile.Revision,1,7,sequence,10,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,0,null,Array.Empty<PlayableEntitySnapshot>(),Basis.Buildings.ToArray(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,sites:Basis.Sites.ToArray(),vision:vision,activeProfile:Profile,groundTrails:trails);
            public void Dispose(){World?.Dispose();UnityEngine.Object.DestroyImmediate(Host);}
        }
        static IEnumerable<NavPoint> Queries(Scene scene)
        {
            var random=new System.Random(81733);
            for(int i=0;i<500;i++)yield return new NavPoint(random.NextDouble()*280-140,random.NextDouble()*280-140);
            foreach(var pad in scene.Host.GetComponentsInChildren<Transform>(true).Where(t=>t.name.StartsWith("Site pad ",StringComparison.Ordinal))){
                var half=pad.Find("Technical apron").GetComponent<MeshFilter>().sharedMesh.bounds.extents;var center=pad.position;
                foreach(int sign in new[]{-1,1})for(int ulp=-2;ulp<=2;ulp++){
                    float x=BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(center.x+sign*half.x)+ulp);
                    float z=BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(center.z+sign*half.z)+ulp);
                    yield return new NavPoint(x,center.z);yield return new NavPoint(center.x,z);yield return new NavPoint(x,z);
                }
            }
        }
        [TestCase(0)][TestCase(1)][TestCase(2)]
        public void CachedPadQueriesMatchNativeAtEdgesAndKeepGeneralTransformFallback(int transformCase)
        {
            using(var scene=new Scene()){
                if(transformCase==1)foreach(var pad in scene.Host.GetComponentsInChildren<Transform>(true).Where(t=>t.name.StartsWith("Site pad ",StringComparison.Ordinal)))pad.localRotation=Quaternion.Euler(0,37,0);
                if(transformCase==2){scene.Host.transform.position=new Vector3(3,2,-1);scene.Host.transform.rotation=Quaternion.Euler(13,17,9);scene.Host.transform.localScale=new Vector3(1.2f,.8f,.7f);}
                var points=Queries(scene).ToArray();var native=points.Select(scene.World.TrailSurface).ToArray();
                typeof(PlayableWorld).GetMethod("CaptureTrailPadQueries",Private).Invoke(scene.World,null);
                try{
                    CollectionAssert.AreEqual(native,points.Select(scene.World.TrailSurface).ToArray());
                    var entries=(System.Collections.IEnumerable)typeof(PlayableWorld).GetField("trailPadQueries",Private).GetValue(scene.World);int fast=0;
                    foreach(var entry in entries)if((bool)entry.GetType().GetField("Axis",Private|BindingFlags.Public).GetValue(entry))fast++;
                    if(transformCase==0)Assert.Greater(fast,0,"Reach the certified fast path");else Assert.Zero(fast,"General transforms must retain native clipping");
                }finally{typeof(PlayableWorld).GetField("trailPadQueriesActive",Private).SetValue(scene.World,false);}
            }
        }
        static byte[] MeshBytes(Scene scene)
        {
            var mesh=scene.Host.GetComponentsInChildren<MeshFilter>(true).Single(f=>f.name=="Ground vehicle trails").sharedMesh;
            using(var stream=new MemoryStream())using(var writer=new BinaryWriter(stream)){
                foreach(var p in mesh.vertices){writer.Write(p.x);writer.Write(p.y);writer.Write(p.z);}
                foreach(int i in mesh.triangles)writer.Write(i);
                foreach(int channel in new[]{0,1}){var uv=new List<Vector4>();mesh.GetUVs(channel,uv);foreach(var p in uv){writer.Write(p.x);writer.Write(p.y);writer.Write(p.z);writer.Write(p.w);}}
                foreach(var c in mesh.colors){writer.Write(c.r);writer.Write(c.g);writer.Write(c.b);writer.Write(c.a);}return stream.ToArray();
            }
        }
        [Test]
        public void FogClippingRemainsExactWithCachedPadQueries()
        {
            bool previous=NativeGraphicsSettings.GroundTrailsEnabled;
            try{NativeGraphicsSettings.GroundTrailsEnabled=true;using(var scene=new Scene()){
                var c=scene.Basis.Buildings[0].Position;var facts=Enumerable.Range(0,80).Select(i=>new PlayableGroundTrail(new NavPoint(c.X-20+i*.5,c.Z+18),new NavPoint(c.X-19.8+i*.5,c.Z+18),0,i*.5,3,PlayableEntityKind.Tank)).ToArray();
                var vision=new PlayableVision(1,scene.Profile.ArenaHalfExtent,scene.Profile.ArenaHalfExtent,scene.Profile.VisionCellSize,scene.Profile.FogEdgeFeather);vision.Refresh(new[]{new VisionSource(new NavPoint(c.X,c.Z+18),10)},Array.Empty<KnownBuilding>());
                scene.World.NativeTrailPadReferenceEvidence=true;scene.World.RenderGroundTrails(scene.Frame(1,facts));int full=scene.World.GroundTrailQuadCount;
                scene.World.RenderGroundTrails(scene.Frame(2,facts,vision.Snapshot()));var expected=MeshBytes(scene);int visible=scene.World.GroundTrailQuadCount;
                Assert.Greater(visible,0);Assert.Less(visible,full,"Reach the hidden-contact clipping branch");
                var last=facts[facts.Length-1];facts[facts.Length-1]=new PlayableGroundTrail(last.From,last.To,last.Heading,last.Distance,last.Tick,last.Kind);
                scene.World.NativeTrailPadReferenceEvidence=false;scene.World.RenderGroundTrails(scene.Frame(3,facts,vision.Snapshot()));CollectionAssert.AreEqual(expected,MeshBytes(scene));
            }}finally{NativeGraphicsSettings.GroundTrailsEnabled=previous;}
        }
        static void RenderPair(Scene scene,PlayableGroundTrail[] facts,string output,long sequence)
        {
            var center=scene.Basis.Buildings[0].Position;
            var sun=new GameObject("Trail query control sun").AddComponent<Light>();sun.transform.SetParent(scene.Host.transform);var lighting=IndustrialLightingProfile.Load();lighting.ApplyScene(sun);
            scene.World.ApplyGraphics(ActorMaterialProfile.Load(),lighting);scene.World.TrailArt.opacity=1;
            var camera=new GameObject("Trail query control camera").AddComponent<Camera>();camera.transform.SetParent(scene.Host.transform);camera.orthographic=true;camera.orthographicSize=14;camera.nearClipPlane=.1f;camera.farClipPlane=300;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.gray;
            camera.transform.position=new Vector3((float)center.X,24,(float)center.Z-24);camera.transform.LookAt(new Vector3((float)center.X,0,(float)center.Z));
            var texture=new RenderTexture(512,512,24);texture.Create();camera.targetTexture=texture;var image=new Texture2D(512,512,TextureFormat.RGB24,false);
            Color32[] reference=null;var previous=RenderTexture.active;
            try{foreach(int mode in new[]{0,1,2}){
                scene.World.NativeTrailPadReferenceEvidence=mode!=1;NativeGraphicsSettings.GroundTrailsEnabled=mode!=2;
                var last=facts[facts.Length-1];facts[facts.Length-1]=new PlayableGroundTrail(last.From,last.To,last.Heading,last.Distance,last.Tick,last.Kind);
                scene.World.RenderGroundTrails(scene.Frame(sequence++,facts));camera.Render();RenderTexture.active=texture;image.ReadPixels(new Rect(0,0,512,512),0,0);image.Apply();
                var pixels=image.GetPixels32();File.WriteAllBytes(Path.Combine(output,mode==0?"native.png":mode==1?"candidate.png":"disabled.png"),image.EncodeToPNG());
                if(mode==0)reference=pixels;else if(mode==1)CollectionAssert.AreEqual(reference,pixels,"Actual rendered pixels must match the native apron control");
                else Assert.Greater(reference.Where((p,i)=>p.r!=pixels[i].r||p.g!=pixels[i].g||p.b!=pixels[i].b).Count(),100,"The compared frame must contain visible ground impressions");
            }}finally{RenderTexture.active=previous;camera.targetTexture=null;texture.Release();UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(texture);NativeGraphicsSettings.GroundTrailsEnabled=true;}
        }
        [Test,Explicit("Exclusive fixed-input native/candidate/native trail mesh CPU control")]
        public void FixedTrailMeshKeepsExactBuffersAndRecordsCost()
        {
            string output=Environment.GetEnvironmentVariable("SPACEWARS_TRAIL_PAD_OUTPUT");Assert.IsNotNull(output);Directory.CreateDirectory(output);
            bool previous=NativeGraphicsSettings.GroundTrailsEnabled;
            try{NativeGraphicsSettings.GroundTrailsEnabled=true;using(var scene=new Scene()){
                var random=new System.Random(51017);var center=scene.Basis.Buildings[0].Position;var facts=new PlayableGroundTrail[2000];
                for(int i=0;i<facts.Length;i++){
                    double x=i<1000?center.X+random.NextDouble()*24-12:random.NextDouble()*250-125,z=i<1000?center.Z+random.NextDouble()*24-12:random.NextDouble()*250-125,heading=random.NextDouble()*Math.PI*2;
                    facts[i]=new PlayableGroundTrail(new NavPoint(x,z),new NavPoint(x+Math.Cos(heading)*.14,z+Math.Sin(heading)*.14),heading,i*.14,3,(PlayableEntityKind)(i%3));
                }
                var report=new System.Text.StringBuilder();byte[] expected=null;long sequence=1;
                for(int stage=0;stage<3;stage++){
                    scene.World.NativeTrailPadReferenceEvidence=stage!=1;long ticks=0;
                    for(int repeat=0;repeat<4;repeat++){
                        var last=facts[facts.Length-1];facts[facts.Length-1]=new PlayableGroundTrail(last.From,last.To,last.Heading,last.Distance,last.Tick,last.Kind);
                        var view=scene.Frame(sequence++,facts);long start=Stopwatch.GetTimestamp();scene.World.RenderGroundTrails(view);long elapsed=Stopwatch.GetTimestamp()-start;if(repeat>0)ticks+=elapsed;
                        var bytes=MeshBytes(scene);if(expected==null)expected=bytes;else CollectionAssert.AreEqual(expected,bytes,"All vertices, indices, UVs and colors must match the exact native control");
                    }
                    report.AppendLine("TRAIL_PAD_CONTROL stage="+stage+" samples=2000 repeats=3 total_ms="+(ticks*1000d/Stopwatch.Frequency).ToString("R",System.Globalization.CultureInfo.InvariantCulture));
                }
                using(var hash=SHA256.Create())report.AppendLine("mesh_sha256="+BitConverter.ToString(hash.ComputeHash(expected)).Replace("-","").ToLowerInvariant());
                File.WriteAllText(Path.Combine(output,"measurement.txt"),report.ToString());TestContext.Out.Write(report.ToString());
                RenderPair(scene,facts,output,sequence);
            }}finally{NativeGraphicsSettings.GroundTrailsEnabled=previous;}
        }
    }
}
