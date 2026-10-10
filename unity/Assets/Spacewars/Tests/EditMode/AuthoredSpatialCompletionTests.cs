using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    public sealed class AuthoredSpatialCompletionTests
    {
        private static readonly Type D=typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.PlayableDomain",true);
        private const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
        private static object Call(object d,string name,params object[] args)=>D.GetMethod(name,F).Invoke(d,args);
        private static NavigationSession Nav(object d)=>(NavigationSession)D.GetProperty("Navigation",F).GetValue(d);
        private static PlayableSpatialCompletion Receipt(object d,int id)=>(PlayableSpatialCompletion)Call(d,"SpatialCompletion",id,PlayableOwner.Player);
        private static void Deliver(NavigationSession nav){while(nav.Requests.TryDequeue(out var r))nav.Answers.TryEnqueue(new NavigationAnswer(r,new SharedFlowRouter(r.Geometry,r.Profile).FindPath(r.Start,r.Goal)));nav.ApplyResults();}
        private static int SpatialTag(byte[] domain){var tag=BitConverter.GetBytes(0x53505231);for(int i=0;i<domain.Length-4;i++)if(domain.Skip(i).Take(4).SequenceEqual(tag))return i;throw new Exception("No spatial tag.");}
        [TestCase(false,0)][TestCase(false,6)][TestCase(false,500)][TestCase(true,0)][TestCase(true,6)][TestCase(true,500)]
        public void AuthoredPendingInstalledCompletedWorldsPreserveOriginalAssignedCurrentAndHistory(bool foundry,int cut)
        {
            var p=foundry?PlayableProfile.Create(PlayableProfile.Default.CopyData(),new FoundryMap(new FoundryProfileData())):PlayableProfile.ThreeCrossingsDefault;
            var d=Activator.CreateInstance(D,F,null,new object[]{p,1L,false},null);var nav=Nav(d);
            var start=foundry?new NavPoint(0,35):new NavPoint(-20,0);var goal=foundry?new NavPoint(77,35):new NavPoint(20,0);
            int id=(int)Call(d,"SpawnUnit",start,PlayableOwner.Player,PlayableEntityKind.Tank);
            Assert.AreEqual(PlayableCommandStatus.Applied,Call(d,"Apply",new PlayableCommand(1,1,"player-1",PlayableCommandKind.Move,new[]{id},goal,origin:PlayableOrderOrigin.Ai,source:"authored-receipt",jobId:13,actionId:17),null));
            var original=nav.GroupFor(id).OriginalLocation.Value;Assert.AreEqual(goal,original.Position);Assert.AreNotEqual("flat",original.SurfaceId);
            if(cut>0)Deliver(nav);for(int i=0;i<cut;i++){Deliver(nav);Call(d,"Step",1d/30);}
            var bytes=(byte[])Call(d,"CaptureWorldBytes",7,"authored-surface-test");var before=PlayableWorldState.Decode(bytes);
            var restored=D.GetMethod("RestoreWorldBytes",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{bytes,p,7,"authored-surface-test"});var restoredNav=Nav(restored);
            var after=PlayableWorldState.Decode((byte[])Call(restored,"CaptureWorldBytes",7,"authored-surface-test"));
            CollectionAssert.AreEqual(before.Binding,after.Binding);CollectionAssert.AreEqual(before.Domain.Take(SpatialTag(before.Domain)),after.Domain.Take(SpatialTag(after.Domain)),"Existing positional domain, stats and MatchHistory bytes");
            Assert.AreEqual(nav.Crowd.Units.Single(u=>u.Id==id).Location,restoredNav.Crowd.Units.Single(u=>u.Id==id).Location);
            Assert.AreEqual(original,restoredNav.GroupFor(id).OriginalLocation.Value);
            for(int i=0;i<1500&&Receipt(restored,id)==null;i++){Deliver(restoredNav);Call(restored,"Step",1d/30);}
            var receipt=Receipt(restored,id);Assert.NotNull(receipt);Assert.AreEqual(original,receipt.OriginalLocation);Assert.AreEqual(17,receipt.Order.ActionId);
            var actor=restoredNav.Crowd.Units.Single(u=>u.Id==id);Assert.AreEqual(receipt.AssignedLocation,actor.GoalLocation.Value);Assert.True(p.AuthoredMap.IsValidLocation(actor.Location,actor.Radius));
            var own=(PlayableSnapshot)Call(restored,"PlayerSnapshot",1L,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7,PlayableOwner.Player);
            var observation=PlayableAiObservation.From(own);Assert.AreEqual(receipt,observation.Entities.Single(u=>u.Id==id).Completion);Assert.AreEqual(actor.Location,observation.Entities.Single(u=>u.Id==id).Location.Value);
        }
        private static byte[] DomainAfterTerms(byte[] bytes,bool legacy)
        {
            var legacyCodec=typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.LegacyQueueCodec",true);
            int fields=legacy?((Array)legacyCodec.GetProperty("Fields",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null)).Length:PlayableProfileMetadata.Fields.Count;
            using(var stream=new MemoryStream(bytes))using(var reader=new BinaryReader(stream)){
                int count=reader.ReadInt32();for(int i=0;i<count;i++){reader.ReadInt32();int n=reader.ReadInt32();if(n>=0)reader.ReadBytes(n);reader.ReadBytes(fields*8);}return bytes.Skip((int)stream.Position).ToArray();
            }
        }
        // Frozen native map2 input of capture_commit4405e0f7 (runtimeb02d6a46).
        // Only tests hydrate a detached actual FoundryMap; production map3 is untouched.
        private static PlayableProfile CapturedFoundryV2Profile()
        {
            var map=new FoundryMap(new FoundryProfileData{revision=2,scale=1,flankHeight=6,rearHeight=3,directFireHeight=1});
            double UpperHeight=map.UpperHeight;
            NavPoint Point(double x,double z)=>map.Point(x,z);
            NavObstacle Rock(double x,double z,double xx,double zz,double top)=>new NavObstacle(new NavPolygon(new[]{Point(x,z),Point(xx,z),Point(xx,zz),Point(x,zz)}),0,top);
            var solid=new List<NavObstacle>(); var lava=new List<NavObstacle>();
            foreach(int side in new[]{-1,1})
            {
                solid.Add(Rock(side<0?-60:28,-26,side<0?-28:60,26,UpperHeight+7));
                lava.Add(new NavObstacle(new NavPolygon(new[]{Point(side*42,-19),Point(side*47,-8),Point(side*44,3),Point(side*48,18),Point(side*41,11),Point(side*40,-4)})));
            }
            // Boundary and upper cliff edges are visible basalt volumes; no hidden collision fences.
            solid.Add(Rock(-140,-140,-90,140,UpperHeight+10));solid.Add(Rock(90,-140,140,140,UpperHeight+10));
            solid.Add(Rock(-90,130,90,140,UpperHeight+10));solid.Add(Rock(-90,-140,90,-130,UpperHeight+10));
            // Two complementary pockets, mirrored front/rear across Z. Full envelopes stay clear.
            foreach(int end in new[]{-1,1})
            {
                solid.Add(Rock(-26,end<0?-84:58,-19,end<0?-58:84,UpperHeight+9));
                solid.Add(Rock(-51,end<0?-89:84,-28,end<0?-84:89,UpperHeight+8));
                solid.Add(Rock(19,end<0?-84:58,26,end<0?-58:84,UpperHeight+9));
                solid.Add(Rock(28,end<0?-56:52,51,end<0?-52:56,UpperHeight+8));
            }
            // Continuous side river lies wholly within the visible blocked eastern bank.
            lava.Add(new NavObstacle(new NavPolygon(new[]{Point(113,-140),Point(111,-108),Point(117,-71),Point(110,-32),Point(115,3),Point(109,42),Point(116,81),Point(111,113),Point(114,140),Point(133,140),Point(130,110),Point(135,80),Point(128,41),Point(134,2),Point(129,-32),Point(136,-72),Point(130,-110),Point(132,-140)})));
            // Flanks emerge in front of the homes; corner basalt closes the direct rear shortcut.
            foreach(int side in new[]{-1,1})foreach(int end in new[]{-1,1})
                solid.Add(Rock(side<0?-90:60,end<0?-130:98,side<0?-60:90,end<0?-98:130,UpperHeight+6));
            // Major factory/store volumes in opposite corners, both working and abandoned on both sides.
            solid.Add(Rock(-88,104,-70,126,UpperHeight+13));solid.Add(Rock(70,105,88,126,UpperHeight+9));
            solid.Add(Rock(-88,-126,-70,-105,UpperHeight+9));solid.Add(Rock(70,-126,88,-104,UpperHeight+13));
            var flags=BindingFlags.Instance|BindingFlags.NonPublic;
            var type=typeof(FoundryMap);
            type.GetField("<Solids>k__BackingField",flags).SetValue(map,solid.AsReadOnly());
            type.GetField("<Lava>k__BackingField",flags).SetValue(map,lava.AsReadOnly());
            var geometry=new NavGeometry(map.HalfExtent,map.Solids.ToArray(),map.Revision);
            type.GetField("geometry",flags).SetValue(map,geometry);
            var locations=type.GetField("locations",flags);
            locations.SetValue(map,Activator.CreateInstance(locations.GetValue(map).GetType(),BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,new object[]{geometry,map.Supports,map.SurfaceTransitions},null));
            return PlayableProfile.Create(PlayableProfile.Default.CopyData(),map);
        }
        private static OfflineMatchConfiguration CapturedFoundryV2Configuration(PlayableProfile p,int seed)
        {
            var map=(FoundryMap)p.AuthoredMap;var sites=map.Sites(p);var starts=new OfflineStart[6];var roster=new OfflineParticipant[6];
            // Exact4405e0f7 defaults, roster/start construction and unmirrored all-pair loop.
            for(int i=0;i<6;i++){var home=sites[i].Position;starts[i]=new OfflineStart((i<3?"A":"B")+(i%3+1),i+1,home,new NavPoint(home.X,home.Z+(i<3?-12:12)*map.Scale),i<3?-Math.PI/2:Math.PI/2,i+1);roster[i]=new OfflineParticipant(i==0?"foundry-1":"foundry-"+(i+1),i+1,i<3?1:2,OfflineControl.Human);}
            for(int i=1;i<6;i++)roster[i]=new OfflineParticipant(roster[i].Id,i+1,i<3?1:2,OfflineControl.Ai);
            var geometry=(NavGeometry)typeof(FoundryMap).GetField("geometry",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(map);var costs=new double[6,6];var router=new SharedFlowRouter(geometry,p.Navigation);
            for(int a=0;a<6;a++)for(int b=a+1;b<6;b++){var path=router.FindPath(starts[a].ExplorerAnchor,starts[b].ExplorerAnchor);if(path.Length==0)throw new ArgumentException("Disconnected captured foundry starts.");double distance=0;var last=starts[a].ExplorerAnchor;foreach(var q in path){distance+=Math.Sqrt((q.X-last.X)*(q.X-last.X)+(q.Z-last.Z)*(q.Z-last.Z));last=q;}costs[a,b]=costs[b,a]=distance;}
            return new OfflineMatchConfiguration(p,"native-foundry-v2",map.Id,"native-typed-flow-v1",seed,roster,starts,sites,map.Solids.ToArray(),costs,terrain:map.Id);
        }
        [TestCase("pending")][TestCase("installed")][TestCase("arrived")]
        public void IndependentlyCapturedFoundryV2WorldsRejectCurrentV3Binding(string stage)
        {
            var root=new DirectoryInfo(Directory.GetCurrentDirectory());while(root!=null&&!File.Exists(Path.Combine(root.FullName,"unity/Tests/Fixtures/a1q-surface-legacy/manifest.json")))root=root.Parent;Assert.NotNull(root);
            var bytes=File.ReadAllBytes(Path.Combine(root.FullName,"unity/Tests/Fixtures/a1q-surface-legacy","foundry-"+stage+".world"));
            var p=PlayableProfile.Create(PlayableProfile.Default.CopyData(),new FoundryMap(new FoundryProfileData()));Assert.AreEqual(3,((FoundryMap)p.AuthoredMap).Revision);var config=((FoundryMap)p.AuthoredMap).Configuration(p,19092026);
            var error=Assert.Throws<TargetInvocationException>(()=>D.GetMethod("RestoreConfiguredWorldBytes",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{bytes,p,19092026,config.SourceIdentity,config,false}));
            Assert.That(error.InnerException,Is.TypeOf<ArgumentException>());StringAssert.Contains("binding mismatch",error.InnerException.Message);
        }
        [TestCase("flat","pending")][TestCase("flat","installed")][TestCase("flat","arrived")]
        [TestCase("crossings","pending")][TestCase("crossings","installed")][TestCase("crossings","arrived")]
        [TestCase("foundry","pending")][TestCase("foundry","installed")][TestCase("foundry","arrived")]
        public void IndependentlyCapturedV9V1WorldsLoadWithoutInventedAuthoredReceipt(string map,string stage)
        {
            var root=new DirectoryInfo(Directory.GetCurrentDirectory());while(root!=null&&!File.Exists(Path.Combine(root.FullName,"unity/Tests/Fixtures/a1q-surface-legacy/manifest.json")))root=root.Parent;Assert.NotNull(root);
            var bytes=File.ReadAllBytes(Path.Combine(root.FullName,"unity/Tests/Fixtures/a1q-surface-legacy",map+"-"+stage+".world"));var before=PlayableWorldState.Decode(bytes);
            var p=map=="flat"?PlayableProfile.Default:map=="crossings"?PlayableProfile.ThreeCrossingsDefault:CapturedFoundryV2Profile();
            var config=map=="foundry"?CapturedFoundryV2Configuration(p,19092026):null;
            object restored=config==null?D.GetMethod("RestoreWorldBytes",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{bytes,p,19092026,"a1q-surface-legacy"}):D.GetMethod("RestoreConfiguredWorldBytes",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{bytes,p,19092026,config.SourceIdentity,config,false});
            var nav=Nav(restored);var actor=nav.Crowd.Units.Single(u=>nav.GroupFor(u.Id)?.CommandSequence==1&&nav.GroupFor(u.Id)?.OwnerId==(config?.Roster[0].Id??"player-1"));
            var after=PlayableWorldState.Decode((byte[])Call(restored,"CaptureWorldBytes",19092026,config?.SourceIdentity??"a1q-surface-legacy"));var wire=typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.WorldWire",true);var binding=(byte[])wire.GetMethod("Binding",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{p,true});CollectionAssert.AreEqual(before.Binding,binding,"Exact schema9 profile/map binding");Assert.AreEqual(10,BitConverter.ToInt32(after.Binding,0));CollectionAssert.AreEqual(DomainAfterTerms(before.Domain,true).Take(SpatialTag(DomainAfterTerms(before.Domain,true))),DomainAfterTerms(after.Domain,false).Take(SpatialTag(DomainAfterTerms(after.Domain,false))),"Positional authority/stats/history unchanged after explicit term codec migration");
            for(int i=0;i<400;i++){Deliver(nav);Call(restored,"Step",1d/30);}Assert.AreEqual(NavigationOutcome.Arrived,actor.Outcome);
            if(map=="flat")Assert.NotNull(Receipt(restored,actor.Id));else Assert.Null(Receipt(restored,actor.Id),"Legacy authored route never invented eligibility");
        }
        [Test] public void InstalledOldRouteAndPendingDifferentSurfaceReplacementRestoreIndependently()
        {
            var p=PlayableProfile.ThreeCrossingsDefault;var d=Activator.CreateInstance(D,F,null,new object[]{p,1L,false},null);var nav=Nav(d);int id=(int)Call(d,"SpawnUnit",new NavPoint(-20,0),PlayableOwner.Player,PlayableEntityKind.Tank);
            Assert.AreEqual(PlayableCommandStatus.Applied,Call(d,"Apply",new PlayableCommand(1,1,"player-1",PlayableCommandKind.Move,new[]{id},new NavPoint(-18,0)).AsHuman(),null));Deliver(nav);Call(d,"Step",1d/30);
            var oldGoal=nav.Crowd.Units.Single(u=>u.Id==id).GoalLocation.Value;
            Assert.AreEqual(PlayableCommandStatus.Applied,Call(d,"Apply",new PlayableCommand(1,2,"player-1",PlayableCommandKind.Move,new[]{id},new NavPoint(20,0)).AsHuman(),null));
            var pending=nav.GroupFor(id).Members.Single(m=>m.Entity==id).GoalLocation.Value;Assert.AreNotEqual(oldGoal.SurfaceId,pending.SurfaceId);
            var bytes=(byte[])Call(d,"CaptureWorldBytes",7,"authored-replacement-test");var restored=D.GetMethod("RestoreWorldBytes",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{bytes,p,7,"authored-replacement-test"});nav=Nav(restored);
            Assert.True(nav.IsPending(id));Assert.True(nav.Crowd.Units.Single(u=>u.Id==id).Moving);Assert.AreEqual(oldGoal,nav.Crowd.Units.Single(u=>u.Id==id).GoalLocation.Value);Assert.AreEqual(pending,nav.GroupFor(id).Members.Single(m=>m.Entity==id).GoalLocation.Value);
            for(int i=0;i<100;i++)Call(restored,"Step",1d/30);Assert.True(nav.IsPending(id));Assert.Null(Receipt(restored,id));
            for(int i=0;i<1500&&Receipt(restored,id)==null;i++){Deliver(nav);Call(restored,"Step",1d/30);}Assert.NotNull(Receipt(restored,id));Assert.AreEqual(2,Receipt(restored,id).Order.Sequence);Assert.AreEqual(pending,Receipt(restored,id).AssignedLocation);
        }
        private static byte[] NavigationBytes(NavigationSessionState state)
        {
            var wire=typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.WorldWire",true);
            var write=wire.GetMethod("Write",BindingFlags.Static|BindingFlags.NonPublic,null,new[]{typeof(BinaryWriter),typeof(NavigationSessionState)},null);
            using(var stream=new MemoryStream()){using(var writer=new BinaryWriter(stream,System.Text.Encoding.UTF8,true))write.Invoke(null,new object[]{writer,state});return stream.ToArray();}
        }
        [TestCase(0)][TestCase(1)][TestCase(2)][TestCase(3)][TestCase(4)][TestCase(5)][TestCase(6)][TestCase(7)]
        public void PresentV2WrongSemanticsProviderOriginalPendingAndInstalledSurfacesReject(int corruption)
        {
            var p=PlayableProfile.ThreeCrossingsDefault;var d=Activator.CreateInstance(D,F,null,new object[]{p,1L,false},null);var nav=Nav(d);int id=(int)Call(d,"SpawnUnit",new NavPoint(-20,0),PlayableOwner.Player,PlayableEntityKind.Tank);var goal=new NavPoint(-p.AuthoredMap.Supports.Single(s=>s.SurfaceId=="bridge/central").Bounds.MaxX,0);
            Assert.AreEqual(PlayableCommandStatus.Applied,Call(d,"Apply",new PlayableCommand(1,1,"player-1",corruption==5?PlayableCommandKind.AttackMove:PlayableCommandKind.Move,new[]{id},goal).AsHuman(),null));
            if(corruption==4)Deliver(nav);
            var world=PlayableWorldState.Decode((byte[])Call(d,"CaptureWorldBytes",7,"authored-negative-test"));var state=nav.CaptureState();var other=new NavLocation(goal,"bridge/central");Assert.True(p.AuthoredMap.IsValidLocation(other,0));
            if(corruption==0)state.SurfaceSemanticsVersion=0;
            if(corruption==1)state.SurfaceProviderId="other-provider";
            if(corruption==2)state.Groups.Single(g=>g.Members.Any(m=>m.Entity==id)).OriginalLocation=other;
            if(corruption==3||corruption==5)state.Requests.Single(r=>r.Entity==id).GoalLocation=other;
            if(corruption==4)state.Crowd.Units.Single(u=>u.Id==id).GoalLocation=other;
            if(corruption==6)state.Groups.Single(g=>g.Members.Any(m=>m.Entity==id)).Members.Single(m=>m.Entity==id).GoalLocation=other;
            if(corruption==7)state.Reservations.Single(r=>r.Entity==id).Point=new NavPoint(-18,0);
            world.Navigation=NavigationBytes(state);
            Assert.Throws<TargetInvocationException>(()=>D.GetMethod("RestoreWorldBytes",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{world.Encode(),p,7,"authored-negative-test"}));
        }
        [TestCase(PlayableCommandKind.Move)][TestCase(PlayableCommandKind.AttackMove)]
        public void TacticalSnapshotAnchorSurvivesRestoreWithoutChangingPositionalCodec(PlayableCommandKind kind)
        {
            var p=PlayableProfile.ThreeCrossingsDefault;var d=Activator.CreateInstance(D,F,null,new object[]{p,1L,false},null);var nav=Nav(d);int id=(int)Call(d,"SpawnUnit",new NavPoint(-20,0),PlayableOwner.Player,PlayableEntityKind.Tank);
            Assert.AreEqual(PlayableCommandStatus.Applied,Call(d,"Apply",new PlayableCommand(1,1,"player-1",kind,new[]{id},new NavPoint(20,0)).AsHuman(),null));
            var original=nav.GroupFor(id).OriginalLocation.Value;var bytes=(byte[])Call(d,"CaptureWorldBytes",7,"authored-anchor-test");
            var restored=D.GetMethod("RestoreWorldBytes",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{bytes,p,7,"authored-anchor-test"});
            var view=(PlayableSnapshot)Call(restored,"PlayerSnapshot",1L,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7,PlayableOwner.Player);
            Assert.Null(view.Entities.Single(u=>u.Id==id).CurrentOrder);Assert.AreEqual(original,view.Entities.Single(u=>u.Id==id).Queue.Pending.Anchor.Value);Deliver(Nav(restored));view=(PlayableSnapshot)Call(restored,"PlayerSnapshot",2L,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7,PlayableOwner.Player);Assert.AreEqual(original,view.Entities.Single(u=>u.Id==id).CurrentOrder.DestinationLocation.Value);Assert.AreEqual(original,PlayableAiObservation.From(view).Entities.Single(u=>u.Id==id).CurrentOrder.DestinationLocation.Value);
        }
    }
}
