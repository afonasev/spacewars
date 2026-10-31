using UnityEngine;

namespace Spacewars.Presentation
{
    // Packing only: placement does not change the world target or stick-sector selection.
    public static class RadialViewportLayout
    {
        public const float Edge=12,Gap=6;
        public static Rect Fit(Rect preferred,Vector2 viewport,Rect? obstacle=null)
        {
            float maxX=Mathf.Max(Edge,viewport.x-preferred.width-Edge),maxY=Mathf.Max(Edge,viewport.y-preferred.height-Edge);
            var initial=new Rect(Mathf.Clamp(preferred.x,Edge,maxX),Mathf.Clamp(preferred.y,Edge,maxY),preferred.width,preferred.height);
            if(!obstacle.HasValue||!initial.Overlaps(obstacle.Value))return initial;
            var avoid=obstacle.Value;
            var xs=new[]{initial.x,Edge,maxX,avoid.xMin-preferred.width-Gap,avoid.xMax+Gap};
            var ys=new[]{initial.y,Edge,maxY,avoid.yMin-preferred.height-Gap,avoid.yMax+Gap};
            var result=initial;float distance=float.PositiveInfinity;
            foreach(float x in xs)foreach(float y in ys)
            {
                var next=new Rect(x,y,preferred.width,preferred.height);
                if(x<Edge||x>maxX||y<Edge||y>maxY||next.Overlaps(avoid))continue;
                float d=(next.position-preferred.position).sqrMagnitude;if(d<distance){distance=d;result=next;}
            }
            return result;
        }
    }
}
