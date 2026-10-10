using System;
using UnityEngine;
using UnityEngine.UIElements;
namespace Spacewars.Presentation
{
    public static class NativeControlsHelp
    {
        [Serializable] private sealed class Content {public string source;public Tab[] tabs;}
        [Serializable] private sealed class Tab {public string title;public Row[] rows;}
        [Serializable] private sealed class Row {public string button,text;}
        public static VisualElement Open(VisualElement root,NativeMenuNavigation navigation,Action closed)
        {
            var data=JsonUtility.FromJson<Content>(Resources.Load<TextAsset>("NativeGamepadHelp").text);
            var page=OrbitalTheme.Screen(root,"controls-help");var card=OrbitalTheme.Card(page);card.style.width=1000;card.style.maxWidth=Length.Percent(96);card.style.maxHeight=Length.Percent(94);
            card.Add(OrbitalTheme.Text("УПРАВЛЕНИЕ","orbital-title"));
            var tabs=new VisualElement();tabs.style.flexDirection=FlexDirection.Row;tabs.style.flexWrap=Wrap.Wrap;card.Add(tabs);
            var scroll=OrbitalTheme.Scroll();scroll.name="controls-scroll";scroll.focusable=true;scroll.style.flexShrink=1;scroll.style.minHeight=0;card.Add(scroll);
            var body=new VisualElement();body.style.flexDirection=FlexDirection.Row;body.style.flexWrap=Wrap.Wrap;body.style.alignItems=Align.FlexStart;scroll.Add(body);
            var diagram=new Image{name="controller-diagram",image=Resources.Load<Texture2D>("GamepadHelpController"),scaleMode=ScaleMode.ScaleToFit};diagram.style.width=360;diagram.style.minWidth=260;diagram.style.flexShrink=1;diagram.style.marginRight=16;diagram.style.height=240;body.Add(diagram);
            diagram.RegisterCallback<GeometryChangedEvent>(_=>diagram.style.height=diagram.resolvedStyle.width*682/1024);
            void Callout(string key,float x,float y)
            {
                var label=OrbitalTheme.Text(key,"orbital-muted");label.name="help-button-"+key;label.style.position=Position.Absolute;label.style.left=Length.Percent(x);label.style.top=Length.Percent(y);label.style.fontSize=12;label.style.whiteSpace=WhiteSpace.NoWrap;label.style.color=OrbitalTheme.Cyan;label.style.backgroundColor=OrbitalTheme.Panel;label.style.marginTop=label.style.marginBottom=0;label.style.paddingLeft=3;label.style.paddingRight=3;label.style.translate=new Translate(Length.Percent(-50),Length.Percent(-50),0);diagram.Add(label);
                if(key=="A"||key=="B"||key=="X"||key=="Y")label.schedule.Execute(()=>label.text=NativeControllerGlyph.Symbol(key,navigation.CurrentGamepad)).Every(80);
            }
            Callout("LT",28,10);Callout("LB",31,19);Callout("RT",72,10);Callout("RB",69,19);Callout("LS / L3",29,43);Callout("RS",61,59);Callout("View",45,43);Callout("Start",55,43);Callout("Y",73,34);Callout("X",68,43);Callout("B",79,43);Callout("A",73,52);Callout("D-pad",39,65);
            var descriptions=new VisualElement{name="help-context-actions"};descriptions.style.flexGrow=1;descriptions.style.flexBasis=0;descriptions.style.minWidth=300;body.Add(descriptions);
            var buttons=new Button[data.tabs.Length];
            void Select(int selected)
            {
                descriptions.Clear();scroll.scrollOffset=Vector2.zero;
                for(int i=0;i<buttons.Length;i++){buttons[i].style.color=i==selected?OrbitalTheme.Cyan:OrbitalTheme.Ink;buttons[i].style.borderTopColor=i==selected?OrbitalTheme.Cyan:OrbitalTheme.Line;}
                foreach(var row in data.tabs[selected].rows){var item=new VisualElement();item.style.marginTop=8;descriptions.Add(item);var title=OrbitalTheme.Text(row.button,"orbital-muted");title.style.color=OrbitalTheme.Cyan;title.style.marginTop=title.style.marginBottom=2;item.Add(title);var detail=OrbitalTheme.Text(row.text,"orbital-muted");detail.style.whiteSpace=WhiteSpace.Normal;detail.style.marginTop=detail.style.marginBottom=2;item.Add(detail);}
            }
            for(int i=0;i<data.tabs.Length;i++){int selected=i;buttons[i]=OrbitalTheme.Action(data.tabs[i].title,()=>Select(selected),"help-tab-"+i);buttons[i].style.flexGrow=1;buttons[i].style.flexBasis=0;tabs.Add(buttons[i]);}Select(0);
            var keyboard=OrbitalTheme.Text("Мышь: ЛКМ / рамка — выбор; ПКМ — приказ; Shift + ПКМ — очередь. Клавиатура: A — атака, S — стоп, H — HOLD; 1–9 — группа; Ctrl / Shift + цифра — назначить; повтор — камера; 0 / F2 — армия; Home — сброс; Tab — карта; стрелки / мышь у края — прокрутка камеры. Скорости обоих способов меняются в настройках.","orbital-muted");keyboard.style.whiteSpace=WhiteSpace.Normal;keyboard.style.marginTop=16;scroll.Add(keyboard);
            var local=OrbitalTheme.Text("Лобби: X — бот; Y — присоединить этот контроллер. Один человек использует мышь, клавиатуру и геймпад одновременно. У 2–4 людей отдельные камера, выделение, группы и скорости; общий минимап и пауза. Меню принадлежит открывшему его игроку. После отключения или потери фокуса отпустите кнопки и явно продолжите матч.","orbital-muted");local.style.whiteSpace=WhiteSpace.Normal;local.style.marginTop=12;scroll.Add(local);
            Action back=()=>{page.RemoveFromHierarchy();closed();};card.Add(OrbitalTheme.Action("Назад",back,"controls-back"));var hints=OrbitalTheme.Text("","orbital-hints");card.Add(hints);navigation.SetScope(page,back,buttons[0],hints);return page;
        }
    }
}
