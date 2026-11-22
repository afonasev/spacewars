using System.Collections.Generic;
using Spacewars.Simulation;
using UnityEngine;

namespace Spacewars.Presentation
{
    // Normalized seams, stencils and service-panel layout are authored visual geometry.
    // Designer-controlled dimensions/brightness live in the Environment Art profile.
    public static class ConstructionApronMesh
    {
        // The authored technical pad is 1.41 m wide before scale and apron margin.
        public static Vector2 DefenseSupportHalf(PlayableProfile profile,EnvironmentArtProfile art)
            =>Vector2.one*(.705f*(float)profile.TurretModelScale+art.apronMargin);

        public static List<Vector3> CapturePolygon(Vector2 half,EnvironmentArtProfile art,float progress)
        {
            var result=new List<Vector3>();progress=Mathf.Clamp01(progress);if(progress<=0)return result;
            float w=Mathf.Min(art.apronMarkWidth,Mathf.Min(half.x,half.y)*.09f),trim=w*1.7f;
            float x=half.x-trim,z=half.y-trim,cut=Mathf.Min(trim,Mathf.Min(half.x,half.y)*.15f);
            var contour=new[]{new Vector3(-x+cut,0,-z),new Vector3(x-cut,0,-z),new Vector3(x,0,-z+cut),new Vector3(x,0,z-cut),new Vector3(x-cut,0,z),new Vector3(-x+cut,0,z),new Vector3(-x,0,z-cut),new Vector3(-x,0,-z+cut)};
            float front=Mathf.Lerp(-z,z,progress);
            var from=contour[contour.Length-1];
            foreach(var to in contour)
            {
                bool fromInside=from.z<=front,toInside=to.z<=front;
                if(fromInside!=toInside)result.Add(Vector3.Lerp(from,to,(front-from.z)/(to.z-from.z)));
                if(toInside)result.Add(to);
                from=to;
            }
            return result;
        }

        internal const float MineCurbHeight=.21f;

        public static Mesh Build(Vector2 half,EnvironmentArtProfile art,bool mineOpening=false)
        {
            var vertices=new List<Vector3>();var colors=new List<Color>();var uv=new List<Vector2>();var triangles=new List<int>();
            float h=art.apronHeight,x=half.x,z=half.y,w=Mathf.Min(art.apronMarkWidth,Mathf.Min(x,z)*.09f);
            var metal=new Color(.19f,.23f,.25f,1);var seam=new Color(.34f,.36f,.35f,1);
            var white=new Color(.62f,.64f,.61f,1);var yellow=new Color(.70f,.48f,.14f,1);
            void Polygon(Vector2[] points,float y,Color color)
            {
                int offset=vertices.Count;
                foreach(var p in points){vertices.Add(new Vector3(p.x,y,p.y));colors.Add(color);uv.Add(p/art.concreteTile);}
                for(int i=1;i<points.Length-1;i++){triangles.Add(offset);triangles.Add(offset+i+1);triangles.Add(offset+i);}
            }
            Vector2[] Chamfer(float a,float b,float cut)=>new[]{new Vector2(-a+cut,-b),new Vector2(a-cut,-b),new Vector2(a,-b+cut),new Vector2(a,b-cut),new Vector2(a-cut,b),new Vector2(-a+cut,b),new Vector2(-a,b-cut),new Vector2(-a,-b+cut)};
            void Rect(float a,float b,float c,float d,float y,Color color)=>Polygon(new[]{new Vector2(a,b),new Vector2(c,b),new Vector2(c,d),new Vector2(a,d)},y,color);
            float trim=w*1.7f,cut=Mathf.Min(w*1.7f,Mathf.Min(x,z)*.15f);
            if(mineOpening){
                // The lower bed sits above the permanent yard. Its sloped lining
                // and raised curb belong to the empty-site mesh, hidden on build.
                var lip=Chamfer(x,z,cut);
                var mouth=Chamfer(x*.76f,z*.72f,cut);
                var bed=Chamfer(x*.56f,z*.49f,cut*.7f);
                float top=h+MineCurbHeight;
                Polygon(bed,h+.003f,new Color(.09f,.085f,.08f,1));
                void Band(Vector2[] outer,float high,Vector2[] inner,float low,Color tint){
                    for(int i=0;i<outer.Length;i++){
                        int next=(i+1)%outer.Length;
                        int offset=vertices.Count;
                        foreach(var v in new[]{outer[i],outer[next],inner[next],inner[i]})uv.Add(v/art.concreteTile);
                        vertices.Add(new Vector3(outer[i].x,high,outer[i].y));
                        vertices.Add(new Vector3(outer[next].x,high,outer[next].y));
                        vertices.Add(new Vector3(inner[next].x,low,inner[next].y));
                        vertices.Add(new Vector3(inner[i].x,low,inner[i].y));
                        for(int k=0;k<4;k++)colors.Add(tint);
                        triangles.AddRange(new[]{offset,offset+2,offset+1,offset,offset+3,offset+2});
                    }
                }
                Band(Chamfer(x+.05f,z+.05f,cut),h,lip,top,metal);
                Band(lip,top,mouth,top,new Color(.52f,.48f,.40f,1));
                Band(mouth,top,bed,h+.004f,new Color(.25f,.235f,.21f,1));
                for(int i=-1;i<=1;i++)
                    Rect(-x*.49f,i*z*.22f-.04f,x*.49f,i*z*.22f+.04f,h+.009f,metal);
                for(int side=-1;side<=1;side+=2){
                    float rail=side*x*.85f;
                    Rect(rail-.08f,-z*.66f,rail+.08f,z*.77f,top+.004f,white);
                }
                var opening=new Mesh{name="Lunar loading shaft"};
                opening.SetVertices(vertices);opening.SetColors(colors);opening.SetUVs(0,uv);
                opening.SetTriangles(triangles,0);opening.RecalculateNormals();opening.RecalculateBounds();
                return opening;
            }
            Polygon(Chamfer(x,z,cut),h,metal);
            float value=art.apronConcreteValue;
            Polygon(Chamfer(x-trim,z-trim,cut),h+.001f,new Color(value,value,value,0));
            // Panel joints remain quiet under a building, while the apron remains visible at the edge.
            foreach(float side in new[]{-1f,1f})
            {
                float px=side*x/3,pz=side*z/3;
                Rect(px-w*.15f,-z+trim,px+w*.15f,z-trim,h+.002f,seam);
                Rect(-x+trim,pz-w*.15f,x-trim,pz+w*.15f,h+.002f,seam);
            }
            for(int side=-1;side<=1;side+=2)
            {
                for(int i=0;i<6;i++)
                {
                    float px=Mathf.Lerp(-x*.68f,x*.68f,i/5f),pz=Mathf.Lerp(-z*.68f,z*.68f,i/5f);
                    Color paint=i%3==0?yellow:white;
                    Rect(px-x*.06f,side*(z-trim*2)-w/2,px+x*.06f,side*(z-trim*2)+w/2,h+.004f,paint);
                    Rect(side*(x-trim*2)-w/2,pz-z*.06f,side*(x-trim*2)+w/2,pz+z*.06f,h+.004f,paint);
                }
                for(int other=-1;other<=1;other+=2)
                {
                    // Flush hatch and grille occupy the exposed service margin, never a collider.
                    float hx=side*x*.43f,hz=other*(z-trim*4);
                    Rect(hx-w*2,hz-w*1.1f,hx+w*2,hz+w*1.1f,h+.003f,metal);
                    Rect(hx-w*1.6f,hz-w*.7f,hx+w*1.6f,hz+w*.7f,h+.004f,seam);
                    for(int slot=0;slot<4;slot++)Rect(hx-w*1.3f+slot*w*.65f,hz-w*.6f,hx-w*1.1f+slot*w*.65f,hz+w*.6f,h+.005f,metal);
                }
            }
            // Center registration cross is covered naturally by the building, rather than hidden in code.
            Rect(-w*2,-w*.25f,w*2,w*.25f,h+.004f,seam);Rect(-w*.25f,-w*2,w*.25f,w*2,h+.004f,seam);
            var mesh=new Mesh{name="Technical construction apron"};mesh.SetVertices(vertices);mesh.SetColors(colors);mesh.SetUVs(0,uv);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
        }
    }
}
