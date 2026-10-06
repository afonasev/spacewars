using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Spacewars.Editor;
using Spacewars.StyleA.Preview;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Spacewars.Tests.EditorAsset
{
    public sealed class StyleAMaterialImportTests
    {
        [Test] public void SavedStyleAEmissionSurvivesRepeatedForcedImport()
        {
            var rows=StyleAMaterialImportProject.Sources().rows;
            var paths=rows.Select(r=>r.path).Concat(new[] {
                "Assets/Spacewars/Content/Resources/StyleA/refineryUpgraded-foundation-concrete.mat",
                "Assets/Spacewars/Content/Resources/StyleA/refineryUpgraded-team-emissive.mat"
            }).ToArray();
            Assert.AreEqual(18,paths.Distinct().Count());
            var expected=paths.ToDictionary(path=>path,path=>{
                var material=AssetDatabase.LoadAssetAtPath<Material>(path);
                Assert.IsNotNull(material,path);
                var emission=material.GetColor("_EmissionColor");
                Assert.Greater(emission.maxColorComponent,0,path);
                return emission;
            });
            for(var pass=0;pass<2;pass++)
            {
                foreach(var path in paths)
                    AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceUpdate|ImportAssetOptions.ForceSynchronousImport);
                AssetDatabase.SaveAssets();
                foreach(var path in paths)
                {
                    var material=AssetDatabase.LoadAssetAtPath<Material>(path);
                    Assert.IsTrue((material.globalIlluminationFlags & MaterialGlobalIlluminationFlags.AnyEmissive)!=0,path+" pass "+pass);
                    Assert.IsTrue(material.IsKeywordEnabled("_EMISSION"),path+" pass "+pass);
                    Assert.AreEqual(expected[path],material.GetColor("_EmissionColor"),path+" pass "+pass);
                    StringAssert.Contains("m_LightmapFlags: "+(int)material.globalIlluminationFlags,File.ReadAllText(path));
                }
            }
        }
        [Test] public void ExactSixteenSourceMaterialsHaveValidEmissionRoleBindings()
        {
            var rows=StyleAMaterialImportProject.Sources().rows;
            Assert.AreEqual(16,rows.Length);Assert.AreEqual(16,rows.Select(r=>r.path).Distinct().Count());
            Assert.AreEqual(6,rows.Count(r=>r.name.EndsWith("foundation-concrete",StringComparison.Ordinal)));
            var used=StyleALodAuditProject.LoadPrefabs().SelectMany(p=>p.GetComponentsInChildren<Renderer>(true)).SelectMany(r=>r.sharedMaterials).Distinct().ToArray();
            foreach(var row in rows)
            {
                var material=AssetDatabase.LoadAssetAtPath<Material>(row.path);
                Assert.Contains(material,used,row.path);
                Assert.AreEqual("Universal Render Pipeline/Lit",material.shader.name);
                Assert.IsTrue(material.shader.keywordSpace.FindKeyword("_EMISSION").isValid);
                Assert.Greater(row.sourceEmissionColor.maxColorComponent,0);
                Assert.IsTrue(row.sourceEmission);
            }
        }
        [Test] public void ReconstructedClonesPreserveTexturesBaseColorAndSharedMaterialState()
        {
            foreach(var row in StyleAMaterialImportProject.Sources().rows)
            {
                var original=AssetDatabase.LoadAssetAtPath<Material>(row.path);
                var before=JsonUtility.ToJson(MaterialImportComparison.Read(original,row.name));
                var clone=MaterialImportComparison.Clone(original,row,true,true);
                try
                {
                    Assert.IsTrue(clone.IsKeywordEnabled("_EMISSION"));
                    Assert.AreEqual(original.GetColor("_BaseColor"),clone.GetColor("_BaseColor"));
                    Assert.AreEqual(original.GetColor("_EmissionColor"),clone.GetColor("_EmissionColor"));
                    foreach(var property in original.GetTexturePropertyNames()) Assert.AreSame(original.GetTexture(property),clone.GetTexture(property));
                    if(row.name.EndsWith("foundation-concrete",StringComparison.Ordinal))
                    {
                        // Unity Color property roundtrip may differ by one float ULP; shader-effective values must agree.
                        var actual=clone.GetColor("_Color");
                        for(var channel=0;channel<4;channel++) Assert.That(actual[channel],Is.EqualTo(row.sourceLegacyColor[channel]).Within(0.0000002f));
                    }
                    Assert.AreEqual(before,JsonUtility.ToJson(MaterialImportComparison.Read(original,row.name)));
                }
                finally { UnityEngine.Object.DestroyImmediate(clone); }
            }
        }
        [Test] public void LegacyOnlyLeavesEmissionKeywordAndAllOtherRolesUntouched()
        {
            foreach(var row in StyleAMaterialImportProject.Sources().rows)
            {
                var original=AssetDatabase.LoadAssetAtPath<Material>(row.path);var clone=MaterialImportComparison.Clone(original,row,false,true);
                try
                {
                    CollectionAssert.AreEquivalent(original.shaderKeywords,clone.shaderKeywords);
                    if(!row.name.EndsWith("foundation-concrete",StringComparison.Ordinal))Assert.AreEqual(original.GetColor("_Color"),clone.GetColor("_Color"));
                }
                finally { UnityEngine.Object.DestroyImmediate(clone); }
            }
        }
        [Test] public void MissingMaterialIsRejected()
        { Assert.Throws<ArgumentException>(()=>MaterialImportComparison.Clone(null,new MaterialSourceRow(),true,true)); }
    }
}
