Shader "Spacewars/OfflineCameraMirror"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        ZWrite Off Cull Off
        Pass
        {
            Name "OfflineSourceWorldColor"
            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #pragma vertex Vert
            #pragma fragment Frag
            half4 Frag(Varyings input) : SV_Target0
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv=input.texcoord.xy;
                uv.x=1-uv.x;
                return SAMPLE_TEXTURE2D_X_LOD(_BlitTexture,sampler_LinearClamp,uv,_BlitMipLevel);
            }
            ENDHLSL
        }
    }
}
