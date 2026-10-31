using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object=UnityEngine.Object;

namespace Spacewars.Tests.PlayMode
{
    public sealed class ProducerUpgradePresentationTests
    {
        const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
        static object Call(object o,string method,params object[] args)=>o.GetType().GetMethod(method,F).Invoke(o,args);
        static void Set(object o,string field,object value)=>o.GetType().GetField(field,F).SetValue(o,value);
        static T Get<T>(object o,string field)=>(T)o.GetType().GetField(field,F).GetValue(o);
        [UnityTest] public IEnumerator ProvingEditorUpgradesBothProducerTypesForBothSides()
        {
            var setup=new ProvingGroundSetup(PlayableProfile.Default);
            for(int side=0;side<2;side++)
            {
                var site=setup.Profile.AuthoredMap.Sites(setup.Profile)[side];
                var kinds=new[]{PlayableBuildingKind.Factory,PlayableBuildingKind.Hangar,PlayableBuildingKind.ScientificCenter};
                for(int i=0;i<kinds.Length;i++)Assert.IsNull(setup.PlaceBuilding(side,kinds[i],site.Slots[i].Position));
            }
            var runtime=new PlayableRuntime(setup.Configuration(),814,startPaused:false);
            try
            {
                float deadline=Time.realtimeSinceStartup+30;
                while(!runtime.IsPrepared&&Time.realtimeSinceStartup<deadline)yield return null;
                Assert.True(runtime.IsPrepared);
                foreach(string owner in new[]{"player-1","enemy-1"})
                {
                    var view=runtime.ParticipantView(owner);long seq=0;
                    foreach(var b in view.Buildings.Where(b=>b.Owner==view.Owner&&(b.Kind==PlayableBuildingKind.Factory||b.Kind==PlayableBuildingKind.Hangar)))
                        Assert.True(runtime.EditProvingGround(new PlayableCommand(814,++seq,owner,PlayableCommandKind.UpgradeProducer,new[]{b.Id})),"sandbox command uses the real editor ingress");
                }
                runtime.RequestPause(false);deadline=Time.realtimeSinceStartup+20;
                bool Complete(string owner){var view=runtime.ParticipantView(owner);return view!=null&&view.Buildings.Where(b=>b.Owner==view.Owner&&(b.Kind==PlayableBuildingKind.Factory||b.Kind==PlayableBuildingKind.Hangar)).Count(b=>b.PrivateState?.Upgrade?.Complete==true)==2;}
                while(!(Complete("player-1")&&Complete("enemy-1"))&&Time.realtimeSinceStartup<deadline)yield return null;
                Assert.True(Complete("player-1")&&Complete("enemy-1"));
            }
            finally{runtime.RequestStop();}
            float stopDeadline=Time.realtimeSinceStartup+5;while(!runtime.IsStopped&&Time.realtimeSinceStartup<stopDeadline)yield return null;Assert.True(runtime.IsStopped);
        }
        [UnityTest] public IEnumerator ThreadedQueueCompletesUpgradeAndRendersActualMixedSnapshots()
        {
            // Ordinary native domain and certified threaded routing; no oracle/synchronous Pump.
            var p=PlayableProfile.Default;var d=new PlayableDomain(p,813,false);CertifiedRouteWorker worker=null;
            var host=new GameObject("Producer upgrade HUD evidence");PlayableBootstrap hud=null;RenderTexture target=null;
            PlayableSnapshot View()=>d.Snapshot(1,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7);
            try
            {
                Call(d,"AddCredits",PlayableOwner.Player,10000d);
                int Build(PlayableBuildingKind kind,int slot){Assert.AreEqual(PlayableCommandStatus.Applied,Call(d,"BuildAt",1,slot,kind,1,PlayableOwner.Player,null));Call(d,"AdvanceFoundations");Call(d,"AdvanceBuildings",100d);return View().Buildings.Single(b=>b.SlotId==slot).Id;}
                int factory=Build(PlayableBuildingKind.Factory,1),hangar=Build(PlayableBuildingKind.Hangar,2),center=Build(PlayableBuildingKind.ScientificCenter,3);
                var authority=new PlayableAuthorityTick(d,new AiAuthorityScheduler(Array.Empty<PlayableAiOwnerLoop>()),7);
                worker=new CertifiedRouteWorker(authority.NavigationBinding,p.NavigationRequestsPerFrame);long sequence=0;
                void Send(PlayableCommandKind kind,int id,PlayableResearchKind research=PlayableResearchKind.TankChassis)=>Assert.AreEqual(PlayableCommandStatus.Applied,authority.Apply(new PlayableCommand(813,++sequence,"player-1",kind,new[]{id},researchKind:research)).Status);
                foreach(var producer in new[]{factory,hangar})
                {
                    var kind=producer==factory?PlayableCommandKind.QueueTank:PlayableCommandKind.QueueHawk;
                    Send(kind,producer);Send(PlayableCommandKind.UpgradeProducer,producer);Send(kind,producer);
                }
                Send(PlayableCommandKind.QueueResearch,center,PlayableResearchKind.ImprovedWeaponSystems);Send(PlayableCommandKind.QueueResearch,center,PlayableResearchKind.ScientificCenterUpgrade);Send(PlayableCommandKind.QueueResearch,center,PlayableResearchKind.ImprovedDefensiveSystems);
                hud=host.AddComponent<PlayableBootstrap>();hud.enabled=false;Set(hud,"profile",p);Set(hud,"view",View());Call(hud,"CreateHud");var root=Get<VisualElement>(hud,"root");root.Clear();
                root.style.backgroundColor=new Color(.035f,.055f,.07f);root.style.paddingTop=20;root.style.paddingLeft=24;root.style.paddingRight=24;
                var title=new Label("Фабрика и ангар · улучшение в общей очереди");title.style.fontSize=25;title.style.color=Color.white;root.Add(title);
                var note=new Label("Состояния настоящего threaded-матча · seed 7 · скорость после улучшения ×1,5");note.style.fontSize=15;note.style.color=Color.gray;note.style.marginBottom=18;root.Add(note);
                var row=new VisualElement();row.style.flexDirection=FlexDirection.Row;root.Add(row);
                var panels=new VisualElement[3];for(int i=0;i<3;i++){panels[i]=new VisualElement();panels[i].style.width=480;panels[i].style.marginRight=12;row.Add(panels[i]);}
                var settings=host.GetComponent<UIDocument>().panelSettings;settings.scaleMode=PanelScaleMode.ConstantPixelSize;settings.scale=1;target=new RenderTexture(1520,780,24);target.Create();settings.targetTexture=target;
                void Render(PlayableSnapshot view)
                {
                    Set(hud,"profile",p);Set(hud,"view",view);
                    for(int i=0;i<3;i++)
                    {
                        var panel=panels[i];panel.Clear();int id=i==0?factory:i==1?hangar:center;var b=view.Buildings.Single(x=>x.Id==id);
                        var heading=new Label(i==0?"Фабрика":i==1?"Ангар":"Научный центр");heading.style.color=Color.white;heading.style.fontSize=22;heading.style.marginBottom=14;panel.Add(heading);
                        if(i<2)
                        {
                            Set(hud,"queueLabel",new Label());Set(hud,"selectionLabel",new Label());Set(hud,"progress",new ProgressBar());
                            foreach(var field in new[]{"hawkButton","tankButton","explorerButton","shkvalButton"})Set(hud,field,new Button());
                            Call(hud,"CreateProductionHud",panel);Call(hud,"UpdateProductionHud",b);
                            var slots=panel.Query<Button>().ToList();Assert.AreEqual(6,slots.Count);Assert.True(b.PrivateState.Orders.Any(o=>o.IsUpgrade));Assert.That(slots.First(x=>x.tooltip.Contains("Улучшение здания")).Q<OrbitalGlyph>().Kind,Is.EqualTo("science"));
                        }
                        Call(hud,"CreateScienceHud",panel);Call(hud,"UpdateScienceHud",b);
                    }
                }
                bool capturedBefore=false,capturedUpgrade=false,fastFactory=false,fastHangar=false,centerDone=false;var clock=Stopwatch.StartNew();
                string folder=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.local/producer-upgrades/images"));Directory.CreateDirectory(folder);
                IEnumerator Capture(string name,PlayableSnapshot snapshot)
                {
                    Render(snapshot);for(int i=0;i<8;i++)yield return null;
                    var old=RenderTexture.active;RenderTexture.active=target;var pixels=new Texture2D(target.width,target.height,TextureFormat.RGBA32,false);
                    try{pixels.ReadPixels(new Rect(0,0,target.width,target.height),0,0);pixels.Apply();File.WriteAllBytes(Path.Combine(folder,name+".png"),pixels.EncodeToPNG());}
                    finally{RenderTexture.active=old;Object.Destroy(pixels);}
                }
                while(d.Tick<3000&&clock.Elapsed.TotalSeconds<55)
                {
                    authority.TryAdvance();Assert.IsNull(worker.Publication.Failure);var view=authority.Latest;
                    if(view==null){yield return null;continue;}
                    var a=view.Buildings.Single(b=>b.Id==factory);var b=view.Buildings.Single(x=>x.Id==hangar);
                    if(!capturedBefore&&a.PrivateState.Orders.Count==3&&a.PrivateState.Orders[0].Progress>.1&&b.PrivateState.Orders.Count==3)
                    {
                        Assert.AreEqual(3,view.OwnerResearch.Count);yield return Capture("queue-before-upgrade",view);capturedBefore=true;
                    }
                    if(!capturedUpgrade&&a.PrivateState.Orders.FirstOrDefault()?.IsUpgrade==true&&a.PrivateState.Orders[0].Progress>.1&&b.PrivateState.Orders.Any(o=>o.IsUpgrade))
                    {
                        long pausedTick=d.Tick;double remaining=a.PrivateState.Orders[0].Remaining,credits=(double)Call(d,"Balance",PlayableOwner.Player);
                        for(int i=0;i<10;i++)authority.TryAdvance(true);
                        Assert.AreEqual(pausedTick,d.Tick);Assert.AreEqual(remaining,View().Buildings.Single(x=>x.Id==factory).PrivateState.Orders[0].Remaining);Assert.AreEqual(credits,(double)Call(d,"Balance",PlayableOwner.Player));
                        yield return Capture("queue-upgrade-active",view);capturedUpgrade=true;
                    }
                    fastFactory|=a.PrivateState.Upgrade?.Complete==true&&a.PrivateState.Orders.Any(o=>!o.IsUpgrade&&o.Active&&Math.Abs(o.Duration-p.TankProductionSeconds/p.UpgradedProducerSpeedMultiplier)<1e-9);
                    fastHangar|=b.PrivateState.Upgrade?.Complete==true&&b.PrivateState.Orders.Any(o=>!o.IsUpgrade&&o.Active&&Math.Abs(o.Duration-p.HawkProductionSeconds/p.UpgradedProducerSpeedMultiplier)<1e-9);
                    centerDone=view.Buildings.Single(x=>x.Id==center).ScientificCenterUpgraded;
                    if(capturedBefore&&capturedUpgrade&&fastFactory&&fastHangar&&centerDone)break;
                    // Let the actual route worker publish without imposing realtime match pacing.
                    Thread.Sleep(1);
                }
                TestContext.WriteLine($"PRODUCER_UPGRADE seed=7 tick={d.Tick} before={capturedBefore} upgrading={capturedUpgrade} factoryFast={fastFactory} hangarFast={fastHangar} centerDone={centerDone} wall={clock.Elapsed.TotalSeconds:R}");
                Assert.True(capturedBefore&&capturedUpgrade&&fastFactory&&fastHangar&&centerDone);Assert.True(d.Navigation.HasCertifiedHost);
                // Real owner projections also supply the gamepad descriptor and stable cancellation order ID.
                var action=OfflinePadWorldActions.Building(View(),p,factory).Single(a=>a.Sector.Id.EndsWith(":producer-upgrade"));Assert.False(action.Sector.Enabled);
            }
            finally
            {
                worker?.Dispose();if(worker!=null)Assert.True(SpinWait.SpinUntil(()=>worker.IsStopped,5000));d.Navigation.ReleaseCommittedHostRoot();d.DestroyOwnedNavigation();
                if(host.GetComponent<UIDocument>()?.panelSettings is PanelSettings settings)settings.targetTexture=null;
                if(target){target.Release();Object.Destroy(target);}Object.Destroy(host);
            }
        }
    }
}
