using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.TestTools;
using Object=UnityEngine.Object;

namespace Spacewars.Tests.PlayMode
{
    public sealed class ArmoredMinePresentationTests
    {
        static PlayableBuildingSnapshot Building(int id=10,double progress=1)=>new PlayableBuildingSnapshot(id,PlayableOwner.Player,PlayableBuildingKind.Mine,default,250,progress,0,0,default,phase:progress<1?ConstructionPhase.Constructing:ConstructionPhase.Ready);
        static Transform Node(Component root,string name)=>root.GetComponentsInChildren<Transform>(true).Single(t=>t.name==name);
        [UnityTest] public IEnumerator SharedMinePathConnectedCyclePauseAndOwnerPaint()
        {
            var host=new GameObject("Mine runtime witness");var world=new PlayableWorld(host.transform,PlayableProfile.Default);
            try
            {
                world.SetPresentationLayer(11);
                var actor=world.Building(10,"Mine",true);var mine=actor.Mine;
                Assert.NotNull(mine);Assert.That(actor.Hull.name,Does.StartWith("mine-armored"));Assert.IsEmpty(actor.Hull.GetComponentsInChildren<Collider>());
                Assert.That(actor.Hull.localScale.x,Is.EqualTo(PlayableProfile.Default.MineModelHeightMeters*PlayableProfile.Default.MineModelScale/1.9945).Within(.00001));
                var profile=mine.Profile;profile.Validate();Assert.AreEqual(21,profile.Parameters().Count());
                Assert.AreEqual(-30,profile.facingYawDegrees);
                var facing=Quaternion.LookRotation(Vector3.back,Vector3.up)*Quaternion.Euler(0,profile.facingYawDegrees,0);
                Assert.Less(Quaternion.Angle(facing,actor.Hull.rotation),.001f);
                world.FaceBuilding(actor);Assert.Less(Quaternion.Angle(facing,actor.Hull.rotation),.001f);
                var root=Node(mine,"modelRoot");var fixedPose=root.localToWorldMatrix;var pivot=Node(mine,"rockerPivot");
                Assert.AreEqual(16,profile.rockerDegrees);
                yield return CaptureFacing(world,actor);
                foreach(var field in profile.Parameters()){float old=field.Read();field.Write(float.NaN);Assert.Throws<ArgumentException>(()=>profile.Validate());field.Write(old);}
                var first=pivot.localRotation;bool moved=false;bool dustSeen=false;
                for(int tick=0;tick<=150;tick++)
                {
                    mine.Drive(Building(),tick,10);moved|=Quaternion.Angle(first,pivot.localRotation)>1;dustSeen|=mine.DustCount>0;
                    Assert.AreEqual(fixedPose,root.localToWorldMatrix,"Foundation cannot move");
                    // Crosshead runner is 0.72 m long; boom tip must stay between its end stops.
                    var tip=Node(mine,"rodTip");var slider=Node(mine,"drillSlider");
                    var localTip=slider.InverseTransformPoint(tip.position);
                    Assert.That(Mathf.Abs(localTip.z),Is.LessThan(.29f),"Boom stays inside slotted crosshead");
                    Assert.That(actor.Hull.InverseTransformPoint(slider.position).y-.58f+.6925f,Is.GreaterThan(.465f),"Crosshead clears fixed guide");
                    float shaftBottom=slider.GetComponentsInChildren<MeshFilter>().SelectMany(f=>f.sharedMesh.vertices.Select(v=>actor.Hull.InverseTransformPoint(f.transform.TransformPoint(v)).y)).Min();
                    Assert.That(shaftBottom,Is.LessThan(.465f),"Shaft stays engaged with the guide at the upper stroke");
                    for(int i=0;i<2;i++)
                    {
                        var a=Node(mine,"hydraulicBase"+i).position;var b=Node(mine,"hydraulicTip"+i).position;
                        CheckCylinder(Node(mine,"hydraulicBarrel"+i),a,Vector3.Lerp(a,b,.6f));
                        CheckCylinder(Node(mine,"hydraulicRod"+i),Vector3.Lerp(a,b,.45f),b);
                    }
                }
                Assert.IsTrue(moved);Assert.IsTrue(dustSeen,"Bottom stroke emits dust during complete cycle");
                var paused=pivot.localRotation;var particles=mine.DustCount;
                for(int f=0;f<4;f++){yield return null;mine.Drive(Building(),150,10);}
                Assert.AreEqual(paused,pivot.localRotation);Assert.AreEqual(particles,mine.DustCount);
                mine.Drive(Building(progress:.5),180,10);Assert.AreEqual(paused,pivot.localRotation);Assert.AreEqual(0,mine.DustCount);
                var other=world.Building(11,"Mine",false);other.Mine.Drive(Building(11),150,11);Assert.That(Quaternion.Angle(paused,Node(other.Mine,"rockerPivot").localRotation),Is.GreaterThan(.5));
                var paint=new Color(.6f,.2f,.8f);PlayableWorld.PaintOwner(actor,paint);
                int lights=0,armor=0;
                foreach(var r in actor.Hull.GetComponentsInChildren<Renderer>())for(int i=0;i<r.sharedMaterials.Length;i++)
                {
                    string role=r.sharedMaterials[i].name;if(!role.EndsWith("team-primary")&&!role.EndsWith("team-emissive"))continue;
                    var block=new MaterialPropertyBlock();r.GetPropertyBlock(block,i);var expected=new MaterialPropertyBlock();expected.SetColor("_BaseColor",paint);Assert.AreEqual(expected.GetColor("_BaseColor"),block.GetColor("_BaseColor"));
                    if(role.EndsWith("team-emissive")){lights++;expected.SetColor("_EmissionColor",paint*actor.MaterialFinish.TeamEmissionFor(role));Assert.AreEqual(expected.GetColor("_EmissionColor"),block.GetColor("_EmissionColor"));}else armor++;
                }
                Assert.Greater(lights,0);Assert.Greater(armor,0);
                var heat=mine.GetComponentsInChildren<Renderer>().Single(r=>r.sharedMaterials.Any(m=>m.name.EndsWith("well-hot-emissive")));
                var heatBlock=new MaterialPropertyBlock();heat.GetPropertyBlock(heatBlock,0);var heatColor=heatBlock.GetColor("_EmissionColor");
                Assert.That(heatColor.r,Is.GreaterThan(heatColor.g*20));Assert.That(heatColor.r,Is.GreaterThan(heatColor.b*20));
                Assert.That(heat.bounds.max.y/actor.Hull.localScale.y,Is.LessThan(.20f),"Red emitter stays below well rim");
                Assert.True(mine.GetComponentsInChildren<ParticleSystem>().All(p=>p.gameObject.layer==11));
                var building=Building();var rules=PlayableProfile.Default;
                var snapshot=new PlayableSnapshot(rules.ProfileId,rules.Revision,1,7,210,210,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,0,null,Array.Empty<PlayableEntitySnapshot>(),new[]{building},Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null);
                world.UpdateFactoryModel(actor,building,snapshot);Assert.That(Quaternion.Angle(paused,pivot.localRotation),Is.GreaterThan(.5),"Shared snapshot path drives mine");
                world.Remove(10);yield return null;Assert.NotNull(world.Building(10,"Mine",true).Mine);
            }
            finally{world.Dispose();Object.Destroy(host);}
        }
        [UnityTest] public IEnumerator ImpactSparksAndGlowFreezeSeekAndStopWithMatchTime()
        {
            var host=new GameObject("Mine impact contract");var world=new PlayableWorld(host.transform,PlayableProfile.Default);
            try
            {
                var mine=world.Building(20,"Mine",true).Mine;
                var sparks=Node(mine,"Bottom stroke sparks").GetComponent<ParticleSystem>();
                var light=Node(mine,"Recessed red shaft light").GetComponent<Light>();
                var outlet=Node(mine,"modelRoot").InverseTransformPoint(sparks.transform.position);
                Assert.AreEqual(.35f-mine.Profile.sparkOriginDepth,outlet.y,.0001f);Assert.Less(outlet.y,.35f,"Spark source must be below the shaft rim");
                Assert.AreEqual(0,outlet.x,.0001f);Assert.AreEqual(.22f,outlet.z,.0001f,"Spark source is at the bore centre, not the moving drill");
                mine.Sample(1.25f,0,true,0);Assert.Greater(light.color.g,.85f*light.color.r,"Exact impact must reach yellow");Assert.Less(light.color.b,.05f);
                mine.Drive(Building(),30,0);Assert.Zero(mine.SparkCount);Assert.AreEqual(1,mine.ImpactGlow);
                Assert.Less(light.color.g,.05f,"Between impacts the shaft light returns to red");
                mine.Drive(Building(),39,0);Assert.Greater(mine.SparkCount,0);Assert.LessOrEqual(mine.SparkCount,mine.Profile.sparkCount);
                Assert.Greater(mine.ImpactGlow,1);Assert.LessOrEqual(mine.ImpactGlow,1+mine.Profile.impactGlowBoost);
                Assert.AreEqual(mine.Profile.wellLightIntensity*mine.ImpactGlow,light.intensity,.0001f);
                var particles=new ParticleSystem.Particle[24];int count=sparks.GetParticles(particles);
                var glow=mine.ImpactGlow;var tint=light.color;var random=UnityEngine.Random.state;
                for(int frame=0;frame<4;frame++)
                {
                    yield return null;mine.Drive(Building(),39,0);var paused=new ParticleSystem.Particle[24];Assert.AreEqual(count,sparks.GetParticles(paused));
                    for(int i=0;i<count;i++){Assert.AreEqual(particles[i].position,paused[i].position);Assert.AreEqual(particles[i].remainingLifetime,paused[i].remainingLifetime);}
                    Assert.AreEqual(glow,mine.ImpactGlow);Assert.AreEqual(tint,light.color,"Colour transition freezes with the paused tick");
                }
                Assert.AreEqual(random,UnityEngine.Random.state,"Cosmetic sampling must not consume global RNG");
                mine.Drive(Building(),75,0);Assert.Zero(mine.SparkCount);Assert.AreEqual(1,mine.ImpactGlow,"No flash at the upper half of the stroke");
                mine.Drive(Building(),39,0);var rewound=new ParticleSystem.Particle[24];Assert.AreEqual(count,sparks.GetParticles(rewound));
                for(int i=0;i<count;i++)Assert.AreEqual(particles[i].position,rewound[i].position,"Seek reconstructs the same burst from match time");
                mine.Drive(Building(progress:.5),39,0);Assert.Zero(mine.SparkCount);Assert.AreEqual(1,mine.ImpactGlow,"Unfinished mine cannot emit or remain bright");
                var other=world.Building(21,"Mine",true).Mine;other.Drive(Building(21),39,0);
                var seek=new ParticleSystem.Particle[24];Assert.AreEqual(count,Node(other,"Bottom stroke sparks").GetComponent<ParticleSystem>().GetParticles(seek));
                for(int i=0;i<count;i++)Assert.AreEqual(particles[i].position,seek[i].position,"First observation in the impact window agrees with a running mine");
                var finish=ActorMaterialProfile.Load();finish.emissionGain*=.5f;mine.Drive(Building(),39,0,finish);
                var heat=mine.GetComponentsInChildren<Renderer>().Single(r=>r.sharedMaterials.Any(m=>m.name.EndsWith("well-hot-emissive")));
                int slot=Array.FindIndex(heat.sharedMaterials,m=>m.name.EndsWith("well-hot-emissive"));var block=new MaterialPropertyBlock();heat.GetPropertyBlock(block,slot);
                Assert.AreEqual(mine.Profile.wellEmission*finish.emissionGain*mine.ImpactGlow,block.GetColor("_EmissionColor").r,.0001f,"A graphics refresh during a paused flash must retain the new emission gain");
            }
            finally{world.Dispose();Object.Destroy(host);}
        }
        static long[] Costs(Transform hull)
        {
            var meshes=hull.GetComponentsInChildren<MeshFilter>().Select(f=>f.sharedMesh).Distinct().ToArray();
            var renderers=hull.GetComponentsInChildren<Renderer>();
            return new long[]{meshes.Length,meshes.Sum(m=>(long)m.vertexCount),meshes.Sum(m=>(long)m.triangles.Length/3),renderers.Length,
                renderers.Sum(r=>(long)r.sharedMaterials.Length),renderers.SelectMany(r=>r.sharedMaterials).Distinct().Count(),
                meshes.Sum(m=>UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(m))};
        }
        static IEnumerator CaptureFacing(PlayableWorld world,PlayableWorld.Actor actor)
        {
            string folder=Environment.GetEnvironmentVariable("SPACEWARS_MINE_FACING_EVIDENCE");if(string.IsNullOrEmpty(folder))yield break;
            System.IO.Directory.CreateDirectory(folder);world.Fog.ShowPublic();
            int frameCount=4;string requestedFrames=Environment.GetEnvironmentVariable("SPACEWARS_MINE_ANIMATION_FRAMES");
            if(requestedFrames!=null){Assert.IsTrue(int.TryParse(requestedFrames,out frameCount));Assert.That(frameCount,Is.InRange(4,120));}
            var rotation=actor.Hull.rotation;var initial=Costs(actor.Hull);
            bool compareImpact=Environment.GetEnvironmentVariable("SPACEWARS_MINE_IMPACT_COMPARISON")=="1";
            float savedCount=actor.Mine.Profile.sparkCount,savedBoost=actor.Mine.Profile.impactGlowBoost;
            bool reviseImpact=Environment.GetEnvironmentVariable("SPACEWARS_MINE_REVISED_IMPACT_COMPARISON")=="1";
            float savedHue=actor.Mine.Profile.impactGlowHue,savedDuration=actor.Mine.Profile.impactGlowDuration,savedLifetime=actor.Mine.Profile.sparkLifetime;
            var sparks=Node(actor.Mine,"Bottom stroke sparks").GetComponent<ParticleSystem>();var savedOrigin=sparks.transform.localPosition;
            var cameraObject=new GameObject("Fixed mine orientation inspection");var camera=cameraObject.AddComponent<Camera>();camera.enabled=false;
            camera.cullingMask=1<<11;camera.orthographic=true;camera.orthographicSize=2.1f;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.13f,.15f,.17f);
            if(compareImpact)cameraObject.AddComponent<ActorGlowCamera>().Configure(ActorMaterialProfile.Load());
            camera.transform.position=new Vector3(0,6,-8);camera.transform.LookAt(new Vector3(0,.7f,0));
            var sunObject=new GameObject("Fixed mine inspection light");var sun=sunObject.AddComponent<Light>();sun.type=LightType.Directional;sun.intensity=1.6f;sun.cullingMask=1<<11;sun.transform.rotation=Quaternion.Euler(50,-35,0);
            var target=new RenderTexture(1280,800,24);target.Create();camera.targetTexture=target;
            actor.Selection.SetActive(false);actor.Health.parent.gameObject.SetActive(false);
            try
            {
                foreach(bool candidate in new[]{false,true})
                {
                    actor.Hull.rotation=compareImpact?rotation:candidate?rotation:Quaternion.LookRotation(Vector3.back,Vector3.up);
                    if(compareImpact){actor.Mine.Profile.sparkCount=candidate?savedCount:0;actor.Mine.Profile.impactGlowBoost=candidate?savedBoost:0;}
                    if(reviseImpact)
                    {
                        actor.Mine.Profile.sparkCount=savedCount;actor.Mine.Profile.impactGlowBoost=savedBoost;
                        actor.Mine.Profile.impactGlowHue=candidate?savedHue:0;actor.Mine.Profile.impactGlowDuration=candidate?savedDuration:.22f;
                        actor.Mine.Profile.sparkLifetime=candidate?savedLifetime:.28f;
                        sparks.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);sparks.transform.localPosition=candidate?savedOrigin:new Vector3(0,.56f,.57f);
                        var main=sparks.main;main.duration=actor.Mine.Profile.sparkLifetime;main.startLifetime=actor.Mine.Profile.sparkLifetime;main.startSpeed=candidate?actor.Mine.Profile.sparkSpeed:2.4f;
                        var shape=sparks.shape;shape.angle=candidate?actor.Mine.Profile.sparkSpread:55;
                    }
                    for(int pose=0;pose<frameCount;pose++)
                    {
                        actor.Mine.Sample(pose*actor.Mine.Profile.cycleSeconds/frameCount,0,true,0);yield return null;
                        CollectionAssert.AreEqual(initial,Costs(actor.Hull),"Yaw must not duplicate meshes, renderers, materials or mesh memory");
                        camera.Render();var old=RenderTexture.active;RenderTexture.active=target;var png=new Texture2D(1280,800,TextureFormat.RGB24,false);
                        try{png.ReadPixels(new Rect(0,0,1280,800),0,0);png.Apply();System.IO.File.WriteAllBytes(System.IO.Path.Combine(folder,(candidate?"after":"before")+"-pose-"+pose+".png"),png.EncodeToPNG());}
                        finally{RenderTexture.active=old;Object.Destroy(png);}
                    }
                }
                System.IO.File.WriteAllText(System.IO.Path.Combine(folder,"model-costs.json"),"{\"fields\":[\"uniqueMeshes\",\"vertices\",\"triangles\",\"renderers\",\"materialSlots\",\"uniqueMaterials\",\"meshMemoryBytes\"],\"before\":["+string.Join(",",initial)+"],\"after\":["+string.Join(",",Costs(actor.Hull))+"]}");
            }
            finally{actor.Mine.Profile.impactGlowHue=savedHue;actor.Mine.Profile.impactGlowDuration=savedDuration;actor.Mine.Profile.sparkLifetime=savedLifetime;sparks.transform.localPosition=savedOrigin;actor.Mine.Profile.sparkCount=savedCount;actor.Mine.Profile.impactGlowBoost=savedBoost;actor.Hull.rotation=rotation;actor.Mine.Sample(0,0,false,0);camera.targetTexture=null;target.Release();Object.Destroy(target);Object.Destroy(cameraObject);Object.Destroy(sunObject);}
        }
        static void CheckCylinder(Transform t,Vector3 start,Vector3 end)
        {
            var mesh=t.GetComponent<MeshFilter>().sharedMesh;
            Assert.That(mesh.bounds.size.y,Is.EqualTo(1).Within(.001),"Rig mesh unit length");
            Assert.That(Vector3.Distance(t.TransformPoint(new Vector3(0,-.5f,0)),start),Is.LessThan(.001));
            Assert.That(Vector3.Distance(t.TransformPoint(new Vector3(0,.5f,0)),end),Is.LessThan(.001));
        }
    }
}
