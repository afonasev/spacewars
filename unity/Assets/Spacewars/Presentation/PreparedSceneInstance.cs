using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;
namespace Spacewars.Presentation
{
    public sealed class PreparedSceneInstance : IDisposable
    {
        public Mesh[] Meshes{get;private set;}public Material[] Materials{get;private set;}public Texture[] Textures{get;private set;}public Transform[] Nodes{get;private set;}
        public readonly List<ModelDetailLod> Lods=new List<ModelDetailLod>();readonly List<Object> owned=new List<Object>();bool disposed;
        public IEnumerator Restore(PreparedScene scene,Transform root,Material existingBase=null,Texture2D fog=null)
        {
            var budget=new LoadingFrameBudget();bool completed=false;Nodes=new Transform[scene.nodes.Length];Nodes[0]=root;
            try{
                Meshes=new Mesh[scene.meshes.Length];for(int i=0;i<Meshes.Length;i++){
                    var d=scene.meshes[i];var mesh=new Mesh{name=d.name,indexFormat=(IndexFormat)d.indexFormat};owned.Add(mesh);Meshes[i]=mesh;
                    if(d.attributes.Length>0)mesh.SetVertexBufferParams(d.vertices,d.attributes.Select(a=>new VertexAttributeDescriptor((VertexAttribute)a.attribute,(VertexAttributeFormat)a.format,a.dimension,a.stream)).ToArray());
                    for(int s=0;s<d.streams.Length;s++)mesh.SetVertexBufferData(d.streams[s],0,0,d.streams[s].Length,s,MeshUpdateFlags.DontRecalculateBounds);
                    int stride=d.indexFormat==(int)IndexFormat.UInt16?2:4;mesh.SetIndexBufferParams(d.indices.Length/stride,(IndexFormat)d.indexFormat);
                    if(stride==2){var indices=new ushort[d.indices.Length/2];Buffer.BlockCopy(d.indices,0,indices,0,d.indices.Length);mesh.SetIndexBufferData(indices,0,0,indices.Length);}else{var indices=new uint[d.indices.Length/4];Buffer.BlockCopy(d.indices,0,indices,0,d.indices.Length);mesh.SetIndexBufferData(indices,0,0,indices.Length);}
                    mesh.subMeshCount=d.submeshes.Length;for(int s=0;s<d.submeshes.Length;s++){var v=d.submeshes[s];mesh.SetSubMesh(s,new SubMeshDescriptor(v.start,v.count,(MeshTopology)v.topology){baseVertex=v.baseVertex,firstVertex=v.firstVertex,vertexCount=v.vertices,bounds=v.bounds},MeshUpdateFlags.DontRecalculateBounds);}mesh.bounds=d.bounds;
                    if(budget.Expired){yield return null;budget.Restart();}
                }
                Textures=new Texture[scene.textures.Length];for(int i=0;i<Textures.Length;i++){
                    var d=scene.textures[i];if(d.path=="$fog"){Textures[i]=fog;continue;}
                    if(!string.IsNullOrEmpty(d.path)){Textures[i]=PreparedMapCache.ResolveTexture(d);if(!Textures[i])throw new InvalidDataException("Missing prepared texture "+d.path);continue;}
                    var texture=new Texture2D(d.width,d.height,(TextureFormat)d.format,d.mips,d.linear){name=d.name,filterMode=(FilterMode)d.filter,wrapModeU=(TextureWrapMode)d.u,wrapModeV=(TextureWrapMode)d.v,wrapModeW=(TextureWrapMode)d.w,anisoLevel=d.aniso,mipMapBias=d.bias};owned.Add(texture);Textures[i]=texture;texture.LoadRawTextureData(d.pixels);texture.Apply(false,!d.readable);
                    if(budget.Expired){yield return null;budget.Restart();}
                }
                Materials=new Material[scene.materials.Length];for(int i=0;i<Materials.Length;i++){
                    var d=scene.materials[i];var shader=string.IsNullOrEmpty(d.path)?Shader.Find(d.shader):Resources.Load<Shader>(d.path);if(!shader)throw new InvalidDataException("Missing prepared shader "+d.shader);
                    var material=i==scene.baseMaterial&&existingBase?existingBase:new Material(shader);if(material!=existingBase)owned.Add(material);material.shader=shader;material.name=d.name;material.shaderKeywords=d.keywords??Array.Empty<string>();material.renderQueue=d.queue;material.globalIlluminationFlags=(MaterialGlobalIlluminationFlags)d.gi;material.enableInstancing=d.instancing;material.doubleSidedGI=d.doubleSided;
                    foreach(var p in d.properties??Array.Empty<PreparedProperty>()){if(p.kind==0)material.SetFloat(p.name,p.number);else if(p.kind==1)SetVectorExactly(material,p.name,p.vector);else if(p.kind==2){material.SetTexture(p.name,p.texture<0?null:Textures[p.texture]);material.SetTextureScale(p.name,p.scale);material.SetTextureOffset(p.name,p.offset);}else if(p.array?.Length>0)material.SetVectorArray(p.name,p.array);}Materials[i]=material;
                }
                MaterialPropertyBlock Block(PreparedProperty[] props){var block=new MaterialPropertyBlock();foreach(var p in props??Array.Empty<PreparedProperty>()){if(p.kind==0)block.SetFloat(p.name,p.number);else if(p.kind==1)block.SetVector(p.name,p.vector);else if(p.kind==2)block.SetTexture(p.name,p.texture<0?null:Textures[p.texture]);else if(p.array?.Length>0)block.SetVectorArray(p.name,p.array);}return block;}
                for(int i=1;i<Nodes.Length;i++){
                    var n=scene.nodes[i];var obj=new GameObject(n.name);var t=obj.transform;Nodes[i]=t;t.SetParent(Nodes[n.parent],false);t.localPosition=n.position;t.localRotation=n.rotation;t.localScale=n.scale;obj.layer=n.layer;
                    if(n.mesh>=0)obj.AddComponent<MeshFilter>().sharedMesh=Meshes[n.mesh];
                    if(n.hasText){var text=obj.AddComponent<TextMesh>();var d=n.text;text.text=d.text;text.anchor=(TextAnchor)d.anchor;text.alignment=(TextAlignment)d.alignment;text.fontSize=d.fontSize;text.characterSize=d.size;text.lineSpacing=d.lineSpacing;text.offsetZ=d.offset;text.richText=d.rich;text.color=d.color;}
                    else if(n.hasRenderer){var r=obj.AddComponent<MeshRenderer>();r.sharedMaterials=n.materials.Select(m=>m<0?null:Materials[m]).ToArray();r.shadowCastingMode=(ShadowCastingMode)n.shadow;r.receiveShadows=n.shadows;r.enabled=n.enabled;r.lightProbeUsage=(LightProbeUsage)n.probes;r.reflectionProbeUsage=(ReflectionProbeUsage)n.reflection;r.motionVectorGenerationMode=(MotionVectorGenerationMode)n.motion;r.sortingLayerID=n.sortLayer;r.sortingOrder=n.sortOrder;r.allowOcclusionWhenDynamic=n.occlusion;r.renderingLayerMask=n.renderingLayer;r.localBounds=n.rendererBounds;r.SetPropertyBlock(Block(n.block));if(n.slots!=null)for(int s=0;s<n.slots.Length;s++)if(n.slots[s]?.values?.Length>0)r.SetPropertyBlock(Block(n.slots[s].values),s);}
                    obj.SetActive(n.active);if(budget.Expired){yield return null;budget.Restart();}
                }
                for(int i=0;i<Nodes.Length;i++)if(scene.nodes[i].hasLod){var d=scene.nodes[i].lod;var obj=Nodes[i].gameObject;var lod=obj.AddComponent<ModelDetailLod>();lod.Configure(d.renderers.Select(n=>(Renderer)Nodes[n].GetComponent<MeshRenderer>()).ToArray(),d.full.Select(n=>Meshes[n]).ToArray(),d.normal.Select(n=>Meshes[n]).ToArray(),d.far.Select(n=>Meshes[n]).ToArray(),d.bounds);lod.ConfigureSelectionBounds(d.selection);if(d.catalog)lod.MarkCatalogBound();lod.SetDetail(d.level);Lods.Add(lod);if(d.hook==1)obj.AddComponent<CrossingSceneryDetail>().Bind(lod);else if(d.hook==2)obj.AddComponent<ModelDetailCameraBinding>().Bind(lod);}
                completed=true;
            }finally{if(!completed){Dispose();for(int i=1;i<Nodes.Length;i++)if(Nodes[i])Release(Nodes[i].gameObject);}}
        }
        static void SetVectorExactly(Material material,string name,Vector4 expected)
        {
            // Color uniforms undergo a native color-space round trip even via
            // SetVector/GetVector. Recover the exact observable original value.
            var input=expected;
            for(int attempt=0;attempt<32;attempt++){
                material.SetVector(name,input);var actual=material.GetVector(name);
                if(actual.Equals(expected))return;
                var next=input+(expected-actual);
                if(next.Equals(input))break;
                input=next;
            }
            throw new InvalidDataException("Prepared material vector cannot restore exactly: "+material.shader.name+"/"+name);
        }
        public void Dispose(){if(disposed)return;disposed=true;foreach(var obj in owned)if(obj)Release(obj);owned.Clear();}
        static void Release(Object value){if(Application.isPlaying)Object.Destroy(value);else Object.DestroyImmediate(value);}
    }
}
