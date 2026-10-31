using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Spacewars.Editor
{
    // Exact-layout Editor consolidation. Asset importers select motion roots;
    // named nodes and articulated parts remain independent.
    public static class StaticBuildingMeshes
    {
        static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
        static string Hash(byte[] bytes){using var hash=SHA256.Create();return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
        static string Layout(Mesh mesh)=>string.Join(";",mesh.GetVertexAttributes().Select(a=>$"{a.attribute}/{a.format}/{a.dimension}"));
        public static string Settings(MeshRenderer r)=>$"{r.enabled}/{r.shadowCastingMode}/{r.receiveShadows}/{r.lightProbeUsage}/{r.reflectionProbeUsage}/{r.renderingLayerMask}/{r.motionVectorGenerationMode}/{r.allowOcclusionWhenDynamic}/{r.sortingLayerID}/{r.sortingOrder}/{r.lightmapIndex}/{r.realtimeLightmapIndex}/{r.receiveGI}";
        public static Dictionary<string,string> CombineMinePerimeter(GameObject model,string folder)
        {
            var body=model.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="modelRoot");
            var filters=model.GetComponentsInChildren<MeshFilter>(true).Where(f=>
                f.transform.parent.name.StartsWith("perimeterModule",StringComparison.Ordinal)||
                f.transform.parent==body&&!f.name.StartsWith("hydraulic",StringComparison.Ordinal)).ToArray();
            return CombineUnder(body,filters,folder,"mine-armored-static-");
        }
        public static Dictionary<string,string> CombineUnder(Transform body,MeshFilter[] filters,string folder,string prefix)
        {
            var expected=new Dictionary<string,string>();
            foreach(var group in filters.GroupBy(f=>new{Material=f.GetComponent<MeshRenderer>().sharedMaterial,Layout=Layout(f.sharedMesh),Settings=Settings(f.GetComponent<MeshRenderer>())}))
            {
                var sources=group.ToArray();if(sources.Length==1)continue;
                Require(sources.All(f=>f.GetComponent<MeshRenderer>().sharedMaterials.Length==1&&!f.GetComponent<MeshRenderer>().HasPropertyBlock()),"Expected static single-material surfaces without property overrides");
                string materialId=AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(group.Key.Material));
                string suffix=Hash(Encoding.UTF8.GetBytes(materialId+"/"+group.Key.Material.name+"/"+group.Key.Layout+"/"+group.Key.Settings)).Substring(0,16);
                string name=prefix+group.Key.Material.name+"-"+suffix;
                Require(!expected.ContainsKey(name),"Duplicate full grouping key output: "+name);
                var instances=sources.Select(f=>new CombineInstance{mesh=f.sharedMesh,transform=body.worldToLocalMatrix*f.transform.localToWorldMatrix}).ToArray();
                var combined=new Mesh{name=name};combined.CombineMeshes(instances,true,true,false);
                ValidateCombined(combined,instances);
                expected.Add(name,Fingerprint(combined)+"/"+group.Key.Settings+"/"+AssetDatabase.GetAssetPath(group.Key.Material));
                string path=folder+name+".asset";var saved=AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if(saved){EditorUtility.CopySerialized(combined,saved);Object.DestroyImmediate(combined);combined=saved;}else AssetDatabase.CreateAsset(combined,path);
                var obj=new GameObject(name);obj.transform.SetParent(body,false);obj.AddComponent<MeshFilter>().sharedMesh=combined;
                var renderer=obj.AddComponent<MeshRenderer>();var original=sources[0].GetComponent<MeshRenderer>();
                renderer.sharedMaterial=group.Key.Material;renderer.enabled=original.enabled;renderer.shadowCastingMode=original.shadowCastingMode;renderer.receiveShadows=original.receiveShadows;
                renderer.lightProbeUsage=original.lightProbeUsage;renderer.reflectionProbeUsage=original.reflectionProbeUsage;renderer.renderingLayerMask=original.renderingLayerMask;
                renderer.motionVectorGenerationMode=original.motionVectorGenerationMode;renderer.allowOcclusionWhenDynamic=original.allowOcclusionWhenDynamic;
                renderer.sortingLayerID=original.sortingLayerID;renderer.sortingOrder=original.sortingOrder;renderer.lightmapIndex=original.lightmapIndex;renderer.realtimeLightmapIndex=original.realtimeLightmapIndex;renderer.receiveGI=original.receiveGI;
                Require(Settings(renderer)==group.Key.Settings,"Renderer settings changed");
                foreach(var source in sources){Object.DestroyImmediate(source.GetComponent<MeshRenderer>());Object.DestroyImmediate(source);}
            }
            return expected;
        }
        static void ValidateCombined(Mesh combined,CombineInstance[] parts)
        {
            int offset=0,indexOffset=0;var vertices=combined.vertices;var normals=combined.normals;var tangents=combined.tangents;var colors=combined.colors;var indices=combined.triangles;
            var uv=Enumerable.Range(0,8).Select(channel=>{var values=new List<Vector4>();combined.GetUVs(channel,values);return values;}).ToArray();
            foreach(var part in parts)
            {
                var mesh=part.mesh;var points=mesh.vertices;var ns=mesh.normals;var ts=mesh.tangents;var cs=mesh.colors;
                Require(Layout(mesh)==Layout(combined),"Vertex layout changed");
                for(int i=0;i<points.Length;i++)
                {
                    Require((vertices[offset+i]-part.transform.MultiplyPoint3x4(points[i])).sqrMagnitude<1e-12f,"Static vertex changed");
                    Require((normals[offset+i]-part.transform.inverse.transpose.MultiplyVector(ns[i]).normalized).sqrMagnitude<1e-10f,"Static normal changed");
                    if(ts.Length>0)
                    {
                        var direction=part.transform.MultiplyVector(new Vector3(ts[i].x,ts[i].y,ts[i].z)).normalized;
                        var expected=new Vector4(direction.x,direction.y,direction.z,ts[i].w*(part.transform.determinant<0?-1:1));
                        Require((tangents[offset+i]-expected).sqrMagnitude<1e-10f,"Static tangent changed");
                    }
                    if(cs.Length>0)Require(colors[offset+i]==cs[i],"Static color changed");
                }
                for(int channel=0;channel<8;channel++)
                {
                    var source=new List<Vector4>();mesh.GetUVs(channel,source);
                    Require((source.Count==0)==(uv[channel].Count==0),"UV channel presence changed");
                    for(int i=0;i<source.Count;i++)Require(uv[channel][offset+i]==source[i],"Static UV changed: "+channel);
                }
                foreach(int index in mesh.triangles)Require(indices[indexOffset++]==offset+index,"Triangle/winding changed");
                offset+=points.Length;
            }
            Require(offset==combined.vertexCount&&indexOffset==indices.Length,"Geometry count changed");
        }
        // Compare the serialized/reloaded binding with the pre-save complete mesh,
        // detecting aliases, overwritten meshes and lost attributes on disk.
        public static void ValidateSaved(GameObject prefab,Dictionary<string,string> expected)
        {
            Require(prefab,"Saved prefab missing");
            foreach(var row in expected)
            {
                var filter=prefab.GetComponentsInChildren<MeshFilter>(true).Single(f=>f.name==row.Key);
                Require(Fingerprint(filter.sharedMesh)+"/"+Settings(filter.GetComponent<MeshRenderer>())+"/"+AssetDatabase.GetAssetPath(filter.GetComponent<MeshRenderer>().sharedMaterial)==row.Value,"Saved mesh/settings mismatch: "+row.Key);
            }
        }
        public static string Fingerprint(Mesh mesh)
        {
            using var stream=new MemoryStream();using var writer=new BinaryWriter(stream);
            writer.Write(Layout(mesh));writer.Write(mesh.vertexCount);writer.Write(mesh.subMeshCount);
            foreach(var v in mesh.vertices){writer.Write(v.x);writer.Write(v.y);writer.Write(v.z);}
            foreach(var v in mesh.normals){writer.Write(v.x);writer.Write(v.y);writer.Write(v.z);}
            foreach(var v in mesh.tangents){writer.Write(v.x);writer.Write(v.y);writer.Write(v.z);writer.Write(v.w);}
            foreach(var v in mesh.colors){writer.Write(v.r);writer.Write(v.g);writer.Write(v.b);writer.Write(v.a);}
            for(int channel=0;channel<8;channel++){var values=new List<Vector4>();mesh.GetUVs(channel,values);writer.Write(values.Count);foreach(var v in values){writer.Write(v.x);writer.Write(v.y);writer.Write(v.z);writer.Write(v.w);}}
            foreach(int index in mesh.triangles)writer.Write(index);writer.Flush();return Hash(stream.ToArray());
        }
    }
}
