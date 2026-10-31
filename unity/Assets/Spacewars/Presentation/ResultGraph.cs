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
        internal sealed class Line
        {
            internal readonly PlayableOwner Owner;internal readonly int Slot;internal readonly Color Color;internal readonly List<Vector2> Points;
            internal Line(PlayableOwner owner,int slot,Color color,List<Vector2> points){Owner=owner;Slot=slot;Color=color;Points=points;}
            internal double Value(double seconds){double value=0;foreach(var point in Points){if(point.x>seconds+1e-6)break;value=point.y;}return value;}
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
        internal double Cursor;internal bool HideEvents;
        internal ResultGraph(List<Line> lines,HashSet<PlayableOwner> visible,double duration,double maximum,Action<double,Vector2?> inspect,MatchFact[] events,Func<MatchFact,double> eventValue,Func<MatchFact,VisualElement> icon,Action<VisualElement,IReadOnlyList<MatchFact>> openEvents,MatchFact[] exits=null,Func<MatchFact,string> exitLabel=null)
        {
            this.lines=lines;this.visible=visible;this.duration=Math.Max(1,duration);this.maximum=Math.Max(1,maximum);this.events=events;this.eventValue=eventValue;this.icon=icon;this.openEvents=openEvents;this.exits=exits??Array.Empty<MatchFact>();this.exitLabel=exitLabel??(f=>f.Kind.ToString());
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
            foreach(var f in events.Where(f=>visible.Contains(f.Owner)).OrderBy(f=>f.Seconds).ThenBy(f=>f.Sequence))
            {
                var pt=Point(new Vector2((float)f.Seconds,(float)eventValue(f)));var bounds=new Rect(Mathf.Clamp(pt.x-9,0,Math.Max(0,contentRect.width-23)),pt.y+7,23,23);
                var overlaps=Stacks.Where(s=>s.Anchor==null&&s.Bounds.Overlaps(bounds)).ToArray();var stack=overlaps.FirstOrDefault();
                if(stack==null){stack=new EventStack{Bounds=bounds};Stacks.Add(stack);}
                else{stack.Bounds=Union(stack.Bounds,bounds);foreach(var merge in overlaps.Skip(1)){stack.Facts.AddRange(merge.Facts);stack.Bounds=Union(stack.Bounds,merge.Bounds);Stacks.Remove(merge);}}
                stack.Facts.Add(f);
            }
            foreach(var stack in Stacks.Where(s=>s.Anchor==null))
            {
                stack.Facts.Sort((a,b)=>a.Sequence.CompareTo(b.Sequence));var anchor=icon(stack.Facts[0]);stack.Anchor=anchor;anchor.name="result-event-stack-"+stack.Facts[0].Sequence;anchor.AddToClassList("result-event-marker");anchor.style.position=Position.Absolute;anchor.style.left=stack.Bounds.x;anchor.style.top=stack.Bounds.y;
                if(stack.Facts.Count>1)
                {
                    for(int i=2;i>=1;i--){var plate=new VisualElement{pickingMode=PickingMode.Ignore};plate.style.position=Position.Absolute;plate.style.left=i*3;plate.style.top=i*3;plate.style.width=18;plate.style.height=18;plate.style.backgroundColor=PostMatchResultsView.GraphPanel;plate.style.borderBottomWidth=plate.style.borderRightWidth=1;plate.style.borderBottomColor=plate.style.borderRightColor=OrbitalTheme.Muted;anchor.Insert(0,plate);}
                    var count=new Label(stack.Facts.Count.ToString()){pickingMode=PickingMode.Ignore,name="result-stack-count"};count.style.position=Position.Absolute;count.style.right=-8;count.style.top=-9;count.style.fontSize=9;count.style.backgroundColor=OrbitalTheme.Panel;count.style.color=OrbitalTheme.Ink;anchor.Add(count);
                }
                anchor.userData=(Action)(()=>openEvents(anchor,stack.Facts));anchor.RegisterCallback<PointerEnterEvent>(_=>openEvents(anchor,stack.Facts));Add(anchor);
            }
        }
        private static Rect Union(Rect a,Rect b)=>Rect.MinMaxRect(Math.Min(a.xMin,b.xMin),Math.Min(a.yMin,b.yMin),Math.Max(a.xMax,b.xMax),Math.Max(a.yMax,b.yMax));
        private void Draw(MeshGenerationContext context)
        {
            if(contentRect.width<=0||contentRect.height<=0)return;
            var p=context.painter2D;p.lineWidth=1;
            for(int i=0;i<5;i++){float y=i*(contentRect.height-4)/4+2;p.strokeColor=i%2==0?OrbitalTheme.Line:new Color(.12f,.19f,.23f);p.BeginPath();p.MoveTo(new Vector2(0,y));p.LineTo(new Vector2(contentRect.width,y));p.Stroke();}
            foreach(var line in lines.Where(l=>visible.Contains(l.Owner)).OrderByDescending(l=>l.Slot))
            {
                p.strokeColor=line.Color;p.lineWidth=1.5f;
                for(int i=1;i<line.Points.Count;i++){var from=Point(line.Points[i-1]);var to=Point(line.Points[i]);p.BeginPath();p.MoveTo(from);p.LineTo(new Vector2(to.x,from.y));p.LineTo(to);p.Stroke();}
                // Slot-specific marks supplement ownership color, including overlapping curves.
                int stride=Math.Max(1,line.Points.Count/12);
                for(int i=line.Slot%stride;i<line.Points.Count;i+=stride)Marker(p,Point(line.Points[i]),line.Color,line.Slot,2);
                Marker(p,Point(new Vector2((float)Cursor,(float)line.Value(Cursor))),line.Color,line.Slot,3.5f);
            }
            p.strokeColor=OrbitalTheme.Cyan;p.lineWidth=1;p.BeginPath();float x=(float)(Cursor/duration)*contentRect.width;p.MoveTo(new Vector2(x,0));p.LineTo(new Vector2(x,contentRect.height));p.Stroke();
        }
        private static void Marker(Painter2D p,Vector2 center,Color color,int slot,float size)
        {
            p.fillColor=color;p.BeginPath();int sides=slot%3==0?8:slot%3==1?4:3;
            for(int i=0;i<sides;i++){float angle=(float)(i*Math.PI*2/sides-Math.PI/2);var v=center+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*size;if(i==0)p.MoveTo(v);else p.LineTo(v);}p.ClosePath();p.Fill();
        }
    }
}
