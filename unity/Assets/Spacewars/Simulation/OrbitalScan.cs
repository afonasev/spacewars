using System;
using System.Collections.Generic;
using System.Linq;

namespace Spacewars.Simulation
{
    // Only already clipped draw fragments cross the perspective boundary. In particular,
    // an enemy never receives an unseen scan centre or a list of hidden contacts.
    public readonly struct OrbitalScanLine
    {
        public readonly NavPoint From,To;public readonly double Opacity;
        public OrbitalScanLine(NavPoint from,NavPoint to,double opacity){From=from;To=to;Opacity=opacity;}
    }
    public readonly struct OrbitalScanTriangle
    {
        public readonly NavPoint A,B,C;public readonly double Opacity,Highlight,OpacityB,HighlightB,OpacityC,HighlightC;
        public OrbitalScanTriangle(NavPoint a,NavPoint b,NavPoint c,double opacity,double highlight=0):this(a,b,c,opacity,highlight,opacity,highlight,opacity,highlight){}
        public OrbitalScanTriangle(NavPoint a,NavPoint b,NavPoint c,double opacity,double highlight,double opacityB,double highlightB,double opacityC,double highlightC){A=a;B=b;C=c;Opacity=opacity;Highlight=highlight;OpacityB=opacityB;HighlightB=highlightB;OpacityC=opacityC;HighlightC=highlightC;}
    }
    public static class OrbitalScanVisibility
    {
        // A circle is convex: containment in the SAME source proves the whole face visible.
        // Union-only vertex tests could bridge a hidden gap between separate sources.
        public static bool Covered(NavPoint a,NavPoint b,NavPoint c,IReadOnlyList<VisionSource> sources)
        {
            foreach(var source in sources){
                bool Inside(NavPoint p){double x=p.X-source.Position.X,z=p.Z-source.Position.Z;return x*x+z*z<=source.Radius*source.Radius+1e-9;}
                if(Inside(a)&&Inside(b)&&Inside(c))return true;
            }
            return false;
        }
        public static IEnumerable<OrbitalScanLine> Clip(NavPoint a,NavPoint b,double opacity,IReadOnlyList<VisionSource> sources)
        {
            double dx=b.X-a.X,dz=b.Z-a.Z,aa=dx*dx+dz*dz;if(aa<1e-18)yield break;
            var intervals=new List<(double lo,double hi)>();
            foreach(var source in sources){
                double x=a.X-source.Position.X,z=a.Z-source.Position.Z,bb=2*(x*dx+z*dz),cc=x*x+z*z-source.Radius*source.Radius,disc=bb*bb-4*aa*cc;
                if(disc<0)continue;double root=Math.Sqrt(disc),lo=Math.Max(0,(-bb-root)/(2*aa)),hi=Math.Min(1,(-bb+root)/(2*aa));
                if(hi>lo)intervals.Add((lo,hi));
            }
            if(intervals.Count==0)yield break;
            var sorted=intervals.OrderBy(i=>i.lo).ToArray();double left=sorted[0].lo,right=sorted[0].hi;
            for(int i=1;i<=sorted.Length;i++){
                if(i<sorted.Length&&sorted[i].lo<=right){right=Math.Max(right,sorted[i].hi);continue;}
                yield return new OrbitalScanLine(new NavPoint(a.X+dx*left,a.Z+dz*left),new NavPoint(a.X+dx*right,a.Z+dz*right),opacity);
                if(i<sorted.Length){left=sorted[i].lo;right=sorted[i].hi;}
            }
        }
    }
}
