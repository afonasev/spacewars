using System;
using UnityEngine;

namespace Spacewars.Presentation
{
    // Crossing-specific silhouettes. The three authored tiers share the same main
    // masses, ground contact, front access and local origin; only small fittings vary.
    public static class CrossingStartYardKit
    {
        static readonly Color Shell=new Color(.67f,.65f,.57f),Metal=new Color(.48f,.53f,.54f),Dark=new Color(.17f,.20f,.21f),Amber=new Color(.85f,.58f,.24f),Glass=new Color(.21f,.37f,.40f);
        public static Mesh Build(string kind,int tier=0)
        {
            if(tier<0||tier>2)throw new ArgumentOutOfRangeException(nameof(tier));
            if(CrossingIndustrialFleet.IsVehicle(kind))return CrossingIndustrialFleet.Build(kind,tier);
            var b=new EnvironmentDetails.Builder();
            void Box(float x,float y,float z,float w,float h,float d,Color c)=>b.Box(new Vector3(x,y,z),new Vector3(w,h,d),c);
            void Beam(Vector3 a,Vector3 z,float width,Color c)
            {
                var local=new EnvironmentDetails.Builder();local.Box(Vector3.zero,new Vector3(width,(z-a).magnitude,width),c);
                var mesh=local.Mesh("Yard member");b.Append(mesh,Matrix4x4.TRS((a+z)*.5f,Quaternion.FromToRotation(Vector3.up,z-a),Vector3.one));UnityEngine.Object.Destroy(mesh);
            }
            if(kind=="HeavyTractorWreck")
            {
                // Broad low track pods, rear glazed cab and forward blade read as a
                // parked quarry dozer even when its tread links are subpixel.
                foreach(int side in new[]{-1,1})
                {
                    Box(side*1.12f,.48f,0,.72f,.92f,4.1f,Dark);
                    Box(side*1.12f,.94f,0,.86f,.14f,4.25f,Metal);
                    if(tier<2)for(int i=0;i<(tier==0?12:6);i++)
                    {
                        float z=-1.85f+i*3.7f/(tier==0?11:5);
                        Box(side*1.12f,.08f,z,.75f,.12f,.14f,Metal);
                        Box(side*1.50f,.48f,z,.035f,.68f,.11f,Metal);
                    }
                    Beam(new Vector3(side*.91f,.67f,.5f),new Vector3(side*1.25f,.6f,2.5f),.16f,Metal);
                }
                Box(0,1.04f,0,1.95f,.63f,3.65f,Amber);
                Box(0,1.63f,.87f,1.65f,.65f,1.65f,Shell);
                Box(0,1.84f,-.78f,1.65f,1.24f,1.65f,Dark);
                Box(0,1.98f,-.78f,1.7f,.72f,1.7f,Glass);
                foreach(float x in new[]{-.84f,.84f})foreach(float z in new[]{-1.63f,.07f})Box(x,2,z,.11f,1.10f,.11f,Shell);
                Box(0,2.51f,-.78f,1.94f,.19f,1.95f,Amber);
                Box(0,.69f,2.61f,3.55f,1.18f,.23f,Metal);
                Box(0,.14f,2.75f,3.66f,.19f,.34f,Shell);
                if(tier<2)
                {
                    Box(.64f,2.21f,.81f,.16f,1.0f,.16f,Dark);
                    Box(0,1.61f,1.71f,1.30f,.42f,.04f,Dark);
                    for(int i=0;i<(tier==0?7:3);i++)Box(-.53f+i*1.06f/(tier==0?6:2),1.61f,1.75f,.055f,.39f,.04f,Metal);
                    if(tier==0)foreach(int side in new[]{-1,1})Box(side*.68f,1.94f,1.65f,.23f,.17f,.12f,Shell);
                }
            }
            else if(kind.StartsWith("Perimeter"))
            {
                bool hangar=kind=="PerimeterHangar",workshop=kind=="PerimeterWorkshop";
                float w=hangar?3.26f:workshop?1.87f:1.78f,d=hangar?5.4f:workshop?2.78f:1.6f,h=hangar?2.2f:workshop?1.9f:1.05f;
                Box(0,h/2,0,w-.16f,h,d-.16f,Shell);
                if(hangar)
                {
                    float y=h+.08f,rise=.74f;
                    var a=new Vector3(-w/2,y,-d/2);var c=new Vector3(w/2,y,-d/2);var peak=new Vector3(0,y+rise,-d/2);var back=Vector3.forward*d;
                    b.Triangle(a,a+back,peak+back,Metal);b.Triangle(a,peak+back,peak,Metal);
                    b.Triangle(peak,peak+back,c+back,Metal);b.Triangle(peak,c+back,c,Metal);
                    b.Triangle(a,peak,c,Shell);b.Triangle(a+back,c+back,peak+back,Shell);
                }
                else Box(0,h+.06f,0,w,.12f,d,Metal);
                // Shutter remains at the same negative-Z entrance used by access planning.
                Box(0,h*.44f,-d/2+.03f,w*.73f,h*.83f,.09f,Dark);
                Box(0,h*.92f,-d/2+.18f,w*.9f,.13f,.48f,Amber);
                foreach(int side in new[]{-1,1})Box(side*w*.43f,h*.46f,-d/2+.07f,.10f,h*.88f,.12f,Metal);
                if(tier<2)
                {
                    int count=tier==0?8:4;
                    for(int i=0;i<count;i++)Box(0,.12f+i*h*.72f/(count-1),-d/2-.025f,w*.70f,.055f,.025f,Metal);
                    Box(w*.30f,h+.17f,d*.25f,w*.28f,.22f,d*.25f,Dark);
                    if(tier==0)foreach(int side in new[]{-1,1})for(int i=0;i<4;i++)Box(side*(w/2-.055f),h*.5f,-d*.32f+i*d*.21f,.05f,h,.10f,Metal);
                }
            }
            else if(kind=="CargoModule")
            {
                Box(0,.11f,0,2.5f,.22f,1.5f,Dark);Box(0,.78f,0,2.3f,1.15f,1.35f,Shell);Box(0,1.40f,0,2.44f,.12f,1.46f,Metal);
                foreach(int side in new[]{-1,1})Box(side*1.12f,.8f,-.70f,.14f,1.2f,.09f,Amber);
                Box(0,.79f,-.7f,.05f,1.10f,.04f,Dark);
                if(tier<2)for(int i=0;i<(tier==0?9:4);i++)Box(-.98f+i*1.96f/(tier==0?8:3),.8f,.70f,.055f,1.0f,.04f,Metal);
            }
            else if(kind=="ExtractionSkid")
            {
                Box(0,.12f,0,2.35f,.24f,1.55f,Dark);Box(-.57f,.67f,0,.94f,1.10f,1.25f,Shell);
                foreach(float z in new[]{-.58f,.58f})
                {
                    Beam(new Vector3(.03f,.24f,z),new Vector3(.46f,1.83f,z),.13f,Amber);
                    Beam(new Vector3(1.06f,.24f,z),new Vector3(.46f,1.83f,z),.13f,Amber);
                }
                Box(.46f,1.86f,0,.40f,.23f,1.36f,Metal);Box(.46f,.98f,0,.30f,1.58f,.30f,Dark);
                Box(-.57f,1.28f,0,1.02f,.12f,1.34f,Metal);
                if(tier<2){Box(-.57f,.69f,-.65f,.70f,.60f,.04f,Dark);for(int i=0;i<(tier==0?6:3);i++)Box(-.57f,.43f+i*.50f/(tier==0?5:2),-.69f,.68f,.04f,.035f,Metal);}
            }
            else
            {
                Box(0,.11f,0,1.8f,.22f,1.35f,Dark);Box(0,.8f,0,1.50f,1.2f,1.13f,Shell);Box(0,1.46f,0,1.67f,.14f,1.27f,Metal);
                Box(0,.83f,-.59f,1.15f,.85f,.05f,Dark);Box(.53f,1.53f,.32f,.25f,.14f,.25f,Amber);
                if(tier<2)for(int i=0;i<(tier==0?7:3);i++)Box(0,.48f+i*.7f/(tier==0?6:2),-.63f,1.10f,.055f,.04f,Metal);
            }
            var result=b.Mesh("Crossing yard "+kind+" LOD"+tier);
            if(kind=="HeavyTractorWreck")
            {
                // Fixed material IDs in vertex alpha: paint, steel, rubber, glass.
                // Each Builder.Box/Beam face is two triangles (six vertices), before
                // lossless indexing. Metric face UVs anchor wear to real panel edges.
                var colors=result.colors;var vertices=result.vertices;var uv=new System.Collections.Generic.List<Vector4>(vertices.Length);
                for(int i=0;i<vertices.Length;i+=6)
                {
                    float w=Vector3.Distance(vertices[i],vertices[i+5]),h=Vector3.Distance(vertices[i],vertices[i+1]);
                    uv.Add(new Vector4(0,0,w,h));uv.Add(new Vector4(0,h,w,h));uv.Add(new Vector4(w,h,w,h));
                    uv.Add(new Vector4(0,0,w,h));uv.Add(new Vector4(w,h,w,h));uv.Add(new Vector4(w,0,w,h));
                }
                for(int i=0;i<colors.Length;i++){var c=colors[i];c.a=c==Metal?1f/3:c==Dark?2f/3:c==Glass?1:0;colors[i]=c;}
                result.colors=colors;result.SetUVs(0,uv);
            }
            FinalProceduralMesh.IndexOwned(result);return result;
        }
    }
}
