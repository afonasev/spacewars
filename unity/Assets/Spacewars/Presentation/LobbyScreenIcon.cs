using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.UIElements;
namespace Spacewars.Presentation
{
    public sealed class LobbyScreenIcon:VisualElement
    {
        public int Slot{get;private set;} public int Screens{get;private set;}
        public LobbyScreenIcon(int slot,int count)
        {
            Slot=slot;Screens=count;pickingMode=PickingMode.Ignore;style.width=30;style.height=26;
            generateVisualContent+=ctx=>{
                var p=ctx.painter2D;var ink=new Color(.44f,.85f,.91f);var r=new Rect(3,3,24,16);
                p.fillColor=new Color(.07f,.13f,.16f);p.strokeColor=ink;p.lineWidth=1.5f;
                Draw(r,true);
                if(Slot>=0){Rect fill=r;fill.xMin+=2;fill.xMax-=2;fill.yMin+=2;fill.yMax-=2;
                    if(Screens==2){fill.width/=2;if(Slot==1)fill.x+=fill.width;}
                    else if(Screens>=3){fill.width/=2;fill.height/=2;fill.x+=(Slot%2)*fill.width;fill.y+=(Slot/2)*fill.height;}
                    p.fillColor=ink;Draw(fill,false);}
                else {p.BeginPath();p.MoveTo(new Vector2(2,23));p.LineTo(new Vector2(28,1));p.Stroke();}
                p.BeginPath();p.MoveTo(new Vector2(15,19));p.LineTo(new Vector2(15,23));p.MoveTo(new Vector2(10,23));p.LineTo(new Vector2(20,23));p.Stroke();
                void Draw(Rect b,bool outline){p.BeginPath();p.MoveTo(new Vector2(b.xMin,b.yMin));p.LineTo(new Vector2(b.xMax,b.yMin));p.LineTo(new Vector2(b.xMax,b.yMax));p.LineTo(new Vector2(b.xMin,b.yMax));p.ClosePath();p.Fill();if(outline)p.Stroke();}
            };
        }
    }
}
