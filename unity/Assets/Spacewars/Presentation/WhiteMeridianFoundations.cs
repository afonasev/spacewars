using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
using UnityEngine;

namespace Spacewars.Presentation
{
    public sealed partial class PlayableWorld
    {
        private EnvironmentArtProfile meridianApronArt;
        private EnvironmentArtProfile SiteApronArt(bool light)
        {
            if(!light)return ApronArt;
            if(meridianApronArt==null){meridianApronArt=JsonUtility.FromJson<EnvironmentArtProfile>(JsonUtility.ToJson(ApronArt));meridianApronArt.apronConcreteValue=MeridianArt.slotValue;}
            return meridianApronArt;
        }
        private void CreateMeridianFoundation(WhiteMeridianMap map,TerritorySite site,WhiteMeridianArtProfile art)
        {
            float margin=site.Kind==PlayableBuildingKind.Outpost?art.outpostMargin:art.foundationMargin;
            // Static dressing only: no collider, support, site or navigation mutation.
            // Camera-facing slot rectangles fit within their circumcircle at every angle.
            var slot=ApronHalf(PlayableBuildingKind.Factory);float reach=slot.magnitude+margin;
            var center=ApronHalf(site.Kind);float halfX=center.magnitude+margin,halfZ=halfX;
            foreach(var s in site.Slots){halfX=Mathf.Max(halfX,Mathf.Abs((float)(s.Position.X-site.Position.X))+reach);halfZ=Mathf.Max(halfZ,Mathf.Abs((float)(s.Position.Z-site.Position.Z))+reach);}
            // Compact neutral-site slab. Building slots keep their original coordinates
            // and receive their own existing construction aprons when rendered.
            bool compact=site.Kind==PlayableBuildingKind.Outpost;
            if(compact){halfX*=art.outpostCoreRatio;halfZ*=art.outpostCoreRatio;}
            float cut=Mathf.Min(halfX,halfZ)*art.foundationCorner;
            Vector2[] Contour(float inset){float x=halfX-inset,z=halfZ-inset,c=Mathf.Max(0,cut-inset);return new[]{new Vector2(-x+c,-z),new Vector2(x-c,-z),new Vector2(x,-z+c),new Vector2(x,z-c),new Vector2(x-c,z),new Vector2(-x+c,z),new Vector2(-x,z-c),new Vector2(-x,-z+c)};}
            trailHardAreas.Add(new NavObstacle(new NavPolygon(Contour(0).Select(p=>new NavPoint(site.Position.X+p.x,site.Position.Z+p.y)))));
            void Face(string label,Vector2[] outline,float lift,Color color)
            {
                var vertices=new List<Vector3>();var triangles=new List<int>();
                foreach(var p in outline){var q=new NavPoint(site.Position.X+p.x,site.Position.Z+p.y);vertices.Add(new Vector3((float)q.X,(float)map.SurfaceHeight(q)+lift,(float)q.Z));}
                float area=0;for(int i=0;i<outline.Length;i++){var a=outline[i];var b=outline[(i+1)%outline.Length];area+=a.x*b.y-b.x*a.y;}
                for(int i=1;i<vertices.Count-1;i++){triangles.Add(0);if(area>=0){triangles.Add(i+1);triangles.Add(i);}else{triangles.Add(i);triangles.Add(i+1);}}
                var mesh=new Mesh{name=label};mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();Emit(label,mesh,3,color,new Vector4((float)site.Position.X,(float)site.Position.Z,halfX,halfZ));
            }
            // Millimetre offsets prevent depth conflict; all dressing stays below the existing apron datum.
            Face("Meridian foundation copper "+site.Id,Contour(0),.008f,new Color(.55f,.39f,.25f));
            float v=art.foundationValue;Face("Meridian foundation concrete "+site.Id,Contour(art.foundationTrim),.010f,new Color(v,v*.98f,v*.94f));
            // Normalized panel layout is authored content; seams terminate before the perimeter.
            foreach(float side in new[]{-1f,1f}){
                float x=halfX*side/3,z=halfZ*side/3,w=art.foundationTrim*.14f;
                var joint=new Color(v*.82f,v*.81f,v*.78f);
                Face("Meridian foundation joint",new[]{new Vector2(x-w,-halfZ+art.foundationTrim),new Vector2(x+w,-halfZ+art.foundationTrim),new Vector2(x+w,halfZ-art.foundationTrim),new Vector2(x-w,halfZ-art.foundationTrim)},.012f,joint);
                Face("Meridian foundation joint",new[]{new Vector2(-halfX+art.foundationTrim,z-w),new Vector2(halfX-art.foundationTrim,z-w),new Vector2(halfX-art.foundationTrim,z+w),new Vector2(-halfX+art.foundationTrim,z+w)},.012f,joint);
            }
            // Authored flush service panels stay at the perimeter; no props or colliders.
            void Rect(string label,float x,float z,float hx,float hz,float lift,Color color)=>Face(label,new[]{new Vector2(x-hx,z-hz),new Vector2(x+hx,z-hz),new Vector2(x+hx,z+hz),new Vector2(x-hx,z+hz)},lift,color);
            foreach(float side in new[]{-1f,1f}){
                float x=side*halfX*.82f,z=-side*halfZ*.42f;
                Rect("Meridian copper service strip",x,z,art.foundationTrim*2,halfZ*.3f,.015f,new Color(.48f,.35f,.25f));
                Rect("Meridian inset service hatch",x-side*halfX*.08f,z,halfX*.045f,halfZ*.10f,.017f,new Color(v*.7f,v*.72f,v*.72f));
                for(int slat=-3;slat<=3;slat++)Rect("Meridian service vent",x-side*halfX*.08f,z+slat*halfZ*.024f,halfX*.037f,art.foundationTrim*.2f,.019f,new Color(.26f,.29f,.28f));
                Rect("Meridian repaired slab",side*halfX*.55f,side*halfZ*.82f,halfX*.16f,halfZ*.065f,.014f,new Color(v*.94f,v*.95f,v*.94f));
            }
            void Overlay(string label,Vector2[] outline,float lift,Color color){
                var vertices=new List<Vector3>();var triangles=new List<int>();
                foreach(var p in outline){var q=new NavPoint(site.Position.X+p.x,site.Position.Z+p.y);vertices.Add(new Vector3((float)q.X,(float)map.SurfaceHeight(q)+lift,(float)q.Z));}
                float area=0;for(int i=0;i<outline.Length;i++){var a=outline[i];var b=outline[(i+1)%outline.Length];area+=a.x*b.y-b.x*a.y;}
                for(int i=1;i<vertices.Count-1;i++){triangles.Add(0);if(area>=0){triangles.Add(i+1);triangles.Add(i);}else{triangles.Add(i);triangles.Add(i+1);}}
                var mesh=new Mesh{name=label};mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();Emit(label,mesh,3,color,new Vector4((float)site.Position.X,(float)site.Position.Z,halfX,halfZ));
            }
            float wearLift=ApronArt.apronHeight+.012f;
            // Fine branching entry cracks and scuffs stay near the perimeter, clear of the center and slot aprons.
            Vector2 A=new Vector2(-halfX*.78f,halfZ*.34f),B=new Vector2(-halfX*.72f,halfZ*.29f),C=new Vector2(-halfX*.70f,halfZ*.25f),D=new Vector2(-halfX*.65f,halfZ*.23f);
            Color fracture=new Color(.49f,.45f,.39f);float crackWidth=.012f;
            void Crack(string label,Vector2 from,Vector2 to){
                var delta=(to-from).normalized*crackWidth;var cross=new Vector2(-delta.y,delta.x);var points=new[]{from+cross,to+cross,to-cross,from-cross};
                Overlay(label,points,wearLift,fracture);
            }
            Crack("Meridian foundation crack "+site.Id,A,B);Crack("Meridian foundation crack "+site.Id,B,C);Crack("Meridian foundation crack "+site.Id,C,D);
            Crack("Meridian foundation hairline "+site.Id,B,B+new Vector2(halfX*.06f,-halfZ*.04f));
        }
    }
}
