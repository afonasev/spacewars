using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;

using Spacewars.Simulation;
using static Spacewars.Runtime.WorldWire;

namespace Spacewars.Runtime
{
    internal sealed partial class PlayableDomain
    {
        private static void WriteOrderStamp(BinaryWriter w,PlayableOrderStamp o){w.Write(o!=null);if(o==null)return;w.Write(PlayableOrderStamp.Version);w.Write(o.UnitId);w.Write((int)o.Owner);w.Write(o.Generation);w.Write(o.Revision);w.Write(o.Sequence);w.Write(o.Tick);w.Write((int)o.Origin);w.Write((int)o.Kind);String(w,o.Source);w.Write(o.JobId);w.Write(o.ActionId);}
        private static PlayableOrderStamp ReadOrderStamp(BinaryReader r){if(!Boolean(r))return null;if(r.ReadInt32()!=PlayableOrderStamp.Version)throw new ArgumentException("Unsupported order stamp.");return new PlayableOrderStamp(r.ReadInt32(),EnumValue<PlayableOwner>(r),r.ReadInt64(),r.ReadInt64(),r.ReadInt64(),r.ReadInt64(),EnumValue<PlayableOrderOrigin>(r),EnumValue<PlayableCommandKind>(r),String(r),r.ReadInt64(),r.ReadInt64());}
        private static void WriteRecord(BinaryWriter w,Unit v) {w.Write(v!=null);
            if(v==null)return;
            w.Write((int)v.Kind);w.Write(v.ScenarioIndex);w.Write(v.AuthoredUpgrade.HasValue);if(v.AuthoredUpgrade.HasValue)w.Write(v.AuthoredUpgrade.Value);
            w.Write(v.BurstRemaining);
            w.Write(v.BurstTarget);
            w.Write(v.BurstSequence);
            w.Write(v.BurstSize);
            Number(w,v.BurstSpread);
            Number(w,v.BurstDelay);
            w.Write(v.ExplicitTarget);
            WorldWire.Write(w,v.PreviousPosition);
            WorldWire.Write(w,v.Velocity);
            w.Write(v.Id);
            w.Write(v.Health);
            w.Write(v.Target);
            w.Write((int)v.Owner);
            Number(w,v.Turret);
            Number(w,v.Reload);
            Number(w,v.StoppedSeconds);
            Number(w,v.Repath);
            WorldWire.Write(w,v.AttackMove);
            w.Write(v.HasAttackMove);
            w.Write(v.PatrolStarted);
            WorldWire.Write(w,v.CurrentOrder);WriteOrderStamp(w,v.LastOrder);
            }
        private static Unit ReadUnit(BinaryReader r) {if(!Boolean(r))return null;
            var v=new Unit();
            v.Kind=EnumValue<PlayableEntityKind>(r);v.ScenarioIndex=r.ReadInt32();v.AuthoredUpgrade=Boolean(r)?(bool?)Boolean(r):null;
            v.BurstRemaining=r.ReadInt32();
            v.BurstTarget=r.ReadInt32();
            v.BurstSequence=r.ReadInt32();
            v.BurstSize=r.ReadInt32();
            v.BurstSpread=Number(r);
            v.BurstDelay=Number(r);
            v.ExplicitTarget=Boolean(r);
            v.PreviousPosition=ReadPoint(r);
            v.Velocity=ReadPoint(r);
            v.Id=r.ReadInt32();
            v.Health=r.ReadInt32();
            v.Target=r.ReadInt32();
            v.Owner=EnumValue<PlayableOwner>(r);
            v.Turret=Number(r);
            v.Reload=Number(r);
            v.StoppedSeconds=Number(r);
            v.Repath=Number(r);
            v.AttackMove=ReadPoint(r);
            v.HasAttackMove=Boolean(r);
            v.PatrolStarted=Boolean(r);
            v.CurrentOrder=ReadPlayableTacticalOrderSnapshot(r);v.LastOrder=ReadOrderStamp(r);
            return v;
            }
        private static void WriteRecord(BinaryWriter w,Building v) {w.Write(v!=null);
            if(v==null)return;
            w.Write(v.TermsRevision);
            WriteRecord(w,v.Upgrade);
            w.Write(v.LastDamageTime.HasValue);
            if(v.LastDamageTime.HasValue)Number(w,v.LastDamageTime.Value);
            WriteRecord(w,v.Sale);
            WriteRecord(w,v.Repair);
            w.Write(v.Id);
            w.Write(v.SiteId);
            w.Write(v.SlotId);
            w.Write(v.ParentId);
            w.Write(v.PaidCost);
            Number(w,v.Health);
            Number(w,v.Heading);
            Number(w,v.RetryAt);
            w.Write((int)v.Phase);
            String(w,v.BlockedReason);
            Array(w,v.Evacuated.ToArray(),x=>{w.Write(x);
            });
            w.Write((int)v.Owner);
            w.Write((int)v.Kind);
            WorldWire.Write(w,v.Position);
            WorldWire.Write(w,v.Rally);
            w.Write(v.HasRally);
            Number(w,v.Build);
            w.Write(v.Ready);
            Array(w,v.Orders.ToArray(),x=>{WriteRecord(w,x);
            });
            w.Write(v.RepeatTank);
            w.Write((int)v.RepeatKind);
            }
        private static Building ReadBuilding(BinaryReader r) {if(!Boolean(r))return null;
            var v=new Building();v.TermsRevision=r.ReadInt32();
            v.Upgrade=ReadRefineryUpgrade(r);
            v.LastDamageTime=Boolean(r)?(double?)Number(r):null;
            v.Sale=ReadSaleState(r);
            v.Repair=ReadRepairState(r);
            v.Id=r.ReadInt32();
            v.SiteId=r.ReadInt32();
            v.SlotId=r.ReadInt32();
            v.ParentId=r.ReadInt32();
            v.PaidCost=r.ReadInt32();
            v.Health=Number(r);
            v.Heading=Number(r);
            v.RetryAt=Number(r);
            v.Phase=EnumValue<ConstructionPhase>(r);
            v.BlockedReason=String(r);
            foreach(int id in Array(r,()=>r.ReadInt32()))if(!v.Evacuated.Add(id))throw new ArgumentException("Duplicate evacuated ID.");
            v.Owner=EnumValue<PlayableOwner>(r);
            v.Kind=EnumValue<PlayableBuildingKind>(r);
            v.Position=ReadPoint(r);
            v.Rally=ReadPoint(r);
            v.HasRally=Boolean(r);
            v.Build=Number(r);
            v.Ready=Boolean(r);
            v.Orders.AddRange(Array(r,()=>ReadProductionOrder(r)));
            v.RepeatTank=Boolean(r);
            v.RepeatKind=EnumValue<PlayableEntityKind>(r);
            return v;
            }
        private static void WriteRecord(BinaryWriter w,Projectile v) {w.Write(v!=null);
            if(v==null)return;
            w.Write(v.TermsRevision);
            WriteRecord(w,v.Rocket);
            w.Write(v.Id);
            w.Write(v.Owner);
            w.Write(v.Target);
            WorldWire.Write(w,v.Position);
            w.Write((int)v.Kind);
            w.Write(v.Damage);
            Number(w,v.Speed);
            Number(w,v.Radius);
            Number(w,v.Remaining);
            Number(w,v.DirectionX);
            Number(w,v.DirectionZ);
            Number(w,v.Height);
            Number(w,v.VerticalSlope);
            w.Write((int)v.Faction);
            }
        private static Projectile ReadProjectile(BinaryReader r) {if(!Boolean(r))return null;
            var v=new Projectile();v.TermsRevision=r.ReadInt32();
            v.Rocket=ReadRocket(r);
            v.Id=r.ReadInt32();
            v.Owner=r.ReadInt32();
            v.Target=r.ReadInt32();
            v.Position=ReadPoint(r);
            v.Kind=EnumValue<PlayableEntityKind>(r);
            v.Damage=r.ReadInt32();
            v.Speed=Number(r);
            v.Radius=Number(r);
            v.Remaining=Number(r);
            v.DirectionX=Number(r);
            v.DirectionZ=Number(r);
            v.Height=Number(r);
            v.VerticalSlope=Number(r);
            v.Faction=EnumValue<PlayableOwner>(r);
            return v;
            }
        private static void WriteRecord(BinaryWriter w,ProductionOrder v) {w.Write(v!=null);
            if(v==null)return;
            w.Write(v.TermsRevision);
            w.Write(v.Id);
            w.Write((int)v.Kind);
            w.Write(v.PaidCost);
            w.Write(v.PopulationCost);
            Number(w,v.Duration);
            Number(w,v.Remaining);
            w.Write(v.Active);
            }
        private static ProductionOrder ReadProductionOrder(BinaryReader r) {if(!Boolean(r))return null;
            var v=new ProductionOrder();v.TermsRevision=r.ReadInt32();
            v.Id=r.ReadInt64();
            v.Kind=EnumValue<PlayableEntityKind>(r);
            v.PaidCost=r.ReadInt32();
            v.PopulationCost=r.ReadInt32();
            v.Duration=Number(r);
            v.Remaining=Number(r);
            v.Active=Boolean(r);
            return v;
            }
        private static void WriteRecord(BinaryWriter w,ResearchOrder v) {w.Write(v!=null);
            if(v==null)return;
            w.Write(v.Id);
            w.Write((int)v.Kind);
            w.Write(v.CenterId);
            Number(w,v.PaidCost);
            Number(w,v.Elapsed);
            Number(w,v.Duration);
            w.Write(v.Active);
            w.Write(v.Complete);
            }
        private static ResearchOrder ReadResearchOrder(BinaryReader r) {if(!Boolean(r))return null;
            var v=new ResearchOrder();
            v.Id=r.ReadInt64();
            v.Kind=EnumValue<PlayableResearchKind>(r);
            v.CenterId=r.ReadInt32();
            v.PaidCost=Number(r);
            v.Elapsed=Number(r);
            v.Duration=Number(r);
            v.Active=Boolean(r);
            v.Complete=Boolean(r);
            return v;
            }
        private static void WriteRecord(BinaryWriter w,RefineryUpgrade v) {w.Write(v!=null);
            if(v==null)return;
            w.Write(v.TermsRevision);
            Number(w,v.PaidCost);
            Number(w,v.Duration);
            Number(w,v.Elapsed);
            w.Write(v.Complete);
            }
        private static RefineryUpgrade ReadRefineryUpgrade(BinaryReader r) {if(!Boolean(r))return null;
            var v=new RefineryUpgrade();v.TermsRevision=r.ReadInt32();
            v.PaidCost=Number(r);
            v.Duration=Number(r);
            v.Elapsed=Number(r);
            v.Complete=Boolean(r);
            return v;
            }
        private static void WriteRecord(BinaryWriter w,SaleState v) {w.Write(v!=null);
            if(v==null)return;
            w.Write(v.TermsRevision);
            Number(w,v.Elapsed);
            Number(w,v.Duration);
            }
        private static SaleState ReadSaleState(BinaryReader r) {if(!Boolean(r))return null;
            var v=new SaleState();v.TermsRevision=r.ReadInt32();
            v.Elapsed=Number(r);
            v.Duration=Number(r);
            return v;
            }
        private static void WriteRecord(BinaryWriter w,RepairState v) {w.Write(v!=null);
            if(v==null)return;
            w.Write(v.TermsRevision);
            Number(w,v.MissingHealth);
            Number(w,v.TotalCost);
            Number(w,v.Duration);
            Number(w,v.PaidSeconds);
            Number(w,v.SettlementElapsed);
            w.Write(v.Waiting);
            }
        private static RepairState ReadRepairState(BinaryReader r) {if(!Boolean(r))return null;
            var v=new RepairState();v.TermsRevision=r.ReadInt32();
            v.MissingHealth=Number(r);
            v.TotalCost=Number(r);
            v.Duration=Number(r);
            v.PaidSeconds=Number(r);
            v.SettlementElapsed=Number(r);
            v.Waiting=Boolean(r);
            return v;
            }
        private static void WriteRecord(BinaryWriter w,Rocket v) {w.Write(v!=null);
            if(v==null)return;
            WorldWire.Write(w,v.Flight);
            WorldWire.Write(w,v.Predicted);
            Number(w,v.Elapsed);
            Number(w,v.Radius);
            Number(w,v.BlastRadius);
            Number(w,v.MarkerStartRadius);
            Number(w,v.MarkerOpacity);
            Number(w,v.BuildingHeight);
            }
        private static Rocket ReadRocket(BinaryReader r) {if(!Boolean(r))return null;
            var v=new Rocket();
            v.Flight=ReadFlight(r);
            v.Predicted=ReadBallisticContact(r);
            v.Elapsed=Number(r);
            v.Radius=Number(r);
            v.BlastRadius=Number(r);
            v.MarkerStartRadius=Number(r);
            v.MarkerOpacity=Number(r);
            v.BuildingHeight=Number(r);
            return v;
            }
    }
}
