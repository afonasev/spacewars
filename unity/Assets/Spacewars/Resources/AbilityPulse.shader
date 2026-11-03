Shader "Spacewars/AbilityPulse"
{
    Properties { _MainTex("Energy texture",2D)="white"{} _EmissionGain("Emission",Float)=1 }
    SubShader {
        Tags {"RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent"}
        Pass {
            Blend SrcAlpha One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex);SAMPLER(sampler_MainTex);float _EmissionGain;
            struct A{float3 p:POSITION;float4 c:COLOR;float2 uv:TEXCOORD0;};struct V{float4 p:SV_POSITION;float4 c:COLOR;float2 uv:TEXCOORD0;};
            V Vert(A a){V v;v.p=TransformObjectToHClip(a.p);v.c=a.c;v.uv=a.uv;return v;}
            half4 Frag(V v):SV_Target {if(v.uv.x<0){
                // Bilinear ribbon coordinates: soft transverse core and tapered ends.
                half u=-v.uv.x-1;half edge=saturate(1-abs(2*u-1));half along=saturate(1-abs(2*v.uv.y-1));
                return half4(v.c.rgb*_EmissionGain,v.c.a*edge*edge*along);
            }half4 tex=SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,v.uv);return half4(tex.rgb*v.c.rgb*_EmissionGain,tex.a*v.c.a);}
            ENDHLSL
        }
    }
}
