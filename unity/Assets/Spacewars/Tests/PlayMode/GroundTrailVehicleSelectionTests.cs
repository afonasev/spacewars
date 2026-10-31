using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Newtonsoft.Json;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;
namespace Spacewars.Tests.PlayMode
{
    public sealed class GroundTrailVehicleSelectionTests
    {
        static PlayableSnapshot Frame(PlayableProfile p,int moving,long sequence,PlayableGroundTrail[] facts,long tick=270)
            =>new PlayableSnapshot(p.ProfileId,p.Revision,1,7319,sequence,tick,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,0,null,Enumerable.Range(0,moving).Select(i=>new PlayableEntitySnapshot(i+1,PlayableOwner.Player,(PlayableEntityKind)(i%3),new NavPoint(-40,i*.5),100,true,0,0,0)).ToArray(),Array.Empty<PlayableBuildingSnapshot>(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,groundTrails:facts);
        [TestCase(2)][TestCase(4)] public void SelectedVehiclesKeepEveryImpression(int divisor)
        {
            var facts=GroundTrailDensityRenderingTests.Facts();var selection=new GroundTrailVehicleSelection();selection.Begin(1,270,300);int kept=0;
            var groups=facts.GroupBy(f=>f,GroundTrailJournal.BySource).ToArray();Assert.AreEqual(50,groups.Length);
            foreach(var vehicle in groups){var choices=vehicle.Select(f=>selection.Keep(f,divisor)).Distinct().ToArray();Assert.AreEqual(1,choices.Length,"one complete track per vehicle, never dotted sample thinning");if(choices[0])kept++;}
            Assert.Greater(kept,0);Assert.Less(kept,50);Assert.AreEqual(facts.Length,selection.SampleCount);
            Assert.AreEqual(facts.Length,GroundTrailDensityRenderingTests.Facts().Length,"render selection leaves journal emission intact");
        }
        [Test] public void StopAndFogRevisitDoNotRestoreOmittedHistory()
        {
            var facts=GroundTrailDensityRenderingTests.Facts();var selection=new GroundTrailVehicleSelection();selection.Begin(1,270,300);
            var initial=facts.Select(f=>selection.Keep(f,4)).ToArray();Assert.True(initial.Any(v=>!v));
            selection.Begin(1,271,300);for(int i=facts.Length-1;i>=0;i--)Assert.AreEqual(initial[i],selection.Keep(facts[i],1),"stop/visibility order cannot restore old omitted marks");
            selection.Begin(1,1000,300);Assert.Zero(selection.SampleCount);
        }
        [Test] public void AnonymousSourceAssociatesVehiclesWithoutSerialisingIdentity()
        {
            var journal=new GroundTrailJournal();journal.Observe(9123,PlayableEntityKind.Tank,new NavPoint(0,0),new NavPoint(.1,0),0,1);journal.Observe(9123,PlayableEntityKind.Tank,new NavPoint(.1,0),new NavPoint(.4,0),0,4);
            var facts=journal.Visible(4);Assert.AreEqual(2,facts.Length);Assert.True(GroundTrailJournal.BySource.Equals(facts[0],facts[1]));
            var json=JsonConvert.SerializeObject(facts[0]);Assert.False(json.Contains("Source"));Assert.False(json.Contains("9123"));
            journal.RemoveMissing(new System.Collections.Generic.HashSet<int>());journal.Observe(9123,PlayableEntityKind.Tank,new NavPoint(3,0),new NavPoint(3.1,0),0,5);Assert.False(GroundTrailJournal.BySource.Equals(facts[0],journal.Visible(5).Last()),"reused actor ids are independent sources");
        }
        [TestCase(19,1)][TestCase(20,2)][TestCase(39,2)][TestCase(40,4)] public void NamedGraphicsProfileControlsVehicleCohorts(int moving,int expected)
        {Assert.AreEqual(expected,GroundTrailDensity.Divisor(Frame(PlayableProfile.ThreeCrossingsDefault,moving,1,Array.Empty<PlayableGroundTrail>()),new ModelDetailProfile()));}
        static byte[] MeshBytes(PlayableWorld world)=>(byte[])typeof(GroundTrailPresentationTests).GetMethod("TrailMeshBytes",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{world});
        [Test] public void RendererMatchesUnfilteredGeometryForSelectedWholeVehicles()
        {
            var p=PlayableProfile.ThreeCrossingsDefault;var root=new GameObject("whole vehicle trails test");PlayableWorld reference=null,actual=null;bool enabled=NativeGraphicsSettings.GroundTrailsEnabled;var previous=ModelDetailProfile.Current.Copy();
            try{
                ModelDetailProfile.UseSession(new ModelDetailProfile());NativeGraphicsSettings.GroundTrailsEnabled=true;reference=new PlayableWorld(root.transform,p,staged:true);actual=new PlayableWorld(root.transform,p,staged:true);reference.Fog.ShowPublic();actual.Fog.ShowPublic();
                var facts=GroundTrailDensityRenderingTests.Facts();var selection=new GroundTrailVehicleSelection();selection.Begin(1,270,300);var selected=facts.Where(f=>selection.Keep(f,4)).ToArray();Assert.Less(selected.Length,facts.Length);
                reference.RenderGroundTrails(Frame(p,0,1,selected));actual.RenderGroundTrails(Frame(p,50,1,facts));CollectionAssert.AreEqual(MeshBytes(reference),MeshBytes(actual),"kept vehicles retain exact original geometry, UVs, colours and indices");
                actual.RenderGroundTrails(Frame(p,0,2,facts,271));CollectionAssert.AreEqual(MeshBytes(reference),MeshBytes(actual),"stopping adds no historical geometry");
            }finally{ModelDetailProfile.UseSession(previous);NativeGraphicsSettings.GroundTrailsEnabled=enabled;reference?.Dispose();actual?.Dispose();UnityEngine.Object.DestroyImmediate(root);}
        }
    }
}
