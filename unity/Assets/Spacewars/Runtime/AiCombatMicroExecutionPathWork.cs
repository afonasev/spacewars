using System;
using System.IO;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;
namespace Spacewars.Runtime
{
    // A paid policy component, NOT a permit issuer. Its committed input cursor
    // must come from the owning once-receipt registry, never from a proposal.
    internal sealed class AiCombatMicroExecutionPathWork:IDisposable
    {
        internal readonly struct EscapeEntry
        {
            internal readonly int Tower;internal readonly bool Escaping;internal readonly double Distance;internal readonly NavPoint TowerPosition;
            internal EscapeEntry(int tower,bool escaping,double distance,NavPoint towerPosition){Tower=tower;Escaping=escaping;Distance=distance;TowerPosition=towerPosition;}
        }
        internal sealed class Cursor:IDisposable
        {
            internal readonly long World,Entity;internal readonly string Intent;internal readonly NavLocation At;internal readonly double? AirHeight;
            readonly EscapeEntry[] entries;readonly int count;readonly AiCombatMicroExecutionStorage storage;readonly AiCombatMicroExecutionStorage.Lease memory;internal bool Disposed{get;private set;}
            internal int Count=>Disposed?throw new ObjectDisposedException(nameof(Cursor)):count;internal EscapeEntry Entry(int i)=>Disposed?throw new ObjectDisposedException(nameof(Cursor)):i<0||i>=count?throw new ArgumentOutOfRangeException(nameof(i)):entries[i];
            internal Cursor(long world,long entity,string intent,NavLocation at,EscapeEntry[] entries,int count,AiCombatMicroExecutionStorage storage,AiCombatMicroExecutionStorage.Lease memory,double? airHeight=null){AirHeight=airHeight;this.storage=storage;this.count=count;World=world;Entity=entity;Intent=intent;At=at;this.entries=entries;this.memory=memory;}
            internal void Write(BinaryWriter w)
            {if(Disposed)throw new ObjectDisposedException(nameof(Cursor));if(!storage.TryReserve(8192,out var scratch))throw new InvalidOperationException("Escape codec scratch refused.");using(scratch){w.Write(2);w.Write(World);w.Write(Entity);WorldWire.String(w,Intent);WorldWire.Write(w,At.Position);WorldWire.String(w,At.SurfaceId);w.Write(AirHeight.HasValue);if(AirHeight.HasValue)w.Write(AirHeight.Value);w.Write(count);for(int i=0;i<count;i++)WriteEntry(w,entries[i]);}}
            internal static Cursor Read(BinaryReader r,AiCombatMicroExecutionStorage storage,ref long copyGrant)
            {
                if(copyGrant<32||!storage.TryReserve(8192,out var scratch))throw new InvalidOperationException("Escape codec admission refused.");using(scratch){
                    if(r.ReadInt32()!=2)throw new ArgumentException("Unknown escape cursor.");long world=r.ReadInt64(),entity=r.ReadInt64();string intent=AiCombatMicroExecutionBindingWire.ReadName(r);var point=new NavPoint(WorldWire.Number(r),WorldWire.Number(r));string surface=AiCombatMicroExecutionBindingWire.ReadName(r);double? airHeight=WorldWire.Boolean(r)?WorldWire.Number(r):(double?)null;int count=r.ReadInt32();if(world<1||entity<1||entity>int.MaxValue||count<0||count>WorldWire.MaxItems)throw new ArgumentException("Invalid escape cursor header.");long cost=checked(32L+2L*count);if(copyGrant<cost||!storage.TryReserve(checked(4096L+64L*count),out var memory))throw new InvalidOperationException("Escape cursor admission refused.");
                    try{var entries=new EscapeEntry[count];var at=new NavLocation(point,surface);for(int i=0;i<count;i++){var e=ReadEntry(r);if(e.Tower<=0||i>0&&e.Tower<=entries[i-1].Tower||!MotionExecutionBinding.Same(e.Distance,AiCombatMicroSafety.Distance(e.TowerPosition,point)))throw new ArgumentException("Invalid escape history entry.");entries[i]=e;}var result=new Cursor(world,entity,intent,at,entries,count,storage,memory,airHeight);copyGrant-=cost;return result;}catch{memory.Dispose();throw;}
                }
            }
            public void Dispose(){if(Disposed)return;Disposed=true;memory.Dispose();}
        }
        sealed class Threat
        {
            internal PlayableBuildingSnapshot Tower;internal int StageIndex;internal bool HasPrevious,PreviousEscaping,SeedEscaping,ActualEscaping,ForecastEscaping;
            internal double PreviousDistance,SeedDistance,ActualDistance,ForecastDistance;
        }
        readonly AiCombatMicroCoverScene scene;readonly AiCombatMicroExecutionStorage storage;readonly MotionExecutionPrimitive primitive;
        readonly PlayableEntitySnapshot unit;readonly Threat[] threats;readonly EscapeEntry[] actualEntries;
        readonly bool withdrawal;readonly string intent;readonly int entryCount,sourceCount,priorCount;readonly AiCombatMicroExecutionStorage.Lease memory,scenePin,catalogPin;
        AiCombatMicroSupportSpanWork span;int supportSide;
        AiCombatMicroCoverWork cover;int phase,validatePiece,validateSupport,lengthLeg,threat,piece,leg,grid,steps;
        bool closestDone,complete,allowed,unsupported,disposed,transferred;double closest;NavPoint sample;
        internal bool Complete=>complete;internal bool Supported=>!unsupported;
        internal bool Allowed{get{if(!complete||unsupported)throw new InvalidOperationException("Path verdict unavailable.");return allowed;}}
        internal long Consumed{get;private set;}internal double RemainingLength{get;private set;}
        AiCombatMicroExecutionPathWork(AiCombatMicroCoverScene scene,AiCombatMicroExecutionStorage storage,MotionExecutionPrimitive primitive,PlayableEntitySnapshot unit,Threat[] threats,EscapeEntry[] entries,int entryCount,bool withdrawal,string intent,int sourceCount,int priorCount,AiCombatMicroExecutionStorage.Lease memory,AiCombatMicroExecutionStorage.Lease scenePin,AiCombatMicroExecutionStorage.Lease catalogPin)
        {this.sourceCount=sourceCount;this.priorCount=priorCount;this.scenePin=scenePin;this.catalogPin=catalogPin;this.scene=scene;this.storage=storage;this.primitive=primitive;this.unit=unit;this.threats=threats;actualEntries=entries;this.entryCount=entryCount;this.withdrawal=withdrawal;this.intent=intent;this.memory=memory;}
        internal static bool TryCreate(AiCombatMicroCoverScene scene,AiCombatMicroExecutionStorage storage,MotionExecutionPrimitive primitive,PlayableAiObservation o,AiKnowledgeState knowledge,PlayableEntitySnapshot unit,bool withdrawal,string intent,Cursor committed,ref long inputGrant,out AiCombatMicroExecutionPathWork work)
        {
            work=null;if(storage==null)throw new ArgumentNullException(nameof(storage));if(inputGrant<64||!storage.TryReserve(512,out var scratch))return false;
            using(scratch)return TryCreateCore(scene,storage,primitive,o,knowledge,unit,withdrawal,intent,committed,ref inputGrant,out work);
        }
        static bool TryCreateCore(AiCombatMicroCoverScene scene,AiCombatMicroExecutionStorage storage,MotionExecutionPrimitive primitive,PlayableAiObservation o,AiKnowledgeState knowledge,PlayableEntitySnapshot unit,bool withdrawal,string intent,Cursor committed,ref long inputGrant,out AiCombatMicroExecutionPathWork work)
        {
            work=null;if(scene==null||storage==null||primitive==null||o==null||unit==null||unit.Id!=primitive.Binding.Entity||o.Generation!=primitive.Binding.World||o.OwnerId!=primitive.Binding.Owner)throw new ArgumentException("Foreign execution policy input.");MotionExecutionBinding.Name(intent);
            if(knowledge!=null&&(knowledge.OwnerId!=o.OwnerId||knowledge.Generation!=o.Generation||knowledge.ObservationTick>o.Tick))throw new ArgumentException("Foreign path threat knowledge.");
            if(committed!=null&&(committed.Disposed||committed.World!=o.Generation||committed.Entity!=unit.Id||committed.Intent!=intent||!MotionExecutionBinding.Same(committed.At,primitive.From)||committed.AirHeight.HasValue!=primitive.Air.HasValue||committed.AirHeight.HasValue&&!MotionExecutionBinding.Same(committed.AirHeight.Value,primitive.Air.Value.BeforeHeight)))throw new ArgumentException("Committed escape cursor does not match actual origin/intent.");
            int count=checked(o.Buildings.Count+(knowledge?.Contacts.Count??0));if(count>WorldWire.MaxItems)throw new ArgumentException("Path threat input too large.");long cost=checked(64L+4L*count+2L*count*count+2L*count*(committed?.Count??0)+2L*(committed?.Count??0));
            if(inputGrant<cost||!storage.TryReserve(checked(2048L+2048L*(count+(long)(committed?.Count??0))),out var memory))return false;
            if(!scene.TryRetain(out var scenePin,out var catalogPin)){memory.Dispose();return false;}
            try{
                // Explicit input preparation credits above cover native tower
                // selection/dedup, its temporary list and the detached row copies.
                inputGrant-=cost;var source=System.Linq.Enumerable.ToArray(AiCombatMicroSafety.Towers(o,knowledge));var rows=new Threat[source.Length];int used=committed?.Count??0;var entries=new EscapeEntry[checked(used+rows.Length)];for(int j=0;j<used;j++){var previous=committed.Entry(j);entries[j]=new EscapeEntry(previous.Tower,previous.Escaping,AiCombatMicroSafety.Distance(previous.TowerPosition,primitive.To.Position),previous.TowerPosition);}
                for(int i=0;i<rows.Length;i++){var tower=source[i];var row=new Threat{Tower=new PlayableBuildingSnapshot(tower.Id,tower.Owner,tower.Kind,tower.Position,tower.Health,1,0,0,default,phase:tower.Phase,includePrivateState:false)};if(committed!=null)for(int j=0;j<committed.Count;j++){var previous=committed.Entry(j);if(previous.Tower==tower.Id){if(!MotionExecutionBinding.Same(previous.TowerPosition.X,tower.Position.X)||!MotionExecutionBinding.Same(previous.TowerPosition.Z,tower.Position.Z))throw new ArgumentException("Static threat incarnation moved.");row.HasPrevious=true;row.StageIndex=j;row.PreviousEscaping=previous.Escaping;row.PreviousDistance=previous.Distance;break;}}if(!row.HasPrevious)row.StageIndex=used++;rows[i]=row;}
                var actor=new PlayableEntitySnapshot(unit.Id,unit.Owner,unit.Kind,primitive.From.Position,unit.Health,false,0,0,0,altitude:unit.Altitude);
                if(!storage.TryReserve(checked(2048L+2048L*(rows.Length+(long)(committed?.Count??0))),out var canonical)){memory.Dispose();scenePin.Dispose();catalogPin.Dispose();return false;}memory.Dispose();memory=canonical;work=new AiCombatMicroExecutionPathWork(scene,storage,primitive,actor,rows,entries,used,withdrawal,intent,rows.Length,committed?.Count??0,memory,scenePin,catalogPin);return true;
            }catch{memory.Dispose();scenePin.Dispose();catalogPin.Dispose();throw;}
        }
        void Finish(bool value,bool unsupported=false){allowed=value;this.unsupported=unsupported;complete=true;cover?.Dispose();cover=null;span?.Dispose();span=null;}
        internal int Advance(int grant,Func<bool> admit=null)
        {if(disposed)throw new ObjectDisposedException(nameof(AiCombatMicroExecutionPathWork));if(grant<0)throw new ArgumentOutOfRangeException(nameof(grant));int used=0;while(used<grant&&!complete){if(admit!=null&&!admit()||!Step())break;Consumed++;used++;}return used;}
        bool Step()
        {
            if(phase==0){
                if(!MotionExecutionBinding.Same(primitive.Radius,PlayableUnitRules.Radius(scene.Profile,unit.Kind))){Finish(false,true);return true;}
                if(primitive.Air.HasValue){
                    if(unit.Kind!=PlayableEntityKind.Hawk||primitive.TraceCount!=1||primitive.RemainingBranch!=null||primitive.TracePiece(0).Kind!=MotionExecutionTraceKind.Air||!primitive.TracePiece(0).Supported||!MotionExecutionBinding.Same(unit.Altitude,primitive.Air.Value.BeforeHeight)){Finish(false,true);return true;}
                    RemainingLength=AiCombatMicroSafety.Distance(primitive.From.Position,primitive.AssignedEndpoint.Value.Position);validatePiece=primitive.TraceCount;phase=2;return true;
                }
                if(unit.Kind==PlayableEntityKind.Hawk){Finish(false,true);return true;}
                if(primitive.Binding.HasOwnIntent&&primitive.RemainingBranch==null){Finish(false,true);return true;}
                if(validatePiece==primitive.TraceCount){phase=1;return true;}
                var trace=primitive.TracePiece(validatePiece);if(!trace.Supported){Finish(false,true);return true;}
                if(span!=null){if(!span.Complete)return span.Advance(1)==1;if(!span.Valid){Finish(false,true);return true;}span.Dispose();span=null;if(trace.Kind==MotionExecutionTraceKind.Portal&&supportSide==0){supportSide=1;return true;}validatePiece++;validateSupport=supportSide=0;return true;}
                if(trace.Kind==MotionExecutionTraceKind.Portal){var at=supportSide==0?trace.From:trace.To;return BeginSpan(at,at,null);}
                if(trace.Kind!=MotionExecutionTraceKind.Surface||validateSupport==scene.Supports.Count){Finish(false,true);return true;}var support=scene.Supports.At(validateSupport);
                if(support.Id!=trace.Support||support.SurfaceId!=trace.From.SurfaceId){validateSupport++;return true;}
                double constant=support.Height-support.Origin.X*support.Gradient.X-support.Origin.Z*support.Gradient.Z;
                if(!MotionExecutionBinding.Same(support.Gradient.X,trace.HeightX)||!MotionExecutionBinding.Same(support.Gradient.Z,trace.HeightZ)||!MotionExecutionBinding.Same(constant,trace.HeightConstant)){Finish(false,true);return true;}
                return BeginSpan(trace.From,trace.To,trace.Support);
            }
            if(phase==1){
                var branch=primitive.RemainingBranch;if(branch==null||lengthLeg==branch.Count){phase=2;return true;}var l=branch.Leg(lengthLeg);
                if(!Enum.IsDefined(typeof(NavTypedLegKind),l.Kind)||!MotionExecutionBinding.Number(l.Cost)||l.Cost<0||l.Kind==NavTypedLegKind.Surface&&l.From.SurfaceId!=l.To.SurfaceId||l.Kind==NavTypedLegKind.Portal&&(l.PortalId==null||!l.From.Position.Equals(l.To.Position))){Finish(false,true);return true;}
                if(span!=null){if(!span.Complete)return span.Advance(1)==1;if(!span.Valid){Finish(false,true);return true;}span.Dispose();span=null;if(l.Kind==NavTypedLegKind.Portal&&supportSide==0){supportSide=1;return true;}RemainingLength+=AiCombatMicroSafety.Distance(l.From.Position,l.To.Position);lengthLeg++;supportSide=0;return true;}
                var at=supportSide==0?l.From:l.To;return l.Kind==NavTypedLegKind.Portal?BeginSpan(at,at,null):BeginSpan(l.From,l.To,null);
            }
            if(threat==threats.Length){Finish(true);return true;}var row=threats[threat];
            if(phase==2){if(row.HasPrevious){row.SeedEscaping=row.PreviousEscaping;row.SeedDistance=row.PreviousDistance;phase=4;return true;}sample=primitive.From.Position;phase=13;return true;}
            if(phase==3){if(!cover.Complete)return cover.Advance(1)==1;if(!cover.Supported){Finish(false,true);return true;}row.SeedEscaping=withdrawal&&cover.Covered;row.SeedDistance=AiCombatMicroSafety.Distance(row.Tower.Position,primitive.From.Position);cover.Dispose();cover=null;phase=4;return true;}
            if(phase==4){row.ActualEscaping=row.SeedEscaping;row.ActualDistance=row.SeedDistance;piece=0;grid=steps=0;phase=5;return true;}
            if(phase==5){if(piece==primitive.TraceCount){sample=primitive.To.Position;phase=14;return true;}var trace=primitive.TracePiece(piece);if(trace.Kind==MotionExecutionTraceKind.Portal){if(steps==0){steps=2;grid=0;}if(grid==steps){piece++;steps=0;return true;}sample=trace.From.Position;grid++;}else{if(steps==0)PrepareSamples(row,trace.From.Position,trace.To.Position);if(!NextSample(trace.From.Position,trace.To.Position)){piece++;steps=0;return true;}}phase=7;return true;}
            if(phase==6){if(!cover.Complete)return cover.Advance(1)==1;if(!cover.Supported){Finish(false,true);return true;}double distance=AiCombatMicroSafety.Distance(row.Tower.Position,sample);if(cover.Covered&&(!row.ActualEscaping||distance<row.ActualDistance)){Finish(false);return true;}if(!cover.Covered)row.ActualEscaping=false;row.ActualDistance=distance;cover.Dispose();cover=null;phase=5;return true;}
            if(phase==8){var branch=primitive.RemainingBranch;if(branch==null||leg==branch.Count){if(branch!=null){sample=branch.AssignedEndpoint.Position;phase=12;return true;}threat++;phase=2;return true;}var l=branch.Leg(leg);if(l.Kind==NavTypedLegKind.Portal){if(steps==0){steps=2;grid=0;}if(grid==steps){leg++;steps=0;return true;}sample=l.From.Position;grid++;}else{if(steps==0)PrepareSamples(row,l.From.Position,l.To.Position);if(!NextSample(l.From.Position,l.To.Position)){leg++;steps=0;return true;}}phase=11;return true;}
            if(phase==9){if(!cover.Complete)return cover.Advance(1)==1;if(!cover.Supported){Finish(false,true);return true;}double distance=AiCombatMicroSafety.Distance(row.Tower.Position,sample);if(cover.Covered&&(!row.ForecastEscaping||distance<row.ForecastDistance)){Finish(false);return true;}if(!cover.Covered)row.ForecastEscaping=false;row.ForecastDistance=distance;cover.Dispose();cover=null;phase=8;return true;}
            if(phase==7){if(!BeginCover(row,1))return false;phase=6;return true;}
            if(phase==11){if(!BeginCover(row,2))return false;phase=9;return true;}
            if(phase==12){if(!BeginCover(row,3))return false;phase=10;return true;}
            if(phase==13){if(!BeginCover(row,0))return false;phase=3;return true;}
            if(phase==14){if(!BeginCover(row,4))return false;phase=15;return true;}
            if(phase==15){if(!cover.Complete)return cover.Advance(1)==1;if(!cover.Supported){Finish(false,true);return true;}double distance=AiCombatMicroSafety.Distance(row.Tower.Position,primitive.To.Position);if(cover.Covered&&(!row.ActualEscaping||distance<row.ActualDistance)){Finish(false);return true;}if(!cover.Covered)row.ActualEscaping=false;row.ActualDistance=distance;cover.Dispose();cover=null;actualEntries[row.StageIndex]=new EscapeEntry(row.Tower.Id,row.ActualEscaping,row.ActualDistance,row.Tower.Position);row.ForecastEscaping=row.SeedEscaping;row.ForecastDistance=row.SeedDistance;leg=0;grid=steps=0;phase=8;return true;}
            if(phase==10){if(!cover.Complete)return cover.Advance(1)==1;if(!cover.Supported){Finish(false,true);return true;}if(cover.Covered){Finish(false);return true;}cover.Dispose();cover=null;threat++;phase=2;return true;}
            throw new InvalidOperationException("Invalid path phase.");
        }
        bool BeginSpan(NavLocation from,NavLocation to,string support)
        {long preparation=8;return AiCombatMicroSupportSpanWork.TryCreate(scene.Supports,storage,from,to,support,ref preparation,out span);}
        void PrepareSamples(Threat row,NavPoint from,NavPoint to)
        {double length=AiCombatMicroSafety.Distance(from,to),dx=to.X-from.X,dz=to.Z-from.Z;double count=Math.Max(1,Math.Ceiling(length/primitive.Radius));if(count>=int.MaxValue)throw new InvalidOperationException("Path sample count exceeds the native integer cursor.");steps=(int)count;grid=1;closestDone=false;closest=length==0?0:Math.Max(0,Math.Min(1,((row.Tower.Position.X-from.X)*dx+(row.Tower.Position.Z-from.Z)*dz)/(length*length)));}
        bool NextSample(NavPoint from,NavPoint to)
        {if(grid>steps&&closestDone)return false;double t;if(!closestDone&&(grid>steps||closest<grid/(double)steps)){t=closest;closestDone=true;}else{t=grid++/(double)steps;if(!closestDone&&closest==t)closestDone=true;}sample=t==1?to:new NavPoint(from.X+(to.X-from.X)*t,from.Z+(to.Z-from.Z)*t);return true;}
        bool BeginCover(Threat row,int mode)
        {
            if(primitive.Air.HasValue){
                // Actual Native law: the new height is assigned before the final
                // XZ sweep, including lateral fallback. Never recompute nominal
                // clearance or interpolate an invented terrain/flight plane.
                double height=mode==0?primitive.Air.Value.BeforeHeight:primitive.Air.Value.AssignedHeight;
                var atHeight=new PlayableEntitySnapshot(unit.Id,unit.Owner,unit.Kind,sample,unit.Health,false,0,0,0,altitude:height);
                return AiCombatMicroCoverWork.TryCreate(scene,storage,row.Tower,atHeight,sample,out cover);
            }
            if(mode<=1||mode==4){var trace=primitive.TracePiece(mode==0?0:mode==4?primitive.TraceCount-1:piece);if(trace.Kind==MotionExecutionTraceKind.Portal){string surface=mode==4||mode==1&&grid==2?trace.To.SurfaceId:trace.From.SurfaceId;return AiCombatMicroCoverWork.TryCreateSemanticTarget(scene,storage,row.Tower,unit,new NavLocation(sample,surface),out cover);}return AiCombatMicroCoverWork.TryCreateTypedTarget(scene,storage,row.Tower,unit,new NavLocation(sample,trace.From.SurfaceId),trace.Support,out cover);}
            var branch=primitive.RemainingBranch;string suffixSurface=mode==3?branch.AssignedEndpoint.SurfaceId:branch.Leg(leg).Kind==NavTypedLegKind.Portal&&grid==2?branch.Leg(leg).To.SurfaceId:branch.Leg(leg).From.SurfaceId;return AiCombatMicroCoverWork.TryCreateSemanticTarget(scene,storage,row.Tower,unit,new NavLocation(sample,suffixSurface),out cover);
        }
        internal long CursorPreparationWork{get{int depth=0;for(int n=entryCount;n>0;n>>=1)depth++;return checked(entryCount*(8L+8L*depth));}}
        static void WriteEntry(BinaryWriter w,EscapeEntry entry){w.Write(entry.Tower);w.Write(entry.Escaping);w.Write(entry.Distance);WorldWire.Write(w,entry.TowerPosition);}
        static EscapeEntry ReadEntry(BinaryReader r)=>new EscapeEntry(r.ReadInt32(),WorldWire.Boolean(r),WorldWire.Number(r),new NavPoint(WorldWire.Number(r),WorldWire.Number(r)));
        internal bool TryAcquireActualCursor(ref long copyGrant,out Cursor cursor)
        {
            cursor=null;if(disposed)throw new ObjectDisposedException(nameof(AiCombatMicroExecutionPathWork));if(!complete||unsupported||!allowed||transferred)throw new InvalidOperationException("No unclaimed actual transition.");
            long cost=CursorPreparationWork;if(copyGrant<cost||!storage.TryReserve(checked(4096L+64L*entryCount),out var pin))return false;
            try{var copy=new EscapeEntry[entryCount];for(int i=0;i<copy.Length;i++)copy[i]=actualEntries[i];Array.Sort(copy,(a,b)=>a.Tower.CompareTo(b.Tower));for(int i=0;i<copy.Length;i++)if(copy[i].Tower<=0||i>0&&copy[i-1].Tower==copy[i].Tower)throw new ArgumentException("Duplicate or invalid escape threat.");cursor=new Cursor(primitive.Binding.World,primitive.Binding.Entity,intent,new NavLocation(primitive.To.Position,primitive.To.SurfaceId),copy,copy.Length,storage,pin,primitive.Air?.AssignedHeight);copyGrant-=cost;transferred=true;return true;}catch{pin.Dispose();throw;}
        }
        // Exact work continuation. Inputs are compared to freshly retained cold
        // source roots; no completed Covers query or accepted decision is replayed.
        internal void Write(BinaryWriter w)
        {
            if(disposed)throw new ObjectDisposedException(nameof(AiCombatMicroExecutionPathWork));w.Write(3);scene.WriteBinding(w);AiCombatMicroExecutionBindingWire.WritePrimitive(w,primitive);
            w.Write(withdrawal);WorldWire.String(w,intent);w.Write(unit.Id);w.Write((int)unit.Owner);w.Write((int)unit.Kind);w.Write(unit.Health);w.Write(unit.Altitude);w.Write(sourceCount);w.Write(priorCount);
            w.Write(threats.Length);foreach(var t in threats){var b=t.Tower;w.Write(b.Id);w.Write((int)b.Owner);w.Write((int)b.Kind);WorldWire.Write(w,b.Position);w.Write(b.Health);w.Write((int)b.Phase);w.Write(t.StageIndex);w.Write(t.HasPrevious);w.Write(t.PreviousEscaping);w.Write(t.SeedEscaping);w.Write(t.ActualEscaping);w.Write(t.ForecastEscaping);w.Write(t.PreviousDistance);w.Write(t.SeedDistance);w.Write(t.ActualDistance);w.Write(t.ForecastDistance);}
            w.Write(actualEntries.Length);w.Write(entryCount);foreach(var e in actualEntries)WriteEntry(w,e);
            w.Write(phase);w.Write(validatePiece);w.Write(validateSupport);w.Write(lengthLeg);w.Write(threat);w.Write(piece);w.Write(leg);w.Write(grid);w.Write(steps);w.Write(closestDone);w.Write(closest);WorldWire.Write(w,sample);w.Write(complete);w.Write(allowed);w.Write(unsupported);w.Write(transferred);w.Write(RemainingLength);w.Write(Consumed);w.Write(cover!=null);if(cover!=null)cover.Write(w);w.Write(supportSide);w.Write(span!=null);span?.Write(w);
        }
        internal static AiCombatMicroExecutionPathWork Read(BinaryReader r,AiCombatMicroCoverScene scene,AiCombatMicroExecutionStorage storage,MotionExecutionPrimitive primitive,ref long lifecycleGrant)
        {
            if(r.ReadInt32()!=3)throw new ArgumentException("Unknown execution path cursor.");scene.CheckBinding(r);AiCombatMicroExecutionBindingWire.CheckPrimitive(r,primitive);
            bool withdrawal=WorldWire.Boolean(r);string intent=AiCombatMicroExecutionBindingWire.ReadName(r);int id=r.ReadInt32();var owner=WorldWire.EnumValue<PlayableOwner>(r);var kind=WorldWire.EnumValue<PlayableEntityKind>(r);int health=r.ReadInt32();double altitude=WorldWire.Number(r);int sourceCount=r.ReadInt32(),priorCount=r.ReadInt32();long bytes=checked(2048L+2048L*(sourceCount+(long)priorCount));
            int count=r.ReadInt32();if(sourceCount<0||sourceCount>WorldWire.MaxItems||priorCount<0||priorCount>WorldWire.MaxItems||count<0||count!=sourceCount||bytes<2048||bytes>WorldWire.MaxBytes||lifecycleGrant<32L+8L*count||!storage.TryReserve(bytes,out var memory))throw new InvalidOperationException("Path cold admission refused.");
            AiCombatMicroExecutionStorage.Lease scenePin=null,catalogPin=null;AiCombatMicroExecutionPathWork work=null;
            try{
                if(!scene.TryRetain(out scenePin,out catalogPin))throw new InvalidOperationException("Path source retention refused.");var rows=new Threat[count];
                for(int i=0;i<count;i++){int tower=r.ReadInt32();var faction=WorldWire.EnumValue<PlayableOwner>(r);var building=WorldWire.EnumValue<PlayableBuildingKind>(r);var point=WorldWire.ReadPoint(r);int hp=r.ReadInt32();var construction=WorldWire.EnumValue<ConstructionPhase>(r);rows[i]=new Threat{Tower=new PlayableBuildingSnapshot(tower,faction,building,point,hp,1,0,0,default,phase:construction,includePrivateState:false),StageIndex=r.ReadInt32(),HasPrevious=WorldWire.Boolean(r),PreviousEscaping=WorldWire.Boolean(r),SeedEscaping=WorldWire.Boolean(r),ActualEscaping=WorldWire.Boolean(r),ForecastEscaping=WorldWire.Boolean(r),PreviousDistance=WorldWire.Number(r),SeedDistance=WorldWire.Number(r),ActualDistance=WorldWire.Number(r),ForecastDistance=WorldWire.Number(r)};}
                int capacity=r.ReadInt32(),used=r.ReadInt32();if(capacity!=priorCount+count||capacity>WorldWire.MaxItems||used<priorCount||used>capacity||lifecycleGrant<32L+8L*count+2L*capacity||bytes<2048L+2048L*Math.Max(count,capacity-count))throw new ArgumentException("Invalid path cold capacity.");var entries=new EscapeEntry[capacity];for(int i=0;i<capacity;i++)entries[i]=ReadEntry(r);
                foreach(var row in rows)if(row.Tower.Id<=0||row.StageIndex<0||row.StageIndex>=used)throw new ArgumentException("Invalid staged tower slot.");
                var actor=new PlayableEntitySnapshot(id,owner,kind,primitive.From.Position,health,false,0,0,0,altitude:altitude);if(id!=primitive.Binding.Entity)throw new ArgumentException("Foreign path actor.");
                work=new AiCombatMicroExecutionPathWork(scene,storage,primitive,actor,rows,entries,used,withdrawal,intent,sourceCount,priorCount,memory,scenePin,catalogPin);
                work.phase=r.ReadInt32();work.validatePiece=r.ReadInt32();work.validateSupport=r.ReadInt32();work.lengthLeg=r.ReadInt32();work.threat=r.ReadInt32();work.piece=r.ReadInt32();work.leg=r.ReadInt32();work.grid=r.ReadInt32();work.steps=r.ReadInt32();work.closestDone=WorldWire.Boolean(r);work.closest=WorldWire.Number(r);work.sample=WorldWire.ReadPoint(r);work.complete=WorldWire.Boolean(r);work.allowed=WorldWire.Boolean(r);work.unsupported=WorldWire.Boolean(r);work.transferred=WorldWire.Boolean(r);work.RemainingLength=WorldWire.Number(r);work.Consumed=r.ReadInt64();bool hasCover=WorldWire.Boolean(r);
                if(work.phase<0||work.phase>15||work.validatePiece<0||work.validatePiece>primitive.TraceCount||work.validateSupport<0||work.validateSupport>scene.Supports.Count||work.lengthLeg<0||work.lengthLeg>(primitive.RemainingBranch?.Count??0)||work.threat<0||work.threat>count||work.piece<0||work.piece>primitive.TraceCount||work.leg<0||work.leg>(primitive.RemainingBranch?.Count??0)||work.grid<0||work.steps<0||work.grid>(long)work.steps+1||work.closest<0||work.closest>1||work.RemainingLength<0||work.Consumed<0||work.allowed&&(!work.complete||work.unsupported)||work.transferred&&!work.allowed||hasCover&&(work.complete||work.threat>=count))throw new ArgumentException("Impossible path work cursor.");
                if(work.allowed&&(work.phase!=2||work.threat!=count||work.validatePiece!=primitive.TraceCount||work.lengthLeg!=(primitive.RemainingBranch?.Count??0))||!work.complete&&(work.unsupported||work.allowed||work.transferred)||hasCover!=(!work.complete&&(work.phase==3||work.phase==6||work.phase==9||work.phase==10||work.phase==15))||work.phase>=2&&(work.validatePiece!=primitive.TraceCount||work.lengthLeg!=(primitive.RemainingBranch?.Count??0)))throw new ArgumentException("Unproven path phase/completion.");
                if(hasCover&&primitive.Air.HasValue){double height=work.phase==3?primitive.Air.Value.BeforeHeight:primitive.Air.Value.AssignedHeight;work.cover=AiCombatMicroCoverWork.Read(r,scene,storage,airHeight:height,airPoint:work.sample);}
                else if(hasCover){string surface,support=null;int mode=work.phase;if(mode==3||mode==6||mode==15){var t=primitive.TracePiece(mode==3?0:mode==15?primitive.TraceCount-1:work.piece);surface=t.Kind==MotionExecutionTraceKind.Portal&&(mode==15||mode==6&&work.grid==2)?t.To.SurfaceId:t.From.SurfaceId;support=t.Kind==MotionExecutionTraceKind.Portal?null:t.Support;}else if(mode==9){var l=primitive.RemainingBranch.Leg(work.leg);surface=l.Kind==NavTypedLegKind.Portal&&work.grid==2?l.To.SurfaceId:l.From.SurfaceId;}else if(mode==10)surface=primitive.RemainingBranch.AssignedEndpoint.SurfaceId;else throw new ArgumentException("Unexpected active coverage phase.");work.cover=AiCombatMicroCoverWork.Read(r,scene,storage,new NavLocation(work.sample,surface),support);}
                work.supportSide=r.ReadInt32();bool hasSpan=WorldWire.Boolean(r);if(work.supportSide<0||work.supportSide>1||hasSpan&&(work.complete||work.phase>1)||!hasSpan&&work.phase>1&&work.supportSide!=0)throw new ArgumentException("Invalid support span phase.");
                if(hasSpan){NavLocation from,to;string support=null;if(work.phase==0){if(work.validatePiece>=primitive.TraceCount)throw new ArgumentException("Missing trace for span.");var t=primitive.TracePiece(work.validatePiece);if(t.Kind==MotionExecutionTraceKind.Portal){from=to=work.supportSide==0?t.From:t.To;}else{from=t.From;to=t.To;support=t.Support;}}else{if(primitive.RemainingBranch==null||work.lengthLeg>=primitive.RemainingBranch.Count)throw new ArgumentException("Missing suffix for span.");var l=primitive.RemainingBranch.Leg(work.lengthLeg);if(l.Kind==NavTypedLegKind.Portal){from=to=work.supportSide==0?l.From:l.To;}else{from=l.From;to=l.To;}}work.span=AiCombatMicroSupportSpanWork.Read(r,scene.Supports,storage,from,to,support,ref lifecycleGrant);}
                lifecycleGrant-=32L+8L*count+2L*capacity;return work;
            }catch{if(work!=null)work.Dispose();else{memory.Dispose();scenePin?.Dispose();catalogPin?.Dispose();}throw;}
        }
        public void Dispose(){if(disposed)return;disposed=true;cover?.Dispose();span?.Dispose();memory.Dispose();scenePin.Dispose();catalogPin.Dispose();}
    }
}
