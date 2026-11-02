using System;
using System.Collections.Generic;
namespace Spacewars.Simulation
{
    public enum MotionOwnIntentKind { None, Standalone, Grouped, Air }
    public enum MotionExecutionKind { OwnRoute, AttachmentShortcutRejoin, BoundedLocalRepair, CooperativeYield, PureTurnWait, ProducerException, AirDiscrete }
    public enum MotionExecutionTraceKind { Surface, Portal, Air }
    public enum MotionExecutionLaw { Unknown, AffineSupportChord, SemanticPortalCrossing, NativeAssignedAltitude }

    // Neutral values only. OwnerPolicy is an opaque owner-issued revision binding,
    // not permission in itself. Clocks belong to the owner snapshot, never Stopwatch.
    public readonly struct MotionExecutionBinding : IEquatable<MotionExecutionBinding>
    {
        public readonly long World,Group,Membership,RootOrder,Command,Entity,Incarnation,Mobility,Request,Order,Activation;
        public readonly MotionOwnIntentKind OwnIntent;
        public bool HasOwnIntent=>OwnIntent!=MotionOwnIntentKind.None;
        public readonly string Owner,Route,Profile,Geometry,Hold,OwnerPolicy;
        public MotionExecutionBinding(long world,long group,long membership,long rootOrder,long command,long entity,long incarnation,long mobility,long request,long order,long activation,string owner,string route,string profile,string geometry,string hold,string ownerPolicy,MotionOwnIntentKind ownIntent=MotionOwnIntentKind.Grouped)
        {
            if(world<1||entity<1||incarnation<0||ownIntent!=MotionOwnIntentKind.Air&&incarnation<1||mobility<0||(!Enum.IsDefined(typeof(MotionOwnIntentKind),ownIntent)||rootOrder<0||command<0||request<0||order<0||activation<0||(ownIntent==MotionOwnIntentKind.Grouped?(group<1||membership<1||rootOrder<1||command<1||request<1||order<1||activation<1):ownIntent==MotionOwnIntentKind.Air?(group!=0||membership!=0||rootOrder!=0||command<1):ownIntent==MotionOwnIntentKind.Standalone?(group!=0||membership!=0||request<1):(group!=0||membership!=0||rootOrder!=0||command!=0||request!=0||order!=0||activation!=0))))throw new ArgumentException("Invalid execution identity.");
            OwnIntent=ownIntent;World=world;Group=group;Membership=membership;RootOrder=rootOrder;Command=command;Entity=entity;Incarnation=incarnation;Mobility=mobility;Request=request;Order=order;Activation=activation;
            Owner=Name(owner);Route=Name(route,ownIntent==MotionOwnIntentKind.None);if(ownIntent==MotionOwnIntentKind.None&&route!=null)throw new ArgumentException("Idle actor has no own route.");Profile=Name(profile);Geometry=Name(geometry);Hold=Name(hold,true);OwnerPolicy=Name(ownerPolicy);
        }
        internal static string Name(string value,bool optional=false){if(optional&&value==null)return null;if(string.IsNullOrWhiteSpace(value)||value.Length>512)throw new ArgumentException("Invalid bounded execution name.");return value;}
        internal static bool Number(double value)=>!double.IsNaN(value)&&!double.IsInfinity(value);
        internal static bool Same(double a,double b)=>BitConverter.DoubleToInt64Bits(a)==BitConverter.DoubleToInt64Bits(b);
        internal static NavLocation Location(NavLocation value){if(!Number(value.Position.X)||!Number(value.Position.Z))throw new ArgumentException("Invalid execution position.");return new NavLocation(value.Position,Name(value.SurfaceId));}
        internal static bool Same(NavLocation a,NavLocation b)=>a.SurfaceId==b.SurfaceId&&Same(a.Position.X,b.Position.X)&&Same(a.Position.Z,b.Position.Z);
        public bool Equals(MotionExecutionBinding b)=>OwnIntent==b.OwnIntent&&World==b.World&&Group==b.Group&&Membership==b.Membership&&RootOrder==b.RootOrder&&Command==b.Command&&Entity==b.Entity&&Incarnation==b.Incarnation&&Mobility==b.Mobility&&Request==b.Request&&Order==b.Order&&Activation==b.Activation&&Owner==b.Owner&&Route==b.Route&&Profile==b.Profile&&Geometry==b.Geometry&&Hold==b.Hold&&OwnerPolicy==b.OwnerPolicy;
        public override bool Equals(object o)=>o is MotionExecutionBinding b&&Equals(b);
        public override int GetHashCode()=>World.GetHashCode()^Request.GetHashCode()^Activation.GetHashCode();
    }
    public readonly struct MotionExecutionTracePiece
    {
        public readonly MotionExecutionTraceKind Kind;
        public readonly MotionExecutionLaw Law;
        public readonly NavLocation From,To;
        public readonly string Portal,Support;
        public readonly bool Forward;
        public readonly double CutFrom,CutTo,HeightX,HeightZ,HeightConstant;
        public MotionExecutionTracePiece(MotionExecutionTraceKind kind,MotionExecutionLaw law,NavLocation from,NavLocation to,string support,double heightX,double heightZ,double heightConstant,double cutFrom,double cutTo,string portal=null,bool forward=true)
        {
            if(!Enum.IsDefined(typeof(MotionExecutionTraceKind),kind)||!Enum.IsDefined(typeof(MotionExecutionLaw),law)||!MotionExecutionBinding.Number(heightX)||!MotionExecutionBinding.Number(heightZ)||!MotionExecutionBinding.Number(heightConstant)||!MotionExecutionBinding.Number(cutFrom)||!MotionExecutionBinding.Number(cutTo)||cutFrom<0||cutTo<cutFrom||cutTo>1)throw new ArgumentException("Invalid execution trace piece.");
            Kind=kind;Law=law;From=MotionExecutionBinding.Location(from);To=MotionExecutionBinding.Location(to);Support=MotionExecutionBinding.Name(support,true);Portal=MotionExecutionBinding.Name(portal,true);Forward=forward;CutFrom=cutFrom;CutTo=cutTo;HeightX=heightX;HeightZ=heightZ;HeightConstant=heightConstant;
            if(kind==MotionExecutionTraceKind.Portal&&Portal==null||kind!=MotionExecutionTraceKind.Portal&&Portal!=null)throw new ArgumentException("Trace portal identity mismatch.");
        }
        // Portal crossings remain explicit. An unsupported law is representable,
        // but never turns into an empty or flattened safety approval.
        public bool Supported=>Kind==MotionExecutionTraceKind.Air?Law==MotionExecutionLaw.NativeAssignedAltitude&&Support==null&&Portal==null&&HeightX==0&&HeightZ==0&&HeightConstant==0:Kind==MotionExecutionTraceKind.Surface?Law==MotionExecutionLaw.AffineSupportChord&&Support!=null&&From.SurfaceId==To.SurfaceId:Law==MotionExecutionLaw.SemanticPortalCrossing&&MotionExecutionBinding.Same(From.Position.X,To.Position.X)&&MotionExecutionBinding.Same(From.Position.Z,To.Position.Z);
        public double HeightAt(NavPoint p){if(Kind!=MotionExecutionTraceKind.Surface||!Supported)throw new InvalidOperationException("Unsupported support law.");return HeightX*p.X+HeightZ*p.Z+HeightConstant;}
        public bool Same(MotionExecutionTracePiece b)=>Kind==b.Kind&&Law==b.Law&&MotionExecutionBinding.Same(From,b.From)&&MotionExecutionBinding.Same(To,b.To)&&Portal==b.Portal&&Support==b.Support&&Forward==b.Forward&&MotionExecutionBinding.Same(CutFrom,b.CutFrom)&&MotionExecutionBinding.Same(CutTo,b.CutTo)&&MotionExecutionBinding.Same(HeightX,b.HeightX)&&MotionExecutionBinding.Same(HeightZ,b.HeightZ)&&MotionExecutionBinding.Same(HeightConstant,b.HeightConstant);
    }
    // Existing NavTypedLeg values are detached from provider/physical owner edges.
    // These are the WHOLE remaining path, not next-step chord cuts or duration.
    public sealed class MotionExecutionRemainingBranch
    {
        readonly NavTypedLeg[] legs;
        public MotionExecutionBinding Binding {get;}
        public NavLocation Origin {get;} public NavLocation AssignedEndpoint {get;}
        public int CurrentLeg {get;} public double Progress {get;}
        public int Count=>legs.Length;
        public NavTypedLeg Leg(int index)=>legs[index];
        MotionExecutionRemainingBranch(MotionExecutionBinding binding,NavLocation origin,NavLocation endpoint,int currentLeg,double progress,NavTypedLeg[] legs){Binding=binding;Origin=origin;AssignedEndpoint=endpoint;CurrentLeg=currentLeg;Progress=progress;this.legs=legs;}
        public static bool TryCreate(MotionExecutionBinding binding,NavLocation origin,NavLocation endpoint,int currentLeg,double progress,IReadOnlyList<NavTypedLeg> source,ref long copyGrant,Func<long,bool> reserve,out MotionExecutionRemainingBranch branch)
        {
            branch=null;if(!binding.HasOwnIntent||source==null||currentLeg<0||!MotionExecutionBinding.Number(progress)||progress<0||progress>1)throw new ArgumentException("Invalid remaining branch cursor.");origin=MotionExecutionBinding.Location(origin);endpoint=MotionExecutionBinding.Location(endpoint);
            long cost=checked(2L*source.Count);if(copyGrant<cost||reserve==null||!reserve(checked(512L+(NavTypedLeg.StorageCharge+32L)*source.Count)))return false;copyGrant-=cost;var copy=new NavTypedLeg[source.Count];
            for(int i=0;i<copy.Length;i++){var leg=source[i];if(leg==null||i==0&&!MotionExecutionBinding.Same(origin,leg.From)||i>0&&!MotionExecutionBinding.Same(copy[i-1].To,leg.From))throw new ArgumentException("Incomplete remaining typed branch.");copy[i]=leg.Detached();}
            if(copy.Length==0?!MotionExecutionBinding.Same(origin,endpoint):!MotionExecutionBinding.Same(copy[copy.Length-1].To,endpoint))throw new ArgumentException("Remaining branch endpoint mismatch.");branch=new MotionExecutionRemainingBranch(binding,origin,endpoint,currentLeg,progress,copy);return true;
        }
        public bool SameHeader(MotionExecutionRemainingBranch b)=>b!=null&&Binding.Equals(b.Binding)&&MotionExecutionBinding.Same(Origin,b.Origin)&&MotionExecutionBinding.Same(AssignedEndpoint,b.AssignedEndpoint)&&CurrentLeg==b.CurrentLeg&&MotionExecutionBinding.Same(Progress,b.Progress)&&Count==b.Count;
        public bool SameLeg(MotionExecutionRemainingBranch b,int i){if(b==null||i<0||i>=Count||i>=b.Count)return false;var a=legs[i];var c=b.legs[i];return MotionExecutionBinding.Same(a.From,c.From)&&MotionExecutionBinding.Same(a.To,c.To)&&MotionExecutionBinding.Same(a.Cost,c.Cost)&&a.Kind==c.Kind&&a.PortalId==c.PortalId;}
    }
    // Exact scalar projection of one Native discrete Air publication. The law
    // assigns the new altitude before the actual final XZ sweep; nominal clearance
    // remains the original producer value even when a lateral fallback is chosen.
    // This is not a ground support plane or a forecast of future flight heights.
    public readonly struct MotionExecutionAirStep
    {
        public readonly double BeforeHeight,AssignedHeight,NominalClearance;
        public readonly NavPoint Nominal,Final;
        public readonly bool HeightAssigned;
        public MotionExecutionAirStep(double beforeHeight,double assignedHeight,NavPoint nominal,double nominalClearance,bool heightAssigned,NavPoint final)
        {
            if(!MotionExecutionBinding.Number(beforeHeight)||!MotionExecutionBinding.Number(assignedHeight)||!MotionExecutionBinding.Number(nominalClearance)||!MotionExecutionBinding.Number(nominal.X)||!MotionExecutionBinding.Number(nominal.Z)||!MotionExecutionBinding.Number(final.X)||!MotionExecutionBinding.Number(final.Z))throw new ArgumentException("Invalid discrete Air law.");
            BeforeHeight=beforeHeight;AssignedHeight=assignedHeight;Nominal=nominal;NominalClearance=nominalClearance;HeightAssigned=heightAssigned;Final=final;
        }
        public bool Same(MotionExecutionAirStep b)=>MotionExecutionBinding.Same(BeforeHeight,b.BeforeHeight)&&MotionExecutionBinding.Same(AssignedHeight,b.AssignedHeight)&&MotionExecutionBinding.Same(Nominal.X,b.Nominal.X)&&MotionExecutionBinding.Same(Nominal.Z,b.Nominal.Z)&&MotionExecutionBinding.Same(NominalClearance,b.NominalClearance)&&HeightAssigned==b.HeightAssigned&&MotionExecutionBinding.Same(Final.X,b.Final.X)&&MotionExecutionBinding.Same(Final.Z,b.Final.Z);
    }
    public sealed class MotionExecutionPrimitive
    {
        readonly MotionExecutionTracePiece[] trace;
        public MotionExecutionBinding Binding {get;}
        public MotionExecutionAirStep? Air {get;}
        public long PrimitiveId {get;}
        public MotionExecutionRemainingBranch RemainingBranch {get;}
        public MotionExecutionKind Kind {get;}
        public long OwnerTick {get;}
        public double Dt {get;}
        public NavLocation From {get;} public NavLocation To {get;} public NavLocation? AssignedEndpoint {get;}
        public double Radius {get;} public string BodyProfile {get;}
        public double HeadingBefore {get;} public double HeadingAfter {get;}
        public long Controller {get;} public long CooperativeCohort {get;} public long OriginalOwnRequest {get;}
        public int TraceCount=>trace.Length;
        public MotionExecutionTracePiece TracePiece(int index)=>trace[index];
        MotionExecutionPrimitive(MotionExecutionBinding binding,long id,MotionExecutionKind kind,long ownerTick,double dt,NavLocation from,NavLocation to,NavLocation? endpoint,double radius,string body,double before,double after,long controller,long cohort,long original,MotionExecutionTracePiece[] trace,MotionExecutionRemainingBranch remaining,MotionExecutionAirStep? air)
        {Air=air;RemainingBranch=remaining;Binding=binding;PrimitiveId=id;Kind=kind;OwnerTick=ownerTick;Dt=dt;From=from;To=to;AssignedEndpoint=endpoint;Radius=radius;BodyProfile=body;HeadingBefore=before;HeadingAfter=after;Controller=controller;CooperativeCohort=cohort;OriginalOwnRequest=original;this.trace=trace;}
        // Snapshot copy is explicitly pre-paid and memory-admitted before birth.
        // No lazy enumerator/callback survives in the immutable proposal.
        public static bool TryCreate(MotionExecutionBinding binding,long id,MotionExecutionKind kind,long ownerTick,double dt,NavLocation from,NavLocation to,NavLocation? endpoint,double radius,string body,double before,double after,long controller,long cohort,long original,IReadOnlyList<MotionExecutionTracePiece> pieces,ref long copyGrant,Func<long,bool> reserve,out MotionExecutionPrimitive primitive,MotionExecutionRemainingBranch remaining=null,MotionExecutionAirStep? air=null)
        {
            primitive=null;if(air.HasValue!=(kind==MotionExecutionKind.AirDiscrete)||air.HasValue!=(binding.OwnIntent==MotionOwnIntentKind.Air)||air.HasValue&&(remaining!=null||!MotionExecutionBinding.Same(to.Position.X,air.Value.Final.X)||!MotionExecutionBinding.Same(to.Position.Z,air.Value.Final.Z)))throw new ArgumentException("Air primitive/law mismatch.");if(remaining!=null&&(!binding.Equals(remaining.Binding)||!MotionExecutionBinding.Same(from,remaining.Origin)||!endpoint.HasValue||!MotionExecutionBinding.Same(endpoint.Value,remaining.AssignedEndpoint)))throw new ArgumentException("Remaining branch/primitive binding mismatch.");if(id<1||ownerTick<0||!MotionExecutionBinding.Number(dt)||dt<0||!MotionExecutionBinding.Number(radius)||radius<=0||!MotionExecutionBinding.Number(before)||!MotionExecutionBinding.Number(after)||controller<0||cohort<0||original!=(binding.HasOwnIntent?binding.Request:0)||endpoint.HasValue!=binding.HasOwnIntent||pieces==null||pieces.Count<1||!Enum.IsDefined(typeof(MotionExecutionKind),kind)||binding.World<1)throw new ArgumentException("Invalid prepared primitive.");
            from=MotionExecutionBinding.Location(from);to=MotionExecutionBinding.Location(to);endpoint=endpoint.HasValue?MotionExecutionBinding.Location(endpoint.Value):(NavLocation?)null;body=MotionExecutionBinding.Name(body);
            long copyCost=2L*pieces.Count+(air.HasValue?16:0);if(copyGrant<copyCost||reserve==null||!reserve(checked(512L+192L*pieces.Count+(air.HasValue?128:0))))return false;copyGrant-=copyCost;
            double cut=0;var copy=new MotionExecutionTracePiece[pieces.Count];for(int i=0;i<copy.Length;i++){var p=pieces[i];if(p.From.SurfaceId==null||p.To.SurfaceId==null||i==0&&!MotionExecutionBinding.Same(p.From,from)||i>0&&!MotionExecutionBinding.Same(copy[i-1].To,p.From))throw new ArgumentException("Incomplete ordered execution transcript.");
                if((p.Kind==MotionExecutionTraceKind.Air)!=air.HasValue||!MotionExecutionBinding.Same(p.CutFrom,cut)||p.Kind!=MotionExecutionTraceKind.Portal&&p.CutTo<=p.CutFrom||p.Kind==MotionExecutionTraceKind.Portal&&!MotionExecutionBinding.Same(p.CutFrom,p.CutTo))throw new ArgumentException("Execution cuts gap, overlap or reversal.");
                if(p.Kind!=MotionExecutionTraceKind.Portal)cut=p.CutTo;copy[i]=p;}
            if(!MotionExecutionBinding.Same(cut,1)||!MotionExecutionBinding.Same(copy[copy.Length-1].To,to))throw new ArgumentException("Execution endpoint differs from transcript.");
            primitive=new MotionExecutionPrimitive(binding,id,kind,ownerTick,dt,from,to,endpoint,radius,body,before,after,controller,cohort,original,copy,remaining,air);return true;
        }
        public bool SameHeader(MotionExecutionPrimitive b)=>b!=null&&Air.HasValue==b.Air.HasValue&&(!Air.HasValue||Air.Value.Same(b.Air.Value))&&Binding.Equals(b.Binding)&&PrimitiveId==b.PrimitiveId&&Kind==b.Kind&&OwnerTick==b.OwnerTick&&MotionExecutionBinding.Same(Dt,b.Dt)&&MotionExecutionBinding.Same(From,b.From)&&MotionExecutionBinding.Same(To,b.To)&&AssignedEndpoint.HasValue==b.AssignedEndpoint.HasValue&&(!AssignedEndpoint.HasValue||MotionExecutionBinding.Same(AssignedEndpoint.Value,b.AssignedEndpoint.Value))&&MotionExecutionBinding.Same(Radius,b.Radius)&&BodyProfile==b.BodyProfile&&MotionExecutionBinding.Same(HeadingBefore,b.HeadingBefore)&&MotionExecutionBinding.Same(HeadingAfter,b.HeadingAfter)&&Controller==b.Controller&&CooperativeCohort==b.CooperativeCohort&&OriginalOwnRequest==b.OriginalOwnRequest&&TraceCount==b.TraceCount&&(RemainingBranch==null?b.RemainingBranch==null:RemainingBranch.SameHeader(b.RemainingBranch));
        // Full exact match = SameHeader AND every SameTracePiece index AND,
        // when RemainingBranch exists, its SameHeader AND EVERY SameLeg index.
        // Each comparison is separately paid/cursored by owner/consumer. The
        // same header/chord with different remaining legs is NOT one permit.
        // No digest or arbitrary object-reference shortcut authorizes a match.
        public bool SameTracePiece(MotionExecutionPrimitive other,int index)=>other!=null&&index>=0&&index<TraceCount&&index<other.TraceCount&&trace[index].Same(other.trace[index]);
    }
    public sealed class MotionExecutionProposal
    {
        readonly MotionExecutionPrimitive[] primitives;
        // Group/Membership identify the CONTROLLER bundle, not every receiver own intent.
        public long World {get;} public long Group {get;} public long Membership {get;} public long BundleId {get;} public long OwnerTick {get;}
        public int Count=>primitives.Length;
        public MotionExecutionPrimitive Primitive(int index)=>primitives[index];
        MotionExecutionProposal(long world,long group,long membership,long bundle,long tick,MotionExecutionPrimitive[] primitives){World=world;Group=group;Membership=membership;BundleId=bundle;OwnerTick=tick;this.primitives=primitives;}
        // Completeness against current live membership is verified by the real
        // owner/Nav transaction, not asserted by this detached value container.
        public static bool TryCreate(long world,long group,long membership,long bundle,long tick,IReadOnlyList<MotionExecutionPrimitive> source,ref long copyGrant,Func<long,bool> reserve,out MotionExecutionProposal proposal)
        {proposal=null;if(world<1||group<0||membership<0||(group==0)!=(membership==0)||bundle<1||tick<0||source==null||source.Count<1)throw new ArgumentException("Invalid final group proposal.");int depth=0;for(int n=source.Count;n>0;n>>=1)depth++;
            // Two balanced sets: at most two tree heights per insert, plus fill
            // and array initialization. Debit the caller's ACTUAL grant by ref.
            long copyCost=checked(source.Count*(8L+8L*depth));if(copyGrant<copyCost||reserve==null||!reserve(checked(512L+192L*source.Count)))return false;copyGrant-=copyCost;
            var entities=new SortedSet<long>();var ids=new SortedSet<long>();var copy=new MotionExecutionPrimitive[source.Count];for(int i=0;i<copy.Length;i++){var p=source[i];if(p==null||p.Binding.World!=world||p.OwnerTick!=tick||!entities.Add(p.Binding.Entity)||!ids.Add(p.PrimitiveId))throw new ArgumentException("Group proposal binding or unique receiver mismatch.");copy[i]=p;}proposal=new MotionExecutionProposal(world,group,membership,bundle,tick,copy);return true;}
    }
    public sealed class MotionExecutionPermit
    {
        public MotionExecutionProposal Proposal {get;}
        public long PermitId {get;}
        public string OwnerPolicy {get;}
        public string StagedTransition {get;}
        // This immutable value is supplied by the owner only after paid policy.
        // It does not mutate the owner cursor; a matching committed receipt does.
        public MotionExecutionPermit(MotionExecutionProposal proposal,long permitId,string ownerPolicy,string stagedTransition)
        {Proposal=proposal??throw new ArgumentNullException(nameof(proposal));if(permitId<1)throw new ArgumentException("Invalid one-use permit.");PermitId=permitId;OwnerPolicy=MotionExecutionBinding.Name(ownerPolicy);StagedTransition=MotionExecutionBinding.Name(stagedTransition);}
    }
}
