using System;
using System.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEditor;
using UnityEngine;
namespace Spacewars.Tests.EditMode
{
 public sealed class AntennaCityArtTests
 {
  [Test] public void ProfilesAndTextureImportsAreBoundAndValidated()
  {
   var art=AntennaCityArtProfile.Load();art.Validate();Assert.AreEqual(13,art.Parameters().Count());
   foreach(var p in art.Parameters()){float before=p.Read();p.Write(float.NaN);Assert.Throws<ArgumentException>(()=>art.Validate(),p.Path);p.Write(before);}
   Assert.AreEqual("AntennaCityLightingProfile",IndustrialLightingProfile.ResourceName("antenna-city-greybox-v1"));
   Assert.AreEqual("IndustrialLightingProfile",IndustrialLightingProfile.ResourceName("unknown"));
   var lighting=IndustrialLightingProfile.Load("antenna-city-greybox-v1");lighting.Validate();Assert.That(lighting.ambientValue,Is.GreaterThanOrEqualTo(.45f));
   var shader=Resources.Load<Shader>("AntennaCitySurface");Assert.NotNull(shader);Assert.False(ShaderUtil.ShaderHasError(shader));
   foreach(string family in new[]{"Environment/NaturalFrontier/Concrete","Environment/NullDock/Steel"})foreach(string suffix in new[]{"","-b","-c"}){
    var texture=Resources.Load<Texture2D>(family+suffix);Assert.NotNull(texture);Assert.That(texture.width,Is.GreaterThanOrEqualTo(512));Assert.That(texture.mipmapCount,Is.GreaterThan(1));Assert.AreEqual(TextureWrapMode.Repeat,texture.wrapMode);Assert.That(texture.anisoLevel,Is.GreaterThanOrEqualTo(4));
   }
  }
  [Test] public void YardPadsAndLanePaintFollowStreetFramesWithoutBlockingConstruction()
  {
   var map=new AntennaCityMap();var profile=PlayableProfile.Create(PlayableProfile.Default.CopyData(),map);var pads=AntennaCityPads.Create(map,profile);
   foreach(var pad in pads){
    var axis=new NavPoint(Math.Cos(pad.Heading),Math.Sin(pad.Heading));
    var vertices=pad.Outer.Footprint.Vertices;
    foreach(var v in vertices)Assert.True(map.SupportsFootprint(v,0),"pad corner "+pad.SiteId);
    // At least one border edge is parallel to the same local street/plaza axis.
    Assert.True(vertices.Select((a,i)=>{var b=vertices[(i+1)%vertices.Count];return Math.Abs((b.X-a.X)*axis.Z-(b.Z-a.Z)*axis.X);}).Any(c=>c<1e-7),"pad axis "+pad.SiteId);
    if(pad.Kind==PlayableBuildingKind.Headquarters||pad.Kind==PlayableBuildingKind.Outpost){
     Assert.AreEqual(4,vertices.Count);var p=vertices[0];var q=vertices[1];double radius=pad.Kind==PlayableBuildingKind.Headquarters?map.BaseYardRadius:map.OutpostYardRadius;
     Assert.AreEqual(2*radius*map.Scale*map.KeyPointPadScale,Math.Sqrt(Math.Pow(p.X-q.X,2)+Math.Pow(p.Z-q.Z,2)),1e-7,"No shrinking to fit the old yard");
    }
   }
   var paint=PlayableWorld.CityRoadMarkings(map);
   try{
    CollectionAssert.IsSubsetOf(new[]{4f,9f,10f},paint.uv2.Select(uv=>uv.x).Distinct().ToArray());
    Assert.Greater(paint.vertexCount,0);var v=paint.vertices;var t=paint.triangles;
    for(int i=0;i<t.Length;i+=3){var c=(v[t[i]]+v[t[i+1]]+v[t[i+2]])/3;var p=new NavPoint(c.x,c.z);
     Assert.True(map.Streets.Any(r=>r.Contains(p)),"paint must follow an authored street");
     Assert.False(map.Yards.Any(y=>y.Contains(p)),"paint inside a yard");
     Assert.False(map.Supports.Any(s=>s.Id!="city/street"&&s.Contains(p)),"paint under a raised support");
     Assert.LessOrEqual(map.Streets.Count(r=>r.Contains(p)),1,"paint crosses a junction");
    }
   }finally{UnityEngine.Object.DestroyImmediate(paint);}
  }
  [Test] public void AllCityTiersStayWithinTheOriginalObstacleEnvelope()
  {
   var map=new AntennaCityMap();var art=AntennaCityArtProfile.Load();long close=0,normal=0,far=0;int index=0;
   foreach(var quarter in map.Quarters){
    var shells=AntennaCityArchitecture.JoinConvexNeighbours(quarter);
    int theme=AntennaCityArchitecture.ThemeForQuarter(map,index,index%6);
    var tiers=Enumerable.Range(0,3).Select(level=>AntennaCityArchitecture.Quarter(shells,map.BlockHeight,theme,level,art)).ToArray();
    try{
     for(int level=0;level<3;level++){
      var mesh=tiers[level];Assert.AreEqual(1,mesh.subMeshCount);Assert.AreEqual(mesh.vertexCount,mesh.colors.Length);Assert.AreEqual(mesh.vertexCount,mesh.uv2.Length);Assert.AreEqual(tiers[0].bounds,mesh.bounds,"LOD must keep exact outer silhouette and culling envelope");
      foreach(var v in mesh.vertices){Assert.That(v.y,Is.InRange(-.001f,(float)map.BlockHeight+.001f));
       // Euclidean distance to the polygon handles acute source corners correctly;
       // four cardinal offset samples can all miss the inside of a thin wedge.
       bool inside=quarter.Any(b=>b.Contains(new NavPoint(v.x,v.z))||b.Footprint.Vertices.Select((a,i)=>{
        var z=b.Footprint.Vertices[(i+1)%b.Footprint.Vertices.Count];double dx=z.X-a.X,dz=z.Z-a.Z;
        double t=Math.Max(0,Math.Min(1,((v.x-a.X)*dx+(v.z-a.Z)*dz)/(dx*dx+dz*dz)));
        return Math.Sqrt(Math.Pow(v.x-a.X-t*dx,2)+Math.Pow(v.z-a.Z-t*dz,2));
       }).Min()<=.005);
       Assert.True(inside,"Decoration escaped blocked footprint at "+v);
      }
     }
     close+=tiers[0].triangles.Length/3;normal+=tiers[1].triangles.Length/3;far+=tiers[2].triangles.Length/3;
     var again=AntennaCityArchitecture.Quarter(shells,map.BlockHeight,theme,0,art);try{CollectionAssert.AreEqual(tiers[0].vertices,again.vertices);CollectionAssert.AreEqual(tiers[0].triangles,again.triangles);}finally{UnityEngine.Object.DestroyImmediate(again);}
    }finally{foreach(var m in tiers)UnityEngine.Object.DestroyImmediate(m);}index++;
   }
   Assert.Less(normal,close);Assert.Less(far,normal);TestContext.WriteLine("CITY_LOD_TRIANGLES close="+close+" normal="+normal+" far="+far);
  }
 }
}
