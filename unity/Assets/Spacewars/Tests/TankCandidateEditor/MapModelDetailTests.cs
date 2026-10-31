using System.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using UnityEngine;
using Object=UnityEngine.Object;
namespace Spacewars.Tests.EditMode
{
    public sealed class MapModelDetailTests
    {
        [TestCase("Wrecks")][TestCase("ThreeCrossings")][TestCase("WhiteMeridian")][TestCase("CrimsonGrove")][TestCase("MossHighland")][TestCase("BasaltCrown")][TestCase("Foundry")][TestCase("Campaign")][TestCase("AiTest")][TestCase("ProvingGround")][TestCase("Diagnostic")][TestCase("Nereida")]
        public void PerMapCatalogHasValidReducedGeometryAndCompleteMaterialSlots(string key)
        {
            var catalog=Resources.Load<ModelDetailCatalog>("ModelDetail/"+key);Assert.NotNull(catalog);
            Assert.AreEqual(catalog.entries.Length,catalog.entries.Select(e=>e.hash).Distinct().Count());
            if(key=="ProvingGround"||key=="Diagnostic")Assert.Greater(catalog.exemptions.Length,0,"Simple cube scenery needs an explicit measured exception");
            foreach(var entry in catalog.exemptions){Assert.AreEqual(64,entry.hash.Length);Assert.IsNotEmpty(entry.reason);Assert.Greater(entry.indices,0);}
            foreach(var entry in catalog.entries){
                Assert.NotNull(entry.normal);Assert.NotNull(entry.far);Assert.AreEqual(entry.submeshes,entry.normal.subMeshCount);Assert.AreEqual(entry.submeshes,entry.far.subMeshCount);
                Assert.Greater(entry.indices,0);Assert.LessOrEqual(entry.normal.triangles.Length,entry.indices);Assert.LessOrEqual(entry.far.triangles.Length,entry.normal.triangles.Length);
                foreach(var mesh in new[]{entry.normal,entry.far}){Assert.AreEqual(mesh.vertexCount,mesh.normals.Length);Assert.True(mesh.triangles.All(i=>i>=0&&i<mesh.vertexCount));Assert.True(mesh.vertices.Concat(mesh.normals).All(v=>!float.IsNaN(v.x)&&!float.IsInfinity(v.x)&&!float.IsNaN(v.y)&&!float.IsInfinity(v.y)&&!float.IsNaN(v.z)&&!float.IsInfinity(v.z)));}
                if(key!="Wrecks")Assert.Greater(entry.compactBounds.Length,0);
            }
        }
        [Test] public void CompactSelectionDoesNotTreatAWholeMapBatchAsOneScreenFillingProp()
        {
            var obj=GameObject.CreatePrimitive(PrimitiveType.Cube);var cameraObject=new GameObject("Compact scenery camera");var mesh=Object.Instantiate(obj.GetComponent<MeshFilter>().sharedMesh);
            try{
                mesh.bounds=new Bounds(Vector3.zero,Vector3.one*200);obj.GetComponent<MeshFilter>().sharedMesh=mesh;var renderer=obj.GetComponent<MeshRenderer>();var bounds=renderer.localBounds;
                var detail=obj.AddComponent<ModelDetailLod>();detail.Configure(new Renderer[]{renderer},new[]{mesh},new[]{mesh},new[]{mesh},mesh.bounds);
                detail.ConfigureSelectionBounds(new[]{new Bounds(new Vector3(-50,0,0),Vector3.one),new Bounds(new Vector3(50,0,0),Vector3.one)});
                var camera=cameraObject.AddComponent<Camera>();camera.orthographic=true;camera.orthographicSize=10;camera.transform.position=new Vector3(0,30,-30);camera.transform.LookAt(Vector3.zero);
                Assert.That(detail.ProjectedScreenRatio(camera),Is.EqualTo(.05f).Within(.00001f));detail.SelectScreenRatio(.05f,ModelDetailProfile.Load());Assert.AreEqual(2,detail.Level);
                Assert.AreEqual(bounds,renderer.localBounds);detail.SetDetail(0);Assert.AreSame(mesh,obj.GetComponent<MeshFilter>().sharedMesh);Assert.AreEqual(bounds,renderer.localBounds);
            }finally{Object.DestroyImmediate(obj);Object.DestroyImmediate(cameraObject);Object.DestroyImmediate(mesh);}
        }
    }
}
