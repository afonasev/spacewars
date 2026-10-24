using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

public sealed class PlayableRepeatProductionTests
{
    private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;

    [Test] public void MixedQueueKeepsRepeatOnChosenKindWhileOtherKindBuilds()
    {
        var host=new GameObject("mixed repeat production HUD");var hud=host.AddComponent<PlayableBootstrap>();hud.enabled=false;
        void Set(string name,object value)=>typeof(PlayableBootstrap).GetField(name,Flags).SetValue(hud,value);
        object Get(string name)=>typeof(PlayableBootstrap).GetField(name,Flags).GetValue(hud);
        void Call(string name,params object[] args)=>typeof(PlayableBootstrap).GetMethod(name,Flags).Invoke(hud,args);
        try
        {
            Set("profile",PlayableProfile.Default);Call("CreateHud");var root=(VisualElement)Get("root");
            var orders=new[]{new PlayableProductionOrderSnapshot(1,PlayableEntityKind.Explorer,100,2,15,7.5,true),new PlayableProductionOrderSnapshot(2,PlayableEntityKind.Tank,150,3,15,15,false)};
            var factory=new PlayableBuildingSnapshot(5,PlayableOwner.Player,PlayableBuildingKind.Factory,default,250,1,2,.5,default,orders:orders,repeat:true);
            var view=new PlayableSnapshot(PlayableProfile.RequiredProfileId,1,1,7,1,1,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,900,null,Array.Empty<PlayableEntitySnapshot>(),new[]{factory},Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,population:new PlayablePopulationSnapshot(2,2,100));
            Set("view",view);Call("UpdateProductionHud",factory);
            Assert.That(((Button)Get("tankButton")).text,Does.Contain("Повтор ВКЛ"));
            Assert.That(((Button)Get("explorerButton")).text,Does.Not.Contain("Повтор ВКЛ"));
            Assert.That(((ProgressBar)Get("progress")).title,Does.Contain("Исследователь"));
            var badges=root.Query<Label>("repeat-badge").ToList();
            Assert.AreEqual(1,badges.Count(b=>b.style.display.value==DisplayStyle.Flex));
            var repeatSlot=(Button)badges.Single(b=>b.style.display.value==DisplayStyle.Flex).parent;
            Assert.AreEqual("tank",repeatSlot.Q<OrbitalGlyph>().Kind);
        }
        finally{UnityEngine.Object.DestroyImmediate(host);}
    }

    [UnityTest] public IEnumerator ThreadedFactoryFinishesManualOrdersBeforeResumingRepeat()
    {
        var data=PlayableProfile.Default.CopyData();data.startingCredits=10000;data.factoryBuildSeconds=1;data.tankProductionSeconds=2;data.explorerProductionDurationSec=2;
        var profile=PlayableProfile.Create(data);var runtime=new PlayableRuntime(profile,91,7,autonomousOwnerAi:false,autonomousEnemyAi:false,humanControlledPlayer:true);
        using var routes=new UnityHostRouteService();long sequence=0;int factory=0;
        PlayableBuildingPrivateState State()=>runtime.Latest.Buildings.Single(b=>b.Id==factory).PrivateState;
        void Send(PlayableCommandKind kind,int id=0)
        {
            var result=runtime.TrySubmit(new PlayableCommand(91,++sequence,"player-1",kind,id==0?Array.Empty<int>():new[]{id}));
            Assert.AreEqual(PlayableCommandStatus.Accepted,result.Status);
        }
        IEnumerator Until(Func<bool> condition,string label)
        {
            double deadline=Time.realtimeSinceStartupAsDouble+45;
            while(!condition()&&Time.realtimeSinceStartupAsDouble<deadline)
            {
                routes.Service(runtime,8);Assert.IsNull(runtime.Latest?.Failure,label);yield return null;
            }
            Assert.IsNull(runtime.Latest?.Failure,label);Assert.True(condition(),label+"; tick="+runtime.Latest?.Tick);
        }
        IEnumerator Applied(long command)
        {
            yield return Until(()=>runtime.ReceiptsAfter(command-1).Any(r=>r.Sequence==command&&r.Status!=PlayableCommandStatus.Accepted),"command "+command);
            Assert.AreEqual(PlayableCommandStatus.Applied,runtime.ReceiptsAfter(command-1).Single(r=>r.Sequence==command&&r.Status!=PlayableCommandStatus.Accepted).Status);
        }
        void Trace(string phase)=>TestContext.WriteLine(phase+" tick="+runtime.Latest.Tick+" repeat="+State().Repeat+" queue="+string.Join(",",State().Orders.Select(o=>o.Id+":"+o.Kind+":"+o.Active)));
        try
        {
            yield return Until(()=>runtime.IsPrepared&&runtime.Latest?.Tick>0,"threaded host prepared");
            Send(PlayableCommandKind.BuildFactory);yield return Applied(sequence);
            yield return Until(()=>runtime.Latest.Buildings.Any(b=>b.Owner==PlayableOwner.Player&&b.Kind==PlayableBuildingKind.Factory&&b.Phase==ConstructionPhase.Ready),"factory ready");
            factory=runtime.Latest.Buildings.Single(b=>b.Owner==PlayableOwner.Player&&b.Kind==PlayableBuildingKind.Factory).Id;
            int initialUnits=runtime.Latest.Entities.Count(e=>e.Owner==PlayableOwner.Player);
            Send(PlayableCommandKind.ToggleRepeatProduction,factory);yield return Applied(sequence);
            yield return Until(()=>State().Orders.Count==1&&State().Orders[0].Active,"first repeat active");var active=State().Orders[0];
            Send(PlayableCommandKind.QueueExplorer,factory);yield return Applied(sequence);Send(PlayableCommandKind.QueueTank,factory);yield return Applied(sequence);
            var queued=State().Orders.ToArray();Assert.True(State().Repeat);Assert.AreEqual(active.Id,queued[0].Id);
            CollectionAssert.AreEqual(new[]{PlayableEntityKind.Tank,PlayableEntityKind.Explorer,PlayableEntityKind.Tank},queued.Select(o=>o.Kind));Trace("manual queue appended");
            yield return Until(()=>State().Orders.Any(o=>o.Id==queued[1].Id&&o.Active),"manual Explorer starts");Assert.True(State().Repeat);Assert.AreEqual(initialUnits+1,runtime.Latest.Entities.Count(e=>e.Owner==PlayableOwner.Player));Trace("manual Explorer active");
            yield return Until(()=>State().Orders.Any(o=>o.Id==queued[2].Id&&o.Active),"manual Tank starts");Assert.True(State().Repeat);Assert.AreEqual(initialUnits+2,runtime.Latest.Entities.Count(e=>e.Owner==PlayableOwner.Player));Trace("manual Tank active");
            yield return Until(()=>State().Orders.Any(o=>o.Id>queued[2].Id&&o.Active),"repeat resumes");Assert.True(State().Repeat);Assert.AreEqual(PlayableEntityKind.Tank,State().Orders.Single().Kind);Assert.AreEqual(initialUnits+3,runtime.Latest.Entities.Count(e=>e.Owner==PlayableOwner.Player));Trace("Tank repeat resumed after three births");
            Send(PlayableCommandKind.ToggleRepeatProduction,factory);yield return Applied(sequence);
            Assert.False(State().Repeat);yield return Until(()=>State().Orders.Count==0,"paid repeat head finishes after disable");
            Assert.AreEqual(initialUnits+4,runtime.Latest.Entities.Count(e=>e.Owner==PlayableOwner.Player));
        }
        finally{runtime.RequestStop();Assert.True(System.Threading.SpinWait.SpinUntil(()=>runtime.IsStopped,5000),"owned runtime stops");runtime.Dispose();}
    }
}
