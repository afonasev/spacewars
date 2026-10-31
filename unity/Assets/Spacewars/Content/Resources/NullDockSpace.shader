Shader "Spacewars/NullDockSpace"
{
 Properties {_Planet("Planet center x z radius",Vector)=(-190,110,140,0)}
 SubShader {Tags {"RenderType"="Opaque" "RenderPipeline"="UniversalPipeline"} Pass {
 Tags {"LightMode"="UniversalForward"} Cull Off ZWrite On
 HLSLPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 CBUFFER_START(UnityPerMaterial) float4 _Planet; CBUFFER_END
 struct A{float4 p:POSITION;};struct V{float4 p:SV_POSITION;float2 uv:TEXCOORD0;};
 V vert(A i){V o;float3 p=TransformObjectToWorld(i.p.xyz);o.p=TransformWorldToHClip(p);o.uv=p.xz;return o;}
 float hash(float2 p){return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
 float noise(float2 p){float2 c=floor(p),f=frac(p);f=f*f*(3-2*f);return lerp(lerp(hash(c),hash(c+float2(1,0)),f.x),lerp(hash(c+float2(0,1)),hash(c+1),f.x),f.y);}
 float fbm(float2 p){return noise(p)*.55+noise(p*2.13)*.28+noise(p*4.41)*.12+noise(p*8.7)*.05;}
 half4 frag(V i):SV_Target{
 float2 cell=floor(i.uv*1.1),q=frac(i.uv*1.1)-.5;float star=step(.995,hash(cell))*(1-smoothstep(.015,.07,length(q)));
 float3 col=float3(.003,.006,.016)+float3(.28,.42,.58)*star;
 float2 p=(i.uv-_Planet.xy)/_Planet.z;float d=length(p);
 float rim=exp(-abs(d-1)*55);col+=float3(.035,.18,.3)*rim;
 if(d<1){float3 n=float3(p.x,sqrt(max(0,1-dot(p,p))),p.y);float light=saturate(dot(n,normalize(float3(-.7,.4,.5))));float land=fbm(p*7+fbm(p*12)*2);float3 surface=lerp(float3(.015,.07,.14),float3(.09,.17,.16),smoothstep(.48,.58,land));float clouds=smoothstep(.55,.73,fbm(p*18+float2(fbm(p*5),fbm(p*6))*5));surface=lerp(surface,float3(.45,.58,.63),clouds*.8);col=surface*(.07+light*.8)+float3(.025,.12,.22)*pow(1-n.y,4)*light;}
 return half4(col,1);}
 ENDHLSL
 }}
}
