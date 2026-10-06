using System;
using System.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;

namespace Spacewars.Tests.PlayMode
{
    public sealed class ThreeCrossingsMaterialTests
    {
        private static ThreeCrossingsMap Map()=>new ThreeCrossingsMap(JsonUtility.FromJson<ThreeCrossingsProfileData>(Resources.Load<TextAsset>("ThreeCrossingsProfile").text));
        [Test] public void ShippedRoadCorridorsStayOnAcceptedSupport()
        {
            var map=Map();var p=ThreeCrossingsSurfaceProfile.Load();
            Assert.AreEqual(3,map.Revision);
            ThreeCrossingsMaterials.ValidateRoads(p,map);
            ThreeCrossingsMaterials.ValidateRoads(new ThreeCrossingsSurfaceProfile(),map);
            var gameplay=PlayableProfile.Create(JsonUtility.FromJson<PlayableProfileData>(Resources.Load<TextAsset>("PlayableProfile").text),map);
            foreach(var mine in TerritoryRules.Sites(gameplay).Where(site=>site.Kind==PlayableBuildingKind.Mine))
                foreach(var road in ThreeCrossingsMaterials.Segments(p))
                {
                    double dx=road.B.X-road.A.X,dz=road.B.Z-road.A.Z,len=dx*dx+dz*dz;
                    double t=len>0?Math.Max(0,Math.Min(1,((mine.Position.X-road.A.X)*dx+(mine.Position.Z-road.A.Z)*dz)/len)):0;
                    double x=mine.Position.X-road.A.X-t*dx,z=mine.Position.Z-road.A.Z-t*dz;
                    Assert.Greater(Math.Sqrt(x*x+z*z),gameplay.MineFootprintRadius+p.roadWidth/2,"Road overlaps mine reserve "+mine.Id);
                }
            // Every base and every bridge belongs to the same connected road network.
            var edges=ThreeCrossingsMaterials.Segments(p).ToArray();
            var reachable=new System.Collections.Generic.List<NavPoint>{new NavPoint(-46,-46)};
            bool changed;do{changed=false;foreach(var e in edges)
            {
                bool a=reachable.Any(v=>v.X==e.A.X&&v.Z==e.A.Z),b=reachable.Any(v=>v.X==e.B.X&&v.Z==e.B.Z);
                if(a&&!b){reachable.Add(e.B);changed=true;}if(b&&!a){reachable.Add(e.A);changed=true;}
            }}while(changed);
            foreach(var target in new[]{new NavPoint(-46,46),new NavPoint(46,-46),new NavPoint(46,46),new NavPoint(19,0),new NavPoint(18,38),new NavPoint(18,-38)})
                Assert.IsTrue(reachable.Any(v=>v.X==target.X&&v.Z==target.Z),"Disconnected destination "+target.X+","+target.Z);
        }
        [Test] public void MetadataRejectsOutOfRangeAndNonFiniteValuesIncludingRoadCoordinates()
        {
            var p=ThreeCrossingsSurfaceProfile.Load();var fields=p.Parameters().ToArray();
            Assert.AreEqual(fields.Length,fields.Select(f=>f.Path).Distinct().Count());
            foreach(var f in fields)
            {
                Assert.IsNotEmpty(f.Group);Assert.IsNotEmpty(f.Label);Assert.IsNotEmpty(f.Description);Assert.IsNotEmpty(f.Unit);Assert.Greater(f.Step,0);
                float value=f.Read();
                foreach(float invalid in new[]{float.NaN,float.PositiveInfinity,f.Minimum-f.Step,f.Maximum+f.Step})
                {f.Write(invalid);Assert.Throws<ArgumentException>(()=>p.Validate(),f.Path);}
                f.Write(value);
            }
            p.Validate();
            p.roads[0].points[0].x=0;
            Assert.Throws<ArgumentException>(()=>ThreeCrossingsMaterials.ValidateRoads(p,Map()),"Road through water must fail");
        }
        [Test] public void SurfaceRolesUseExistingMeshesAndShareWorldSamplingAndFog()
        {
            var root=new GameObject("Material test");
            var profile=PlayableProfile.Create(JsonUtility.FromJson<PlayableProfileData>(Resources.Load<TextAsset>("PlayableProfile").text),Map());
            try{using(var world=new PlayableWorld(root.transform,profile))
            {
                Material shared=null;var block=new MaterialPropertyBlock();int ground=0,concrete=0,metal=0;
                foreach(var r in root.GetComponentsInChildren<MeshRenderer>())
                {
                    r.GetPropertyBlock(block);int role=(int)block.GetFloat("_SurfaceRole");if(role==0)continue;
                    if(shared==null)shared=r.sharedMaterial;Assert.AreSame(shared,r.sharedMaterial);
                    Assert.AreSame(world.Fog.Texture,r.sharedMaterial.GetTexture("_FogMask"));
                    if(r.name.EndsWith("-ramp"))Assert.AreEqual(2,role);
                    if(role==1)ground++;if(role==2)concrete++;if(role==3)metal++;
                    Assert.IsNull(r.GetComponent<Collider>());
                }
                Assert.AreEqual(2,ground);Assert.GreaterOrEqual(concrete,12);Assert.AreEqual(3,metal);
                var mask=(Texture2D)shared.GetTexture("_RoadMask");
                Assert.Greater(mask.GetPixelBilinear((float)(-19.0/128+.5),.5f).r,.9f);
                Assert.Less(mask.GetPixelBilinear(.5f,.65f).r,.01f);
                Assert.AreEqual("three-crossings-materials-v1@1",world.SurfaceRevision);
            }}finally{UnityEngine.Object.DestroyImmediate(root);}
        }
    }
}
