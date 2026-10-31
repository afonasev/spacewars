Shader "Spacewars/WreckSmoke"
{
    Properties { _Phase("Phase",Float)=0 _Size("Size",Float)=1 _Rise("Rise",Float)=1 _Opacity("Opacity",Float)=.35 _Seed("Seed",Float)=1 }
    SubShader {
        Tags {"RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent"}
        Pass {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            float _Phase,_Size,_Rise,_Opacity,_Seed;
            CBUFFER_END
            struct A {float4 positionOS:POSITION;float2 uv:TEXCOORD0;float2 id:TEXCOORD1;};
            struct V {float4 positionCS:SV_POSITION;float2 uv:TEXCOORD0;float fade:TEXCOORD1;float shade:TEXCOORD2;};
            float Hash(float x){return frac(sin(x*12.9898+_Seed*.137)*43758.5453);}
            V Vert(A a){
                V o;float id=a.id.x;float age=saturate(_Phase);float angle=Hash(id+1)*6.2831853;float spread=(.08+age*.35)*_Size;float size=_Size*(.18+age*.32)*( .75+Hash(id+9)*.5);
                float3 center=TransformObjectToWorld(float3(cos(angle)*spread,age*_Rise*(.45+Hash(id+3)*.55)+size*.4,sin(angle)*spread));
                float3 right=UNITY_MATRIX_I_V._m00_m10_m20,up=UNITY_MATRIX_I_V._m01_m11_m21;
                float3 at=center+right*a.positionOS.x*size+up*a.positionOS.y*size;
                o.positionCS=TransformWorldToHClip(at);o.uv=a.uv;o.fade=(1-age)*(1-age)*_Opacity;o.shade=.12+Hash(id+13)*.08;return o;
            }
            half4 Frag(V i):SV_Target {float2 p=i.uv*2-1;float r=dot(p,p);float cloud=saturate(1-r);cloud=cloud*cloud*(.83+.17*sin(p.x*12+p.y*8));return half4(i.shade,i.shade,i.shade,cloud*i.fade);}
            ENDHLSL
        }
    }
}
