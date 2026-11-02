using System;
using System.Collections.Generic;
using Spacewars.Simulation;
namespace Spacewars.Runtime
{
    // Context-owned restartable derivative. Source native points stay immutable;
    // only a complete paid explicit corridor can leave this preparation.
    internal sealed class RecastSpinePreparation : NavOwnedStorage,IDisposable
    {
        internal enum Result { Pending,Succeeded,Failed }
        private enum Phase { Scan,Strict,Height,Authority,Repair,Helper,HelperCopy,Append,Allocate,Copy,Done }
        private const int Capacity=8192;
        private readonly RecastPayload source;
        private readonly RecastRoutePoint[] raw,points;
        private RecastLocalAttachment helper;
        private RecastPayload output;
        private NavLocation cursor,target;
        private RecastRoutePoint point;
        private Phase phase;
        private int corner,count,copy,helperCopy;
        private bool strict,height,disposed;
        internal Result Status{get;private set;}
        internal long Work{get;private set;}
        internal long HelperWork{get;private set;}
        internal bool HelperActive=>helper!=null&&phase==Phase.Helper;
        internal RecastPayload Completed=>Status==Result.Succeeded?output:null;
        private static long Layout=>checked(NavStorageLayout.ObjectBytesBound(typeof(RecastSpinePreparation))+32L+Capacity*NavStorageLayout.ValueBytes(typeof(RecastRoutePoint)));
        private RecastSpinePreparation(RecastPayload source):base(Layout){this.source=source;RequireEdge(source);source.Retain();raw=(RecastRoutePoint[])source.Value;points=new RecastRoutePoint[Capacity];}
        internal static RecastSpinePreparation Create(RecastPayload source,INavStorageReservation r){
            if(source==null||!(source.Value is RecastRoutePoint[] values)||values.Length==0||values.Length>Capacity)throw new ArgumentException("Invalid immutable spine corridor.");
            NavStorageAdmission.RequireOwned(r,source);
            using(var allocation=new NavStorageAllocation(r.TryReserve)){allocation.Reserve(Layout);var result=allocation.Register(new RecastSpinePreparation(source));result.Retain();return result;}
        }
        private static bool Finite(double x)=>!double.IsNaN(x)&&!double.IsInfinity(x);
        private static bool Trace(ILayeredNavigationProvider provider,NavigationRequest request,NavLocation from,NavLocation to,INavStorageReservation r){
            if(!request.Geometry.SegmentFree(from.Position,to.Position,request.Profile.Radius))return false;
            var clearance=new NavClearanceProfile("ground",1,"ground",0,request.Profile.Radius);IReadOnlyList<NavTypedLeg> legs=null;
            try{if(provider is PartitionNavigationProvider p)return p.TryTraceOwned(from,to.Position,clearance,out var reached,out legs,r)&&reached.Equals(to);
                return RecastLayerTrace.Try(provider,from,to,clearance,out legs,r);
            }finally{(legs as NavOwnedStorage)?.ReleaseOwner();}
        }
        private static bool Height(RecastAuthorityGeometry geometry,NavLocation location,double rawHeight){
            try{double value=geometry.Point(location)[1];return Finite(value)&&Finite(rawHeight)&&Math.Abs(value-rawHeight)<=2*geometry.CellHeight+1e-6;}
            catch(ArgumentException){return false;}
        }
        internal Result Step(ILayeredNavigationProvider provider,NavigationRequest request,RecastAuthorityGeometry geometry,INavStorageReservation r,int maximumWork,Func<bool> cancelled=null){
            NavStorageAdmission.RequireOwned(r,this);if(disposed)throw new ObjectDisposedException(nameof(RecastSpinePreparation));
            int left=maximumWork;var clearance=new NavClearanceProfile("ground",1,"ground",0,request.Profile.Radius);
            while(Status==Result.Pending){
                if(cancelled?.Invoke()==true)throw new OperationCanceledException();
                if(left<4)return Status;
                if(phase==Phase.Helper){
                    long before=helper.Work;try{helper.Step(provider,request.Geometry,request.Profile,r,left,cancelled);}
                    finally{int used=checked((int)(helper.Work-before));if(used>left)throw new InvalidOperationException("Spine connector work exceeded allowance.");left-=used;Work+=used;HelperWork+=used;}
                    if(helper.Status==RecastLocalAttachment.Result.Pending)return Status;
                    if(helper.Status!=RecastLocalAttachment.Result.Succeeded){Status=Result.Failed;phase=Phase.Done;continue;}
                    helperCopy=0;phase=Phase.HelperCopy;continue;
                }
                // Each state transition, source scan, trace and point copy is charged.
                // Compound support checks conservatively use four existing units.
                int charge=phase==Phase.Copy?1:4;left-=charge;Work+=charge;
                switch(phase){
                    case Phase.Scan:
                        if(count==0){cursor=request.StartLocation.Value;points[count++]=new RecastRoutePoint(cursor,geometry.Point(cursor)[1]);}
                        if(corner==raw.Length){if(!cursor.Equals(raw[raw.Length-1].Location)){Status=Result.Failed;phase=Phase.Done;}else{copy=0;phase=Phase.Allocate;}break;}
                        point=raw[corner];target=point.Location;phase=Phase.Strict;break;
                    case Phase.Strict:strict=Trace(provider,request,cursor,target,r);phase=Phase.Height;break;
                    case Phase.Height:height=Height(geometry,target,point.Height);phase=strict&&height?Phase.Append:Phase.Authority;break;
                    case Phase.Authority:{
                        bool eligible=corner>0&&corner<raw.Length-1&&point.PortalId==null&&
                            !point.Location.Position.Equals(request.StartLocation.Value.Position)&&!point.Location.Position.Equals(request.GoalLocation.Value.Position);
                        if(eligible&&provider is PartitionNavigationProvider physical&&request.Geometry.SegmentFree(cursor.Position,target.Position,request.Profile.Radius)){
                            IReadOnlyList<NavTypedLeg> trace=null;
                            try{if(physical.TryTraceOwned(cursor,target.Position,clearance,out var reached,out trace,r)&&reached.Position.Equals(target.Position)&&physical.IsValid(reached,clearance)&&Height(geometry,reached,point.Height)){
                                // The declared native support must exist; an unknown label
                                // never grants permission to construct an authority point.
                                try{if(Finite(geometry.Point(point.Location)[1])){target=reached;phase=Phase.Append;break;}}catch(ArgumentException){}
                            }}finally{(trace as NavOwnedStorage)?.ReleaseOwner();}
                        }
                        phase=Phase.Repair;break;
                    }
                    case Phase.Repair:
                        if(point.PortalId==null&&height&&provider.IsValid(cursor,clearance)&&provider.IsValid(target,clearance)&&!request.Geometry.SegmentFree(cursor.Position,target.Position,request.Profile.Radius)){
                            if(helper==null){helper=RecastLocalAttachment.Create(request.Profile,(provider as INavStorageFacade)?.StorageRoot,r);RequireEdge(helper);}
                            helper.Begin(cursor,target,false,r);phase=Phase.Helper;
                        }else{corner++;phase=Phase.Scan;}break;
                    case Phase.HelperCopy:
                        if(helperCopy==helper.Count){corner++;phase=Phase.Scan;break;}
                        var step=helper.Location(helperCopy++);target=new NavLocation(step.Position,step.SurfaceId);
                        if(!Trace(provider,request,cursor,target,r))throw new InvalidOperationException("Spine connector lost its complete physical trace.");
                        if(count==Capacity)throw new InvalidOperationException("Recast typed point capacity exceeded.");
                        points[count++]=new RecastRoutePoint(target,geometry.Point(target)[1]);cursor=target;break;
                    case Phase.Append:
                        if(!cursor.Equals(target)){if(count==Capacity)throw new InvalidOperationException("Recast typed point capacity exceeded.");points[count++]=new RecastRoutePoint(target,geometry.Point(target)[1],point.PortalId);cursor=target;}
                        corner++;phase=Phase.Scan;break;
                    case Phase.Allocate:
                        output=RecastPayload.Allocate(r,checked(32L+count*NavStorageLayout.ValueBytes(typeof(RecastRoutePoint))));RequireEdge(output);output.Value=new RecastRoutePoint[count];phase=Phase.Copy;break;
                    case Phase.Copy:
                        if(copy<count){((RecastRoutePoint[])output.Value)[copy]=points[copy];copy++;break;}
                        Status=Result.Succeeded;phase=Phase.Done;break;
                    default:throw new InvalidOperationException("Invalid spine completion phase.");
                }
            }
            return Status;
        }
        public void Dispose(){if(disposed)return;disposed=true;Release();}
        protected override void ReleaseEdges(){helper?.Dispose();helper=null;output?.Dispose();output=null;source.Release();}
    }
}
