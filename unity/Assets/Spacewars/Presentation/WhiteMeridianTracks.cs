using System;
using System.Collections.Generic;
using Spacewars.Simulation;
using UnityEngine;
namespace Spacewars.Presentation
{
    public sealed partial class PlayableWorld
    {
        private Texture2D meridianRoadMask;
        private static Texture2D MeridianRoadMask(WhiteMeridianMap map,WhiteMeridianArtProfile art)
        {
            // Authored service tracks: rendering only. No route request or gameplay replay.
            var routes=new List<Vector2[]>();var compactPatches=new List<Vector2>();
            foreach(int sx in new[]{-1,1}){
                // Continuous allied shelf and actual straight secondary descents.
                routes.Add(new[]{new Vector2(sx*62,50),new Vector2(sx*74,50),new Vector2(sx*74,0),new Vector2(sx*74,-50),new Vector2(sx*62,-50)});
                foreach(int sz in new[]{-1,1}){
                    routes.Add(new[]{new Vector2(sx*62,sz*50),new Vector2(sx*58,sz*40),new Vector2(sx*58,sz*32),new Vector2(sx*58,sz*24),new Vector2(sx*58,sz*20),new Vector2(sx*40,sz*20)});
                    routes.Add(new[]{new Vector2(sx*62,sz*50),new Vector2(sx*48,sz*50),new Vector2(sx*40,sz*50),new Vector2(sx*32,sz*50),new Vector2(sx*20,sz*55),new Vector2(0,sz*58)});
                }
            }
            // Rotationally paired approaches skirt shoulders and cross the two actual gaps.
            // Several offset passes make a used route rather than one immaculate line.
            foreach(int side in new[]{-1,1})foreach(float offset in new[]{-.8f,0f,.8f}){
                var route=new[]{new Vector2(-40,20),new Vector2(-30,25),new Vector2(-15,26),new Vector2(-12,18),new Vector2(-12,12),new Vector2(-15,4),new Vector2(-15,-7),new Vector2(-19,-17),new Vector2(-28,-22),new Vector2(-40,-20)};
                for(int i=0;i<route.Length;i++)route[i]=new Vector2((route[i].x+offset)*side,route[i].y*side);
                routes.Add(route);compactPatches.Add(new Vector2(-15*side,0));
            }
            // Basin traffic joins the northern/southern depots to both passage routes.
            // Cross-country diagonals connect the loops instead of leaving four isolated roads.
            foreach(int sz in new[]{-1,1})foreach(int sx in new[]{-1,1}){
                routes.Add(new[]{new Vector2(0,sz*58),new Vector2(sx*5,sz*48),new Vector2(sx*10,sz*38),new Vector2(sx*15,sz*29),new Vector2(sx*28,sz*25),new Vector2(sx*40,sz*20)});
            }
            foreach(int sign in new[]{-1,1})foreach(float offset in new[]{-.45f,.45f}){
                var path=new[]{new Vector2(-40,20),new Vector2(-30,25),new Vector2(-15,26),new Vector2(-12,18),new Vector2(-12,12),new Vector2(-15,4),new Vector2(-15,-7),new Vector2(-9,-14),new Vector2(5,-18),new Vector2(22,-22),new Vector2(40,-20)};
                for(int i=0;i<path.Length;i++)path[i]=new Vector2((path[i].x+offset)*sign,path[i].y*sign);routes.Add(path);
            }
            const int size=2048; // Visual-mask sampling resolution, not a simulation raster.
            var colors=new Color32[size*size];float extent=(float)map.HalfExtent,pixel=extent*2/size,half=art.roadWidth*.5f;
            float Fade(float lo,float hi,float value)=>Mathf.SmoothStep(0,1,Mathf.InverseLerp(lo,hi,value));
            int written=0,clipped=0;
            foreach(var route in routes){float travelled=0;
                Vector2 Sample(int i,float t){var a=route[i-1]*(float)map.Scale;var b=route[i]*(float)map.Scale;var before=route[Mathf.Max(0,i-2)]*(float)map.Scale;var after=route[Mathf.Min(route.Length-1,i+1)]*(float)map.Scale;return .5f*((2*a)+(-before+b)*t+(2*before-5*a+4*b-after)*t*t+(-before+3*a-3*b+after)*t*t*t);}
                bool supported=true;
                for(int i=1;i<route.Length&&supported;i++){
                    int samples=Mathf.CeilToInt(Vector2.Distance(route[i-1],route[i])*(float)map.Scale/.5f);
                    for(int j=0;j<samples;j++){var a=Sample(i,(float)j/samples);var b=Sample(i,(float)(j+1)/samples);if(map.SupportsSweep(new NavPoint(a.x,a.y),new NavPoint(b.x,b.y),half))continue;
                        Debug.LogWarning("MERIDIAN_TRACK_REJECT entire route "+route[0]+" -> "+route[route.Length-1]+" at "+a+" -> "+b);supported=false;break;}
                }
                if(!supported){clipped++;continue;}

                for(int i=1;i<route.Length;i++){
                    var a=route[i-1]*(float)map.Scale;var b=route[i]*(float)map.Scale;var tangent=(b-a).normalized;var normal=new Vector2(-tangent.y,tangent.x);float length=(b-a).magnitude;
                    int steps=Mathf.CeilToInt(length/.5f);
                    Vector2 Point(float t)=>Sample(i,t);
                    for(int step=0;step<steps;step++){
                        var start=Point((float)step/steps);var end=Point((float)(step+1)/steps);
                        // Do not draw a corridor over a cliff, forbidden seam or solid.
                        if(!map.SupportsSweep(new NavPoint(start.x,start.y),new NavPoint(end.x,end.y),half)){clipped++;continue;}
                        written++;var delta=end-start;float len=delta.magnitude;var side=new Vector2(-delta.y,delta.x)/len;
                        int x0=Mathf.Clamp(Mathf.FloorToInt((Mathf.Min(start.x,end.x)-half+extent)/pixel),0,size-1),x1=Mathf.Clamp(Mathf.CeilToInt((Mathf.Max(start.x,end.x)+half+extent)/pixel),0,size-1);
                        int z0=Mathf.Clamp(Mathf.FloorToInt((Mathf.Min(start.y,end.y)-half+extent)/pixel),0,size-1),z1=Mathf.Clamp(Mathf.CeilToInt((Mathf.Max(start.y,end.y)+half+extent)/pixel),0,size-1);
                        for(int z=z0;z<=z1;z++)for(int x=x0;x<=x1;x++){
                            var q=new Vector2((x+.5f)*pixel-extent,(z+.5f)*pixel-extent);float along=Vector2.Dot(q-start,delta)/delta.sqrMagnitude;if(along<0||along>1)continue;
                            float cross=Vector2.Dot(q-start,side);float bed=1-Fade(.35f*half,half,Mathf.Abs(cross));
                            float tire=1-Fade(.045f,.18f,Mathf.Abs(Mathf.Abs(cross)-art.wheelSeparation*.5f));
                            float distance=travelled+(step+along)*length/steps;float tread=.55f+.45f*Mathf.Pow(Mathf.Max(0,Mathf.Cos(distance*Mathf.PI*4)),3);
                            int k=z*size+x;colors[k]=new Color32((byte)Mathf.Max(colors[k].r,Mathf.RoundToInt(bed*255)),(byte)Mathf.Max(colors[k].g,Mathf.RoundToInt(tire*tread*255)),colors[k].b,colors[k].a);
                        }
                    }
                    travelled+=length;
                }
            }
            // Broad, weather-softened soil pads collect where vehicles repeatedly turn or cross.
            float patchRadius=5.2f;
            foreach(var authored in compactPatches){
                var center=authored*(float)map.Scale;float radius=patchRadius*(float)map.Scale;
                int x0=Mathf.Clamp(Mathf.FloorToInt((center.x-radius+extent)/pixel),0,size-1),x1=Mathf.Clamp(Mathf.CeilToInt((center.x+radius+extent)/pixel),0,size-1);
                int z0=Mathf.Clamp(Mathf.FloorToInt((center.y-radius+extent)/pixel),0,size-1),z1=Mathf.Clamp(Mathf.CeilToInt((center.y+radius+extent)/pixel),0,size-1);
                for(int z=z0;z<=z1;z++)for(int x=x0;x<=x1;x++){
                    var q=new Vector2((x+.5f)*pixel-extent,(z+.5f)*pixel-extent);float d=Vector2.Distance(q,center);
                    float edge=d+Mathf.Sin(q.x*.31f+q.y*.17f)*.48f+Mathf.Sin(q.y*.23f-q.x*.11f)*.35f;
                    float soil=1-Fade(radius*.5f,radius,edge);if(soil<=0)continue;
                    int k=z*size+x;colors[k].b=(byte)Mathf.Max(colors[k].b,Mathf.RoundToInt(soil*190));
                }
            }
            // Shallow visual depressions: alpha encodes depth only; physical ground is flat.
            foreach(int side in new[]{-1,1})for(int i=0;i<18;i++){
                var center=new Vector2(side*(14.3f+Mathf.Sin(i*7.3f)*2),side*(-6+i*.7f));float radius=.3f+.22f*(.5f+.5f*Mathf.Sin(i*3.7f));
                center*=(float)map.Scale;radius*=(float)map.Scale;
                if(!map.SupportsFootprint(new NavPoint(center.x,center.y),radius+.3f))continue;
                int cx=Mathf.RoundToInt((center.x+extent)/pixel),cz=Mathf.RoundToInt((center.y+extent)/pixel),reach=Mathf.CeilToInt(radius/pixel);
                for(int z=Mathf.Max(0,cz-reach);z<=Mathf.Min(size-1,cz+reach);z++)for(int x=Mathf.Max(0,cx-reach);x<=Mathf.Min(size-1,cx+reach);x++){
                    float d=Vector2.Distance(new Vector2((x+.5f)*pixel-extent,(z+.5f)*pixel-extent),center)/radius;
                    float depth=1-Mathf.SmoothStep(0,1,d);int k=z*size+x;colors[k].a=(byte)Mathf.Max(colors[k].a,Mathf.RoundToInt(depth*255));
                }
            }
            Debug.Log("MERIDIAN_TRACK_MASK supported segments="+written+" clipped="+clipped+"; no simulation mutation");
            var texture=new Texture2D(size,size,TextureFormat.RGBA32,false,true){name="Meridian buried service tracks",wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Bilinear,anisoLevel=4};texture.SetPixels32(colors);texture.Apply(false,true);return texture;
        }
    }
}
