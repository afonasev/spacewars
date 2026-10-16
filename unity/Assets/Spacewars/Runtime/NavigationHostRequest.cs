using System;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    // Runtime-only owning edges: one outstanding transfer, removed on consumption.
    // The links live in the admitted request header, so handoff needs no growth.
    internal sealed class NavigationTransportOwner : NavOwnedStorage
    {
        private static readonly long Layout=NavStorageLayout.ObjectBytesBound(typeof(NavigationTransportOwner));
        private NavigationHostRequest first;
        internal override bool IsImmutableCaptureRoot=>false;
        private NavigationTransportOwner():base(Layout){Retain();}
        internal static NavigationTransportOwner CreateOwned(INavStorageReservation reservation)
        {using(var allocation=new NavStorageAllocation(reservation.TryReserve)){allocation.Reserve(Layout);return allocation.Register(new NavigationTransportOwner());}}
        internal void Validate(NavigationRequest request)
        {if(request?.StorageOwner is NavigationHostRequest owner){RequireEdge(owner);if(owner.TransportOwner!=null&&!ReferenceEquals(owner.TransportOwner,this))throw new ArgumentException("Foreign transport owner.");}}
        private NavigationHostRequest Acquire(NavigationRequest request)
        {
            var owner=request?.StorageOwner as NavigationHostRequest;if(owner==null)return null;
            Validate(request);
            if(owner.TransportOwner==null){owner.Retain();owner.TransportOwner=this;owner.TransportNext=first;if(first!=null)first.TransportPrevious=owner;first=owner;}
            return owner;
        }
        internal void ValidateAnswer(NavigationAnswer answer)=>Validate(answer?.Request);
        internal void AcceptAnswer(NavigationAnswer answer)=>Answer(answer?.Request);
        internal void Transfer(NavigationRequest request){var owner=Acquire(request);if(owner!=null)owner.TransportExternal=true;}
        internal bool CanTransferPrepared(NavigationHostRequest owner,bool newEdge)
            =>owner!=null&&(newEdge?owner.TransportOwner==null:ReferenceEquals(owner.TransportOwner,this))&&owner.TransportAnswers==0;
        // Caller holds a prepaid destination edge when newEdge is true.
        internal void TransferPreparedNoThrow(NavigationHostRequest owner,bool newEdge)
        {if(newEdge){owner.TransportOwner=this;owner.TransportPrevious=null;owner.TransportNext=first;if(first!=null)first.TransportPrevious=owner;first=owner;}owner.TransportExternal=true;}
        internal void Answer(NavigationRequest request){var owner=Acquire(request);if(owner!=null){owner.TransportAnswers++;owner.TransportExternal=false;}}
        internal bool CanAnswerPrepared(NavigationHostRequest owner,bool newEdge)=>CanTransferPrepared(owner,newEdge);
        internal void AnswerPreparedNoThrow(NavigationHostRequest owner,bool newEdge){if(newEdge){owner.TransportOwner=this;owner.TransportPrevious=null;owner.TransportNext=first;if(first!=null)first.TransportPrevious=owner;first=owner;}owner.TransportAnswers=1;owner.TransportExternal=false;}
        // Stale ingress is one answer per queued head, including repeated
        // aliases of a request whose earlier answer is still unread.
        internal bool CanAppendAnswerPrepared(NavigationHostRequest owner,bool newEdge)
            =>owner!=null&&(newEdge?owner.TransportOwner==null&&owner.TransportAnswers==0&&!owner.TransportExternal:ReferenceEquals(owner.TransportOwner,this))&&owner.TransportAnswers<int.MaxValue;
        internal void AppendAnswerPreparedNoThrow(NavigationHostRequest owner,bool newEdge)
        {if(newEdge){owner.TransportOwner=this;owner.TransportPrevious=null;owner.TransportNext=first;if(first!=null)first.TransportPrevious=owner;first=owner;}owner.TransportAnswers++;owner.TransportExternal=false;}
        internal void RestoreAnswerCount(NavigationRequest request,int count,bool external)
        {
            if(count<1)throw new ArgumentException("Invalid restored answer multiplicity.");
            var owner=Acquire(request);if(owner==null||owner.TransportAnswers>count)throw new ArgumentException("Restored transport answers exceed tracking catalog.");
            owner.TransportAnswers=count;owner.TransportExternal=external;
        }
        internal void Consume(NavigationRequest request)
        {
            var owner=request?.StorageOwner as NavigationHostRequest;
            if(owner==null||!ReferenceEquals(owner.TransportOwner,this)||owner.TransportAnswers==0)return;
            owner.TransportAnswers--;if(owner.TransportAnswers==0&&!owner.TransportExternal)Remove(owner);
        }
        internal bool CanReplayExternalPrepared(NavigationRequest request)
            =>request?.StorageOwner==null||ReferenceEquals(request.StorageOwner.TransportOwner,this)&&request.StorageOwner.TransportExternal&&request.StorageOwner.TransportAnswers==0;
        internal NavigationHostRequest ReplayExternalPreparedNoThrow(NavigationRequest request)
            =>request.StorageOwner==null?null:ConsumePreparedNoThrow(request);
        internal bool CanConsumeCountedPrepared(NavigationRequest request,int count,bool external)
            =>request?.StorageOwner is NavigationHostRequest owner&&ReferenceEquals(owner.TransportOwner,this)&&count>0&&owner.TransportAnswers==count&&owner.TransportExternal==external;
        internal bool CanConsumePrepared(NavigationRequest request)=>request?.StorageOwner is NavigationHostRequest owner&&ReferenceEquals(owner.TransportOwner,this)&&owner.TransportAnswers==1&&!owner.TransportExternal;
        // The caller has frozen exact per-request multiplicity/external state
        // and validated the whole drain count before the first mutation.
        internal NavigationHostRequest ConsumeCountedPreparedNoThrow(NavigationRequest request)
        {
            var owner=request.StorageOwner;owner.TransportAnswers--;
            if(owner.TransportAnswers!=0||owner.TransportExternal)return null;
            if(owner.TransportPrevious!=null)owner.TransportPrevious.TransportNext=owner.TransportNext;else first=owner.TransportNext;
            if(owner.TransportNext!=null)owner.TransportNext.TransportPrevious=owner.TransportPrevious;
            owner.TransportOwner=null;owner.TransportPrevious=owner.TransportNext=null;return owner;
        }
        internal NavigationHostRequest ConsumePreparedNoThrow(NavigationRequest request)
        {
            var owner=request.StorageOwner;owner.TransportAnswers=0;
            if(owner.TransportPrevious!=null)owner.TransportPrevious.TransportNext=owner.TransportNext;else first=owner.TransportNext;
            if(owner.TransportNext!=null)owner.TransportNext.TransportPrevious=owner.TransportPrevious;
            owner.TransportOwner=null;owner.TransportPrevious=owner.TransportNext=null;owner.TransportExternal=false;return owner;
        }
        private void Remove(NavigationHostRequest owner)
        {
            if(owner.TransportPrevious!=null)owner.TransportPrevious.TransportNext=owner.TransportNext;else first=owner.TransportNext;
            if(owner.TransportNext!=null)owner.TransportNext.TransportPrevious=owner.TransportPrevious;
            owner.TransportOwner=null;owner.TransportPrevious=owner.TransportNext=null;owner.TransportExternal=false;owner.TransportAnswers=0;owner.Release();
        }
        internal override void ReleaseOwner()=>Release();
        protected override void ReleaseEdges(){while(first!=null)Remove(first);}
    }
    // The request facade and its profile, geometry, and names have one
    // physical owner which host rows and answers can reference directly.
    internal sealed class NavigationHostRequest : NavOwnedStorage, INavPhysicalBody
    {
        private static readonly long LayoutBase=NavStorageLayout.ObjectBytesBound(typeof(NavigationHostRequest))+
            NavStorageLayout.ObjectBytesBound(typeof(NavigationRequest));
        private static long LayoutBytes(bool member)=>Math.Max(256,LayoutBase+(member?NavStorageLayout.ObjectBytesBound(typeof(NavigationMemberIdentity)):0));
        private NavDeclarationStorage profile;
        private NavGeometry.OwnedBuffers geometry,baseGeometry;
        private NavOwnedText held,certified;
        internal NavOwnedText HeldOwner=>held;
        private NavNameReference startName,goalName;
        internal NavigationRequest Request {get;private set;}
        private bool released;
        internal NavigationTransportOwner TransportOwner;
        internal NavigationHostRequest TransportPrevious,TransportNext;
        internal int TransportAnswers;
        internal bool TransportExternal;
        internal override bool IsImmutableCaptureRoot=>Request!=null;
        public NavPhysicalTag PhysicalTag=>new NavPhysicalTag(NavPhysicalStorageKind.HostRequest);
        private NavigationHostRequest(bool ownerReference,bool member):base(LayoutBytes(member))
        {if(ownerReference)Retain();}
        private static NavigationHostRequest Allocate(INavStorageReservation reservation,bool ownerReference,bool member)
        {
            using(var allocation=new NavStorageAllocationContext(reservation.TryReserve)){
                allocation.Reserve(LayoutBytes(member));var value=new NavigationHostRequest(ownerReference,member);allocation.Register(value);return value;
            }
        }

        internal NavOwnedText HeldIdentityOwner=>held;
        private void Own(NavDeclarationStorage profileRoot,NavGeometry.OwnedBuffers geometryRoot,NavGeometry.OwnedBuffers baseRoot,
            NavOwnedText heldRoot,NavOwnedText certifiedRoot,NavNameReference start,NavNameReference goal)
        {
            profile=NavPhysicalMacro.Acquire(this,profileRoot);geometry=NavPhysicalMacro.Acquire(this,geometryRoot);
            baseGeometry=NavPhysicalMacro.Acquire(this,baseRoot);held=NavPhysicalMacro.Acquire(this,heldRoot);
            certified=NavPhysicalMacro.Acquire(this,certifiedRoot);
            if(start.Owner!=null){start.Validate(start.Resolve(),this);start.Owner.Retain();}
            if(goal.Owner!=null){goal.Validate(goal.Resolve(),this);goal.Owner.Retain();}
            startName=start;goalName=goal;
        }
        private static NavigationMemberIdentity Member(NavigationMemberIdentity value)
            =>value==null?null:new NavigationMemberIdentity(value.Incarnation,value.MobilityRevision,value.GroupId,value.OrderRevision,value.MembershipRevision);
        private static NavLocation? Location(NavLocation? source,NavNameReference name)
            =>source.HasValue?new NavLocation(source.Value.Position,name):(NavLocation?)null;
        internal static NavNameReference OwnLocationName(NavLocation? source,INavStorageReservation reservation,out NavOwnedText privateText)
        {
            privateText=null;if(!source.HasValue)return default;
            var named=source.Value.NameReference;
            if(named.Owner!=null){NavStorageAdmission.RequireOwned(reservation,named.Owner);named.Validate(source.Value.SurfaceId,named.Owner);return named;}
            privateText=NavOwnedText.Copy(source.Value.SurfaceId,reservation);
            return new NavNameReference(privateText,NavNameRole.Text);
        }
        internal static NavigationHostRequest CopyOwned(NavigationRequest source,INavStorageReservation reservation)
            =>CopyOwned(source,source?.CertifiedProviderBinding,reservation);
        internal static NavigationHostRequest CopyOwned(NavigationRequest source,string certifiedProviderBinding,INavStorageReservation reservation,NavGeometry authorityBase=null)
        {
            if(source==null)throw new ArgumentNullException(nameof(source));
            return CreateOwned(source.Session,source.Request,source.Entity,source.Order,source.Profile,source.Geometry,
                source.Start,source.Goal,source.BaseGeometry,source.HeldIdentity,source.MemberIdentity,
                source.StartLocation,source.GoalLocation,certifiedProviderBinding,reservation,authorityBase);
        }
        internal static NavigationHostRequest CreateOwned(long session,long request,int entity,long order,
            NavigationProfile profile,NavGeometry geometry,NavPoint origin,NavPoint target,NavGeometry baseGeometry,
            string heldIdentity,NavigationMemberIdentity memberIdentity,NavLocation? startLocation,NavLocation? goalLocation,
            string certifiedProviderBinding,INavStorageReservation reservation,NavGeometry authorityBase=null)
        {
            if(reservation==null||profile==null||geometry==null||baseGeometry==null)
                throw new ArgumentNullException();
            if(profile.StorageRoot!=null)NavStorageAdmission.RequireOwned(reservation,profile.StorageRoot);
            if(geometry.StorageRoot!=null)NavStorageAdmission.RequireOwned(reservation,geometry.StorageRoot);
            if(baseGeometry.StorageRoot!=null)NavStorageAdmission.RequireOwned(reservation,baseGeometry.StorageRoot);
            if(startLocation?.NameReference.Owner!=null)
                NavStorageAdmission.RequireOwned(reservation,startLocation.Value.NameReference.Owner);
            if(goalLocation?.NameReference.Owner!=null)
                NavStorageAdmission.RequireOwned(reservation,goalLocation.Value.NameReference.Owner);
            if(authorityBase!=null)NavStorageAdmission.RequireOwned(reservation,authorityBase.StorageRoot);
            var owner=Allocate(reservation,true,memberIdentity!=null);
            NavigationProfile copiedProfile=null;NavGeometry copiedGeometry=null,copiedBase=null;
            NavOwnedText held=null,certified=null,startPrivate=null,goalPrivate=null;
            bool newProfile=false,newGeometry=false,newBase=false;
            try{
                copiedProfile=profile.StorageRoot==null?NavigationProfile.CopyOwned(profile,reservation):profile;
                newProfile=!ReferenceEquals(copiedProfile,profile);
                NavStorageAdmission.RequireOwned(reservation,copiedProfile.StorageRoot);
                copiedGeometry=authorityBase!=null&&ReferenceEquals(geometry,baseGeometry)?authorityBase:geometry.StorageRoot==null?NavGeometry.CopyOwned(geometry,reservation):geometry;
                newGeometry=!ReferenceEquals(copiedGeometry,geometry)&&!ReferenceEquals(copiedGeometry,authorityBase);
                NavStorageAdmission.RequireOwned(reservation,copiedGeometry.StorageRoot);
                copiedBase=authorityBase??(ReferenceEquals(baseGeometry,geometry)?copiedGeometry:
                    baseGeometry.StorageRoot==null?NavGeometry.CopyOwned(baseGeometry,reservation):baseGeometry);
                newBase=!ReferenceEquals(copiedBase,baseGeometry)&&!ReferenceEquals(copiedBase,copiedGeometry)&&!ReferenceEquals(copiedBase,authorityBase);
                NavStorageAdmission.RequireOwned(reservation,copiedBase.StorageRoot);
                if(heldIdentity!=null)held=NavOwnedText.Copy(heldIdentity,reservation);
                if(certifiedProviderBinding!=null)certified=NavOwnedText.Copy(certifiedProviderBinding,reservation);
                var start=OwnLocationName(startLocation,reservation,out startPrivate);
                var goal=startPrivate!=null&&goalLocation.HasValue&&
                    ReferenceEquals(startLocation.Value.SurfaceId,goalLocation.Value.SurfaceId)
                    ?start:OwnLocationName(goalLocation,reservation,out goalPrivate);
                owner.Own((NavDeclarationStorage)copiedProfile.StorageRoot,(NavGeometry.OwnedBuffers)copiedGeometry.StorageRoot,
                    (NavGeometry.OwnedBuffers)copiedBase.StorageRoot,held,certified,start,goal);
                owner.Request=new NavigationRequest(session,request,entity,order,copiedProfile,copiedGeometry,
                    origin,target,copiedBase,held?.Value,Member(memberIdentity),Location(startLocation,start),Location(goalLocation,goal));
                owner.Request.CertifiedProviderBinding=certified?.Value;owner.Request.BindStorageOwner(owner);
                return owner;
            }catch{owner.ReleaseStorage();throw;}
            finally{
                if(newProfile)copiedProfile.StorageRoot.Release();
                if(newGeometry)copiedGeometry.StorageRoot.Release();
                if(newBase)copiedBase.StorageRoot.Release();
                held?.Release();certified?.Release();startPrivate?.Release();goalPrivate?.Release();
            }
        }
        public void WritePhysicalBody(NavPhysicalBodyWriter writer)
        {
            if(!ReferenceEquals(Request?.StorageOwner,this))throw new ArgumentException("Request facade owner identity contradiction.");
            var r=Request;
            if(r==null||!ReferenceEquals(profile,r.Profile.StorageRoot)||!ReferenceEquals(geometry,r.Geometry.StorageRoot)||
                !ReferenceEquals(baseGeometry,r.BaseGeometry.StorageRoot)||!ReferenceEquals(r.HeldIdentity,held?.Value)||
                !ReferenceEquals(r.CertifiedProviderBinding,certified?.Value)||
                r.StartLocation.HasValue!=(startName.Owner!=null)||r.GoalLocation.HasValue!=(goalName.Owner!=null)||
                r.StartLocation.HasValue&&(!ReferenceEquals(r.StartLocation.Value.SurfaceId,startName.Resolve())||
                    !ReferenceEquals(r.StartLocation.Value.NameReference.Owner,startName.Owner))||
                r.GoalLocation.HasValue&&(!ReferenceEquals(r.GoalLocation.Value.SurfaceId,goalName.Resolve())||
                    !ReferenceEquals(r.GoalLocation.Value.NameReference.Owner,goalName.Owner))||
                StorageBytes!=NavStorageLayout.Charge(LayoutBytes(r.MemberIdentity!=null)))throw new ArgumentException("Physical host request owner contradiction.");
            NavPhysicalMacro.WriteEdge(writer,this,profile);NavPhysicalMacro.WriteEdge(writer,this,geometry);
            NavPhysicalMacro.WriteEdge(writer,this,baseGeometry);NavPhysicalMacro.WriteEdge(writer,this,held,true);
            NavPhysicalMacro.WriteEdge(writer,this,certified,true);
            if(startName.Owner!=null)startName.Validate(startName.Resolve(),this);startName.Write(writer);
            if(goalName.Owner!=null)goalName.Validate(goalName.Resolve(),this);goalName.Write(writer);
            writer.Output.Write(r.Session);writer.Output.Write(r.Request);writer.Output.Write(r.Entity);writer.Output.Write(r.Order);
            NavContract.Point(writer.Output,r.Start);NavContract.Point(writer.Output,r.Goal);
            if(startName.Owner!=null)NavContract.Point(writer.Output,r.StartLocation.Value.Position);
            if(goalName.Owner!=null)NavContract.Point(writer.Output,r.GoalLocation.Value.Position);
            writer.Output.Write(r.MemberIdentity!=null);
            if(r.MemberIdentity!=null){var m=r.MemberIdentity;writer.Output.Write(m.Incarnation);writer.Output.Write(m.MobilityRevision);
                writer.Output.Write(m.GroupId);writer.Output.Write(m.OrderRevision);writer.Output.Write(m.MembershipRevision);}
        }
        internal static NavigationHostRequest ReadPhysicalBody(NavPhysicalBodyReader reader)
        {
            var profile=NavPhysicalMacro.ReadEdge<NavDeclarationStorage>(reader);
            var geometry=NavPhysicalMacro.ReadEdge<NavGeometry.OwnedBuffers>(reader);
            var baseGeometry=NavPhysicalMacro.ReadEdge<NavGeometry.OwnedBuffers>(reader);
            var held=NavPhysicalMacro.ReadEdge<NavOwnedText>(reader,true);
            var certified=NavPhysicalMacro.ReadEdge<NavOwnedText>(reader,true);
            var startName=NavNameReference.Read(reader);
            var goalName=NavNameReference.Read(reader);
            long session=reader.Input.ReadInt64(),request=reader.Input.ReadInt64();int entity=reader.Input.ReadInt32();long order=reader.Input.ReadInt64();
            var start=NavPhysicalGeometryValue.Point(reader);var goal=NavPhysicalGeometryValue.Point(reader);
            var startLocation=startName.Owner==null?(NavLocation?)null:new NavLocation(NavPhysicalGeometryValue.Point(reader),startName);
            var goalLocation=goalName.Owner==null?(NavLocation?)null:new NavLocation(NavPhysicalGeometryValue.Point(reader),goalName);
            bool hasMember=NavPhysicalMacro.ReadBoolean(reader);
            long incarnation=0,mobility=0,group=0,revision=0,membership=0;
            if(hasMember){incarnation=reader.Input.ReadInt64();mobility=reader.Input.ReadInt64();group=reader.Input.ReadInt64();revision=reader.Input.ReadInt64();membership=reader.Input.ReadInt64();}
            if(!(profile.Facade is NavigationProfile p)||geometry.Facade==null||baseGeometry.Facade==null)
                throw new ArgumentException("Invalid physical host request fields.");
            var owner=Allocate(reader.Reservation,false,hasMember);
            var member=hasMember?new NavigationMemberIdentity(incarnation,mobility,group,revision,membership):null;
            owner.Own(profile,geometry,baseGeometry,held,certified,startName,goalName);
            owner.Request=new NavigationRequest(session,request,entity,order,p,geometry.Facade,start,goal,baseGeometry.Facade,held?.Value,member,startLocation,goalLocation);
            owner.Request.CertifiedProviderBinding=certified?.Value;owner.Request.BindStorageOwner(owner);return owner;
        }
        internal void ReleaseStorage(){if(released)return;released=true;Release();}
        internal override void ReleaseOwner()=>ReleaseStorage();
        protected override void ReleaseEdges(){profile?.Release();geometry?.Release();baseGeometry?.Release();held?.Release();certified?.Release();startName.Owner?.Release();goalName.Owner?.Release();}
    }
}
