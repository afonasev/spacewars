Shader "Spacewars/GaussEnergy"
{
 Properties { _BaseColor("Energy",Color)=(1,1,1,1) _Phase("Phase",Float)=0 _Ripple("Ripple",Float)=0 }
 SubShader
 {
  Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
  Pass
  {
   Blend SrcAlpha One
   ZWrite Off
   Cull Off
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   struct Attributes { float4 positionOS:POSITION;float2 uv:TEXCOORD0;float4 color:COLOR; };
   struct Varyings { float4 positionCS:SV_POSITION;float2 uv:TEXCOORD0;float4 color:COLOR; };
   CBUFFER_START(UnityPerMaterial)
   float4 _BaseColor;float _Phase;float _Ripple;
   CBUFFER_END
   Varyings vert(Attributes v){Varyings o;o.positionCS=TransformObjectToHClip(v.positionOS.xyz);o.uv=v.uv;o.color=v.color;return o;}
   half4 frag(Varyings i):SV_Target
   {
    // Fixed waveform coefficients describe the authored energy texture, not simulation timing.
    float edge=saturate(1-abs(i.uv.y*2-1));edge=edge*edge*(3-2*edge);
    float ripple=lerp(1,.75+.25*sin(i.uv.x*95-_Phase*24),_Ripple);
    float ends=saturate(i.uv.x*30)*saturate((1-i.uv.x)*30);
    return half4(_BaseColor.rgb*i.color.rgb,_BaseColor.a*i.color.a*edge*ripple*ends);
   }
   ENDHLSL
  }
 }
}
