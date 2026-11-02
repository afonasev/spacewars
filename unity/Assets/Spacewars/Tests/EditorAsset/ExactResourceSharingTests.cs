using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using NUnit.Framework;
using Spacewars.Editor;
using UnityEditor;
using UnityEngine;

namespace Spacewars.Tests.EditorAsset
{
    public sealed class ExactResourceSharingTests
    {
        [Serializable] sealed class Entry { public string source,target,sha256; }
        [Serializable] sealed class Catalog { public Entry[] aliases; }
        static Entry[] Entries()=>JsonUtility.FromJson<Catalog>(File.ReadAllText("Assets/Spacewars/Editor/ExactResourceAliases.json")).aliases;
        [Test] public void AllAliasesLoadExactCommittedResources()
        {
            foreach(var row in Entries()){
                Assert.IsFalse(File.Exists(row.source),row.source);
                Assert.AreEqual(row.target,ExactResourceAliases.Resolve(row.source));
                using var hash=SHA256.Create();
                Assert.AreEqual(row.sha256,BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(row.target))).Replace("-","").ToLowerInvariant(),row.target);
                var asset=AssetDatabase.LoadMainAssetAtPath(row.target);Assert.IsNotNull(asset,row.target);
                if(row.source.EndsWith(".png",StringComparison.Ordinal)){
                    Assert.IsInstanceOf<Texture2D>(asset);var importer=(TextureImporter)AssetImporter.GetAtPath(row.target);
                    Assert.IsFalse(importer.sRGBTexture);Assert.AreEqual(2048,importer.maxTextureSize);
                }else Assert.IsInstanceOf<Mesh>(asset);
            }
        }
        [Test] public void PackedMaskReworkDoesNotOverwriteSharedPixels()
        {
            var row=Entries().First(e=>e.source.EndsWith(".png",StringComparison.Ordinal));
            Assert.AreEqual(row.target,ExactResourceAliases.PackedMaskPath(row.source,File.ReadAllBytes(row.target)));
            Assert.AreEqual(row.source,ExactResourceAliases.PackedMaskPath(row.source,new byte[]{1,2,3}));
        }
        [Test] public void MeshRegenerationReusesSharedDataAndRetainsCompleteAttributes()
        {
            var save=typeof(ModelDetailAssets).GetMethod("SaveMesh",BindingFlags.NonPublic|BindingFlags.Static);
            foreach(var row in Entries().Where(e=>e.source.EndsWith(".asset",StringComparison.Ordinal))){
                var canonical=AssetDatabase.LoadAssetAtPath<Mesh>(row.target);
                var copy=UnityEngine.Object.Instantiate(canonical);
                Assert.AreSame(canonical,save.Invoke(null,new object[]{copy,row.source}),row.source);
                Assert.IsFalse(File.Exists(row.source),row.source);
            }
        }
    }
}
