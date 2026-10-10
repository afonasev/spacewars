using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Simulation;
using Spacewars.Runtime;
namespace Spacewars.Tests.EditMode
{
    public sealed class OfflineParticipantAuthorityTests
    {
        internal static readonly Type D=typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.PlayableDomain",true);
        internal const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
        internal static object Domain(OfflineParticipantAuthority a)=>typeof(OfflineParticipantAuthority).GetField("domain",F).GetValue(a);
        internal static object Call(OfflineParticipantAuthority a,string method,params object[] args)=>D.GetMethod(method,F).Invoke(Domain(a),args);
        internal static string[][] Rows(){var dir=new DirectoryInfo(Directory.GetCurrentDirectory());while(dir!=null&&!File.Exists(Path.Combine(dir.FullName,"unity/Tests/Fixtures/native-offline-participant-authority/source-fixture.tsv")))dir=dir.Parent;if(dir==null)throw new Exception("Native genesis fixture missing.");return File.ReadAllLines(Path.Combine(dir.FullName,"unity/Tests/Fixtures/native-offline-participant-authority/source-fixture.tsv")).Skip(1).Select(s=>s.Split('|')).ToArray();}
        private static double N(string v)=>double.Parse(v,CultureInfo.InvariantCulture);
        internal static OfflineMatchConfiguration Config(int count=4,bool allies=true,int seed=19092026,int[] logical=null,bool scenario=false,bool single=false)
        {
            var p=PlayableProfile.Default;var rows=Rows();var starts=rows.Where(x=>x[0]=="S").Select(x=>new OfflineStart(x[1],int.Parse(x[2]),new NavPoint(N(x[3]),N(x[4])),new NavPoint(N(x[5]),N(x[6])),pin:int.Parse(x[7])==0?(int?)null:int.Parse(x[7]))).ToArray();
            var sites=starts.Select(s=>new TerritorySite(s.SiteId,PlayableBuildingKind.Headquarters,s.Position,rows.Where(x=>x[0]=="L"&&int.Parse(x[1])==s.SiteId).Select(x=>new TerritorySlot(int.Parse(x[2]),new NavPoint(N(x[3]),N(x[4])),N(x[5]))).ToArray())).Concat(new[]{new TerritorySite(9,PlayableBuildingKind.Outpost,new NavPoint(0,0),rows.Where(x=>x[0]=="L"&&x[1]=="9").Select(x=>new TerritorySlot(int.Parse(x[2]),new NavPoint(N(x[3]),N(x[4])),N(x[5]))).ToArray())}).ToArray();
            var costs=new double[8,8];foreach(var r in rows.Where(x=>x[0]=="R"))costs[int.Parse(r[1]),int.Parse(r[2])]=N(r[3]);
            var roster=Enumerable.Range(0,count).Select(i=>new OfflineParticipant("owner-"+(i*17+11),logical==null?i+1:logical[i],single?3:allies?(i%2==0?3:7):i+1,i<4?OfflineControl.Human:OfflineControl.Ai)).ToArray();
            return new OfflineMatchConfiguration(p,"source-flat-authority-v1","native-flat-eight-starts-v1","native:flat-genesis-fixture",seed,roster,starts,sites,Array.Empty<NavObstacle>(),costs,new[]{"spectator-99"},scenario?rows.Where(x=>x[0]=="U").Select(x=>new OfflineScenarioUnit(int.Parse(x[1]),(PlayableEntityKind)int.Parse(x[2]),new NavPoint(N(x[3]),N(x[4])),N(x[5]),x[6]=="1")).ToArray():null,scenarioBuildings:scenario?rows.Where(x=>x[0]=="B").Select(x=>new OfflineScenarioBuilding(int.Parse(x[1]),(PlayableBuildingKind)int.Parse(x[2]),int.Parse(x[3]),int.Parse(x[4]),new NavPoint(N(x[5]),N(x[6])),N(x[7]))).ToArray():null);
        }
        internal static void Deliver(OfflineParticipantAuthority a,int tick){if(tick%3!=0)return;while(a.Navigation.Requests.TryDequeue(out var request))Assert.True(a.Navigation.Answers.TryEnqueue(new NavigationAnswer(request,new SharedFlowRouter(request.Geometry,request.Profile).FindPath(request.Start,request.Goal))));}
        internal static void Step(OfflineParticipantAuthority a,int ticks){for(int t=0;t<ticks;t++){Deliver(a,t);a.Step(1d/30);}}
        internal static PlayableCommand Send(OfflineParticipantAuthority a,long seq,string id,PlayableCommandKind kind,int entity=0,NavPoint target=default(NavPoint),int site=0,int slot=0,int parent=0,PlayableBuildingKind building=PlayableBuildingKind.Factory,PlayableResearchKind research=PlayableResearchKind.TankChassis)
            =>new PlayableCommand(a.Navigation.Generation,seq,id,kind,new[]{entity},target,siteId:site,slotId:slot,parentId:parent,buildingKind:building,researchKind:research);
        [TestCase(2,false)][TestCase(3,false)][TestCase(4,false)][TestCase(5,false)][TestCase(6,false)][TestCase(7,false)][TestCase(8,false)]
        [TestCase(2,true)][TestCase(3,true)][TestCase(4,true)][TestCase(5,true)][TestCase(6,true)][TestCase(7,true)][TestCase(8,true)]
        public void SourceAssignmentsAndGenesisUseOneWorld(int count,bool allies)
        {
            var c=Config(count,allies);var rows=Rows().Where(x=>x[0]=="A"&&int.Parse(x[1])==count&&x[2]==(allies?"allies":"ffa")).ToArray();
            for(int i=0;i<count;i++)Assert.AreEqual(int.Parse(rows.Single(x=>x[3]==c.Roster[i].Id)[5]),c.Assignments[i]);
            var a=new OfflineParticipantAuthority(c,71);var ids=new HashSet<int>();
            for(int i=0;i<count;i++){var v=a.View(c.Roster[i].Id);var own=v.Entities.Where(e=>(int)e.Owner==i).ToArray();Assert.AreEqual(1,own.Length);Assert.AreEqual(PlayableEntityKind.Explorer,own[0].Kind);Assert.True(ids.Add(own[0].Id));var hq=v.Buildings.Single(b=>(int)b.Owner==i);Assert.AreEqual(c.Starts[c.Assignments[i]].Position,hq.Position);Assert.True(ids.Add(hq.Id));Assert.AreEqual(c.Profile.StartingCredits,v.Credits);}
            Assert.AreEqual(count*2,ids.Count);Assert.AreEqual(count,a.Navigation.Crowd.Units.Count);
            Assert.Throws<ArgumentException>(()=>a.View("spectator-99"));Assert.AreEqual(PlayableCommandStatus.InvalidOwner,a.Apply(Send(a,1,"spectator-99",PlayableCommandKind.Move)).Status);
        }
        [Test] public void AlliedVisionNeverGrantsCommandOrMoney()
        {
            var a=new OfflineParticipantAuthority(Config(),71);var c=a.Configuration;var ally=a.View(c.Roster[2].Id);var unit=ally.Entities.Single(e=>(int)e.Owner==2);var other=a.View(c.Roster[0].Id);Assert.True(other.Entities.Any(e=>e.Id==unit.Id),"team vision union");
            Assert.AreEqual(PlayableCommandStatus.InvalidEntity,a.Apply(Send(a,1,c.Roster[0].Id,PlayableCommandKind.Move,unit.Id,new NavPoint(-5,5))).Status);
            int hq=other.Buildings.Single(b=>b.Owner==PlayableOwner.Player).Id,site=c.Starts[c.Assignments[0]].SiteId;
            Assert.AreEqual(PlayableCommandStatus.Applied,a.Apply(Send(a,2,c.Roster[0].Id,PlayableCommandKind.BuildAt,site:site,slot:1,parent:hq)).Status);
            var money=Rows().Single(x=>x[0]=="M");Assert.AreEqual(int.Parse(money[1]),a.View(c.Roster[0].Id).Credits);Assert.AreEqual(int.Parse(money[2]),a.View(c.Roster[2].Id).Credits);
            var factory=a.View(c.Roster[0].Id).Buildings.Single(b=>b.Kind==PlayableBuildingKind.Factory);
            Assert.AreEqual(PlayableCommandStatus.InvalidEntity,a.Apply(Send(a,3,c.Roster[2].Id,PlayableCommandKind.QueueTank,factory.Id)).Status);
        }
        [Test] public void PinsAbsentLogicalPlayersAndSeedAreExplicit()
        {
            var c=Config(2,true,logical:new[]{2,8});Assert.AreEqual(7,c.Assignments[1]);Assert.AreNotEqual(c.Assignments[0],c.Assignments[1]);var again=Config(2,true,logical:new[]{2,8});CollectionAssert.AreEqual(c.Assignments,again.Assignments);
        }
        [Test] public void CaptureTieUsesRosterAndEnemyTeamContests()
        {
            var a=new OfflineParticipantAuthority(Config(),71);foreach(var n in a.Navigation.Crowd.Units)typeof(NavUnit).GetProperty("Position").SetValue(n,new NavPoint(20,25));
            foreach(var index in new[]{0,2}){var id=a.View(a.Configuration.Roster[index].Id).Entities.Single(e=>(int)e.Owner==index).Id;a.Navigation.Crowd.TryGet(id,out var n);typeof(NavUnit).GetProperty("Position").SetValue(n,new NavPoint(0,index));}
            Call(a,"AdvanceCapture",a.Configuration.Profile.OutpostCaptureSeconds);var site=a.View(a.Configuration.Roster[0].Id).Sites.Single(x=>x.Site.Id==9);Assert.AreEqual(PlayableOwner.Player,site.Claimant);Assert.AreEqual(1,site.Progress);
            var enemy=a.View(a.Configuration.Roster[1].Id).Entities.Single(e=>e.Owner==PlayableOwner.Enemy);a.Navigation.Crowd.TryGet(enemy.Id,out var en);typeof(NavUnit).GetProperty("Position").SetValue(en,new NavPoint(1,0));Call(a,"AdvanceCapture",1d);Assert.True(a.View(a.Configuration.Roster[0].Id).Sites.Single(x=>x.Site.Id==9).Contested);
        }
        [Test] public void IndividualEliminationAndTeamVictoryAreRestorable()
        {
            var c=Config();var a=new OfflineParticipantAuthority(c,71);foreach(int index in new[]{0,1,3}){int hq=a.View(c.Roster[index].Id).Buildings.Single(b=>(int)b.Owner==index).Id;Call(a,"Damage",hq,10000);a.Step(1d/30);if(index==0){Assert.Null(a.WinnerTeam);Assert.AreEqual(PlayableCommandStatus.InvalidOwner,a.Apply(Send(a,2,c.Roster[0].Id,PlayableCommandKind.Move)).Status);}}
            Assert.AreEqual(int.Parse(Rows().Single(x=>x[0]=="E")[1]),a.WinnerTeam);CollectionAssert.AreEquivalent(new[]{c.Roster[0].Id,c.Roster[1].Id,c.Roster[3].Id},a.EliminatedOwners);var restored=OfflineParticipantAuthority.Restore(a.CaptureBytes(),c);Assert.AreEqual(a.WinnerTeam,restored.WinnerTeam);CollectionAssert.AreEqual(a.EliminatedOwners,restored.EliminatedOwners);
        }
        [TestCase(2)][TestCase(8)] public void SourceScenarioOwnershipAndVariantsSurviveDetachedRestore(int count)
        {
            var c=Config(count,scenario:true);var a=new OfflineParticipantAuthority(c,71);var row=Rows().Single(x=>x[0]=="G"&&int.Parse(x[1])==count);var all=(PlayableSnapshot)Call(a,"Snapshot",1L,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,c.Seed);Assert.AreEqual(int.Parse(row[2]),all.Entities.Count);Assert.AreEqual(int.Parse(row[3]),all.Buildings.Count);
            if(count==8){var own=a.View(c.Roster[7].Id).Entities.Where(e=>e.Owner==PlayableOwner.Eighth&&e.Kind==PlayableEntityKind.Tank).ToArray();Assert.AreEqual(2,own.Length);Assert.AreEqual(1,own.Count(e=>e.Upgraded));Assert.AreEqual(new NavPoint(10,10),own.Single(e=>e.Upgraded).Position);var b=OfflineParticipantAuthority.Restore(a.CaptureBytes(),c);Assert.AreEqual(PlayableWorldRestoreTests.Facts(a.View(c.Roster[7].Id)),PlayableWorldRestoreTests.Facts(b.View(c.Roster[7].Id)));}
        }
        [TestCase(2)][TestCase(8)] public void SingleTeamUsesSourceAssignmentsWithoutAutomaticVictory(int count)
        {
            var c=Config(count,single:true);var rows=Rows().Where(x=>x[0]=="A"&&int.Parse(x[1])==count&&x[2]=="single").ToArray();
            for(int i=0;i<count;i++)Assert.AreEqual(int.Parse(rows.Single(x=>x[3]==c.Roster[i].Id)[5]),c.Assignments[i]);
            var a=new OfflineParticipantAuthority(c,71);Step(a,1);Assert.AreEqual(PlayableMatchOutcome.Playing,a.View(c.Roster[0].Id).Outcome);
            for(int i=0;i<count;i++)foreach(var b in a.View(c.Roster[i].Id).Buildings.Where(x=>(int)x.Owner==i).ToArray())Call(a,"Damage",b.Id,10000);
            Call(a,"CheckOfflineOutcome");Assert.AreEqual(PlayableMatchOutcome.Playing,a.View(c.Roster[0].Id).Outcome);Assert.Null(a.WinnerTeam);
            Assert.AreEqual(PlayableMatchOutcome.Playing,OfflineParticipantAuthority.Restore(a.CaptureBytes(),c).View(c.Roster[0].Id).Outcome);
        }
        [Test] public void OwnerReplayWatermarksSurviveRestoreAndRemainIndependent()
        {
            var c=Config();var a=new OfflineParticipantAuthority(c,71);var first=a.View(c.Roster[0].Id).Entities.Single(e=>e.Owner==PlayableOwner.Player);var second=a.View(c.Roster[1].Id).Entities.Single(e=>e.Owner==PlayableOwner.Enemy);
            var command=Send(a,1,c.Roster[0].Id,PlayableCommandKind.Hold,first.Id);Assert.AreEqual(PlayableCommandStatus.Applied,a.Apply(command).Status);Assert.AreEqual(PlayableCommandStatus.Applied,a.Apply(Send(a,1,c.Roster[1].Id,PlayableCommandKind.Hold,second.Id)).Status);
            var b=OfflineParticipantAuthority.Restore(a.CaptureBytes(),c);Assert.AreEqual(PlayableCommandStatus.InvalidSequence,b.Apply(command).Status);Assert.AreEqual(PlayableCommandStatus.Applied,b.Apply(Send(b,2,c.Roster[0].Id,PlayableCommandKind.Stop,first.Id)).Status);
        }
        [Test] public void SharedRuntimePublishesStableOwnerViewsAndNoImplicitAi()
        {
            var c=Config(8);var r=new PlayableRuntime(c,71);
            try{Assert.Null(r.AiCheckpoint);Assert.Null(r.OpeningComposition);Assert.Throws<ArgumentException>(()=>r.ParticipantView("spectator-99"));foreach(var p in c.Roster){var view=r.ParticipantView(p.Id);Assert.AreEqual(p.Id,view.OwnerId);Assert.AreEqual(p.Team,view.Team);Assert.AreEqual(8,view.Participants.Count);Assert.AreEqual(p.Id,PlayableAiObservation.From(view).OwnerId);Assert.AreEqual(p.Team,PlayableAiObservation.From(view).Team);Assert.AreEqual(17,PlayableAiObservation.From(view).SchemaVersion);}
                Assert.AreEqual(PlayableCommandStatus.InvalidOwner,r.TrySubmit(new PlayableCommand(71,1,c.Roster[7].Id,PlayableCommandKind.Move,Array.Empty<int>())).Status);
                var owner=c.Roster[0].Id;var own=r.ParticipantView(owner).Entities.Single(e=>e.Owner==PlayableOwner.Player);Assert.AreEqual(PlayableCommandStatus.Accepted,r.TrySubmit(new PlayableCommand(71,2,owner,PlayableCommandKind.Hold,new[]{own.Id})).Status);
                PlayableCommandReceipt receipt=null;for(int i=0;i<100&&receipt==null;i++){receipt=r.DrainReceipts().FirstOrDefault();if(receipt==null)System.Threading.Thread.Sleep(10);}Assert.NotNull(receipt);Assert.AreEqual(owner,receipt.OwnerId);Assert.AreEqual(PlayableCommandStatus.Applied,receipt.Status);
            }finally{r.RequestStop();for(int i=0;i<100&&!r.IsStopped;i++)System.Threading.Thread.Sleep(10);Assert.True(r.IsStopped);}
        }
        [Test] public void UnsupportedAndCorruptBindingsFailClosed()
        {
            var c=Config();var a=new OfflineParticipantAuthority(c,71);var b=a.CaptureBytes();Assert.Throws<ArgumentException>(()=>OfflineParticipantAuthority.Restore(b,Config(seed:8)));Assert.Throws<ArgumentException>(()=>OfflineParticipantAuthority.Restore(b,Config(4,false)));var envelope=PlayableWorldState.Decode(b);envelope.Participants[0]^=1;Assert.Throws<ArgumentException>(()=>OfflineParticipantAuthority.Restore(envelope.Encode(),c));b[30]^=1;Assert.Throws<ArgumentException>(()=>OfflineParticipantAuthority.Restore(b,c));
            Assert.Throws<ArgumentException>(()=>new OfflineMatchConfiguration(c.Profile,c.SourceIdentity,c.MapIdentity,c.RouteProvenance,c.Seed,c.Roster.ToArray(),c.Starts.ToArray(),c.Sites.ToArray(),c.Obstacles.ToArray(),new double[8,8],terrain:"elevated"));
        }
    }
}
