using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class Oct29AiMatchProgressTests
{
    [UnityTest,Timeout(300000)] public IEnumerator NullDockArmyDepartsThroughFog() => Run(2);
    [UnityTest,Timeout(300000)] public IEnumerator BlackFoundryAirArmyDeparts() => Run(6);
    [UnityTest,Timeout(300000)] public IEnumerator MossHighlandProductionProgress() => Run(4);
    static double Distance(NavPoint a,NavPoint b)=>Math.Sqrt(Math.Pow(a.X-b.X,2)+Math.Pow(a.Z-b.Z,2));
    IEnumerator Run(int number)
    {
        var source=JObject.Parse(File.ReadAllText(Path.Combine(Application.dataPath,"../Tests/Fixtures/Oct29AiMatches.json")));
        var entry=source["Scenarios"].Single(s=>(int)s["Number"]==number);
        IPlayableTerrain map=number==2?(IPlayableTerrain)new NullDockMap():number==4?new MossHighlandMap():new FoundryMap(new FoundryProfileData());
        var profile=PlayableProfile.Create(JsonUtility.FromJson<PlayableProfileData>(source["Profile"].ToString()),map);
        var ai=new AiProfile(JsonUtility.FromJson<AiProfileData>(source["Ai"].ToString()));Assert.AreEqual((string)source["AiHash"],ai.Hash);
        var lobby=new NativeLobbyConfiguration{HasExplicitSeed=true,ExplicitSeed=(int)entry["Seed"],NullDock=number==2,MossHighland=number==4,Foundry=number==6,
            Participants=entry["Participants"].Select(s=>new NativeLobbyParticipant{Name=(string)s["Id"],Team=(int)s["Team"],Color=(int)s["Color"],Difficulty=(AiDifficulty)Enum.Parse(typeof(AiDifficulty),(string)s["Difficulty"])}).ToList()};
        Assert.IsNull(lobby.Validate(profile,true,true));
        string output=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.local/ai-match-repair/witness-"+number+"-"+DateTime.UtcNow.ToString("HHmmssfff")));Directory.CreateDirectory(output);
        using var session=new TestMatchLogSession(output,new TestMatchLoggingProfile(),new{Source="Oct29 ordinary threaded witness",number});
        TestMatchJournal journal=null;PlayableRuntime.JournalFactory=c=>journal=session.CreateMatch(c);
        var runtime=PlayableRuntime.CreateLobbyMatch(profile,number,lobby,aiProfile:ai);var host=new UnityHostRouteService();
        var authority=(PlayableAuthorityTick)typeof(PlayableRuntime).GetField("authorityTick",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(runtime);
        var homes=new Dictionary<PlayableOwner,NavPoint>();double maxHeavy=0,maxAir=0;long sampled=-1;bool reached=false;var blockedHeads=new Dictionary<long,long>();double maxBlockedSeconds=0;bool sawThirdProduction=false;
        try
        {
            runtime.SetSpectatorSpeed(PlayableRuntime.LocalSpectatorId,4);double deadline=Time.realtimeSinceStartupAsDouble+280;
            while(!runtime.IsStopped&&Time.realtimeSinceStartupAsDouble<deadline)
            {
                host.Service(runtime,profile.NavigationRequestsPerFrame);var frame=runtime.SpectatorFrame(PlayableRuntime.LocalSpectatorId);var view=frame.Overview;
                Assert.IsNull(runtime.Latest.Failure);Assert.IsNull(host.LastProjectionFailure);
                if(view.Tick!=sampled)
                {
                    sampled=view.Tick;
                    foreach(var h in view.Buildings.Where(b=>b.Kind==PlayableBuildingKind.Headquarters&&!homes.ContainsKey(b.Owner)))homes.Add(h.Owner,h.Position);
                    foreach(var u in view.Entities.Where(u=>u.Owner==PlayableOwner.Enemy&&homes.ContainsKey(u.Owner)))
                    {
                        if(u.Kind==PlayableEntityKind.Tank||u.Kind==PlayableEntityKind.Shkval)maxHeavy=Math.Max(maxHeavy,Distance(u.Position,homes[u.Owner]));
                        if(u.Kind==PlayableEntityKind.Hawk)maxAir=Math.Max(maxAir,Distance(u.Position,homes[u.Owner]));
                    }
                }
                if(number==4)
                {
                    var third=frame.Players.Single(v=>v.OwnerId=="foundry-3");
                    foreach(var b in third.Buildings.Where(b=>b.Owner==third.Owner&&b.Kind==PlayableBuildingKind.Factory&&b.PrivateState!=null))
                    {
                        var head=b.PrivateState.Orders.FirstOrDefault();
                        if(head==null||!head.Active||head.Remaining>0)continue;
                        sawThirdProduction=true;
                        if(!blockedHeads.TryGetValue(head.Id,out long since))blockedHeads.Add(head.Id,since=view.Tick);
                        maxBlockedSeconds=Math.Max(maxBlockedSeconds,(view.Tick-since)/30d);
                    }
                }
                if(view.Tick>=(int)entry["EndTick"]||view.Outcome!=PlayableMatchOutcome.Playing){reached=true;break;}
                yield return null;
            }
            var checkpoints=authority.CaptureDiagnosticCheckpoints();
            File.WriteAllText(Path.Combine(output,"result.json"),new JObject{["Number"]=number,["Tick"]=runtime.Latest.Tick,["MaxFoundry2HeavyDistance"]=maxHeavy,["MaxFoundry2AirDistance"]=maxAir,["ReachedHorizonOrEnd"]=reached,["MaxThirdFactoryReadyHeadSeconds"]=maxBlockedSeconds,["SawThirdProduction"]=sawThirdProduction}.ToString());
            TestContext.WriteLine("OCT29_WITNESS "+File.ReadAllText(Path.Combine(output,"result.json"))+" journal="+journal?.DirectoryPath);
            Assert.True(reached,"Original horizon or genuine match termination must be reached within the fixed timeout");
            if(number==2)Assert.Greater(maxHeavy,26,"Heavy army must leave the original 16–26m home pocket");
            if(number==6)Assert.Greater(maxAir,7.3,"Aircraft must leave the original 0.1–7.3m home pocket");
            if(number==4){Assert.True(sawThirdProduction);Assert.Less(maxBlockedSeconds,47,"Ready paid output must improve on the original 47-second exit blockage");}
            runtime.RequestFinish();
            double finish=Time.realtimeSinceStartupAsDouble+5;while(runtime.Latest.Outcome==PlayableMatchOutcome.Playing&&Time.realtimeSinceStartupAsDouble<finish){host.Service(runtime,profile.NavigationRequestsPerFrame);yield return null;}
        }
        finally
        {
            runtime.RequestStop();Assert.True(System.Threading.SpinWait.SpinUntil(()=>runtime.IsStopped,5000));host.Dispose();runtime.Dispose();PlayableRuntime.JournalFactory=null;
        }
        double drain=Time.realtimeSinceStartupAsDouble+10;while(journal!=null&&!journal.WriterStopped&&Time.realtimeSinceStartupAsDouble<drain)yield return null;
        Assert.NotNull(journal);Assert.True(journal.WriterStopped);var summary=JObject.Parse(File.ReadAllText(Path.Combine(journal.DirectoryPath,"summary.json")));Assert.AreEqual(0,(long)summary["Dropped"]);Assert.AreEqual("complete",(string)summary["Status"]);
    }
}
