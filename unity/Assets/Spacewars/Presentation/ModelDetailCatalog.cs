using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEngine;

namespace Spacewars.Presentation
{
    // Per-map resources contain only reduced meshes; the exact close mesh remains
    // owned by its original generator/prefab. A changed source fails back to close.
    public sealed class ModelDetailCatalog : ScriptableObject
    {
        [Serializable] public sealed class Entry
        {
            public string hash;
            public int vertices,indices,submeshes;
            public Mesh normal,far;
            public Bounds[] compactBounds=Array.Empty<Bounds>();
        }
        [Serializable] public sealed class SourceException
        {
            public string hash,reason;public int vertices,indices,submeshes;
        }
        public SourceException[] exemptions=Array.Empty<SourceException>();
        public Entry[] entries=Array.Empty<Entry>();
        Dictionary<string,Entry[]> signatures;
        static string Signature(int vertices,int indices,int submeshes)=>vertices+":"+indices+":"+submeshes;
        public void InvalidateLookup()=>signatures=null;
        public Entry Find(Mesh mesh)
        {
            if(signatures==null)signatures=entries.GroupBy(e=>Signature(e.vertices,e.indices,e.submeshes)).ToDictionary(g=>g.Key,g=>g.ToArray());
            int count=0;for(int i=0;i<mesh.subMeshCount;i++)count+=(int)mesh.GetIndexCount(i);
            if(!signatures.TryGetValue(Signature(mesh.vertexCount,count,mesh.subMeshCount),out var possible))return null;
            string hash=Identity(mesh);return possible.FirstOrDefault(e=>e.hash==hash);
        }
        public bool AccountsFor(Mesh mesh)
        {
            if(Find(mesh)!=null)return true;
            int count=0;for(int i=0;i<mesh.subMeshCount;i++)count+=(int)mesh.GetIndexCount(i);
            var possible=exemptions.Where(e=>e.vertices==mesh.vertexCount&&e.indices==count&&e.submeshes==mesh.subMeshCount).ToArray();
            if(possible.Length==0)return false;string hash=Identity(mesh);return possible.Any(e=>e.hash==hash);
        }
        public static bool IsModeled(string group,string name)
        {
            bool Starts(params string[] prefixes)=>prefixes.Any(p=>name.StartsWith(p,StringComparison.Ordinal));
            switch(group){
                case "Wrecks":return true;
                case "NullDock":return false; // Explicitly excluded from LOD by user decision.
                case "ThreeCrossings":return !Starts("east-bank","west-bank","platform-","River —","Platform steel foundation")&&!(Starts("Hydroworks ")&&name.EndsWith("|5",StringComparison.Ordinal));
                case "WhiteMeridian":return !Starts("Meridian basin","Meridian connected shelf","Meridian foundation","Meridian copper service strip","Meridian repaired slab","Impassable crash pocket sand","Wreck end sand mound");
                case "CrimsonGrove":return !Starts("grove/","Complete construction apron");
                case "MossHighland":return !Starts("outer-ring","plateau","Moss border river");
                case "BasaltCrown":return !Starts("citadel","outer-ring");
                case "Foundry":return Starts("Connected layered basalt","Foundry ");
                case "Campaign":return name!="Outer worlds desert";
                case "AiTest":return false;
                case "ProvingGround":case "Diagnostic":return name=="Boundary";
                case "Nereida":return !Starts("Reserved site ","Factory reserve ","Landing pad","Open water","ice-","Ice fracture/","home/","home-x/","home-z/","terrace/");
                default:throw new ArgumentException("Unclassified map "+group);
            }
        }
        public static string Identity(Mesh mesh)
        {
            using var sha=SHA256.Create();using var stream=new CryptoStream(Stream.Null,sha,CryptoStreamMode.Write);
            // Batch small expanded-vertex writes without changing the SHA256 byte stream.
            using var buffered=new BufferedStream(stream,64*1024);
            using(var writer=new BinaryWriter(buffered,System.Text.Encoding.UTF8,true)){
                foreach(var a in mesh.GetVertexAttributes()){writer.Write((int)a.attribute);writer.Write((int)a.format);writer.Write(a.dimension);writer.Write(a.stream);}
                using var data=Mesh.AcquireReadOnlyMeshData(mesh);var bytes=new byte[mesh.vertexBufferCount][];
                for(int s=0;s<bytes.Length;s++)bytes[s]=data[0].GetVertexData<byte>(s).ToArray();
                for(int sub=0;sub<mesh.subMeshCount;sub++){
                    writer.Write((int)mesh.GetTopology(sub));var indices=mesh.GetIndices(sub,true);writer.Write(indices.Length);
                    foreach(int index in indices)for(int s=0;s<bytes.Length;s++)writer.Write(bytes[s],index*mesh.GetVertexBufferStride(s),mesh.GetVertexBufferStride(s));
                }
                writer.Flush();
            }
            stream.FlushFinalBlock();return BitConverter.ToString(sha.Hash).Replace("-","").ToLowerInvariant();
        }
    }
}
