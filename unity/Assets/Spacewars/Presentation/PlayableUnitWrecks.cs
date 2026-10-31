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
        {public ModelDetailLod Detail;public GameObject Model,Flash,Smoke,Hit;public PlayableWreckSnapshot State;public Transform[] Debris;public Vector3[] Velocities;}
        readonly Dictionary<int,UnitWreck> unitWrecks=new Dictionary<int,UnitWreck>();
        readonly WebProjectileVisuals wreckFlashVisuals=new WebProjectileVisuals();
        readonly WreckSmokeVisuals wreckSmokeVisuals=new WreckSmokeVisuals();
        Color? wreckKeyColor;
        Color WreckKeyColor{get{if(!wreckKeyColor.HasValue){var light=IndustrialLightingProfile.Load(profile.AuthoredMap?.Id);wreckKeyColor=Color.HSVToRGB(light.sunHue,light.sunSaturation,1);}return wreckKeyColor.Value;}}
        long wreckGeneration=long.MinValue,wreckTick=-1;
        public UnitWreckProfile WreckProfile{get;}=UnitWreckProfile.Load();
        public int UnitWreckCount=>unitWrecks.Values.Count(w=>w.State.HasGround&&w.State.DestroyedTick<0);
        public int AirDestructionCount=>unitWrecks.Values.Count(w=>w.State.Kind==PlayableEntityKind.Hawk&&!w.State.HasGround&&wreckTick<w.State.RevealTick+(long)Math.Ceiling(profile.HawkDebrisSeconds*30));
        // Keep approved prefab/LOD resources resident before combat begins. Previously
        // the first death synchronously loaded a mesh catalogue on the render thread.
        readonly Dictionary<string,UnityEngine.Object> wreckResources=new Dictionary<string,UnityEngine.Object>();
        System.Collections.IEnumerator PrepareWreckResources()
        {
            foreach(string kind in new[]{"tank","explorer","shkval","hawk"})foreach(bool upgraded in new[]{false,true})
                for(int variant=1;variant<=(kind=="hawk"?4:3);variant++){
                    string key=kind+(upgraded?"-upgraded-":"-")+variant;
                    foreach(string prefix in new[]{"Wrecks/","ModelDetail/Wrecks/"}){
                        // Synchronous Drain tooling and staged loading use the same path;
                        // yield between resources, never depend on an ignored async request.
                        var asset=Resources.Load<UnityEngine.Object>(prefix+key);
                        if(asset)wreckResources[prefix+key]=asset;
                        yield return null;
                    }
                    if(wreckResources.TryGetValue("Wrecks/"+key,out var prefab)&&prefab is GameObject model)
                        wreckDetailBindings[key]=DescribeWreckDetail(model,key);
                    yield return null;
                }
        }
        public void RenderUnitWrecks(PlayableSnapshot view)
        {
            if(view==null)return;
            if(wreckGeneration!=view.Generation||view.Tick<wreckTick){ClearUnitWrecks();wreckGeneration=view.Generation;}
            wreckTick=view.Tick;var current=new HashSet<int>(view.Wrecks.Select(w=>w.Id));
            foreach(var id in unitWrecks.Keys.Where(id=>!current.Contains(id)).ToArray()){ReleaseWreck(unitWrecks[id]);unitWrecks.Remove(id);}
            foreach(var state in view.Wrecks){
                if(unitWrecks.TryGetValue(state.Id,out var previous)&&previous.State.HasGround!=state.HasGround){ReleaseWreck(previous);unitWrecks.Remove(state.Id);}
                if(!unitWrecks.TryGetValue(state.Id,out var w)){
                    w=new UnitWreck{State=state};unitWrecks.Add(state.Id,w);
                    if(state.HasGround){
                        string kind=state.Kind==PlayableEntityKind.Hawk?"hawk":state.Kind==PlayableEntityKind.Explorer?"explorer":state.Kind==PlayableEntityKind.Shkval?"shkval":"tank";
                        var prefab=Resources.Load<GameObject>("Wrecks/"+kind+(state.Upgraded?"-upgraded-":"-")+state.Variant);if(!prefab)throw new InvalidOperationException("Missing approved wreck for "+kind);
                        w.Model=UnityEngine.Object.Instantiate(prefab,root,false);w.Model.name="Unit wreck "+state.Id;Layer(w.Model);w.Detail=BindWreckDetail(w.Model,kind+(state.Upgraded?"-upgraded-":"-")+state.Variant);
                        double scale=state.Kind==PlayableEntityKind.Hawk?profile.HawkModelScale:state.Kind==PlayableEntityKind.Shkval?profile.ShkvalModelRadius*profile.ShkvalModelScale/2.05:state.Kind==PlayableEntityKind.Explorer?profile.ExplorerModelRadius*profile.ExplorerModelScale/1.0773:profile.TankModelRadius*profile.TankModelScale/1.585;
                        w.Model.transform.localScale=Vector3.one*(float)scale;
                        foreach(var renderer in w.Model.GetComponentsInChildren<MeshRenderer>())MaterialFinish?.ApplyWreck(renderer,WreckKeyColor);
                    }
                    if(state.Kind==PlayableEntityKind.Hawk&&!state.HasGround){
                        w.Model=new GameObject("Air destruction "+state.Id);w.Model.transform.SetParent(root,false);Layer(w.Model);
                        int count=(int)profile.HawkDebrisCount;w.Debris=new Transform[count];w.Velocities=new Vector3[count];var color=OwnerPaint?.Invoke(state.Owner)??(state.Owner==PlayableOwner.Player?Ally:Enemy);
                        for(int i=0;i<count;i++){double angle=state.Heading+i*2*Math.PI/count;var fragment=Part("Air fragment",w.Model.transform,Vector3.zero,Vector3.one*(float)(profile.HawkRadius/count),i%2==0?color:Dark).transform;var collider=fragment.GetComponent<Collider>();if(collider)collider.enabled=false;w.Debris[i]=fragment;w.Velocities[i]=new Vector3((float)Math.Cos(angle),1,(float)Math.Sin(angle))*(float)profile.HawkDebrisSpeed;}
                    }
                    w.Flash=wreckFlashVisuals.ImpactFlash(root);Layer(w.Flash);
                    w.Smoke=wreckSmokeVisuals.Create(root,state.Id);Layer(w.Smoke);
                }
                w.State=state;bool visible=view.Vision==null||view.Vision.IsVisible(state.Position);
                var ground=Point(state.Position);long effectTick=state.HasGround?state.DeathTick:state.RevealTick;double age=(view.Tick-effectTick)/30d;
                double fall=state.Kind==PlayableEntityKind.Hawk&&state.HasGround?Math.Max(0,Math.Min(1,(double)(view.Tick-state.DeathTick)/Math.Max(1,state.LandTick-state.DeathTick))):state.Kind==PlayableEntityKind.Hawk?0:1;
                var at=ground+Vector3.up*(float)((state.DeathHeight-ground.y)*(1-fall*fall));
                if(w.Debris!=null){
                    w.Model.SetActive(visible&&age<profile.HawkDebrisSeconds);
                    for(int i=0;i<w.Debris.Length;i++){var fragment=w.Debris[i];var pos=at+w.Velocities[i]*(float)age+Vector3.down*(float)(profile.HawkDebrisGravity*age*age/2);double floor=profile.AuthoredMap?.SurfaceHeight(new NavPoint(pos.x,pos.z))??0;fragment.gameObject.SetActive(pos.y>floor+profile.HawkMissileRadius);fragment.position=pos;fragment.rotation=Quaternion.Euler((float)(age*profile.HawkRotorSpeed),(float)(i*360d/w.Debris.Length+age*profile.HawkRotorSpeed),0);}
                }
                else if(w.Model){
                    bool landed=view.Tick>=state.RevealTick;
                    double sink=view.Tick<state.SinkTick?0:Math.Min(1,(double)(view.Tick-state.SinkTick)/Math.Max(1,state.ExpireTick-state.SinkTick));
                    w.Model.transform.position=at-Vector3.up*(float)(state.Burial*fall+profile.WreckSinkDepth*sink);
                    var slope=landed?profile.AuthoredMap?.SurfaceGradient(state.Position)??default:default;
                    w.Model.transform.rotation=Quaternion.FromToRotation(Vector3.up,new Vector3(-(float)slope.X,1,-(float)slope.Z).normalized)*Quaternion.Euler(0,90-(float)state.Heading*Mathf.Rad2Deg,0);
                    bool falling=state.Kind==PlayableEntityKind.Hawk&&view.Tick<state.LandTick;
                    w.Model.SetActive(visible&&state.DestroyedTick<0&&view.Tick<state.ExpireTick&&(landed||falling));
                }
                double flashAge=age/Math.Max(1d/30,profile.ImpactEffectSec);
                w.Flash.SetActive(visible&&flashAge<1&&state.DestroyedTick<0);w.Flash.transform.position=at;
                w.Flash.transform.localScale=Vector3.one*(float)(profile.ImpactEffectRadius*2*Math.Max(double.Epsilon,flashAge));WebProjectileVisuals.Opacity(w.Flash,Mathf.Clamp01(1-(float)flashAge));
                // Ground death smoke starts at death; a falling Hawk emits on contact.
                long smokeStart=state.Kind==PlayableEntityKind.Hawk&&state.HasGround?state.LandTick:state.HasGround?state.DeathTick:state.RevealTick;
                double smokeAge=(view.Tick-smokeStart)/30d;
                w.Smoke.SetActive(visible&&state.DestroyedTick<0&&smokeAge>=0&&smokeAge<profile.UnitDeathSmokeSeconds);
                w.Smoke.transform.position=state.HasGround?ground:at;
                wreckSmokeVisuals.Update(w.Smoke,(float)(smokeAge/profile.UnitDeathSmokeSeconds),(float)profile.WreckSmokeSize,(float)(profile.WreckSmokeRise*profile.UnitDeathSmokeSeconds),(float)profile.WreckSmokeOpacity);
                if(state.DestroyedTick>=0){
                    if(!w.Hit){w.Hit=wreckFlashVisuals.DirectImpact(root,state.Id,state.ImpactKind==PlayableEntityKind.Tank);Layer(w.Hit);w.Smoke.SetActive(false);}
                    double hitAge=(view.Tick-state.DestroyedTick)/30d;
                    w.Hit.transform.position=ground+Vector3.up*(float)(state.Radius/2);w.Hit.SetActive(visible&&hitAge<profile.DirectImpactSeconds);
                    wreckFlashVisuals.UpdateDirectImpact(w.Hit,(float)(hitAge/profile.DirectImpactSeconds),(float)profile.DirectImpactRadius,(float)profile.DirectImpactSparkLength,(float)profile.DirectImpactSparkWidth,(float)profile.DirectImpactGlow,0);
                    w.Smoke.SetActive(visible&&hitAge<profile.WreckHitSmokeSeconds);w.Smoke.transform.position=ground;
                    wreckSmokeVisuals.Update(w.Smoke,(float)(hitAge/profile.WreckHitSmokeSeconds),(float)profile.WreckSmokeSize,(float)(profile.WreckSmokeRise*profile.WreckHitSmokeSeconds),(float)profile.WreckSmokeOpacity);
                }
            }
        }
        void ReleaseWreck(UnitWreck w){ReleaseWorldObject(w.Model);ReleaseWorldObject(w.Flash);ReleaseWorldObject(w.Smoke);ReleaseWorldObject(w.Hit);}
        void ClearUnitWrecks(){foreach(var w in unitWrecks.Values)ReleaseWreck(w);unitWrecks.Clear();wreckSmokeVisuals.Dispose();wreckGeneration=long.MinValue;wreckTick=-1;}
    }
}
