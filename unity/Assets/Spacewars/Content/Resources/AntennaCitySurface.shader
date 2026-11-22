Shader "Spacewars/AntennaCitySurface"
{
 Properties {
  _FogMask("Team fog",2D)="black"{} _FogBounds("Map half bounds",Vector)=(170,170,0,0) _FogColor("Fog tint",Color)=(0,0,0,1)
  _Concrete("Concrete",2D)="gray"{} _ConcreteB("Concrete B",2D)="gray"{} _ConcreteC("Concrete C",2D)="gray"{}
  _CityFinishMap("City surface variation and street repairs",2D)="gray"{}
  _CityWindowActivity("Observed window activity",2D)="black"{} _CityWindowBlend("Window fade",Float)=0
  _MirrorDistricts("Observatory district albedo and hard cover",2D)="white"{}
  _LunarPad("Apron concrete",2D)="gray"{} _LunarPadB("Apron concrete B",2D)="gray"{} _LunarPadC("Apron concrete C",2D)="gray"{}
  _Steel("Steel",2D)="gray"{} _SteelB("Steel B",2D)="gray"{} _SteelC("Steel C",2D)="gray"{}
  _Finish("Tile contrast patch bay",Vector)=(4,.25,19,3) _CityLights("Window and sign emission",Vector)=(.55,.85,0,0)
 }
 SubShader {
  Tags {"RenderType"="Opaque" "RenderPipeline"="UniversalPipeline"}
  HLSLINCLUDE
   float hash(float2 p){return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
   float noise(float2 p){float2 c=floor(p),f=frac(p);f=f*f*(3-2*f);return lerp(lerp(hash(c),hash(c+float2(1,0)),f.x),lerp(hash(c+float2(0,1)),hash(c+1),f.x),f.y);}
   float FinishPatch(float2 p,float size){return noise(p/size+noise(p/(size*.6))*.7);}
   float2 RoadRepair(float2 p){
    float2 patchUV=float2(p.x+.57*p.y,p.y-.57*p.x)/float2(12,7);
    patchUV.x+=hash(float2(floor(patchUV.y),0))*3;
    float2 cell=floor(patchUV), q=frac(patchUV);
    float2 margin=.12+float2(hash(cell+7),hash(cell+19))*.19;
    float border=min((min(q.x,1-q.x)-margin.x)*12,(min(q.y,1-q.y)-margin.y)*7);
    return float2(hash(cell)>=.80?border:-2,.09+.14*hash(cell+41));
   }
  ENDHLSL
  Pass {
   Tags {"LightMode"="UniversalForward"}
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #pragma multi_compile_local _ SPACEWARS_CITY_WINDOWS
   #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
   #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
   TEXTURE2D(_CityFinishMap);SAMPLER(sampler_CityFinishMap);
   TEXTURE2D(_CityWindowActivity);SAMPLER(sampler_CityWindowActivity);
   TEXTURE2D(_FogMask);SAMPLER(sampler_FogMask);
   TEXTURE2D(_Concrete);SAMPLER(sampler_Concrete);TEXTURE2D(_ConcreteB);TEXTURE2D(_ConcreteC);
   TEXTURE2D(_MirrorDistricts);SAMPLER(sampler_MirrorDistricts);
   TEXTURE2D(_LunarPad);TEXTURE2D(_LunarPadB);TEXTURE2D(_LunarPadC);
   TEXTURE2D(_Steel);TEXTURE2D(_SteelB);TEXTURE2D(_SteelC);
   CBUFFER_START(UnityPerMaterial)
   float4 _FogBounds,_FogColor,_Finish,_CityLights;float _CityWindowBlend;
   CBUFFER_END
   #if defined(SPACEWARS_CITY_WINDOWS)
    #define NOVA_CITY true
   #else
    #define NOVA_CITY (_CityLights.z>.5)
   #endif
   struct A{float4 positionOS:POSITION;float3 normalOS:NORMAL;float4 color:COLOR;float2 uv:TEXCOORD0;float2 role:TEXCOORD1;};
   struct V{float4 positionCS:SV_POSITION;float3 p:TEXCOORD0;float3 n:TEXCOORD1;float4 color:COLOR;float2 uv:TEXCOORD2;float2 role:TEXCOORD3;
    #if defined(SPACEWARS_CITY_WINDOWS)
     half4 lighting:TEXCOORD4;
    #endif
   };
   V vert(A i){V o;o.p=TransformObjectToWorld(i.positionOS.xyz);o.positionCS=TransformWorldToHClip(o.p);o.n=TransformObjectToWorldNormal(i.normalOS);o.color=i.color;o.uv=i.uv;o.role=i.role;
    #if defined(SPACEWARS_CITY_WINDOWS)
     // City panels use a single tint and normal per face. Evaluate their ambient
     // and directional terms once at each vertex; shadow attenuation stays per pixel.
     half3 normal=normalize(o.n);
     o.color.rgb=SRGBToLinear(i.color.rgb);
     o.lighting=half4(SampleSH(normal),saturate(dot(normal,GetMainLight().direction)));
    #endif
    return o;
   }
   float CityStroke(float v,float width){return 1-smoothstep(width,width+max(fwidth(v),.003),abs(v));}
   float lunarImpact(float2 cell){
    float2 id=floor(cell);
    float2 q=frac(cell)-.5-(float2(hash(id+19),hash(id+71))-.5)*.45;
    float radius=lerp(.085,.23,hash(id+37));
    float r=length(q)/radius;
    float rim=1-smoothstep(.04,.38,abs(r-1));
    float bowl=1-smoothstep(.5,1,r);
    float sunward=dot(q/max(length(q),.001),float2(.63,-.77));
    return (rim*(.10+.28*sunward)-bowl*(.11+.14*sunward))*smoothstep(.3,.72,hash(id+87));
   }
   half4 frag(V i):SV_Target {
    float fog=SAMPLE_TEXTURE2D(_FogMask,sampler_FogMask,i.p.xz/(_FogBounds.xy*2)+.5).r;
    if(NOVA_CITY && fog>=1)return half4(_FogColor.rgb,1);
    float3 n=normalize(i.n),an=abs(n);float2 uv=an.y>=max(an.x,an.z)?i.p.xz:an.x>an.z?i.p.zy:i.p.xy;
    #if defined(SPACEWARS_CITY_WINDOWS)
     float3 finishMap=SAMPLE_TEXTURE2D_LOD(_CityFinishMap,sampler_CityFinishMap,i.p.xz/(_FogBounds.xy*2)+.5,0).rgb;
     float patch=finishMap.r;
    #else
     float patch=FinishPatch(i.p.xz,_Finish.z);
    #endif
    float2 tile=uv/_Finish.x;float3 a,b,c;
    // Each patch uses one texture except in its narrow transition bands.
    // Explicit gradients keep mip selection stable across material boundaries.
    if(NOVA_CITY){
     float2 dx=ddx(tile),dy=ddy(tile);
     bool steel=i.role.x>1.5;
     a=b=c=0;
     [branch] if(patch<.40)a=steel?SAMPLE_TEXTURE2D_GRAD(_Steel,sampler_Concrete,tile,dx,dy).rgb:SAMPLE_TEXTURE2D_GRAD(_Concrete,sampler_Concrete,tile,dx,dy).rgb;
     [branch] if(patch>.36 && patch<.66)b=steel?SAMPLE_TEXTURE2D_GRAD(_SteelB,sampler_Concrete,tile,dx,dy).rgb:SAMPLE_TEXTURE2D_GRAD(_ConcreteB,sampler_Concrete,tile,dx,dy).rgb;
     [branch] if(patch>.62)c=steel?SAMPLE_TEXTURE2D_GRAD(_SteelC,sampler_Concrete,tile,dx,dy).rgb:SAMPLE_TEXTURE2D_GRAD(_ConcreteC,sampler_Concrete,tile,dx,dy).rgb;
    }
    else if(i.role.x< -1.5){a=SAMPLE_TEXTURE2D(_LunarPad,sampler_Concrete,tile).rgb;b=SAMPLE_TEXTURE2D(_LunarPadB,sampler_Concrete,tile).rgb;c=SAMPLE_TEXTURE2D(_LunarPadC,sampler_Concrete,tile).rgb;}
    else if(i.role.x>1.5){a=SAMPLE_TEXTURE2D(_Steel,sampler_Concrete,tile).rgb;b=SAMPLE_TEXTURE2D(_SteelB,sampler_Concrete,tile).rgb;c=SAMPLE_TEXTURE2D(_SteelC,sampler_Concrete,tile).rgb;}
    else{a=SAMPLE_TEXTURE2D(_Concrete,sampler_Concrete,tile).rgb;b=SAMPLE_TEXTURE2D(_ConcreteB,sampler_Concrete,tile).rgb;c=SAMPLE_TEXTURE2D(_ConcreteC,sampler_Concrete,tile).rgb;}
    float3 tex=lerp(lerp(a,b,smoothstep(.36,.40,patch)),c,smoothstep(.62,.66,patch));
    #if defined(SPACEWARS_CITY_WINDOWS)
     float3 base=i.color.rgb;
    #else
     float3 base=SRGBToLinear(i.color.rgb);
    #endif
    float3 emission=0;
    float3 col=base*clamp(1+(tex-.15)*_Finish.y*5,.7,1.3);
    if(!NOVA_CITY && i.role.x<-.5 && i.role.x> -1.5){
     // Exposed regolith on the lunar map: broad mineral bands and scattered impact bowls.
     float2 warp=float2(noise(uv/34),noise(uv/29+8));
     float maria=noise(uv/57+warp*.8);
     float tone=dot(lerp(base,col,.43),float3(.21,.72,.07));
     col=tone*lerp(float3(.64,.69,.82),float3(1.14,1.15,1.19),smoothstep(.2,.8,maria));
     if(an.y<.5){
      float bedding=i.p.y+.52*noise(i.p.xz*.13)+.18*noise(i.p.xz*.6);
      float ledge=frac(bedding*.78);
      float dust=smoothstep(-1.0,-.12,bedding);
      col*=lerp(float3(.45,.44,.48),float3(.84,.80,.74),dust)
          *(.72+.23*smoothstep(.08,.30,ledge)+.12*noise(uv*.9));
     }else{
      float2 apronBounds=_FogBounds.xz;
      float2 nearest=clamp(i.p.xz,-apronBounds,apronBounds);
      float2 districtUV=(2*nearest-i.p.xz)/float2(_FogBounds.x*2,_FogBounds.y)+.5;
      float4 district=SAMPLE_TEXTURE2D(_MirrorDistricts,sampler_MirrorDistricts,districtUV);
      // Beyond the playfield, blend the bank colour into open regolith instead
      // of stretching the last row of the district texture across the horizon.
      float outside=max(abs(i.p.x)-_FogBounds.x,abs(i.p.z)-_FogBounds.z);
      float horizon=smoothstep(0,8,outside);
      float beds=noise(uv*float2(.045,.15)+warp*.8);
      float3 outerRock=float3(.55,.53,.51)+.13*(maria-.5)+.055*(beds-.5);
      district.rgb=lerp(district.rgb,outerRock,horizon);
      district.a*=1-horizon;
      float uncovered=1-smoothstep(.35,.65,district.a);
      col=SRGBToLinear(district.rgb)*clamp(.95+(dot(tex,float3(.21,.72,.07))-.15)*.85,.83,1.20);
      float cluster=smoothstep(.38,.72,noise(uv/23+warp*.65));
      float quietCenter=lerp(.55,1,smoothstep(8,46,abs(i.p.z)));
      float2 rotated=float2(.8*uv.x+.6*uv.y,-.6*uv.x+.8*uv.y);
      col*=1+uncovered*quietCenter*(lunarImpact(rotated/22+.31)*.72
           +cluster*lunarImpact(uv/6.4+warp*.22)*.65);
      // Height and real surface normals carry the relief across district boundaries.
      float elevation=max(i.p.y,0);
      float exposed=saturate(dot(n.xz,float2(-.83,.55))*8+.14);
      float patchiness=noise(i.p.xz*.105+warp)*.66+noise(i.p.xz*.27)*.34;
      float shifted=elevation+.48*(patchiness-.5);
      float shoulder=smoothstep(.22,.72,shifted)*(1-smoothstep(.92,1.45,shifted));
      float dust=smoothstep(.01,.26,elevation)*(1-smoothstep(.28,.80,shifted));
      float worn=shoulder*exposed*smoothstep(.27,.64,patchiness);
      col*=1+uncovered*(.08*dust*(1-exposed)-.23*worn+.07*smoothstep(.65,1.50,elevation));
      col*=1+1.55*dot(n.xz,float2(.60,-.80));

     }
    }else if(i.role.x<.5 && i.role.x> -1.5){
     // Dry fine-grained asphalt; no glossy false puddles or noisy painted carpet.
     col*=.96+.08*noise(uv*8);
     if(NOVA_CITY){
      // Sparse resurfaced strips with a dark tar joint around each repair.
      // Intersecting road rectangles share a plane, so their repair masks must agree.
      #if defined(SPACEWARS_CITY_WINDOWS)
       float border=finishMap.g*4-2;
       float tone=finishMap.b;
      #else
       float2 repairData=RoadRepair(uv);
       float border=repairData.x,tone=repairData.y;
      #endif
      float repair=smoothstep(-.06,.06,border);
      col*=1+repair*tone-.23*repair*CityStroke(border,.07);
     }
    }else if(i.role.x<1.5){
     // Constructive joints follow the local yard/support frame; albedo stays world-continuous.
     float2 cell=frac(i.uv/5);float seam=min(min(cell.x,1-cell.x),min(cell.y,1-cell.y))*5;
     if(i.role.x< -1.5){
      // Keep concrete grain inside the tonal range even under bright lunar lighting.
      float grain=noise(uv*2.6);
      col=base*(.66+.6*tex+.16*grain);
     }
     col*=1-.22*CityStroke(seam,.025);
    }else if(i.role.x<2.5){
     float seam=min(frac(uv.x/3),1-frac(uv.x/3))*3;col*=1-.25*CityStroke(seam,.035);
     if(NOVA_CITY && n.y>.65){
      float2 ribUV=float2(.866*uv.x-.5*uv.y,.5*uv.x+.866*uv.y);
      float sheet=frac(ribUV.x/1.35);
      float seam=min(sheet,1-sheet)*1.35;
      float batten=CityStroke(seam,.045);
      float glint=CityStroke((sheet-.085)*1.35,.035);
      col*=.95+.15*hash(float2(floor(ribUV.x/1.35),0))-.33*batten+.27*glint;
     }
    }else if(i.role.x<3.5){
     // Facade UV is metres along each wall, independent of its world heading.
     float2 bay=float2(i.uv.x/_Finish.w,i.uv.y/2.6);float2 f=frac(bay);float theme=i.role.y;
     float window=step(.14,f.x)*step(f.x,.84)*step(.24,f.y)*step(f.y,.66)*step(.8,i.uv.y);
     float industrial=step(1.5,theme)*step(theme,3.5);
     window*=lerp(1,step(.5,frac(floor(bay.x)*.5)),industrial);
     float lit;
     if(NOVA_CITY){
      // The wall plane and local bay stay constant across a pane at every zoom.
      float wall=round(dot(i.p.xz,n.xz)*2);
      float2 room=floor(bay)+float2(wall,round(n.x*13+n.z*23));
      float order=frac(52.9829189*frac(dot(room,float2(.06711056,.00583715))));
      float2 samples=SAMPLE_TEXTURE2D(_CityWindowActivity,sampler_CityWindowActivity,i.p.xz/(_FogBounds.xy*2)+.5).rg;
      float activity=lerp(samples.x,samples.y,_CityWindowBlend);
      lit=smoothstep(.01+order*.82,.15+order*.82,activity);
     }else{lit=step(.3,hash(floor(bay)+floor(i.p.xz/25)));}
     float3 warm=SRGBToLinear(float3(.95,.60,.24)),cool=SRGBToLinear(float3(.22,.64,.75));
     float3 glass=lerp(cool,warm,step(.48,hash(floor(bay)+theme)));
     col=lerp(col,float3(.025,.046,.065),window);emission+=window*lit*glass*_CityLights.x;
     float mullion=CityStroke((f.x-.5)*_Finish.w,.045);emission*=1-window*mullion;col*=1-.25*CityStroke(f.x*_Finish.w,.045);
     float belt=CityStroke(i.uv.y-3.0,.10);col=lerp(col,base*.42,belt);
     if(NOVA_CITY){
      float concrete=1-smoothstep(.62,.76,i.p.y);
      float joint=CityStroke(min(f.x,1-f.x)*_Finish.w,.055);
      col=lerp(col,SRGBToLinear(float3(.61,.59,.54))*(.85+.26*tex)*(1-.30*joint),concrete);
      // One framed service bay per broad hall frontage.
      if(theme>6.5 && i.uv.y>.24 && i.uv.y<2.8){
       float door=abs(i.uv.x-3.1);
       if(door<1.35){col=SRGBToLinear(float3(.29,.32,.34))*(.76+.24*frac(i.uv.y*3));emission=0;}
       if((door>=1.35 && door<1.64)||(i.uv.y>2.49 && door<1.64)){
        col=SRGBToLinear(float3(.72,.50,.30));emission=0;
       }
      }
     }
    }else if(i.role.x>4.5&&i.role.x<5.5){emission=base*_CityLights.y;}
    else if(i.role.x>5.5&&i.role.x<6.5){
     // Amber transit display with a vehicle glyph and schedule bars, not random text.
     float2 q=i.uv;float box=step(.16,q.x)*step(q.x,.62)*step(.32,q.y)*step(q.y,.68);
     float inset=step(.21,q.x)*step(q.x,.57)*step(.42,q.y)*step(q.y,.61);
     float bars=step(.7,q.x)*step(q.x,.9)*(CityStroke(q.y-.39,.025)+CityStroke(q.y-.53,.025)+CityStroke(q.y-.67,.025));
     float glyph=saturate(box-inset+bars);col=base*.07;emission=base*glyph*_CityLights.y;
    }else if(i.role.x>8.5){
     // UV runs in metres along the kerb; transverse UV spans one recessed channel.
     float along=i.uv.x, across=i.uv.y;
     float rim=CityStroke(min(across,1-across),.07);
     float plate=frac(along/2.6);
     float joint=CityStroke(min(plate,1-plate)*2.6,.035);
     col=base*(.79+.22*tex)*(1-.42*joint);
     if(i.role.x<9.5){
      // Paired conduits can be seen through occasional recessed access panels.
      float access=step(4.5,fmod(floor(along/2.6),6))*step(.12,plate)*step(plate,.88);
      float bed=access*step(.16,across)*step(across,.84);
      float cables=CityStroke(across-.38,.07)+CityStroke(across-.62,.07);
      col=lerp(col,base*(.23+.73*cables),bed);
      col*=1-.24*CityStroke(across-.5,.035)*(1-bed);
     }else{
      float grate=step(.5,fmod(floor(along/2.6),5))*step(fmod(floor(along/2.6),5),1.5);
      grate*=step(.18,plate)*step(plate,.82)*step(.12,across)*step(across,.88);
      float slat=CityStroke((frac(along/.30)-.5)*.30,.066);
      col=lerp(col,base*(.17+1.12*slat),grate);
     }
     col=lerp(col,base*1.22,rim);
    }else if(i.role.x>7.5 && NOVA_CITY){
     float panel=frac(i.uv.x/1.15);
     float frame=CityStroke(min(panel,1-panel)*1.15,.055);
     col=lerp(base*.61,base*.20,frame);
     emission=base*.30*(1-frame)*_CityLights.x;
    }else if(i.role.x>6.5){
     float grate=CityStroke((frac(i.uv.x*3)-.5)/3,.07);col*=.35+.65*grate;
    }
    Light sun=GetMainLight(TransformWorldToShadowCoord(i.p));
    #if defined(SPACEWARS_CITY_WINDOWS)
     float3 litCol=col*(i.lighting.rgb+sun.color*i.lighting.a*sun.shadowAttenuation)+emission;
    #else
     float3 litCol=col*(SampleSH(n)+sun.color*saturate(dot(n,sun.direction))*sun.shadowAttenuation)+emission;
    #endif

    return half4(lerp(litCol,_FogColor.rgb,fog),1);
   }
   ENDHLSL
  }
  // Built once at world creation. The signed seam distance retains sharp tar
  // joints even though the slowly varying field has fewer texels than the screen.
  Pass {
   Name "CityFinishMap"
   Tags {"LightMode"="CityFinishMap"}
   ZTest Always ZWrite Off Cull Off
   HLSLPROGRAM
   #pragma vertex vert_img
   #pragma fragment BuildFinishMap
   #include "UnityCG.cginc"
   float4 _FogBounds,_Finish;
   half4 BuildFinishMap(v2f_img i):SV_Target{
    float2 p=(i.uv-.5)*2*_FogBounds.xy;
    float2 repair=RoadRepair(p);
    return half4(FinishPatch(p,_Finish.z),saturate(repair.x*.25+.5),repair.y,1);
   }
   ENDHLSL
  }
  UsePass "Universal Render Pipeline/Lit/ShadowCaster"
  UsePass "Universal Render Pipeline/Lit/DepthOnly"
 }
}
