Shader "Spacewars/DirectImpact"
{
    Properties
    {
        _BaseColor ("Tint", Color) = (1,1,1,1)
        _DstBlend ("Destination blend", Float) = 1
        _Phase ("Snapshot age", Float) = 0
        _Seed ("Contact variation", Float) = 0
        _SparkLength ("Spark travel / radius", Float) = 2
        _SparkWidth ("Spark width / radius", Float) = 0.035
        _Intensity ("Hot metal intensity", Float) = 1.5
        _SmokeOpacity ("Smoke opacity", Float) = 0.14
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha [_DstBlend]
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float _Phase, _Seed, _SparkLength, _SparkWidth, _Intensity, _SmokeOpacity;
            CBUFFER_END
            struct Attributes { float3 positionOS:POSITION; float2 uv:TEXCOORD0; float2 particle:TEXCOORD1; };
            struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; float2 particle:TEXCOORD1; };
            float Hash(float x) { return frac(sin(x*127.1+311.7)*43758.5453); }
            float Noise(float2 p)
            {
                float2 i=floor(p),f=frac(p);f=f*f*(3-2*f);
                return lerp(lerp(Hash(dot(i,float2(17,59))),Hash(dot(i+float2(1,0),float2(17,59))),f.x),
                    lerp(Hash(dot(i+float2(0,1),float2(17,59))),Hash(dot(i+1,float2(17,59))),f.x),f.y);
            }
            Varyings Vert(Attributes input)
            {
                Varyings output;
                float mode=input.particle.x,seed=input.particle.y+_Seed;
                float age=saturate(_Phase),r=Hash(seed+2),s=Hash(seed+8);
                float3 right=UNITY_MATRIX_V[0].xyz,up=UNITY_MATRIX_V[1].xyz;
                float scale=length(TransformObjectToWorldDir(float3(1,0,0),false));
                float3 offset=0;
                float2 p=input.positionOS.xy;
                if(mode<0.5) offset=(right*p.x+up*p.y)*(1.3+age*.5); // feathered glow
                else if(mode<1.5) offset=(right*p.x+up*p.y)*(.36*(1-age*.7)); // tiny irregular core
                else if(mode<3.5)
                {
                    // Non-uniform authored ejection fan; per-contact seed avoids repeated stamps.
                    float angle=(.18+r*.9)*PI;
                    float2 direction=float2(cos(angle),sin(angle));
                    float travel=pow(age,.65)*_SparkLength*(.45+s*1.1);
                    float extent=mode<2.5?_SparkLength*(.07+r*.18)*(1-age*.65):_SparkWidth*(.6+s);
                    float width=mode<2.5?_SparkWidth*(.35+s*.8):extent;
                    float2 q=direction*(travel+p.x*extent)+float2(-direction.y,direction.x)*p.y*width;
                    q.y-=age*age*_SparkLength*.18;
                    offset=right*q.x+up*q.y;
                }
                else
                {
                    float size=(.45+age*1.25)*(mode<4.5?1:.72);
                    float2 drift=float2((r-.6)*age*.65,age*(.55+s*.6));
                    offset=right*(p.x*size+drift.x)+up*(p.y*size+drift.y);
                }
                // Millimetre render bias avoids a coplanar contact point
                // disappearing into its armour triangle; this never moves the hit fact.
                float3 center=TransformObjectToWorld(float3(0,0,0))+UNITY_MATRIX_V[2].xyz*.002;
                output.positionCS=TransformWorldToHClip(center+offset*scale);
                output.uv=input.uv;output.particle=input.particle;return output;
            }
            half4 Frag(Varyings input):SV_Target
            {
                float2 p=input.uv*2-1;float age=saturate(_Phase),mode=input.particle.x,seed=input.particle.y+_Seed;
                float energy=1-age,alpha;float3 color;
                if(mode<.5)
                {
                    alpha=exp(-dot(p,p)*6)*.4*pow(energy,1.5);
                    color=float3(1,.22,.025)*_Intensity;
                }
                else if(mode<1.5)
                {
                    float broken=Noise(p*7+seed)*.4+.6;
                    alpha=exp(-(abs(p.x)*5+abs(p.y)*6))*broken*energy;
                    color=float3(1,.94,.72)*_Intensity*2;
                }
                else if(mode<3.5)
                {
                    float taper=.2+.8*sqrt(saturate(1-p.x*p.x));
                    float edge=saturate((1-abs(p.y)/taper)*3);
                    alpha=edge*smoothstep(1,.55,abs(p.x))*pow(energy,.6+Hash(seed));
                    float hotTip=pow(saturate(input.uv.x),5)*energy*.7;
                    color=lerp(float3(1,.22,.018),float3(1,.9,.55),hotTip)*_Intensity;
                }
                else
                {
                    float cloud=Noise(p*3+seed+age*.3)*.6+Noise(p*7-seed)*.3+Noise(p*13)*.1;
                    alpha=exp(-dot(p,p)*3.3)*smoothstep(.22,.78,cloud)*sin(age*PI)*_SmokeOpacity;
                    color=lerp(float3(.18,.16,.14),float3(.40,.39,.37),cloud);
                }
                return half4(color*_BaseColor.rgb,alpha*_BaseColor.a);
            }
            ENDHLSL
        }
    }
}
