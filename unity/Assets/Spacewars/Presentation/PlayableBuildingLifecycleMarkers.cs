using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.UIElements;

namespace Spacewars.Presentation
{
    public sealed partial class PlayableBootstrap
    {
        private readonly Dictionary<int,LifecycleMarker> lifecycleMarkers=new Dictionary<int,LifecycleMarker>();
        // Normalized vector icon geometry is an authored shape; size/opacity/cadence are profile-owned.
        private sealed class LifecycleMarker : Label
        {
            public bool Selling;
            private static readonly Vector2[] Wrench={new Vector2(.68f,.08f),new Vector2(.52f,.12f),new Vector2(.40f,.26f),new Vector2(.40f,.42f),new Vector2(.13f,.69f),new Vector2(.13f,.80f),new Vector2(.20f,.87f),new Vector2(.31f,.87f),new Vector2(.58f,.60f),new Vector2(.74f,.60f),new Vector2(.88f,.46f),new Vector2(.92f,.30f),new Vector2(.75f,.44f),new Vector2(.62f,.31f),new Vector2(.76f,.16f)};
            public LifecycleMarker(){generateVisualContent+=ctx=>
            {
                if(Selling)return;var painter=ctx.painter2D;painter.fillColor=new Color(1,.85f,.3f);painter.BeginPath();
                for(int i=0;i<Wrench.Length;i++){var p=new Vector2(Wrench[i].x*contentRect.width,Wrench[i].y*contentRect.height);if(i==0)painter.MoveTo(p);else painter.LineTo(p);}
                painter.ClosePath();painter.Fill();
            };}
        }
        private readonly HashSet<int> activeLifecycleMarkers=new HashSet<int>();
        private readonly List<int> staleLifecycleMarkers=new List<int>();
        private double BuildingVisualHeight(PlayableBuildingKind kind)=>kind==PlayableBuildingKind.Factory?profile.FactoryModelHeightMeters*profile.FactoryModelScale:
            kind==PlayableBuildingKind.ScientificCenter?profile.ScienceModelHeightMeters*profile.ScienceModelScale:
            kind==PlayableBuildingKind.Refinery?profile.RefineryModelHeightMeters*profile.RefineryModelScale:
            kind==PlayableBuildingKind.Outpost?profile.OutpostModelHeightMeters*profile.OutpostModelScale:
            kind==PlayableBuildingKind.Mine?profile.MineModelHeightMeters*profile.MineModelScale:profile.HeadquartersModelHeightMeters*profile.HeadquartersModelScale;
        private void UpdateLifecycleMarkers()
        {
            if(root?.panel==null)return;
            var active=activeLifecycleMarkers;active.Clear();
            foreach(var b in view.Buildings)
            {
                var state=b.PrivateState?.Lifecycle;
                double height=BuildingVisualHeight(b.Kind);
                if(world.Actors.TryGetValue(b.Id,out var actor)&&actor.Hull!=null)
                    actor.Hull.localPosition=Vector3.down*(float)(state?.Selling==true?height*state.SaleProgress:0);
                if(state==null||(!state.Selling&&!state.Repairing)||b.Phase==ConstructionPhase.Pending)continue;
                active.Add(b.Id);
                if(!lifecycleMarkers.TryGetValue(b.Id,out var marker))
                {
                    marker=new LifecycleMarker();marker.pickingMode=PickingMode.Ignore;marker.style.position=Position.Absolute;
                    marker.style.unityTextAlign=TextAnchor.MiddleCenter;marker.style.unityFontStyleAndWeight=FontStyle.Bold;
                    marker.style.backgroundColor=new Color(.03f,.06f,.08f,.9f);marker.style.color=new Color(1,.85f,.3f);
                    root.Insert(0,marker);lifecycleMarkers.Add(b.Id,marker);
                }
                var point=cameraView.WorldToScreenPoint(world.Point(b.Position)+Vector3.up*(float)(height+profile.LifecycleMarkerOffsetMeters));
                var panel=RuntimePanelUtils.ScreenToPanel(root.panel,new Vector2(point.x,Screen.height-point.y));
                float size=(float)(profile.LifecycleMarkerPixels*(state.Selling?profile.SaleMarkerScale:1));
                marker.Selling=state.Selling;marker.text=state.Selling?"$":"";marker.MarkDirtyRepaint();
                marker.style.width=size;marker.style.height=size;marker.style.fontSize=size;
                marker.style.left=panel.x-size/2;marker.style.top=panel.y-size/2;
                marker.style.opacity=(float)(profile.LifecycleMarkerMinOpacity+(1-profile.LifecycleMarkerMinOpacity)*(.5+.5*System.Math.Sin(view.Tick/30d*System.Math.PI*2*profile.LifecycleMarkerPulseHz)));
                marker.style.display=point.z>0?DisplayStyle.Flex:DisplayStyle.None;
            }
            staleLifecycleMarkers.Clear();foreach(int id in lifecycleMarkers.Keys)if(!active.Contains(id))staleLifecycleMarkers.Add(id);
            foreach(int id in staleLifecycleMarkers){lifecycleMarkers[id].RemoveFromHierarchy();lifecycleMarkers.Remove(id);}
        }
    }
}
