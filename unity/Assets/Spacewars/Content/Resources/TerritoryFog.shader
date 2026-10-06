Shader "Spacewars/TerritoryFog"
{
    Properties
    {
        _BaseColor("Base color",Color)=(1,1,1,1)
        _FogColor("Fog tint",Color)=(0,0,0,1)
        _FogMask("Team fog",2D)="black" {}
        _SurfaceRole("Surface role",Float)=0
        _RoadMask("Dirt road mask",2D)="black" {}
        _GroundTint("Ground tint",Color)=(.45,.46,.39,1)
        _RoadTint("Road tint",Color)=(.6,.52,.4,1)
        _SurfaceNoise("Surface noise",Vector)=(11,.38,.24,.16)
        _Concrete("Concrete",Vector)=(.69,4,.045,.22)
        _Metal("Metal",Vector)=(.41,2,2.5,.32)
        _SurfaceExtras("Surface extras",Vector)=(.09,1,.28,48)
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
            TEXTURE2D(_RoadMask); SAMPLER(sampler_RoadMask);
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _FogColor;
                float4 _FogBounds;
                float _SurfaceRole;
                half4 _GroundTint, _RoadTint;
                float4 _SurfaceNoise, _Concrete, _Metal, _SurfaceExtras;
            CBUFFER_END
            struct Attributes {float4 positionOS:POSITION;float3 normalOS:NORMAL;};
            struct Varyings {float4 positionCS:SV_POSITION;float3 positionWS:TEXCOORD0;half3 normalWS:TEXCOORD1;};
            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionWS=TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS=TransformWorldToHClip(output.positionWS);
                output.normalWS=TransformObjectToWorldNormal(input.normalOS);
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
            half4 frag(Varyings input):SV_Target
            {
                float2 uv=input.positionWS.xz/(2*_FogBounds.xy)+.5;
                half fog=SAMPLE_TEXTURE2D(_FogMask,sampler_FogMask,uv).r;
                float3 normal=normalize(input.normalWS);
                half3 surface=_BaseColor.rgb;
                float highlight=0;
                // Fixed role IDs match ThreeCrossingsWorld; thresholds and projection axes are not art parameters.
                if(_SurfaceRole>.5)
                {
                    // Dominant-plane projection gives vertical cliff faces grain as well.
                    float2 plane=abs(normal.y)>.5?input.positionWS.xz:abs(normal.x)>abs(normal.z)?input.positionWS.zy:input.positionWS.xy;
                    float broad=noise(plane/_SurfaceNoise.x)-.5;
                    float grain=noise(plane/_SurfaceNoise.z)-.5;
                    if(_SurfaceRole<1.5)
                    {
                        float road=SAMPLE_TEXTURE2D(_RoadMask,sampler_RoadMask,uv).r*step(.5,normal.y);
                        surface=lerp(_GroundTint.rgb*(1+broad*_SurfaceNoise.y*2),_RoadTint.rgb*(1+broad*_SurfaceNoise.y),road);
                        surface*=1+grain*_SurfaceNoise.w;
                    }
                    else if(_SurfaceRole<2.5)
                    {
                        float joint=joints(plane,_Concrete.yy,_Concrete.z);
                        surface=_Concrete.xxx*(1+broad*_SurfaceNoise.w+grain*_SurfaceNoise.w)*(1-joint*_Concrete.w);
                    }
                    else if(_SurfaceRole<3.5)
                    {
                        float seam=joints(plane,_Metal.yz,_Concrete.z);
                        float brush=noise(plane/float2(_SurfaceNoise.z,_Metal.y))-.5;
                        surface=_Metal.xxx*(1+brush*_SurfaceExtras.x)*(1-seam*_Metal.w);
                        Light main=GetMainLight();
                        float3 eye=GetWorldSpaceNormalizeViewDir(input.positionWS);
                        highlight=pow(saturate(dot(reflect(-main.direction,normal),eye)),_SurfaceExtras.w)*_SurfaceExtras.z*(1-seam);
                    }
                    else surface=_BaseColor.rgb*_SurfaceExtras.y*(1+broad*_SurfaceNoise.y+grain*_SurfaceNoise.w);
                }
                Light light=GetMainLight();
                half3 lit=surface*(SampleSH(normal)+light.color*saturate(dot(normal,light.direction)))+highlight*light.color;
                // Neutral optical coefficients, not gameplay or designer-tunable values.
                half grey=dot(lit,half3(.2126,.7152,.0722));
                lit=lerp(lit,grey.xxx,fog);
                return half4(lerp(lit,_FogColor.rgb,fog),1);
            }
            ENDHLSL
        }
    }
}
