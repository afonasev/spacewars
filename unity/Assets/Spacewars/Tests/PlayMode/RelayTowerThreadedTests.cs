using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.TestTools;

namespace Spacewars.Tests.PlayMode
{
    public sealed class RelayTowerThreadedTests
    {
        [UnityTest,Timeout(90000)]
        public IEnumerator OrdinaryHostBuildsMineAndRendersTheChangingReserve()
        {
            const long generation=917;
            const int seed=7112026,mineId=4;
            Assert.True(NativeSilentStartup.IsSilent);
            var output=Path.GetFullPath("../.local/mine-reserve-economy/witness");
            Directory.CreateDirectory(output);
            TestMatchLogSession.StartDefault(Path.Combine(output,"TestLogs"));
            var store=NativeBalanceStore.ForApplication(PlayableProfile.Default,Path.Combine(output,"balance.json"));
            var data=store.Active.CopyData();
            Assert.AreEqual(1300,data.mineReserveCredits);
            data.mineReserveCredits=6; // Two seconds of production for the focused witness.
            data.mineBuildSeconds=1;
            var profile=PlayableProfile.Create(data,displayName:store.Active.DisplayName+" · mine witness");
            var point=new NavPoint(0,-8);var scoutStart=new NavPoint(-8,-8);var capturePoint=new NavPoint(-2.5,-8);
            var roster=new[]{new OfflineParticipant("player-1",1,1,OfflineControl.Human),new OfflineParticipant("player-2",2,2,OfflineControl.Human)};
            var starts=new[]{
                new OfflineStart("west",1,new NavPoint(-24,-18),new NavPoint(-19,-18),pin:1),
                new OfflineStart("east",2,new NavPoint(24,18),new NavPoint(19,18),pin:2)
            };
            var sites=starts.Select(s=>new TerritorySite(s.SiteId,PlayableBuildingKind.Headquarters,s.Position,Array.Empty<TerritorySlot>()))
                .Append(new TerritorySite(mineId,PlayableBuildingKind.Mine,point,Array.Empty<TerritorySlot>())).ToArray();
            var config=new OfflineMatchConfiguration(profile,"mine-reserve-v1","mine-flat","ordinary-threaded",seed,roster,starts,
                sites,Array.Empty<NavObstacle>(),new double[,]{{0,60},{60,0}},
                scenario:new[]{new OfflineScenarioUnit(1,PlayableEntityKind.Explorer,scoutStart)});
            using var runtime=new PlayableRuntime(config,generation,startPaused:true);
            using var host=new UnityHostRouteService();
            var root=new GameObject("Mine reserve ordinary witness");
            var world=new PlayableWorld(root.transform,profile);
            var camera=new GameObject("Mine gameplay camera").AddComponent<Camera>();
            camera.transform.SetParent(root.transform);
            camera.orthographic=true;camera.orthographicSize=4.7f;
            camera.transform.position=world.Point(point)+new Vector3(6,11,-9);
            camera.transform.LookAt(world.Point(point)+Vector3.up);
            var light=new GameObject("Review daylight").AddComponent<Light>();
            light.transform.SetParent(root.transform);
            light.type=LightType.Directional;light.intensity=1.7f;light.transform.rotation=Quaternion.Euler(50,-35,0);
            double deadline=Time.realtimeSinceStartupAsDouble+70;
            long sequence=0;
            TerritorySiteSnapshot Mine()=>runtime.Latest?.Sites.FirstOrDefault(s=>s.Site.Id==mineId);
            IEnumerator Until(Func<bool> ready,string phase)
            {
                while(!ready())
                {
                    Assert.Less(Time.realtimeSinceStartupAsDouble,deadline,phase);
                    host.Service(runtime,8);Assert.IsNull(runtime.Latest?.Failure,phase);yield return null;
                }
            }

            try
            {
                yield return Until(()=>runtime.IsPrepared&&runtime.Latest!=null,"ordinary host preparation");
                var scout=runtime.Latest.Entities.Single(e=>e.Owner==PlayableOwner.Player&&e.Position.Equals(scoutStart));
                double otherRate=runtime.Latest.IncomePerSecond;
                runtime.RequestPause(false);
                Assert.True(runtime.TrySubmit(new PlayableCommand(generation,++sequence,"player-1",PlayableCommandKind.Move,
                    new[]{scout.Id},capturePoint)).Accepted);
                yield return Until(()=>Mine()?.Claimant==PlayableOwner.Player&&Mine().Progress>=1,"scout route and mine claim");
                Assert.AreNotEqual(scoutStart,runtime.Latest.Entities.Single(e=>e.Id==scout.Id).Position);
                Assert.AreEqual(6,Mine().MineReserve);
                Assert.True(runtime.TrySubmit(new PlayableCommand(generation,++sequence,"player-1",PlayableCommandKind.BuildAt,
                    null,siteId:mineId,buildingKind:PlayableBuildingKind.Mine)).Accepted);
                yield return Until(()=>Mine()?.Ready==true,"construction complete");

                var working=runtime.Latest;
                Assert.That(working.IncomePerSecond,Is.EqualTo(otherRate+3).Within(1e-8));
                var building=working.Buildings.Single(b=>b.SiteId==mineId);
                var actor=world.Building(building.Id,building.Kind.ToString(),true);
                actor.Root.position=world.Point(point);world.FaceBuilding(actor,camera);
                world.Fog.ShowPublic();
                float Render(PlayableSnapshot view,string file)
                {
                    world.RenderSites(view,camera);
                    world.UpdateFactoryModel(actor,view.Buildings.Single(b=>b.Id==building.Id),view);
                    var bar=root.GetComponentsInChildren<Transform>().Single(t=>t.name=="Mine reserve");
                    Assert.IsNull(bar.GetComponentInChildren<TextMesh>(),"Reserve is a bar without numbers");
                    Capture(camera,Path.Combine(output,file));
                    return bar.Find("Reserve fill").localScale.x;
                }

                Assert.Greater(Render(working,"working.png"),0);
                yield return Until(()=>Mine()?.MineDepleted==true,"reserve exhausted");
                var exhausted=runtime.Latest;
                Assert.That(exhausted.IncomePerSecond,Is.EqualTo(otherRate+.6).Within(1e-8));
                Assert.Zero(Render(exhausted,"depleted.png"));
                Assert.False(root.GetComponentsInChildren<Transform>().Any(t=>t.name=="Reserve fill"));
                double income=exhausted.SettledIncome;
                yield return Until(()=>runtime.Latest.Tick>=exhausted.Tick+30,"one second of reduced payments");
                Assert.That(runtime.Latest.SettledIncome-income,Is.GreaterThanOrEqualTo(otherRate+.6-1e-8));
            }
            finally
            {
                world.Dispose();if(root)UnityEngine.Object.DestroyImmediate(root);
                runtime.Dispose();Assert.True(System.Threading.SpinWait.SpinUntil(()=>runtime.IsStopped,5000));
            }
        }

        [UnityTest,Timeout(120000)]
        public IEnumerator OrdinaryHostCapturesFromBesidePlatformAndKeepsIncomeAfterDeparture()
        {
            const int seed=7112026;const long generation=911;
            string output=Path.GetFullPath("../.local/relay-integration/witness");Directory.CreateDirectory(output);
            Assert.True(NativeSilentStartup.IsSilent);TestMatchLogSession.StartDefault(Path.Combine(output,"TestLogs"));
            var profile=PlayableProfile.RecastGroupCorridorDefault;var map=(ThreeCrossingsMap)profile.AuthoredMap;
            var original=map.Configuration(profile,seed,new[]{new OfflineParticipant("player-1",1,1,OfflineControl.Human),new OfflineParticipant("player-2",2,2,OfflineControl.Human)});
            var costs=new double[4,4];for(int i=0;i<4;i++)for(int j=0;j<4;j++)costs[i,j]=original.RouteCost(i,j);
            var start=new NavPoint(-43,-4);var goal=new NavPoint(-42.5,0);var leave=new NavPoint(-43,-19);
            var config=new OfflineMatchConfiguration(profile,original.SourceIdentity,original.MapIdentity,original.RouteProvenance,seed,original.Roster.ToArray(),original.Starts.ToArray(),original.Sites.ToArray(),original.Obstacles.ToArray(),costs,scenario:new[]{new OfflineScenarioUnit(1,PlayableEntityKind.Explorer,start)},terrain:map.Id);
            PlayableRuntime runtime=null;UnityHostRouteService host=null;GameObject root=null;PlayableWorld world=null;
            double deadline=Time.realtimeSinceStartupAsDouble+90;long sequence=0,issued=-1,firstMotion=-1;
            IEnumerator Until(Func<bool> done,string phase)
            {while(!done()&&Time.realtimeSinceStartupAsDouble<deadline){host.Service(runtime,8);Assert.IsNull(runtime.Latest?.Failure,phase);yield return null;}Assert.True(done(),phase);}
            try
            {
                runtime=new PlayableRuntime(config,generation,startPaused:true);host=new UnityHostRouteService();
                yield return Until(()=>runtime.IsPrepared&&runtime.Latest!=null,"prepared ordinary host");
                var initial=runtime.Latest;Assert.AreEqual(2,config.Sites.Count(s=>s.Kind==PlayableBuildingKind.RelayTower));Assert.AreEqual(4,config.Sites.Count(s=>s.Kind==PlayableBuildingKind.Mine));
                var actor=initial.Entities.Single(e=>e.Owner==PlayableOwner.Player&&Math.Abs(e.Position.X-start.X)<.01&&Math.Abs(e.Position.Z-start.Z)<.01);
                Assert.True(runtime.NavigationGeometry.IsFree(goal,PlayableUnitRules.Radius(profile,actor.Kind)));double originalRate=initial.IncomePerSecond;
                runtime.RequestPause(false);issued=initial.Tick;
                Assert.True(runtime.TrySubmit(new PlayableCommand(generation,++sequence,"player-1",PlayableCommandKind.Move,new[]{actor.Id},goal)).Accepted);
                yield return Until(()=>{
                    var v=runtime.Latest;var unit=v.Entities.Single(e=>e.Id==actor.Id);
                    if(firstMotion<0&&!unit.Position.Equals(actor.Position))firstMotion=v.Tick;
                    return v.Sites.Single(s=>s.Site.Id==7).Owner==PlayableOwner.Player;
                },"real route, movement and relay capture");
                Assert.GreaterOrEqual(firstMotion,issued);var captured=runtime.Latest;
                double distance=AiIncomeSiteSelection.Distance(captured.Entities.Single(e=>e.Id==actor.Id).Position,new NavPoint(-40,0));
                Assert.Greater(distance,1.7,"capture is possible outside physical platform");Assert.LessOrEqual(distance,profile.RelayCaptureRadius);
                Assert.That(captured.IncomePerSecond-originalRate,Is.EqualTo(profile.RelayIncomePerPeriod/profile.IncomePeriodSeconds).Within(.000001));
                Assert.False(captured.Buildings.Any(b=>b.Kind==PlayableBuildingKind.RelayTower));
                Assert.True(captured.Vision.Sources.Any(v=>v.Position.Equals(new NavPoint(-40,0))&&v.Radius==profile.RelayVisionRange),"full capture grants the independent relay source");
                runtime.RequestPause(true);yield return Until(()=>runtime.Latest.Paused,"pause for captured map frame");
                root=new GameObject("Relay ordinary map witness");var timer=System.Diagnostics.Stopwatch.StartNew();world=new PlayableWorld(root.transform,profile);timer.Stop();
                world.Fog.Update(runtime.Latest.Vision,100);var cameraObject=new GameObject("Relay gameplay camera");cameraObject.transform.SetParent(root.transform);var camera=cameraObject.AddComponent<Camera>();camera.orthographic=true;camera.orthographicSize=8;camera.transform.position=new Vector3(-32,13,-15);camera.transform.LookAt(new Vector3(-40,1.5f,0));
                RenderSettings.ambientLight=new Color(.55f,.58f,.62f);var light=new GameObject("Review daylight").AddComponent<Light>();light.transform.SetParent(root.transform);light.type=LightType.Directional;light.intensity=1.7f;light.transform.rotation=Quaternion.Euler(50,-35,0);
                world.RenderSites(runtime.Latest,camera);yield return null;
                Capture(camera,Path.Combine(output,"crossings-captured-relay.png"));
                File.WriteAllText(Path.Combine(output,"map-preparation.json"),"{\"world_prepare_ms\":"+timer.Elapsed.TotalMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture)+",\"scope\":\"world preparation; not menu-to-frame acceptance\"}");
                runtime.RequestPause(false);
                Assert.True(runtime.TrySubmit(new PlayableCommand(generation,++sequence,"player-1",PlayableCommandKind.Move,new[]{actor.Id},leave)).Accepted);
                yield return Until(()=>AiIncomeSiteSelection.Distance(runtime.Latest.Entities.Single(e=>e.Id==actor.Id).Position,new NavPoint(-40,0))>profile.RelayCaptureRadius+1,"departure");
                var departed=runtime.Latest;long until=departed.Tick+180;int credits=departed.Credits;
                yield return Until(()=>runtime.Latest.Tick>=until,"persistent control and actual settled income");
                Assert.AreEqual(PlayableOwner.Player,runtime.Latest.Sites.Single(s=>s.Site.Id==7).Owner);Assert.Greater(runtime.Latest.Credits,credits);Assert.AreEqual(captured.IncomePerSecond,runtime.Latest.IncomePerSecond);
                Assert.True(runtime.Latest.Vision.Sources.Any(v=>v.Position.Equals(new NavPoint(-40,0))&&v.Radius==profile.RelayVisionRange),"relay source remains after capturer departs");
                Assert.Greater(AiIncomeSiteSelection.Distance(runtime.Latest.Entities.Single(e=>e.Id==actor.Id).Position,new NavPoint(-40,0)),PlayableUnitRules.Vision(profile,actor.Kind),"capturer no longer supplies sight at the relay");
                Assert.True(runtime.Latest.Vision.IsVisible(new NavPoint(-40,0)),"latched tower alone still reveals its surroundings");
                runtime.RequestFinish();yield return Until(()=>runtime.Result!=null,"result history");
                Assert.AreEqual(1,runtime.Result.Facts.Count(f=>f.Kind==MatchFactKind.RelayCaptured));Assert.Greater(runtime.Result.Players[0].EarnedCredits,0);
                File.WriteAllText(Path.Combine(output,"witness.json"),"{\"result\":\"PASS\",\"seed\":"+seed+",\"issued\":"+issued+",\"first_motion\":"+firstMotion+",\"final_tick\":"+runtime.Latest.Tick+",\"capture_distance\":"+distance.ToString(System.Globalization.CultureInfo.InvariantCulture)+"}");
            }
            finally
            {
                world?.Dispose();if(root)UnityEngine.Object.DestroyImmediate(root);runtime?.Dispose();
                if(runtime!=null)Assert.True(System.Threading.SpinWait.SpinUntil(()=>runtime.IsStopped,5000));host?.Dispose();
            }
        }
        [UnityTest,Timeout(120000)]
        public IEnumerator TeamMatchTransfersOnlyAfterAlliedDefenseAndFullRecovery()
        {
            const int seed=7112026;const long generation=916;
            Assert.True(NativeSilentStartup.IsSilent);
            var profile=PlayableProfile.Default;
            const int relayId=4;
            var relayPosition=new NavPoint(0,-8);
            var starts=new[]{
                new OfflineStart("owner",1,new NavPoint(-24,-18),new NavPoint(-19,-18),pin:1),
                new OfflineStart("enemy",2,new NavPoint(24,18),new NavPoint(19,18),pin:2),
                new OfflineStart("ally",3,new NavPoint(-24,18),new NavPoint(-19,18),pin:3)
            };
            var sites=starts.Select(s=>new TerritorySite(s.SiteId,PlayableBuildingKind.Headquarters,s.Position,Array.Empty<TerritorySlot>()))
                .Concat(new[]{new TerritorySite(relayId,PlayableBuildingKind.RelayTower,relayPosition,Array.Empty<TerritorySlot>())}).ToArray();
            var roster=new[]{new OfflineParticipant("player-1",1,1,OfflineControl.Human),
                new OfflineParticipant("player-2",2,2,OfflineControl.Human),new OfflineParticipant("player-3",3,1,OfflineControl.Human)};
            var ownerStart=new NavPoint(-12,-8);var enemyStart=new NavPoint(12,-8);var allyStart=new NavPoint(0,4);
            var units=new[]{new OfflineScenarioUnit(1,PlayableEntityKind.Explorer,ownerStart),
                new OfflineScenarioUnit(2,PlayableEntityKind.Explorer,enemyStart),new OfflineScenarioUnit(3,PlayableEntityKind.Explorer,allyStart)};
            // Isolate transfer and contest from the authored map's movement projection.
            var config=new OfflineMatchConfiguration(profile,"relay-transfer-v1","relay-flat","ordinary-threaded",
                seed,roster,starts,sites,Array.Empty<NavObstacle>(),new double[,]{{0,60,36},{60,0,48},{36,48,0}},scenario:units);
            var output=Environment.GetEnvironmentVariable("RELAY_TRANSFER_EVIDENCE")??Path.GetFullPath("../.local/relay-transfer");
            Directory.CreateDirectory(output);TestMatchLogSession.StartDefault(Path.Combine(output,"TestLogs"));
            using var runtime=new PlayableRuntime(config,generation,startPaused:true);
            using var host=new UnityHostRouteService();
            double deadline=Time.realtimeSinceStartupAsDouble+100;
            long sequence=0;
            PlayableSnapshot View(int slot)=>runtime.ParticipantView("player-"+slot);
            TerritorySiteSnapshot Relay()=>View(1)?.Sites.FirstOrDefault(s=>s.Site.Id==relayId);
            double Distance(int slot,int actor)=>AiIncomeSiteSelection.Distance(View(slot).Entities.Single(e=>e.Id==actor).Position,relayPosition);
            int At(int slot,NavPoint start)=>View(slot).Entities.Single(e=>AiIncomeSiteSelection.Distance(e.Position,start)<.01).Id;
            void Move(int slot,int actor,NavPoint point)=>Assert.True(runtime.TrySubmit(new PlayableCommand(
                generation,++sequence,"player-"+slot,PlayableCommandKind.Move,new[]{actor},point)).Accepted);
            long Send()
            {
                Assert.True(runtime.TrySubmit(new PlayableCommand(generation,++sequence,"player-1",PlayableCommandKind.TransferRelay,
                    null,siteId:relayId,targetId:2)).Accepted);
                return sequence;
            }
            IEnumerator Until(Func<bool> done,string phase)
            {
                while(!done())
                {
                    Assert.Less(Time.realtimeSinceStartupAsDouble,deadline,phase);
                    host.Service(runtime,8);
                    Assert.IsNull(runtime.Latest?.Failure,phase);
                    yield return null;
                }
            }
            try
            {
                yield return Until(()=>runtime.IsPrepared&&View(1)!=null&&View(2)!=null&&View(3)!=null,"prepared team match");
                int owner=At(1,ownerStart),enemy=At(2,enemyStart),ally=At(3,allyStart);
                runtime.RequestPause(false);
                Move(1,owner,new NavPoint(-3,-8));
                yield return Until(()=>Relay()?.Owner==PlayableOwner.Player,"ordinary movement and capture");
                Assert.Greater(AiIncomeSiteSelection.Distance(ownerStart,View(1).Entities.Single(e=>e.Id==owner).Position),1);
                var action=OfflinePadWorldActions.RelayTransfer(View(1),relayId,_=>"Алина",_=>Color.green).Single();
                Assert.AreEqual(2,action.Command.Target);Assert.AreEqual(Color.green,action.PlayerColor);
                Assert.AreEqual(View(3).IncomePerSecond.ToString("0.#")+"/с",action.PlayerIncome);
                CollectionAssert.AreEquivalent(new[]{PlayableOwner.Third},View(1).RelayRecipientIncome.Keys);
                Assert.IsEmpty(OfflinePadWorldActions.RelayTransfer(View(3),relayId,_=>"Игрок",_=>Color.blue));

                Move(1,owner,new NavPoint(-15,-8));
                yield return Until(()=>Distance(1,owner)>profile.RelayCaptureRadius+1,"owner leaves the zone");
                var allyGoal=new NavPoint(0,-2);var enemyGoal=new NavPoint(0,-14);
                foreach(var goal in new[]{allyGoal,enemyGoal})
                {
                    Assert.Less(AiIncomeSiteSelection.Distance(goal,relayPosition),profile.RelayCaptureRadius);
                    Assert.True(runtime.NavigationGeometry.IsFree(goal,profile.ExplorerCollisionRadius));
                }
                Move(3,ally,allyGoal);
                yield return Until(()=>Distance(3,ally)<profile.RelayCaptureRadius,"only a non-owner ally remains in the zone");
                Move(2,enemy,enemyGoal);
                yield return Until(()=>Relay()?.Contested==true,"ally alone contests the enemy");
                Assert.AreEqual(1,Relay().Progress);
                Assert.IsEmpty(OfflinePadWorldActions.RelayTransfer(View(1),relayId,_=>"Алина",_=>Color.green));
                long rejected=Send();
                yield return Until(()=>runtime.DrainReceipts().Any(r=>r.Sequence==rejected&&r.Status==PlayableCommandStatus.Rejected),
                    "authority rejects a stale transfer during the contest");

                Move(3,ally,allyStart);
                yield return Until(()=>Relay()!=null&&Relay().Progress<.95,"enemy partially neutralizes undefended control");
                Move(2,enemy,enemyStart);
                yield return Until(()=>Distance(2,enemy)>profile.RelayCaptureRadius&&Relay()!=null&&Relay().Progress<1,
                    "enemy outside the zone while control recovers");
                rejected=Send();
                yield return Until(()=>runtime.DrainReceipts().Any(r=>r.Sequence==rejected&&r.Status==PlayableCommandStatus.Rejected),
                    "authority rejects transfer during incomplete recovery");
                yield return Until(()=>Relay()?.Progress==1,"control fully recovered");
                yield return RenderTransferMenus(new[]{action},output,"relay-transfer-radial.png");
                yield return RenderTransferMenus(new[]{
                    BotPreview("Кадет","4/с",2),
                    BotPreview("Ветеран","6,5/с",4)
                },output,"relay-transfer-bots.png");
                double donorRate=View(1).IncomePerSecond,recipientRate=View(3).IncomePerSecond;
                double relayRate=profile.RelayIncomePerPeriod/profile.IncomePeriodSeconds;
                long sent=Send();
                yield return Until(()=>runtime.DrainReceipts().Any(r=>r.Sequence==sent&&r.Status==PlayableCommandStatus.Applied),
                    "safe transfer applies");
                yield return Until(()=>Relay()?.Owner==PlayableOwner.Third&&View(3).Sites.Any(s=>s.Site.Id==relayId&&s.Owner==PlayableOwner.Third),
                    "both allied views see the new owner");
                Assert.AreEqual(PlayableOwner.Third,View(1).MapRelayControl.Single(r=>r.SiteId==relayId).Owner);
                Assert.True(View(1).Vision.IsVisible(relayPosition));
                Assert.That(View(1).IncomePerSecond,Is.EqualTo(donorRate-relayRate).Within(1e-8));
                Assert.That(View(3).IncomePerSecond,Is.EqualTo(recipientRate+relayRate).Within(1e-8));
                yield return Until(()=>View(3).IncomeEvents.Any(e=>e.Kind==PlayableBuildingKind.RelayTower&&e.BuildingId==relayId),
                    "new owner receives the next income marker");
                Assert.False(View(1).IncomeEvents.Any(e=>e.Kind==PlayableBuildingKind.RelayTower&&e.BuildingId==relayId));
                yield return CheckSplitScreenTransferMenu(View(1),View(3),relayId);
                runtime.RequestFinish();yield return Until(()=>runtime.Result!=null,"transfer in match result");
                Assert.AreEqual(PlayableOwner.Third,runtime.Result.Facts.Single(f=>f.Kind==MatchFactKind.RelayTransferredIn).Owner);
                TestContext.WriteLine("RELAY_TRANSFER seed="+seed+" tick="+View(1).Tick+" recipient=player-3 rate="+relayRate);
            }
            finally
            {
                runtime.RequestStop();
                Assert.True(System.Threading.SpinWait.SpinUntil(()=>runtime.IsStopped,5000));
            }
        }
        private static IEnumerator CheckSplitScreenTransferMenu(PlayableSnapshot donor,PlayableSnapshot recipient,int relayId)
        {
            const System.Reflection.BindingFlags flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            var type=typeof(PlayableBootstrap);
            void Put(PlayableBootstrap seat,string name,object value)=>type.GetField(name,flags).SetValue(seat,value);
            T Get<T>(PlayableBootstrap seat,string name)=>(T)type.GetField(name,flags).GetValue(seat);
            void Call(PlayableBootstrap seat,string name,params object[] args)=>type.GetMethod(name,flags).Invoke(seat,args);
            var host=new GameObject("Relay split-screen HUD");
            var child=new GameObject("Relay split-screen teammate");child.transform.SetParent(host.transform);
            var left=host.AddComponent<PlayableBootstrap>();var right=child.AddComponent<PlayableBootstrap>();
            left.enabled=right.enabled=false;
            var target=new RenderTexture(1500,760,24);
            UnityEngine.UIElements.PanelSettings panel=null;
            try
            {
                Put(right,"localCoordinator",left);
                var views=new[]{donor,recipient};
                var seats=new[]{left,right};
                for(int index=0;index<seats.Length;index++)
                {
                    var seat=seats[index];
                    Put(seat,"profile",views[index].ActiveProfile);
                    Put(seat,"view",views[index]);
                    Put(seat,"selectedSite",relayId);
                    Put(seat,"battleController",index==1);
                    var camera=new GameObject("Relay viewport camera").AddComponent<Camera>();
                    camera.transform.SetParent(seat.transform);camera.enabled=false;camera.orthographic=true;
                    camera.transform.position=new Vector3(0,23,-25);camera.transform.LookAt(new Vector3(0,0,-8));
                    Put(seat,"cameraView",camera);
                    Put(seat,"world",new PlayableWorld(seat.transform,views[index].ActiveProfile));
                    Call(seat,"CreateHud");
                    if(index==0)
                    {
                        panel=host.GetComponent<UnityEngine.UIElements.UIDocument>().panelSettings;
                        panel.scale=1;target.Create();panel.targetTexture=target;
                    }
                    Call(seat,"ConfigureLocalViewport",index,2);
                }
                for(int frame=0;frame<3;frame++)yield return null;
                foreach(var seat in seats)Call(seat,"UpdateHud");
                yield return null;
                var leftRing=Get<OrbitalBattleRing>(left,"battleRing");
                var rightRing=Get<OrbitalBattleRing>(right,"battleRing");
                var leftRoot=Get<UnityEngine.UIElements.VisualElement>(left,"root");
                var rightRoot=Get<UnityEngine.UIElements.VisualElement>(right,"root");
                Assert.AreEqual(UnityEngine.UIElements.DisplayStyle.None,leftRing.resolvedStyle.display,"The former owner cannot transfer again.");
                Assert.AreEqual(UnityEngine.UIElements.DisplayStyle.Flex,rightRing.resolvedStyle.display,"The recipient gets the action in their own seat.");
                var returnAction=Get<OfflinePadAction[]>(right,"battleActions").Single();
                Assert.AreEqual((int)donor.Owner,returnAction.Command.Target);
                Assert.AreEqual(donor.IncomePerSecond.ToString("0.#")+"/с",returnAction.PlayerIncome);
                Assert.AreEqual(leftRoot.worldBound.xMax,rightRoot.worldBound.xMin,1);
                Assert.That(rightRing.worldBound.xMin,Is.GreaterThanOrEqualTo(rightRoot.worldBound.xMin));
                Assert.That(rightRing.worldBound.xMax,Is.LessThanOrEqualTo(rightRoot.worldBound.xMax));
            }
            finally
            {
                if(panel)panel.targetTexture=null;
                target.Release();UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [UnityTest]
        public IEnumerator RelayLabelsAreCenteredForEveryRecipientCount()
        {
            Assert.True(NativeSilentStartup.IsSilent);
            var output=Environment.GetEnvironmentVariable("RELAY_TRANSFER_EVIDENCE")??Path.GetFullPath("../.local/relay-transfer");
            Directory.CreateDirectory(output);
            foreach(int diameter in new[]{280,160})
                yield return RenderRelayLayoutGrid(diameter,output);
        }

        private static IEnumerator RenderRelayLayoutGrid(int diameter,string output)
        {
            const int columns=4;
            int cell=diameter+24, row=diameter+54;
            var holder=new GameObject("Relay layout grid");
            var panel=ScriptableObject.CreateInstance<UnityEngine.UIElements.PanelSettings>();
            var target=new RenderTexture(columns*cell,2*row,24);target.Create();
            panel.targetTexture=target;panel.scaleMode=UnityEngine.UIElements.PanelScaleMode.ConstantPixelSize;panel.scale=1;
            panel.themeStyleSheet=Resources.Load<UnityEngine.UIElements.ThemeStyleSheet>("FoundationTheme");
            try
            {
                var document=holder.AddComponent<UnityEngine.UIElements.UIDocument>();document.panelSettings=panel;
                var root=document.rootVisualElement;
                root.style.unityFont=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                root.style.backgroundColor=new Color(.06f,.09f,.12f);
                OrbitalTheme.Install(root);
                yield return null;
                var rings=new OrbitalBattleRing[7];
                var names=new[]{"Кадет","Ветеран","Боец","Орион","Альтаир","Сокол","Лира"};
                for(int count=1;count<=rings.Length;count++)
                {
                    float x=(count-1)%columns*cell+12,y=(count-1)/columns*row;
                    var caption=new UnityEngine.UIElements.Label(count+" "+(count==1?"союзник":count<=4?"союзника":"союзников"));
                    caption.style.position=UnityEngine.UIElements.Position.Absolute;
                    caption.style.left=x;caption.style.top=y+8;caption.style.width=diameter;
                    caption.style.unityTextAlign=TextAnchor.MiddleCenter;caption.style.color=Color.white;caption.style.fontSize=20;
                    root.Add(caption);
                    var ring=rings[count-1]=new OrbitalBattleRing();
                    root.Add(ring);ring.style.display=UnityEngine.UIElements.DisplayStyle.Flex;
                    ring.Resize(diameter);ring.style.left=x;ring.style.top=y+40;
                    var actions=Enumerable.Range(0,count).Select(index=>BotPreview(names[index],index%2==0?"4/с":"12,5/с",index+1)).ToArray();
                    ring.Set(actions,"Вышка связи",contextGlyph:"relay-tower");
                    var card=UnityEngine.UIElements.UQueryExtensions.Q<RadialActionCard>(ring);
                    card.style.display=UnityEngine.UIElements.DisplayStyle.None;
                }
                for(int frame=0;frame<8;frame++)yield return null;
                var settings=RadialCardProfile.Load();
                var directions=new UnityEngine.UIElements.FlexDirection[rings.Length];
                for(int count=1;count<=rings.Length;count++)
                for(int index=0;index<count;index++)
                {
                    var ring=rings[count-1];
                    var petal=UnityEngine.UIElements.UQueryExtensions.Q<UnityEngine.UIElements.Button>(ring,"radial-relay-transfer:"+(index+1));
                    var group=UnityEngine.UIElements.UQueryExtensions.Q<UnityEngine.UIElements.VisualElement>(petal,"radial-relay-labels");
                    var name=UnityEngine.UIElements.UQueryExtensions.Q<UnityEngine.UIElements.Label>(petal,"radial-sector-title");
                    var income=UnityEngine.UIElements.UQueryExtensions.Q<UnityEngine.UIElements.Label>(petal,"radial-sector-count");
                    var angle=(float)index/count*2*Mathf.PI-Mathf.PI/2;
                    var anchor=Vector2.one*diameter/2+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*
                        (diameter*(OrbitalBattleRing.InnerRatio+OrbitalBattleRing.OuterRatio)/2);
                    var origin=petal.worldBound.position;
                    var outline=RadialPetalGeometry.Outline(diameter,index,count,settings.sectorGap,settings.petalCorner);
                    Assert.That(Vector2.Distance(group.worldBound.center-origin,anchor),Is.LessThan(1.1f));
                    foreach(var label in new[]{name,income})
                    {
                        var bounds=label.worldBound;bounds.position-=origin;
                        Assert.Greater(bounds.width,0,$"{diameter}/{count}/{index}: {label.text}");
                        bounds.xMin+=1;bounds.xMax-=1;bounds.yMin+=1;bounds.yMax-=1;
                        Assert.True(RadialPetalGeometry.ContainsRectangle(outline,bounds),
                            $"{diameter}/{count}/{index}: {label.text} stays in the petal ({bounds})");
                    }
                    if(index==0)directions[count-1]=group.resolvedStyle.flexDirection;
                    else Assert.AreEqual(directions[count-1],group.resolvedStyle.flexDirection,"All petals share the same arrangement.");
                    if(group.resolvedStyle.flexDirection==UnityEngine.UIElements.FlexDirection.Column)
                    {
                        Assert.AreEqual(anchor.x,name.worldBound.center.x-origin.x,1.1f);
                        Assert.AreEqual(anchor.x,income.worldBound.center.x-origin.x,1.1f);
                    }
                    else Assert.AreEqual(anchor.x,(name.worldBound.xMin+income.worldBound.xMax)/2-origin.x,1.1f);
                }
                var old=RenderTexture.active;RenderTexture.active=target;
                var image=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);
                try
                {
                    image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();
                    File.WriteAllBytes(Path.Combine(output,"relay-layouts-"+diameter+".png"),image.EncodeToPNG());
                }
                finally{RenderTexture.active=old;UnityEngine.Object.DestroyImmediate(image);}
            }
            finally
            {
                panel.targetTexture=null;target.Release();UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(holder);UnityEngine.Object.DestroyImmediate(panel);
            }
        }

        private static OfflinePadAction BotPreview(string name,string income,int color)
        {
            Assert.True(ColorUtility.TryParseHtmlString(NativeLobbyConfiguration.ColorHex[color],out var paint));
            const string detail="Союзник получит контроль над вышкой и весь её дальнейший доход.";
            return new OfflinePadAction("relay-transfer:"+color,name,true,detail)
            {
                PlayerColor=paint,PlayerIncome=income,
                Presentation=new RadialActionPresentation{Title=name,Description=detail,Stats=new[]{"Текущий доход: "+income}},
                Command=new OfflinePadActionCommand{Kind=PlayableCommandKind.TransferRelay,Target=color}
            };
        }

        private static IEnumerator RenderTransferMenus(OfflinePadAction[] actions,string output,string filename)
        {
            var holder=new GameObject("Relay transfer menu render");
            var panel=ScriptableObject.CreateInstance<UnityEngine.UIElements.PanelSettings>();
            var target=new RenderTexture(1280,510,24);target.Create();panel.targetTexture=target;
            panel.themeStyleSheet=Resources.Load<UnityEngine.UIElements.ThemeStyleSheet>("FoundationTheme");
            panel.scaleMode=UnityEngine.UIElements.PanelScaleMode.ConstantPixelSize;
            panel.scale=1;
            try
            {
                var document=holder.AddComponent<UnityEngine.UIElements.UIDocument>();document.panelSettings=panel;
                var root=document.rootVisualElement;
                root.style.unityFont=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                root.style.backgroundColor=new Color(.06f,.09f,.12f);
                OrbitalTheme.Install(root);
                yield return null;
                float fit=Mathf.Min(root.layout.width/target.width,root.layout.height/target.height);
                if(fit>0&&fit<.99f)
                {
                    panel.scale*=fit;
                    yield return null;
                }
                Assert.GreaterOrEqual(root.layout.width,target.width-1,"Capture fits both input previews");
                Assert.GreaterOrEqual(root.layout.height,target.height-1,"Capture fits both complete radial menus");
                for(int i=0;i<2;i++)
                {
                    var label=new UnityEngine.UIElements.Label(i==0?"Мышь":"Геймпад");
                    label.style.position=UnityEngine.UIElements.Position.Absolute;
                    label.style.left=i*640+28;label.style.top=24;label.style.fontSize=24;label.style.color=Color.white;
                    root.Add(label);
                    var ring=new OrbitalBattleRing();
                    root.Add(ring);ring.style.display=UnityEngine.UIElements.DisplayStyle.Flex;ring.Resize(220);ring.style.left=i*640+35;ring.style.top=86;
                    ring.ConfigureInput(i==1);
                    ring.Set(actions,"Вышка связи",actions[0].Sector.Id,contextGlyph:"relay-tower");
                    ring.PositionDetail(false,365,450,86,40);
                    Assert.IsNull(UnityEngine.UIElements.UQueryExtensions.Q<UnityEngine.UIElements.VisualElement>(ring,"radial-player-color"));
                    foreach(var action in actions)
                    {
                        var petal=UnityEngine.UIElements.UQueryExtensions.Q<UnityEngine.UIElements.Button>(ring,"radial-"+action.Sector.Id);
                        var name=UnityEngine.UIElements.UQueryExtensions.Q<UnityEngine.UIElements.Label>(petal,"radial-sector-title");
                        var income=UnityEngine.UIElements.UQueryExtensions.Q<UnityEngine.UIElements.Label>(petal,"radial-sector-count");
                        Assert.AreEqual(action.Label,name.text);
                        Assert.AreEqual(action.PlayerColor.Value,name.style.color.value);
                        Assert.AreEqual(action.PlayerIncome,income.text);
                    }
                }
                for(int frame=0;frame<8;frame++)yield return null;
                var old=RenderTexture.active;RenderTexture.active=target;
                var image=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);
                try
                {
                    image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();
                    Assert.Greater(image.GetPixels32().Where((_,i)=>i%173==0).Select(c=>(int)c.r<<16|(int)c.g<<8|c.b).Distinct().Count(),8);
                    File.WriteAllBytes(Path.Combine(output,filename),image.EncodeToPNG());
                }
                finally{RenderTexture.active=old;UnityEngine.Object.DestroyImmediate(image);}
            }
            finally
            {
                panel.targetTexture=null;target.Release();UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(holder);UnityEngine.Object.DestroyImmediate(panel);
            }
        }
        static void Capture(Camera camera,string path)
        {
            var rt=new RenderTexture(1400,1000,24);var image=new Texture2D(1400,1000,TextureFormat.RGB24,false);var old=RenderTexture.active;
            try{camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,1400,1000),0,0);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());}
            finally{camera.targetTexture=null;RenderTexture.active=old;rt.Release();UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(image);}
        }
    }
}
