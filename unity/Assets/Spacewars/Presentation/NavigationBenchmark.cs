using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using Spacewars.Simulation;
using Spacewars.Runtime;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Spacewars.Presentation
{
    public sealed class NavigationBenchmark : MonoBehaviour
    {
        private readonly List<string> frames = new List<string>();
        private readonly List<GameObject> visuals = new List<GameObject>();
        private Material blue, orange, stone;
        private readonly List<GameObject> walls = new List<GameObject>();
        private string output;
        private int cameras = 1;
        private bool stress=true;private int repeats=3;
        private volatile bool stopped;
        private Thread worker;
        private NavPoint[] latest = Array.Empty<NavPoint>();
        private string metricContext="setup,none,0,0";
        private string status = "Preparing navigation comparison";
        private readonly object gate = new object();
        private readonly Queue<Action> mainJobs = new Queue<Action>();
        private Exception failure;
        private int completed;
        private int captureDone;
        private static string F(double value) { return value.ToString("R",CultureInfo.InvariantCulture); }
        private void Start()
        {
            AudioListener.volume=0; QualitySettings.vSyncCount=0; Application.targetFrameRate=-1;
            var args=Environment.GetCommandLineArgs();
            for(int i=0;i+1<args.Length;i++) { if(args[i]=="-navigationEvidence") output=args[i+1]; if(args[i]=="-navigationCameras") cameras=int.Parse(args[i+1]);if(args[i]=="-navigationStress")stress=args[i+1]!="0";if(args[i]=="-navigationRepeats")repeats=Math.Max(1,Math.Min(3,int.Parse(args[i+1]))); }
            if(string.IsNullOrEmpty(output)) { Debug.LogError("-navigationEvidence directory is required"); Application.Quit(2); return; }
            Directory.CreateDirectory(output);
            var profileRows=new List<string>{"path,group,label,value,unit,min,max,step"};var exportedProfile=new NavigationProfile();
            foreach(var field in NavigationProfile.Metadata)profileRows.Add(field.Path+","+field.Group+","+field.Label+","+F(field.Read(exportedProfile))+","+field.Unit+","+F(field.Minimum)+","+F(field.Maximum)+","+F(field.Step));
            File.WriteAllLines(Path.Combine(output,"profile.csv"),profileRows);
            blue=Material(new Color(.15f,.65f,.85f)); orange=Material(new Color(.85f,.44f,.12f)); stone=Material(new Color(.27f,.31f,.32f));
            var ground=GameObject.CreatePrimitive(PrimitiveType.Cube); ground.transform.position=new Vector3(0,-.3f,0); ground.transform.localScale=new Vector3(100,.5f,100); ground.GetComponent<Renderer>().sharedMaterial=Material(new Color(.1f,.15f,.17f));
            var light=new GameObject("Sun").AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.5f;light.transform.rotation=Quaternion.Euler(50,-35,0);
            var displayProfile=new NavigationProfile();
            for(int i=0;i<cameras;i++){var camera=new GameObject("Navigation Camera").AddComponent<Camera>();camera.orthographic=true;camera.orthographicSize=(float)displayProfile.CameraOrthoSize;camera.transform.position=new Vector3(0,(float)displayProfile.CameraHeight,(float)displayProfile.CameraOffsetZ);camera.transform.LookAt(Vector3.zero);camera.backgroundColor=new Color(.035f,.055f,.065f);camera.clearFlags=CameraClearFlags.SolidColor;
                if(cameras==2) camera.rect=new Rect(i*.5f,0,.5f,1);if(cameras==4)camera.rect=new Rect((i%2)*.5f,(i/2)*.5f,.5f,.5f);}
            worker=new Thread(Run){IsBackground=true,Name="Spacewars.NavigationExperiment"};worker.Start();
        }
        private Material Material(Color color) {var template=Resources.Load<Material>("RuntimeLit");var m=new Material(template);m.color=color;return m;}
        private void Update()
        {
            frames.Add(metricContext+","+F(Time.unscaledDeltaTime*1000d));
            // Technical queue-service quantum bounds main-thread work; not a simulation parameter.
            for(int i=0;i<4;i++){Action job=null;lock(gate){if(mainJobs.Count>0)job=mainJobs.Dequeue();}if(job==null)break;job();}
            var positions=Volatile.Read(ref latest);
            while(visuals.Count<positions.Length){var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.transform.localScale=new Vector3(1.1f,.7f,1.4f);go.GetComponent<Renderer>().sharedMaterial=visuals.Count%2==0?blue:orange;visuals.Add(go);}
            for(int i=0;i<visuals.Count;i++){visuals[i].SetActive(i<positions.Length);if(i<positions.Length)visuals[i].transform.position=new Vector3((float)positions[i].X,.4f,(float)positions[i].Z);}
            if(completed!=0){completed=0;StartCoroutine(Finish());}
        }
        private void OnGUI(){GUI.color=Color.white;GUI.Label(new Rect(20,12,1100,40),"SPACEWARS · NAVIGATION U2 · DIAGNOSTIC\n"+status);}
        private IEnumerator CaptureFixture(string name)
        {
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(output,name+"-"+cameras+"-camera.png"));
            yield return null;Volatile.Write(ref captureDone,1);
        }
        private IEnumerator Finish()
        {
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(output,"native-"+cameras+"-camera.png"));
            File.WriteAllLines(Path.Combine(output,"frames-"+cameras+".csv"),new[]{"scenario,backend,count,repeat,frame_ms"}.Concat(frames));
            File.WriteAllText(Path.Combine(output,"host-"+cameras+".json"),JsonUtility.ToJson(new Host{unity=Application.unityVersion,cpu=SystemInfo.processorType,gpu=SystemInfo.graphicsDeviceName,os=SystemInfo.operatingSystem,memoryMiB=SystemInfo.systemMemorySize,width=Screen.width,height=Screen.height,cameras=cameras,repeats=repeats,stress=stress,profile="unity-navigation-diagnostic-v1@1",fixture="deterministic-grid-v1",seed=0,classification="DIAGNOSTIC_NOT_PERFORMANCE_ACCEPTANCE",failure=failure==null?"":failure.ToString()},true));
            yield return null;yield return null;Application.Quit(failure==null?0:1);
        }
        [Serializable] private sealed class Host{public string unity,cpu,gpu,os,profile,fixture,classification,failure;public int memoryMiB,width,height,cameras,seed,repeats;public bool stress;}
        private T Main<T>(Func<T> action)
        {
            T result=default(T);Exception error=null;int done=0;
            lock(gate){if(mainJobs.Count>=256)throw new InvalidOperationException("Main-thread navigation queue full");mainJobs.Enqueue(()=>{try{result=action();}catch(Exception e){error=e;}finally{Volatile.Write(ref done,1);}});}
            // Serial experiment coordinator suspends simulated time during planning/replanning. Not a live game runtime.
            while(Volatile.Read(ref done)==0&&!stopped)Thread.Sleep(1);
            if(stopped)throw new OperationCanceledException();if(error!=null)throw error;return result;
        }
        private void Run()
        {
            try
            {
                LiveProbe();
                ArrivalProbe("field");ArrivalProbe("navmesh");
                using(var summary=new StreamWriter(Path.Combine(output,"trials-"+cameras+".csv")))
                using(var steps=new StreamWriter(Path.Combine(output,"steps-"+cameras+".csv")))
                using(var requests=new StreamWriter(Path.Combine(output,"requests-"+cameras+".csv")))
                {
                    summary.WriteLine("scenario,backend,count,repeat,bake_ms,route_ms,steps,arrived,moving,unreachable,overlaps,geometryViolations,holdViolations,stepTotal_ms,wall_ms,gc0,heapBytes,rejectedResults,fieldBuilds,repairs,repairFailed,repairCpuMs,maxNoProgressTicks,fieldCacheHits,bakeCpuMs,geometryRebuilds,rebuildResponseMs,rebuildCpuMs");
                    steps.WriteLine("scenario,backend,count,repeat,tick,step_ms");requests.WriteLine("scenario,backend,count,repeat,entity,response_ms,query_cpu_ms,points");
                    string[] scenarios={"open","turn","passage","opposing","hold","stop","unreachable","construction","retarget","distinct"};
                    bool correctness=true;
                    // Warmup is retained separately and excluded from selection statistics.
                    Trial("warmup","field",12,-1,summary,steps,requests);
                    Trial("warmup","navmesh",12,-1,summary,steps,requests);
                    for(int repeat=0;repeat<repeats;repeat++)foreach(string scenario in scenarios)
                        foreach(string backend in repeat%2==0?new[]{"field","navmesh"}:new[]{"navmesh","field"})
                            correctness &= Trial(scenario,backend,12,repeat,summary,steps,requests);
                    bool scaleCorrectness=true;
                    if(correctness&&stress)foreach(int count in new[]{50,100,200,500})for(int repeat=0;repeat<3;repeat++)
                        foreach(string backend in repeat%2==0?new[]{"field","navmesh"}:new[]{"navmesh","field"})scaleCorrectness &= Trial("open",backend,count,repeat,summary,steps,requests);
                    status=!correctness?"Correctness failures retained — scale gate closed":!scaleCorrectness?"Scale failures retained — no scale acceptance":stress?"Serial comparison complete — hardware acceptance open":"Small scenarios complete — stress excluded from this invocation";
                    File.WriteAllText(Path.Combine(output,"correctness-"+cameras+".txt"),status);
                }
            }catch(Exception e){failure=e;status=e.Message;}finally{Volatile.Write(ref completed,1);}
        }
        [Serializable] private sealed class FixturePoint{public int id;public double x,z;public bool held;}
        [Serializable] private sealed class FixtureRect{public double minX,minZ,maxX,maxZ;}
        [Serializable] private sealed class TrialFixture{public string version,scenario,profile;public int count,seed;public double halfExtent;public FixturePoint[] starts,goals;public FixtureRect[] obstacles,navigationObstacles;public string[] events;}
        private static FixtureRect[] Rects(NavGeometry geometry){if(geometry.Obstacles.Any(r=>r.Polygon!=null))throw new InvalidOperationException("Rectangle fixture export cannot represent polygon terrain");return geometry.Obstacles.Select(r=>new FixtureRect{minX=r.MinX,minZ=r.MinZ,maxX=r.MaxX,maxZ=r.MaxZ}).ToArray();}
        private void SaveFixture(string scenario,int count,NavigationProfile profile,NavGeometry geometry,NavGeometry routeGeometry,NavCrowd crowd,NavPoint[] goals)
        {
            var events=new List<string>{"tick 0: shared group goal plus allocated slots; distinct scenario uses individual goals"};
            if(scenario=="stop")events.Add("tick 90: Stop/HOLD all commanded units");
            if(scenario=="retarget")events.Add("tick 90: new common destination (-30,15), reallocate slots");
            if(scenario=="construction"){events.Add("tick 90: add rectangle [-3,-8,3,8], geometry revision 2 and replan");events.Add("tick 240: remove rectangle, geometry revision 3 and replan");}
            var fixture=new TrialFixture{version="deterministic-grid-v3-runtime-arrivals",scenario=scenario,count=count,seed=0,profile=profile.ProfileId+"@"+profile.Revision,halfExtent=geometry.HalfExtent,
                starts=crowd.Units.Select(u=>new FixturePoint{id=u.Id,x=u.Position.X,z=u.Position.Z,held=u.Held}).ToArray(),
                goals=goals.Select((p,id)=>new FixturePoint{id=id,x=p.X,z=p.Z}).ToArray(),obstacles=Rects(geometry),navigationObstacles=Rects(routeGeometry),events=events.ToArray()};
            string json=JsonUtility.ToJson(fixture,true),path=Path.Combine(output,"fixture-"+scenario+"-"+count+".json");
            if(File.Exists(path)&&File.ReadAllText(path)!=json)throw new Exception("A/B fixture mismatch: "+scenario);
            File.WriteAllText(path,json);
        }
        private void ArrivalProbe(string backend)
        {
            using(var report=new StreamWriter(Path.Combine(output,"arrival-regression-"+backend+"-"+cameras+".csv"))){
                report.WriteLine("case,backend,outcome,ticks,overlaps,geometryViolations,x,z");
                foreach(bool dense in new[]{false,true}){
                    var profile=new NavigationProfile();var geometry=new NavGeometry(50,Array.Empty<NavObstacle>(),1);var crowd=new NavCrowd(geometry,profile);
                    crowd.Add(0,new NavPoint(-24,0));var goal=new NavPoint(24,0);
                    if(dense){int id=1;for(int x=-1;x<=1;x++)for(int z=-1;z<=1;z++)if(x!=0||z!=0)crowd.Add(id++,new NavPoint(24+x*1.8,z*1.8));}
                    else{var slots=NavArrivalAllocator.Allocate(geometry,profile,goal,12);for(int i=1;i<12;i++)crowd.Add(i,slots[i]);goal=slots[0];}
                    UnityNavigationRouter native=null;NavPoint[] route;
                    if(backend=="navmesh"){native=Main(()=>new UnityNavigationRouter(geometry,profile));route=Main(()=>native.FindPath(crowd.Units[0].Position,goal));}
                    else route=new SharedFlowRouter(geometry,profile).FindPath(crowd.Units[0].Position,goal);
                    if(!crowd.SetRoute(0,goal,route))throw new Exception("Arrival probe initial route rejected");
                    int tick=0,overlaps=0,violations=0;
                    for(;tick<2700&&crowd.Units[0].Moving;tick++){
                        crowd.Step(1d/30);var position=crowd.Units[0].Position;
                        if(!geometry.IsFree(position,profile.Radius))violations++;
                        for(int i=1;i<crowd.Units.Count;i++){var other=crowd.Units[i].Position;double dx=position.X-other.X,dz=position.Z-other.Z;if(dx*dx+dz*dz<4*profile.Radius*profile.Radius-1e-7)overlaps++;}
                    }
                    var unit=crowd.Units[0];report.WriteLine((dense?"dense-enclosed":"late-inner-slot")+","+backend+","+unit.Outcome+","+tick+","+overlaps+","+violations+","+F(unit.Position.X)+","+F(unit.Position.Z));report.Flush();
                    if(native!=null)Main(()=>{native.Dispose();return true;});
                    if(unit.Outcome!=(dense?NavigationOutcome.Blocked:NavigationOutcome.Arrived)||overlaps!=0||violations!=0)throw new Exception("Arrival regression failed: "+backend+" dense="+dense);
                }
            }
        }
        private void LiveProbe()
        {
            metricContext="live-delay,none,1,0";
            var runtime=new NavigationRuntime(42,new NavGeometry(50,Array.Empty<NavObstacle>(),1),new NavigationProfile(),new[]{new NavPoint(0,0)});
            var rows=new List<string>{"elapsed_ms,tick,x,z,rejected"};var timer=Stopwatch.StartNew();
            try{
                runtime.TrySubmit(new NavigationIntent(0,new NavPoint(20,0)));
                NavigationRequest request=null;
                if(!SpinWait.SpinUntil(()=>runtime.Requests.TryDequeue(out request),2000))throw new Exception("Live request missing");
                long tick=runtime.Latest.Tick;
                while(runtime.Latest.Tick<tick+12&&timer.ElapsedMilliseconds<2000){var view=runtime.Latest;rows.Add(F(timer.Elapsed.TotalMilliseconds)+","+view.Tick+","+F(view.Positions[0].X)+","+F(view.Positions[0].Z)+","+view.Rejected);Thread.Sleep(10);}
                if(runtime.Latest.Tick<tick+12)throw new Exception("Planner stalled live ticks");
                runtime.TrySubmit(new NavigationIntent(0,new NavPoint(0,0),true,true));tick=runtime.Latest.Tick;
                if(!SpinWait.SpinUntil(()=>runtime.Latest.Tick>=tick+2,2000))throw new Exception("Stop not serviced");
                runtime.Answers.TryEnqueue(new NavigationAnswer(request,new[]{new NavPoint(20,0)}));
                if(!SpinWait.SpinUntil(()=>runtime.Latest.Rejected==1,2000)||runtime.Latest.Positions[0].X!=0)throw new Exception("Late navigation moved stopped unit");
                File.WriteAllLines(Path.Combine(output,"live-delay-"+cameras+".csv"),rows);
            }finally{runtime.Dispose();if(!SpinWait.SpinUntil(()=>runtime.IsStopped,2000))throw new Exception("Live worker shutdown failed");}
        }
        private bool Trial(string scenario,string backend,int count,int repeat,StreamWriter summary,StreamWriter steps,StreamWriter requests)
        {
            metricContext=scenario+","+backend+","+count+","+repeat;
            status=scenario+" / "+backend+" / "+count+" units / repeat "+repeat;
            var profile=new NavigationProfile();
            var obstacles=new List<NavObstacle>();
            if(scenario=="turn")obstacles.Add(new NavObstacle(-5,-20,5,8));
            if(scenario=="passage"||scenario=="opposing"||scenario=="hold"){obstacles.Add(new NavObstacle(-3,-50,3,-6));obstacles.Add(new NavObstacle(-3,6,3,50));}
            if(scenario=="unreachable")obstacles.Add(new NavObstacle(-2,-50,2,50));
            var geometry=new NavGeometry(count>12?100:50,obstacles.ToArray(),1);
            var routeObstacles=new List<NavObstacle>(obstacles);
            if(scenario=="hold")routeObstacles.Add(new NavObstacle(-profile.Radius,-profile.Radius,profile.Radius,profile.Radius));
            var routeGeometry=new NavGeometry(count>12?100:50,routeObstacles.ToArray(),1);
            Main(()=>{foreach(var old in walls)Destroy(old);walls.Clear();foreach(var obstacle in geometry.Obstacles){var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.transform.position=new Vector3((float)(obstacle.MinX+obstacle.MaxX)/2,1,(float)(obstacle.MinZ+obstacle.MaxZ)/2);go.transform.localScale=new Vector3((float)(obstacle.MaxX-obstacle.MinX),2,(float)(obstacle.MaxZ-obstacle.MinZ));go.GetComponent<Renderer>().sharedMaterial=stone;walls.Add(go);}return true;});
            var session=new NavigationSession(1,geometry,profile,routeGeometry);
            var crowd=session.Crowd;
            int columns=(int)Math.Ceiling(Math.Sqrt(count));
            var goals=new NavPoint[count];
            for(int i=0;i<count;i++){
                bool reverse=scenario=="opposing"&&i%2==1;
                var start=new NavPoint((reverse?24:count>12?-70:-24)+(i/columns)*1.8,(i%columns-columns/2)*1.8);
                goals[i]=new NavPoint((reverse?-24:count>12?48:24)+(i/columns)*profile.ArrivalSlotSpacing,(i%columns-columns/2)*profile.ArrivalSlotSpacing);
                if(scenario=="distinct")goals[i]=new NavPoint(20+(i%3)*3,-16+(i/3)*4);
                crowd.Add(i,start);
            }
            if(scenario=="hold"){crowd.Add(count,new NavPoint(0,0));crowd.Stop(count,true);}
            if(scenario!="distinct"){
                if(scenario=="opposing"){
                    var forward=Enumerable.Range(0,count).Where(i=>i%2==0).ToArray();var reverse=Enumerable.Range(0,count).Where(i=>i%2==1).ToArray();
                    var a=session.AllocateArrivalSlots(new NavPoint(24,0),forward);var b=session.AllocateArrivalSlots(new NavPoint(-24,0),reverse);
                    for(int i=0;i<forward.Length;i++)goals[forward[i]]=a[i];for(int i=0;i<reverse.Length;i++)goals[reverse[i]]=b[i];
                }else{goals=session.AllocateArrivalSlots(new NavPoint(count>12?48:24,0),Enumerable.Range(0,count).ToArray());if(goals.Length!=count)throw new Exception("Arrival allocator could not fit fixture");}
            }
            SaveFixture(scenario,count,profile,geometry,routeGeometry,crowd,goals);
            UnityNavigationRouter native=null;SharedFlowRouter field=null;
            Stopwatch clock=Stopwatch.StartNew();
            if(backend=="navmesh")native=Main(()=>new UnityNavigationRouter(routeGeometry,profile));else field=new SharedFlowRouter(routeGeometry,profile);
            double bake=clock.Elapsed.TotalMilliseconds,bakeCpu=native==null?0:native.BuildMilliseconds,routeTotal=0,rebuildResponse=0,rebuildCpu=0;int unreachable=0,fieldBuilds=0,fieldHits=0,geometryRebuilds=0;
            Action plan=()=>{for(int i=0;i<count;i++){
                if(!session.Move(i,goals[i]))throw new InvalidOperationException("Navigation request queue overflow");
                NavigationRequest request;if(!session.Requests.TryDequeue(out request))throw new InvalidOperationException("Missing request");
                double queryCpu=0;
                // A real shared group destination; both providers append the same independent arrival slots.
                var sharedGoal=scenario=="distinct"?request.Goal:goals[scenario=="opposing"&&i%2==1?1:0];
                Func<NavPoint[]> find=()=>{int previousBuilds=field==null?0:field.RebuildCount,previousHits=field==null?0:field.FieldCacheHits;var q=Stopwatch.StartNew();var result=backend=="navmesh"?native.FindPath(request.Start,sharedGoal):field.FindPath(request.Start,sharedGoal);if(result.Length>0&&(sharedGoal.X!=request.Goal.X||sharedGoal.Z!=request.Goal.Z)){
                    if(!geometry.SegmentFree(sharedGoal,request.Goal,profile.Radius))result=Array.Empty<NavPoint>();
                    else{var expanded=new NavPoint[result.Length+1];Array.Copy(result,expanded,result.Length);expanded[result.Length]=request.Goal;result=expanded;}}
                    queryCpu=q.Elapsed.TotalMilliseconds;if(field!=null){fieldBuilds+=field.RebuildCount-previousBuilds;fieldHits+=field.FieldCacheHits-previousHits;}return result;};
                clock.Restart();var route=backend=="navmesh"?Main(find):find();
                double ms=clock.Elapsed.TotalMilliseconds;routeTotal+=ms;requests.WriteLine(scenario+","+backend+","+count+","+repeat+","+i+","+F(ms)+","+F(queryCpu)+","+route.Length);
                if(route.Length==0)unreachable++;
                if(!session.Answers.TryEnqueue(new NavigationAnswer(request,route)))throw new InvalidOperationException("Navigation answer queue overflow");
                session.ApplyResults();
            }};
            plan();NavPoint[] stoppedPositions=null;int overlaps=0,violations=0,holdViolations=0,doneTicks=0;double stepTotal=0;int gc=GC.CollectionCount(0);var wall=Stopwatch.StartNew();
            // 90 simulation seconds; workload duration is protocol, not balance.
            for(int tick=0;tick<2700&&!stopped;tick++){
                if(tick==90&&scenario=="stop"){stoppedPositions=crowd.Units.Select(u=>u.Position).ToArray();for(int i=0;i<count;i++)session.Stop(i,true);}
                if(tick==90&&scenario=="retarget"){goals=session.AllocateArrivalSlots(new NavPoint(-30,15),Enumerable.Range(0,count).ToArray());plan();}
                if((tick==90||tick==240)&&scenario=="construction"){
                    geometry=new NavGeometry(50,tick==90?new[]{new NavObstacle(-3,-8,3,8)}:Array.Empty<NavObstacle>(),tick==90?2:3);session.ChangeGeometry(geometry);
                    var rebuildClock=Stopwatch.StartNew();geometryRebuilds++;if(native!=null){Main(()=>{native.Dispose();native=new UnityNavigationRouter(geometry,profile);return true;});rebuildCpu+=native.BuildMilliseconds;}else field=new SharedFlowRouter(geometry,profile);rebuildResponse+=rebuildClock.Elapsed.TotalMilliseconds;plan();}
                clock.Restart();session.Step(1d/30);double ms=clock.Elapsed.TotalMilliseconds;stepTotal+=ms;steps.WriteLine(scenario+","+backend+","+count+","+repeat+","+tick+","+F(ms));doneTicks++;
                for(int i=0;i<crowd.Units.Count;i++){
                    if(!geometry.IsFree(crowd.Units[i].Position,profile.Radius))violations++;
                    for(int j=0;j<i;j++){var a=crowd.Units[i].Position;var b=crowd.Units[j].Position;double dx=a.X-b.X,dz=a.Z-b.Z;if(dx*dx+dz*dz<4*profile.Radius*profile.Radius-1e-7)overlaps++;}
                }
                if(stoppedPositions!=null)for(int i=0;i<count;i++)if(crowd.Units[i].Position.X!=stoppedPositions[i].X||crowd.Units[i].Position.Z!=stoppedPositions[i].Z)holdViolations++;
                if(scenario=="hold"){var p=crowd.Units[count].Position;if(p.X!=0||p.Z!=0)holdViolations++;}
                if(tick%30==0)Volatile.Write(ref latest,crowd.Units.Select(u=>u.Position).ToArray());
                if(tick==210&&repeat==0&&count==12&&backend=="field"&&(scenario=="turn"||scenario=="passage"||scenario=="opposing")){
                    Volatile.Write(ref latest,crowd.Units.Select(u=>u.Position).ToArray());Volatile.Write(ref captureDone,0);
                    Main(()=>{StartCoroutine(CaptureFixture(scenario));return true;});
                    while(Volatile.Read(ref captureDone)==0&&!stopped)Thread.Sleep(1);
                }
                if(tick>300&&crowd.Units.All(u=>!u.Moving))break;
            }
            int moving=crowd.Units.Take(count).Count(u=>u.Moving);int arrived=crowd.Units.Take(count).Count(u=>{var p=u.Position;var g=goals[u.Id];return Math.Sqrt((p.X-g.X)*(p.X-g.X)+(p.Z-g.Z)*(p.Z-g.Z))<=profile.ArrivalTolerance;});
            summary.WriteLine(scenario+","+backend+","+count+","+repeat+","+F(bake)+","+F(routeTotal)+","+doneTicks+","+arrived+","+moving+","+unreachable+","+overlaps+","+violations+","+holdViolations+","+F(stepTotal)+","+F(wall.Elapsed.TotalMilliseconds)+","+(GC.CollectionCount(0)-gc)+","+GC.GetTotalMemory(false)+","+session.RejectedResults+","+fieldBuilds+","+crowd.RepairCount+","+crowd.RepairFailed+","+F(crowd.RepairCpuMs)+","+crowd.MaxNoProgressTicks+","+fieldHits+","+F(bakeCpu)+","+geometryRebuilds+","+F(rebuildResponse)+","+F(rebuildCpu));summary.Flush();steps.Flush();requests.Flush();
            using(var endpoints=new StreamWriter(Path.Combine(output,scenario+"-"+backend+"-"+count+"-"+repeat+"-endpoints.csv"))){endpoints.WriteLine("id,x,z,goalX,goalZ,moving");foreach(var u in crowd.Units)endpoints.WriteLine(u.Id+","+F(u.Position.X)+","+F(u.Position.Z)+","+F(u.Goal.X)+","+F(u.Goal.Z)+","+u.Moving);}
            if(native!=null)Main(()=>{native.Dispose();return true;});
            return overlaps==0&&violations==0&&holdViolations==0&&(scenario=="stop"?moving==0:scenario=="unreachable"?unreachable==count:arrived==count&&moving==0&&session.RejectedResults==0);
        }
        private void OnDestroy(){stopped=true;}
    }
}
