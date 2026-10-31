using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.UIElements;
namespace Spacewars.Presentation
{
    // Selection/map rings and transient accepted-order Vector confirmation; no route requests.
    public sealed class PlayableOrderMarkerLayer : VisualElement
    {
        IReadOnlyList<PlayableQueueMarker> markers=Array.Empty<PlayableQueueMarker>();
        readonly Func<NavLocation,Vector2?> project;readonly bool compact;readonly EnvironmentArtProfile art;
        public int VisibleCount=>markers.Count;
        public PlayableOrderMarkerLayer(Func<NavLocation,Vector2?> project,bool compact=false,EnvironmentArtProfile art=null)
        {
            this.project=project;this.compact=compact;this.art=art??EnvironmentArtProfile.Load();this.art.Validate();pickingMode=PickingMode.Ignore;name="tactical-order-markers";
            style.position=Position.Absolute;style.left=0;style.right=0;style.top=0;style.bottom=0;generateVisualContent+=Draw;
        }
        public void Set(IReadOnlyList<PlayableQueueMarker> next){markers=next??Array.Empty<PlayableQueueMarker>();MarkDirtyRepaint();}
        void Draw(MeshGenerationContext ctx)
        {
            var p=ctx.painter2D;// Preserve the established map-to-field proportions while tuning the shared visual profile.
            float radius=art.orderMarkerRadius*(compact?.5f:1),font=art.orderMarkerFontSize*(compact?.625f:1);
            foreach(var marker in markers)
            {
                if(!marker.Location.HasValue)continue;var point=project(marker.Location.Value);if(!point.HasValue)continue;
                var at=point.Value;if(at.x<0||at.y<0||at.x>contentRect.width||at.y>contentRect.height)continue;
                if(!compact&&marker.ConfirmationElapsed>=0)
                {
                    DrawVector(p,at,marker.Attack,(float)(marker.ConfirmationElapsed/art.orderConfirmationDuration));
                    continue;
                }
                var color=marker.Attack?Color.red:Color.green;
                p.lineWidth=art.orderMarkerWidth;p.strokeColor=color;p.BeginPath();p.Arc(at,radius,0,360);p.Stroke();
                var text=marker.Number.ToString();ctx.DrawText(text,at-new Vector2(text.Length*font*.28f,font*.55f),font,color,null);
            }
        }
        void DrawVector(Painter2D p,Vector2 at,bool attack,float t)
        {
            if(t<0||t>=1)return;
            // The 3/4-way silhouettes and polygon proportions define the approved Vector icon.
            // Timing, size, excursion, recoil, opacity and stroke are designer-facing profile values.
            float approach=Mathf.Clamp01(t/art.orderConfirmationConverge);
            float radius=art.orderConfirmationRadius*(1+art.orderConfirmationTravel*Mathf.Pow(1-approach,3));
            float recoil=Mathf.Clamp01((t-art.orderConfirmationConverge)/(art.orderConfirmationFadeStart-art.orderConfirmationConverge));
            radius+=art.orderConfirmationRadius*art.orderConfirmationRecoil*Mathf.Sin(recoil*Mathf.PI);
            float fade=Mathf.SmoothStep(1,0,Mathf.InverseLerp(art.orderConfirmationFadeStart,1,t));
            var color=attack?new Color(1,.08f,.06f):new Color(.08f,1,.12f);color.a=fade*art.orderConfirmationOpacity;
            int count=attack?4:3;
            for(int i=0;i<count;i++)
            {
                float angle=(-90+i*360f/count)*Mathf.Deg2Rad;
                var axis=new Vector2(Mathf.Cos(angle),Mathf.Sin(angle));var side=new Vector2(-axis.y,axis.x);
                float length=art.orderConfirmationRadius*.8f,width=art.orderConfirmationRadius*.3f;
                var tip=at+axis*(radius-length*.65f);
                var outer=at+axis*(radius+length*.35f);
                p.fillColor=color;p.strokeColor=new Color(0,.04f,.025f,color.a*.85f);p.lineWidth=art.orderConfirmationWidth;
                p.BeginPath();p.MoveTo(tip);p.LineTo(outer+side*width);p.LineTo(outer-axis*length*.25f);p.LineTo(outer-side*width);p.ClosePath();p.Fill();p.Stroke();
            }
            p.fillColor=color;p.strokeColor=color;p.lineWidth=art.orderConfirmationWidth;
            float center=art.orderConfirmationRadius*.16f;
            if(attack){p.BeginPath();p.MoveTo(at-new Vector2(center,0));p.LineTo(at+new Vector2(center,0));p.MoveTo(at-new Vector2(0,center));p.LineTo(at+new Vector2(0,center));p.Stroke();}
            else{p.BeginPath();p.Arc(at,art.orderConfirmationRadius*.2f,0,360);p.Fill();}
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
