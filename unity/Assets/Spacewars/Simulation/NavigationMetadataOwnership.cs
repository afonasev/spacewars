using System;

namespace Spacewars.Simulation
{
    internal enum NavMetadataRole { Clearance=1,GraphProfile,Subscription,CompileLimits }
    internal interface INavMetadataFacade : INavStorageFacade { NavMetadataRole MetadataRole {get;} }
    // A selector is not an independently allocated/charged facade. Its consumer
    // owns the aggregate edge; the facade's backreference never owns a reference.
    internal readonly struct NavMetadataReference
    {
        internal readonly NavOwnedStorage Owner;
        internal readonly NavMetadataRole Role;
        internal NavMetadataReference(NavOwnedStorage owner,NavMetadataRole role)
        {if(owner!=null||role!=0)NavMetadataOwnership.Resolve(owner,role);Owner=owner;Role=role;}
        internal object Value=>Owner==null&&Role==0?null:NavMetadataOwnership.Resolve(Owner,Role);
        internal static NavMetadataReference From(INavMetadataFacade value,INavStorageReservation reservation)
        {
            var owner=NavMetadataOwnership.Require(value,reservation);
            return new NavMetadataReference(owner,value.MetadataRole);
        }
        internal void Validate(NavOwnedStorage parent)
        {if(Owner==null&&Role==0)return;NavMetadataOwnership.Resolve(Owner,Role);parent.RequireEdge(Owner);}
        internal void Write(NavPhysicalBodyWriter writer)
        {if(Owner!=null||Role!=0)NavMetadataOwnership.Resolve(Owner,Role);writer.Edge(Owner);writer.Output.Write((int)Role);}
        internal static NavMetadataReference Read(NavPhysicalBodyReader reader)
        {var owner=reader.Edge();return new NavMetadataReference(owner,(NavMetadataRole)reader.Input.ReadInt32());}
    }
    internal static class NavMetadataOwnership
    {
        internal static object Resolve(NavOwnedStorage owner,NavMetadataRole role)
        {
            if(owner==null||owner.References<=0)throw new ArgumentException("Missing live metadata aggregate.");
            object value=null;
            if(owner is NavGraphMetadata aggregate){
                switch(role){case NavMetadataRole.Clearance:value=aggregate.Clearance;break;case NavMetadataRole.GraphProfile:value=aggregate.Profile;break;case NavMetadataRole.CompileLimits:value=aggregate.Limits;break;}
            }else if(owner is NavDeclarationStorage storage&&storage.Facade is NavScheduledRequest recipe){
                switch(role){case NavMetadataRole.Clearance:value=recipe.Clearance;break;case NavMetadataRole.GraphProfile:value=recipe.Profile;break;case NavMetadataRole.Subscription:value=recipe.Subscription;break;case NavMetadataRole.CompileLimits:value=recipe.Limits;break;}
            }else if(owner is NavDeclarationStorage subscriptionStorage&&subscriptionStorage.Facade is NavRouteSubscription subscription&&role==NavMetadataRole.Subscription){
                value=subscription;
            }
            if(!(value is INavMetadataFacade facade)||facade.MetadataRole!=role||!ReferenceEquals(facade.StorageRoot,owner))throw new ArgumentException("Wrong metadata owner or field role.");
            return value;
        }
        internal static void CheckCopyInputs(NavClearanceProfile clearance,NavGraphProfile profile,NavGraphCompileLimits limits,INavStorageReservation reservation)
        {
            if(clearance==null||profile==null||limits==null)throw new ArgumentNullException("metadata");
            if(clearance.MetadataOwner!=null)Require(clearance,reservation);
            if(profile.MetadataOwner!=null)Require(profile,reservation);
            if(limits.MetadataOwner!=null)Require(limits,reservation);
        }
        internal static void RequireGraph(NavClearanceProfile clearance,NavGraphProfile profile,NavGraphCompileLimits limits,INavStorageReservation reservation)
        {Require(clearance,reservation);Require(profile,reservation);Require(limits,reservation);}
        internal static void RetainDistinct(NavOwnedStorage parent,NavOwnedStorage a,NavOwnedStorage b,NavOwnedStorage c,out NavOwnedStorage first,out NavOwnedStorage second,out NavOwnedStorage third)
        {
            first=second=third=null;
            if(parent.IsRegistered){parent.RequireEdge(a);parent.RequireEdge(b);if(c!=null)parent.RequireEdge(c);}
            if(a!=null){a.Retain();first=a;}
            if(b!=null&&!ReferenceEquals(b,a)){b.Retain();second=b;}
            if(c!=null&&!ReferenceEquals(c,a)&&!ReferenceEquals(c,b)){c.Retain();third=c;}
        }
        internal static NavOwnedStorage Require(INavMetadataFacade value,INavStorageReservation reservation)
        {
            if(value==null)throw new ArgumentNullException(nameof(value));
            NavStorageAdmission.RequireOwned(reservation,value.StorageRoot);
            if(!ReferenceEquals(Resolve(value.StorageRoot,value.MetadataRole),value))throw new ArgumentException("Metadata facade is not its owner's exact field.");
            return value.StorageRoot;
        }
    }
    public sealed partial class NavClearanceProfile : INavMetadataFacade
    {
        internal NavOwnedStorage MetadataOwner;
        NavOwnedStorage INavStorageFacade.StorageRoot=>MetadataOwner;
        NavMetadataRole INavMetadataFacade.MetadataRole=>NavMetadataRole.Clearance;
    }
    public sealed partial class NavGraphProfile : INavMetadataFacade
    {
        internal NavOwnedStorage MetadataOwner;
        NavOwnedStorage INavStorageFacade.StorageRoot=>MetadataOwner;
        NavMetadataRole INavMetadataFacade.MetadataRole=>NavMetadataRole.GraphProfile;
    }
    public sealed partial class NavRouteSubscription : INavMetadataFacade
    {
        internal NavOwnedStorage MetadataOwner;
        NavOwnedStorage INavStorageFacade.StorageRoot=>MetadataOwner;
        NavMetadataRole INavMetadataFacade.MetadataRole=>NavMetadataRole.Subscription;
    }
    public sealed partial class NavGraphCompileLimits : INavMetadataFacade
    {
        internal NavOwnedStorage MetadataOwner;
        NavOwnedStorage INavStorageFacade.StorageRoot=>MetadataOwner;
        NavMetadataRole INavMetadataFacade.MetadataRole=>NavMetadataRole.CompileLimits;
    }
    // One concrete aggregate covers only copied public fields. Existing owned
    // inputs keep their original owners; mixed-owner fields never get merged.
    internal sealed class NavGraphMetadata : NavOwnedStorage,INavPhysicalBody
    {
        internal readonly NavClearanceProfile Clearance;
        internal readonly NavGraphProfile Profile;
        internal readonly NavGraphCompileLimits Limits;
        internal override bool IsImmutableCaptureRoot=>true;
        public NavPhysicalTag PhysicalTag=>new NavPhysicalTag(NavPhysicalStorageKind.Metadata);
        private NavGraphMetadata(long bytes,NavClearanceProfile clearance,NavGraphProfile profile,NavGraphCompileLimits limits):base(bytes)
        {Clearance=clearance;Profile=profile;Limits=limits;if(clearance!=null)clearance.MetadataOwner=this;if(profile!=null)profile.MetadataOwner=this;if(limits!=null)limits.MetadataOwner=this;}
        private static long Layout(NavClearanceProfile c,NavGraphProfile p)=>checked(512L+NavProviderCopy.TextBytes(c?.Id)+NavProviderCopy.TextBytes(c?.MobilityClass)+NavProviderCopy.TextBytes(p?.ExactProfileBinding)+NavProviderCopy.TextBytes(p?.CostId));
        internal static NavGraphMetadata CopyOwned(NavClearanceProfile clearance,NavGraphProfile profile,NavGraphCompileLimits limits,INavStorageReservation reservation)
        {NavMetadataOwnership.CheckCopyInputs(clearance,profile,limits,reservation);return CopyFields(clearance,profile,limits,reservation);}
        internal static NavGraphMetadata CopyUnowned(NavClearanceProfile clearance,NavGraphProfile profile,NavGraphCompileLimits limits,INavStorageReservation reservation)
        {
            NavMetadataOwnership.CheckCopyInputs(clearance,profile,limits,reservation);
            if(clearance.MetadataOwner!=null)clearance=null;if(profile.MetadataOwner!=null)profile=null;if(limits.MetadataOwner!=null)limits=null;
            return clearance==null&&profile==null&&limits==null?null:CopyFields(clearance,profile,limits,reservation);
        }
        internal static NavGraphMetadata CopyOptionalUnowned(NavClearanceProfile clearance,NavGraphProfile profile,NavGraphCompileLimits limits,INavStorageReservation reservation)
        {
            if(reservation==null||clearance==null&&profile==null&&limits==null)throw new ArgumentException("Missing optional metadata input.");
            if(clearance?.MetadataOwner!=null){NavMetadataOwnership.Require(clearance,reservation);clearance=null;}
            if(profile?.MetadataOwner!=null){NavMetadataOwnership.Require(profile,reservation);profile=null;}
            if(limits?.MetadataOwner!=null){NavMetadataOwnership.Require(limits,reservation);limits=null;}
            return clearance==null&&profile==null&&limits==null?null:CopyFields(clearance,profile,limits,reservation);
        }
        private static NavGraphMetadata CopyFields(NavClearanceProfile clearance,NavGraphProfile profile,NavGraphCompileLimits limits,INavStorageReservation reservation)
        {
            long bytes=Layout(clearance,profile);
            using(var allocation=new NavStorageAllocation(reservation.TryReserve)){
                allocation.Reserve(bytes);
                var c=clearance==null?null:new NavClearanceProfile(NavProviderCopy.Text(clearance.Id),clearance.Revision,NavProviderCopy.Text(clearance.MobilityClass),clearance.MobilityRevision,clearance.Radius);
                var p=profile==null?null:new NavGraphProfile(NavProviderCopy.Text(profile.ExactProfileBinding),NavProviderCopy.Text(profile.CostId),profile.CostRevision,profile.GridCell);
                var l=limits==null?null:new NavGraphCompileLimits(limits.MaxSamples,limits.MaxNodes,limits.MaxArcs);
                var result=allocation.Register(new NavGraphMetadata(bytes,c,p,l));result.Retain();return result;
            }
        }
        public void WritePhysicalBody(NavPhysicalBodyWriter writer)
        {
            int mask=(Clearance!=null?1:0)|(Profile!=null?2:0)|(Limits!=null?4:0);if(mask==0)throw new ArgumentException("Empty metadata aggregate.");
            if(Clearance!=null)NavMetadataOwnership.Resolve(this,NavMetadataRole.Clearance);if(Profile!=null)NavMetadataOwnership.Resolve(this,NavMetadataRole.GraphProfile);if(Limits!=null)NavMetadataOwnership.Resolve(this,NavMetadataRole.CompileLimits);
            if(StorageBytes!=NavStorageLayout.Charge(Layout(Clearance,Profile)))throw new ArgumentException("Metadata aggregate layout contradiction.");
            writer.Output.Write(mask);
            if(Clearance!=null){NavPhysicalTextPlan.WritePlan(writer,Clearance.Id);NavPhysicalTextPlan.WritePlan(writer,Clearance.MobilityClass);}
            if(Profile!=null){NavPhysicalTextPlan.WritePlan(writer,Profile.ExactProfileBinding);NavPhysicalTextPlan.WritePlan(writer,Profile.CostId);}
            if(Clearance!=null){NavPhysicalTextPlan.WriteData(writer,Clearance.Id);NavPhysicalTextPlan.WriteData(writer,Clearance.MobilityClass);writer.Output.Write(Clearance.Revision);writer.Output.Write(Clearance.MobilityRevision);writer.Output.Write(Clearance.Radius);}
            if(Profile!=null){NavPhysicalTextPlan.WriteData(writer,Profile.ExactProfileBinding);NavPhysicalTextPlan.WriteData(writer,Profile.CostId);writer.Output.Write(Profile.CostRevision);writer.Output.Write(Profile.GridCell);}
            if(Limits!=null){writer.Output.Write(Limits.MaxSamples);writer.Output.Write(Limits.MaxNodes);writer.Output.Write(Limits.MaxArcs);}
        }
        internal static NavGraphMetadata ReadPhysicalBody(NavPhysicalBodyReader reader)
        {
            int mask=reader.Input.ReadInt32();if(mask<1||mask>7)throw new ArgumentException("Invalid metadata aggregate fields.");
            var a=(mask&1)!=0?NavPhysicalTextPlan.Read(reader):default;var b=(mask&1)!=0?NavPhysicalTextPlan.Read(reader):default;
            var c=(mask&2)!=0?NavPhysicalTextPlan.Read(reader):default;var d=(mask&2)!=0?NavPhysicalTextPlan.Read(reader):default;
            long bytes=checked(512L+((mask&1)!=0?a.Bytes+b.Bytes:0)+((mask&2)!=0?c.Bytes+d.Bytes:0));
            using(var allocation=new NavStorageAllocation(reader.Reservation.TryReserve)){
                allocation.Reserve(bytes);NavClearanceProfile clearance=null;NavGraphProfile profile=null;NavGraphCompileLimits limits=null;
                if((mask&1)!=0){string id=a.ReadData(reader),mobility=b.ReadData(reader);clearance=new NavClearanceProfile(id,reader.Input.ReadInt32(),mobility,reader.Input.ReadInt32(),reader.Input.ReadDouble());}
                if((mask&2)!=0){string binding=c.ReadData(reader),cost=d.ReadData(reader);profile=new NavGraphProfile(binding,cost,reader.Input.ReadInt32(),reader.Input.ReadDouble());}
                if((mask&4)!=0)limits=new NavGraphCompileLimits(reader.Input.ReadInt32(),reader.Input.ReadInt32(),reader.Input.ReadInt32());
                return allocation.Register(new NavGraphMetadata(bytes,clearance,profile,limits));
            }
        }
        protected override void ReleaseEdges(){}
    }
}
