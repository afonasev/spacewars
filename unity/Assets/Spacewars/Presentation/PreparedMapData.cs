using System;
using System.Collections.Generic;
using UnityEngine;
namespace Spacewars.Presentation
{
    [Serializable] public sealed class PreparedProperty { public string name;public int kind,texture=-1;public float number;public Vector4 vector;public Vector4[] array;public Vector2 scale=Vector2.one,offset; }
    [Serializable] public sealed class PreparedMaterial { public string name,shader,path;public string[] keywords;public int queue,gi;public bool instancing,doubleSided;public PreparedProperty[] properties; }
    [Serializable] public sealed class PreparedAttribute { public int attribute,format,dimension,stream; }
    [Serializable] public sealed class PreparedSubmesh { public int start,count,topology,baseVertex,firstVertex,vertices;public Bounds bounds; }
    [Serializable] public sealed class PreparedMesh { public string name;public int vertices,indexFormat;public PreparedAttribute[] attributes;public PreparedSubmesh[] submeshes;public Bounds bounds;[NonSerialized] public byte[][] streams;[NonSerialized] public byte[] indices; }
    [Serializable] public sealed class PreparedTexture { public string name,path;public int width,height,format,mips,filter,u,v,w,aniso;public float bias;public bool linear,readable;[NonSerialized] public byte[] pixels; }
    [Serializable] public sealed class PreparedLod { public int[] renderers,full,normal,far;public Bounds bounds;public Bounds[] selection;public bool catalog;public int hook,level; }
    [Serializable] public sealed class PreparedText { public string text;public int anchor,alignment,fontSize;public float size,lineSpacing,offset;public bool rich;public Color color; }
    [Serializable] public sealed class PreparedNode { public string name;public int parent,layer,mesh=-1;public Vector3 position,scale;public Quaternion rotation;public bool active,hasRenderer,hasText,hasLod;public int[] materials;public PreparedProperty[] block;public PreparedPropertyGroup[] slots;public int shadow,probes,reflection,motion,sortLayer,sortOrder;public bool shadows,occlusion,enabled;public uint renderingLayer;public Bounds rendererBounds;public PreparedLod lod;public PreparedText text; }
    [Serializable] public sealed class PreparedPropertyGroup { public PreparedProperty[] values; }
    [Serializable] public sealed class PreparedBinding { public string name;public int index; }
    [Serializable] public sealed class PreparedAccess { public int group;public Vector2 gate,direction;public Vector2[] points; }
    [Serializable] public sealed class PreparedMineAccess { public int site;public double cx,cz,ex,ez; }
    [Serializable] public sealed class PreparedSurface { public string profile,art;public int[] textures;public PreparedAccess[] accesses;public PreparedMineAccess[] mines; }
    [Serializable] public sealed class PreparedScene { public PreparedNode[] nodes;public int[] worldLods;public PreparedMesh[] meshes;public PreparedTexture[] textures;public PreparedMaterial[] materials;public PreparedBinding[] materialFields,textureFields;public PreparedSurface crossings;public bool hasCrossings;public string dockMetrics;public int gate=-1,baseMaterial=-1; }
    [Serializable] public sealed class PreparedSchema { public PreparedSchemaProperty[] properties=Array.Empty<PreparedSchemaProperty>();public PreparedShaderSchema[] shaders=Array.Empty<PreparedShaderSchema>(); }
    [Serializable] public sealed class PreparedSchemaProperty { public string name;public int kind; }
    [Serializable] public sealed class PreparedShaderSchema { public string name;public PreparedSchemaProperty[] properties; }
    // kind: 0 scalar, 1 raw vector/color, 2 texture, 3 vector array.
}
