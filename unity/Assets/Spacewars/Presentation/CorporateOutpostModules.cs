using UnityEngine;

namespace Spacewars.Presentation
{
    // Reusable authored local geometry: proportions/topology describe machinery,
    // while scale and site reach belong to the map's validated presentation profile.
    public static class CorporateOutpostModules
    {
        static readonly Color Steel=new Color(.72f,.76f,.76f),Casing=new Color(.58f,.65f,.65f),Dark=new Color(.24f,.28f,.30f),Paint=new Color(.82f,.70f,.43f);
        public static bool IsModule(string kind)=>kind=="CargoModule"||kind=="ExtractionSkid";
        static void Tube(EnvironmentDetails.Builder b,Vector3 center,float radius,float length,Color tint)
        {
            const int sides=12; // Authored pipe topology, independent of gameplay/art tuning.
            for(int i=0;i<sides;i++)
            {
                float a=i*Mathf.PI*2/sides,n=(i+1)*Mathf.PI*2/sides;
                var p=center+new Vector3(Mathf.Cos(a)*radius,-length/2,Mathf.Sin(a)*radius);
                var q=center+new Vector3(Mathf.Cos(n)*radius,-length/2,Mathf.Sin(n)*radius);
                var r=q+Vector3.up*length;var s=p+Vector3.up*length;
                b.Triangle(p,r,q,tint);b.Triangle(p,s,r,tint);
                b.Triangle(s,center+Vector3.up*length/2,r,tint);
                b.Triangle(p,q,center-Vector3.up*length/2,tint);
            }
        }
        static void Badge(EnvironmentDetails.Builder b,Vector3 center,float size)
        {
            // Neutral logistics stencil: broken double chevron and serial bars;
            // no team ownership, faction colors or selectable-unit silhouette.
            b.Box(center,new Vector3(size,.025f,size*.6f),Dark);
            for(int i=0;i<3;i++)b.Box(center+new Vector3(-size*.28f+i*size*.22f,.018f,0),new Vector3(size*.10f,.015f,size*.38f),Paint);
            b.Triangle(center+new Vector3(size*.15f,.04f,-size*.22f),center+new Vector3(size*.15f,.04f,size*.22f),center+new Vector3(size*.35f,.04f,0),Steel);
        }
        public static Mesh Build(string kind,float serviceScale=0)
        {
            var b=new EnvironmentDetails.Builder();
            if(kind=="CargoModule")
            {
                b.Box(new Vector3(0,.12f,0),new Vector3(2.5f,.24f,1.5f),Dark);
                b.Box(new Vector3(0,.80f,0),new Vector3(2.30f,1.15f,1.35f),Casing);
                foreach(int side in new[]{-1,1})
                {
                    for(int i=0;i<8;i++)b.Box(new Vector3(-1.01f+i*.29f,.8f,side*.70f),new Vector3(.065f,.98f,.055f),Steel);
                    foreach(float x in new[]{-1.15f,1.15f})b.Box(new Vector3(x,.78f,side*.69f),new Vector3(.12f,1.22f,.12f),Paint);
                }
                b.Box(new Vector3(0,1.39f,0),new Vector3(2.42f,.10f,1.45f),Steel);
                foreach(float z in new[]{-.42f,.42f})b.Box(new Vector3(1.18f,.80f,z),new Vector3(.045f,.93f,.07f),Dark);
                b.Box(new Vector3(1.21f,.62f,0),new Vector3(.08f,.11f,.31f),Paint);
                Badge(b,new Vector3(.38f,1.45f,0),.72f);
            }
            else
            {
                b.Box(new Vector3(0,.10f,0),new Vector3(2.35f,.20f,1.55f),Dark);
                b.Box(new Vector3(-.58f,.65f,0),new Vector3(.90f,1.10f,1.30f),Casing);
                b.Box(new Vector3(-.58f,1.25f,0),new Vector3(1.05f,.12f,1.40f),Steel);
                for(int i=0;i<5;i++)b.Box(new Vector3(-.58f,.55f+i*.11f,-.68f),new Vector3(.66f,.045f,.045f),Dark);
                foreach(float z in new[]{-.40f,.40f})
                {
                    Tube(b,new Vector3(.45f,.75f,z),.27f,1.10f,Steel);
                    b.Box(new Vector3(.45f,1.02f,z),new Vector3(.58f,.10f,.58f),Paint);
                }
                b.Box(new Vector3(.34f,.26f,-.73f),new Vector3(1.35f,.16f,.12f),Steel);
                b.Box(new Vector3(.95f,.48f,0),new Vector3(.12f,.60f,1.10f),Dark);
                Tube(b,new Vector3(-.89f,1.58f,.34f),.10f,.70f,Dark);
                Badge(b,new Vector3(-.58f,1.33f,-.05f),.55f);
            }
            if(serviceScale>0)
            {
                // Service hardware stays inside the existing authored module footprint.
                // These fixed local ports/topology are not independent map placement controls.
                float width=.04f*serviceScale,height=.65f*serviceScale,floor=kind=="CargoModule"?.24f:.20f;
                float x=kind=="CargoModule"?-1.18f:1.08f;
                foreach(float z in new[]{-.60f,-.20f,.20f,.60f})b.Box(new Vector3(x,floor+.1f+height/2,z),new Vector3(width,height,width),Paint);
                foreach(float y in new[]{floor+.1f,floor+.1f+height})b.Box(new Vector3(x,y,0),new Vector3(width,width,1.24f),Steel);
                // Paired process pipes, saddles and a cable tray along the exposed base sill.
                foreach(float y in new[]{floor+.08f,floor+.20f})
                {
                    var pipe=new EnvironmentDetails.Builder();Tube(pipe,Vector3.zero,.03f*serviceScale,1.9f,Steel);var m=pipe.Mesh("Mine sill pipe");
                    b.Append(m,Matrix4x4.TRS(new Vector3(0,y,-.71f),Quaternion.Euler(0,0,90),Vector3.one));Object.Destroy(m);
                    foreach(float px in new[]{-.8f,.8f})b.Box(new Vector3(px,floor+.065f,-.71f),new Vector3(.12f,.13f,.08f),Dark);
                }
                for(int i=0;i<7;i++)b.Box(new Vector3(-.95f+i*.28f,floor+.05f,.72f),new Vector3(.04f,.05f,.04f),Steel);
                foreach(float z in new[]{.70f,.74f})b.Box(new Vector3(-.1f,floor+.08f,z),new Vector3(1.75f,.012f*serviceScale,.012f*serviceScale),Paint);
                // Retired side panel and disconnected equipment remain on the service sill.
                b.Box(new Vector3(-.88f,.38f,.70f),new Vector3(.46f,.42f*serviceScale,.035f),Dark);
                b.Box(new Vector3(.73f,floor+.08f,.72f),new Vector3(.35f,.13f*serviceScale,.04f),Casing);
            }
            return b.Mesh(kind);
        }
        public static Mesh Utility(string kind)
        {
            var b=new EnvironmentDetails.Builder();
            var original=EnvironmentDetails.Service(kind);
            b.Append(original,Matrix4x4.identity);Object.Destroy(original);
            float roof=kind=="PumpNode"?1.26f:kind=="CableCabinet"?1.81f:1.26f;
            b.Box(new Vector3(0,roof+.08f,0),new Vector3(1.3f,.13f,.85f),Dark);
            for(int i=0;i<5;i++)b.Box(new Vector3(-.48f+i*.24f,roof+.17f,0),new Vector3(.08f,.10f,.72f),Steel);
            Tube(b,new Vector3(.77f,roof+.25f,.43f),.11f,.52f,Steel);
            b.Box(new Vector3(.77f,roof+.52f,.43f),new Vector3(.32f,.10f,.32f),Dark);
            Badge(b,new Vector3(-.61f,roof+.23f,.43f),.43f);
            return b.Mesh("Detailed "+kind);
        }
        public static Mesh Compound(string kind="PerimeterCompound")
        {
            var b=new EnvironmentDetails.Builder();
            // Three authored building silhouettes: large maintenance hangar, medium
            // technical workshop and compact utility annex. Low roofs protect gameplay visibility.
            if(kind=="PerimeterCompound"||kind=="PerimeterHangar")
            {
            b.Box(new Vector3(-.78f,1.13f,-.87f),new Vector3(3.12f,2.20f,5.20f),Casing);
            var roof=new[]{new Vector3(-2.41f,2.27f,-3.57f),new Vector3(-.78f,3.02f,-3.57f),new Vector3(.85f,2.27f,-3.57f),new Vector3(-2.41f,2.27f,1.83f),new Vector3(-.78f,3.02f,1.83f),new Vector3(.85f,2.27f,1.83f)};
            b.Triangle(roof[0],roof[3],roof[4],Steel);b.Triangle(roof[0],roof[4],roof[1],Steel);
            b.Triangle(roof[1],roof[4],roof[5],Steel);b.Triangle(roof[1],roof[5],roof[2],Steel);
            b.Triangle(roof[0],roof[1],roof[2],Casing);b.Triangle(roof[3],roof[5],roof[4],Casing);
            b.Box(new Vector3(-.78f,1.03f,-3.50f),new Vector3(2.39f,1.96f,.10f),Dark);
            for(int i=0;i<8;i++)b.Box(new Vector3(-.78f,.18f+i*.24f,-3.57f),new Vector3(2.34f,.065f,.065f),Steel);
            foreach(float z in new[]{-2.85f,-1.7f,-.55f,.6f})b.Box(new Vector3(-2.39f,1.13f,z),new Vector3(.08f,2.28f,.12f),Paint);
            }
            if(kind=="PerimeterCompound"||kind=="PerimeterWorkshop")
            {
            b.Box(new Vector3(1.52f,.98f,1.24f),new Vector3(1.73f,1.90f,2.65f),Casing);
            b.Box(new Vector3(1.52f,1.98f,1.24f),new Vector3(1.87f,.12f,2.78f),Steel);
            for(int i=0;i<4;i++)b.Box(new Vector3(1.52f,2.09f,.45f+i*.48f),new Vector3(1.35f,.09f,.13f),Dark);
            b.Box(new Vector3(1.53f,.91f,-.11f),new Vector3(.73f,1.5f,.08f),Dark);
            Badge(b,new Vector3(1.52f,2.06f,2.13f),.66f);
            }
            if(kind=="PerimeterCompound"||kind=="PerimeterAnnex")
            {
            b.Box(new Vector3(-.82f,.54f,3.10f),new Vector3(1.65f,1.05f,1.46f),Paint);
            b.Box(new Vector3(-.82f,1.10f,3.10f),new Vector3(1.78f,.10f,1.60f),Steel);
            Tube(b,new Vector3(-1.23f,1.32f,3.24f),.10f,.50f,Dark);

            Badge(b,new Vector3(-.62f,1.18f,3.10f),.49f);
            }
            var mesh=b.Mesh(kind);if(kind!="PerimeterCompound")
            {
                var shift=new Vector3(mesh.bounds.center.x,0,mesh.bounds.center.z);var v=mesh.vertices;
                for(int i=0;i<v.Length;i++)v[i]-=shift;mesh.vertices=v;mesh.RecalculateBounds();
            }
            return mesh;
        }
        public static Mesh BridgeSteelwork(Spacewars.Simulation.MapSupport support,Spacewars.Simulation.ThreeCrossingsMap map,int requestedSide=0,ThreeCrossingsSurfaceProfile profile=null)
        {
            var b=new EnvironmentDetails.Builder();var deck=support.Bounds;
            void Beam(Vector3 a,Vector3 z,float width)
            {
                var kit=new EnvironmentDetails.Builder();kit.Box(Vector3.zero,new Vector3(width,(z-a).magnitude,width),Steel);
                var mesh=kit.Mesh("Steel member");b.Append(mesh,Matrix4x4.TRS((a+z)*.5f,Quaternion.FromToRotation(Vector3.up,z-a),Vector3.one));Object.Destroy(mesh);
            }
            float h=(float)map.RailHeight,w=(float)map.RailThickness*.15f;
            foreach(int side in new[]{-1,1})
            {
                if(requestedSide!=0&&requestedSide!=side)continue;
                float z=(float)(side<0?deck.MinZ:deck.MaxZ)+side*w;
                float start=(float)deck.MinX,end=(float)deck.MaxX;
                // Slim chord beneath the deck, two rails and open cross-braced panels.
                Beam(new Vector3(start,-.30f,z),new Vector3(end,-.30f,z),w*1.8f);
                foreach(float y in new[]{h*.18f,h})Beam(new Vector3(start,y,z),new Vector3(end,y,z),w);
                int panels=Mathf.CeilToInt((end-start)/(h*1.8f));
                for(int i=0;i<=panels;i++)
                {
                    float x=Mathf.Lerp(start,end,i/(float)panels);Beam(new Vector3(x,-.32f,z),new Vector3(x,h,z),w);
                    if(i==panels)continue;float next=Mathf.Lerp(start,end,(i+1)/(float)panels);
                    Beam(new Vector3(x,h*.18f,z),new Vector3(next,h,z),w*.65f);
                    Beam(new Vector3(x,h,z),new Vector3(next,h*.18f,z),w*.65f);
                }
            }
            if(profile!=null)
            foreach(int side in new[]{-1,1})
            {
                if(requestedSide!=0&&requestedSide!=side)continue;
                float z=(float)(side<0?deck.MinZ:deck.MaxZ)+side*profile.bridgeMemberWidth*1.6f;
                float start=(float)deck.MinX,end=(float)deck.MaxX,width=profile.bridgeMemberWidth;
                for(float x=start;x<=end+.001f;x+=profile.bridgeJointSpacing)
                {
                    b.Box(new Vector3(x,-.11f,z),new Vector3(width*4,width*2,width*2),Dark);
                    foreach(float dx in new[]{-width,width})b.Box(new Vector3(x+dx,.025f,z),new Vector3(width*.7f,.05f,width*.7f),Paint);
                    // All brace geometry is external to the driving corridor, beneath its slim edge.
                    Beam(new Vector3(x,-.43f,z),new Vector3(Mathf.Min(x+profile.bridgeJointSpacing,end),-.11f,z),width);
                }
            }
            return b.Mesh("Open metal bridge lattice");
        }
        public static float Radius(Mesh mesh,float scale)
        {
            float radius=0;foreach(var v in mesh.vertices)radius=Mathf.Max(radius,new Vector2(v.x,v.z).magnitude*scale);return radius;
        }
    }
}
