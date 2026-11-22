Shader "Hidden/Spacewars/SSAO Temporal Depth"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            Name "SSAO_Occlusion"
            HLSLPROGRAM
            #pragma editor_sync_compilation
            #pragma vertex Vert
            #pragma fragment Estimate
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #pragma multi_compile_local_fragment _INTERLEAVED_GRADIENT _BLUE_NOISE
            #pragma multi_compile_local_fragment _SOURCE_DEPTH_LOW _SOURCE_DEPTH_MEDIUM _SOURCE_DEPTH_HIGH _SOURCE_DEPTH_NORMALS
            #pragma multi_compile_local_fragment _ _ORTHOGRAPHIC
            #pragma multi_compile_local_fragment _SAMPLE_COUNT_LOW _SAMPLE_COUNT_MEDIUM _SAMPLE_COUNT_HIGH
            #pragma multi_compile_local_fragment _ SPACEWARS_SSAO_TEMPORAL

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRenderingKeywords.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SSAO.hlsl"

            Texture2D<uint> _TemporalDirtyTiles;
            Texture2D<float4> _TemporalPreviousAO;

            half4 Estimate(Varyings input) : SV_Target
            {
            #if defined(SPACEWARS_SSAO_TEMPORAL) && defined(_ORTHOGRAPHIC)
                uint2 pixel = uint2(input.positionCS.xy);
                UNITY_BRANCH if (_TemporalDirtyTiles.Load(int3(pixel / 16, 0)) == 0)
                    return half4(_TemporalPreviousAO.Load(int3(pixel, 0)));
            #endif
                return SSAO(input);
            }
            ENDHLSL
        }

        UsePass "Hidden/Universal Render Pipeline/ScreenSpaceAmbientOcclusion/SSAO_BILATERAL_HORIZONTALBLUR"
        UsePass "Hidden/Universal Render Pipeline/ScreenSpaceAmbientOcclusion/SSAO_BILATERAL_VERTICALBLUR"
        UsePass "Hidden/Universal Render Pipeline/ScreenSpaceAmbientOcclusion/SSAO_BILATERAL_FINALBLUR"
        Pass
        {
            Name "CopyRawDepth"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment CopyRawDepth
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            float CopyRawDepth(Varyings input) : SV_Target
            {
                return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, input.texcoord).r;
            }
            ENDHLSL
        }
    }
}
