Shader "Spacewars/TankMuzzle"
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
 float pulse=1-.55*saturate(_Phase);
 o.positionCS=TransformWorldToHClip(origin+size*pulse*(axis*i.uv.x*3.5+side*(i.uv.y*2-1)));
 o.uv=i.uv;return o;
 }
 half4 Frag(V i):SV_Target {
 float x=i.uv.x,y=abs(i.uv.y*2-1),age=saturate(_Phase);
 float fringe=sin(x*35+age*19)*.08+sin(x*67-age*11)*.035;
 float width=(.18+.72*sin(pow(x,.65)*PI))*pow(1-x,.55);
 float flame=saturate((width+fringe-y)*7)*pow(1-x,.6);
 float core=exp(-y*y*70)*pow(1-x,2);
 float halo=exp(-y*y*5-x*4)*.25;
 float3 color=lerp(float3(1,.16,.012),float3(1,.88,.55),saturate(core*2+.35*(1-x)));
 color=lerp(color,float3(1,.98,.86),core);
 return half4(color*_BaseColor.rgb*1.6,(flame+halo)*pow(1-age,.65)*_BaseColor.a);
 }
 ENDHLSL
 }
 }
}
