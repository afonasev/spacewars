using System;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.UIElements;

namespace Spacewars.Presentation
{
    public sealed class LobbyColorField : BaseField<int>
    {
        public Button Trigger { get; }
        public Action Open;
        private readonly VisualElement swatch;
        public LobbyColorField(int color) : base(null,new Button())
        {
            Trigger=this.Q<Button>();Trigger.name="lobby-color-trigger";
            Trigger.clicked+=()=>Open?.Invoke();
            Trigger.style.flexDirection=FlexDirection.Row;Trigger.style.alignItems=Align.Center;
            Trigger.style.marginLeft=Trigger.style.marginRight=Trigger.style.marginTop=Trigger.style.marginBottom=0;
            swatch=new VisualElement{name="lobby-color-swatch",pickingMode=PickingMode.Ignore};
            swatch.style.flexGrow=0;swatch.style.flexShrink=0;swatch.style.width=22;swatch.style.height=22;swatch.style.marginRight=8;Trigger.Add(swatch);
            var arrow=new Label("▼"){pickingMode=PickingMode.Ignore};arrow.style.fontSize=10;Trigger.Add(arrow);
            SetValueWithoutNotify(color);
        }
        public override void SetValueWithoutNotify(int color)
        {
            base.SetValueWithoutNotify(color);
            if(swatch==null)return;
            ColorUtility.TryParseHtmlString(NativeLobbyConfiguration.ColorHex[color],out var paint);
            swatch.style.backgroundColor=paint;tooltip=Trigger.tooltip=NativeLobbyConfiguration.ColorNames[color];
        }
    }
}
