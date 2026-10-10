using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Spacewars.Presentation;
namespace Spacewars.Tests.PlayMode {
public class SurfaceVariantTests {
[Test] public void SiblingFamiliesBindMipmappedResolutionAndCalibratedGains() {
var m=new Material(Resources.Load<Shader>("TerritoryFog"));
try {
foreach(bool foundry in new[]{false,true}) {
if(foundry)FoundrySurfaceProfile.Load().Apply(m);else EnvironmentArt.Apply(m,EnvironmentArtProfile.Load());
foreach(var role in new[]{"Earth","Rock","Concrete","Steel"}) {
var textures=new[]{"","B","C"}.Select(s=>(Texture2D)m.GetTexture("_"+role+"Tex"+s)).ToArray();
Assert.AreEqual(3,textures.Distinct().Count());
foreach(var t in textures) {Assert.IsNotNull(t);Assert.AreEqual(1024,t.width);Assert.AreEqual(1024,t.height);Assert.AreNotEqual(TextureFormat.RGBA32,t.format);Assert.AreNotEqual(TextureFormat.RGB24,t.format);Assert.Greater(t.mipmapCount,1);Assert.AreEqual(TextureWrapMode.Repeat,t.wrapMode);Assert.AreEqual(FilterMode.Trilinear,t.filterMode);Assert.GreaterOrEqual(t.anisoLevel,8);}
var gain=m.GetVector("_"+role+"VariantGain");Assert.AreEqual(1,gain.x);Assert.Greater(gain.y,0);Assert.Greater(gain.z,0);Assert.Greater(gain.w,0);
}
Assert.Greater(m.GetVector("_TextureVariants").x,m.GetVector("_ArtTiles").x);
}
}finally{UnityEngine.Object.DestroyImmediate(m);}
}
[Test] public void FoundryParametersValidateSiblingAndContrastRanges() {
var p=FoundrySurfaceProfile.Load();
foreach(var f in p.Parameters()) {
float original=f.Read();Assert.IsNotEmpty(f.Description);Assert.Greater(f.Step,0);
foreach(var invalid in new[]{float.NaN,float.PositiveInfinity,f.Minimum-f.Step,f.Maximum+f.Step}) {f.Write(invalid);Assert.Throws<ArgumentException>(()=>p.Validate(),f.Path);}f.Write(original);
}p.Validate();
}
}}
