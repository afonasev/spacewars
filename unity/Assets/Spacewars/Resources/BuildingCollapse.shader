Shader "Spacewars/BuildingCollapse"
{
    Properties
    {
        _Phase ("Simulation phase",Float)=0
        _Seed ("Variation",Float)=0
        _Radius ("Footprint",Float)=1
        _Roof ("Remaining roof height",Float)=1
        _FireScale ("Fire scale",Float)=.65
        _SmokeOpacity ("Smoke opacity",Float)=.32
    }
    SubShader
    {
        Tags {"RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent"}
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            float _Phase,_Seed,_Radius,_Roof,_FireScale,_SmokeOpacity;
            CBUFFER_END
            struct A{float3 p:POSITION;float2 uv:TEXCOORD0;float2 particle:TEXCOORD1;};
            struct V{float4 p:SV_POSITION;float2 uv:TEXCOORD0;float2 particle:TEXCOORD1;};
            float Hash(float n){return frac(sin(n*127.1+311.7)*43758.5453);}
            float Noise(float2 p){float2 i=floor(p),f=frac(p);f=f*f*(3-2*f);return lerp(lerp(Hash(dot(i,float2(17,59))),Hash(dot(i+float2(1,0),float2(17,59))),f.x),lerp(Hash(dot(i+float2(0,1),float2(17,59))),Hash(dot(i+1,float2(17,59))),f.x),f.y);}
            V Vert(A a)
            {
                V o;float mode=a.particle.x,seed=a.particle.y+_Seed,t=saturate(_Phase),r=Hash(seed),s=Hash(seed+9),angle=r*2*PI;
                float3 right=UNITY_MATRIX_V[0].xyz,up=UNITY_MATRIX_V[1].xyz;
                float3 center=TransformObjectToWorld(float3(0,0,0));float2 size;
                if(mode<.5)
                {
                    center+=float3(cos(angle),0,sin(angle))*_Radius*(.22+s*.38);
                    center.y+=max(.12,_Roof*(.82+s*.06));
                    float flicker=.75+.25*sin(t*120+seed*7);
                    size=float2(.12,.38)*_Radius*_FireScale*flicker;
                    center.y+=size.y;
                }
                else if(mode<1.5)
                {
                    float age=frac(t*1.8+s);
                    float spread=(.25+age*.65)*_Radius;
                    center+=float3(cos(angle)*spread,max(.04,_Roof*.928)+age*_Radius*.65,sin(angle)*spread);
                    size=(.16+age*.35)*_Radius;
                }
                else if(mode<2.5)
                {
                    float age=frac(t*3+s);
                    center+=float3(cos(angle),0,sin(angle))*age*_Radius*.7;
                    center.y+=max(.1,_Roof*.92)+sin(age*PI)*_Radius*.35;
                    size=float2(.008,.055)*_Radius*(1-age);
                }
                else
                {
                    float onset=(a.particle.y-32)*.17;
                    float age=saturate((t-onset)/.08);
                    center+=float3(cos(angle)*_Radius*.35,max(.12,_Roof*.92),sin(angle)*_Radius*.35);
                    size=(.10+age*.24)*_Radius*_FireScale;
                }
                o.p=TransformWorldToHClip(center+right*a.p.x*size.x+up*a.p.y*size.y);
                o.uv=a.uv;o.particle=a.particle;return o;
            }
            half4 Frag(V i):SV_Target
            {
                float2 p=i.uv*2-1;float t=saturate(_Phase),seed=i.particle.y+_Seed,mode=i.particle.x;
                float n=Noise(p*4+float2(seed,t*10)),alpha;float3 color;
                if(mode<.5)
                {
                    float width=(1-i.uv.y)*(.65+n*.35);
                    alpha=smoothstep(width,width*.3,abs(p.x))*smoothstep(1,.3,p.y)*smoothstep(-1,-.65,p.y);
                    alpha*=(.3+.7*smoothstep(0,.06,t))*(1-smoothstep(.62,.83,t));
                    float flash=pow(saturate(sin(t*50+seed)),14);
                    color=lerp(float3(1,.17,.012),float3(1,.93,.55),saturate((1-i.uv.y)*.8+flash));
                }
                else if(mode<1.5)
                {
                    alpha=exp(-dot(p,p)*3)*smoothstep(.20,.75,n)*_SmokeOpacity;
                    alpha*=smoothstep(0,.12,t)*(1-smoothstep(.70,1,t));
                    color=lerp(float3(.14,.13,.12),float3(.48,.44,.38),n);
                }
                else if(mode<2.5)
                {
                    alpha=saturate(1-dot(p,p))*smoothstep(0,.04,t)*(1-smoothstep(.5,.8,t));
                    color=float3(1,.5,.06);
                }
                else
                {
                    float age=(t-(i.particle.y-32)*.17)/.08;
                    alpha=exp(-dot(p,p)*5)*saturate(1-age)*step(0,age);
                    color=lerp(float3(1,.92,.65),float3(1,.25,.025),saturate(age));
                }
                return half4(color,alpha);
            }
            ENDHLSL
        }
    }
}
