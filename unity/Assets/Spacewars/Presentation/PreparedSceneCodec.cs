using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;
namespace Spacewars.Presentation
{
    public static partial class PreparedSceneCodec
    {
        sealed class FieldsResolver : DefaultContractResolver
        {
            protected override IList<JsonProperty> CreateProperties(Type type,MemberSerialization serialization)
            {
                if(type==typeof(Bounds))return base.CreateProperties(type,serialization).Where(p=>p.PropertyName=="center"||p.PropertyName=="extents").ToList();
                return type.GetFields(BindingFlags.Public|BindingFlags.Instance).Select(f=>base.CreateProperty(f,serialization)).ToList();
            }
        }
        static readonly JsonSerializerSettings metadataSettings=new JsonSerializerSettings{ContractResolver=new FieldsResolver()};
        // Persist public DTO fields with round-trip precision; exclude computed
        // Unity vector/bounds properties and retain null optional metadata.
        public static string Json(object value)=>JsonConvert.SerializeObject(value,metadataSettings);
        public static T FromJson<T>(string value)=>JsonConvert.DeserializeObject<T>(value,metadataSettings);
        public static PreparedScene Capture(Transform root,Material baseMaterial=null,Texture2D fog=null,Action<PreparedScene,Func<Texture,int>,Func<Material,int>> metadata=null)
        {
            var transforms=root.GetComponentsInChildren<Transform>(true);var ids=transforms.Select((t,i)=>(t,i)).ToDictionary(v=>v.t,v=>v.i);
            var meshes=new List<PreparedMesh>();var materials=new List<PreparedMaterial>();var textures=new List<PreparedTexture>();
            var meshIds=new Dictionary<Mesh,int>();var materialIds=new Dictionary<Material,int>();var textureIds=new Dictionary<Texture,int>();
            int TextureId(Texture value){
                if(!value)return -1;if(textureIds.TryGetValue(value,out int known))return known;
                var d=new PreparedTexture{name=value.name,path=value==fog?"$fog":PreparedMapCache.ResourcePath(value)};
                if(string.IsNullOrEmpty(d.path)){
                    if(!(value is Texture2D texture))throw new InvalidDataException("Unsupported generated texture "+value.name);
                    d.width=texture.width;d.height=texture.height;d.format=(int)texture.format;d.mips=texture.mipmapCount;d.filter=(int)texture.filterMode;d.u=(int)texture.wrapModeU;d.v=(int)texture.wrapModeV;d.w=(int)texture.wrapModeW;d.aniso=texture.anisoLevel;d.bias=texture.mipMapBias;d.readable=texture.isReadable;
                    d.linear=!UnityEngine.Experimental.Rendering.GraphicsFormatUtility.IsSRGBFormat(texture.graphicsFormat);d.pixels=PreparedMapCache.Pixels(texture);
                }
                int id=textures.Count;textures.Add(d);textureIds.Add(value,id);return id;
            }
            int MeshId(Mesh mesh){
                if(!mesh)return -1;if(meshIds.TryGetValue(mesh,out int known))return known;
                var d=new PreparedMesh{name=mesh.name,vertices=mesh.vertexCount,indexFormat=(int)mesh.indexFormat,bounds=mesh.bounds,
                    attributes=mesh.GetVertexAttributes().Select(a=>new PreparedAttribute{attribute=(int)a.attribute,format=(int)a.format,dimension=a.dimension,stream=a.stream}).ToArray(),
                    submeshes=Enumerable.Range(0,mesh.subMeshCount).Select(i=>{var s=mesh.GetSubMesh(i);return new PreparedSubmesh{start=s.indexStart,count=s.indexCount,topology=(int)s.topology,baseVertex=s.baseVertex,firstVertex=s.firstVertex,vertices=s.vertexCount,bounds=s.bounds};}).ToArray(),streams=new byte[mesh.vertexBufferCount][]};
                using(var data=Mesh.AcquireReadOnlyMeshData(mesh)){for(int i=0;i<d.streams.Length;i++)d.streams[i]=data[0].GetVertexData<byte>(i).ToArray();d.indices=data[0].GetIndexData<byte>().ToArray();}
                int id=meshes.Count;meshes.Add(d);meshIds.Add(mesh,id);return id;
            }
            Dictionary<string,int> Properties(Shader shader){
                var names=new Dictionary<string,int>();foreach(var p in PreparedMapCache.Schema.properties??Array.Empty<PreparedSchemaProperty>())names[p.name]=p.kind;
                for(int i=0;i<shader.GetPropertyCount();i++){int type=(int)shader.GetPropertyType(i);names[shader.GetPropertyName(i)]=type==4?2:type==0||type==1?1:0;}
                return names;
            }
            PreparedProperty[] Block(MaterialPropertyBlock block,Shader shader){
                if(block.isEmpty)return Array.Empty<PreparedProperty>();var list=new List<PreparedProperty>();
                foreach(var pair in Properties(shader))if(block.HasProperty(pair.Key)){
                    var p=new PreparedProperty{name=pair.Key,kind=pair.Value};
                    if(p.kind==0)p.number=block.GetFloat(p.name);else if(p.kind==1)p.vector=block.GetVector(p.name);else if(p.kind==2)p.texture=TextureId(block.GetTexture(p.name));else p.array=block.GetVectorArray(p.name);
                    list.Add(p);
                }
                return list.ToArray();
            }
            int MaterialId(Material material){
                if(!material)return -1;if(materialIds.TryGetValue(material,out int known))return known;
                var list=new List<PreparedProperty>();var names=new Dictionary<string,int>();
                for(int i=0;i<material.shader.GetPropertyCount();i++){int type=(int)material.shader.GetPropertyType(i);names[material.shader.GetPropertyName(i)]=type==4?2:type==0||type==1?1:0;}
                var extra=PreparedMapCache.Schema.shaders?.FirstOrDefault(s=>s.name==material.shader.name);
                if(extra!=null)foreach(var p in extra.properties)if(!names.ContainsKey(p.name))names[p.name]=p.kind;
                foreach(var pair in names){var p=new PreparedProperty{name=pair.Key,kind=pair.Value};
                    if(p.kind==0)p.number=material.GetFloat(p.name);else if(p.kind==1)p.vector=material.GetVector(p.name);else if(p.kind==2){p.texture=TextureId(material.GetTexture(p.name));p.scale=material.GetTextureScale(p.name);p.offset=material.GetTextureOffset(p.name);}else{p.array=material.GetVectorArray(p.name);if(p.array==null||p.array.Length==0)continue;}list.Add(p);
                }
                int id=materials.Count;materials.Add(new PreparedMaterial{name=material.name,shader=material.shader.name,path=PreparedMapCache.ResourcePath(material.shader),keywords=material.shaderKeywords,queue=material.renderQueue,gi=(int)material.globalIlluminationFlags,instancing=material.enableInstancing,doubleSided=material.doubleSidedGI,properties=list.ToArray()});materialIds.Add(material,id);return id;
            }
            int baseIndex=baseMaterial?MaterialId(baseMaterial):-1;
            var nodes=new PreparedNode[transforms.Length];
            for(int i=0;i<transforms.Length;i++){
                var t=transforms[i];
                if(i>0)foreach(var component in t.GetComponents<Component>())if(!(component is Transform||component is MeshFilter||component is MeshRenderer||component is Collider||component is ModelDetailLod||component is CrossingSceneryDetail||component is ModelDetailCameraBinding||component is TextMesh))throw new InvalidDataException("Unsupported prepared component "+component.GetType().Name);
                var n=new PreparedNode{name=t.name,parent=i==0?-1:ids[t.parent],layer=t.gameObject.layer,position=t.localPosition,rotation=t.localRotation,scale=t.localScale,active=t.gameObject.activeSelf};nodes[i]=n;
                var filter=t.GetComponent<MeshFilter>();if(filter)n.mesh=MeshId(filter.sharedMesh);
                var text=t.GetComponent<TextMesh>();if(text){n.hasText=true;n.text=new PreparedText{text=text.text,anchor=(int)text.anchor,alignment=(int)text.alignment,fontSize=text.fontSize,size=text.characterSize,lineSpacing=text.lineSpacing,offset=text.offsetZ,rich=text.richText,color=text.color};}
                var renderer=t.GetComponent<MeshRenderer>();if(renderer&&!text){n.hasRenderer=true;
                    n.materials=renderer.sharedMaterials.Select(MaterialId).ToArray();n.shadow=(int)renderer.shadowCastingMode;n.shadows=renderer.receiveShadows;n.enabled=renderer.enabled;n.probes=(int)renderer.lightProbeUsage;n.reflection=(int)renderer.reflectionProbeUsage;n.motion=(int)renderer.motionVectorGenerationMode;n.sortLayer=renderer.sortingLayerID;n.sortOrder=renderer.sortingOrder;n.occlusion=renderer.allowOcclusionWhenDynamic;n.renderingLayer=renderer.renderingLayerMask;n.rendererBounds=renderer.localBounds;
                    var block=new MaterialPropertyBlock();renderer.GetPropertyBlock(block);n.block=Block(block,renderer.sharedMaterial.shader);
                    n.slots=Enumerable.Range(0,n.materials.Length).Select(slot=>{renderer.GetPropertyBlock(block,slot);return new PreparedPropertyGroup{values=Block(block,renderer.sharedMaterials[slot].shader)};}).ToArray();
                }
                var lod=t.GetComponent<ModelDetailLod>();if(lod){n.hasLod=true;n.lod=new PreparedLod{renderers=lod.Renderers.Select(r=>ids[r.transform]).ToArray(),full=lod.OriginalMeshes.Select(MeshId).ToArray(),normal=lod.NormalMeshes.Select(MeshId).ToArray(),far=lod.FarMeshes.Select(MeshId).ToArray(),bounds=lod.ReferenceBounds,selection=lod.SelectionBounds,catalog=lod.IsCatalogBound,level=lod.Level,hook=t.GetComponent<CrossingSceneryDetail>()?1:t.GetComponent<ModelDetailCameraBinding>()?2:0};}
            }
            var scene=new PreparedScene{nodes=nodes,baseMaterial=baseIndex};metadata?.Invoke(scene,TextureId,MaterialId);scene.meshes=meshes.ToArray();scene.textures=textures.ToArray();scene.materials=materials.ToArray();return scene;
        }
        public static byte[] Encode(string key,PreparedScene scene)=>PreparedMapCache.Envelope(key,w=>{
            w.Write(Json(scene));foreach(var mesh in scene.meshes){w.Write(mesh.streams.Length);foreach(var stream in mesh.streams){w.Write(stream.Length);w.Write(stream);}w.Write(mesh.indices.Length);w.Write(mesh.indices);}
            foreach(var texture in scene.textures)if(string.IsNullOrEmpty(texture.path)){w.Write(texture.pixels.Length);w.Write(texture.pixels);}
        });
        public static PreparedScene Decode(byte[] payload)
        {
            using var reader=new BinaryReader(new MemoryStream(payload));var scene=FromJson<PreparedScene>(reader.ReadString());
            byte[] Bytes(){int count=reader.ReadInt32();if(count<0||count>128*1024*1024||count>reader.BaseStream.Length-reader.BaseStream.Position)throw new InvalidDataException("Prepared buffer length");return reader.ReadBytes(count);}
            if(scene.nodes==null||scene.nodes.Length==0||scene.nodes.Length>100000||scene.meshes==null||scene.materials==null||scene.textures==null)throw new InvalidDataException("Prepared scene metadata");
            foreach(var mesh in scene.meshes){int streams=reader.ReadInt32();if(streams<0||streams>8||mesh.vertices<0||mesh.vertices>10000000)throw new InvalidDataException("Prepared mesh metadata");mesh.streams=new byte[streams][];for(int i=0;i<streams;i++)mesh.streams[i]=Bytes();mesh.indices=Bytes();}
            foreach(var texture in scene.textures)if(string.IsNullOrEmpty(texture.path))texture.pixels=Bytes();
            if(reader.BaseStream.Position!=reader.BaseStream.Length)throw new InvalidDataException("Trailing prepared scene data");
            for(int i=0;i<scene.nodes.Length;i++)if(i>0&&(scene.nodes[i].parent<0||scene.nodes[i].parent>=i))throw new InvalidDataException("Prepared parent order");
            return scene;
        }
    }
}
