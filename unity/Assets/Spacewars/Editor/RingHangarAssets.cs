using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Spacewars.Editor
{
    // Authored geometry dimensions, panel counts and iris shape are art invariants.
    // Gameplay footprint and rendered size are governed by the Hangar profile fields.
    public static class RingHangarAssets
    {
        const string Output="Assets/Spacewars/Content/Resources/StyleA/";
        static readonly List<CombineInstance>[] parts={new(),new(),new(),new()};
        static readonly List<Mesh> temporary=new();
        static readonly Dictionary<string,CombineInstance[][]> movable=new();
        static void Stash(string name,int[] starts){var rows=new CombineInstance[parts.Length][];for(int i=0;i<parts.Length;i++){rows[i]=parts[i].Skip(starts[i]).ToArray();parts[i].RemoveRange(starts[i],parts[i].Count-starts[i]);}movable.Add(name,rows);}
        static void Shape(int role,Vector3[] vertices,int[] triangles)
        {
            // Split face vertices: sharing top/side normals makes annular segments look corrugated.
            var flat=new Vector3[triangles.Length];var indices=new int[triangles.Length];var uv=new Vector2[triangles.Length];
            for(int i=0;i<triangles.Length;i+=3){
                var n=Vector3.Cross(vertices[triangles[i+1]]-vertices[triangles[i]],vertices[triangles[i+2]]-vertices[triangles[i]]).normalized;
                for(int j=0;j<3;j++){var v=vertices[triangles[i+j]];flat[i+j]=v;indices[i+j]=i+j;uv[i+j]=Mathf.Abs(n.y)>.5f?new Vector2(v.x,v.z):new Vector2(v.x+v.z,v.y);}
            }
            var mesh=new Mesh {vertices=flat,triangles=indices,uv=uv};mesh.RecalculateNormals();mesh.RecalculateTangents();mesh.RecalculateBounds();temporary.Add(mesh);
            parts[role].Add(new CombineInstance {mesh=mesh,transform=Matrix4x4.identity});
        }
        static Vector3 V(float r,float y,float a)=>new(r*Mathf.Cos(a),y,r*Mathf.Sin(a));
        static void Ring(int role,float inner,float outer,float bottom,float top,int count=64,float start=0,float sweep=Mathf.PI*2,float taper=0)
        {
            for(int i=0;i<count;i++){
                float a=start+sweep*i/count,b=start+sweep*(i+1)/count;
                Shape(role,new[]{V(inner-taper,top,a),V(inner-taper,top,b),V(outer-taper,top,b),V(outer-taper,top,a),V(inner,bottom,a),V(inner,bottom,b),V(outer,bottom,b),V(outer,bottom,a)},
                    new[]{0,1,2,0,2,3,3,2,6,3,6,7,4,0,3,4,3,7,5,6,2,5,2,1,4,5,1,4,1,0,7,6,5,7,5,4});
            }
        }
        static void Box(int role,Vector3 center,Vector3 size,float yaw=0)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cube);var mesh=go.GetComponent<MeshFilter>().sharedMesh;
            parts[role].Add(new CombineInstance {mesh=mesh,transform=Matrix4x4.TRS(center,Quaternion.Euler(0,yaw,0),size)});UnityEngine.Object.DestroyImmediate(go);
        }
        [MenuItem("Spacewars/Assets/Build Ring Hangar")]
        public static void Build()
        {
            foreach(var list in parts)list.Clear();temporary.Clear();movable.Clear();Directory.CreateDirectory(Output);
            // Model fits ordinary factory envelope: diameter 2.8m, height 1.25m.
            Ring(3,0,1.4f,0,.12f,64);
            // Annular wall leaves the elevator shaft open down to the foundation.
            Ring(1,.94f,1.16f,.12f,1.5f,48);
            for(int i=0;i<12;i++){
                float a=i*Mathf.PI/6;
                Ring(0,1.19f,1.3f,.2f,1.38f,4,a+.025f,Mathf.PI/6-.05f,.12f);
                Ring(0,1.06f,1.18f,1.38f,1.52f,4,a+.025f,Mathf.PI/6-.05f,.05f);
                var radial=V(1.3f,.82f,a);
                Box(1,radial,new Vector3(.17f,1.36f,.11f),-a*Mathf.Rad2Deg);
                Box(2,V(1.366f,1.17f,a),new Vector3(.025f,.18f,.07f),-a*Mathf.Rad2Deg);
                if(i%2==0){
                    Box(1,V(1.29f,.22f,a),new Vector3(.22f,.22f,.26f),-a*Mathf.Rad2Deg);
                    for(int j=0;j<3;j++)Box(1,V(1.309f,.45f+j*.11f,a+.25f),new Vector3(.03f,.045f,.24f),-(a+.25f)*Mathf.Rad2Deg);
                }
            }
            Ring(1,.94f,1.13f,1.5f,1.68f);
            Ring(3,.965f,.99f,1.68f,1.7f);
            // Dark disk below separate curved armor petals keeps every seam legible.
            var lidStart=parts.Select(p=>p.Count).ToArray();Ring(1,0,.965f,1.5f,1.59f);Stash("irisBacking",lidStart);
            for(int blade=0;blade<8;blade++){
                var vertices=new List<Vector3>();var triangles=new List<int>();
                for(int j=0;j<=20;j++){
                    float r=.012f+.94f*j/20;
                    float a=blade*Mathf.PI/4+.8f*(1-r/.952f);
                    vertices.Add(V(r,1.615f,a+.012f));vertices.Add(V(r,1.615f,a+Mathf.PI/4-.012f));
                    if(j>0){int n=j*2;triangles.AddRange(new[]{n-2,n-1,n+1,n-2,n+1,n});}
                }
                var petalStart=parts.Select(p=>p.Count).ToArray();Shape(0,vertices.ToArray(),triangles.ToArray());Stash("roofPetal"+blade,petalStart);
                float angle=blade*Mathf.PI/4;
                Ring(1,1.0f,1.16f,1.68f,1.75f,2,angle-.06f,.12f);
                Ring(2,1.04f,1.1f,1.751f,1.754f,2,angle-.035f,.07f);
            }
            var root=new GameObject("hangar");var model=new GameObject("modelRoot");model.transform.SetParent(root.transform,false);
            string[] roles={"team-primary","structure","team-emissive","foundation-concrete"};
            var platformStart=parts.Select(p=>p.Count).ToArray();Ring(1,0,.88f,.13f,.19f);Stash("launchPlatform",platformStart);
            void Emit(string name,CombineInstance[][] rows){
                var parent=model.transform;if(name!="body"){var node=new GameObject(name);node.transform.SetParent(model.transform,false);parent=node.transform;}
                for(int i=0;i<rows.Length;i++){
                    if(rows[i].Length==0)continue;
                    var combined=new Mesh {name="hangar-"+name+"-"+roles[i]};combined.CombineMeshes(rows[i]);
                    var points=combined.vertices;for(int v=0;v<points.Length;v++)points[v].y*=1.25f/1.754f;combined.vertices=points;combined.RecalculateNormals();combined.RecalculateTangents();combined.RecalculateBounds();
                    string path=Output+combined.name+".asset";var old=AssetDatabase.LoadAssetAtPath<Mesh>(path);
                    if(old){EditorUtility.CopySerialized(combined,old);UnityEngine.Object.DestroyImmediate(combined);combined=old;}else AssetDatabase.CreateAsset(combined,path);
                    var go=new GameObject("hangar:"+roles[i]);go.transform.SetParent(parent,false);go.AddComponent<MeshFilter>().sharedMesh=combined;
                    var material=name=="launchPlatform"?PlatformMaterial():AssetDatabase.LoadAssetAtPath<Material>(Output+"factory-"+roles[i]+".mat");
                    if(!material)throw new InvalidOperationException("Missing shared Style A material "+roles[i]);
                    go.AddComponent<MeshRenderer>().sharedMaterial=material;
                }
            }
            Emit("body",parts.Select(p=>p.ToArray()).ToArray());foreach(var node in movable)Emit(node.Key,node.Value);
            PrefabUtility.SaveAsPrefabAsset(root,Output+"hangar.prefab");UnityEngine.Object.DestroyImmediate(root);
            foreach(var mesh in temporary)UnityEngine.Object.DestroyImmediate(mesh);
            Icon();AssetDatabase.SaveAssets();Debug.Log("RING_HANGAR_ASSETS_PASS");
        }
        static Material PlatformMaterial()
        {
            const string name="hangar-platform-neutral-metal";
            var material=AssetDatabase.LoadAssetAtPath<Material>(Output+name+".mat");
            if(!material){material=new Material(AssetDatabase.LoadAssetAtPath<Material>(Output+"factory-neutral-metal.mat"));AssetDatabase.CreateAsset(material,Output+name+".mat");}
            float gray=new Spacewars.Presentation.ActorMaterialProfile().hangarPlatformGray;
            material.name=name;material.SetColor("_BaseColor",new Color(gray,gray,gray,1));
            EditorUtility.SetDirty(material);return material;
        }
        static void Icon()
        {
            // Match Orbital icon language: flat pale silhouette, cyan accent, transparent negative space.
            const int size=256;var texture=new Texture2D(size,size,TextureFormat.RGBA32,false);var pixels=new Color[size*size];
            Color ink=new(.89f,.95f,.97f,1),cyan=new(.22f,.82f,.9f,1);
            for(int y=0;y<size;y++)for(int x=0;x<size;x++){
                float u=(x-127.5f)/112,v=(y-140)/65f;float r=Mathf.Sqrt(u*u+v*v);Color c=Color.clear;
                bool body=y>=48&&y<=140&&Mathf.Abs(u)<.98f;
                bool baseEllipse=u*u+Mathf.Pow((y-55)/28f,2)<1.12f;
                if(body||baseEllipse)c=ink;
                if(y>140&&r>1)c=Color.clear;
                if(r<1)c=ink;
                if(r>.76f&&r<.84f)c=Color.clear;
                if(r<.70f){float angle=Mathf.Atan2(v,u)-.8f*(1-r/.7f);float phase=Mathf.Repeat(angle,Mathf.PI/4);if(phase<.065f||r<.035f)c=Color.clear;}
                if(y<116&&y>70&&(Mathf.Abs(u)>.78f&&Mathf.Abs(u)<.84f))c=Color.clear;
                if(y>=63&&y<=69&&Mathf.Abs(u)<.35f)c=cyan;
                pixels[y*size+x]=c;
            }
            texture.SetPixels(pixels);texture.Apply();string path="Assets/Spacewars/Content/Resources/OrbitalIcons/hangar.png";File.WriteAllBytes(path,texture.EncodeToPNG());UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path);var importer=(TextureImporter)AssetImporter.GetAtPath(path);importer.alphaIsTransparency=true;importer.mipmapEnabled=false;importer.textureCompression=TextureImporterCompression.Uncompressed;importer.wrapMode=TextureWrapMode.Clamp;importer.SaveAndReimport();
        }
    }
}
