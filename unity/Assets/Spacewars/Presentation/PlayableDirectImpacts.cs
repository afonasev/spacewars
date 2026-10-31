using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
using UnityEngine;

namespace Spacewars.Presentation
{
    public sealed partial class PlayableBootstrap
    {
        private readonly Dictionary<long,DirectContactVisual> directContacts=new Dictionary<long,DirectContactVisual>();
        private readonly DirectContactSurface directContactSurface=new DirectContactSurface();
        private TankMechanismProfile tankImpactArt;
        private long directContactGeneration=long.MinValue;
        private void ClearDirectContacts()
        {
            foreach(var hit in directContacts.Values){Destroy(hit.Root);if(hit.Trail)Destroy(hit.Trail);}
            directContacts.Clear();directContactSurface.Clear();directContactGeneration=long.MinValue;
        }
        private void RenderDirectContacts()
        {
            if(tankImpactArt==null)tankImpactArt=TankMechanismProfile.Load();
            if(directContactGeneration!=view.Generation){ClearDirectContacts();directContactGeneration=view.Generation;}
            var visible=new HashSet<long>();
            foreach(var e in view.Sounds)
            {
                if(e.Kind!=PlayableSoundKind.Impact||e.Contact==null||e.Tick>view.Tick)continue;
                double seconds=(view.Tick-e.Tick)/30d;
                double lifetime=e.Weapon==PlayableEntityKind.Tank?tankImpactArt.impactSeconds:profile.DirectImpactSeconds;
                if(seconds>=lifetime)continue;
                visible.Add(e.Id);
                if(!directContacts.TryGetValue(e.Id,out var hit))
                {
                    Vector3 from=new Vector3((float)e.Contact.From.X,(float)e.Contact.From.Y,(float)e.Contact.From.Z);
                    if(shells.TryGetValue(e.Contact.ProjectileId,out var last))from=last.transform.position;
                    world.Actors.TryGetValue(e.Contact.TargetId,out var actor);
                    var fallback=e.Contact.Impact.HasValue?new Vector3((float)e.Contact.Impact.Value.X,(float)e.Contact.Impact.Value.Y,(float)e.Contact.Impact.Value.Z):world.Point(e.Position);
                    Vector3 point=e.Building&&e.Contact.Impact.HasValue?fallback:e.Weapon==PlayableEntityKind.Hawk
                        ?(actor!=null?directContactSurface.ResolveRocket(actor.Hull,from,fallback):directContactSurface.ResolveObstacle(world.GraphicsRoot,world.Actors.Values.Select(a=>a.Root),from,fallback,(float)profile.HawkMissileLength))
                        :directContactSurface.Resolve(actor?.Hull,from,fallback);
                    hit=new DirectContactVisual{Root=projectileVisuals.DirectImpact(transform,e.Contact.ProjectileId,e.Weapon==PlayableEntityKind.Tank),Point=point,Tick=e.Tick,PresentedTick=view.Tick,
                        Target=actor?.Hull,Scale=e.Weapon==PlayableEntityKind.Explorer?(float)profile.ExplorerImpactScale:1};
                    if(e.Weapon==PlayableEntityKind.Tank&&(from-point).sqrMagnitude>.000001f)hit.Root.transform.rotation=Quaternion.LookRotation(from-point);
                    if(hit.Target)hit.LocalPoint=hit.Target.InverseTransformPoint(point);
                    // One terminal segment joins the previous shell to the visible surface,
                    // including shells fast enough to spawn and hit between rendered frames.
                    float length=Vector3.Distance(from,point);
                    if(length>Mathf.Epsilon)
                    {
                        var direction=(point-from).normalized;
                        if(e.Weapon==PlayableEntityKind.Hawk)
                        {
                            hit.Trail=projectileVisuals.HawkRocket(transform,(float)profile.HawkMissileRadius,(float)profile.HawkMissileLength,Color.gray);
                            hit.Trail.name="Terminal Hawk missile";
                            hit.Trail.transform.localScale=Vector3.one*(float)profile.HawkMissileVisualScale;
                            // The missile has already struck; do not leave Rocket's unconfigured exhaust cone visible.
                            hit.Trail.transform.Find("rocket-exhaust").gameObject.SetActive(false);
                            hit.Trail.transform.position=point-direction*WebProjectileVisuals.RocketTipOffset((float)profile.HawkMissileLength)*(float)profile.HawkMissileVisualScale;
                        }
                        else
                        {
                            float thickness=e.Weapon==PlayableEntityKind.Explorer?(float)profile.ExplorerTracerThickness:(float)profile.TankProjectileCollisionRadius;
                            hit.Trail=projectileVisuals.Tracer(transform,length,thickness);
                            hit.Trail.name="Terminal projectile segment";hit.Trail.transform.position=(from+point)/2;
                        }
                        hit.Trail.transform.rotation=Quaternion.FromToRotation(Vector3.right,direction);world.Layer(hit.Trail);
                    }
                    world.Layer(hit.Root);directContacts.Add(e.Id,hit);
                }
                if(hit.Target)hit.Point=hit.Target.TransformPoint(hit.LocalPoint);
                if(e.Weapon==PlayableEntityKind.Hawk&&hit.Trail)
                    hit.Trail.transform.position=hit.Point-hit.Trail.transform.right*WebProjectileVisuals.RocketTipOffset((float)profile.HawkMissileLength)*(float)profile.HawkMissileVisualScale;
                hit.Root.transform.position=hit.Point;
                float age=Mathf.Clamp01((float)(seconds/lifetime));
                projectileVisuals.UpdateDirectImpact(hit.Root,age,(float)profile.DirectImpactRadius*hit.Scale,(float)profile.DirectImpactSparkLength*hit.Scale,(float)profile.DirectImpactSparkWidth,(float)profile.DirectImpactGlow,(float)profile.DirectImpactSmokeOpacity*hit.Scale);
                if(hit.Trail&&view.Tick>hit.PresentedTick){Destroy(hit.Trail);hit.Trail=null;}
            }
            foreach(var id in directContacts.Keys.Where(id=>!visible.Contains(id)).ToArray())
            {var hit=directContacts[id];Destroy(hit.Root);if(hit.Trail)Destroy(hit.Trail);directContacts.Remove(id);}
        }
    }
}
