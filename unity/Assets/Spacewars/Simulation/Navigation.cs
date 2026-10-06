using System;
using System.Collections.Generic;

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

    public sealed class NavGeometry
    {
        private readonly NavObstacle[] obstacles;
        private readonly IReadOnlyList<NavObstacle> obstacleView;
        public NavGeometry(double halfExtent, NavObstacle[] obstacles, int revision)
        {
            if (halfExtent <= 0d || Double.IsNaN(halfExtent) || Double.IsInfinity(halfExtent)) throw new ArgumentException("Invalid half extent.");
            if (revision < 1) throw new ArgumentException("Geometry revision must be positive.");
            HalfExtent = halfExtent; Revision = revision; this.obstacles = obstacles == null ? new NavObstacle[0] : (NavObstacle[])obstacles.Clone(); obstacleView = Array.AsReadOnly(this.obstacles);
        }
        public double HalfExtent { get; private set; }
        public int Revision { get; private set; }
        public IReadOnlyList<NavObstacle> Obstacles { get { return obstacleView; } }
        public bool IsFree(NavPoint point, double radius)
        {
            if (radius < 0d || Double.IsNaN(radius) || Double.IsInfinity(radius) || Double.IsNaN(point.X) || Double.IsInfinity(point.X) || Double.IsNaN(point.Z) || Double.IsInfinity(point.Z) || Math.Abs(point.X) + radius > HalfExtent || Math.Abs(point.Z) + radius > HalfExtent) return false;
            for (int i = 0; i < obstacles.Length; i++) if (point.X+radius>=obstacles[i].MinX&&point.X-radius<=obstacles[i].MaxX&&point.Z+radius>=obstacles[i].MinZ&&point.Z-radius<=obstacles[i].MaxZ&&PointRectDistanceSquared(point, obstacles[i]) <= radius * radius) return false;
            return true;
        }
        public bool SegmentFree(NavPoint from, NavPoint to, double radius)
        {
            if (!IsFree(from, radius) || !IsFree(to, radius)) return false;
            for (int i = 0; i < obstacles.Length; i++) if (Math.Max(from.X,to.X)+radius>=obstacles[i].MinX&&Math.Min(from.X,to.X)-radius<=obstacles[i].MaxX&&Math.Max(from.Z,to.Z)+radius>=obstacles[i].MinZ&&Math.Min(from.Z,to.Z)-radius<=obstacles[i].MaxZ&&SegmentRectDistanceSquared(from, to, obstacles[i]) <= radius * radius) return false;
            return true;
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
    public sealed class NavigationProfile
    {
        public const string DefaultProfileId = "unity-navigation-diagnostic-v1";
        public NavigationProfile() : this(DefaultProfileId, 1, .72d, 4d, 4.5d, .8d, .15d, 3.6d, .08d) { }
        public NavigationProfile(string profileId, int revision, double radius, double tankSpeed, double tankTurnSpeed, double gridCell, double arrivalTolerance, double localDetourDistance, double forwardAlignmentAngle, double cameraOrthoSize=56, double cameraHeight=85, double cameraOffsetZ=-40, double repairRetrySeconds=.3, double arrivalSlotSpacing=3.6, double blockedTimeoutSeconds=5, double localGridCell=.2)
        { ProfileId = profileId; Revision = revision; Radius = radius; TankSpeed = tankSpeed; TankTurnSpeed = tankTurnSpeed; GridCell = gridCell; ArrivalTolerance = arrivalTolerance; LocalDetourDistance = localDetourDistance; ForwardAlignmentAngle = forwardAlignmentAngle; CameraOrthoSize=cameraOrthoSize; CameraHeight=cameraHeight; CameraOffsetZ=cameraOffsetZ; RepairRetrySeconds=repairRetrySeconds; ArrivalSlotSpacing=arrivalSlotSpacing; BlockedTimeoutSeconds=blockedTimeoutSeconds; LocalGridCell=localGridCell; Validate(this); }
        public string ProfileId { get; private set; } public int Revision { get; private set; } public double Radius { get; private set; } public double TankSpeed { get; private set; } public double TankTurnSpeed { get; private set; } public double GridCell { get; private set; } public double ArrivalTolerance { get; private set; } public double LocalDetourDistance { get; private set; } public double ForwardAlignmentAngle { get; private set; }
        public double LocalGridCell {get;private set;}
        public double BlockedTimeoutSeconds {get;private set;}
        public double ArrivalSlotSpacing {get;private set;}
        public double RepairRetrySeconds {get;private set;}
        public double CameraOrthoSize {get;private set;} public double CameraHeight {get;private set;} public double CameraOffsetZ {get;private set;}
        public NavigationProfile ForUnit(double radius,double speed,double turn)=>new NavigationProfile(ProfileId,Revision,radius,speed,turn,GridCell,ArrivalTolerance,LocalDetourDistance,ForwardAlignmentAngle,CameraOrthoSize,CameraHeight,CameraOffsetZ,RepairRetrySeconds,ArrivalSlotSpacing,BlockedTimeoutSeconds,LocalGridCell);
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
        public static void Validate(NavigationProfile profile)
        { if (profile == null || String.IsNullOrWhiteSpace(profile.ProfileId) || profile.Revision < 1) throw new ArgumentException("Navigation profile provenance is incomplete."); foreach (NavigationProfileField field in Metadata) { double value = field.Read(profile); if (Double.IsNaN(value) || Double.IsInfinity(value) || value < field.Minimum || value > field.Maximum) throw new ArgumentException("Invalid navigation profile field: " + field.Path); } }
    }

    public struct NavRouteIdentity
    {
        public readonly long Session, Request, Order; public readonly string EntityId, ProfileId; public readonly int ProfileRevision, GeometryRevision;
        public NavRouteIdentity(long session, long request, string entityId, long order, string profileId, int profileRevision, int geometryRevision) { Session = session; Request = request; EntityId = entityId; Order = order; ProfileId = profileId; ProfileRevision = profileRevision; GeometryRevision = geometryRevision; }
        public bool Matches(NavRouteIdentity other) { return Session == other.Session && Request == other.Request && Order == other.Order && EntityId == other.EntityId && ProfileId == other.ProfileId && ProfileRevision == other.ProfileRevision && GeometryRevision == other.GeometryRevision; }
    }

    public sealed class NavMailbox<T>
    {
        public const int Capacity = 256; private readonly Queue<T> queue = new Queue<T>(); private readonly object gate = new object();
        public int Count { get { lock(gate) return queue.Count; } }
        // The owner must quiesce producers and consumers before a checkpoint.
        public T[] CopyItems() { lock(gate) return queue.ToArray(); }
        public bool TryEnqueue(T item) { lock (gate) { if (queue.Count >= Capacity) return false; queue.Enqueue(item); return true; } }
        public bool TryDequeue(out T item) { lock (gate) { if (queue.Count == 0) { item = default(T); return false; } item = queue.Dequeue(); return true; } }
    }
}
