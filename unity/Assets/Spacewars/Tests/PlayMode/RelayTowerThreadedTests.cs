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
        [UnityTest,Timeout(120000)]
        public IEnumerator OrdinaryHostCapturesFromBesidePlatformAndKeepsIncomeAfterDeparture()
        {
            const int seed=7112026;const long generation=911;
            string output=Path.GetFullPath("../.local/relay-integration/witness");Directory.CreateDirectory(output);
            Assert.True(NativeSilentStartup.IsSilent);TestMatchLogSession.StartDefault(Path.Combine(output,"TestLogs"));
            var profile=PlayableProfile.RecastGroupCorridorDefault;var map=(ThreeCrossingsMap)profile.AuthoredMap;
            var original=map.Configuration(profile,seed,new[]{new OfflineParticipant("player-1",1,1,OfflineControl.Human),new OfflineParticipant("player-2",2,2,OfflineControl.Human)});
            var costs=new double[4,4];for(int i=0;i<4;i++)for(int j=0;j<4;j++)costs[i,j]=original.RouteCost(i,j);
            var start=new NavPoint(-43,-4);var goal=new NavPoint(-42.5,0);var leave=new NavPoint(-43,-7);
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
                runtime.RequestPause(true);yield return Until(()=>runtime.Latest.Paused,"pause for captured map frame");
                root=new GameObject("Relay ordinary map witness");var timer=System.Diagnostics.Stopwatch.StartNew();world=new PlayableWorld(root.transform,profile);timer.Stop();
                world.Fog.ShowPublic();var cameraObject=new GameObject("Relay gameplay camera");cameraObject.transform.SetParent(root.transform);var camera=cameraObject.AddComponent<Camera>();camera.orthographic=true;camera.orthographicSize=8;camera.transform.position=new Vector3(-32,13,-15);camera.transform.LookAt(new Vector3(-40,1.5f,0));
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
        static void Capture(Camera camera,string path)
        {
            var rt=new RenderTexture(1400,1000,24);var image=new Texture2D(1400,1000,TextureFormat.RGB24,false);var old=RenderTexture.active;
            try{camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,1400,1000),0,0);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());}
            finally{camera.targetTexture=null;RenderTexture.active=old;rt.Release();UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(image);}
        }
    }
}
