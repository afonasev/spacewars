Shader "Spacewars/ConstructionApron"
{
    Properties
    {
        _BaseColor("Tint",Color)=(1,1,1,1)
        _ConcreteTex("Authored concrete",2D)="white" {}
        _FogMask("Team fog",2D)="black" {}
        _FogColor("Fog tint",Color)=(0,0,0,1)
        _FogBounds("Map half bounds",Vector)=(32,32,0,0)
    }
    SubShader
    {
        Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry"}
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_ConcreteTex); SAMPLER(sampler_ConcreteTex);
            TEXTURE2D(_FogMask); SAMPLER(sampler_FogMask);
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor, _FogColor;
                float4 _FogBounds;
            CBUFFER_END
            struct Attributes {float4 positionOS:POSITION;float3 normalOS:NORMAL;float2 uv:TEXCOORD0;half4 color:COLOR;};
            struct Varyings {float4 positionCS:SV_POSITION;float3 positionWS:TEXCOORD0;half3 normalWS:TEXCOORD1;float2 uv:TEXCOORD2;half4 color:COLOR;};
            Varyings vert(Attributes v)
            {
                Varyings o;o.positionWS=TransformObjectToWorld(v.positionOS.xyz);o.positionCS=TransformWorldToHClip(o.positionWS);
                o.normalWS=TransformObjectToWorldNormal(v.normalOS);o.uv=v.uv;o.color=v.color;return o;
            }
            half4 frag(Varyings i):SV_Target
            {
                // The authored concrete family's measured mean is calibration, not a tuning value.
                half3 grain=clamp(SAMPLE_TEXTURE2D(_ConcreteTex,sampler_ConcreteTex,i.uv).rgb/.385,.65,1.35);
                half3 surface=i.color.rgb*_BaseColor.rgb*lerp(grain,half3(1,1,1),i.color.a);
                Light light=GetMainLight();half3 lit=surface*(SampleSH(normalize(i.normalWS))+light.color*saturate(dot(normalize(i.normalWS),light.direction)));
                half fog=SAMPLE_TEXTURE2D(_FogMask,sampler_FogMask,i.positionWS.xz/(2*_FogBounds.xy)+.5).r;
                half grey=dot(lit,half3(.2126,.7152,.0722));lit=lerp(lit,grey.xxx,fog);
                return half4(lerp(lit,_FogColor.rgb,fog),1);
            }
            ENDHLSL
        }
    }
}
