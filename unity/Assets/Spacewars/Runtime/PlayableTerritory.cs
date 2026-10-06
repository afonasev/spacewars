using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    internal sealed partial class PlayableDomain
    {
        private sealed class SiteState
        {
            public TerritorySite Site;
            public PlayableOwner? Claimant,Locked;
            public double Progress;
            public bool Contested;
            public int CenterId;
        }
        private readonly Dictionary<int,SiteState> sites=new Dictionary<int,SiteState>();
        private void InitializeSites()
        {
            foreach(var site in (offline==null?TerritoryRules.Sites(profile):offline.Sites))sites.Add(site.Id,new SiteState{Site=site});
        }
        private PlayableCommandStatus BuildAt(int siteId,int slotId,PlayableBuildingKind kind,int parentId,PlayableOwner owner,out string message)
        {
            message="Invalid site or ownership.";
            if(!sites.TryGetValue(siteId,out var site)||!Enum.IsDefined(typeof(PlayableBuildingKind),kind))return PlayableCommandStatus.InvalidTarget;
            NavPoint position=site.Site.Position;double heading=0;
            if(slotId==0)
            {
                if(kind!=site.Site.Kind||site.CenterId!=0||site.Progress<1||site.Claimant!=owner||(site.Locked.HasValue&&site.Locked!=owner)||parentId!=0)return PlayableCommandStatus.InvalidTarget;
            }
            else
            {
                if(kind!=PlayableBuildingKind.Factory&&kind!=PlayableBuildingKind.Refinery&&kind!=PlayableBuildingKind.ScientificCenter)return PlayableCommandStatus.InvalidTarget;
                if(!buildings.TryGetValue(site.CenterId,out var parent)||parent.Id!=parentId||!parent.Ready||parent.Sale!=null||parent.Owner!=owner||slotId<1||slotId>site.Site.Slots.Count)return PlayableCommandStatus.InvalidTarget;
                foreach(var b in buildings.Values)if(b.SiteId==siteId&&b.SlotId==slotId){message="Slot occupied.";return PlayableCommandStatus.OccupiedPad;}
                var slot=site.Site.Slots[slotId-1];position=slot.Position;heading=slot.Heading;
            }
            int cost=TerritoryRules.Cost(profile,kind);
            if(Balance(owner)<cost){message="Insufficient credits.";return PlayableCommandStatus.InsufficientCredits;}
            AddCredits(owner,-cost);
            var building=new Building{Id=nextId++,Owner=owner,Kind=kind,Position=position,SiteId=siteId,SlotId=slotId,ParentId=parentId,PaidCost=cost,Phase=ConstructionPhase.Pending,Heading=heading};
            buildings.Add(building.Id,building);
            if(slotId==0){site.CenterId=building.Id;site.Locked=owner;}
            message="Construction ordered.";return PlayableCommandStatus.Applied;
        }
        private double Balance(PlayableOwner owner)=>offline==null?(owner==PlayableOwner.Player?credits:enemyCredits):ownerCredits[owner];
        private void AddCredits(PlayableOwner owner,double amount){if(offline!=null)ownerCredits[owner]+=amount;else if(owner==PlayableOwner.Player)credits+=amount;else enemyCredits+=amount;}
        private PlayableCommandStatus CancelBuilding(int id,PlayableOwner owner,out string message)
        {
            message="Select own unfinished building.";
            if(!buildings.TryGetValue(id,out var b)||b.Owner!=owner||b.Ready||b.Sale!=null)return PlayableCommandStatus.InvalidEntity;
            AddCredits(owner,b.PaidCost*profile.BuildingCancellationRefundRatio);
            bool solid=b.Phase!=ConstructionPhase.Pending;RemoveBuilding(b,true);if(solid)RebuildGeometry();
            message="Construction cancelled.";return PlayableCommandStatus.Applied;
        }
        private void RemoveBuilding(Building b,bool cancellation)
        {
            if(!buildings.Remove(b.Id))return;
            if(b.Kind==PlayableBuildingKind.ScientificCenter)InterruptResearchCenter(b.Id);
            var children=new List<Building>();foreach(var child in buildings.Values)if(child.ParentId==b.Id)children.Add(child);
            foreach(var child in children)RemoveBuilding(child,false);
            if(b.SlotId==0&&sites.TryGetValue(b.SiteId,out var site)&&site.CenterId==b.Id)
            {
                site.CenterId=0;site.Contested=false;
                if(cancellation){site.Claimant=b.Owner;site.Progress=1;site.Locked=b.Owner;}
                else{site.Locked=null;site.Claimant=null;site.Progress=0;}
            }
        }
        private void AdvanceCapture(double dt)
        {
            foreach(var state in sites.Values)
            {
                if(state.Locked.HasValue||state.CenterId!=0){state.Contested=false;continue;}
                var present=new HashSet<PlayableOwner>();double radius=TerritoryRules.CaptureRadius(profile,state.Site.Kind);
                foreach(var u in units.Values)if(!eliminated.Contains(u.Owner)&&navigation.Crowd.TryGet(u.Id,out var n)&&Distance(n.Position,state.Site.Position)<=radius)present.Add(u.Owner);
                state.Contested=present.Select(TeamOf).Distinct().Count()>1;if(state.Contested)continue;
                double delta=dt/TerritoryRules.CaptureSeconds(profile,state.Site.Kind);
                var captureOwner=Owners.Where(present.Contains).Select(x=>(PlayableOwner?)x).FirstOrDefault();
                if(state.Claimant.HasValue&&present.Contains(state.Claimant.Value)){
                    state.Progress=Math.Min(1,state.Progress+delta);continue;
                }
                if(state.Progress>0){state.Progress=Math.Max(0,state.Progress-delta);if(state.Progress<=1e-9){state.Progress=0;state.Claimant=null;}continue;}
                if(captureOwner.HasValue){state.Claimant=captureOwner;state.Progress=Math.Min(1,delta);}
            }
        }
        private bool InFootprint(Building b,NavPoint point,double unitRadius)
        {
            double radius=BuildingRadius(b.Kind)+unitRadius;
            return Math.Abs(point.X-b.Position.X)<=radius&&Math.Abs(point.Z-b.Position.Z)<=radius;
        }
        private void AdvanceFoundations()
        {
            bool changed=false;
            foreach(var b in buildings.Values)
            {
                if(b.Phase!=ConstructionPhase.Pending||b.Sale!=null)continue;
                bool occupied=false,enemy=false;
                foreach(var u in units.Values)
                {
                    if(!navigation.Crowd.TryGet(u.Id,out var n)||!InFootprint(b,n.Position,n.Radius))continue;
                    occupied=true;
                    if(u.Owner!=b.Owner){enemy=true;continue;}
                    if(b.Evacuated.Contains(u.Id)&&(n.Moving||navigation.IsPending(u.Id)))continue;
                    if(elapsed<b.RetryAt)continue;
                    if(TryEvacuate(b,u,n.Position))b.Evacuated.Add(u.Id);
                }
                if(occupied){b.BlockedReason=enemy?"Враг на площадке":"Освобождаем площадку";if(elapsed>=b.RetryAt)b.RetryAt=elapsed+profile.EvacuationRetrySeconds;continue;}
                b.Phase=ConstructionPhase.Constructing;b.BlockedReason=null;b.Health=0;changed=true;
            }
            if(changed)RebuildGeometry();
        }
        private bool TryEvacuate(Building b,Unit u,NavPoint from)
        {
            double unitRadius=PlayableUnitRules.Radius(profile,u.Kind);double radius=BuildingRadius(b.Kind)+unitRadius+profile.EvacuationClearance;
            int candidates=Math.Max(1,(int)Math.Ceiling(Math.PI*2*radius/profile.Navigation.ArrivalSlotSpacing));
            double start=Math.Atan2(from.Z-b.Position.Z,from.X-b.Position.X);
            for(int i=0;i<candidates;i++)
            {
                double angle=start+i*2*Math.PI/candidates;
                var target=new NavPoint(b.Position.X+Math.Cos(angle)*radius,b.Position.Z+Math.Sin(angle)*radius);
                if(InFootprint(b,target,unitRadius)||!Geometry.IsFree(target,unitRadius)||!navigation.Crowd.CanPlace(target,unitRadius))continue;
                var allocated=navigation.AllocateArrivalSlots(target,new[]{u.Id});
                if(allocated.Length!=1||InFootprint(b,allocated[0],unitRadius))continue;
                if(navigation.Move(u.Id,allocated[0])){u.Target=0;u.HasAttackMove=false;u.StoppedSeconds=0;return true;}
            }
            return false;
        }
        private NavPoint FactoryExit(Building b)=>new NavPoint(b.Position.X+Math.Cos(b.Heading)*profile.FactoryExitDistance,b.Position.Z+Math.Sin(b.Heading)*profile.FactoryExitDistance);
        private TerritorySiteSnapshot[] SiteSnapshots()
        {
            var result=new List<TerritorySiteSnapshot>();
            foreach(var s in sites.Values)result.Add(new TerritorySiteSnapshot(s.Site,s.Claimant,s.Locked,s.Progress,s.Contested,s.CenterId,buildings.TryGetValue(s.CenterId,out var center)&&center.Ready));
            return result.ToArray();
        }
    }
}
