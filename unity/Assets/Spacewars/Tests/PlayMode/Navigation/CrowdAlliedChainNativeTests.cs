using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.TestTools;

namespace Spacewars.Tests.PlayMode.Navigation
{
    public sealed class CrowdAlliedChainNativeTests
    {
        private const long Generation=73;
        private const int Seed=20102026;
        private static NavPoint[] Origins(PlayableProfile profile){double step=2*profile.TankCollisionRadius+.08;return new[]{new NavPoint(-5,0),new NavPoint(-5+step,0),new NavPoint(-5+2*step,0)};}
        private static readonly NavPoint Goal=new NavPoint(10,0);
        private const BindingFlags Fields=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        private static object Read(object value,string name)
        {
            if(value==null)throw new ArgumentNullException(nameof(value),"Cannot read "+name+" from a null observer source.");
            var type=value.GetType();var property=type.GetProperty(name,Fields);
            if(property!=null)return property.GetValue(value); // null is a valid property value
            var field=type.GetField(name,Fields);if(field!=null)return field.GetValue(value);
            throw new MissingMemberException(type.FullName,name);
        }
        private sealed class Row
        {
            internal readonly int Id;internal readonly NavPoint Position,Goal;internal readonly double Heading;internal readonly bool Moving,Pending;internal readonly long Group,Revision,Order;internal readonly NavPoint[] Route;internal readonly PlayableTacticalOrderSnapshot Tactical;internal readonly long? HumanOrder;
            internal Row(NavUnit actor,object unit,NavigationSession nav){Id=actor.Id;Position=actor.Position;Goal=actor.Goal;Heading=actor.Heading;Moving=actor.Moving;Pending=nav.IsPending(actor.Id);Group=actor.InstalledGroupId;Revision=actor.InstalledRevision;Order=actor.InstalledOrder;Route=actor.Route.ToArray();Tactical=((PlayableTacticalOrderSnapshot)Read(unit,"CurrentOrder"))?.Copy();HumanOrder=((PlayableOrderStamp)Read(unit,"LastOrder"))?.Sequence;}
        }
        private sealed class Frame
        {
            internal readonly long Tick;internal readonly Row[] Rows;internal readonly string Motion;
            internal Frame(object domain,int[] ids,IDictionary units){var nav=(NavigationSession)Read(domain,"Navigation");Tick=(long)Read(domain,"Tick");Rows=ids.Select(id=>new Row(nav.Crowd.Units.Single(a=>a.Id==id),units[id],nav)).ToArray();Motion=(string)Read(nav,"LastOwnedMovementComputation");}
        }
        private static OfflineMatchConfiguration Configuration(PlayableProfile profile)
        {
            var roster=new[]{new OfflineParticipant("owner-11",1,1,OfflineControl.Human),new OfflineParticipant("owner-28",2,2,OfflineControl.Human)};
            var starts=new[]{new OfflineStart("start-1",1,profile.Headquarters(PlayableOwner.Player),new NavPoint(profile.Headquarters(PlayableOwner.Player).X+6,profile.Headquarters(PlayableOwner.Player).Z),pin:1),new OfflineStart("start-2",2,profile.Headquarters(PlayableOwner.Enemy),new NavPoint(profile.Headquarters(PlayableOwner.Enemy).X-6,profile.Headquarters(PlayableOwner.Enemy).Z),Math.PI,2)};
            var sites=new[]{new TerritorySite(1,PlayableBuildingKind.Headquarters,starts[0].Position,Array.Empty<TerritorySlot>()),new TerritorySite(2,PlayableBuildingKind.Headquarters,starts[1].Position,Array.Empty<TerritorySlot>())};
            var actors=Origins(profile).Select(p=>new OfflineScenarioUnit(1,PlayableEntityKind.Tank,p)).ToArray();
            return new OfflineMatchConfiguration(profile,"native-flat-allied-chain-v1","native-flat-eight-starts-v1","native-flat-two-human-native-starts-v1",Seed,roster,starts,sites,Array.Empty<NavObstacle>(),new double[,]{{0,100},{100,0}},scenario:actors);
        }

        [UnityTest]
        public IEnumerator OrdinaryMoveIntoTwoAlliedTankReceiversPreservesOrderAndFootprints()
        {
            var profile=PlayableProfile.Default;var origins=Origins(profile);var config=Configuration(profile);var runtime=new PlayableRuntime(config,Generation,startPaused:true);var host=new UnityHostRouteService();
            object domain=null;NavigationSession nav=null;var observerErrors=new System.Collections.Concurrent.ConcurrentQueue<string>();var frames=new System.Collections.Concurrent.ConcurrentQueue<Frame>();var observed=new System.Collections.Generic.List<Frame>();Frame current=null;int[] ids=Array.Empty<int>();long firstRootMotion=-1;bool rootProgress=false,receiverMoved=false,freeMoved=false;Row retained=null;string folder=System.IO.Path.Combine(Environment.CurrentDirectory,".local/qa/b12-successor");System.IO.Directory.CreateDirectory(folder);
            void Drain(){while(frames.TryDequeue(out var frame)){observed.Add(frame);current=frame;}}
            void Healthy(string phase){Assert.IsEmpty(observerErrors.ToArray(),phase+": authority observer errors");Assert.IsNotNull(runtime.Latest,phase);Assert.IsNull(runtime.Latest.Failure,phase+": "+runtime.Latest.Failure);Assert.False(runtime.IsStopped,phase);}
            IEnumerator Until(Func<bool> ready,double seconds,string phase){double end=Time.realtimeSinceStartupAsDouble+seconds;while(!ready()&&Time.realtimeSinceStartupAsDouble<end){host.Service(runtime,8);Drain();if(runtime.Latest!=null)Healthy(phase);yield return new WaitForSecondsRealtime(.001f);}Drain();Healthy(phase);Assert.True(ready(),phase+" tick="+runtime.Latest.Tick+" motion="+current?.Motion);}
            try{
                yield return Until(()=>runtime.IsPrepared&&runtime.Latest!=null&&runtime.Latest.Sequence>0,180,"ordinary host startup");
                ids=runtime.Latest.Entities.Where(e=>e.Owner==PlayableOwner.Player&&e.Kind==PlayableEntityKind.Tank).OrderBy(e=>e.Id).Select(e=>e.Id).ToArray();Assert.AreEqual(3,ids.Length);
                CollectionAssert.AreEqual(origins,ids.Select(id=>runtime.Latest.Entities.Single(e=>e.Id==id).Position).ToArray());Assert.True(origins.All(p=>runtime.NavigationGeometry.IsFree(p,profile.TankCollisionRadius)));Assert.True(runtime.NavigationGeometry.IsFree(Goal,profile.TankCollisionRadius));
                double gap=origins[2].X-origins[1].X;Assert.LessOrEqual(gap,profile.FollowDistance+profile.FollowArrivalTolerance,"frozen passive Follow has no reason to request a route");Assert.LessOrEqual(.08,profile.Navigation.ArrivalTolerance,"frozen chain contact gap");Assert.AreEqual(11,PlayableWorldState.CurrentVersion);
                domain=typeof(PlayableRuntime).GetField("domain",Fields).GetValue(runtime);var units=(IDictionary)Read(domain,"units");nav=(NavigationSession)Read(domain,"Navigation");nav.GetType().GetProperty("MovementDiagnosticObserver",Fields).SetValue(nav,new Action<NavigationSession>(_=>{}));
                // The hook runs once after movement/velocity/Follow on the domain
                // authority thread. The consumer receives detached immutable rows.
                domain.GetType().GetField("CombatStepForQa",Fields).SetValue(domain,new Action(()=>{try{frames.Enqueue(new Frame(domain,ids,units));}catch(Exception e){observerErrors.Enqueue(e.ToString());}}));
                TestContext.WriteLine("B12_CHAIN_CONTRACT source="+config.SourceIdentity+" map="+config.MapIdentity+" seed="+Seed+" gen="+Generation+" profile="+profile.ProfileId+"/"+profile.Revision+" origins="+string.Join(";",origins.Select(p=>p.X+":"+p.Z))+" goal="+Goal.X+":"+Goal.Z+" wire="+PlayableWorldState.CurrentVersion);
                runtime.RequestPause(false);yield return Until(()=>!runtime.Latest.Paused,5,"resume");
                Assert.True(runtime.TrySubmit(new PlayableCommand(Generation,1,config.Roster[0].Id,PlayableCommandKind.Follow,new[]{ids[1]},targetId:ids[2])).Accepted);
                bool Passive()=>current!=null&&current.Rows[1].Tactical?.Kind==PlayableTacticalOrderKind.Follow&&current.Rows[1].Tactical.CommandSequence==1&&current.Rows[1].Tactical.TargetId==ids[2]&&!current.Rows[1].Moving&&!current.Rows[1].Pending&&current.Rows[1].Route.Length==0&&current.Rows[2].Tactical==null&&!current.Rows[2].Moving&&!current.Rows[2].Pending&&current.Rows[2].Route.Length==0;
                yield return Until(()=>Passive()&&runtime.ReceiptsAfter(0).Any(r=>r.Sequence==1&&r.Status==PlayableCommandStatus.Applied),60,"passive real Follow and idle receivers before initiator");
                runtime.RequestPause(true);yield return Until(()=>runtime.Latest.Paused,5,"freeze passive identity");Drain();Assert.True(Passive());retained=current.Rows[1];CollectionAssert.AreEqual(origins,current.Rows.Select(r=>r.Position).ToArray(),"receivers have not initiated their own movement");
                // Capture while authority is paused, without borrowing live state
                // across a tick. This is current format, not an imported fixture.
                using(var before=(PlayableWorldState)domain.GetType().GetMethod("CaptureWorldState",Fields).Invoke(domain,new object[]{Seed,config.SourceIdentity})){Assert.AreEqual(11,before.Version);System.IO.File.WriteAllBytes(System.IO.Path.Combine(folder,"before-move.bin"),before.Encode());}
                runtime.RequestPause(false);yield return Until(()=>!runtime.Latest.Paused,5,"resume initiator");Assert.True(runtime.TrySubmit(new PlayableCommand(Generation,2,config.Roster[0].Id,PlayableCommandKind.Move,new[]{ids[0]},Goal)).Accepted);
                yield return Until(()=>runtime.ReceiptsAfter(0).Any(r=>r.Sequence==2&&r.Status==PlayableCommandStatus.Applied),60,"root-only Move applied");
                double playUntil=Time.realtimeSinceStartupAsDouble+20;int checkedFrames=0;Frame previous=null;
                void Check(){Drain();while(checkedFrames<observed.Count){var frame=observed[checkedFrames++];if(previous!=null){Assert.AreEqual(previous.Tick+1,frame.Tick,"coherent authority tick stream, no missing motion frames");for(int i=0;i<3;i++){var a=frame.Rows[i];var b=previous.Rows[i];double dx=a.Position.X-b.Position.X,dz=a.Position.Z-b.Position.Z;Assert.LessOrEqual(dx*dx+dz*dz,profile.TankSpeed*profile.TankSpeed/900d+1e-8,"per-tick speed");double turn=Math.Abs(Math.Atan2(Math.Sin(a.Heading-b.Heading),Math.Cos(a.Heading-b.Heading)));Assert.LessOrEqual(turn,profile.TankTurnSpeed/30d+1e-8,"per-tick turn");Assert.True(runtime.NavigationGeometry.IsFree(a.Position,profile.TankCollisionRadius),"full footprint");Assert.True(runtime.NavigationGeometry.SegmentFree(b.Position,a.Position,profile.TankCollisionRadius),"full static sweep");for(int j=0;j<i;j++){var c=frame.Rows[j];var d=previous.Rows[j];double sx=b.Position.X-d.Position.X,sz=b.Position.Z-d.Position.Z,vx=dx-(c.Position.X-d.Position.X),vz=dz-(c.Position.Z-d.Position.Z),q=vx*vx+vz*vz,t=q==0?0:Math.Max(0,Math.Min(1,-(sx*vx+sz*vz)/q)),x=sx+vx*t,z=sz+vz*t;Assert.GreaterOrEqual(x*x+z*z,4*profile.TankCollisionRadius*profile.TankCollisionRadius-1e-7,"all-pair relative swept separation");}}
                    }
                    var root=frame.Rows[0];var middle=frame.Rows[1];var last=frame.Rows[2];if(root.HumanOrder==2){Assert.AreEqual(PlayableTacticalOrderKind.Follow,middle.Tactical?.Kind);Assert.AreEqual(1L,middle.Tactical.CommandSequence);Assert.AreEqual(ids[2],middle.Tactical.TargetId);Assert.AreEqual(retained.Tactical.Destination,middle.Tactical.Destination,"Follow anchor unchanged");Assert.AreEqual(retained.Group,middle.Group);Assert.AreEqual(retained.Revision,middle.Revision);Assert.AreEqual(retained.Order,middle.Order);Assert.AreEqual(retained.Goal,middle.Goal);CollectionAssert.AreEqual(retained.Route,middle.Route);Assert.False(middle.Moving||middle.Pending,"yield does not grant own navigation intent");Assert.IsNull(last.Tactical);Assert.False(last.Moving||last.Pending);Assert.Zero(last.Route.Length);
                        if(root.Position.X>origins[0].X+.2){rootProgress=true;if(firstRootMotion<0)firstRootMotion=frame.Tick;}
                        if(firstRootMotion>=0){receiverMoved|=middle.Position.X>origins[1].X+1e-4;freeMoved|=last.Position.X>origins[2].X+1e-4;}
                    }previous=frame;
                }}
                while(Time.realtimeSinceStartupAsDouble<playUntil&&!(rootProgress&&receiverMoved&&freeMoved)){host.Service(runtime,8);Healthy("chain motion");Check();yield return null;}Check();
                TestContext.WriteLine("B12_CHAIN firstMotion="+firstRootMotion+" frames="+observed.Count+" rootProgress="+rootProgress+" receiverMoved="+receiverMoved+" freeMoved="+freeMoved+" workerPumps="+host.CertifiedPublication?.Pumps+" last="+current?.Motion);
                Assert.True(rootProgress,"initiator progresses through the chain");Assert.True(receiverMoved&&freeMoved,"both passive allied receivers displaced after root motion, own orders and empty routes retained");Assert.Greater(host.CertifiedPublication?.Pumps??0,0,"real threaded host serviced root route");
            }finally{
                runtime.RequestPause(true);double pauseBy=Time.realtimeSinceStartupAsDouble+5;while(!runtime.IsStopped&&runtime.Latest?.Paused!=true&&Time.realtimeSinceStartupAsDouble<pauseBy)System.Threading.Thread.Sleep(1);
                Drain();if(domain!=null){domain.GetType().GetField("CombatStepForQa",Fields).SetValue(domain,null);nav.GetType().GetProperty("MovementDiagnosticObserver",Fields).SetValue(nav,null);if(runtime.Latest?.Paused==true)try{using(var after=(PlayableWorldState)domain.GetType().GetMethod("CaptureWorldState",Fields).Invoke(domain,new object[]{Seed,config.SourceIdentity}))System.IO.File.WriteAllBytes(System.IO.Path.Combine(folder,"after-move.bin"),after.Encode());}catch(Exception e){TestContext.WriteLine("B12_CAPTURE "+e);}}
                System.IO.File.WriteAllLines(System.IO.Path.Combine(folder,"authority-frames.txt"),observed.Select(f=>f.Tick+" "+string.Join(";",f.Rows.Select(r=>r.Id+":"+r.Position.X.ToString("R")+","+r.Position.Z.ToString("R")+":moving="+r.Moving+":pending="+r.Pending+":human="+r.HumanOrder+":group="+r.Group+":order="+r.Order+":route="+r.Route.Length+":tactical="+r.Tactical?.Kind))+" motion="+f.Motion));runtime.RequestStop();host.Dispose();System.IO.File.WriteAllLines(System.IO.Path.Combine(folder,"observer-errors.txt"),observerErrors.ToArray());Assert.IsEmpty(observerErrors.ToArray(),"final authority observer errors; see observer-errors.txt");
            }
            double stopBy=Time.realtimeSinceStartupAsDouble+5;while(!runtime.IsStopped&&Time.realtimeSinceStartupAsDouble<stopBy)yield return null;
        }
    }
}
