using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using Spacewars.Simulation;
using Spacewars.Presentation;
using Spacewars.Runtime;
using UnityEngine.TestTools;
using UnityEngine;
namespace Spacewars.Tests.PlayMode
{
    public sealed class FoundryNativeTests
    {
        private UnityHostRouteService botRoutes;
        [TearDown]public void ReleaseOwnedBotRoutes(){botRoutes?.Dispose();botRoutes=null;}
        [Test] public void UnityRoutesTraverseBothWideFlankRampsForAllUnitFootprints()
        {
            var p=FoundryGreybox.LoadProfile();var m=(FoundryMap)p.AuthoredMap;var g=new NavGeometry(m.HalfExtent,m.Solids.ToArray(),1);
            foreach(var kind in new[]{PlayableEntityKind.Tank,PlayableEntityKind.Explorer,PlayableEntityKind.Shkval})
            using(var router=new UnityNavigationRouter(g,p.Navigation.ForUnit(PlayableUnitRules.Radius(p,kind),PlayableUnitRules.Speed(p,kind),p.Navigation.TankTurnSpeed)))
            foreach(int side in new[]{-1,1})foreach(int end in new[]{-1,1})
            {
                var a=m.Point(side*77,end*35);var b=m.Point(0,end*35);var path=router.FindPath(a,b);Assert.IsNotEmpty(path,kind+" flank "+side+" edge "+end);
                foreach(var q in path){Assert.True(g.SegmentFree(a,q,PlayableUnitRules.Radius(p,kind)));a=q;}
            }
        }
        [Test] public void AuthorityMovementCrossesRampAndCapturesExistingNeutralMine()
        {
            var p=FoundryGreybox.LoadProfile();var m=(FoundryMap)p.AuthoredMap;var c=m.Configuration(p,19092026);var a=new OfflineParticipantAuthority(c,1);var view=a.View(c.Roster[0].Id);var explorer=view.Entities.Single(e=>e.Owner==PlayableOwner.Player);
            var center=m.Point(0,0);var target=m.Point(-77,1);double lowest=m.UpperHeight,highest=0;long sequence=0;
            using(var service=new UnityHostRouteService())
            foreach(var waypoint in new[]{center,target})
            {
                Assert.That(a.Apply(new PlayableCommand(1,++sequence,c.Roster[0].Id,PlayableCommandKind.Move,new[]{explorer.Id},waypoint)).Status,Is.EqualTo(PlayableCommandStatus.Applied).Or.EqualTo(PlayableCommandStatus.Accepted));
                for(int i=0;i<1800;i++)
                {
                    service.Service(a.Navigation.Requests,a.Navigation.Answers,1,a.NavigationBinding.Geometry,p,32);a.Step(1d/30);
                    var point=a.Navigation.Crowd.Units.Single(e=>e.Id==explorer.Id).Position;double height=m.SurfaceHeight(point);lowest=Math.Min(lowest,height);highest=Math.Max(highest,height);
                }
                var reached=a.Navigation.Crowd.Units.Single(e=>e.Id==explorer.Id).Position;
                Assert.Less(Math.Sqrt(Math.Pow(reached.X-waypoint.X,2)+Math.Pow(reached.Z-waypoint.Z,2)),p.Navigation.ArrivalTolerance+1,"Waypoint must be reached through supported routes");
            }
            Assert.AreEqual(0,lowest,1e-8);Assert.AreEqual(m.UpperHeight,highest,1e-8);
            var after=a.View(c.Roster[0].Id);var moved=after.Entities.Single(e=>e.Id==explorer.Id);Assert.Less(Math.Sqrt(Math.Pow(moved.Position.X-target.X,2)+Math.Pow(moved.Position.Z-target.Z,2)),p.Navigation.ArrivalTolerance+1);
            var mine=after.Sites.Single(s=>s.Site.Id==21);Assert.AreEqual(PlayableOwner.Player,mine.Claimant);Assert.Greater(mine.Progress,0);
        }
        [UnityTest] public IEnumerator GreyboxSceneBootsAndRendersRealSixOwnerBuildings()
        {
            var root=new UnityEngine.GameObject("Foundry PlayMode inspection");var component=root.AddComponent<FoundryGreybox>();
            yield return null;yield return null;
            var runtime=(PlayableRuntime)typeof(FoundryGreybox).GetField("runtime",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).GetValue(component);
            double stopDeadline=0;
            try
            {
                Assert.NotNull(runtime);Assert.AreEqual(6,runtime.Latest.Participants.Count);Assert.IsNull(runtime.Latest.Failure);
                var camera=(UnityEngine.Camera)typeof(FoundryGreybox).GetField("cameraView",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).GetValue(component);
                var pick=typeof(FoundryGreybox).GetMethod("TryGround",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
                Assert.True((bool)pick.Invoke(component,new object[]{(Vector2)camera.WorldToScreenPoint(new Vector3(77,6,35)),default(NavPoint)}));
                Assert.False((bool)pick.Invoke(component,new object[]{(Vector2)camera.WorldToScreenPoint(new Vector3(44,13,0)),default(NavPoint)}),"Rock/unsupported clicks must not become center move orders");
                string output=Environment.GetEnvironmentVariable("FOUNDRY_EVIDENCE");if(!string.IsNullOrEmpty(output))
                {
                    var target=new UnityEngine.RenderTexture(1600,1600,24);var previous=UnityEngine.RenderTexture.active;camera.targetTexture=target;camera.Render();UnityEngine.RenderTexture.active=target;
                    var texture=new UnityEngine.Texture2D(1600,1600,UnityEngine.TextureFormat.RGB24,false);texture.ReadPixels(new UnityEngine.Rect(0,0,1600,1600),0,0);texture.Apply();System.IO.File.WriteAllBytes(System.IO.Path.Combine(output,"playmode-six-starts.png"),texture.EncodeToPNG());camera.targetTexture=null;UnityEngine.RenderTexture.active=previous;UnityEngine.Object.Destroy(texture);UnityEngine.Object.Destroy(target);
                }
            }
            finally{stopDeadline=Time.realtimeSinceStartupAsDouble+2;UnityEngine.Object.Destroy(root);}
            // Batch render frames are uncapped; wait for the real asynchronous terminal
            // state and the owned worker under one bounded wall deadline from Destroy.
            yield return null;var worker=(System.Threading.Thread)typeof(PlayableRuntime).GetField("worker",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(runtime);
            while((!runtime.IsStopped||worker.IsAlive)&&Time.realtimeSinceStartupAsDouble<stopDeadline)yield return null;
            Assert.True(runtime.IsStopped);Assert.False(worker.IsAlive);Assert.AreEqual(RuntimeStatus.Stopped,runtime.Latest.Status);Assert.IsNull(runtime.Latest.Failure);
        }
        [UnityTest] public IEnumerator ExistingAiRunsOnSixStartTerrainWithoutRuntimeErrors()
        {
            var p=FoundryGreybox.LoadProfile();var m=(FoundryMap)p.AuthoredMap;var authority=new PlayableAuthorityTick(m.Configuration(p,19092026,true),1);
            using(var service=new UnityHostRouteService())
            {
                // Short initialization/navigation smoke, not AI balance/tournament acceptance.
                int advances=0;for(int i=0;i<4096&&advances<300;i++){service.Service(authority,32);if(authority.TryAdvance())advances++;if(i%60==0)yield return null;}
                Assert.AreEqual(300,advances,"routes pending="+authority.NavigationBinding.RoutePort.Admission.Requests.Count+
                    " queue="+service.CertifiedCounters.PendingSubscriptions+" ready="+service.CertifiedCounters.ReadyResults+
                    " graph="+service.CertifiedCounters.PhysicalGraphWork+" field="+service.CertifiedCounters.PhysicalFieldWork+
                    " connector="+service.CertifiedCounters.ConnectorWork+" retained="+service.CertifiedRetainedBytes+
                    " projection="+service.LastProjectionFailure);
                Assert.IsNull(authority.Latest.Failure);Assert.AreEqual(6,authority.Latest.Participants.Count);
            }
        }
        [Test] public void SixOwnersProduceResearchCaptureAndFightWithProductionRules()
        {
            var p=FoundryGreybox.LoadProfile();var map=(FoundryMap)p.AuthoredMap;var config=map.Configuration(p,19092026);var authority=new OfflineParticipantAuthority(config,1);long[] sequence=new long[6];
            using(var routes=new UnityHostRouteService())
            {
                void Step(double seconds){for(int i=0;i<seconds*30;i++){routes.Service(authority.Navigation.Requests,authority.Navigation.Answers,1,authority.NavigationBinding.Geometry,p,32);authority.Step(1d/30);}}
                void Apply(int owner,PlayableCommandKind kind,int[] ids=null,NavPoint target=default(NavPoint),int site=0,int slot=0,PlayableBuildingKind building=PlayableBuildingKind.Factory,int parent=0)
                {var receipt=authority.Apply(new PlayableCommand(1,++sequence[owner],config.Roster[owner].Id,kind,ids??Array.Empty<int>(),target,siteId:site,slotId:slot,buildingKind:building,parentId:parent));Assert.That(receipt.Status,Is.EqualTo(PlayableCommandStatus.Applied).Or.EqualTo(PlayableCommandStatus.Accepted),owner+" "+kind+": "+receipt.Message);}
                for(int i=0;i<6;i++){var home=authority.View(config.Roster[i].Id).Buildings.Single(b=>(int)b.Owner==i);Apply(i,PlayableCommandKind.BuildAt,site:home.SiteId,slot:1,parent:home.Id);}
                Step(30);
                for(int i=0;i<6;i++){var home=authority.View(config.Roster[i].Id).Buildings.Single(b=>(int)b.Owner==i&&b.Kind==PlayableBuildingKind.Headquarters);Apply(i,PlayableCommandKind.BuildAt,site:home.SiteId,slot:2,building:PlayableBuildingKind.ScientificCenter,parent:home.Id);}
                Step(Math.Max(p.FactoryBuildSeconds,p.ScienceBuildSeconds)+2);
                Step(90);
                for(int i=0;i<6;i++){var v=authority.View(config.Roster[i].Id);var factory=v.Buildings.Single(b=>(int)b.Owner==i&&b.Kind==PlayableBuildingKind.Factory);var science=v.Buildings.Single(b=>(int)b.Owner==i&&b.Kind==PlayableBuildingKind.ScientificCenter);Assert.AreEqual(ConstructionPhase.Ready,factory.Phase);Assert.AreEqual(ConstructionPhase.Ready,science.Phase);Apply(i,PlayableCommandKind.QueueTank,new[]{factory.Id});Apply(i,PlayableCommandKind.QueueResearch,new[]{science.Id});}
                Step(Math.Max(p.TankProductionSeconds,p.TankChassisSeconds)+2);
                for(int i=0;i<6;i++){var v=authority.View(config.Roster[i].Id);Assert.True(v.Entities.Any(e=>(int)e.Owner==i&&e.Kind==PlayableEntityKind.Tank&&e.Upgraded));Assert.True(v.Entities.Any(e=>(int)e.Owner==i&&e.Kind==PlayableEntityKind.Tank));}
                var explorer=authority.View(config.Roster[0].Id).Entities.Single(e=>e.Owner==PlayableOwner.Player&&e.Kind==PlayableEntityKind.Explorer);Apply(0,PlayableCommandKind.Move,new[]{explorer.Id},map.Point(-17,18));Step(90);
                Assert.AreEqual(PlayableOwner.Player,authority.View(config.Roster[0].Id).Sites.Single(s=>s.Site.Id==23).Claimant);
                var first=authority.View(config.Roster[0].Id).Entities.Single(e=>e.Owner==PlayableOwner.Player&&e.Kind==PlayableEntityKind.Tank);
                var enemy=authority.View(config.Roster[3].Id).Entities.Single(e=>(int)e.Owner==3&&e.Kind==PlayableEntityKind.Tank);
                Apply(0,PlayableCommandKind.AttackMove,new[]{first.Id},map.Point(0,0));Apply(3,PlayableCommandKind.AttackMove,new[]{enemy.Id},map.Point(0,0));Step(120);
                authority.FinishManually();Assert.AreEqual(6,authority.Result.Players.Count);Assert.True(authority.Result.Facts.Any(f=>f.Kind==MatchFactKind.UnitDestroyed),"Real weapon damage must occur on foundry terrain");
            }
        }
        [UnityTest, Timeout(300000)] public IEnumerator AllFiveLobbyBotsDevelopOnFoundryTerrain()
        {
            var p=FoundryGreybox.LoadProfile();var map=(FoundryMap)p.AuthoredMap;var config=map.Configuration(p,19092026,humanId:"player-1");var authority=new PlayableAuthorityTick(config,1);var moved=new bool[6];
            using(var routes=botRoutes=new UnityHostRouteService())
            {
                var elapsed=System.Diagnostics.Stopwatch.StartNew();int advances=0;for(int tries=0;tries<30000&&advances<3600;tries++){routes.Service(authority,64);if(authority.TryAdvance()){advances++;if(advances%600==0)Debug.Log("FOUNDRY_PROGRESS tick="+authority.Tick+" wallMs="+elapsed.ElapsedMilliseconds+" navBuilds="+routes.NavMeshBuilds+" flowBuilds="+routes.FlowRebuilds+" requests="+routes.NavMeshRequests+"/"+routes.FlowRequests);if(advances%30==0)for(int owner=1;owner<6;owner++){var v=authority.ParticipantView(config.Roster[owner].Id);var start=config.Starts[config.Assignments[owner]].ExplorerAnchor;moved[owner]|=v.Entities.Any(e=>(int)e.Owner==owner&&e.Kind==PlayableEntityKind.Explorer&&Math.Sqrt(Math.Pow(e.Position.X-start.X,2)+Math.Pow(e.Position.Z-start.Z,2))>p.Navigation.ArrivalTolerance);}}if(tries%120==0)yield return null;}
                Assert.AreEqual(3600,advances,"routes pending="+authority.NavigationBinding.RoutePort.Admission.Requests.Count+
                    " queue="+routes.CertifiedCounters.PendingSubscriptions+" ready="+routes.CertifiedCounters.ReadyResults+
                    " graph="+routes.CertifiedCounters.PhysicalGraphWork+" field="+routes.CertifiedCounters.PhysicalFieldWork+
                    " connector="+routes.CertifiedCounters.ConnectorWork+" retained="+routes.CertifiedRetainedBytes+
                    " cache="+routes.CertifiedCounters.CacheBytes+" epoch="+routes.SolverEpoch+
                    " projection="+routes.LastProjectionFailure);
                Assert.IsNull(authority.Latest.Failure);
                var lines=new System.Collections.Generic.List<string>();
                for(int i=1;i<6;i++)
                {
                    var v=authority.ParticipantView(config.Roster[i].Id);var checkpoint=authority.CaptureDiagnosticCheckpoints().Single(c=>c.OwnerId==v.OwnerId);
                    lines.Add(v.OwnerId+" credits="+v.Credits+" buildings="+string.Join(";",v.Buildings.Where(b=>(int)b.Owner==i).Select(b=>b.Kind+":"+b.Phase+":"+b.SiteId))+" claims="+string.Join(";",v.Sites.Where(site=>site.Claimant==(PlayableOwner)i||site.Owner==(PlayableOwner)i).Select(site=>site.Site.Id+":"+site.Owner+":"+site.Progress+":"+site.Ready))+" units="+string.Join(";",v.Entities.Where(e=>(int)e.Owner==i).Select(e=>e.Kind+":"+e.Position.X+","+e.Position.Z+":"+e.NavigationOutcome))+" expansion="+checkpoint.Expansion?.SiteId+":"+checkpoint.Expansion?.Phase+":"+checkpoint.Expansion?.Reason);
                }
                string output=Environment.GetEnvironmentVariable("FOUNDRY_EVIDENCE");if(!string.IsNullOrEmpty(output))System.IO.File.WriteAllLines(System.IO.Path.Combine(output,"bot-development.txt"),lines);
                foreach(var line in lines)Debug.Log("FOUNDRY_BOT "+line);
                for(int i=1;i<6;i++){var v=authority.ParticipantView(config.Roster[i].Id);Assert.Greater(v.Buildings.Count(b=>(int)b.Owner==i&&b.Kind==PlayableBuildingKind.Factory),0,"Bot factory "+i);Assert.Greater(v.Entities.Count(e=>(int)e.Owner==i),1,"Bot production "+i);Assert.True(moved[i],"Bot actual reconnaissance movement "+i);Assert.True(v.Buildings.Any(b=>(int)b.Owner==i&&b.Phase==ConstructionPhase.Ready&&(b.Kind==PlayableBuildingKind.Mine||b.Kind==PlayableBuildingKind.Outpost||b.Kind==PlayableBuildingKind.Refinery)),"Bot paid income development "+i);}
                Assert.True(Enumerable.Range(1,5).Any(i=>authority.ParticipantView(config.Roster[i].Id).Sites.Any(site=>site.Owner==(PlayableOwner)i&&site.Ready&&site.Site.Kind!=PlayableBuildingKind.Headquarters)),"Existing AI must complete real neutral territorial expansion");
            }
        }
    }
}
