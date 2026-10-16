using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    // Detached authority publication. A route worker may retain this object;
    // no member points back to mutable group, crowd or session state.
    public sealed class NavigationAdmissionMember
    {
        internal NavigationAdmissionMember(NavigationAuthorityMember source, NavigationRequest request)
        {
            var member=source.Data;
            Entity=member.Entity;Slot=member.Slot;Incarnation=member.Incarnation;
            ActivationRevision=member.OrderRevision;MobilityRevision=member.MobilityRevision;
            AssignedEndpoint=source.GoalLocation;HasGoal=member.HasGoal;
            Phase=member.Phase;FormationReleased=member.FormationReleased;
            PendingRequest=request;HoldBinding=request?.HeldIdentity;
            RequestProfile=request?.Profile;MovementGeometry=request?.Geometry;
        }
        internal NavigationAdmissionMember(GroupMemberState member, NavigationRequest request)
        {
            Entity = member.Entity; Slot = member.Slot; Incarnation = member.Incarnation;
            ActivationRevision = member.OrderRevision; MobilityRevision = member.MobilityRevision;
            AssignedEndpoint = member.GoalLocation; HasGoal = member.HasGoal;
            Phase = member.Phase; FormationReleased = member.FormationReleased;
            PendingRequest = request; HoldBinding = request?.HeldIdentity;
            RequestProfile = request?.Profile; MovementGeometry = request?.Geometry;
        }
        public int Entity { get; } public int Slot { get; }
        public long Incarnation { get; } public long ActivationRevision { get; }
        public long MobilityRevision { get; }
        public bool HasGoal { get; } public NavLocation? AssignedEndpoint { get; }
        public GroupMemberPhase Phase { get; } public bool FormationReleased { get; }
        public NavigationRequest PendingRequest { get; }
        public string HoldBinding { get; } public NavigationProfile RequestProfile { get; }
        public NavGeometry MovementGeometry { get; }
    }
    public sealed class NavigationAdmissionGroup
    {
        internal NavigationAdmissionGroup(NavigationAuthorityGroup source,IReadOnlyList<NavigationAdmissionMember> members)
        {
            var group=source.Data;
            GroupId=group.GroupId;WorldGeneration=group.WorldGeneration;
            RootOrderRevision=group.OrderRevision;CommandSequence=group.CommandSequence;
            IssuedTick=group.IssuedTick;MembershipRevision=group.MembershipRevision;
            OwnerId=source.OwnerIdOwner?.Value;TeamId=group.TeamId;TargetId=group.TargetId;
            Source=source.SourceOwner?.Value;JobId=group.JobId;ActionId=group.ActionId;
            Origin=group.Origin;Kind=group.Kind;OriginalTerminal=source.OriginalLocation;
            OriginalGoal=group.OriginalGoal;Members=members;
        }
        internal NavigationAdmissionGroup(GroupOrderState group, Func<int,NavigationRequest> pending)
        {
            GroupId = group.GroupId; WorldGeneration = group.WorldGeneration;
            RootOrderRevision = group.OrderRevision; CommandSequence = group.CommandSequence;
            IssuedTick = group.IssuedTick; MembershipRevision = group.MembershipRevision;
            OwnerId = group.OwnerId; TeamId = group.TeamId; TargetId = group.TargetId;
            Source = group.Source; JobId = group.JobId; ActionId = group.ActionId;
            Origin = group.Origin; Kind = group.Kind;
            OriginalTerminal = group.OriginalLocation; OriginalGoal = group.OriginalGoal;
            // The admission graph reserves exactly this array and these
            // records. Sorting in place avoids hidden LINQ staging buffers.
            var members = new NavigationAdmissionMember[group.Members.Length];
            for (int i = 0; i < members.Length; i++) {
                var member = group.Members[i];
                members[i] = new NavigationAdmissionMember(member, pending(member.Entity));
            }
            for (int i = 1; i < members.Length; i++) {
                var member = members[i]; int j = i;
                while (j > 0 && members[j - 1].Entity > member.Entity) {
                    members[j] = members[j - 1]; j--;
                }
                members[j] = member;
            }
            Members = Array.AsReadOnly(members);
        }
        public long GroupId { get; } public long WorldGeneration { get; }
        public long RootOrderRevision { get; } public long CommandSequence { get; }
        public long IssuedTick { get; } public long MembershipRevision { get; }
        public string OwnerId { get; } public PlayableOrderOrigin Origin { get; }
        public int TeamId { get; } public int TargetId { get; }
        public string Source { get; } public long JobId { get; } public long ActionId { get; }
        public PlayableCommandKind Kind { get; }
        public NavLocation? OriginalTerminal { get; } public NavPoint OriginalGoal { get; }
        public IReadOnlyList<NavigationAdmissionMember> Members { get; }
    }
    public sealed class NavigationAdmission
    {
        // Shared exact flat provider factory for host admission and authority
        // qualification; both sides must derive the same binding from geometry.
        public static LayeredNavigationProvider CertifiedFlatProvider(NavGeometry geometry)
        {
            double h=geometry.HalfExtent;
            var bounds=new NavPolygon(new[]{new NavPoint(-h,-h),new NavPoint(h,-h),new NavPoint(h,h),new NavPoint(-h,h)});
            var area=new NavTraversalArea(bounds,h,geometry.Obstacles);
            string digest;
            using(var stream=new MemoryStream()){
                using(var writer=new BinaryWriter(stream)){
                    writer.Write(h);writer.Write(geometry.Revision);writer.Write(geometry.Obstacles.Count);
                    foreach(var obstacle in geometry.Obstacles){
                        writer.Write(obstacle.MinX);writer.Write(obstacle.MinZ);writer.Write(obstacle.MaxX);writer.Write(obstacle.MaxZ);
                        writer.Write(obstacle.CircleRadius);writer.Write(obstacle.Bottom);writer.Write(obstacle.Top);
                        writer.Write(obstacle.Polygon!=null);
                        if(obstacle.Polygon!=null){writer.Write(obstacle.Polygon.Vertices.Count);
                            foreach(var vertex in obstacle.Polygon.Vertices){writer.Write(vertex.X);writer.Write(vertex.Z);}}
                    }
                }
                using(var hash=SHA256.Create())digest=BitConverter.ToString(hash.ComputeHash(stream.ToArray())).Replace("-","").ToLowerInvariant();
            }
            return new LayeredNavigationProvider("flat",1,1,geometry.Revision,digest,
                new[]{new NavLayerSurface(NavLocation.FlatSurface,"ground",0,1,area)},Array.Empty<NavLayerPortal>());
        }
        private readonly IReadOnlyList<NavigationRequest> requestLookup;
        public static string ExactProfileBinding(NavigationProfile profile)
        {
            if(profile==null)throw new ArgumentNullException(nameof(profile));
            using(var stream=new MemoryStream()){
                using(var writer=new BinaryWriter(stream,Encoding.UTF8,true)){
                    writer.Write(profile.ProfileId);writer.Write(profile.Revision);
                    foreach(var field in NavigationProfile.Metadata)writer.Write(field.Read(profile));profile.WriteCrowdBinding(writer);
                }
                using(var hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(stream.ToArray())).Replace("-","").ToLowerInvariant();
            }
        }
        internal NavigationAdmission(long generation, long publishedThroughRequestSequence, IPlayableTerrain authoredTerrain, string providerId, int surfaceVersion,
            NavGeometry geometry, NavigationProfile profile, NavigationAdmissionGroup[] groups,
            NavigationRequest[] requests,long publicationIdentity=0)
        {
            if(publicationIdentity<0)throw new ArgumentException("Invalid admission publication identity.");
            PublicationIdentity=publicationIdentity;
            Generation = generation; PublishedThroughRequestSequence = publishedThroughRequestSequence;
            SurfaceProviderId = providerId; SurfaceSemanticsVersion = surfaceVersion;
            // Only sealed, immutable authored maps may cross the worker boundary.
            // Their geometry is still bound and checked per scheduled request.
            AuthoredTerrain = PartitionNavigationProvider.SupportsTerrain(authoredTerrain)
                ? authoredTerrain : null;
            if(AuthoredTerrain!=null&&(AuthoredTerrain.Id!=providerId||
                AuthoredTerrain.SurfaceSemanticsVersion!=surfaceVersion||
                AuthoredTerrain.HalfExtent!=geometry.HalfExtent))
                throw new ArgumentException("Authored provider publication differs from authority geometry.");
            Geometry = geometry; Profile = profile; Groups = Array.AsReadOnly(groups);
            Requests = Array.AsReadOnly(requests);
            requestLookup=(NavigationRequest[])requests.Clone();
            Array.Sort((NavigationRequest[])requestLookup,(left,right)=>left.Request.CompareTo(right.Request));
        }
        internal NavigationAdmission(long generation,long sequence,IPlayableTerrain terrain,string providerId,int surfaceVersion,
            NavGeometry geometry,NavigationProfile profile,IReadOnlyList<NavigationAdmissionGroup> groups,
            IReadOnlyList<NavigationRequest> requests,long publicationIdentity)
        {
            if(publicationIdentity<0)throw new ArgumentException("Invalid admission publication identity.");
            PublicationIdentity=publicationIdentity;Generation=generation;PublishedThroughRequestSequence=sequence;
            SurfaceProviderId=providerId;SurfaceSemanticsVersion=surfaceVersion;
            AuthoredTerrain=PartitionNavigationProvider.SupportsTerrain(terrain)?terrain:null;
            if(AuthoredTerrain!=null&&(AuthoredTerrain.Id!=providerId||AuthoredTerrain.SurfaceSemanticsVersion!=surfaceVersion||AuthoredTerrain.HalfExtent!=geometry.HalfExtent))throw new ArgumentException("Authored provider publication differs from authority geometry.");
            Geometry=geometry;Profile=profile;Groups=groups;Requests=requests;requestLookup=requests;
        }
        internal long PublicationIdentity {get;}
        public long Generation { get; } public string SurfaceProviderId { get; }
        public IPlayableTerrain AuthoredTerrain { get; }
        public long PublishedThroughRequestSequence { get; }
        public int SurfaceSemanticsVersion { get; }
        public NavGeometry Geometry { get; } public NavigationProfile Profile { get; }
        public IReadOnlyList<NavigationAdmissionGroup> Groups { get; }
        public IReadOnlyList<NavigationRequest> Requests { get; }
        public bool Contains(NavigationRequest request)
        {
            if(request==null)return false;int low=0,high=requestLookup.Count-1;
            while(low<=high){int middle=low+(high-low)/2;long sequence=requestLookup[middle].Request;if(sequence<request.Request)low=middle+1;else if(sequence>request.Request)high=middle-1;else {for(int i=middle;i>=0&&requestLookup[i].Request==sequence;i--)if(ReferenceEquals(requestLookup[i],request))return true;for(int i=middle+1;i<requestLookup.Count&&requestLookup[i].Request==sequence;i++)if(ReferenceEquals(requestLookup[i],request))return true;return false;}}return false;
        }
        internal static long ConcreteLayoutBytes(IEnumerable<GroupOrderState> groups,int requestCount)
        {
            long bytes=NavStorageLayout.ObjectBytesBound(typeof(NavigationAdmission))+2*NavStorageLayout.ObjectBytesBound(typeof(System.Collections.ObjectModel.ReadOnlyCollection<NavigationRequest>))+2*32L+16L*requestCount;
            int groupCount=0;foreach(var group in groups){groupCount++;bytes=checked(bytes+NavStorageLayout.ObjectBytesBound(typeof(NavigationAdmissionGroup))+NavStorageLayout.ObjectBytesBound(typeof(System.Collections.ObjectModel.ReadOnlyCollection<NavigationAdmissionMember>))+32L+group.Members.Length*(8L+NavStorageLayout.ObjectBytesBound(typeof(NavigationAdmissionMember))));}
            return checked(bytes+32L+8L*groupCount);
        }
    }
}
