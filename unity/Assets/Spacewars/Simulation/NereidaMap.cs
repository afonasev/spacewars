using System;
using System.Collections.Generic;
using System.Linq;

namespace Spacewars.Simulation
{
    [Serializable] public sealed class NereidaProfileData
    {
        public string id = "nereida-greybox-v1";
        public int revision = 1;
        public double scale = 1, middleHeight = 6, homeHeight = 12, waterDepth = 4, deckThickness = 1;
    }
    public sealed class NereidaField
    {
        private readonly System.Reflection.FieldInfo field;
        public string Path { get; } public string Group => "Nereida geometry";
        public string Label { get; } public string Description { get; } public string Unit { get; }
        public double Minimum { get; } public double Maximum { get; } public double Step { get; }
        public NereidaField(string key, string label, string description, string unit, double min, double max, double step)
        { field = typeof(NereidaProfileData).GetField(key); Path = "maps.nereida." + key; Label = label; Description = description; Unit = unit; Minimum = min; Maximum = max; Step = step; }
        public double Read(NereidaProfileData p) => (double)field.GetValue(p);
        public void Write(NereidaProfileData p, double value)
        { if (double.IsNaN(value) || double.IsInfinity(value) || value < Minimum || value > Maximum) throw new ArgumentOutOfRangeException(Path); field.SetValue(p, value); }
    }
    public enum NereidaMode { Duel, Teams, FreeForAll }

    // Authored geometric prototype, intentionally not registered in the match lobby.
    // Stacked crowd execution, picking and combat must be integrated before promotion.
    public sealed class NereidaMap : ILayeredPlayableTerrain
    {
        public static readonly IReadOnlyList<NereidaField> Fields = Array.AsReadOnly(new[] {
            new NereidaField("scale", "Layout scale", "Scales the authored roads, site reserves and distances together.", "ratio", 1, 1.5, .05),
            new NereidaField("middleHeight", "Road elevation", "Bridge deck and outpost terrace above the lower ice route.", "m", 6, 10, .5),
            new NereidaField("homeHeight", "Station elevation", "Upper station terraces; must exceed the road elevation.", "m", 11, 18, .5),
            new NereidaField("waterDepth", "Visible water depth", "Water below the lower ice shelves.", "m", 2, 8, .5),
            new NereidaField("deckThickness", "Bridge thickness", "Structure below the road, preserving at least four metres of underpass clearance.", "m", .5, 2, .25) });
        public string Id => "nereida-greybox-v1";
        public int Revision { get; } public int SurfaceSemanticsVersion => 2;
        public double Scale { get; } public double MiddleHeight { get; } public double HomeHeight { get; }
        public double WaterDepth { get; } public double DeckThickness { get; }
        public double HalfExtent => 144 * Scale;
        public double DirectFireHeight => 1; // Inspection-only terrain; no new combat tuning.
        public IReadOnlyList<MapSupport> Supports { get; }
        public IReadOnlyList<MapSupport> Ramps { get; }
        public IReadOnlyList<NavObstacle> Solids => Array.Empty<NavObstacle>();
        // Water is the absence of a support, not an XZ blocker through both layers.
        public IReadOnlyList<NavObstacle> MovementBlockers => Solids;
        public IReadOnlyList<NavSurfaceTransition> SurfaceTransitions { get; }
        public LayeredNavigationProvider Provider { get; }
        public ILayeredNavigationProvider LayeredNavigation => Provider;
        public NereidaMap(NereidaProfileData p)
        {
            if (p == null || p.id != Id || p.revision < 1) throw new ArgumentException("Invalid Nereida profile identity.");
            foreach (var f in Fields) f.Write(p, f.Read(p));
            if (p.homeHeight <= p.middleHeight || p.middleHeight - p.deckThickness < 4) throw new ArgumentException("Invalid elevation or bridge clearance.");
            Revision = p.revision; Scale = p.scale; MiddleHeight = p.middleHeight; HomeHeight = p.homeHeight;
            WaterDepth = p.waterDepth; DeckThickness = p.deckThickness;
            var supports = new List<MapSupport>(); var ramps = new List<MapSupport>();
            var layers = new List<NavLayerSurface>(); var portals = new List<NavLayerPortal>();
            // Authored shoreline chains are shared by the lower navigation perimeter and mesh
            // supports. Only the protected route/site cores repeat; shore silhouettes do not.
            var lowerBoundary = new List<NavPoint>();
            for (int q=0;q<4;q++) {
                lowerBoundary.AddRange(LowerShore(q));
                lowerBoundary.Add(Point(q,-30,30));
            }
            var lower = new NavObstacle(new NavPolygon(lowerBoundary));
            layers.Add(new NavLayerSurface("ice", "lower", 0, 1, new NavTraversalArea(lower.Footprint, HalfExtent, new[] { Box(0,-10,-10,10,10) })));
            for (int q = 0; q < 4; q++) {
                var arm = new List<NavPoint> { Point(q,-16,16), Point(q,16,16) };
                arm.AddRange(LowerShore(q));
                AddSupport("ice-arm/"+q, new NavObstacle(new NavPolygon(arm)), 0, "ice");
                AddSupport("ice-ring/"+q, Box(q,-10,10,16,16), 0, "ice");
                AddSupport("ice-mine/"+q, Box(q,16,16,30,30), 0, "ice");
                AddLayer("home/"+q, HomeFootprint(q), HomeHeight, "upper");
                AddLayer("terrace/"+q, TerraceFootprint(q), MiddleHeight, "middle");
                AddLayer("bridge/"+q, Box(q,-30,30,30,54), MiddleHeight, "middle", true);
                // Two home exits; independent lower access avoids the bridge choke.
                Ramp("home-x/"+q, q, Box(q,54,88,72,108), Point(q,48,98), Point(q,78,98),
                    "terrace/"+q, "home/"+q, MiddleHeight, HomeHeight, Point(q,54,98), Point(q,72,98));
                Ramp("home-z/"+q, q, Box(q,88,54,108,72), Point(q,98,48), Point(q,98,78),
                    "terrace/"+q, "home/"+q, MiddleHeight, HomeHeight, Point(q,98,54), Point(q,98,72));
                Ramp("ice-access/"+q, q, Box(q,16,86,30,106), Point(q,10,96), Point(q,36,96),
                    "ice", "terrace/"+q, 0, MiddleHeight, Point(q,16,96), Point(q,30,96));
            }
            for (int q = 0; q < 4; q++) {
                Link("bridge-east/"+q, Point(q,24,42), "bridge/"+q, Point(q,36,42), "terrace/"+q, Box(q,18,32,42,52));
                Link("bridge-west/"+q, Point(q,-24,42), "bridge/"+q, Point(q,-36,42), "terrace/"+((q+1)%4), Box(q,-42,32,-18,52));
            }
            Supports = supports.AsReadOnly(); Ramps = ramps.AsReadOnly();
            SurfaceTransitions = Array.AsReadOnly(portals.Select(v => new NavSurfaceTransition(v.Id, v.Entry.SurfaceId, v.Exit.SurfaceId)).ToArray());
            string binding = NavContract.Digest(w => { foreach (var l in layers) l.Write(w); foreach (var v in portals) v.Write(w); });
            Provider = new LayeredNavigationProvider(Id, Revision, SurfaceSemanticsVersion, Revision, binding, layers, portals);

            void AddSupport(string name, NavObstacle bounds, double height, string surface, bool bridge = false)
                => supports.Add(new MapSupport(name, bounds, bridge, height, surfaceId:surface));
            void AddLayer(string name, NavObstacle bounds, double height, string family, bool bridge = false)
            { AddSupport(name, bounds, height, name, bridge); layers.Add(new NavLayerSurface(name, family, height, 1, new NavTraversalArea(bounds.Footprint, HalfExtent, Array.Empty<NavObstacle>()))); }
            void Link(string id, NavPoint a, string from, NavPoint b, string to, NavObstacle corridor)
                => portals.Add(new NavLayerPortal(id, new NavLocation(a,from), new NavLocation(b,to), NavPortalDirection.Both, true,
                    LayeredNavigationProvider.Distance(a,b), new NavTraversalArea(corridor.Footprint, HalfExtent, Array.Empty<NavObstacle>()), new[] { "ground" }));
            void Ramp(string id, int q, NavObstacle bounds, NavPoint entry, NavPoint exit, string from, string to, double low, double high, NavPoint foot, NavPoint crest)
            {
                double dx=crest.X-foot.X,dz=crest.Z-foot.Z,d2=dx*dx+dz*dz;
                var support = new MapSupport(id,bounds,false,low,new NavPoint(dx*(high-low)/d2,dz*(high-low)/d2),foot,id);
                ramps.Add(support); supports.Add(support);
                // Corridor includes the full footprint at both in-surface landing anchors.
                double pad=8*Scale;
                var corridor = new NavObstacle(Math.Min(entry.X,exit.X)-pad,Math.Min(entry.Z,exit.Z)-pad,Math.Max(entry.X,exit.X)+pad,Math.Max(entry.Z,exit.Z)+pad);
                Link(id,entry,from,exit,to,corridor);
            }
        }
        public NavPoint Point(int quarter, double x, double z)
        { for (int i=0;i<quarter;i++) { double old=x; x=-z; z=old; } return new NavPoint(x*Scale,z*Scale); }
        public NavObstacle Box(int q,double x,double z,double xx,double zz) => Polygon(q,x,z,xx,z,xx,zz,x,zz);
        private IReadOnlyList<NavPoint> LowerShore(int q)
        {
            // Minimum 32 m central corridor and the x=16 ramp landing stay intact.
            // q-specific bends/caps are map content; all coordinates scale with the profile.
            double[] xy;
            switch(q) {
                case 0: xy=new double[]{16,30, 18,53, 23,64, 20,80, 16,86, 16,106, 22,116, 19,132, 9,139, -9,136, -20,125, -22,110, -17,92, -23,76, -20,58, -16,30};break;
                case 1: xy=new double[]{16,30, 22,48, 19,70, 23,79, 16,86, 16,106, 20,121, 12,136, -4,139, -17,129, -23,116, -19,97, -22,85, -17,64, -21,46, -16,30};break;
                case 2: xy=new double[]{16,30, 19,46, 24,60, 20,76, 16,86, 16,106, 23,114, 20,129, 6,137, -14,133, -21,120, -18,104, -24,91, -20,72, -23,53, -16,30};break;
                default:xy=new double[]{16,30, 23,52, 20,65, 22,78, 16,86, 16,106, 19,120, 15,138, -2,140, -19,132, -23,117, -21,99, -17,80, -22,68, -19,49, -16,30};break;
            }
            var points=new List<NavPoint>();
            for(int i=0;i<xy.Length;i+=2)points.Add(Point(q,xy[i],xy[i+1]));
            return points.AsReadOnly();
        }
        private NavObstacle HomeFootprint(int q)
        {
            // Every precinct retains the same protected 60 x 60 station/build envelope.
            // The irregular spurs add a different authored ice-cliff edge around that core.
            switch(q) {
                case 0: return Polygon(q,72,72,92,72,98,67,111,68,116,72,132,72,132,91,137,97,136,111,132,116,132,132,105,132,99,137,86,136,81,132,72,132,72,111,67,105,68,93,72,88);
                case 1: return Polygon(q,72,72,96,72,102,68,116,67,121,72,132,72,132,103,137,108,136,121,132,125,132,132,112,132,106,137,93,137,88,132,72,132,72,119,67,113,68,100,72,94);
                case 2: return Polygon(q,72,72,88,72,93,67,108,68,114,72,132,72,132,96,137,102,137,114,132,120,132,132,101,132,95,137,82,137,78,132,72,132,72,108,67,102,68,90);
                default:return Polygon(q,72,72,101,72,107,67,120,68,124,72,132,72,132,88,137,94,136,108,132,113,132,132,116,132,110,137,97,136,92,132,72,132,72,122,67,116,68,103,72,98);
            }
        }
        private NavObstacle TerraceFootprint(int q)
        {
            // The inner 24 m lanes and fort reserve stay common; only the outer shelf edge varies.
            switch(q) {
                case 0: return Polygon(q,30,30,96,30,110,27,124,29,132,35,135,43,132,50,118,54,66,54,66,66,54,66,52,87,56,105,54,132,43,135,30,132);
                case 1: return Polygon(q,30,30,108,30,120,27,132,32,136,41,132,48,121,53,66,54,66,66,54,66,55,83,51,102,54,119,54,132,39,136,30,132);
                case 2: return Polygon(q,30,30,89,30,103,27,117,29,132,34,135,40,132,47,115,54,66,54,66,66,54,66,51,79,56,97,52,112,54,132,46,136,30,132);
                default:return Polygon(q,30,30,101,30,114,28,128,30,136,38,133,46,120,52,66,54,66,66,54,66,56,91,52,110,54,124,52,132,41,135,30,132);
            }
        }
        private NavObstacle Polygon(int q, params double[] coordinates)
        { var points=new List<NavPoint>(); for(int i=0;i<coordinates.Length;i+=2)points.Add(Point(q,coordinates[i],coordinates[i+1]));return new NavObstacle(new NavPolygon(points)); }
        private static NavClearanceProfile Clearance(double radius) => new NavClearanceProfile("nereida-query",1,"ground",1,radius);
        public bool TryLocate(NavPoint p,double radius,out NavLocation location) => Provider.TryLocate(p,Clearance(radius),out location);
        public bool IsValidLocation(NavLocation p,double radius) => Provider.IsValid(p,Clearance(radius));
        public bool TryTraverse(NavLocation from,NavPoint to,double radius,out NavLocation location)
        { location=default;var target=new NavLocation(to,from.SurfaceId);if(!Provider.CanSweep(from,target,Clearance(radius)))return false;location=target;return true; }
        public bool CompatibleCombatSurface(NavLocation a,NavLocation b) => Provider.CompatibleCombatSurface(a,b);
        public MapSupport SupportAt(NavPoint p)
        { var hits=Supports.Where(s=>s.Contains(p)).ToArray();return hits.Select(s=>s.SurfaceId).Distinct().Count()==1?hits.FirstOrDefault():null; }
        public double SurfaceHeight(NavPoint p) => SupportAt(p)?.HeightAt(p) ?? throw new ArgumentException("Explicit surface required or point is water.");
        public NavPoint SurfaceGradient(NavPoint p) => SupportAt(p)?.Gradient ?? throw new ArgumentException("Explicit surface required or point is water.");
        public bool SupportsFootprint(NavPoint p,double radius) => TryLocate(p,radius,out _);
        public bool SupportsSweep(NavPoint a,NavPoint b,double radius) => TryLocate(a,radius,out var from)&&TryTraverse(from,b,radius,out _);
        public TerritorySite[] Sites(PlayableProfile p)
        {
            var result=new List<TerritorySite>();
            for(int q=0;q<4;q++) {
                result.Add(new TerritorySite(q+1,PlayableBuildingKind.Headquarters,Point(q,100,102),p,q*Math.PI/2));
                result.Add(new TerritorySite(q+5,PlayableBuildingKind.Outpost,Point(q,48,48),p,q*Math.PI/2));
                result.Add(new TerritorySite(q+9,PlayableBuildingKind.Mine,Point(q,120,80),p));
                result.Add(new TerritorySite(q+13,PlayableBuildingKind.Mine,Point(q,110,42),p));
                result.Add(new TerritorySite(q+17,PlayableBuildingKind.Mine,Point(q,23,23),p));
            }
            return result.OrderBy(s=>s.Id).ToArray();
        }
        public NavPoint Headquarters(PlayableOwner owner) => throw new InvalidOperationException("Prototype: bind a seat explicitly; ordinary match integration pending.");
        // Returns start indices in participant order. Teams: participants 0/1 allied, 2/3 allied.
        public static int[] AssignStarts(NereidaMode mode,uint seed)
        {
            if(!Enum.IsDefined(typeof(NereidaMode),mode))throw new ArgumentOutOfRangeException(nameof(mode));
            uint state=seed; int Next(int count){state=unchecked(state*1664525u+1013904223u);return (int)((state>>16)%(uint)count);}
            int first=Next(4),direction=Next(2)==0?1:3;
            if(mode==NereidaMode.Duel)return new[]{first,(first+2)%4};
            if(mode==NereidaMode.Teams)return new[]{first,(first+direction)%4,(first+2)%4,(first+2+direction)%4};
            var seats=new[]{0,1,2,3};for(int i=3;i>0;i--){int j=Next(i+1);int temp=seats[i];seats[i]=seats[j];seats[j]=temp;}return seats;
        }
    }
}
