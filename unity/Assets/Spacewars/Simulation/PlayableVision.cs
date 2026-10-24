using System;
using System.Collections.Generic;
using System.Linq;

namespace Spacewars.Simulation
{
    public readonly struct VisionSource
    {
        public readonly NavPoint Position;
        public readonly double Radius;
        public VisionSource(NavPoint position,double radius)
        {
            if(!Finite(position.X)||!Finite(position.Z)||!Finite(radius)||radius<=0)throw new ArgumentException("Invalid vision source.");
            Position=position;Radius=radius;
        }
        internal static bool Finite(double n)=>!double.IsNaN(n)&&!double.IsInfinity(n);
    }

    // Deliberately excludes HP, queues, progress, targets and other live state.
    public sealed class KnownBuilding
    {
        public KnownBuilding(int id,int team,PlayableOwner owner,PlayableBuildingKind kind,NavPoint position,double heading,bool refineryUpgraded=false)
        {RefineryUpgraded=refineryUpgraded;Id=id;Team=team;Owner=owner;Kind=kind;Position=position;Heading=heading;}
        public bool RefineryUpgraded{get;} public int Id{get;} public int Team{get;} public PlayableOwner Owner{get;}
        public PlayableBuildingKind Kind{get;} public NavPoint Position{get;} public double Heading{get;}
    }

    public sealed class TeamVisionSnapshot
    {
        private readonly VisionSource[] sourceValues;
        internal TeamVisionSnapshot(int team,long revision,long fogRevision,double halfWidth,double halfDepth,double cellSize,VisionSource[] sources,long[] cells,byte[] coverage,KnownBuilding[] memory)
        {
            Team=team;Revision=revision;FogRevision=fogRevision;HalfWidth=halfWidth;HalfDepth=halfDepth;CellSize=cellSize;
            sourceValues=(VisionSource[])sources.Clone();Sources=Array.AsReadOnly(sourceValues);DiscoveredCells=Array.AsReadOnly((long[])cells.Clone());
            Coverage=Array.AsReadOnly((byte[])coverage.Clone());KnownBuildings=Array.AsReadOnly((KnownBuilding[])memory.Clone());
        }
        public int Team{get;} public long Revision{get;} public long FogRevision{get;} public double HalfWidth{get;} public double HalfDepth{get;} public double CellSize{get;}
        public IReadOnlyList<VisionSource> Sources{get;} public IReadOnlyList<long> DiscoveredCells{get;}
        public IReadOnlyList<byte> Coverage{get;} public IReadOnlyList<KnownBuilding> KnownBuildings{get;}
        public bool IsVisible(NavPoint p)=>PlayableVision.VisibleArray(sourceValues,p);
    }

    // One owner-thread instance per team. No renderer, terrain or camera dependency.
    public sealed partial class PlayableVision
    {
        // Technical bound: fixed 64KiB history per team, independent of discovery-cell count.
        // Sampling precision is presentation-only and must never authorize entity visibility.
        public const int RasterResolution=256;
        private readonly int team;
        private readonly double halfWidth,halfDepth,cellSize,feather;
        private VisionSource[] sources=Array.Empty<VisionSource>();
        private readonly HashSet<long> discovered=new HashSet<long>();
        private readonly Dictionary<int,KnownBuilding> known=new Dictionary<int,KnownBuilding>();
        private readonly byte[] coverage=new byte[RasterResolution*RasterResolution];
        private long revision;
        private TeamVisionSnapshot cached;
        // Derived owner-thread inputs only. Their storage is bounded by this vision's
        // largest source/building input sets; it is never serialized or exposed to callers.
        private readonly List<VisionSource> refreshInput=new List<VisionSource>();
        private VisionSource[] lastRefreshSources;
        private KnownBuilding[] lastRefreshBuildings;
        public PlayableVision(int team,double halfWidth,double halfDepth,double cellSize,double feather)
        {
            if(!Positive(halfWidth)||!Positive(halfDepth)||!Positive(cellSize)||!Positive(feather))throw new ArgumentException("Invalid vision bounds.");
            this.team=team;this.halfWidth=halfWidth;this.halfDepth=halfDepth;this.cellSize=cellSize;this.feather=feather;
        }
        private static bool Positive(double n)=>VisionSource.Finite(n)&&n>0;
        public long CoverageUpdates{get;private set;}
        public bool IsVisible(NavPoint point)=>VisibleArray(sources,point);
        public bool IsDiscovered(NavPoint point)=>VisionSource.Finite(point.X)&&VisionSource.Finite(point.Z)&&Math.Abs(point.X)<=halfWidth&&Math.Abs(point.Z)<=halfDepth&&discovered.Contains(CellKey((int)Math.Floor(point.X/cellSize),(int)Math.Floor(point.Z/cellSize)));
        public static long CellKey(int x,int z)=>((long)x<<32)|(uint)z;
        public static bool Visible(IReadOnlyList<VisionSource> current,NavPoint point)
        {
            if(current is VisionSource[] array)return VisibleArray(array,point);
            if(!VisionSource.Finite(point.X)||!VisionSource.Finite(point.Z))return false;
            foreach(var s in current)
            {
                double x=point.X-s.Position.X,z=point.Z-s.Position.Z;
                // Numerical tolerance mirrors the reference point test, not a tunable radius.
                if(x*x+z*z<=(s.Radius+0.0001)*(s.Radius+0.0001))return true;
            }
            return false;
        }
        // Live vision and detached snapshots own these arrays. Avoid interface
        // enumeration per query without exposing storage or changing generic callers.
        internal static bool VisibleArray(VisionSource[] current,NavPoint point)
        {
            if(!VisionSource.Finite(point.X)||!VisionSource.Finite(point.Z))return false;
            foreach(var s in current)
            {
                double x=point.X-s.Position.X,z=point.Z-s.Position.Z;
                if(x*x+z*z<=(s.Radius+0.0001)*(s.Radius+0.0001))return true;
            }
            return false;
        }
        public static bool MayShareSampledVisiblePath(IReadOnlyList<VisionSource> current,NavPoint from,NavPoint to,double maximumStep)
        {
            if(current==null||!VisionSource.Finite(maximumStep)||maximumStep<=0)throw new ArgumentException("Invalid visible-path scope.");
            var seen=new bool[current.Count];var queue=new Queue<int>();
            bool Contains(VisionSource s,NavPoint p){double x=p.X-s.Position.X,z=p.Z-s.Position.Z,r=s.Radius+0.0001;return x*x+z*z<=r*r;}
            for(int i=0;i<current.Count;i++)if(Contains(current[i],from)){seen[i]=true;queue.Enqueue(i);}
            while(queue.Count>0){
                int i=queue.Dequeue();if(Contains(current[i],to))return true;
                for(int j=0;j<current.Count;j++)if(!seen[j]){
                    double x=current[i].Position.X-current[j].Position.X,z=current[i].Position.Z-current[j].Position.Z;
                    // Accepted segment samples are at most maximumStep apart. Widen
                    // further by a second step so numerical/tiny-gap cases are retained.
                    double reach=(current[i].Radius+0.0001)+(current[j].Radius+0.0001)+2*maximumStep;
                    double d=x*x+z*z,limit=reach*reach;
                    if(!VisionSource.Finite(d)||!VisionSource.Finite(limit)||d<=limit){seen[j]=true;queue.Enqueue(j);}
                }
            }
            return false;
        }
        public void Refresh(IEnumerable<VisionSource> nextSources,IReadOnlyList<KnownBuilding> actualBuildings)
        {
            refreshInput.Clear();foreach(var source in nextSources)refreshInput.Add(source);
            if(SameRefreshInputs(actualBuildings)){
                using var reused=PerformanceProfile.Measure(ProfileStage.VisionReuse);return;
            }
            using var measured=PerformanceProfile.Measure(ProfileStage.VisionRefresh);
            var input=refreshInput.ToArray();
            var next=input.OrderBy(s=>s.Position.X).ThenBy(s=>s.Position.Z).ThenBy(s=>s.Radius).ToArray();
            bool changed=next.Length!=sources.Length;
            for(int i=0;!changed&&i<next.Length;i++)changed=next[i].Position.X!=sources[i].Position.X||next[i].Position.Z!=sources[i].Position.Z||next[i].Radius!=sources[i].Radius;
            if(changed){var previous=sources;sources=next;Explore(previous);}
            bool memoryChanged=false;
            var confirmed=new HashSet<int>();
            foreach(var b in actualBuildings)
            {
                if(b.Team==team||!IsVisible(b.Position))continue;
                confirmed.Add(b.Id);
                if(!known.TryGetValue(b.Id,out var prior)||!Same(prior,b)){known[b.Id]=b;memoryChanged=true;}
            }
            foreach(var prior in known.Values.ToArray())
                if(IsVisible(prior.Position)&&!confirmed.Contains(prior.Id)){known.Remove(prior.Id);memoryChanged=true;}
            if(changed||memoryChanged){revision++;cached=null;}
            lastRefreshSources=input;lastRefreshBuildings=actualBuildings.ToArray();
        }
        private bool SameRefreshInputs(IReadOnlyList<KnownBuilding> buildings)
        {
            if(lastRefreshSources==null||lastRefreshBuildings==null||refreshInput.Count!=lastRefreshSources.Length||buildings.Count!=lastRefreshBuildings.Length)return false;
            for(int i=0;i<refreshInput.Count;i++){
                var a=refreshInput[i];var b=lastRefreshSources[i];
                if(a.Position.X!=b.Position.X||a.Position.Z!=b.Position.Z||a.Radius!=b.Radius)return false;
            }
            for(int i=0;i<buildings.Count;i++){
                var a=buildings[i];var b=lastRefreshBuildings[i];
                if(a==null||b==null||!Same(a,b))return false;
            }
            return true;
        }
        private static bool Same(KnownBuilding a,KnownBuilding b)=>a.Id==b.Id&&a.Team==b.Team&&a.Owner==b.Owner&&a.Kind==b.Kind&&a.Position.X==b.Position.X&&a.Position.Z==b.Position.Z&&a.Heading==b.Heading&&a.RefineryUpgraded==b.RefineryUpgraded;
        private void Explore(VisionSource[] previous)
        {
            CoverageUpdates++;
            int old=0;
            foreach(var s in sources)
            {
                // History is a monotone union/max. Unchanged circles contributed their
                // exact cells and coverage already; only current visibility uses all sources.
                while(old<previous.Length&&Compare(previous[old],s)<0)old++;
                if(old<previous.Length&&Compare(previous[old],s)==0)continue;
                int minX=Math.Max((int)Math.Floor(-halfWidth/cellSize),(int)Math.Floor((s.Position.X-s.Radius)/cellSize));
                int maxX=Math.Min((int)Math.Ceiling(halfWidth/cellSize)-1,(int)Math.Floor((s.Position.X+s.Radius)/cellSize));
                int minZ=Math.Max((int)Math.Floor(-halfDepth/cellSize),(int)Math.Floor((s.Position.Z-s.Radius)/cellSize));
                int maxZ=Math.Min((int)Math.Ceiling(halfDepth/cellSize)-1,(int)Math.Floor((s.Position.Z+s.Radius)/cellSize));
                double reach=s.Radius+cellSize*Math.Sqrt(2)/2;
                for(int z=minZ;z<=maxZ;z++)for(int x=minX;x<=maxX;x++)
                {
                    double dx=(x+.5)*cellSize-s.Position.X,dz=(z+.5)*cellSize-s.Position.Z;
                    if(dx*dx+dz*dz<=reach*reach)discovered.Add(CellKey(x,z));
                }
                int left=Pixel(s.Position.X-s.Radius-feather,halfWidth),right=Pixel(s.Position.X+s.Radius+feather,halfWidth);
                int bottom=Pixel(s.Position.Z-s.Radius-feather,halfDepth),top=Pixel(s.Position.Z+s.Radius+feather,halfDepth);
                for(int y=bottom;y<=top;y++)for(int x=left;x<=right;x++)
                {
                    int i=y*RasterResolution+x;if(coverage[i]==255)continue;
                    double dx=((x+.5)/RasterResolution*2-1)*halfWidth-s.Position.X,dz=((y+.5)/RasterResolution*2-1)*halfDepth-s.Position.Z;
                    double amount=Math.Max(0,Math.Min(1,(s.Radius+feather-Math.Sqrt(dx*dx+dz*dz))/feather));
                    coverage[i]=Math.Max(coverage[i],(byte)Math.Round(amount*255));
                }
            }
        }
        private static int Compare(VisionSource a,VisionSource b)
        {
            int c=a.Position.X.CompareTo(b.Position.X);if(c!=0)return c;
            c=a.Position.Z.CompareTo(b.Position.Z);return c!=0?c:a.Radius.CompareTo(b.Radius);
        }
        private static int Pixel(double n,double half)=>Math.Max(0,Math.Min(RasterResolution-1,(int)Math.Floor((n+half)/(2*half)*RasterResolution)));
        public TeamVisionSnapshot Snapshot()
        {
            if(cached==null)cached=new TeamVisionSnapshot(team,revision,CoverageUpdates,halfWidth,halfDepth,cellSize,sources,discovered.OrderBy(x=>x).ToArray(),coverage,known.Values.OrderBy(b=>b.Id).ToArray());
            return cached;
        }
        public static double BuildingRadius(PlayableProfile p,PlayableBuildingKind kind,ConstructionPhase phase)
        {
            if(phase==ConstructionPhase.Pending)return 0;
            double radius;
            switch(kind){case PlayableBuildingKind.Hangar:radius=p.HangarVisionRange;break;case PlayableBuildingKind.ScientificCenter:radius=p.ScienceVisionRange;break;case PlayableBuildingKind.Headquarters:radius=p.HeadquartersVisionRange;break;case PlayableBuildingKind.Outpost:radius=p.OutpostVisionRange;break;case PlayableBuildingKind.Factory:radius=p.FactoryVisionRange;break;case PlayableBuildingKind.Refinery:radius=p.RefineryVisionRange;break;case PlayableBuildingKind.Mine:radius=p.MineVisionRange;break;default:throw new ArgumentOutOfRangeException(nameof(kind));}
            return phase==ConstructionPhase.Ready?radius:radius*p.ConstructionVisionMultiplier;
        }
    }
}
