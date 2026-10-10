Shader "Spacewars/UnitSelectionRing"
{
    Properties {
        _RingWidth("Ring width / radius",Float)=.06
        _FogMask("Team fog",2D)="black" {}
        _FogBounds("Map half bounds",Vector)=(32,32,0,0)
    }
    SubShader {
        Tags {"RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline"}
        Pass {
            Tags {"LightMode"="UniversalForward"}
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_FogMask); SAMPLER(sampler_FogMask);
            CBUFFER_START(UnityPerMaterial)
            float _RingWidth; float4 _FogBounds;
            CBUFFER_END
            struct Attributes {float4 positionOS:POSITION; float2 uv:TEXCOORD0;};
            struct Varyings {float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; float3 positionWS:TEXCOORD1;};
            Varyings vert(Attributes v) {
                Varyings o; o.positionWS=TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS=TransformWorldToHClip(o.positionWS);o.uv=v.uv;return o;
            }
            half4 frag(Varyings i):SV_Target {
                float r=length(i.uv*2-1);
                float aa=max(fwidth(r),.0001);
                // Leave a derivative-width margin inside the quad so the outer edge also antialiases.
                float outer=1-aa;
                float alpha=(1-smoothstep(outer-aa,outer,r))*smoothstep(outer-_RingWidth-aa,outer-_RingWidth,r);
                float fog=SAMPLE_TEXTURE2D(_FogMask,sampler_FogMask,i.positionWS.xz/(2*_FogBounds.xy)+.5).r;
                // Fixed green is the approved selection art, independent of team paint and scene lighting.
                return half4(.12,.9,.28,alpha*(1-fog));
            }
            ENDHLSL
        }
    }
}
