using System;
namespace Spacewars.Simulation
{
    // The old Work facade keeps its original layout/pointer count. New policy
    // state is a distinct owned BODY behind its former ready-snapshot slot.
    internal sealed class NavHistoricalArrivalState : NavOwnedStorage,INavPhysicalBody
    {
        internal NavigationArrivalWork Oracle;
        internal NavPagedList<NavTypedLeg> Legs;
        internal NavScheduledRequest Recipe;
        internal long EpochBase;
        internal int Gate,Cursor,Member,Leg;
        private bool frozen;internal bool DemandV3,OccupancyPrefix,SeamBound,Removal,Exact,Astar,Lazy,Certified,RectangleOrigins,TerminalRectangle,TerminalCorner,OriginShadow,OwnedArrivalAnchor,TerminalCost;
        internal int SourceOld,PreparationMember,RegionCursor,RemovalStage,EndpointCursor;internal bool EndpointFound;
        internal bool Page0Occupancy;
        internal int OccupancyStage,OccupancyMember,OccupancyCandidate,OccupancyOldRow,OccupancyCurrentRow;
        internal bool OccupancyOldBlocked,OccupancyCurrentBlocked;
        internal NavHistoricalArrivalState(Func<long,bool> reserve=null,bool birthOnly=false,bool v3=false,bool prefix=false):base(512)
        {if(prefix&&!v3)throw new ArgumentException("Occupancy-prefix policy requires Demand V3.");using(var a=new NavStorageAllocation(reserve)){a.Reserve(512);a.Register(this);DemandV3=v3;OccupancyPrefix=prefix;if(!birthOnly)Retain();}}
        internal void Seal(){if(frozen)return;Oracle?.SealPreparedIfMutable();Legs?.Seal();frozen=true;}
        internal NavHistoricalArrivalState Fork(INavStorageReservation r)
        {
            NavStorageAdmission.RequireOwned(r,this);if(!frozen)throw new InvalidOperationException("Seal historical state before fork.");
            var result=new NavHistoricalArrivalState(r.TryReserve,v3:DemandV3,prefix:OccupancyPrefix){Page0Occupancy=Page0Occupancy,OccupancyStage=OccupancyStage,OccupancyMember=OccupancyMember,OccupancyCandidate=OccupancyCandidate,OccupancyOldRow=OccupancyOldRow,OccupancyCurrentRow=OccupancyCurrentRow,OccupancyOldBlocked=OccupancyOldBlocked,OccupancyCurrentBlocked=OccupancyCurrentBlocked,RectangleOrigins=RectangleOrigins,TerminalRectangle=TerminalRectangle,TerminalCorner=TerminalCorner,OriginShadow=OriginShadow,OwnedArrivalAnchor=OwnedArrivalAnchor,TerminalCost=TerminalCost,Certified=Certified,Lazy=Lazy,Astar=Astar,Exact=Exact,Removal=Removal,SourceOld=SourceOld,PreparationMember=PreparationMember,RegionCursor=RegionCursor,RemovalStage=RemovalStage,EndpointCursor=EndpointCursor,EndpointFound=EndpointFound,SeamBound=SeamBound,EpochBase=EpochBase,Gate=Gate,Cursor=Cursor,Member=Member,Leg=Leg};
            try{result.Oracle=NavPhysicalMacro.Acquire(result,Oracle);result.Legs=Legs?.ForkOwned(r);result.Recipe=Recipe;NavPhysicalMacro.Acquire(result,Recipe?.StorageRoot);return result;}catch{result.ReleaseOwner();throw;}
        }
        internal override bool IsImmutableCaptureRoot=>IsRegistered&&frozen;
        protected override void ReleaseEdges(){Oracle?.Release();Legs?.Release();Recipe?.StorageRoot?.Release();}
        NavPhysicalTag INavPhysicalBody.PhysicalTag=>new NavPhysicalTag(OccupancyPrefix?NavPhysicalStorageKind.HistoricalArrivalStateV4:DemandV3?NavPhysicalStorageKind.HistoricalArrivalStateV3:NavPhysicalStorageKind.HistoricalArrivalStateV2,value:Page0Occupancy?NavPhysicalValueKind.Page0OccupancyArrivalPolicyV17:TerminalCost?NavPhysicalValueKind.TerminalCostArrivalPolicyV16:OwnedArrivalAnchor?NavPhysicalValueKind.OwnedArrivalAnchorPolicyV15:OriginShadow?NavPhysicalValueKind.OriginShadowArrivalPolicyV14:TerminalCorner?NavPhysicalValueKind.TerminalCornerArrivalPolicyV13:TerminalRectangle?NavPhysicalValueKind.TerminalRectangleArrivalPolicyV12:RectangleOrigins?NavPhysicalValueKind.RectangleOriginsArrivalPolicyV11:Certified?NavPhysicalValueKind.CertifiedLazyArrivalPolicyV10:Lazy?NavPhysicalValueKind.LazyArrivalPolicyV9:Astar?NavPhysicalValueKind.ExactAstarArrivalPolicyV8:Exact?NavPhysicalValueKind.ExactArrivalPolicyV7:Removal?NavPhysicalValueKind.RemovalPolicyV6:SeamBound?NavPhysicalValueKind.SeamBoundPolicyV4:NavPhysicalValueKind.None);
        void INavPhysicalBody.WritePhysicalBody(NavPhysicalBodyWriter w)
        {
            if(Page0Occupancy&&(!TerminalCost||!OwnedArrivalAnchor||!OriginShadow||!TerminalCorner||!TerminalRectangle||!RectangleOrigins||!Certified||!Lazy||!Astar||!Exact||!Removal||!OccupancyPrefix||!DemandV3))throw new ArgumentException("Page0 owner underlying policy mismatch.");
            if(!IsImmutableCaptureRoot||TerminalCost&&!OwnedArrivalAnchor||OwnedArrivalAnchor&&!OriginShadow||OriginShadow&&!TerminalCorner||TerminalCorner&&!TerminalRectangle||TerminalRectangle&&!RectangleOrigins||NavStorageLayout.ObjectBytesBound(GetType())>512)throw new ArgumentException("Historical owner layout mismatch.");
            NavPhysicalMacro.WriteEdge(w,this,Oracle,true);NavPhysicalMacro.WriteEdge(w,this,Legs,true);NavPhysicalMacro.WriteEdge(w,this,Recipe?.StorageRoot,true);
            w.Output.Write(EpochBase);w.Output.Write(Gate);w.Output.Write(Cursor);w.Output.Write(Member);w.Output.Write(Leg);if(Removal){w.Output.Write(SourceOld);w.Output.Write(PreparationMember);w.Output.Write(RegionCursor);w.Output.Write(RemovalStage);w.Output.Write(EndpointCursor);w.Output.Write(EndpointFound);}if(Page0Occupancy){w.Output.Write(OccupancyStage);w.Output.Write(OccupancyMember);w.Output.Write(OccupancyCandidate);w.Output.Write(OccupancyOldRow);w.Output.Write(OccupancyCurrentRow);w.Output.Write(OccupancyOldBlocked);w.Output.Write(OccupancyCurrentBlocked);}
        }
        internal static NavHistoricalArrivalState ReadPhysicalBody(NavPhysicalBodyReader r,bool v3=false,bool prefix=false,bool seam=false,bool removal=false,bool exact=false,bool astar=false,bool lazy=false,bool certified=false,bool rectangular=false,bool terminalRectangle=false,bool terminalCorner=false,bool originShadow=false,bool ownedAnchor=false,bool terminalCost=false,bool page0=false)
        {
            var oracle=NavPhysicalMacro.ReadEdge<NavigationArrivalWork>(r,true);var legs=NavPhysicalMacro.ReadEdge<NavPagedList<NavTypedLeg>>(r,true);var recipe=NavPhysicalMacro.ReadEdge<NavDeclarationStorage>(r,true);
            long epoch=r.Input.ReadInt64();int gate=r.Input.ReadInt32(),cursor=r.Input.ReadInt32(),member=r.Input.ReadInt32(),leg=r.Input.ReadInt32();
            if(page0&&!terminalCost||terminalCost&&!ownedAnchor||ownedAnchor&&!originShadow||originShadow&&!terminalCorner||terminalCorner&&!terminalRectangle||terminalRectangle&&!rectangular||epoch<0||gate<0||gate>4||cursor<0||member<0||leg<0||recipe!=null&&!(recipe.Facade is NavScheduledRequest)||oracle==null&&(legs!=null||recipe!=null||epoch!=0||gate!=0||cursor!=0||member!=0||leg!=0))throw new ArgumentException("Historical scalar/edge mismatch.");
            var value=new NavHistoricalArrivalState(r.Reservation.TryReserve,true,v3,prefix){Page0Occupancy=page0,RectangleOrigins=rectangular,TerminalRectangle=terminalRectangle,TerminalCorner=terminalCorner,OriginShadow=originShadow,OwnedArrivalAnchor=ownedAnchor,TerminalCost=terminalCost,Certified=certified,Lazy=lazy,Astar=astar,Exact=exact,SeamBound=seam,EpochBase=epoch,Gate=gate,Cursor=cursor,Member=member,Leg=leg,frozen=true};if(removal){value.Removal=true;value.SourceOld=r.Input.ReadInt32();value.PreparationMember=r.Input.ReadInt32();value.RegionCursor=r.Input.ReadInt32();value.RemovalStage=r.Input.ReadInt32();value.EndpointCursor=r.Input.ReadInt32();value.EndpointFound=NavPhysicalMacro.ReadBoolean(r);}
            if(page0){value.OccupancyStage=r.Input.ReadInt32();value.OccupancyMember=r.Input.ReadInt32();value.OccupancyCandidate=r.Input.ReadInt32();value.OccupancyOldRow=r.Input.ReadInt32();value.OccupancyCurrentRow=r.Input.ReadInt32();value.OccupancyOldBlocked=NavPhysicalMacro.ReadBoolean(r);value.OccupancyCurrentBlocked=NavPhysicalMacro.ReadBoolean(r);}
            value.Oracle=NavPhysicalMacro.Acquire(value,oracle);value.Legs=NavPhysicalMacro.Acquire(value,legs);value.Recipe=recipe?.Facade as NavScheduledRequest;NavPhysicalMacro.Acquire(value,recipe);return value;
        }
    }
}
