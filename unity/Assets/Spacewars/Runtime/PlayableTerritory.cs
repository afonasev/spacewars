using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    internal sealed partial class PlayableDomain
    {
        private sealed class SiteState
        {
            public TerritorySite Site;
            public PlayableOwner? Claimant,Locked;
            public int RelayClaimTeam,RelayOwnerTeam;
            public double Progress,ControlledSeconds,MineReserve,MineReserveCapacity;
            public bool Contested;
            public int CenterId;
            public readonly Dictionary<int,NavPoint> Rallies=new Dictionary<int,NavPoint>();
        }
        private readonly Dictionary<int,SiteState> sites=new Dictionary<int,SiteState>();
        private void InitializeSites()
        {
            foreach(var site in (offline==null?TerritoryRules.Sites(profile):offline.Sites))sites.Add(site.Id,new SiteState{Site=site,MineReserve=site.Kind==PlayableBuildingKind.Mine?profile.MineReserveCredits:0,
                    MineReserveCapacity=site.Kind==PlayableBuildingKind.Mine?profile.MineReserveCredits:0});
        }
        private PlayableCommandStatus BuildAt(int siteId,int slotId,PlayableBuildingKind kind,int parentId,PlayableOwner owner,out string message)
        {
            message="Invalid site or ownership.";
            if(!sites.TryGetValue(siteId,out var site)||!Enum.IsDefined(typeof(PlayableBuildingKind),kind))return PlayableCommandStatus.InvalidTarget;
            if(site.Site.Kind==PlayableBuildingKind.RelayTower||kind==PlayableBuildingKind.RelayTower)return PlayableCommandStatus.InvalidTarget;
            if(BuildingSlotCollapsing(siteId,slotId)){message="Здание разрушается. Дождитесь освобождения площадки.";return PlayableCommandStatus.OccupiedPad;}
            NavPoint position=site.Site.Position;double heading=TerritoryRules.BuildingHeading;
            if(slotId==0)
            {
                if(kind!=site.Site.Kind||site.CenterId!=0||site.Progress<1||site.Claimant!=owner||(site.Locked.HasValue&&site.Locked!=owner)||parentId!=0)return PlayableCommandStatus.InvalidTarget;
            }
            else
            {
                if(!TerritoryRules.Defensive(kind)&&kind!=PlayableBuildingKind.Factory&&kind!=PlayableBuildingKind.Refinery&&kind!=PlayableBuildingKind.ScientificCenter&&kind!=PlayableBuildingKind.Hangar)return PlayableCommandStatus.InvalidTarget;
                if(!buildings.TryGetValue(site.CenterId,out var parent)||parent.Id!=parentId||!parent.Ready||parent.Sale!=null||parent.Owner!=owner||slotId<1)return PlayableCommandStatus.InvalidTarget;
                foreach(var b in buildings.Values)if(b.SiteId==siteId&&b.SlotId==slotId){message="Slot occupied.";return PlayableCommandStatus.OccupiedPad;}
                bool defense=TerritoryRules.DefenseSlot(slotId);
                if(defense!=(TerritoryRules.Defensive(kind)))return PlayableCommandStatus.InvalidTarget;
                var slot=(defense?site.Site.DefenseSlots(profile):site.Site.Slots).FirstOrDefault(x=>x.Id==slotId);
                if(slot==null)return PlayableCommandStatus.InvalidTarget;position=slot.Position;
            }
            if(kind==PlayableBuildingKind.ScientificCenter&&!buildings.Values.Any(b=>b.Owner==owner&&TerritoryRules.ScienceProducer(b.Kind)&&b.Ready&&b.Health>0&&b.Sale==null)){message="Нужна фабрика или ангар";return PlayableCommandStatus.Rejected;}
            if(kind==PlayableBuildingKind.GaussTower&&!buildings.Values.Any(b=>b.Owner==owner&&b.Kind==PlayableBuildingKind.ScientificCenter&&b.Ready&&b.Health>0&&b.Sale==null)){message="Нужен готовый исследовательский центр";return PlayableCommandStatus.Rejected;}
            int cost=TerritoryRules.Cost(profile,kind);
            if(!CanAfford(owner,cost)){message="Insufficient credits.";return PlayableCommandStatus.InsufficientCredits;}
            AddCredits(owner,-cost);
            var building=new Building{TermsRevision=profile.Revision,Id=nextId++,Owner=owner,Kind=kind,Position=position,SiteId=siteId,SlotId=slotId,ParentId=parentId,PaidCost=cost,Phase=ConstructionPhase.Pending,Heading=heading};
            if((kind==PlayableBuildingKind.Factory||kind==PlayableBuildingKind.Hangar||TerritoryRules.Center(kind))&&site.Rallies.TryGetValue(slotId,out var rally)){building.Rally=rally;building.HasRally=true;}
            buildings.Add(building.Id,building);
            Fact(owner,MatchFactKind.BuildingOrdered,cost,(int)kind,kind.ToString());
            if(slotId==0){site.CenterId=building.Id;site.Locked=owner;}
            message="Construction ordered.";return PlayableCommandStatus.Applied;
        }
        private bool FreeProvingGround=>offline?.ProvingGround!=null;
        private bool CanAfford(PlayableOwner owner,double cost)=>FreeProvingGround||Balance(owner)>=cost;
        private double ActionDuration(double ordinary)=>FreeProvingGround?profile.ProvingGroundActionSeconds:ordinary;
        private double BuildingDuration(Building b)=>ActionDuration(TerritoryRules.Duration(Terms(b.TermsRevision),b.Kind));
        private double Balance(PlayableOwner owner)=>offline==null?(owner==PlayableOwner.Player?credits:enemyCredits):ownerCredits[owner];
        private void AddCredits(PlayableOwner owner,double amount){if(FreeProvingGround)return;if(offline!=null)ownerCredits[owner]+=amount;else if(owner==PlayableOwner.Player)credits+=amount;else enemyCredits+=amount;}
        private PlayableCommandStatus CancelBuilding(int id,PlayableOwner owner,out string message)
        {
            message="Select own unfinished building.";
            if(!buildings.TryGetValue(id,out var b)||b.Owner!=owner||b.Ready||b.Sale!=null)return PlayableCommandStatus.InvalidEntity;
            bool solid=b.Phase!=ConstructionPhase.Pending;
            if(solid&&!TryRebuildGeometry(removal:b))
            {
                message="Construction cancellation is waiting for navigation. Try again.";
                return PlayableCommandStatus.Rejected;
            }
            Sound(PlayableSoundKind.ConstructionCancelled,b.Position,PlayableEntityKind.Tank,owner,true);
            AddCredits(owner,b.PaidCost*profile.BuildingCancellationRefundRatio);
            RemoveBuilding(b,true);
            if(solid)ReconcileWreckRoutes();
            message="Construction cancelled.";return PlayableCommandStatus.Applied;
        }
        private void RemoveBuilding(Building b,bool cancellation)=>RemoveBuildingWithAttacker(b,cancellation,null);
        private void RemoveBuildingWithAttacker(Building b,bool cancellation,PlayableOwner? attacker,bool destroyed=false)
        {
            if(!buildings.Remove(b.Id))return;
            boosts.Remove(b.Id);
            boostWear.Remove(b.Id);
            if(offline!=null&&TerritoryRules.Center(b.Kind)&&b.Phase!=ConstructionPhase.Pending)
                lastRemovedCenter[b.Owner]=b.Kind;
            if(!cancellation&&b.Sale==null&&b.Phase!=ConstructionPhase.Pending)AddLingeringVision(b.Owner,b.Position,PlayableVision.BuildingRadius(profile,b.Kind,b.Phase));
            if(b.Ready)RecordBuildingCount(b.Owner,b.Kind);
            if(!cancellation&&b.Sale==null&&b.Phase!=ConstructionPhase.Pending)BeginBuildingDestruction(b);
            if(!cancellation&&b.Sale==null)Fact(b.Owner,MatchFactKind.BuildingLost,0,(int)b.Kind);
            RecordKill(attacker,b.Owner,b.Id,true,(int)b.Kind,TerritoryRules.Cost(profile,b.Kind));
            if(destroyed)
            {
                if(b.Kind==PlayableBuildingKind.ScientificCenter)RefundDestroyedResearchQueue(b);
                if(b.Kind==PlayableBuildingKind.Factory||b.Kind==PlayableBuildingKind.Hangar)
                    AddCredits(b.Owner,b.Orders.Sum(order=>order.PaidCost));
            }
            if(b.Kind==PlayableBuildingKind.ScientificCenter)InterruptResearchCenter(b.Id);
            var children=new List<Building>();foreach(var child in buildings.Values)if(child.ParentId==b.Id)children.Add(child);
            foreach(var child in children)RemoveBuildingWithAttacker(child,false,attacker,destroyed);
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
                if(state.Site.Kind==PlayableBuildingKind.RelayTower){AdvanceRelayCapture(state,dt);continue;}
                if(state.Locked.HasValue||state.CenterId!=0){state.Contested=false;continue;}
                var present=new HashSet<PlayableOwner>();double radius=TerritoryRules.CaptureRadius(profile,state.Site.Kind);
                foreach(var u in units.Values)if(u.Kind!=PlayableEntityKind.Skat&&!eliminated.Contains(u.Owner)&&!Materializing(u)&&!Frozen(u)&&TryActor(u.Id,out var n)&&Distance(n.Position,state.Site.Position)<=radius)present.Add(u.Owner);
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
            var foundations=new List<Building>();
            foreach(var b in buildings.Values)
            {
                if(b.Phase!=ConstructionPhase.Pending||b.Sale!=null)continue;
                bool occupied=false,enemy=false;
                foreach(var u in units.Values)
                {
                    if(!TryActor(u.Id,out var n)||!InFootprint(b,n.Position,n.Radius))continue;
                    occupied=true;
                    if(u.Owner!=b.Owner){enemy=true;continue;}
                    if(b.Evacuated.Contains(u.Id)&&(n.Moving||navigation.IsPending(u.Id)))continue;
                    if(elapsed<b.RetryAt)continue;
                    if(TryEvacuate(b,u,n.Position))b.Evacuated.Add(u.Id);
                }
                if(occupied){b.BlockedReason=enemy?"Враг на площадке":"Освобождаем площадку";if(elapsed>=b.RetryAt)b.RetryAt=elapsed+profile.EvacuationRetrySeconds;continue;}
                foundations.Add(b);
            }
            if(foundations.Count>0&&TryRebuildGeometry(foundations))foreach(var b in foundations)Sound(PlayableSoundKind.ConstructionStarted,b.Position,PlayableEntityKind.Tank,b.Owner,true);
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
                // Evacuation replaces the old command: retaining its common-point
                // group can mark this unit Arrived while it is still on the pad.
                if(navigation.MoveGroup(new[]{u.Id},allocated)){
                    // Native evacuation has replaced the old issuance. Retire its
                    // domain queue too, rather than save an attack/move whose
                    // installed navigation now belongs to the evacuation root.
                    tacticalQueues.Remove(u.Id);InvalidateSpatialCompletion(u.Id);
                    u.CurrentOrder=null;u.WreckWaiting=false;InterruptBurst(u);u.ExplicitTarget=false;
                    u.Target=0;u.HasAttackMove=false;u.StoppedSeconds=0;return true;
                }
            }
            return false;
        }
        private NavPoint FactoryExit(Building b)=>new NavPoint(b.Position.X+Math.Cos(b.Heading)*profile.FactoryExitDistance,b.Position.Z+Math.Sin(b.Heading)*profile.FactoryExitDistance);
        private const int MineReserveStateMarker=0x4D525331; // MRS1
        private void WriteMineReserves(BinaryWriter writer)
        {
            if(profile.MineReserveCredits==0&&!sites.Values.Any(s=>s.MineReserveCapacity>0))return;
            writer.Write(MineReserveStateMarker);
            foreach(var site in sites.Values.Where(s=>s.Site.Kind==PlayableBuildingKind.Mine).OrderBy(s=>s.Site.Id))
            {
                WorldWire.Number(writer,site.MineReserveCapacity);
                WorldWire.Number(writer,site.MineReserve);
            }
        }

        private void ReadMineReserves(BinaryReader reader)
        {
            long start=reader.BaseStream.Position;
            if(start==reader.BaseStream.Length||reader.ReadInt32()!=MineReserveStateMarker)
            {
                reader.BaseStream.Position=start;
                if(profile.MineReserveCredits>0)throw new ArgumentException("Missing mine reserves.");
                return;
            }
            foreach(var site in sites.Values.Where(s=>s.Site.Kind==PlayableBuildingKind.Mine).OrderBy(s=>s.Site.Id))
            {
                site.MineReserveCapacity=WorldWire.Number(reader);
                site.MineReserve=WorldWire.Number(reader);
                if(site.MineReserveCapacity<0||site.MineReserveCapacity>100000||site.MineReserve<0||site.MineReserve>site.MineReserveCapacity)
                    throw new ArgumentException("Invalid mine reserve.");
            }
        }

        private TerritorySiteSnapshot[] SiteSnapshots()
        {
            var result=new List<TerritorySiteSnapshot>();
            foreach(var s in sites.Values)result.Add(new TerritorySiteSnapshot(s.Site,RelayClaimantPaint(s),RelayOwnerPaint(s),s.Progress,s.Contested,s.CenterId,s.Site.Kind==PlayableBuildingKind.RelayTower?RelayOwnerPaint(s).HasValue:buildings.TryGetValue(s.CenterId,out var center)&&center.Ready,s.ControlledSeconds,
                s.MineReserveCapacity>0?s.MineReserve:(double?)null,s.MineReserveCapacity,s.MineReserveCapacity>0&&s.MineReserve==0));
            return result.ToArray();
        }
    }
}
