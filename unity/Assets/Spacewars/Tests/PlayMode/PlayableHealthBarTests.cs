using System.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;

public sealed class PlayableHealthBarTests
{
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
                Assert.That(outline.localScale.x,Is.EqualTo(2+2*PlayableProfile.Default.HealthBarOutlineWidth).Within(.0001));
                Assert.That(outline.localScale.y,Is.EqualTo(.15+2*PlayableProfile.Default.HealthBarOutlineWidth).Within(.0001));
                var color=new MaterialPropertyBlock();outline.GetComponent<Renderer>().GetPropertyBlock(color);Assert.AreEqual(Color.black,color.GetColor("_BaseColor"));
                actor.Root.rotation=Quaternion.Euler(0,heading,0);
                actor.Root.localScale=new Vector3(1,construction,1);
                PlayableWorld.UpdateHealth(actor,camera,1);
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
                Assert.That(actor.Health.localScale.x,Is.EqualTo(2));
            }
        }
        finally{world.Dispose();Object.DestroyImmediate(host);Object.DestroyImmediate(cameraObject);}
    }
    [Test] public void HealthAndBuildingOutlineControlsValidateWithLabMetadata()
    {
        foreach(var field in PlayableProfileMetadata.Fields.Where(f=>f.Path.StartsWith("presentation.healthBar.")||f.Path.StartsWith("presentation.buildingSelection.")))
        {
            var data=PlayableProfile.Default.CopyData();Assert.IsNotEmpty(field.Description);Assert.IsNotEmpty(field.Unit);Assert.Greater(field.Step,0);
            foreach(double bad in new[]{double.NaN,double.PositiveInfinity,field.Minimum-field.Step,field.Maximum+field.Step})
            {Assert.Throws<System.ArgumentOutOfRangeException>(()=>field.Write(data,bad));
                typeof(PlayableProfileData).GetField(field.FieldName).SetValue(data,bad);Assert.Throws<System.ArgumentException>(()=>PlayableProfile.Validate(data));}
        }
    }
    private static Vector3 ScreenEdge(Transform fill,Camera camera,float x)=>camera.WorldToScreenPoint(fill.TransformPoint(new Vector3(x,0,0)));
}
