using System.Collections.Generic;
using System.Globalization;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.UIElements;

namespace Spacewars.Presentation
{
    public sealed partial class PlayableBootstrap
    {
        private VisualElement incomeMarkerLayer;
        private readonly Dictionary<int,Label> incomeMarkers=new Dictionary<int,Label>();
        private readonly HashSet<int> activeIncomeMarkers=new HashSet<int>();
        private readonly List<int> staleIncomeMarkers=new List<int>();
        private void ClearIncomeMarkers()
        {
            incomeMarkerLayer?.Clear();incomeMarkers.Clear();
        }
        private void UpdateIncomeMarkers()
        {
            if(root?.panel==null||view==null)return;
            if(incomeMarkerLayer==null||incomeMarkerLayer.parent!=root)
            {
                incomeMarkerLayer?.RemoveFromHierarchy();incomeMarkers.Clear();
                incomeMarkerLayer=new VisualElement{name="income-markers",pickingMode=PickingMode.Ignore};
                incomeMarkerLayer.style.position=Position.Absolute;
                incomeMarkerLayer.style.left=incomeMarkerLayer.style.right=incomeMarkerLayer.style.top=incomeMarkerLayer.style.bottom=0;
                incomeMarkerLayer.style.overflow=Overflow.Hidden;root.Insert(0,incomeMarkerLayer);
            }
            activeIncomeMarkers.Clear();
            foreach(var e in view.IncomeEvents)
            {
                double age=(view.Tick-e.Tick)/30d;
                if(e.Owner!=view.Owner||age<0||age>=profile.IncomeMarkerDurationSeconds)continue;
                activeIncomeMarkers.Add(e.BuildingId);
                if(!incomeMarkers.TryGetValue(e.BuildingId,out var label))
                {
                    label=new Label{name="income-"+e.BuildingId,pickingMode=PickingMode.Ignore};
                    label.style.position=Position.Absolute;label.style.unityFontStyleAndWeight=FontStyle.Bold;
                    label.style.unityTextAlign=TextAnchor.MiddleCenter;
                    label.style.textShadow=new TextShadow{color=Color.black,offset=new Vector2(0,2),blurRadius=3};
                    incomeMarkerLayer.Add(label);incomeMarkers.Add(e.BuildingId,label);
                }
                label.text="+"+e.Amount.ToString("0.##",CultureInfo.InvariantCulture);label.style.color=LobbyPaint(e.Owner);
                float font=(float)profile.IncomeMarkerFontPixels,width=font*(label.text.Length+1),height=font*2;
                label.style.fontSize=font;label.style.width=width;label.style.height=height;
                var point=cameraView.WorldToScreenPoint(world.Point(e.Position)+Vector3.up*(float)(BuildingVisualHeight(e.Kind)+profile.LifecycleMarkerOffsetMeters));
                var panel=RuntimePanelUtils.ScreenToPanel(root.panel,new Vector2(point.x,Screen.height-point.y));
                var local=root.WorldToLocal(panel);
                float progress=(float)(age/profile.IncomeMarkerDurationSeconds),eased=1-(1-progress)*(1-progress);
                label.style.left=local.x-width/2;label.style.top=local.y-height-(float)profile.IncomeMarkerRisePixels*eased;
                label.style.opacity=1-progress;label.style.display=point.z>0?DisplayStyle.Flex:DisplayStyle.None;
            }
            staleIncomeMarkers.Clear();foreach(int id in incomeMarkers.Keys)if(!activeIncomeMarkers.Contains(id))staleIncomeMarkers.Add(id);
            foreach(int id in staleIncomeMarkers){incomeMarkers[id].RemoveFromHierarchy();incomeMarkers.Remove(id);}
        }
    }
}
