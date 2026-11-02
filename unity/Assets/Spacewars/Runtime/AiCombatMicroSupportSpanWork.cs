using System;
using System.IO;
using Spacewars.Simulation;
namespace Spacewars.Runtime
{
    // Paid ownership proof for a complete typed span, including concave polygons
    // and earlier overlapping declarations. No live provider query is performed.
    internal sealed class AiCombatMicroSupportSpanWork:IDisposable
    {
        readonly AiCombatSupportCatalog catalog;readonly AiCombatMicroExecutionStorage storage;
        readonly NavLocation from,to;readonly string support;readonly double[] cuts;
        readonly AiCombatMicroExecutionStorage.Lease memory;
        AiCombatMicroExecutionStorage.Lease pointMemory;AiCombatMicroSupportWork point;
        int region,edge,count=2,sort=1,swap=1,interval=1,phase;bool complete,valid,disposed;
        internal bool Complete=>complete;internal bool Valid=>complete&&valid;
        internal long Consumed{get;private set;}
        AiCombatMicroSupportSpanWork(AiCombatSupportCatalog catalog,AiCombatMicroExecutionStorage storage,NavLocation from,NavLocation to,string support,double[] cuts,AiCombatMicroExecutionStorage.Lease memory)
        {this.catalog=catalog;this.storage=storage;this.from=from;this.to=to;this.support=support;this.cuts=cuts;this.memory=memory;cuts[1]=1;}
        internal static bool TryCreate(AiCombatSupportCatalog catalog,AiCombatMicroExecutionStorage storage,NavLocation from,NavLocation to,string support,ref long preparation,out AiCombatMicroSupportSpanWork work)
        {
            work=null;if(from.SurfaceId!=to.SurfaceId)throw new ArgumentException("A support span cannot flatten a portal.");
            long cost=8;if(preparation<cost)return false;int capacity=catalog.BoundaryCutCapacity;
            if(!storage.TryReserve(checked(2048L+8L*capacity),out var memory))return false;
            try{work=new AiCombatMicroSupportSpanWork(catalog,storage,from,to,support,new double[capacity],memory);preparation-=cost;return true;}catch{memory.Dispose();throw;}
        }
        internal int Advance(int grant)
        {if(disposed)throw new ObjectDisposedException(nameof(AiCombatMicroSupportSpanWork));if(grant<0)throw new ArgumentOutOfRangeException(nameof(grant));int used=0;while(used<grant&&!complete){if(!Step())break;used++;Consumed++;}return used;}
        void Finish(bool value){complete=true;valid=value;point=null;pointMemory?.Dispose();pointMemory=null;}
        NavPoint At(double t)=>t==0?from.Position:t==1?to.Position:new NavPoint(from.Position.X+(to.Position.X-from.Position.X)*t,from.Position.Z+(to.Position.Z-from.Position.Z)*t);
        bool Begin(NavPoint at,bool closure)
        {
            bool Reserve(long bytes)=>storage.TryReserve(bytes,out pointMemory);
            if(closure||catalog.Selector==AiCombatSupportSelector.UniqueSemanticSurface){
                var location=new NavLocation(at,from.SurfaceId);
                return support==null?AiCombatMicroSupportWork.TryCreateSemantic(catalog,location,Reserve,out point):AiCombatMicroSupportWork.TryCreateTyped(catalog,location,support,Reserve,out point);
            }
            return AiCombatMicroSupportWork.TryCreate(catalog,at,Reserve,out point);
        }
        bool PointValid()
        {
            if(!point.Supported)return false;
            if(catalog.Selector==AiCombatSupportSelector.Flat)return from.SurfaceId==NavLocation.FlatSurface;
            return point.HasResolvedSupport&&point.ResolvedSurface==from.SurfaceId&&(support==null||point.ResolvedSupport==support);
        }
        bool Step()
        {
            if(catalog.Selector==AiCombatSupportSelector.Unsupported){Finish(false);return true;}
            if(phase==0||phase==2){if(!Begin(phase==0?from.Position:to.Position,true))return false;phase++;return true;}
            if(phase==1||phase==3||phase==7){
                if(!point.Complete)return point.Advance(1)==1;
                if(!PointValid()){Finish(false);return true;}point=null;pointMemory.Dispose();pointMemory=null;
                if(phase==1)phase=2;else if(phase==3)phase=4;else{interval++;phase=6;}return true;
            }
            if(phase==4){
                if(from.Position.Equals(to.Position)){interval=count;phase=6;return true;}
                if(region==catalog.Count){phase=5;return true;}
                var bounds=catalog.At(region).Bounds;int vertices=bounds.Polygon?.Vertices.Count??4;
                NavPoint Vertex(int i)=>bounds.Polygon!=null?bounds.Polygon.Vertices[i]:new NavPoint(i==0||i==3?bounds.MinX:bounds.MaxX,i<2?bounds.MinZ:bounds.MaxZ);
                AddCuts(Vertex(edge),Vertex((edge+1)%vertices));if(++edge==vertices){edge=0;region++;}return true;
            }
            if(phase==5){
                // One insertion-sort comparison/swap per paid step. Preserve all
                // distinct cut values; zero-width intervals are skipped below.
                if(sort>=count){phase=6;return true;}
                if(swap>0&&cuts[swap]<cuts[swap-1]){double value=cuts[swap];cuts[swap]=cuts[swap-1];cuts[swap-1]=value;swap--;}
                else{sort++;swap=sort;}return true;
            }
            if(phase==6){
                if(interval==count){Finish(true);return true;}
                if(cuts[interval]<=cuts[interval-1]){interval++;return true;}
                if(!Begin(At((cuts[interval-1]+cuts[interval])/2),false))return false;phase=7;return true;
            }
            throw new InvalidOperationException("Invalid paid support span phase.");
        }
        void AddCuts(NavPoint a,NavPoint b)
        {
            double dx=to.Position.X-from.Position.X,dz=to.Position.Z-from.Position.Z,length=dx*dx+dz*dz;if(length==0)return;
            double ex=b.X-a.X,ez=b.Z-a.Z,den=dx*ez-dz*ex,ax=a.X-from.Position.X,az=a.Z-from.Position.Z;
            if(Math.Abs(den)>1e-12){double t=(ax*ez-az*ex)/den,u=(ax*dz-az*dx)/den;if(t>=0&&t<=1&&u>=0&&u<=1)cuts[count++]=t;}
            else if(Math.Abs(ax*dz-az*dx)<=1e-10){double t=(ax*dx+az*dz)/length;if(t>0&&t<1)cuts[count++]=t;t=((b.X-from.Position.X)*dx+(b.Z-from.Position.Z)*dz)/length;if(t>0&&t<1)cuts[count++]=t;}
        }
        internal void Write(BinaryWriter w)
        {
            w.Write(1);catalog.WriteBinding(w);WorldWire.Write(w,from.Position);WorldWire.String(w,from.SurfaceId);WorldWire.Write(w,to.Position);WorldWire.String(w,support);
            w.Write(region);w.Write(edge);w.Write(count);w.Write(sort);w.Write(swap);w.Write(interval);w.Write(phase);w.Write(complete);w.Write(valid);w.Write(Consumed);
            for(int i=0;i<count;i++)w.Write(cuts[i]);w.Write(point!=null);point?.Write(w);
        }
        internal static AiCombatMicroSupportSpanWork Read(BinaryReader r,AiCombatSupportCatalog catalog,AiCombatMicroExecutionStorage storage,NavLocation from,NavLocation to,string support,ref long preparation)
        {
            if(r.ReadInt32()!=1)throw new ArgumentException("Unknown support span.");catalog.CheckBinding(r);
            var a=WorldWire.ReadPoint(r);string surface=AiCombatMicroExecutionBindingWire.ReadName(r);var b=WorldWire.ReadPoint(r);string savedSupport=AiCombatMicroExecutionBindingWire.ReadName(r,true);
            if(!MotionExecutionBinding.Same(new NavLocation(a,surface),from)||!MotionExecutionBinding.Same(b.X,to.Position.X)||!MotionExecutionBinding.Same(b.Z,to.Position.Z)||savedSupport!=support)throw new ArgumentException("Foreign support span.");
            if(!TryCreate(catalog,storage,from,to,support,ref preparation,out var work))throw new InvalidOperationException("Cold support span admission refused.");
            try{
                work.region=r.ReadInt32();work.edge=r.ReadInt32();work.count=r.ReadInt32();work.sort=r.ReadInt32();work.swap=r.ReadInt32();work.interval=r.ReadInt32();work.phase=r.ReadInt32();work.complete=WorldWire.Boolean(r);work.valid=WorldWire.Boolean(r);work.Consumed=r.ReadInt64();
                if(work.region<0||work.region>catalog.Count||work.edge<0||work.region==catalog.Count&&work.edge!=0||work.region<catalog.Count&&work.edge>=(catalog.At(work.region).Bounds.Polygon?.Vertices.Count??4)||work.count<2||work.count>work.cuts.Length||work.sort<1||work.sort>work.count||work.swap<0||work.swap>work.sort||work.interval<1||work.interval>work.count||work.phase<0||work.phase>7||work.Consumed<0||work.valid&&!work.complete||work.complete&&work.valid&&(work.phase!=6||work.interval!=work.count))throw new ArgumentException("Impossible support span cursor.");
                for(int i=0;i<work.count;i++){double t=WorldWire.Number(r);if(t<0||t>1)throw new ArgumentException("Invalid support cut.");work.cuts[i]=t;}
                bool hasPoint=WorldWire.Boolean(r);if(hasPoint!=(!work.complete&&(work.phase==1||work.phase==3||work.phase==7)))throw new ArgumentException("Missing support point cursor.");
                if(work.phase>=6){if(work.cuts[0]!=0||work.cuts[work.count-1]!=1)throw new ArgumentException("Incomplete sorted cuts.");for(int i=1;i<work.count;i++)if(work.cuts[i]<work.cuts[i-1])throw new ArgumentException("Unsorted support cuts.");}
                if(hasPoint){work.point=AiCombatMicroSupportWork.Read(r,catalog,bytes=>storage.TryReserve(bytes,out work.pointMemory));var at=work.phase==1?from.Position:work.phase==3?to.Position:work.At((work.cuts[work.interval-1]+work.cuts[work.interval])/2);bool typed=work.phase!=7||catalog.Selector==AiCombatSupportSelector.UniqueSemanticSurface;if(!work.point.MatchesPoint(at,typed?support:null,typed?from.SurfaceId:null))throw new ArgumentException("Foreign nested support point.");}return work;
            }catch{work.Dispose();throw;}
        }
        public void Dispose(){if(disposed)return;disposed=true;pointMemory?.Dispose();memory.Dispose();}
    }
}
