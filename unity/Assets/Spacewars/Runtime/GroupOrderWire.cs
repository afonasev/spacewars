using System;
using System.IO;
using Spacewars.Simulation;
using static Spacewars.Runtime.WorldWire;
namespace Spacewars.Runtime
{
    internal static class GroupOrderWire
    {
        internal static void WriteLocation(BinaryWriter w,NavLocation? location){w.Write(location.HasValue);if(location.HasValue){WorldWire.Write(w,location.Value.Position);String(w,location.Value.SurfaceId);}}
        internal static NavLocation? ReadLocation(BinaryReader r)=>Boolean(r)?(NavLocation?)new NavLocation(ReadPoint(r),String(r)):null;
        private static void WriteCorridor(BinaryWriter w,NavCorridorDescriptor c)
        {
            w.Write(c!=null);if(c==null)return;
            w.Write(c.Version);String(w,c.GraphKey);String(w,c.ProviderBinding);String(w,c.ProfileBinding);
            String(w,c.SurfaceProviderId);String(w,c.HoldBinding);w.Write(c.TopologyRevision);w.Write(c.Radius);w.Write(c.GridCell);w.Write((int)c.Provenance);
            w.Write(c.World);w.Write(c.Group);w.Write(c.RootRevision);w.Write(c.CommandSequence);w.Write(c.IssuedTick);w.Write(c.ActivationRevision);
            w.Write(c.Entity);w.Write(c.Incarnation);w.Write(c.Mobility);w.Write(c.Request);w.Write(c.Order);
            WriteLocation(w,c.Origin);WriteLocation(w,c.Endpoint);String(w,c.StableKey);
            Array(w,System.Linq.Enumerable.ToArray(c.Legs),l=>{WriteLocation(w,l.From);WriteLocation(w,l.To);w.Write((int)l.Kind);
                String(w,l.PortalId);w.Write(l.Cost);w.Write((int)l.Section);w.Write(l.Branch);w.Write(l.DownstreamMergeLeg);});
        }
        private static NavCorridorDescriptor ReadCorridor(BinaryReader r)
        {
            if(!Boolean(r))return null;
            int version=r.ReadInt32();string graph=String(r),provider=String(r),profile=String(r),surface=String(r),hold=String(r);
            int topology=r.ReadInt32();double radius=Number(r),gridCell=Number(r);var provenance=EnumValue<NavRouteProvenance>(r);
            long world=r.ReadInt64(),group=r.ReadInt64(),root=r.ReadInt64(),command=r.ReadInt64(),issued=r.ReadInt64(),activation=r.ReadInt64(),
                entity=r.ReadInt64(),incarnation=r.ReadInt64(),mobility=r.ReadInt64(),request=r.ReadInt64(),order=r.ReadInt64();
            var origin=ReadLocation(r);var endpoint=ReadLocation(r);string sealedKey=String(r);
            var legs=Array(r,()=>{
                var from=ReadLocation(r);var to=ReadLocation(r);var kind=EnumValue<NavTypedLegKind>(r);
                string portal=String(r);double cost=Number(r);var section=EnumValue<NavCorridorSection>(r);
                long branch=r.ReadInt64();int merge=r.ReadInt32();
                if(!from.HasValue||!to.HasValue)throw new ArgumentException("Missing typed corridor location.");
                return new NavCorridorLeg(from.Value,to.Value,kind,portal,cost,section,branch,merge);
            });
            if(!origin.HasValue||!endpoint.HasValue)throw new ArgumentException("Missing corridor endpoint.");
            var result=new NavCorridorDescriptor(version,graph,provider,profile,surface,hold,topology,radius,gridCell,provenance,
                world,group,root,command,issued,activation,entity,incarnation,mobility,request,order,
                origin.Value,endpoint.Value,legs);
            if(result.StableKey!=sealedKey)throw new ArgumentException("Corridor descriptor seal mismatch.");
            return result;
        }
        private const int Tag=0x47525031;
        internal static void WriteExtension(BinaryWriter w,NavigationSessionState state)
        {
            w.Write(Tag);w.Write(7);w.Write(state.GroupSequence);
            Array(w,state.Groups??System.Array.Empty<GroupOrderState>(),g=>{
                w.Write(g.WorldGeneration);w.Write(g.GroupId);w.Write(g.OrderRevision);w.Write(g.CommandSequence);w.Write(g.IssuedTick);w.Write(g.MembershipRevision);
                String(w,g.OwnerId);w.Write(g.TeamId);w.Write((int)g.Origin);w.Write((int)g.Kind);String(w,g.Source);w.Write(g.JobId);w.Write(g.ActionId);WorldWire.Write(w,g.OriginalGoal);w.Write(g.TargetId);
                Array(w,g.Members,m=>{w.Write(m.Entity);w.Write(m.Incarnation);w.Write(m.OrderRevision);w.Write(m.MobilityRevision);w.Write(m.Slot);w.Write((int)m.Phase);
                    WorldWire.Write(w,m.Goal);w.Write(m.HasGoal);w.Write(m.FormationReleased);w.Write(m.Branch);w.Write(m.Reservation);w.Write(m.Progress);w.Write(m.ContactTick);String(w,m.ContactPhase);});
            });
            Array(w,state.Requests,r=>{var i=r.MemberIdentity;w.Write(i!=null);if(i==null)return;w.Write(i.Incarnation);w.Write(i.MobilityRevision);w.Write(i.GroupId);w.Write(i.OrderRevision);w.Write(i.MembershipRevision);});
            w.Write(state.SurfaceSemanticsVersion);String(w,state.SurfaceProviderId);
            Array(w,state.Crowd.Units,u=>{w.Write(u.Id);WriteLocation(w,u.Location);WriteLocation(w,u.GoalLocation);});
            Array(w,state.Requests,r=>{WriteLocation(w,r.StartLocation);WriteLocation(w,r.GoalLocation);});
            Array(w,state.Groups,g=>{w.Write(g.GroupId);WriteLocation(w,g.OriginalLocation);Array(w,g.Members,m=>{w.Write(m.Entity);WriteLocation(w,m.GoalLocation);});});
            Array(w,state.Crowd.Units,u=>{w.Write(u.Id);w.Write(u.InstalledGroupId);w.Write(u.InstalledRevision);w.Write(u.InstalledRequest);w.Write(u.InstalledOrder);w.Write(u.FailedRequest>0);if(u.FailedRequest>0){w.Write(u.FailedRequest);w.Write(u.FailedOrder);w.Write(u.FailedGroupId);w.Write(u.FailedRevision);WriteLocation(w,u.FailedGoal);w.Write((int)u.LastFailure);}});
            Array(w,state.GroupKeys,id=>w.Write(id));Array(w,state.MemberGroupKeys,id=>w.Write(id));Array(w,state.GroupLayouts,layout=>Array(w,layout,slot=>w.Write(slot)));
            Array(w,state.ProbeAnswers??System.Array.Empty<NavigationAnswerState>(),a=>w.Write((int)a.Status));
            Array(w,state.AnswerMailbox,a=>w.Write((int)a.Status));
            Array(w,state.BarrierAnswers??System.Array.Empty<NavigationAnswerState>(),a=>w.Write((int)a.Status));
            Array(w,state.TechnicalFailures??System.Array.Empty<NavigationTechnicalFailureState>(),f=>{w.Write(f.Entity);w.Write(f.Request);w.Write((int)f.Status);});
            Array(w,state.Requests,r=>String(w,r.CertifiedProviderBinding));
            Array(w,state.ProbeAnswers??System.Array.Empty<NavigationAnswerState>(),a=>WriteCorridor(w,a.Corridor));
            Array(w,state.AnswerMailbox,a=>WriteCorridor(w,a.Corridor));
            Array(w,state.BarrierAnswers??System.Array.Empty<NavigationAnswerState>(),a=>WriteCorridor(w,a.Corridor));
            Array(w,state.InstalledExecutions??System.Array.Empty<NavigationInstalledExecutionState>(),e=>{
                w.Write(e.Entity);w.Write(e.Detached);WriteCorridor(w,e.Corridor);
                Array(w,e.InstalledRoute??System.Array.Empty<NavPoint>(),p=>WorldWire.Write(w,p));
                String(w,e.ProfileId);w.Write(e.ProfileRevision);
                Array(w,e.ProfileValues??System.Array.Empty<double>(),v=>Number(w,v));
                String(w,e.HeldIdentity);w.Write(e.GeometryIndex);w.Write(e.BaseGeometryIndex);});
            Array(w,state.Groups,g=>{w.Write(g.GroupId);w.Write(g.MarchVersion);w.Write(g.MarchProfileRevision);
                Number(w,g.MarchStretch);Number(w,g.MarchAnchor);w.Write(g.MarchInitialized);
                Array(w,g.Members,m=>{w.Write(m.Entity);w.Write(m.ProgressRequest);});});
        }
        internal static void ReadExtension(BinaryReader r,NavigationSessionState state)
        {
            if(r.BaseStream.Position==r.BaseStream.Length){LegacyAnswerStatuses(state);return;}
            if(r.ReadInt32()!=Tag)throw new ArgumentException("Unsupported navigation group extension.");
            int version=r.ReadInt32();if(version<1||version>7)throw new ArgumentException("Unsupported navigation surface extension.");
            state.GroupSequence=r.ReadInt64();state.Groups=Array(r,()=>new GroupOrderState{
                WorldGeneration=r.ReadInt64(),GroupId=r.ReadInt64(),OrderRevision=r.ReadInt64(),CommandSequence=r.ReadInt64(),IssuedTick=r.ReadInt64(),MembershipRevision=r.ReadInt64(),
                OwnerId=String(r),TeamId=r.ReadInt32(),Origin=EnumValue<PlayableOrderOrigin>(r),Kind=EnumValue<PlayableCommandKind>(r),Source=String(r),JobId=r.ReadInt64(),ActionId=r.ReadInt64(),OriginalGoal=ReadPoint(r),TargetId=r.ReadInt32(),
                Members=Array(r,()=>new GroupMemberState{Entity=r.ReadInt32(),Incarnation=r.ReadInt64(),OrderRevision=r.ReadInt64(),MobilityRevision=r.ReadInt64(),Slot=r.ReadInt32(),Phase=EnumValue<GroupMemberPhase>(r),
                    Goal=ReadPoint(r),HasGoal=Boolean(r),FormationReleased=Boolean(r),Branch=r.ReadInt64(),Reservation=r.ReadInt64(),Progress=Number(r),ContactTick=r.ReadInt64(),ContactPhase=String(r)})});
            var identities=Array(r,()=>Boolean(r)?new NavigationMemberIdentity(r.ReadInt64(),r.ReadInt64(),r.ReadInt64(),r.ReadInt64(),r.ReadInt64()):null);
            if(identities.Length!=state.Requests.Length)throw new ArgumentException("Request group identity count mismatch.");
            for(int i=0;i<identities.Length;i++)state.Requests[i].MemberIdentity=identities[i];
            if(version==1){LegacyAnswerStatuses(state);return;}
            state.SurfaceSemanticsVersion=r.ReadInt32();if(state.SurfaceSemanticsVersion<1)throw new ArgumentException("Invalid present surface semantics version.");state.SurfaceProviderId=String(r);
            int actors=r.ReadInt32();if(actors!=state.Crowd.Units.Length)throw new ArgumentException("Surface actor count mismatch.");
            foreach(var u in state.Crowd.Units){if(r.ReadInt32()!=u.Id)throw new ArgumentException("Surface actor identity mismatch.");u.Location=ReadLocation(r);u.GoalLocation=ReadLocation(r);if(!u.Location.HasValue)throw new ArgumentException("Missing actor surface.");}
            int requests=r.ReadInt32();if(requests!=state.Requests.Length)throw new ArgumentException("Surface request count mismatch.");
            foreach(var request in state.Requests){request.StartLocation=ReadLocation(r);request.GoalLocation=ReadLocation(r);}
            int groups=r.ReadInt32();if(groups!=state.Groups.Length)throw new ArgumentException("Surface group count mismatch.");
            foreach(var g in state.Groups){if(r.ReadInt64()!=g.GroupId)throw new ArgumentException("Surface group identity mismatch.");g.OriginalLocation=ReadLocation(r);int count=r.ReadInt32();if(count!=g.Members.Length)throw new ArgumentException("Surface member count mismatch.");foreach(var m in g.Members){if(r.ReadInt32()!=m.Entity)throw new ArgumentException("Surface member identity mismatch.");m.GoalLocation=ReadLocation(r);}}
            if(version>=3){int count=r.ReadInt32();if(count!=state.Crowd.Units.Length)throw new ArgumentException("Installed execution count mismatch.");foreach(var u in state.Crowd.Units){if(r.ReadInt32()!=u.Id)throw new ArgumentException("Installed execution actor mismatch.");u.InstalledGroupId=r.ReadInt64();u.InstalledRevision=r.ReadInt64();u.InstalledRequest=r.ReadInt64();u.InstalledOrder=r.ReadInt64();if(Boolean(r)){u.FailedRequest=r.ReadInt64();u.FailedOrder=r.ReadInt64();u.FailedGroupId=r.ReadInt64();u.FailedRevision=r.ReadInt64();u.FailedGoal=ReadLocation(r);u.LastFailure=EnumValue<NavigationOutcome>(r);if(u.FailedRequest<1||u.FailedRequest>state.RequestSequence||u.FailedOrder<1||u.FailedGroupId<1||u.FailedGroupId>state.GroupSequence||u.FailedRevision<1||!u.FailedGoal.HasValue||(u.LastFailure!=NavigationOutcome.Unreachable&&u.LastFailure!=NavigationOutcome.Rejected&&u.LastFailure!=NavigationOutcome.Stopped))throw new ArgumentException("Invalid terminal route failure.");}if(u.InstalledGroupId<0||u.InstalledGroupId>state.GroupSequence||u.InstalledRevision<0||u.InstalledRequest<0||u.InstalledRequest>state.RequestSequence||u.InstalledOrder<0)throw new ArgumentException("Invalid installed execution identity.");}}
            if(version>=4){state.GroupLayoutVersion=1;state.GroupKeys=Array(r,()=>r.ReadInt64());state.MemberGroupKeys=Array(r,()=>r.ReadInt32());state.GroupLayouts=Array(r,()=>Array(r,()=>r.ReadInt32()));if(state.GroupLayouts.Length!=2)throw new ArgumentException("Invalid group layout count.");}
            if(version>=5){
                state.TransportVersion=1;
                ReadStatuses(state.ProbeAnswers??System.Array.Empty<NavigationAnswerState>());
                ReadStatuses(state.AnswerMailbox);
                ReadStatuses(state.BarrierAnswers??System.Array.Empty<NavigationAnswerState>());
                state.TechnicalFailures=Array(r,()=>new NavigationTechnicalFailureState{
                    Entity=r.ReadInt32(),Request=r.ReadInt64(),Status=EnumValue<NavSolveStatus>(r)});
                var seen=new System.Collections.Generic.HashSet<int>();
                foreach(var failure in state.TechnicalFailures){
                    var actor=System.Array.Find(state.Crowd.Units,u=>u.Id==failure.Entity);
                    if(!seen.Add(failure.Entity)||actor==null||actor.FailedRequest!=failure.Request||
                        actor.LastFailure!=NavigationOutcome.Rejected||
                        (failure.Status!=NavSolveStatus.InvalidEndpoint&&failure.Status!=NavSolveStatus.BlockedConnector&&failure.Status!=NavSolveStatus.CapacityExceeded))
                        throw new ArgumentException("Invalid technical route failure extension.");
                }
            }else LegacyAnswerStatuses(state);
            if(version>=6){
                state.CorridorWireVersion=1;
                int certified=r.ReadInt32();if(certified!=state.Requests.Length)throw new ArgumentException("Certified request count mismatch.");
                foreach(var request in state.Requests){request.CertifiedProviderBinding=String(r);
                    if(request.CertifiedProviderBinding!=null)NavContract.Sha256(request.CertifiedProviderBinding);}
                ReadCorridors(state.ProbeAnswers??System.Array.Empty<NavigationAnswerState>());
                ReadCorridors(state.AnswerMailbox);
                ReadCorridors(state.BarrierAnswers??System.Array.Empty<NavigationAnswerState>());
                state.InstalledExecutions=Array(r,()=>new NavigationInstalledExecutionState{
                    Entity=r.ReadInt32(),Detached=Boolean(r),Corridor=ReadCorridor(r),
                    InstalledRoute=Array(r,()=>ReadPoint(r)),ProfileId=String(r),ProfileRevision=r.ReadInt32(),
                    ProfileValues=Array(r,()=>Number(r)),HeldIdentity=String(r),
                    GeometryIndex=r.ReadInt32(),BaseGeometryIndex=r.ReadInt32()});
            }else{state.CorridorWireVersion=0;state.InstalledExecutions=System.Array.Empty<NavigationInstalledExecutionState>();}
            if(version>=7){
                int count=r.ReadInt32();if(count!=state.Groups.Length)throw new ArgumentException("March group count mismatch.");
                foreach(var group in state.Groups){
                    if(r.ReadInt64()!=group.GroupId)throw new ArgumentException("March group identity mismatch.");
                    group.MarchVersion=r.ReadInt32();group.MarchProfileRevision=r.ReadInt32();
                    group.MarchStretch=Number(r);group.MarchAnchor=Number(r);group.MarchInitialized=Boolean(r);
                    int members=r.ReadInt32();if(members!=group.Members.Length)throw new ArgumentException("March member count mismatch.");
                    foreach(var member in group.Members){if(r.ReadInt32()!=member.Entity)throw new ArgumentException("March member identity mismatch.");member.ProgressRequest=r.ReadInt64();}
                }
            }

            void ReadCorridors(NavigationAnswerState[] answers)
            {
                int count=r.ReadInt32();if(count!=answers.Length)throw new ArgumentException("Corridor answer count mismatch.");
                foreach(var answer in answers){answer.Corridor=ReadCorridor(r);
                    if(answer.Corridor!=null&&answer.Status!=NavSolveStatus.Ready)throw new ArgumentException("Corridor on failed answer.");}
            }

            void ReadStatuses(NavigationAnswerState[] answers)
            {
                int count=r.ReadInt32();if(count!=answers.Length)throw new ArgumentException("Route status count mismatch.");
                foreach(var answer in answers){answer.Status=EnumValue<NavSolveStatus>(r);
                    if(answer.Status==NavSolveStatus.Pending||(answer.Status==NavSolveStatus.Ready)!=(answer.Route.Length>0))throw new ArgumentException("Invalid route status.");}
            }
        }
        private static void LegacyAnswerStatuses(NavigationSessionState state)
        {
            foreach(var array in new[]{state.ProbeAnswers??System.Array.Empty<NavigationAnswerState>(),state.AnswerMailbox,
                state.BarrierAnswers??System.Array.Empty<NavigationAnswerState>()})
                foreach(var answer in array)answer.Status=answer.Route.Length>0?NavSolveStatus.Ready:NavSolveStatus.UnreachableInGraph;
        }
    }
}
