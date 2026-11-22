Shader "Spacewars/CerberusPlasma"
{
    Properties
    {
        _BaseColor("Plasma tint", Color) = (.7,.95,1,.65)
        _FlowPhase("Plasma flow phase", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha One
            ZWrite Off
            Cull Back
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor;
            float _FlowPhase;
            CBUFFER_END
            struct Varyings
            {
                float4 clipPosition : SV_POSITION;
                float3 local : TEXCOORD0;
                float3 world : TEXCOORD1;
                float fog : TEXCOORD2;
                float3 normalWS : TEXCOORD3;
                nointerpolation float core : TEXCOORD4;
            };
            Varyings Vert(float3 local : POSITION, float3 normal : NORMAL, float2 layer : TEXCOORD0)
            {
                Varyings o;
                float waves = sin(local.z * 16 - _FlowPhase * 1.5 + local.x * 4)
                    + .5 * sin(local.z * 29 - _FlowPhase * 2.1 + local.y * 7);
                local.xy *= 1 + .18 * waves;
                float bend = sin(local.z * 3.14159) * local.z * .16;
                local.xy += bend * float2(sin(local.z * 13 - _FlowPhase), cos(local.z * 9 - _FlowPhase * 1.2));
                o.normalWS = TransformObjectToWorldNormal(normal);
                o.local = local;
                o.core = layer.x;
                local.xy *= lerp(1.0, .38, o.core);
                o.world = TransformObjectToWorld(local);
                o.clipPosition = TransformWorldToHClip(o.world);
                o.fog = ComputeFogFactor(o.clipPosition.z);
                return o;
            }
            half4 Frag(Varyings i) : SV_Target
            {
                float axial = saturate(i.local.z);
                float3 radial = float3(i.local.xy / max(.20, axial), 0);
                float facing = abs(dot(normalize(i.normalWS),
                    GetWorldSpaceNormalizeViewDir(i.world)));
                float wave = sin(axial * 24 - _FlowPhase * 1.8 + radial.x * 4 + radial.y * 3);
                float filament = .5 + .5 * sin(radial.x * 9 + radial.y * 7 + axial * 17 - _FlowPhase + wave * 2);
                // Broad luminous packets travel along the stream with dark gaps between them.
                float packet = .5 + .5 * cos(axial * 13.5 - _FlowPhase * 1.65 + .7 * sin(radial.y * 2 + axial * 5));
                float glow = smoothstep(.22, .85, packet);
                float end = 1 - .65 * smoothstep(.72 + wave * .06, 1, axial);
                float alpha = pow(saturate(facing), .8) * end * (.35 + .65 * filament) * (.12 + .88 * glow);
                half3 tint = lerp(lerp(half3(.81,.90,1), _BaseColor.rgb, .72), _BaseColor.rgb, i.core);
                return half4(MixFogColor(tint * (.70 + .45 * glow), half3(0,0,0), i.fog), saturate(alpha * 2) * lerp(.54, 1.0, i.core));
            }
            ENDHLSL
        }
    }
}
