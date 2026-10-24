using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
using UnityEngine;
namespace Spacewars.Presentation
{
    public sealed partial class PlayableWorld
    {
        private void CreateMeridianDecor(WhiteMeridianMap map,WhiteMeridianArtProfile art)
        {
            // Authored prop silhouettes and locations, not simulation objects. Each
            // vertex is checked against its solid or route-free rock-foot apron.
            void Place(MeridianPart part,string name,NavObstacle solid,int kind,Color color,
                Vector3 origin,Vector3 tangent,Vector3 inward,float scale=1,Func<Vector3,float> ground=null)
            {
                var mesh=part.Mesh(name);var vertices=mesh.vertices;
                for(int i=0;i<vertices.Length;i++){
                    var v=vertices[i];vertices[i]=origin+(tangent*v.x+Vector3.up*v.y+inward*v.z)*scale;
                    if(ground!=null)vertices[i].y+=ground(vertices[i]);
                    var point=new NavPoint(vertices[i].x,vertices[i].z);
                    if(name.StartsWith("expedition ")){
                        // Selected concept places small equipment on the sand at rock feet.
                        // Keep it within the narrow dressing apron, away from tire routes
                        // and sites; it has no colliders or simulation identity.
                        if(solid.Footprint.DistanceSquared(point)>4||map.Sites(profile).Any(site=>Math.Pow(site.Position.X-point.X,2)+Math.Pow(site.Position.Z-point.Z,2)<64))
                            throw new InvalidOperationException("Expedition prop leaves rock apron or enters a track/site: "+name);
                    }else if(solid.Footprint.DistanceSquared(point)>0.000001)
                        throw new InvalidOperationException("Decoration outside existing solid: "+name);
                }
                mesh.vertices=vertices;mesh.RecalculateNormals();mesh.RecalculateBounds();
                Emit("Meridian decor "+name,mesh,kind,color);
            }
            // Low, irregular chips remain on the outer lip of existing rock solids.
            // The density control changes decoration only; deterministic samples do not
            // touch gameplay RNG or map data.
            int ridgeIndex=0;
            foreach(var solid in map.Ridges.Concat(map.BorderRocks.Take(4))){
                var poly=solid.Footprint;var center=Vector3.zero;
                foreach(var q in poly.Vertices)center+=new Vector3((float)q.X,0,(float)q.Z);center/=poly.Vertices.Count;
                var chips=new MeridianPart();var salt=new MeridianPart();int emitted=0;
                for(int edge=0;edge<poly.Vertices.Count;edge++){
                    var a=poly.Vertices[edge];var b=poly.Vertices[(edge+1)%poly.Vertices.Count];
                    int count=Mathf.CeilToInt((float)Math.Sqrt((b.X-a.X)*(b.X-a.X)+(b.Z-a.Z)*(b.Z-a.Z))*art.decorDensity*2);
                    for(int j=0;j<count;j++){
                        float seed=ridgeIndex*53+edge*19+j*7.31f;
                        float h=.5f+.5f*Mathf.Sin(seed*3.71f);if(h<.28f)continue;
                        float t=(j+.2f+.5f*h)/count;
                        var q=Vector3.Lerp(new Vector3((float)a.X,0,(float)a.Z),new Vector3((float)b.X,0,(float)b.Z),t);
                        q=Vector3.Lerp(q,center,.018f+.014f*h);
                        var point=new NavPoint(q.x,q.z);if(!poly.Contains(point))continue;
                        float safe=(float)Math.Sqrt(poly.BoundaryDistanceSquared(point))*.65f;
                        float radius=Mathf.Min(safe,(.07f+.20f*h*h)*art.decorScale);if(radius<.035f)continue;
                        q.y=(float)map.SurfaceHeight(point)+.012f;
                        chips.Rock(q,radius,seed);emitted++;
                        if(h>.65f){var v=q+new Vector3(radius*.2f,.012f,0);salt.Rock(v,Mathf.Min(radius*.36f,safe*.3f),seed+7);}
                    }
                }
                if(emitted>0){Place(chips,"gypsum chips "+ridgeIndex,solid,1,Color.white,Vector3.zero,Vector3.right,Vector3.forward);Place(salt,"salt fragments "+ridgeIndex,solid,2,new Color(.78f,.75f,.65f),Vector3.zero,Vector3.right,Vector3.forward);}
                ridgeIndex++;
            }
            // Three different expedition vignettes on a narrow rock-foot apron.
            // Site clearance is checked here; the Editor audit checks rendered tracks.
            for(int scene=0;scene<3;scene++){
                var solid=scene==0?map.Ridges[4]:scene==1?map.Ridges[2]:map.BorderRocks[1];
                var poly=solid.Footprint;var center=Vector3.zero;foreach(var q in poly.Vertices)center+=new Vector3((float)q.X,0,(float)q.Z);center/=poly.Vertices.Count;
                int selected=0;float best=float.NegativeInfinity;
                for(int edge=0;edge<poly.Vertices.Count;edge++){
                    var a=poly.Vertices[edge];var b=poly.Vertices[(edge+1)%poly.Vertices.Count];
                    float length=(float)Math.Sqrt((b.X-a.X)*(b.X-a.X)+(b.Z-a.Z)*(b.Z-a.Z));
                    // Basin-facing long edge, away from the construction plateaus.
                    float score=length*.2f-(float)Math.Sqrt(Math.Pow((a.X+b.X)*.5,2)+Math.Pow((a.Z+b.Z)*.5,2));
                    if(score>best){best=score;selected=edge;}
                }
                var pa=poly.Vertices[selected];var pb=poly.Vertices[(selected+1)%poly.Vertices.Count];
                var origin=new Vector3((float)((pa.X+pb.X)*.5),0,(float)((pa.Z+pb.Z)*.5));
                var tangent=new Vector3((float)(pb.X-pa.X),0,(float)(pb.Z-pa.Z)).normalized;
                var inward=Vector3.Cross(tangent,Vector3.up);if(Vector3.Dot(inward,center-origin)<0)inward=-inward;
                origin-=inward*1.0f*art.decorScale;origin.y=(float)map.SurfaceHeight(new NavPoint(origin.x,origin.z))+.03f;
                if(scene<2){
                    var nearest=map.Sites(profile).OrderBy(site=>Math.Pow(site.Position.X-origin.x,2)+Math.Pow(site.Position.Z-origin.z,2)).First();
                    float toward=Vector3.Dot(new Vector3((float)nearest.Position.X-origin.x,0,(float)nearest.Position.Z-origin.z),tangent);
                    origin-=tangent*(Mathf.Sign(toward)*.9f);
                }
                var paint=new MeridianPart();var metal=new MeridianPart();var dark=new MeridianPart();
                // Ribbed sealed case, partly buried, with inset lid and two latches.
                paint.Box(new Vector3(-.45f,.19f,0),new Vector3(.95f,.38f,.36f));
                metal.Box(new Vector3(-.45f,.39f,0),new Vector3(.82f,.045f,.29f));
                for(int k=0;k<5;k++)metal.Box(new Vector3(-.82f+k*.18f,.19f,-.19f),new Vector3(.035f,.31f,.028f));
                foreach(float x in new[]{-.77f,-.14f})dark.Box(new Vector3(x,.29f,-.21f),new Vector3(.09f,.09f,.035f));
                if(scene==0){
                    // Damaged survey head on a bent mast and surviving low tripod.
                    var foot=new Vector3(.65f,.02f,0);var elbow=new Vector3(.61f,.64f,.02f);var head=new Vector3(.92f,1.12f,.04f);
                    metal.Tube(foot,elbow,.045f,.035f,8);metal.Tube(elbow,head,.035f,.03f,8);
                    metal.Tube(foot+new Vector3(-.25f,0,-.15f),elbow*.35f+foot*.65f,.025f,.02f,6);
                    paint.Box(head,new Vector3(.36f,.20f,.25f));dark.Box(head+new Vector3(0,0,-.135f),new Vector3(.22f,.10f,.025f));
                }else if(scene==1){
                    // Open maintenance crate with a removed narrow access panel.
                    paint.Box(new Vector3(.45f,.11f,0),new Vector3(.55f,.22f,.33f));
                    dark.Box(new Vector3(.45f,.225f,0),new Vector3(.43f,.018f,.24f));
                    paint.Plate(new Vector3(.73f,.02f,-.14f),new Vector3(1.18f,.07f,-.13f),new Vector3(1.10f,.10f,.14f),new Vector3(.76f,.03f,.14f),.035f);
                    for(int k=0;k<3;k++)metal.Tube(new Vector3(.29f+k*.13f,.24f,-.08f),new Vector3(.29f+k*.13f,.24f,.08f),.025f,.025f,6);
                }else{
                    // Collapsed cable frame, short slack loop; no glowing objective cue.
                    metal.Tube(new Vector3(.25f,.02f,-.12f),new Vector3(1.12f,.48f,.10f),.04f,.03f,8);
                    metal.Tube(new Vector3(.26f,.02f,.12f),new Vector3(1.12f,.48f,.10f),.035f,.025f,8);
                    paint.Box(new Vector3(1.1f,.47f,.10f),new Vector3(.19f,.24f,.12f));
                }
                for(int k=0;k<20;k++){
                    float t=k/20f,u=(k+1)/20f;
                    Vector3 Cable(float v)=>new Vector3(-1.1f+2.3f*v,.028f+.015f*Mathf.Sin(v*9),-.20f+.045f*Mathf.Sin(v*11));
                    dark.Tube(Cable(t),Cable(u),.018f,.018f,5);
                }
                Place(paint,"expedition "+scene+" casing",solid,2,new Color(.46f,.42f,.32f),origin,tangent,inward,art.decorScale);
                Place(metal,"expedition "+scene+" ribs",solid,2,new Color(.32f,.30f,.24f),origin,tangent,inward,art.decorScale);
                Place(dark,"expedition "+scene+" inserts",solid,2,new Color(.14f,.13f,.10f),origin,tangent,inward,art.decorScale);
            }
            // Sparse crash debris is confined to the already blocked end pockets.
            for(int section=0;section<3;section+=2){
                var solid=map.Wreck[section];float x=section==0?(float)solid.MinX+2:(float)solid.MaxX-2;
                var poly=solid.Footprint;float lo=float.MaxValue,hi=float.MinValue;
                for(int k=0;k<poly.Vertices.Count;k++){var a=poly.Vertices[k];var b=poly.Vertices[(k+1)%poly.Vertices.Count];if(Math.Abs(a.X-b.X)<1e-8||x<Math.Min(a.X,b.X)||x>Math.Max(a.X,b.X))continue;float z=(float)(a.Z+(b.Z-a.Z)*(x-a.X)/(b.X-a.X));lo=Mathf.Min(lo,z);hi=Mathf.Max(hi,z);}
                var origin=new Vector3(x,0,(lo+hi)*.5f);var armor=new MeridianPart();var ribs=new MeridianPart();
                armor.Plate(new Vector3(-.65f,.01f,-.35f),new Vector3(.52f,.06f,-.28f),new Vector3(.35f,.15f,.33f),new Vector3(-.48f,.09f,.24f),.07f);
                armor.Plate(new Vector3(-.35f,.01f,.48f),new Vector3(.45f,.04f,.52f),new Vector3(.21f,.08f,.83f),new Vector3(-.40f,.03f,.77f),.05f);
                for(int k=0;k<4;k++)ribs.Box(new Vector3(-.45f+k*.25f,.12f,0),new Vector3(.05f,.08f,.46f));
                ribs.Tube(new Vector3(-.70f,.08f,-.43f),new Vector3(.65f,.13f,-.38f),.045f,.025f,7);
                float SandHeight(Vector3 q){
                    float lower=float.MaxValue,upper=float.MinValue;
                    for(int k=0;k<poly.Vertices.Count;k++){
                        var a=poly.Vertices[k];var b=poly.Vertices[(k+1)%poly.Vertices.Count];
                        if(Math.Abs(a.X-b.X)<1e-8||q.x<Math.Min(a.X,b.X)||q.x>Math.Max(a.X,b.X))continue;
                        float slope=(float)((b.Z-a.Z)/(b.X-a.X)),z=(float)(a.Z+(q.x-a.X)*slope),margin=.23f*Mathf.Sqrt(1+slope*slope);
                        lower=Mathf.Min(lower,z+margin);upper=Mathf.Max(upper,z-margin);
                    }
                    float start=section==0?(float)solid.MinX+.35f:(float)solid.MaxX-.35f-art.wreckEndPocket;
                    float u=Mathf.Clamp01((q.x-start)/art.wreckEndPocket),t=Mathf.InverseLerp(lower,upper,q.z);
                    return -.025f+1.15f*Mathf.Pow(Mathf.Sin(u*Mathf.PI),2)*Mathf.Pow(Mathf.Sin(t*Mathf.PI),2);
                }
                Place(armor,"wreck fragments "+section,solid,4,new Color(.60f,.54f,.43f),origin,Vector3.right,Vector3.forward,art.decorScale,SandHeight);
                Place(ribs,"wreck fragment ribs "+section,solid,2,new Color(.28f,.25f,.19f),origin,Vector3.right,Vector3.forward,art.decorScale,SandHeight);
            }
        }
    }
}
