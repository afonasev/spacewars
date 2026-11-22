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
            internal readonly PlayableProfile Profile;
            internal readonly PlayableWorld World;internal readonly PlayableSnapshot Basis;
            internal readonly IEqualityComparer<NavPoint> TrailComparer;
            internal Scene(bool crossings=false){
                Profile=PlayableProfile.Create(PlayableProfile.Default.CopyData(),crossings?(IPlayableTerrain)ThreeCrossingsMap.Default:new AiTestMap());
                var setup=new NativeLobbyConfiguration{AiTestMap=!crossings,HumanPresent=false,AiPresent=true,HasExplicitSeed=true,ExplicitSeed=7};
                if(crossings)setup.Participants=Enumerable.Range(0,4).Select(i=>new NativeLobbyParticipant{Name="Trail fixture "+i,Team=i/2+1,Color=i}).ToList();else setup.InitializeParticipants();
                // Trail clipping needs only normal genesis buildings/sites, not
                // the unrelated 400-unit performance scenario.
                var runtime=PlayableRuntime.CreateLobbyMatch(Profile,1,setup,startPaused:true);
                try{Basis=runtime.SpectatorFrame(PlayableRuntime.LocalSpectatorId).Overview;}
                finally{runtime.Dispose();Assert.True(SpinWait.SpinUntil(()=>runtime.IsStopped,5000),"Owned paused authority must stop before control");}
                Assert.LessOrEqual(Basis.Entities.Count,crossings?4:8,"bounded trail-only genesis");
                World=new PlayableWorld(Host.transform,Profile);World.Fog.ShowPublic();World.RenderSites(Basis);
                TrailComparer=((Dictionary<NavPoint,GroundTrailSurfaceKind>)typeof(PlayableWorld).GetField("trailTerrainKinds",Private).GetValue(World)).Comparer;
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
                var query=typeof(PlayableWorld).GetMethod("TrailPadAt",Private);
                var nativeQuad=points.Select(p=>(bool)query.Invoke(scene.World,new object[]{new Vector3((float)p.X,(float)(scene.Profile.AuthoredMap.SurfaceHeight(p)),(float)p.Z),null})).ToArray();
                typeof(PlayableWorld).GetMethod("CaptureTrailPadQueries",Private).Invoke(scene.World,null);
                try{
                    CollectionAssert.AreEqual(native,points.Select(scene.World.TrailSurface).ToArray());
                    CollectionAssert.AreEqual(nativeQuad,points.Select(p=>(bool)query.Invoke(scene.World,new object[]{new Vector3((float)p.X,(float)(scene.Profile.AuthoredMap.SurfaceHeight(p)),(float)p.Z),null})).ToArray(),"cached quads preserve the native pad predicate at all tested edges/transforms");
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
        static void SetPointComparison(Scene scene,bool native)
        {
            var field=typeof(PlayableWorld).GetField("trailTerrainKinds",Private);
            var current=(Dictionary<NavPoint,GroundTrailSurfaceKind>)field.GetValue(scene.World);
            field.SetValue(scene.World,new Dictionary<NavPoint,GroundTrailSurfaceKind>(current,native?EqualityComparer<NavPoint>.Default:scene.TrailComparer));
        }
        static void RenderPair(Scene scene,PlayableGroundTrail[] facts,string output,long sequence,bool pointComparison=false,bool padCandidateComparison=false,bool padMemoComparison=false)
        {
            var center=scene.Basis.Buildings[0].Position;
            var sun=new GameObject("Trail query control sun").AddComponent<Light>();sun.transform.SetParent(scene.Host.transform);var lighting=IndustrialLightingProfile.Load();lighting.ApplyScene(sun);
            scene.World.ApplyGraphics(ActorMaterialProfile.Load(),lighting);scene.World.TrailArt.opacity=1;
            var camera=new GameObject("Trail query control camera").AddComponent<Camera>();camera.transform.SetParent(scene.Host.transform);camera.orthographic=true;camera.orthographicSize=14;camera.nearClipPlane=.1f;camera.farClipPlane=300;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.gray;
            camera.transform.position=new Vector3((float)center.X,24,(float)center.Z-24);camera.transform.LookAt(new Vector3((float)center.X,0,(float)center.Z));
            var texture=new RenderTexture(512,512,24);texture.Create();camera.targetTexture=texture;var image=new Texture2D(512,512,TextureFormat.RGB24,false);
            Color32[] reference=null;var previous=RenderTexture.active;
            try{foreach(int mode in new[]{0,1,2}){
                scene.World.NativeTrailPadReferenceEvidence=!pointComparison&&!padCandidateComparison&&!padMemoComparison&&mode!=1;
                if(pointComparison)SetPointComparison(scene,mode!=1);
                if(padCandidateComparison)SetPadCandidateComparison(scene,mode!=1);
                SetPadMemoComparison(scene,!padMemoComparison||mode!=1);
                NativeGraphicsSettings.GroundTrailsEnabled=mode!=2;
                var last=facts[facts.Length-1];facts[facts.Length-1]=new PlayableGroundTrail(last.From,last.To,last.Heading,last.Distance,last.Tick,last.Kind);
                scene.World.RenderGroundTrails(scene.Frame(sequence++,facts));camera.Render();RenderTexture.active=texture;image.ReadPixels(new Rect(0,0,512,512),0,0);image.Apply();
                var pixels=image.GetPixels32();File.WriteAllBytes(Path.Combine(output,mode==0?"native.png":mode==1?"candidate.png":"disabled.png"),image.EncodeToPNG());
                if(mode==0)reference=pixels;else if(mode==1)CollectionAssert.AreEqual(reference,pixels,"Actual rendered pixels must match the native apron control");
                else Assert.Greater(reference.Where((p,i)=>p.r!=pixels[i].r||p.g!=pixels[i].g||p.b!=pixels[i].b).Count(),100,"The compared frame must contain visible ground impressions");
            }}finally{RenderTexture.active=previous;camera.targetTexture=null;texture.Release();UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(texture);NativeGraphicsSettings.GroundTrailsEnabled=true;}
        }
        [Test,Explicit("Exclusive fixed-input native/candidate/native trail mesh CPU control")]
        public void FixedTrailMeshKeepsExactBuffersAndRecordsCost()=>CheckFixedTrailMesh(false);
        [Test,Explicit("Exclusive cached Crossings trail native/candidate/native CPU and pixel control")]
        public void CachedTrailMeshKeepsExactBuffersAndRecordsCost()=>CheckFixedTrailMesh(true);
        [Test,Explicit("Exclusive original/candidate/original trail point dictionary CPU and pixel control")]
        public void TrailPointComparisonKeepsExactBuffersAndPixels()=>CheckFixedTrailMesh(true,true);
        [Test,Explicit("Exclusive predecessor/candidate/predecessor trail pad candidate CPU and pixel control")]
        public void TrailPadCandidatesKeepExactBuffersAndPixels()=>CheckFixedTrailMesh(true,padCandidateComparison:true);
        [Test,Explicit("Exclusive predecessor/candidate/predecessor trail pad memo CPU and pixel control")]
        public void TrailPadMemoKeepsExactBuffersAndPixels()=>CheckFixedTrailMesh(true,padMemoComparison:true);
        private static void SetPadMemoComparison(Scene scene,bool disabled)=>
            typeof(PlayableWorld).GetField("DisableTrailPadMemoForQa",Private).SetValue(scene.World,disabled);
        private static void SetPadCandidateComparison(Scene scene,bool predecessor)=>
            typeof(PlayableWorld).GetField("DisableTrailPadCandidateCullForQa",Private).SetValue(scene.World,predecessor);
        private static void CheckFixedTrailMesh(bool crossings,bool pointComparison=false,bool padCandidateComparison=false,bool padMemoComparison=false)
        {
            string output=Environment.GetEnvironmentVariable("SPACEWARS_TRAIL_PAD_OUTPUT");Assert.IsNotNull(output);Directory.CreateDirectory(output);
            bool previous=NativeGraphicsSettings.GroundTrailsEnabled;
            try{NativeGraphicsSettings.GroundTrailsEnabled=true;using(var scene=new Scene(crossings)){
                var random=new System.Random(51017);var center=scene.Basis.Buildings[0].Position;var facts=new PlayableGroundTrail[2000];
                for(int i=0;i<facts.Length;i++){
                    double x=i<1000?center.X+random.NextDouble()*24-12:random.NextDouble()*250-125,z=i<1000?center.Z+random.NextDouble()*24-12:random.NextDouble()*250-125,heading=random.NextDouble()*Math.PI*2;
                    facts[i]=new PlayableGroundTrail(new NavPoint(x,z),new NavPoint(x+Math.Cos(heading)*.14,z+Math.Sin(heading)*.14),heading,i*.14,3,(PlayableEntityKind)(i%3));
                }
                var report=new System.Text.StringBuilder();byte[] expected=null;long sequence=1;
                for(int stage=0;stage<3;stage++){
                    scene.World.NativeTrailPadReferenceEvidence=!pointComparison&&!padCandidateComparison&&!padMemoComparison&&stage!=1;
                    if(pointComparison)SetPointComparison(scene,stage!=1);
                    if(padCandidateComparison)SetPadCandidateComparison(scene,stage!=1);
                    SetPadMemoComparison(scene,!padMemoComparison||stage!=1);
                    long ticks=0;
                    for(int repeat=0;repeat<4;repeat++){
                        var last=facts[facts.Length-1];facts[facts.Length-1]=new PlayableGroundTrail(last.From,last.To,last.Heading,last.Distance,last.Tick,last.Kind);
                        var view=scene.Frame(sequence++,facts);long start=Stopwatch.GetTimestamp();scene.World.RenderGroundTrails(view);long elapsed=Stopwatch.GetTimestamp()-start;if(repeat>0)ticks+=elapsed;
                        var bytes=MeshBytes(scene);if(expected==null)expected=bytes;else CollectionAssert.AreEqual(expected,bytes,"All vertices, indices, UVs and colors must match the exact native control");
                    }
                    if(padMemoComparison&&stage==1)
                        Assert.True((bool)typeof(PlayableWorld).GetField("trailPadMemoActive",Private).GetValue(scene.World),
                            "exercise pad memo on the measured geometry");
                    report.AppendLine("TRAIL_PAD_CONTROL stage="+stage+" samples=2000 repeats=3 total_ms="+(ticks*1000d/Stopwatch.Frequency).ToString("R",System.Globalization.CultureInfo.InvariantCulture));
                }
                using(var hash=SHA256.Create())report.AppendLine("mesh_sha256="+BitConverter.ToString(hash.ComputeHash(expected)).Replace("-","").ToLowerInvariant());
                File.WriteAllText(Path.Combine(output,"measurement.txt"),report.ToString());TestContext.Out.Write(report.ToString());
                RenderPair(scene,facts,output,sequence,pointComparison,padCandidateComparison,padMemoComparison);
            }}finally{NativeGraphicsSettings.GroundTrailsEnabled=previous;}
        }

        [Test,Explicit("Exclusive fixed-input trail geometry turnover control")]
        public void TrailGeometryTurnoverReusesBuffersWithoutChangingMesh()
        {
            const int frames=16,samples=96;
            string output=Environment.GetEnvironmentVariable("SPACEWARS_TRAIL_PAD_OUTPUT");
            Assert.IsNotEmpty(output);
            Directory.CreateDirectory(output);
            var expected=new byte[frames][];
            bool previous=NativeGraphicsSettings.GroundTrailsEnabled;
            var report=new List<string>();
            try{
                NativeGraphicsSettings.GroundTrailsEnabled=true;
                for(int stage=0;stage<3;stage++){
                    using var scene=new Scene(true);
                    bool reference=stage!=1;
                    typeof(PlayableWorld).GetField("UseTrailAllocationReferenceForQa",Private).SetValue(scene.World,reference);
                    long elapsed=0,allocated=0;
                    for(int frame=0;frame<frames;frame++){
                        var facts=new PlayableGroundTrail[samples];
                        for(int i=0;i<facts.Length;i++){
                            double x=-40+(i%12)*.75+(frame%3)*.11,z=-24+(i/12)*.75;
                            facts[i]=new PlayableGroundTrail(new NavPoint(x,z),new NavPoint(x+.14,z),0,i*.14,3,(PlayableEntityKind)(i%3));
                        }
                        var view=scene.Frame(frame+1,facts);
                        long beforeBytes=GC.GetAllocatedBytesForCurrentThread(),start=Stopwatch.GetTimestamp();
                        scene.World.RenderGroundTrails(view);
                        long elapsedTicks=Stopwatch.GetTimestamp()-start;
                        long bytes=GC.GetAllocatedBytesForCurrentThread()-beforeBytes;
                        if(frame>=3){elapsed+=elapsedTicks;allocated+=bytes;}
                        Assert.Greater(scene.World.GroundTrailQuadCount,0);
                        var actual=MeshBytes(scene);
                        if(stage==0)expected[frame]=actual;
                        else CollectionAssert.AreEqual(expected[frame],actual,"exact mesh after replacing every trail sample at frame "+frame);
                    }
                    int geometry=(int)typeof(PlayableWorld).GetField("TrailGeometryCreatedForQa",Private).GetValue(scene.World);
                    int arrays=(int)typeof(PlayableWorld).GetField("TrailCornerArraysCreatedForQa",Private).GetValue(scene.World);
                    Assert.AreEqual(reference?frames*samples:2*samples,geometry,"two batches cover turnover after warmup");
                    if(reference)Assert.GreaterOrEqual(arrays,frames*samples*2);
                    else Assert.Zero(arrays);
                    report.Add(FormattableString.Invariant($"stage={stage} frames={frames} samples={samples} geometry_created={geometry} corner_arrays={arrays} warm_ms={elapsed*1000d/Stopwatch.Frequency:R} warm_thread_alloc_bytes={allocated}"));
                }
            }finally{NativeGraphicsSettings.GroundTrailsEnabled=previous;}
            File.WriteAllLines(Path.Combine(output,"turnover.txt"),report);
            foreach(string line in report)TestContext.Out.WriteLine(line);
        }

        [Test,Explicit("Exclusive trail dictionary reference/candidate/reference timing; no native diagnostic dependency")]
        public void TrailPointComparisonPreservesDefaultKeysAndRecordsCost()
        {
            using(var scene=new Scene(true)){
                var comparer=scene.TrailComparer;
                var scalars=new[]{0d,BitConverter.Int64BitsToDouble(long.MinValue),double.Epsilon,-double.Epsilon,1d,-1d,double.MaxValue,double.MinValue,double.PositiveInfinity,double.NegativeInfinity,double.NaN,BitConverter.Int64BitsToDouble(0x7ff8000000000031L)};
                int comparisons=0;
                foreach(double x in scalars)foreach(double z in scalars)foreach(double u in scalars)foreach(double v in scalars){var a=new NavPoint(x,z);var b=new NavPoint(u,v);bool same=EqualityComparer<NavPoint>.Default.Equals(a,b);Assert.AreEqual(same,comparer.Equals(a,b));if(same)Assert.AreEqual(comparer.GetHashCode(a),comparer.GetHashCode(b));comparisons++;}
                var keys=Enumerable.Range(0,2048).Select(i=>new NavPoint(-48+(i%4)*.17,-40+(i/4)*.03125)).Concat(Queries(scene)).Concat(scalars.SelectMany(x=>scalars.Select(z=>new NavPoint(x,z)))).ToArray();
                var reference=new Dictionary<NavPoint,GroundTrailSurfaceKind>();var candidate=new Dictionary<NavPoint,GroundTrailSurfaceKind>(comparer);
                for(int i=0;i<keys.Length;i++){reference[keys[i]]=(GroundTrailSurfaceKind)(i%3);candidate[keys[i]]=(GroundTrailSurfaceKind)(i%3);}
                Assert.AreEqual(reference.Count,candidate.Count);foreach(var pair in reference){Assert.True(candidate.TryGetValue(pair.Key,out var value));Assert.AreEqual(pair.Value,value);}
                long expected=-1;
                for(int arm=0;arm<3;arm++){
                    var dictionary=arm==1?candidate:reference;
                    for(int warm=0;warm<32;warm++)dictionary.TryGetValue(keys[warm],out _);
                    long clock=Stopwatch.GetTimestamp(),checksum=0;
                    for(int repeat=0;repeat<3;repeat++)for(int i=0;i<keys.Length;i++){
                        var point=new NavPoint(keys[i].X,keys[i].Z);
                        if(dictionary.TryGetValue(point,out var value))checksum+=1+(int)value;
                    }
                    long ticks=Stopwatch.GetTimestamp()-clock;
                    if(expected<0)expected=checksum;else Assert.AreEqual(expected,checksum);
                    TestContext.WriteLine("TRAIL_POINT_CONTROL arm="+arm+" keys="+keys.Length+" comparisons="+comparisons+" ms="+(ticks*1000d/Stopwatch.Frequency)+" checksum="+checksum);
                }
            }
        }
    }
}
