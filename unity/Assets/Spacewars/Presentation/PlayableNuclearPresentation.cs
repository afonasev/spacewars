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
        // The shared rocket mesh has its nose on local +X.
        private static readonly Quaternion NuclearDescentRotation = Quaternion.FromToRotation(Vector3.right, Vector3.down);
        private static readonly Color NuclearRed = new Color(1,.21f,.18f,.95f);
        private static readonly Color NuclearAmber = new Color(1,.53f,.18f,1);
        private readonly Dictionary<long,GameObject> nuclearRockets = new Dictionary<long,GameObject>();
        private bool NuclearTargeting => scanTargetAbility==OrbitalAbilities.NuclearStrike;

        private void UpdateNuclearPresentation()
        {
            var strikes=view.Orbital.NuclearStrikes;
            if(strikes.Count==0 && nuclearRockets.Count==0 && !hadNuclearMapSignals)return;
            var signals=SignalPerspectiveActive&&!view.OwnerEliminated
                ?strikes.Where(strike=>strike.AlliedSignal).ToArray():Array.Empty<OrbitalNuclearSnapshot>();
            compactMap?.SetNuclearSignals(signals);
            tacticalMap?.SetNuclearSignals(signals);
            hadNuclearMapSignals=signals.Length>0;

            foreach(long id in nuclearRockets.Keys.Where(id=>!strikes.Any(strike=>strike.Id==id &&
                strike.Phase==OrbitalNuclearPhase.Descent && strike.Center.HasValue &&
                view.Vision.IsVisible(strike.Center.Value.Position))).ToArray())
            {
                if(nuclearRockets[id])Destroy(nuclearRockets[id]);
                nuclearRockets.Remove(id);
            }
            if(projectileVisuals==null || world==null)return;
            foreach(var strike in strikes)
            {
                if(strike.Phase!=OrbitalNuclearPhase.Descent || !strike.Center.HasValue ||
                    !view.Vision.IsVisible(strike.Center.Value.Position))continue;
                float rocketScale=(float)ScanData.orbitalNuclearRocketScale;
                float rocketLength=(float)profile.ShkvalProjectileLength*rocketScale;
                if(!nuclearRockets.TryGetValue(strike.Id,out var rocket) || !rocket)
                {
                    float rocketRadius=(float)profile.ShkvalProjectileRadius*rocketScale;
                    rocket=projectileVisuals.Rocket(transform,rocketRadius,rocketLength,NuclearRed,true,additiveExhaust:false);
                    // A top-down camera looks into the nozzle during vertical flight.
                    // Keep the plume narrower than the casing so the silhouette reads.
                    projectileVisuals.Exhaust(rocket,rocketLength*.55f,rocketRadius*.8f,.48f,.48f);
                    rocket.name="Nuclear strike descent";
                    world.Layer(rocket);
                    nuclearRockets[strike.Id]=rocket;
                }
                float fall=(float)strike.Progress;
                float noseHeight=Mathf.Lerp((float)ScanData.orbitalNuclearDropHeight,0,fall*fall);
                float centerHeight=noseHeight+WebProjectileVisuals.RocketTipOffset(rocketLength);
                rocket.transform.SetPositionAndRotation(NuclearGround(strike.Center.Value)+Vector3.up*centerHeight,
                    NuclearDescentRotation);
            }
        }

        private bool hadNuclearMapSignals;

        private Vector3 NuclearGround(NavLocation location) =>
            new Vector3((float)location.Position.X,(float)OrbitalRepairNanobots.Height(profile.AuthoredMap,location)+.06f,
                (float)location.Position.Z);

        private void DrawNuclearStrikes(MeshGenerationContext context)
        {
            var painter=context.painter2D;
            foreach(var strike in view.Orbital.NuclearStrikes)
            {
                var center=strike.Center;
                if(strike.Phase==OrbitalNuclearPhase.Impact)
                {
                    if(center.HasValue)DrawNuclearBlast(context,center.Value,(float)strike.Radius,(float)strike.Progress);
                    continue;
                }
                var style=NuclearWarningArtwork.Style;
                if(center.HasValue)
                {
                    var location=center.Value;
                    NuclearWarningArtwork.DrawInterior(context,location.Position,(float)strike.Radius,(float)strike.Progress,
                        strike.Phase==OrbitalNuclearPhase.Descent,view.Vision,
                        point=>RepairPixel(NuclearGround(new NavLocation(point,location.SurfaceId))));
                }
                void Rim(Color color,float width)
                {
                    painter.strokeColor=color;painter.lineWidth=width;painter.BeginPath();
                    foreach(var segment in strike.VisibleRim)
                    {
                        painter.MoveTo(RepairPixel(NuclearGround(segment.From)));
                        painter.LineTo(RepairPixel(NuclearGround(segment.To)));
                    }
                    if(strike.VisibleRim.Count>0)painter.Stroke();
                }
                float lineWidth=(float)ScanData.orbitalNuclearReticleWidth*style.widthScale;
                var glow=style.color;glow.a=style.glowOpacity;
                Rim(glow,lineWidth+style.glowPixels);
                Rim(style.outline,lineWidth+style.outlinePixels*2);
                Rim(style.color,lineWidth);
            }
        }

        private void DrawNuclearTerrainRing(Painter2D painter,NavLocation center,float radius,Color color,float width)
        {
            var origin=NuclearGround(center);
            painter.strokeColor=color;
            painter.lineWidth=width;
            painter.BeginPath();
            for(int i=0;i<=64;i++)
            {
                float angle=i*Mathf.PI/32;
                var point=RepairPixel(origin+new Vector3(Mathf.Cos(angle)*radius,0,Mathf.Sin(angle)*radius));
                if(i==0)painter.MoveTo(point);else painter.LineTo(point);
            }
            painter.Stroke();
        }

        internal static void DrawNuclearGlyph(Painter2D painter,Vector2 at,float radius)
        {
            painter.strokeColor=NuclearRed;
            painter.lineWidth=Mathf.Clamp(radius*.14f,1,2.6f);
            painter.BeginPath();
            painter.Arc(at,radius,0,360);
            painter.Stroke();
            painter.BeginPath();
            foreach(var direction in new[]{Vector2.up,Vector2.right,Vector2.down,Vector2.left})
            {
                painter.MoveTo(at+direction*radius*.70f);
                painter.LineTo(at+direction*radius*1.34f);
            }
            painter.Stroke();
        }

        private void DrawNuclearBlast(MeshGenerationContext context,NavLocation center,float radius,float age)
        {
            var ground=NuclearGround(center);
            float scale=Vector2.Distance(RepairPixel(ground),RepairPixel(ground+Vector3.right*radius));
            if(scale<1)return;
            var painter=context.painter2D;
            float waveRadius=radius*(float)ScanData.orbitalNuclearWaveScale*Mathf.Lerp(.12f,1.25f,Mathf.Sqrt(age));
            float waveAlpha=Mathf.Max(0,1-age*1.7f);
            DrawNuclearTerrainRing(painter,center,waveRadius,new Color(1,.31f,.07f,waveAlpha*.32f),8*(1-age)+1);
            DrawNuclearTerrainRing(painter,center,waveRadius,new Color(1,.70f,.31f,waveAlpha),1.7f);

            Vector2 origin=RepairPixel(ground);
            float smoke=(float)ScanData.orbitalNuclearSmokeOpacity*(1-age);
            for(int i=0;i<12;i++)
            {
                float angle=i*2.399963f;
                var drift=new Vector2(Mathf.Cos(angle),Mathf.Sin(angle)*.48f);
                var position=origin+drift*scale*Mathf.Lerp(.06f,.58f,age)+Vector2.up*scale*(.09f+age*.36f)*(i%4+1)/4;
                DrawNuclearGlow(context,position,scale*(.11f+age*.19f),new Color(.26f,.20f,.19f,smoke*.55f));
            }
            float flash=Mathf.Clamp01((float)ScanData.orbitalNuclearFlashStrength*Mathf.Pow(1-age,4));
            Vector2 flame=origin+Vector2.up*scale*(.11f+age*.36f);
            DrawNuclearGlow(context,flame,scale*(.48f+age*.19f),new Color(1,.36f,.055f,flash*.72f));
            DrawNuclearGlow(context,flame,scale*(.25f+age*.09f),new Color(1,.79f,.43f,flash));
            DrawNuclearGlow(context,flame,scale*.12f,new Color(1,.97f,.81f,flash));
            if(age>=.75f)return;
            painter.lineWidth=1.25f;
            painter.strokeColor=new Color(NuclearAmber.r,NuclearAmber.g,NuclearAmber.b,1-age/.75f);
            painter.BeginPath();
            for(int i=0;i<30;i++)
            {
                float angle=i*2.399963f;
                var direction=new Vector2(Mathf.Cos(angle),Mathf.Sin(angle)*.57f);
                float distance=scale*(.2f+age*(.9f+(i%5)*.22f));
                var tip=origin+direction*distance+Vector2.up*scale*(.10f+Mathf.Sin(age*Mathf.PI)*(.16f+i%3*.11f));
                painter.MoveTo(tip-direction*scale*.045f);
                painter.LineTo(tip);
            }
            painter.Stroke();
        }

        private static void DrawNuclearGlow(MeshGenerationContext context,Vector2 center,float radius,Color color)
        {
            const int sides=20;
            var mesh=context.Allocate(sides+1,sides*3);
            mesh.SetNextVertex(new Vertex{position=new Vector3(center.x,center.y,Vertex.nearZ),tint=color});
            color.a=0;
            for(int i=0;i<sides;i++)
            {
                float angle=i*Mathf.PI*2/sides;
                var point=center+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*radius;
                mesh.SetNextVertex(new Vertex{position=new Vector3(point.x,point.y,Vertex.nearZ),tint=color});
                mesh.SetNextIndex(0);
                mesh.SetNextIndex((ushort)(i+1));
                mesh.SetNextIndex((ushort)((i+1)%sides+1));
            }
        }
    }
}
