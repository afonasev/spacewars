using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Spacewars.Runtime;
using Spacewars.Simulation;

internal static class FormationDetourOffline
{
    const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    const string GeometrySha="9ae334555f34abcf4229ad2ac7360d0449ace9750d997c8dc7e61579417876a7";
    const string A3Sha="d1d49b8127875a1d854dafced8e02de2bf3fde839ebc845ab9b471eae7d44ddf";
    const string Provider="56ab9ffe562b3c1e86bb18fd5e126ebcf65b90f9705005e23039b35df98d7b44";
    const string Map="33ce3119a22bd95dbd36a09ebd39f922e4b74dede120cdd210201494995b7e3f";
    const string Profile="1b234b7ab95010fc8be4f9747dcf93f236e4b09a6f9c79e0aedc1477103723b5";
    static string Sha(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    static byte[] Read(ZipArchive zip,string name){using var input=zip.GetEntry(name)?.Open()??throw new InvalidDataException("Missing immutable input "+name);using var output=new MemoryStream();input.CopyTo(output);return output.ToArray();}
    static JToken Json(ZipArchive zip,string name)=>JToken.Parse(System.Text.Encoding.UTF8.GetString(Read(zip,name)));
    static NavPoint Point(JToken p)=>new NavPoint((double)p["X"],(double)p["Z"]);
    static NavLocation Location(JToken p)=>new NavLocation(Point(p["Position"]),(string)(p["SurfaceId"]??p["Surface"]));
    static void Check(bool ok,string text){if(!ok)throw new InvalidOperationException(text);}
    internal static void Run()
    {
        string output=Environment.GetEnvironmentVariable("RECAST_DETOUR_OUTPUT")??throw new ArgumentException("Explicit unique RECAST_DETOUR_OUTPUT required.");
        Check(!Directory.Exists(output),"Offline output already exists; no replay.");Directory.CreateDirectory(output);
        var result=new Dictionary<string,object>{{"Result","NotRun"},{"Claim","Controlled frozen A3 poststep49@2573 geometry case; not actual midstep retry or whole formation proof"},{"Source",Environment.GetEnvironmentVariable("SPACEWARS_QA_SOURCE")}};
        NavCrowd crowd=null;NavLocalRepairWorkspace workspace=null;ulong native=RecastNative.sw_recast_allocation_count();bool called=false;
        void Save()=>File.WriteAllText(Path.Combine(output,"result.json"),JsonConvert.SerializeObject(result,Formatting.Indented));
        try{
            Check(native==0,"Native baseline not zero.");
            string root="/Users/eaafonasev/Projects/spacewars-planning/evidence/recast-group-formation";
            string a3Path=Path.Combine(root,"movement-observation-20261031/ordinary-a3/raw-evidence.zip"),priorPath=Path.Combine(root,"phase-a-preflight-20261031/attachment-cause/raw-evidence.zip");
            var a3Bytes=File.ReadAllBytes(a3Path);Check(Sha(a3Bytes)==A3Sha,"A3 archive identity mismatch.");
            using var a3=new ZipArchive(new MemoryStream(a3Bytes));using var prior=ZipFile.OpenRead(priorPath);
            var geometryBytes=Read(prior,"member24-geometry.bin");Check(geometryBytes.Length==14375&&Sha(geometryBytes)==GeometrySha,"Actual geometry bytes changed.");
            var capture=System.Text.Encoding.UTF8.GetString(Read(prior,"member24-connectivity1.log")).Split('\n').Where(l=>l.StartsWith("{\"Case\":\"exact-paid-member-input-captured\"",StringComparison.Ordinal)).Select(JObject.Parse).Single();
            double[] values=capture["ProfileValues"].ToObject<double[]>();Check(values.Length==14,"Actual profile wire count changed.");
            var profile=NavigationProfile.FromWireValues((string)capture["ProfileId"],(int)capture["ProfileRevision"],values);
            Check(profile.CaptureWireValues().SequenceEqual(values)&&NavigationAdmission.ExactProfileBinding(profile)==Profile,"Actual profile binding mismatch.");
            Check(profile.Radius==.58&&profile.LocalGridCell==.2&&profile.LocalDetourDistance==3.6,"Frozen radius/grid/bound changed.");
            var dto=WorldWire.Unpack(geometryBytes,WorldWire.ReadNavigationGeometryState);Check(dto.Revision==2&&dto.Obstacles.Length==78,"Actual geometry revision/count mismatch.");
            var geometry=new NavGeometry(dto.HalfExtent,dto.Obstacles,dto.Revision);var terrain=PlayableProfile.RecastGroupCorridorDefault.AuthoredMap;
            Check(terrain.Id=="three-crossings-greybox-v1"&&terrain.Revision==5&&!(terrain is IRecastPhysicalTerrain),"Controlled terrain semantics mismatch.");
            var provider=new PartitionNavigationProvider(terrain,geometry,(string)capture["HeldIdentity"]);Check(provider.Binding==Provider,"Full double-geometry/provider identity mismatch.");
            var authority=RecastAuthorityGeometry.Prepare(terrain,geometry,profile);Check(authority.Binding==Map,"Authority map binding mismatch.");
            var observation=Json(a3,"ordinary-a3/movement-observation.json");var frame=Json(a3,"ordinary-a3/frames.json").Single(f=>(long)f["Tick"]==2573);var route=observation["Routes"].Single(r=>(int)r["Id"]==49);var sample=observation["Samples"].Single(s=>(int)s["Id"]==49&&(long)s["Tick"]==2573);
            Check((string)route["ProviderBinding"]==Provider&&(string)route["PlanProfileBinding"]==Profile&&(int)sample["GlobalIndex"]==2&&(long)sample["InstalledRequest"]==(long)route["InstalledRequest"],"A3 route/sample identity mismatch.");
            var spine=observation["Spines"].Single(s=>(string)s["Binding"]==(string)route["Spine"]);Check((string)spine["MapBinding"]==Map&&(int)spine["GeometryRevision"]==2,"A3 spine binding mismatch.");
            var target=Point(route["Route"][2]);Check(target.Equals(new NavPoint(8,34.98857024149607)),"Actual GLOBAL target changed.");
            var bodies=frame["Bodies"].ToArray();Check(bodies.Length==51&&bodies.Select(b=>(int)b["Id"]).Distinct().Count()==51,"All51 copied actors required.");
            crowd=new NavCrowd(geometry,profile,terrain);
            foreach(var b in bodies.OrderBy(b=>(int)b["Id"])){
                var at=Point(b["Position"]);var location=Location(b["Location"]);Check(location.Position.Equals(at),"Saved position/location differs.");
                var actor=crowd.Add((int)b["Id"],at,(double)b["Radius"],(double)b["Speed"],profile.TankTurnSpeed,location);
                Check(actor.Position.Equals(at)&&actor.Location.Equals(location)&&actor.Radius==(double)b["Radius"],"Copied actor was altered.");
            }
            typeof(NavCrowd).GetMethod("BuildIndex",Hidden).Invoke(crowd,null);
            double maximum=(double)typeof(NavCrowd).GetField("maximumRadius",Hidden).GetValue(crowd);Check(maximum==bodies.Max(b=>(double)b["Radius"]),"Copied maximum radius mismatch.");
            var self=crowd.Units.Single(a=>a.Id==49);Check(self.Position.Equals(Point(sample["Position"]))&&self.Location.Equals(Location(sample["Location"]))&&self.Radius==.58,"Focused actor identity mismatch.");
            var input=new{A3Archive=a3Path,A3SHA=A3Sha,GeometryArchive=priorPath,GeometrySHA=GeometrySha,GeometryBytes=geometryBytes.Length,GeometryRevision=dto.Revision,ObstacleCount=dto.Obstacles.Length,ProviderBinding=provider.Binding,MapBinding=authority.Binding,ProfileId=profile.ProfileId,ProfileRevision=profile.Revision,ProfileBinding=NavigationAdmission.ExactProfileBinding(profile),ProfileValues=values,Tick=2573,Entity=49,Target=target,MaximumRadius=maximum,Bodies=bodies.Select(b=>new{Id=(int)b["Id"],Position=Point(b["Position"]),Location=new{Position=Point(b["Location"]["Position"]),SurfaceId=(string)b["Location"]["SurfaceId"]},Radius=(double)b["Radius"]}).ToArray()};
            var inputBytes=System.Text.Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(input));File.WriteAllBytes(Path.Combine(output,"input.json"),inputBytes);result["InputSHA256"]=Sha(inputBytes);result["Input"]=input;
            workspace=NavLocalRepairWorkspace.Create(18,null,null);workspace.Reset();Check(workspace.Radius==18&&workspace.Nodes.Length==1369&&workspace.OpenCount==0&&workspace.RouteCount==0,"Fresh fixed workspace mismatch.");
            var method=typeof(NavCrowd).GetMethod("CommitDetour",Hidden)??throw new MissingMethodException("Current CommitDetour unavailable.");
            int repairs=crowd.RepairCount,failed=crowd.RepairFailed;result["Before"]=new{RepairCount=repairs,RepairFailed=failed};result["ReadyForOneCall"]=true;Save();
            object[] args={self,target,workspace};bool succeeded;
            called=true;try{succeeded=(bool)method.Invoke(crowd,args);}finally{workspace=(NavLocalRepairWorkspace)args[2];}
            var path=self.localRoute.ToArray();result["Returned"]=succeeded;result["After"]=new{crowd.RepairCount,crowd.RepairFailed,self.localIndex,LocalPath=path};result["ExitStage"]=succeeded?"Succeeded":crowd.RepairFailed==failed+1?"NoBest":"SmoothingRefused";result["Result"]=succeeded?"ObservedPath":"ObservedNoPath";
            Save();Console.WriteLine(JsonConvert.SerializeObject(result)); // Preserve actual case before post-call assertions.
            Check(crowd.RepairCount-repairs==(succeeded?1:0)&&crowd.RepairFailed-failed==(succeeded?0:(string)result["ExitStage"]=="NoBest"?1:0),"Counter/return disagreement.");
            if(succeeded){
                var clear=typeof(NavCrowd).GetMethod("ClearFrom",Hidden);var at=self.Location;Check(path.Length>0,"Successful detour has no path.");
                foreach(var point in path){object[] check={self,at,point,default(NavLocation)};Check((bool)clear.Invoke(crowd,check),"Returned path fails SAME physical/peer predicates.");at=(NavLocation)check[3];}
                result["ReturnedPathValidation"]="Same ClearFrom geometry/peer/terrain predicates succeeded on every returned segment";
            }else result["ReturnedPathValidation"]="No path returned; not proof of continuous/global unreachability or acceptance PASS";
        }catch(Exception error){result["Result"]=called?"ControlFailure":"NotRun";result["Error"]=error.ToString();throw;}
        finally{
            workspace?.Release();crowd?.ReleaseCrowdStorage();ulong after=RecastNative.sw_recast_allocation_count();bool workspaceReleased=workspace==null||workspace.References==0,spatialReleased=crowd==null||typeof(NavCrowd).GetField("spatial",Hidden).GetValue(crowd)==null;result["Called"]=called;result["Cleanup"]=new{WorkspaceReleased=workspaceReleased,SpatialReleased=spatialReleased,NativeBefore=native,NativeAfter=after};Save();Check(workspaceReleased&&spatialReleased&&after==native&&after==0,"Offline control storage cleanup failed.");
        }
    }
}
