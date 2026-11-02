Shader "Spacewars/TrapMineBurst"
{
    SubShader {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct A {float3 positionOS:POSITION;float4 color:COLOR;};struct V {float4 positionCS:SV_POSITION;float4 color:COLOR;};
            V Vert(A v){V o;o.positionCS=TransformObjectToHClip(v.positionOS);o.color=v.color;return o;}half4 Frag(V v):SV_Target{return v.color;}
            ENDHLSL
        }
    }
}
