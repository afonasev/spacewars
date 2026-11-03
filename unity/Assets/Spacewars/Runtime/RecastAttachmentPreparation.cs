using System;
using System.Collections.Generic;
using Spacewars.Simulation;
namespace Spacewars.Runtime
{
    // Context-owned derivative, absent from save authority. Every accepted bend
    // is copied into the ordinary complete member transcript before Ready.
    internal sealed class RecastAttachmentPreparation : NavOwnedStorage,IDisposable
    {
        private enum Stage { Init,Center,Radius,Scan,PeerScan,PeerCopy,PeerCompact,CountSeeds,Targets,Seeds,Field,StartRoute,Route,BranchCopy,Build,Origin,SpineLegs,SpinePoints,Terminal,PublishLegs,PublishPoints,Done,DonorBindings,DonorQualify,DonorSeeds,DonorReset,DonorSelect,DonorCompact }
        private const long Header=512;
        private readonly RecastPayload[] origins,terminals,results;
        internal readonly RecastMemberRoute[] WorkingRoutes;
        internal bool Complete=>stage==Stage.Done;
        private readonly int[] joins,exits,indices,donors;
        private readonly NavTypedLeg[] legs=new NavTypedLeg[16384];
        private readonly RecastRoutePoint[] points=new RecastRoutePoint[4096];
        private RecastAttachmentField field;
        private Stage stage;
        private bool origin=true,disposed;
        private int cursor,stop,count,seedCount,seedCursor,routeCursor,copy,member,segment,traceCursor,legCount,pointCount,originCount,terminalIndex;
        private int groupPoints,groupLegs,donorCount,bindingField;
        private bool donorShared;
        private NavPoint center;
        private double originRadius;
        private RecastPayload branch,publish;
        private NavPoint[] branchPoints;
        private NavTypedLeg[] publishedLegs;
        private RecastRoutePoint[] publishedPoints;
        internal long Work{get;private set;}
        private static long Align(long n)=>checked((n+7)/8*8);
        private static long Layout(int m)=>checked(Header+10*32L+4*Align(8L*m)+4*Align(4L*m)+216L*16384+56L*4096);
        private RecastAttachmentPreparation(int m):base(Layout(m))
        {
            if(NavStorageLayout.ObjectBytesBound(typeof(RecastAttachmentPreparation))>Header)throw new InvalidOperationException("Attachment preparation header estimate exceeded.");
            WorkingRoutes=new RecastMemberRoute[m];origins=new RecastPayload[m];terminals=new RecastPayload[m];results=new RecastPayload[m];joins=new int[m];exits=new int[m];indices=new int[m];donors=new int[m];
        }
        internal static RecastAttachmentPreparation Create(int members,INavStorageReservation r)
        {
            using(var allocation=new NavStorageAllocation(r.TryReserve)){allocation.Reserve(Layout(members));var result=allocation.Register(new RecastAttachmentPreparation(members));result.Retain();return result;}
        }
        private void BeginField(NavigationRequest[] members,RecastSpineProposal spine,ILayeredNavigationProvider provider,INavStorageReservation r,string surface){
                if(field==null){field=RecastAttachmentField.Create(members[0].Profile.LocalGridCell,Math.Max(originRadius,spine.FanRadius),members.Length,checked(spine.Stops.Length+members.Length),(provider as INavStorageFacade)?.StorageRoot,r);RequireEdge(field);}
                field.Begin(origin?center:spine.FanCenter,origin?originRadius:spine.FanRadius,surface,origin,count,checked(seedCount+(!origin?donorCount:0)),r);cursor=stop=seedCursor=0;stage=Stage.Targets;
            }
        private static double Distance(NavPoint a,NavPoint b){double x=a.X-b.X,z=a.Z-b.Z;return Math.Sqrt(x*x+z*z);}
        private static bool Eligible(RecastSpineProposal spine,int node,bool origin,string surface)=> (origin?node<=spine.Fan:node>=spine.Fan)&&spine.Node(node).SurfaceId==surface;
#if RECAST_ATTACHMENT_PHASE_AUDIT
        private static bool Trace(ILayeredNavigationProvider provider,NavigationRequest request,NavLocation a,NavLocation b,INavStorageReservation r,out IReadOnlyList<NavTypedLeg> trace){
            bool result=TraceCore(provider,request,a,b,r,out trace);global::RecastAttachmentPhaseAudit.HelperTrace(request.Entity,a,b,result,trace);return result;
        }
        private static bool TraceCore(ILayeredNavigationProvider provider,NavigationRequest request,NavLocation a,NavLocation b,INavStorageReservation r,out IReadOnlyList<NavTypedLeg> trace)
#else
        private static bool Trace(ILayeredNavigationProvider provider,NavigationRequest request,NavLocation a,NavLocation b,INavStorageReservation r,out IReadOnlyList<NavTypedLeg> trace)
#endif
        {
            trace=null;if(!request.Geometry.SegmentFree(a.Position,b.Position,request.Profile.Radius))return false;
            var clearance=new NavClearanceProfile("ground",1,"ground",0,request.Profile.Radius);
            if(provider is PartitionNavigationProvider partition)return partition.TryTraceOwned(a,b.Position,clearance,out var reached,out trace,r)&&reached.Equals(b);
            return RecastLayerTrace.Try(provider,a,b,clearance,out trace,r);
        }
        private static bool Whole(ILayeredNavigationProvider p,NavigationRequest q,NavLocation a,NavLocation b,INavStorageReservation r)
        {IReadOnlyList<NavTypedLeg> trace=null;try{return Trace(p,q,a,b,r,out trace);}finally{(trace as NavOwnedStorage)?.ReleaseOwner();}}
        private void Point(NavLocation p,RecastAuthorityGeometry geometry){if(pointCount==points.Length)throw new InvalidOperationException("Recast branch capacity exceeded.");points[pointCount++]=new RecastRoutePoint(p,geometry.Point(p)[1]);}
        private void Leg(NavTypedLeg leg){if(legCount==legs.Length)throw new InvalidOperationException("Recast branch capacity exceeded.");legs[legCount++]=leg;}
        private NavLocation BranchPoint(NavigationRequest request,RecastSpineProposal spine,int i,bool fromOrigin)
        {
            var payload=fromOrigin?origins[member]:terminals[member];
            if(payload!=null){var values=(NavPoint[])payload.Value;return new NavLocation(values[i],fromOrigin?request.StartLocation.Value.SurfaceId:request.GoalLocation.Value.SurfaceId);}
            return fromOrigin?(i==0?request.StartLocation.Value:spine.Node(joins[member])):(i==0?spine.Node(exits[member]):request.GoalLocation.Value);
        }
        private int BranchCount(bool fromOrigin){var payload=fromOrigin?origins[member]:terminals[member];return payload==null?2:((NavPoint[])payload.Value).Length;}
        internal bool Step(NavigationRequest[] members,RecastMemberRoute[] baseline,RecastSpineProposal spine,ILayeredNavigationProvider provider,RecastAuthorityGeometry geometry,INavStorageReservation r,int maximumWork,Func<bool> cancelled)
        {
            NavStorageAdmission.RequireOwned(r,this);int left=maximumWork;var routes=WorkingRoutes;
#if RECAST_ATTACHMENT_PHASE_AUDIT
            global::RecastAttachmentPhaseAudit.HelperStep(this,members,baseline,spine,provider);
#endif

            while(stage!=Stage.Done){
                if(cancelled?.Invoke()==true)throw new OperationCanceledException();
                if(left<4)return false;
                if(stage==Stage.Field||stage==Stage.Route){
                    long before=field.Work;try{field.Step(provider,members[0].Geometry,members[0].Profile.Radius,r,left,cancelled);}finally{int used=checked((int)(field.Work-before));Work+=used;left-=used;}
                    if(field.Status==RecastAttachmentField.Result.Pending||field.RoutePending)return false;
                    if(stage==Stage.Field){routeCursor=0;stage=Stage.StartRoute;continue;}
                    int prefix=!origin&&field.SourceDonor(routeCursor)>=0?1:0;
                    branch=RecastPayload.Allocate(r,32+16L*(field.Count+prefix));RequireEdge(branch);branchPoints=new NavPoint[field.Count+prefix];branch.Value=branchPoints;copy=0;stage=Stage.BranchCopy;continue;
                }
                left--;Work++;
#if RECAST_ATTACHMENT_PHASE_AUDIT
global::RecastAttachmentPhaseAudit.HelperCharge(this,(int)stage,1,origin);
#endif

                switch(stage){
                    case Stage.Init:
                        routes[cursor]=baseline[cursor];joins[cursor]=routes[cursor]?.Join??-1;exits[cursor]=routes[cursor]?.Exit??-1;if(routes[cursor]!=null){groupPoints+=routes[cursor].Waypoints.Length;groupLegs+=routes[cursor].Legs.Length;}if(++cursor==members.Length){cursor=0;stage=Stage.Center;}break;
                    case Stage.Center:
                        center=new NavPoint(center.X+members[cursor].StartLocation.Value.Position.X,center.Z+members[cursor].StartLocation.Value.Position.Z);
                        if(++cursor==members.Length){center=new NavPoint(center.X/members.Length,center.Z/members.Length);cursor=0;stage=Stage.Radius;}break;
                    case Stage.Radius:
                        originRadius=Math.Max(originRadius,Distance(center,members[cursor].StartLocation.Value.Position)+members[cursor].Profile.Radius);
                        if(++cursor==members.Length){cursor=0;stage=Stage.Scan;}break;
                    case Stage.Scan:{
                        if(cursor==members.Length){stop=seedCount=0;stage=count==0?(origin?Stage.Scan:Stage.Build):Stage.PeerScan;routeCursor=member=0;if(count==0){if(origin){origin=false;cursor=0;}else member=0;}break;}
                        if(routes[cursor]!=null){cursor++;stop=0;break;}
                        var request=members[cursor];string surface=origin?request.StartLocation.Value.SurfaceId:request.GoalLocation.Value.SurfaceId;
                        if(stop==spine.Stops.Length){indices[count++]=cursor++;stop=0;break;}
                        int node=spine.Stops[stop++];if(!Eligible(spine,node,origin,surface))break;
                        left--;Work++;
#if RECAST_ATTACHMENT_PHASE_AUDIT
global::RecastAttachmentPhaseAudit.HelperCharge(this,(int)stage,1,origin);
#endif

                        if(Whole(provider,request,origin?request.StartLocation.Value:spine.Node(node),origin?spine.Node(node):request.GoalLocation.Value,r)){
                            if(origin)joins[cursor]=node;else exits[cursor]=node;cursor++;stop=0;
                        }break;
                    }
                    case Stage.PeerScan:{
                        if(routeCursor==count){cursor=stop=0;stage=Stage.PeerCompact;break;}
                        if(member==members.Length){routeCursor++;member=0;break;}
                        int recipient=indices[routeCursor];var request=members[recipient];var donor=members[member];int node=origin?joins[member]:exits[member];
                        var recipientPoint=origin?request.StartLocation.Value:request.GoalLocation.Value;var donorPoint=origin?donor.StartLocation.Value:donor.GoalLocation.Value;
                        if(member==recipient||node<0||!Eligible(spine,node,origin,recipientPoint.SurfaceId)||donorPoint.SurfaceId!=recipientPoint.SurfaceId||donor.HeldIdentity!=request.HeldIdentity||(origin?origins[member]:terminals[member])!=null||routes[member]!=null&&!ReferenceEquals(routes[member].Spine,spine)){member++;break;}
                        // Direct donors only. Both complete edges are certified at
                        // the recipient's current clearance and knowledge.
                        left--;Work++;
#if RECAST_ATTACHMENT_PHASE_AUDIT
global::RecastAttachmentPhaseAudit.HelperCharge(this,(int)stage,1,origin);
#endif

                        if(!Whole(provider,request,origin?recipientPoint:spine.Node(node),donorPoint,r)){member++;break;}
                        left--;Work++;
#if RECAST_ATTACHMENT_PHASE_AUDIT
global::RecastAttachmentPhaseAudit.HelperCharge(this,(int)stage,1,origin);
#endif

                        if(!Whole(provider,request,donorPoint,origin?spine.Node(node):recipientPoint,r)){member++;break;}
                        branch=RecastPayload.Allocate(r,32+16*3);RequireEdge(branch);branchPoints=new NavPoint[3];branch.Value=branchPoints;copy=0;stage=Stage.PeerCopy;break;
                    }
                    case Stage.PeerCopy:{
                        int recipient=indices[routeCursor],node=origin?joins[member]:exits[member];
                        branchPoints[copy]=copy==1?(origin?members[member].Start:members[member].Goal):copy==0?(origin?members[recipient].Start:spine.Node(node).Position):(origin?spine.Node(node).Position:members[recipient].Goal);
                        if(++copy==3){if(origin){origins[recipient]=branch;joins[recipient]=node;}else{terminals[recipient]=branch;exits[recipient]=node;}branch=null;branchPoints=null;routeCursor++;member=0;stage=Stage.PeerScan;}break;
                    }
                    case Stage.PeerCompact:
                        if(cursor<count){int recipient=indices[cursor++];if((origin?joins[recipient]:exits[recipient])<0)indices[stop++]=recipient;break;}
                        count=stop;stop=seedCount=0;
                        if(count>0)stage=Stage.CountSeeds;
                        else if(origin){origin=false;cursor=0;stage=Stage.Scan;}
                        else{member=0;stage=Stage.Build;}
                        break;
                    case Stage.CountSeeds:{
                        string surface=origin?members[0].StartLocation.Value.SurfaceId:members[0].GoalLocation.Value.SurfaceId;
                        if(stop<spine.Stops.Length){if(Eligible(spine,spine.Stops[stop++],origin,surface))seedCount++;break;}
                        donorCount=bindingField=cursor=0;donorShared=true;
                        if(!origin){stage=Stage.DonorBindings;break;}
                        BeginField(members,spine,provider,r,surface);break;
                    }
                    case Stage.DonorBindings:{
                        if(cursor==members.Length){cursor=0;if(donorShared){stage=Stage.DonorReset;break;}BeginField(members,spine,provider,r,members[0].GoalLocation.Value.SurfaceId);break;}
                        var a=members[0];var b=members[cursor];
                        if(!ReferenceEquals(a.Geometry,b.Geometry)||a.HeldIdentity!=b.HeldIdentity||a.Profile.ProfileId!=b.Profile.ProfileId||a.Profile.Revision!=b.Profile.Revision||a.Profile.WireValueCount!=b.Profile.WireValueCount||a.GoalLocation.Value.SurfaceId!=b.GoalLocation.Value.SurfaceId){donorShared=false;cursor=members.Length;break;}
                        if(ReferenceEquals(a.Profile,b.Profile)){cursor++;bindingField=0;break;}
                        if(BitConverter.DoubleToInt64Bits(a.Profile.WireValueAt(bindingField))!=BitConverter.DoubleToInt64Bits(b.Profile.WireValueAt(bindingField))){donorShared=false;cursor=members.Length;break;}
                        if(++bindingField==a.Profile.WireValueCount){cursor++;bindingField=0;}break;
                    }
                    case Stage.DonorReset:
                        if(cursor<count){donors[cursor++]=-1;break;}
                        cursor=0;stage=Stage.DonorQualify;break;
                    case Stage.DonorQualify:{
                        if(cursor==members.Length){cursor=stop=donorCount=0;stage=Stage.DonorCompact;break;}
                        int donor=cursor++,node=exits[donor];var q=members[indices[0]];var goal=members[donor].GoalLocation.Value;
                        // Direct accepted prefixes only. Never chain through another
                        // curved/copied terminal. All field recipients share exact bindings.
                        if(node<0||terminals[donor]!=null||routes[donor]!=null&&!ReferenceEquals(routes[donor].Spine,spine)||!Eligible(spine,node,false,goal.SurfaceId))break;
                        left--;Work++;
#if RECAST_ATTACHMENT_PHASE_AUDIT
                        global::RecastAttachmentPhaseAudit.HelperCharge(this,(int)stage,1,origin);
#endif
                        if(Whole(provider,q,spine.Node(node),goal,r)){member=donor;routeCursor=0;stage=Stage.DonorSelect;}
                        break;
                    }
                    case Stage.DonorSelect:{
                        // One comparison per recipient, using the existing paid M slots.
                        // Shared exact bindings make the qualified direct prefix valid
                        // for every unresolved recipient; distance is only a root hint.
                        int recipient=indices[routeCursor],best=donors[routeCursor];
                        var endpoint=members[recipient].Goal;var candidate=members[member].Goal;
                        double dx=candidate.X-endpoint.X,dz=candidate.Z-endpoint.Z,distance=dx*dx+dz*dz;
                        double previous=double.PositiveInfinity;
                        if(best>=0){var point=members[best].Goal;double x=point.X-endpoint.X,z=point.Z-endpoint.Z;previous=x*x+z*z;}
                        if(best<0||distance<previous||distance==previous&&(members[member].Entity<members[best].Entity||members[member].Entity==members[best].Entity&&members[member].Request<members[best].Request))donors[routeCursor]=member;
                        if(++routeCursor==count)stage=Stage.DonorQualify;
                        break;
                    }
                    case Stage.DonorCompact:
                        // Compact behind the read cursor; each duplicate comparison
                        // and each emitted unique winner consumes its own paid step.
                        if(cursor==count){BeginField(members,spine,provider,r,members[0].GoalLocation.Value.SurfaceId);break;}
                        if(donors[cursor]<0){cursor++;stop=0;break;}
                        if(stop<donorCount){if(donors[stop++]==donors[cursor]){cursor++;stop=0;}break;}
                        donors[donorCount++]=donors[cursor++];stop=0;break;
                    case Stage.DonorSeeds:
                        if(cursor==donorCount){stage=Stage.Field;break;}
                        int sourceDonor=donors[cursor++];field.SetSeed(seedCursor++,exits[sourceDonor],members[sourceDonor].GoalLocation.Value,sourceDonor);break;
                    case Stage.Targets:
                        field.SetTarget(cursor,origin?members[indices[cursor]].StartLocation.Value:members[indices[cursor]].GoalLocation.Value);if(++cursor==count)stage=Stage.Seeds;break;
                    case Stage.Seeds:{
                        string surface=origin?members[0].StartLocation.Value.SurfaceId:members[0].GoalLocation.Value.SurfaceId;
                        if(stop<spine.Stops.Length){int node=spine.Stops[stop++];if(Eligible(spine,node,origin,surface))field.SetSeed(seedCursor++,node,spine.Node(node));}
                        else{cursor=0;stage=!origin&&donorCount>0?Stage.DonorSeeds:Stage.Field;}break;
                    }
                    case Stage.StartRoute:
                        if(routeCursor==count){if(origin){origin=false;cursor=stop=count=0;stage=Stage.Scan;}else{member=0;stage=Stage.Build;}break;}
                        if(!field.Resolved(routeCursor)){routeCursor++;break;}
                        int selected=indices[routeCursor];if(origin)joins[selected]=field.SourceStop(routeCursor);else exits[selected]=field.SourceStop(routeCursor);
                        field.BeginRoute(routeCursor);stage=Stage.Route;break;
                    case Stage.BranchCopy:
                        if(copy<branchPoints.Length){
                            int prefix=!origin&&field.SourceDonor(routeCursor)>=0?1:0;
                            // The field starts at the donor; ordinary publication starts
                            // at its GENUINE spine exit and validates every complete edge.
                            branchPoints[copy]=prefix!=0&&copy==0?spine.Node(exits[indices[routeCursor]]).Position:field.Location(copy-prefix).Position;copy++;break;
                        }
                        int target=indices[routeCursor];if(origin)origins[target]=branch;else terminals[target]=branch;branch=null;branchPoints=null;routeCursor++;
                        stage=Stage.StartRoute;break;
                    case Stage.Build:
                        if(member==members.Length){stage=Stage.Done;break;}
                        if(routes[member]!=null||joins[member]<0||exits[member]<0){member++;break;}
                        legCount=pointCount=0;segment=1;traceCursor=0;stage=Stage.Origin;break;
                    case Stage.Origin:
                    case Stage.Terminal:{
                        bool fromOrigin=stage==Stage.Origin;var request=members[member];
                        if(segment==BranchCount(fromOrigin)){if(fromOrigin){originCount=legCount;cursor=joins[member];stage=Stage.SpineLegs;}else{
                            if(groupPoints+pointCount>65536||groupLegs+legCount>65536)throw new InvalidOperationException("Recast group result capacity exceeded.");
                            publish=RecastPayload.Allocate(r,NavStorageLayout.ObjectBytesBound(typeof(RecastMemberRoute))+64+216L*legCount+56L*pointCount);RequireEdge(publish);publishedLegs=new NavTypedLeg[legCount];publishedPoints=new RecastRoutePoint[pointCount];copy=0;stage=Stage.PublishLegs;
                        }break;}
                        var a=BranchPoint(request,spine,segment-1,fromOrigin);var b=BranchPoint(request,spine,segment,fromOrigin);IReadOnlyList<NavTypedLeg> trace=null;
                        try{
                            left--;Work++;
#if RECAST_ATTACHMENT_PHASE_AUDIT
global::RecastAttachmentPhaseAudit.HelperCharge(this,(int)stage,1,origin);
#endif

                            if(!Trace(provider,request,a,b,r,out trace))throw new InvalidOperationException("Accepted Recast attachment edge changed before publication.");
                            while(traceCursor<trace.Count&&left>=2){var leg=trace[traceCursor++];Leg(new NavTypedLeg(new NavLocation(leg.From.Position,leg.From.SurfaceId),new NavLocation(leg.To.Position,leg.To.SurfaceId),leg.Cost,leg.Kind,leg.PortalId));left-=2;Work+=2;
#if RECAST_ATTACHMENT_PHASE_AUDIT
global::RecastAttachmentPhaseAudit.HelperCharge(this,(int)stage,2,origin);
#endif
}
                            if(traceCursor==trace.Count&&left>0){Point(b,geometry);left--;Work++;
#if RECAST_ATTACHMENT_PHASE_AUDIT
global::RecastAttachmentPhaseAudit.HelperCharge(this,(int)stage,1,origin);
#endif
segment++;traceCursor=0;}
                        }finally{(trace as NavOwnedStorage)?.ReleaseOwner();}break;
                    }
                    case Stage.SpineLegs:
                        if(cursor<exits[member])Leg(spine.Legs[cursor++]);else{stop=0;stage=Stage.SpinePoints;}break;
                    case Stage.SpinePoints:
                        if(stop<spine.Stops.Length){int node=spine.Stops[stop++];if(node>joins[member]&&node<=exits[member])Point(spine.Node(node),geometry);}
                        else{terminalIndex=legCount;segment=1;traceCursor=0;stage=Stage.Terminal;}break;
                    case Stage.PublishLegs:
                        if(copy<legCount){publishedLegs[copy]=legs[copy];legs[copy++]=null;}else{copy=0;stage=Stage.PublishPoints;}break;
                    case Stage.PublishPoints:
                        if(copy<pointCount){publishedPoints[copy]=points[copy];points[copy++]=default;break;}
                        var route=new RecastMemberRoute(publishedPoints,publishedLegs,spine,joins[member],exits[member],originCount,terminalIndex,true);publish.Value=route;results[member]=publish;publish=null;publishedLegs=null;publishedPoints=null;routes[member]=route;groupPoints+=pointCount;groupLegs+=legCount;member++;stage=Stage.Build;break;
                }
            }

#if RECAST_ATTACHMENT_PHASE_AUDIT
            global::RecastAttachmentPhaseAudit.HelperDone(this,Work);
#endif
            return true;
        }
        public void Dispose(){if(disposed)return;disposed=true;Release();}
        protected override void ReleaseEdges(){field?.Dispose();field=null;branch?.Dispose();publish?.Dispose();foreach(var p in origins)p?.Dispose();foreach(var p in terminals)p?.Dispose();foreach(var p in results)p?.Dispose();}
    }
}
