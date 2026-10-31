Shader "Spacewars/TowerRangeZone"
{
    Properties {
        _FogMask("Team fog",2D)="black" {}
        _FogBounds("Map half bounds",Vector)=(32,32,0,0)
    }
    SubShader {
        Tags {"RenderType"="Transparent" "Queue"="Transparent-10" "RenderPipeline"="UniversalPipeline"}
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
            float4 _FogBounds,_Zone,_Color,_Appearance;
            float _Lift,_InnerFade;
            CBUFFER_END
            struct Attributes {float4 positionOS:POSITION;};
            struct Varyings {float4 positionCS:SV_POSITION;float3 positionWS:TEXCOORD0;};
            Varyings vert(Attributes v) {
                Varyings o;o.positionWS=TransformObjectToWorld(v.positionOS.xyz);o.positionWS.y+=_Lift;
                o.positionCS=TransformWorldToHClip(o.positionWS);return o;
            }
            half4 frag(Varyings i):SV_Target {
                float distanceToTower=length(i.positionWS.xz-_Zone.xy);
                float aa=max(fwidth(distanceToTower),.0001); // Numerical derivative floor, not an authored width.
                clip(_Zone.w+aa-distanceToTower);
                float2 fog=SAMPLE_TEXTURE2D(_FogMask,sampler_FogMask,i.positionWS.xz/(2*_FogBounds.xy)+.5).rg;
                clip(fog.g-.001); // Never paint an unexplored surface through unseen fog.
                float edge=1-smoothstep(_Zone.w-_Appearance.w-aa,_Zone.w+aa,distanceToTower);
                float rim=smoothstep(_Zone.w-_Appearance.z-aa,_Zone.w-_Appearance.z+aa,distanceToTower)*(1-smoothstep(_Zone.w-aa,_Zone.w+aa,distanceToTower));
                float fill=_Appearance.x*lerp(_InnerFade,1,saturate(distanceToTower/_Zone.w))*edge;
                return half4(_Color.rgb,max(fill,rim*_Appearance.y)*(1-fog.r));
            }
            ENDHLSL
        }
    }
}
