using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Tests.EditMode
{
    internal static class NativeAiPostOpeningFixtures
    {
        private sealed class Placement
        {
            public int SiteId,SlotId;
            public PlayableBuildingKind Kind;
            public NavPoint Position;
            public double Heading,Radius;
        }

        internal static OfflineMatchConfiguration WithReadyHome(OfflineMatchConfiguration source,string fixtureKey)
        {
            if(source==null||string.IsNullOrWhiteSpace(fixtureKey))throw new ArgumentException("A source fixture and distinct identity key are required.");
            int participantIndex=Enumerable.Range(0,source.Roster.Count).Single(i=>source.Roster[i].LogicalPlayer==1&&source.Roster[i].Control==OfflineControl.Ai);
            var participant=source.Roster[participantIndex];var homeStart=source.Starts[source.Assignments[participantIndex]];
            var home=source.Sites.Single(s=>s.Id==homeStart.SiteId&&s.Kind==PlayableBuildingKind.Headquarters);
            var opening=PlayableAiOpeningComposition.Initialize(source.Seed,participant.Id,aiProfile:AiProfile.Initial);
            int refineryCount=new AiOpeningExecutor(opening,71).RequiredHomeRefineryCount(AiProfile.Initial);
            int refineryQuote=TerritoryRules.Cost(source.Profile,PlayableBuildingKind.Refinery);
            int factoryQuote=TerritoryRules.Cost(source.Profile,PlayableBuildingKind.Factory);

            var profileSlots=new TerritorySite(home.Id,PlayableBuildingKind.Headquarters,home.Position,source.Profile);
            if(home.Slots.Count>0)
            {
                if(home.Slots.Count!=profileSlots.Slots.Count||!home.Slots.Zip(profileSlots.Slots,(a,b)=>a.Id==b.Id&&a.Position.Equals(b.Position)&&a.Heading==b.Heading).All(equal=>equal))
                    throw new ArgumentException("The existing home slots do not match the current profile.");
                return BuildWithHomeSlots(source,fixtureKey,participant,home,home,refineryCount,refineryQuote,factoryQuote,"profile-slots");
            }
            if(source.Profile.AuthoredMap!=null)throw new ArgumentException("Authored-map home pads must retain the map-authored site layout.");

            // Flat fixtures may author their own slot phase. Their original start anchors are
            // fixed and the configuration validator checks every pad, including empty pads.
            // Search the complete unique rotation interval in stable 0.1-degree steps and let
            // OfflineMatchConfiguration apply its full, unchanged source validation contract.
            int halfPeriodTenths=(int)Math.Round(1800d/profileSlots.Slots.Count,MidpointRounding.AwayFromZero);
            ArgumentException lastLayoutError=null;
            for(int step=0;step<=halfPeriodTenths;step++)
            {
                int[] signedSteps=step==0?new[]{0}:step==halfPeriodTenths?new[]{-step}:new[]{-step,step};
                foreach(int phaseTenths in signedSteps)
                {
                    double phase=phaseTenths/10d*Math.PI/180d;
                    var rotatedHome=new TerritorySite(home.Id,PlayableBuildingKind.Headquarters,home.Position,source.Profile,phase);
                    try{return BuildWithHomeSlots(source,fixtureKey,participant,home,rotatedHome,refineryCount,refineryQuote,factoryQuote,
                        "padphase-"+(phaseTenths<0?"m":"p")+Math.Abs(phaseTenths).ToString("D4",CultureInfo.InvariantCulture));}
                    catch(ArgumentException error)
                    {
                        if(!IsRetryableLayoutError(error))throw;
                        lastLayoutError=error;
                    }
                }
            }
            throw new ArgumentException("No rotated flat-fixture home slot layout satisfies all authored envelopes, anchors, actors, and required occupied pads.",lastLayoutError);
        }

        private static bool IsRetryableLayoutError(ArgumentException error)
            =>error.Message.Contains("Invalid start/anchor/pin")||error.Message.Contains("Unsupported site envelope")||
              error.Message.Contains("Overlapping authored envelopes")||error.Message.Contains("Unsupported scenario unit")||
              error.Message.Contains("too few safe empty pads");

        private static OfflineMatchConfiguration BuildWithHomeSlots(OfflineMatchConfiguration source,string fixtureKey,OfflineParticipant participant,
            TerritorySite originalHome,TerritorySite placedHome,int refineryCount,int refineryQuote,int factoryQuote,string layoutIdentity)
        {
            var placements=SelectSafeHomePlacements(source,placedHome,refineryCount);
            var sites=source.Sites.Select(s=>s.Id==originalHome.Id?placedHome:s).ToArray();
            var homeBuildings=placements.Select(p=>new OfflineScenarioBuilding(participant.LogicalPlayer,p.Kind,p.SiteId,p.SlotId,p.Position,p.Heading));
            var scenarios=source.ScenarioBuildings.Concat(homeBuildings).ToArray();
            var costs=new double[source.Starts.Count,source.Starts.Count];
            for(int i=0;i<source.Starts.Count;i++)for(int j=0;j<source.Starts.Count;j++)costs[i,j]=source.RouteCost(i,j);
            string identity=fixtureKey+"-post-opening-home-ready-r"+refineryCount+"-rq"+refineryQuote+"-fq"+factoryQuote+"-"+layoutIdentity;
            string terrain=source.Profile.AuthoredMap==null?"flat-ground-v1":source.Profile.AuthoredMap.Id;
            var result=new OfflineMatchConfiguration(source.Profile,source.SourceIdentity+"-"+identity,source.MapIdentity+"-"+identity,
                source.RouteProvenance,source.Seed,source.Roster.ToArray(),source.Starts.ToArray(),sites,source.Obstacles.ToArray(),costs,
                source.Spectators.ToArray(),source.ScenarioUnits.ToArray(),terrain,scenarios);

            if(!ReferenceEquals(source.Profile,result.Profile)||source.Seed!=result.Seed||!source.Starts.SequenceEqual(result.Starts)||
                !source.Roster.SequenceEqual(result.Roster)||!source.Obstacles.SequenceEqual(result.Obstacles)||
                !source.ScenarioUnits.SequenceEqual(result.ScenarioUnits)||source.ScenarioBuildings.Any(b=>!result.ScenarioBuildings.Contains(b)))
                throw new InvalidOperationException("Post-opening facts changed a pre-existing fixture contract.");
            return result;
        }

        private static Placement[] SelectSafeHomePlacements(OfflineMatchConfiguration source,TerritorySite home,int refineryCount)
        {
            int required=refineryCount+1;
            var occupied=source.ScenarioBuildings.Where(b=>b.SiteId==home.Id&&b.SlotId>0).Select(b=>b.SlotId).ToArray();
            var selected=new List<Placement>();
            var geometry=new NavGeometry(source.Profile.ArenaHalfExtent,source.Obstacles.ToArray(),1);
            for(int slotIndex=0;slotIndex<home.Slots.Count&&selected.Count<required;slotIndex++)
            {
                int slotId=home.Slots[slotIndex].Id;
                if(occupied.Contains(slotId))continue;
                var slot=home.Slots[slotIndex];
                var kind=selected.Count<refineryCount?PlayableBuildingKind.Refinery:PlayableBuildingKind.Factory;
                var candidate=new Placement{SiteId=home.Id,SlotId=slotId,Kind=kind,Position=slot.Position,Heading=slot.Heading,Radius=TerritoryRules.Radius(source.Profile,kind)};
                if(IsSafe(candidate,source,home,geometry,selected))selected.Add(candidate);
            }
            if(selected.Count!=required)throw new ArgumentException("The current home profile has too few safe empty pads for its opening.");
            return selected.ToArray();
        }

        private static bool IsSafe(Placement candidate,OfflineMatchConfiguration source,TerritorySite home,NavGeometry geometry,IReadOnlyList<Placement> selected)
        {
            var p=source.Profile;
            // Buildings use axis-aligned square footprints with half-width TerritoryRules.Radius.
            // sqrt(2) is a conservative circle for terrain and arena bounds; pairwise checks below
            // use the source AABB predicates directly.
            if(!geometry.IsFree(candidate.Position,candidate.Radius*Math.Sqrt(2)))return false;
            foreach(var start in source.Starts)
            {
                if(BoxOverlap(candidate.Position,candidate.Radius,start.ExplorerAnchor,p.ExplorerCollisionRadius))return false;
                var site=source.Sites.Single(s=>s.Id==start.SiteId);
                if(BoxOverlap(candidate.Position,candidate.Radius,site.Position,TerritoryRules.Radius(p,site.Kind)))return false;
            }
            foreach(var actor in source.ScenarioUnits)
                if(BoxOverlap(candidate.Position,candidate.Radius,actor.Position,PlayableUnitRules.Radius(p,actor.Kind)))return false;
            foreach(var site in source.Sites)
            {
                if(site.Id!=home.Id&&BoxOverlap(candidate.Position,candidate.Radius,site.Position,TerritoryRules.Radius(p,site.Kind)))return false;
                var slots=site.Id==home.Id?home.Slots:site.Slots;
                foreach(var slot in slots)
                {
                    if(site.Id==home.Id&&slot.Id==candidate.SlotId)continue;
                    double other=Math.Max(p.ScienceFootprintRadius,Math.Max(p.FactoryFootprintRadius,p.RefineryFootprintRadius));
                    if(BoxOverlap(candidate.Position,candidate.Radius,slot.Position,other))return false;
                }
            }
            foreach(var building in source.ScenarioBuildings)
            {
                double other=TerritoryRules.Radius(p,building.Kind);
                if(BoxOverlap(candidate.Position,candidate.Radius,building.Position,other))return false;
            }
            foreach(var other in selected)
                if(BoxOverlap(candidate.Position,candidate.Radius,other.Position,other.Radius))return false;
            return true;
        }

        private static bool BoxOverlap(NavPoint a,double aRadius,NavPoint b,double bRadius)
            =>Math.Abs(a.X-b.X)<=aRadius+bRadius&&Math.Abs(a.Z-b.Z)<=aRadius+bRadius;
    }
}
