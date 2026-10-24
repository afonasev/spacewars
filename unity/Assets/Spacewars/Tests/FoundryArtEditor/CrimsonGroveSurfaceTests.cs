using System;
using System.Linq;
using NUnit.Framework;
using Spacewars.Simulation;
using Spacewars.Presentation;
using UnityEngine;
using UnityEditor;
namespace Spacewars.Tests.EditMode
{
 public sealed class CrimsonGroveSurfaceTests
 {
  [Test] public void ApronsContainAllSlotsAndStayOnTheirExistingTerrace(){
   var m=new CrimsonGroveMap();var p=PlayableProfile.Create(PlayableProfile.Default.CopyData(),m);var art=CrimsonGroveArtProfile.Load();
   foreach(var site in m.Sites(p).Where(s=>s.Kind!=PlayableBuildingKind.Mine)){
    var pad=CrimsonGroveSurfaces.Pad(site,p,art);Assert.True(pad.Contains(site.Position));
    foreach(var slot in site.Slots){Assert.True(pad.Contains(slot.Position));Assert.GreaterOrEqual(pad.BoundaryDistanceSquared(slot.Position),p.OrdinaryPadRadius*p.OrdinaryPadRadius);}
    foreach(var v in pad.Footprint.Vertices){Assert.True(m.SupportsFootprint(v,0),"apron clearance "+site.Id);Assert.AreEqual(m.SurfaceHeight(site.Position),m.SurfaceHeight(v),1e-6,"flat apron "+site.Id);}
   }
  }
  [Test] public void DecorativeRimIsNarrowAndRetainsItsFullInnerBoundary(){
   var m=new CrimsonGroveMap();var art=CrimsonGroveArtProfile.Load();
   foreach(var border in m.Border){var trim=CrimsonGroveSurfaces.TrimBorder(m,border,art);double inner=border.Footprint.Vertices.Min(v=>Math.Sqrt(v.X*v.X+v.Z*v.Z));
    Assert.True(trim.Footprint.Vertices.All(v=>Math.Sqrt(v.X*v.X+v.Z*v.Z)<=inner+art.outerRimWidth+1e-6));
    foreach(var v in border.Footprint.Vertices.Where(v=>Math.Sqrt(v.X*v.X+v.Z*v.Z)<inner+.01))Assert.True(trim.Footprint.Vertices.Any(t=>Math.Abs(t.X-v.X)+Math.Abs(t.Z-v.Z)<1e-6));
   }
  }
  [Test] public void SculptedRocksStayInsideEveryAuthoredSolidAndOriginalHeight(){
   var map=new CrimsonGroveMap();var art=CrimsonGroveArtProfile.Load();
   foreach(var authored in map.Solids){
    var rock=map.Border.Contains(authored)?CrimsonGroveSurfaces.TrimBorder(map,authored,art):authored;
    var mesh=CrimsonGroveSurfaces.LayeredRock(rock,art,out var cap);
    try{Assert.Greater(mesh.vertexCount,0);foreach(var v in mesh.vertices){
     Assert.False(float.IsNaN(v.x)||float.IsNaN(v.y)||float.IsNaN(v.z));
     Assert.LessOrEqual(rock.Footprint.DistanceSquared(new NavPoint(v.x,v.z)),1e-8,"dressing escaped blocked footprint");
     Assert.That(v.y,Is.InRange(-.0001f,(float)rock.Top+.0001f));
    }Assert.Greater(cap.MinimumHeight,0);}finally{UnityEngine.Object.DestroyImmediate(mesh);}
   }
  }
  [Test] public void CrystalClustersHaveSeparateFacetsAndRespectReservedClearance(){
   var art=CrimsonGroveArtProfile.Load();
   for(int seed=0;seed<12;seed++){
    var mesh=PlayableWorld.GroveCrystal(art.crownRadius,art.treeHeight,art,new System.Random(seed));
    try{Assert.AreEqual(Mathf.RoundToInt(art.crystalShards)*6*9,mesh.vertexCount);
     Assert.AreEqual(mesh.vertexCount,mesh.colors.Length);
     foreach(var v in mesh.vertices)Assert.LessOrEqual(new Vector2(v.x,v.z).magnitude,art.crownRadius+.0001f);
     Assert.Greater(mesh.normals.Distinct().Count(),24,"individual crystal facets");
    }finally{UnityEngine.Object.DestroyImmediate(mesh);}
   }
   Assert.Greater(art.Terrace.grayscale,art.Ground.grayscale*1.5f,"clearly separated support levels");
   art.Validate();
  }
  [Test] public void FinishedSupportsKeepExactTopAndRecessEverySideInsideAuthority(){
   var map=new CrimsonGroveMap();var art=CrimsonGroveArtProfile.Load();int recessed=0;
   foreach(var support in map.Supports){
    var top=CrimsonGroveSurfaces.SupportTop(support);var side=CrimsonGroveSurfaces.SupportSides(support,art);
    try{
     foreach(var v in top.vertices)Assert.AreEqual(support.HeightAt(new NavPoint(v.x,v.z)),v.y,1e-5,"authored top unchanged");
     foreach(var v in side.vertices){var p=new NavPoint(v.x,v.z);
      Assert.False(float.IsNaN(v.x)||float.IsNaN(v.y)||float.IsNaN(v.z));
      Assert.LessOrEqual(support.Bounds.Footprint.DistanceSquared(p),1e-8,"side escaped volume");
      Assert.LessOrEqual(v.y,support.HeightAt(p)+1e-5);Assert.GreaterOrEqual(v.y,-2.0001f);
      if(v.y>0&&support.Bounds.Footprint.BoundaryDistanceSquared(p)>.0001)recessed++;
     }
    }finally{UnityEngine.Object.DestroyImmediate(top);UnityEngine.Object.DestroyImmediate(side);}
   }Assert.Greater(recessed,100,"side faces have actual recessed geometry");
  }
  [Test] public void PrefabPanelsAndFittingsStayWithinCompleteFlushApron(){
   var m=new CrimsonGroveMap();var p=PlayableProfile.Create(PlayableProfile.Default.CopyData(),m);var art=CrimsonGroveArtProfile.Load();
   foreach(var site in m.Sites(p).Where(s=>s.Kind!=PlayableBuildingKind.Mine)){
    var pad=CrimsonGroveSurfaces.Pad(site,p,art);var mesh=CrimsonGroveSurfaces.PrefabApron(pad,site,m,art);
    try{Assert.Greater(mesh.vertexCount,pad.Footprint.Vertices.Count*2);Assert.AreEqual(mesh.vertexCount,mesh.colors.Length);
     // Original full polygon remains the continuous foundation, including all slots.
     for(int i=0;i<pad.Footprint.Vertices.Count;i++){
      var v=mesh.vertices[i];Assert.AreEqual(pad.Footprint.Vertices[i].X,v.x,1e-5);Assert.AreEqual(pad.Footprint.Vertices[i].Z,v.z,1e-5);
     }
     foreach(var v in mesh.vertices){Assert.LessOrEqual(pad.Footprint.DistanceSquared(new NavPoint(v.x,v.z)),1e-8);Assert.That(v.y-m.SurfaceHeight(site.Position),Is.InRange(.0119,.024));}
    }finally{UnityEngine.Object.DestroyImmediate(mesh);}
   }
  }
  [Test] public void ActualImportedSiblingMaterialsRemainSharpAndRepeatable(){
   foreach(var family in new[]{"Earth","Rock","Concrete"})foreach(var suffix in new[]{"","-b","-c"}){
    var tex=Resources.Load<Texture2D>("Environment/NaturalFrontier/"+family+suffix);Assert.NotNull(tex);
    var importer=(TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(tex));
    Assert.GreaterOrEqual(tex.width,1024);Assert.GreaterOrEqual(tex.height,1024);Assert.AreEqual(TextureWrapMode.Repeat,tex.wrapMode);Assert.True(importer.mipmapEnabled);
    TestContext.WriteLine(family+suffix+" "+tex.width+"x"+tex.height+" "+tex.format+" filter="+tex.filterMode+" aniso="+tex.anisoLevel+" mipmaps="+importer.mipmapEnabled);
   }
  }
 }
}
