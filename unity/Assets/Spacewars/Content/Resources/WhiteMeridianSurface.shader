Shader "Spacewars/WhiteMeridianSurface" {
 Properties {
 _Scorch("Scorch intensity reach softness",Vector)=(.48,3.5,.55,0)
 _PitRelief("Cosmetic pit slope",Float)=.12
 _SelectedStyle("Split spacing contrast burial skid",Vector)=(8,.22,1.1,6)
 _FogMask("Fog",2D)="black" {} _FogBounds("Bounds",Vector)=(80,80,0,0) _FogColor("Fog tint",Color)=(0,0,0,1)
 _SaltTex("Salt",2D)="gray" {} _SaltTexB("Salt B",2D)="gray" {} _SaltTexC("Salt C",2D)="gray" {}
 _RoadMask("Service tracks",2D)="black" {}
 _SoilTex("Soil",2D)="gray" {} _SoilTexB("Soil B",2D)="gray" {} _SoilTexC("Soil C",2D)="gray" {}
 _SoilVariantGain("Soil calibration",Vector)=(1,1,1,.14) _Landscape("Soil sand burial track",Vector)=(.7,.55,.55,.3) _FoundationArt("Foundation wear cracks wind",Vector)=(.4,.3,.72,.69)
 [PerRendererData] _SiteBounds("Foundation center half size",Vector)=(0,0,1,1)
 _RockTex("Rock",2D)="gray" {} _RockTexB("Rock B",2D)="gray" {} _RockTexC("Rock C",2D)="gray" {}
 _SaltVariantGain("Salt calibration",Vector)=(1,1,1,.38) _RockVariantGain("Rock calibration",Vector)=(1,1,1,.14)
 _SaltTint("Salt ivory",Color)=(.88,.827,.722,1) _DustTint("Peach dust",Color)=(.71,.49,.35,1) _RockTint("Gypsum",Color)=(.86,.79,.65,1) _CopperTint("Copper",Color)=(.47,.25,.13,1)
 _Detail("Tile regions blend contrast",Vector)=(5,21,.06,.3) _Geology("Dust scale coverage strata spacing contrast",Vector)=(18,.52,.8,.16)
 _MineralPatches("Asymmetric mineral deposits",Float)=.75
 _NormalSlope("Normal slope limit",Float)=.045 _CopperRadius("Copper apron radius",Float)=4.5
 [PerRendererData] _Kind("Ground rock ship",Float)=0 [PerRendererData] _BaseColor("Hull paint",Color)=(1,1,1,1)
 }
 SubShader { Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque"}
 Pass { Tags {"LightMode"="UniversalForward"} Cull Off
 HLSLPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
 #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
 TEXTURE2D(_RoadMask);SAMPLER(sampler_RoadMask);
 TEXTURE2D(_SoilTex);TEXTURE2D(_SoilTexB);TEXTURE2D(_SoilTexC);SAMPLER(sampler_SoilTex);
 TEXTURE2D(_FogMask);SAMPLER(sampler_FogMask);
 TEXTURE2D(_SaltTex);TEXTURE2D(_SaltTexB);TEXTURE2D(_SaltTexC);SAMPLER(sampler_SaltTex);
 TEXTURE2D(_RockTex);TEXTURE2D(_RockTexB);TEXTURE2D(_RockTexC);SAMPLER(sampler_RockTex);
 CBUFFER_START(UnityPerMaterial)
 float4 _FogBounds,_FogColor,_SaltTint,_DustTint,_RockTint,_CopperTint,_Detail,_Geology,_SaltVariantGain,_RockVariantGain,_SoilVariantGain,_BaseColor,_Landscape,_SiteBounds,_FoundationArt,_SelectedStyle,_Scorch;
 float _PitRelief,_Kind,_NormalSlope,_CopperRadius,_MineralPatches;float4 _Mines[8];float4 _ScorchSections[3];
 CBUFFER_END
 struct A{float4 positionOS:POSITION;float3 normalOS:NORMAL;float2 uv:TEXCOORD0;};struct V{float4 positionCS:SV_POSITION;float3 p:TEXCOORD0;float3 n:TEXCOORD1;float2 uv:TEXCOORD2;};
 V vert(A a){V v;v.uv=a.uv;v.p=TransformObjectToWorld(a.positionOS.xyz);v.n=TransformObjectToWorldNormal(a.normalOS);v.positionCS=TransformWorldToHClip(v.p);return v;}
 float hash(float2 p){return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
 float noise(float2 p){float2 c=floor(p),f=frac(p);f=f*f*(3-2*f);return lerp(lerp(hash(c),hash(c+float2(1,0)),f.x),lerp(hash(c+float2(0,1)),hash(c+1),f.x),f.y);}
 float3 variants(float2 w){float2 p=w/_Detail.y;p+=float2(noise(p*.73+19),noise(p*.81+43))-.5;float3 s=float3(noise(p+float2(7,31)),noise(p+float2(53,11)),noise(p+float2(29,71)));float m=max(s.x,max(s.y,s.z));float3 a=smoothstep(m-_Detail.z,m,s);return a/max(dot(a,1),.0001);}
 #define FAMILY(role,smp,uv,vw) ((SAMPLE_TEXTURE2D(_##role##Tex,smp,uv).rgb*vw.x+SAMPLE_TEXTURE2D(_##role##TexB,smp,uv).rgb*_##role##VariantGain.y*vw.y+SAMPLE_TEXTURE2D(_##role##TexC,smp,uv).rgb*_##role##VariantGain.z*vw.z)/max(_##role##VariantGain.w,.001))
 float fbm(float2 p){return noise(p)*.62+noise(p*2.17+19)*.26+noise(p*4.43+37)*.12;}
 float mineralFracture(float2 p){
   float2 cell=floor(p);float first=100,second=100;
   [unroll]for(int x=-1;x<=1;x++)[unroll]for(int z=-1;z<=1;z++){
     float2 key=cell+float2(x,z);
     float2 seed=key+.2+.6*float2(hash(key+17),hash(key+43));
     float distanceToSeed=length(p-seed);
     if(distanceToSeed<first){second=first;first=distanceToSeed;}
     else second=min(second,distanceToSeed);
   }
   float edge=second-first;
   return 1-smoothstep(.006,.022+fwidth(edge),edge);
 }
 half4 frag(V v):SV_Target{
 float3 n=normalize(v.n);float3 axis=abs(n);float2 uv=(axis.y>=max(axis.x,axis.z)?v.p.xz:(axis.x>axis.z?v.p.zy:v.p.xy))/_Detail.x;float3 weights=variants(v.p.xz);
 float3 salt=FAMILY(Salt,sampler_SaltTex,uv,weights),rock=FAMILY(Rock,sampler_RockTex,uv,weights);
 float3 soil=FAMILY(Soil,sampler_SoilTex,uv,weights);
 float mineral=fbm(v.p.xz/17+float2(17,41));
 float soilMask=smoothstep(.39,.58,mineral)*_Landscape.x;
 float dust=smoothstep(.30,.70,fbm(v.p.xz/_Geology.x+float2(29,7)))*_Geology.y;
 float saltGrain=dot(salt,float3(.2126,.7152,.0722)),rockGrain=dot(rock,float3(.2126,.7152,.0722)),soilGrain=dot(soil,float3(.2126,.7152,.0722));
 float rawGrain=_Kind<.5?lerp(saltGrain,soilGrain,soilMask):(_Kind<2.5?rockGrain:saltGrain);
 float grain=lerp(1,clamp(rawGrain,.5,1.7),_Detail.w);
 float3 sandTint=lerp(_SaltTint.rgb,_DustTint.rgb,.48);
 float wind=sin(dot(v.p.xz,float2(.65,.3))*8+noise(v.p.xz*.5)*5)*.5+.5;
 float3 albedo=lerp(_SaltTint.rgb,_DustTint.rgb,dust)*grain;
 albedo=lerp(albedo,float3(.56,.48,.37)*grain,soilMask*.85);
 float saltPatch=smoothstep(.49,.64,fbm(v.p.xz/13+float2(71,19)));
 if(_Kind<.5){
   float2 q=v.p.xz*80/_FogBounds.xy;
   // Authored, deliberately unmirrored mineral districts with eroded boundaries.
   float irregular=fbm(q*.13)*14;
   float chalk=1-smoothstep(14,40,length((q-float2(-8,36))*float2(.55,1))+irregular*.45);
   float compact=1-smoothstep(14,44,length((q-float2(15,-25))*float2(.65,1))+irregular*.4);
   albedo=lerp(albedo,_SaltTint.rgb*grain,max(saltPatch*.65,chalk*_MineralPatches));
   albedo=lerp(albedo,lerp(_SaltTint.rgb,_DustTint.rgb,.55)*grain,compact*_MineralPatches);
 }
 if(_Kind<.5&&n.y<.7)albedo=lerp(albedo,_RockTint.rgb*lerp(1,clamp(rockGrain,.5,1.7),_Detail.w),(1-smoothstep(.25,.7,n.y)));
 if(_Kind>.5)albedo=_RockTint.rgb*grain*lerp(.89,1.06,fbm(v.p.xz*.24));
 if(_Kind>1.5)albedo=_BaseColor.rgb*grain;
 if((_Kind>1.5&&_Kind<2.5)||_Kind>3.5){
   // Metal has its own scale of wear; gypsum texture must not flatten the hull.
   float mottling=fbm(v.p.xz*3.7+v.p.y*.8);
   float pitting=smoothstep(.69,.82,noise(v.p.xz*21+v.p.y*7));
   albedo=_BaseColor.rgb*lerp(.82,1.08,mottling);
   albedo=lerp(albedo,float3(.20,.12,.065),pitting*.12);
 }
 if(_Kind>3.5&&_Kind<4.5){
   float edge=min(min(v.uv.x,v.uv.y),min(1-v.uv.x,1-v.uv.y));
   albedo*=lerp(.72,1,smoothstep(.004,.055,edge));
   float streak=smoothstep(.78,.9,noise(float2(v.uv.x*61,floor(v.p.x*3))))*(1-v.uv.y);
   albedo=lerp(albedo,float3(.29,.22,.14),streak*.13);
 }
 if(_Kind>4.5)albedo=_BaseColor.rgb;
 if(_Kind>1.5&&_Kind<2.5){
   // Consistent lee-side burial on old hulls, flat pigment not a terrain obstacle.
   float buried=1-smoothstep(.12,_SelectedStyle.z+noise(v.p.xz*.23)*.6,v.p.y);
   albedo=lerp(albedo,sandTint*grain,buried*.32);
 }
 if(_Kind>.5&&_Kind<1.5){
   // Shallow branching mineral seams on the joined crown, not parallel stripes.
   float seam=mineralFracture(v.p.xz/_SelectedStyle.x);
   albedo*=1-seam*_SelectedStyle.y*smoothstep(.3,.8,n.y);
 }

 if(_Kind<.5){
   albedo*=1-(1-soilMask)*dust*wind*.065;
   float dist=1000;[unroll]for(int i=0;i<8;i++)dist=min(dist,distance(v.p.xz,_Mines[i].xy));
   float deposit=(1-smoothstep(_CopperRadius*.45,_CopperRadius,dist+noise(v.p.xz*1.7)*.7))*smoothstep(.7,.9,n.y);albedo=lerp(albedo,_CopperTint.rgb*grain,deposit*.65);
   float2 roadUv=v.p.xz/(2*_FogBounds.xy)+.5;
   float4 road=SAMPLE_TEXTURE2D(_RoadMask,sampler_RoadMask,roadUv);
   float2 texel=1.0/2048.0;
   float px=SAMPLE_TEXTURE2D(_RoadMask,sampler_RoadMask,roadUv+float2(texel.x,0)).a-SAMPLE_TEXTURE2D(_RoadMask,sampler_RoadMask,roadUv-float2(texel.x,0)).a;
   float pz=SAMPLE_TEXTURE2D(_RoadMask,sampler_RoadMask,roadUv+float2(0,texel.y)).a-SAMPLE_TEXTURE2D(_RoadMask,sampler_RoadMask,roadUv-float2(0,texel.y)).a;
   float rx=SAMPLE_TEXTURE2D(_RoadMask,sampler_RoadMask,roadUv+float2(texel.x,0)).g-SAMPLE_TEXTURE2D(_RoadMask,sampler_RoadMask,roadUv-float2(texel.x,0)).g;
   float rz=SAMPLE_TEXTURE2D(_RoadMask,sampler_RoadMask,roadUv+float2(0,texel.y)).g-SAMPLE_TEXTURE2D(_RoadMask,sampler_RoadMask,roadUv-float2(0,texel.y)).g;
   n=normalize(n+float3(px+rx*.3,0,pz+rz*.3)*_PitRelief*4);
   albedo*=1-road.a*.13;
   float windNoise=fbm(v.p.xz/5+float2(67,23));
   float burial=smoothstep(.28,.7,windNoise)*_Landscape.z;
   float exposed=1-smoothstep(.51,.78,windNoise+burial*.18);
   albedo=lerp(albedo,float3(.62,.49,.35)*grain,road.r*(.12+burial*.30));
   albedo=lerp(albedo,soil*grain,road.b*exposed*.55);
   albedo*=1-road.g*exposed*_Landscape.w;
   float2 crash=v.p.xz*80/_FogBounds.xy;
   float along=dot(crash,normalize(float2(1,.24))),across=dot(crash,normalize(float2(-.24,1)));
   float scar=(1-smoothstep(_SelectedStyle.w*.5,_SelectedStyle.w,abs(across)))*(1-smoothstep(31,40,abs(along)));
   float erased=smoothstep(.38,.66,fbm(crash*.16+23));
   albedo=lerp(albedo,lerp(_DustTint.rgb,_SaltTint.rgb,.5)*grain,scar*erased*.42);
   // World-space pigment only: no mesh/depth/normal change, and the existing fog
   // is applied afterwards. Unequal lobes and sand breaks avoid a uniform halo.
   float fire=0;
   float brokenEdge=fbm(v.p.xz*.39+float2(11,59));
   float ash=fbm(v.p.xz*1.8+float2(37,13));
   [unroll]for(int section=0;section<3;section++){
     float4 bounds=_ScorchSections[section];
     float2 local=v.p.xz-bounds.xy;
     local+=float2(noise(v.p.xz*.23+17)-.5,noise(v.p.xz*.27+43)-.5)*_Scorch.y*.65;
     float radius=length(max(abs(local)-bounds.zw*.82,0))/max(_Scorch.y,.01);
     float patch=1-smoothstep(1-_Scorch.z,1,radius+(brokenEdge-.5)*.27);
     fire=max(fire,patch);
   }
   float sandBreaks=smoothstep(.28,.72,fbm(v.p.xz*.67+float2(91,7)));
   fire*=lerp(.48,1,brokenEdge)*(1-sandBreaks*.48)*smoothstep(.72,.94,n.y);
   float3 soot=albedo*float3(.39,.37,.34);
   float3 ashTint=lerp(soot,albedo*float3(.67,.65,.61),smoothstep(.48,.72,ash)*.45);
   albedo=lerp(albedo,ashTint,fire*_Scorch.x);


 }
 if(_Kind>2.5&&_Kind<3.5){
   float2 local=abs(v.p.xz-_SiteBounds.xy)/max(_SiteBounds.zw,.01);
   float edge=max(local.x,local.y);float drift=fbm(v.p.xz*.34+float2(23,67));
   float2 signedLocal=(v.p.xz-_SiteBounds.xy)/max(_SiteBounds.zw,.01);
   float windAxis=dot(signedLocal,normalize(_FoundationArt.zw));
   float lee=saturate(.5+windAxis*.5);
   float cover=smoothstep(.79,1.025,edge+drift*.18+lee*.12)*smoothstep(.3,.6,drift)*_Landscape.y;
   float leeField=fbm(v.p.xz*.14+float2(47,13));
   float ribbons=smoothstep(.62,.90,edge+noise(float2(windAxis*4.2,drift*5.3))*.12+lee*.09)*smoothstep(.20,.70,leeField)*(.18+.82*lee);
   albedo=lerp(albedo,sandTint*grain*(1-wind*.045),saturate(cover+ribbons*_Landscape.y*.42));
   // Frequent-entry wear stays near the outer walk-in lanes, away from the center and pads.
   float entry=smoothstep(.55,.68,edge)*(1-smoothstep(.9,1.0,edge))*(.35+.65*lee);
   float lanes=saturate(noise(float2(windAxis*2.7+noise(v.p.xz*.16),edge*8))*1.3);
   float wear=entry*lanes*_FoundationArt.x*.16;
   albedo=lerp(albedo,float3(.57,.51,.43)*grain,wear);
   // Fine branching hairlines read as worn concrete, not open fissures or geometry.
   float2 crackUv=(v.p.xz-_SiteBounds.xy)*.42;
   float crackNoise=fbm(crackUv*1.7+float2(31,17));
   float crackA=abs(crackUv.y-sin(crackUv.x*1.25+crackNoise*1.1)*1.35);
   float crackB=abs((crackUv.y+1.2)-sin(crackUv.x*.83+2.4+crackNoise*.8)*1.05);
   float crackC=abs(crackUv.x-(crackUv.y*.34+sin(crackUv.y*1.4+crackNoise)*.5));
   float cracks=max(max(1-smoothstep(.018,.07,crackA),1-smoothstep(.016,.064,crackB)),1-smoothstep(.015,.058,crackC));
   float edgeGate=smoothstep(.48,.72,edge)*(1-smoothstep(.92,1.015,edge));
   float coreGate=smoothstep(2.1,3.2,length(v.p.xz-_SiteBounds.xy));
   float crackMask=cracks*edgeGate*coreGate*_FoundationArt.y*.38;
   albedo=lerp(albedo,float3(.31,.29,.26)*grain,crackMask);
 }
 float band=1-smoothstep(.04,.18,abs(frac(v.p.y/_Geology.z+noise(v.p.xz*.19)*.8+noise(v.p.xz*.7)*.23)-.5));if(_Kind<1.5)albedo*=1-band*_Geology.w*(1-saturate(n.y));
 float e=.03;float gx=(noise((v.p.xz+float2(e,0))*2.1)-noise((v.p.xz-float2(e,0))*2.1))/(2*e);float gz=(noise((v.p.xz+float2(0,e))*2.1)-noise((v.p.xz-float2(0,e))*2.1))/(2*e);
 float3 slope=float3(gx,0,gz);slope-=n*dot(slope,n);slope*=min(_NormalSlope,_NormalSlope/max(length(slope),.0001));n=normalize(n-slope);
 Light sun=GetMainLight(TransformWorldToShadowCoord(v.p+normalize(v.n)*.025));float3 lit=albedo*(SampleSH(n)+sun.color*saturate(dot(n,sun.direction))*sun.shadowAttenuation);if((_Kind>1.5&&_Kind<2.5)||_Kind>3.5){
   float3 viewDirection=SafeNormalize(_WorldSpaceCameraPos-v.p);
   float3 halfDirection=SafeNormalize(sun.direction+viewDirection);
   float gloss=_Kind>4.5?96:28;
   float spec=pow(saturate(dot(n,halfDirection)),gloss)*sun.shadowAttenuation;
   lit+=sun.color*spec*(_Kind>4.5?.34:.13);
   if(_Kind>4.5)lit+=float3(.055,.075,.085)*pow(1-saturate(dot(n,viewDirection)),3);
 }
 float2 mapUV=v.p.xz/(2*_FogBounds.xy)+.5;float fog=SAMPLE_TEXTURE2D(_FogMask,sampler_FogMask,mapUV).r;float grey=dot(lit,float3(.2126,.7152,.0722));lit=lerp(lit,grey.xxx,fog);return half4(lerp(lit,_FogColor.rgb,fog),1);
 }
 ENDHLSL
 }
 Pass {Name "ShadowCaster" Tags {"LightMode"="ShadowCaster"} ZWrite On ZTest LEqual ColorMask 0 Cull Off
 HLSLPROGRAM
 #pragma vertex shadowVert
 #pragma fragment shadowFrag
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
 float3 _LightDirection;struct ShadowA {float4 positionOS:POSITION;float3 normalOS:NORMAL;};
 float4 shadowVert(ShadowA a):SV_POSITION{float3 p=TransformObjectToWorld(a.positionOS.xyz),n=TransformObjectToWorldNormal(a.normalOS);return ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(p,n,_LightDirection)));}half4 shadowFrag():SV_Target{return 0;}
 ENDHLSL
 }
 }
}
