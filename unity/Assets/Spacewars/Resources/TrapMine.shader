Shader "Spacewars/TrapMine"
{
    Properties { _OwnerColor("Owner",Color)=(1,.5,.1,1) }
    SubShader {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
        Pass {
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            float4 _OwnerColor;
            struct A { float3 positionOS:POSITION;float3 normalOS:NORMAL;float4 color:COLOR; };
            struct V { float4 positionCS:SV_POSITION;float3 normalWS:TEXCOORD0;float4 color:COLOR; };
            V Vert(A v){V o;o.positionCS=TransformObjectToHClip(v.positionOS);o.normalWS=TransformObjectToWorldNormal(v.normalOS);o.color=v.color;return o;}
            half4 Frag(V v):SV_Target { Light light=GetMainLight();float shade=.42+.58*saturate(dot(normalize(v.normalWS),light.direction));return half4(lerp(v.color.rgb*shade,_OwnerColor.rgb*1.3,v.color.a),1); }
            ENDHLSL
        }
    }
}
