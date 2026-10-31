Shader "Spacewars/NullDockSurface"
{
    Properties {
        _FogMask("Team fog",2D)="black"{} _FogBounds("Map half bounds",Vector)=(96,96,0,0) _FogColor("Fog tint",Color)=(0,0,0,1)
        _Steel("Steel albedo",2D)="gray"{} _SteelB("Steel sibling B",2D)="gray"{} _SteelC("Steel sibling C",2D)="gray"{}
        _Panel("Ceramic panels",2D)="gray"{} _PanelB("Panel sibling B",2D)="gray"{} _PanelC("Panel sibling C",2D)="gray"{}
        _Finish("Panel size joint texture tile contrast",Vector)=(4,.055,3,.35) _PatchSize("Variant patch size",Float)=19 _Emission("Beacon intensity",Float)=1.4 _RouteWidth("Transit width",Float)=9
        _CoreEmission("Recessed core intensity",Float)=1.1
        _BattleColor("Battle paint sRGB",Vector)=(.68,.43,.39,1) _ContestedColor("Contested paint sRGB",Vector)=(.7,.63,.4,1) _RearColor("Rear paint sRGB",Vector)=(.45,.6,.5,1)
        _ThreatBounds("Central half extents and rear thresholds",Vector)=(48,28,60,20)
        _BaseSitesA("HQ centers XZ",Vector)=(-66,0,66,0) _BaseSitesB("Outpost centers XZ",Vector)=(28,50,-28,-50) _SiteDisk("Inner radius and lift",Vector)=(11.3,.13,0,0)
    }
    SubShader {
        Tags {"RenderType"="Opaque" "RenderPipeline"="UniversalPipeline"}
        Pass {
            Tags {"LightMode"="UniversalForward"}
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_FogMask);SAMPLER(sampler_FogMask);TEXTURE2D(_Steel);SAMPLER(sampler_Steel);TEXTURE2D(_SteelB);TEXTURE2D(_SteelC);TEXTURE2D(_Panel);TEXTURE2D(_PanelB);TEXTURE2D(_PanelC);
            CBUFFER_START(UnityPerMaterial)
            float4 _FogBounds,_FogColor,_Finish,_BattleColor,_ContestedColor,_RearColor,_ThreatBounds,_BaseSitesA,_BaseSitesB,_SiteDisk;float _PatchSize,_Emission,_RouteWidth,_CoreEmission;
            CBUFFER_END
            struct A {float4 positionOS:POSITION;float3 normalOS:NORMAL;float4 color:COLOR;float2 role:TEXCOORD1;};
            struct V {float4 positionCS:SV_POSITION;float3 p:TEXCOORD0;float3 n:TEXCOORD1;float4 color:COLOR;float2 role:TEXCOORD2;};
            V vert(A i){V o;o.p=TransformObjectToWorld(i.positionOS.xyz);o.positionCS=TransformWorldToHClip(o.p);o.n=TransformObjectToWorldNormal(i.normalOS);o.color=i.color;o.role=i.role;return o;}
            float hash(float2 p){return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
            float noise(float2 p){float2 c=floor(p),f=frac(p);f=f*f*(3-2*f);return lerp(lerp(hash(c),hash(c+float2(1,0)),f.x),lerp(hash(c+float2(0,1)),hash(c+1),f.x),f.y);}
            float segment(float2 p,float2 a,float2 b){float2 d=b-a;return length(p-a-d*saturate(dot(p-a,d)/dot(d,d)));}
            float route(float2 p){return min(min(segment(p,float2(-49,0),float2(-29,8)),segment(p,float2(-29,8),float2(-6,23))),min(segment(p,float2(-6,23),float2(19,23)),segment(p,float2(19,23),float2(49,0))));}
            half4 frag(V i):SV_Target {
                float3 n=normalize(i.n);float3 an=abs(n);float2 uv=an.y>=max(an.x,an.z)?i.p.xz:an.x>an.z?i.p.zy:i.p.xy;
                float patch=noise(i.p.xz/_PatchSize+noise(i.p.xz/(_PatchSize*.6))*.7);
                float3 a=SAMPLE_TEXTURE2D(_Steel,sampler_Steel,uv/_Finish.z).rgb;
                float3 b=SAMPLE_TEXTURE2D(_SteelB,sampler_Steel,uv/_Finish.z).rgb;
                float3 c=SAMPLE_TEXTURE2D(_SteelC,sampler_Steel,uv/_Finish.z).rgb;
                float3 tex=lerp(lerp(a,b,smoothstep(.36,.40,patch)),c,smoothstep(.62,.66,patch));
                if(i.role.x>.5&&i.role.x<1.5){
                    float3 pa=SAMPLE_TEXTURE2D(_Panel,sampler_Steel,uv/_Finish.z).rgb,pb=SAMPLE_TEXTURE2D(_PanelB,sampler_Steel,uv/_Finish.z).rgb,pc=SAMPLE_TEXTURE2D(_PanelC,sampler_Steel,uv/_Finish.z).rgb;
                    tex=lerp(lerp(pa,pb,smoothstep(.36,.40,patch)),pc,smoothstep(.62,.66,patch));
                }
                float3 painted=i.color.rgb;
                // Only horizontal ground-floor role 0 is zoned. Ramps and models retain their materials.
                if(i.role.x<.5&&n.y>.999){
                    float central=step(abs(i.p.x),_ThreatBounds.x)*step(abs(i.p.z),_ThreatBounds.y);
                    float rear=max(step(_ThreatBounds.z,i.p.x)*step(_ThreatBounds.w,i.p.z),step(i.p.x,-_ThreatBounds.z)*step(i.p.z,-_ThreatBounds.w));
                    painted=lerp(lerp(_ContestedColor.rgb,_RearColor.rgb,rear),_BattleColor.rgb,central);
                }
                if(i.role.x>.5&&i.role.x<1.5&&n.y>.999&&i.p.y>0){
                    float d=min(min(length(i.p.xz-_BaseSitesA.xy),length(i.p.xz-_BaseSitesA.zw)),min(length(i.p.xz-_BaseSitesB.xy),length(i.p.xz-_BaseSitesB.zw)));
                    float disk=1-smoothstep(_SiteDisk.x-max(fwidth(d),.001),_SiteDisk.x,d);
                    painted=lerp(painted,1,disk*_SiteDisk.y);
                }
                float3 baseColor=SRGBToLinear(painted);
                float surfaceContrast=i.role.x>7.5?_Finish.w*.25:_Finish.w;
                float3 col=baseColor*clamp(1+(tex-.12)*surfaceContrast*7,.5,1.7);
                // Engineered rectangular panels with service subdivisions; no staggered brick bond.
                float row=floor(uv.y/(_Finish.x*1.1));
                float rowPattern=hash(float2(row,7));
                float2 panelScale=float2(_Finish.x*lerp(1.4,3.2,rowPattern),_Finish.x*1.1);
                float2 cell=uv/panelScale;cell.x+=hash(float2(row,21))*2;float2 id=floor(cell),local=frac(cell)*panelScale;
                float2 edge=min(local,panelScale-local);
                float aa=max(.006,max(fwidth(uv.x),fwidth(uv.y)));
                float panelBorder=min(min(edge.x,edge.y),abs(edge.x+edge.y-.45)*.707);
                float joint=1-smoothstep(_Finish.y,_Finish.y+aa,panelBorder);
                if(i.role.x<1.5){
                    float variant=hash(id);
                    col*=lerp(.94,1.06,variant);
                    col*=1-.24*joint;
                    float lip=(1-smoothstep(_Finish.y+.025,_Finish.y+.025+aa,min(edge.x,edge.y)))-joint;
                    col+=lip*.012;
                    // Corner fasteners and inset access-panel linework are scaled in metres.
                    float bolt=1-smoothstep(.055,.055+aa,length(edge-float2(.35,.35)));
                    col=lerp(col,baseColor*.20,bolt*.9);
                    float inset=min(abs(edge.x-.38),abs(edge.y-.38));
                    float outline=(1-smoothstep(.018,.018+aa,inset))*step(.35,min(edge.x,edge.y));
                    col*=1-outline*.16;
                    float subPanel=step(.64,variant)*(1-smoothstep(.018,.018+aa,abs(local.x-panelScale.x*.68)));
                    col*=1-subPanel*.16;
                    float access=step(.91,variant)*step(.62,local.x/panelScale.x)*step(.22,local.y/panelScale.y)*step(local.y/panelScale.y,.78);
                    float slat=1-smoothstep(.025,.025+aa,abs(frac(uv.y*2.8)-.5)/2.8);
                    col*=1-access*(.24+slat*.40);
                    float worn=pow(noise(uv*2.3),6)*noise(uv*.31);
                    col+=worn*.005;
                    // Small ochre inspection marks, not dense repeating hazard stripes.
                    float marker=step(.55,variant)*step(local.x,.85)*step(.42,local.x)*step(local.y,.2)*step(.10,local.y);
                    col=lerp(col,SRGBToLinear(float3(.50,.30,.08)),marker);
                }
                if(i.role.x<.5&&an.y>.7){
                    float transit=min(route(i.p.xz),route(-i.p.xz));float road=1-smoothstep(_RouteWidth*.5,_RouteWidth*.5+.15,transit);
                    col=lerp(col,col*.94,road);
                    float laneEdge=1-smoothstep(.04,.04+aa,abs(transit-_RouteWidth*.5));float breaks=step(.22,frac((i.p.x+i.p.z)/5));
                    col=lerp(col,SRGBToLinear(float3(.55,.38,.12)),laneEdge*breaks*.62);

                }
                if(i.role.x>1.5&&i.role.x<2.5&&an.y<.7){float rib=1-smoothstep(.06,.06+aa,min(frac(uv.x*1.5),1-frac(uv.x*1.5)));col*=1-.3*rib;}
                if(i.role.x>2.5&&i.role.x<3.5){float2 q=abs(frac(uv*3)-.5);float grill=(1-smoothstep(.08,.08+aa*3,min(q.x,q.y)));col*=.25+.75*grill;}
                if(i.role.x>5.5&&i.role.x<6.5){float stripe=step(.48,frac((uv.x+uv.y)*.9));col=lerp(float3(.07,.08,.08),baseColor,stripe);}
                Light sun=GetMainLight(TransformWorldToShadowCoord(i.p));
                float diffuse=saturate(dot(n,sun.direction));float3 light=SampleSH(n)+sun.color*diffuse*sun.shadowAttenuation;
                float metal=i.role.x>=1.5&&i.role.x<7.5?1:.3;
                float spec=pow(saturate(dot(n,normalize(sun.direction+GetWorldSpaceNormalizeViewDir(i.p)))),lerp(28,56,metal))*.24*sun.shadowAttenuation;
                col=col*light+sun.color*spec;
                if(i.role.x>4.5&&i.role.x<5.5)col+=baseColor*_Emission;
                if(i.role.x>6.5&&i.role.x<7.5){
                    // Opaque recessed core: directional internal light and fine longitudinal filaments,
                    // without transparent sorting, extra passes, particles or fog leakage.
                    float transverse=dot(i.p.xz,float2(.894427,-.447214));
                    float filament=pow(saturate(cos(transverse*13)),18);
                    float facing=pow(saturate(n.y),5);
                    col=col*.30+baseColor*_CoreEmission*(.16+.64*facing+.20*filament);
                    col+=float3(.10,.17,.18)*pow(saturate(n.y),36);
                }
                float fog=SAMPLE_TEXTURE2D(_FogMask,sampler_FogMask,i.p.xz/(_FogBounds.xy*2)+.5).r;
                return half4(lerp(col,_FogColor.rgb,fog),1);
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
    }
}
