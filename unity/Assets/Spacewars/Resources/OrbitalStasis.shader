Shader "Spacewars/OrbitalStasis"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float3 position : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 position : SV_POSITION; float2 uv : TEXCOORD0; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.position = TransformObjectToHClip(input.position);
                output.uv = input.uv;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                clip(input.uv.y);
                half y = saturate(input.uv.y);
                half rim = 1 - smoothstep(0.015, 0.055, min(y, 1 - y));
                half3 wall = lerp(half3(0.34, 0.72, 1), half3(0.64, 0.49, 1), y);
                return half4(lerp(wall, half3(0.83, 0.85, 1), rim), lerp(0.15, 0.88, rim));
            }
            ENDHLSL
        }
    }
}
