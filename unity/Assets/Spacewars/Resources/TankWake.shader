Shader "Spacewars/TankWake"
{
 Properties { _Phase("Shot phase",Float)=0 _BaseColor("Tint",Color)=(1,1,1,1) }
 SubShader {
 Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
 Pass {
 Blend SrcAlpha One
 ZWrite Off
 Cull Off
 HLSLPROGRAM
 #pragma vertex Vert
 #pragma fragment Frag
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 CBUFFER_START(UnityPerMaterial)
 float _Phase;float4 _BaseColor;
 CBUFFER_END
 struct A {float3 positionOS:POSITION;float2 uv:TEXCOORD0;};
 struct V {float4 positionCS:SV_POSITION;float2 uv:TEXCOORD0;};
 V Vert(A i) {
 V o;float3 origin=TransformObjectToWorld(float3(0,0,0));
 float3 axis=normalize(TransformObjectToWorldDir(float3(0,0,1)));
 float3 view=normalize(GetCameraPositionWS()-origin);float3 side=cross(axis,view);
 side=dot(side,side)<.001?UNITY_MATRIX_V[0].xyz:normalize(side);
 float size=length(TransformObjectToWorldDir(float3(1,0,0),false));
 // Authored flame silhouette, normalized by profile flash size.
 float pulse=1;
 o.positionCS=TransformWorldToHClip(origin+size*pulse*(axis*i.uv.x+side*(i.uv.y*2-1)*.08));
 o.uv=i.uv;return o;
 }
 half4 Frag(V i):SV_Target {
 float x=i.uv.x,y=i.uv.y*2-1;
 float alpha=exp(-y*y*8)*pow(1-x,2)*smoothstep(0,.05,x);
 float3 color=lerp(float3(1,.9,.5),float3(1,.16,.01),x);
 return half4(color*1.4,alpha*.8);

 }
 ENDHLSL
 }
 }
}
