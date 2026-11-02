using System;
using System.IO;

namespace Spacewars.Simulation
{
    // Streaming exact input comparison: no full snapshot buffer/hash/replay.
    internal sealed class PredicateBindingComparison : Stream
    {
        readonly BinaryReader reader;
        internal PredicateBindingComparison(BinaryReader reader){this.reader=reader;}
        public override void Write(byte[] buffer,int offset,int count){for(int i=0;i<count;i++)WriteByte(buffer[offset+i]);}
        public override void WriteByte(byte value){if(reader.ReadByte()!=value)throw new ArgumentException("Predicate input binding mismatch.");}
        public override bool CanRead=>false;public override bool CanSeek=>false;public override bool CanWrite=>true;
        public override long Length=>throw new NotSupportedException();public override long Position{get=>throw new NotSupportedException();set=>throw new NotSupportedException();}
        public override void Flush(){}public override int Read(byte[] b,int o,int c)=>throw new NotSupportedException();public override long Seek(long o,SeekOrigin s)=>throw new NotSupportedException();public override void SetLength(long n)=>throw new NotSupportedException();
    }
    public enum CombatPredicateStatus { Pending, Complete }

    // A step is one part decision, BVH node, or triangle query. Triangle math
    // contains only fixed-size operations (three vertices/edges, two face sides).
    // No geometry decode, model construction or caller callback occurs in Advance.
    public sealed class CombatPoseWork
    {
        readonly BuildingCombatPose pose;
        readonly BallisticPoint from,to;
        readonly double radius;
        readonly bool overlap;
        int root,part,phase,leaf,winding;
        ulong path;
        double best=double.PositiveInfinity,rootBest=double.PositiveInfinity,partBest=double.PositiveInfinity;
        bool hit,worldPass;
        long consumed;
        // phase: 0 part; 1 sweep nodes; 2 sweep leaf; 3 motion2 triangles;
        // 4 nearest nodes; 5 nearest leaf; 6 winding start; 7 winding nodes;
        // 8 winding leaf; 9 sentry world triangles.
        public CombatPredicateStatus Status {get;private set;}
        public double Fraction {get {RequireComplete();return best;}}
        public bool Hit {get {RequireComplete();return hit;}}
        public BallisticPoint Contact {get {RequireComplete();return double.IsPositiveInfinity(best)?to:new BallisticPoint(from.X+(to.X-from.X)*best,from.Y+(to.Y-from.Y)*best,from.Z+(to.Z-from.Z)*best);}}
        public long Consumed=>consumed;
        public const long RetainedBytes=512; // no variable traversal stack or copied mesh
        internal CombatPoseWork(BuildingCombatPose pose,BallisticPoint from,BallisticPoint to,double radius,bool overlap)
        {if(!pose.Prepared||!Finite(from)||!Finite(to)||!Finite(radius)||radius<0)throw new ArgumentException("Unprepared pose or invalid query.");this.pose=pose;this.from=from;this.to=to;this.radius=radius;this.overlap=overlap;}
        static bool Finite(double n)=>!double.IsNaN(n)&&!double.IsInfinity(n);
        static bool Finite(BallisticPoint p)=>Finite(p.X)&&Finite(p.Y)&&Finite(p.Z);
        void RequireComplete(){if(Status!=CombatPredicateStatus.Complete)throw new InvalidOperationException("Predicate pending.");}
        public int Advance(int grant,Func<bool> admitStep=null)
        {if(grant<0)throw new ArgumentOutOfRangeException(nameof(grant));int used=0;while(used<grant&&Status==CombatPredicateStatus.Pending){if(admitStep!=null&&!admitStep())break;Step();used++;consumed++;}return used;}
        CombatMesh Mesh=>pose.MeshAt(root);
        CombatPart Part=>Mesh.Parts[part];
        V A=>pose.QueryLocal(from,root)-CombatPart.Stretch(Part.Shift(pose.Opening,pose.Platform),pose.QueryHeight);
        V B=>pose.QueryLocal(to,root)-CombatPart.Stretch(Part.Shift(pose.Opening,pose.Platform),pose.QueryHeight);
        double R=>overlap&&pose.WorldTriangles(radius)&&!worldPass?0:pose.QueryRadius(radius);
        // Balanced authored trees have <=2,000,000 triangles, eight/leaf: depth
        // <=18. Enforce a generous 32-bit path ceiling on hydration and lookup.
        CombatNode Node()
        {var n=Part.Tree;ulong mask=1;while(mask<=path/2)mask<<=1;for(mask>>=1;mask!=0;mask>>=1){if(n.Left==null)throw new ArgumentException("Invalid BVH path.");n=(path&mask)==0?n.Left:n.Right;}return n;}
        static ulong Next(ulong p){while(p>1&&(p&1)!=0)p>>=1;return p==1?0:p+1;}
        void NextPart(){
            // Native mesh combines completed parts with an ordered comparison,
            // ignoring a NaN part. Do not merge individual triangle values here.
            if(!overlap&&!pose.WorldTriangles(radius)&&partBest<rootBest)rootBest=partBest;
            part++;phase=0;path=0;leaf=0;partBest=double.PositiveInfinity;winding=0;}
        void Finish(){Status=CombatPredicateStatus.Complete;hit=overlap?hit:!double.IsPositiveInfinity(best);}
        void Step()
        {
            if(root==pose.RootCount){Finish();return;}
            if(part==Mesh.Parts.Length){if(overlap&&pose.WorldTriangles(radius)&&!worldPass){worldPass=true;part=0;phase=0;}else{
                // Sentry combines complete Mesh.Segment roots via Math.Min;
                // construction world-triangle queries already accumulated above.
                if(!overlap&&!pose.WorldTriangles(radius))best=Math.Min(best,rootBest);
                rootBest=double.PositiveInfinity;root++;part=0;phase=0;worldPass=false;
            }return;}
            var p=Part;
            if(phase==0){
                if(pose.WorldTriangles(radius)&&(!overlap||worldPass)){
                    phase=9;leaf=0;return;
                }
                if(!p.Active(pose.Opening,Mesh.Roller)){NextPart();return;}
                if(overlap){if(p.Motion!=2&&p.Tree.Distance2(A,pose.QueryHeight)>R*R){NextPart();return;}phase=4;}
                else phase=1;
                path=1;leaf=0;partBest=double.PositiveInfinity;
                if(p.Motion==2){phase=3;leaf=0;}
                return;
            }
            if(phase==9){
                if(leaf==p.Triangles.Length){NextPart();return;}
                var t=pose.WorldTriangle(p.Triangles[leaf++],root);
                if(overlap){if((t.Closest(new V(from.X,from.Y,from.Z))-new V(from.X,from.Y,from.Z)).Length2<=radius*radius){hit=true;Finish();}}
                else best=Math.Min(best,t.Sweep(new V(from.X,from.Y,from.Z),new V(to.X,to.Y,to.Z),radius));
                return;
            }

            if(phase==3){
                if(leaf==p.Triangles.Length){if(overlap){if(partBest<=R*R+1e-20){hit=true;Finish();}else NextPart();}else NextPart();return;}
                var t=p.Triangle(leaf++,pose.Opening,pose.QueryHeight);
                if(overlap){double d=(t.Closest(A)-A).Length2;if(d<partBest)partBest=d;}else partBest=Math.Min(partBest,t.Sweep(A,B,R));
                return;
            }
            if(phase==6){
                var a=A;var q=new V(a.X,a.Y/pose.QueryHeight,a.Z);
                if(p.Motion==2||p.Tree.Distance2(q)>0){NextPart();return;}
                phase=7;path=1;winding=0;return;
            }
            if(path==0){
                if(phase==4){if(partBest<=R*R+1e-20){hit=true;Finish();}else phase=6;}
                else if(phase==7){if(winding!=0){hit=true;Finish();}else NextPart();}
                else NextPart();
                return;
            }
            var n=Node();
            if(phase==1||phase==4||phase==7){
                bool pass;
                if(phase==1)pass=n.Segment(A,B,R,Math.Min(1,partBest),pose.QueryHeight);
                else if(phase==4)pass=n.Distance2(A,pose.QueryHeight)<=partBest;
                else{var a=A;var q=new V(a.X,a.Y/pose.QueryHeight,a.Z);var d=new V(1,.3713906763541037,.217173291)*((p.Tree.Max-p.Tree.Min).Length2+1);pass=n.Segment(q,q+d,0);}
                if(!pass){path=Next(path);return;}
                if(n.Left!=null){path*=2;return;}
                leaf=n.Start;phase=phase==1?2:phase==4?5:8;return;
            }
            if(leaf==n.Start+n.Count){path=Next(path);phase=phase==2?1:phase==5?4:7;return;}
            int triangle=p.Order[leaf++];
            if(phase==2){partBest=Math.Min(partBest,p.Triangle(triangle,0,pose.QueryHeight).Sweep(A,B,R));}
            else if(phase==5){double d=(p.Triangle(triangle,0,pose.QueryHeight).Closest(A)-A).Length2;if(d<partBest)partBest=d;}
            else{var a=A;var q=new V(a.X,a.Y/pose.QueryHeight,a.Z);var d=new V(1,.3713906763541037,.217173291)*((p.Tree.Max-p.Tree.Min).Length2+1);var t=p.Triangles[triangle];if(t.Ray(q,d,out _))winding+=V.Dot(V.Cross(t.B-t.A,t.C-t.A),d)>0?1:-1;}
        }
        public bool TryFork(Func<long,bool> reserve,out CombatPoseWork fork){fork=null;if(reserve==null||!reserve(RetainedBytes))return false;fork=(CombatPoseWork)MemberwiseClone();return true;}
        // Codec is linear in a fixed-size cursor and input binding. Caller owns
        // the immutable prepared asset/pose, as on a cold current-format restore.
        public void Write(BinaryWriter w)
        {w.Write(2);pose.WriteQueryBinding(w);Point(w,from);Point(w,to);w.Write(radius);w.Write(overlap);w.Write(root);w.Write(part);w.Write(phase);w.Write(leaf);w.Write(winding);w.Write(path);w.Write(best);w.Write(rootBest);w.Write(partBest);w.Write(hit);w.Write(worldPass);w.Write((int)Status);w.Write(consumed);}
        static void Point(BinaryWriter w,BallisticPoint p){w.Write(p.X);w.Write(p.Y);w.Write(p.Z);}
        static BallisticPoint Point(BinaryReader r)=>new BallisticPoint(r.ReadDouble(),r.ReadDouble(),r.ReadDouble());
        public static CombatPoseWork Read(BinaryReader r,BuildingCombatPose preparedPose,Func<long,bool> reserve)
        {
            if(reserve==null||!reserve(RetainedBytes))throw new InvalidOperationException("Cursor admission refused.");
            if(r.ReadInt32()!=2)throw new ArgumentException("Unknown predicate cursor.");preparedPose.ReadQueryBinding(r);
            var work=new CombatPoseWork(preparedPose,Point(r),Point(r),r.ReadDouble(),r.ReadBoolean());
            work.root=r.ReadInt32();work.part=r.ReadInt32();work.phase=r.ReadInt32();work.leaf=r.ReadInt32();work.winding=r.ReadInt32();work.path=r.ReadUInt64();work.best=r.ReadDouble();work.rootBest=r.ReadDouble();work.partBest=r.ReadDouble();work.hit=r.ReadBoolean();work.worldPass=r.ReadBoolean();work.Status=(CombatPredicateStatus)r.ReadInt32();work.consumed=r.ReadInt64();
            if(work.root<0||work.root>preparedPose.RootCount||work.part<0||work.phase<0||work.phase>9||work.leaf<0||work.path>uint.MaxValue||work.consumed<0||(double.IsNaN(work.best)&&(!preparedPose.WorldTriangles(work.radius)||work.overlap))||work.best<0||double.IsNaN(work.rootBest)||work.rootBest<0||(work.rootBest>1&&!double.IsPositiveInfinity(work.rootBest))||(double.IsNaN(work.partBest)&&(work.overlap||work.phase<1||work.phase>3))||work.partBest<0||!Enum.IsDefined(typeof(CombatPredicateStatus),work.Status))throw new ArgumentException("Invalid predicate cursor.");
            if(work.Status==CombatPredicateStatus.Pending&&work.hit||work.Status==CombatPredicateStatus.Complete&&!work.overlap&&work.root!=preparedPose.RootCount||work.Status==CombatPredicateStatus.Complete&&work.overlap&&!work.hit&&work.root!=preparedPose.RootCount||!work.overlap&&work.hit!=(!double.IsPositiveInfinity(work.best)&&work.Status==CombatPredicateStatus.Complete)||!work.overlap&&work.best>1&&!double.IsPositiveInfinity(work.best)||work.overlap&&!double.IsPositiveInfinity(work.best)||work.root==preparedPose.RootCount&&(work.part!=0||work.phase!=0||work.path!=0||work.worldPass||!double.IsPositiveInfinity(work.rootBest)))throw new ArgumentException("Impossible predicate terminal state.");
            if(work.root<preparedPose.RootCount){if(work.part>work.Mesh.Parts.Length)throw new ArgumentException("Invalid part cursor.");if(work.part<work.Mesh.Parts.Length){var p=work.Part;if(work.leaf>Math.Max(p.Order.Length,p.Triangles.Length))throw new ArgumentException("Invalid leaf cursor.");if(work.path>0)work.Node();
                if(work.phase==2||work.phase==5||work.phase==8){if(work.path==0)throw new ArgumentException("Missing leaf path.");var node=work.Node();if(node.Left!=null||work.leaf<node.Start||work.leaf>node.Start+node.Count)throw new ArgumentException("Leaf/path disagreement.");}
                if(work.phase==0&&(work.path!=0||work.leaf!=0)||work.phase==6&&work.path!=0||work.phase==3&&p.Motion!=2||work.phase==9&&!preparedPose.WorldTriangles(work.radius)||work.worldPass&&(!work.overlap||!preparedPose.WorldTriangles(work.radius))||Math.Abs((long)work.winding)>p.Triangles.Length)throw new ArgumentException("Impossible predicate phase.");}}
            return work;
        }
    }
    public static partial class BuildingCombatGeometry
    {
        internal static bool SentryPrepared;
        // Never performs cold decode. Existing synchronous Pose is the explicit
        // asset-preparation path; a missing model is not a certified predicate.
        public static bool TryPreparedTurretPose(PlayableProfile p,PlayableBuildingKind kind,NavPoint at,double ground,double heading,double pitch,double progress,double recoil,out BuildingCombatPose pose)
        {pose=default;if(!TerritoryRules.Defensive(kind))throw new ArgumentException("Not a defensive building.");if(!SentryPrepared)return false;pose=new BuildingCombatPose(new SentryCombatPose(at,ground+ApronLift,p.TurretModelScale,heading,pitch,progress,kind==PlayableBuildingKind.RepairTower,kind==PlayableBuildingKind.GaussTower,recoil));return true;}
        public static bool TryPreparedPose(PlayableProfile p,PlayableBuildingKind kind,bool upgraded,NavPoint at,double ground,out BuildingCombatPose pose,double heading=TerritoryRules.BuildingHeading,double opening=0,double platform=0,double constructionProgress=1)
        {pose=default;if(TerritoryRules.Defensive(kind)){if(!SentryPrepared)return false;}else{string key=PreparedModelKey(kind,upgraded);lock(Cache)if(!Cache.ContainsKey(key))return false;}pose=Pose(p,kind,upgraded,at,ground,heading,opening,platform,constructionProgress);return true;}
    }
    public readonly partial struct BuildingCombatPose
    {
        internal bool Prepared=>mesh!=null||sentry.HasValue;
        internal int RootCount=>sentry.HasValue?3:1;
        internal double Opening=>sentry.HasValue?0:opening;
        internal double Platform=>sentry.HasValue?0:platform;
        internal double QueryHeight=>sentry.HasValue?1:height;
        internal CombatMesh MeshAt(int root)=>sentry.HasValue?sentry.Value.QueryMesh(root):mesh;
        internal V QueryLocal(BallisticPoint p,int root)=>sentry.HasValue?sentry.Value.QueryLocal(p,root):Local(p);
        internal double QueryRadius(double radius)=>radius/(sentry.HasValue?sentry.Value.QueryScale:scale);
        internal bool WorldTriangles(double radius)=>sentry.HasValue&&sentry.Value.QueryHeight!=1&&radius!=0;
        internal CombatTriangle WorldTriangle(CombatTriangle t,int root)=>sentry.Value.QueryWorldTriangle(t,root);
        public bool TryCreateSegmentWork(BallisticPoint a,BallisticPoint b,double radius,Func<long,bool> reserve,out CombatPoseWork work)
        {work=null;if(!Prepared||reserve==null||!reserve(CombatPoseWork.RetainedBytes))return false;work=new CombatPoseWork(this,a,b,radius,false);return true;}
        public bool TryCreateOverlapWork(BallisticPoint a,double radius,Func<long,bool> reserve,out CombatPoseWork work)
        {work=null;if(!Prepared||reserve==null||!reserve(CombatPoseWork.RetainedBytes))return false;work=new CombatPoseWork(this,a,a,radius,true);return true;}
        internal void WriteQueryBinding(BinaryWriter w)
        {w.Write(BuildingCombatGeometry.Version);w.Write(sentry.HasValue);if(sentry.HasValue)sentry.Value.WriteQueryBinding(w);else{w.Write(mesh.PreparedKey??"");w.Write(origin.X);w.Write(origin.Y);w.Write(origin.Z);w.Write(scale);w.Write(cos);w.Write(sin);w.Write(opening);w.Write(platform);w.Write(height);}}
        internal void ReadQueryBinding(BinaryReader r)
        {using(var stream=new PredicateBindingComparison(r))using(var w=new BinaryWriter(stream,System.Text.Encoding.UTF8,true))WriteQueryBinding(w);}

    }
    internal readonly partial struct SentryCombatPose
    {
        internal CombatMesh QueryMesh(int root)=>Mesh(root);
        internal V QueryLocal(BallisticPoint p,int root)=>Local(p,root);
        internal double QueryScale=>scale;
        internal double QueryHeight=>height;
        internal CombatTriangle QueryWorldTriangle(CombatTriangle t,int root)=>WorldTriangle(t,root);
        internal void WriteQueryBinding(BinaryWriter w){w.Write(repair);w.Write(gauss);w.Write(at.X);w.Write(at.Y);w.Write(at.Z);w.Write(scale);w.Write(yaw);w.Write(pitch);w.Write(height);w.Write(recoil);}
    }
}
