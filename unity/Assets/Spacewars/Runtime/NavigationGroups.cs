using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    public enum GroupMemberPhase { PendingRoute, March, Arrived, Unreachable, Blocked, Stopped, Held, Combat, Detached, Dead, Allocating }
    // Serializable authority records. Public observations/checkpoints are detached copies.
    [Serializable] public sealed class GroupMemberState
    {
        public int Entity, Slot; public long Incarnation, OrderRevision, MobilityRevision;
        public GroupMemberPhase Phase; public NavPoint Goal; public NavLocation? GoalLocation; public bool HasGoal, FormationReleased;
        public long Branch, Reservation, ContactTick, ProgressRequest; public string ContactPhase; public double Progress; public int ProgressVersion; public double ProgressOffset;
        internal GroupMemberState Copy()=>(GroupMemberState)MemberwiseClone();
    }
    [Serializable] public sealed class GroupOrderState
    {
        public long WorldGeneration, GroupId, OrderRevision, CommandSequence, IssuedTick, MembershipRevision;
        // Version zero is an explicit pre-march checkpoint. New issuances bind
        // their current Balance Lab revision and retain a geometric anchor.
        public int MarchVersion, MarchProfileRevision; public double MarchStretch, MarchAnchor;
        public bool MarchInitialized;
        public string OwnerId, Source; public int TeamId, TargetId;
        public PlayableOrderOrigin Origin; public PlayableCommandKind Kind; public long JobId, ActionId;
        public NavPoint OriginalGoal; public NavLocation? OriginalLocation; public GroupMemberState[] Members;
        internal GroupOrderState CopyHeader()=>(GroupOrderState)MemberwiseClone();
        internal GroupOrderState Copy(){var copy=(GroupOrderState)MemberwiseClone();copy.Members=Members.Select(m=>m.Copy()).ToArray();return copy;}
    }
    // Immutable application identity. Membership changes do not cancel unaffected member connectors.
    public sealed class NavigationMemberIdentity
    {
        public NavigationMemberIdentity(long incarnation,long mobility,long group=0,long order=0,long membership=0)
        {Incarnation=incarnation;MobilityRevision=mobility;GroupId=group;OrderRevision=order;MembershipRevision=membership;}
        public long Incarnation{get;} public long MobilityRevision{get;} public long GroupId{get;} public long OrderRevision{get;} public long MembershipRevision{get;}
    }
    // Immutable member payload, separate from mutable Session progress slots.
    // Both hot copies and cold BODY definitions use the same scalar/edge factory.
    internal sealed class NavigationAuthorityMember : NavOwnedStorage, INavPhysicalBody
    {
        internal readonly struct Values
        {
            internal readonly int Entity,Slot;internal readonly long Incarnation,OrderRevision,MobilityRevision,Branch,Reservation,ContactTick,ProgressRequest;
            internal readonly GroupMemberPhase Phase;internal readonly NavPoint Goal,Location;internal readonly bool HasLocation,HasGoal,FormationReleased;internal readonly double Progress,ProgressOffset;internal readonly int ProgressVersion;
            internal Values(int entity,int slot,long incarnation,long order,long mobility,GroupMemberPhase phase,NavPoint goal,NavPoint location,bool hasLocation,bool hasGoal,bool released,long branch,long reservation,long contactTick,long progressRequest,double progress,int progressVersion=0,double progressOffset=0)
            {Entity=entity;Slot=slot;Incarnation=incarnation;OrderRevision=order;MobilityRevision=mobility;Phase=phase;Goal=goal;Location=hasLocation?location:default;HasLocation=hasLocation;HasGoal=hasGoal;FormationReleased=released;Branch=branch;Reservation=reservation;ContactTick=contactTick;ProgressRequest=progressRequest;Progress=progress;ProgressVersion=progressVersion;ProgressOffset=progressOffset;}
            internal Values(GroupMemberState source):this(source.Entity,source.Slot,source.Incarnation,source.OrderRevision,source.MobilityRevision,source.Phase,source.Goal,source.GoalLocation?.Position??default,source.GoalLocation.HasValue,source.HasGoal,source.FormationReleased,source.Branch,source.Reservation,source.ContactTick,source.ProgressRequest,source.Progress,source.ProgressVersion,source.ProgressOffset){}
            internal void Validate(){if(Slot<0||ContactTick<0||ProgressRequest<0||Branch<0||Reservation<0||(uint)Phase>(uint)GroupMemberPhase.Allocating||!Finite(Goal.X)||!Finite(Goal.Z)||HasLocation&&(!Finite(Location.X)||!Finite(Location.Z))||!Finite(Progress)||ProgressVersion!=0&&ProgressVersion!=2||!Finite(ProgressOffset)||ProgressVersion==0&&ProgressOffset!=0||ProgressVersion==2&&(Progress<0||ProgressOffset<0||ProgressOffset>Progress))throw new ArgumentException("Invalid immutable group member scalars.");}
            internal bool BitwiseSame(Values other)=>Entity==other.Entity&&Slot==other.Slot&&Incarnation==other.Incarnation&&OrderRevision==other.OrderRevision&&MobilityRevision==other.MobilityRevision&&Phase==other.Phase&&HasLocation==other.HasLocation&&HasGoal==other.HasGoal&&FormationReleased==other.FormationReleased&&Branch==other.Branch&&Reservation==other.Reservation&&ContactTick==other.ContactTick&&ProgressRequest==other.ProgressRequest&&Bits(Goal.X,other.Goal.X)&&Bits(Goal.Z,other.Goal.Z)&&Bits(Location.X,other.Location.X)&&Bits(Location.Z,other.Location.Z)&&Bits(Progress,other.Progress)&&ProgressVersion==other.ProgressVersion&&Bits(ProgressOffset,other.ProgressOffset);
            private static bool Bits(double a,double b)=>BitConverter.DoubleToInt64Bits(a)==BitConverter.DoubleToInt64Bits(b);
            private static bool Finite(double value)=>!double.IsNaN(value)&&!double.IsInfinity(value);
        }
        private static readonly long LayoutBytes=Math.Max(256,NavStorageLayout.ObjectBytesBound(typeof(NavigationAuthorityMember)));
        internal readonly Values Data;
        private NavOwnedText contact;private NavNameReference goalName;
        private bool sealedRoot,released;
        internal string ContactPhase=>contact?.Value;
        internal NavOwnedText ContactOwner=>contact;
        internal NavNameReference GoalSelector=>goalName;
        internal NavLocation? GoalLocation=>Data.HasLocation?new NavLocation(Data.Location,goalName):(NavLocation?)null;
        internal override bool IsImmutableCaptureRoot=>sealedRoot;
        public NavPhysicalTag PhysicalTag=>new NavPhysicalTag(NavPhysicalStorageKind.AuthorityMember,value:Data.ProgressVersion==2?NavPhysicalValueKind.GroupMarchProgressV2:NavPhysicalValueKind.None);
        private NavigationAuthorityMember(Values data,bool owner):base(LayoutBytes){Data=data;if(owner)Retain();}
        private static NavigationAuthorityMember Allocate(Values data,INavStorageReservation reservation,bool owner)
        {using(var allocation=new NavStorageAllocation(reservation.TryReserve)){allocation.Reserve(LayoutBytes);return allocation.Register(new NavigationAuthorityMember(data,owner));}}
        private static NavigationAuthorityMember Create(Values data,NavOwnedText contact,NavNameReference goal,INavStorageReservation reservation,bool owner)
        {
            data.Validate();if(reservation==null||data.HasLocation!=(goal.Owner!=null))throw new ArgumentException("Missing member location owner.");
            if(contact!=null)NavStorageAdmission.RequireOwned(reservation,contact);
            if(goal.Owner!=null){NavStorageAdmission.RequireOwned(reservation,goal.Owner);goal.Require(reservation,goal.Resolve());}
            var result=Allocate(data,reservation,owner);
            try{result.contact=NavPhysicalMacro.Acquire(result,contact);if(goal.Owner!=null){result.RequireEdge(goal.Owner);goal.Owner.Retain();}result.goalName=goal;result.sealedRoot=true;return result;}
            catch{if(owner)result.ReleaseStorage();throw;}
        }
        internal static NavigationAuthorityMember CreateOwned(Values data,NavOwnedText contact,NavNameReference goal,INavStorageReservation reservation)=>Create(data,contact,goal,reservation,true);
        internal static NavigationAuthorityMember CopyWithValuesOwned(NavigationAuthorityMember source,Values data,INavStorageReservation reservation)
        {
            if(source==null||!source.IsImmutableCaptureRoot)throw new ArgumentException("Missing sealed member source.");
            NavStorageAdmission.RequireOwned(reservation,source);
            return CreateOwned(data,source.contact,data.HasLocation?source.goalName:default,reservation);
        }
        internal static NavigationAuthorityMember CopyOwned(GroupMemberState source,INavStorageReservation reservation)
        {
            if(source==null||reservation==null)throw new ArgumentNullException();var values=new Values(source);var location=source.GoalLocation;var phase=source.ContactPhase;values.Validate();
            if(location?.NameReference.Owner!=null)location.Value.NameReference.Require(reservation,location.Value.SurfaceId);
            NavOwnedText contact=null,privateGoal=null;
            try{if(phase!=null)contact=NavOwnedText.Copy(phase,reservation);
                var goal=NavigationHostRequest.OwnLocationName(location,reservation,out privateGoal);
                return CreateOwned(values,contact,goal,reservation);
            }finally{contact?.Release();privateGoal?.Release();}
        }
        public void WritePhysicalBody(NavPhysicalBodyWriter writer)
        {
            if(!sealedRoot||StorageBytes!=NavStorageLayout.Charge(LayoutBytes)||Data.HasLocation!=(goalName.Owner!=null))throw new ArgumentException("Invalid member ownership.");Data.Validate();
            var w=writer.Output;w.Write(Data.Entity);w.Write(Data.Slot);w.Write(Data.Incarnation);w.Write(Data.OrderRevision);w.Write(Data.MobilityRevision);w.Write((int)Data.Phase);
            WorldWire.Write(w,Data.Goal);w.Write(Data.HasLocation);if(Data.HasLocation)WorldWire.Write(w,Data.Location);w.Write(Data.HasGoal);w.Write(Data.FormationReleased);
            w.Write(Data.Branch);w.Write(Data.Reservation);w.Write(Data.ContactTick);w.Write(Data.ProgressRequest);WorldWire.Number(w,Data.Progress);if(Data.ProgressVersion==2)WorldWire.Number(w,Data.ProgressOffset);
            NavPhysicalMacro.WriteEdge(writer,this,contact,true);if(goalName.Owner!=null)goalName.Validate(goalName.Resolve(),this);goalName.Write(writer);
        }
        internal static NavigationAuthorityMember ReadPhysicalBody(NavPhysicalBodyReader reader,bool v2=false)
        {
            var r=reader.Input;int entity=r.ReadInt32(),slot=r.ReadInt32();long incarnation=r.ReadInt64(),order=r.ReadInt64(),mobility=r.ReadInt64();var phase=(GroupMemberPhase)r.ReadInt32();
            var goal=WorldWire.ReadPoint(r);bool hasLocation=NavPhysicalMacro.ReadBoolean(reader);var location=hasLocation?WorldWire.ReadPoint(r):default;bool hasGoal=NavPhysicalMacro.ReadBoolean(reader),released=NavPhysicalMacro.ReadBoolean(reader);
            long branch=r.ReadInt64(),reservation=r.ReadInt64(),contactTick=r.ReadInt64(),progressRequest=r.ReadInt64();double progress=WorldWire.Number(r),offset=v2?WorldWire.Number(r):0;
            var values=new Values(entity,slot,incarnation,order,mobility,phase,goal,location,hasLocation,hasGoal,released,branch,reservation,contactTick,progressRequest,progress,v2?2:0,offset);values.Validate();
            var contact=NavPhysicalMacro.ReadEdge<NavOwnedText>(reader,true);var goalName=NavNameReference.Read(reader);
            return Create(values,contact,goalName,reader.Reservation,false);
        }
        internal void ReleaseStorage(){if(released)return;released=true;Release();}
        internal override void ReleaseOwner()=>ReleaseStorage();
        protected override void ReleaseEdges(){contact?.Release();goalName.Owner?.Release();contact=null;goalName=default;sealedRoot=false;}
    }
    internal sealed class NavigationAuthorityGroup : NavOwnedStorage, INavPhysicalBody
    {
        internal readonly struct Values
        {
            internal readonly long WorldGeneration,GroupId,OrderRevision,CommandSequence,IssuedTick,MembershipRevision,JobId,ActionId;
            internal readonly int MarchVersion,MarchProfileRevision,TeamId,TargetId;
            internal readonly double MarchStretch,MarchAnchor;internal readonly bool MarchInitialized,HasLocation;
            internal readonly PlayableOrderOrigin Origin;internal readonly PlayableCommandKind Kind;internal readonly NavPoint OriginalGoal,Location;
            internal Values(GroupOrderState source){WorldGeneration=source.WorldGeneration;GroupId=source.GroupId;OrderRevision=source.OrderRevision;CommandSequence=source.CommandSequence;IssuedTick=source.IssuedTick;MembershipRevision=source.MembershipRevision;JobId=source.JobId;ActionId=source.ActionId;MarchVersion=source.MarchVersion;MarchProfileRevision=source.MarchProfileRevision;TeamId=source.TeamId;TargetId=source.TargetId;MarchStretch=source.MarchStretch;MarchAnchor=source.MarchAnchor;MarchInitialized=source.MarchInitialized;Origin=source.Origin;Kind=source.Kind;OriginalGoal=source.OriginalGoal;HasLocation=source.OriginalLocation.HasValue;Location=source.OriginalLocation?.Position??default;}
            internal Values(long world,long group,long order,long command,long tick,long membership,long job,long action,int march,int revision,int team,int target,double stretch,double anchor,bool initialized,PlayableOrderOrigin origin,PlayableCommandKind kind,NavPoint goal,bool hasLocation,NavPoint location){WorldGeneration=world;GroupId=group;OrderRevision=order;CommandSequence=command;IssuedTick=tick;MembershipRevision=membership;JobId=job;ActionId=action;MarchVersion=march;MarchProfileRevision=revision;TeamId=team;TargetId=target;MarchStretch=stretch;MarchAnchor=anchor;MarchInitialized=initialized;Origin=origin;Kind=kind;OriginalGoal=goal;HasLocation=hasLocation;Location=hasLocation?location:default;}
            internal void ValidateScalars(){if(WorldGeneration<1||GroupId<1||OrderRevision<1||MembershipRevision<1||CommandSequence<0||IssuedTick<0||(uint)Origin>(uint)PlayableOrderOrigin.Ai||(uint)Kind>(uint)PlayableCommandKind.Follow||MarchVersion<0||MarchVersion>2||!Finite(MarchAnchor)||MarchVersion==1&&MarchAnchor>0||MarchVersion==2&&MarchAnchor<0||!Finite(MarchStretch)||!Finite(OriginalGoal.X)||!Finite(OriginalGoal.Z)||HasLocation&&(!Finite(Location.X)||!Finite(Location.Z))||
                (MarchVersion==0?MarchInitialized||MarchAnchor!=0||MarchStretch!=0||MarchProfileRevision!=0:MarchProfileRevision<1||MarchStretch<=0||!MarchInitialized&&MarchAnchor!=0)||
                (Origin==PlayableOrderOrigin.Ai?JobId<1||ActionId<1:JobId!=0||ActionId!=0))throw new ArgumentException("Invalid immutable group scalars.");}
            internal void Validate(string source){ValidateScalars();if(Origin==PlayableOrderOrigin.Ai?string.IsNullOrWhiteSpace(source):source!=null)throw new ArgumentException("Invalid group source role.");}
            internal bool SameIssuance(Values other)=>WorldGeneration==other.WorldGeneration&&GroupId==other.GroupId&&OrderRevision==other.OrderRevision&&CommandSequence==other.CommandSequence&&IssuedTick==other.IssuedTick&&MembershipRevision==other.MembershipRevision&&JobId==other.JobId&&ActionId==other.ActionId&&TeamId==other.TeamId&&TargetId==other.TargetId&&HasLocation==other.HasLocation&&Origin==other.Origin&&Kind==other.Kind&&Bits(OriginalGoal.X,other.OriginalGoal.X)&&Bits(OriginalGoal.Z,other.OriginalGoal.Z)&&Bits(Location.X,other.Location.X)&&Bits(Location.Z,other.Location.Z);
            internal bool BitwiseSame(Values other)=>WorldGeneration==other.WorldGeneration&&GroupId==other.GroupId&&OrderRevision==other.OrderRevision&&CommandSequence==other.CommandSequence&&IssuedTick==other.IssuedTick&&MembershipRevision==other.MembershipRevision&&JobId==other.JobId&&ActionId==other.ActionId&&MarchVersion==other.MarchVersion&&MarchProfileRevision==other.MarchProfileRevision&&TeamId==other.TeamId&&TargetId==other.TargetId&&MarchInitialized==other.MarchInitialized&&HasLocation==other.HasLocation&&Origin==other.Origin&&Kind==other.Kind&&Bits(MarchStretch,other.MarchStretch)&&Bits(MarchAnchor,other.MarchAnchor)&&Bits(OriginalGoal.X,other.OriginalGoal.X)&&Bits(OriginalGoal.Z,other.OriginalGoal.Z)&&Bits(Location.X,other.Location.X)&&Bits(Location.Z,other.Location.Z);
            private static bool Bits(double left,double right)=>BitConverter.DoubleToInt64Bits(left)==BitConverter.DoubleToInt64Bits(right);
            private static bool Finite(double value)=>!double.IsNaN(value)&&!double.IsInfinity(value);
        }
        private static long fullValidationVisits,versionSequence;
        private readonly long versionIdentity;internal long VersionIdentity=>versionIdentity;internal readonly long MemberParentIdentity;internal readonly int ChangedMemberIndex;
        internal static long FullValidationVisits=>System.Threading.Interlocked.Read(ref fullValidationVisits);
        private static readonly long LayoutBytes=Math.Max(256,NavStorageLayout.ObjectBytesBound(typeof(NavigationAuthorityGroup)));
        internal readonly Values Data;private NavOwnedStorage memberRoot;internal System.Collections.Generic.IReadOnlyList<NavOwnedStorage> Members=>(System.Collections.Generic.IReadOnlyList<NavOwnedStorage>)memberRoot;
        private NavOwnedText ownerId,source;private NavNameReference originalName;private bool sealedRoot,released;
        internal NavOwnedText OwnerIdOwner=>ownerId;internal NavOwnedText SourceOwner=>source;internal NavNameReference OriginalSelector=>originalName;
        internal string OwnerId=>ownerId?.Value;internal string Source=>source?.Value;
        internal NavLocation? OriginalLocation=>Data.HasLocation?new NavLocation(Data.Location,originalName):(NavLocation?)null;
        internal override bool IsImmutableCaptureRoot=>sealedRoot;
        public NavPhysicalTag PhysicalTag=>new NavPhysicalTag(NavPhysicalStorageKind.AuthorityGroup);
        private NavigationAuthorityGroup(Values data,bool owner,long parentIdentity,int changedMember):base(LayoutBytes){Data=data;versionIdentity=System.Threading.Interlocked.Increment(ref versionSequence);MemberParentIdentity=parentIdentity;ChangedMemberIndex=changedMember;if(owner)Retain();}
        private static void PreflightMembers(Values data,System.Collections.Generic.IReadOnlyList<NavOwnedStorage> members,INavStorageReservation reservation)
        {
            if(reservation==null||!(members is NavRetainedArray<NavOwnedStorage>)&&!(members is NavPagedList<NavOwnedStorage>))throw new ArgumentException("Member input must be an owned immutable concrete backing.");
            var input=(NavOwnedStorage)members;if(!input.IsImmutableCaptureRoot)throw new ArgumentException("Member backing is not sealed.");NavStorageAdmission.RequireOwned(reservation,input);
            if(members.Count<1||members.Count>AuthoritySlotMap<int,int>.MaximumSlots)throw new ArgumentException("Missing immutable member entries.");
            System.Threading.Interlocked.Add(ref fullValidationVisits,members.Count);int previous=-1;
            for(int i=0;i<members.Count;i++){
                if(!(members[i] is NavigationAuthorityMember member)||!member.IsImmutableCaptureRoot)throw new ArgumentException("Wrong immutable group member kind.");NavStorageAdmission.RequireOwned(reservation,member);var value=member.Data;
                if(value.Entity<=previous||value.OrderRevision<data.OrderRevision||value.Phase==GroupMemberPhase.Dead||value.Phase==GroupMemberPhase.Detached||(data.MarchVersion==0?value.Progress<0||value.ProgressRequest!=0:data.MarchVersion==1?value.Progress>0||value.ProgressVersion!=0:value.ProgressVersion!=2))throw new ArgumentException("Group member context contradiction.");previous=value.Entity;
            }
            // Reject all foreign/type/scalar input before even validation scratch.
            using(NavStorageScratch.Reserve(reservation,checked(64L+4L*members.Count))){var slots=new int[members.Count];for(int i=0;i<slots.Length;i++)slots[i]=((NavigationAuthorityMember)members[i]).Data.Slot;Array.Sort(slots);for(int i=1;i<slots.Length;i++)if(slots[i]==slots[i-1])throw new ArgumentException("Duplicate member slot.");}
        }
        private static void Preflight(Values data,NavOwnedText ownerId,NavOwnedText source,NavNameReference location,System.Collections.Generic.IReadOnlyList<NavOwnedStorage> members,INavStorageReservation reservation)
        {
            data.Validate(source?.Value);if(data.HasLocation!=(location.Owner!=null))throw new ArgumentException("Missing original location owner.");
            if(ownerId!=null)NavStorageAdmission.RequireOwned(reservation,ownerId);if(source!=null)NavStorageAdmission.RequireOwned(reservation,source);if(location.Owner!=null)location.Require(reservation,location.Resolve());
            PreflightMembers(data,members,reservation);
        }
        private static NavigationAuthorityGroup Create(Values data,NavOwnedText ownerId,NavOwnedText source,NavNameReference location,NavOwnedStorage members,INavStorageReservation reservation,bool owner,bool inheritedMembership=false,long parentIdentity=0,int changedMember=-1)
        {
            if(members==null)throw new ArgumentException("Missing member backing.");NavStorageAdmission.RequireOwned(reservation,members);if(!(members is System.Collections.Generic.IReadOnlyList<NavOwnedStorage> sequence))throw new ArgumentException("Wrong member backing kind.");if(inheritedMembership){data.Validate(source?.Value);if(!members.IsImmutableCaptureRoot||data.HasLocation!=(location.Owner!=null))throw new ArgumentException("Invalid inherited immutable membership.");if(ownerId!=null)NavStorageAdmission.RequireOwned(reservation,ownerId);if(source!=null)NavStorageAdmission.RequireOwned(reservation,source);if(location.Owner!=null)location.Require(reservation,location.Resolve());}
            else Preflight(data,ownerId,source,location,sequence,reservation);
            using(var allocation=new NavStorageAllocation(reservation.TryReserve)){
                allocation.Reserve(LayoutBytes);var result=allocation.Register(new NavigationAuthorityGroup(data,owner,parentIdentity,changedMember));
                try{result.ownerId=NavPhysicalMacro.Acquire(result,ownerId);result.source=NavPhysicalMacro.Acquire(result,source);result.memberRoot=NavPhysicalMacro.Acquire(result,members);if(location.Owner!=null){result.RequireEdge(location.Owner);location.Owner.Retain();}result.originalName=location;result.sealedRoot=true;return result;}
                catch{if(owner)result.ReleaseStorage();throw;}
            }
        }
        internal static NavigationAuthorityGroup CreateOwned(Values data,NavOwnedText ownerId,NavOwnedText source,NavNameReference location,NavRetainedArray<NavOwnedStorage> members,INavStorageReservation reservation)=>Create(data,ownerId,source,location,members,reservation,true);
        internal static NavigationAuthorityGroup CopyNamesOwned(GroupOrderState value,NavRetainedArray<NavOwnedStorage> members,INavStorageReservation reservation)
        {
            if(value==null||members==null)throw new ArgumentNullException();var data=new Values(value);var id=value.OwnerId;var name=value.Source;var location=value.OriginalLocation;
            data.Validate(name);NavStorageAdmission.RequireOwned(reservation,members);if(location?.NameReference.Owner!=null)location.Value.NameReference.Require(reservation,location.Value.SurfaceId);PreflightMembers(data,members,reservation);
            NavOwnedText ownedId=null,ownedSource=null,privateLocation=null;
            try{if(id!=null)ownedId=NavOwnedText.Copy(id,reservation);if(name!=null)ownedSource=NavOwnedText.Copy(name,reservation);var selector=NavigationHostRequest.OwnLocationName(location,reservation,out privateLocation);return CreateOwned(data,ownedId,ownedSource,selector,members,reservation);}
            finally{ownedId?.Release();ownedSource?.Release();privateLocation?.Release();}
        }
        internal static NavRetainedArray<NavOwnedStorage> CopyMembersOwned(Values data,NavOwnedText ownerId,NavOwnedText source,NavNameReference location,System.Collections.Generic.IReadOnlyList<NavOwnedStorage> members,INavStorageReservation reservation)
        {Preflight(data,ownerId,source,location,members,reservation);return NavRetainedArray<NavOwnedStorage>.Copy(members,reservation.TryReserve);}
        internal static NavigationAuthorityGroup CopyWithValuesOwned(NavigationAuthorityGroup current,Values data,INavStorageReservation reservation)
        {if(current==null||!current.IsImmutableCaptureRoot)throw new ArgumentException("Missing sealed group source.");NavStorageAdmission.RequireOwned(reservation,current);return Create(data,current.ownerId,current.source,data.HasLocation?current.originalName:default,current.memberRoot,reservation,true);}
        internal static NavigationAuthorityGroup CopyWithMemberOwned(NavigationAuthorityGroup current,int index,NavigationAuthorityMember member,INavStorageReservation reservation)
            =>CopyWithMemberAndMarchOwned(current,index,member,current.Data,reservation);
        internal static NavigationAuthorityGroup CopyWithMemberAndMarchOwned(NavigationAuthorityGroup current,int index,NavigationAuthorityMember member,Values data,INavStorageReservation reservation)
        {
            if(current==null||member==null||!current.IsImmutableCaptureRoot||!member.IsImmutableCaptureRoot||index<0||index>=current.Members.Count)throw new ArgumentException("Invalid affected member path.");NavStorageAdmission.RequireOwned(reservation,current);NavStorageAdmission.RequireOwned(reservation,member);
            var original=current.Data;
            if(!data.SameIssuance(original))throw new ArgumentException("Member/march write changed immutable issuance.");
            var old=((NavigationAuthorityMember)current.Members[index]).Data;var next=member.Data;
            if(next.Entity!=old.Entity||next.Slot!=old.Slot||next.OrderRevision<current.Data.OrderRevision||next.Phase==GroupMemberPhase.Dead||next.Phase==GroupMemberPhase.Detached||(current.Data.MarchVersion==0?next.Progress<0||next.ProgressRequest!=0:current.Data.MarchVersion==1?next.Progress>0||next.ProgressVersion!=0:next.ProgressVersion!=2))throw new ArgumentException("Scalar member update changed membership/context.");
            NavPagedList<NavOwnedStorage> pages=null;
            try{if(current.memberRoot is NavPagedList<NavOwnedStorage> existing)pages=existing.ForkOwned(reservation);else {pages=NavPagedList<NavOwnedStorage>.Create(reservation.TryReserve);for(int i=0;i<current.Members.Count;i++)pages.Add(current.Members[i]);}
                pages[index]=member;pages.Seal();return Create(data,current.ownerId,current.source,current.originalName,pages,reservation,true,true,current.VersionIdentity,index);
            }finally{pages?.ReleaseStorage();}
        }
        public void WritePhysicalBody(NavPhysicalBodyWriter writer)
        {
            if(!sealedRoot||StorageBytes!=NavStorageLayout.Charge(LayoutBytes))throw new ArgumentException("Invalid group ownership.");Data.Validate(Source);var w=writer.Output;
            w.Write(Data.WorldGeneration);w.Write(Data.GroupId);w.Write(Data.OrderRevision);w.Write(Data.CommandSequence);w.Write(Data.IssuedTick);w.Write(Data.MembershipRevision);w.Write(Data.JobId);w.Write(Data.ActionId);
            w.Write(Data.MarchVersion);w.Write(Data.MarchProfileRevision);w.Write(Data.TeamId);w.Write(Data.TargetId);WorldWire.Number(w,Data.MarchStretch);WorldWire.Number(w,Data.MarchAnchor);w.Write(Data.MarchInitialized);w.Write((int)Data.Origin);w.Write((int)Data.Kind);WorldWire.Write(w,Data.OriginalGoal);w.Write(Data.HasLocation);if(Data.HasLocation)WorldWire.Write(w,Data.Location);
            NavPhysicalMacro.WriteEdge(writer,this,ownerId,true);NavPhysicalMacro.WriteEdge(writer,this,source,true);if(originalName.Owner!=null)originalName.Validate(originalName.Resolve(),this);originalName.Write(writer);NavPhysicalMacro.WriteEdge(writer,this,memberRoot);
        }
        internal static NavigationAuthorityGroup ReadPhysicalBody(NavPhysicalBodyReader reader)
        {
            var r=reader.Input;
            // Stack scalars only: no mutable checkpoint facade is constructed.
            long world=r.ReadInt64(),group=r.ReadInt64(),order=r.ReadInt64(),command=r.ReadInt64(),tick=r.ReadInt64(),membership=r.ReadInt64(),job=r.ReadInt64(),action=r.ReadInt64();
            int march=r.ReadInt32(),revision=r.ReadInt32(),team=r.ReadInt32(),target=r.ReadInt32();double stretch=WorldWire.Number(r),anchor=WorldWire.Number(r);bool initialized=NavPhysicalMacro.ReadBoolean(reader);var origin=(PlayableOrderOrigin)r.ReadInt32();var kind=(PlayableCommandKind)r.ReadInt32();var goal=WorldWire.ReadPoint(r);bool hasLocation=NavPhysicalMacro.ReadBoolean(reader);var location=hasLocation?WorldWire.ReadPoint(r):default;
            var data=new Values(world,group,order,command,tick,membership,job,action,march,revision,team,target,stretch,anchor,initialized,origin,kind,goal,hasLocation,location);
            data.ValidateScalars();var ownerId=NavPhysicalMacro.ReadEdge<NavOwnedText>(reader,true);var source=NavPhysicalMacro.ReadEdge<NavOwnedText>(reader,true);data.Validate(source?.Value);var name=NavNameReference.Read(reader);var members=reader.Edge();
            return Create(data,ownerId,source,name,members,reader.Reservation,false);
        }
        internal void ReleaseStorage(){if(released)return;released=true;Release();}
        internal override void ReleaseOwner()=>ReleaseStorage();
        protected override void ReleaseEdges(){ownerId?.Release();source?.Release();originalName.Owner?.Release();memberRoot?.Release();ownerId=source=null;originalName=default;memberRoot=null;sealedRoot=false;}
    }
    public sealed partial class NavigationSession
    {
        private long groupSequence;
        private IDictionary<long,GroupOrderState> groups=new AuthoritySlotMap<long,GroupOrderState>();
        private IDictionary<int,long> memberGroups=new AuthoritySlotMap<int,long>();
        private GroupOrderState commandGroup;
        private long commandActivationRevision;
        private volatile NavigationAdmission admission;
        private NavigationAdmissionStorage admissionOwner;
        private long admissionPublicationIdentity;
        public NavigationAdmission Admission => admission;
        internal NavigationAdmissionPin PinAdmission()
        {
            using(EnterTransport()){
                if(admissionOwner!=null&&!ReferenceEquals(admissionOwner.Value,admission))throw new InvalidOperationException("Admission owner/facade differs.");
                return new NavigationAdmissionPin(admissionOwner,admission);
            }
        }
        private bool AdmissionMatchesCurrent()
        {
            var previous=admission;
            if(previous==null||previous.PublishedThroughRequestSequence!=requestSequence||
                !ReferenceEquals(previous.Geometry,navigationGeometry)||!ReferenceEquals(previous.Profile,profile)||
                previous.Requests.Count!=pending.Count+probes.Count||previous.Groups.Count!=groups.Count)return false;
            foreach(var request in previous.Requests){
                var map=request.Entity<0?probes:pending;
                if(!map.TryGetValue(request.Entity,out var active)||!ReferenceEquals(active,request))return false;
            }
            foreach(var snapshot in previous.Groups){
                if(!groups.TryGetValue(snapshot.GroupId,out var group)||
                    snapshot.RootOrderRevision!=group.OrderRevision||snapshot.CommandSequence!=group.CommandSequence||
                    snapshot.IssuedTick!=group.IssuedTick||snapshot.MembershipRevision!=group.MembershipRevision||
                    snapshot.OwnerId!=group.OwnerId||snapshot.TeamId!=group.TeamId||snapshot.TargetId!=group.TargetId||
                    snapshot.Source!=group.Source||snapshot.JobId!=group.JobId||snapshot.ActionId!=group.ActionId||
                    snapshot.Origin!=group.Origin||snapshot.Kind!=group.Kind||
                    !snapshot.OriginalGoal.Equals(group.OriginalGoal)||!Nullable.Equals(snapshot.OriginalTerminal,group.OriginalLocation)||
                    snapshot.Members.Count!=group.Members.Length)return false;
                for(int i=0;i<group.Members.Length;i++){
                    var member=group.Members[i];var saved=snapshot.Members[i];
                    if(saved.Entity!=member.Entity||saved.Slot!=member.Slot||saved.Incarnation!=member.Incarnation||
                        saved.ActivationRevision!=member.OrderRevision||saved.MobilityRevision!=member.MobilityRevision||
                        saved.Phase!=member.Phase||saved.FormationReleased!=member.FormationReleased||
                        saved.HasGoal!=member.HasGoal||!Nullable.Equals(saved.AssignedEndpoint,member.GoalLocation)||
                        !ReferenceEquals(saved.PendingRequest,pending.TryGetValue(member.Entity,out var active)?active:null))return false;
                }
            }
            return true;
        }
        private void PublishAdmission()
        {
            authorityOperationIdentity=checked(authorityOperationIdentity+1);
            if(commandGroup!=null||admissionBatchDepth!=0)return; // Publish complete accepted batches only.
            RefreshGroupMembers();
            if(AdmissionMatchesCurrent())return;
            // Legacy callers below still require prepared mutation carriers.
            // This compatibility publisher owns their actual snapshot graph;
            // it is not the atomic Ready/member publication path.
            if(CanOwnAdmissionProjection()){
                long operation=authorityOperationIdentity,publication=admissionPublicationIdentity;
                if(!physicalHostBudget.TryOpenScope(NavigationPreparationKind.Admission,Generation,publication,out var scope))throw new InvalidOperationException("Admission publication refused.");
                using(scope){NavigationAdmissionStorage prepared=null;bool transferred=false;
                    try{prepared=PrepareAdmissionProjection(groups,pending,requestSequence,checked(publication+1),operation,scope,admissionOwner);scope.Attach(prepared);
                        scope.Publish(prepared,_=>{using(EnterTransport()){
                            if(operation!=authorityOperationIdentity||publication!=admissionPublicationIdentity||scope.IsCancelled)throw new InvalidOperationException("Admission publication lost observed source.");
                            transferred=PublishAdmissionOwnerNoThrow(prepared);
                        }});scope.ResolveCommitted();
                    }finally{if(!transferred)prepared?.Release();}
                }
                SignalHost();return;
            }
            var requests=pending.Values.Concat(probes.Values).OrderBy(r=>r.Request).ToArray();
            long nextPublication=checked(admissionPublicationIdentity+1);
            var snapshot=new NavigationAdmission(Generation,requestSequence,terrain,terrain?.Id??NavLocation.FlatSurface,
                terrain?.SurfaceSemanticsVersion??1,navigationGeometry,profile,
                groups.Values.OrderBy(g=>g.GroupId).Select(g=>new NavigationAdmissionGroup(g,
                    id=>pending.TryGetValue(id,out var request)?request:null)).ToArray(),requests,nextPublication);
            admissionPublicationIdentity=nextPublication;PublishLegacyAdmissionNoThrow(snapshot);SignalHost();
        }
        public IReadOnlyList<GroupOrderState> GroupOrders=>groups.Values.OrderBy(g=>g.GroupId).Select(g=>g.Copy()).ToArray();
        internal bool HasGroupMember(int entity)=>memberGroups.ContainsKey(entity);
        public GroupOrderState GroupFor(int entity)=>memberGroups.TryGetValue(entity,out var id)?groups[id].Copy():null;
        internal void PrepareCommandGroup(PlayableCommand command,long revision,long tick,int team,long groupId=0,long issuanceRevision=0,long issuedTick=-1)
        {
            commandActivationRevision=revision;
            if(commandGroup!=null)throw new InvalidOperationException("Nested command group.");
            commandGroup=new GroupOrderState{WorldGeneration=Generation,GroupId=groupId==0?checked(groupSequence+1):groupId,OrderRevision=issuanceRevision==0?revision:issuanceRevision,CommandSequence=command.Sequence,IssuedTick=issuedTick<0?tick:issuedTick,
                OwnerId=command.PlayerId,TeamId=team,Origin=command.Origin,Kind=command.Kind,Source=command.Source,JobId=command.JobId,ActionId=command.ActionId,
                OriginalGoal=command.Target,OriginalLocation=command.TargetLocation,TargetId=command.TargetId,MembershipRevision=1};
        }
        internal void FinishCommandGroup(bool applied,IEnumerable<int> ids)
        {
            // Only this legacy API promises the exact base exception type.
            // Internal publishers retain typed refusals for rollback/retry.
            bool published=false;
            try{FinishCommandGroupCore(applied,ids,out published);}
            catch(NavigationCommandRefused error){
                // Callee finally blocks have now supplied the publication phase.
                if(published)throw;
                throw new InvalidOperationException(error.Message,error);
            }
        }
        private void FinishCommandGroupCore(bool applied,IEnumerable<int> ids,out bool published)
        {
            published=false;
            if(preparedCommandGroup!=null){FinishPreparedCommandGroup(applied,ids,out published);return;}
            if(commandMoveRefused){using(EnterTransport()){commandGroup=null;commandActivationRevision=0;commandMoveRefused=false;}return;}
            if(applied){
                var recipients=ids.Distinct().OrderBy(id=>id).ToArray();
                if(recipients.Length>0){
                    var context=commandGroup;
                    try{FinishOwnedMetadataGroup(recipients,out published);}finally{using(EnterTransport())if(ReferenceEquals(commandGroup,context)){commandGroup=null;commandActivationRevision=0;}}
                    return;
                }
            }
            AdmissionBatch(()=>FinishCommandGroupLocked(applied,ids));
        }
        private void FinishCommandGroupLocked(bool applied,IEnumerable<int> ids)
        {
            var next=commandGroup;commandGroup=null;if(!applied){commandActivationRevision=0;return;}
            BindGroup(next,ids);
            PublishAdmission();
        }
        private void BindGroup(GroupOrderState next,IEnumerable<int> ids)
        {
            var members=new List<GroupMemberState>();
            foreach(int id in ids.Distinct().OrderBy(id=>id)){
                if(!Crowd.TryGet(id,out var actor))throw new InvalidOperationException("Accepted group member is absent.");
                DetachMember(id);
                retainedGoals.TryGetValue(id,out var goal);
                members.Add(new GroupMemberState{Entity=id,Incarnation=actor.Incarnation,OrderRevision=commandActivationRevision==0?next.OrderRevision:commandActivationRevision,MobilityRevision=actor.MobilityRevision,
                    Slot=id,Goal=goal,GoalLocation=retainedGoals.ContainsKey(id)&&Crowd.Locate(goal,actor.Radius,out var location)?(NavLocation?)location:null,HasGoal=retainedGoals.ContainsKey(id),Reservation=reservations.ContainsKey(id)?id:0,
                    Phase=allocating.ContainsKey(id)?GroupMemberPhase.Allocating:actor.Held?GroupMemberPhase.Held:IsPending(id)?GroupMemberPhase.PendingRoute:actor.Moving?GroupMemberPhase.March:GroupMemberPhase.Stopped});
            }
            if(members.Count==0)return;
            if(groups.TryGetValue(next.GroupId,out var existing)){
                if(existing.CommandSequence!=next.CommandSequence||existing.OrderRevision!=next.OrderRevision)throw new InvalidOperationException("Issuance collision.");
                next.MarchVersion=existing.MarchVersion;next.MarchProfileRevision=existing.MarchProfileRevision;
                next.MarchStretch=existing.MarchStretch;next.MarchAnchor=0;next.MarchInitialized=false;
                int nextSlot=existing.Members.Max(m=>m.Slot)+1;
                foreach(var member in SpatialSlotOrder(members,next.OriginalGoal)){member.Slot=nextSlot++;if(next.MarchVersion==2){member.ProgressVersion=2;member.Progress=member.ProgressOffset=Math.Max(0,existing.MarchAnchor);}}
                next.Members=existing.Members.Concat(members).OrderBy(m=>m.Entity).ToArray();next.MembershipRevision=existing.MembershipRevision+1;groups[next.GroupId]=next;
            }else{
                if(marchConfigured){next.MarchVersion=2;next.MarchProfileRevision=marchProfileRevision;next.MarchStretch=marchStretch;}
                int slot=0;foreach(var member in SpatialSlotOrder(members,next.OriginalGoal))member.Slot=slot++;
                foreach(var member in members)if(next.MarchVersion==2)member.ProgressVersion=2;next.Members=members.ToArray();groups.Add(next.GroupId,next);
            }
            groupSequence=Math.Max(groupSequence,next.GroupId);commandActivationRevision=0;
            foreach(var member in members)memberGroups.Add(member.Entity,next.GroupId);
            PublishAdmission();
        }
        private IEnumerable<GroupMemberState> SpatialSlotOrder(IEnumerable<GroupMemberState> members,NavPoint goal)
        {
            var rows=members.Select(m=>new { Member=m, Position=Crowd.TryGet(m.Entity,out var actor)?actor.Position:default(NavPoint) }).ToArray();
            double cx=rows.Average(x=>x.Position.X),cz=rows.Average(x=>x.Position.Z);
            double dx=goal.X-cx,dz=goal.Z-cz,length=Math.Sqrt(dx*dx+dz*dz);
            if(length==0)return rows.OrderBy(x=>x.Member.Entity).Select(x=>x.Member);
            // Sorting once by the transverse coordinate minimizes displacement
            // to neutral relative lanes; identity stays stable across ticks.
            return rows.OrderBy(x=>((x.Position.X-cx)*(-dz)+(x.Position.Z-cz)*dx)/length)
                .ThenBy(x=>x.Member.Entity).Select(x=>x.Member);
        }
        private void BindLegacyGroup(IEnumerable<int> ids,NavPoint goal,bool restoredLegacy=false)
        {
            long id=checked(groupSequence+1);
            BindGroup(new GroupOrderState{WorldGeneration=Generation,GroupId=id,OrderRevision=id,MembershipRevision=1,Kind=PlayableCommandKind.Move,
                Origin=PlayableOrderOrigin.Unknown,OriginalGoal=goal},ids);
            if(restoredLegacy&&groups.TryGetValue(id,out var group)){
                group.MarchVersion=0;group.MarchProfileRevision=0;group.MarchStretch=0;
                group.MarchAnchor=0;group.MarchInitialized=false;
            }
        }
        private void DetachMember(int id)
        {
            if(!memberGroups.TryGetValue(id,out var groupId))return;
            DetachInstalledExecution(id);
            var group=groups[groupId];group.Members=group.Members.Where(m=>m.Entity!=id).ToArray();group.MembershipRevision++;
            group.MarchAnchor=0;group.MarchInitialized=false;
            memberGroups.Remove(id);if(group.Members.Length==0)groups.Remove(groupId);
        }
        private static void RestoreGroupMap<TKey,TValue>(IDictionary<TKey,TValue> map,TKey[] keys,int[] layout)
        {
            if(keys==null||keys.Length!=map.Count||keys.Distinct().Count()!=keys.Length||keys.Any(key=>!map.ContainsKey(key)))throw new ArgumentException("Invalid group map row order.");
            var rows=keys.Select(key=>new KeyValuePair<TKey,TValue>(key,map[key])).ToArray();map.Clear();foreach(var row in rows)map.Add(row.Key,row.Value);NavigationCommandLayout.Restore(map,layout);
        }
        private GroupMemberState Member(int id)
        {return memberGroups.TryGetValue(id,out var groupId)?groups[groupId].Members.First(m=>m.Entity==id):null;}
        private void ResetMarchProgress(int id)
        {
            var member=Member(id);if(member==null||groups[memberGroups[id]].MarchVersion==0)return;
            if(member.ProgressVersion==2)member.ProgressOffset=member.Progress;else member.Progress=0;member.ProgressRequest=0;
            var group=groups[memberGroups[id]];group.MarchAnchor=0;group.MarchInitialized=false;
        }
        private NavigationMemberIdentity RequestIdentity(NavUnit unit)
        {
            var member=Member(unit.Id);var group=commandGroup??(member==null?null:groups[memberGroups[unit.Id]]);
            return new NavigationMemberIdentity(unit.Incarnation,unit.MobilityRevision,group?.GroupId??0,commandGroup!=null?commandActivationRevision:member?.OrderRevision??0,group?.MembershipRevision??0);
        }
        private bool CurrentMember(NavigationRequest request)
        {
            var identity=request.MemberIdentity;
            if(identity==null)return true; // Supported pre-A1 v9 request; its original order/reference checks still apply.
            if(!Crowd.TryGet(request.Entity,out var actor)||actor.Incarnation!=identity.Incarnation||actor.MobilityRevision!=identity.MobilityRevision)return false;
            if(identity.GroupId==0)return true;
            var member=Member(request.Entity);
            return member!=null&&memberGroups[request.Entity]==identity.GroupId&&member.Incarnation==identity.Incarnation&&member.OrderRevision==identity.OrderRevision;
        }
        private void RefreshGroupMembers()
        {
            foreach(int id in memberGroups.Keys.ToArray())RefreshGroupMember(id);
        }
        private void RefreshGroupMember(int id)
        {
                if(groups is NavigationCommandSlots<long,GroupOrderState> slots&&memberGroups.TryGetValue(id,out var ownedGroup)&&slots.OwnerFor(ownedGroup) is NavigationLiveGroup)return;
                var member=Member(id);if(!Crowd.TryGet(id,out var actor)||actor.Incarnation!=member.Incarnation){DetachMember(id);return;}
                member.MobilityRevision=actor.MobilityRevision;
                if(allocating.ContainsKey(id)){member.Phase=GroupMemberPhase.Allocating;return;}member.Reservation=reservations.ContainsKey(id)?id:0;
                if(member.FormationReleased){member.Phase=GroupMemberPhase.Combat;return;}
                member.Phase=allocating.ContainsKey(id)?GroupMemberPhase.Allocating:actor.Held?GroupMemberPhase.Held:IsPending(id)?GroupMemberPhase.PendingRoute:actor.Moving?GroupMemberPhase.March:
                    actor.Outcome==NavigationOutcome.Arrived?GroupMemberPhase.Arrived:actor.Outcome==NavigationOutcome.Unreachable?GroupMemberPhase.Unreachable:
                    actor.Outcome==NavigationOutcome.Blocked||actor.Outcome==NavigationOutcome.Rejected?GroupMemberPhase.Blocked:GroupMemberPhase.Stopped;
        }
        // Called at the post-movement visibility phase; never changes targeting/fire/movement intent.
        internal void SetFormationContact(int entity,bool visibleContact,long tick)
        {
            if(TryOwnedFormationContact(entity,visibleContact,tick).HasValue)return;
            using(EnterTransport()){
                var member=Member(entity);if(member==null)return;
                if(member.FormationReleased!=visibleContact){
                    var group=groups[memberGroups[entity]];
                    group.MarchAnchor=0;group.MarchInitialized=false;
                }
                member.FormationReleased=visibleContact;member.ContactTick=tick;member.ContactPhase="PostMovementVisibility";
                PublishAdmission();
            }
        }
        // A3 consumes this guard to connect forward from the actor's current position, never the old anchor.
        public bool TryForwardResume(long generation,int entity,long groupId,long revision,long incarnation,out NavPoint from,out NavPoint originalGoal)
        {
            from=originalGoal=default(NavPoint);var member=Member(entity);
            if(generation!=Generation||!groups.TryGetValue(groupId,out var group)||(group.Kind!=PlayableCommandKind.Move&&group.Kind!=PlayableCommandKind.AttackMove))return false;
            if(member==null||memberGroups[entity]!=groupId||member.OrderRevision!=revision||member.Incarnation!=incarnation||member.FormationReleased||
                !Crowd.TryGet(entity,out var actor)||actor.Incarnation!=incarnation||actor.Held||member.Phase==GroupMemberPhase.Arrived||member.Phase==GroupMemberPhase.Unreachable)return false;
            from=actor.Position;originalGoal=groups[groupId].OriginalGoal;return true;
        }
        // A failed held refresh deliberately keeps its historical pending
        // request after Stop advances actor mobility. It remains ineligible for
        // live result acceptance; only this exact checkpoint shape is admitted.
        private bool RetainedHeldStopIntent(NavigationRequest request)
        {
            var identity=request.MemberIdentity;if(identity==null||identity.GroupId<1||!Crowd.TryGet(request.Entity,out var actor)||actor.Incarnation!=identity.Incarnation||identity.MobilityRevision>=actor.MobilityRevision||actor.Held||actor.Moving||actor.Outcome!=NavigationOutcome.Stopped||actor.route.Length!=0||actor.localRoute.Length!=0)return false;
            var member=Member(request.Entity);if(member==null||member.Incarnation!=identity.Incarnation||member.OrderRevision!=identity.OrderRevision||!memberGroups.TryGetValue(request.Entity,out long group)||group!=identity.GroupId)return false;
            if(!retainedGoals.TryGetValue(request.Entity,out var goal)||!goal.Equals(request.Goal)||!reservations.TryGetValue(request.Entity,out var reservation)||!reservation.Equals(request.Goal)||!movementIdentities.TryGetValue(request.Entity,out var held)||held!=request.HeldIdentity||CurrentHeldBinding(held,request.Entity))return false;
            return true;
        }
        private bool RetainedHeldStopPendingForRestore(NavigationRequest request,NavigationSessionState state)
        {
            if(!RetainedHeldStopIntent(request))return false;
            // These tables hydrate after group validation. Their saved rows still
            // receive the normal exact BODY/descriptor checks later in restore.
            foreach(int index in state.AllocationRequestIndices??Array.Empty<int>())if(index<0||index>=state.Requests.Length||state.Requests[index].Entity==request.Entity)return false;
            foreach(var installed in state.InstalledExecutions??Array.Empty<NavigationInstalledExecutionState>())if(installed.Entity==request.Entity&&!installed.Detached)return false;
            return true;
        }
        private void RestoreGroups(NavigationSessionState state)
        {
            if(state.GroupLayoutVersion!=0&&state.GroupLayoutVersion!=1||state.GroupLayoutVersion==1&&(state.Groups==null||state.GroupKeys==null||state.MemberGroupKeys==null||state.GroupLayouts==null||state.GroupLayouts.Length!=2)||state.GroupLayoutVersion==0&&(state.GroupKeys!=null||state.MemberGroupKeys!=null||state.GroupLayouts!=null))throw new ArgumentException("Invalid group layout metadata.");
            if(state.Groups==null){ // Current v9 legacy worlds have individual intents; never guess a common command.
                foreach(var unit in Crowd.Units)if(retainedGoals.TryGetValue(unit.Id,out var goal))BindLegacyGroup(new[]{unit.Id},goal,true);
                return;
            }
            if(state.GroupSequence<0)throw new ArgumentException("Invalid group allocator.");
            foreach(var saved in state.Groups){
                if(saved==null||saved.WorldGeneration!=Generation||saved.GroupId<1||saved.GroupId>state.GroupSequence||saved.OrderRevision<1||saved.MembershipRevision<1||saved.CommandSequence<0||saved.IssuedTick<0||
                    saved.MarchVersion<0||saved.MarchVersion>2||saved.MarchVersion==1&&saved.MarchAnchor>0||saved.MarchVersion==2&&saved.MarchAnchor<0||double.IsNaN(saved.MarchAnchor)||double.IsInfinity(saved.MarchAnchor)||
                    (saved.MarchVersion==0?(saved.MarchInitialized||saved.MarchAnchor!=0||saved.MarchStretch!=0||saved.MarchProfileRevision!=0):
                        !marchConfigured||saved.MarchProfileRevision<1||saved.MarchStretch<=0||double.IsNaN(saved.MarchStretch)||double.IsInfinity(saved.MarchStretch)||
                        !saved.MarchInitialized&&saved.MarchAnchor!=0||
                        saved.MarchProfileRevision==marchProfileRevision&&saved.MarchStretch!=marchStretch)||
                    !Enum.IsDefined(typeof(PlayableOrderOrigin),saved.Origin)||!Enum.IsDefined(typeof(PlayableCommandKind),saved.Kind)||saved.Members==null||saved.Members.Length==0||groups.ContainsKey(saved.GroupId)||
                    (saved.Origin==PlayableOrderOrigin.Ai?(string.IsNullOrWhiteSpace(saved.Source)||saved.JobId<1||saved.ActionId<1):(saved.Source!=null||saved.JobId!=0||saved.ActionId!=0)))throw new ArgumentException("Invalid group checkpoint.");
                int previous=-1;var slots=new HashSet<int>();
                foreach(var member in saved.Members){
                    if(member==null||member.ContactTick<0||(saved.MarchVersion==0?member.Progress<0||member.ProgressRequest!=0:saved.MarchVersion==1?member.Progress>0||member.ProgressVersion!=0:member.Progress<0||member.ProgressVersion!=2||member.ProgressOffset<0||member.ProgressOffset>member.Progress)||double.IsNaN(member.Progress)||double.IsInfinity(member.Progress)||member.ProgressRequest<0||member.ProgressRequest>state.RequestSequence||member.Branch<0||member.Reservation<0||member.Entity<=previous||memberGroups.ContainsKey(member.Entity)||member.OrderRevision<saved.OrderRevision||member.Slot<0||!slots.Add(member.Slot)||
                        !Enum.IsDefined(typeof(GroupMemberPhase),member.Phase)||member.Phase==GroupMemberPhase.Dead||member.Phase==GroupMemberPhase.Detached||
                        !Crowd.TryGet(member.Entity,out var actor)||member.Incarnation!=actor.Incarnation||member.MobilityRevision!=actor.MobilityRevision)throw new ArgumentException("Invalid group member checkpoint.");
                    previous=member.Entity;memberGroups.Add(member.Entity,saved.GroupId);
                }
                groups.Add(saved.GroupId,saved.Copy());
            }
            if(state.GroupLayouts!=null){
                if(state.GroupLayouts.Length!=2)throw new ArgumentException("Invalid group layout count.");
                RestoreGroupMap(groups,state.GroupKeys,state.GroupLayouts[0]);RestoreGroupMap(memberGroups,state.MemberGroupKeys,state.GroupLayouts[1]);
            }
            groupSequence=state.GroupSequence;
            foreach(var request in pending.Values)if(!CurrentMember(request)&&!RetainedHeldStopPendingForRestore(request,state))throw new ArgumentException("Invalid pending member identity.");
        }
    }
}
