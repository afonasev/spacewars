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
        internal TeamVisionSnapshot(int team,long revision,long fogRevision,double halfWidth,double halfDepth,double cellSize,VisionSource[] sources,long[] cells,byte[] coverage,KnownBuilding[] memory)
        {
            Team=team;Revision=revision;FogRevision=fogRevision;HalfWidth=halfWidth;HalfDepth=halfDepth;CellSize=cellSize;
            Sources=Array.AsReadOnly((VisionSource[])sources.Clone());DiscoveredCells=Array.AsReadOnly((long[])cells.Clone());
            Coverage=Array.AsReadOnly((byte[])coverage.Clone());KnownBuildings=Array.AsReadOnly((KnownBuilding[])memory.Clone());
        }
        public int Team{get;} public long Revision{get;} public long FogRevision{get;} public double HalfWidth{get;} public double HalfDepth{get;} public double CellSize{get;}
        public IReadOnlyList<VisionSource> Sources{get;} public IReadOnlyList<long> DiscoveredCells{get;}
        public IReadOnlyList<byte> Coverage{get;} public IReadOnlyList<KnownBuilding> KnownBuildings{get;}
        public bool IsVisible(NavPoint p)=>PlayableVision.Visible(Sources,p);
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
        public PlayableVision(int team,double halfWidth,double halfDepth,double cellSize,double feather)
        {
            if(!Positive(halfWidth)||!Positive(halfDepth)||!Positive(cellSize)||!Positive(feather))throw new ArgumentException("Invalid vision bounds.");
            this.team=team;this.halfWidth=halfWidth;this.halfDepth=halfDepth;this.cellSize=cellSize;this.feather=feather;
        }
        private static bool Positive(double n)=>VisionSource.Finite(n)&&n>0;
        public long CoverageUpdates{get;private set;}
        public bool IsVisible(NavPoint point)=>Visible(sources,point);
        public bool IsDiscovered(NavPoint point)=>VisionSource.Finite(point.X)&&VisionSource.Finite(point.Z)&&Math.Abs(point.X)<=halfWidth&&Math.Abs(point.Z)<=halfDepth&&discovered.Contains(CellKey((int)Math.Floor(point.X/cellSize),(int)Math.Floor(point.Z/cellSize)));
        public static long CellKey(int x,int z)=>((long)x<<32)|(uint)z;
        public static bool Visible(IReadOnlyList<VisionSource> current,NavPoint point)
        {
            if(!VisionSource.Finite(point.X)||!VisionSource.Finite(point.Z))return false;
            foreach(var s in current)
            {
                double x=point.X-s.Position.X,z=point.Z-s.Position.Z;
                // Numerical tolerance mirrors the reference point test, not a tunable radius.
                if(x*x+z*z<=(s.Radius+0.0001)*(s.Radius+0.0001))return true;
            }
            return false;
        }
        public void Refresh(IEnumerable<VisionSource> nextSources,IReadOnlyList<KnownBuilding> actualBuildings)
        {
            var next=nextSources.OrderBy(s=>s.Position.X).ThenBy(s=>s.Position.Z).ThenBy(s=>s.Radius).ToArray();
            bool changed=next.Length!=sources.Length;
            for(int i=0;!changed&&i<next.Length;i++)changed=next[i].Position.X!=sources[i].Position.X||next[i].Position.Z!=sources[i].Position.Z||next[i].Radius!=sources[i].Radius;
            if(changed){sources=next;Explore();}
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
        }
        private static bool Same(KnownBuilding a,KnownBuilding b)=>a.Id==b.Id&&a.Team==b.Team&&a.Owner==b.Owner&&a.Kind==b.Kind&&a.Position.X==b.Position.X&&a.Position.Z==b.Position.Z&&a.Heading==b.Heading&&a.RefineryUpgraded==b.RefineryUpgraded;
        private void Explore()
        {
            CoverageUpdates++;
            foreach(var s in sources)
            {
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
                    double dx=((x+.5)/RasterResolution*2-1)*halfWidth-s.Position.X,dz=((y+.5)/RasterResolution*2-1)*halfDepth-s.Position.Z;
                    double amount=Math.Max(0,Math.Min(1,(s.Radius+feather-Math.Sqrt(dx*dx+dz*dz))/feather));
                    int i=y*RasterResolution+x;coverage[i]=Math.Max(coverage[i],(byte)Math.Round(amount*255));
                }
            }
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
            switch(kind){case PlayableBuildingKind.ScientificCenter:radius=p.ScienceVisionRange;break;case PlayableBuildingKind.Headquarters:radius=p.HeadquartersVisionRange;break;case PlayableBuildingKind.Outpost:radius=p.OutpostVisionRange;break;case PlayableBuildingKind.Factory:radius=p.FactoryVisionRange;break;case PlayableBuildingKind.Refinery:radius=p.RefineryVisionRange;break;case PlayableBuildingKind.Mine:radius=p.MineVisionRange;break;default:throw new ArgumentOutOfRangeException(nameof(kind));}
            return phase==ConstructionPhase.Ready?radius:radius*p.ConstructionVisionMultiplier;
        }
    }
}
