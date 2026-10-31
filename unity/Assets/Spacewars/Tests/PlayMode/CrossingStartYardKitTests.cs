using System;
using System.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;
namespace Spacewars.Tests.PlayMode
{
    public sealed class CrossingStartYardKitTests
    {
        [TestCase("HeavyTractorWreck")][TestCase("PerimeterHangar")][TestCase("PerimeterWorkshop")][TestCase("PerimeterAnnex")]
        [TestCase("CargoModule")][TestCase("ExtractionSkid")][TestCase("PumpNode")][TestCase("VentilationBlock")][TestCase("CableCabinet")]
        public void ReducedTiersKeepSilhouetteAndUseCompactProjectedSelection(string kind)
        {
            var meshes=Enumerable.Range(0,3).Select(t=>CrossingStartYardKit.Build(kind,t)).ToArray();
            var obj=new GameObject("Yard LOD test");var camera=new GameObject("Camera").AddComponent<Camera>();
            try
            {
                var bounds=meshes[0].bounds;
                foreach(var mesh in meshes){Assert.Less(mesh.bounds.size.magnitude,10);Assert.AreEqual(1,mesh.subMeshCount);Assert.True(mesh.vertices.All(v=>!float.IsNaN(v.x)&&v.y>=-.001f));Assert.Less((mesh.bounds.size-bounds.size).magnitude,.5f);}
                Assert.Less(meshes[1].triangles.Length,meshes[0].triangles.Length);
                Assert.Less(meshes[2].triangles.Length,meshes[1].triangles.Length);
                obj.AddComponent<MeshFilter>().sharedMesh=meshes[0];var renderer=obj.AddComponent<MeshRenderer>();var lod=obj.AddComponent<ModelDetailLod>();lod.Configure(new Renderer[]{renderer},new[]{meshes[0]},new[]{meshes[1]},new[]{meshes[2]},bounds);
                camera.orthographic=true;camera.transform.position=new Vector3(0,5,-20);camera.transform.LookAt(Vector3.zero);
                camera.orthographicSize=2;lod.Select(camera);Assert.AreEqual(0,lod.Level);
                camera.orthographicSize=100;lod.Select(camera);Assert.AreEqual(2,lod.Level);
                Assert.AreSame(meshes[2],obj.GetComponent<MeshFilter>().sharedMesh);Assert.AreEqual(bounds,lod.ReferenceBounds);
            }
            finally{foreach(var mesh in meshes)UnityEngine.Object.DestroyImmediate(mesh);UnityEngine.Object.DestroyImmediate(obj);UnityEngine.Object.DestroyImmediate(camera.gameObject);}
        }
        [Test] public void TractorMaterialBoundariesAndMetricFaceUvsSurviveEveryLod()
        {
            for(int tier=0;tier<3;tier++)
            {
                var mesh=CrossingStartYardKit.Build("HeavyTractorWreck",tier);
                try
                {
                    var ids=mesh.colors.Select(c=>Mathf.RoundToInt(c.a*3)).Distinct().OrderBy(x=>x).ToArray();
                    CollectionAssert.AreEqual(new[]{0,1,2,3},ids,"paint, steel, rubber, glass");
                    var uv=new System.Collections.Generic.List<Vector4>();mesh.GetUVs(0,uv);Assert.AreEqual(mesh.vertexCount,uv.Count);
                    foreach(var q in uv){Assert.Greater(q.z,0);Assert.Greater(q.w,0);Assert.That(q.x,Is.InRange(-.001f,q.z+.001f));Assert.That(q.y,Is.InRange(-.001f,q.w+.001f));}
                    var colors=mesh.colors;var triangles=mesh.triangles;
                    for(int i=0;i<triangles.Length;i+=3){Assert.AreEqual(colors[triangles[i]].a,colors[triangles[i+1]].a);Assert.AreEqual(colors[triangles[i]].a,colors[triangles[i+2]].a);}
                }
                finally{UnityEngine.Object.DestroyImmediate(mesh);}
            }
        }
        [Serializable] sealed class AssetCost { public string kind;public int[] vertices,triangles;public long[] bytes; }
        [Serializable] sealed class AssetCosts { public AssetCost[] models; }
        [Test] public void RecordOriginalAndThreeTierAssetCosts()
        {
            var output=Environment.GetEnvironmentVariable("CROSSINGS_YARD_EVIDENCE");Assert.IsNotEmpty(output);System.IO.Directory.CreateDirectory(output);
            var profile=ThreeCrossingsSurfaceProfile.Load();
            var kinds=new[]{"HeavyTractorWreck","PerimeterHangar","PerimeterWorkshop","PerimeterAnnex","CargoModule","ExtractionSkid","PumpNode","VentilationBlock","CableCabinet"};
            var rows=kinds.Select(kind=>
            {
                var old=kind=="HeavyTractorWreck"?HeavyTractorWreck.Build():kind.StartsWith("Perimeter")?CorporateOutpostModules.Compound(kind):CorporateOutpostModules.IsModule(kind)?CorporateOutpostModules.Build(kind,profile.mineUtilityScale):CorporateOutpostModules.Utility(kind);
                FinalProceduralMesh.IndexOwned(old);
                var meshes=new[]{old}.Concat(Enumerable.Range(0,3).Select(t=>CrossingStartYardKit.Build(kind,t))).ToArray();
                var cost=new AssetCost{kind=kind,vertices=meshes.Select(m=>m.vertexCount).ToArray(),triangles=meshes.Select(m=>m.triangles.Length/3).ToArray(),bytes=meshes.Select(m=>UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(m)).ToArray()};
                foreach(var mesh in meshes)UnityEngine.Object.DestroyImmediate(mesh);return cost;
            }).ToArray();
            System.IO.File.WriteAllText(System.IO.Path.Combine(output,"asset-costs.json"),JsonUtility.ToJson(new AssetCosts{models=rows},true));
        }
        [Test] public void ResourceMinesMoveSymmetricallyAndRetainReservedConstructionClearance()
        {
            var data=JsonUtility.FromJson<ThreeCrossingsProfileData>(Resources.Load<TextAsset>("ThreeCrossingsProfile").text);var map=new ThreeCrossingsMap(data);
            var profile=PlayableProfile.Create(JsonUtility.FromJson<PlayableProfileData>(Resources.Load<TextAsset>("PlayableProfile").text),map);
            var geometry=new NavGeometry(map.HalfExtent,map.MovementBlockers.ToArray(),map.Revision);var sites=map.ReservedSites(profile);
            foreach(var site in sites.Where(s=>s.Kind==PlayableBuildingKind.Mine))
            {
                Assert.True(geometry.IsFree(site.Position,profile.MineFootprintRadius*Math.Sqrt(2)+profile.TankCollisionRadius),"mine reserve "+site.Id);
                Assert.True(sites.Any(s=>s.Kind==site.Kind&&s.Position.X==-site.Position.X&&s.Position.Z==-site.Position.Z),"symmetric mine "+site.Id);
            }
            Assert.AreEqual(50,map.MineZ);Assert.AreEqual(1,data.platformHeight);Assert.AreEqual(.4,data.platformWallHeight);
        }
    }
}
