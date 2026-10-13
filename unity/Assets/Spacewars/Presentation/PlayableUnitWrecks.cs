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
        {public GameObject Model,Flash;public NavPoint Position;public long RevealTick,ExpireTick,DeathTick;public float FlashSeconds;}
        readonly Dictionary<long,UnitWreck> unitWrecks=new Dictionary<long,UnitWreck>();
        readonly Dictionary<long,long> wreckSeen=new Dictionary<long,long>();
        readonly WebProjectileVisuals wreckFlashVisuals=new WebProjectileVisuals();
        long wreckGeneration=long.MinValue,wreckTick=-1;
        public UnitWreckProfile WreckProfile{get;}=UnitWreckProfile.Load();
        public int UnitWreckCount=>unitWrecks.Count;
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
        void ClearUnitWrecks()
        {foreach(var w in unitWrecks.Values){ReleaseWorldObject(w.Model);if(w.Flash)ReleaseWorldObject(w.Flash);}unitWrecks.Clear();wreckSeen.Clear();wreckGeneration=long.MinValue;wreckTick=-1;}
    }
}
