Shader "Spacewars/GroundTrails"
{
 Properties { _FogMask("Visibility",2D)="white"{} }
 SubShader
 {
  Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent-20" "RenderType"="Transparent" }
  Pass
  {
   Blend DstColor Zero
   ZWrite Off Cull Off Offset -1,-1
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   TEXTURE2D(_FogMask);SAMPLER(sampler_FogMask);
   float _Extent,_Now,_Lifetime,_Fade,_Opacity,_Edge,_Grain;
   struct A{float4 positionOS:POSITION;float4 uv:TEXCOORD0;float4 data:TEXCOORD1;float4 color:COLOR;};
   struct V{float4 positionCS:SV_POSITION;float4 uv:TEXCOORD0;float4 data:TEXCOORD1;float4 color:COLOR;float2 world:TEXCOORD2;};
   V vert(A a){V o;float3 w=TransformObjectToWorld(a.positionOS.xyz);o.positionCS=TransformWorldToHClip(w);o.world=w.xz;o.uv=a.uv;o.data=a.data;o.color=a.color;return o;}
   float hash(float2 p){return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
   half4 frag(V i):SV_Target
   {
    float fog=SAMPLE_TEXTURE2D(_FogMask,sampler_FogMask,(i.world+_Extent)/(2*_Extent)).r;
    clip(.02-fog); // Current visibility only, never remembered/discovered ground.
    float age=_Now-i.data.x;clip(_Lifetime-age);
    float x=i.uv.x,y=i.uv.y,kind=i.data.y;
    float irregular=(hash(floor(i.world*30))-.5)*_Edge;
    float edge=smoothstep(0,_Edge,min(x,1-x)+irregular);
    float tread;
    if(kind<.5){float t=frac(y);tread=1-smoothstep(.28,.38,abs(t-.5));tread*=.8+.2*smoothstep(.1,.2,min(x,1-x));}
    else {float t=frac(y+abs(x-.5)*(kind<1.5?1.2:.85));tread=1-smoothstep(.23,.36,abs(t-.5));tread*=smoothstep(.025,.08,abs(x-.5));}
    float grain=lerp(1,hash(floor(i.world*45)),_Grain);
    float alpha=(.12+.88*tread)*edge*grain*_Opacity*i.color.a*saturate((_Lifetime-age)/_Fade);
    float3 shade=i.color.rgb;
    // Subtle raised rim on loose pale material, no luminous or team-colored paint.
    if(i.data.z>2.5){float rim=(1-smoothstep(.02,.07,abs(frac(y)-.14)))*.25;shade+=rim;}
    // Multiplicative pigment retains the ground lighting, including cast shadows.
    return half4(lerp(half3(1,1,1),shade,alpha),1);
   }
   ENDHLSL
  }
 }
}
