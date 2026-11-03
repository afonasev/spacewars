using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.UIElements;
namespace Spacewars.Presentation
{
    public sealed partial class PlayableBootstrap
    {
        private readonly Dictionary<int,VisualElement> systemFailureMarkers=new Dictionary<int,VisualElement>();
        private sealed class SystemFailureMarker : VisualElement
        {
            public SystemFailureMarker()
            {
                name="system-failure-marker";pickingMode=PickingMode.Ignore;style.position=Position.Absolute;
                generateVisualContent+=ctx=>{
                    var p=ctx.painter2D;float s=contentRect.width;var center=new Vector2(s/2,s/2);
                    p.fillColor=new Color(.8f,.08f,.14f,.95f);p.BeginPath();p.MoveTo(Vector2.zero);p.LineTo(new Vector2(s,0));p.LineTo(new Vector2(s,s));p.LineTo(new Vector2(0,s));p.ClosePath();p.Fill();
                    p.strokeColor=Color.white;p.lineWidth=s/12;p.BeginPath();
                    for(int i=0;i<=24;i++){float a=(-60+300*i/24f)*Mathf.Deg2Rad;var at=center+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*s*.29f;if(i==0)p.MoveTo(at);else p.LineTo(at);}p.Stroke();
                    p.BeginPath();p.MoveTo(new Vector2(s/2,s*.16f));p.LineTo(new Vector2(s/2,s*.5f));p.Stroke();
                };
            }
        }
        private void UpdateSystemFailureMarkers()
        {
            var visible=new HashSet<int>();
            foreach(var b in view.Buildings.Where(b=>b.SystemsDisabled&&view.Vision.IsVisible(b.Position)))
            {
                visible.Add(b.Id);
                if(!systemFailureMarkers.TryGetValue(b.Id,out var marker)){marker=new SystemFailureMarker();root.Insert(0,marker);systemFailureMarkers.Add(b.Id,marker);}
                float size=(float)profile.LifecycleMarkerPixels;
                var point=cameraView.WorldToViewportPoint(world.Point(b.Position)+Vector3.up*(float)(BuildingVisualHeight(b.Kind)+profile.LifecycleMarkerOffsetMeters));
                var panel=new Vector2(point.x*root.contentRect.width,(1-point.y)*root.contentRect.height);
                marker.style.width=marker.style.height=size;marker.style.left=panel.x-size/2;marker.style.top=panel.y-size/2;
                marker.style.display=point.z>0&&!mapOpen?DisplayStyle.Flex:DisplayStyle.None;
            }
            foreach(int id in systemFailureMarkers.Keys.Where(id=>!visible.Contains(id)).ToArray()){systemFailureMarkers[id].RemoveFromHierarchy();systemFailureMarkers.Remove(id);}
            if(view.Buildings.Any(b=>selection.Contains(b.Id)&&b.SystemsDisabled)){
                battleRally=false;battleSector=null;confirmSaleBuilding=0;
                battleGestures.Cancel();if(battleRing!=null)battleRing.style.display=DisplayStyle.None;
                if(upgradeRefinery!=null)upgradeRefinery.style.display=DisplayStyle.None;
            }
        }
        private void DrawSystemFailureTargets(MeshGenerationContext context,Func<NavPoint,Vector2> project,NavPoint center)
        {
            if(!view.Vision.IsVisible(center))return;
            var visible=view.Buildings.Where(b=>b.Health>0&&IsOpponent(b.Owner)&&view.Vision.IsVisible(b.Position)).ToArray();
            var chosen=OrbitalSystemFailure.Select(visible.Select(b=>new OrbitalBuildingTarget(b.Id,b.Kind,b.Position)),center,ScanData.orbitalSystemFailureRadius,ScanData.orbitalSystemFailureMaxTargets);
            var p=context.painter2D;p.strokeColor=OrbitalTheme.Cyan;p.lineWidth=(float)ScanData.orbitalMinePreviewWidth;
            foreach(var b in visible.Where(b=>chosen.Contains(b.Id))){double r=TerritoryRules.Radius(profile,b.Kind);p.BeginPath();for(int i=0;i<=32;i++){double a=i*Math.PI/16;var at=project(new NavPoint(b.Position.X+Math.Cos(a)*r,b.Position.Z+Math.Sin(a)*r));if(i==0)p.MoveTo(at);else p.LineTo(at);}p.Stroke();}
        }
    }
}
