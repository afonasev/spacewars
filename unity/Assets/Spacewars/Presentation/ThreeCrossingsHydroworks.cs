using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
using UnityEngine;

namespace Spacewars.Presentation
{
    // Presentation only. Authored proportions below describe machine parts, not gameplay controls.
    // All above-ground fittings are external to bridge clear bands; shelves stay below ground.
    public static class ThreeCrossingsHydroworks
    {
        public sealed class Part
        {
            public string Name; public Mesh Mesh; public int Role;
            public Part(string name,Mesh mesh,int role){Name=name;Mesh=mesh;Role=role;}
        }
        static readonly Color Steel=new Color(.38f,.43f,.45f),Dark=new Color(.13f,.18f,.20f),Amber=new Color(.70f,.40f,.12f),Concrete=new Color(.48f,.50f,.47f);
        sealed class Kit
        {
            public readonly List<Part> Parts=new List<Part>();
            public string Zone; public float WaterLevel;
            public void Add(string name,int role,Action<EnvironmentDetails.Builder> draw)
            {var b=new EnvironmentDetails.Builder();draw(b);var mesh=b.Mesh(name);
                if(role==5){var v=mesh.vertices;var colors=mesh.colors;for(int i=0;i<v.Length;i++)colors[i].a=Mathf.InverseLerp(-.65f,WaterLevel,v[i].y);mesh.colors=colors;}
                Parts.Add(new Part(Zone+" / "+name,mesh,role));}
            public void Box(string name,Vector3 p,Vector3 size,Color c,int role=3)=>Add(name,role,b=>b.Box(p,size,c));
            public void Beam(string name,Vector3 a,Vector3 z,float width,Color c)
            {
                Add(name,3,b=>{var local=new EnvironmentDetails.Builder();local.Box(Vector3.zero,new Vector3(width,(z-a).magnitude,width),c);var m=local.Mesh(name);b.Append(m,Matrix4x4.TRS((a+z)*.5f,Quaternion.FromToRotation(Vector3.up,z-a),Vector3.one));UnityEngine.Object.Destroy(m);});
            }
            public void Pipe(string name,Vector3 a,Vector3 z,float radius,Color c)
            {
                Add(name,3,b=>{
                    var axis=(z-a).normalized;var u=Vector3.Cross(axis,Mathf.Abs(axis.y)>.9f?Vector3.forward:Vector3.up).normalized;var v=Vector3.Cross(axis,u);
                    const int sides=12; // Maintained smooth cylindrical silhouette at close inspection.
                    for(int i=0;i<sides;i++)
                    {float p=i*Mathf.PI*2/sides,q=(i+1)*Mathf.PI*2/sides;var r=(u*Mathf.Cos(p)+v*Mathf.Sin(p))*radius;var s=(u*Mathf.Cos(q)+v*Mathf.Sin(q))*radius;b.Triangle(a+r,z+s,z+r,c);b.Triangle(a+r,a+s,z+s,c);b.Triangle(a,a+s,a+r,c);b.Triangle(z,z+r,z+s,c);}
                });
            }
            public void Valve(Vector3 p,float r)
            {
                Pipe("Valve housing",p-Vector3.up*.35f,p+Vector3.up*.1f,r*.45f,Dark);
                for(int i=0;i<12;i++){float a=i*Mathf.PI/6,z=(i+1)*Mathf.PI/6;Beam("Valve wheel",p+new Vector3(Mathf.Cos(a)*r,0,Mathf.Sin(a)*r),p+new Vector3(Mathf.Cos(z)*r,0,Mathf.Sin(z)*r),.065f,Amber);}
                Beam("Valve spoke",p-Vector3.right*r,p+Vector3.right*r,.055f,Amber);Beam("Valve spoke",p-Vector3.forward*r,p+Vector3.forward*r,.055f,Amber);
            }
        }
        // Static maintenance assemblies reuse the host zone and steel role: no new materials,
        // production objects, colliders or update loop. Dimensions are authored machine proportions.
        static readonly Color Teal=new Color(.24f,.43f,.43f),Ivory=new Color(.72f,.70f,.57f);
        static void Cabinet(Kit k,Vector3 p)
        {
            k.Box("Instrument cabinet",p+Vector3.up*.48f,new Vector3(.66f,.94f,.48f),Teal);
            k.Box("Cabinet door",p+new Vector3(0,.48f,-.25f),new Vector3(.55f,.78f,.045f),Teal);
            k.Box("Cabinet latch",p+new Vector3(.20f,.45f,-.29f),new Vector3(.045f,.17f,.055f),Dark);
            k.Box("Hazard placard",p+new Vector3(0,.68f,-.28f),new Vector3(.25f,.25f,.025f),Amber);
            k.Add("Hazard symbol",3,b=>b.Triangle(p+new Vector3(-.08f,.61f,-.296f),p+new Vector3(0,.76f,-.296f),p+new Vector3(.08f,.61f,-.296f),Dark));
            k.Box("Instrument readout",p+new Vector3(0,.90f,-.278f),new Vector3(.24f,.07f,.025f),Ivory);
        }
        static void Grille(Kit k,Vector3 p,float width,float length)
        {
            k.Box("Inspection cover rim",p,new Vector3(width,.065f,length),Steel);
            k.Box("Inspection cover recess",p+Vector3.up*.041f,new Vector3(width-.1f,.035f,length-.1f),Dark);
            for(float z=-length*.5f+.16f;z<length*.5f-.08f;z+=.18f)
                k.Box("Service grille bar",p+new Vector3(0,.073f,z),new Vector3(width-.15f,.04f,.055f),Steel);
        }
        static void SpareCoil(Kit k,Vector3 p)
        {
            for(int ring=0;ring<3;ring++)for(int i=0;i<16;i++)
            {
                float a=i*Mathf.PI/8,b=(i+1)*Mathf.PI/8,r=.24f+ring*.06f;
                k.Beam("Stowed cable coil",p+new Vector3(Mathf.Cos(a)*r,ring*.045f,Mathf.Sin(a)*r),p+new Vector3(Mathf.Cos(b)*r,ring*.045f,Mathf.Sin(b)*r),.055f,Dark);
            }
        }
        static void CollarBolts(Kit k,Vector3 p,float radius)
        {
            for(int i=0;i<6;i++){float a=i*Mathf.PI/3;k.Box("Flange fastener",p+new Vector3(Mathf.Cos(a)*radius,0,Mathf.Sin(a)*radius),new Vector3(.065f,.075f,.065f),Steel);}
        }
        static void PlatformDetails(Kit k,float x,float z,int side)
        {
            Cabinet(k,new Vector3(x-.72f,.49f,z-.60f));
            Grille(k,new Vector3(x-.30f,.50f,z+.85f),.56f,.6f);
            SpareCoil(k,new Vector3(x+.66f,.51f,z+.87f));
            foreach(float dx in new[]{-.17f,.17f})
            {
                var at=new Vector3(x+.72f+dx,.71f,z+.25f);
                k.Pipe("Service canister",at-Vector3.up*.23f,at+Vector3.up*.23f,.105f,Ivory);
                k.Pipe("Canister cap",at+Vector3.up*.23f,at+Vector3.up*.31f,.06f,Amber);
            }
            for(int j=0;j<8;j++)
                k.Box("Plinth edge warning",new Vector3(x-1.18f+j*.34f,.145f,z+side*1.47f),new Vector3(.23f,.055f,.16f),j%2==0?Amber:Ivory);
            for(int j=0;j<3;j++)CollarBolts(k,new Vector3(x-.9f,-.65f+j*.8f,z+.6f),.2f);
        }
        static void BankAccess(Kit k,float x,float z,int side)
        {
            float wall=x+side*1.24f;
            foreach(float dz in new[]{-.31f,.31f})k.Beam("Bank ladder rail",new Vector3(wall,-2.5f,z+dz),new Vector3(wall,.18f,z+dz),.075f,Steel);
            for(int j=0;j<9;j++)k.Beam("Bank ladder rung",new Vector3(wall,-2.3f+j*.28f,z-.31f),new Vector3(wall,-2.3f+j*.28f,z+.31f),.065f,Amber);
            // Face the existing foundation's front wall so the calibrated intervals read
            // from the normal gameplay camera rather than disappearing edge-on behind the skid.
            float staff=x+.55f;
            k.Box("Water level staff backing",new Vector3(staff,-1.65f,z-2.535f),new Vector3(.24f,2.3f,.06f),Dark);
            for(int j=0;j<12;j++)
                k.Box("Water level interval",new Vector3(staff,-2.7f+j*.19f,z-2.58f),new Vector3(.18f,.14f,.035f),j%2==0?Ivory:Amber);
        }
        public static List<Part> Build(ThreeCrossingsMap map,ThreeCrossingsSurfaceProfile p,bool optimize=true)
        {
            var k=new Kit{WaterLevel=-(float)map.WaterDepth};
            foreach(var support in map.Supports.Where(s=>s.IsBridge))
            {
                k.Zone=support.Id;var d=support.Bounds;
                float lo=(float)d.MinX,hi=(float)d.MaxX,z0=(float)d.MinZ,z1=(float)d.MaxZ;
                bool hero=support.Id=="central-bridge",north=support.Id=="north-bridge";
                foreach(int side in new[]{-1,1})
                {
                    float z=side<0?z0-p.hydroEdgeOffset:z1+p.hydroEdgeOffset;
                    float h=hero?.8f:north?1.05f:.55f;
                    k.Box("Continuous outer girder",new Vector3((lo+hi)*.5f,-.35f,z),new Vector3(hi-lo,.55f,.32f),Steel);
                    k.Beam("Open railing",new Vector3(lo,h,z),new Vector3(hi,h,z),.09f,Steel);
                    // Cable bundle sits underneath the outside girder; clamps read as one service run.
                    k.Box("Cable tray bed",new Vector3((lo+hi)*.5f,-.51f,z+side*.28f),new Vector3(hi-lo,.12f,.34f),Dark);
                    foreach(float dz in new[]{-.10f,.10f})k.Pipe("Service cable",new Vector3(lo,-.39f,z+side*.28f+dz),new Vector3(hi,-.39f,z+side*.28f+dz),.045f,Dark);
                    for(float cx=lo+.6f;cx<hi;cx+=2.1f)
                    {
                        k.Box("Cable tray clamp",new Vector3(cx,-.38f,z+side*.28f),new Vector3(.11f,.16f,.40f),Steel);
                        if(!hero)k.Box("Outer edge warning tab",new Vector3(cx,.10f,z),new Vector3(.30f,.065f,.22f),Amber);
                    }
                    int bays=Mathf.CeilToInt((hi-lo)/(north?2.5f:3.5f));
                    for(int i=0;i<=bays;i++)
                    {
                        float x=Mathf.Lerp(lo,hi,i/(float)bays);
                        k.Beam("Railing stanchion",new Vector3(x,-.45f,z),new Vector3(x,h,z),.11f,Steel);
                        if(i==bays)continue;
                        float next=Mathf.Lerp(lo,hi,(i+1)/(float)bays);
                        if(north){k.Beam("Service truss diagonal",new Vector3(x,-.6f,z),new Vector3(next,h,z),.12f,Steel);k.Beam("Service truss diagonal",new Vector3(x,h,z),new Vector3(next,-.6f,z),.12f,Steel);}
                        else if(!hero){k.Box("Modular plate joint",new Vector3(x,-.2f,z),new Vector3(.22f,.8f,.48f),Amber);k.Beam("Module underbrace",new Vector3(x,-1.1f,z),new Vector3(next,-.3f,z),.18f,Steel);}
                        else k.Box("Concrete edge cap",new Vector3((x+next)*.5f,.12f,z),new Vector3(next-x-.1f,.30f,.55f),Concrete,2);
                        k.Box("Joint shoe",new Vector3(x,.04f,z),new Vector3(.34f,.09f,.45f),Dark);
                        foreach(float dx in new[]{-.10f,.10f})k.Box("Joint fastener",new Vector3(x+dx,.10f,z),new Vector3(.07f,.05f,.07f),Amber);
                    }
                }
                if(hero)
                {
                    float tail=z0-p.hydroEdgeOffset-.45f,bottom=-(float)map.WaterDepth+.05f;
                    // Five buttresses and four independently readable gate bays under the clear deck.
                    for(int i=0;i<=4;i++)
                    {
                        float x=Mathf.Lerp(lo+.5f,hi-.5f,i/4f);
                        k.Box("Spillway buttress",new Vector3(x,(bottom-.1f)*.5f,tail),new Vector3(.65f,-bottom+.1f,1.35f),Concrete,2);
                        k.Box("Pier crown",new Vector3(x,-.28f,tail),new Vector3(.92f,.25f,1.6f),Steel);
                        k.Box("Water level stain",new Vector3(x,bottom+.25f,tail-.69f),new Vector3(.66f,.35f,.025f),Dark,2);
                        if(i==4)continue;
                        float next=Mathf.Lerp(lo+.5f,hi-.5f,(i+1)/4f),a=x+.38f,b=next-.38f;
                        k.Box("Recessed sluice gate",new Vector3((a+b)*.5f,-.7f,tail+.25f),new Vector3(b-a,1.15f,.22f),Dark);
                        for(int j=0;j<5;j++)k.Box("Gate stiffener",new Vector3((a+b)*.5f,-1.1f+j*.21f,tail+.08f),new Vector3(b-a,.06f,.12f),Steel);
                        k.Add("Falling water",5,mesh=>{
                            const int strips=24,steps=8;
                            for(int j=0;j<strips;j++)for(int row=0;row<steps;row++)
                            {
                                float l=Mathf.Lerp(a,b,j/(float)strips),r=Mathf.Lerp(a,b,(j+1)/(float)strips);
                                Vector3 V(float x,float t)
                                {
                                    float reach=p.hydroSpillReach*(.93f+.06f*Mathf.Sin(x*1.3f)+.025f*Mathf.Sin(x*4.7f));
                                    float foot=bottom-.18f+.06f*Mathf.Sin(x*3.7f);
                                    return new Vector3(x,Mathf.Lerp(-.65f,foot,Mathf.Pow(t,1.6f)),tail-.15f-reach*t);
                                }
                                float t0=row/(float)steps,t1=(row+1)/(float)steps;
                                var v0=V(l,t0);var v1=V(r,t0);var v2=V(r,t1);var v3=V(l,t1);
                                // Alpha carries cascade travel to smoothly dissolve the foot into the basin.
                                mesh.Triangle(v0,v1,v2,Color.white);mesh.Triangle(v0,v2,v3,Color.white);
                            }
                        });
                    }
                    foreach(int sx in new[]{-1,1})foreach(int sz in new[]{-1,1})
                    {
                        float x=sx*(hi-.9f),z=sz<0?z0-2.2f:z1+2.2f;
                        k.Box("Hoist maintenance plinth",new Vector3(x,-.6f,z),new Vector3(3.1f,1.4f,3.2f),Concrete,2);
                        k.Box("Wingwall foundation",new Vector3(x,-1.7f,z+sz*2),new Vector3(1.35f,2.8f,4.2f),Concrete,2);
                        PlatformDetails(k,x,z,sz);
                        k.Pipe("Hydraulic supply riser",new Vector3(x-.9f,-2.5f,z+.6f),new Vector3(x-.9f,1.8f,z+.6f),.16f,Steel);
                        for(int j=0;j<3;j++)k.Pipe("Pipe flange",new Vector3(x-.9f,-.8f+j*.8f,z+.6f),new Vector3(x-.9f,-.68f+j*.8f,z+.6f),.25f,Amber);
                        k.Box("Gate hoist frame",new Vector3(x,.35f,z),new Vector3(2.6f,.22f,2.7f),Steel);
                        foreach(float dx in new[]{-.95f,.95f})k.Box("Gate hoist leg",new Vector3(x+dx,p.hydroMachineHeight*.5f,z),new Vector3(.20f,p.hydroMachineHeight,.28f),Amber);
                        foreach(float dz in new[]{-.9f,.9f})k.Box("Open gantry top chord",new Vector3(x,p.hydroMachineHeight,z+dz),new Vector3(2.6f,.22f,.22f),Amber);
                        k.Box("Winch reduction gearbox",new Vector3(x+1,p.hydroMachineHeight-.5f,z),new Vector3(.55f,.95f,1.15f),Amber);
                        for(int coil=0;coil<10;coil++)k.Pipe("Cable wound on drum",new Vector3(x-.7f+coil*.14f,p.hydroMachineHeight-.5f,z),new Vector3(x-.65f+coil*.14f,p.hydroMachineHeight-.5f,z),.43f,Dark);
                        var wheel=new Vector3(x+.8f,1.25f,z-1.2f);
                        for(int tooth=0;tooth<16;tooth++){float a=tooth*Mathf.PI/8,b=(tooth+1)*Mathf.PI/8;k.Beam("Vertical handwheel",wheel+new Vector3(Mathf.Cos(a)*.44f,Mathf.Sin(a)*.44f,0),wheel+new Vector3(Mathf.Cos(b)*.44f,Mathf.Sin(b)*.44f,0),.075f,Amber);}
                        foreach(float angle in new[]{0f,60f,120f}){var spoke=Quaternion.Euler(0,0,angle)*Vector3.right*.44f;k.Beam("Handwheel spoke",wheel-spoke,wheel+spoke,.06f,Steel);}
                        k.Pipe("Gearbox input shaft",wheel+Vector3.forward*.02f,wheel+Vector3.forward*.9f,.1f,Steel);
                        foreach(float dx in new[]{-1.3f,1.3f})foreach(float dz in new[]{-1.3f,1.3f})k.Box("Plinth anchor bolt",new Vector3(x+dx,.14f,z+dz),new Vector3(.16f,.16f,.16f),Steel);
                        k.Pipe("Winch drum",new Vector3(x-.8f,p.hydroMachineHeight-.5f,z),new Vector3(x+.8f,p.hydroMachineHeight-.5f,z),.38f,Steel);
                        k.Pipe("Gate drive",new Vector3(x,-1.4f,z),new Vector3(x,p.hydroMachineHeight,z),.09f,Dark);
                        k.Valve(new Vector3(x+.8f,p.hydroMachineHeight+.24f,z),.32f);
                        for(int j=0;j<5;j++)k.Beam("Service ladder rung",new Vector3(x-.5f,.15f+j*.3f,z-1.08f),new Vector3(x+.1f,.15f+j*.3f,z-1.08f),.055f,Steel);
                        foreach(float dx in new[]{-.5f,.1f})k.Beam("Service ladder rail",new Vector3(x+dx,0,z-1.08f),new Vector3(x+dx,1.65f,z-1.08f),.06f,Steel);
                    }
                }
                else foreach(float x in new[]{lo+.5f,hi-.5f})
                {
                    k.Box("Bridge service pedestal",new Vector3(x,.35f,z0-.8f),new Vector3(.8f,.7f,.9f),Dark);
                    k.Box(north?"Service junction box":"Modular coupler",new Vector3(x,.85f,z0-.8f),new Vector3(.65f,.55f,.7f),Amber);
                }
            }
            // Follow exact water polygons; no invented navigable shelves or rocks over support.
            int zone=0;
            foreach(var water in map.Water)
            {
                var rim=water.Footprint.Vertices;
                for(int edge=0;edge<rim.Count;edge++)
                {
                    var a=rim[edge];var b=rim[(edge+1)%rim.Count];
                    if(a.X*b.X<=0||Math.Abs(a.Z-b.Z)<2)continue;
                    float sign=Mathf.Sign((float)a.X);float length=(float)Math.Abs(a.Z-b.Z);
                    int pieces=Mathf.CeilToInt(length/3.5f);k.Zone="quarry-bank-"+(zone++/3);
                    for(int j=0;j<pieces;j++)
                    {
                        float t0=j/(float)pieces,t1=(j+1)/(float)pieces;
                        var aa=new Vector3(Mathf.Lerp((float)a.X,(float)b.X,t0),0,Mathf.Lerp((float)a.Z,(float)b.Z,t0));
                        var bb=new Vector3(Mathf.Lerp((float)a.X,(float)b.X,t1),0,Mathf.Lerp((float)a.Z,(float)b.Z,t1));
                        float w=p.hydroShelfScale*(.85f+.15f*Mathf.Sin((aa.z+bb.z)*.4f));
                        k.Add("Connected quarry strata",4,mesh=>{
                            // Endpoint functions use continuous world coordinates so neighboring segments share exact rims.
                            Vector3 Rim(Vector3 v,float width,float height)=>v+new Vector3(-sign*width*p.hydroShelfScale*(.85f+.15f*Mathf.Sin(v.z*.8f)),height+.07f*Mathf.Sin(v.z*1.1f),0);
                            var previousA=Rim(aa,0,-.35f);var previousB=Rim(bb,0,-.35f);
                            float[] widths={.22f,.38f,.41f,.73f,.77f,1f},heights={-.65f,-.72f,-1.6f,-1.72f,-2.65f,-2.8f};
                            for(int tier=0;tier<widths.Length;tier++)
                            {
                                var nextA=Rim(aa,widths[tier],heights[tier]);var nextB=Rim(bb,widths[tier],heights[tier]);
                                var c=tier<3?new Color(.85f,.87f,.85f):new Color(.64f,.71f,.69f);
                                if(Vector3.Cross(previousB-previousA,nextB-previousA).y>0){mesh.Triangle(previousA,previousB,nextB,c);mesh.Triangle(previousA,nextB,nextA,c);}
                                else {mesh.Triangle(previousA,nextB,previousB,c);mesh.Triangle(previousA,nextA,nextB,c);}
                                previousA=nextA;previousB=nextB;
                            }
                            var lowA=previousA+Vector3.down*1.5f;var lowB=previousB+Vector3.down*1.5f;
                            mesh.Triangle(previousA,lowB,previousB,Dark);mesh.Triangle(previousA,lowA,lowB,Dark);
                            mesh.Triangle(previousA,previousB,lowB,Dark);mesh.Triangle(previousA,lowB,lowA,Dark);
                        });
                        if(j%3==1)
                        {
                            var rock=EnvironmentDetails.Boulder((j+edge)%3);var pos=(aa+bb)*.5f+new Vector3(-sign*w*.45f,-2.65f,0);
                            k.Add("Rooted fractured outcrop",4,mesh=>mesh.Append(rock,Matrix4x4.TRS(pos,Quaternion.Euler(0,j*47+edge*13,0),new Vector3(w*.75f,1.4f,w*.9f))));UnityEngine.Object.Destroy(rock);
                        }
                    }
                }
            }
            // Purposeful pumping alcoves on the water side of the banks, never in approaches.
            foreach(int side in new[]{-1,1})
            {
                float z=side*23,x=side*8.4f;k.Zone="pump-alcove-"+side;
                if(map.SupportAt(new NavPoint(x,z))!=null)continue;
                k.Box("Pump alcove foundation",new Vector3(x,-1.7f,z),new Vector3(2.4f,2.4f,5),Concrete,2);
                k.Box("Twin pump skid",new Vector3(x,-.3f,z),new Vector3(2.2f,.30f,3.5f),Steel);
                BankAccess(k,x,z,side);
                Cabinet(k,new Vector3(x-.65f,-.12f,z+1.45f));
                Grille(k,new Vector3(x+.58f,-.10f,z-1.3f),.65f,.62f);
                SpareCoil(k,new Vector3(x-.70f,-.10f,z-1.36f));
                // Spare flange is stowed flat beside the coil on the existing skid.
                k.Pipe("Stowed spare flange",new Vector3(x+.5f,-.08f,z+1.4f),new Vector3(x+.5f,.015f,z+1.4f),.27f,Steel);
                k.Pipe("Spare flange bore",new Vector3(x+.5f,.016f,z+1.4f),new Vector3(x+.5f,.022f,z+1.4f),.15f,Dark);
                foreach(float dz in new[]{-.9f,.9f})
                {
                    CollarBolts(k,new Vector3(x,.32f,z+dz),.34f);
                    k.Pipe("Water intake",new Vector3(x,-2.5f,z+dz),new Vector3(x,1,z+dz),.28f,Steel);
                    k.Pipe("Intake flange",new Vector3(x,.15f,z+dz),new Vector3(x,.29f,z+dz),.41f,Amber);
                    k.Box("Pump motor casing",new Vector3(x+.6f,.45f,z+dz),new Vector3(.8f,.65f,.8f),Dark);
                    for(int j=0;j<5;j++)k.Box("Motor cooling rib",new Vector3(x+.6f,.82f,z+dz-.29f+j*.14f),new Vector3(.78f,.11f,.055f),Steel);
                    k.Valve(new Vector3(x,1.1f,z+dz),.35f);
                }
            }
            if(!optimize)return k.Parts;
            var result=new List<Part>();
            // Spatial zones preserve bridge/bank visibility granularity; identical material roles merge.
            foreach(var group in k.Parts.GroupBy(part=>part.Name.Split('/')[0]+"|"+part.Role))
            {
                var b=new EnvironmentDetails.Builder();foreach(var part in group)b.Append(part.Mesh,Matrix4x4.identity);
                var mesh=b.Mesh(group.Key);FinalProceduralMesh.IndexOwned(mesh);
                // Recreate the indexed buffer at its final size. Unity can retain the old larger
                // allocation after in-place indexing; copy every stream emitted by this fixed kit.
                var compact=new Mesh{name=mesh.name,indexFormat=mesh.indexFormat};compact.SetVertices(mesh.vertices);compact.SetNormals(mesh.normals);compact.SetColors(mesh.colors);compact.SetTriangles(mesh.triangles,0);compact.bounds=mesh.bounds;
                UnityEngine.Object.Destroy(mesh);result.Add(new Part("Hydroworks "+group.Key,compact,group.First().Role));
            }
            foreach(var part in k.Parts)UnityEngine.Object.Destroy(part.Mesh);
            return result;
        }
    }
}
