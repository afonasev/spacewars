Shader "Spacewars/MossHighlandSurface"
{
 Properties {
  _FogMask("Native fog",2D)="black" {} _FogColor("Fog",Color)=(.04,.04,.04,1) _FogBounds("Bounds",Vector)=(56,56,0,0)
  _PaintA("Dark stone light scree weights",2D)="black" {} _PaintB("Cooled mineral sand weights",2D)="black" {}
  _DarkTex("Dark basalt",2D)="gray" {} _DarkTexB("Dark B",2D)="gray" {} _DarkTexC("Dark C",2D)="gray" {}
  _StoneTex("Stone",2D)="gray" {} _StoneTexB("Stone B",2D)="gray" {} _StoneTexC("Stone C",2D)="gray" {}
  _SandTex("Mineral",2D)="gray" {} _SandTexB("Mineral B",2D)="gray" {} _SandTexC("Mineral C",2D)="gray" {}
  _DarkVariantGain("Dark calibration",Vector)=(1,1,1,.04) _StoneVariantGain("Stone calibration",Vector)=(1,1,1,.11) _SandVariantGain("Sand calibration",Vector)=(1,1,1,.45)
  _DetailSettings("Tile region blend contrast",Vector)=(7,23,.06,.8)
  _RoadLayout("Authored ring X Z causeway half width bypass half width",Vector)=(40,32,4,3)
  _RoadSlabs("Slab length width joint contrast",Vector)=(2.75,2,.03,.3)
  _RoadFinish("Flush edging dust feather grain",Vector)=(.25,.35,.4,.18)
  _Geology("Strata spacing contrast",Vector)=(.4,.2,0,0)
  [PerRendererData] _RoadSurface("Road-bearing terrain only",Float)=1
  _NormalSlope("Finite normal slope",Float)=.075
  _ReceiverBias("Shadow receiver offset",Float)=.025
 }
 SubShader {
 Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque"}
 Pass {
 Tags {"LightMode"="UniversalForward"}
 HLSLPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
 #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
 TEXTURE2D(_FogMask);SAMPLER(sampler_FogMask);
 TEXTURE2D(_PaintA);SAMPLER(sampler_PaintA);TEXTURE2D(_PaintB);SAMPLER(sampler_PaintB);
 TEXTURE2D(_DarkTex);TEXTURE2D(_DarkTexB);TEXTURE2D(_DarkTexC);SAMPLER(sampler_DarkTex);
 TEXTURE2D(_StoneTex);TEXTURE2D(_StoneTexB);TEXTURE2D(_StoneTexC);SAMPLER(sampler_StoneTex);
 TEXTURE2D(_SandTex);TEXTURE2D(_SandTexB);TEXTURE2D(_SandTexC);SAMPLER(sampler_SandTex);
 CBUFFER_START(UnityPerMaterial)
 float4 _FogBounds,_FogColor,_DetailSettings,_DarkVariantGain,_StoneVariantGain,_SandVariantGain;
 float4 _RoleTints[7];float _NormalSlope,_ReceiverBias,_RoadSurface;
 float4 _NaturalPattern,_Geology;
 CBUFFER_END
 struct A {float4 positionOS:POSITION;float3 normalOS:NORMAL;};
 struct V {float4 positionCS:SV_POSITION;float3 p:TEXCOORD0;float3 n:TEXCOORD1;};
 V vert(A a){V v;v.p=TransformObjectToWorld(a.positionOS.xyz);v.positionCS=TransformWorldToHClip(v.p);v.n=TransformObjectToWorldNormal(a.normalOS);return v;}
 float hash(float2 p){return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
 float noise(float2 p){float2 c=floor(p),f=frac(p);f=f*f*(3-2*f);return lerp(lerp(hash(c),hash(c+float2(1,0)),f.x),lerp(hash(c+float2(0,1)),hash(c+1),f.x),f.y);}
 float3 variants(float2 w){float2 p=w/_DetailSettings.y;p+=float2(noise(p*.73+19),noise(p*.81+43))-.5;
 float3 s=float3(noise(p+float2(7,31)),noise(p+float2(53,11)),noise(p+float2(29,71)));float top=max(s.x,max(s.y,s.z));float3 a=smoothstep(top-_DetailSettings.z,top,s);return a/max(dot(a,1),.0001);}
 // Oblique projection covers horizontal ground and both vertical axes; it is
 // continuous across mesh and ramp seams and does not average independent views.
 float2 detailUV(float3 p){return float2(dot(p,float3(.70710678,0,-.70710678)),dot(p,float3(.40824829,-.81649658,.40824829)))/_DetailSettings.x;}
 #define FAMILY(role,smp,uv,vw) ((SAMPLE_TEXTURE2D(_##role##Tex,smp,uv).rgb*vw.x + SAMPLE_TEXTURE2D(_##role##TexB,smp,uv).rgb*_##role##VariantGain.y*vw.y + SAMPLE_TEXTURE2D(_##role##TexC,smp,uv).rgb*_##role##VariantGain.z*vw.z)/max(_##role##VariantGain.w,.001))
 half4 frag(V v):SV_Target {
 float2 mapUV=v.p.xz/(2*_FogBounds.xy)+.5;float4 a=SAMPLE_TEXTURE2D(_PaintA,sampler_PaintA,mapUV),b=SAMPLE_TEXTURE2D(_PaintB,sampler_PaintB,mapUV);
 float total=max(dot(a,1)+b.x+b.y+b.z,.001);a/=total;b/=total;
 float2 uv=detailUV(v.p);float3 weights=variants(v.p.xz),dark=1,stone=1,sand=1;
 // Mostly one sibling pattern is visible; blending occurs only at region edges.
 if(a.x+b.x>.001)dark=FAMILY(Dark,sampler_DarkTex,uv,weights);
 stone=FAMILY(Stone,sampler_StoneTex,uv,weights);
 sand=FAMILY(Sand,sampler_SandTex,uv,weights);
 dark=dot(dark,float3(.2126,.7152,.0722)).xxx;stone=dot(stone,float3(.2126,.7152,.0722)).xxx;sand=dot(sand,float3(.2126,.7152,.0722)).xxx;
 dark=lerp(1,clamp(dark,.2,2.5),_DetailSettings.w);stone=lerp(1,clamp(stone,.2,2.5),_DetailSettings.w);sand=lerp(1,clamp(sand,.2,2.5),_DetailSettings.w);
 // Natural mask warp is shared across roles; the sharp construction mask is unwarped.
 float2 warp=(float2(noise(v.p.xz*.28+11),noise(v.p.xz*.31+37))-.5)*_NaturalPattern.z/(2*_FogBounds.xy);
 float4 wa=SAMPLE_TEXTURE2D(_PaintA,sampler_PaintA,mapUV+warp),wb=SAMPLE_TEXTURE2D(_PaintB,sampler_PaintB,mapUV+warp);
 wa.z=a.z;float sum=max(dot(wa,1)+wb.x+wb.y+wb.z,.001);a=wa/sum;b=wb/sum;
 float damp=(1-smoothstep(_NaturalPattern.y-.09,_NaturalPattern.y+.09,noise(v.p.xz/_NaturalPattern.x+19)))*b.x;
 damp*=step(.001,_NaturalPattern.y);
 float3 moss=stone*_RoleTints[4].rgb;
 float3 earth=sand*_RoleTints[5].rgb;
 float3 albedo=dark*(a.x*_RoleTints[0].rgb+a.y*_RoleTints[1].rgb)+moss*(b.x-damp)+earth*(damp+b.y)+sand*(a.z*_RoleTints[2].rgb+a.w*_RoleTints[3].rgb+b.z*_RoleTints[6].rgb);
 float3 n=normalize(v.n);

 // Horizontal strata wrap cliff corners continuously in world space.
 float layer=v.p.y/_Geology.x+noise(v.p.xz*.35)*.35;
 float band=1-smoothstep(.04,.16,abs(frac(layer)-.5));
 albedo*=1-band*_Geology.y*(1-saturate(n.y));
 float grain=dot(albedo,float3(.2126,.7152,.0722));
 float3 dx=ddx(v.p),dy=ddy(v.p),r1=cross(dy,n),r2=cross(n,dx);float det=dot(dx,r1);
 float3 slope=(r1*ddx(grain)+r2*ddy(grain))*sign(det)/max(abs(det),.00001);
 if(!any(slope!=slope)){slope=clamp(slope,-_NormalSlope,_NormalSlope);slope*=min(1,_NormalSlope/max(length(slope),.0001));n=normalize(n-slope);}
 Light sun=GetMainLight(TransformWorldToShadowCoord(v.p+normalize(v.n)*_ReceiverBias));
 float3 lit=albedo*(SampleSH(n)+sun.color*saturate(dot(n,sun.direction))*sun.shadowAttenuation);
 float fog=SAMPLE_TEXTURE2D(_FogMask,sampler_FogMask,mapUV).r;float grey=dot(lit,float3(.2126,.7152,.0722));lit=lerp(lit,grey.xxx,fog);
 return half4(lerp(lit,_FogColor.rgb,fog),1);
 }
 ENDHLSL
 }
 Pass {
 Name "ShadowCaster" Tags {"LightMode"="ShadowCaster"} ZWrite On ZTest LEqual ColorMask 0
 HLSLPROGRAM
 #pragma vertex shadowVert
 #pragma fragment shadowFrag
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
 float3 _LightDirection;
 struct ShadowA {float4 positionOS:POSITION;float3 normalOS:NORMAL;};
 float4 shadowVert(ShadowA a):SV_POSITION{float3 p=TransformObjectToWorld(a.positionOS.xyz),n=TransformObjectToWorldNormal(a.normalOS);return ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(p,n,_LightDirection)));}
 half4 shadowFrag():SV_Target{return 0;}
 ENDHLSL
 }
 }
}
