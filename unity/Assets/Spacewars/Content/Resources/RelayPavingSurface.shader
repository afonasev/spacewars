Shader "Spacewars/RelayPavingSurface"
{
    Properties {
        _MainTex("Painted ground",2D)="white"{}
        _FogMask("Team fog",2D)="black"{}
        _FogColor("Fog tint",Color)=(0,0,0,1)
        _FogBounds("Map half bounds",Vector)=(32,32,0,0)
    }
    SubShader {
        Tags {"RenderPipeline"="UniversalPipeline" "Queue"="Transparent-30" "RenderType"="Transparent"}
        Pass {
            Tags {"LightMode"="UniversalForward"}
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            TEXTURE2D(_FogMask); SAMPLER(sampler_FogMask);
            CBUFFER_START(UnityPerMaterial)
            float4 _FogBounds,_FogColor;
            CBUFFER_END
            struct Attributes {float4 positionOS:POSITION; float3 normalOS:NORMAL; float2 uv:TEXCOORD0;};
            struct Varyings {float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; float3 normalWS:TEXCOORD1; float2 uv:TEXCOORD2;};
            Varyings vert(Attributes v) {
                Varyings o;
                o.positionWS=TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS=TransformWorldToHClip(o.positionWS);
                o.normalWS=TransformObjectToWorldNormal(v.normalOS); o.uv=v.uv;
                return o;
            }
            half4 frag(Varyings i):SV_Target {
                half4 paint=SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,i.uv);
                clip(paint.a-.003);
                float3 n=normalize(i.normalWS);
                Light sun=GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                float3 lit=paint.rgb*(SampleSH(n)+sun.color*saturate(dot(n,sun.direction))*sun.shadowAttenuation);
                float fog=SAMPLE_TEXTURE2D(_FogMask,sampler_FogMask,i.positionWS.xz/(2*_FogBounds.xy)+.5).r;
                return half4(lerp(lit,_FogColor.rgb,fog),paint.a);
            }
            ENDHLSL
        }
    }
}
