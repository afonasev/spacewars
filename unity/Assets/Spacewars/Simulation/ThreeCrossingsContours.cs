using System;
using System.Collections.Generic;
using System.Linq;
namespace Spacewars.Simulation
{
    [Serializable] public sealed class MapVertexData
    {public string id;public double x,z;public MapVertexData(string id,double x,double z){this.id=id;this.x=x;this.z=z;}}
    [Serializable] public sealed class MapContourData{public string id;public MapVertexData[] vertices;}
    public sealed partial class ThreeCrossingsProfileData
    {
        public double platformRadius=11.5,platformChamfer=3,platformHeight=2,rampLength=5,rampWidth=8,platformWallWidth=.6,platformWallHeight=.8;
        public double rockHeight=5,rockCrownHeight=3,rockCrownInset=.8;
        public MapContourData[] contours=DefaultContours();
        public MapVertexData[] riverBends=new[]{new MapVertexData("south-mouth",-10,-56),new MapVertexData("south-upper",-10,-27),new MapVertexData("south-middle",-11,-22),new MapVertexData("south-lower",-9,-17),new MapVertexData("north-lower",-9,17),new MapVertexData("north-middle",-11,22),new MapVertexData("north-upper",-10,27),new MapVertexData("north-mouth",-10,56)};
        private static MapContourData[] DefaultContours()=>new[]{
            // Profile-owned continuous silhouettes; boundary masses have no overlapping coplanar pieces.
            new MapContourData{ id="west-boundary-mass",vertices=new[]{new MapVertexData("v00",-64,-64),new MapVertexData("v01",-10,-64),new MapVertexData("v02",-10,-57),new MapVertexData("v03",-9,-51),new MapVertexData("v04",-8,-43),new MapVertexData("v05",-10,-43),new MapVertexData("v06",-13,-52),new MapVertexData("v07",-14,-51),new MapVertexData("v08",-18,-53),new MapVertexData("v09",-21,-59),new MapVertexData("v10",-27,-61),new MapVertexData("v11",-34,-60),new MapVertexData("v12",-40,-61),new MapVertexData("v13",-47,-60),new MapVertexData("v14",-54,-61),new MapVertexData("v15",-59,-57),new MapVertexData("v16",-61,-51),new MapVertexData("v17",-60,-41),new MapVertexData("v18",-58,-36),new MapVertexData("v19",-59,-29),new MapVertexData("v20",-57,-22),new MapVertexData("v21",-58,-17),new MapVertexData("v22",-54,-17),new MapVertexData("v23",-50,-15),new MapVertexData("v24",-47,-16),new MapVertexData("v25",-46,-13),new MapVertexData("v26",-49,-10),new MapVertexData("v27",-51,-4),new MapVertexData("v28",-50,3),new MapVertexData("v29",-49,10),new MapVertexData("v30",-47,14),new MapVertexData("v31",-50,17),new MapVertexData("v32",-55,19),new MapVertexData("v33",-56,21),new MapVertexData("v34",-60,29),new MapVertexData("v35",-58,36),new MapVertexData("v36",-60,43),new MapVertexData("v37",-59,53),new MapVertexData("v38",-59,57),new MapVertexData("v39",-54,61),new MapVertexData("v40",-47,60),new MapVertexData("v41",-40,61),new MapVertexData("v42",-34,60),new MapVertexData("v43",-27,61),new MapVertexData("v44",-21,59),new MapVertexData("v45",-18,53),new MapVertexData("v46",-14,51),new MapVertexData("v47",-13,52),new MapVertexData("v48",-10,43),new MapVertexData("v49",-8,43),new MapVertexData("v50",-9,51),new MapVertexData("v51",-10,57),new MapVertexData("v52",-10,64),new MapVertexData("v53",-64,64)}},
            new MapContourData{ id="west-pocket-inner",vertices=new[]{new MapVertexData("v00",-34,-16),new MapVertexData("v01",-30,-13),new MapVertexData("v02",-27,-7),new MapVertexData("v03",-26,0),new MapVertexData("v04",-28,8),new MapVertexData("v05",-31,14),new MapVertexData("v06",-35,16),new MapVertexData("v07",-35,12),new MapVertexData("v08",-32,7),new MapVertexData("v09",-31,0),new MapVertexData("v10",-32,-7),new MapVertexData("v11",-35,-12)}},
            new MapContourData{ id="west-north-island",vertices=new[]{new MapVertexData("v00",-33,21),new MapVertexData("v01",-28,19),new MapVertexData("v02",-24,22),new MapVertexData("v03",-23,27),new MapVertexData("v04",-26,31),new MapVertexData("v05",-30,33),new MapVertexData("v06",-33,29),new MapVertexData("v07",-34,25)}},
            new MapContourData{ id="west-south-island",vertices=new[]{new MapVertexData("v00",-34,-33),new MapVertexData("v01",-29,-35),new MapVertexData("v02",-24,-32),new MapVertexData("v03",-23,-28),new MapVertexData("v04",-27,-24),new MapVertexData("v05",-31,-23),new MapVertexData("v06",-34,-26),new MapVertexData("v07",-36,-30)}},
        };
    }
    public sealed class MapVertexField
    {
        private readonly MapVertexData vertex;private readonly bool x;
        public MapVertexField(string contour,MapVertexData vertex,bool x){this.vertex=vertex;this.x=x;Path="maps.threeCrossings.contours."+contour+"."+vertex.id+"."+(x?"x":"z");Group=contour;Label=vertex.id+" "+(x?"X":"Z");}
        public string Path{get;} public string Group{get;} public string Label{get;} public string Description=>"Authoritative contour coordinate; affects visible silhouette, movement and cover.";
        public string Unit=>"m";public double Minimum=>-96;public double Maximum=>96;public double Step=>.25;
        public double Read()=>x?vertex.x:vertex.z;public void Write(double value){if(x)vertex.x=value;else vertex.z=value;}
    }
    public sealed partial class ThreeCrossingsMap
    {
        public static IEnumerable<MapVertexField> ContourFields(ThreeCrossingsProfileData d)
        {foreach(var c in d.contours.Concat(new[]{new MapContourData{id="river-shore",vertices=d.riverBends}}))foreach(var v in c.vertices){yield return new MapVertexField(c.id,v,true);yield return new MapVertexField(c.id,v,false);}}
        private void BuildContours(ThreeCrossingsProfileData d,out List<MapSupport> supports,out List<NavObstacle> water,out List<NavObstacle> solids)
        {
            supports=new List<MapSupport>();water=new List<NavObstacle>();solids=new List<NavObstacle>();
            if(d.riverBends==null||d.riverBends.Length!=8)throw new ArgumentException("Expected eight river bends");
            if(d.contours==null||d.contours.Length==0||d.contours.Select(c=>c.id).Distinct().Count()!=d.contours.Length)throw new ArgumentException("Missing or duplicate authored contour IDs");
            foreach(var c in d.contours)if(c.vertices==null||c.vertices.Select(v=>v.id).Distinct().Count()!=c.vertices.Length)throw new ArgumentException("Invalid stable vertex IDs");
            foreach(var f in ContourFields(d)){double v=f.Read();if(double.IsNaN(v)||double.IsInfinity(v)||v<f.Minimum||v>f.Maximum)throw new ArgumentException("Invalid contour coordinate: "+f.Path);}
            if(d.platformChamfer>=d.platformRadius||d.rampWidth>=2*(d.platformRadius-d.platformChamfer)||d.platformHeight/d.rampLength>1)throw new ArgumentException("Invalid platform/ramp geometry");
            // Profile contours are authored in the reference 128 m composition; map extent scales that content.
            double scale=HalfExtent/64;
            foreach(var c in d.contours)foreach(int rotation in new[]{1,-1})
            {
                var polygon=new NavPolygon(c.vertices.Select(v=>new NavPoint(rotation*v.x*scale,rotation*v.z*scale)));
                solids.Add(new NavObstacle(polygon,-WaterDepth,d.rockHeight));
                // A continuous inset crown keeps the large rock mass coherent; it is itself authoritative solid geometry.
                NavPolygon crown=null;double inset=d.rockCrownInset;
                for(int attempt=0;attempt<12&&crown==null;attempt++,inset/=2)
                {
                    var points=new List<NavPoint>();var v=polygon.Vertices;
                    for(int i=0;i<v.Count;i++){var a=v[(i+v.Count-1)%v.Count];var b=v[i];var next=v[(i+1)%v.Count];double ax=b.X-a.X,az=b.Z-a.Z,bx=next.X-b.X,bz=next.Z-b.Z,al=Math.Sqrt(ax*ax+az*az),bl=Math.Sqrt(bx*bx+bz*bz);double nx=-az/al-bz/bl,nz=ax/al+bx/bl,den=1+(ax*bx+az*bz)/(al*bl);points.Add(new NavPoint(b.X+inset*nx/den,b.Z+inset*nz/den));}
                    try{var candidate=new NavPolygon(points);if(polygon.StrictlyContains(candidate))crown=candidate;}catch(ArgumentException){ }
                }
                if(crown==null)throw new ArgumentException("Rock crown cannot fit authored contour "+c.id);
                solids.Add(new NavObstacle(crown,d.rockHeight,d.rockHeight+d.rockCrownHeight));
            }
            // The authored river is monotone. Bridge bands are exact full straight contact edges.
            if(d.riverBends==null||d.riverBends.Length!=8||d.riverBends.Select(v=>v.id).Distinct().Count()!=8)throw new ArgumentException("Expected eight stable river bends");
            NavPoint Bend(int i)=>new NavPoint(d.riverBends[i].x,d.riverBends[i].z);
            var west=new List<NavPoint>{new NavPoint(d.riverBends[0].x,-HalfExtent),Bend(0),
                new NavPoint(-RiverHalfWidth,-CrossingZ-SideBridgeWidth/2),new NavPoint(-RiverHalfWidth,-CrossingZ+SideBridgeWidth/2),Bend(1),Bend(2),Bend(3),
                new NavPoint(-CentralBridgeHalfSpan,-CentralBridgeWidth/2),new NavPoint(-CentralBridgeHalfSpan,CentralBridgeWidth/2),Bend(4),Bend(5),Bend(6),
                new NavPoint(-RiverHalfWidth,CrossingZ-SideBridgeWidth/2),new NavPoint(-RiverHalfWidth,CrossingZ+SideBridgeWidth/2),Bend(7),new NavPoint(d.riverBends[7].x,HalfExtent)};
            for(int i=0;i<west.Count;i++)if(west[i].X>=0||i>0&&west[i].Z<=west[i-1].Z)throw new ArgumentException("River bends must remain ordered between bridge contacts");
            // All material surface and water coordinates come from this one shore chain.
            var east=west.Select(p=>new NavPoint(-p.X,-p.Z)).Reverse().ToList();
            var banks=new[]{new NavPolygon(new[]{new NavPoint(-HalfExtent,-HalfExtent)}.Concat(west).Concat(new[]{new NavPoint(-HalfExtent,HalfExtent)})),
                new NavPolygon(new[]{new NavPoint(HalfExtent,HalfExtent)}.Concat(east.AsEnumerable().Reverse()).Concat(new[]{new NavPoint(HalfExtent,-HalfExtent)}))};
            foreach(var band in new[]{new[]{0,2},new[]{3,7},new[]{8,12},new[]{13,15}}){var pts=west.Skip(band[0]).Take(band[1]-band[0]+1).Concat(east.Skip(band[0]).Take(band[1]-band[0]+1).Reverse());water.Add(new NavObstacle(new NavPolygon(pts)));}
            foreach(int sx in new[]{-1,1})foreach(int sz in new[]{-1,1})BuildPlatform(d,new NavPoint(sx*BaseCoordinate,sz*BaseCoordinate),sx,sz,supports,solids);
            for(int i=0;i<2;i++)supports.Add(new MapSupport(i==0?"west-bank":"east-bank",new NavObstacle(banks[i]),false));
            foreach(double z in new[]{-CrossingZ,0,CrossingZ}){double width=z==0?CentralBridgeWidth:SideBridgeWidth,span=z==0?CentralBridgeHalfSpan:RiverHalfWidth;supports.Add(new MapSupport(z==0?"central-bridge":z>0?"north-bridge":"south-bridge",new NavObstacle(-span,z-width/2,span,z+width/2),true));}
        }
        private static void BuildPlatform(ThreeCrossingsProfileData d,NavPoint center,int sx,int sz,List<MapSupport> supports,List<NavObstacle> solids)
        {
            double r=d.platformRadius,c=d.platformChamfer,g=d.rampWidth/2,w=d.platformWallWidth;
            var local=new[]{new NavPoint(-r+c,-r),new NavPoint(r-c,-r),new NavPoint(r,-r+c),new NavPoint(r,r-c),new NavPoint(r-c,r),new NavPoint(-r+c,r),new NavPoint(-r,r-c),new NavPoint(-r,-r+c)};
            var polygon=new NavPolygon(local.Select(p=>new NavPoint(p.X+center.X,p.Z+center.Z)));
            string id="platform-"+sx+"-"+sz;supports.Add(new MapSupport(id,new NavObstacle(polygon),false,d.platformHeight));
            for(int i=0;i<local.Length;i++)
            {
                var a=local[i];var b=local[(i+1)%local.Length];bool xGate=a.X==b.X&&a.X==-sx*r,zGate=a.Z==b.Z&&a.Z==-sz*r;
                if(xGate){double lo=Math.Min(a.Z,b.Z),hi=Math.Max(a.Z,b.Z);Wall(new NavPoint(a.X,lo),new NavPoint(a.X,-g));Wall(new NavPoint(a.X,g),new NavPoint(a.X,hi));}
                else if(zGate){double lo=Math.Min(a.X,b.X),hi=Math.Max(a.X,b.X);Wall(new NavPoint(lo,a.Z),new NavPoint(-g,a.Z));Wall(new NavPoint(g,a.Z),new NavPoint(hi,a.Z));}
                else Wall(a,b);
            }
            foreach(bool alongX in new[]{true,false})
            {
                int sign=alongX?-sx:-sz;var inner=new NavPoint(center.X+(alongX?sign*r:0),center.Z+(alongX?0:sign*r));
                var outer=new NavPoint(inner.X+(alongX?sign*d.rampLength:0),inner.Z+(alongX?0:sign*d.rampLength));
                var footprint=alongX?new NavObstacle(Math.Min(inner.X,outer.X),center.Z-g,Math.Max(inner.X,outer.X),center.Z+g):new NavObstacle(center.X-g,Math.Min(inner.Z,outer.Z),center.X+g,Math.Max(inner.Z,outer.Z));
                var gradient=alongX?new NavPoint(-sign*d.platformHeight/d.rampLength,0):new NavPoint(0,-sign*d.platformHeight/d.rampLength);
                supports.Insert(0,new MapSupport(id+(alongX?"-east-west-ramp":"-north-south-ramp"),footprint,false,0,gradient,outer));
                foreach(int side in new[]{-1,1}){var a=new NavPoint(inner.X-center.X+(alongX?0:side*(g+w/2)),inner.Z-center.Z+(alongX?side*(g+w/2):0));var b=new NavPoint(outer.X-center.X+(alongX?0:side*(g+w/2)),outer.Z-center.Z+(alongX?side*(g+w/2):0));Wall(a,b);}
            }
            void Wall(NavPoint a,NavPoint b)
            {
                double dx=b.X-a.X,dz=b.Z-a.Z,l=Math.Sqrt(dx*dx+dz*dz);if(l<1e-6)return;double nx=-dz/l*w/2,nz=dx/l*w/2;
                var p=new NavPolygon(new[]{new NavPoint(center.X+a.X+nx,center.Z+a.Z+nz),new NavPoint(center.X+b.X+nx,center.Z+b.Z+nz),new NavPoint(center.X+b.X-nx,center.Z+b.Z-nz),new NavPoint(center.X+a.X-nx,center.Z+a.Z-nz)});
                solids.Add(new NavObstacle(p,0,d.platformHeight+d.platformWallHeight));
            }
        }
    }
}
