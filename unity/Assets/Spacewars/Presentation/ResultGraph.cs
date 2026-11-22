using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Runtime;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.UIElements;

namespace Spacewars.Presentation
{
    internal sealed class ResultGraph:VisualElement
    {
        internal const int EventIconSize=48;
        private const int StackFrameInset=2;

        internal sealed class Line
        {
            internal readonly PlayableOwner Owner;internal readonly int Slot;internal readonly Color Color;internal readonly List<Vector2> Points;
            internal Line(PlayableOwner owner,int slot,Color color,List<Vector2> points){Owner=owner;Slot=slot;Color=color;Points=points;}
            private int cachedColumns;
            private List<Vector2> overview;

            internal double Value(double seconds)
            {
                int lo=0,hi=Points.Count;
                while(lo<hi)
                {
                    int mid=(lo+hi)/2;
                    if(Points[mid].x<=seconds+1e-6)lo=mid+1;else hi=mid;
                }
                return lo==0?0:Points[lo-1].y;
            }

            internal List<Vector2> Overview(int columns,double duration)
            {
                if(Points.Count<=columns)return Points;
                if(cachedColumns==columns)return overview;
                cachedColumns=columns;
                overview=new List<Vector2>();
                int Column(Vector2 point)=>(int)(point.x/duration*columns);
                for(int first=0;first<Points.Count;)
                {
                    int column=Column(Points[first]);
                    int min=first,max=first,next=first+1;
                    while(next<Points.Count&&Column(Points[next])==column)
                    {
                        if(Points[next].y<Points[min].y)min=next;
                        if(Points[next].y>Points[max].y)max=next;
                        next++;
                    }
                    // Preserve the range and chronology of each pixel column.
                    overview.Add(Points[first]);
                    int early=Math.Min(min,max),late=Math.Max(min,max),last=next-1;
                    if(early>first&&early<last)overview.Add(Points[early]);
                    if(late>early&&late<last)overview.Add(Points[late]);
                    if(last>first)overview.Add(Points[last]);
                    first=next;
                }
                return overview;
            }
        }
        internal sealed class EventStack
        {
            internal readonly List<MatchFact> Facts=new();internal VisualElement Anchor;internal Rect Bounds;
            internal double Time=>Facts[0].Seconds;
        }
        private readonly List<Line> lines;private readonly HashSet<PlayableOwner> visible;private readonly double duration,maximum;
        private readonly MatchFact[] events;private readonly Func<MatchFact,double> eventValue;private readonly Func<MatchFact,VisualElement> icon;
        private readonly Action<VisualElement,IReadOnlyList<MatchFact>> openEvents;
        private readonly MatchFact[] exits;private readonly Func<MatchFact,string> exitLabel;
        internal readonly List<EventStack> Stacks=new();
        internal readonly int Metric;
        internal double Cursor;internal bool HideEvents;
        internal ResultGraph(int metric,List<Line> lines,HashSet<PlayableOwner> visible,double duration,double maximum,Action<double,Vector2?> inspect,MatchFact[] events,Func<MatchFact,double> eventValue,Func<MatchFact,VisualElement> icon,Action<VisualElement,IReadOnlyList<MatchFact>> openEvents,MatchFact[] exits=null,Func<MatchFact,string> exitLabel=null)
        {
            Metric=metric;
            this.lines=lines;this.visible=visible;this.duration=duration>0?duration:1;this.maximum=Math.Max(1,maximum);this.events=events;this.eventValue=eventValue;this.icon=icon;this.openEvents=openEvents;this.exits=exits??Array.Empty<MatchFact>();this.exitLabel=exitLabel??(f=>f.Kind.ToString());
            generateVisualContent+=Draw;RegisterCallback<PointerMoveEvent>(e=>inspect(Math.Max(0,Math.Min(duration,e.localPosition.x/Math.Max(1,contentRect.width)*duration)),e.localPosition));
            RegisterCallback<GeometryChangedEvent>(_=>RefreshEvents());style.backgroundColor=PostMatchResultsView.GraphPanel;style.overflow=Overflow.Visible;style.borderLeftWidth=1;style.borderLeftColor=Color.clear;
        }
        internal PlayableOwner? NearestOwner(double time,float y)=>lines.Where(l=>visible.Contains(l.Owner)).OrderBy(l=>Math.Abs(Point(new Vector2((float)time,(float)l.Value(time))).y-y)).Select(l=>(PlayableOwner?)l.Owner).FirstOrDefault();
        private Vector2 Point(Vector2 p)=>new Vector2(p.x/(float)duration*contentRect.width,contentRect.height-2-p.y/(float)maximum*(contentRect.height-4));
        internal void RefreshEvents()
        {
            Clear();Stacks.Clear();MarkDirtyRepaint();if(contentRect.width<=0||contentRect.height<=0)return;
            foreach(var fact in exits.Where(f=>visible.Contains(f.Owner)).OrderBy(f=>f.Seconds).ThenBy(f=>f.Sequence))
            {
                var line=lines.First(l=>l.Owner==fact.Owner);float x=Mathf.Clamp((float)(fact.Seconds/duration)*contentRect.width,0,Math.Max(0,contentRect.width-6));
                int samePixel=exits.Count(f=>visible.Contains(f.Owner)&&f.Sequence<fact.Sequence&&Math.Abs(f.Seconds-fact.Seconds)/duration*contentRect.width<3);x=Mathf.Clamp(x+(x>contentRect.width-30?-1:1)*samePixel*3,0,Math.Max(0,contentRect.width-6));
                var band=new VisualElement{name="result-exit-band-"+fact.Sequence,focusable=true,pickingMode=PickingMode.Ignore};band.AddToClassList("result-focusable");band.AddToClassList("result-event-marker");band.style.position=Position.Absolute;band.style.left=x;band.style.top=0;band.style.bottom=0;band.style.width=6;band.style.backgroundColor=new Color(line.Color.r,line.Color.g,line.Color.b,.22f);band.style.borderLeftWidth=2;band.style.borderLeftColor=line.Color;
                var label=new Label(exitLabel(fact)){pickingMode=PickingMode.Ignore};label.style.position=Position.Absolute;label.style.left=Mathf.Min(4,contentRect.width-x-98);label.style.top=line.Slot*13+2;label.style.width=98;label.style.fontSize=9;label.style.color=line.Color;label.style.overflow=Overflow.Hidden;label.style.textOverflow=TextOverflow.Ellipsis;label.style.whiteSpace=WhiteSpace.NoWrap;band.Add(label);
                var stack=new EventStack{Anchor=band,Bounds=new Rect(x,0,6,contentRect.height)};stack.Facts.Add(fact);Stacks.Add(stack);band.userData=(Action)(()=>openEvents(band,stack.Facts));Add(band);
            }
            if(HideEvents)return;
            bool showRelayLoss=visible.Count==1;
            foreach(var f in events.Where(f=>visible.Contains(f.Owner)&&(showRelayLoss||f.Kind!=MatchFactKind.RelayNeutralized))
                .OrderBy(f=>f.Seconds).ThenBy(f=>f.Sequence))
            {
                var pt=EventPoint(f);
                const int footprint=EventIconSize+2*StackFrameInset;
                var bounds=new Rect(pt.x-footprint/2f,pt.y-footprint/2f,footprint,footprint);
                // Only visible marker footprints block space. Dense runs of events
                // must not merge into a single stack that spans the whole timeline.
                var stack=Stacks.FirstOrDefault(s=>s.Anchor==null&&s.Bounds.Overlaps(bounds));
                if(stack==null){stack=new EventStack{Bounds=bounds};Stacks.Add(stack);}
                stack.Facts.Add(f);
            }
            foreach(var stack in Stacks.Where(s=>s.Anchor==null))
            {
                stack.Facts.Sort((a,b)=>a.Tick!=b.Tick?a.Tick.CompareTo(b.Tick):a.Sequence.CompareTo(b.Sequence));
                var first=stack.Facts[0];
                var anchor=icon(first);
                stack.Anchor=anchor;
                anchor.name="result-event-stack-"+first.Sequence;
                anchor.AddToClassList("result-event-marker");
                anchor.style.position=Position.Absolute;
                var pt=EventPoint(first);
                anchor.style.left=pt.x-EventIconSize/2f;
                anchor.style.top=pt.y-EventIconSize/2f;
                if(stack.Facts.Count>1)StyleEventStack(anchor);
                anchor.userData=(Action)(()=>openEvents(anchor,stack.Facts));anchor.RegisterCallback<PointerEnterEvent>(_=>openEvents(anchor,stack.Facts));Add(anchor);
            }
        }
        private Vector2 EventPoint(MatchFact fact)
            =>Point(new Vector2((float)fact.Seconds,(float)eventValue(fact)));

        private static void StyleEventStack(VisualElement anchor)
        {
            var outline=new Color(.58f,.70f,.75f);
            var frame=new VisualElement{name="result-stack-frame",pickingMode=PickingMode.Ignore};
            frame.style.position=Position.Absolute;
            frame.style.left=frame.style.top=-StackFrameInset;
            frame.style.width=frame.style.height=EventIconSize+2*StackFrameInset;
            frame.style.borderTopWidth=frame.style.borderRightWidth=frame.style.borderBottomWidth=frame.style.borderLeftWidth=1.5f;
            TintStackPlate(frame,outline);
            anchor.Add(frame);
            anchor.RegisterCallback<FocusInEvent>(_=>TintStackPlate(frame,OrbitalTheme.Cyan));
            anchor.RegisterCallback<FocusOutEvent>(_=>TintStackPlate(frame,outline));
        }

        private static void TintStackPlate(VisualElement plate,Color color)
        {
            plate.style.borderTopColor=plate.style.borderRightColor=plate.style.borderBottomColor=plate.style.borderLeftColor=color;
        }

        private void Draw(MeshGenerationContext context)
        {
            if(contentRect.width<=0||contentRect.height<=0)return;
            var p=context.painter2D;p.lineWidth=1;
            for(int i=0;i<5;i++){float y=i*(contentRect.height-4)/4+2;p.strokeColor=i%2==0?OrbitalTheme.Line:new Color(.12f,.19f,.23f);p.BeginPath();p.MoveTo(new Vector2(0,y));p.LineTo(new Vector2(contentRect.width,y));p.Stroke();}
            int columns=Math.Max(1,Mathf.CeilToInt(contentRect.width));
            foreach(var line in lines.Where(l=>visible.Contains(l.Owner)).OrderByDescending(l=>l.Slot))
            {
                var points=line.Overview(columns,duration);
                if(points.Count<2)continue;
                p.strokeColor=line.Color;p.lineWidth=1.65f;
                // Painter2D allocates one mesh per stroke. Bound its vertex count
                // and start each piece at the previous piece's endpoint.
                const int stepsPerStroke=256;
                for(int start=0;start<points.Count-1;start+=stepsPerStroke)
                {
                    var from=Point(points[start]);p.BeginPath();p.MoveTo(from);
                    int end=Math.Min(points.Count-1,start+stepsPerStroke);
                    for(int i=start+1;i<=end;i++)
                    {
                        var to=Point(points[i]);
                        if(to.x!=from.x)p.LineTo(new Vector2(to.x,from.y));
                        if(to.y!=from.y)p.LineTo(to);
                        from=to;
                    }
                    p.Stroke();
                }
            }
            p.strokeColor=OrbitalTheme.Cyan;p.lineWidth=1;p.BeginPath();float x=(float)(Cursor/duration)*contentRect.width;p.MoveTo(new Vector2(x,0));p.LineTo(new Vector2(x,contentRect.height));p.Stroke();
        }
    }
}
