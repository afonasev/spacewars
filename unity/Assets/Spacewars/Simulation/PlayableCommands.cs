using System;
using System.Collections.Generic;

namespace Spacewars.Simulation
{
    public enum PlayableCommandKind { BuildFactory, BuildRefinery, QueueTank, SetRally, Move, Attack, AttackMove, Stop, Restart, BuildAt, CancelBuilding, CancelProductionOrder, ToggleRepeatProduction, SellBuilding, StartBuildingRepair, CancelBuildingRepair, UpgradeRefinery, CancelRefineryUpgrade, QueueExplorer, QueueShkval, QueueResearch, CancelResearch, Hold, Follow }
    public enum PlayableCommandStatus { Accepted, Applied, Rejected, Overflow, StaleGeneration, InvalidSequence, InvalidOwner, InvalidEntity, InvalidTarget, InsufficientCredits, OccupiedPad, Stopped, Cancelled }
    public enum PlayableOrderMode { Replace, Append }
    public enum PlayableOrderOrigin { Unknown,Human,Ai }
    // Versioned authority provenance is separate from the existing gameplay command schema9.
    // Unknown means legacy/direct authority input, never an inferred human or AI origin.
    public sealed class PlayableOrderStamp
    {
        public const int Version=1;
        public readonly int UnitId; public readonly PlayableOwner Owner; public readonly long Generation,Revision,Sequence,Tick,JobId,ActionId;
        public readonly PlayableOrderOrigin Origin; public readonly PlayableCommandKind Kind; public readonly string Source;
        public PlayableOrderStamp(int unit,PlayableOwner owner,long generation,long revision,long sequence,long tick,PlayableOrderOrigin origin,PlayableCommandKind kind,string source,long job,long action)
        {UnitId=unit;Owner=owner;Generation=generation;Revision=revision;Sequence=sequence;Tick=tick;Origin=origin;Kind=kind;Source=source;JobId=job;ActionId=action;}
    }
    public sealed class PlayableCommand
    {
        public const int CurrentSchemaVersion=10;
        private readonly int[] entityIds;
        public PlayableCommand(long generation,long sequence,string playerId,PlayableCommandKind kind,int[] entityIds,NavPoint target=default(NavPoint),int pad=0,int siteId=0,int slotId=0,PlayableBuildingKind buildingKind=PlayableBuildingKind.Headquarters,int parentId=0,int targetId=0,long productionOrderId=0,PlayableEntityKind unitKind=PlayableEntityKind.Tank,PlayableResearchKind researchKind=PlayableResearchKind.TankChassis,PlayableOrderOrigin origin=PlayableOrderOrigin.Unknown,string source=null,long jobId=0,long actionId=0,PlayableOrderMode mode=PlayableOrderMode.Replace)
        { Mode=mode;Origin=origin;Source=source;JobId=jobId;ActionId=actionId;ResearchKind=researchKind;UnitKind=unitKind;ProductionOrderId=productionOrderId; TargetId=targetId;SiteId=siteId;SlotId=slotId;BuildingKind=buildingKind;ParentId=parentId;Generation=generation;Sequence=sequence;PlayerId=playerId;Kind=kind;this.entityIds=entityIds==null?new int[0]:(int[])entityIds.Clone();Target=target;Pad=pad;SchemaVersion=CurrentSchemaVersion; }
        // Bound by authority after schema9 ingress; never inferred by transport or AI.
        public NavLocation? TargetLocation{get;private set;}
        public PlayableCommand WithResolvedLocation(NavLocation location)
        {if(!location.Position.Equals(Target))throw new ArgumentException("Command location/XZ mismatch.");var copy=new PlayableCommand(Generation,Sequence,PlayerId,Kind,entityIds,Target,Pad,SiteId,SlotId,BuildingKind,ParentId,TargetId,ProductionOrderId,UnitKind,ResearchKind,Origin,Source,JobId,ActionId,Mode);copy.TargetLocation=location;return copy;}
        public PlayableOrderMode Mode{get;}
        public PlayableOrderOrigin Origin{get;} public string Source{get;} public long JobId{get;} public long ActionId{get;}
        public PlayableCommand AsHuman(){var copy=new PlayableCommand(Generation,Sequence,PlayerId,Kind,entityIds,Target,Pad,SiteId,SlotId,BuildingKind,ParentId,TargetId,ProductionOrderId,UnitKind,ResearchKind,PlayableOrderOrigin.Human,mode:Mode);return TargetLocation.HasValue?copy.WithResolvedLocation(TargetLocation.Value):copy;}
        public PlayableResearchKind ResearchKind{get;} public PlayableEntityKind UnitKind{get;} public long ProductionOrderId{get;} public int TargetId{get;} public int SiteId{get;} public int SlotId{get;} public int ParentId{get;} public PlayableBuildingKind BuildingKind{get;}
        public int SchemaVersion {get;} public long Generation {get;} public long Sequence {get;} public string PlayerId {get;} public PlayableCommandKind Kind {get;} public NavPoint Target {get;} public int Pad {get;}
        public IReadOnlyList<int> EntityIds {get{return Array.AsReadOnly(entityIds);}}
        public int[] CopyEntityIds(){return (int[])entityIds.Clone();}
    }
    public sealed class PlayableCommandReceipt
    { public PlayableCommandReceipt(long sequence,long appliedTick,PlayableCommandStatus status,string message,double latencyMilliseconds,string ownerId=null){OwnerId=ownerId;Sequence=sequence;AppliedTick=appliedTick;Status=status;Message=message;LatencyMilliseconds=latencyMilliseconds;} public string OwnerId{get;} public long Sequence{get;} public long AppliedTick{get;} public PlayableCommandStatus Status{get;} public string Message{get;} public double LatencyMilliseconds{get;} }
    public sealed class PlayableCommandSubmitResult
    { public PlayableCommandSubmitResult(PlayableCommandStatus status){Status=status;} public PlayableCommandStatus Status{get;} public bool Accepted{get{return Status==PlayableCommandStatus.Accepted;}} }
}
