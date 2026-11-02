using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using Spacewars.Runtime;
using Spacewars.Simulation;

// Native Air mechanical source qualification. No AI policy/permit is supplied.
internal sealed class AirMotionChecks : IAirMotionAuthority,IDisposable
{
    private const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    private readonly PlayableDomain domain;
    private PlayableDomain.AirMotionPin latest,pending,resume;
    private bool defer,refuse,replay,waitBeforeAcceptance,unavailableGuard,unexpectedGuard,keepGuardPending;
    private int prepared,committed,rows;
    private long maxInstallBytes;
    private static void Require(bool value,string message){if(!value)throw new Exception(message);}
    private static object Call(PlayableDomain d,string name,params object[] args)
    {try{return typeof(PlayableDomain).GetMethod(name,Hidden).Invoke(d,args);}catch(TargetInvocationException error){throw error.InnerException;}}
    private static PlayableSnapshot View(PlayableDomain d)=>d.Snapshot(1,RuntimeStatus.Running,false,null,null,7);
    private AirMotionChecks(PlayableDomain d){domain=d;d.BindAirMotionAuthority(this);}
    public NavigationMotionAdoption Prepare(PlayableDomain.AirMotionPublication publication)
    {
        prepared++;Require(publication.Count==View(domain).Entities.Count(e=>e.Kind==PlayableEntityKind.Hawk),"incomplete receiver set");
        for(int i=0;i<publication.Count;i++){var row=publication[i];var before=View(domain).Entities.Single(e=>e.Id==row.Entity);Require(before.Position.Equals(row.Before)&&before.Altitude==row.BeforeHeight,"live aircraft changed during preparation");CheckProjection(row.BeforeActor,CaptureActor(domain,row.Entity),"prepared before");Require(row.AfterActor.Motion.Entity==row.Entity&&row.AfterActor.Motion.Position.Equals(row.Final)&&row.AfterActor.Height==row.AssignedHeight,"after projection is not the actual final row");rows++;}
        if(waitBeforeAcceptance){waitBeforeAcceptance=false;return null;}
        if(refuse){refuse=false;throw new InvalidOperationException("intentional complete-Air refusal");}
        if(replay){publication.SelectSource(latest);throw new Exception("committed air source was replayed");}
        var pin=resume;
        if(pin!=null){Require(publication.Matches(pin),"pending full input/output mismatch");publication.SelectSource(pin);resume=null;}else pin=publication.AcceptSource();
        if(defer){defer=false;pending=pin;return null;}
        return new Adoption(this,publication,pin);
    }
    private sealed class Adoption : NavigationMotionAdoption
    {
        private readonly AirMotionChecks owner;private readonly PlayableDomain.AirMotionPublication publication;private readonly PlayableDomain.AirMotionPin pin;private readonly NavUnit[] actors;private long allocated;
        internal Adoption(AirMotionChecks o,PlayableDomain.AirMotionPublication p,PlayableDomain.AirMotionPin source){owner=o;publication=p;pin=source;actors=Enumerable.Range(0,p.Count).Select(i=>Actor(o.domain,p[i].Entity)).ToArray();}
        internal override void ValidateCurrent()
        {
            bool blocked=false;try{owner.domain.Step(1d/30);}catch(InvalidOperationException){blocked=true;}Require(blocked,"air callback reentered Domain clock");
            blocked=false;try{owner.domain.CaptureWorldBytes(7,"air-current");}catch(InvalidOperationException){blocked=true;}Require(blocked,"air callback reentered capture");
            for(int i=0;i<publication.Count;i++){var row=publication[i];Require(View(owner.domain).Entities.Single(e=>e.Id==row.Entity).Position.Equals(row.Before),"first Air write preceded final guard");}
            if(owner.unavailableGuard){owner.unavailableGuard=false;owner.keepGuardPending=true;throw new PlayableDomain.AirMotionUnavailableException("intentional expected Air guard refusal");}
            if(owner.unexpectedGuard){owner.unexpectedGuard=false;throw new InvalidOperationException("intentional unexpected Air guard fault");}
            allocated=GC.GetAllocatedBytesForCurrentThread();
        }
        internal override void ApplyCommittedNoThrow()
        {
            owner.maxInstallBytes=Math.Max(owner.maxInstallBytes,GC.GetAllocatedBytesForCurrentThread()-allocated);
            if(!publication.Committed)throw new Exception("missing factual committed air publication");
            for(int i=0;i<publication.Count;i++){var row=publication[i];var actual=actors[i];if(!actual.Position.Equals(row.Final))throw new Exception("incomplete final-XZ/assigned-height install");}
            for(int i=0;i<publication.Count;i++)CheckProjection(publication[i].AfterActor,CaptureActor(owner.domain,publication[i].Entity),"committed after");
            owner.committed++;
        }
        public override void Dispose()
        {
            if(!publication.Committed){if(owner.keepGuardPending){owner.keepGuardPending=false;owner.pending=pin;}else Retire(owner.domain,pin);return;}
            for(int i=0;i<publication.Count;i++){var row=publication[i];Require(View(owner.domain).Entities.Single(e=>e.Id==row.Entity).Altitude==row.AssignedHeight,"assigned height was not installed");}
            if(owner.latest!=null)Retire(owner.domain,owner.latest);owner.latest=pin;
        }
    }
    private static void Retire(PlayableDomain d,PlayableDomain.AirMotionPin pin)
    {Require(d.TryRetireAirMotionSource(pin),"air retirement capacity refusal");pin.Dispose();}
    public void Dispose(){if(latest!=null){Retire(domain,latest);latest=null;}if(pending!=null){Retire(domain,pending);pending=null;}}
    private static void Close(PlayableDomain d)
    {if(d==null)return;var b=d.Navigation.PhysicalHostBudget;d.Navigation.ReleaseCommittedHostRoot();d.DestroyOwnedNavigation();Require(b==null||b.PhysicalBytes==0&&b.HeldBytes==0&&b.ActiveScopes==0,"air source lifetime leak");}
    private static object Unit(PlayableDomain d,int id)=>((IEnumerable)typeof(PlayableDomain).GetField("units",Hidden).GetValue(d)).Cast<object>().Select(x=>x.GetType().GetProperty("Value").GetValue(x)).Single(x=>(int)x.GetType().GetField("Id").GetValue(x)==id);
    private static object Air(PlayableDomain d,int id){var u=Unit(d,id);return u.GetType().GetField("Air").GetValue(u);}
    private static NavUnit Actor(PlayableDomain d,int id){var a=Air(d,id);return (NavUnit)a.GetType().GetField("Actor").GetValue(a);}
    private static int Spawn(PlayableDomain d,NavPoint point)=>(int)Call(d,"SpawnUnit",point,PlayableOwner.Player,PlayableEntityKind.Hawk);
    private static void Move(PlayableDomain d,long sequence,int id,NavPoint point,PlayableOrderMode mode=PlayableOrderMode.Replace)
    {Require(d.Apply(new PlayableCommand(905,sequence,"player-1",PlayableCommandKind.Move,new[]{id},point,mode:mode),out _)==PlayableCommandStatus.Applied,"air move setup refused");}
    private static void SetAir(PlayableDomain d,int id,string field,object value){var a=Air(d,id);a.GetType().GetField(field).SetValue(a,value);}
    private static object Field(object value,string field)=>value.GetType().GetField(field).GetValue(value);
    private static long Revision(PlayableDomain d)=>(long)typeof(PlayableDomain).GetField("nextOrderRevision",Hidden).GetValue(d);
    private static PlayableDomain.AirMotionActorState CaptureActor(PlayableDomain d,int id)
    {var u=Unit(d,id);return new PlayableDomain.AirMotionActorState(new NavMotionActorState(Actor(d,id)),(double)Field(Air(d,id),"Height"),(bool)Field(Air(d,id),"Departing"),(PlayableTacticalOrderSnapshot)Field(u,"CurrentOrder"),(PlayableOrderStamp)Field(u,"LastOrder"));}
    private static void CheckProjection(PlayableDomain.AirMotionActorState actual,PlayableDomain.AirMotionActorState expected,string where)
    {
        foreach(var f in typeof(NavMotionActorState).GetFields(BindingFlags.Instance|BindingFlags.NonPublic))Require(Equals(f.GetValue(actual.Motion),f.GetValue(expected.Motion)),where+" motion "+f.Name);
        Require(actual.Height==expected.Height&&actual.Departing==expected.Departing,where+" height/departure");
        Require((actual.CurrentOrder==null)==(expected.CurrentOrder==null)&&(actual.CommandStamp==null)==(expected.CommandStamp==null),where+" order/stamp presence");
        if(actual.CurrentOrder!=null)foreach(var f in typeof(PlayableTacticalOrderSnapshot).GetProperties())Require(Equals(f.GetValue(actual.CurrentOrder),f.GetValue(expected.CurrentOrder)),where+" order "+f.Name);
        if(actual.CommandStamp!=null)foreach(var f in typeof(PlayableOrderStamp).GetFields())Require(Equals(f.GetValue(actual.CommandStamp),f.GetValue(expected.CommandStamp)),where+" stamp "+f.Name);
    }
    private static byte[] AirLawBytes(PlayableDomain d)
    {
        using(var stream=new MemoryStream())using(var writer=new BinaryWriter(stream)){
            writer.Write(Revision(d));foreach(var entity in View(d).Entities.Where(e=>e.Kind==PlayableEntityKind.Hawk).OrderBy(e=>e.Id)){
                var u=Unit(d,entity.Id);typeof(PlayableDomain).GetMethods(BindingFlags.Static|BindingFlags.NonPublic).Single(m=>m.Name=="WriteRecord"&&m.GetParameters()[1].ParameterType==u.GetType()).Invoke(null,new[]{(object)writer,u});
                typeof(PlayableDomain).GetMethod("WriteAirDefault",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new[]{(object)writer,u});
            }writer.Flush();return stream.ToArray();}
    }
    private static void CheckStepPending()
    {
        PlayableDomain d=null;AirMotionChecks audit=null;
        try{d=new PlayableDomain(PlayableProfile.Default,905,false);int id=Spawn(d,new NavPoint(-16,16));Move(d,1,id,new NavPoint(10,16));audit=new AirMotionChecks(d);var before=CaptureActor(d,id);long revision=Revision(d),tick=d.Tick;
            audit.defer=true;d.Step(1d/30);Require(d.Tick==tick+1&&audit.pending!=null&&audit.committed==0,"ordinary Domain.Step did not tolerate expected paid pending");CheckProjection(CaptureActor(d,id),before,"Domain.Step pending");Require(Revision(d)==revision,"pending advanced order allocator");
            bool stale=false;try{d.ResumeAirMotion(audit.pending);}catch(InvalidOperationException){stale=true;}Require(stale,"later Domain point resumed stale input source");Retire(d,audit.pending);audit.pending=null;Call(d,"AdvanceHawks",1d/30);Require(audit.committed==1,"fresh Air step did not recover after stale cancellation");
            Console.WriteLine("AIR actual Domain.Step expected pending/no-Air-write + stale source cancellation/retry PASS");
        }finally{audit?.Dispose();Close(d);}
    }
    private static void CheckDiscreteLaws()
    {
        var p=PlayableProfile.Default;PlayableDomain d=null;AirMotionChecks audit=null;
        try{
            d=new PlayableDomain(p,905,false);int followerEarly=Spawn(d,new NavPoint(-20,-10)),leaderLater=Spawn(d,new NavPoint(-5,-10)),leaderEarly=Spawn(d,new NavPoint(-20,15)),followerLater=Spawn(d,new NavPoint(-5,15));
            Move(d,1,leaderLater,new NavPoint(5,-10));Move(d,2,leaderEarly,new NavPoint(-10,15));
            Require(d.Apply(new PlayableCommand(905,3,"player-1",PlayableCommandKind.Follow,new[]{followerEarly},targetId:leaderLater),out _)==PlayableCommandStatus.Applied,"later leader Follow setup");
            Require(d.Apply(new PlayableCommand(905,4,"player-1",PlayableCommandKind.Follow,new[]{followerLater},targetId:leaderEarly),out _)==PlayableCommandStatus.Applied,"earlier leader Follow setup");
            var oldLater=Actor(d,leaderLater);var laterGoal=PlayableUnitRules.FollowGoal(p,oldLater.Position,oldLater.Heading);audit=new AirMotionChecks(d);Call(d,"AdvanceHawks",1d/30);
            Require(Actor(d,followerEarly).Goal.Equals(laterGoal),"early follower observed future later peer");
            Require(Actor(d,followerLater).Goal.Equals(PlayableUnitRules.FollowGoal(p,Actor(d,leaderEarly).Position,Actor(d,leaderEarly).Heading)),"later follower missed earlier updated peer");
            audit.Dispose();audit=null;Close(d);d=null;
            d=new PlayableDomain(p,905,false);double spacing=2*p.HawkRadius+p.HawkSeparation;int moving=Spawn(d,new NavPoint(-16,16)),peer=Spawn(d,new NavPoint(-16+spacing+.0001,16));Move(d,1,moving,new NavPoint(5,16));
            Require(d.Apply(new PlayableCommand(905,2,"player-1",PlayableCommandKind.Hold,new[]{peer}),out _)==PlayableCommandStatus.Applied,"peer Hold setup");
            SetAir(d,moving,"Velocity",new NavPoint(p.HawkSpeed,0));double startHeight=(double)Field(Air(d,moving),"Height")+5;SetAir(d,moving,"Height",startHeight);audit=new AirMotionChecks(d);Call(d,"AdvanceHawks",1d/30);
            var lateral=audit.latest[0];Require(lateral.HeightAssigned&&!lateral.Final.Equals(lateral.Nominal)&&!lateral.Final.Equals(lateral.Before),"fixture did not select actual lateral fallback");
            Require(lateral.AssignedHeight==startHeight-p.HawkDescentSpeed/30,"height assignment changed discrete descent law");
            Require(lateral.NominalClearance==(double)Call(d,"ClearHeight",lateral.Nominal),"nominal nextClear provenance lost");
            double sign=(moving&1)==0?1:-1;var actual=Actor(d,moving);Require(lateral.Final.Equals(new NavPoint(lateral.Before.X-sign*Math.Sin(actual.Heading)*p.HawkStrafeSpeed/30,lateral.Before.Z+sign*Math.Cos(actual.Heading)*p.HawkStrafeSpeed/30)),"lateral finalXZ law changed");
            audit.Dispose();audit=null;Close(d);d=null;
            d=new PlayableDomain(p,905,false);int deferred=Spawn(d,new NavPoint(-16,16));Move(d,1,deferred,new NavPoint(-16,16));Move(d,2,deferred,new NavPoint(-12,16),PlayableOrderMode.Append);var u=Unit(d,deferred);u.GetType().GetField("BurstRemaining").SetValue(u,2);u.GetType().GetField("BurstTarget").SetValue(u,17);u.GetType().GetField("StoppedSeconds").SetValue(u,9d);var defaultField=u.GetType().GetField("DefaultCombat");defaultField.SetValue(u,Activator.CreateInstance(defaultField.FieldType,true));long revision=Revision(d);audit=new AirMotionChecks(d);Call(d,"AdvanceHawks",1d/30);
            Require(Revision(d)==revision+1&&((PlayableOrderStamp)Field(u,"LastOrder")).Sequence==2,"deferred order revision/stamp was not atomically installed");
            Require(audit.latest[0].BeforeActor.CommandStamp.Sequence==1&&audit.latest[0].AfterActor.CommandStamp.Sequence==2&&audit.latest[0].AfterActor.CurrentOrder.CommandSequence==2,"deferred projection lost before/after command identity");
            Require((int)Field(u,"BurstRemaining")==0&&(int)Field(u,"BurstTarget")==0&&(double)Field(u,"StoppedSeconds")==0&&(double)Field(u,"Reload")==p.HawkReloadSeconds&&Field(u,"DefaultCombat")==null,"deferred order non-AirState side effects missing");
            Require(((ICollection)Field(Air(d,deferred),"Deferred")).Count==0,"deferred queue did not advance");
            audit.Dispose();audit=null;Close(d);d=null;
            d=new PlayableDomain(p,905,false);int departing=Spawn(d,new NavPoint(-16,16)),yielding=Spawn(d,new NavPoint(5,16));double safe=(double)Call(d,"ClearHeight",Actor(d,departing).Position);SetAir(d,departing,"Height",safe-5);SetAir(d,departing,"Departing",true);
            Require(d.Apply(new PlayableCommand(905,1,"player-1",PlayableCommandKind.Hold,new[]{yielding}),out _)==PlayableCommandStatus.Applied,"yield Hold setup");var savedGoal=Actor(d,yielding).Goal;SetAir(d,yielding,"Yielding",true);SetAir(d,yielding,"YieldGoal",new NavPoint(8,16));SetAir(d,yielding,"Short",true);SetAir(d,yielding,"Escape",true);SetAir(d,yielding,"FacingTarget",99999);SetAir(d,yielding,"ShortRemaining",0d);audit=new AirMotionChecks(d);Call(d,"AdvanceHawks",1d/30);
            var departure=audit.latest[0];Require(!departure.HeightAssigned&&departure.Final.Equals(departure.Before)&&departure.AssignedHeight==safe-5+p.HawkClimbSpeed/30,"departure early climb law changed");
            Require(Actor(d,yielding).Goal.Equals(savedGoal)&&!Actor(d,yielding).Moving&&(bool)Field(Air(d,yielding),"Yielding")&&!(bool)Field(Air(d,yielding),"Short")&&!(bool)Field(Air(d,yielding),"Escape"),"yield restoration/expired short escape law changed");
            Console.WriteLine("AIR ordered earlier/later Follow; nominal-clearance/new-height/lateral-XZ; deferred combat+revision; departing/yield/short-escape PASS");
        }finally{audit?.Dispose();Close(d);}
    }
    internal static void Run()
    {
        CheckDiscreteLaws();CheckStepPending();
        var profile=PlayableProfile.Default;PlayableDomain d=null,cold=null;AirMotionChecks authority=null,restored=null;PlayableDomain.AirMotionPin immutablePin=null;
        try{
            d=new PlayableDomain(profile,905,false);int first=Spawn(d,new NavPoint(-6,10)),second=Spawn(d,new NavPoint(6,10)),idle=Spawn(d,new NavPoint(0,16));
            Move(d,1,first,new NavPoint(6,10));Move(d,2,second,new NavPoint(-6,10));Move(d,3,idle,new NavPoint(0,16));Move(d,4,idle,new NavPoint(2,16),PlayableOrderMode.Append);
            authority=new AirMotionChecks(d);var stableActor=Actor(d,first);var unchanged=d.CaptureWorldBytes(7,"air-current");
            authority.refuse=true;bool refused=false;try{Call(d,"AdvanceHawks",1d/30);}catch(InvalidOperationException){refused=true;}Require(refused&&unchanged.SequenceEqual(d.CaptureWorldBytes(7,"air-current")),"refusal changed live state");
            Call(d,"AdvanceHawks",1d/30);Require(ReferenceEquals(stableActor,Actor(d,first)),"live actor identity replaced");
            var beforeCapacity=d.CaptureWorldBytes(7,"air-current");var budget=d.Navigation.PhysicalHostBudget;budget.AllocationAdmission=bytes=>false;
            try{Call(d,"AdvanceHawks",1d/30);}finally{budget.AllocationAdmission=null;}
            Require(beforeCapacity.SequenceEqual(d.CaptureWorldBytes(7,"air-current")),"temporary Air admission refusal changed world or threw");
            // Refuse the source allocation AFTER scope admission as a second
            // resource path; no policy/guard exception is swallowed.
            int admissions=0;budget.AllocationAdmission=bytes=>++admissions<=1;
            try{Call(d,"AdvanceHawks",1d/30);}finally{budget.AllocationAdmission=null;}
            Require(beforeCapacity.SequenceEqual(d.CaptureWorldBytes(7,"air-current")),"Air preparation capacity refusal changed world");

            var immutable=authority.latest.OutputValues.ToArray();long firstOp=authority.latest.Operation;
            for(int i=0;i<80;i++)Call(d,"AdvanceHawks",1d/30);Require(immutable.Length>0&&authority.latest.Operation>firstOp,"source operation watermark did not increase");
            Require(d.TryRetainAirMotionSource(authority.latest.Operation,true,out var receipt)&&receipt.Committed,"missing owning COMMITTED lookup");
            var retainedOutput=receipt.OutputValues.ToArray();for(int i=0;i<5;i++)Call(d,"AdvanceHawks",1d/30);Require(retainedOutput.SequenceEqual(receipt.OutputValues.ToArray()),"later live motion mutated retained source");receipt.Dispose();
            authority.replay=true;refused=false;try{Call(d,"AdvanceHawks",1d/30);}catch(InvalidOperationException){refused=true;}authority.replay=false;Require(refused,"committed source allowed execution");
            var law=AirLawBytes(d);authority.unexpectedGuard=true;refused=false;try{Call(d,"AdvanceHawks",1d/30);}catch(InvalidOperationException){refused=true;}Require(refused&&law.SequenceEqual(AirLawBytes(d)),"unexpected final guard fault was swallowed or changed Air");
            var beforeWait=d.CaptureWorldBytes(7,"air-current");authority.waitBeforeAcceptance=true;Call(d,"AdvanceHawks",1d/30);Require(beforeWait.SequenceEqual(d.CaptureWorldBytes(7,"air-current")),"preacceptance pending changed world");
            authority.unavailableGuard=true;Call(d,"AdvanceHawks",1d/30);Require(authority.pending!=null&&law.SequenceEqual(AirLawBytes(d)),"expected final guard refusal wrote Air/allocator");var guardPin=authority.pending;authority.pending=null;authority.resume=guardPin;int commits=authority.committed;d.ResumeAirMotion(guardPin);Require(authority.committed==commits+1&&authority.latest.Operation==guardPin.Operation,"guard-refused same-point operation did not resume once");
            law=AirLawBytes(d);authority.defer=true;Call(d,"AdvanceHawks",1d/30);Require(authority.pending!=null&&law.SequenceEqual(AirLawBytes(d)),"paid pending changed Air state/allocator or was not retained");
            long pendingOp=authority.pending.Operation,committedOp=authority.latest.Operation;var pendingOutput=authority.pending.OutputValues.ToArray();
            var saved=d.CaptureWorldBytes(7,"air-current");cold=PlayableDomain.RestoreWorldBytes(saved,profile,7,"air-current");Require(saved.SequenceEqual(cold.CaptureWorldBytes(7,"air-current")),"pending+committed current world changed bytes");Require(cold.AwaitingAirSources,"cold source restore fabricated live pins");
            refused=false;try{cold.Step(1d/30);}catch(InvalidOperationException){refused=true;}Require(refused,"cold sources advanced Domain clock");
            refused=false;try{cold.Apply(new PlayableCommand(905,9,"player-1",PlayableCommandKind.Stop,new[]{first}),out _);}catch(InvalidOperationException){refused=true;}Require(refused,"cold sources accepted command");
            Require(cold.TryRetainAirMotionSource(pendingOp,false,out var pendingPin)&&!pendingPin.Committed&&pendingOutput.SequenceEqual(pendingPin.OutputValues.ToArray()),"pending source was not retained exactly");Require(cold.AwaitingAirSources,"partial source rebind opened clock");
            Require(cold.TryRetainAirMotionSource(committedOp,true,out var committedPin)&&committedPin.Committed&&!cold.AwaitingAirSources,"paid full source rebind failed");
            for(int i=0;i<pendingPin.Count;i++){CheckProjection(pendingPin[i].BeforeActor,authority.pending[i].BeforeActor,"cold pending before");CheckProjection(pendingPin[i].AfterActor,authority.pending[i].AfterActor,"cold pending after");CheckProjection(committedPin[i].BeforeActor,authority.latest[i].BeforeActor,"cold committed before");CheckProjection(committedPin[i].AfterActor,authority.latest[i].AfterActor,"cold committed after");}
            Require(cold.TryRetainAirMotionSource(committedOp,true,out immutablePin),"missing independent immutable pin");var retainedBefore=authority.latest[0];
            restored=new AirMotionChecks(cold);restored.latest=committedPin;restored.resume=pendingPin;cold.ResumeAirMotion(pendingPin);Require(restored.committed==1&&restored.latest.Operation==pendingOp,"pending restore did not commit same operation once");
            Require(cold.TryRetainAirMotionSource(pendingOp,true,out var exact)&&exact.OutputValues.Span.SequenceEqual(pendingOutput),"resumed committed output differs");exact.Dispose();
            bool consumed=false;try{cold.ResumeAirMotion(pendingPin);}catch(InvalidOperationException){consumed=true;}Require(consumed,"committed pending source allowed second execution");
            Call(cold,"AdvanceHawks",1d/30);Require(restored.latest.Operation>pendingOp,"restored operation allocator reused ID");CheckProjection(immutablePin[0].BeforeActor,retainedBefore.BeforeActor,"immutable retained before");CheckProjection(immutablePin[0].AfterActor,retainedBefore.AfterActor,"immutable retained after");
            Require(authority.maxInstallBytes==0&&restored.maxInstallBytes==0,"allocation between final Air guard and factual adoption: "+authority.maxInstallBytes+"/"+restored.maxInstallBytes);
            Console.WriteLine($"AIR complete discrete preparation/atomic refusal/current source/pending resume PASS prepared={authority.prepared} committed={authority.committed} receivers={authority.rows} saved={saved.Length} pendingOp={pendingOp}");
            restored.Dispose();restored=null;var retired=cold.CaptureWorldBytes(7,"air-current");var again=PlayableDomain.RestoreWorldBytes(retired,profile,7,"air-current");try{Require(!again.AwaitingAirSources&&again.AirOperationWatermark==cold.AirOperationWatermark&&retired.SequenceEqual(again.CaptureWorldBytes(7,"air-current")),"retired source watermark/current bytes changed");}finally{Close(again);}
        }finally{immutablePin?.Dispose();restored?.Dispose();authority?.Dispose();Close(cold);Close(d);}
        Console.WriteLine("AIR paid pins/registry/decode/physical shutdown zero PASS");
    }
}
