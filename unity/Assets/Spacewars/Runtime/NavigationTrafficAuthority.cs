using System;
using System.Collections.Generic;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    // One immutable session authority. Borrowed installed owners bind every queue
    // row to a concrete request/profile/HOLD provider; rows never own a second graph.
    internal sealed class NavigationTrafficAuthority:NavOwnedStorage,INavPhysicalBody
    {
        internal const int Width=8;
        internal readonly long NextOrdinal;
        internal readonly double Radius;
        internal NavRetainedArray<long> Values;
        internal NavRetainedArray<NavOwnedStorage> Executions;
        internal NavRetainedArray<NavPoint> Exits;
        internal int Count=>Executions.Count;
        internal long At(int row,int column)=>Values[row*Width+column];
        private static readonly long Layout=Math.Max(128,NavStorageLayout.ObjectBytesBound(typeof(NavigationTrafficAuthority)));
        private NavigationTrafficAuthority(long next,double radius):base(Layout){NextOrdinal=next;Radius=radius;}
        internal override bool IsImmutableCaptureRoot=>Values!=null&&Executions!=null&&Exits!=null;
        public NavPhysicalTag PhysicalTag=>new NavPhysicalTag(NavPhysicalStorageKind.TrafficAuthority);
        internal static NavigationTrafficAuthority Create(long next,double radius,long[] values,NavOwnedStorage[] owners,NavPoint[] exits,int count,INavStorageReservation reservation)
        {
            NavigationTrafficAuthority value;
            using(var allocation=new NavStorageAllocation(reservation.TryReserve)){allocation.Reserve(Layout);value=allocation.Register(new NavigationTrafficAuthority(next,radius));value.Retain();}
            try{value.Values=NavRetainedArray<long>.Copy(new ArraySegment<long>(values,0,count*Width),reservation.TryReserve);value.Executions=NavRetainedArray<NavOwnedStorage>.Copy(new ArraySegment<NavOwnedStorage>(owners,0,count),reservation.TryReserve);value.Exits=NavRetainedArray<NavPoint>.Copy(new ArraySegment<NavPoint>(exits,0,count),reservation.TryReserve);value.Validate();return value;}
            catch{value.Release();throw;}
        }
        private void Validate()
        {
            if(NextOrdinal<1||double.IsNaN(Radius)||double.IsInfinity(Radius)||Radius<=0||Count>LayeredNavigationScheduler.MaxRetainedBytes/(Width*8L+8+16)||Values.Count!=Count*Width||Exits.Count!=Count)throw new ArgumentException("Invalid Traffic authority dimensions.");
            for(int i=0;i<Count;i++){
                if(!(Executions[i] is NavigationInstalledExecutionOwner owner)||owner.Entity!=At(i,0)||owner.Request.Request.MemberIdentity==null||owner.Request.Request.MemberIdentity.Incarnation!=At(i,1)||owner.Request.Request.MemberIdentity.OrderRevision!=At(i,2)||owner.Request.Request.MemberIdentity.MobilityRevision!=At(i,3)||owner.Request.Request.MemberIdentity.GroupId!=At(i,4)||At(i,2)<0||At(i,3)<0||At(i,4)<1||At(i,5)<1||At(i,5)>=NextOrdinal||At(i,6)<0||At(i,6)>At(i,5)||At(i,6)>=NextOrdinal||(At(i,7)!=1&&At(i,7)!=-1)||double.IsNaN(Exits[i].X)||double.IsNaN(Exits[i].Z)||double.IsInfinity(Exits[i].X)||double.IsInfinity(Exits[i].Z))throw new ArgumentException("Traffic member/request/ordinal contradiction.");
                if(owner.Corridor.Recipe.Clearance.Radius>Radius)throw new ArgumentException("Traffic footprint is smaller than its bound member.");
                for(int j=0;j<i;j++){
                    if(At(i,0)==At(j,0)||At(i,5)==At(j,5))throw new ArgumentException("Duplicate Traffic member/arrival ordinal.");
                    if(At(i,6)>0&&At(i,6)==At(j,6)&&(At(i,4)!=At(j,4)||At(i,7)!=At(j,7)))throw new ArgumentException("Traffic grant crosses group/direction identity.");
                }
            }
        }
        internal int Find(int entity,long incarnation){for(int i=0;i<Count;i++)if(At(i,0)==entity&&At(i,1)==incarnation)return i;return -1;}
        public void WritePhysicalBody(NavPhysicalBodyWriter writer)
        {Validate();if(!IsImmutableCaptureRoot||StorageBytes!=NavStorageLayout.Charge(Layout))throw new ArgumentException("Unsealed Traffic authority.");writer.Output.Write(NextOrdinal);writer.Output.Write(Radius);NavPhysicalMacro.WriteEdge(writer,this,Values);NavPhysicalMacro.WriteEdge(writer,this,Executions);NavPhysicalMacro.WriteEdge(writer,this,Exits);}
        internal static NavigationTrafficAuthority ReadPhysicalBody(NavPhysicalBodyReader reader)
        {
            long next=reader.Input.ReadInt64();double radius=reader.Input.ReadDouble();var values=NavPhysicalMacro.ReadEdge<NavRetainedArray<long>>(reader);var owners=NavPhysicalMacro.ReadEdge<NavRetainedArray<NavOwnedStorage>>(reader);var exits=NavPhysicalMacro.ReadEdge<NavRetainedArray<NavPoint>>(reader);
            using(var allocation=new NavStorageAllocation(reader.Reservation.TryReserve)){allocation.Reserve(Layout);var value=allocation.Register(new NavigationTrafficAuthority(next,radius));value.Values=NavPhysicalMacro.Acquire(value,values);value.Executions=NavPhysicalMacro.Acquire(value,owners);value.Exits=NavPhysicalMacro.Acquire(value,exits);value.Validate();return value;}
        }
        protected override void ReleaseEdges(){Values?.Release();Executions?.Release();Exits?.Release();}
        internal static NavigationTrafficAuthority Prepare(NavigationTrafficAuthority source,NavPreparedCrowdStep frame,NavigationInstalledExecutionOwner[] installed,bool[] detached,IDictionary<int,long> members,IPlayableTerrain terrain,INavStorageReservation reservation)
        {
            int capacity=frame.After.Length,obstacles=0;long probeScratch=0;double radius=source?.Radius??0;NavClearanceProfile profile=null;
            for(int i=0;i<capacity;i++)if(installed[i]!=null&&frame.After[i].Moving&&!frame.After[i].Held&&!detached[i]&&members.ContainsKey(frame.After[i].Id)){
                if(installed[i].Corridor.ProviderOwner.Provider is PartitionNavigationProvider partition)probeScratch=Math.Max(probeScratch,partition.TraceScratchBound);var clear=installed[i].Corridor.Recipe.Clearance;if(profile==null||clear.Radius>profile.Radius)profile=clear;radius=Math.Max(radius,clear.Radius);obstacles=Math.Max(obstacles,installed[i].Request.Request.Geometry.Obstacles.Count);
            }
            if(profile==null){if(source==null)return null;if(source.Count==0){source.Retain();return source;}return Create(source.NextOrdinal,source.Radius,Array.Empty<long>(),Array.Empty<NavOwnedStorage>(),Array.Empty<NavPoint>(),0,reservation);}
            long scratch=checked(probeScratch+10*32L+capacity*(Width*8L+8+16+NavStorageLayout.ValueBytes(typeof(NavigationTrafficStrip))+4)+2*8L*obstacles+NavStorageLayout.ObjectBytesBound(typeof(NavClearanceProfile)));
            using(NavStorageScratch.Reserve(reservation,scratch)){
                var rows=new long[capacity*Width];var owners=new NavOwnedStorage[capacity];var exits=new NavPoint[capacity];var strips=new NavigationTrafficStrip[capacity];var actorRows=new int[capacity];var left=new double[obstacles];var right=new double[obstacles];
                var clearance=new NavClearanceProfile(profile.Id,profile.Revision,profile.MobilityClass,profile.MobilityRevision,radius);int count=0;long ordinal=source?.NextOrdinal??1;
                for(int i=0;i<capacity;i++){
                    var actor=frame.After[i];var owner=installed[i];if(owner==null||!actor.Moving||actor.Held||detached[i]||!members.TryGetValue(actor.Id,out long group)||owner.Request.Request.MemberIdentity==null||owner.Corridor.Recipe.Clearance.MobilityClass!=profile.MobilityClass||!NavigationTrafficStrip.TryCreate(owner,terrain,clearance,left,right,out var strip)||strip.Cleared(actor))continue;
                    var identity=owner.Request.Request.MemberIdentity;if(identity.Incarnation!=actor.Incarnation||identity.MobilityRevision!=actor.MobilityRevision||identity.GroupId!=group)continue;
                    int old=source?.Find(actor.Id,actor.Incarnation)??-1;long age=0,grant=0;
                    if(old>=0&&source.At(old,2)==identity.OrderRevision&&source.At(old,4)==group&&source.At(old,7)==strip.Direction&&MatchesPassage((NavigationInstalledExecutionOwner)source.Executions[old],strip)){age=source.At(old,5);var previous=(NavigationInstalledExecutionOwner)source.Executions[old];if(source.At(old,3)==actor.MobilityRevision&&previous.Corridor.ProviderOwner.Provider.Binding==owner.Corridor.ProviderOwner.Provider.Binding)grant=source.At(old,6);}
                    if(age==0){age=ordinal;ordinal=checked(ordinal+1);}
                    int at=count*Width;rows[at]=actor.Id;rows[at+1]=actor.Incarnation;rows[at+2]=identity.OrderRevision;rows[at+3]=actor.MobilityRevision;rows[at+4]=group;rows[at+5]=age;rows[at+6]=grant;rows[at+7]=strip.Direction;owners[count]=owner;exits[count]=grant>0?source.Exits[old]:strip.Downstream(0);strips[count]=strip;actorRows[count++]=i;
                }
                for(int first=0;first<count;first++){
                    bool handled=false;for(int prior=0;prior<first;prior++)if(strips[first].SamePassage(strips[prior])){handled=true;break;}if(handled)continue;
                    long active=0;int winner=-1;for(int i=first;i<count;i++)if(strips[first].SamePassage(strips[i])&&rows[i*Width+6]>0&&(active==0||rows[i*Width+6]<active)){active=rows[i*Width+6];winner=i;}
                    if(active==0){
                        // A token cannot erase an actual footprint, including a cancelled member.
                        for(int i=first;i<count;i++)if(strips[first].SamePassage(strips[i])&&(winner<0||rows[i*Width+5]<rows[winner*Width+5]))winner=i;
                        bool blocked=false;for(int actor=0;actor<capacity;actor++)if(strips[first].Occupies(frame.After[actor])){
                            int match=-1;for(int i=first;i<count;i++)if(actorRows[i]==actor&&strips[first].SamePassage(strips[i])){match=i;break;}
                            if(match<0){blocked=true;break;}if(winner<0||!strips[first].Occupies(frame.After[actorRows[winner]]))winner=match;
                            else if(rows[match*Width+4]!=rows[winner*Width+4]||strips[match].Direction!=strips[winner].Direction){blocked=true;break;}
                        }
                        if(!blocked&&winner>=0){
                            int seat=0;bool safe=true;for(int i=first;i<count;i++)if(strips[first].SamePassage(strips[i])&&rows[i*Width+4]==rows[winner*Width+4]&&strips[i].Direction==strips[winner].Direction){
                                var owner=(NavigationInstalledExecutionOwner)owners[i];var exit=strips[i].Downstream(seat++);if(!strips[i].ExitSeatSafe((PartitionNavigationProvider)owner.Corridor.ProviderOwner.Provider,owner.Request.Request.Geometry,terrain,clearance,seat-1)){safe=false;break;}
                                for(int actor=0;actor<capacity;actor++)if(frame.After[actor].Id!=(int)rows[i*Width]&&DistanceSquared(exit,frame.After[actor].Position)<Math.Pow(radius+frame.After[actor].Radius,2)){safe=false;break;}exits[i]=exit;
                            }
                            if(safe){active=rows[winner*Width+5];for(int i=first;i<count;i++)if(strips[first].SamePassage(strips[i])&&rows[i*Width+4]==rows[winner*Width+4]&&strips[i].Direction==strips[winner].Direction)rows[i*Width+6]=active;}
                        }
                    }
                    for(int i=first;i<count;i++)if(strips[first].SamePassage(strips[i])&&rows[i*Width+6]!=active||strips[first].SamePassage(strips[i])&&active==0){int row=actorRows[i];frame.Preferred[row]=double.IsNaN(frame.Preferred[row])?0:Math.Min(frame.Preferred[row],0);}
                }
                return count==0&&source==null?null:Create(ordinal,radius,rows,owners,exits,count,reservation);
            }
        }
        private static bool MatchesPassage(NavigationInstalledExecutionOwner owner,NavigationTrafficStrip strip)
        {
            int count=0;foreach(var leg in owner.Corridor.TypedRoute.Legs)if(leg.Kind==NavTypedLegKind.Portal){if(leg.PortalId!=strip.First.Id&&leg.PortalId!=strip.Second.Id)return false;count++;}return count==2;
        }
        private static double DistanceSquared(NavPoint a,NavPoint b){double x=a.X-b.X,z=a.Z-b.Z;return x*x+z*z;}
    }
    public sealed partial class NavigationSession
    {
        private NavigationTrafficAuthority trafficAuthority;
        private bool HasTraffic=>trafficAuthority?.Count>0;
        private bool? TryDispatchPreparedTraffic(double dt)
        {
            LastTrafficDispatchStage=1;if(terrain==null&&!HasTraffic)return null;
            LastTrafficDispatchStage=2;if(!TryPrepareAnswerNamesBatch(out var names,false,true))return false;
            using(names){
                var required=RequiresTrafficFrame(names);if(!required.HasValue){LastTrafficDispatchStage=3;return false;}if(!required.Value){LastTrafficDispatchStage=4;return null;}
                // Ready-arrival PLAN publication emits work for a later Host.Service;
                // it cannot be projected as an answer from that future service stage.
                // End this maintenance dispatch before any corridor install or motion.
                LastTrafficDispatchStage=5;if(PrepareOwnedReadyArrivals())return false;
                LastTrafficDispatchStage=6;return TryPublishOwnedResultBatch(names,dt,true,requireTrafficFrame:true)??false;
            }
        }
        private bool? RequiresTrafficFrame(NavigationPreparedAnswerNames names)
        {
            NavigationInstalledExecutions pins;NavigationHostCorridor profilePin=null;long operation,publication;int obstacles=0;long probe=0;NavClearanceProfile profile=null;double radius=0;
            using(EnterTransport()){
                if(!AnswerNamesSourceCurrent(names))return null;if(HasTraffic)return true;if(terrain==null)return false;
                pins=installedExecutions;pins.Retain();operation=authorityOperationIdentity;publication=admissionPublicationIdentity;
                void Measure(NavigationHostCorridor corridor){if(corridor==null||!(corridor.ProviderOwner.Provider is PartitionNavigationProvider provider))return;var clear=corridor.Recipe.Clearance;if(profile==null||clear.Radius>radius){profilePin?.Release();profilePin=corridor;profilePin.Retain();profile=clear;radius=clear.Radius;}obstacles=Math.Max(obstacles,corridor.RequestOwner.Request.Geometry.Obstacles.Count);probe=Math.Max(probe,provider.TraceScratchBound);}
                foreach(var actor in Crowd.Units)if(actor.Moving&&!actor.Held&&!pins.IsDetached(actor.Id))Measure(pins.OwnerFor(actor.Id)?.Corridor);
                foreach(var answer in names.Answers)if(answer.Status==NavSolveStatus.Ready)Measure(answer.StorageOwner?.CorridorOwner);
            }
            try{
                if(profile==null)return false;
                if(!physicalHostBudget.TryOpenScope(NavigationPreparationKind.Projection,Generation,publication,out var scope))return null;
                using(scope){
                    if(!NavStorageScratch.TryReserve(scope.OwnedReservation(),checked(probe+2*32L+16L*obstacles),out var scratch))return null;
                    using(scratch){
                        var left=new double[obstacles];var right=new double[obstacles];
                        using(EnterTransport()){
                            if(operation!=authorityOperationIdentity||publication!=admissionPublicationIdentity||!ReferenceEquals(pins,installedExecutions)||!AnswerNamesSourceCurrent(names))return null;
                            foreach(var actor in Crowd.Units)if(actor.Moving&&!actor.Held&&!pins.IsDetached(actor.Id)&&NavigationTrafficStrip.TryCreate(pins.OwnerFor(actor.Id)?.Corridor,terrain,profile,left,right,out _))return true;
                            foreach(var answer in names.Answers)if(answer.Status==NavSolveStatus.Ready&&NavigationTrafficStrip.TryCreate(answer.StorageOwner?.CorridorOwner,terrain,profile,left,right,out _))return true;
                            return false;
                        }
                    }
                }
            }finally{profilePin?.Release();pins.Release();}
        }
        private void ValidateRestoredTraffic(NavigationTrafficAuthority source)
        {
            if(source==null)return;int obstacles=0;long probe=0;
            for(int i=0;i<source.Count;i++){var owner=(NavigationInstalledExecutionOwner)source.Executions[i];obstacles=Math.Max(obstacles,owner.Request.Request.Geometry.Obstacles.Count);if(owner.Corridor.ProviderOwner.Provider is PartitionNavigationProvider provider)probe=Math.Max(probe,provider.TraceScratchBound);}
            if(!physicalHostBudget.TryOpenScope(NavigationPreparationKind.Decode,Generation,0,out var scope))throw new InvalidOperationException("Traffic restore validation admission refused.");
            using(scope)using(NavStorageScratch.Reserve(scope.OwnedReservation(),checked(probe+2*32L+16L*obstacles+NavStorageLayout.ObjectBytesBound(typeof(NavClearanceProfile))))){
                NavStorageAdmission.RequireOwned(scope.OwnedReservation(),source);var left=new double[obstacles];var right=new double[obstacles];
                for(int i=0;i<source.Count;i++){
                    var owner=(NavigationInstalledExecutionOwner)source.Executions[i];var original=owner.Corridor.Recipe.Clearance;
                    var clearance=new NavClearanceProfile(original.Id,original.Revision,original.MobilityClass,original.MobilityRevision,source.Radius);
                    if(original.Radius>source.Radius||!NavigationTrafficStrip.TryCreate(owner,terrain,clearance,left,right,out var strip)||strip.Direction!=source.At(i,7))throw new ArgumentException("Restored Traffic passage/profile differs from installed proof.");
                    var exit=source.Exits[i];var provider=(PartitionNavigationProvider)owner.Corridor.ProviderOwner.Provider;
                    if(!owner.Request.Request.Geometry.SegmentFree(strip.Wait,exit,source.Radius)||!terrain.SupportsSweep(strip.Wait,exit,source.Radius)||!provider.TryLocate(strip.Wait,clearance,out var wait)||!provider.TryTraverse(wait,exit,clearance,out _))throw new ArgumentException("Restored Traffic downstream proof differs.");
                    if(Crowd.TryGet(owner.Entity,out var actor)&&actor.Incarnation==source.At(i,1)&&actor.Moving&&!actor.Held&&actor.MobilityRevision==source.At(i,3)&&actor.InstalledRequest==owner.Request.Request.Request&&!ReferenceEquals(installedExecutions.OwnerFor(owner.Entity),owner))throw new ArgumentException("Restored Traffic lost its exact installed-owner alias.");
                }
            }
        }
    }

}
