using System;
using System.Linq;
using UnityEngine;
using Spacewars.Simulation;
namespace Spacewars.Presentation
{
 public sealed partial class PlayableBootstrap
 {
  private readonly System.Collections.Generic.Dictionary<int,ShkvalLaunchSnapshot> observedShkvalLaunches=new System.Collections.Generic.Dictionary<int,ShkvalLaunchSnapshot>();
  private readonly struct ShkvalMuzzleFrame{public readonly Vector3 Position,Direction;public ShkvalMuzzleFrame(Vector3 position,Vector3 direction){Position=position;Direction=direction;}}
  private readonly System.Collections.Generic.Dictionary<int,ShkvalMuzzleFrame> shkvalLaunchFrames=new System.Collections.Generic.Dictionary<int,ShkvalMuzzleFrame>();
  private void UpdateShkvalLaunchPresentation()
  {
   foreach(var e in view.Entities)
   {
    if(e.Kind!=PlayableEntityKind.Shkval||!world.Actors.TryGetValue(e.Id,out var actor)||!actor.Launcher)continue;
    // The weapon pose and physical release share this immutable simulation sample.
    actor.Turret.localRotation=Quaternion.Euler(0,(float)(e.HullHeading-e.TurretHeading)*Mathf.Rad2Deg,0);
    world.AimExplorer(actor,e);
    var carriage=actor.Launcher.Find("launchCarriage");if(!carriage)continue;
    double age=double.PositiveInfinity;
    // Cell timers survive immediate impacts and save/restore, and stop with simulation pause.
    if(e.ShkvalCells!=null){double remaining=0;for(int c=0;c<4;c++)remaining=Math.Max(remaining,e.ShkvalCells.Remaining(c));if(remaining>0)age=2*profile.ShkvalFireIntervalMs/1000d-remaining;}
    else foreach(var shot in view.Projectiles)if(shot.Visible&&shot.Kind==PlayableEntityKind.Shkval&&shot.OwnerId==e.Id)age=Math.Min(age,shot.Age);
    float recoil=ShkvalHullRecoil(age);
    var back=actor.Root.InverseTransformDirection(actor.Turret.forward);back.y=0;
    actor.Hull.localPosition=-back.normalized*recoil;
    float phase=(float)(age/ShkvalArt.settleSeconds);
    float travel=phase>=0&&phase<1?ShkvalArt.launchTravel*Mathf.Sin(phase*Mathf.PI*2)*(1-phase):0;
    carriage.localPosition=Vector3.back*travel;
    // Four authored channels are a geometry invariant, not a salvo/balance setting.
    if(actor.LoadedRockets==null)actor.LoadedRockets=new Transform[4];
    for(int cell=0;cell<4;cell++){
     if(!actor.LoadedRockets[cell]){
      var loaded=projectileVisuals.LoadedRocket(actor.Root,(float)profile.ShkvalProjectileRadius,(float)profile.ShkvalProjectileLength,ShotColor(e.Owner));
      loaded.name="Shkval loaded rocket "+cell;world.Layer(loaded);actor.LoadedRockets[cell]=loaded.transform;
     }
     var rocket=actor.LoadedRockets[cell];var muzzle=carriage.Find("muzzle"+cell);
     if(!muzzle)throw new InvalidOperationException("Missing Shkval muzzle");
     double remaining=e.ShkvalCells?.Remaining(cell)??0;
     double interval=profile.ShkvalFireIntervalMs/1000d;
     rocket.gameObject.SetActive(remaining<=interval);
     float progress=Mathf.SmoothStep(0,1,1-Mathf.Clamp01((float)(remaining/interval)));
     var direction=muzzle.forward;
     rocket.position=muzzle.position-direction*(WebProjectileVisuals.RocketTipOffset((float)profile.ShkvalProjectileLength)*(float)ShkvalLaunchGeometry.RocketScale-(float)ShkvalLaunchGeometry.LoadedProtrusion+ShkvalArt.loadingTravel*(1-progress));
     rocket.rotation=Quaternion.Euler(0,-Mathf.Atan2(direction.z,direction.x)*Mathf.Rad2Deg,0)*Quaternion.Euler(0,0,Mathf.Atan2(direction.y,new Vector2(direction.x,direction.z).magnitude)*Mathf.Rad2Deg);
     rocket.localScale=Vector3.one*ShkvalArt.rocketScale;
    }
   }
  }
  private GameObject CreateShkvalFlight(PlayableProjectileSnapshot shot)
  {
   var launch=shot.Launch;GameObject rocket=null;
   if(launch!=null&&world.Actors.TryGetValue(shot.OwnerId,out var actor)&&actor.LoadedRockets!=null)
   {
    var source=view.Entities.FirstOrDefault(e=>e.Id==shot.OwnerId);int cell=launch.Barrel;
    double interval=profile.ShkvalFireIntervalMs/1000d;double remaining=source?.ShkvalCells?.Remaining(cell)??0;
    // Match the saved launch clock, not merely the barrel: a late/fog-restored old shot
    // must not steal the new rocket now feeding or ready in that same channel.
    // Runtime fires before advancing projectiles in its fixed 30 Hz step; therefore
    // the first projectile snapshot can be one tick older than its freshly reset cell.
    double clockOffset=shot.Age-(2*interval-remaining);
    bool sameLaunch=Math.Abs(clockOffset)<.000001||Math.Abs(clockOffset-1d/30d)<.000001;
    if(remaining>interval&&sameLaunch&&actor.LoadedRockets[cell])
    {
     var loaded=actor.LoadedRockets[cell];rocket=loaded.gameObject;
     var replacement=projectileVisuals.LoadedRocket(actor.Root,(float)profile.ShkvalProjectileRadius,(float)profile.ShkvalProjectileLength,ShotColor(source.Owner));
     replacement.name="Shkval loaded rocket "+cell;world.Layer(replacement);
     replacement.transform.position=loaded.position;replacement.transform.rotation=loaded.rotation;replacement.transform.localScale=loaded.localScale;replacement.SetActive(false);
     actor.LoadedRockets[cell]=replacement.transform;
     loaded.SetParent(transform,true);rocket.SetActive(true);
    }
   }
   if(!rocket)rocket=launch!=null?projectileVisuals.LoadedRocket(transform,(float)profile.ShkvalProjectileRadius,(float)profile.ShkvalProjectileLength,ShotColor(shot.Faction)):projectileVisuals.Rocket(transform,(float)profile.ShkvalProjectileRadius,(float)profile.ShkvalProjectileLength,ShotColor(shot.Faction),true);
   projectileVisuals.IgniteRocket(rocket,(float)profile.ShkvalProjectileLength,true);return rocket;
  }
  private float ShkvalHullRecoil(double age)
  {
   if(age<0||age>=ShkvalArt.hullRecoilAttackSeconds+ShkvalArt.hullRecoilReturnSeconds)return 0;
   float phase=age<ShkvalArt.hullRecoilAttackSeconds?Mathf.SmoothStep(0,1,(float)age/ShkvalArt.hullRecoilAttackSeconds):1-Mathf.SmoothStep(0,1,((float)age-ShkvalArt.hullRecoilAttackSeconds)/ShkvalArt.hullRecoilReturnSeconds);
   return ShkvalArt.hullRecoilTravel*phase;
  }
  private ShkvalMuzzleFrame ShkvalMuzzle(ShkvalLaunchSnapshot l)
  {
   var slope=profile.AuthoredMap?.SurfaceGradient(l.Ground.Ground)??default(NavPoint);
   var frame=ShkvalLaunchGeometry.At(l.Ground,l.Hull,l.Turret,l.Pitch,l.Upgraded,l.Barrel,profile.ShkvalModelRadius*profile.ShkvalModelScale/2.05,profile.ShkvalProjectileLength,slope);
   return new ShkvalMuzzleFrame(new Vector3((float)frame.Mouth.X,(float)frame.Mouth.Y,(float)frame.Mouth.Z),new Vector3((float)frame.Direction.X,(float)frame.Direction.Y,(float)frame.Direction.Z));
  }

  private void UpdateShkvalExhaustClip(PlayableProjectileSnapshot shot,GameObject rocket)
  {
   var plane=Vector4.zero;bool clip=false,visible=true;
   if(shkvalLaunchFrames.TryGetValue(shot.Id,out var frame)){
    var point=frame.Position;var normal=frame.Direction;
    // During ejection follow the actual rendered/recoiling mouth when observable.
    // Restored or no-longer-visible sources use their immutable observed launch frame.
    if(shot.Age<ShkvalArt.exhaustMuzzleFollowSeconds&&observedShkvalLaunches.TryGetValue(shot.Id,out var launch)&&world.Actors.TryGetValue(shot.OwnerId,out var actor)&&actor.Root.gameObject.activeInHierarchy&&actor.UnitUpgraded==launch.Upgraded&&actor.Launcher){
     var muzzle=actor.Launcher.Find("launchCarriage/muzzle"+launch.Barrel);if(muzzle){point=muzzle.position;normal=muzzle.forward;}
    }
    plane=new Vector4(normal.x,normal.y,normal.z,-Vector3.Dot(normal,point));clip=true;
    visible=Vector3.Dot(normal,rocket.transform.Find("rocket-exhaust").position-point)>0;
   }
   projectileVisuals.ClipRocketExhaust(rocket,plane,clip,visible);
  }
  private void ApplyShkvalMuzzlePose(PlayableProjectileSnapshot shot,Transform shell)
  {
   shell.localScale=Vector3.one*ShkvalArt.rocketScale;
   if(shot.Launch!=null)observedShkvalLaunches[shot.Id]=shot.Launch;
   if(!observedShkvalLaunches.TryGetValue(shot.Id,out var l))return;
   if(!shkvalLaunchFrames.TryGetValue(shot.Id,out var frame)){frame=ShkvalMuzzle(l);shkvalLaunchFrames.Add(shot.Id,frame);}
   // Flight position and tangent are already authoritative, including at release.
  }
 }
}
