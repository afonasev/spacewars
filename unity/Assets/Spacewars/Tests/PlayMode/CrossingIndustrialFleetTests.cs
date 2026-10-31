using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;
namespace Spacewars.Tests.PlayMode
{
    public sealed class CrossingIndustrialFleetTests
    {
        [TestCase("AutonomousQuarryModule")][TestCase("ColonyTractor")][TestCase("WalkingLoader")]
        public void ThreeTiersPreserveContactBoundsMaterialUvsAndProjectedSelection(string kind)
        {
            var meshes=Enumerable.Range(0,3).Select(t=>CrossingIndustrialFleet.Build(kind,t)).ToArray();var obj=new GameObject(kind);
            try
            {
                foreach(var m in meshes)
                {
                    Assert.AreEqual(1,m.subMeshCount);Assert.Less(m.bounds.size.magnitude,10);
                    Assert.That(m.bounds.min.y,Is.InRange(-.001f,.15f));Assert.Less((m.bounds.size-meshes[0].bounds.size).magnitude,.15f);
                    Assert.True(m.vertices.All(v=>float.IsFinite(v.x)&&float.IsFinite(v.y)&&float.IsFinite(v.z)));
                    CollectionAssert.AreEquivalent(new[]{0,1,2,3},m.colors.Select(c=>Mathf.RoundToInt(c.a*3)).Distinct());
                    var uv=new List<Vector4>();m.GetUVs(0,uv);Assert.AreEqual(m.vertexCount,uv.Count);
                    foreach(var q in uv){Assert.Greater(q.z,0);Assert.Greater(q.w,0);Assert.That(q.x,Is.InRange(-.001f,q.z+.001f));Assert.That(q.y,Is.InRange(-.001f,q.w+.001f));}
                    var c=m.colors;var indices=m.triangles;for(int i=0;i<indices.Length;i+=3){Assert.AreEqual(c[indices[i]].a,c[indices[i+1]].a);Assert.AreEqual(c[indices[i]].a,c[indices[i+2]].a);}
                }
                Assert.Less(meshes[1].triangles.Length,meshes[0].triangles.Length*.8f);Assert.Less(meshes[2].triangles.Length,meshes[0].triangles.Length*.45f);
                obj.AddComponent<MeshFilter>().sharedMesh=meshes[0];var r=obj.AddComponent<MeshRenderer>();var lod=obj.AddComponent<ModelDetailLod>();lod.Configure(new Renderer[]{r},new[]{meshes[0]},new[]{meshes[1]},new[]{meshes[2]},meshes[0].bounds);
                var profile=ModelDetailProfile.Load();lod.SelectScreenRatio(.5f,profile);Assert.AreEqual(0,lod.Level);lod.SelectScreenRatio((profile.normalEnter+profile.farExit)/2,profile);Assert.AreEqual(1,lod.Level);lod.SelectScreenRatio(profile.farEnter*.5f,profile);Assert.AreEqual(2,lod.Level);Assert.AreSame(meshes[2],obj.GetComponent<MeshFilter>().sharedMesh);
                var output=Environment.GetEnvironmentVariable("CROSSINGS_YARD_EVIDENCE");System.IO.Directory.CreateDirectory(output);
                System.IO.File.WriteAllLines(System.IO.Path.Combine(output,kind+"-cost.csv"),new[]{"tier,vertices,triangles,mesh_bytes,submeshes"}.Concat(meshes.Select((m,t)=>t+","+m.vertexCount+","+m.triangles.Length/3+","+UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(m)+","+m.subMeshCount)));
            }
            finally{foreach(var m in meshes)UnityEngine.Object.DestroyImmediate(m);UnityEngine.Object.DestroyImmediate(obj);}
        }
        [Test] public void SixMachinesUseSymmetricPocketsOutsideRoadsSitesAndOtherScenery()
        {
            var map=new ThreeCrossingsMap(JsonUtility.FromJson<ThreeCrossingsProfileData>(Resources.Load<TextAsset>("ThreeCrossingsProfile").text));var gameplay=PlayableProfile.Create(JsonUtility.FromJson<PlayableProfileData>(Resources.Load<TextAsset>("PlayableProfile").text),map);var material=new Material(Resources.Load<Shader>("TerritoryFog"));
            try{using(var surfaces=new ThreeCrossingsMaterials(ThreeCrossingsSurfaceProfile.Load(),map,material,gameplay))
            {
                var plan=ThreeCrossingsDetails.Plan(map,gameplay,surfaces);var fleet=plan.Where(p=>CrossingIndustrialFleet.IsVehicle(p.Kind)).ToArray();Assert.AreEqual(6,fleet.Length);Assert.AreEqual(0,plan.Count(p=>p.Kind=="HeavyTractorWreck"));
                foreach(var kind in CrossingIndustrialFleet.Kinds)
                {
                    var pair=fleet.Where(p=>p.Kind==kind).ToArray();Assert.AreEqual(2,pair.Length);Assert.Less((pair[0].Position+pair[1].Position).magnitude,.01f);Assert.AreEqual(180,Mathf.Abs(Mathf.DeltaAngle(pair[0].Rotation,pair[1].Rotation)),.001f);
                    var mesh=CrossingIndustrialFleet.Build(kind,0);try{foreach(var p in pair){Assert.True(ThreeCrossingsDetails.TractorFootprintClear(map,gameplay,surfaces,map.ReservedSites(gameplay).ToArray(),mesh.bounds,p.Position.x,p.Position.z,p.Rotation,p.Scale),kind);Assert.False(surfaces.AccessOccupied(new Vector2(p.Position.x,p.Position.z),p.Radius),"hangar access");}}finally{UnityEngine.Object.DestroyImmediate(mesh);}
                }
                var output=Environment.GetEnvironmentVariable("CROSSINGS_YARD_EVIDENCE");System.IO.Directory.CreateDirectory(output);System.IO.File.WriteAllLines(System.IO.Path.Combine(output,"placements.csv"),new[]{"kind,x,y,z,yaw,radius"}.Concat(fleet.Select(p=>p.Kind+","+p.Position.x+","+p.Position.y+","+p.Position.z+","+p.Rotation+","+p.Radius)));
            }}finally{UnityEngine.Object.DestroyImmediate(material);}
        }
    }
}
