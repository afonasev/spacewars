using System;
using System.Globalization;
using System.Linq;
using Spacewars.Simulation;
namespace Spacewars.Runtime
{
    // Exact existing source-executed flat authored binding. No general terrain or
    // surface/portal loader and no interpretation as ordinary Three Crossings.
    public static class SourceFlatFixture
    {
        public static OfflineMatchConfiguration Load(string fixture,PlayableProfile profile,bool allied=false)
        {
            var rows=fixture.Split(new[]{'\n'},StringSplitOptions.RemoveEmptyEntries).Skip(1).Select(s=>s.TrimEnd('\r').Split('|')).ToArray();
            double N(string v)=>double.Parse(v,CultureInfo.InvariantCulture);
            var starts=rows.Where(x=>x[0]=="S").Select(x=>new OfflineStart(x[1],int.Parse(x[2]),new NavPoint(N(x[3]),N(x[4])),new NavPoint(N(x[5]),N(x[6])),pin:int.Parse(x[7])==0?(int?)null:int.Parse(x[7]))).ToArray();
            var sites=starts.Select(s=>new TerritorySite(s.SiteId,PlayableBuildingKind.Headquarters,s.Position,rows.Where(x=>x[0]=="L"&&int.Parse(x[1])==s.SiteId).Select(x=>new TerritorySlot(int.Parse(x[2]),new NavPoint(N(x[3]),N(x[4])),N(x[5]))).ToArray())).Concat(new[]{new TerritorySite(9,PlayableBuildingKind.Outpost,new NavPoint(0,0),rows.Where(x=>x[0]=="L"&&x[1]=="9").Select(x=>new TerritorySlot(int.Parse(x[2]),new NavPoint(N(x[3]),N(x[4])),N(x[5]))).ToArray())}).ToArray();
            var costs=new double[starts.Length,starts.Length];foreach(var row in rows.Where(x=>x[0]=="R"))costs[int.Parse(row[1]),int.Parse(row[2])]=N(row[3]);
            // Source refuses an all-allied match: test allied local humans with a
            // third hostile owner. No autonomous AI policy is enabled by this binding.
            var roster=allied?new[]{new OfflineParticipant("owner-11",1,3,OfflineControl.Human),new OfflineParticipant("owner-28",2,3,OfflineControl.Human),new OfflineParticipant("opponent",3,7,OfflineControl.Ai)}:
                new[]{new OfflineParticipant("owner-11",1,1,OfflineControl.Human),new OfflineParticipant("owner-28",2,2,OfflineControl.Human)};
            return new OfflineMatchConfiguration(profile,"source-flat-authority-v1","native-flat-eight-starts-v1","src/sim/map.route:source-fixture.tsv",19092026,roster,starts,sites,Array.Empty<NavObstacle>(),costs);
        }
    }
}
