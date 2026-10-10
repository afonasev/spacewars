using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.UIElements;
namespace Spacewars.Presentation
{
    // Approved A rings; no permanent chain or route/reservation requests.
    public sealed class PlayableOrderMarkerLayer : VisualElement
    {
        IReadOnlyList<PlayableQueueMarker> markers=Array.Empty<PlayableQueueMarker>();
        readonly Func<NavLocation,Vector2?> project;readonly bool compact;
        public int VisibleCount=>markers.Count;
        public PlayableOrderMarkerLayer(Func<NavLocation,Vector2?> project,bool compact=false)
        {
            this.project=project;this.compact=compact;pickingMode=PickingMode.Ignore;name="tactical-order-markers";
            style.position=Position.Absolute;style.left=0;style.right=0;style.top=0;style.bottom=0;generateVisualContent+=Draw;
        }
        public void Set(IReadOnlyList<PlayableQueueMarker> next){markers=next??Array.Empty<PlayableQueueMarker>();MarkDirtyRepaint();}
        void Draw(MeshGenerationContext ctx)
        {
            var p=ctx.painter2D;float radius=compact?6:12,font=compact?10:16;
            foreach(var marker in markers)
            {
                if(!marker.Location.HasValue)continue;var point=project(marker.Location.Value);if(!point.HasValue)continue;
                var at=point.Value;if(at.x<0||at.y<0||at.x>contentRect.width||at.y>contentRect.height)continue;
                var color=marker.Attack?Color.red:Color.green;bool active=(marker.Phase&QueueMarkerPhase.Active)!=0;
                p.lineWidth=2;p.strokeColor=color;p.fillColor=active?color:new Color(.05f,.08f,.1f,.9f);p.BeginPath();p.Arc(at,radius,0,360);p.Fill();p.Stroke();
                var text=marker.Number.ToString();ctx.DrawText(text,at-new Vector2(text.Length*font*.28f,font*.55f),font,active?Color.black:color,null);
            }
        }
        public static Vector2? WorldPoint(VisualElement root,Camera camera,PlayableProfile profile,NavLocation location,bool mirrored=false)
        {
            if(root?.panel==null||camera==null)return null;var p=location.Position;double height=profile.AuthoredMap?.Supports.FirstOrDefault(s=>s.Id==location.SurfaceId)?.HeightAt(p)??profile.AuthoredMap?.SurfaceHeight(p)??0;
            var world=new Vector3((float)p.X,(float)height+.1f,(float)p.Z);
            var viewport=camera.WorldToViewportPoint(world);if(viewport.z<=0||viewport.x<0||viewport.x>1||viewport.y<0||viewport.y>1)return null;
            if(mirrored)viewport.x=1-viewport.x;
            if(camera.targetTexture!=null)return new Vector2((camera.rect.x+viewport.x*camera.rect.width)*root.contentRect.width,(1-camera.rect.y-viewport.y*camera.rect.height)*root.contentRect.height);
            var screen=camera.WorldToScreenPoint(world);if(mirrored)screen.x=camera.pixelRect.xMax-(screen.x-camera.pixelRect.x);return RuntimePanelUtils.ScreenToPanel(root.panel,new Vector2(screen.x,Screen.height-screen.y));
        }
    }
}
