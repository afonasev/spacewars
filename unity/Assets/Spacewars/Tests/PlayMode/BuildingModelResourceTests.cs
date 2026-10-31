using System.Linq;
using NUnit.Framework;
using UnityEngine;

public sealed class BuildingModelResourceTests
{
    [TestCase("factory-armored",25,20648)]
    [TestCase("mine-armored",17,27788)]
    public void ConsolidatedBuildingsKeepReadableGeometryAndIndependentAnimationRoots(string key,int renderers,int triangles)
    {
        var model=Resources.Load<GameObject>("StyleA/"+key);
        Assert.NotNull(model);
        Assert.AreEqual(renderers,model.GetComponentsInChildren<MeshRenderer>(true).Length);
        var meshes=model.GetComponentsInChildren<MeshFilter>(true).Select(f=>f.sharedMesh).ToArray();
        Assert.IsTrue(meshes.All(m=>m&&m.isReadable&&m.vertexCount>0));
        Assert.AreEqual(triangles,meshes.Sum(m=>m.triangles.Length/3));
        Assert.IsEmpty(model.GetComponentsInChildren<Collider>(true));
        var nodes=model.GetComponentsInChildren<Transform>(true);
        if(key=="factory-armored")
        {
            foreach(string fan in new[]{"fan0","fan1"})
                Assert.AreEqual(1,nodes.Single(t=>t.name==fan).GetComponentsInChildren<MeshRenderer>(true).Length);
            Assert.AreEqual(3,model.GetComponentsInChildren<MeshRenderer>(true).Count(r=>r.name.StartsWith("workGlow")));
            for(int i=0;i<6;i++)Assert.AreEqual(1,nodes.Count(t=>t.name=="doorSection"+i));
        }
        else
        {
            foreach(string name in new[]{"rockerPivot","drillSlider","hydraulicBarrel0","hydraulicBarrel1","hydraulicRod0","hydraulicRod1","dustSocket"})Assert.AreEqual(1,nodes.Count(t=>t.name==name));
            for(int i=0;i<10;i++)Assert.AreEqual(1,nodes.Count(t=>t.name=="perimeterModule"+i),"Named editable grouping retained");
        }
    }
}
