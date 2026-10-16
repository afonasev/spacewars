Shader "Spacewars/MossHighlandSurface"
{
 Properties {
  _FogMask("Native fog",2D)="black" {} _FogColor("Fog",Color)=(.04,.04,.04,1) _FogBounds("Bounds",Vector)=(56,56,0,0)
  _Transitions("Cliff foot and apron distances",2D)="black" {}
  _PaintA("Dark stone light scree weights",2D)="black" {} _PaintB("Cooled mineral sand weights",2D)="black" {}
  _DarkTex("Dark basalt",2D)="gray" {} _DarkTexB("Dark B",2D)="gray" {} _DarkTexC("Dark C",2D)="gray" {}
  _StoneTex("Stone",2D)="gray" {} _StoneTexB("Stone B",2D)="gray" {} _StoneTexC("Stone C",2D)="gray" {}
  _SandTex("Mineral",2D)="gray" {} _SandTexB("Mineral B",2D)="gray" {} _SandTexC("Mineral C",2D)="gray" {}
  _DarkVariantGain("Dark calibration",Vector)=(1,1,1,.04) _StoneVariantGain("Stone calibration",Vector)=(1,1,1,.11) _SandVariantGain("Sand calibration",Vector)=(1,1,1,.45)
  _ConcreteTex("Concrete",2D)="gray" {} _ConcreteTexB("Concrete B",2D)="gray" {} _ConcreteTexC("Concrete C",2D)="gray" {}
  _ConcreteVariantGain("Concrete calibration",Vector)=(1,1,1,.38)
  _DetailSettings("Tile region blend contrast",Vector)=(7,23,.06,.8)
  _RoadLayout("Authored ring X Z causeway half width bypass half width",Vector)=(40,32,4,3)
  _RoadSlabs("Slab length width joint contrast",Vector)=(2.75,2,.03,.3)
  _RoadFinish("Flush edging dust feather grain",Vector)=(.25,.35,.4,.18)
  _Geology("Strata spacing contrast",Vector)=(.4,.2,0,0)
  [PerRendererData] _BorderSurface("Natural boundary role",Float)=0
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
 TEXTURE2D(_Transitions);SAMPLER(sampler_Transitions);
 TEXTURE2D(_PaintA);SAMPLER(sampler_PaintA);TEXTURE2D(_PaintB);SAMPLER(sampler_PaintB);
 TEXTURE2D(_DarkTex);TEXTURE2D(_DarkTexB);TEXTURE2D(_DarkTexC);SAMPLER(sampler_DarkTex);
 TEXTURE2D(_StoneTex);TEXTURE2D(_StoneTexB);TEXTURE2D(_StoneTexC);SAMPLER(sampler_StoneTex);
 TEXTURE2D(_SandTex);TEXTURE2D(_SandTexB);TEXTURE2D(_SandTexC);SAMPLER(sampler_SandTex);
 TEXTURE2D(_ConcreteTex);TEXTURE2D(_ConcreteTexB);TEXTURE2D(_ConcreteTexC);SAMPLER(sampler_ConcreteTex);
 CBUFFER_START(UnityPerMaterial)
 float4 _FogBounds,_FogColor,_DetailSettings,_DarkVariantGain,_StoneVariantGain,_SandVariantGain;
 float4 _RoleTints[7];float _NormalSlope,_ReceiverBias,_RoadSurface,_BorderSurface;
 float4 _BorderSettings;
 float4 _Scree,_EdgeTransition,_CliffVariation,_RouteReadability;
 float4 _NaturalPattern,_Geology,_HighlandPattern,_ApronSlabs,_ApronFinish,_ApronCovers,_RoleDetail,_ApronWeather,_ConcreteVariantGain;
 CBUFFER_END
 struct A {float4 positionOS:POSITION;float3 normalOS:NORMAL;float2 uv:TEXCOORD0;};
 struct V {float4 positionCS:SV_POSITION;float3 p:TEXCOORD0;float3 n:TEXCOORD1;float shore:TEXCOORD2;};
 V vert(A a){V v;v.p=TransformObjectToWorld(a.positionOS.xyz);v.positionCS=TransformWorldToHClip(v.p);v.n=TransformObjectToWorldNormal(a.normalOS);v.shore=a.uv.x;return v;}
 float hash(float2 p){return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
 float noise(float2 p){float2 c=floor(p),f=frac(p);f=f*f*(3-2*f);return lerp(lerp(hash(c),hash(c+float2(1,0)),f.x),lerp(hash(c+float2(0,1)),hash(c+1),f.x),f.y);}
 float3 variants(float2 w){float2 p=w/_DetailSettings.y;p+=float2(noise(p*.73+19),noise(p*.81+43))-.5;
 float3 s=float3(noise(p+float2(7,31)),noise(p+float2(53,11)),noise(p+float2(29,71)));float top=max(s.x,max(s.y,s.z));float3 a=smoothstep(top-_DetailSettings.z,top,s);return a/max(dot(a,1),.0001);}
 // Ground and ramps share undistorted XZ coordinates. Vertical rock gets a
 // side projection with equal texel scale; never average several sharp images.
 float2 detailPosition(float3 p,float3 n){return abs(n.y)>.5?p.xz:float2(p.x+p.z,-p.y);}
 #define FAMILY(role,smp,uv,vw) ((SAMPLE_TEXTURE2D(_##role##Tex,smp,uv).rgb*vw.x + SAMPLE_TEXTURE2D(_##role##TexB,smp,uv).rgb*_##role##VariantGain.y*vw.y + SAMPLE_TEXTURE2D(_##role##TexC,smp,uv).rgb*_##role##VariantGain.z*vw.z)/max(_##role##VariantGain.w,.001))
 // Nine neighbouring cells avoid a visible selection grid. Chips are flat shading.
 float2 chips(float2 p,float density){
  float2 q=p/_Scree.z,c=floor(q);float coverage=0,light=0;
  float aa=max(length(fwidth(q))*.5,.015);
  for(int y=-1;y<=1;y++)for(int x=-1;x<=1;x++){
   float2 cell=c+float2(x,y);float seed=hash(cell+17);
   float2 centre=cell+float2(hash(cell+31),hash(cell+73));
   float2 d=q-centre;float angle=seed*6.2831853;float sn=sin(angle),cs=cos(angle);
   d=mul(float2x2(cs,-sn,sn,cs),d);
   float radius=lerp(.13,.36,hash(cell+97));
   float shape=max(abs(d.x)*.85+abs(d.y)*.55,max(abs(d.x)*.45+abs(d.y),abs(d.x)));
   float chip=(1-smoothstep(radius-aa,radius+aa,shape))*step(seed,density);
   coverage+=chip;light+=chip*(.75+hash(cell+123)*.45+clamp((d.y-d.x)/max(radius,.01),-1,1)*.2);
  }
  // Hash can equal zero exactly: zero density must still yield no chips.
  return float2(saturate(coverage)*step(.000001,density),light/max(coverage,.001));
 }
 half4 frag(V v):SV_Target {
 float2 mapUV=v.p.xz/(2*_FogBounds.xy)+.5;float4 a=SAMPLE_TEXTURE2D(_PaintA,sampler_PaintA,mapUV),b=SAMPLE_TEXTURE2D(_PaintB,sampler_PaintB,mapUV);
 float total=max(dot(a,1)+b.x+b.y+b.z,.001);a/=total;b/=total;
 float2 texelPosition=detailPosition(v.p,normalize(v.n));float3 weights=variants(v.p.xz);
 // Mostly one sibling pattern is visible; blending occurs only at region edges.
 float2 rockPosition=texelPosition/_RoleDetail.x;
 float3 cliffWeights=variants(v.p.xz*_DetailSettings.y/_CliffVariation.y);
 float3 dark=(SAMPLE_TEXTURE2D(_DarkTex,sampler_DarkTex,rockPosition).rgb*cliffWeights.x
  +SAMPLE_TEXTURE2D(_DarkTexB,sampler_DarkTex,mul(float2x2(.62,-.78,.78,.62),rockPosition)+.37).rgb*_DarkVariantGain.y*cliffWeights.y
  +SAMPLE_TEXTURE2D(_DarkTexC,sampler_DarkTex,mul(float2x2(.36,.93,-.93,.36),rockPosition)+.71).rgb*_DarkVariantGain.z*cliffWeights.z)/max(_DarkVariantGain.w,.001);
 // Continuous low-frequency UV distortion and sibling-specific orientation
 // remove repeated diagonal rock rows without averaging unrelated projections.
 float2 rocky=texelPosition+_NaturalPattern.w*(float2(noise(texelPosition/_DetailSettings.x+17),noise(texelPosition/_DetailSettings.x+59))-.5);
 float2 uv=rocky/_DetailSettings.x;
 float3 stone=(SAMPLE_TEXTURE2D(_StoneTex,sampler_StoneTex,uv).rgb*weights.x
  +SAMPLE_TEXTURE2D(_StoneTexB,sampler_StoneTex,float2(-uv.y,uv.x)+.37).rgb*_StoneVariantGain.y*weights.y
  +SAMPLE_TEXTURE2D(_StoneTexC,sampler_StoneTex,-uv+.63).rgb*_StoneVariantGain.z*weights.z)/max(_StoneVariantGain.w,.001);
 float2 soilUV=rocky/_RoleDetail.y;
 float3 sand=(SAMPLE_TEXTURE2D(_SandTex,sampler_SandTex,soilUV).rgb*weights.x
  +SAMPLE_TEXTURE2D(_SandTexB,sampler_SandTex,float2(-soilUV.y,soilUV.x)+.23).rgb*_SandVariantGain.y*weights.y
  +SAMPLE_TEXTURE2D(_SandTexC,sampler_SandTex,-soilUV+.71).rgb*_SandVariantGain.z*weights.z)/max(_SandVariantGain.w,.001);
 float3 concreteDetail=FAMILY(Concrete,sampler_ConcreteTex,v.p.xz/_RoleDetail.z,weights);
 // Keep bright mineral edges and pigment; low-contrast clipping erased stones.
 dark=lerp(1,max(dark,0),_DetailSettings.w);stone=lerp(1,max(stone,0),_DetailSettings.w);sand=lerp(1,max(sand,0),_DetailSettings.w);
 // Natural mask warp is shared across roles; the sharp construction mask is unwarped.
 float2 warp=(float2(noise(v.p.xz*.28+11),noise(v.p.xz*.31+37))-.5)*_NaturalPattern.z/(2*_FogBounds.xy);
 float4 wa=SAMPLE_TEXTURE2D(_PaintA,sampler_PaintA,mapUV+warp),wb=SAMPLE_TEXTURE2D(_PaintB,sampler_PaintB,mapUV+warp);
 float construction=a.z;wa.z=0;float sum=dot(wa,1)+wb.x+wb.y+wb.z;
 // A warped sample can land entirely inside concrete while this pixel is outside.
 // Recover its original natural weights instead of producing a black unpainted seam.
 if(sum<.001){wa=a;wa.z=0;wb=b;sum=dot(wa,1)+wb.x+wb.y+wb.z;}
 sum=max(sum,.001);
 a=wa/sum*(1-construction);b=wb/sum*(1-construction);a.z=construction;
 // Two spatial scales form connected natural patches, independent of albedo variants.
 float2 terrain=v.p.xz/_NaturalPattern.x;
 float patch=noise(terrain+19)*.7+noise(terrain*2.7+41)*.3;
 float damp=1-smoothstep(_NaturalPattern.y-_HighlandPattern.y,_NaturalPattern.y+_HighlandPattern.y,patch);
 damp*=step(.001,_NaturalPattern.y);
 float outcrop=1-smoothstep(_HighlandPattern.x-_HighlandPattern.y,_HighlandPattern.x+_HighlandPattern.y,(noise(terrain*2.3+83)*.7+noise(terrain*6.9+53)*.3));
 outcrop*=step(.001,_HighlandPattern.x);
 float macro=1+(noise(terrain*.6+7)-.5)*_HighlandPattern.z;
 float3 luminanceWeights=float3(.2126,.7152,.0722);
 float3 moss=lerp(dot(stone,luminanceWeights)*_RoleTints[4].rgb,stone*dot(_RoleTints[4].rgb,luminanceWeights),_RoleDetail.w)*macro;
 float3 earth=sand*_RoleTints[5].rgb;
 float3 natural=lerp(lerp(moss,earth,damp),dark*_RoleTints[1].rgb,outcrop);
 float3 n=normalize(v.n);
 // Construction stays on the authored mask. Details are flat shading, not obstacles.
 float4 route=SAMPLE_TEXTURE2D(_Transitions,sampler_Transitions,mapUV);
 float sideRole=step(.5,route.a)*(1-step(.85,route.a)),centreRole=step(.85,route.a);
 float2 slab=v.p.xz/_ApronSlabs.x;
 // Side service pads have longer slabs; centre has a restrained concentric motif.
 slab.x/=1+sideRole;
 float2 edge=min(frac(slab),1-frac(slab))*_ApronSlabs.x;
 float pixel=max(max(fwidth(v.p.x),fwidth(v.p.z)),.001);
 float joint=1-smoothstep(_ApronSlabs.y*.5,_ApronSlabs.y*.5+pixel,min(edge.x,edge.y));
 float3 concrete=pow(_ApronFinish.x,2.2).xxx*(1+(hash(floor(slab))-.5)*_ApronSlabs.w);
 concrete*=max(.1,1+(concreteDetail-1)*(1+_ApronWeather.x));
 float stain=1-smoothstep(.22,.55,noise(v.p.xz/_ApronSlabs.x+59));
 concrete*=1-stain*_ApronWeather.x*.4;
 concrete*=1-joint*_ApronSlabs.z;
 concrete*=1-sideRole*_RouteReadability.y*.45;
 float centreBand=1-smoothstep(.25,.25+pixel,abs(length(v.p.xz)-5.5));
 concrete*=1-centreRole*centreBand*_RouteReadability.y;
 // Four mask taps find the inside edge of arbitrary authored apron shapes.
 float2 offset=_ApronFinish.y/(2*_FogBounds.xy);
 float east=SAMPLE_TEXTURE2D(_PaintA,sampler_PaintA,mapUV+float2(offset.x,0)).b;
 float west=SAMPLE_TEXTURE2D(_PaintA,sampler_PaintA,mapUV-float2(offset.x,0)).b;
 float north=SAMPLE_TEXTURE2D(_PaintA,sampler_PaintA,mapUV+float2(0,offset.y)).b;
 float south=SAMPLE_TEXTURE2D(_PaintA,sampler_PaintA,mapUV-float2(0,offset.y)).b;
 float ex=1-min(east,west),ez=1-min(north,south);
 float along=ex>ez?v.p.z:v.p.x;
 float drain=smoothstep(.25,.75,max(ex,ez));
 // Crisp rectangular slots, antialiased at their edges; fade to the mean
 // only when the grating is smaller than a screen pixel.
 float bar=frac(along/_ApronFinish.z);
 float barAA=max(fwidth(along)/_ApronFinish.z,.001);
 float grate=smoothstep(.25-barAA,.25+barAA,bar)*(1-smoothstep(.75-barAA,.75+barAA,bar));
 grate=lerp(grate,.5,saturate(barAA));
 float3 grateMetal=float3(.055,.06,.06)*(1-grate*.8);
 concrete=lerp(concrete,grateMetal,drain*_ApronFinish.w);
 float2 coverDistance=abs(frac(slab)-.5)*_ApronSlabs.x;
 float coverEdge=max(coverDistance.x,coverDistance.y);
 float coverEnabled=step(1-_ApronCovers.y,hash(floor(slab)+37));
 float cover=coverEnabled*(1-smoothstep(_ApronCovers.x*.5,_ApronCovers.x*.5+pixel,coverEdge));
 float rim=smoothstep(_ApronCovers.x*.5-_ApronSlabs.y*2-pixel,_ApronCovers.x*.5-_ApronSlabs.y*2,coverEdge);
 concrete*=1-cover*lerp(.2,_ApronSlabs.z,rim);
 float2 ingress=_ApronWeather.y/(2*_FogBounds.xy);
 float inside=min(min(SAMPLE_TEXTURE2D(_PaintA,sampler_PaintA,mapUV+float2(ingress.x,0)).b,SAMPLE_TEXTURE2D(_PaintA,sampler_PaintA,mapUV-float2(ingress.x,0)).b),min(SAMPLE_TEXTURE2D(_PaintA,sampler_PaintA,mapUV+float2(0,ingress.y)).b,SAMPLE_TEXTURE2D(_PaintA,sampler_PaintA,mapUV-float2(0,ingress.y)).b));
 float edgeMoss=(1-inside)*smoothstep(.35,.7,noise(v.p.xz*1.3+21))*_ApronWeather.z*(1-drain);
 concrete=lerp(concrete,moss,edgeMoss);
 // Narrow compacted ground outside the exact engineered mask. No footprint growth.
 float2 transition=SAMPLE_TEXTURE2D(_Transitions,sampler_Transitions,mapUV).rg;
 float edgeSoil=transition.g*(.8+.2*noise(v.p.xz*2.1));
 float3 compacted=lerp(earth,_RoleTints[5].rgb,_EdgeTransition.w)*_Scree.w;
 natural=lerp(natural,compacted,edgeSoil);
 earth=lerp(earth,compacted,edgeSoil);
 float dust=drain*_EdgeTransition.y*(.6+.4*noise(v.p.xz*1.7+51));
 concrete=lerp(concrete,earth,dust*(1-grate*.8));
 float drainGrowth=drain*smoothstep(.63,.79,noise(v.p.xz*1.9+67))*_EdgeTransition.z;
 concrete=lerp(concrete,moss,drainGrowth*(1-grate));
 // Soil follows the actual ramp mask, including short level approaches.
 float ramp=route.b*_RouteReadability.x*saturate(n.y);
 float3 rampGround=lerp(earth,_RoleTints[5].rgb,.18)*1.05;
 natural=lerp(natural,rampGround,ramp);earth=lerp(earth,rampGround,ramp);
 float3 albedo=dark*(a.x*_RoleTints[0].rgb+a.y*_RoleTints[1].rgb)+natural*b.x+earth*b.y+concrete*a.z+sand*(a.w*_RoleTints[3].rgb+b.z*_RoleTints[6].rgb);
 // Replace rocky ground paint on the inclined traversable surface as well.
 albedo=lerp(albedo,rampGround,ramp*(1-construction));
 // Chips thin out with distance; soil remains visible between individual stones.
 float scree=transition.r*(1-construction)*saturate(n.y);
 float2 debris=chips(v.p.xz,_Scree.y*scree*(.55+.45*noise(v.p.xz*1.7+91)));
 float3 bed=lerp(albedo,earth,scree*.65);
 // Reuse the actual rock family at chip scale, retaining sharp fractured pigment.
 float3 chipDetail=SAMPLE_TEXTURE2D(_DarkTex,sampler_DarkTex,v.p.xz/(_Scree.z*2)).rgb/max(_DarkVariantGain.w,.001);
 float3 chipColor=_RoleTints[1].rgb*debris.y*lerp(1,chipDetail,_DetailSettings.w);
 albedo=lerp(bed,chipColor,debris.x);
 if(_BorderSurface>.5 && _BorderSurface<1.5){
  float growth=smoothstep(.3,.85,n.y)*_BorderSettings.x*smoothstep(.15,.65,noise(v.p.xz*.19+81));
  albedo=lerp(dark*_RoleTints[1].rgb*.78,moss,growth);
 }
 if(_BorderSurface>1.5){
  float water=smoothstep(.45,1.25,v.shore);
  float ripple=sin((v.p.z+v.p.x*.3)*13+noise(v.p.xz*1.8+_Time.y*.06)*7-_Time.y*.9)*.5+.5;
  ripple=pow(ripple,16)*smoothstep(.55,.8,noise(v.p.xz*3+_Time.y*.03))*_BorderSettings.z;
  float3 deep=pow(float3(.36,.85,1)*_BorderSettings.y,2.2);
  float3 current=deep*(.8+.3*noise(v.p.xz*.9))+ripple*.11;
  albedo=lerp(dark*_RoleTints[1].rgb*.85,current,water);
 }
 // Layered rock with nonuniform weathering; continuous across mesh boundaries.
 float2 geology=v.p.xz/_CliffVariation.y;
 float tilt=(noise(geology+117)-.5)*2*_CliffVariation.x;
 float layer=(v.p.y+sin(geology.x*2.1+noise(geology+61))*_CliffVariation.y*tilt)/_Geology.x+noise(v.p.xz*.35)*.35;
 float band=1-smoothstep(.04,.16,abs(frac(layer)-.5));
 float crevice=smoothstep(.35,.65,noise(float2(v.p.x+v.p.z,v.p.y)*.7));
 albedo*=1-(band*_Geology.y+crevice*_Geology.z)*(1-saturate(n.y));
 // Material separation keeps lit cliff steps readable even without cast shadows.
 // Classify using the geometric normal, before micro-relief; leave ground/ramps alone.
 float cliffFace=(1-smoothstep(.25,.75,abs(n.y)))*max(saturate(a.x+a.y),step(.5,_BorderSurface)*(1-step(1.5,_BorderSurface)));
 albedo*=1-cliffFace*_CliffVariation.z;
 float grain=dot(albedo,float3(.2126,.7152,.0722));
 float3 dx=ddx(v.p),dy=ddy(v.p),r1=cross(dy,n),r2=cross(n,dx);float det=dot(dx,r1);
 float3 slope=(r1*ddx(grain)+r2*ddy(grain))*sign(det)/max(abs(det),.00001);
 if(!any(slope!=slope)){slope=clamp(slope,-_NormalSlope,_NormalSlope);slope*=min(1,_NormalSlope/max(length(slope),.0001));n=normalize(n-slope);}
 float receiverOffset=_BorderSurface>.5&&_BorderSurface<1.5?_BorderSettings.w:_ReceiverBias;
 Light sun=GetMainLight(TransformWorldToShadowCoord(v.p+normalize(v.n)*receiverOffset));
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
