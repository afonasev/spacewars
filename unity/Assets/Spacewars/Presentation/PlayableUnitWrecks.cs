using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
using UnityEngine;
namespace Spacewars.Presentation
{
    public sealed partial class PlayableWorld
    {
        sealed class UnitWreck
        {public GameObject Model,Flash;public NavPoint Position;public long RevealTick,ExpireTick,DeathTick;public float FlashSeconds;public bool Air;public Vector3 Origin;public Transform[] Debris;public Vector3[] Velocities;}
        readonly Dictionary<long,UnitWreck> unitWrecks=new Dictionary<long,UnitWreck>();
        readonly Dictionary<long,long> wreckSeen=new Dictionary<long,long>();
        readonly WebProjectileVisuals wreckFlashVisuals=new WebProjectileVisuals();
        long wreckGeneration=long.MinValue,wreckTick=-1;
        public UnitWreckProfile WreckProfile{get;}=UnitWreckProfile.Load();
        public int UnitWreckCount=>unitWrecks.Values.Count(w=>!w.Air);
        public int AirDestructionCount=>unitWrecks.Values.Count(w=>w.Air);
        public void RenderUnitWrecks(PlayableSnapshot view)
        {
            if(view==null)return;WreckProfile.Validate();
            if(wreckGeneration!=view.Generation||view.Tick<wreckTick){ClearUnitWrecks();wreckGeneration=view.Generation;}
            wreckTick=view.Tick;
            // 30 is the authoritative prototype tick rate, not an art control.
            foreach(var e in view.Sounds)
            {
                if(e.Kind!=PlayableSoundKind.Destroyed||e.Building||e.UnitId<=0||e.Tick>view.Tick||wreckSeen.ContainsKey(e.Id))continue;
                wreckSeen[e.Id]=e.Tick;
                if(e.Weapon==PlayableEntityKind.Hawk){CreateAirDestruction(e,view.Tick);continue;}
                // Remove float storage noise before ceil: 0.1 s is exactly three ticks.
                long reveal=e.Tick+(long)Math.Ceiling(profile.ImpactEffectSec*30),expire=reveal+(long)Math.Ceiling(Math.Round(WreckProfile.lifetimeSeconds,6)*30d);
                if(view.Tick>=expire)continue;
                string kind=e.Weapon==PlayableEntityKind.Explorer?"explorer":e.Weapon==PlayableEntityKind.Shkval?"shkval":"tank";
                // Event identity selects one of the three already approved models, without RNG/state authority.
                var prefab=Resources.Load<GameObject>("Wrecks/"+kind+"-"+(1+e.Id%3));if(!prefab)throw new InvalidOperationException("Missing approved wreck for "+kind);
                var model=UnityEngine.Object.Instantiate(prefab,root,false);model.name="Unit wreck "+e.UnitId;Layer(model);
                model.transform.position=Point(e.Position);var slope=profile.AuthoredMap?.SurfaceGradient(e.Position)??default(NavPoint);
                model.transform.rotation=Quaternion.FromToRotation(Vector3.up,new Vector3(-(float)slope.X,1,-(float)slope.Z).normalized)*Quaternion.Euler(0,90-(float)e.HullHeading*Mathf.Rad2Deg,0);
                double scale=e.Weapon==PlayableEntityKind.Shkval?profile.ShkvalModelRadius*profile.ShkvalModelScale/2.05:e.Weapon==PlayableEntityKind.Explorer?profile.ExplorerModelRadius*profile.ExplorerModelScale/1.0773:profile.TankModelRadius*profile.TankModelScale/1.585;
                model.transform.localScale=Vector3.one*(float)scale;
                var flash=wreckFlashVisuals.ImpactFlash(root);Layer(flash);flash.transform.position=Point(e.Position);
                unitWrecks.Add(e.Id,new UnitWreck{Model=model,Flash=flash,Position=e.Position,RevealTick=reveal,ExpireTick=expire,DeathTick=e.Tick,FlashSeconds=(float)profile.ImpactEffectSec});
            }
            foreach(var pair in unitWrecks.ToArray())
            {
                var w=pair.Value;if(view.Tick>=w.ExpireTick){ReleaseWorldObject(w.Model);if(w.Flash)ReleaseWorldObject(w.Flash);unitWrecks.Remove(pair.Key);continue;}
                bool visible=view.Vision==null||view.Vision.IsVisible(w.Position);
                if(w.Air)
                {
                    double age=(view.Tick-w.DeathTick)/30d;w.Model.SetActive(visible);
                    for(int i=0;i<w.Debris.Length;i++)
                    {
                        var fragment=w.Debris[i];if(!fragment.gameObject.activeSelf)continue;
                        var at=w.Origin+w.Velocities[i]*(float)age+Vector3.down*(float)(profile.HawkDebrisGravity*age*age/2);
                        var support=profile.AuthoredMap?.SupportAt(new NavPoint(at.x,at.z));double floor=support!=null?support.HeightAt(new NavPoint(at.x,at.z)):0;
                        // This visual has no authority/collider and vanishes before touching any surface.
                        if(at.y<=floor+profile.HawkMissileRadius){fragment.gameObject.SetActive(false);continue;}
                        fragment.position=at;fragment.rotation=Quaternion.Euler((float)(age*profile.HawkRotorSpeed),(float)(i*360d/w.Debris.Length+age*profile.HawkRotorSpeed),0);
                    }
                }
                else
                w.Model.SetActive(visible&&view.Tick>=w.RevealTick);
                if(w.Flash)
                {
                    float age=(view.Tick-w.DeathTick)/30f/w.FlashSeconds;
                    if(view.Tick>=w.RevealTick){ReleaseWorldObject(w.Flash);w.Flash=null;}
                    else{w.Flash.SetActive(visible);w.Flash.transform.localScale=Vector3.one*(float)profile.ImpactEffectRadius*2*Mathf.Max(Mathf.Epsilon,age);WebProjectileVisuals.Opacity(w.Flash,1-age);}
                }
            }
            // Native transient journals retain 30 ticks. IDs remain deduplicated while the fact may recur.
            foreach(var id in wreckSeen.Where(p=>view.Tick-p.Value>30&&!unitWrecks.ContainsKey(p.Key)).Select(p=>p.Key).ToArray())wreckSeen.Remove(id);
        }
        void CreateAirDestruction(PlayableSoundEvent e,long tick)
        {
            long expire=e.Tick+(long)Math.Ceiling(profile.HawkDebrisSeconds*30);if(tick>=expire)return;
            var holder=new GameObject("Air destruction "+e.UnitId);holder.transform.SetParent(root,false);Layer(holder);
            var origin=new Vector3((float)e.Position.X,(float)e.Altitude,(float)e.Position.Z);int count=(int)profile.HawkDebrisCount;
            var debris=new Transform[count];var velocities=new Vector3[count];var color=OwnerPaint?.Invoke(e.Owner)??(e.Owner==PlayableOwner.Player?Ally:Enemy);
            for(int i=0;i<count;i++)
            {
                double angle=e.HullHeading+i*2*Math.PI/count;
                var fragment=Part("Air fragment",holder.transform,origin,Vector3.one*(float)(profile.HawkRadius/count),i%2==0?color:Dark).transform;
                var collider=fragment.GetComponent<Collider>();if(collider)collider.enabled=false;fragment.position=origin;debris[i]=fragment;velocities[i]=new Vector3((float)Math.Cos(angle),1,(float)Math.Sin(angle))*(float)profile.HawkDebrisSpeed;
            }
            var flash=wreckFlashVisuals.ImpactFlash(root);Layer(flash);flash.transform.position=origin;
            unitWrecks.Add(e.Id,new UnitWreck{Model=holder,Flash=flash,Position=e.Position,RevealTick=e.Tick+(long)Math.Ceiling(profile.ImpactEffectSec*30),ExpireTick=expire,DeathTick=e.Tick,FlashSeconds=(float)profile.ImpactEffectSec,Air=true,Origin=origin,Debris=debris,Velocities=velocities});
        }
        void ClearUnitWrecks()
        {foreach(var w in unitWrecks.Values){ReleaseWorldObject(w.Model);if(w.Flash)ReleaseWorldObject(w.Flash);}unitWrecks.Clear();wreckSeen.Clear();wreckGeneration=long.MinValue;wreckTick=-1;}
    }
}
