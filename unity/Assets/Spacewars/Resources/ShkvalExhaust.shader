Shader "Spacewars/ShkvalExhaust"
{
 Properties {
  _BaseColor("Tint",Color)=(1,1,1,1)
  [HideInInspector] _LaunchClipPlane("Observed muzzle plane",Vector)=(0,0,0,0)
  [HideInInspector] _LaunchClipEnabled("Clip enabled",Float)=0
  [HideInInspector] _Surface("Surface",Float)=1
  [HideInInspector] _SrcBlend("Source blend",Float)=5
  [HideInInspector] _DstBlend("Destination blend",Float)=1
  [HideInInspector] _ZWrite("Depth write",Float)=0
  [HideInInspector] _Cull("Cull",Float)=0
 }
 SubShader {
  Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
  Pass {
   Tags { "LightMode"="UniversalForward" }
   Blend [_SrcBlend] [_DstBlend]
   ZWrite [_ZWrite]
   ZTest LEqual
   Cull [_Cull]
   HLSLPROGRAM
   #pragma vertex Vert
   #pragma fragment Frag
   #pragma multi_compile_fog
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   CBUFFER_START(UnityPerMaterial)
   float4 _BaseColor;float4 _LaunchClipPlane;float _LaunchClipEnabled;
   CBUFFER_END
   struct A {float3 positionOS:POSITION;};
   struct V {float4 positionCS:SV_POSITION;float3 positionWS:TEXCOORD0;float fog:TEXCOORD1;};
   V Vert(A i){V o;o.positionWS=TransformObjectToWorld(i.positionOS);o.positionCS=TransformWorldToHClip(o.positionWS);o.fog=ComputeFogFactor(o.positionCS.z);return o;}
   half4 Frag(V i):SV_Target {
    if(_LaunchClipEnabled>.5)clip(dot(float4(i.positionWS,1),_LaunchClipPlane));
    return half4(MixFog(_BaseColor.rgb,i.fog),_BaseColor.a);
   }
   ENDHLSL
  }
 }
}
