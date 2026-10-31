using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    internal sealed partial class PlayableDomain
    {
        private sealed class ObservedBuildingDestruction
        {internal PlayableBuildingDestruction Snapshot;internal int Mask;}
        private readonly List<ObservedBuildingDestruction> buildingDestructions=new List<ObservedBuildingDestruction>();
        private bool BuildingSlotCollapsing(int site,int slot)=>buildingDestructions.Any(d=>d.Snapshot.SiteId==site&&d.Snapshot.SlotId==slot&&Tick<d.Snapshot.EndTick);
        private void BeginBuildingDestruction(Building b)
        {
            int mask=0;
            foreach(var owner in Owners)if(owner==b.Owner||Vision(owner).IsVisible(b.Position))mask|=1<<(int)owner;
            // Native simulation frequency is a wire/game clock invariant, not an art control.
            long end=Tick+(long)Math.Ceiling(profile.BuildingDestructionSeconds*30);
            var snapshot=new PlayableBuildingDestruction(b.Id,b.SiteId,b.SlotId,b.Owner,b.Kind,b.Position,b.Kind==PlayableBuildingKind.Factory&&b.LaunchPhase==FactoryReleaseRules.Closed?TerritoryRules.BuildingHeading:b.Heading,
                b.Ready?1:b.Build/BuildingDuration(b),b.Upgrade?.Complete==true||Research(b.Owner).Any(o=>PlayableResearchRules.Local(o.Kind)&&o.CenterId==b.Id&&o.Complete),Tick,end);
            buildingDestructions.Add(new ObservedBuildingDestruction{Snapshot=snapshot,Mask=mask});
            Sound(PlayableSoundKind.Destroyed,b.Position,PlayableEntityKind.Tank,b.Owner,true);
        }
        private PlayableBuildingDestruction[] BuildingDestructions(PlayableOwner? observer=null)
            =>buildingDestructions.Where(d=>Tick<d.Snapshot.EndTick&&(!observer.HasValue||
                (d.Mask&(1<<(int)observer.Value))!=0&&Vision(observer.Value).IsVisible(d.Snapshot.Position))).Select(d=>d.Snapshot).ToArray();
        private void WriteBuildingDestructions(BinaryWriter w)
        {
            w.Write(0x42444331);
            WorldWire.Array(w,buildingDestructions.Where(d=>Tick<d.Snapshot.EndTick).ToArray(),d=>{
                var s=d.Snapshot;w.Write(s.Id);w.Write(s.SiteId);w.Write(s.SlotId);w.Write((int)s.Owner);w.Write((int)s.Kind);
                WorldWire.Number(w,s.Position.X);WorldWire.Number(w,s.Position.Z);WorldWire.Number(w,s.Heading);WorldWire.Number(w,s.Progress);
                w.Write(s.Upgraded);w.Write(s.StartTick);w.Write(s.EndTick);w.Write(d.Mask);
            });
        }
        private void ReadBuildingDestructions(BinaryReader r)
        {
            if(r.ReadInt32()!=0x42444331)throw new ArgumentException("Invalid building destruction section.");
            var rows=WorldWire.Array(r,()=>{
                int id=r.ReadInt32(),site=r.ReadInt32(),slot=r.ReadInt32();var owner=WorldWire.EnumValue<PlayableOwner>(r);var kind=WorldWire.EnumValue<PlayableBuildingKind>(r);
                var position=new NavPoint(WorldWire.Number(r),WorldWire.Number(r));double heading=WorldWire.Number(r),progress=WorldWire.Number(r);
                bool upgraded=WorldWire.Boolean(r);long start=r.ReadInt64(),end=r.ReadInt64();int mask=r.ReadInt32();
                if(id<=0||id>=nextId||buildings.ContainsKey(id)||!sites.TryGetValue(site,out var state)||slot<0||(slot>state.Site.Slots.Count&&!(TerritoryRules.Defensive(kind)&&state.Site.DefenseSlots(profile).Any(x=>x.Id==slot)))||!HasOwner(owner)||progress<0||progress>1||start<0||start>Tick||end<=Tick||end<=start||mask<0||mask>255)
                    throw new ArgumentException("Invalid building destruction record.");
                return WorldWire.Record(r,()=>new ObservedBuildingDestruction{Snapshot=WorldWire.Record(r,()=>new PlayableBuildingDestruction(id,site,slot,owner,kind,position,heading,progress,upgraded,start,end)),Mask=mask});
            });
            if(rows.Select(d=>d.Snapshot.Id).Distinct().Count()!=rows.Length||rows.Select(d=>(d.Snapshot.SiteId,d.Snapshot.SlotId)).Distinct().Count()!=rows.Length)
                throw new ArgumentException("Duplicate building destruction reservation.");
            buildingDestructions.AddRange(rows);
        }
    }
}
