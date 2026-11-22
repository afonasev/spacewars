using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;

public sealed partial class AntennaCityPreparedTests
{
    // Controlled presentation inputs keep the actors, camera and publication ticks
    // identical on both revisions. All models, fog and facade pixels use the game renderer.
    static IEnumerator WindowStreetSequence(PlayableBootstrap app,PlayableSnapshot match,Vector3 street,string output,StreamWriter log)
    {
        var world=Get<PlayableWorld>(app,"world");
        var city=(AntennaCityMap)Get<PlayableProfile>(app,"profile").AuthoredMap;
        var center=new NavPoint(street.x,street.z);
        var sensors=new PlayableVision(0,city.HalfExtent,city.HalfExtent,2,2);
        sensors.Refresh(new[]{new VisionSource(center,city.HalfExtent*3)},Array.Empty<KnownBuilding>());
        var vision=sensors.Snapshot();
        var field=typeof(PlayableBootstrap).GetField("view",Flags);
        var oldView=field.GetValue(app);
        bool wasEnabled=app.enabled;
        var headquarters=match.Buildings.Where(b=>b.Kind==PlayableBuildingKind.Headquarters).ToArray();
        var home=headquarters.First(b=>b.Owner==match.Owner).Position;
        var homeFocus=new Vector3((float)home.X,0,(float)home.Z);
        var timer=new Stopwatch();
        var updateTimes=new double[900];
        int updates=0;
        PlayableSnapshot Frame(int tick,long clock)
        {
            bool moving=tick>=30&&tick<120;
            bool battle=tick>=120&&tick<390;
            var position=new NavPoint(center.X+Math.Min(0,(tick-120)/15d)-4,center.Z);
            var opponent=new NavPoint(center.X+6,center.Z);
            var actors=tick>=120?new[]{Unit(701,match.Owner,position,false),Unit(702,PlayableOwner.Enemy,opponent,false)}:
                moving?new[]{Unit(701,match.Owner,position,true)}:Array.Empty<PlayableEntitySnapshot>();
            var sounds=battle&&clock%10==0?new[]{new PlayableSoundEvent(clock,clock,PlayableSoundKind.Shot,
                clock%20==0?position:opponent,PlayableEntityKind.Tank)}:Array.Empty<PlayableSoundEvent>();
            // Like the authority journal, retain a shot until the next one arrives.
            if(battle&&sounds.Length==0){
                long shot=clock-clock%10;
                sounds=new[]{new PlayableSoundEvent(shot,shot,PlayableSoundKind.Shot,shot%20==0?position:opponent,PlayableEntityKind.Tank)};
            }
            var rounds=battle?new[]{new PlayableProjectileSnapshot(803,701,702,
                new NavPoint(position.X+(opponent.X-position.X)*(clock%10)/10,center.Z))}:Array.Empty<PlayableProjectileSnapshot>();
            return new PlayableSnapshot(match.ProfileId,match.ProfileRevision,match.Generation+1,match.Seed,clock,clock,
                RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,match.Credits,match.Geometry,actors,headquarters,
                rounds,match.Metrics,null,vision:vision,sounds:sounds,participants:match.Participants.ToArray(),activeProfile:match.ActiveProfile);
        }
        try{
            app.enabled=false;
            for(int tick=0;tick<=900;tick++){
                var view=Frame(tick,tick);
                timer.Restart();
                world.UpdateMatchAnimation(view);
                timer.Stop();
                if(tick>0)updateTimes[updates++]=timer.Elapsed.TotalMilliseconds;
                string stage=tick==0?"sleep":tick==118?"traffic":tick==384?"battle":tick==510?"quiet":tick==900?"late":null;
                if(stage==null)continue;
                field.SetValue(app,view);Call(app,"Render");
                Shot(app,Path.Combine(output,"windows-"+stage+".png"),18,street+new Vector3(0,65,-40),street);
                if(tick==0)Shot(app,Path.Combine(output,"windows-hq.png"),32,homeFocus+new Vector3(0,60,-42),homeFocus);
                if((tick==0||stage=="battle")&&Environment.GetEnvironmentVariable("NOVA_CITY_RENDER_BENCH")=="1"){
                    log.WriteLine("CONTROLLED_CAMERA "+stage+" units="+view.Entities.Count+" shots="+view.Projectiles.Count);
                    var materials=world.GraphicsRoot.GetComponentsInChildren<Renderer>(true)
                        .SelectMany(r=>r.sharedMaterials).Where(m=>m&&m.HasProperty("_FogMask")).Distinct().ToArray();
                    var masks=materials.Select(m=>m.GetTexture("_FogMask")).ToArray();
                    try{
                        world.InspectionReveal();
                        var frames=Enumerable.Range(1,234).Select(n=>Frame(tick,tick+n)).ToArray();
                        int sample=0;
                        for(int pass=0;pass<3;pass++)MeasureCityRender(app,log,street,()=>{
                            var next=frames[sample++];field.SetValue(app,next);Call(app,"Render");
                        });
                    }finally{
                        for(int i=0;i<materials.Length;i++)materials[i].SetTexture("_FogMask",masks[i]);
                        for(int replay=0;replay<=tick;replay++)world.UpdateMatchAnimation(Frame(replay,replay));
                    }
                }
            }
            yield return ActualStreetCombat(app,center,street,output,log);
            Array.Sort(updateTimes);
            log.WriteLine($"ANIMATION_CPU_MS ticks={updates} median={updateTimes[450]:F4} p90={updateTimes[810]:F4} mean={updateTimes.Average():F4}");
        }finally{field.SetValue(app,oldView);app.enabled=wasEnabled;}
    }
    static IEnumerator ActualStreetCombat(PlayableBootstrap app,NavPoint center,Vector3 street,string output,StreamWriter log)
    {
        var profile=Get<PlayableProfile>(app,"profile");
        var world=Get<PlayableWorld>(app,"world");
        var domain=new Spacewars.Runtime.PlayableDomain(profile,904,false);
        var metrics=new PlayableRuntimeMetrics(0,0,0,0,0);
        var spawn=typeof(Spacewars.Runtime.PlayableDomain).GetMethod("SpawnUnit",Flags);
        var field=typeof(PlayableBootstrap).GetField("view",Flags);
        var original=field.GetValue(app);
        int shotCount=0;
        long lastShot=0,captured=-1;
        try{
            var teams=new int[2,2];
            for(int side=0;side<2;side++)
                for(int pair=0;pair<2;pair++)
                    teams[side,pair]=(int)spawn.Invoke(domain,new object[]{
                        new NavPoint(center.X+(side==0?-3:3),center.Z+(pair==0?-2:2)),(PlayableOwner)side,PlayableEntityKind.Tank});
            typeof(Spacewars.Runtime.PlayableDomain).GetMethod("RefreshVision",Flags).Invoke(domain,null);
            for(int side=0;side<2;side++)
                for(int pair=0;pair<2;pair++){
                    var attack=new PlayableCommand(904,pair+1,side==0?"player-1":"enemy-1",PlayableCommandKind.Attack,
                        new[]{teams[side,pair]},targetId:teams[1-side,pair]);
                    Assert.AreEqual(PlayableCommandStatus.Applied,domain.Apply(attack,out var reason),reason);
                }
            for(int tick=1;tick<=300;tick++){
                domain.Step(1d/30);
                var view=domain.PlayerSnapshot(tick,RuntimeStatus.Running,false,metrics,null,1112026);
                world.UpdateMatchAnimation(view);
                for(int i=0;i<view.Sounds.Count;i++){
                    var e=view.Sounds[i];
                    if(e.Kind==PlayableSoundKind.Shot&&e.Id>lastShot){lastShot=e.Id;shotCount++;}
                }
                if(shotCount<4||view.Projectiles.Count==0||tick<75)continue;
                world.Fog.Reset();
                field.SetValue(app,view);Call(app,"Render");
                // Unity destroys retired actors at the end of the frame.
                yield return null;
                var cityMaterial=(Material)typeof(PlayableWorld).GetField("cityMaterial",Flags).GetValue(world);
                Assert.AreSame(world.Fog.Texture,cityMaterial.GetTexture("_FogMask"));
                Assert.True(view.Vision.IsVisible(center));
                world.Fog.Update(view.Vision,5);
                Shot(app,Path.Combine(output,"windows-combat-match.png"),18,street+new Vector3(0,65,-40),street,reveal:null);
                CheckSpectatorSwitch(app,domain.SpectatorSnapshot(tick,RuntimeStatus.Running,false,metrics,null,1112026),log);
                captured=tick;break;
            }
            log.WriteLine($"PARTICIPANT_COMBAT tick={captured} observedShots={shotCount}");
            Assert.Greater(captured,0,"Participant saw live fire in the diagnostic street engagement");
        }finally{
            field.SetValue(app,original);
            domain.Navigation.ReleaseCommittedHostRoot();domain.DestroyOwnedNavigation();
        }
    }

    static void CheckSpectatorSwitch(PlayableBootstrap app,Spacewars.Runtime.PlayableSpectatorFrame frame,StreamWriter log)
    {
        var type=typeof(PlayableBootstrap);
        var mode=type.GetField("spectatorMode",Flags);
        var publication=type.GetField("spectatorFrame",Flags);
        var perspective=type.GetField("spectatorPerspective",Flags);
        var selectedView=type.GetField("view",Flags);
        var oldMode=mode.GetValue(app);var oldFrame=publication.GetValue(app);
        var oldPerspective=perspective.GetValue(app);var oldView=selectedView.GetValue(app);
        var world=Get<PlayableWorld>(app,"world");
        var city=(AntennaCityMap)Get<PlayableProfile>(app,"profile").AuthoredMap;
        var material=(Material)typeof(PlayableWorld).GetField("cityMaterial",Flags).GetValue(world);
        float Home(PlayableOwner owner)
        {
            var hq=frame.Overview.Buildings.Single(b=>b.Kind==PlayableBuildingKind.Headquarters&&b.Owner==owner);
            var map=(Texture2D)material.GetTexture("_CityWindowActivity");
            return map.GetPixelBilinear((float)(.5+hq.Position.X/(2*city.HalfExtent)),(float)(.5+hq.Position.Z/(2*city.HalfExtent))).r;
        }
        try{
            mode.SetValue(app,true);publication.SetValue(app,frame);perspective.SetValue(app,null);
            selectedView.SetValue(app,frame.Overview);Call(app,"Render");
            Assert.Greater(Home(PlayableOwner.Player),.1f);Assert.Greater(Home(PlayableOwner.Enemy),.1f);
            var select=type.GetMethod("SetSpectatorPerspective",Flags);
            foreach(var owner in new[]{PlayableOwner.Player,PlayableOwner.Enemy}){
                select.Invoke(app,new object[]{owner});Call(app,"Render");
                Assert.AreSame(frame.Perspective(owner),selectedView.GetValue(app));
                Assert.Greater(Home(owner),.1f);
                Assert.AreEqual(0,Home(owner==PlayableOwner.Player?PlayableOwner.Enemy:PlayableOwner.Player));
            }
            log.WriteLine("SPECTATOR_SWITCH overview / player / enemy; both HQ fields checked on tick "+frame.Overview.Tick);
        }finally{
            mode.SetValue(app,oldMode);publication.SetValue(app,oldFrame);perspective.SetValue(app,oldPerspective);
            selectedView.SetValue(app,oldView);
        }
    }

    static PlayableEntitySnapshot Unit(int id,PlayableOwner owner,NavPoint position,bool moving)=>
        new PlayableEntitySnapshot(id,owner,PlayableEntityKind.Tank,position,100,moving,0,owner==PlayableOwner.Player?0:Math.PI,0,maxHealth:100);
}
