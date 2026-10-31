using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Spacewars.Presentation
{
    // Only for a privately owned, finished presentation mesh. Call AFTER all
    // deformation/normal generation; never on imported assets or append templates.
    public static class FinalProceduralMesh
    {
        public static void IndexOwned(Mesh mesh)
        {
            if(!mesh||!mesh.isReadable||mesh.vertexCount==0)return;
            if(mesh.blendShapeCount!=0||mesh.bindposes.Length!=0)
                throw new ArgumentException("Final procedural indexing excludes animated meshes");
            int count=mesh.vertexCount,streams=mesh.vertexBufferCount;
            var attributes=mesh.GetVertexAttributes();
            foreach(var a in attributes)
                if(a.attribute==VertexAttribute.BlendIndices||a.attribute==VertexAttribute.BlendWeight)
                    throw new ArgumentException("Final procedural indexing excludes skin data");
            var bytes=new byte[streams][];var strides=new int[streams];
            using(var data=Mesh.AcquireReadOnlyMeshData(mesh))
                for(int s=0;s<streams;s++){strides[s]=mesh.GetVertexBufferStride(s);bytes[s]=data[0].GetVertexData<byte>(s).ToArray();}
            bool Same(int a,int b)
            {
                for(int s=0;s<streams;s++)
                    if(!new ReadOnlySpan<byte>(bytes[s],a*strides[s],strides[s]).SequenceEqual(new ReadOnlySpan<byte>(bytes[s],b*strides[s],strides[s])))return false;
                return true;
            }
            // One collision chain array instead of a List/backing array per distinct
            // hash. Representatives still follow first occurrence in the input.
            var heads=new Dictionary<int,int>();var next=new int[count];
            var representatives=new int[count];int unique=0;var remap=new int[count];
            for(int i=0;i<count;i++)
            {
                int hash=unchecked((int)2166136261);
                unchecked{for(int s=0;s<streams;s++)for(int k=0;k<strides[s];k++)hash=(hash^bytes[s][i*strides[s]+k])*16777619;}
                if(!heads.TryGetValue(hash,out int head))head=-1;
                int found=-1;for(int candidate=head;candidate>=0;candidate=next[candidate])if(Same(i,representatives[candidate])){found=candidate;break;}
                if(found<0){found=unique++;representatives[found]=i;next[found]=head;heads[hash]=found;}remap[i]=found;
            }
            var format=unique<65535?IndexFormat.UInt16:IndexFormat.UInt32;
            if(unique==count&&mesh.indexFormat==format)return;
            int submeshes=mesh.subMeshCount;var indices=new int[submeshes][];var descriptors=new SubMeshDescriptor[submeshes];
            for(int s=0;s<submeshes;s++)
            {
                descriptors[s]=mesh.GetSubMesh(s);indices[s]=mesh.GetIndices(s,true);
                for(int i=0;i<indices[s].Length;i++)indices[s][i]=remap[indices[s][i]];
            }
            var bounds=mesh.bounds;
            mesh.Clear(false);mesh.indexFormat=format;mesh.SetVertexBufferParams(unique,attributes);
            for(int s=0;s<streams;s++)
            {
                var compact=new byte[unique*strides[s]];
                for(int i=0;i<unique;i++)Buffer.BlockCopy(bytes[s],representatives[i]*strides[s],compact,i*strides[s],strides[s]);
                mesh.SetVertexBufferData(compact,0,0,compact.Length,s,MeshUpdateFlags.DontRecalculateBounds);
            }
            mesh.subMeshCount=submeshes;
            for(int s=0;s<submeshes;s++)
            {
                mesh.SetIndices(indices[s],descriptors[s].topology,s,false);
                var descriptor=mesh.GetSubMesh(s);descriptor.bounds=descriptors[s].bounds;
                mesh.SetSubMesh(s,descriptor,MeshUpdateFlags.DontRecalculateBounds);
            }
            mesh.bounds=bounds;
        }
    }
}
