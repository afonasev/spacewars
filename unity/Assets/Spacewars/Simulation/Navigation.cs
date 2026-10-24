using System;
using System.Collections.Generic;
using System.IO;

namespace Spacewars.Simulation
{
    public struct NavPoint
    {
        public readonly double X;
        public readonly double Z;
        public NavPoint(double x, double z) { X = x; Z = z; }
    }

    public struct NavObstacle
    {
        public readonly double MinX, MinZ, MaxX, MaxZ;
        public readonly double CircleRadius;
        public NavPoint CircleCenter=>new NavPoint((MinX+MaxX)/2,(MinZ+MaxZ)/2);
        // Exact circle is the source unit footprint; no polygon inflation or new balance value.
        public NavObstacle(NavPoint center,double radius){if(radius<=0||!Finite(radius)||!Finite(center.X)||!Finite(center.Z))throw new ArgumentException("Invalid circle.");CircleRadius=radius;MinX=center.X-radius;MaxX=center.X+radius;MinZ=center.Z-radius;MaxZ=center.Z+radius;Polygon=null;Bottom=0;Top=double.NaN;}
        public readonly NavPolygon Polygon; public readonly double Bottom, Top;
        public NavObstacle(NavPolygon polygon,double bottom=0,double top=double.NaN){CircleRadius=0;Polygon=polygon??throw new ArgumentNullException(nameof(polygon));MinX=polygon.MinX;MinZ=polygon.MinZ;MaxX=polygon.MaxX;MaxZ=polygon.MaxZ;Bottom=bottom;Top=top;}
        public bool Contains(NavPoint p)=>Polygon!=null?Polygon.Contains(p):p.X>=MinX&&p.X<=MaxX&&p.Z>=MinZ&&p.Z<=MaxZ;
        public double DistanceSquared(NavPoint p){if(Polygon!=null)return Polygon.DistanceSquared(p);double x=Math.Max(MinX-p.X,Math.Max(0,p.X-MaxX)),z=Math.Max(MinZ-p.Z,Math.Max(0,p.Z-MaxZ));return x*x+z*z;}
        public double BoundaryDistanceSquared(NavPoint p)=>Polygon!=null?Polygon.BoundaryDistanceSquared(p):Contains(p)?Math.Pow(Math.Min(Math.Min(p.X-MinX,MaxX-p.X),Math.Min(p.Z-MinZ,MaxZ-p.Z)),2):DistanceSquared(p);
        // Structural identity for decoded polygon geometry; object identity is not wire identity.
        public bool GeometryEquals(NavObstacle other)
        {
            if(MinX!=other.MinX||MinZ!=other.MinZ||MaxX!=other.MaxX||MaxZ!=other.MaxZ||CircleRadius!=other.CircleRadius||!Bottom.Equals(other.Bottom)||!Top.Equals(other.Top)||(Polygon==null)!=(other.Polygon==null))return false;
            if(Polygon!=null){if(Polygon.Vertices.Count!=other.Polygon.Vertices.Count)return false;for(int i=0;i<Polygon.Vertices.Count;i++)if(!Polygon.Vertices[i].Equals(other.Polygon.Vertices[i]))return false;}return true;
        }
        public NavPolygon Footprint=>Polygon??new NavPolygon(new[]{new NavPoint(MinX,MinZ),new NavPoint(MaxX,MinZ),new NavPoint(MaxX,MaxZ),new NavPoint(MinX,MaxZ)});
        public NavObstacle(double minX, double minZ, double maxX, double maxZ)
        {
            if (!Finite(minX) || !Finite(minZ) || !Finite(maxX) || !Finite(maxZ) || minX > maxX || minZ > maxZ) throw new ArgumentException("Invalid obstacle bounds.");
            CircleRadius=0;MinX = minX; MinZ = minZ; MaxX = maxX; MaxZ = maxZ; Polygon=null;Bottom=0;Top=double.NaN;
        }
        // Wire hydration retains exact bounds produced by the original center +/- radius
        // arithmetic. Midpoint reconstruction can change IEEE754 low bits.
        public static NavObstacle RestoreCircle(double minX,double minZ,double maxX,double maxZ,double radius)
        { if(!Finite(minX)||!Finite(minZ)||!Finite(maxX)||!Finite(maxZ)||!Finite(radius)||radius<=0||!CircleAxis(minX,maxX,radius)||!CircleAxis(minZ,maxZ,radius))throw new ArgumentException("Invalid saved circle bounds.");return new NavObstacle(minX,minZ,maxX,maxZ,radius); }
        private static bool CircleAxis(double min,double max,double radius)
        {
            if(min>max)return false;
            // Require an exact IEEE754 constructor witness, not approximate bounds.
            foreach(double c in new[]{min+radius,max-radius,min/2+max/2}){
                if(c-radius==min&&c+radius==max)return true;
                long bits=BitConverter.DoubleToInt64Bits(c);
                foreach(long adjacent in new[]{bits-1,bits+1}){double candidate=BitConverter.Int64BitsToDouble(adjacent);if(Finite(candidate)&&candidate-radius==min&&candidate+radius==max)return true;}
            }
            return false;
        }
        private NavObstacle(double minX,double minZ,double maxX,double maxZ,double radius){MinX=minX;MinZ=minZ;MaxX=maxX;MaxZ=maxZ;CircleRadius=radius;Polygon=null;Bottom=0;Top=double.NaN;}
        private static bool Finite(double value) { return !Double.IsNaN(value) && !Double.IsInfinity(value); }
    }

    public sealed partial class NavGeometry : INavStorageFacade
    {
        private readonly NavObstacle[] obstacles;
        private readonly IReadOnlyList<NavObstacle> obstacleView;
        private readonly OwnedBuffers owned;
        NavOwnedStorage INavStorageFacade.StorageRoot=>owned;
        internal NavOwnedStorage StorageRoot=>owned;
        internal sealed partial class OwnedBuffers : NavOwnedStorage
        {
            internal NavRetainedArray<NavObstacle> Obstacles;internal NavGeometry Facade;private bool released;
            internal OwnedBuffers():base(192){}
            internal override void ReleaseOwner(){if(released)return;released=true;Release();}
            protected override void ReleaseEdges(){Facade=null;Obstacles?.ReleaseOwner();}
        }
        private NavGeometry(NavGeometry source,OwnedBuffers storage)
        {owned=storage;HalfExtent=source.HalfExtent;Revision=source.Revision;obstacles=storage.Obstacles.ReadOnlyBacking;obstacleView=storage.Obstacles;}
        private sealed class OwnedCopySource : IReadOnlyList<NavObstacle>
        {
            private readonly NavGeometry source;private readonly INavStorageReservation reservation;private readonly Func<NavPolygon,NavPolygon> canonicalPolygon;
            private readonly Dictionary<NavPolygon,NavPolygon> polygons=new Dictionary<NavPolygon,NavPolygon>();
            internal OwnedCopySource(NavGeometry source,INavStorageReservation reservation,Func<NavPolygon,NavPolygon> canonicalPolygon){this.source=source;this.reservation=reservation;this.canonicalPolygon=canonicalPolygon;}
            public int Count=>source.obstacles.Length;
            public NavObstacle this[int index]{get{
                var obstacle=source.obstacles[index];if(obstacle.Polygon==null)return obstacle;
                var polygon=obstacle.Polygon;
                if(canonicalPolygon!=null)return new NavObstacle(canonicalPolygon(polygon),obstacle.Bottom,obstacle.Top);
                if(!polygons.TryGetValue(polygon,out var copy)){
                    copy=NavPolygon.CopyOwned(polygon,reservation);polygons.Add(polygon,copy);
                    // The scope birth keeps the borrowed copy live until the
                    // destination array acquires its owning element edge.
                    copy.StorageRoot.ReleaseOwner();
                }
                return new NavObstacle(copy,obstacle.Bottom,obstacle.Top);
            }}
            public IEnumerator<NavObstacle> GetEnumerator(){for(int n=0;n<Count;n++)yield return this[n];}
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()=>GetEnumerator();
        }
        internal static NavGeometry CopyOwned(NavGeometry source,INavStorageReservation reservation,Func<NavPolygon,NavPolygon> canonicalPolygon=null)
        {
            if(source==null||reservation==null)throw new ArgumentNullException();
            using(var scratch=NavStorageScratch.Reserve(reservation,2048L+128L*source.obstacles.Length))
            using(var allocation=new NavStorageAllocation(reservation.TryReserve)){
                allocation.Reserve(192);var storage=allocation.Register(new OwnedBuffers());
                storage.Obstacles=NavRetainedArray<NavObstacle>.Copy(new OwnedCopySource(source,reservation,canonicalPolygon),reservation.TryReserve);
                storage.Facade=new NavGeometry(source,storage);storage.Retain();return storage.Facade;
            }
        }
        // Immutable broad phase. Exact footprint/sweep predicates still decide every hit.
        private const int SpatialWidth=16;
        private readonly int[][] spatial;
        public long SpatialIndexRetainedBytes {get;private set;}
        public NavGeometry(double halfExtent, NavObstacle[] obstacles, int revision)
        {
            if (halfExtent <= 0d || Double.IsNaN(halfExtent) || Double.IsInfinity(halfExtent)) throw new ArgumentException("Invalid half extent.");
            if (revision < 1) throw new ArgumentException("Geometry revision must be positive.");
            HalfExtent = halfExtent; Revision = revision; this.obstacles = obstacles == null ? new NavObstacle[0] : (NavObstacle[])obstacles.Clone(); obstacleView = Array.AsReadOnly(this.obstacles);
            if(this.obstacles.Length>=8){
                var buckets=new List<int>[SpatialWidth*SpatialWidth];
                for(int i=0;i<this.obstacles.Length;i++){
                    var o=this.obstacles[i];int left=Bucket(o.MinX),right=Bucket(o.MaxX),bottom=Bucket(o.MinZ),top=Bucket(o.MaxZ);
                    for(int x=left;x<=right;x++)for(int z=bottom;z<=top;z++){
                        int index=x*SpatialWidth+z;if(buckets[index]==null)buckets[index]=new List<int>();buckets[index].Add(i);
                    }
                }
                spatial=new int[buckets.Length][];SpatialIndexRetainedBytes=32+8L*spatial.Length;
                for(int i=0;i<spatial.Length;i++)if(buckets[i]!=null){spatial[i]=buckets[i].ToArray();SpatialIndexRetainedBytes+=32+4L*spatial[i].Length;}
            }
        }
        public double HalfExtent { get; private set; }
        public int Revision { get; private set; }
        public IReadOnlyList<NavObstacle> Obstacles { get { return obstacleView; } }
        public bool IsFree(NavPoint point, double radius)
        {
            if (radius < 0d || Double.IsNaN(radius) || Double.IsInfinity(radius) || Double.IsNaN(point.X) || Double.IsInfinity(point.X) || Double.IsNaN(point.Z) || Double.IsInfinity(point.Z) || Math.Abs(point.X) + radius > HalfExtent || Math.Abs(point.Z) + radius > HalfExtent) return false;
            bool Hit(int i)=>point.X+radius>=obstacles[i].MinX&&point.X-radius<=obstacles[i].MaxX&&point.Z+radius>=obstacles[i].MinZ&&point.Z-radius<=obstacles[i].MaxZ&&(obstacles[i].Polygon!=null?(obstacles[i].Polygon.Contains(point)||obstacles[i].Polygon.BoundaryWithin(point,radius)):PointRectDistanceSquared(point,obstacles[i])<=radius*radius);
            if(spatial!=null&&SmallQuery(point.X-radius,point.Z-radius,point.X+radius,point.Z+radius,out int left,out int bottom,out int right,out int top)){
                for(int x=left;x<=right;x++)for(int z=bottom;z<=top;z++){var ids=spatial[x*SpatialWidth+z];if(ids!=null)for(int n=0;n<ids.Length;n++)if(Hit(ids[n]))return false;}
            }else for(int i=0;i<obstacles.Length;i++)if(Hit(i))return false;
            return true;
        }
        public bool SegmentFree(NavPoint from, NavPoint to, double radius)
            =>SegmentFree(from,to,radius,out _);
        internal bool SegmentFree(NavPoint from,NavPoint to,double radius,out int blocker)
        {
            blocker=-1;if (!IsFree(from, radius) || !IsFree(to, radius)) return false;
            return SegmentFreeBetweenFreeEndpoints(from,to,radius,out blocker);
        }
        // Internal callers must own exact free-footprint certificates for this immutable geometry.
        internal bool SegmentFreeBetweenFreeEndpoints(NavPoint from,NavPoint to,double radius)
            =>SegmentFreeBetweenFreeEndpoints(from,to,radius,out _);
        internal bool SegmentFreeBetweenFreeEndpoints(NavPoint from,NavPoint to,double radius,out int blocker)
        {
            blocker=-1;
            double minX=Math.Min(from.X,to.X)-radius,minZ=Math.Min(from.Z,to.Z)-radius,maxX=Math.Max(from.X,to.X)+radius,maxZ=Math.Max(from.Z,to.Z)+radius;
            bool Hit(int i)=>maxX>=obstacles[i].MinX&&minX<=obstacles[i].MaxX&&maxZ>=obstacles[i].MinZ&&minZ<=obstacles[i].MaxZ&&(obstacles[i].Polygon!=null?obstacles[i].Polygon.SweepWithin(from,to,radius):SegmentRectDistanceSquared(from,to,obstacles[i])<=radius*radius);
            if(spatial!=null&&SmallQuery(minX,minZ,maxX,maxZ,out int left,out int bottom,out int right,out int top)){
                for(int x=left;x<=right;x++)for(int z=bottom;z<=top;z++){var ids=spatial[x*SpatialWidth+z];if(ids!=null)for(int n=0;n<ids.Length;n++)if(Hit(ids[n])){blocker=ids[n];return false;}}
            }else for(int i=0;i<obstacles.Length;i++)if(Hit(i)){blocker=i;return false;}
            return true;
        }
        private int Bucket(double coordinate)=>(int)Math.Max(0,Math.Min(SpatialWidth-1,Math.Floor((coordinate+HalfExtent)/(2*HalfExtent)*SpatialWidth)));
        private bool SmallQuery(double minX,double minZ,double maxX,double maxZ,out int left,out int bottom,out int right,out int top){
            left=Bucket(minX);bottom=Bucket(minZ);right=Bucket(maxX);top=Bucket(maxZ);
            return (right-left+1)*(top-bottom+1)<=8;
        }
        private static double PointRectDistanceSquared(NavPoint p, NavObstacle r)
        {
            if(r.CircleRadius>0){double dx=p.X-r.CircleCenter.X,dz=p.Z-r.CircleCenter.Z,d=Math.Max(0,Math.Sqrt(dx*dx+dz*dz)-r.CircleRadius);return d*d;}
            if(r.Polygon!=null)return r.Polygon.DistanceSquared(p);
            double x = p.X < r.MinX ? r.MinX - p.X : p.X > r.MaxX ? p.X - r.MaxX : 0d;
            double z = p.Z < r.MinZ ? r.MinZ - p.Z : p.Z > r.MaxZ ? p.Z - r.MaxZ : 0d;
            return x * x + z * z;
        }
        private static double SegmentRectDistanceSquared(NavPoint a, NavPoint b, NavObstacle r)
        {
            if(r.CircleRadius>0){double d=Math.Max(0,Math.Sqrt(PointSegmentDistanceSquared(r.CircleCenter,a,b))-r.CircleRadius);return d*d;}
            if(r.Polygon!=null)return r.Polygon.SegmentDistanceSquared(a,b);
            if (SegmentIntersectsRect(a, b, r)) return 0d;
            NavPoint p1 = new NavPoint(r.MinX, r.MinZ), p2 = new NavPoint(r.MaxX, r.MinZ), p3 = new NavPoint(r.MaxX, r.MaxZ), p4 = new NavPoint(r.MinX, r.MaxZ);
            return Math.Min(Math.Min(SegmentDistanceSquared(a, b, p1, p2), SegmentDistanceSquared(a, b, p2, p3)), Math.Min(SegmentDistanceSquared(a, b, p3, p4), SegmentDistanceSquared(a, b, p4, p1)));
        }
        private static bool SegmentIntersectsRect(NavPoint a, NavPoint b, NavObstacle r)
        {
            if (a.X >= r.MinX && a.X <= r.MaxX && a.Z >= r.MinZ && a.Z <= r.MaxZ) return true;
            if (b.X >= r.MinX && b.X <= r.MaxX && b.Z >= r.MinZ && b.Z <= r.MaxZ) return true;
            NavPoint p1 = new NavPoint(r.MinX, r.MinZ), p2 = new NavPoint(r.MaxX, r.MinZ), p3 = new NavPoint(r.MaxX, r.MaxZ), p4 = new NavPoint(r.MinX, r.MaxZ);
            return SegmentsIntersect(a, b, p1, p2) || SegmentsIntersect(a, b, p2, p3) || SegmentsIntersect(a, b, p3, p4) || SegmentsIntersect(a, b, p4, p1);
        }
        private static double SegmentDistanceSquared(NavPoint a, NavPoint b, NavPoint c, NavPoint d)
        {
            if (SegmentsIntersect(a, b, c, d)) return 0d;
            return Math.Min(Math.Min(PointSegmentDistanceSquared(a, c, d), PointSegmentDistanceSquared(b, c, d)), Math.Min(PointSegmentDistanceSquared(c, a, b), PointSegmentDistanceSquared(d, a, b)));
        }
        private static double PointSegmentDistanceSquared(NavPoint p, NavPoint a, NavPoint b)
        {
            double dx = b.X - a.X, dz = b.Z - a.Z, length = dx * dx + dz * dz;
            if (length == 0d) { dx = p.X - a.X; dz = p.Z - a.Z; return dx * dx + dz * dz; }
            double t = Math.Max(0d, Math.Min(1d, ((p.X - a.X) * dx + (p.Z - a.Z) * dz) / length)); dx = p.X - (a.X + dx * t); dz = p.Z - (a.Z + dz * t); return dx * dx + dz * dz;
        }
        private static bool SegmentsIntersect(NavPoint a, NavPoint b, NavPoint c, NavPoint d)
        {
            double ab1 = Cross(a, b, c), ab2 = Cross(a, b, d), cd1 = Cross(c, d, a), cd2 = Cross(c, d, b);
            if (((ab1 > 0d && ab2 < 0d) || (ab1 < 0d && ab2 > 0d)) && ((cd1 > 0d && cd2 < 0d) || (cd1 < 0d && cd2 > 0d))) return true;
            const double e = 0.000000001d;
            return Math.Abs(ab1) <= e && OnSegment(a, b, c) || Math.Abs(ab2) <= e && OnSegment(a, b, d) || Math.Abs(cd1) <= e && OnSegment(c, d, a) || Math.Abs(cd2) <= e && OnSegment(c, d, b);
        }
        private static double Cross(NavPoint a, NavPoint b, NavPoint p) { return (b.X - a.X) * (p.Z - a.Z) - (b.Z - a.Z) * (p.X - a.X); }
        private static bool OnSegment(NavPoint a, NavPoint b, NavPoint p) { return p.X >= Math.Min(a.X, b.X) - .000000001d && p.X <= Math.Max(a.X, b.X) + .000000001d && p.Z >= Math.Min(a.Z, b.Z) - .000000001d && p.Z <= Math.Max(a.Z, b.Z) + .000000001d; }
    }

    public sealed class NavigationProfileField
    {
        private readonly Func<NavigationProfile, double> read;
        public NavigationProfileField(string path, string group, string label, string description, string unit, double minimum, double maximum, double step, Func<NavigationProfile, double> read) { Path = path; Group = group; Label = label; Description = description; Unit = unit; Minimum = minimum; Maximum = maximum; Step = step; this.read = read; }
        public string Path { get; private set; } public string Group { get; private set; } public string Label { get; private set; } public string Description { get; private set; } public string Unit { get; private set; } public double Minimum { get; private set; } public double Maximum { get; private set; } public double Step { get; private set; }
        public double Read(NavigationProfile profile) { return read(profile); }
    }
    public sealed partial class NavigationProfile
    {
        // Absent historical extensions always use these v1 values, even when
        // future new-match defaults change.
        public const int CrowdV1HeadingIntervals=16,CrowdV1SpeedBands=2,CrowdV1NeighborCap=8;
        public const double CrowdV1LookaheadSeconds=1.2;
        internal const int LegacyWireValueCount=14,CrowdWireValueCount=18,CrowdBindingMarker=0x43524D31;
        public int CrowdHeadingIntervals {get;private set;} public int CrowdSpeedBands {get;private set;} public int CrowdNeighborCap {get;private set;} public double CrowdLookaheadSeconds {get;private set;}
        public bool HasCrowdV1Defaults=>CrowdHeadingIntervals==CrowdV1HeadingIntervals&&CrowdSpeedBands==CrowdV1SpeedBands&&CrowdLookaheadSeconds==CrowdV1LookaheadSeconds&&CrowdNeighborCap==CrowdV1NeighborCap;
        public int GroupCorridorMaximumExcessPercent {get;private set;}
        internal int WireValueCount=>GroupCorridorMaximumExcessPercent!=0?19:HasCrowdV1Defaults?LegacyWireValueCount:CrowdWireValueCount;
        public const string DefaultProfileId = "unity-navigation-diagnostic-v1";
        public NavigationProfile() : this(DefaultProfileId, 1, .72d, 4d, 4.5d, .8d, .15d, 3.6d, .08d) { }
        public NavigationProfile(string profileId, int revision, double radius, double tankSpeed, double tankTurnSpeed, double gridCell, double arrivalTolerance, double localDetourDistance, double forwardAlignmentAngle, double cameraOrthoSize=56, double cameraHeight=85, double cameraOffsetZ=-40, double repairRetrySeconds=.3, double arrivalSlotSpacing=3.6, double blockedTimeoutSeconds=5, double localGridCell=.2,double crowdHeadingIntervals=CrowdV1HeadingIntervals,double crowdSpeedBands=CrowdV1SpeedBands,double crowdLookaheadSeconds=CrowdV1LookaheadSeconds,double crowdNeighborCap=CrowdV1NeighborCap,int groupCorridorMaximumExcessPercent=0)
        { ProfileId = profileId; Revision = revision; Radius = radius; TankSpeed = tankSpeed; TankTurnSpeed = tankTurnSpeed; GridCell = gridCell; ArrivalTolerance = arrivalTolerance; LocalDetourDistance = localDetourDistance; ForwardAlignmentAngle = forwardAlignmentAngle; CameraOrthoSize=cameraOrthoSize; CameraHeight=cameraHeight; CameraOffsetZ=cameraOffsetZ; RepairRetrySeconds=repairRetrySeconds; ArrivalSlotSpacing=arrivalSlotSpacing; BlockedTimeoutSeconds=blockedTimeoutSeconds; LocalGridCell=localGridCell;CrowdHeadingIntervals=Count(crowdHeadingIntervals);CrowdSpeedBands=Count(crowdSpeedBands);CrowdLookaheadSeconds=crowdLookaheadSeconds;CrowdNeighborCap=Count(crowdNeighborCap);GroupCorridorMaximumExcessPercent=groupCorridorMaximumExcessPercent; Validate(this); }
        public string ProfileId { get; private set; } public int Revision { get; private set; } public double Radius { get; private set; } public double TankSpeed { get; private set; } public double TankTurnSpeed { get; private set; } public double GridCell { get; private set; } public double ArrivalTolerance { get; private set; } public double LocalDetourDistance { get; private set; } public double ForwardAlignmentAngle { get; private set; }
        public double LocalGridCell {get;private set;}
        public double BlockedTimeoutSeconds {get;private set;}
        public double ArrivalSlotSpacing {get;private set;}
        public double RepairRetrySeconds {get;private set;}
        public double CameraOrthoSize {get;private set;} public double CameraHeight {get;private set;} public double CameraOffsetZ {get;private set;}
        public NavigationProfile ForUnit(double radius,double speed,double turn)=>new NavigationProfile(ProfileId,Revision,radius,speed,turn,GridCell,ArrivalTolerance,LocalDetourDistance,ForwardAlignmentAngle,CameraOrthoSize,CameraHeight,CameraOffsetZ,RepairRetrySeconds,ArrivalSlotSpacing,BlockedTimeoutSeconds,LocalGridCell,CrowdHeadingIntervals,CrowdSpeedBands,CrowdLookaheadSeconds,CrowdNeighborCap,GroupCorridorMaximumExcessPercent);
        public static NavigationProfile Default { get { return new NavigationProfile(); } }
        public static readonly IReadOnlyList<NavigationProfileField> Metadata = new[] {
            new NavigationProfileField("navigation.camera.orthoSize", "Camera", "View size", "Diagnostic camera orthographic half height.", "m",5,150,1,p=>p.CameraOrthoSize),
            new NavigationProfileField("navigation.camera.height", "Camera", "Height", "Diagnostic camera height above ground.", "m",1,300,1,p=>p.CameraHeight),
            new NavigationProfileField("navigation.camera.offsetZ", "Camera", "Z offset", "Diagnostic camera longitudinal offset.", "m",-200,200,1,p=>p.CameraOffsetZ),
            new NavigationProfileField("navigation.repairRetrySeconds", "Navigation", "Repair delay", "Blocked motion time before a bounded local repair.", "s",.05,3,.05,p=>p.RepairRetrySeconds),
            new NavigationProfileField("navigation.arrivalSlotSpacing", "Navigation", "Arrival spacing", "Slot spacing preserves a full footprint aisle after other units park.", "m",.5,10,.1,p=>p.ArrivalSlotSpacing),
            new NavigationProfileField("navigation.blockedTimeoutSeconds", "Navigation", "Blocked timeout", "Continuous inability to move ends the order with an explicit blocked outcome.", "s",.5,60,.5,p=>p.BlockedTimeoutSeconds),
            new NavigationProfileField("navigation.localGridCell", "Navigation", "Local repair grid", "Resolution of bounded local collision repair, independent of global field resolution.", "m",.1,1,.05,p=>p.LocalGridCell),
            new NavigationProfileField("navigation.radius", "Navigation", "Tank radius", "Authoritative tank collision radius.", "m", .1, 5, .01, p => p.Radius),
            new NavigationProfileField("navigation.tankSpeed", "Navigation", "Tank speed", "Maximum forward tank speed.", "m/s", .1, 50, .1, p => p.TankSpeed),
            new NavigationProfileField("navigation.tankTurnSpeed", "Navigation", "Tank turn speed", "Maximum hull turn speed.", "rad/s", .1, 30, .1, p => p.TankTurnSpeed),
            new NavigationProfileField("navigation.gridCell", "Navigation", "Grid cell", "Shared reverse-field cell size.", "m", .1, 5, .1, p => p.GridCell),
            new NavigationProfileField("navigation.arrivalTolerance", "Navigation", "Arrival tolerance", "Distance accepted at a route waypoint.", "m", .01, 1, .01, p => p.ArrivalTolerance),
            new NavigationProfileField("navigation.localDetourDistance", "Navigation", "Local detour distance", "Maximum committed local detour distance.", "m", .2, 8, .1, p => p.LocalDetourDistance),
            new NavigationProfileField("navigation.forwardAlignmentAngle", "Navigation", "Forward alignment", "Maximum heading error before forward movement.", "rad", .01, 1, .01, p => p.ForwardAlignmentAngle)
        };
        public static readonly IReadOnlyList<NavigationProfileField> CrowdMetadata=new[]{
            new NavigationProfileField("crowdMotion.headingIntervals","Crowd motion","Heading intervals","Angular sampling work and steering choices inside the reachable turn set.","intervals",4,64,1,p=>p.CrowdHeadingIntervals),
            new NavigationProfileField("crowdMotion.speedBands","Crowd motion","Speed bands","Forward speed choices; wait and turn-in-place remain explicit.","bands",1,4,1,p=>p.CrowdSpeedBands),
            new NavigationProfileField("crowdMotion.lookaheadSeconds","Crowd motion","Lookahead","Heuristic prediction window; actual-step swept safety remains authoritative.","s",.1,3,.1,p=>p.CrowdLookaheadSeconds),
            new NavigationProfileField("crowdMotion.localNeighborCap","Crowd motion","Local neighbor capacity","Bounded local work; overflow waits conservatively instead of truncating hazards.","actors",1,32,1,p=>p.CrowdNeighborCap)
        };
        private static int Count(double value)
        {if(double.IsNaN(value)||double.IsInfinity(value)||value!=Math.Truncate(value)||value<int.MinValue||value>int.MaxValue)throw new ArgumentException("Crowd counts must be finite integers.");return (int)value;}
        public static readonly NavigationProfileField GroupCorridorMetadata=new NavigationProfileField("navigation.groupCorridor.maximumExcessPercent","Group navigation","Group route excess","Per-member certified weighted route excess above its own global optimum; missing certificate uses exact routing.","%",15,15,1,p=>p.GroupCorridorMaximumExcessPercent);
        internal double WireValueAt(int index)=>index==18?GroupCorridorMaximumExcessPercent:index<LegacyWireValueCount?Metadata[index].Read(this):CrowdMetadata[index-LegacyWireValueCount].Read(this);
        internal double[] CaptureWireValues(){var values=new double[WireValueCount];for(int i=0;i<values.Length;i++)values[i]=WireValueAt(i);return values;}
        internal bool MatchesWireValues(IReadOnlyList<double> values,bool ignoreMobility=false)
        {if(values==null||values.Count!=WireValueCount)return false;for(int i=0;i<values.Count;i++)if((!ignoreMobility||i<7||i>9)&&BitConverter.DoubleToInt64Bits(values[i])!=BitConverter.DoubleToInt64Bits(WireValueAt(i)))return false;return true;}
        internal static NavigationProfile FromWireValues(string id,int revision,IReadOnlyList<double> v)
        {
            if(v==null||v.Count!=LegacyWireValueCount&&v.Count!=CrowdWireValueCount&&v.Count!=19)throw new ArgumentException("Invalid navigation profile values.");bool extended=v.Count>=CrowdWireValueCount;
            var result=new NavigationProfile(id,revision,v[7],v[8],v[9],v[10],v[11],v[12],v[13],v[0],v[1],v[2],v[3],v[4],v[5],v[6],extended?v[14]:CrowdV1HeadingIntervals,extended?v[15]:CrowdV1SpeedBands,extended?v[16]:CrowdV1LookaheadSeconds,extended?v[17]:CrowdV1NeighborCap,v.Count==19?Count(v[18]):0);
            if(extended&&v.Count!=19&&result.HasCrowdV1Defaults)throw new ArgumentException("Redundant default crowd profile extension.");return result;
        }
        internal void WriteCrowdValues(BinaryWriter writer){for(int i=0;i<CrowdMetadata.Count;i++)writer.Write(CrowdMetadata[i].Read(this));}
        internal void WriteCrowdBinding(BinaryWriter writer){if(!HasCrowdV1Defaults){writer.Write(CrowdBindingMarker);writer.Write(1);WriteCrowdValues(writer);}if(GroupCorridorMaximumExcessPercent!=0){writer.Write("certified-group-corridor-v1");writer.Write(GroupCorridorMaximumExcessPercent);}}
        public static void Validate(NavigationProfile profile)
        {if(profile!=null&&profile.GroupCorridorMaximumExcessPercent!=0&&profile.GroupCorridorMaximumExcessPercent!=15)throw new ArgumentException("Unapproved certified corridor quality."); if (profile == null || String.IsNullOrWhiteSpace(profile.ProfileId) || profile.Revision < 1) throw new ArgumentException("Navigation profile provenance is incomplete.");for(int i=0;i<Metadata.Count+CrowdMetadata.Count;i++){var field=i<Metadata.Count?Metadata[i]:CrowdMetadata[i-Metadata.Count];double value=field.Read(profile);if(Double.IsNaN(value)||Double.IsInfinity(value)||value<field.Minimum||value>field.Maximum)throw new ArgumentException("Invalid navigation profile field: "+field.Path);if(i>=Metadata.Count){double steps=(value-field.Minimum)/field.Step;if(Math.Abs(steps-Math.Round(steps))>1e-8)throw new ArgumentException("Crowd field must align to metadata step: "+field.Path);}} }
    }

    public struct NavRouteIdentity
    {
        public readonly long Session, Request, Order; public readonly string EntityId, ProfileId; public readonly int ProfileRevision, GeometryRevision;
        public NavRouteIdentity(long session, long request, string entityId, long order, string profileId, int profileRevision, int geometryRevision) { Session = session; Request = request; EntityId = entityId; Order = order; ProfileId = profileId; ProfileRevision = profileRevision; GeometryRevision = geometryRevision; }
        public bool Matches(NavRouteIdentity other) { return Session == other.Session && Request == other.Request && Order == other.Order && EntityId == other.EntityId && ProfileId == other.ProfileId && ProfileRevision == other.ProfileRevision && GeometryRevision == other.GeometryRevision; }
    }

    internal sealed class NavMailboxRetention : NavOwnedStorage
    {
        private static long Layout(int capacity)=>checked(NavStorageLayout.ObjectBytesBound(typeof(NavMailboxRetention))+32L+8L*capacity);
        internal readonly NavOwnedStorage[] Owners;private bool released;private NavOwnedStorage transport,backing;
        internal void OwnTransport(NavOwnedStorage value){RequireEdge(value);value.Retain();transport=value;}
        internal void OwnBacking(NavOwnedStorage value){RequireEdge(value);value.Retain();backing=value;}
        internal override bool IsImmutableCaptureRoot=>false;
        private NavMailboxRetention(int capacity):base(Layout(capacity)){Owners=new NavOwnedStorage[capacity];Retain();}
        internal static NavMailboxRetention CreateOwned(int capacity,INavStorageReservation reservation){using(var allocation=new NavStorageAllocation(reservation.TryReserve)){allocation.Reserve(Layout(capacity));return allocation.Register(new NavMailboxRetention(capacity));}}
        internal void Put(int slot,NavOwnedStorage owner){if(owner!=null){RequireEdge(owner);owner.Retain();}Owners[slot]?.Release();Owners[slot]=owner;}
        internal NavOwnedStorage TakeSlotNoThrow(int slot){var owner=Owners[slot];Owners[slot]=null;return owner;}
        internal void ClearSlot(int slot){var owner=Owners[slot];Owners[slot]=null;owner?.Release();}
        internal void ReleaseStorage(){if(released)return;released=true;Release();}
        internal override void ReleaseOwner()=>ReleaseStorage();
        protected override void ReleaseEdges(){for(int i=0;i<Owners.Length;i++){Owners[i]?.Release();Owners[i]=null;}transport?.Release();transport=null;backing?.Release();backing=null;}
    }
    public sealed class NavMailbox<T>
    {
        public const int Capacity=256;
        private readonly int capacity;private readonly object gate=new object();
        private bool closed;internal void CloseNoThrow(){closed=true;}
        private T[] items;private int head,count,transferThread;private long revision;private NavMailboxRetention retention;private Func<T,NavOwnedStorage> ownerSelector;
        private NavOwnedStorage[] slotOwners;
        private void ReleaseSlotOwner(int slot){if(slotOwners==null)return;var old=slotOwners[slot];slotOwners[slot]=null;old?.Release();}
        private object authorityGate;private Action<T> validateEnqueue,acceptedEnqueue,publicTake;
        internal void SelectOwner(Func<T,NavOwnedStorage> selector){ownerSelector=selector;}
        internal void BindTransport(object authority,Action<T> validate,Action<T> accepted,Action<T> taken){authorityGate=authority;validateEnqueue=validate;acceptedEnqueue=accepted;publicTake=taken;}
        public NavMailbox(int capacity=Capacity){if(capacity<1||capacity>4096)throw new ArgumentOutOfRangeException(nameof(capacity));this.capacity=capacity;items=Array.Empty<T>();}
        internal void BindRetention(NavMailboxRetention storage,Func<T,NavOwnedStorage> selector,T[] actualItems=null){lock(gate){if(count!=0||retention!=null||storage.Owners.Length!=capacity)throw new InvalidOperationException("Invalid mailbox retention binding.");if(actualItems!=null){if(items.Length!=0||actualItems.Length!=capacity)throw new InvalidOperationException("Invalid paid mailbox items backing.");items=actualItems;}retention=storage;ownerSelector=selector;}}
        internal void ReleaseRetention(){NavMailboxRetention old;lock(gate){if(count!=0)throw new InvalidOperationException("Mailbox must drain before release.");old=retention;retention=null;ownerSelector=null;}old?.ReleaseStorage();}
        public int InstanceCapacity=>capacity;
        internal long Revision{get{lock(gate)return revision;}}
        internal bool HasInitialPreparedBacking{get{lock(gate)return count==0&&items.Length==0&&slotOwners==null&&retention==null;}}
        internal void ObserveRetainedBacking(out T[] actualItems,out NavMailboxRetention storage,out Action<T> validate,out Action<T> accepted){lock(gate){RejectTransferReentry();actualItems=items;storage=retention;validate=validateEnqueue;accepted=acceptedEnqueue;}}
        internal void ObserveState(out long observedRevision,out int observedCount){lock(gate){RejectTransferReentry();observedRevision=revision;observedCount=count;}}
        internal T ReadPreparedAt(long expected,int index){lock(gate){RejectTransferReentry();if(revision!=expected||index<0||index>=count)throw new InvalidOperationException("Mailbox changed before prepared observation.");return items[(head+index)%capacity];}}
        internal T[] CopyItemsAtRevision(long expected){lock(gate){RejectTransferReentry();if(revision!=expected)throw new InvalidOperationException("Mailbox changed before prepared snapshot.");var copy=new T[count];for(int i=0;i<count;i++)copy[i]=items[(head+i)%capacity];return copy;}}
        // Expected optimistic-read drift is distinct from closed/reentrant mailbox misuse.
        // The revision check and copy share the mailbox gate; a same-count replacement
        // is still stale, and refusal never exposes a partial snapshot.
        internal bool TryCopyItemsAtRevision(long expected,out T[] copy)
        {lock(gate){RejectTransferReentry();if(revision!=expected){copy=null;return false;}copy=new T[count];for(int i=0;i<count;i++)copy[i]=items[(head+i)%capacity];return true;}}

        private void RejectTransferReentry(){if(closed)throw new ObjectDisposedException("NavMailbox");if(transferThread==System.Threading.Thread.CurrentThread.ManagedThreadId)throw new InvalidOperationException("Mailbox transfer callback cannot reenter its mailbox.");}
        public int Count{get{lock(gate)return count;}}
        public T[] CopyItems(){lock(gate){RejectTransferReentry();var copy=new T[count];for(int i=0;i<count;i++)copy[i]=items[(head+i)%capacity];return copy;}}
        internal bool TryPeek(out T item){lock(gate){RejectTransferReentry();item=count==0?default:items[head];return count!=0;}}
        public bool TryEnqueue(T item){lock(authorityGate??gate){lock(gate){RejectTransferReentry();if(count==capacity)return false;validateEnqueue?.Invoke(item);if(items.Length==0)items=new T[capacity];retention?.Put((head+count)%capacity,ownerSelector(item));if(slotOwners!=null){var owner=ownerSelector(item);owner?.Retain();slotOwners[(head+count)%capacity]=owner;}acceptedEnqueue?.Invoke(item);items[(head+count)%capacity]=item;count++;revision++;return true;}}}
        public bool TryDequeue(out T item)=>Take(out item,true);
        internal bool TryDiscard(out T item)=>Take(out item,false);
        private bool Take(out T item,bool transfer){lock(authorityGate??gate){lock(gate){RejectTransferReentry();if(count==0){item=default;return false;}item=items[head];if(transfer)publicTake?.Invoke(item);items[head]=default;retention?.ClearSlot(head);ReleaseSlotOwner(head);head=(head+1)%capacity;count--;revision++;return true;}}}
        internal bool TryTransfer(Func<T,bool> accept,out T item){if(accept==null)throw new ArgumentNullException(nameof(accept));lock(authorityGate??gate){lock(gate){RejectTransferReentry();if(count==0){item=default;return false;}item=items[head];transferThread=System.Threading.Thread.CurrentThread.ManagedThreadId;bool accepted;try{accepted=accept(item);}finally{transferThread=0;}if(!accepted){item=default;return false;}items[head]=default;retention?.ClearSlot(head);ReleaseSlotOwner(head);head=(head+1)%capacity;count--;revision++;return true;}}}
        internal readonly struct PreparedWriteGate : IDisposable
        {
            private readonly NavMailbox<T> mailbox;
            internal PreparedWriteGate(NavMailbox<T> mailbox){this.mailbox=mailbox;System.Threading.Monitor.Enter(mailbox.gate);try{mailbox.RejectTransferReentry();}catch{System.Threading.Monitor.Exit(mailbox.gate);throw;}}
            internal void Validate(long expected,T[] replacement,int replacementCount){if(mailbox.revision!=expected||replacement==null||replacement.Length!=mailbox.capacity||replacementCount<0||replacementCount>mailbox.capacity||mailbox.revision==long.MaxValue)throw new InvalidOperationException("Prepared mailbox lost its source/capacity.");}
            internal void ValidateTakeOne(long expected,int expectedCount,NavOwnedStorage[] retired)
            {if(mailbox.revision!=expected||mailbox.count!=expectedCount||expectedCount<1||mailbox.publicTake!=null||retired==null||retired.Length<2||mailbox.revision==long.MaxValue)throw new InvalidOperationException("Prepared drain lost source/retirement storage.");}
            internal void ValidateTakeBatch(long expected,int expectedCount,T[] ordered,int offset,NavOwnedStorage[] retired)
            {
                if(mailbox.revision!=expected||mailbox.count!=expectedCount||expectedCount<0||mailbox.publicTake!=null||ordered==null||offset<0||ordered.Length-offset!=expectedCount||retired==null||retired.Length!=2*expectedCount||expected>long.MaxValue-expectedCount)throw new InvalidOperationException("Prepared batch drain lost its exact source.");
                for(int i=0;i<expectedCount;i++)if(!ReferenceEquals(mailbox.items[(mailbox.head+i)%mailbox.capacity],ordered[offset+i]))throw new InvalidOperationException("Prepared batch answer FIFO changed.");
            }
            internal void TakeBatchNoThrow(NavOwnedStorage[] retired)
            {
                int count=mailbox.count;
                for(int i=0;i<count;i++){int slot=mailbox.head;retired[2*i]=mailbox.retention?.TakeSlotNoThrow(slot);if(mailbox.slotOwners!=null){retired[2*i+1]=mailbox.slotOwners[slot];mailbox.slotOwners[slot]=null;}mailbox.items[slot]=default;mailbox.head=(slot+1)%mailbox.capacity;mailbox.count--;mailbox.revision++;}
            }
            internal void ValidateTransferOne(long expected,int expectedCount,T expectedHead,Action<T> expectedTake,NavOwnedStorage[] retired)
            {if(mailbox.revision!=expected||mailbox.count!=expectedCount||expectedCount<1||!ReferenceEquals(mailbox.items[mailbox.head],expectedHead)||!ReferenceEquals(mailbox.publicTake,expectedTake)||retired==null||retired.Length<2||mailbox.revision==long.MaxValue)throw new InvalidOperationException("Prepared ingress drain lost its exact source/handler.");}
            internal void ValidateAppendOne(long expected,int expectedCount,T[] expectedItems,NavOwnedStorage[] expectedOwners,Action<T> expectedValidate,Action<T> expectedTake)
            {int slot=(mailbox.head+mailbox.count)%mailbox.capacity;if(mailbox.revision!=expected||mailbox.count!=expectedCount||expectedCount<0||expectedCount==mailbox.capacity||mailbox.revision==long.MaxValue||!ReferenceEquals(mailbox.items,expectedItems)||!ReferenceEquals(mailbox.slotOwners,expectedOwners)||expectedItems==null||expectedItems.Length!=mailbox.capacity||expectedOwners==null||expectedOwners.Length!=mailbox.capacity||mailbox.retention!=null||!ReferenceEquals(mailbox.validateEnqueue,expectedValidate)||!ReferenceEquals(mailbox.publicTake,expectedTake)||mailbox.acceptedEnqueue!=null||!ReferenceEquals(mailbox.items[slot],null)||expectedOwners[slot]!=null)throw new InvalidOperationException("Prepared append lost its exact request backing/handlers.");}
            internal void ValidateAppendBatch(long expected,int expectedCount,int appendCount,T[] expectedItems,NavOwnedStorage[] expectedOwners,Action<T> expectedValidate,Action<T> expectedTake)
            {
                if(mailbox.revision!=expected||mailbox.count!=expectedCount||expectedCount<0||appendCount<1||appendCount>mailbox.capacity-expectedCount||expected>long.MaxValue-appendCount||!ReferenceEquals(mailbox.items,expectedItems)||expectedItems==null||expectedItems.Length!=mailbox.capacity||!ReferenceEquals(mailbox.slotOwners,expectedOwners)||expectedOwners==null||expectedOwners.Length!=mailbox.capacity||mailbox.retention!=null||!ReferenceEquals(mailbox.validateEnqueue,expectedValidate)||!ReferenceEquals(mailbox.publicTake,expectedTake)||mailbox.acceptedEnqueue!=null)throw new InvalidOperationException("Prepared append batch lost exact request source.");
                for(int i=0;i<appendCount;i++){int slot=(mailbox.head+mailbox.count+i)%mailbox.capacity;if(!ReferenceEquals(mailbox.items[slot],null)||expectedOwners[slot]!=null)throw new InvalidOperationException("Prepared append batch spare slot changed.");}
            }
            internal void ValidateDiscardAndAppend(long expected,T[] observed,int appendCount,T[] expectedItems,NavOwnedStorage[] expectedOwners,Action<T> expectedValidate,Action<T> expectedTake,NavOwnedStorage[] retired)
            {
                if(mailbox.revision!=expected||observed==null||mailbox.count!=observed.Length||appendCount<0||appendCount>mailbox.capacity||expected>long.MaxValue-observed.Length-appendCount||!ReferenceEquals(mailbox.items,expectedItems)||expectedItems==null||expectedItems.Length!=mailbox.capacity||!ReferenceEquals(mailbox.slotOwners,expectedOwners)||expectedOwners==null||expectedOwners.Length!=mailbox.capacity||mailbox.retention!=null||!ReferenceEquals(mailbox.validateEnqueue,expectedValidate)||!ReferenceEquals(mailbox.publicTake,expectedTake)||mailbox.acceptedEnqueue!=null||retired==null||retired.Length!=2*observed.Length)throw new InvalidOperationException("Prepared discard/append lost its exact request backing.");
                for(int i=0;i<observed.Length;i++){int slot=(mailbox.head+i)%mailbox.capacity;if(!ReferenceEquals(mailbox.items[slot],observed[i]))throw new InvalidOperationException("Prepared request FIFO changed.");}
                for(int i=observed.Length;i<mailbox.capacity;i++){int slot=(mailbox.head+i)%mailbox.capacity;if(!ReferenceEquals(mailbox.items[slot],null)||expectedOwners[slot]!=null)throw new InvalidOperationException("Prepared request spare slot changed.");}
            }
            internal void ValidateInitialAppend(long expected,Action<T> expectedValidate,Action<T> expectedTake)
            {if(mailbox.revision!=expected||mailbox.count!=0||mailbox.revision==long.MaxValue||mailbox.items.Length!=0||mailbox.slotOwners!=null||mailbox.retention!=null||!ReferenceEquals(mailbox.validateEnqueue,expectedValidate)||!ReferenceEquals(mailbox.publicTake,expectedTake)||mailbox.acceptedEnqueue!=null)throw new InvalidOperationException("Prepared initial append lost its empty source.");}
            internal void AppendOneNoThrow(T item,NavOwnedStorage prepaidOwner)
            {int slot=(mailbox.head+mailbox.count)%mailbox.capacity;mailbox.items[slot]=item;mailbox.slotOwners[slot]=prepaidOwner;mailbox.count++;mailbox.revision++;}
            internal void ValidateRetainedAppend(long expected,int expectedCount,T[] items,NavMailboxRetention retention,Action<T> validate,Action<T> accepted)
            {int slot=(mailbox.head+mailbox.count)%mailbox.capacity;if(mailbox.revision!=expected||mailbox.count!=expectedCount||expectedCount<0||expectedCount==mailbox.capacity||mailbox.revision==long.MaxValue||!ReferenceEquals(mailbox.items,items)||items.Length!=mailbox.capacity||!ReferenceEquals(mailbox.retention,retention)||retention==null||retention.Owners.Length!=mailbox.capacity||mailbox.slotOwners!=null||mailbox.publicTake!=null||!ReferenceEquals(mailbox.validateEnqueue,validate)||!ReferenceEquals(mailbox.acceptedEnqueue,accepted)||!ReferenceEquals(items[slot],null)||retention.Owners[slot]!=null)throw new InvalidOperationException("Prepared retained append lost source/backing.");}
            internal void AppendRetainedNoThrow(T item,NavOwnedStorage prepaidOwner)
            {int slot=(mailbox.head+mailbox.count)%mailbox.capacity;mailbox.retention.Owners[slot]=prepaidOwner;mailbox.items[slot]=item;mailbox.count++;mailbox.revision++;}
            internal void TakeOneNoThrow(NavOwnedStorage[] retired)
            {int slot=mailbox.head;retired[0]=mailbox.retention?.TakeSlotNoThrow(slot);if(mailbox.slotOwners!=null){retired[1]=mailbox.slotOwners[slot];mailbox.slotOwners[slot]=null;}mailbox.items[slot]=default;mailbox.head=(slot+1)%mailbox.capacity;mailbox.count--;mailbox.revision++;}
            internal void InstallNoThrow(T[] replacement,int replacementCount,NavOwnedStorage[] owners=null){mailbox.slotOwners=owners;mailbox.items=replacement;mailbox.head=0;mailbox.count=replacementCount;mailbox.revision++;}
            public void Dispose()=>System.Threading.Monitor.Exit(mailbox.gate);
        }
        // Explicit legacy checkpoint replay only. The Session prepares input
        // order and pins off gate; publication reuses the real mailbox backing.
        internal sealed class PreparedCheckpointReplay : NavOwnedStorage,IDisposable
        {
            private static long Layout(int capacity)=>checked(NavStorageLayout.ObjectBytesBound(typeof(PreparedCheckpointReplay))+4*32L+32L*capacity);
            internal readonly T[] Replay;private readonly NavOwnedStorage[] pins,retired,retiredTransport;
            private readonly NavMailbox<T> source;private readonly T[] observedItems;private readonly NavOwnedStorage[] observedOwners;private readonly long observedRevision;private readonly int observedCount;private readonly object observedAuthority;private readonly Action<T> observedValidate,observedAccepted,observedTake;private readonly Func<T,NavOwnedStorage> observedSelector;
            internal int Count;private int retiredTransportCount;private bool disposed;
            private PreparedCheckpointReplay(NavMailbox<T> source):base(Layout(source.capacity))
            {this.source=source;Replay=new T[source.capacity];pins=new NavOwnedStorage[source.capacity];retired=new NavOwnedStorage[source.capacity];retiredTransport=new NavOwnedStorage[source.capacity];observedItems=source.items;observedOwners=source.slotOwners;observedRevision=source.revision;observedCount=source.count;observedAuthority=source.authorityGate;observedValidate=source.validateEnqueue;observedAccepted=source.acceptedEnqueue;observedTake=source.publicTake;observedSelector=source.ownerSelector;Retain();}
            internal static PreparedCheckpointReplay Create(NavMailbox<T> source,INavStorageReservation r)
            {using(var a=new NavStorageAllocation(r.TryReserve)){a.Reserve(Layout(source.capacity));lock(source.gate){source.RejectTransferReentry();if(source.retention!=null)throw new InvalidOperationException("Legacy request replay cannot replace answer retention.");return a.Register(new PreparedCheckpointReplay(source));}}}
            internal bool SourceCurrent(){lock(source.gate)return !source.closed&&source.transferThread==0&&source.revision==observedRevision&&source.count==observedCount&&ReferenceEquals(source.items,observedItems)&&ReferenceEquals(source.slotOwners,observedOwners)&&source.retention==null&&ReferenceEquals(source.authorityGate,observedAuthority)&&ReferenceEquals(source.validateEnqueue,observedValidate)&&ReferenceEquals(source.acceptedEnqueue,observedAccepted)&&ReferenceEquals(source.publicTake,observedTake)&&ReferenceEquals(source.ownerSelector,observedSelector)&&Count<=source.capacity&&(Count==0||source.items.Length==source.capacity);}
            internal void CopyQueued(){lock(source.gate){if(!SourceCurrent()||Count+source.count>Replay.Length)throw new InvalidOperationException("Legacy checkpoint replay changed or exceeds capacity.");for(int i=0;i<source.count;i++)Replay[Count++]=source.items[(source.head+i)%source.capacity];}}
            internal void PinInputs(){for(int i=0;i<Count;i++){source.validateEnqueue?.Invoke(Replay[i]);var owner=source.ownerSelector?.Invoke(Replay[i]);if(owner!=null){RequireEdge(owner);owner.Retain();pins[i]=owner;}}}
            internal void ApplyNoThrow(){lock(source.gate){for(int i=0;i<source.items.Length;i++){source.items[i]=default;if(source.slotOwners!=null){retired[i]=source.slotOwners[i];source.slotOwners[i]=null;}}for(int i=0;i<Count;i++){source.items[i]=Replay[i];if(source.slotOwners!=null){source.slotOwners[i]=pins[i];pins[i]=null;}}source.head=0;source.count=Count;source.revision++;}}
            internal void RetireTransportNoThrow(NavOwnedStorage owner){if(owner!=null)retiredTransport[retiredTransportCount++]=owner;}
            public void Dispose(){if(disposed)return;disposed=true;Release();}
            internal override void ReleaseOwner()=>Dispose();
            protected override void ReleaseEdges(){foreach(var p in pins)p?.Release();foreach(var p in retired)p?.Release();foreach(var p in retiredTransport)p?.Release();}
        }
        internal PreparedWriteGate EnterPreparedWrite()=>new PreparedWriteGate(this);
    }
}
