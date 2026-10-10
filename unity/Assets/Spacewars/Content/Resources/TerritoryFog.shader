Shader "Spacewars/TerritoryFog"
{
    Properties
    {
        _BaseColor("Base color",Color)=(1,1,1,1)
        _FogColor("Fog tint",Color)=(0,0,0,1)
        _FogMask("Team fog",2D)="black" {}
        _SurfaceRole("Surface role",Float)=0
        _FoundryPadBounds("Foundry pad center extent hex",Vector)=(0,0,1,0)
        _FoundryPadCenter("Foundry central foundation",Float)=0
        _FoundryPadFinish("Foundry sand width scale foundation radius feather",Vector)=(2.25,3,3.5,.25)
        _FoundrySandTint("Foundry edge sand",Color)=(.55,.473,.363,1)
        _FoundryCenterValue("Foundry central concrete value",Float)=.28
        _FoundryVariation("Foundry texture variation",Vector)=(.8,13,0,0)
        _FoundryGrain("Foundry deposit grain contrasts",Vector)=(13,.3,.18,.12)
        _FoundryLavaFinish("Lava depth glow crust scale flow",Vector)=(3.5,1.2,1.5,.18)
        _FoundryLavaRim("Molten level rim level enabled",Vector)=(0,0,0,0)
        _FoundryLavaTint("Molten tint",Color)=(1,.25,.035,1)
        _FoundryLavaHeat("Molten center and cooled rim",2D)="black" {}
        _RoadMask("Dirt road mask",2D)="black" {}
        _GroundTint("Ground tint",Color)=(.45,.46,.39,1)
        _RoadTint("Road tint",Color)=(.6,.52,.4,1)
        _SurfaceNoise("Surface noise",Vector)=(11,.38,.24,.16)
        _Concrete("Concrete",Vector)=(.69,4,.045,.22)
        _Metal("Metal",Vector)=(.41,2,2.5,.32)
        _SurfaceExtras("Surface extras",Vector)=(.09,1,.28,48)
        _EarthTex("Earth albedo",2D)="gray" {}
        _RockTex("Rock albedo",2D)="gray" {}
        _ConcreteTex("Concrete albedo",2D)="gray" {}
        _SteelTex("Steel albedo",2D)="gray" {}
        _TextureVariants("Variant region scale transition concrete contrast",Vector)=(18,.08,2,0)
        _EarthTexB("Earth sibling B",2D)="gray" {}
        _EarthTexC("Earth sibling C",2D)="gray" {}
        _EarthVariantGain("Earth calibrated gain and mean",Vector)=(1,1,1,.2)
        _RockTexB("Rock sibling B",2D)="gray" {}
        _RockTexC("Rock sibling C",2D)="gray" {}
        _RockVariantGain("Rock calibrated gain and mean",Vector)=(1,1,1,.2)
        _ConcreteTexB("Concrete sibling B",2D)="gray" {}
        _ConcreteTexC("Concrete sibling C",2D)="gray" {}
        _ConcreteVariantGain("Concrete calibrated gain and mean",Vector)=(1,1,1,.2)
        _SteelTexB("Steel sibling B",2D)="gray" {}
        _SteelTexC("Steel sibling C",2D)="gray" {}
        _SteelVariantGain("Steel calibrated gain and mean",Vector)=(1,1,1,.2)
        _RockMask("Rock footprint distance",2D)="white" {}
        _Geology("Geology scale coverage strata contrast",Vector)=(52,.52,.85,.32)
        _BasaltTint("Basalt",Color)=(.23,.25,.27,1)
        _RustTint("Oxidised rock",Color)=(.57,.295,.16,1)
        _NaturalBlend("Natural width variation patch ground height",Vector)=(2.4,.45,2.5,0)
        _ShoreMask("Shore distance",2D)="white" {}
        _ArtTiles("Art tile meters",Vector)=(7,6,5,4)
        _ArtReliefLimit("Maximum micro-relief slope",Float)=.35
        _ArtDetail("Texture relief moss wet width",Vector)=(0,0,0,1)
        _WaterDeep("Deep water",Color)=(.09,.34,.32,1)
        _WaterShallow("Shallow water",Color)=(.34,.63,.6,1)
        _WaterMotion("Ripple scale speed strength reflection",Vector)=(2.5,.35,.25,.35)
        _ShoreSettings("Shore width foam distance range",Vector)=(2.8,.35,8,0)
        _WeatheringMask("Dust contact wear",2D)="black" {}
        _Weathering("Reach strength direction wear",Vector)=(3.25,.72,.61,.18)
        _ContactShade("Contact shade",Float)=.24
        _Dressing("Dressing kind",Float)=0
        _FogBounds("Map half bounds",Vector)=(32,32,0,0)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_FogMask); SAMPLER(sampler_FogMask);
            TEXTURE2D(_FoundryLavaHeat); SAMPLER(sampler_FoundryLavaHeat);
            TEXTURE2D(_RoadMask); SAMPLER(sampler_RoadMask);
            TEXTURE2D(_EarthTex); SAMPLER(sampler_EarthTex);
            TEXTURE2D(_RockTex); SAMPLER(sampler_RockTex);
            TEXTURE2D(_ConcreteTex); SAMPLER(sampler_ConcreteTex);
            TEXTURE2D(_SteelTex); SAMPLER(sampler_SteelTex);
            TEXTURE2D(_RockMask); SAMPLER(sampler_RockMask);
            TEXTURE2D(_ShoreMask); SAMPLER(sampler_ShoreMask);
            TEXTURE2D(_WeatheringMask); SAMPLER(sampler_WeatheringMask);
            TEXTURE2D(_EarthTexB);
            TEXTURE2D(_EarthTexC);
            TEXTURE2D(_RockTexB);
            TEXTURE2D(_RockTexC);
            TEXTURE2D(_ConcreteTexB);
            TEXTURE2D(_ConcreteTexC);
            TEXTURE2D(_SteelTexB);
            TEXTURE2D(_SteelTexC);
            CBUFFER_START(UnityPerMaterial)
                float4 _TextureVariants;
                float4 _EarthVariantGain;
                float4 _RockVariantGain;
                float4 _ConcreteVariantGain;
                float4 _SteelVariantGain;
                half4 _BaseColor;
                half4 _FogColor;
                float4 _FogBounds;
                float _SurfaceRole;
                float _ArtReliefLimit;
                float4 _FoundryGrain;
                float4 _FoundryLavaFinish,_FoundryLavaRim;
                half4 _FoundryLavaTint;
                float4 _FoundryVariation;
                float4 _FoundryPadBounds, _FoundryPadFinish;
                half4 _FoundrySandTint;
                float _FoundryPadCenter, _FoundryCenterValue;
                half4 _GroundTint, _RoadTint;
                float4 _SurfaceNoise, _Concrete, _Metal, _SurfaceExtras;
                float4 _ArtTiles, _ArtDetail, _WaterMotion, _ShoreSettings, _NaturalBlend;
                half4 _WaterDeep, _WaterShallow, _BasaltTint, _RustTint;
                float4 _Geology, _Weathering;
                float _ContactShade, _Dressing;
            CBUFFER_END
            struct Attributes {float4 positionOS:POSITION;float3 normalOS:NORMAL;half4 color:COLOR;};
            struct Varyings {float4 positionCS:SV_POSITION;float3 positionWS:TEXCOORD0;half3 normalWS:TEXCOORD1;half4 color:COLOR;};
            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionWS=TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS=TransformWorldToHClip(output.positionWS);
                output.normalWS=TransformObjectToWorldNormal(input.normalOS);
                output.color=input.color;
                return output;
            }
            // Hash, cubic interpolation and half-cell offsets are fixed sampling invariants.
            float hash(float2 p) { return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453); }
            float noise(float2 p)
            {
                float2 cell=floor(p), f=frac(p); f=f*f*(3-2*f);
                return lerp(lerp(hash(cell),hash(cell+float2(1,0)),f.x),lerp(hash(cell+float2(0,1)),hash(cell+1),f.x),f.y);
            }
            float joints(float2 p,float2 size,float width)
            {
                float2 d=abs(frac(p/size+.5)-.5)*size;
                float edge=min(d.x,d.y),aa=max(fwidth(edge),.0001);
                return 1-smoothstep(width-aa,width+aa,edge);
            }
            // One fixed detail projection remains continuous across hard mesh edges.
            // Siblings share a sampler/import contract. Region selection is continuous;
            // no square-cell ID, UV rotation or discontinuous derivative is introduced.
            float3 variantWeights(float2 world)
            {
                float2 p=world/_TextureVariants.x;
                p+=float2(noise(p*.73+19),noise(p*.81+43))-.5;
                float3 scores=float3(noise(p+float2(7,31)),noise(p+float2(53,11)),noise(p+float2(29,71)));
                float largest=max(scores.x,max(scores.y,scores.z));
                float3 weights=smoothstep(largest-_TextureVariants.y,largest,scores);
                return weights/max(dot(weights,1),.0001);
            }
            #define VARIANT_SAMPLE(role,smp,coord,world) (SAMPLE_TEXTURE2D(_##role##Tex,smp,coord).rgb*_##role##VariantGain.x*variantWeights(world).x + SAMPLE_TEXTURE2D(_##role##TexB,smp,coord).rgb*_##role##VariantGain.y*variantWeights(world).y + SAMPLE_TEXTURE2D(_##role##TexC,smp,coord).rgb*_##role##VariantGain.z*variantWeights(world).z)
            #define NATURAL_SAMPLE(role,smp,p,tile) VARIANT_SAMPLE(role,smp,(p).xz/(tile),(p).xz)
            // Oblique XZ basis retains two-dimensional detail on axis-aligned terrain.
            float3 naturalPosition(float3 p) { return float3(dot(p,float3(0.64863783,-0.57400305,0.49978942)),dot(p,float3(0.68211449,0.72972141,-0.04718577)),dot(p,float3(-0.33762227,0.37152008,0.86486070))); }
            // Detail projection has equal coverage on the three principal face axes.
            // The historical noise basis is retained for palette/geology stability;
            // its near-zero Z coverage must not stretch photographed cliff detail.
            float3 detailPosition(float3 p)
            {
                return float3(dot(p,float3(.70710678,0,-.70710678)),dot(p,float3(.57735027,.57735027,.57735027)),dot(p,float3(.40824829,-.81649658,.40824829)));
            }
            float naturalNoise(float3 p,float scale)
            {
                p=naturalPosition(p);
                return noise(p.xz/scale)*.5+noise(p.zy/scale)*.25+noise(p.xy/scale)*.25;
            }
            float3 reliefNormal(float3 normal,float3 gradient)
            {
                // Screen derivatives can spike at quad/noise boundaries. Micro relief
                // must stay finite and bounded; it never changes the underlying surface.
                float3 slope=clamp(gradient*_ArtDetail.y*.1,-_ArtReliefLimit,_ArtReliefLimit);
                if(any(slope!=slope))return normal;
                slope*=min(1,_ArtReliefLimit/max(length(slope),.0001));
                return normalize(normal-slope);
            }
            half4 frag(Varyings input):SV_Target
            {
                float2 uv=input.positionWS.xz/(2*_FogBounds.xy)+.5;
                half fog=SAMPLE_TEXTURE2D(_FogMask,sampler_FogMask,uv).r;
                float3 normal=normalize(input.normalWS);
                half3 surface=_BaseColor.rgb;
                // Open AI test field: exactly one albedo, without procedural dressing.
                if(_SurfaceRole<-.5)surface*=SAMPLE_TEXTURE2D(_EarthTex,sampler_EarthTex,input.positionWS.xz/8).rgb;
                float highlight=0;
                half3 emission=0;
                float3 eye=GetWorldSpaceNormalizeViewDir(input.positionWS);
                float shore=SAMPLE_TEXTURE2D(_ShoreMask,sampler_ShoreMask,uv).r*_ShoreSettings.z;
                // Fixed role IDs match ThreeCrossingsWorld; thresholds and projection axes are not art parameters.
                if(_SurfaceRole>.5&&_SurfaceRole<5.5)
                {
                    // Dominant-plane projection gives vertical cliff faces grain as well.
                    float2 plane=abs(normal.y)>.5?input.positionWS.xz:abs(normal.x)>abs(normal.z)?input.positionWS.zy:input.positionWS.xy;
                    float broad=noise(plane/_SurfaceNoise.x)-.5;
                    float grain=noise(plane/_SurfaceNoise.z)-.5;
                    bool natural=_SurfaceRole<1.5||(_SurfaceRole>3.5&&_SurfaceRole<4.5);
                    if(natural)
                    {
                        float3 p=input.positionWS;
                        broad=naturalNoise(p,_SurfaceNoise.x)-.5;
                        grain=naturalNoise(p,_SurfaceNoise.z)-.5;
                        float variation=(naturalNoise(p,_NaturalBlend.z)-.5)*2;
                        float width=_NaturalBlend.x*(1+variation*_NaturalBlend.y);
                        float height=p.y-_NaturalBlend.w;
                        float road=SAMPLE_TEXTURE2D(_RoadMask,sampler_RoadMask,uv).r;
                        // Break up feathered dirt edges without introducing a hard threshold.
                        road=smoothstep(0,1,saturate(road+variation*_NaturalBlend.y*road*(1-road)*2));
                        road*=1-smoothstep(0,width,abs(height));
                        half3 soil=lerp(_GroundTint.rgb*(1+broad*_SurfaceNoise.y*2),_RoadTint.rgb*(1+broad*_SurfaceNoise.y),road);
                        float3 texturePosition=detailPosition(p);
                        half3 earth=NATURAL_SAMPLE(Earth,sampler_EarthTex,texturePosition,_ArtTiles.x);
                        // One continuous geological field, shared by soil and cliffs. No corner masks.
                        float exposure=naturalNoise(p,_Geology.x)*.65+naturalNoise(p+float3(19,0,7),_Geology.x*.37)*.35;
                        float oxidation=smoothstep(.40,.60,exposure+(_Geology.y-.5)*.7);
                        half3 geology=lerp(_BasaltTint.rgb,_RustTint.rgb,oxidation);
                        soil=lerp(geology*1.2,_RoadTint.rgb,road)*(1+broad*_SurfaceNoise.y);
                        earth=dot(earth,half3(.2126,.7152,.0722)).xxx;
                        soil*=(1+grain*_SurfaceNoise.w)*lerp(1,clamp(earth/.18,.35,2),_ArtDetail.x*(1-road*.65));
                        half3 rock=NATURAL_SAMPLE(Rock,sampler_RockTex,texturePosition,_ArtTiles.y);
                        float rockDetail=dot(rock,half3(.2126,.7152,.0722));
                        rock=geology*lerp(1,clamp(rockDetail/.18,.45,1.65),_ArtDetail.x)*_SurfaceExtras.y;
                        float strata=sin((p.y+naturalNoise(p,5)*1.4+p.x*.055+p.z*.025)/_Geology.z*6.283);
                        float seam=pow(saturate(strata),8);
                        rock*=1-seam*_Geology.w;
                        rock*=.85+.3*naturalNoise(p,1.8);
                        float distance=SAMPLE_TEXTURE2D(_RockMask,sampler_RockMask,uv).r*_ShoreSettings.z;
                        float foot=.5*(1-smoothstep(0,width,distance));
                        if(_SurfaceRole>3.5)foot=.5+.5*smoothstep(0,width,max(0,height));
                        float bank=.5*(1-smoothstep(0,width,shore))+.5*smoothstep(0,width,max(0,-height));
                        float stone=_Dressing>.5?1:saturate(max(foot,bank));
                        float moss=smoothstep(.3,.7,naturalNoise(p,_ArtTiles.y))*_ArtDetail.z;
                        rock=lerp(rock,soil*.8,moss);
                        surface=lerp(soil,rock,stone);
                        surface*=1-(1-smoothstep(0,_ArtDetail.w,shore))*.28;
                    }
                    else if(_SurfaceRole<2.5)
                    {
                        float joint=joints(plane,_Concrete.yy,_Concrete.z);
                        surface=_Concrete.xxx*(1+broad*_SurfaceNoise.w+grain*_SurfaceNoise.w)*(1-joint*_Concrete.w);
                        half3 concrete=VARIANT_SAMPLE(Concrete,sampler_ConcreteTex,plane/_ArtTiles.z,input.positionWS.xz);
                        surface*=lerp(1,clamp(concrete/.35,.5,1.5),_ArtDetail.x);
                        // Flush service plates and paired rails are surface markings, never obstacles.
                        float2 bay=abs(frac(plane/12+.5)-.5)*12;
                        float edging=(1-smoothstep(.08,.16,abs(bay.x-4.8)))*step(1.2,bay.y);
                        float plate=step(bay.x,1.3)*step(bay.y,2.5);
                        surface=lerp(surface,surface*.62,plate*.5);
                        surface=lerp(surface,half3(.28,.20,.075),edging*.65);
                    }
                    else if(_SurfaceRole<3.5)
                    {
                        float seam=joints(plane,_Metal.yz,_Concrete.z);
                        float brush=noise(plane/float2(_SurfaceNoise.z,_Metal.y))-.5;
                        surface=_Metal.xxx*(1+brush*_SurfaceExtras.x)*(1-seam*_Metal.w);
                        half3 steel=VARIANT_SAMPLE(Steel,sampler_SteelTex,plane/_ArtTiles.w,input.positionWS.xz);
                        surface*=lerp(1,clamp(steel/.2,.35,1.8),_ArtDetail.x);
                        // Fine tread ridges and paired fasteners repeat inside the existing panel layout.
                        float2 panel=frac(plane/_Metal.yz+.5);
                        float2 boltDistance=abs(panel-.5);
                        float bolt=1-smoothstep(.01,.023,length(boltDistance-float2(.40,.40)));
                        surface=lerp(surface,surface*1.7,bolt);
                        Light main=GetMainLight();
                        highlight=pow(saturate(dot(reflect(-main.direction,normal),eye)),_SurfaceExtras.w)*_SurfaceExtras.z*(1-seam);
                    }
                    else
                    {
                        float2 p=input.positionWS.xz/_WaterMotion.x;
                        float time=_Time.y*_WaterMotion.y;
                        float warp=noise(p*.37+time*.03)*5;
                        float a=dot(p,float2(.8,.6))*6.283-time+warp;
                        float b=dot(p,float2(-.6,.8))*9.17+time*.73+noise(p*.61-time*.025)*4;
                        float c=dot(p,float2(.93,-.37))*17.3-time*1.27;
                        float2 slope=float2(.8,.6)*cos(a)+float2(-.6,.8)*cos(b)*.45+float2(.93,-.37)*cos(c)*.18;
                        normal=normalize(float3(-slope.x*_WaterMotion.z,1,-slope.y*_WaterMotion.z));
                        float shallow=1-smoothstep(0,_ShoreSettings.x,shore);
                        surface=lerp(_WaterDeep.rgb,_WaterShallow.rgb,shallow);
                        surface*=1+.025*(sin(a)+sin(b)*.4);
                        float foam=(1-smoothstep(.05,_ShoreSettings.x*.27,shore))*smoothstep(.40,.72,noise(p*2+time*.1));
                        surface=lerp(surface,_WaterShallow.rgb+half3(.25,.25,.23),foam*_ShoreSettings.y);
                        float fresnel=pow(1-saturate(dot(normal,eye)),4);
                        float cloud=noise(input.positionWS.xz/18+float2(time*.015,0));
                        surface=lerp(surface,half3(.35,.48,.52)*( .8+cloud*.4),_WaterMotion.w*(.22+fresnel*.78));
                        Light main=GetMainLight();
                        highlight=pow(saturate(dot(normal,normalize(main.direction+eye))),96)*_WaterMotion.w;
                    }
                    if(_Dressing>.5)surface*=input.color.rgb;
                    if(_SurfaceRole<4.5)
                    {
                        float3 weather=SAMPLE_TEXTURE2D(_WeatheringMask,sampler_WeatheringMask,uv).rgb;
                        float2 wind=float2(cos(_Weathering.z),sin(_Weathering.z));
                        float2 dustUV=float2(dot(input.positionWS.xz,wind),dot(input.positionWS.xz,float2(-wind.y,wind.x)));
                        float grit=noise(dustUV/float2(2.7,.45));
                        float dust=saturate(weather.r*(.6+.7*grit))*_Weathering.y*smoothstep(.3,.85,normal.y);
                        if(_Dressing>.5)dust*=.35;
                        if(_SurfaceRole>1.5&&_SurfaceRole<3.5)
                        {
                            half3 sand=_RoadTint.rgb*(.82+.22*grit);
                            // Keep construction seams readable through the deposit.
                            surface=lerp(surface,sand,dust);highlight*=1-dust;
                        }
                        surface*=1-weather.g*_ContactShade*smoothstep(.2,.85,normal.y);
                        surface*=1-weather.b*_Weathering.w*smoothstep(.3,.85,normal.y);

                        // World-space micro relief, no displacement or collision changes.
                        float rough=natural?naturalNoise(input.positionWS,_SurfaceNoise.z):noise(plane/_SurfaceNoise.z);
                        float3 dx=ddx(input.positionWS),dy=ddy(input.positionWS);
                        float3 r1=cross(dy,normal),r2=cross(normal,dx);
                        float determinant=dot(dx,r1);
                        float3 gradient=(r1*ddx(rough)+r2*ddy(rough))*sign(determinant)/max(abs(determinant),.00001);
                        normal=reliefNormal(normal,gradient);
                    }
                }
                // Fixed content role contract: 6 ash, 7 gravel, 8 concrete, 9 slag, 10 basalt, 11 lava.
                if(_SurfaceRole>5.5)
                {
                    float3 p=input.positionWS;
                    float broad=noise(p.xz/_FoundryGrain.x)-.5;
                    float grain=noise((p.xz+p.y)/_FoundryGrain.y)-.5;
                    float3 projected=detailPosition(p);
                    float3 earth=NATURAL_SAMPLE(Earth,sampler_EarthTex,projected,_ArtTiles.x);
                    float3 rockTexture=NATURAL_SAMPLE(Rock,sampler_RockTex,projected,_ArtTiles.y);
                    if(_SurfaceRole>=9.5&&_SurfaceRole<10.5)
                    {
                        // A single oblique plane becomes singular on some inclined
                        // basalt feet. Upward faces use world XZ; vertical walls keep
                        // the balanced projection, blending only the slope transition.
                        float cap=smoothstep(.35,.65,normal.y);
                        if(cap>0)rockTexture=lerp(rockTexture,NATURAL_SAMPLE(Rock,sampler_RockTex,p,_ArtTiles.y),cap);
                    }
                    float2 plane=abs(normal.y)>.5?p.xz:abs(normal.x)>abs(normal.z)?p.zy:p.xy;
                    surface=_BaseColor.rgb*(1+broad*_FoundryGrain.z+grain*_FoundryGrain.w);
                    float detail=dot(earth,float3(.299,.587,.114));
                    if(_SurfaceRole<7.5)
                    {
                        // Suppress ruler-straight lighting edges of shallow authored
                        // support triangles without altering terrain, ramps or rock normals.
                        if(normal.y>.75)normal=normalize(lerp(normal,float3(0,1,0),_FoundryVariation.z));
                        float2 masks=SAMPLE_TEXTURE2D(_RoadMask,sampler_RoadMask,uv).rg;
                        float rust=smoothstep(.35,.75,noise(p.xz/_FoundryGrain.x+13));
                        float deposit=smoothstep(.25,.75,naturalNoise(p+float3(23,0,41),_FoundryGrain.x*.55));
                        float3 ash=surface*lerp(float3(.82,.86,.93),float3(1.22,1.03,.82),rust);
                        ash=lerp(ash,ash*float3(.72,.76,.79),deposit*_FoundryVariation.x*.45);
                        float3 slag=_RoadTint.rgb*(1+broad*_FoundryGrain.z+grain*_FoundryGrain.w);
                        surface=lerp(ash,slag,masks.r);
                        surface=lerp(surface,_BasaltTint.rgb*float3(.91,.85,.78),masks.g*.6);
                        surface*=lerp(1,.35+detail*2.2,_ArtDetail.x);
                    }
                    else if(_SurfaceRole<8.5)
                    {
                        // Concrete finish uses luminance rather than the inherited warm
                        // tile color, and no regular tiled joint grid. Sibling regions
                        // keep broad wear irregular while retaining fine fractures.
                        float3 concrete=NATURAL_SAMPLE(Concrete,sampler_ConcreteTex,projected,_ArtTiles.z);
                        float wear=dot(concrete,float3(.299,.587,.114));
                        surface=_BaseColor.rgb*lerp(1,clamp(.82+wear*.45+(wear-_ConcreteVariantGain.w)*_TextureVariants.z,.35,2),_ArtDetail.x);
                        float2 local=abs(p.xz-_FoundryPadBounds.xy);
                        // Exact inward distance for the fixed six-sided circumscribed
                        // polygon (diagonal slope 1/2), or the unchanged square sites.
                        float edge=_FoundryPadBounds.z-max(local.x,local.y);
                        if(_FoundryPadBounds.w>.5)
                            edge=min(_FoundryPadBounds.z-local.y,(_FoundryPadBounds.z-local.x-local.y*.5)/sqrt(1.25));
                        float drift=noise(p.xz/_FoundryPadFinish.y+float2(17,43));
                        float sand=1-smoothstep(0,_FoundryPadFinish.x*(.3+1.4*drift),max(0,edge));
                        float sandGrain=noise(p.xz/_FoundryGrain.y)-.5;
                        float3 sandColor=_FoundrySandTint.rgb*(1+grain*_FoundryGrain.w+broad*_FoundryGrain.z);
                        surface=lerp(surface,sandColor,sand);
                        // Clipped foundation corners read as a separate industrial slab,
                        // rather than a round painted shadow beneath the building.
                        float centerDistance=max(max(local.x,local.y),(local.x+local.y)/sqrt(2));
                        float center=1-smoothstep(_FoundryPadFinish.z-_FoundryPadFinish.w,_FoundryPadFinish.z+_FoundryPadFinish.w,centerDistance);
                        float centerDetail=dot(rockTexture,float3(.299,.587,.114));
                        float3 foundation=_FoundryCenterValue.xxx*(.5+centerDetail*1.8)*(1+broad*_FoundryGrain.z+grain*_FoundryGrain.w);
                        surface=lerp(surface,foundation,center*_FoundryPadCenter);
                        detail=lerp(wear,sandGrain+.5,sand);
                        detail=lerp(detail,centerDetail,center*_FoundryPadCenter);
                    }
                    else if(_SurfaceRole<10.5)
                    {
                        float mineral=smoothstep(.42,.78,naturalNoise(p,_FoundryGrain.x));
                        float3 basalt=_BasaltTint.rgb;
                        surface=lerp(basalt,basalt*float3(1.34,.90,.64),mineral);
                        float layer=p.y/_Geology.z+(noise(p.xz/_FoundryGrain.x)-.5)*2.2+noise(p.xz/(_FoundryGrain.x*.3))*.35;
                        float strata=.5+.5*sin(layer*6.283185);
                        surface*=1-_Geology.w*smoothstep(.65,.95,strata)*(1-saturate(normal.y)*.65);
                        surface*=lerp(1,.3+rockTexture*2.0,_ArtDetail.x);
                        detail=dot(rockTexture,float3(.299,.587,.114));
                        float warmth=SAMPLE_TEXTURE2D(_FoundryLavaHeat,sampler_FoundryLavaHeat,uv).g*_FoundryLavaRim.z*(1-smoothstep(_FoundryLavaRim.x,max(_FoundryLavaRim.y,_FoundryLavaRim.x+.001),p.y));
                        emission=_FoundryLavaTint.rgb*warmth*_FoundryLavaFinish.y*.12;

                    }
                    else if(_SurfaceRole<11.5)
                    {
                        float heat=SAMPLE_TEXTURE2D(_FoundryLavaHeat,sampler_FoundryLavaHeat,uv).r;
                        float2 flow=p.xz+float2(0,_Time.y*_FoundryLavaFinish.w);
                        float2 q=flow/_FoundryLavaFinish.z;
                        float crust=smoothstep(.48,.67,noise(q+noise(q*.37)*2)*.7+noise(q*2.7)*.3);
                        float molten=heat*(1-crust*.92);
                        surface=_BasaltTint.rgb*(.4+noise(q*3)*.2)*(1-molten);
                        emission=_FoundryLavaTint.rgb*molten*_FoundryLavaFinish.y*(.65+heat*.65);

                    }
                    else
                    {
                        float3 steel=VARIANT_SAMPLE(Steel,sampler_SteelTex,plane/_ArtTiles.w,input.positionWS.xz);
                        surface*=lerp(1,.65+steel*.65,_ArtDetail.x*.5);
                        detail=dot(steel,float3(.299,.587,.114));
                    }
                    if(_SurfaceRole<10.5||_SurfaceRole>11.5)
                    {
                        float3 dx=ddx(p),dy=ddy(p),r1=cross(dy,normal),r2=cross(normal,dx);
                        float determinant=dot(dx,r1);
                        float3 gradient=(r1*ddx(detail)+r2*ddy(detail))*sign(determinant)/max(abs(determinant),.00001);
                        normal=reliefNormal(normal,gradient);
                    }
                    highlight=0;
                }
                Light light=GetMainLight();
                half3 lit=surface*(SampleSH(normal)+light.color*saturate(dot(normal,light.direction)))+highlight*light.color+emission;
                // Neutral optical coefficients, not gameplay or designer-tunable values.
                half grey=dot(lit,half3(.2126,.7152,.0722));
                lit=lerp(lit,grey.xxx,fog);
                return half4(lerp(lit,_FogColor.rgb,fog),1);
            }
            ENDHLSL
        }
    }
}
