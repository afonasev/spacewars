using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class NullDockRecastIngressTests
{
    private const string Human="foundry-2";
    [UnityTest, Timeout(65000)]
    public IEnumerator DistantTankDoesNotBlockOtherGroundOrders()
    {
        const int seed=754446459;
        var map=new NullDockMap(JsonUtility.FromJson<NullDockProfileData>(Resources.Load<TextAsset>("NullDockProfile").text));
        var baseline=PlayableProfile.Create(JsonUtility.FromJson<PlayableProfileData>(Resources.Load<TextAsset>("PlayableProfile").text),map);
        var store=new NativeBalanceStore(baseline,Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".json"),false);
        store.Select(NativeBalanceStore.CorridorExperimentId,7);
        var profile=PlayableProfile.Create(store.Active.CopyData(),map);
        Assert.AreEqual(15,profile.Navigation.GroupCorridorMaximumExcessPercent);
        var roster=new[]{new OfflineParticipant("player-1",1,2,OfflineControl.Human),new OfflineParticipant(Human,2,1,OfflineControl.Human)};
        var basis=map.Configuration(profile,seed,roster);
        // Match journal, generation 2, tick 10380, before orders 62 and 67.
        var units=new[]{
            new OfflineScenarioUnit(2,PlayableEntityKind.Hawk,new NavPoint(34.317847417110706,7.078416834426378),-1.8881306797901205),
            new OfflineScenarioUnit(2,PlayableEntityKind.Hawk,new NavPoint(28.47446305123328,3.5613347918117104),-0.19272294849426064),
            new OfflineScenarioUnit(2,PlayableEntityKind.Hawk,new NavPoint(32.70509818695214,4.395010404668072),1.3884534429892021),
            new OfflineScenarioUnit(2,PlayableEntityKind.Hawk,new NavPoint(30.40752845206958,2.3975642576732272),-0.8553173320042754),
            // Authored fixtures disallow even vertically separated overlaps in XZ.
            new OfflineScenarioUnit(2,PlayableEntityKind.Hawk,new NavPoint(34.08694829083036,0.1179125663772238),1.675052203847094),
            new OfflineScenarioUnit(2,PlayableEntityKind.Hawk,new NavPoint(37.18397313872497,2.4038641765278985),2.4524151561838763),
            new OfflineScenarioUnit(2,PlayableEntityKind.Hawk,new NavPoint(35.91646323969718,5.492343813063538),-1.1217375896007453),
            new OfflineScenarioUnit(2,PlayableEntityKind.Tank,new NavPoint(32.63378585051774,2.2459361253657892),0.3693741535248495),
            new OfflineScenarioUnit(2,PlayableEntityKind.Explorer,new NavPoint(29.099984252801754,-1.324037282694063),0.28136766572575267),
            new OfflineScenarioUnit(2,PlayableEntityKind.Hawk,new NavPoint(28.705302510188147,0.9190278496470349),0.9680507876140368),
            new OfflineScenarioUnit(2,PlayableEntityKind.Tank,new NavPoint(32.6642442390628,-1.2176184396753322),-0.8593259729876254),
            new OfflineScenarioUnit(2,PlayableEntityKind.Hawk,new NavPoint(29.63713202018956,5.589223320771902),-2.0888184574310396),
            new OfflineScenarioUnit(2,PlayableEntityKind.Tank,new NavPoint(-16.027520048043332,10.369362171454743),0.402039048694452)
        };
        var loggedIds=new[]{14,16,22,24,27,31,34,36,38,39,40,42,43};
        var config=new OfflineMatchConfiguration(profile,basis.SourceIdentity,basis.MapIdentity,basis.RouteProvenance,seed,roster,
            basis.Starts.ToArray(),basis.Sites.ToArray(),basis.Obstacles.ToArray(),new double[2,2],scenario:units,terrain:map.Id);
        using(var runtime=new PlayableRuntime(config,2,true,aiProfile:store.ActiveAiProfile))
        using(var routes=new UnityHostRouteService())
        {
            void Service()=>routes.Service(runtime,profile.NavigationRequestsPerFrame);
            PlayableSnapshot View()=>runtime.ParticipantView(Human);
            IEnumerator Until(Func<bool> done, double seconds)
            {
                double until=Time.realtimeSinceStartupAsDouble+seconds;
                while(!done()&&Time.realtimeSinceStartupAsDouble<until){Service();Assert.IsNull(runtime.Latest?.Failure);yield return null;}
                Assert.True(done(),"Threaded match did not reach the required tick");
            }
            runtime.RequestPause(false);
            yield return Until(()=>View()!=null&&!View().Paused,20);
            var first=View();
            var all=units.Select(u=>first.Entities.Single(e=>e.Kind==u.Kind&&e.Position.Equals(u.Position)).Id).ToArray();
            int[] Select(params int[] original)=>original.Select(id=>all[Array.IndexOf(loggedIds,id)]).ToArray();
            var tracked=Select(36,38,40);
            int distantTank=Select(43).Single();
            var firstGoal=new NavPoint(51.34516906738281,2.772965669631958);
            var secondGoal=new NavPoint(37.07463073730469,1.9244340658187866);
            bool Changed(PlayableSnapshot view,int id,NavPoint before)=>!view.Entities.Single(e=>e.Id==id).Position.Equals(before);
            void Send(long sequence,PlayableCommandKind kind,int[] ids,NavPoint goal)
            {Assert.True(runtime.TrySubmit(new PlayableCommand(2,sequence,Human,kind,ids,goal)).Accepted);}
            void Report(long sequence)
            {
                var receipt=runtime.ReceiptsAfter(0).Single(r=>r.OwnerId==Human&&r.Sequence==sequence);
                TestContext.WriteLine($"profile={NativeBalanceStore.CorridorExperimentId}@7 seed={seed} receipt={receipt.Status} {receipt.Message} tick={receipt.AppliedTick}");
                foreach(int id in tracked.Append(distantTank))
                {
                    var entity=View().Entities.Single(e=>e.Id==id);
                    TestContext.WriteLine($"loggedId={loggedIds[Array.IndexOf(all,id)]} position=({entity.Position.X:R},{entity.Position.Z:R}) outcome={entity.NavigationOutcome} pending={entity.Queue?.Pending?.CommandSequence} active={entity.Queue?.Active?.CommandSequence}");
                }
                Assert.AreEqual(PlayableCommandStatus.Applied,receipt.Status);
            }
            Send(62,PlayableCommandKind.Move,all,firstGoal);
            yield return Until(()=>View().Tick>=first.Tick+180,10);
            Report(62);
            bool movedFirst=tracked.Any(id=>Changed(View(),id,first.Entities.Single(e=>e.Id==id).Position));
            var beforeNext=View();
            Send(67,PlayableCommandKind.AttackMove,tracked,secondGoal);
            yield return Until(()=>View().Tick>=beforeNext.Tick+300,15);
            Report(67);
            bool movedSecond=tracked.All(id=>Changed(View(),id,beforeNext.Entities.Single(e=>e.Id==id).Position));
            var final=View();
            TestContext.WriteLine($"firstMoved={movedFirst} secondMoved={movedSecond}");
            runtime.RequestStop();
            yield return Until(()=>runtime.IsStopped,5);
            Assert.True(movedFirst,"The mixed order should start ground movement.");
            Assert.True(movedSecond,"Each nearby ground unit must move after the replacement order.");
            Assert.True(tracked.All(id=>final.Entities.Single(e=>e.Id==id).Queue?.Completion?.Order.Sequence==67),
                "All three nearby ground units must finish the replacement order.");
            Assert.AreEqual(62,final.Entities.Single(e=>e.Id==distantTank).OrderStamp?.Sequence,
                "The later order must preserve the distant tank's original intent.");
        }
    }
}
