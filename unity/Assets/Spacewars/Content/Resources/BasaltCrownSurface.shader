Shader "Spacewars/BasaltCrownSurface"
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
  _SiteFinish("Site links and feather",Vector)=(2,.4,0,0)
  _SiteTint("Site paving tint",Vector)=(.27,.25,.23,1)
  _MineFinish("Apron radius feather",Vector)=(4.75,.85,0,0)
  _MineTint("Mine apron stone",Vector)=(.23,.21,.19,1)
  [PerRendererData] _DetailKind("Combined geometry kind",Float)=0
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
 float4 _RoadLayout,_RoadSlabs,_RoadFinish,_Geology,_MineFinish,_MineTint,_SiteFinish,_SiteTint;float _DetailKind;
 float4 _MineCenters[4],_SitePads[32],_SiteHubs[32];int _SitePadCount;
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
 // Analytic union keeps the ring/causeway junctions open; no seams on mesh boundaries.
 float2 roadField(float2 p){
   float2 q=abs(p)-_RoadLayout.xy;
   float outer=max(q.x-_RoadLayout.w,q.y-_RoadLayout.w);
   float inner=max(q.x+_RoadLayout.w,q.y+_RoadLayout.w);
   float ring=max(outer,-inner);
   float central=max(abs(p.x)-_RoadLayout.x,abs(p.y)-_RoadLayout.z);
   return float2(min(ring,central),central<ring?0:step(q.y,q.x));
 }
 float lineAA(float distance,float width,float aa){return saturate((width-distance)/aa+.5)-saturate((-width-distance)/aa+.5);}
 float3 dressedRoad(float3 natural,float3 stone,float3 sand,float3 p,float upward){
   float2 field=roadField(p.xz);float aa=max(fwidth(field.x),.008);
   float coverage=(1-smoothstep(-_RoadFinish.z,_RoadFinish.z,field.x))*upward*_RoadSurface;
   // Swap slab axes on north/south lanes. Courses continue across ramp/flat joins.
   float2 course=lerp(p.xz,p.zx,field.y);
   float row=floor(course.y/_RoadSlabs.y);
   float2 cell=float2(course.x/_RoadSlabs.x+frac(row*.5),course.y/_RoadSlabs.y);
   float2 inCell=frac(cell),metres=min(inCell,1-inCell)*_RoadSlabs.xy;
   float seam=lineAA(min(metres.x,metres.y),_RoadSlabs.z,max(fwidth(course.x),fwidth(course.y)));
   float slabTone=lerp(.93,1.07,hash(floor(cell)));
   float3 paving=_RoleTints[2].rgb*lerp(1,stone,_RoadFinish.w)*slabTone;
   seam*=lerp(.35,1,noise(p.xz*1.7+5));
   paving*=1-seam*_RoadSlabs.w;
   // Sparse hairline cracks, weathered rather than a second regular tile selector.
   float crack=abs(inCell.x-(.35+.18*noise(float2(row,inCell.y*5))));
   float crackMask=lineAA(crack*_RoadSlabs.x,_RoadSlabs.z*.32,max(fwidth(course.x),.008))*step(.78,hash(floor(cell)+73));
   paving*=1-crackMask*_RoadSlabs.w*.5;
   float edge=1-smoothstep(_RoadFinish.x,_RoadFinish.x+aa,abs(field.x+_RoadFinish.x));
   float groove=lineAA(abs(field.x+_RoadFinish.x*2.2),_RoadSlabs.z,aa);
   float edgeSegment=step(.14,frac(course.x/(_RoadSlabs.x*.5)));
   paving=lerp(paving,_RoleTints[2].rgb*lerp(1,sand,.25)*1.12,edge*edgeSegment*.8);
   paving*=1-groove*_RoadSlabs.w*.6;
   float dust=_RoadFinish.y*saturate(noise(p.xz*.45+17)*.6+.45+field.x*.3);
   paving=lerp(paving,_RoleTints[3].rgb*lerp(1,sand,.45),dust);
   return lerp(natural,paving,coverage);
 }
 float apronDistance(float2 p,float2 center){float2 q=abs(p-center);return max(max(q.x,q.y),(q.x+q.y)*.70710678)-_MineFinish.x;}
 float3 mineApron(float3 natural,float3 stone,float3 sand,float3 p,float upward){
   float d=1000;
   [unroll] for(int i=0;i<4;i++)d=min(d,apronDistance(p.xz,_MineCenters[i].xy));
   float dustEdge=noise(p.xz*1.4)*_MineFinish.y*.3;
   float mask=(1-smoothstep(-_MineFinish.y,0,d+dustEdge))*upward*_RoadSurface;
   float rim=lineAA(abs(d+_MineFinish.y),.075,max(fwidth(d),.01));
   float3 apron=_MineTint.rgb*lerp(1,stone,.45);
   // Mineral flecks follow the existing textures, not a regular tile selector.
   apron=lerp(apron,_RoleTints[5].rgb*lerp(1,sand,.4),smoothstep(.57,.75,noise(p.xz*.75+91))*.32);
   apron*=1-rim*.28;
   return lerp(natural,apron,mask);
 }
 float3 siteApron(float3 natural,float3 stone,float3 p,float upward){
   float d=1000;
   [loop] for(int i=0;i<_SitePadCount;i++){
     float2 q=abs(p.xz-_SitePads[i].xy);
     float pad=max(max(q.x,q.y),(q.x+q.y)*.70710678)-_SitePads[i].z;
     float2 a=p.xz-_SiteHubs[i].xy,b=_SitePads[i].xy-_SiteHubs[i].xy;
     float t=saturate(dot(a,b)/max(dot(b,b),.0001));
     d=min(d,min(pad,length(a-t*b)-_SiteFinish.x*.5));
   }
   float mask=(1-smoothstep(-_SiteFinish.y,0,d))*upward*_RoadSurface;
   float2 cell=p.xz/_RoadSlabs.xy;float2 joints=min(frac(cell),1-frac(cell))*_RoadSlabs.xy;
   float seam=lineAA(min(joints.x,joints.y),_RoadSlabs.z,max(fwidth(p.x),fwidth(p.z)));
   float rim=lineAA(abs(d+_SiteFinish.y*2),_RoadSlabs.z,max(fwidth(d),.01));
   float3 paving=_SiteTint.rgb*lerp(1,stone,_RoadFinish.w);
   paving*=1-max(seam,rim)*_RoadSlabs.w;
   return lerp(natural,paving,mask);
 }
 half4 frag(V v):SV_Target {
 float2 mapUV=v.p.xz/(2*_FogBounds.xy)+.5;float4 a=SAMPLE_TEXTURE2D(_PaintA,sampler_PaintA,mapUV),b=SAMPLE_TEXTURE2D(_PaintB,sampler_PaintB,mapUV);
 float total=max(dot(a,1)+b.x+b.y+b.z,.001);a/=total;b/=total;
 float2 uv=detailUV(v.p);float3 weights=variants(v.p.xz),dark=1,stone=1,sand=1;
 // Mostly one sibling pattern is visible; blending occurs only at region edges.
 if(a.x+b.x>.001)dark=FAMILY(Dark,sampler_DarkTex,uv,weights);
 stone=FAMILY(Stone,sampler_StoneTex,uv,weights);
 sand=FAMILY(Sand,sampler_SandTex,uv,weights);
 dark=lerp(1,clamp(dark,.2,2.5),_DetailSettings.w);stone=lerp(1,clamp(stone,.2,2.5),_DetailSettings.w);sand=lerp(1,clamp(sand,.2,2.5),_DetailSettings.w);
 float3 albedo=dark*(a.x*_RoleTints[0].rgb+b.x*_RoleTints[4].rgb)+stone*(a.y*_RoleTints[1].rgb+b.y*_RoleTints[5].rgb)+sand*(a.z*_RoleTints[2].rgb+a.w*_RoleTints[3].rgb+b.z*_RoleTints[6].rgb);
 float3 n=normalize(v.n);
 albedo=mineApron(albedo,stone,sand,v.p,smoothstep(.7,.9,n.y));
 albedo=dressedRoad(albedo,stone,sand,v.p,smoothstep(.7,.9,n.y));
 albedo=siteApron(albedo,stone,v.p,smoothstep(.7,.9,n.y));
 if(_DetailKind>.5){
   if(_DetailKind<1.5)albedo=_RoleTints[2].rgb*lerp(1,stone,.35);
   else if(_DetailKind<2.5)albedo=_RoleTints[1].rgb*lerp(1,stone,.7);
   else albedo=lerp(_RoleTints[5].rgb,_RoleTints[2].rgb,.3)*lerp(1,sand,.5);
 }
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
