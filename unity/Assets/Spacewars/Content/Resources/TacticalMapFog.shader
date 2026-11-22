Shader "Hidden/Spacewars/TacticalMapFog"
{
    Properties
    {
        _MainTex ("Terrain", 2D) = "white" {}
        _MapFog ("Team fog", 2D) = "black" {}
        _BoostTerrain ("Schematic terrain brightness", Float) = 1
        _FogTint ("Fog tint", Vector) = (0,0,0,1)
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            sampler2D _MapFog;
            float4 _FogTint;
            float _BoostTerrain;

            float4 frag(v2f_img input) : SV_Target
            {
                float4 ground = tex2D(_MainTex, input.uv);
                float brightest = max(ground.r, max(ground.g, ground.b));
                ground.rgb *= lerp(1, min(1.3, 0.97 / max(0.01, brightest)), _BoostTerrain);
                float opacity = tex2D(_MapFog, input.uv).r;
                float4 color = lerp(ground, _FogTint, opacity * 0.72);
                #ifndef UNITY_COLORSPACE_GAMMA
                    color = saturate(color);
                    color.rgb = float3(GammaToLinearSpaceExact(color.r),
                                       GammaToLinearSpaceExact(color.g),
                                       GammaToLinearSpaceExact(color.b));
                #endif
                return color;
            }
            ENDCG
        }
    }
}
