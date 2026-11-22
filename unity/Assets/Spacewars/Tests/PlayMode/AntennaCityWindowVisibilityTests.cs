using System;
using System.Linq;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class AntennaCityWindowVisibilityTests
{
    [UnityTest]
    public IEnumerator HiddenSourcesAndObserverChangesCannotLightVisibleWindows()
    {
        var authoring=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("Spacewars.Editor.PreparedMapsAuthoring")).First(t=>t!=null);
        var profile=(PlayableProfile)authoring.GetMethod("Profile").Invoke(null,new object[]{"AntennaCity"});
        var host=new GameObject("Two city observers");
        var player=new PlayableWorld(host.transform,profile,staged:true);
        var control=new PlayableWorld(host.transform,profile,staged:true);
        try{
            yield return player.Preparation;
            yield return control.Preparation;
            var first=Vision(0,new NavPoint(0,0),15);
            var second=Vision(1,new NavPoint(27,0),15);
            var all=Vision(0,default,200);
            var outside=new NavPoint(15.1,0);
            PlayableSnapshot View(int tick,TeamVisionSnapshot vision,bool sources,PlayableOwner owner=PlayableOwner.Player)
            {
                var buildings=sources?new[]{new PlayableBuildingSnapshot(1,PlayableOwner.Enemy,PlayableBuildingKind.Headquarters,
                    outside,100,1,0,0,default,phase:ConstructionPhase.Ready)}:Array.Empty<PlayableBuildingSnapshot>();
                var units=sources?new[]{new PlayableEntitySnapshot(2,PlayableOwner.Enemy,PlayableEntityKind.Tank,outside,100,true,0,0,0)}:Array.Empty<PlayableEntitySnapshot>();
                var sounds=sources?new[]{new PlayableSoundEvent(tick+1,tick,PlayableSoundKind.Shot,outside,PlayableEntityKind.Tank)}:Array.Empty<PlayableSoundEvent>();
                return new PlayableSnapshot(profile.ProfileId,profile.Revision,1,7,tick,tick,RuntimeStatus.Running,false,
                    PlayableMatchOutcome.Playing,0,null,units,buildings,Array.Empty<PlayableProjectileSnapshot>(),
                    new PlayableRuntimeMetrics(0,0,0,0,0),null,vision:vision,owner:owner,sounds:sounds);
            }
            for(int tick=0;tick<=90;tick++){
                player.UpdateMatchAnimation(View(tick,first,true));
                control.UpdateMatchAnimation(View(tick,first,false));
            }
            var blank=Pixels(control);
            Assert.That(blank.All(v=>v==0));
            CollectionAssert.AreEqual(blank,Pixels(player),"HQ, moving tank and shots just outside sight");
            for(int tick=90;tick<=180;tick++)
                player.UpdateMatchAnimation(View(tick,second,true,PlayableOwner.Enemy));
            Assert.Greater(Pixels(player).Max(),180,"Second observer sees the same source");
            CollectionAssert.AreEqual(blank,Pixels(control),"Concurrent world keeps its own field");
            player.UpdateMatchAnimation(View(180,first,true));
            CollectionAssert.AreEqual(blank,Pixels(player),"Switch owner on the very same tick");
            for(int tick=180;tick<=270;tick++)
                player.UpdateMatchAnimation(View(tick,all,true),overview:true);
            Assert.Greater(Pixels(player).Max(),180);
            player.UpdateMatchAnimation(View(270,first,true));
            CollectionAssert.AreEqual(blank,Pixels(player),"Overview to participant with matching owner ID on the same tick");
        }finally{player.Dispose();control.Dispose();UnityEngine.Object.Destroy(host);}
    }
    static TeamVisionSnapshot Vision(int team,NavPoint point,double radius)
    {
        var sensor=new PlayableVision(team,170,170,2,2);
        sensor.Refresh(new[]{new VisionSource(point,radius)},Array.Empty<KnownBuilding>());
        return sensor.Snapshot();
    }
    static byte[] Pixels(PlayableWorld world)
    {
        var material=(Material)typeof(PlayableWorld).GetField("cityMaterial",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(world);
        return ((Texture2D)material.GetTexture("_CityWindowActivity")).GetRawTextureData();
    }
}
