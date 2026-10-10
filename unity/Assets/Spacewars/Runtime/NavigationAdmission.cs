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
        internal NavigationAdmissionGroup(GroupOrderState group, Func<int,NavigationRequest> pending)
        {
            GroupId = group.GroupId; WorldGeneration = group.WorldGeneration;
            RootOrderRevision = group.OrderRevision; CommandSequence = group.CommandSequence;
            IssuedTick = group.IssuedTick; MembershipRevision = group.MembershipRevision;
            OwnerId = group.OwnerId; TeamId = group.TeamId; TargetId = group.TargetId;
            Source = group.Source; JobId = group.JobId; ActionId = group.ActionId;
            Origin = group.Origin; Kind = group.Kind;
            OriginalTerminal = group.OriginalLocation; OriginalGoal = group.OriginalGoal;
            Members = Array.AsReadOnly(group.Members.OrderBy(m => m.Entity)
                .Select(m => new NavigationAdmissionMember(m, pending(m.Entity))).ToArray());
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
        private readonly HashSet<NavigationRequest> requestSet;
        public static string ExactProfileBinding(NavigationProfile profile)
        {
            if(profile==null)throw new ArgumentNullException(nameof(profile));
            using(var stream=new MemoryStream()){
                using(var writer=new BinaryWriter(stream,Encoding.UTF8,true)){
                    writer.Write(profile.ProfileId);writer.Write(profile.Revision);
                    foreach(var field in NavigationProfile.Metadata)writer.Write(field.Read(profile));
                }
                using(var hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(stream.ToArray())).Replace("-","").ToLowerInvariant();
            }
        }
        internal NavigationAdmission(long generation, long publishedThroughRequestSequence, IPlayableTerrain authoredTerrain, string providerId, int surfaceVersion,
            NavGeometry geometry, NavigationProfile profile, NavigationAdmissionGroup[] groups,
            NavigationRequest[] requests)
        {
            Generation = generation; PublishedThroughRequestSequence = publishedThroughRequestSequence;
            SurfaceProviderId = providerId; SurfaceSemanticsVersion = surfaceVersion;
            // Only sealed, immutable authored maps may cross the worker boundary.
            // Their geometry is still bound and checked per scheduled request.
            AuthoredTerrain = authoredTerrain is ThreeCrossingsMap || authoredTerrain is FoundryMap
                ? authoredTerrain : null;
            if(AuthoredTerrain!=null&&(AuthoredTerrain.Id!=providerId||
                AuthoredTerrain.SurfaceSemanticsVersion!=surfaceVersion||
                AuthoredTerrain.HalfExtent!=geometry.HalfExtent))
                throw new ArgumentException("Authored provider publication differs from authority geometry.");
            Geometry = geometry; Profile = profile; Groups = Array.AsReadOnly(groups);
            Requests = Array.AsReadOnly(requests);
            requestSet = new HashSet<NavigationRequest>(requests);
        }
        public long Generation { get; } public string SurfaceProviderId { get; }
        public IPlayableTerrain AuthoredTerrain { get; }
        public long PublishedThroughRequestSequence { get; }
        public int SurfaceSemanticsVersion { get; }
        public NavGeometry Geometry { get; } public NavigationProfile Profile { get; }
        public IReadOnlyList<NavigationAdmissionGroup> Groups { get; }
        public IReadOnlyList<NavigationRequest> Requests { get; }
        public bool Contains(NavigationRequest request) => requestSet.Contains(request);
    }
}
