Shader "Spacewars/ReinforcementWarp"
{
    Properties
    {
        _Progress ("Materialization progress", Range(0,1)) = 0
        _Opacity ("Intensity", Range(0,1)) = 0.62
        _PulseCount ("Pulses per materialization", Float) = 2
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+10" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
            float _Progress;
            float _Opacity;
            float _PulseCount;
            CBUFFER_END

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
                float progress = saturate(_Progress);
                float fade = 1.0 - smoothstep(0.60, 1.0, progress);
                float pulse = 0.84 + 0.16 * cos(progress * _PulseCount * TWO_PI);
                float height = input.uv.y;
                float intensity;
                if (height < 0.0)
                {
                    float radialDistance = abs(height + 1.31);
                    intensity = 0.63 * exp(-radialDistance * 23.0)
                              + 0.36 * exp(-radialDistance * 6.0);
                }
                else
                {
                    float waveHeight = lerp(0.12, 0.94, progress);
                    float scan = 0.22 * exp(-abs(height - waveHeight) * 32.0);
                    float baseGlow = 0.11 * exp(-height * 8.0);
                    float wall = 0.055 * (1.0 - height);
                    intensity = (scan + baseGlow + wall) * (1.0 - smoothstep(0.86, 1.0, height));
                }
                return half4(0.28, 0.83, 1.0, intensity * fade * pulse * _Opacity);
            }
            ENDHLSL
        }
    }
}
