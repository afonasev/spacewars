using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Spacewars.Presentation
{
    // A shared overlay follows viewport boundaries; the minimap frame is an explicit exclusion.
    public sealed class SplitScreenDividers : VisualElement
    {
        readonly SplitScreenHudProfile profile=SplitScreenHudProfile.Load();
        readonly List<Rect> segments=new List<Rect>();
        int count;Rect? excluded;Vector2 measured;
        public IReadOnlyList<Rect> PaintedSegments=>segments;
        public SplitScreenDividers()
        {
            name="ordinary-viewport-dividers";pickingMode=PickingMode.Ignore;
            style.position=Position.Absolute;style.left=style.right=style.top=style.bottom=0;
            RegisterCallback<GeometryChangedEvent>(_=>Rebuild());
            generateVisualContent+=ctx=>{var p=ctx.painter2D;p.fillColor=OrbitalTheme.Muted;foreach(var r in segments){p.BeginPath();p.MoveTo(new Vector2(r.xMin,r.yMin));p.LineTo(new Vector2(r.xMax,r.yMin));p.LineTo(new Vector2(r.xMax,r.yMax));p.LineTo(new Vector2(r.xMin,r.yMax));p.ClosePath();p.Fill();}};
        }
        public void Set(int players,Rect? minimap)
        {
            if(count==players&&excluded==minimap&&measured==contentRect.size)return;
            count=players;excluded=minimap;Rebuild();
        }
        void Rebuild()
        {
            measured=contentRect.size;segments.Clear();
            if(count>=2&&measured.x>0&&measured.y>0)
            {
                Rect? cut=excluded;
                if(cut.HasValue){var r=cut.Value;r.xMin-=profile.minimapGap;r.xMax+=profile.minimapGap;r.yMin-=profile.minimapGap;r.yMax+=profile.minimapGap;cut=r;}
                // Half-screen boundaries are the topology of LocalRect, not tuneable spacing.
                Add(new Rect(measured.x/2-profile.thickness/2,0,profile.thickness,measured.y),cut,true);
                if(count>=3)Add(new Rect(0,measured.y/2-profile.thickness/2,measured.x,profile.thickness),cut,false);
            }
            MarkDirtyRepaint();
        }
        void Add(Rect line,Rect? cut,bool vertical)
        {
            if(!cut.HasValue||!line.Overlaps(cut.Value)){segments.Add(line);return;}
            var r=cut.Value;
            if(vertical)
            {
                float a=Mathf.Clamp(r.yMin,line.yMin,line.yMax),b=Mathf.Clamp(r.yMax,line.yMin,line.yMax);
                if(a>line.yMin)segments.Add(new Rect(line.x,line.yMin,line.width,a-line.yMin));
                if(b<line.yMax)segments.Add(new Rect(line.x,b,line.width,line.yMax-b));
            }
            else
            {
                float a=Mathf.Clamp(r.xMin,line.xMin,line.xMax),b=Mathf.Clamp(r.xMax,line.xMin,line.xMax);
                if(a>line.xMin)segments.Add(new Rect(line.xMin,line.y,a-line.xMin,line.height));
                if(b<line.xMax)segments.Add(new Rect(b,line.y,line.xMax-b,line.height));
            }
        }
    }
}
