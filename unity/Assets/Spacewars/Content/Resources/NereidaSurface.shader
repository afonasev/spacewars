Shader "Spacewars/NereidaSurface"
{
 Properties {
  _BaseColor("Tint",Color)=(1,1,1,1)
  _DetailTex("Albedo",2D)="gray" {} _DetailTexB("Sibling B",2D)="gray" {} _DetailTexC("Sibling C",2D)="gray" {}
  _DetailVariantGain("Calibrated gain and mean",Vector)=(1,1,1,.2)
  _DetailSettings("Tile region transition contrast",Vector)=(7,24,.06,.45)
  _SnowSettings("Snow scale coverage strata spacing contrast",Vector)=(27,.35,.8,.25)
  _SnowFinish("Linear snow value and drift feather",Vector)=(.32,2,0,0)
  _DriftCount("Authored drift boundary count",Float)=0
  _NormalSlope("Finite normal slope",Float)=.15
  _Role("Ice snow rock steel water",Float)=0
 }
 SubShader {
 Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
 Pass {
  Tags {"LightMode"="UniversalForward"}
  HLSLPROGRAM
  #pragma vertex vert
  #pragma fragment frag
  #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
  #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
  TEXTURE2D(_DetailTex);TEXTURE2D(_DetailTexB);TEXTURE2D(_DetailTexC);SAMPLER(sampler_DetailTex);
  CBUFFER_START(UnityPerMaterial)
  float4 _BaseColor,_DetailVariantGain,_DetailSettings,_SnowSettings,_SnowFinish;
  float4 _DriftPoints[16];float _DriftCount;
  float _NormalSlope,_Role;
  CBUFFER_END
  struct A {float4 positionOS:POSITION;float3 normalOS:NORMAL;};
  struct V {float4 positionCS:SV_POSITION;float3 p:TEXCOORD0;float3 n:TEXCOORD1;};
  V vert(A a) {V v;v.p=TransformObjectToWorld(a.positionOS.xyz);v.positionCS=TransformWorldToHClip(v.p);v.n=TransformObjectToWorldNormal(a.normalOS);return v;}
  // Same continuous sibling-region policy and crisp oblique basis as the approved kit.
  float hash(float2 p){return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
  float noise(float2 p){float2 c=floor(p),f=frac(p);f=f*f*(3-2*f);return lerp(lerp(hash(c),hash(c+float2(1,0)),f.x),lerp(hash(c+float2(0,1)),hash(c+1),f.x),f.y);}
  float3 weights(float2 w){float2 p=w/_DetailSettings.y;p+=float2(noise(p*.73+19),noise(p*.81+43))-.5;
   float3 s=float3(noise(p+float2(7,31)),noise(p+float2(53,11)),noise(p+float2(29,71)));float top=max(s.x,max(s.y,s.z));float3 a=smoothstep(top-_DetailSettings.z,top,s);return a/max(dot(a,1),.0001);}
  half4 frag(V v):SV_Target {
   float3 n=normalize(v.n);
   // Flush artwork samples the exact underlying plane, including macro/variant phase.
   if(_Role>4.5)v.p.y=0;
   float2 uv=float2(dot(v.p,float3(.70710678,0,-.70710678)),dot(v.p,float3(.40824829,-.81649658,.40824829)))/_DetailSettings.x;
   float3 w=weights(v.p.xz);
   float3 tex=SAMPLE_TEXTURE2D(_DetailTex,sampler_DetailTex,uv).rgb*_DetailVariantGain.x*w.x
    +SAMPLE_TEXTURE2D(_DetailTexB,sampler_DetailTex,uv).rgb*_DetailVariantGain.y*w.y
    +SAMPLE_TEXTURE2D(_DetailTexC,sampler_DetailTex,uv).rgb*_DetailVariantGain.z*w.z;
   float grain=dot(tex,float3(.2126,.7152,.0722))/max(_DetailVariantGain.w,.001);
   float3 color=_BaseColor.rgb*lerp(1,clamp(grain,.35,1.8),_DetailSettings.w);
   float field=noise(v.p.xz/_SnowSettings.x+float2(9,35))*.7+noise(v.p.xz/(_SnowSettings.x*.37)+31)*.3;
   float side=1-smoothstep(.15,.75,abs(n.y));
   // Snow/ice/geological color fields are independent of high-frequency albedo.
   if(_Role<2.5||_Role>4.5) {
    float snow=smoothstep(1-_SnowSettings.y-.09,1-_SnowSettings.y+.09,field)*(1-side);
    if(_Role>.5&&_Role<1.5)snow=.72+.28*snow;
    if(_Role>4.5) {
     float distance=10000;
     for(int i=0;i<(int)_DriftCount;i++) {float2 a=_DriftPoints[i].xy,b=_DriftPoints[(i+1)%(int)_DriftCount].xy;float2 ab=b-a;
      float t=saturate(dot(v.p.xz-a,ab)/max(dot(ab,ab),.0001));distance=min(distance,length(v.p.xz-a-ab*t));}
     float drift=smoothstep(0,_SnowFinish.y,distance);snow=max(snow,drift);
    }
    color=lerp(color,_SnowFinish.x*float3(.89,.97,1.03)*lerp(.85,1.15,saturate(grain)),snow);
    float layer=noise(float2(v.p.y/_SnowSettings.z+noise(v.p.xz/6)*2,dot(v.p.xz,float2(.31,.67))/11));
    color*=1-side*_SnowSettings.w*layer;
    if(_Role<1.5)color=lerp(color,color*float3(.45,.82,.94),side*.6);
   }
   if(_Role>2.5&&_Role<3.5){float2 panel=abs(frac(uv*float2(3,5))-.5);float seam=1-smoothstep(.018,.035,min(panel.x,panel.y));color*=1-seam*.35;}
   float3 gradient=ddx(grain)*ddx(v.p)+ddy(grain)*ddy(v.p);
   if(!any(gradient!=gradient)){float3 slope=clamp(gradient,-_NormalSlope,_NormalSlope);slope*=min(1,_NormalSlope/max(length(slope),.0001));n=normalize(n-slope);}
   Light sun=GetMainLight();float diffuse=saturate(dot(n,sun.direction));
   float3 light=SampleSH(n)+sun.color*(.2+.8*diffuse);
   if(_Role>3.5&&_Role<4.5){float ripple=sin(v.p.x*.8+v.p.z*.34+noise(v.p.xz/8)*3);color*=1+.09*ripple;light=max(light,.5);}
   return half4(color*light,1);
  }
  ENDHLSL
 }
 UsePass "Universal Render Pipeline/Lit/ShadowCaster"
 }
}
