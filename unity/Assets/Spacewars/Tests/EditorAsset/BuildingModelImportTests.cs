using System.Linq;
using NUnit.Framework;
using Spacewars.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

namespace Spacewars.Tests.EditorAsset
{
    public sealed class BuildingModelImportTests
    {
        const string Folder="Assets/BuildingModelOptimizationTest";
        [TestCase(false)]
        [TestCase(true)]
        public void SameMaterialGroupsHaveDistinctSavedMeshesForDifferentLayoutsOrRendererSettings(bool settings)
        {
            Assert.False(AssetDatabase.IsValidFolder(Folder),"Do not touch an existing foreign test folder");
            AssetDatabase.CreateFolder("Assets","BuildingModelOptimizationTest");
            var model=new GameObject("fixture");var body=new GameObject("modelRoot");body.transform.SetParent(model.transform);
            var material=new Material(Shader.Find("Universal Render Pipeline/Lit")){name="same-material"};
            AssetDatabase.CreateAsset(material,Folder+"/fixture.mat");
            var meshes=new Mesh[4];
            try
            {
                for(int i=0;i<4;i++)
                {
                    var parent=new GameObject("perimeterModule"+i);parent.transform.SetParent(body.transform);
                    var go=new GameObject("surface"+i);go.transform.SetParent(parent.transform);go.transform.localPosition=Vector3.right*i;
                    var mesh=new Mesh{vertices=new[]{Vector3.zero,Vector3.forward,Vector3.right},normals=new[]{Vector3.up,Vector3.up,Vector3.up},triangles=new[]{0,1,2},uv=new[]{Vector2.zero,Vector2.up,Vector2.right}};
                    mesh.tangents=Enumerable.Repeat(new Vector4(1,0,0,1),3).ToArray();mesh.colors=Enumerable.Repeat(new Color(.25f,.5f,.75f,1),3).ToArray();
                    if(!settings&&i>=2)mesh.uv2=new[]{Vector2.one,Vector2.up,Vector2.right};
                    mesh.SetUVs(3,Enumerable.Repeat(new Vector4(.1f,.2f,.3f,.4f),3).ToList());
                    meshes[i]=mesh;go.AddComponent<MeshFilter>().sharedMesh=mesh;var r=go.AddComponent<MeshRenderer>();r.sharedMaterial=material;
                    if(settings&&i>=2){r.shadowCastingMode=ShadowCastingMode.Off;r.receiveShadows=false;}
                }
                var expected=StaticBuildingMeshes.CombineMinePerimeter(model,Folder+"/");Assert.AreEqual(2,expected.Count);
                string path=Folder+"/fixture.prefab";PrefabUtility.SaveAsPrefabAsset(model,path);AssetDatabase.SaveAssets();AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
                var saved=AssetDatabase.LoadAssetAtPath<GameObject>(path);StaticBuildingMeshes.ValidateSaved(saved,expected);
                var filters=saved.GetComponentsInChildren<MeshFilter>();Assert.AreEqual(2,filters.Select(f=>f.sharedMesh).Distinct().Count());
                Assert.AreEqual(4,filters.Sum(f=>f.sharedMesh.triangles.Length/3));
            }
            finally{Object.DestroyImmediate(model);foreach(var mesh in meshes)if(mesh)Object.DestroyImmediate(mesh);AssetDatabase.DeleteAsset(Folder);}
        }
    }
}
