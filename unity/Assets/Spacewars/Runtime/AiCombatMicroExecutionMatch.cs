using System;
using System.IO;
using Spacewars.Simulation;
namespace Spacewars.Runtime
{
    // Exact ordered matching is paid even when both references happen to be the
    // same object. Matching is a component result, never policy authorization.
    internal sealed class AiCombatMicroExecutionMatch:IDisposable
    {
        readonly MotionExecutionProposal expected,actual;
        readonly AiCombatMicroExecutionStorage.Lease self,expectedPin,actualPin;
        int primitive,trace,leg,phase;bool complete,equal=true,disposed;
        internal bool Complete=>complete;
        internal bool Equal{get{if(!complete)throw new InvalidOperationException("Execution match pending.");return equal;}}
        internal long Consumed{get;private set;}
        AiCombatMicroExecutionMatch(MotionExecutionProposal expected,MotionExecutionProposal actual,AiCombatMicroExecutionStorage.Lease self,AiCombatMicroExecutionStorage.Lease expectedPin,AiCombatMicroExecutionStorage.Lease actualPin){this.expected=expected;this.actual=actual;this.self=self;this.expectedPin=expectedPin;this.actualPin=actualPin;}
        internal static bool TryCreate(MotionExecutionProposal expected,MotionExecutionProposal actual,AiCombatMicroExecutionStorage.Lease expectedRoot,AiCombatMicroExecutionStorage.Lease actualRoot,AiCombatMicroExecutionStorage storage,out AiCombatMicroExecutionMatch work)
        {
            work=null;if(expected==null||actual==null||expectedRoot==null||actualRoot==null||storage==null)throw new ArgumentNullException();if(!storage.TryReserve(192,out var self))return false;if(!expectedRoot.TryShare(out var a)){self.Dispose();return false;}if(!actualRoot.TryShare(out var b)){a.Dispose();self.Dispose();return false;}
            try{work=new AiCombatMicroExecutionMatch(expected,actual,self,a,b);return true;}catch{self.Dispose();a.Dispose();b.Dispose();throw;}
        }
        void Reject(){equal=false;complete=true;}
        internal int Advance(int grant,Func<bool> admit=null)
        {if(disposed)throw new ObjectDisposedException(nameof(AiCombatMicroExecutionMatch));if(grant<0)throw new ArgumentOutOfRangeException(nameof(grant));int used=0;while(used<grant&&!complete){if(admit!=null&&!admit())break;Step();Consumed++;used++;}return used;}
        void Step()
        {
            if(phase==0){if(expected.World!=actual.World||expected.Group!=actual.Group||expected.Membership!=actual.Membership||expected.BundleId!=actual.BundleId||expected.OwnerTick!=actual.OwnerTick||expected.Count!=actual.Count){Reject();return;}phase=1;return;}
            if(primitive==expected.Count){complete=true;return;}
            var a=expected.Primitive(primitive);var b=actual.Primitive(primitive);
            if(phase==1){if(!a.SameHeader(b)){Reject();return;}phase=2;return;}
            if(phase==2){if(trace<a.TraceCount){if(!a.SameTracePiece(b,trace++))Reject();return;}phase=3;return;}
            if(phase==3){if(a.RemainingBranch!=null&&leg<a.RemainingBranch.Count){if(!a.RemainingBranch.SameLeg(b.RemainingBranch,leg++))Reject();return;}primitive++;trace=leg=0;phase=1;return;}
            throw new InvalidOperationException("Invalid execution match phase.");
        }
        internal void Write(BinaryWriter w)
        {if(disposed)throw new ObjectDisposedException(nameof(AiCombatMicroExecutionMatch));w.Write(2);AiCombatMicroExecutionBindingWire.Write(w,expected);AiCombatMicroExecutionBindingWire.Write(w,actual);w.Write(primitive);w.Write(trace);w.Write(leg);w.Write(phase);w.Write(complete);w.Write(equal);w.Write(Consumed);}
        internal static AiCombatMicroExecutionMatch Read(BinaryReader r,MotionExecutionProposal expected,MotionExecutionProposal actual,AiCombatMicroExecutionStorage.Lease expectedRoot,AiCombatMicroExecutionStorage.Lease actualRoot,AiCombatMicroExecutionStorage storage)
        {
            if(r.ReadInt32()!=2)throw new ArgumentException("Unknown execution match cursor.");AiCombatMicroExecutionBindingWire.Check(r,expected);AiCombatMicroExecutionBindingWire.Check(r,actual);if(!TryCreate(expected,actual,expectedRoot,actualRoot,storage,out var work))throw new InvalidOperationException("Execution matcher admission refused.");
            try{work.primitive=r.ReadInt32();work.trace=r.ReadInt32();work.leg=r.ReadInt32();work.phase=r.ReadInt32();work.complete=WorldWire.Boolean(r);work.equal=WorldWire.Boolean(r);work.Consumed=r.ReadInt64();
                if(work.primitive<0||work.primitive>expected.Count||work.trace<0||work.leg<0||work.phase<0||work.phase>3||work.Consumed<0||!work.equal&&!work.complete||work.complete&&work.equal&&work.primitive!=expected.Count||work.phase==0&&(work.primitive!=0||work.trace!=0||work.leg!=0)||work.phase==1&&(work.trace!=0||work.leg!=0))throw new ArgumentException("Impossible execution match cursor.");
                if(work.primitive<expected.Count){var p=expected.Primitive(work.primitive);if(work.trace>p.TraceCount||work.leg>(p.RemainingBranch?.Count??0)||work.phase==2&&work.leg!=0||work.phase==3&&work.trace!=p.TraceCount)throw new ArgumentException("Invalid execution match offset.");}return work;
            }catch{work.Dispose();throw;}
        }
        public void Dispose(){if(disposed)return;disposed=true;self.Dispose();expectedPin.Dispose();actualPin.Dispose();}
    }
    // Exact binding stream for component codecs, not a digest or an authorization.
    // Its declared linear lifecycle cost covers every trace and remaining leg.
    internal static class AiCombatMicroExecutionBindingWire
    {
        internal static string ReadName(BinaryReader r,bool optional=false)
        {int count=r.ReadInt32();if(optional&&count==-1)return null;if(count<0||count>2048)throw new ArgumentException("Invalid bounded execution name.");var bytes=r.ReadBytes(count);if(bytes.Length!=count)throw new EndOfStreamException();var value=new System.Text.UTF8Encoding(false,true).GetString(bytes);return MotionExecutionBinding.Name(value,optional);}
        static void Location(BinaryWriter w,NavLocation p){WorldWire.Write(w,p.Position);WorldWire.String(w,p.SurfaceId);}
        static void Binding(BinaryWriter w,MotionExecutionBinding b){w.Write(b.World);w.Write(b.Group);w.Write(b.Membership);w.Write(b.RootOrder);w.Write(b.Command);w.Write(b.Entity);w.Write(b.Incarnation);w.Write(b.Mobility);w.Write(b.Request);w.Write(b.Order);w.Write(b.Activation);w.Write((int)b.OwnIntent);WorldWire.String(w,b.Owner);WorldWire.String(w,b.Route);WorldWire.String(w,b.Profile);WorldWire.String(w,b.Geometry);WorldWire.String(w,b.Hold);WorldWire.String(w,b.OwnerPolicy);}
        internal static void Write(BinaryWriter w,MotionExecutionProposal p)
        {
            w.Write(p.World);w.Write(p.Group);w.Write(p.Membership);w.Write(p.BundleId);w.Write(p.OwnerTick);w.Write(p.Count);
            for(int i=0;i<p.Count;i++)WritePrimitive(w,p.Primitive(i));
        }
        internal static void WritePrimitive(BinaryWriter w,MotionExecutionPrimitive a)
        {
            Binding(w,a.Binding);w.Write(a.PrimitiveId);w.Write((int)a.Kind);w.Write(a.OwnerTick);w.Write(a.Dt);Location(w,a.From);Location(w,a.To);w.Write(a.AssignedEndpoint.HasValue);if(a.AssignedEndpoint.HasValue)Location(w,a.AssignedEndpoint.Value);w.Write(a.Radius);WorldWire.String(w,a.BodyProfile);w.Write(a.HeadingBefore);w.Write(a.HeadingAfter);w.Write(a.Controller);w.Write(a.CooperativeCohort);w.Write(a.OriginalOwnRequest);w.Write(a.TraceCount);
                for(int j=0;j<a.TraceCount;j++){var t=a.TracePiece(j);w.Write((int)t.Kind);w.Write((int)t.Law);Location(w,t.From);Location(w,t.To);WorldWire.String(w,t.Portal);WorldWire.String(w,t.Support);w.Write(t.Forward);w.Write(t.CutFrom);w.Write(t.CutTo);w.Write(t.HeightX);w.Write(t.HeightZ);w.Write(t.HeightConstant);}
                w.Write(a.Air.HasValue);if(a.Air.HasValue){var air=a.Air.Value;w.Write(air.BeforeHeight);w.Write(air.AssignedHeight);WorldWire.Write(w,air.Nominal);w.Write(air.NominalClearance);w.Write(air.HeightAssigned);WorldWire.Write(w,air.Final);}
                var branch=a.RemainingBranch;w.Write(branch!=null);if(branch==null)return;Binding(w,branch.Binding);Location(w,branch.Origin);Location(w,branch.AssignedEndpoint);w.Write(branch.CurrentLeg);w.Write(branch.Progress);w.Write(branch.Count);
                for(int j=0;j<branch.Count;j++){var l=branch.Leg(j);Location(w,l.From);Location(w,l.To);w.Write(l.Cost);w.Write((int)l.Kind);WorldWire.String(w,l.PortalId);}
        }
        static NavLocation ReadLocation(BinaryReader r)=>new NavLocation(WorldWire.ReadPoint(r),ReadName(r));
        static MotionExecutionBinding ReadBinding(BinaryReader r)
        {
            long world=r.ReadInt64(),group=r.ReadInt64(),membership=r.ReadInt64(),root=r.ReadInt64(),command=r.ReadInt64(),entity=r.ReadInt64(),incarnation=r.ReadInt64(),mobility=r.ReadInt64(),request=r.ReadInt64(),order=r.ReadInt64(),activation=r.ReadInt64();var intent=WorldWire.EnumValue<MotionOwnIntentKind>(r);
            return new MotionExecutionBinding(world,group,membership,root,command,entity,incarnation,mobility,request,order,activation,ReadName(r),ReadName(r,true),ReadName(r),ReadName(r),ReadName(r,true),ReadName(r),intent);
        }
        static int ReadCount(BinaryReader r){int n=r.ReadInt32();if(n<0||n>WorldWire.MaxItems)throw new ArgumentException("Invalid saved execution value count.");return n;}
        // Decode accepted values, never reconstruct them from current HP, orders,
        // knowledge, membership or geometry. Every birth uses the existing paid
        // DTO factories; Native provenance is authenticated by the consumer.
        internal static MotionExecutionProposal Read(BinaryReader r,ref long grant,Func<long,bool> reserve)
        {
            long world=r.ReadInt64(),group=r.ReadInt64(),membership=r.ReadInt64(),bundle=r.ReadInt64(),tick=r.ReadInt64();int count=ReadCount(r);
            if(grant<count||!reserve(checked(256L+8L*count)))throw new InvalidOperationException("Saved proposal array admission refused.");grant-=count;var values=new MotionExecutionPrimitive[count];
            for(int i=0;i<count;i++){
                var binding=ReadBinding(r);long id=r.ReadInt64();var kind=WorldWire.EnumValue<MotionExecutionKind>(r);long ownerTick=r.ReadInt64();double dt=WorldWire.Number(r);var from=ReadLocation(r);var to=ReadLocation(r);NavLocation? endpoint=WorldWire.Boolean(r)?ReadLocation(r):(NavLocation?)null;double radius=WorldWire.Number(r);string body=ReadName(r);double before=WorldWire.Number(r),after=WorldWire.Number(r);long controller=r.ReadInt64(),cohort=r.ReadInt64(),original=r.ReadInt64();int traces=ReadCount(r);
                if(grant<traces||!reserve(checked(256L+192L*traces)))throw new InvalidOperationException("Saved trace array admission refused.");grant-=traces;var pieces=new MotionExecutionTracePiece[traces];
                for(int j=0;j<traces;j++){var traceKind=WorldWire.EnumValue<MotionExecutionTraceKind>(r);var law=WorldWire.EnumValue<MotionExecutionLaw>(r);var a=ReadLocation(r);var b=ReadLocation(r);string portal=ReadName(r,true),support=ReadName(r,true);bool forward=WorldWire.Boolean(r);double cutFrom=WorldWire.Number(r),cutTo=WorldWire.Number(r),x=WorldWire.Number(r),z=WorldWire.Number(r),constant=WorldWire.Number(r);pieces[j]=new MotionExecutionTracePiece(traceKind,law,a,b,support,x,z,constant,cutFrom,cutTo,portal,forward);}
                MotionExecutionAirStep? air=null;if(WorldWire.Boolean(r)){air=new MotionExecutionAirStep(WorldWire.Number(r),WorldWire.Number(r),WorldWire.ReadPoint(r),WorldWire.Number(r),WorldWire.Boolean(r),WorldWire.ReadPoint(r));}
                MotionExecutionRemainingBranch branch=null;
                if(WorldWire.Boolean(r)){
                    var branchBinding=ReadBinding(r);var origin=ReadLocation(r);var assigned=ReadLocation(r);int current=r.ReadInt32();double progress=WorldWire.Number(r);int legs=ReadCount(r);
                    if(grant<legs||!reserve(checked(256L+256L*legs)))throw new InvalidOperationException("Saved branch array admission refused.");grant-=legs;var source=new NavTypedLeg[legs];
                    for(int j=0;j<legs;j++){var a=ReadLocation(r);var b=ReadLocation(r);double cost=WorldWire.Number(r);var legKind=WorldWire.EnumValue<NavTypedLegKind>(r);source[j]=new NavTypedLeg(a,b,cost,legKind,ReadName(r,true));}
                    if(!MotionExecutionRemainingBranch.TryCreate(branchBinding,origin,assigned,current,progress,source,ref grant,reserve,out branch))throw new InvalidOperationException("Saved branch admission refused.");
                }
                if(!MotionExecutionPrimitive.TryCreate(binding,id,kind,ownerTick,dt,from,to,endpoint,radius,body,before,after,controller,cohort,original,pieces,ref grant,reserve,out values[i],branch,air))throw new InvalidOperationException("Saved primitive admission refused.");
            }
            if(!MotionExecutionProposal.TryCreate(world,group,membership,bundle,tick,values,ref grant,reserve,out var result))throw new InvalidOperationException("Saved proposal admission refused.");return result;
        }
        internal static void CheckPrimitive(BinaryReader r,MotionExecutionPrimitive p){using(var comparison=new PredicateBindingComparison(r))using(var w=new BinaryWriter(comparison,System.Text.Encoding.UTF8,true))WritePrimitive(w,p);}
        internal static void Check(BinaryReader r,MotionExecutionProposal p){using(var comparison=new PredicateBindingComparison(r))using(var w=new BinaryWriter(comparison,System.Text.Encoding.UTF8,true))Write(w,p);}
    }
}
