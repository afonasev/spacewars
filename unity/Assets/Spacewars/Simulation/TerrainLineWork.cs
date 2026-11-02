using System;
using System.IO;
namespace Spacewars.Simulation
{
    // Fully detached native sample heights, not a mutable terrain/Domain delegate.
    // Preparation is explicit: each Append supplies ONE paid source element. The
    // arrays are admitted before birth and become private immutable input at Seal.
    public sealed class TerrainLineInput
    {
        internal readonly BallisticPoint From,To;
        internal readonly double Radius,WallHeight;
        internal readonly int Steps;
        readonly double[] heights;
        readonly NavObstacle[] solids;
        int heightCount,solidCount;
        bool sealedInput;
        public int HeightCount=>heights.Length;
        public int SolidCount=>solids.Length;
        public bool Ready=>sealedInput;
        // Explicit bulk preparation credit covers array zeroing plus one fill per
        // element; it is separate from query Advance grants and memory admission.
        public long PreparationWork=>2L*(heights.Length+solids.Length);
        TerrainLineInput(BallisticPoint from,BallisticPoint to,double radius,double wall,int steps,int count)
        {From=from;To=to;Radius=radius;WallHeight=wall;Steps=steps;heights=new double[steps+1];solids=new NavObstacle[count];}
        public static bool TryCreate(BallisticPoint from,BallisticPoint to,double radius,double wallHeight,int solids,long prepaidPreparation,Func<long,bool> reserve,out TerrainLineInput input)
        {
            input=null;if(reserve==null||solids<0||!Finite(from.X)||!Finite(from.Y)||!Finite(from.Z)||!Finite(to.X)||!Finite(to.Y)||!Finite(to.Z)||!Finite(radius)||radius<0||!Finite(wallHeight))throw new ArgumentException("Invalid line input.");
            double distance=Math.Sqrt(Math.Pow(to.X-from.X,2)+Math.Pow(to.Z-from.Z,2)+Math.Pow(to.Y-from.Y,2));double n=Math.Max(1,Math.Ceiling(distance/.025));if(n>=int.MaxValue)return false;
            long required=checked(2L*((int)n+1L+solids));if(prepaidPreparation<required)return false;
            long bytes=checked(256+8L*((int)n+1)+80L*solids);if(!reserve(bytes))return false;input=new TerrainLineInput(from,to,radius,wallHeight,(int)n,solids);return true;
        }
        static bool Finite(double v)=>!double.IsNaN(v)&&!double.IsInfinity(v);
        public void AppendHeight(double value){if(sealedInput||heightCount==heights.Length||!Finite(value))throw new ArgumentException("Invalid height sample.");heights[heightCount++]=value;}
        public void AppendSolid(NavObstacle value){if(sealedInput||solidCount==solids.Length||!Finite(value.MinX)||!Finite(value.MaxX)||!Finite(value.MinZ)||!Finite(value.MaxZ)||value.MinX>value.MaxX||value.MinZ>value.MaxZ)throw new ArgumentException("Invalid solid.");solids[solidCount++]=value;}
        public void Seal(){if(heightCount!=heights.Length||solidCount!=solids.Length)throw new InvalidOperationException("Incomplete source preparation.");sealedInput=true;}
        internal double Height(int i)=>heights[i];
        internal NavObstacle Solid(int i)=>solids[i];
        public BallisticPoint Sample(int i){if(i<0||i>Steps)throw new ArgumentOutOfRangeException(nameof(i));double t=(double)i/Steps;return new BallisticPoint(From.X+(To.X-From.X)*t,From.Y+(To.Y-From.Y)*t,From.Z+(To.Z-From.Z)*t);}
        // Exact immutable-input binding is streamed by the component codec, not
        // recomputed or copied during Advance. Current owner integration is separate.
        internal void WriteBinding(BinaryWriter w)
        {
            w.Write(From.X);w.Write(From.Y);w.Write(From.Z);w.Write(To.X);w.Write(To.Y);w.Write(To.Z);w.Write(Radius);w.Write(WallHeight);w.Write(Steps);w.Write(solids.Length);
            foreach(double h in heights)w.Write(h);
            foreach(var s in solids){w.Write(s.MinX);w.Write(s.MinZ);w.Write(s.MaxX);w.Write(s.MaxZ);w.Write(s.CircleRadius);w.Write(s.Bottom);w.Write(s.Top);w.Write(s.Polygon?.Vertices.Count??0);if(s.Polygon!=null)foreach(var p in s.Polygon.Vertices){w.Write(p.X);w.Write(p.Z);}}
        }
        internal void CheckBinding(BinaryReader r)
        {
            // The codec intentionally makes its linear input-comparison cost
            // explicit; it is never an Advance step or a free snapshot builder.
            using(var stream=new PredicateBindingComparison(r))using(var w=new BinaryWriter(stream,System.Text.Encoding.UTF8,true))WriteBinding(w);
        }
    }
    public sealed class TerrainLineWork
    {
        readonly TerrainLineInput input;
        int sample,solid,edge,phase;
        bool inside,boundary;
        double polygonDistance=double.PositiveInfinity;
        long consumed;
        bool clear=true;
        public CombatPredicateStatus Status {get;private set;}
        public bool Clear {get{if(Status!=CombatPredicateStatus.Complete)throw new InvalidOperationException("Line pending.");return clear;}}
        public long Consumed=>consumed;
        TerrainLineWork(TerrainLineInput input){this.input=input;}
        public static bool TryCreate(TerrainLineInput input,Func<long,bool> reserve,out TerrainLineWork work)
        {work=null;if(input==null||!input.Ready)throw new ArgumentException("Unprepared line snapshot.");if(reserve==null||!reserve(128))return false;work=new TerrainLineWork(input);return true;}
        public int Advance(int grant,Func<bool> admitStep=null)
        {if(grant<0)throw new ArgumentOutOfRangeException(nameof(grant));int used=0;while(used<grant&&Status==CombatPredicateStatus.Pending){if(admitStep!=null&&!admitStep())break;Step();consumed++;used++;}return used;}
        void Block(){clear=false;Status=CombatPredicateStatus.Complete;}
        void NextSolid(){solid++;phase=1;edge=0;inside=boundary=false;polygonDistance=double.PositiveInfinity;}
        void Step()
        {
            if(sample>input.Steps){Status=CombatPredicateStatus.Complete;return;}
            var p=input.Sample(sample);double radius=input.Radius;
            if(phase==0){if(p.Y-radius<input.Height(sample)-1e-5){Block();return;}phase=1;return;}
            if(solid==input.SolidCount){sample++;solid=0;phase=0;return;}
            var s=input.Solid(solid);
            if(phase==1){
                // Same conservative filter/order and widened bounds as native.
                double minX=Math.Min(input.From.X,input.To.X)-radius-1e-5,maxX=Math.Max(input.From.X,input.To.X)+radius+1e-5,minZ=Math.Min(input.From.Z,input.To.Z)-radius-1e-5,maxZ=Math.Max(input.From.Z,input.To.Z)+radius+1e-5;
                if(s.MaxX<minX||s.MinX>maxX||s.MaxZ<minZ||s.MinZ>maxZ||p.Y+radius<s.Bottom||p.Y-radius>(double.IsNaN(s.Top)?input.WallHeight:s.Top)||p.X+radius<s.MinX||p.X-radius>s.MaxX||p.Z+radius<s.MinZ||p.Z-radius>s.MaxZ){NextSolid();return;}
                if(s.Polygon==null){if(s.DistanceSquared(p.Ground)<=radius*radius)Block();else NextSolid();return;}
                phase=2;return;
            }
            var vertices=s.Polygon.Vertices;
            if(edge==vertices.Count){if((inside||boundary?0:polygonDistance)<=radius*radius)Block();else NextSolid();return;}
            // Native Contains uses previous→current edge order, then native
            // BoundaryDistanceSquared uses current→next. Accumulate both without
            // changing floating point edge direction or boundary tolerances.
            var a=vertices[(edge+vertices.Count-1)%vertices.Count];var b=vertices[edge];
            bool bounds=p.X>=s.MinX&&p.X<=s.MaxX&&p.Z>=s.MinZ&&p.Z<=s.MaxZ;
            if(bounds){if(p.X>=Math.Min(a.X,b.X)-1e-9&&p.X<=Math.Max(a.X,b.X)+1e-9&&p.Z>=Math.Min(a.Z,b.Z)-1e-9&&p.Z<=Math.Max(a.Z,b.Z)+1e-9&&Distance(p.Ground,a,b)<=1e-18)boundary=true;if((a.Z>p.Z)!=(b.Z>p.Z)&&p.X<(b.X-a.X)*(p.Z-a.Z)/(b.Z-a.Z)+a.X)inside=!inside;}
            polygonDistance=Math.Min(polygonDistance,Distance(p.Ground,b,vertices[(edge+1)%vertices.Count]));edge++;
        }
        static double Distance(NavPoint p,NavPoint a,NavPoint b){double x=b.X-a.X,z=b.Z-a.Z,t=Math.Max(0,Math.Min(1,((p.X-a.X)*x+(p.Z-a.Z)*z)/(x*x+z*z)));double dx=p.X-(a.X+t*x),dz=p.Z-(a.Z+t*z);return dx*dx+dz*dz;}
        public bool TryFork(Func<long,bool> reserve,out TerrainLineWork fork){fork=null;if(reserve==null||!reserve(128))return false;fork=(TerrainLineWork)MemberwiseClone();return true;}
        public void Write(BinaryWriter w){w.Write(1);input.WriteBinding(w);w.Write(sample);w.Write(solid);w.Write(edge);w.Write(phase);w.Write(inside);w.Write(boundary);w.Write(polygonDistance);w.Write(consumed);w.Write(clear);w.Write((int)Status);}
        public static TerrainLineWork Read(BinaryReader r,TerrainLineInput preparedInput,Func<long,bool> reserve)
        {
            if(reserve==null||!reserve(128))throw new InvalidOperationException("Cursor admission refused.");
            if(preparedInput==null||!preparedInput.Ready||r.ReadInt32()!=1)throw new ArgumentException("Invalid line cursor header.");preparedInput.CheckBinding(r);var work=new TerrainLineWork(preparedInput){sample=r.ReadInt32(),solid=r.ReadInt32(),edge=r.ReadInt32(),phase=r.ReadInt32(),inside=r.ReadBoolean(),boundary=r.ReadBoolean(),polygonDistance=r.ReadDouble(),consumed=r.ReadInt64(),clear=r.ReadBoolean(),Status=(CombatPredicateStatus)r.ReadInt32()};
            if(work.sample<0||work.sample>preparedInput.Steps+1||work.solid<0||work.solid>preparedInput.SolidCount||work.edge<0||work.phase<0||work.phase>2||work.consumed<0||double.IsNaN(work.polygonDistance)||work.polygonDistance<0||!Enum.IsDefined(typeof(CombatPredicateStatus),work.Status))throw new ArgumentException("Invalid line cursor.");
            if(work.Status==CombatPredicateStatus.Pending&&!work.clear||work.Status==CombatPredicateStatus.Complete&&work.clear&&work.sample!=preparedInput.Steps+1||work.sample==preparedInput.Steps+1&&(work.solid!=0||work.phase!=0)||work.Status==CombatPredicateStatus.Complete&&!work.clear&&(work.sample>preparedInput.Steps||work.solid>preparedInput.SolidCount)||work.phase!=2&&(work.edge!=0||work.inside||work.boundary||!double.IsPositiveInfinity(work.polygonDistance)))throw new ArgumentException("Impossible line phase/status.");
            if(work.phase==2&&(work.solid==preparedInput.SolidCount||preparedInput.Solid(work.solid).Polygon==null||work.edge>preparedInput.Solid(work.solid).Polygon.Vertices.Count))throw new ArgumentException("Invalid polygon cursor.");return work;
        }
    }
}
