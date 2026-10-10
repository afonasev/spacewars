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
        private static PlayableProfile Gameplay(ThreeCrossingsMap map)=>PlayableProfile.Create(JsonUtility.FromJson<PlayableProfileData>(Resources.Load<TextAsset>("PlayableProfile").text),map);
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
                    if(role==1)ground++;if(role==2)concrete++;if(role==3&&!r.name.StartsWith("Detail —"))metal++;
                    Assert.IsNull(r.GetComponent<Collider>());
                }
                Assert.AreEqual(2,ground);Assert.GreaterOrEqual(concrete,24);Assert.AreEqual(9,metal); // Three decks plus six rails.
                var mask=(Texture2D)shared.GetTexture("_RoadMask");
                Assert.Greater(mask.GetPixelBilinear((float)(-19.0/128+.5),.5f).r,.9f);
                Assert.Less(mask.GetPixelBilinear(.5f,.65f).r,.01f);
                Assert.AreEqual("three-crossings-materials-v1@4",world.SurfaceRevision);
                Assert.AreEqual("natural-frontier-v1@7",world.EnvironmentRevision);
            }}finally{UnityEngine.Object.DestroyImmediate(root);}
        }
        [Test] public void ExpandedRoadPaintStopsAtSolidsAndFillsBridgeContacts()
        {
            var map=Map();var material=new Material(Resources.Load<Shader>("TerritoryFog"));
            try{using(var surfaces=new ThreeCrossingsMaterials(ThreeCrossingsSurfaceProfile.Load(),map,material,Gameplay(map)))
            {
                var pixels=surfaces.RoadMask.GetPixels();int size=ThreeCrossingsMaterials.MaskSize;
                var gameplay=Gameplay(map);var mines=TerritoryRules.Sites(gameplay).Where(s=>s.Kind==PlayableBuildingKind.Mine).ToArray();
                for(int y=0;y<size;y++)for(int x=0;x<size;x++)
                {
                    var p=new NavPoint(((x+.5)/size*2-1)*map.HalfExtent,((y+.5)/size*2-1)*map.HalfExtent);
                    bool reserve=mines.Any(m=>Math.Pow(p.X-m.Position.X,2)+Math.Pow(p.Z-m.Position.Z,2)<=gameplay.MineFootprintRadius*gameplay.MineFootprintRadius);
                    if(reserve||!map.SupportsFootprint(p,0))Assert.Less(pixels[y*size+x].r,.01f,"Paint on blocked/unsupported terrain");
                }
                foreach(var bridge in map.Supports.Where(s=>s.IsBridge))foreach(int side in new[]{-1,1})
                {
                    double x=side*(bridge.Bounds.MaxX+1),z=(bridge.Bounds.MinZ+bridge.Bounds.MaxZ)/2;
                    double half=(bridge.Bounds.MaxZ-bridge.Bounds.MinZ)/2;
                    foreach(double offset in new[]{-half+1,0,half-1})
                        Assert.Greater(surfaces.RoadMask.GetPixelBilinear((float)(x/128+.5),(float)((z+offset)/128+.5)).r,.7f,"Approach narrower than deck");
                }
            }}finally{UnityEngine.Object.DestroyImmediate(material);}
        }
        [Test] public void GeologicalDressingRemainsInsideAuthoritativeRockVolumes()
        {
            var root=new GameObject("Rock envelope test");var map=Map();
            var p=PlayableProfile.Create(JsonUtility.FromJson<PlayableProfileData>(Resources.Load<TextAsset>("PlayableProfile").text),map);
            try{using(var world=new PlayableWorld(root.transform,p))
            {
                var block=new MaterialPropertyBlock();int rocks=0;
                foreach(var r in root.GetComponentsInChildren<MeshRenderer>())
                {
                    r.GetPropertyBlock(block);if(block.GetFloat("_SurfaceRole")!=4||r.name.StartsWith("Detail —"))continue;rocks++;
                    var mesh=r.GetComponent<MeshFilter>().sharedMesh;Assert.Greater(mesh.vertexCount,100);
                    foreach(var v in mesh.vertices)
                        Assert.IsTrue(map.Solids.Any(s=>v.y>=s.Bottom-.001&&v.y<=s.Top+.001&&(s.Contains(new NavPoint(v.x,v.z))||s.BoundaryDistanceSquared(new NavPoint(v.x,v.z))<.000001)),"Dressing extends into a route or above cover");
                }
                Assert.Greater(rocks,0);
            }}finally{UnityEngine.Object.DestroyImmediate(root);}
        }
        [Test] public void DetailModulesProtectTravelBuildSitesAndShareFog()
        {
            var map=Map();var gameplay=Gameplay(map);var material=new Material(Resources.Load<Shader>("TerritoryFog"));
            try{using(var surfaces=new ThreeCrossingsMaterials(ThreeCrossingsSurfaceProfile.Load(),map,material,gameplay))
            {
                var plan=ThreeCrossingsDetails.Plan(map,gameplay,surfaces);
                Assert.Greater(plan.Count(p=>p.Kind=="Boulder"),30,"Cliff clusters must actually populate");
                var stonesInPlan=plan.Where(p=>p.Kind=="Boulder").ToArray();
                Assert.Greater(stonesInPlan.Count(p=>p.Radius>=surfaces.Art.boulderRadius*.8f),5,"Selected feet need prominent fragments");
                Assert.Greater(stonesInPlan.Count(p=>stonesInPlan.Any(q=>q.Position!=p.Position&&Vector3.Distance(p.Position,q.Position)<p.Radius+q.Radius+1)),stonesInPlan.Length/2,"Most debris belongs to close groups");
                Assert.AreEqual(3,plan.Count(p=>p.Kind!="Boulder"),"All authored service anchors must resolve");
                foreach(var p in plan)
                {
                    var point=new NavPoint(p.Position.x,p.Position.z);
                    Assert.IsTrue(map.SupportsFootprint(point,p.Radius+surfaces.Art.detailClearance));
                    foreach(var site in TerritoryRules.Sites(gameplay))
                        Assert.Greater(Math.Sqrt(Math.Pow(point.X-site.Position.X,2)+Math.Pow(point.Z-site.Position.Z,2)),TerritoryRules.Radius(gameplay,site.Kind)+p.Radius);
                    foreach(bool stones in new[]{true,false})
                    {
                        if((p.Kind=="Boulder")!=stones)continue;
                        var mesh=ThreeCrossingsDetails.Build(new[]{p},surfaces.Art,stones);
                        foreach(var v in mesh.vertices)
                        {
                            Assert.IsTrue(map.SupportsFootprint(new NavPoint(v.x,v.z),0),"Module overhangs solid or water");
                            Assert.Less(surfaces.RoadMask.GetPixelBilinear(v.x/128+.5f,v.z/128+.5f).r,.02f,"Detail obscures route");
                        }
                        UnityEngine.Object.DestroyImmediate(mesh);
                    }
                }
                surfaces.BuildWeathering(map,plan,material);
                Assert.AreSame(surfaces.WeatheringMask,material.GetTexture("_WeatheringMask"));
                Assert.Greater(surfaces.WeatheringMask.GetPixels().Count(c=>c.r>.2f),100);
                Assert.Less(surfaces.WeatheringMask.GetPixelBilinear(.5f,.5f).r,.01f,"Bridge center remains exposed metal");
                int cleanEdge=0,dustyEdge=0;
                for(int i=0;i<100;i++)
                {
                    float x=Mathf.Lerp(-11.7f,11.7f,i/99f);
                    foreach(float z in new[]{-6.7f,6.7f})
                    {
                        float dust=surfaces.WeatheringMask.GetPixelBilinear(x/128+.5f,z/128+.5f).r;
                        if(dust<.03f)cleanEdge++;if(dust>.2f)dustyEdge++;
                    }
                }
                Assert.Greater(cleanEdge,40,"Deck perimeter must have substantial clean breaks");
                Assert.Greater(dustyEdge,4,"Separated deposits must still reach the deck");
            }}finally{UnityEngine.Object.DestroyImmediate(material);}
            var root=new GameObject("Detail fog test");
            try{using(var world=new PlayableWorld(root.transform,gameplay))
            {
                var details=root.GetComponentsInChildren<MeshRenderer>().Where(r=>r.name.StartsWith("Detail —")).ToArray();Assert.AreEqual(2,details.Length);
                foreach(var r in details){Assert.IsNull(r.GetComponent<Collider>());Assert.AreSame(world.Fog.Texture,r.sharedMaterial.GetTexture("_FogMask"));}
                world.InspectionHide();foreach(var r in details)Assert.AreSame(Texture2D.whiteTexture,r.sharedMaterial.GetTexture("_FogMask"));
            }}finally{UnityEngine.Object.DestroyImmediate(root);}
        }
        [Test] public void EnvironmentParametersRejectNonFiniteAndOutOfRangeValues()
        {
            var art=EnvironmentArtProfile.Load();var fields=art.Parameters().ToArray();
            Assert.AreEqual(fields.Length,fields.Select(f=>f.Path).Distinct().Count());
            foreach(var field in fields)
            {
                float original=field.Read();Assert.IsNotEmpty(field.Description);Assert.Greater(field.Step,0);
                foreach(float invalid in new[]{float.NaN,float.NegativeInfinity,field.Minimum-field.Step,field.Maximum+field.Step})
                {field.Write(invalid);Assert.Throws<ArgumentException>(()=>art.Validate(),field.Path);}
                field.Write(original);
            }
            art.Validate();
        }
        [Test] public void NaturalFootprintsExcludeConstructedPlatformsAndFadeOutsideRocks()
        {
            var map=Map();var material=new Material(Resources.Load<Shader>("TerritoryFog"));
            try{using(var surfaces=new ThreeCrossingsMaterials(ThreeCrossingsSurfaceProfile.Load(),map,material,Gameplay(map)))
            {
                float Sample(double x,double z)=>surfaces.RockMask.GetPixelBilinear((float)(x/(map.HalfExtent*2)+.5),(float)(z/(map.HalfExtent*2)+.5)).r*EnvironmentArt.ShoreDistanceRange;
                var rock=map.Solids.First(r=>r.Top>=map.WaterDepth&&r.Bottom<=0&&r.MaxX-r.MinX<20);
                Assert.Less(Sample((rock.MinX+rock.MaxX)/2,(rock.MinZ+rock.MaxZ)/2),.3f);
                Assert.Greater(Sample(-46,-46),surfaces.Art.naturalBlendWidth,"Base platform must not paint a rock apron");
                bool intermediate=false;
                foreach(var c in surfaces.RockMask.GetPixels())if(c.r>.02f&&c.r<surfaces.Art.naturalBlendWidth/EnvironmentArt.ShoreDistanceRange) {intermediate=true;break;}
                Assert.IsTrue(intermediate,"Natural contact mask needs a graded apron, not a binary boundary");
                Assert.AreSame(surfaces.RockMask,material.GetTexture("_RockMask"));
            }}finally{UnityEngine.Object.DestroyImmediate(material);}
        }
        [Test] public void WaterSharesFogAndUsesBankDistanceInsteadOfBridgeEdges()
        {
            var root=new GameObject("Environment test");var map=Map();
            var p=PlayableProfile.Create(JsonUtility.FromJson<PlayableProfileData>(Resources.Load<TextAsset>("PlayableProfile").text),map);
            try{using(var world=new PlayableWorld(root.transform,p))
            {
                var block=new MaterialPropertyBlock();int count=0;
                foreach(var r in root.GetComponentsInChildren<MeshRenderer>().Where(r=>r.name.StartsWith("River")))
                {
                    r.GetPropertyBlock(block);Assert.AreEqual(5,block.GetFloat("_SurfaceRole"));count++;
                    Assert.AreSame(world.Fog.Texture,r.sharedMaterial.GetTexture("_FogMask"));
                    Assert.IsNull(r.GetComponent<Collider>());
                    foreach(string key in new[]{"Earth","Rock","Concrete","Steel"})
                    {
                        var texture=(Texture2D)r.sharedMaterial.GetTexture("_"+key+"Tex");
                        Assert.IsNotNull(texture);Assert.Greater(texture.mipmapCount,1);Assert.AreEqual(TextureWrapMode.Repeat,texture.wrapMode);
                    }
                    var shore=(Texture2D)r.sharedMaterial.GetTexture("_ShoreMask");
                    Assert.Greater(shore.GetPixelBilinear(.5f,.5f).r,.9f,"Bridge must not create a false riverbank at channel center");
                    Assert.Greater(shore.GetPixelBilinear((float)(-34.5/128+.5),(float)(-46.0/128+.5)).r,.9f,"Inland platform edge is not a riverbank");
                    Assert.IsFalse(float.IsNaN(r.sharedMaterial.GetVector("_WaterMotion").x));
                }
                Assert.Greater(count,0);world.InspectionHide();
                foreach(var r in root.GetComponentsInChildren<MeshRenderer>().Where(r=>r.name.StartsWith("River")))
                    Assert.AreSame(Texture2D.whiteTexture,r.sharedMaterial.GetTexture("_FogMask"));
            }}finally{UnityEngine.Object.DestroyImmediate(root);}
        }
    }
}
