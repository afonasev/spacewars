using System;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UIElements;

namespace Spacewars.Presentation
{
    // SOURCE GamepadOverlay SVG unit geometry and palette, scaled to its profile
    // radius/viewport bounds. It does not own selection or submit commands.
    public sealed class OfflinePadRing : VisualElement
    {
        private OfflinePadAction[] actions=Array.Empty<OfflinePadAction>();private string highlighted;
        private readonly VisualElement ring;private readonly Label title,centre,hint,detail;private readonly ProgressBar progress;
        private static Color Hex(string value){ColorUtility.TryParseHtmlString(value,out var color);return color;}
        public OfflinePadRing()
        {
            pickingMode=PickingMode.Ignore;style.display=DisplayStyle.None;style.position=Position.Absolute;style.left=Length.Percent(50);style.width=Length.Percent(50);style.top=0;style.height=Length.Percent(100);style.overflow=Overflow.Hidden;style.backgroundColor=new Color(.02f,.035f,.043f,.25f);
            title=new Label();title.pickingMode=PickingMode.Ignore;title.style.position=Position.Absolute;title.style.left=Length.Percent(10);title.style.width=Length.Percent(80);title.style.unityTextAlign=TextAnchor.MiddleCenter;title.style.backgroundColor=Hex("#151a1d");Add(title);
            ring=new VisualElement{pickingMode=PickingMode.Ignore};ring.style.position=Position.Absolute;ring.generateVisualContent+=Draw;Add(ring);
            centre=new Label();centre.pickingMode=PickingMode.Ignore;centre.style.position=Position.Absolute;centre.style.left=Length.Percent(29);centre.style.top=Length.Percent(29);centre.style.width=Length.Percent(42);centre.style.height=Length.Percent(22);centre.style.whiteSpace=WhiteSpace.Normal;centre.style.unityTextAlign=TextAnchor.MiddleCenter;centre.style.fontSize=15;centre.style.backgroundColor=Hex("#151a1d");ring.Add(centre);
            hint=new Label();hint.pickingMode=PickingMode.Ignore;hint.style.position=Position.Absolute;hint.style.left=Length.Percent(29);hint.style.top=Length.Percent(51);hint.style.width=Length.Percent(42);hint.style.height=Length.Percent(20);hint.style.whiteSpace=WhiteSpace.Normal;hint.style.unityTextAlign=TextAnchor.UpperCenter;hint.style.fontSize=12;hint.style.color=Hex("#9ab5b8");hint.style.backgroundColor=Hex("#151a1d");ring.Add(hint);
            progress=new ProgressBar();progress.pickingMode=PickingMode.Ignore;progress.lowValue=0;progress.highValue=1;progress.style.position=Position.Absolute;progress.style.left=Length.Percent(34);progress.style.top=Length.Percent(67);progress.style.width=Length.Percent(32);progress.style.height=6;ring.Add(progress);
            detail=new Label();detail.pickingMode=PickingMode.Ignore;detail.style.position=Position.Absolute;detail.style.left=Length.Percent(5);detail.style.width=Length.Percent(90);detail.style.whiteSpace=WhiteSpace.Normal;detail.style.unityTextAlign=TextAnchor.UpperCenter;detail.style.backgroundColor=Hex("#151a1d");detail.style.fontSize=13;Add(detail);
        }
        public void Set(OfflinePadAction[] next,string selected,string mode,int page,int pages,bool added,double held,float radius)
        {
            actions=next;highlighted=selected;style.display=next.Length>0?DisplayStyle.Flex:DisplayStyle.None;
            float diameter=Math.Min(radius*2,Math.Min(Screen.height*.72f,Screen.width*.5f*.9f));ring.style.width=diameter;ring.style.height=diameter;ring.style.left=(Screen.width*.5f-diameter)/2;ring.style.top=(Screen.height-diameter)/2;
            title.style.top=(Screen.height-diameter)/2-32;title.text=(mode=="groupAssign"?"Назначить группу":mode=="groupWheel"?"Группы":mode=="baseWheel"?"Базы":"Действия здания")+(pages>1?" · "+(page%pages+1)+"/"+pages+" · ◀ ▶":"");
            var action=next.FirstOrDefault(a=>a.Sector.Id==selected);centre.text=added?"Добавлено ✓":action?.Label??"Левый стик";
            hint.text=added?"Отпустите RT":action==null?"Выберите сектор":!action.Sector.Enabled?"Недоступно":mode=="buildingWheel"?action.Sector.Hold=="sell"?"Удерживайте A":action.Sector.Hold=="repeat"?"A: один · удерживать: ∞":"A: подтвердить":mode=="groupAssign"?"RT: отпустить — заменить; держать — добавить":"Отпустите бампер";
            progress.value=(float)held;progress.title="";progress.style.display=held>0||mode=="groupAssign"&&action?.Sector.Enabled==true?DisplayStyle.Flex:DisplayStyle.None;
            detail.style.top=(Screen.height+diameter)/2+6;detail.text=(added?"Юниты добавлены. Отпускание RT закроет кольцо.":action?.Detail??"Отпустите стик, чтобы вернуть указатель в центр")+"\n"+(added?"Назначение завершено":"B — отмена");ring.MarkDirtyRepaint();
        }
        private static string LabelLines(string label)
        {
            var words=Regex.Replace(label,@"^(?:Построить|Произвести):\s*|^Группа\s+","").Split(new[]{' '},StringSplitOptions.RemoveEmptyEntries);var lines=new System.Collections.Generic.List<string>();
            foreach(var word in words){if(lines.Count>0&&lines[lines.Count-1].Length+word.Length+1<=13)lines[lines.Count-1]+=" "+word;else lines.Add(word);}return string.Join("\n",lines);
        }
        private void Draw(MeshGenerationContext context)
        {
            if(actions.Length==0)return;float scale=ring.contentRect.width/400f;var center=new Vector2(200,200)*scale;var painter=context.painter2D;
            for(int index=0;index<actions.Length;index++){
                var action=actions[index];bool active=action.Sector.Id==highlighted;double start=(index-.5)/actions.Length*Math.PI*2-Math.PI/2,end=(index+.5)/actions.Length*Math.PI*2-Math.PI/2-.002;
                Vector2 Point(double angle,float radius)=>center+new Vector2((float)Math.Cos(angle),(float)Math.Sin(angle))*radius*scale;
                painter.fillColor=Hex(!action.Sector.Enabled?"#202427":active?"#204b52":"#151a1d");painter.strokeColor=Hex(!action.Sector.Enabled?"#41494c":active?"#65d7dc":"#607076");painter.lineWidth=(active?3:1.5f)*scale;painter.BeginPath();
                int segments=Math.Max(4,64/actions.Length);painter.MoveTo(Point(start,192));for(int n=1;n<=segments;n++)painter.LineTo(Point(start+(end-start)*n/segments,192));painter.LineTo(Point(end,105));for(int n=segments-1;n>=0;n--)painter.LineTo(Point(start+(end-start)*n/segments,105));painter.ClosePath();painter.Fill();painter.Stroke();
                double angle=index/(double)actions.Length*Math.PI*2-Math.PI/2;string label=LabelLines(action.Label);var lines=label.Split('\n');var position=Point(angle,146);
                // SOURCE 14px SVG text, centered in each scaled sector.
                for(int line=0;line<lines.Length;line++)context.DrawText(lines[line],position+new Vector2(-36*scale,(line-(lines.Length-1)*.5f)*15.4f*scale-7*scale),14*scale,Hex(action.Sector.Enabled?"#e6eeee":"#7f898b"),null);
            }
        }
    }
}
