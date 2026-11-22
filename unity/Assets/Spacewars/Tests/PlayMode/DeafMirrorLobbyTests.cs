using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

public sealed class DeafMirrorLobbyTests
{
    [UnityTest, Timeout(360000)]
    public IEnumerator TeamDeploymentGeometryAndFirstMovement()
    {
        const BindingFlags fields=BindingFlags.Instance|BindingFlags.NonPublic;
        var host=new GameObject("Deaf Mirror lobby witness");
        var app=host.AddComponent<PlayableBootstrap>();
        T Get<T>(string key)=>(T)typeof(PlayableBootstrap).GetField(key,fields).GetValue(app);
        void Call(string key)=>typeof(PlayableBootstrap).GetMethod(key,fields).Invoke(app,null);
        IEnumerator Until(Func<bool> predicate,int seconds,string phase){
            double deadline=Time.realtimeSinceStartupAsDouble+seconds;
            while(!predicate()&&Time.realtimeSinceStartupAsDouble<deadline)yield return null;
            Assert.True(predicate(),phase);
        }
        var keyboard=Keyboard.current==null?InputSystem.AddDevice<Keyboard>():null;
        var mouse=Mouse.current==null?InputSystem.AddDevice<Mouse>():null;
        bool oldTrailSetting=NativeGraphicsSettings.GroundTrailsEnabled;
        NativeGraphicsSettings.GroundTrailsEnabled=true;
        var cache=PreparedMapCache.DirectoryOverride;
        var output=Environment.GetEnvironmentVariable("MIRROR_EVIDENCE")??Application.temporaryCachePath;
        Directory.CreateDirectory(output);
        PreparedMapCache.DirectoryOverride=Path.Combine(output,"mirror-cache");
        try{
            yield return Until(()=>Get<VisualElement>("lobbyScreen")!=null,45,"lobby");
            Call("ShowLobby");
            Get<VisualElement>("lobbyScreen").Q<DropdownField>("lobby-map-choice").value=DeafMirrorMap.DisplayName;
            var draft=Get<NativeLobbyConfiguration>("lobbySetup");
            draft.HasExplicitSeed=true;draft.ExplicitSeed=21112026;
            CollectionAssert.AreEqual(new[]{1,1,1,1,2,2,2,2},draft.Participants.Select(p=>p.Team));
            var profile=Get<PlayableProfile>("profile");
            var teamChoice=Get<VisualElement>("lobbyScreen").Q<DropdownField>("lobby-team-0");
            CollectionAssert.AreEqual(new[]{"1","2"},teamChoice.choices);
            var invalid=draft.Copy();
            invalid.Participants[0].Team=3;
            Assert.NotNull(invalid.ValidateLobby(profile,true,true));
            invalid.Participants[0].Team=2;
            Assert.NotNull(invalid.ValidateLobby(profile,true,true));
            invalid.Participants.RemoveAt(0);
            Assert.IsNull(invalid.AddParticipant(false,0,8));
            Assert.AreEqual(1,invalid.Participants.Last().Team);
            Assert.IsNull(invalid.ValidateLobby(profile,true,true));
            var map=(DeafMirrorMap)profile.AuthoredMap;
            var sites=map.Sites(profile);
            var mines=sites.Where(s=>s.Kind==PlayableBuildingKind.Mine).ToArray();
            Assert.AreEqual(14,mines.Length);
            var pads=(AntennaCityPads.Pad[])typeof(PlayableWorld).GetMethod("MirrorPads",BindingFlags.Static|BindingFlags.NonPublic)
                .Invoke(null,new object[]{map,profile});
            foreach(var site in mines){
                var pad=pads.Single(candidate=>candidate.SiteId==site.Id);
                Assert.AreEqual(site.Position,pad.Center);
                var vertices=pad.Outer.Footprint.Vertices;
                Assert.AreEqual(6,vertices.Count);
                var south=vertices.Where(p=>Math.Abs(p.Z-pad.Outer.MinZ)<1e-7).OrderBy(p=>p.X).ToArray();
                Assert.AreEqual(2,south.Length,"mine apron has a straight south edge");
                Assert.That((south[0].X+south[1].X)*.5,Is.EqualTo(site.Position.X).Within(1e-7));
            }
            Assert.AreEqual(6,sites.Count(s=>s.Kind==PlayableBuildingKind.Outpost));
            Assert.AreEqual(3,sites.Count(s=>s.Kind==PlayableBuildingKind.RelayTower));
            foreach(var deck in map.Bridges){
                var center=deck.CircleCenter;
                Assert.True(map.SupportsSweep(new NavPoint(center.X,deck.MinZ-2),new NavPoint(center.X,deck.MaxZ+2),profile.TankCollisionRadius));
            }
            foreach(int side in new[]{-1,1}){
                AssertHill(side*40,side*82,29,1.65);
                AssertHill(side*131,-side*14,24,1.35);
            }
            void AssertHill(double x,double z,double reach,double height){
                var a=new NavPoint(x-reach,z);
                var b=new NavPoint(x+reach,z);
                Assert.That(map.SurfaceHeight(new NavPoint(x,z)),Is.EqualTo(height).Within(1e-6));
                Assert.True(map.SupportsSweep(a,b,profile.TankCollisionRadius),$"hill {x},{z} west to east");
                Assert.True(map.SupportsSweep(b,a,profile.TankCollisionRadius),$"hill {x},{z} east to west");
            }
            var firstLaunch=Time.realtimeSinceStartupAsDouble;
            Call("LaunchLobbyMatch");
            yield return Until(()=>!Get<bool>("inLobby")&&!Get<bool>("preparing"),150,"match ready");
            Debug.Log("MIRROR_FIRST_SECONDS "+(Time.realtimeSinceStartupAsDouble-firstLaunch));
            var runtime=Get<PlayableRuntime>("runtime");
            var view=runtime.ParticipantView("player-1");
            Assert.AreEqual(8,view.Participants.Count);
            foreach(var team in view.Participants.GroupBy(p=>p.Team)){
                var homes=team.Select(p=>runtime.ParticipantView(p.Id)).Select(v=>v.Buildings.First(b=>b.Kind==PlayableBuildingKind.Headquarters&&b.Owner==v.Owner));
                Assert.AreEqual(1,homes.Select(b=>Math.Sign(b.Position.X)).Distinct().Count());
            }
            Debug.Log("MIRROR_PROFILE "+Get<NativeBalanceStore>("balanceStore").ActiveIdentity);
            var world=Get<PlayableWorld>("world");
            world.InspectionReveal();
            Capture(Get<Camera>("cameraView"),output,"overview",Vector3.zero,137);
            Capture(Get<Camera>("cameraView"),output,"north-bridge",new Vector3(-66,0,53),29);
            var home=view.Buildings.First(b=>b.Kind==PlayableBuildingKind.Headquarters&&b.Owner==view.Owner);
            var mine=mines.OrderBy(s=>Math.Abs(s.Position.X-home.Position.X)+Math.Abs(s.Position.Z-home.Position.Z)).First();
            var yard=new Vector3((float)(home.Position.X+mine.Position.X)*.5f,0,(float)home.Position.Z);
            var camera=Get<Camera>("cameraView");
            Capture(camera,output,"headquarters-and-mine",yard,22);
            var mineCenter=new Vector3((float)mine.Position.X,0,(float)mine.Position.Z);
            Assert.False(view.Buildings.Any(b=>b.SiteId==mine.Id));
            var emptyPad=world.GraphicsRoot.Find("Site pad "+mine.Id+":0");
            Assert.NotNull(emptyPad);
            AssertSouth(emptyPad,mineCenter);
            Assert.True(emptyPad.Find("Technical apron").GetComponent<MeshRenderer>().enabled);
            Assert.AreEqual("Lunar loading shaft",emptyPad.Find("Technical apron").GetComponent<MeshFilter>().sharedMesh.name);
            Capture(camera,output,"mine-empty",mineCenter,7,ordinaryAngle:true);
            foreach(var site in sites.Where(s=>s.Kind!=PlayableBuildingKind.RelayTower)){
                double r=site.Kind==PlayableBuildingKind.Mine?profile.MineFootprintRadius+2.8:
                    profile.SlotRingRadius+(site.Kind==PlayableBuildingKind.Headquarters?4.8:3.2);
                Assert.AreEqual(GroundTrailSurfaceKind.Hard,world.TrailSurface(new NavPoint(site.Position.X+r*.78,site.Position.Z)));
                Assert.AreEqual(0,map.SurfaceHeight(site.Position),1e-7);
                Assert.True(Enumerable.Range(0,8).Any(i=>{
                    double angle=i*Math.PI/4;
                    var outside=new NavPoint(site.Position.X+(r+3)*Math.Cos(angle),site.Position.Z+(r+3)*Math.Sin(angle));
                    var inside=new NavPoint(site.Position.X+r*.78*Math.Cos(angle),site.Position.Z+r*.78*Math.Sin(angle));
                    return map.SupportsSweep(outside,inside,profile.TankCollisionRadius);
                }),$"tank footprint can enter the apron of {site.Kind} {site.Id}");
            }
            Assert.AreEqual(GroundTrailSurfaceKind.Hard,world.TrailSurface(new NavPoint(-12,2)),"relay hatch");
            Assert.AreEqual(GroundTrailSurfaceKind.Sand,world.TrailSurface(new NavPoint(-15,3)),"soil beside relay hatch");
            Assert.AreEqual(GroundTrailSurfaceKind.Hard,world.TrailSurface(map.Bridges[0].CircleCenter));
            foreach(int turn in new[]{-1,1}){
                foreach(int bankSide in new[]{-1,1}){
                    double edgeZ=bankSide<0?47:61.8;
                    NavPoint Approach(double distance)=>new NavPoint(-66.2*turn,(edgeZ+bankSide*distance)*turn);
                    Assert.AreEqual(GroundTrailSurfaceKind.Hard,world.TrailSurface(Approach(.8)),"bridge sill");
                    Assert.AreEqual(GroundTrailSurfaceKind.Sand,world.TrailSurface(Approach(5)),"dust beyond sill");
                }
            }
            Assert.AreEqual(GroundTrailSurfaceKind.Sand,world.TrailSurface(DeafMirrorMap.Plan(640,320)));
            // Across a graphite reflector foundation: loose dust, hard deck, loose dust.
            foreach(var distance in new[]{-5,0,5}){
                var point=new NavPoint(144-distance*.727,84+distance*.687);
                Assert.AreEqual(distance==0?GroundTrailSurfaceKind.Hard:GroundTrailSurfaceKind.Sand,
                    world.TrailSurface(point),"north-east service strip, offset "+distance);
            }
            var scout=view.Entities.First(e=>e.Owner==view.Owner);
            var from=scout.Position;
            var goal=new NavPoint(from.X-Math.Sign(from.X)*7,from.Z+4);
            Assert.True(runtime.TrySubmit(new PlayableCommand(runtime.Generation,runtime.HumanSequenceWatermark+1,"player-1",PlayableCommandKind.Move,new[]{scout.Id},goal).AsHuman()).Accepted);
            yield return Until(()=>{var q=runtime.ParticipantView("player-1").Entities.Single(e=>e.Id==scout.Id).Position;return Math.Abs(q.X-goal.X)+Math.Abs(q.Z-goal.Z)<2;},70,"scout arrival");
            Assert.True(view.Participants.Where(p=>p.Control==OfflineControl.Ai).Any());
            yield return Until(()=>view.Participants.Where(p=>p.Control==OfflineControl.Ai).Any(p=>{
                var ai=runtime.ParticipantView(p.Id);
                return ai.Buildings.Any(b=>b.Owner==ai.Owner&&b.Kind!=PlayableBuildingKind.Headquarters);
            }),75,"AI construction");
            int bank=Math.Sign(home.Position.X);
            // Cross the service spine between the two central HQ aprons.
            var spine=new NavPoint(bank*203.2,0);
            NavPoint Offset(double dx)=>new NavPoint(spine.X+dx,spine.Z);
            foreach(double dx in new[]{-9.0,9.0}){
                var crossingGoal=Offset(dx);
                Assert.True(runtime.TrySubmit(new PlayableCommand(runtime.Generation,runtime.HumanSequenceWatermark+1,
                    "player-1",PlayableCommandKind.Move,new[]{scout.Id},crossingGoal).AsHuman()).Accepted);
                yield return Until(()=>{
                    var at=runtime.ParticipantView("player-1").Entities.Single(e=>e.Id==scout.Id).Position;
                    return Math.Abs(at.X-crossingGoal.X)+Math.Abs(at.Z-crossingGoal.Z)<2;
                },80,"service apron crossing");
            }
            yield return null;
            Assert.AreEqual(GroundTrailSurfaceKind.Sand,world.TrailSurface(Offset(-8)));
            Assert.AreEqual(GroundTrailSurfaceKind.Hard,world.TrailSurface(spine));
            Assert.AreEqual(GroundTrailSurfaceKind.Sand,world.TrailSurface(Offset(8)));
            Assert.Greater(world.GroundTrailQuadCount,0);
            Capture(camera,output,"service-apron-tracks",world.Point(spine),10,ordinaryAngle:true);
            double cx=bank*131,cz=-bank*14;
            var near=new NavPoint(cx+bank*24,cz);
            var far=new NavPoint(cx-bank*24,cz);
            NavPoint ScoutPosition()=>runtime.ParticipantView("player-1").Entities.Single(e=>e.Id==scout.Id).Position;
            bool crossed=false;
            foreach(var target in new[]{near,far,near}){
                double peak=0;
                bool record=target.Equals(far);
                Assert.True(runtime.TrySubmit(new PlayableCommand(runtime.Generation,runtime.HumanSequenceWatermark+1,
                    "player-1",PlayableCommandKind.Move,new[]{scout.Id},target).AsHuman()).Accepted);
                yield return Until(()=>{
                    var p=ScoutPosition();
                    double height=map.SurfaceHeight(p);
                    if(record&&height>1){
                        Capture(Get<Camera>("cameraView"),output,"regolith-rise",new Vector3((float)cx,1,(float)cz),17,true);
                        record=false;
                    }
                    peak=Math.Max(peak,height);
                    return Math.Abs(p.X-target.X)+Math.Abs(p.Z-target.Z)<2;
                },70,$"scout arrival at {target.X},{target.Z}");
                if(!target.Equals(near)||peak>0)Debug.Log($"MIRROR_ROUTE target={target.X},{target.Z} peak={peak:F3}");
                if(!target.Equals(near)||crossed)Assert.That(peak,Is.GreaterThan(1),"scout crosses the upper slope");
                crossed|=target.Equals(far);
            }
            var mineApproach=new NavPoint(mine.Position.X,mine.Position.Z+profile.MineCaptureRadius*.55);
            Assert.True(runtime.TrySubmit(new PlayableCommand(runtime.Generation,runtime.HumanSequenceWatermark+1,
                "player-1",PlayableCommandKind.Move,new[]{scout.Id},mineApproach).AsHuman()).Accepted);
            yield return Until(()=>runtime.ParticipantView("player-1").Sites.Any(s=>s.Site.Id==mine.Id&&
                s.Claimant==view.Owner&&s.Progress>=1),70,"claim mine site");
            Assert.True(runtime.TrySubmit(new PlayableCommand(runtime.Generation,runtime.HumanSequenceWatermark+1,
                "player-1",PlayableCommandKind.BuildAt,Array.Empty<int>(),siteId:mine.Id,buildingKind:PlayableBuildingKind.Mine).AsHuman()).Accepted);
            PlayableBuildingSnapshot built=null;
            yield return Until(()=>{
                built=runtime.ParticipantView("player-1").Buildings.FirstOrDefault(b=>b.SiteId==mine.Id&&b.Kind==PlayableBuildingKind.Mine&&
                    b.Phase==ConstructionPhase.Ready);
                return built!=null&&world.Actors.ContainsKey(built.Id);
            },70,"complete mine construction");
            Assert.AreEqual(mine.Position,built.Position);
            AssertSouth(world.Actors[built.Id].Hull,mineCenter);
            AssertSouth(world.GraphicsRoot.Find("Site pad "+mine.Id+":0"),mineCenter);
            Assert.False(emptyPad.Find("Technical apron").GetComponent<MeshRenderer>().enabled);
            Debug.Log($"MIRROR_MINE site={mine.Id} position={mine.Position.X},{mine.Position.Z} facing=south empty-and-built");
            Capture(camera,output,"mine-built",mineCenter,7,ordinaryAngle:true);
            Call("ReturnToLobby");
            yield return Until(()=>Get<bool>("inLobby"),30,"return to lobby");
            draft=Get<NativeLobbyConfiguration>("lobbySetup");
            draft.Participants.RemoveAll(p=>p.Team==1&&!p.Human);
            draft.Participants.RemoveAt(draft.Participants.Count-1);
            Call("RebuildRoster");
            var repeatLaunch=Time.realtimeSinceStartupAsDouble;
            Call("LaunchLobbyMatch");
            yield return Until(()=>!Get<bool>("inLobby")&&!Get<bool>("preparing"),90,"reload");
            Debug.Log("MIRROR_REPEAT_SECONDS "+(Time.realtimeSinceStartupAsDouble-repeatLaunch));
            Assert.True(Get<PlayableWorld>("world").PreparationCacheHit);
            Assert.True(Get<PlayableMapTerrain>("mapTerrain").PreparationCacheHit);
            var small=Get<PlayableRuntime>("runtime").ParticipantView("player-1");
            Assert.AreEqual(4,small.Participants.Count);
            Assert.AreEqual(4,small.PublicSites.Count(s=>s.Kind==PlayableBuildingKind.Headquarters));
            var smallRuntime=Get<PlayableRuntime>("runtime");
            var homesByTeam=small.Participants.GroupBy(p=>p.Team).OrderBy(g=>g.Count()).Select(team=>
                team.Select(p=>smallRuntime.ParticipantView(p.Id)).Select(v=>v.Buildings.Single(b=>b.Owner==v.Owner&&b.Kind==PlayableBuildingKind.Headquarters).Position).ToArray()).ToArray();
            Assert.AreEqual(1,homesByTeam[0].Length);
            Assert.AreEqual(3,homesByTeam[1].Length);
            Assert.AreEqual(2,homesByTeam[1].Count(h=>Math.Abs(h.Z)>50));
            var opposingCenter=homesByTeam[1].Single(h=>Math.Abs(h.Z)<50);
            Assert.AreEqual(-opposingCenter.Z,homesByTeam[0][0].Z,1e-6);
            Assert.AreEqual(-opposingCenter.X,homesByTeam[0][0].X,1e-6);
            var smallWorld=Get<PlayableWorld>("world");
            yield return Until(()=>smallWorld.GraphicsRoot.Find("Abandoned observatory stations")!=null,5,"vacant station ruins");
            var ruins=smallWorld.GraphicsRoot.Find("Abandoned observatory stations");
            Assert.AreEqual(4,ruins.childCount);
            smallWorld.InspectionReveal();
            Capture(camera,output,"vacant-station",ruins.GetChild(0).position,9,ordinaryAngle:true);
            Capture(camera,output,"asymmetric-start",Vector3.zero,137);
            Call("ReturnToLobby");
            yield return Until(()=>Get<bool>("inLobby"),30,"return for 3×3");
            draft=Get<NativeLobbyConfiguration>("lobbySetup");
            Assert.IsNull(draft.AddParticipant(false,0,8));
            Assert.IsNull(draft.AddParticipant(false,0,8));
            Call("RebuildRoster");
            Call("LaunchLobbyMatch");
            yield return Until(()=>!Get<bool>("inLobby")&&!Get<bool>("preparing"),90,"3×3 ready");
            var trioRuntime=Get<PlayableRuntime>("runtime");
            var trios=trioRuntime.ParticipantView("player-1");
            Assert.AreEqual(6,trios.PublicSites.Count(s=>s.Kind==PlayableBuildingKind.Headquarters));
            var centers=trios.Participants.GroupBy(p=>p.Team).Select(team=>{
                var homes=team.Select(p=>trioRuntime.ParticipantView(p.Id)).Select(v=>v.Buildings.Single(b=>b.Owner==v.Owner&&b.Kind==PlayableBuildingKind.Headquarters).Position).ToArray();
                Assert.AreEqual(3,homes.Length);
                Assert.AreEqual(2,homes.Count(h=>Math.Abs(h.Z)>50));
                return homes.Single(h=>Math.Abs(h.Z)<50);
            }).ToArray();
            Assert.AreEqual(2,centers.Length);
            Assert.AreEqual(-centers[0].X,centers[1].X);
            Assert.AreEqual(-centers[0].Z,centers[1].Z,1e-6);
            var trioWorld=Get<PlayableWorld>("world");
            yield return Until(()=>trioWorld.GraphicsRoot.Find("Abandoned observatory stations")?.childCount==2,5,"two vacant central sites");
            trioWorld.InspectionReveal();
            Capture(camera,output,"three-against-three",Vector3.zero,137);
            for(int pair=0;pair<3;pair++){
                var rock=map.Rocks[2*pair].CircleCenter;
                Capture(camera,output,"outcrop-"+pair,trioWorld.Point(rock),10,lowAngle:true);
            }
            Capture(camera,output,"fissure",new Vector3(4,-2,49),35,lowAngle:true);
            foreach(var facility in map.Facilities.Where(f=>f.CircleCenter.Z>80)){
                var point=facility.CircleCenter;
                Capture(camera,output,point.X<0?"west-dish":"east-reflector",trioWorld.Point(point),13,ordinaryAngle:true);
                if(point.X<0)Capture(camera,output,"west-ridge",trioWorld.Point(point)+Vector3.forward*8,30,ordinaryAngle:true);
            }
            var eastHome=map.Headquarters((PlayableOwner)4);
            var eastMine=sites.Where(site=>site.Kind==PlayableBuildingKind.Mine)
                .OrderBy(site=>(site.Position.X-eastHome.X)*(site.Position.X-eastHome.X)
                    +(site.Position.Z-eastHome.Z)*(site.Position.Z-eastHome.Z)).First();
            Capture(camera,output,"north-east-headquarters",(trioWorld.Point(eastHome)+trioWorld.Point(eastMine.Position))*.5f,22);
            Capture(camera,output,"east-yard",new Vector3(165,0,3),42,ordinaryAngle:true);
            var relay=sites.Where(s=>s.Kind==PlayableBuildingKind.RelayTower).OrderBy(s=>Math.Abs(s.Position.Z)).First();
            Capture(camera,output,"central-relay",trioWorld.Point(relay.Position),16,ordinaryAngle:true);
            var crossing=map.Bridges.OrderByDescending(b=>b.CircleCenter.Z).First();
            var units=trios.Entities.Where(e=>e.Owner==trios.Owner).Select(e=>e.Id).ToArray();
            Assert.IsNotEmpty(units);
            var bridgeCenter=crossing.CircleCenter;
            Assert.True(trioRuntime.TrySubmit(new PlayableCommand(trioRuntime.Generation,trioRuntime.HumanSequenceWatermark+1,
                "player-1",PlayableCommandKind.Move,units,bridgeCenter).AsHuman()).Accepted);
            yield return Until(()=>trioRuntime.ParticipantView("player-1").Entities.Any(e=>
                units.Contains(e.Id)&&crossing.Contains(e.Position)),90,"opening unit on the north bridge");
            yield return null;
            Capture(camera,output,"north-bridge-ordinary",trioWorld.Point(bridgeCenter),19,ordinaryAngle:true);
        }finally{
            NativeGraphicsSettings.GroundTrailsEnabled=oldTrailSetting;
            PreparedMapCache.DirectoryOverride=cache;
            UnityEngine.Object.Destroy(host);
            if(keyboard!=null)InputSystem.RemoveDevice(keyboard);
            if(mouse!=null)InputSystem.RemoveDevice(mouse);
        }
    }
    [Test]
    public void CentralSeatsAndDiagonalFlanksForEveryTeamSize(){
        var map=new DeafMirrorMap();
        var duels=new System.Collections.Generic.HashSet<int>();
        for(int a=1;a<=4;a++)for(int b=1;b<=4;b++)for(int seed=0;seed<32;seed++){
            var roster=Enumerable.Range(0,a+b).Select(i=>new OfflineParticipant("p"+i,i+1,i<a?1:2,OfflineControl.Ai)).ToArray();
            var seats=map.StartingSites(roster,seed);
            Assert.AreEqual(a+b,seats.Distinct().Count());
            int Row(int id)=>(id-1)%4;
            int Bank(int id)=>(id-1)/4;
            Assert.AreNotEqual(Bank(seats[0]),Bank(seats[a]));
            if((a==1||a==3)&&(b==1||b==3)){
                int left=seats.Take(a).Select(Row).Single(r=>r==1||r==2);
                int right=seats.Skip(a).Select(Row).Single(r=>r==1||r==2);
                Assert.AreEqual(3,left+right);
            }
            foreach(var team in new[]{seats.Take(a).ToArray(),seats.Skip(a).ToArray()}){
                Assert.AreEqual(1,team.Select(Bank).Distinct().Count());
                if(team.Length==3){
                    CollectionAssert.AreEquivalent(new[]{0,3},team.Take(2).Select(Row));
                    Assert.Contains(Row(team[2]),new[]{1,2});
                    continue;
                }
                Assert.True(team.Take(2).All(s=>Row(s)==1||Row(s)==2));
                Assert.True(team.Skip(2).All(s=>Row(s)==0||Row(s)==3));
            }
            if(a==1&&b==1)duels.Add(seats[0]);
        }
        Assert.AreEqual(4,duels.Count);
    }

    static void AssertSouth(Transform model,Vector3 center){
        Assert.That(model.position.x,Is.EqualTo(center.x).Within(.001f));
        Assert.That(model.position.z,Is.EqualTo(center.z).Within(.001f));
        Assert.That(Vector3.Angle(model.forward,Vector3.back),Is.LessThan(.01f));
    }

    static void Capture(Camera camera,string folder,string name,Vector3 target,float size,bool lowAngle=false,bool ordinaryAngle=false){
        var rt=RenderTexture.GetTemporary(1600,900,24);
        var old=camera.targetTexture;var active=RenderTexture.active;
        var position=camera.transform.position;var rotation=camera.transform.rotation;
        float zoom=camera.orthographicSize,far=camera.farClipPlane;
        var pixels=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);
        try{
            camera.targetTexture=rt;camera.farClipPlane=1000;camera.orthographicSize=size;
            if(ordinaryAngle)camera.transform.position=target-camera.transform.forward*100;
            else{
                camera.transform.position=target+(lowAngle?new Vector3(-105,46,-94):new Vector3(0,330,-115));
                camera.transform.LookAt(target);
            }
            camera.Render();RenderTexture.active=rt;pixels.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);pixels.Apply();
            File.WriteAllBytes(Path.Combine(folder,name+".png"),pixels.EncodeToPNG());
        }finally{
            camera.targetTexture=old;camera.farClipPlane=far;camera.orthographicSize=zoom;
            camera.transform.SetPositionAndRotation(position,rotation);RenderTexture.active=active;
            UnityEngine.Object.Destroy(pixels);RenderTexture.ReleaseTemporary(rt);
        }
    }
}
