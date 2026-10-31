using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.Profiling;

namespace Spacewars.Tests.PlayMode
{
    public sealed class CrossingsHydroworksTests
    {
        static ThreeCrossingsMap Map()=>new ThreeCrossingsMap(JsonUtility.FromJson<ThreeCrossingsProfileData>(Resources.Load<TextAsset>("ThreeCrossingsProfile").text));
        static string Fingerprint(IEnumerable<ThreeCrossingsHydroworks.Part> parts)
        {
            // Per triangle stream includes every visual vertex attribute the builder emits.
            // Exact bit comparison also protects hard normals, colors, winding and spatial placement.
            var values=new List<string>();
            foreach(var p in parts)
            {
                var v=p.Mesh.vertices;var n=p.Mesh.normals;var c=p.Mesh.colors;var ix=p.Mesh.triangles;
                for(int i=0;i<ix.Length;i+=3)
                {
                    var bytes=new List<byte>();bytes.AddRange(BitConverter.GetBytes(p.Role));
                    for(int j=0;j<3;j++){int k=ix[i+j];foreach(float f in new[]{v[k].x,v[k].y,v[k].z,n[k].x,n[k].y,n[k].z,c[k].r,c[k].g,c[k].b,c[k].a})bytes.AddRange(BitConverter.GetBytes(f));}
                    values.Add(Convert.ToBase64String(bytes.ToArray()));
                }
            }
            values.Sort(StringComparer.Ordinal);using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(string.Join("\n",values)))).Replace("-","");
        }
        static long Buffers(Mesh m)=>m.vertexCount*Enumerable.Range(0,m.vertexBufferCount).Sum(i=>m.GetVertexBufferStride(i))+(long)m.triangles.Length*(m.indexFormat==UnityEngine.Rendering.IndexFormat.UInt16?2:4);
        static string Costs(List<ThreeCrossingsHydroworks.Part> parts)=>"meshes/renderers/base-pass-submissions="+parts.Count+" vertices="+parts.Sum(p=>p.Mesh.vertexCount)+" triangles="+parts.Sum(p=>p.Mesh.triangles.Length/3)+" meshBytes="+parts.Sum(p=>Profiler.GetRuntimeMemorySizeLong(p.Mesh))+" bufferBytes="+parts.Sum(p=>Buffers(p.Mesh))+" materials=1 newTextures=0";
        static void Release(IEnumerable<ThreeCrossingsHydroworks.Part> parts){foreach(var p in parts)UnityEngine.Object.DestroyImmediate(p.Mesh);}
        [Test] public void BatchingAndIndexingPreserveEveryDetailedTriangle()
        {
            var map=Map();var profile=ThreeCrossingsSurfaceProfile.Load();var raw=ThreeCrossingsHydroworks.Build(map,profile,false);var optimized=ThreeCrossingsHydroworks.Build(map,profile,true);
            try
            {
                string before=Fingerprint(raw),after=Fingerprint(optimized);Assert.AreEqual(before,after,"No visual geometry may be sacrificed for optimization");
                Assert.Less(optimized.Count,raw.Count/10);Assert.Less(optimized.Sum(p=>p.Mesh.vertexCount),raw.Sum(p=>p.Mesh.vertexCount));
                var output=Environment.GetEnvironmentVariable("SPACEWARS_HYDRO_EVIDENCE");if(!string.IsNullOrEmpty(output)){Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,"optimization.txt"),"Same detailed candidate source, identical settings, exact visual triangle attributes.\nRAW "+Costs(raw)+"\nOPT "+Costs(optimized)+"\nsha256="+after+"\nCounts are potential base-pass submissions, not actual driver draws/FPS.\n");}
            }
            finally{Release(raw);Release(optimized);}
        }
        [Test] public void AboveGroundDetailsKeepAllBridgeClearBandsOpen()
        {
            var map=Map();var parts=ThreeCrossingsHydroworks.Build(map,ThreeCrossingsSurfaceProfile.Load());
            try
            {
                foreach(var part in parts)foreach(var v in part.Mesh.vertices)
                {
                    Assert.IsFalse(float.IsNaN(v.x)||float.IsNaN(v.y)||float.IsNaN(v.z));
                    if(v.y<=.01f)continue;
                    foreach(var bridge in map.Supports.Where(s=>s.IsBridge))Assert.IsFalse(v.x>bridge.Bounds.MinX&&v.x<bridge.Bounds.MaxX&&v.z>bridge.Bounds.MinZ&&v.z<bridge.Bounds.MaxZ,"Fitting intrudes into full clear bridge band: "+part.Name+" "+v);
                }
                Assert.IsTrue(parts.Any(p=>p.Role==5),"Real falling-water geometry required");Assert.IsTrue(parts.Any(p=>p.Role==4),"Quarry shelves required");
            }
            finally{Release(parts);}
        }
        [Test] public void HydroControlsAreNamedRangeValidatedAndDiscoverable()
        {
            var profile=ThreeCrossingsSurfaceProfile.Load();var fields=profile.Parameters().Where(p=>p.Path.Contains(".hydro")).ToArray();Assert.AreEqual(6,fields.Length);
            foreach(var f in fields){float current=f.Read();f.Write(f.Maximum+1);Assert.Throws<ArgumentException>(()=>profile.Validate(),f.Path);f.Write(current);}profile.Validate();
        }
    }
}
