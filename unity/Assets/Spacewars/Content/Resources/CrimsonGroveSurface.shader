Shader "Spacewars/CrimsonGroveSurface" {
 Properties {
 _FogMask("Native fog",2D)="black" {} _FogBounds("Bounds",Vector)=(160,160,0,0) _FogColor("Fog",Color)=(.04,.04,.04,1)
 [PerRendererData] _BaseColor("Tint",Color)=(.5,.5,.5,1) [PerRendererData] _Role("Ground rock leaves",Float)=0
 _EarthTex("Earth",2D)="gray" {} _EarthTexB("Earth B",2D)="gray" {} _EarthTexC("Earth C",2D)="gray" {}
 _RockTex("Rock",2D)="gray" {} _RockTexB("Rock B",2D)="gray" {} _RockTexC("Rock C",2D)="gray" {}
 _EarthVariantGain("Earth calibration",Vector)=(1,1,1,.135) _RockVariantGain("Rock calibration",Vector)=(1,1,1,.136)
 _Detail("Tile patch blend contrast",Vector)=(5,23,.06,.7)
 _SurfaceMask("Foot distance and worn routes",2D)="red" {}
 _ConcreteTex("Concrete",2D)="gray" {} _ConcreteTexB("Concrete B",2D)="gray" {} _ConcreteTexC("Concrete C",2D)="gray" {}
 _ConcreteVariantGain("Concrete calibration",Vector)=(1,1,1,.385)
 _GroundFinish("Macro scale contrast road value foot reach",Vector)=(18,.22,.37,3)
 _RockFinish("Strata contrast fractures normal",Vector)=(1.2,.2,3.5,.075)
 _PadFinish("Pour size wear concrete",Vector)=(5,.22,.63,0)
 _RoadTint("Compacted clay",Color)=(.37,.326,.252,1)
 _MineralTint("Limestone dust",Color)=(.73,.68,.6,1)
 _TerraceTint("Cool upper mineral soil",Color)=(.55,.6,.62,1)
 _TerraceHeight("Upper support height",Float)=3
 _Geology("Bedrock coverage mineral sheet contrast",Vector)=(.5,.3,0,0)
 _Deposits("Road burial deposit scale gravel size coverage",Vector)=(.48,4.5,.35,.4)
 [PerRendererData] _PadOrigin("Pad origin",Vector)=(0,0,0,0)
 }
 SubShader { Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque"}
 Pass { Tags {"LightMode"="UniversalForward"}
 HLSLPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
 #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
 TEXTURE2D(_FogMask);SAMPLER(sampler_FogMask);
 TEXTURE2D(_EarthTex);TEXTURE2D(_EarthTexB);TEXTURE2D(_EarthTexC);SAMPLER(sampler_EarthTex);
 TEXTURE2D(_RockTex);TEXTURE2D(_RockTexB);TEXTURE2D(_RockTexC);SAMPLER(sampler_RockTex);
 TEXTURE2D(_ConcreteTex);TEXTURE2D(_ConcreteTexB);TEXTURE2D(_ConcreteTexC);SAMPLER(sampler_ConcreteTex);
 TEXTURE2D(_SurfaceMask);SAMPLER(sampler_SurfaceMask);
 CBUFFER_START(UnityPerMaterial)
 float4 _BaseColor,_FogBounds,_FogColor,_EarthVariantGain,_RockVariantGain,_ConcreteVariantGain,_Detail,_GroundFinish,_RockFinish,_PadFinish,_MineralTint,_PadOrigin,_RoadTint,_TerraceTint,_Deposits,_Geology;float _Role,_TerraceHeight;
 CBUFFER_END
 struct A{float4 positionOS:POSITION;float3 normalOS:NORMAL;float4 color:COLOR;};
 struct V{float4 positionCS:SV_POSITION;float3 p:TEXCOORD0;float3 n:TEXCOORD1;float4 color:COLOR;};
 V vert(A a){V v;v.color=a.color;v.p=TransformObjectToWorld(a.positionOS.xyz);v.positionCS=TransformWorldToHClip(v.p);v.n=TransformObjectToWorldNormal(a.normalOS);return v;}
 float hash(float2 p){return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
 float noise(float2 p){float2 c=floor(p),f=frac(p);f=f*f*(3-2*f);return lerp(lerp(hash(c),hash(c+float2(1,0)),f.x),lerp(hash(c+float2(0,1)),hash(c+1),f.x),f.y);}
 #define FAMILY(role,smp,uv,w) ((SAMPLE_TEXTURE2D(_##role##Tex,smp,uv).rgb*w.x+SAMPLE_TEXTURE2D(_##role##TexB,smp,uv).rgb*_##role##VariantGain.y*w.y+SAMPLE_TEXTURE2D(_##role##TexC,smp,uv).rgb*_##role##VariantGain.z*w.z)/_##role##VariantGain.w)
 float fractures(float2 p){
 float2 cell=floor(p),f=frac(p);float first=100,second=100;
 for(int y=-1;y<=1;y++)for(int x=-1;x<=1;x++){
 float2 offset=float2(x,y);float2 site=offset+float2(hash(cell+offset+17),hash(cell+offset+71));float d=length(site-f);
 if(d<first){second=first;first=d;}else second=min(second,d);
 }return 1-smoothstep(.015,.085,second-first);
 }
 half4 frag(V v):SV_Target{
 float3 n=normalize(v.n);float2 patch=v.p.xz/_Detail.y;
 patch+=float2(noise(patch*.73+17),noise(patch*.81+47))-.5;
 float3 scores=float3(noise(patch+17),noise(patch+53),noise(patch+91));float peak=max(scores.x,max(scores.y,scores.z));float3 w=smoothstep(peak-_Detail.z,peak,scores);w/=max(dot(w,1),.0001);
 float2 plane=abs(n.y)>.5?v.p.xz:abs(n.x)>abs(n.z)?v.p.zy:v.p.xy;float2 uv=plane/_Detail.x;
 float2 mapUV=v.p.xz/(2*_FogBounds.xy)+.5;float2 mask=SAMPLE_TEXTURE2D(_SurfaceMask,sampler_SurfaceMask,mapUV).rg;
 float3 detail=_Role<.5?FAMILY(Earth,sampler_EarthTex,uv,w):FAMILY(Rock,sampler_RockTex,uv,w);
 float3 albedo=_BaseColor.rgb;float macro=noise(v.p.xz/_GroundFinish.x+13);
 if(_Role<.5){
  float upper=smoothstep(.12,.9,v.p.y/max(_TerraceHeight,.01));
  albedo=lerp(albedo,_TerraceTint.rgb,upper);
  // Large mineral/silt sheets, crisp sibling albedo, and scattered coarse gravel
  // operate at independent scales; the upper/lower distinction stays legible.
  float2 broad=v.p.xz/_GroundFinish.x;
  broad+=float2(noise(broad*.8+31),noise(broad*.7+59))*.7;
  float deposit=noise(broad+71)*.75+noise(broad*2.1+17)*.25;
  float grainEdge=(noise(v.p.xz*.8+97)-.5)*.075;
  float bed=smoothstep(1-_Geology.x-.018,1-_Geology.x+.018,deposit+grainEdge);
  float3 dry=albedo*lerp(1,1.18,upper)*(1+_GroundFinish.y*(macro-.5)*2);
  float soilGrain=dot(detail,float3(.2126,.7152,.0722));
  dry*=lerp(1,clamp(soilGrain,.55,1.6),_Detail.w);
  float3 bedDetail=FAMILY(Rock,sampler_RockTex,uv,w);
  float mineralGrain=clamp(dot(bedDetail,float3(.2126,.7152,.0722)),.55,1.65);
  float3 bedTint=lerp(albedo*float3(.67,.8,.96),_TerraceTint.rgb*float3(.53,.66,.8),upper);
  bedTint*=lerp(1,mineralGrain,min(1,_Detail.w*1.6));
  detail=lerp(detail,bedDetail,bed);
  bedTint*=1-fractures(v.p.xz/(_GroundFinish.x*.24))*.12;
  dry=lerp(dry,bedTint,bed);
  float sheet=smoothstep(.46,.64,noise(broad*.7+113));
  dry=lerp(dry,lerp(albedo*1.22,_MineralTint.rgb*1.12,upper),sheet*_Geology.y);
  // Old road fragments emerge through irregular blankets of ground material.
  float burial=noise(v.p.xz/_Deposits.y+19)*.7+noise(v.p.xz/(_Deposits.y*.32)+43)*.3;
  float exposed=smoothstep(_Deposits.x-.08,_Deposits.x+.12,burial);
  float road=mask.g*exposed;
  float3 oldRoad=_RoadTint.rgb*lerp(.7,1.25,noise(v.p.xz/.8+29));
  oldRoad*=lerp(.85,1.1,clamp(soilGrain,.4,1.6));
  oldRoad*=1-fractures(v.p.xz/3)*.26;
  albedo=lerp(dry,oldRoad,road*.85);
  float dust=(1-smoothstep(0,_GroundFinish.w,mask.r*8))*lerp(.25,1,noise(v.p.xz/3+31));
  albedo=lerp(albedo,_MineralTint.rgb*.72,dust*.42);
  // Jittered nearest stones with independent radius and tint avoid a visible grid.
  float2 gp=v.p.xz/_Deposits.z,gc=floor(gp),gf=frac(gp);float stone=0,stoneShade=0;
  for(int gz=-1;gz<=1;gz++)for(int gx=-1;gx<=1;gx++){
   float2 off=float2(gx,gz),cell=gc+off;
   float key=hash(cell+37);float2 centre=float2(hash(cell+17),hash(cell+71));
   float2 delta=gf-off-centre;float radius=lerp(.1,.3,hash(cell+131));
   float dist=max(abs(delta.x+delta.y*lerp(-.6,.6,key)),abs(delta.y)*lerp(.7,1.6,hash(cell+29)));
   float aa=max(length(fwidth(gp))*.7,.025);
   float visible=(1-smoothstep(radius-aa,radius+aa,dist))*step(1-_Deposits.w,key);
   if(visible>stone){stone=visible;stoneShade=key;}
  }
  float gravelFade=1-smoothstep(_Deposits.z*.35,_Deposits.z*1.3,length(fwidth(v.p.xz)));
  float3 stoneTint=lerp(albedo*lerp(.65,1.7,stoneShade),_MineralTint.rgb*.4,.18);
  float gravelRegion=saturate((1-abs(bed*2-1))*2+dust)*smoothstep(.4,.7,noise(v.p.xz/2+83));
  albedo=lerp(albedo,stoneTint,stone*gravelFade*gravelRegion*.65);
  albedo*=1-(1-smoothstep(0,.65,mask.r*8))*.18;
 }else if(_Role<1.5){
  float crown=smoothstep(.55,.9,n.y);
  float3 capSoil=FAMILY(Earth,sampler_EarthTex,v.p.xz/_Detail.x,w);
  float soilCover=smoothstep(.3,.75,noise(v.p.xz/3+91))*crown;
  detail=lerp(detail,capSoil,soilCover*.75);
  albedo=lerp(albedo,albedo*float3(.74,.73,.69),soilCover*.6);
  float mineral=dot(detail,float3(.2126,.7152,.0722));albedo*=lerp(1,clamp(mineral,.45,1.7),_Detail.w);
  float mineralPatch=noise(v.p.xz/5+v.p.y*.11);
  albedo=lerp(albedo*float3(.7,.78,.85),albedo*1.12,smoothstep(.22,.78,mineralPatch));
  float bedding=(v.p.y+noise(v.p.xz/5)*.45)/_RockFinish.x;
  float layer=1-smoothstep(.02,.16,abs(frac(bedding)-.5));float face=1-smoothstep(.25,.85,abs(n.y));
  float cracks=fractures(plane/_RockFinish.z+float2(noise(plane/7),noise(plane/11))*.25);
  albedo*=1-_RockFinish.y*(cracks*.9+layer*face);
  float strataTone=noise(float2(floor(bedding)*7,17));
  albedo*=lerp(.78,1.16,strataTone);albedo*=lerp(.83,1,saturate(n.y*.5+.5));
 }else if(_Role<2.5){
  // Flat normals expose the individual prism facets; bright tips and dark roots
  // come from per-vertex mineral coloration, without a glowing gameplay cue.
  albedo*=v.color.rgb;
  float3 inclusion=FAMILY(Rock,sampler_RockTex,plane/.65,w);
  float grain=clamp(dot(inclusion,float3(.2126,.7152,.0722)),.5,1.65);
  albedo*=lerp(.82,1.16,saturate(grain-.4));
  float seams=fractures(plane*2.4+v.p.y*.37);
  albedo*=1-seams*.38;
  float veinLine=1-smoothstep(.014,.05,abs(frac(v.p.y*5+noise(v.p.xz*3)*.65)-.5));
  albedo=lerp(albedo,albedo*float3(1.5,1.3,1.4),veinLine*.22);
  float chips=smoothstep(.7,.87,noise(plane*19));
  albedo=lerp(albedo,albedo*1.4,chips*.3);
 }else{
  detail=FAMILY(Concrete,sampler_ConcreteTex,v.p.xz/(_Detail.x*.8),w);
  albedo*=lerp(1,clamp(dot(detail,float3(.2126,.7152,.0722)),.5,1.5),_Detail.w);
  // Geometry carries coarse clipped panel cells, seams and recessed fittings.
  // No world grid or fine repeated tiling competes with buildings.
  albedo*=v.color.rgb;
  float stain=smoothstep(.4,.7,noise(v.p.xz/3+29))*_PadFinish.y;
  albedo=lerp(albedo,albedo*float3(.85,.84,.82),stain);

 }
 // Bounded derivative relief cannot create nonfinite dark streaks at triangle seams.
 if(_Role<1.5||_Role>2.5){
 float grain=dot(detail,float3(.2126,.7152,.0722));float3 dx=ddx(v.p),dy=ddy(v.p),r1=cross(dy,n),r2=cross(n,dx);float det=dot(dx,r1);
 float3 slope=(r1*ddx(grain)+r2*ddy(grain))*sign(det)/max(abs(det),.00001);
 if(!any(slope!=slope)){slope=clamp(slope,-_RockFinish.w,_RockFinish.w);slope*=min(1,_RockFinish.w/max(length(slope),.0001));n=normalize(n-slope);}
 }
 Light sun=GetMainLight(TransformWorldToShadowCoord(v.p+normalize(v.n)*.025));
 float3 lit=albedo*(SampleSH(n)+sun.color*saturate(dot(n,sun.direction))*sun.shadowAttenuation);
 if(_Role>1.5&&_Role<2.5){float spec=pow(saturate(dot(reflect(-sun.direction,n),normalize(_WorldSpaceCameraPos-v.p))),32);lit+=sun.color*spec*.18;}
 float fog=SAMPLE_TEXTURE2D(_FogMask,sampler_FogMask,mapUV).r;
 return half4(lerp(lit,_FogColor.rgb,fog),1);
 }
 ENDHLSL
 }
 UsePass "Universal Render Pipeline/Lit/ShadowCaster"
 }
}
