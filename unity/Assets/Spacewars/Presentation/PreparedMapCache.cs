using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System.Reflection;
using Spacewars.Simulation;
using UnityEngine;
using Object=UnityEngine.Object;
namespace Spacewars.Presentation
{
    public static class PreparedMapCache
    {
        public const int FormatVersion=1;
        const int MaximumPacketBytes=256*1024*1024; // Technical bounds on derived cache storage/read allocation.
        public static bool BypassReads {get;set;}
        public static string DirectoryOverride {get;set;}
        public static Func<string> EditorStamp;
        public static Func<Object,string> EditorResourcePath;
        public static Action<string,byte[]> BakeWriter;
        public static PreparedSchema CaptureSchema;
        public static Func<PreparedSchema> EditorSchema;
        static readonly Dictionary<int,(WeakReference<Object> value,string path)> externalPaths=new Dictionary<int,(WeakReference<Object>,string)>();
        static readonly Dictionary<int,byte[]> retainedPixels=new Dictionary<int,byte[]>();
        static PreparedSchema schema;
        public static PreparedSchema Schema=>CaptureSchema??EditorSchema?.Invoke()??(schema??(schema=JsonUtility.FromJson<PreparedSchema>(Resources.Load<TextAsset>("PreparedMaps/schema")?.text??"{}")));
        public static string Hash(byte[] data){using var sha=SHA256.Create();return BitConverter.ToString(sha.ComputeHash(data)).Replace("-","").ToLowerInvariant();}
        public static string Stamp=>EditorStamp!=null?EditorStamp():Resources.Load<TextAsset>("PreparedMaps/stamp")?.text??("unprepared-"+FormatVersion+"-"+Application.buildGUID);
        public static string MapKey(IPlayableTerrain map)=>map is CampaignOpeningMap?"Campaign":map==null?"Diagnostic":map.GetType().Name.Replace("Map","");
        sealed class GeometryResolver : DefaultContractResolver
        {
            protected override IList<JsonProperty> CreateProperties(Type type,MemberSerialization serialization)
                =>type==typeof(NavObstacle)?type.GetFields(BindingFlags.Public|BindingFlags.Instance).Select(f=>base.CreateProperty(f,serialization)).ToList():base.CreateProperties(type,serialization);
        }
        static readonly JsonSerializerSettings geometrySettings=new JsonSerializerSettings{ContractResolver=new GeometryResolver()};
        public static string Key(IPlayableTerrain map,PlayableProfile profile,object extra=null)
            =>Hash(Encoding.UTF8.GetBytes(FormatVersion+"|"+Application.unityVersion+"|"+QualitySettings.activeColorSpace+"|"+Stamp+"|"+JsonConvert.SerializeObject(map,geometrySettings)+"|"+(profile==null?"":JsonUtility.ToJson(profile.CopyData()))+"|"+JsonConvert.SerializeObject(extra)));
        static string DirectoryPath=>DirectoryOverride??Path.Combine(Application.persistentDataPath,"PreparedMaps",Application.isEditor?Hash(Encoding.UTF8.GetBytes(Application.dataPath)).Substring(0,12):"player");
        public static string VariantPath(string group,string key)=>Path.Combine(DirectoryPath,group+"-"+key+".swmap");
        public static void RememberPixels(Texture2D texture)=>retainedPixels[texture.GetInstanceID()]=texture.GetRawTextureData();
        internal static byte[] Pixels(Texture2D texture)=>retainedPixels.TryGetValue(texture.GetInstanceID(),out var data)?data:texture.GetRawTextureData();
        internal static void ForgetPixels(Texture2D texture){if(texture)retainedPixels.Remove(texture.GetInstanceID());}
        internal static string ResourcePath(Object value)
        {
            if(!value)return null;
            var path=EditorResourcePath?.Invoke(value);if(!string.IsNullOrEmpty(path)){externalPaths[value.GetInstanceID()]=(new WeakReference<Object>(value),path);return path;}
            return externalPaths.TryGetValue(value.GetInstanceID(),out var saved)&&saved.value.TryGetTarget(out var prior)&&ReferenceEquals(prior,value)?saved.path:null;
        }
        internal static Texture ResolveTexture(PreparedTexture data)
        {
            if(data.path=="$fog")return null;
            if(string.IsNullOrEmpty(data.path))return null;
            var found=Resources.LoadAll<Texture>(data.path).FirstOrDefault(t=>t.name==data.name)??Resources.Load<Texture>(data.path);
            if(found)externalPaths[found.GetInstanceID()]=(new WeakReference<Object>(found),data.path);
            return found;
        }
        public static byte[] Envelope(string key,Action<BinaryWriter> write)
        {
            using var raw=new MemoryStream();using(var writer=new BinaryWriter(raw,Encoding.UTF8,true))write(writer);
            var bytes=raw.ToArray();using var packet=new MemoryStream();using(var writer=new BinaryWriter(packet,Encoding.UTF8,true)){writer.Write("SWMAP");writer.Write(FormatVersion);writer.Write(key);writer.Write(bytes.Length);writer.Write(Hash(bytes));}
            using(var zipped=new GZipStream(packet,System.IO.Compression.CompressionLevel.Fastest,true))zipped.Write(bytes,0,bytes.Length);
            return packet.ToArray();
        }
        static byte[] Open(byte[] bytes,string key,bool allowDifferentKey=false)
        {
            using var packet=new MemoryStream(bytes,false);using var reader=new BinaryReader(packet,Encoding.UTF8,true);
            if(reader.ReadString()!="SWMAP"||reader.ReadInt32()!=FormatVersion)throw new InvalidDataException("Prepared map format");
            string actualKey=reader.ReadString();if(!allowDifferentKey&&actualKey!=key)return null;
            int length=reader.ReadInt32();if(length<0||length>MaximumPacketBytes)throw new InvalidDataException("Prepared map length");string hash=reader.ReadString();
            var data=new byte[length];using var zip=new GZipStream(packet,CompressionMode.Decompress,true);int offset=0;
            while(offset<length){int count=zip.Read(data,offset,length-offset);if(count==0)throw new InvalidDataException("Truncated prepared map");offset+=count;}
            if(zip.ReadByte()!=-1||Hash(data)!=hash)throw new InvalidDataException("Prepared map checksum");return data;
        }
        internal static byte[] Read(string group,string key)
        {
            if(BypassReads)return null;
            string path=VariantPath(group,key);
            byte[] local=null;try{if(File.Exists(path)&&new FileInfo(path).Length<=MaximumPacketBytes)local=File.ReadAllBytes(path);}catch(IOException){}catch(UnauthorizedAccessException){}
            foreach(var candidate in new[]{local,Resources.Load<TextAsset>("PreparedMaps/"+group)?.bytes}){
                if(candidate==null)continue;try{var data=Open(candidate,key);if(data!=null)return data;}catch(Exception e)when(e is IOException||e is InvalidDataException||e is FormatException||e is ArgumentException){Debug.LogWarning("Discarded invalid prepared map "+group+": "+e.Message);}
            }
            return null;
        }
        internal static void PrimeResources(string group)
        {
            // A changed numeric profile can generate before its first cache hit. The
            // shipped packet supplies its imported texture paths without loading other maps.
            var resource=Resources.Load<TextAsset>("PreparedMaps/"+group);if(!resource)return;
            try{var bytes=Open(resource.bytes,"",true);using var reader=new BinaryReader(new MemoryStream(bytes));var scene=PreparedSceneCodec.FromJson<PreparedScene>(reader.ReadString());foreach(var texture in scene.textures??Array.Empty<PreparedTexture>())if(!string.IsNullOrEmpty(texture.path)&&texture.path!="$fog")ResolveTexture(texture);}catch(Exception e)when(e is IOException||e is InvalidDataException||e is FormatException||e is ArgumentException){Debug.LogWarning("Prepared resource index unavailable: "+e.Message);}
        }
        internal static void Write(string group,string key,byte[] packet)
        {
            if(BakeWriter!=null){BakeWriter(group,packet);return;}
            string path=VariantPath(group,key),temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try{
                Directory.CreateDirectory(DirectoryPath);File.WriteAllBytes(temporary,packet);
                if(File.Exists(path))File.Replace(temporary,path,null);else File.Move(temporary,path);
                // Keep only four own parameter variants per map/group. Packaged
                // defaults are immutable and never participate in this eviction.
                foreach(var old in new DirectoryInfo(DirectoryPath).GetFiles(group+"-*.swmap").OrderByDescending(p=>p.LastWriteTimeUtc).Skip(4))try{old.Delete();}catch(IOException){}
            }catch(Exception e)when(e is IOException||e is UnauthorizedAccessException){Debug.LogWarning("Could not save prepared map: "+e.Message);}finally{try{if(File.Exists(temporary))File.Delete(temporary);}catch(IOException){}catch(UnauthorizedAccessException){}}
        }
        public static bool TryScene(string group,string key,out PreparedScene scene)
        {
            scene=null;var data=Read(group,key);if(data==null)return false;
            try{scene=PreparedSceneCodec.Decode(data);return true;}catch(Exception e)when(e is IOException||e is InvalidDataException||e is FormatException||e is ArgumentException){Debug.LogWarning("Discarded invalid scene packet: "+e.Message);return false;}
        }
        public static void SaveScene(string group,string key,PreparedScene scene)=>Write(group,key,PreparedSceneCodec.Encode(key,scene));
        public static bool TryColors(string group,string key,Color[] destination)
        {
            var bytes=Read(group,key);if(bytes==null)return false;
            try{using var reader=new BinaryReader(new MemoryStream(bytes));if(reader.ReadInt32()!=destination.Length)return false;for(int i=0;i<destination.Length;i++)destination[i]=new Color(reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle());return true;}catch(IOException){return false;}
        }
        public static void SaveColors(string group,string key,Color[] colors)=>Write(group,key,Envelope(key,w=>{w.Write(colors.Length);foreach(var c in colors){w.Write(c.r);w.Write(c.g);w.Write(c.b);w.Write(c.a);}}));
        public static void ResetSession(){externalPaths.Clear();retainedPixels.Clear();schema=null;}
    }
}
