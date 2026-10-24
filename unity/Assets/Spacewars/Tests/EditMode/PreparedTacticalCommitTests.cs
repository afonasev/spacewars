using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class PreparedTacticalCommitTests
    {
        private const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
        private static object Field(object o,string n)=>o.GetType().GetField(n,F).GetValue(o);
        private static void Set(object o,string n,object v)=>o.GetType().GetField(n,F).SetValue(o,v);
        private static object Item(object map,int id)=>map.GetType().GetProperty("Item").GetValue(map,new object[]{id});
        private static object Unit(PlayableDomain d,int id)=>Item(Field(d,"units"),id);
        private static string UnitState(PlayableDomain d,int id)
        {
            var u=Unit(d,id);var names=new[]{"CurrentOrder","LastOrder","Target","ExplicitTarget","HasAttackMove","AttackMove","Repath","StoppedSeconds","Reload","BurstRemaining","BurstTarget","BurstSequence","BurstSize","BurstSpread","BurstDelay"};
            return Newtonsoft.Json.JsonConvert.SerializeObject(names.ToDictionary(n=>n,n=>Field(u,n)));
        }
        private sealed class World : IDisposable
        {
            internal readonly PlayableProfile Profile;internal PlayableDomain Domain;internal NavigationSession Nav;internal CertifiedRouteKernel Kernel=new CertifiedRouteKernel();
            internal World(bool policy15=false)
            {
                if(policy15){var data=PlayableProfile.Default.CopyData();data.groupCorridorMaximumExcessPercent=15;Profile=PlayableProfile.Create(data);}else Profile=PlayableProfile.Default;
                Domain=new PlayableDomain(Profile,1,false);Nav=Domain.Navigation;Domain.MeasureTacticalCommitForQa=true;Domain.LastTacticalCommitAllocatedBytesForQa=-1;
            }
            internal PlayableRouteBinding Binding()=>new PlayableRouteBinding(Domain.Geometry,Profile,Nav.Admission,Nav.RoutePort);
            internal bool At(CertifiedRouteKernel.PumpStage stage){var r=Nav.PinCommittedHostRoot();try{return r!=null&&r.Frontier.Stage==stage;}finally{r?.Release();}}
            internal void PumpTo(CertifiedRouteKernel.PumpStage stage)
            {Kernel.PauseAtPhysicalBoundary=()=>At(stage);Kernel.Pump(Binding(),4);Assert.True(At(stage),"real physical boundary "+stage);}
            internal NavPoint Position(int id){Assert.True(Nav.Crowd.TryGet(id,out var u));return u.Position;}
            internal PlayableCommand Command(long seq,PlayableCommandKind kind,int[] ids=null,int target=0,NavPoint? goal=null,PlayableOrderMode mode=PlayableOrderMode.Replace)
            {var p=Position(3);return new PlayableCommand(1,seq,PlayableDomain.PlayerId,kind,ids??new[]{3},goal??new NavPoint(p.X+2,p.Z+3),targetId:target,mode:mode);}
            internal int Spawn(PlayableOwner owner,NavPoint p)=>(int)typeof(PlayableDomain).GetMethod("SpawnUnit",F).Invoke(Domain,new object[]{p,owner,PlayableEntityKind.Tank});
            internal Action ChangeOwnedProfile()
            {
                var old=Field(Nav,"profile");var data=PlayableProfile.Default.CopyData();data.groupCorridorMaximumExcessPercent=15;
                Assert.True(Nav.PhysicalHostBudget.TryOpenScope(NavigationPreparationKind.Admission,Nav.Generation,Nav.Admission.PublicationIdentity,out var scope));
                NavigationProfile changed;
                using(scope)changed=NavigationProfile.CopyOwned(PlayableProfile.Create(data).Navigation,scope.OwnedReservation());
                Assert.True(changed.StorageRoot.BelongsTo(Nav.PhysicalHostBudget));Set(Nav,"profile",changed);
                return ()=>{Set(Nav,"profile",old);changed.StorageRoot.Release();};
            }
            internal byte[] Bytes(){using(var state=Domain.CaptureWorldState(7,"atomic-tactical-native-v1"))return state.Encode();}
            internal void Step(int count)
            {Kernel.PauseAtPhysicalBoundary=null;for(int i=0;i<count;i++){Kernel.Pump(Binding(),4);Domain.Step(1d/30);}}
            internal void Cold(byte[] bytes)
            {
                Kernel.Clear();Nav.ReleaseCommittedHostRoot();Domain.DestroyOwnedNavigation();Assert.Zero(Nav.PhysicalHostBudget.PhysicalBytes);
                Domain=PlayableDomain.RestoreWorldBytes(bytes,Profile,7,"atomic-tactical-native-v1");Nav=Domain.Navigation;Kernel=new CertifiedRouteKernel();
            }
            public void Dispose()
            {
                Domain.AfterTacticalPreparationForQa=null;Nav.AfterMovementObservationForQa=null;Nav.AfterStopPreparationForQa=null;Nav.AfterTacticalPublishForQa=null;Nav.BeforeTacticalResolveForQa=null;Nav.AfterTacticalCleanupForQa=null;Nav.PhysicalHostBudget.AllocationAdmission=null;Nav.LastGroupGuardCandidateForQa=null;
                Kernel.PauseAtPhysicalBoundary=null;Kernel.Clear();Nav.ReleaseCommittedHostRoot();Domain.DestroyOwnedNavigation();Assert.Zero(Nav.PhysicalHostBudget.PhysicalBytes);Assert.Zero(Nav.PhysicalHostBudget.HeldBytes);Assert.Zero(Nav.PhysicalHostBudget.ActiveScopes);
            }
        }
        private static byte[] HostBody(NavigationSession nav,NavigationHostRoot root)
        {
            Assert.True(nav.PhysicalHostBudget.TryOpenScope(NavigationPreparationKind.Capture,nav.Generation,nav.Admission.PublicationIdentity,out var scope));
            using(scope)using(var output=NavigationCaptureBytes.Write(scope.OwnedReservation(),writer=>NavPhysicalBodyWire.WriteTo(writer,root,scope.OwnedReservation())))return output.TransferPublic();
        }
        private static void StartAccruedScan(World w)
        {
            var authority=new PlayableAuthorityTick(w.Domain,new AiAuthorityScheduler(Array.Empty<PlayableAiOwnerLoop>()),7,asynchronousRoutes:true);authority.EnableAccruedCredits();
            var at=w.Position(4);Assert.True(w.Nav.Move(4,new NavPoint(at.X-3,at.Z+3)));
            var routing=w.Domain.RoutingProfile;Assert.True(w.Nav.AccountAccruedCredit(w.Domain.Tick+1,routing.NavigationRequestsPerFrame,routing.Revision,routing.MaxAccruedCredits,w.Domain));
            w.PumpTo(CertifiedRouteKernel.PumpStage.Scan);
            var root=w.Nav.PinCommittedHostRoot();try{Assert.True(root.TickQuantum);Assert.True(root.QuantumRunnable);Assert.False(root.QuantumCompleted);Assert.AreNotEqual(0,Field(w.Nav,"hostAllocationThread"));}finally{root.Release();}
        }
        private static void CompleteActualQuantum(World w)
        {
            bool Complete(){var r=w.Nav.PinCommittedHostRoot();try{return r!=null&&r.Frontier.Stage==CertifiedRouteKernel.PumpStage.Idle&&r.QuantumCompleted;}finally{r?.Release();}}
            w.Kernel.PauseAtPhysicalBoundary=Complete;
            for(int i=0;i<10000&&!Complete();i++)w.Kernel.Pump(w.Binding(),4);
            Assert.True(Complete(),"Actual paid kernel publishes completed Idle; command does not close it.");Assert.AreEqual(0,Field(w.Nav,"hostAllocationThread"));
        }
        private static long[] BankValues(NavPagedList<long> bank)=>Enumerable.Range(0,bank.Count).Select(i=>bank[i]).ToArray();
        [TestCase("source")][TestCase("stage")][TestCase("commit")]
        public void SameQuantumCompletionPreservesCurrentHostBankAndColdState(string boundary)
        {
            using(var w=new World()){
                StartAccruedScan(w);NavigationHostRoot closed=null,observed=null;NavPagedList<long> bank=null;NavigationRetentionBudget.Scope scope=null;NavigationSession.MovementReadSet inputs=null;
                byte[] body=null;long[] bankValues=null;long progress=0;CertifiedRouteKernel.PumpFrontier frontier=default;int reached=0;
                void CaptureClosed(){reached++;CompleteActualQuantum(w);closed=w.Nav.PinCommittedHostRoot();bank=w.Nav.PinCreditBank();Assert.NotNull(bank);body=HostBody(w.Nav,closed);bankValues=BankValues(bank);progress=w.Kernel.ProgressSequence;frontier=w.Kernel.CapturePumpFrontier();}
                try{
                    if(boundary=="source"){
                        w.Nav.PrepareCommandGroup(w.Command(1,PlayableCommandKind.Move),1,w.Domain.Tick,1);
                        Assert.True(w.Nav.PhysicalHostBudget.TryOpenScope(NavigationPreparationKind.Admission,1,w.Nav.Admission.PublicationIdentity,out scope));
                        inputs=NavigationSession.MovementReadSet.CreateOwned(w.Nav,scope.OwnedReservation());observed=w.Nav.PinCommittedHostRoot();
                        var type=typeof(NavigationSession).GetNestedType("StopSource",BindingFlags.NonPublic);object source=Activator.CreateInstance(type,F,null,new object[]{w.Nav},null);
                        CaptureClosed();Assert.False((bool)type.GetMethod("Current",F).Invoke(source,new object[]{w.Nav}),"legacy stop source stays strict");
                        Assert.True((bool)type.GetMethod("CurrentForMovement",F).Invoke(source,new object[]{w.Nav,observed,inputs,false}),"both Move early source boundaries use this complete semantic predicate");
                        Assert.False((bool)type.GetMethod("CurrentForMovement",F).Invoke(source,new object[]{w.Nav,observed,inputs,true}),"legacy single cannot opt in");
                        w.Nav.AbortTacticalPreparation();inputs.ReleaseOwner();inputs=null;scope.Dispose();scope=null;observed.Release();observed=null;
                    }else{
                        if(boundary=="stage")w.Nav.AfterMovementObservationForQa=CaptureClosed;else w.Domain.AfterTacticalPreparationForQa=_=>CaptureClosed();
                        Assert.AreEqual(PlayableCommandStatus.Applied,w.Domain.Apply(w.Command(1,PlayableCommandKind.Move),out _));
                        Assert.NotNull(w.Domain.TacticalQueueProjection(3,PlayableOwner.Player).Pending);Assert.Zero(w.Domain.LastTacticalCommitAllocatedBytesForQa);
                    }
                    Assert.AreEqual(1,reached);var current=w.Nav.PinCommittedHostRoot();var currentBank=w.Nav.PinCreditBank();
                    try{Assert.AreSame(closed,current,"command must leave CURRENT host authoritative");Assert.AreSame(bank,currentBank,"command cannot replace/refill bank");CollectionAssert.AreEqual(body,HostBody(w.Nav,current),"entire current solver/work/frontier BODY unchanged by command");CollectionAssert.AreEqual(bankValues,BankValues(currentBank));Assert.AreEqual(progress,w.Kernel.ProgressSequence);Assert.AreEqual(frontier,w.Kernel.CapturePumpFrontier());}
                    finally{current?.Release();currentBank?.Release();}
                    closed.Release();closed=null;bank.Release();bank=null;w.Domain.AfterTacticalPreparationForQa=null;w.Nav.AfterMovementObservationForQa=null;
                    Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(w.Nav),w.Nav.PhysicalHostBudget.PhysicalBytes);Assert.Zero(w.Nav.PhysicalHostBudget.HeldBytes);Assert.Zero(w.Nav.PhysicalHostBudget.ActiveScopes);
                    var bytes=w.Bytes();w.Cold(bytes);CollectionAssert.AreEqual(bytes,w.Bytes(),"complete current-format cold state exact after completion admission");
                }finally{w.Nav.AbortTacticalPreparation();inputs?.ReleaseOwner();scope?.Dispose();observed?.Release();closed?.Release();bank?.Release();}
            }
        }
        [TestCase("source",false,PlayableCommandKind.Move)]
        [TestCase("stage",false,PlayableCommandKind.Move)]
        [TestCase("commit",false,PlayableCommandKind.Move)]
        [TestCase("source",true,PlayableCommandKind.Move)]
        [TestCase("stage",true,PlayableCommandKind.Move)]
        [TestCase("commit",true,PlayableCommandKind.Move)]
        [TestCase("source",true,PlayableCommandKind.AttackMove)]
        public void AllocationStartPreservesCurrentHostBankAndColdState(string boundary,bool credit,PlayableCommandKind kind)
        {
            using(var w=new World()){
                if(credit){
                    var authority=new PlayableAuthorityTick(w.Domain,new AiAuthorityScheduler(Array.Empty<PlayableAiOwnerLoop>()),7,asynchronousRoutes:true);authority.EnableAccruedCredits();
                }
                w.Kernel.Attach(w.Nav.RoutePort);w.Bytes();
                if(credit){
                    var at=w.Position(4);Assert.True(w.Nav.Move(4,new NavPoint(at.X-3,at.Z+3)));
                    var routing=w.Domain.RoutingProfile;
                    Assert.True(w.Nav.AccountAccruedCredit(w.Domain.Tick+1,routing.NavigationRequestsPerFrame,routing.Revision,routing.MaxAccruedCredits,w.Domain));
                    Assert.AreEqual(NavigationCreditPromotion.Promoted,w.Nav.TryPromoteAccruedCredit());
                }
                NavigationHostRoot started=null;NavPagedList<long> bank=null;byte[] body=null;long[] values=null;long progress=0;CertifiedRouteKernel.PumpFrontier frontier=default;int reached=0;
                void Start(){reached++;w.PumpTo(CertifiedRouteKernel.PumpStage.Scan);started=w.Nav.PinCommittedHostRoot();bank=w.Nav.PinCreditBank();body=HostBody(w.Nav,started);values=bank==null?null:BankValues(bank);progress=w.Kernel.ProgressSequence;frontier=w.Kernel.CapturePumpFrontier();}
                try{
                    if(boundary=="source")w.Nav.AfterMovementSourcePinsForTests=Start;
                    else if(boundary=="stage")w.Nav.AfterMovementObservationForQa=Start;
                    else w.Domain.AfterTacticalPreparationForQa=_=>Start();
                    Assert.AreEqual(PlayableCommandStatus.Applied,w.Domain.Apply(w.Command(1,kind),out _));Assert.AreEqual(1,reached);Assert.Zero(w.Domain.LastTacticalCommitAllocatedBytesForQa);
                    var current=w.Nav.PinCommittedHostRoot();var currentBank=w.Nav.PinCreditBank();
                    try{Assert.AreSame(started,current);Assert.AreSame(bank,currentBank);CollectionAssert.AreEqual(body,HostBody(w.Nav,current));if(bank!=null)CollectionAssert.AreEqual(values,BankValues(currentBank));Assert.AreEqual(progress,w.Kernel.ProgressSequence);Assert.AreEqual(frontier,w.Kernel.CapturePumpFrontier());}
                    finally{current?.Release();currentBank?.Release();}
                    w.Nav.AfterMovementSourcePinsForTests=null;w.Nav.AfterMovementObservationForQa=null;w.Domain.AfterTacticalPreparationForQa=null;
                    started.Release();started=null;bank?.Release();bank=null;
                    Assert.Zero(w.Nav.PhysicalHostBudget.HeldBytes);Assert.Zero(w.Nav.PhysicalHostBudget.ActiveScopes);Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(w.Nav),w.Nav.PhysicalHostBudget.PhysicalBytes);
                    var bytes=w.Bytes();w.Cold(bytes);CollectionAssert.AreEqual(bytes,w.Bytes());
                }finally{w.Nav.AfterMovementSourcePinsForTests=null;started?.Release();bank?.Release();}
            }
        }
        [TestCase("worker")][TestCase("attachment")][TestCase("quantum")][TestCase("serial")][TestCase("budget")][TestCase("prefix")][TestCase("consumed")][TestCase("immutable")][TestCase("token")][TestCase("actor")][TestCase("profile")][TestCase("cancel")]
        public void AllocationStartStillRejectsGenuineConflict(string defect)
        {
            using(var w=new World()){
                w.Kernel.Attach(w.Nav.RoutePort);w.Bytes();Action restore=null;NavigationHostRoot changed=null;int reached=0;
                w.Domain.AfterTacticalPreparationForQa=payload=>{
                    reached++;w.PumpTo(CertifiedRouteKernel.PumpStage.Scan);changed=w.Nav.PinCommittedHostRoot();
                    if(defect=="worker"){var old=Field(w.Nav,"certifiedHost");Set(w.Nav,"certifiedHost",new CertifiedRouteKernel());restore=()=>Set(w.Nav,"certifiedHost",old);}
                    if(defect=="attachment"){var old=(long)Field(w.Nav,"certifiedHostAttachmentIdentity");Set(w.Nav,"certifiedHostAttachmentIdentity",old+1);restore=()=>Set(w.Nav,"certifiedHostAttachmentIdentity",old);}
                    if(defect=="quantum"||defect=="serial"||defect=="budget"||defect=="prefix"||defect=="consumed"){
                        string name=defect=="quantum"?"QuantumTick":defect=="serial"?"QuantumSerial":defect=="budget"?"QuantumBudget":defect=="prefix"?"AdmissionPrefix":"ConsumedQuantum";
                        var field=typeof(NavigationHostRoot).GetField("<"+name+">k__BackingField",F);object old=field.GetValue(changed);object next;if(old is int)next=(int)old+1;else next=(long)old+1;field.SetValue(changed,next);restore=()=>field.SetValue(changed,old);
                    }
                    if(defect=="immutable"){byte old=(byte)Field(changed,"lifecycle");Set(changed,"lifecycle",(byte)(old&~1));restore=()=>Set(changed,"lifecycle",old);}
                    if(defect=="token"){int old=(int)Field(w.Nav,"hostAllocationThread");Set(w.Nav,"hostAllocationThread",old+1);restore=()=>Set(w.Nav,"hostAllocationThread",old);}
                    if(defect=="actor"){w.Nav.Crowd.TryGet(3,out var actor);var old=actor.Position;actor.Position=new NavPoint(old.X+.5,old.Z);restore=()=>actor.Position=old;}
                    if(defect=="profile")restore=w.ChangeOwnedProfile();
                    if(defect=="cancel")((NavigationRetentionBudget.Scope)Field(w.Nav,"preparedCommandScope")).RequestCancel();
                };
                try{Assert.AreEqual(PlayableCommandStatus.Rejected,w.Domain.Apply(w.Command(1,PlayableCommandKind.Move),out _),defect);Assert.AreEqual(1,reached);Assert.IsNull(Field(Unit(w.Domain,3),"LastOrder"));}
                finally{restore?.Invoke();changed?.Release();}
            }
        }
        [TestCase("worker")][TestCase("attachment")][TestCase("quantum")][TestCase("serial")][TestCase("budget")][TestCase("prefix")][TestCase("consumed")][TestCase("immutable")][TestCase("token")][TestCase("actor")][TestCase("profile")][TestCase("cancel")]
        public void SameQuantumCompletionStillRejectsGenuineConflict(string defect)
        {
            using(var w=new World()){
                StartAccruedScan(w);Action restore=null;NavigationHostRoot changed=null;int reached=0;
                w.Domain.AfterTacticalPreparationForQa=payload=>{
                    reached++;CompleteActualQuantum(w);changed=w.Nav.PinCommittedHostRoot();
                    if(defect=="worker"){var old=Field(w.Nav,"certifiedHost");Set(w.Nav,"certifiedHost",new CertifiedRouteKernel());restore=()=>Set(w.Nav,"certifiedHost",old);}
                    if(defect=="attachment"){var old=(long)Field(w.Nav,"certifiedHostAttachmentIdentity");Set(w.Nav,"certifiedHostAttachmentIdentity",old+1);restore=()=>Set(w.Nav,"certifiedHostAttachmentIdentity",old);}
                    if(defect=="quantum"||defect=="serial"||defect=="budget"||defect=="prefix"||defect=="consumed"){
                        string name=defect=="quantum"?"QuantumTick":defect=="serial"?"QuantumSerial":defect=="budget"?"QuantumBudget":defect=="prefix"?"AdmissionPrefix":"ConsumedQuantum";
                        var field=typeof(NavigationHostRoot).GetField("<"+name+">k__BackingField",F);object old=field.GetValue(changed);object next;if(old is int)next=(int)old+1;else next=(long)old+1;field.SetValue(changed,next);restore=()=>field.SetValue(changed,old);
                    }
                    if(defect=="immutable"){byte old=(byte)Field(changed,"lifecycle");Set(changed,"lifecycle",(byte)(old&~1));restore=()=>Set(changed,"lifecycle",old);}
                    if(defect=="token"){int old=(int)Field(w.Nav,"hostAllocationThread");Set(w.Nav,"hostAllocationThread",old+1);restore=()=>Set(w.Nav,"hostAllocationThread",old);}
                    if(defect=="actor"){w.Nav.Crowd.TryGet(3,out var actor);var old=actor.Position;actor.Position=new NavPoint(old.X+.5,old.Z);restore=()=>actor.Position=old;}
                    if(defect=="profile")restore=w.ChangeOwnedProfile();
                    if(defect=="cancel")((NavigationRetentionBudget.Scope)Field(w.Nav,"preparedCommandScope")).RequestCancel();
                };
                try{Assert.AreEqual(PlayableCommandStatus.Rejected,w.Domain.Apply(w.Command(1,PlayableCommandKind.Move),out _),defect);Assert.AreEqual(1,reached);Assert.IsNull(Field(Unit(w.Domain,3),"LastOrder"));}
                finally{restore?.Invoke();changed?.Release();}
            }
        }
        private static void PrepareNextIdleCredit(World w)
        {
            StartAccruedScan(w);CompleteActualQuantum(w);
            w.Domain.Step(1d/30);var at=w.Position(4);Assert.True(w.Nav.Move(4,new NavPoint(at.X-4,at.Z+4)));
            var routing=w.Domain.RoutingProfile;
            Assert.True(w.Nav.AccountAccruedCredit(w.Domain.Tick+1,routing.NavigationRequestsPerFrame,routing.Revision,routing.MaxAccruedCredits,w.Domain));
        }
        [TestCase("stage",false,PlayableCommandKind.Move)][TestCase("commit",false,PlayableCommandKind.Move)]
        [TestCase("stage",true,PlayableCommandKind.Move)][TestCase("commit",true,PlayableCommandKind.Move)]
        [TestCase("stage",false,PlayableCommandKind.AttackMove)][TestCase("commit",false,PlayableCommandKind.AttackMove)]
        [TestCase("stage",true,PlayableCommandKind.AttackMove)][TestCase("commit",true,PlayableCommandKind.AttackMove)]
        public void NextIdleQuantumPreservesCurrentHostBankAndColdState(string boundary,bool complete,PlayableCommandKind kind)
        {
            using(var w=new World()){
                PrepareNextIdleCredit(w);NavigationHostRoot advanced=null;NavPagedList<long> bank=null;byte[] body=null;long[] values=null;
                long progress=0;CertifiedRouteKernel.PumpFrontier frontier=default;int reached=0;
                void Advance(){reached++;Assert.AreEqual(NavigationCreditPromotion.Promoted,w.Nav.TryPromoteAccruedCredit());if(complete)CompleteActualQuantum(w);
                    advanced=w.Nav.PinCommittedHostRoot();bank=w.Nav.PinCreditBank();body=HostBody(w.Nav,advanced);values=BankValues(bank);progress=w.Kernel.ProgressSequence;frontier=w.Kernel.CapturePumpFrontier();}
                try{
                    if(boundary=="stage")w.Nav.AfterMovementObservationForQa=Advance;else w.Domain.AfterTacticalPreparationForQa=_=>Advance();
                    Assert.AreEqual(PlayableCommandStatus.Applied,w.Domain.Apply(w.Command(1,kind),out _));Assert.AreEqual(1,reached);Assert.Zero(w.Domain.LastTacticalCommitAllocatedBytesForQa);
                    var current=w.Nav.PinCommittedHostRoot();var currentBank=w.Nav.PinCreditBank();
                    try{Assert.AreSame(advanced,current);Assert.AreSame(bank,currentBank);CollectionAssert.AreEqual(body,HostBody(w.Nav,current));CollectionAssert.AreEqual(values,BankValues(currentBank));Assert.AreEqual(progress,w.Kernel.ProgressSequence);Assert.AreEqual(frontier,w.Kernel.CapturePumpFrontier());}
                    finally{current.Release();currentBank.Release();}
                    w.Nav.AfterMovementObservationForQa=null;w.Domain.AfterTacticalPreparationForQa=null;advanced.Release();advanced=null;bank.Release();bank=null;
                    Assert.Zero(w.Nav.PhysicalHostBudget.HeldBytes);Assert.Zero(w.Nav.PhysicalHostBudget.ActiveScopes);Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(w.Nav),w.Nav.PhysicalHostBudget.PhysicalBytes);
                    var bytes=w.Bytes();w.Cold(bytes);CollectionAssert.AreEqual(bytes,w.Bytes());
                }finally{advanced?.Release();bank?.Release();}
            }
        }
        [TestCase("worker")][TestCase("attachment")][TestCase("serial")][TestCase("tick")][TestCase("budget")]
        [TestCase("prefix")][TestCase("consumed")][TestCase("immutable")][TestCase("token")][TestCase("actor")][TestCase("profile")][TestCase("cancel")]
        public void NextIdleQuantumStillRejectsGenuineConflict(string defect)
        {
            using(var w=new World()){
                PrepareNextIdleCredit(w);Action restore=null;NavigationHostRoot changed=null;int reached=0;
                w.Domain.AfterTacticalPreparationForQa=_=>{
                    reached++;Assert.AreEqual(NavigationCreditPromotion.Promoted,w.Nav.TryPromoteAccruedCredit());changed=w.Nav.PinCommittedHostRoot();
                    if(defect=="worker"){var old=Field(w.Nav,"certifiedHost");Set(w.Nav,"certifiedHost",new CertifiedRouteKernel());restore=()=>Set(w.Nav,"certifiedHost",old);}
                    if(defect=="attachment"){var old=(long)Field(w.Nav,"certifiedHostAttachmentIdentity");Set(w.Nav,"certifiedHostAttachmentIdentity",old+1);restore=()=>Set(w.Nav,"certifiedHostAttachmentIdentity",old);}
                    if(defect=="serial"||defect=="tick"||defect=="budget"||defect=="prefix"||defect=="consumed"){
                        string name=defect=="serial"?"QuantumSerial":defect=="tick"?"QuantumTick":defect=="budget"?"QuantumBudget":defect=="prefix"?"AdmissionPrefix":"ConsumedQuantum";
                        var field=typeof(NavigationHostRoot).GetField("<"+name+">k__BackingField",F);object old=field.GetValue(changed);object next;
                        if(old is int)next=(int)old+1;else next=defect=="tick"?0L:defect=="prefix"?long.MaxValue:(long)old+1;
                        field.SetValue(changed,next);restore=()=>field.SetValue(changed,old);
                    }
                    if(defect=="immutable"){byte old=(byte)Field(changed,"lifecycle");Set(changed,"lifecycle",(byte)(old&~1));restore=()=>Set(changed,"lifecycle",old);}
                    if(defect=="token"){int old=(int)Field(w.Nav,"hostAllocationThread");Set(w.Nav,"hostAllocationThread",old+1);restore=()=>Set(w.Nav,"hostAllocationThread",old);}
                    if(defect=="actor"){w.Nav.Crowd.TryGet(3,out var actor);var old=actor.Position;actor.Position=new NavPoint(old.X+.5,old.Z);restore=()=>actor.Position=old;}
                    if(defect=="profile")restore=w.ChangeOwnedProfile();
                    if(defect=="cancel")((NavigationRetentionBudget.Scope)Field(w.Nav,"preparedCommandScope")).RequestCancel();
                };
                try{Assert.AreEqual(PlayableCommandStatus.Rejected,w.Domain.Apply(w.Command(1,PlayableCommandKind.Move),out _),defect);Assert.AreEqual(1,reached);Assert.IsNull(Field(Unit(w.Domain,3),"LastOrder"));}
                finally{restore?.Invoke();changed?.Release();}
            }
        }
        private static void PrepareActivePhase(World w,bool completion,bool emptyProjection=true)
        {
            var authority=new PlayableAuthorityTick(w.Domain,new AiAuthorityScheduler(Array.Empty<PlayableAiOwnerLoop>()),7,asynchronousRoutes:true);authority.EnableAccruedCredits();
            var at=w.Position(4);Assert.True(w.Nav.Move(4,new NavPoint(at.X-3,at.Z+3)));
            var routing=w.Domain.RoutingProfile;Assert.True(w.Nav.AccountAccruedCredit(w.Domain.Tick+1,routing.NavigationRequestsPerFrame,routing.Revision,routing.MaxAccruedCredits,w.Domain));
            w.PumpTo(completion?CertifiedRouteKernel.PumpStage.Projection:CertifiedRouteKernel.PumpStage.ArrivalAdvance);
            if(completion&&emptyProjection){
                bool Empty(){var root=w.Nav.PinCommittedHostRoot();try{return root.Frontier.Stage==CertifiedRouteKernel.PumpStage.Projection&&root.Projection.Count==0;}finally{root.Release();}}
                w.Kernel.PauseAtPhysicalBoundary=Empty;w.Kernel.Pump(w.Binding(),4);
                Assert.True(Empty(),"Start after actual projection work, before EndAllocation; matching saved packet boundary.");
            }
        }
        [TestCase("stage")][TestCase("commit")]
        public void PostFlushStillRejectsActualProjectionWork(string boundary)
        {
            using(var w=new World()){
                PrepareActivePhase(w,true,false);var before=w.Nav.PinCommittedHostRoot();try{Assert.Greater(before.Projection.Count,0);}finally{before.Release();}
                int reached=0;void Advance(){reached++;w.PumpTo(CertifiedRouteKernel.PumpStage.PostFlush);}
                if(boundary=="stage")w.Nav.AfterMovementObservationForQa=Advance;else w.Domain.AfterTacticalPreparationForQa=_=>Advance();
                Assert.AreEqual(boundary=="stage"?PlayableCommandStatus.Overflow:PlayableCommandStatus.Rejected,w.Domain.Apply(w.Command(1,PlayableCommandKind.Move),out _));Assert.AreEqual(1,reached);Assert.IsNull(Field(Unit(w.Domain,3),"LastOrder"));
            }
        }
        [Test] public void PostFlushCompletionEarlySourceKeepsLegacyStrict()
        {
            using(var w=new World()){
                PrepareActivePhase(w,true);NavigationHostRoot observed=null;NavigationRetentionBudget.Scope scope=null;NavigationSession.MovementReadSet inputs=null;
                try{
                    w.Nav.PrepareCommandGroup(w.Command(1,PlayableCommandKind.Move),1,w.Domain.Tick,1);
                    Assert.True(w.Nav.PhysicalHostBudget.TryOpenScope(NavigationPreparationKind.Admission,1,w.Nav.Admission.PublicationIdentity,out scope));
                    inputs=NavigationSession.MovementReadSet.CreateOwned(w.Nav,scope.OwnedReservation());observed=w.Nav.PinCommittedHostRoot();
                    var type=typeof(NavigationSession).GetNestedType("StopSource",BindingFlags.NonPublic);object source=Activator.CreateInstance(type,F,null,new object[]{w.Nav},null);
                    w.PumpTo(CertifiedRouteKernel.PumpStage.PostFlush);
                    Assert.False((bool)type.GetMethod("Current",F).Invoke(source,new object[]{w.Nav}));
                    Assert.True((bool)type.GetMethod("CurrentForMovement",F).Invoke(source,new object[]{w.Nav,observed,inputs,false}));
                    Assert.False((bool)type.GetMethod("CurrentForMovement",F).Invoke(source,new object[]{w.Nav,observed,inputs,true}));
                }finally{w.Nav.AbortTacticalPreparation();inputs?.ReleaseOwner();scope?.Dispose();observed?.Release();}
            }
        }
        [TestCase("stage",false,PlayableCommandKind.Move)][TestCase("commit",false,PlayableCommandKind.Move)]
        [TestCase("stage",true,PlayableCommandKind.Move)][TestCase("commit",true,PlayableCommandKind.Move)]
        [TestCase("stage",false,PlayableCommandKind.AttackMove)][TestCase("commit",false,PlayableCommandKind.AttackMove)]
        [TestCase("stage",true,PlayableCommandKind.AttackMove)][TestCase("commit",true,PlayableCommandKind.AttackMove)]
        public void ActivePhasePreservesCurrentHostBankAndColdState(string boundary,bool complete,PlayableCommandKind kind)
        {
            using(var w=new World()){
                PrepareActivePhase(w,complete);NavigationHostRoot advanced=null;NavPagedList<long> bank=null;byte[] body=null;long[] values=null;
                long progress=0;CertifiedRouteKernel.PumpFrontier frontier=default;int reached=0;
                void Advance(){reached++;w.PumpTo(complete?CertifiedRouteKernel.PumpStage.PostFlush:CertifiedRouteKernel.PumpStage.PreFlush);
                    advanced=w.Nav.PinCommittedHostRoot();bank=w.Nav.PinCreditBank();body=HostBody(w.Nav,advanced);values=BankValues(bank);progress=w.Kernel.ProgressSequence;frontier=w.Kernel.CapturePumpFrontier();}
                try{
                    if(boundary=="stage")w.Nav.AfterMovementObservationForQa=Advance;else w.Domain.AfterTacticalPreparationForQa=_=>Advance();
                    Assert.AreEqual(PlayableCommandStatus.Applied,w.Domain.Apply(w.Command(1,kind),out _));Assert.AreEqual(1,reached);Assert.Zero(w.Domain.LastTacticalCommitAllocatedBytesForQa);
                    var current=w.Nav.PinCommittedHostRoot();var currentBank=w.Nav.PinCreditBank();
                    try{Assert.AreSame(advanced,current);Assert.AreSame(bank,currentBank);CollectionAssert.AreEqual(body,HostBody(w.Nav,current));CollectionAssert.AreEqual(values,BankValues(currentBank));Assert.AreEqual(progress,w.Kernel.ProgressSequence);Assert.AreEqual(frontier,w.Kernel.CapturePumpFrontier());}
                    finally{current.Release();currentBank.Release();}
                    w.Nav.AfterMovementObservationForQa=null;w.Domain.AfterTacticalPreparationForQa=null;advanced.Release();advanced=null;bank.Release();bank=null;
                    Assert.Zero(w.Nav.PhysicalHostBudget.HeldBytes);Assert.Zero(w.Nav.PhysicalHostBudget.ActiveScopes);Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(w.Nav),w.Nav.PhysicalHostBudget.PhysicalBytes);
                    var bytes=w.Bytes();w.Cold(bytes);CollectionAssert.AreEqual(bytes,w.Bytes());
                }finally{advanced?.Release();bank?.Release();}
            }
        }
        [TestCase(false,"worker")]
        [TestCase(false,"attachment")]
        [TestCase(false,"serial")]
        [TestCase(false,"tick")]
        [TestCase(false,"budget")]
        [TestCase(false,"prefix")]
        [TestCase(false,"consumed")]
        [TestCase(false,"immutable")]
        [TestCase(false,"token")]
        [TestCase(false,"actor")]
        [TestCase(false,"profile")]
        [TestCase(false,"cancel")]
        [TestCase(false,"generation")]
        [TestCase(false,"policy")]
        [TestCase(false,"phase")]
        [TestCase(false,"counter")]
        [TestCase(true,"worker")]
        [TestCase(true,"attachment")]
        [TestCase(true,"serial")]
        [TestCase(true,"tick")]
        [TestCase(true,"budget")]
        [TestCase(true,"prefix")]
        [TestCase(true,"consumed")]
        [TestCase(true,"immutable")]
        [TestCase(true,"token")]
        [TestCase(true,"actor")]
        [TestCase(true,"profile")]
        [TestCase(true,"cancel")]
        [TestCase(true,"generation")]
        [TestCase(true,"policy")]
        [TestCase(true,"phase")]
        [TestCase(true,"counter")]
        [TestCase(false,"source-token")][TestCase(true,"source-token")]
        public void ActivePhaseStillRejectsGenuineConflict(bool complete,string defect)
        {
            using(var w=new World()){
                PrepareActivePhase(w,complete);Action restore=null;NavigationHostRoot changed=null;int reached=0;
                w.Domain.AfterTacticalPreparationForQa=_=>{
                    reached++;w.PumpTo(complete?CertifiedRouteKernel.PumpStage.PostFlush:CertifiedRouteKernel.PumpStage.PreFlush);changed=w.Nav.PinCommittedHostRoot();
                    if(defect=="source-token"){var c=Field(w.Nav,"preparedCommandGroup");int old=(int)Field(c,"AllocationThread");Set(c,"AllocationThread",old+1);restore=()=>Set(c,"AllocationThread",old);}
                    if(defect=="worker"){var old=Field(w.Nav,"certifiedHost");Set(w.Nav,"certifiedHost",new CertifiedRouteKernel());restore=()=>Set(w.Nav,"certifiedHost",old);}
                    if(defect=="attachment"){var old=(long)Field(w.Nav,"certifiedHostAttachmentIdentity");Set(w.Nav,"certifiedHostAttachmentIdentity",old+1);restore=()=>Set(w.Nav,"certifiedHostAttachmentIdentity",old);}
                    if(defect=="serial"||defect=="tick"||defect=="budget"||defect=="prefix"||defect=="consumed"){
                        string name=defect=="serial"?"QuantumSerial":defect=="tick"?"QuantumTick":defect=="budget"?"QuantumBudget":defect=="prefix"?"AdmissionPrefix":"ConsumedQuantum";
                        var field=typeof(NavigationHostRoot).GetField("<"+name+">k__BackingField",F);object old=field.GetValue(changed);object next;
                        if(old is int)next=(int)old+1;else next=defect=="tick"?0L:defect=="prefix"?long.MaxValue:(long)old+1;
                        field.SetValue(changed,next);restore=()=>field.SetValue(changed,old);
                    }
                    if(defect=="immutable"){byte old=(byte)Field(changed,"lifecycle");Set(changed,"lifecycle",(byte)(old&~1));restore=()=>Set(changed,"lifecycle",old);}
                    if(defect=="token"){int old=(int)Field(w.Nav,"hostAllocationThread");Set(w.Nav,"hostAllocationThread",old+1);restore=()=>Set(w.Nav,"hostAllocationThread",old);}
                    if(defect=="actor"){w.Nav.Crowd.TryGet(3,out var actor);var old=actor.Position;actor.Position=new NavPoint(old.X+.5,old.Z);restore=()=>actor.Position=old;}
                    if(defect=="profile")restore=w.ChangeOwnedProfile();
                    if(defect=="generation"){var f=typeof(NavigationHostRoot).GetField("<QuantumGeneration>k__BackingField",F);var old=f.GetValue(changed);f.SetValue(changed,(long)old+1);restore=()=>f.SetValue(changed,old);}
                    if(defect=="policy"){byte old=(byte)Field(changed,"lifecycle");Set(changed,"lifecycle",(byte)(old^16));restore=()=>Set(changed,"lifecycle",old);}
                    if(defect=="phase"||defect=="counter"){
                        var f=typeof(NavigationHostRoot).GetField("<Frontier>k__BackingField",F);var old=changed.Frontier;var a=old;
                        f.SetValue(changed,new CertifiedRouteKernel.PumpFrontier(defect=="phase"?CertifiedRouteKernel.PumpStage.Retirement:a.Stage,a.OriginalBudget,a.ScansUsed,a.TransfersUsed,a.ReadyUsed,a.ProjectionUsed,a.ProjectionAllowance,a.SolveWorkLimit,a.SolveConsumed,a.AllocationFloor,defect=="counter"?a.PhaseUsed+1:a.PhaseUsed,a.ScanAllowance));restore=()=>f.SetValue(changed,old);
                    }
                    if(defect=="cancel")((NavigationRetentionBudget.Scope)Field(w.Nav,"preparedCommandScope")).RequestCancel();
                };
                try{Assert.AreEqual(PlayableCommandStatus.Rejected,w.Domain.Apply(w.Command(1,PlayableCommandKind.Move),out _),defect);Assert.AreEqual(1,reached);Assert.IsNull(Field(Unit(w.Domain,3),"LastOrder"));}
                finally{restore?.Invoke();changed?.Release();}
            }
        }
        [Test] public void DomainValuesRemainCanonicalAcrossAdmissionAppendAndColdContinuation()
        {
            using(var w=new World()){
                var peerPosition=new NavPoint(-22,-14);double radius=PlayableUnitRules.Radius(w.Profile,PlayableEntityKind.Tank);
                Assert.True(w.Nav.Crowd.Locate(peerPosition,radius,out var peerLocation),"owned peer needs a valid full-radius footprint");
                int peer=w.Spawn(PlayableOwner.Player,peerPosition);Assert.AreEqual(PlayableOwner.Player,Field(Unit(w.Domain,peer),"Owner"));
                Assert.True(w.Nav.Crowd.TryGet(peer,out var peerActor));Assert.AreEqual(peerLocation,peerActor.Location);
                Assert.AreEqual(PlayableCommandStatus.Applied,w.Domain.Apply(w.Command(1,PlayableCommandKind.Move,new[]{3,peer}),out _));
                var physical=w.Nav.GroupFor(3).Members.Single(m=>m.Entity==3).GoalLocation.Value;
                Assert.NotNull(physical.NameReference.Owner,"physical goal retains its paid name");var name=physical.NameReference;
                var queues=(System.Collections.IDictionary)Field(w.Domain,"tacticalQueues");var pending=Field(queues[3],"Pending");
                var assigned=(NavLocation?)Field(pending,"Assigned");Assert.True(assigned.Value.Equals(physical));Assert.IsNull(assigned.Value.NameReference.Owner);Assert.AreEqual(NavNameRole.None,assigned.Value.NameReference.Role);Assert.Zero(assigned.Value.NameReference.Index);
                Assert.AreEqual(PlayableCommandStatus.Applied,w.Domain.Apply(w.Command(2,PlayableCommandKind.Move,new[]{3,peer},mode:PlayableOrderMode.Append),out _));
                queues=(System.Collections.IDictionary)Field(w.Domain,"tacticalQueues");var a=(System.Collections.IList)Field(queues[3],"Deferred");var b=(System.Collections.IList)Field(queues[peer],"Deferred");Assert.AreSame(a[0],b[0],"shared issuance remains shared");
                Assert.AreSame(name.Owner,physical.NameReference.Owner);Assert.AreEqual(name.Role,physical.NameReference.Role);Assert.AreEqual(name.Index,physical.NameReference.Index);
                byte[] bytes=w.Bytes();w.Cold(bytes);CollectionAssert.AreEqual(bytes,w.Bytes());w.Step(1);w.Bytes();
            }
        }
        [Test] public void SelectorOnlyDomainMutationStillRefusesPreparedCommitAndCannotBeCaptured()
        {
            using(var w=new World()){
                Assert.AreEqual(PlayableCommandStatus.Applied,w.Domain.Apply(w.Command(1,PlayableCommandKind.Move),out _));
                var queues=(System.Collections.IDictionary)Field(w.Domain,"tacticalQueues");var pending=Field(queues[3],"Pending");var original=(NavLocation?)Field(pending,"Assigned");
                var physical=w.Nav.GroupFor(3).Members.Single(m=>m.Entity==3).GoalLocation;Assert.True(original.Equals(physical));Assert.NotNull(physical.Value.NameReference.Owner);
                int hooks=0;w.Domain.AfterTacticalPreparationForQa=payload=>{hooks++;Set(pending,"Assigned",physical);};
                try{
                    Assert.AreEqual(PlayableCommandStatus.Rejected,w.Domain.Apply(w.Command(2,PlayableCommandKind.Move),out _));Assert.AreEqual(1,hooks);Assert.AreEqual(1,((PlayableOrderStamp)Field(Unit(w.Domain,3),"LastOrder")).Sequence);
                    Assert.Throws<InvalidOperationException>(()=>w.Bytes(),"capture must reject rather than strip injected selectors");
                    Assert.Throws<InvalidOperationException>(()=>PlayableDomain.PreparedTacticalCommit.Create(w.Domain),"preparation cannot launder a noncanonical source");
                }finally{Set(pending,"Assigned",original);w.Domain.AfterTacticalPreparationForQa=null;}
                w.Bytes();
            }
        }
        [TestCase(PlayableCommandKind.Move)][TestCase(PlayableCommandKind.AttackMove)][TestCase(PlayableCommandKind.Attack)]
        [TestCase(PlayableCommandKind.Stop)][TestCase(PlayableCommandKind.Hold)][TestCase(PlayableCommandKind.Follow)]
        public void PublicKindsInstallOneConsistentDomainNavResult(PlayableCommandKind kind)
        {
            using(var w=new World()){
                var p=w.Position(3);int enemy=w.Spawn(PlayableOwner.Enemy,new NavPoint(p.X+4,p.Z));int leader=w.Spawn(PlayableOwner.Player,new NavPoint(p.X,p.Z+4));var u=Unit(w.Domain,3);
                Set(u,"Reload",17.25d);Set(u,"BurstRemaining",2);Set(u,"BurstTarget",enemy);Set(u,"BurstDelay",.125d);Set(u,"BurstSequence",7);Set(u,"BurstSize",3);Set(u,"BurstSpread",.25d);
                long publication=w.Nav.Admission.PublicationIdentity;int cancelled=0,preparation=0;string other=UnitState(w.Domain,4),leaderBefore=UnitState(w.Domain,leader);
                w.Nav.AfterStopPreparationForQa=scope=>{preparation++;Assert.Zero(cancelled,"no eager intermediate Stop");Assert.IsNull(Field(u,"LastOrder"));Assert.AreEqual(publication,w.Nav.Admission.PublicationIdentity);};
                w.Nav.MovementCancelled+=id=>{if(id==3){cancelled++;Assert.AreEqual(1,((PlayableOrderStamp)Field(u,"LastOrder")).Sequence);Assert.AreEqual(1,w.Nav.GroupFor(3).CommandSequence);}};
                var cmd=w.Command(1,kind,kind==PlayableCommandKind.Follow?new[]{3,4,leader}:null,kind==PlayableCommandKind.Follow?leader:enemy);
                Assert.AreEqual(PlayableCommandStatus.Applied,w.Domain.Apply(cmd,out _));Assert.AreEqual(publication+1,w.Nav.Admission.PublicationIdentity);Assert.Zero(w.Domain.LastTacticalCommitAllocatedBytesForQa,"validation + write gate allocates nothing");
                var stamp=(PlayableOrderStamp)Field(u,"LastOrder");Assert.AreEqual(1,stamp.Sequence);Assert.AreEqual(1,stamp.Revision);Assert.AreEqual(kind,stamp.Kind);Assert.AreEqual(PlayableOrderOrigin.Unknown,stamp.Origin);Assert.AreEqual(1,stamp.Generation);
                var group=w.Nav.GroupFor(3);Assert.AreEqual(1,group.CommandSequence);Assert.AreEqual(stamp.Revision,group.Members.Single().OrderRevision);
                if(kind==PlayableCommandKind.Move||kind==PlayableCommandKind.AttackMove){
                    var q=w.Domain.TacticalQueueProjection(3,PlayableOwner.Player);Assert.NotNull(q.Pending);Assert.IsNull(q.Active);Assert.AreEqual(1,q.Pending.CommandSequence);Assert.AreEqual(group.GroupId,q.Pending.GroupId);Assert.AreEqual(17.25d,Field(u,"Reload"));Assert.AreEqual(2,Field(u,"BurstRemaining"));Assert.IsNull(Field(u,"CurrentOrder"));
                    var r=w.Nav.PendingRequestFor(3);Assert.AreEqual(group.GroupId,r.MemberIdentity.GroupId);Assert.AreEqual(stamp.Revision,r.MemberIdentity.OrderRevision);
                }else if(kind==PlayableCommandKind.Attack){Assert.AreEqual(1,preparation);Assert.AreEqual(1,cancelled);Assert.NotNull(w.Domain.TacticalQueueProjection(3,PlayableOwner.Player).Active);Assert.AreEqual(enemy,((PlayableTacticalOrderSnapshot)Field(u,"CurrentOrder")).TargetId);Assert.AreEqual(w.Profile.ExplorerBurstPauseMs/1000d,Field(u,"Reload"));}
                else if(kind==PlayableCommandKind.Follow){Assert.AreEqual(1,cancelled);Assert.AreEqual(1,preparation);Assert.AreEqual(leader,((PlayableTacticalOrderSnapshot)Field(u,"CurrentOrder")).TargetId);Assert.AreEqual(other,UnitState(w.Domain,4));Assert.AreEqual(leaderBefore,UnitState(w.Domain,leader));Assert.AreEqual(17.25d,Field(u,"Reload"));}
                else{Assert.AreEqual(1,cancelled);Assert.IsNull(Field(u,"CurrentOrder"));Assert.AreEqual(w.Profile.ExplorerBurstPauseMs/1000d,Field(u,"Reload"));Assert.True(w.Nav.Crowd.TryGet(3,out var actor));Assert.AreEqual(kind==PlayableCommandKind.Hold,actor.Held);}
                Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(w.Nav),w.Nav.PhysicalHostBudget.PhysicalBytes);Assert.Zero(w.Nav.PhysicalHostBudget.HeldBytes);Assert.Zero(w.Nav.PhysicalHostBudget.ActiveScopes);
            }
        }
        [TestCase(false,false)][TestCase(true,false)][TestCase(false,true)][TestCase(true,true)] public void NonemptySolverReadyProjectionAndTransferToEndCommute(bool policy15,bool firstGate)
        {
            using(var w=new World(policy15)){
                Assert.AreEqual(policy15?15:0,w.Profile.Navigation.GroupCorridorMaximumExcessPercent);
                // A real independent ordinary request supplies nonempty work; no fake frame/header or credits.
                var enemy=w.Position(4);Assert.True(w.Nav.Move(4,new NavPoint(enemy.X-3,enemy.Z+3)));w.PumpTo(CertifiedRouteKernel.PumpStage.Transfer);
                int boundaries=0;w.Kernel.PauseAtPhysicalBoundary=()=>++boundaries>1;w.Kernel.Pump(w.Binding(),4);Assert.True(w.At(CertifiedRouteKernel.PumpStage.Transfer));
                var root=w.Nav.PinCommittedHostRoot();try{Assert.Greater(root.Rows.Count,0);Assert.Greater(root.Scheduler.Counters.PendingSubscriptions,0);}finally{root.Release();}
                int visitedSolve=0,visitedReady=0,visitedProjection=0;long beforeWork=w.Kernel.ProgressSequence;
                Action progress=()=>{
                    Assert.True(w.At(CertifiedRouteKernel.PumpStage.Transfer));w.Kernel.PauseAtPhysicalBoundary=()=>{
                        if(w.At(CertifiedRouteKernel.PumpStage.Solve))visitedSolve++;
                        if(w.At(CertifiedRouteKernel.PumpStage.Ready)){var r=w.Nav.PinCommittedHostRoot();try{if(r.Scheduler.Counters.ReadyResults>0)visitedReady++;}finally{r.Release();}}
                        if(w.At(CertifiedRouteKernel.PumpStage.Projection)){var r=w.Nav.PinCommittedHostRoot();try{if(r.Projection.Count>0)visitedProjection++;}finally{r.Release();}}
                        return w.At(CertifiedRouteKernel.PumpStage.EndAllocation);
                    };w.Kernel.Pump(w.Binding(),4);Assert.True(w.At(CertifiedRouteKernel.PumpStage.EndAllocation));
                };
                if(firstGate)w.Nav.AfterMovementObservationForQa=progress;else w.Domain.AfterTacticalPreparationForQa=payload=>progress();
                Assert.AreEqual(PlayableCommandStatus.Applied,w.Domain.Apply(w.Command(1,PlayableCommandKind.Move),out _));Assert.Greater(visitedSolve,0);Assert.Greater(visitedReady,0);Assert.Greater(visitedProjection,0);Assert.Greater(w.Kernel.ProgressSequence,beforeWork,"nonempty physical work progressed");Assert.Zero(w.Domain.LastTacticalCommitAllocatedBytesForQa);
                Assert.NotNull(w.Domain.TacticalQueueProjection(3,PlayableOwner.Player).Pending);Assert.AreEqual(1,((PlayableOrderStamp)Field(Unit(w.Domain,3),"LastOrder")).Sequence);
            }
        }
        [TestCase(PlayableCommandKind.Stop)][TestCase(PlayableCommandKind.Hold)][TestCase(PlayableCommandKind.Attack)][TestCase(PlayableCommandKind.Follow)]
        public void StopBasedKindsCommuteWithSameAllocationHostProgress(PlayableCommandKind kind)
        {
            using(var w=new World()){
                int leader=w.Spawn(PlayableOwner.Player,new NavPoint(w.Position(3).X,w.Position(3).Z+4));int enemy=w.Spawn(PlayableOwner.Enemy,new NavPoint(w.Position(3).X+4,w.Position(3).Z));
                typeof(PlayableDomain).GetMethod("RefreshVision",F).Invoke(w.Domain,null);Assert.True((bool)typeof(PlayableDomain).GetMethod("VisibleTarget",F).Invoke(w.Domain,new object[]{PlayableOwner.Player,enemy}));
                w.PumpTo(CertifiedRouteKernel.PumpStage.Transfer);int reached=0;
                w.Domain.AfterTacticalPreparationForQa=payload=>{reached++;w.PumpTo(CertifiedRouteKernel.PumpStage.EndAllocation);};
                Assert.AreEqual(PlayableCommandStatus.Applied,w.Domain.Apply(w.Command(1,kind,target:kind==PlayableCommandKind.Follow?leader:enemy),out _));
                Assert.AreEqual(1,reached,"intended preparation/host hook must execute");Assert.AreEqual(1,((PlayableOrderStamp)Field(Unit(w.Domain,3),"LastOrder")).Sequence);Assert.AreEqual(1,w.Nav.GroupFor(3).CommandSequence);Assert.Zero(w.Domain.LastTacticalCommitAllocatedBytesForQa);
            }
        }
        [Test] public void FinalRefusalPreservesEveryBehaviorIncludingReloadAndLayouts()
        {
            using(var w=new World()){
                Assert.AreEqual(PlayableCommandStatus.Applied,w.Domain.Apply(w.Command(1,PlayableCommandKind.Move),out _));Assert.AreEqual(PlayableCommandStatus.Applied,w.Domain.Apply(w.Command(2,PlayableCommandKind.Hold),out _));
                var u=Unit(w.Domain,3);Set(u,"Reload",17.25d);Set(u,"BurstRemaining",2);Set(u,"BurstTarget",4);Set(u,"BurstDelay",.125d);Set(u,"BurstSequence",7);Set(u,"BurstSize",3);Set(u,"BurstSpread",.25d);
                string before=UnitState(w.Domain,3);byte[] bytes=w.Bytes();int hooks=0;PlayableDomain.PreparedTacticalCommit observed=null;
                w.Domain.AfterTacticalPreparationForQa=payload=>{hooks++;observed=payload;((NavigationRetentionBudget.Scope)Field(w.Nav,"preparedCommandScope")).RequestCancel();};
                Assert.AreEqual(PlayableCommandStatus.Rejected,w.Domain.Apply(w.Command(3,PlayableCommandKind.Move),out _));Assert.AreEqual(1,hooks);Assert.NotNull(observed.Refusal);Assert.AreEqual(NavigationCommandRefused.Cause.Cancelled,observed.Refusal.Reason);Assert.AreEqual(before,UnitState(w.Domain,3));CollectionAssert.AreEqual(bytes,w.Bytes());Assert.Zero(w.Nav.PhysicalHostBudget.HeldBytes);Assert.Zero(w.Nav.PhysicalHostBudget.ActiveScopes);
            }
        }
        [TestCase("profile")][TestCase("held")][TestCase("team")][TestCase("position")][TestCase("geometry")][TestCase("attachment")][TestCase("terrain-names")]
        [TestCase("budget")][TestCase("map")][TestCase("operation")][TestCase("authority")][TestCase("mailbox")][TestCase("installed")][TestCase("quantum")][TestCase("token")][TestCase("context")][TestCase("publication")]
        public void RealReadsetDifferencesRejectBeforeAnyDomainWrite(string family)
        {
            using(var w=new World()){
                Assert.AreEqual(PlayableCommandStatus.Applied,w.Domain.Apply(w.Command(1,PlayableCommandKind.Hold),out _));string before=UnitState(w.Domain,3);var oldGroup=w.Nav.GroupFor(3);Action restore=null;NavigationHostRoot current=null;
                if(family=="quantum"||family=="token"){w.PumpTo(CertifiedRouteKernel.PumpStage.Transfer);}
                w.Domain.AfterTacticalPreparationForQa=payload=>{
                    var c=w.Nav.PreparedTacticalCandidate;
                    if(family=="profile"){restore=w.ChangeOwnedProfile();}
                    if(family=="terrain-names"){var old=Field(w.Nav,"terrainNameOwners");Set(w.Nav,"terrainNameOwners",null);restore=()=>Set(w.Nav,"terrainNameOwners",old);}
                    if(family=="held"){w.Nav.Crowd.TryGet(4,out var a);bool old=a.Held;a.Held=!old;restore=()=>a.Held=old;}
                    if(family=="team"){w.Nav.Crowd.TryGet(4,out var a);int old=a.Team;a.Team=997;restore=()=>a.Team=old;}
                    if(family=="position"){w.Nav.Crowd.TryGet(3,out var a);var old=a.Position;a.Position=new NavPoint(old.X+.5,old.Z);restore=()=>a.Position=old;}
                    if(family=="geometry"){var old=Field(w.Nav,"geometry");Set(w.Nav,"geometry",new NavGeometry(w.Profile.ArenaHalfExtent,Array.Empty<NavObstacle>(),997));restore=()=>Set(w.Nav,"geometry",old);}
                    if(family=="attachment"){long old=(long)Field(w.Nav,"certifiedHostAttachmentIdentity");Set(w.Nav,"certifiedHostAttachmentIdentity",old+1);restore=()=>Set(w.Nav,"certifiedHostAttachmentIdentity",old);}
                    if(family=="budget")c.Budget=new NavigationRetentionBudget();
                    if(family=="map")c.OldGroups=new Dictionary<long,GroupOrderState>();
                    if(family=="operation"){long old=(long)Field(w.Nav,"authorityOperationIdentity");Set(w.Nav,"authorityOperationIdentity",old+1);restore=()=>Set(w.Nav,"authorityOperationIdentity",old);}
                    if(family=="authority")w.Nav.SetFormationContact(3,false,77);
                    if(family=="mailbox")c.MailboxRevision++;
                    if(family=="installed")c.InstalledDetached[0]=!c.InstalledDetached[0];
                    if(family=="context"){var ctx=c.CommandContext;long old=ctx.CommandSequence;ctx.CommandSequence=999;restore=()=>ctx.CommandSequence=old;}
                    if(family=="publication"){long old=(long)Field(w.Nav,"admissionPublicationIdentity");Set(w.Nav,"admissionPublicationIdentity",old+1);restore=()=>Set(w.Nav,"admissionPublicationIdentity",old);}
                    if(family=="quantum"){w.PumpTo(CertifiedRouteKernel.PumpStage.Solve);current=w.Nav.PinCommittedHostRoot();var f=typeof(NavigationHostRoot).GetField("<QuantumTick>k__BackingField",F);object old=f.GetValue(current);f.SetValue(current,(long)old+1);restore=()=>f.SetValue(current,old);}
                    if(family=="token"){int old=(int)Field(w.Nav,"hostAllocationThread");Set(w.Nav,"hostAllocationThread",old+1);restore=()=>Set(w.Nav,"hostAllocationThread",old);}
                };
                try{Assert.AreEqual(PlayableCommandStatus.Rejected,w.Domain.Apply(w.Command(2,PlayableCommandKind.Move),out _),family);Assert.AreEqual(before,UnitState(w.Domain,3),family);Assert.AreEqual(oldGroup.GroupId,w.Nav.GroupFor(3).GroupId);if(family=="authority")Assert.AreEqual(77,w.Nav.GroupFor(3).Members.Single().ContactTick);Assert.True(w.Nav.Crowd.TryGet(3,out var actor));if(family!="position")Assert.True(actor.Held);}
                finally{restore?.Invoke();current?.Release();}
            }
        }
        [TestCase("held")][TestCase("team")][TestCase("profile")][TestCase("context")]
        public void FirstStagingGuardRejectsChangedPreparedInputs(string family)
        {
            using(var w=new World()){
                string before=UnitState(w.Domain,3);Action restore=null;int hooks=0;
                w.Nav.AfterMovementObservationForQa=()=>{
                    hooks++;
                    if(family=="held"){w.Nav.Crowd.TryGet(4,out var a);bool old=a.Held;a.Held=!old;restore=()=>a.Held=old;}
                    if(family=="team"){w.Nav.Crowd.TryGet(4,out var a);int old=a.Team;a.Team=999;restore=()=>a.Team=old;}
                    if(family=="profile"){restore=w.ChangeOwnedProfile();}
                    if(family=="context"){var context=(GroupOrderState)Field(w.Nav,"commandGroup");context.TargetId=999;}
                };
                try{Assert.AreEqual(PlayableCommandStatus.Overflow,w.Domain.Apply(w.Command(1,PlayableCommandKind.Move),out _));Assert.AreEqual(1,hooks);Assert.AreEqual(before,UnitState(w.Domain,3));Assert.IsNull(w.Nav.GroupFor(3));}finally{restore?.Invoke();}
            }
        }
        [Test] public void ProjectionPartialPinFailureReleasesGeometryBeforePropagatingInvariant()
        {
            using(var w=new World()){
                var geometry=(NavGeometry)Field(w.Nav,"navigationGeometry");int references=geometry.StorageRoot.References;var old=Field(w.Nav,"profile");long bytes=w.Nav.PhysicalHostBudget.PhysicalBytes;
                Assert.True(w.Nav.PhysicalHostBudget.TryOpenScope(NavigationPreparationKind.Admission,w.Nav.Generation,w.Nav.Admission.PublicationIdentity,out var scope));
                try{
                    Set(w.Nav,"profile",PlayableProfile.Default.Navigation); // Deliberately invalid input tests partial-pin exception safety, not a readset refusal.
                    var error=Assert.Throws<TargetInvocationException>(()=>typeof(NavigationSession).GetMethod("PrepareAdmissionProjection",F).Invoke(w.Nav,new object[]{Field(w.Nav,"groups"),Field(w.Nav,"pending"),Field(w.Nav,"requestSequence"),w.Nav.Admission.PublicationIdentity,Field(w.Nav,"authorityOperationIdentity"),scope,Field(w.Nav,"admissionOwner"),null,null,null,null,0}));
                    Assert.IsInstanceOf<ArgumentException>(error.InnerException);Assert.AreEqual(references,geometry.StorageRoot.References);Assert.AreEqual(bytes,w.Nav.PhysicalHostBudget.PhysicalBytes);
                }finally{Set(w.Nav,"profile",old);scope.Dispose();}
                Assert.Zero(w.Nav.PhysicalHostBudget.HeldBytes);Assert.Zero(w.Nav.PhysicalHostBudget.ActiveScopes);
            }
        }
        [TestCase("staging")][TestCase("final")]
        public void InvariantFailureIsNeverReportedAsExpectedRefusal(string gate)
        {
            using(var w=new World()){
                var marker=new InvalidOperationException("unexpected-tactical-invariant");NavigationRetentionBudget.Scope abandoned=null;
                if(gate=="staging")w.Nav.AfterMovementObservationForQa=()=>w.Nav.PhysicalHostBudget.AllocationAdmission=_=>throw marker;
                else w.Domain.AfterTacticalPreparationForQa=payload=>{abandoned=(NavigationRetentionBudget.Scope)Field(w.Nav,"preparedCommandScope");abandoned.RelinquishOwner();};
                try{
                    var error=Assert.Throws<InvalidOperationException>(()=>w.Domain.Apply(w.Command(1,PlayableCommandKind.Move),out _));
                    if(gate=="staging")Assert.AreSame(marker,error);else{Assert.AreEqual("Navigation preparation belongs to another owner thread.",error.Message);Assert.IsNotNull(error.Data["TacticalSecondaryFailures"]);}
                    Assert.IsNull(Field(Unit(w.Domain,3),"LastOrder"));Assert.IsNull(w.Nav.GroupFor(3));
                }finally{w.Nav.PhysicalHostBudget.AllocationAdmission=null;if(abandoned!=null&&!abandoned.IsResolved){abandoned.AcquireOwner();abandoned.Dispose();}}
            }
        }
        [Test] public void ActualPaidCapacityRefusalKeepsOldCommand()
        {
            using(var w=new World()){
                Assert.AreEqual(PlayableCommandStatus.Applied,w.Domain.Apply(w.Command(1,PlayableCommandKind.Hold),out _));string before=UnitState(w.Domain,3);var b=w.Nav.PhysicalHostBudget;Assert.True(b.TryOpenScope(NavigationPreparationKind.Solve,1,w.Nav.Admission.PublicationIdentity,out var pressure));
                try{long room=LayeredNavigationScheduler.MaxRetainedBytes-b.RetainedBytes;Assert.True(pressure.TryReserve(room));Assert.AreEqual(LayeredNavigationScheduler.MaxRetainedBytes,b.RetainedBytes);Assert.AreEqual(PlayableCommandStatus.Overflow,w.Domain.Apply(w.Command(2,PlayableCommandKind.Move),out _));Assert.AreEqual(before,UnitState(w.Domain,3));}finally{pressure.Dispose();}
            }
        }
        [Test] public void IndependentDomainMutationIsNotRolledBackOnRefusal()
        {
            using(var w=new World()){
                var u=Unit(w.Domain,3);w.Domain.AfterTacticalPreparationForQa=payload=>Set(u,"Reload",37.5d);
                Assert.AreEqual(PlayableCommandStatus.Rejected,w.Domain.Apply(w.Command(1,PlayableCommandKind.Move),out _));Assert.AreEqual(37.5d,Field(u,"Reload"));Assert.IsNull(Field(u,"LastOrder"));Assert.IsNull(w.Nav.GroupFor(3));
            }
        }
        [Test] public void AcceptedDeferredTailSurvivesRefusedActivationAndLaterCommits()
        {
            using(var w=new World()){
                int callbacks=0;w.Domain.AfterTacticalPreparationForQa=payload=>{callbacks++;if(callbacks==2)((NavigationRetentionBudget.Scope)Field(w.Nav,"preparedCommandScope")).RequestCancel();};
                Assert.AreEqual(PlayableCommandStatus.Applied,w.Domain.Apply(w.Command(1,PlayableCommandKind.Move,mode:PlayableOrderMode.Append),out _));
                var q=w.Domain.TacticalQueueProjection(3,PlayableOwner.Player);Assert.IsNull(q.Active);Assert.IsNull(q.Pending);Assert.AreEqual(1,q.Deferred.Count);Assert.IsNull(Field(Unit(w.Domain,3),"LastOrder"));Assert.IsNull(w.Nav.GroupFor(3));
                w.Domain.AfterTacticalPreparationForQa=null;typeof(PlayableDomain).GetMethod("ActivateDeferredOrders",F).Invoke(w.Domain,null);q=w.Domain.TacticalQueueProjection(3,PlayableOwner.Player);Assert.IsEmpty(q.Deferred);Assert.NotNull(q.Pending);Assert.AreEqual(1,q.Pending.CommandSequence);var stamp=(PlayableOrderStamp)Field(Unit(w.Domain,3),"LastOrder");Assert.AreEqual(q.Pending.ActivationRevision,stamp.Revision);Assert.AreEqual(stamp.Revision,w.Nav.PendingRequestFor(3).MemberIdentity.OrderRevision);Assert.Zero(w.Nav.PhysicalHostBudget.ActiveScopes);
            }
        }
        [Test] public void StaggeredRecipientKeepsSharedIssuanceAndCurrentRestore()
        {
            using(var w=new World()){
                int peer=w.Spawn(PlayableOwner.Player,new NavPoint(-22,-14));
                Assert.AreEqual(PlayableCommandStatus.Applied,w.Domain.Apply(w.Command(1,PlayableCommandKind.Move),out _));
                Assert.AreEqual(PlayableCommandStatus.Applied,w.Domain.Apply(w.Command(2,PlayableCommandKind.Move,new[]{3,peer},goal:new NavPoint(-10,-12),mode:PlayableOrderMode.Append),out _));
                void Shared(){var qa=w.Domain.TacticalQueueProjection(3,PlayableOwner.Player);var qb=w.Domain.TacticalQueueProjection(peer,PlayableOwner.Player);Assert.AreEqual(1,qa.Pending.CommandSequence);Assert.AreEqual(2,qa.Deferred.Single().CommandSequence);Assert.AreEqual(2,qb.Pending.CommandSequence);Assert.Greater(qb.Pending.GroupId,0);Assert.AreEqual(qb.Pending.GroupId,qa.Deferred.Single().GroupId);var map=Field(w.Domain,"tacticalQueues");var deferred=((IList)Field(Item(map,3),"Deferred"))[0];var pending=Field(Field(Item(map,peer),"Pending"),"Issuance");Assert.AreSame(deferred,pending,"shared mutable issuance across deferred and activated recipients");}
                Shared();var prefix=w.Bytes();w.Cold(prefix);CollectionAssert.AreEqual(prefix,w.Bytes());Shared();
            }
        }
        [Test] public void CurrentWorldWithSlotHolesHasExactColdContinuation()
        {
            using(var w=new World()){
                Assert.AreEqual(PlayableCommandStatus.Applied,w.Domain.Apply(w.Command(1,PlayableCommandKind.Move),out _));Assert.AreEqual(PlayableCommandStatus.Applied,w.Domain.Apply(w.Command(2,PlayableCommandKind.Hold),out _));Assert.AreEqual(PlayableCommandStatus.Applied,w.Domain.Apply(w.Command(3,PlayableCommandKind.Move),out _));
                var prefix=w.Bytes();w.Step(12);var suffix=w.Bytes();w.Step(12);var next=w.Bytes();w.Cold(prefix);CollectionAssert.AreEqual(prefix,w.Bytes());w.Step(12);CollectionAssert.AreEqual(suffix,w.Bytes());w.Step(12);CollectionAssert.AreEqual(next,w.Bytes());
            }
        }
        private static bool Wait(Func<bool> condition,int milliseconds=10000){var watch=Stopwatch.StartNew();while(watch.ElapsedMilliseconds<milliseconds){if(condition())return true;Thread.Sleep(1);}return condition();}
        [TestCase("observer")][TestCase("resolve")][TestCase("cleanup")][TestCase("terminal")]
        public void PostcommitFailureRetainsAppliedReceiptOriginalErrorAndOwners(string fault)
        {
            var runtime=new PlayableRuntime(PlayableProfile.Default,1,7,autonomousOwnerAi:false,autonomousEnemyAi:false,humanControlledPlayer:true,startPaused:true);NavigationSession nav=null;NavigationRetentionBudget.Scope scope=null;int notifications=0;Action restoreOwner=null;string marker="postcommit-"+fault;Exception primary=null;
            try{
                Assert.True(Wait(()=>runtime.IsPrepared&&runtime.Latest.Paused));var d=(PlayableDomain)Field(runtime,"domain");nav=d.Navigation;
                d.AfterTacticalPreparationForQa=payload=>scope=(NavigationRetentionBudget.Scope)Field(nav,"preparedCommandScope");
                if(fault=="observer")nav.MovementCancelled+=id=>{notifications++;throw new InvalidOperationException(marker);};
                if(fault=="resolve"){nav.BeforeTacticalResolveForQa=()=>scope.RelinquishOwner();restoreOwner=()=>{nav.HostChanged-=restoreOwner;notifications++;scope.AcquireOwner();};nav.HostChanged+=restoreOwner;marker="Navigation preparation belongs to another owner thread.";}
                if(fault=="cleanup")nav.AfterTacticalCleanupForQa=()=>{notifications++;throw new InvalidOperationException(marker);};
                if(fault=="terminal"){nav.MovementCancelled+=id=>throw new InvalidOperationException(marker);restoreOwner=()=>{notifications++;throw new InvalidOperationException("terminal-secondary");};nav.AfterTacticalCleanupForQa=()=>nav.HostChanged+=restoreOwner;}
                runtime.RequestPause(false);Assert.True(Wait(()=>!runtime.Latest.Paused));Assert.True(runtime.TrySubmit(new PlayableCommand(1,1,PlayableDomain.PlayerId,PlayableCommandKind.Hold,new[]{3}).AsHuman()).Accepted);
                Assert.True(Wait(()=>runtime.Latest.Failure!=null));Assert.True(runtime.Latest.Failure.Contains(marker),runtime.Latest.Failure);if(fault=="terminal")Assert.True(runtime.Latest.Failure.Contains("terminal-secondary"),runtime.Latest.Failure);var receipt=runtime.ReceiptsAfter(0).Single(r=>r.Sequence==1);Assert.AreEqual(PlayableCommandStatus.Applied,receipt.Status);Assert.AreEqual(1,notifications);Assert.AreEqual(1,((PlayableOrderStamp)Field(Unit(d,3),"LastOrder")).Sequence);Assert.True(nav.Crowd.TryGet(3,out var actor));Assert.True(actor.Held);
            }catch(Exception error){primary=error;throw;}
            finally{
                Exception cleanup=null;void Attempt(Action action){try{action();}catch(Exception error){if(cleanup==null)cleanup=error;else PlayableDomain.PreserveTacticalSecondary(cleanup,error);}}
                Attempt(()=>{if(nav!=null&&restoreOwner!=null)nav.HostChanged-=restoreOwner;});
                Attempt(()=>{runtime.RequestStop();Assert.True(Wait(()=>runtime.IsStopped));});
                if(runtime.IsStopped&&nav!=null)Attempt(()=>{nav.AfterTacticalPublishForQa=null;nav.BeforeTacticalResolveForQa=null;nav.AfterTacticalCleanupForQa=null;nav.LastGroupGuardCandidateForQa=null;nav.ReleaseCommittedHostRoot();((PlayableDomain)Field(runtime,"domain")).DestroyOwnedNavigation();Assert.Zero(nav.PhysicalHostBudget.PhysicalBytes);Assert.Zero(nav.PhysicalHostBudget.HeldBytes);Assert.Zero(nav.PhysicalHostBudget.ActiveScopes);});
                Attempt(runtime.Dispose);
                if(cleanup!=null){TestContext.Error.WriteLine("Independent cleanup failure: "+PlayableDomain.TacticalFailureText(cleanup));if(primary==null)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(cleanup).Throw();else PlayableDomain.PreserveTacticalSecondary(primary,cleanup);}
            }
        }
    }
}
