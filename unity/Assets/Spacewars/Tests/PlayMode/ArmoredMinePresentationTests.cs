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
                var root=Node(mine,"modelRoot");var fixedPose=root.localToWorldMatrix;var pivot=Node(mine,"rockerPivot");
                var profile=mine.Profile;profile.Validate();Assert.AreEqual(10,profile.Parameters().Count());
                Assert.AreEqual(12,profile.rockerDegrees);
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
        static void CheckCylinder(Transform t,Vector3 start,Vector3 end)
        {
            var mesh=t.GetComponent<MeshFilter>().sharedMesh;
            Assert.That(mesh.bounds.size.y,Is.EqualTo(1).Within(.001),"Rig mesh unit length");
            Assert.That(Vector3.Distance(t.TransformPoint(new Vector3(0,-.5f,0)),start),Is.LessThan(.001));
            Assert.That(Vector3.Distance(t.TransformPoint(new Vector3(0,.5f,0)),end),Is.LessThan(.001));
        }
    }
}
