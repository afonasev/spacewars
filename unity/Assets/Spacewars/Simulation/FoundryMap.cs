using System;
using System.Collections.Generic;
using System.Linq;
namespace Spacewars.Simulation
{
    [Serializable] public sealed class FoundryProfileData
    {
        public string id="black-foundry-greybox-v2"; public int revision=3;
        public double scale=1, flankHeight=6, rearHeight=3, directFireHeight=1;
    }
    public sealed class FoundryField
    {
        private readonly System.Reflection.FieldInfo field;
        public string Path{get;} public string Group=>"Black Foundry geometry"; public string Label{get;} public string Description{get;} public string Unit{get;}
        public double Minimum{get;} public double Maximum{get;} public double Step{get;}
        public FoundryField(string key,string label,string description,string unit,double min,double max,double step){field=typeof(FoundryProfileData).GetField(key);Path="maps.foundry."+key;Label=label;Description=description;Unit=unit;Minimum=min;Maximum=max;Step=step;}
        public double Read(FoundryProfileData p)=>(double)field.GetValue(p);
        public void Write(FoundryProfileData p,double value){if(double.IsNaN(value)||double.IsInfinity(value)||value<Minimum||value>Maximum)throw new ArgumentOutOfRangeException(Path);field.SetValue(p,value);}
    }
    public sealed class FoundryMap : IPlayableTerrain
    {
        public static readonly IReadOnlyList<FoundryField> Fields=Array.AsReadOnly(new[]{
            new FoundryField("scale","Layout scale","Scales authored clearances, roads and site distances; building/unit sizes remain unchanged.","ratio",1,1.5,.05),
            new FoundryField("flankHeight","Upper support height","Raised side mine lines; ramps stay continuous.","m",3,9,.25),
            new FoundryField("rearHeight","Player deck height","Homes, rear and outpost decks; must remain below flanks.","m",1,5,.25),
            new FoundryField("directFireHeight","Direct fire height","Launch and target plane above the local support.","m",.5,2,.05)});
        public string Id{get;} public int Revision{get;} public double HalfExtent=>140*Scale;
        public double Scale{get;} public double RearHeight{get;} public double UpperHeight{get;} public double DirectFireHeight{get;}
        public IReadOnlyList<MapSupport> Supports{get;} public IReadOnlyList<NavObstacle> Solids{get;} public IReadOnlyList<NavObstacle> MovementBlockers=>Solids;
        public IReadOnlyList<NavObstacle> Lava{get;} public IReadOnlyList<NavObstacle> Roads{get;}
        private readonly NavGeometry geometry;
        private readonly TerrainLocations locations;
        public int SurfaceSemanticsVersion=>1;
        public IReadOnlyList<NavSurfaceTransition> SurfaceTransitions=>locations.Transitions;
        public bool TryLocate(NavPoint point,double radius,out NavLocation location)=>locations.TryLocate(point,radius,out location);
        public bool IsValidLocation(NavLocation location,double radius)=>locations.Valid(location,radius);
        public bool TryTraverse(NavLocation from,NavPoint to,double radius,out NavLocation location)=>locations.Traverse(from,to,radius,out location);
        public bool CompatibleCombatSurface(NavLocation anchor,NavLocation other)=>locations.Combat(anchor,other);

        // v2 authored coordinates are map content, not changes to global gameplay tuning.
        public FoundryMap(FoundryProfileData p)
        {
            if(p==null||p.id!="black-foundry-greybox-v2"||p.revision<1)throw new ArgumentException("Invalid foundry profile.");
            foreach(var f in Fields)f.Write(p,f.Read(p));
            if(p.rearHeight>=p.flankHeight)throw new ArgumentException("Center < player deck < flank required.");
            RearHeight=p.rearHeight;Id=p.id;Revision=p.revision;Scale=p.scale;UpperHeight=p.flankHeight;DirectFireHeight=p.directFireHeight;
            var supports=new List<MapSupport>();
            // Ramp precedence is deliberate: shared seam endpoints agree exactly with decks.
            foreach(int side in new[]{-1,1})foreach(int end in new[]{-1,1})
            {
                supports.Add(new MapSupport("flank-ramp",Box(side<0?-60:28,end<0?-44:26,side<0?-28:60,end<0?-26:44),false,0,new NavPoint(side*UpperHeight/(32*Scale),0),Point(side*28,0),surfaceId:"flank-ramp/"+side+"/"+end));
            }
            foreach(int end in new[]{-1,1})supports.Add(new MapSupport("front-ramp",Box(-28,end<0?-60:44,28,end<0?-44:60),false,0,new NavPoint(0,end*RearHeight/(16*Scale)),Point(0,end*44),surfaceId:"front-ramp/"+end));
            foreach(int side in new[]{-1,1})foreach(int end in new[]{-1,1})
            {
                var low=Point(side*28,end*44);var outer=Point(side*60,end*44);var inner=Point(side*28,end*60);var high=Point(side*60,end*60);
                supports.Add(new MapSupport("junction-ramp",new NavObstacle(new NavPolygon(new[]{low,outer,inner})),false,0,new NavPoint(side*UpperHeight/(32*Scale),end*RearHeight/(16*Scale)),low,surfaceId:"junction-ramp/"+side+"/"+end));
                supports.Add(new MapSupport("junction-rear",new NavObstacle(new NavPolygon(new[]{outer,high,inner})),false,RearHeight,new NavPoint(0,end*(RearHeight-UpperHeight)/(16*Scale)),high,surfaceId:"junction-rear/"+side+"/"+end));
                // Triangulated flank/rear corner: continuous at all three neighboring planes.
                var far=Point(side*70,end*44);var farRear=Point(side*70,end*60);
                supports.Add(new MapSupport("flank-corner-low",new NavObstacle(new NavPolygon(new[]{outer,far,high})),false,RearHeight,new NavPoint(0,end*(RearHeight-UpperHeight)/(16*Scale)),high,surfaceId:"flank-corner-low/"+side+"/"+end));
                supports.Add(new MapSupport("flank-corner-high",new NavObstacle(new NavPolygon(new[]{far,farRear,high})),false,RearHeight,new NavPoint(side*(UpperHeight-RearHeight)/(10*Scale),0),high,surfaceId:"flank-corner-high/"+side+"/"+end));
                supports.Add(new MapSupport("rear-flank-ramp",Box(side<0?-70:60,end<0?-98:60,side<0?-60:70,end<0?-60:98),false,RearHeight,new NavPoint(side*(UpperHeight-RearHeight)/(10*Scale),0),Point(side*60,0),surfaceId:"rear-flank-ramp/"+side+"/"+end));
            }
            supports.Add(new MapSupport("lower-common-field",Box(-28,-44,28,44),false,surfaceId:"lower-field"));
            foreach(int side in new[]{-1,1})
            {
                supports.Add(new MapSupport("upper-flank",Box(side<0?-90:70,-130,side<0?-70:90,130),false,UpperHeight,surfaceId:"upper-flank/"+side));
                supports.Add(new MapSupport("flank-crossbar",Box(side<0?-70:60,-44,side<0?-60:70,44),false,UpperHeight,surfaceId:"flank-crossbar/"+side));
            }
            foreach(int end in new[]{-1,1})supports.Add(new MapSupport("allied-rear",Box(-60,end<0?-130:60,60,end<0?-60:130),false,RearHeight,surfaceId:"allied-rear/"+end));
            foreach(int end in new[]{-1,1})supports.Add(new MapSupport("crossbar",Box(-60,end<0?-44:26,60,end<0?-26:44),false,surfaceId:"crossbar/"+end));
            Supports=supports.AsReadOnly();
            var solid=new List<NavObstacle>(); var lava=new List<NavObstacle>();
            foreach(int side in new[]{-1,1})
            {
                solid.Add(Rock(side<0?-60:28,-26,side<0?-28:60,26,UpperHeight+7));
                lava.Add(new NavObstacle(new NavPolygon(new[]{Point(side*42,-19),Point(side*47,-8),Point(side*44,3),Point(side*48,18),Point(side*41,11),Point(side*40,-4)})));
            }
            // Boundary and upper cliff edges are visible basalt volumes; no hidden collision fences.
            solid.Add(Rock(-140,-140,-90,140,UpperHeight+10));solid.Add(Rock(90,-140,140,140,UpperHeight+10));
            solid.Add(Rock(-90,130,90,140,UpperHeight+10));solid.Add(Rock(-90,-140,90,-130,UpperHeight+10));
            // Two complementary pockets, mirrored front/rear across Z. Full envelopes stay clear.
            foreach(int end in new[]{-1,1})
            {
                solid.Add(Rock(-26,end<0?-89:58,-19,end<0?-58:89,UpperHeight+9));
                solid.Add(Rock(-51,end<0?-89:84,-19,end<0?-84:89,UpperHeight+8));
                solid.Add(Rock(19,end<0?-84:52,26,end<0?-52:84,UpperHeight+9));
                solid.Add(Rock(19,end<0?-56:52,51,end<0?-52:56,UpperHeight+8));
            }
            // Continuous side river lies wholly within the visible blocked eastern bank.
            lava.Add(new NavObstacle(new NavPolygon(new[]{Point(113,-140),Point(111,-108),Point(117,-71),Point(110,-32),Point(115,3),Point(109,42),Point(116,81),Point(111,113),Point(114,140),Point(133,140),Point(130,110),Point(135,80),Point(128,41),Point(134,2),Point(129,-32),Point(136,-72),Point(130,-110),Point(132,-140)})));
            // Flanks emerge in front of the homes; corner basalt closes the direct rear shortcut.
            foreach(int side in new[]{-1,1})foreach(int end in new[]{-1,1})
                solid.Add(Rock(side<0?-90:60,end<0?-130:98,side<0?-60:90,end<0?-98:130,UpperHeight+6));
            // Major factory/store volumes in opposite corners, both working and abandoned on both sides.
            solid.Add(Rock(-88,104,-70,126,UpperHeight+13));solid.Add(Rock(70,105,88,126,UpperHeight+9));
            solid.Add(Rock(-88,-126,-70,-105,UpperHeight+9));solid.Add(Rock(70,-126,88,-104,UpperHeight+13));
            Solids=solid.AsReadOnly();Lava=lava.AsReadOnly();
            var roads=new List<NavObstacle>{Box(-53,99,53,113),Box(-53,-113,53,-99),Box(-10,-96,10,96),Box(-90,26,90,44),Box(-90,-44,90,-26),Box(-84,-98,-66,98),Box(66,-98,84,98)};
            foreach(int side in new[]{-1,1})foreach(int end in new[]{-1,1})roads.Add(Box(side<0?-53:35,end<0?-103:44,side<0?-35:53,end<0?-44:103));
            Roads=roads.AsReadOnly();geometry=new NavGeometry(HalfExtent,Solids.ToArray(),Revision);
            var transitions=new List<NavSurfaceTransition>();
            void Link(string a,string b)=>transitions.Add(new NavSurfaceTransition(a+"~"+b,a,b));
            foreach(int side in new[]{-1,1})foreach(int end in new[]{-1,1}){
                string suffix="/"+side+"/"+end,flank="flank-ramp"+suffix,junction="junction-ramp"+suffix,rear="junction-rear"+suffix,
                    low="flank-corner-low"+suffix,high="flank-corner-high"+suffix,ramp="rear-flank-ramp"+suffix,cross="flank-crossbar/"+side,upper="upper-flank/"+side,home="allied-rear/"+end,front="front-ramp/"+end;
                Link("lower-field",flank);Link(flank,junction);Link(front,junction);Link(junction,rear);Link(rear,home);Link(rear,low);
                Link(flank,cross);Link(cross,low);Link(low,high);Link(high,upper);Link(high,ramp);Link(ramp,upper);Link(ramp,home);
            }
            foreach(int end in new[]{-1,1}){Link("lower-field","front-ramp/"+end);Link("front-ramp/"+end,"allied-rear/"+end);}
            foreach(int side in new[]{-1,1})Link("flank-crossbar/"+side,"upper-flank/"+side);
            // Existing declaration precedence authors the ownership partition. The
            // overlapping legacy crossbars are background, not another terrain.
            locations=new TerrainLocations(geometry,Supports,transitions);

        }
        public NavPoint Point(double x,double z)=>new NavPoint(x*Scale,z*Scale);
        private NavObstacle Box(double x,double z,double xx,double zz)=>new NavObstacle(x*Scale,z*Scale,xx*Scale,zz*Scale);
        private NavObstacle Rock(double x,double z,double xx,double zz,double top)=>new NavObstacle(new NavPolygon(new[]{Point(x,z),Point(xx,z),Point(xx,zz),Point(x,zz)}),0,top);
        public MapSupport SupportAt(NavPoint p)=>Supports.FirstOrDefault(s=>s.Contains(p));
        public double SurfaceHeight(NavPoint p)=>SupportAt(p)?.HeightAt(p)??0;
        public NavPoint SurfaceGradient(NavPoint p)=>SupportAt(p)?.Gradient??default(NavPoint);
        public bool SupportsFootprint(NavPoint p,double radius)=>geometry.IsFree(p,radius)&&SupportAt(p)!=null;
        public bool SupportsSweep(NavPoint a,NavPoint b,double radius)=>geometry.SegmentFree(a,b,radius);
        public NavPoint Headquarters(PlayableOwner owner){int i=(int)owner;return Point(new[]{-44d,0,44,-44,0,44}[i],i<3?(i==1?92:108):(i==4?-92:-108));}
        public TerritorySite[] Sites(PlayableProfile p)
        {
            var result=new List<TerritorySite>();
            for(int i=0;i<6;i++)result.Add(new TerritorySite(i+1,PlayableBuildingKind.Headquarters,Headquarters((PlayableOwner)i),p,i<3?0:Math.PI));
            double[,] forts={{-38,70},{38,70},{-38,-70},{38,-70}};
            for(int i=0;i<4;i++)result.Add(new TerritorySite(7+i,PlayableBuildingKind.Outpost,Point(forts[i,0],forts[i,1]),p,i<2?0:Math.PI));
            double[,] mines={{-57,86},{0,71},{57,86},{-57,-86},{0,-71},{57,-86},{-77,60},{77,60},{-77,-60},{77,-60},{-77,0},{77,0},{-17,18},{17,-18}};
            for(int i=0;i<14;i++)result.Add(new TerritorySite(11+i,PlayableBuildingKind.Mine,Point(mines[i,0],mines[i,1]),p));
            return result.ToArray();
        }
        public OfflineMatchConfiguration Configuration(PlayableProfile p,int seed,bool ai=false,string humanId="foundry-1")
        {
            var sites=Sites(p);var starts=new OfflineStart[6];var roster=new OfflineParticipant[6];
            for(int i=0;i<6;i++){var home=sites[i].Position;starts[i]=new OfflineStart((i<3?"A":"B")+(i%3+1),i+1,home,new NavPoint(home.X,home.Z+(i<3?-12:12)*Scale),i<3?-Math.PI/2:Math.PI/2,i+1);roster[i]=new OfflineParticipant(i==0?humanId:"foundry-"+(i+1),i+1,i<3?1:2,ai?OfflineControl.Ai:OfflineControl.Human);}
            // The manual greybox uses one local controller switched across seats, not six simultaneous input devices.
            if(!ai)for(int i=1;i<6;i++)roster[i]=new OfflineParticipant(roster[i].Id,i+1,i<3?1:2,OfflineControl.Ai);
            var costs=new double[6,6];var router=new SharedFlowRouter(geometry,p.Navigation);
            for(int a=0;a<6;a++)for(int b=a+1;b<6;b++)
            {
                // Authored same-team routes are Z mirrors. Query the same half so grid tie
                // breaking cannot assign different costs to geometrically identical rears.
                bool mirror=a>=3;var start=starts[mirror?a-3:a].ExplorerAnchor;var goal=starts[mirror?b-3:b].ExplorerAnchor;
                var path=router.FindPath(start,goal);if(path.Length==0)throw new ArgumentException("Disconnected foundry starts.");
                double distance=0;var last=start;
                foreach(var q in path)
                {
                    var actualLast=mirror?new NavPoint(last.X,-last.Z):last;var actual=mirror?new NavPoint(q.X,-q.Z):q;
                    if(!geometry.SegmentFree(actualLast,actual,p.Navigation.Radius))throw new ArgumentException("Blocked mirrored foundry route.");
                    distance+=Math.Sqrt((q.X-last.X)*(q.X-last.X)+(q.Z-last.Z)*(q.Z-last.Z));last=q;
                }
                costs[a,b]=costs[b,a]=distance;
            }
            return new OfflineMatchConfiguration(p,"native-foundry-v2",Id,"native-typed-flow-v1",seed,roster,starts,sites,Solids.ToArray(),costs,terrain:Id);
        }
    }
}
