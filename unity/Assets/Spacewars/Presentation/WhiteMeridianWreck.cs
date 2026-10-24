using System;
using System.Collections.Generic;
using Spacewars.Simulation;
using UnityEngine;

namespace Spacewars.Presentation
{
    public sealed partial class PlayableWorld
    {
        // Authored structural stations and panel shapes, not simulation dimensions.
        // Every component is contained by the corresponding authoritative blocker.
        private sealed class MeridianPart
        {
            private readonly List<Vector3> vertices=new List<Vector3>();
            private readonly List<int> indices=new List<int>();
            private readonly List<Vector2> uvs=new List<Vector2>();
            public void Tri(Vector3 a,Vector3 b,Vector3 c){int i=vertices.Count;vertices.Add(a);vertices.Add(b);vertices.Add(c);uvs.Add(Vector2.zero);uvs.Add(Vector2.right);uvs.Add(Vector2.one);indices.Add(i);indices.Add(i+1);indices.Add(i+2);}
            public void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d){int i=vertices.Count;vertices.Add(a);vertices.Add(b);vertices.Add(c);vertices.Add(d);uvs.Add(Vector2.zero);uvs.Add(Vector2.right);uvs.Add(Vector2.one);uvs.Add(Vector2.up);indices.Add(i);indices.Add(i+1);indices.Add(i+2);indices.Add(i);indices.Add(i+2);indices.Add(i+3);}
            public void OutQuad(Vector3 a,Vector3 b,Vector3 c,Vector3 d,Vector3 center){
                if(Vector3.Dot(Vector3.Cross(b-a,c-a),(a+b+c+d)*.25f-center)<0)Quad(d,c,b,a);else Quad(a,b,c,d);
            }
            public void UpTri(Vector3 a,Vector3 b,Vector3 c){if(Vector3.Cross(b-a,c-a).y<0)Tri(a,c,b);else Tri(a,b,c);}
            public void UpQuad(Vector3 a,Vector3 b,Vector3 c,Vector3 d){if(Vector3.Cross(b-a,c-a).y<0)Quad(d,c,b,a);else Quad(a,b,c,d);}
            public void Tube(Vector3 a,Vector3 b,float radius,float endRadius,int sides=10){
                var axis=(b-a).normalized;var u=Vector3.Cross(axis,Mathf.Abs(axis.y)>.9f?Vector3.right:Vector3.up).normalized;var v=Vector3.Cross(axis,u);
                for(int i=0;i<sides;i++){float t=i*Mathf.PI*2/sides,tt=(i+1)*Mathf.PI*2/sides;var p=u*Mathf.Cos(t)+v*Mathf.Sin(t);var q=u*Mathf.Cos(tt)+v*Mathf.Sin(tt);Quad(a+q*radius,b+q*endRadius,b+p*endRadius,a+p*radius);}
            }
            // Closed, bevelled metal plates: highlight edges, shaded thickness and real torn ends.
            public void Plate(Vector3 a,Vector3 b,Vector3 c,Vector3 d,float thickness=.12f){
                var n=Vector3.Cross(b-a,c-a).normalized;if(n.y<0)n=-n;
                var center=(a+b+c+d)*.25f;var outer=new[]{a,b,c,d};var inner=new Vector3[4];
                for(int k=0;k<4;k++)inner[k]=Vector3.Lerp(outer[k],center,.055f)+n*thickness*.28f;
                UpQuad(inner[0],inner[1],inner[2],inner[3]);
                for(int k=0;k<4;k++){int j=(k+1)%4;Quad(outer[k],outer[j],inner[j],inner[k]);Quad(outer[j]-n*thickness,outer[k]-n*thickness,outer[k],outer[j]);}
                Quad(d-n*thickness,c-n*thickness,b-n*thickness,a-n*thickness);
            }
            public void Box(Vector3 c,Vector3 size){var x=Vector3.right*size.x*.5f;var y=Vector3.up*size.y*.5f;var z=Vector3.forward*size.z*.5f;
                Plate(c-x+y-z,c+x+y-z,c+x+y+z,c-x+y+z,.08f);
                OutQuad(c-x-y-z,c+x-y-z,c+x+y-z,c-x+y-z,c);OutQuad(c+x-y+z,c-x-y+z,c-x+y+z,c+x+y+z,c);
                OutQuad(c-x-y+z,c-x-y-z,c-x+y-z,c-x+y+z,c);OutQuad(c+x-y-z,c+x-y+z,c+x+y+z,c+x+y-z,c);
            }
            public void Rock(Vector3 c,float r,float seed){
                var low=new Vector3[7];var high=new Vector3[7];
                for(int k=0;k<7;k++){float a=k*Mathf.PI*2/7,rr=r*(.82f+.18f*Mathf.Sin(seed+k*9));low[k]=c+new Vector3(Mathf.Cos(a)*rr,-.04f,Mathf.Sin(a)*rr*.75f);high[k]=c+new Vector3(Mathf.Cos(a)*rr*.56f,r*(.55f+.17f*Mathf.Sin(seed+k*3)),Mathf.Sin(a)*rr*.58f);}
                var top=c+new Vector3(r*.1f,r*.76f,-r*.12f);
                for(int k=0;k<7;k++){int j=(k+1)%7;OutQuad(low[k],low[j],high[j],high[k],c);UpTri(high[k],high[j],top);}
            }
            public Mesh Mesh(string name,bool smooth=false){var m=new Mesh{name=name};m.SetVertices(vertices);m.SetUVs(0,uvs);m.SetTriangles(indices,0);m.RecalculateNormals();
                if(smooth){var normals=m.normals;var sums=new Dictionary<Vector3,Vector3>();for(int i=0;i<vertices.Count;i++){var v=vertices[i];sums.TryGetValue(v,out var sum);sums[v]=sum+normals[i];}for(int i=0;i<vertices.Count;i++)normals[i]=vertices[i].y<=.003f?Vector3.up:sums[vertices[i]].normalized;m.normals=normals;}
                m.RecalculateBounds();return m;}
        }
        private void CreateMeridianCrashShoulder(NavObstacle original,WhiteMeridianMap map,WhiteMeridianArtProfile art)
        {
            int side=original.MinX>0?1:-1;
            double tip=side>0?original.MinX:-original.MaxX,cut=tip+art.cliffPocketRetreat*map.Scale;
            var input=original.Footprint.Vertices;var kept=new List<NavPoint>();var pocket=new List<NavPoint>();
            List<NavPoint> Clip(bool outer){var result=new List<NavPoint>();for(int i=0;i<input.Count;i++){
                var a=input[i];var b=input[(i+1)%input.Count];double da=side*a.X-cut,db=side*b.X-cut;bool ia=outer?da>=0:da<=0,ib=outer?db>=0:db<=0;
                if(ia)result.Add(a);if(ia!=ib){double t=da/(da-db);result.Add(new NavPoint(a.X+(b.X-a.X)*t,a.Z+(b.Z-a.Z)*t));}
            }return result;}
            kept=Clip(true);pocket=Clip(false);
            Emit("Gypsum ridge behind crash pocket",MeridianRock(new NavObstacle(new NavPolygon(kept),0,original.Top),map,art),1,Color.white);
            var sand=new MeridianPart();var chips=new MeridianPart();var center=Vector3.zero;foreach(var q in pocket)center+=new Vector3((float)q.X,0,(float)q.Z);center/=pocket.Count;center.y=.45f;
            Vector3 Edge(NavPoint q)=>new Vector3((float)q.X,.002f,(float)q.Z);
            for(int i=0;i<pocket.Count;i++){
                var a=Edge(pocket[i]);var b=Edge(pocket[(i+1)%pocket.Count]);var aa=Vector3.Lerp(a,center,.45f)+Vector3.up*.20f;var bb=Vector3.Lerp(b,center,.45f)+Vector3.up*.20f;sand.UpQuad(a,b,bb,aa);sand.UpTri(aa,bb,center);
                for(int j=0;j<15;j++){
                    float t=(j+.5f)/15;var q=Vector3.Lerp(Vector3.Lerp(a,b,t),center,.15f+.7f*(.5f+.5f*Mathf.Sin(i*11+j*7)));
                    float r=.12f+.48f*Mathf.Pow(.5f+.5f*Mathf.Sin(i*3+j*5),3);
                    chips.Rock(q,r,i*23+j*3);
                }
            }
            Emit("Impassable crash pocket sand",sand.Mesh("Crash pocket sand",true),0,Color.white);
            Emit("Crash pocket broken gypsum",chips.Mesh("Crash pocket scree"),1,Color.white);
        }
        private void CreateMeridianWreck(NavObstacle shape,int section,WhiteMeridianArtProfile art)
        {
            var glazingCracks=new MeridianPart();var highlights=new MeridianPart();var bolts=new MeridianPart();var wing=new MeridianPart();var armor=new MeridianPart();var broken=new MeridianPart();var frame=new MeridianPart();var machinery=new MeridianPart();var dark=new MeridianPart();var glass=new MeridianPart();var dust=new MeridianPart();var scree=new MeridianPart();
            var polygon=shape.Footprint.Vertices;
            float fullLeft=(float)shape.MinX+.35f,fullRight=(float)shape.MaxX-.35f;
            float left=fullLeft+(section==0?art.wreckEndPocket:0),right=fullRight-(section==2?art.wreckEndPocket:0);
            float height=(float)shape.Top*art.hullHeightRatio*(section==1?.88f:section==0?.85f:1f);
            (float lo,float hi) Span(float x){float lo=float.MaxValue,hi=float.MinValue;for(int i=0;i<polygon.Count;i++){var a=polygon[i];var b=polygon[(i+1)%polygon.Count];if(Math.Abs(a.X-b.X)<1e-8||x<Math.Min(a.X,b.X)||x>Math.Max(a.X,b.X))continue;float z=(float)(a.Z+(b.Z-a.Z)*(x-a.X)/(b.X-a.X));float slope=(float)((b.Z-a.Z)/(b.X-a.X));float margin=.23f*Mathf.Sqrt(1+slope*slope);lo=Mathf.Min(lo,z+margin);hi=Mathf.Max(hi,z-margin);}return(lo,hi);}
            Vector3 P(float u,float angle,float inset=0){float x=Mathf.Lerp(left,right,u);var s=Span(x);float z=(s.lo+s.hi)*.5f,r=(s.hi-s.lo)*.5f;
                // Middle shell leaves room INSIDE its blocker for the fallen wings.
                if(section==1)r*=.55f;
                float crown=section==0?Mathf.Lerp(.25f,1,Mathf.SmoothStep(0,1,u/.32f)):1;
                float dent=section==0?art.cockpitDamage*Mathf.Exp(-Mathf.Pow((u-.22f)*7,2))*(.35f+.65f*Mathf.Sin(angle*3+u*9)):.08f*Mathf.Sin(u*19+angle*3);
                if(section==0){
                    dent+=art.cockpitDamage*.65f*Mathf.Exp(-Mathf.Pow((u-.58f)*8,2)-Mathf.Pow((angle-1.9f)*3,2));
                    float cross=Mathf.Cos(angle),rise=Mathf.Min(1,Mathf.Sin(angle)*1.3f);
                    float width=Mathf.Lerp(.38f,1,Mathf.SmoothStep(0,1,u/.24f));
                    return new Vector3(x,.22f+(height*crown-inset)*rise-dent*rise,z+(r-inset)*cross*width);
                }
                return new Vector3(x,.22f+(height*crown-inset)*Mathf.Sin(angle)-dent*Mathf.Sin(angle),z+(r-inset)*Mathf.Cos(angle));}
            void Shell(MeridianPart part,float a,float b,float lo,float hi,float inset=0){
                var p0=P(a,lo,inset);var p1=P(b,lo,inset);var p2=P(b,hi,inset);var p3=P(a,hi,inset);
                if(part!=glass){float damage=Mathf.Sin(a*71+lo*19);p1.y+=damage*.055f;p2.y-=damage*.085f;if(part==broken){p2+=new Vector3(-.13f,.18f,.06f);p3.y-=.12f;}}
                if(part==glass){
                    if(a<.2f&&lo>1.5f&&lo<1.9f){var q1=Vector3.Lerp(p1,p2,.66f);var q2=Vector3.Lerp(p3,p2,.62f);part.Tri(p0,p1,q1);part.Quad(p0,q1,q2,p3);}
                    else part.Quad(p0,p1,p2,p3);
                    if(a<.2f&&lo>1.1f&&lo<1.6f){
                        Vector3 G(float x,float y)=>Vector3.Lerp(Vector3.Lerp(p0,p1,x),Vector3.Lerp(p3,p2,x),y)+Vector3.up*.016f;
                        var c=G(.56f,.47f);foreach(var end in new[]{G(.18f,.15f),G(.81f,.09f),G(.92f,.77f),G(.31f,.88f)})glazingCracks.Tube(c,end,.007f,.0035f,4);
                    }return;
                }
                part.Plate(p0,p1,p2,p3,.14f);
                var normal=Vector3.Cross(p1-p0,p2-p0).normalized;if(normal.y<0)normal=-normal;
                foreach(var q in new[]{Vector3.Lerp(p0,p2,.12f),Vector3.Lerp(p1,p3,.12f),Vector3.Lerp(p2,p0,.12f),Vector3.Lerp(p3,p1,.12f)})bolts.Tube(q+normal*.01f,q+normal*.07f,.045f,.035f,6);
            }
            // Broad staggered plates, peeled fracture lips and a recognizable glazed bow.
            if(section==0){
                var stations=new[]{0f,.14f,.36f,.64f,.79f,1f};
                for(int i=0;i<stations.Length-1;i++)for(int j=0;j<8;j++){
                    float a=stations[i]+.007f,b=stations[i+1]-.007f,lo=j*Mathf.PI/8+.012f,hi=(j+1)*Mathf.PI/8-.012f;
                    bool window=i==1&&j>=2&&j<=5||i==2&&(j==1||j==6);
                    if(i==4&&j>=2&&j<=6)continue;
                    Shell(window?glass:armor,a,b,lo,hi);
                    if(window){frame.Tube(P(a,lo),P(b,lo),.055f,.055f);frame.Tube(P(a,hi),P(b,hi),.055f,.055f);frame.Tube(P(a,lo),P(a,hi),.065f,.065f);}
                }
                // Creased lower cheek, fractured windscreen and a missing pane corner.
                Vector3 a0=P(.14f,Mathf.PI*.77f),a1=P(.29f,Mathf.PI*.77f),a2=P(.38f,Mathf.PI*.92f),a3=P(.18f,Mathf.PI*.94f);
                var dent=(a0+a1+a2+a3)*.25f+new Vector3(0,-art.cockpitDamage,-.1f);
                broken.Tri(a0,a1,dent);armor.Tri(a1,a2,dent);broken.Tri(a2,a3,dent);armor.Tri(a3,a0,dent);
                var crack=P(.38f,Mathf.PI*.64f)+Vector3.up*.02f;
                foreach(var end in new[]{P(.31f,Mathf.PI*.53f),P(.49f,Mathf.PI*.7f),P(.35f,Mathf.PI*.72f)})dark.Tube(crack,end+Vector3.up*.02f,.008f,.004f,5);
                dark.Tri(P(.59f,Mathf.PI*.68f)+Vector3.up*.015f,P(.63f,Mathf.PI*.59f)+Vector3.up*.015f,P(.64f,Mathf.PI*.72f)+Vector3.up*.015f);
                // Closed tapered nose with an asymmetrically folded tip.
                var tip=P(0,Mathf.PI*.5f);for(int j=0;j<8;j++)armor.Tri(tip,P(.012f,j*Mathf.PI/8),P(.012f,(j+1)*Mathf.PI/8));
            }else{
                for(int i=0;i<12;i++)for(int j=0;j<10;j++){
                    if(section==2&&(j>=2&&j<=7||i>=9))continue;
                    if(section==1&&(i<=2&&j>=3&&j<=6||i>=9&&j>=2&&j<=7||(i>=5&&i<=7)&&(j>=6&&j<=8)))continue;
                    float a=Mathf.Max(.008f,(i+(j%2)*.3f+.18f*Mathf.Sin(i*1.9f+j*7))/12f+.003f),b=Mathf.Min(.992f,(i+1+(j%2)*.3f+.18f*Mathf.Sin((i+1)*1.9f+j*7))/12f-.003f),lo=j*Mathf.PI/10+.018f,hi=(j+1)*Mathf.PI/10-.018f;
                    Shell((i+2*j)%7==0?broken:((i+j)%4==0?highlights:armor),a,b,lo,hi,-.045f*Mathf.Sin(i*9+j*7));
                    if(section==1&&(i==1||i==4)&&j==5){Vector3 p=P(a,hi),q=P(b,hi);broken.Quad(p,q,q+new Vector3(-.12f,.18f,.25f),p+new Vector3(.15f,.24f,.18f));}
                }
            }
            // Exposed frames are real curved beams, joined to longitudinal longerons.
            for(int station=0;station<10;station++){
                float u=.035f+station*.102f;for(int j=0;j<12;j++){
                    if(section==1&&((station==0||station==9)&&j>=4&&j<=7||station==3&&j==5))continue;
                    if(section==0&&(station>7&&j>3&&j<8||u<.64f&&j>=1&&j<=10))continue;
                    frame.Tube(P(u,j*Mathf.PI/12,.1f),P(u,(j+1)*Mathf.PI/12,.1f),.105f,.105f,8);
                }
            }
            foreach(float angle in new[]{.15f,.65f,1.57f,2.5f,2.99f})for(int k=0;k<6;k++){if(section==0&&k<4&&angle>.2f&&angle<2.9f)continue;frame.Tube(P(k/6f,angle,.22f),P((k+1)/6f,angle,.22f),.06f,.06f,6);}
            // Internal tanks, bulkhead hoops and conduits visible through torn plates.
            for(int k=0;k<3;k++){
                if(section==0&&k==0)continue;float u=.25f+k*.24f;var a=P(u,Mathf.PI*.5f,height*.56f);var b=P(u+.13f,Mathf.PI*.5f,height*.56f);
                machinery.Tube(a,b,.5f,.5f,12);frame.Tube(a,b,.22f,.22f,10);
            }
            for(int j=0;j<(section==0?0:3);j++)for(int k=0;k<5;k++){
                var a=P(.1f+k*.17f,2.5f,.45f)+Vector3.up*(j*.22f);var b=P(.27f+k*.17f,2.5f,.45f)+Vector3.up*(j*.22f);machinery.Tube(a,b,.045f,.045f,6);
            }
            // Dense salvageable internals: pressure vessels, collars, equipment trays,
            // heat-exchanger fins and parallel conduits, all within the existing hull.
            for(int k=0;k<6;k++){
                if(section==0&&k<3)continue;float u=.10f+k*.135f;var c=P(u,Mathf.PI*.5f,height*.68f);
                machinery.Box(c+Vector3.up*.15f,new Vector3((right-left)*.09f,.8f,section==1?1.8f:1.25f));
                for(int fin=0;fin<6;fin++)frame.Box(c+new Vector3((fin-2.5f)*.11f,.58f,0),new Vector3(.045f,.18f,1.1f));
                foreach(float side in new[]{-1f,1f}){
                    var a=c+new Vector3(-.4f,.6f,side*.76f);var b=a+Vector3.right*.78f;
                    machinery.Tube(a,b,.26f,.26f,12);
                    foreach(float t in new[]{0f,.17f,.8f,1f})bolts.Tube(Vector3.Lerp(a,b,t)-Vector3.right*.025f,Vector3.Lerp(a,b,t)+Vector3.right*.025f,.29f,.29f,12);
                }
            }
            foreach(float angle in (section==0?new float[0]:new[]{.35f,2.78f})){
                for(int j=0;j<4;j++)for(int k=0;k<8;k++){
                    var a=P(.04f+k*.115f,angle,.28f)+Vector3.up*j*.14f;var b=P(.155f+k*.115f,angle,.28f)+Vector3.up*j*.14f;
                    (j%2==0?frame:machinery).Tube(a,b,.055f,.055f,7);
                    if(k%2==0)bolts.Tube(a-Vector3.right*.045f,a+Vector3.right*.045f,.085f,.085f,8);
                }
            }
            if(section==0){
                // Heavy window reveals, roof patches, equipment access panels and torn rear sheets.
                foreach(float u in new[]{.14f,.36f,.64f})for(int j=1;j<7;j++){
                    var a=P(u,j*Mathf.PI/8,-.025f);var b=P(u,(j+1)*Mathf.PI/8,-.025f);highlights.Tube(a,b,.095f,.095f,6);
                }
                for(int k=0;k<4;k++){
                    float u=.40f+k*.085f;Shell(k%2==0?highlights:armor,u,u+.072f,1.20f,1.89f,-.09f);
                    var a=P(u,2.55f,-.06f);var b=P(u+.07f,2.55f,-.06f);frame.Tube(a,b,.045f,.045f,6);
                }
                for(int j=0;j<7;j++){
                    if(j>1&&j<5)continue;float angle=.18f+j*.44f;var a=P(.78f,angle);var b=P(.91f,angle+.2f);var c=P(.98f,angle+.25f)+new Vector3(-.12f,.22f,.1f);
                    broken.Plate(a,b,c,P(.8f,angle+.27f),.1f);
                }
            }
            if(section==1)foreach(float side in new[]{-1f,1f}){
                var start=P(.17f,Mathf.PI*.5f,height*.55f)+Vector3.forward*side*1.25f;
                var end=P(.83f,Mathf.PI*.5f,height*.55f)+Vector3.forward*side*1.25f;
                machinery.Tube(start,end,.58f,.58f,16);
                for(int band=0;band<8;band++){var c=Vector3.Lerp(start,end,band/7f);frame.Tube(c-Vector3.right*.06f,c+Vector3.right*.06f,.63f,.63f,16);}
            }
            if(section==2){
                // Two large rear-facing bells widen the stern, with recessed throats.
                foreach(float side in new[]{-1f,1f}){
                    var s=Span(right);float half=(s.hi-s.lo)*.5f;float rearRadius=half*.46f;
                    float z=(s.lo+s.hi)*.5f+side*half*.49f;float y=rearRadius+.25f;
                    var rear=new Vector3(right,y,z);var throat=rear-Vector3.right*1.0f;float startX=Mathf.Lerp(left,right,.18f);var startSpan=Span(startX);float startHalf=(startSpan.hi-startSpan.lo)*.5f;
                    var start=new Vector3(startX,y,(startSpan.lo+startSpan.hi)*.5f+side*startHalf*.45f);
                    machinery.Tube(start,throat,Mathf.Min(startHalf*.43f,rearRadius/art.exhaustFlare),rearRadius*.72f,20);
                    frame.Tube(throat,rear,rearRadius*.72f,rearRadius,24);
                    for(int band=0;band<8;band++){
                        float t=band/8f;var c=Vector3.Lerp(start,throat,t);float r=Mathf.Lerp(Mathf.Min(startHalf*.43f,rearRadius/art.exhaustFlare),rearRadius*.72f,t);
                        (band%3==0?broken:bolts).Tube(c-Vector3.right*.07f,c+Vector3.right*.07f,r+.06f,r+.06f,24);
                    }
                    for(int rib=0;rib<12;rib++){
                        float a=rib*Mathf.PI/6;var radial=new Vector3(0,Mathf.Sin(a),Mathf.Cos(a));
                        frame.Tube(start+radial*Mathf.Min(startHalf*.43f,rearRadius/art.exhaustFlare),throat+radial*rearRadius*.74f,.065f,.065f,6);
                    }
                    // Thick lip and nested interior, not a capped painted cylinder.
                    for(int i=0;i<24;i++){
                        float a=i*Mathf.PI/12,b=(i+1)*Mathf.PI/12;Vector3 Rad(float t,float r)=>new Vector3(0,Mathf.Sin(t)*r,Mathf.Cos(t)*r);
                        machinery.Quad(rear+Rad(a,rearRadius),rear+Rad(b,rearRadius),rear+Rad(b,rearRadius*.86f),rear+Rad(a,rearRadius*.86f));
                        dark.Quad(rear+Rad(a,rearRadius*.86f),rear+Rad(b,rearRadius*.86f),throat+Rad(b,rearRadius*.5f),throat+Rad(a,rearRadius*.5f));
                        dark.Tri(throat,throat+Rad(a,rearRadius*.5f),throat+Rad(b,rearRadius*.5f));
                        if(i%2==0){var hub=throat+Vector3.right*.12f;machinery.Quad(hub+Rad(a,rearRadius*.14f),hub+Rad(a+.08f,rearRadius*.48f),hub+Rad(a+.13f,rearRadius*.48f),hub+Rad(a+.07f,rearRadius*.14f));}
                        if(i%3==0)broken.Tube(throat+Rad(a,rearRadius*.75f),rear+Rad(a,rearRadius),.035f,.055f,5);
                    }
                }
            }
            if(section==1){
                // Fallen layered wing halves remain inside the middle solid footprint.
                foreach(float side in new[]{-1f,1f}){
                    var a=P(side>0?.10f:.24f,side>0?.2f:Mathf.PI-.2f);var b=P(side>0?.62f:.80f,side>0?.2f:Mathf.PI-.2f);a.y=.2f;b.y=.45f;
                    float x0=Mathf.Lerp(left,right,side>0?.30f:.14f),x1=Mathf.Lerp(left,right,side>0?.94f:.79f);
                    var sa=Span(x0);var sb=Span(x1);Vector3 c=new Vector3(x1,.18f,side>0?sb.hi-.12f:sb.lo+.12f),d=new Vector3(x0,.12f,side>0?sa.hi-.12f:sa.lo+.12f);
                    a.y=.56f;b.y=.9f;c.y=.12f;d.y=.23f;d=Vector3.Lerp(d,a,.28f);c=Vector3.Lerp(c,b,.05f);
                    for(int along=0;along<5;along++)for(int across=0;across<3;across++){
                        if(along==0&&across>=1||along==4&&across==0||along==3&&across==2&&side<0)continue;
                        Vector3 W(float u,float v)=>Vector3.Lerp(Vector3.Lerp(a,b,u),Vector3.Lerp(d,c,u),v);
                        float u0=along/5f+.009f,u1=(along+1)/5f-.009f,v0=across/3f+.012f,v1=(across+1)/3f-.012f;
                        (along%3==0?highlights:wing).Plate(W(u0,v0),W(u1,v0),W(u1,v1),W(u0,v1),.16f);
                    }
                    for(int row=0;row<4;row++){float t=row/3f;frame.Tube(Vector3.Lerp(a,d,t)-Vector3.up*.1f,Vector3.Lerp(b,c,t)-Vector3.up*.1f,.09f,.09f,6);}
                    for(int i=0;i<=4;i++){float t=i/4f;frame.Tube(Vector3.Lerp(a,b,t)+Vector3.up*.04f,Vector3.Lerp(d,c,t)+Vector3.up*.04f,.035f,.035f,5);}
                    broken.Tube(a,b,.1f,.1f,6);frame.Tube(Vector3.Lerp(d,c,.23f),Vector3.Lerp(d,c,.87f),.045f,.045f,5);
                }
            }
            // The existing filled blocker remains legible as a low buried keel.
            // The continuous basin is the floor; no raised flat polygon under the wreck.
            if(section!=1){
                // Cosmetic displacement stays INSIDE the existing solid: no end bypass.
                float a=section==0?fullLeft:right,b=section==0?left:fullRight;
                const int along=16,across=12; // Shared vertices give a smooth single sand mound.
                var ground=new Vector3[(along+1)*(across+1)];var faces=new List<int>();
                Vector3 Sand(float u,float t){float x=Mathf.Lerp(a,b,u);var span=Span(x);float y=.002f+1.15f*Mathf.Pow(Mathf.Sin(u*Mathf.PI),2)*Mathf.Pow(Mathf.Max(0,Mathf.Sin(t*Mathf.PI)),2);return new Vector3(x,y,Mathf.Lerp(span.lo,span.hi,t));}
                for(int i=0;i<=along;i++)for(int j=0;j<=across;j++)ground[i*(across+1)+j]=Sand((float)i/along,(float)j/across);
                for(int i=0;i<along;i++)for(int j=0;j<across;j++){int k=i*(across+1)+j,n=k+across+1;faces.Add(k);faces.Add(n+1);faces.Add(n);faces.Add(k);faces.Add(k+1);faces.Add(n+1);}
                var mound=new Mesh{name="Smooth wreck end sand mound"};mound.vertices=ground;mound.SetTriangles(faces,0);mound.RecalculateNormals();mound.RecalculateBounds();Emit("Wreck end sand mound",mound,0,Color.white);
                float Noise(int i,float seed){float v=Mathf.Sin(i*12.9898f+seed)*43758.5453f;return v-Mathf.Floor(v);}
                for(int i=0;i<42;i++){
                    float u=.06f+.88f*Noise(i,7),t=.07f+.86f*Noise(i,29),size=.09f+.13f*Noise(i,47);var q=Sand(u,t);size=Mathf.Min(size,(float)Math.Sqrt(shape.Footprint.BoundaryDistanceSquared(new NavPoint(q.x,q.z)))*.85f);
                    var top=q+new Vector3(size*.2f,size*.65f,-size*.1f);var p0=q+new Vector3(-size,0,-size*.6f);var p1=q+new Vector3(size,0,-size*.4f);var p2=q+new Vector3(size*.6f,0,size*.65f);var p3=q+new Vector3(-size*.6f,0,size*.8f);
                    scree.UpTri(p0,p1,top);scree.UpTri(p1,p2,top);scree.UpTri(p2,p3,top);scree.UpTri(p3,p0,top);
                }
            }
            float Random01(int i,float seed){float v=Mathf.Sin(i*12.9898f+seed+section*61)*43758.5453f;return v-Mathf.Floor(v);}
            for(int i=0;i<155;i++){
                float x=Mathf.Lerp(fullLeft+.25f,fullRight-.25f,Random01(i,5));var span=Span(x);
                float r=.09f+Mathf.Pow(Random01(i,17),3)*.50f;
                float t=Random01(i,31);t=t<.5f?t*.45f:.775f+(t-.5f)*.45f;float z=Mathf.Lerp(span.lo+r,span.hi-r,t);
                float allowed=(float)Math.Sqrt(shape.Footprint.BoundaryDistanceSquared(new NavPoint(x,z)))*.85f;if(allowed>.025f)scree.Rock(new Vector3(x,.008f,z),Mathf.Min(r,allowed),i*5.7f);
            }
            void Part(string name,MeridianPart mesh,Color color,int kind=2)=>Emit("Wreck "+section+" "+name,mesh.Mesh(name),kind,color);
            Part("windscreen fractures",glazingCracks,new Color(.43f,.48f,.46f),4);
            Part("fallen wing armor",wing,new Color(.66f,.62f,.52f),4);
            Part("replacement armor panels",highlights,new Color(.67f,.63f,.54f),4);Part("fasteners and machined edges",bolts,new Color(.29f,.25f,.19f),4);
            Part("layered ivory armor",armor,new Color(art.hullValue,art.hullValue*.94f,art.hullValue*.81f),4);
            Part("buckled weathered metal",broken,new Color(.47f,.41f,.32f));Part("structural ribs",frame,new Color(.22f,.17f,.12f));
            Part("machinery and conduits",machinery,new Color(.32f,.34f,.32f));Part("recessed interior",dark,new Color(.045f,.06f,.07f));Part("fractured cockpit glazing",glass,new Color(.065f,.12f,.15f),5);
            Part("end pocket sand berm",dust,Color.white,0);Part("end pocket fine gypsum scree",scree,new Color(.72f,.65f,.52f),1);
        }
    }
}
