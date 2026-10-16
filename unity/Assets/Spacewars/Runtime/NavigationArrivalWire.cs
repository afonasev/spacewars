using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Spacewars.Simulation;
using static Spacewars.Runtime.WorldWire;

namespace Spacewars.Runtime
{
    public sealed partial class NavigationSession
    {
        private static void ArrivalLocation(BinaryWriter w,NavLocation location)=>GroupOrderWire.WriteLocation(w,location);
        private static NavLocation ArrivalLocation(BinaryReader r)=>GroupOrderWire.ReadLocation(r)??throw new ArgumentException("Missing arrival location.");
        private static void WriteArrivalIntent(BinaryWriter w,NavigationRequest r)
        {
            WorldWire.Write(w,new NavigationRequestState{Session=r.Session,Request=r.Request,Entity=r.Entity,Order=r.Order,
                ProfileId=r.ProfileId,ProfileRevision=r.ProfileRevision,ProfileValues=SaveProfile(r.Profile),Geometry=SaveGeometry(r.Geometry),
                BaseGeometry=SaveGeometry(r.BaseGeometry),Start=r.Start,Goal=r.Goal,HeldIdentity=r.HeldIdentity});
            var i=r.MemberIdentity;w.Write(i.Incarnation);w.Write(i.MobilityRevision);w.Write(i.GroupId);w.Write(i.OrderRevision);w.Write(i.MembershipRevision);
            ArrivalLocation(w,r.StartLocation.Value);ArrivalLocation(w,r.GoalLocation.Value);
        }
        private NavigationRequest ReadArrivalIntent(BinaryReader r)
        {
            var row=ReadNavigationRequestState(r);if(row==null||row.Session!=Generation||row.Request<1||row.Request>requestSequence||row.Entity<0||row.Order<1)throw new ArgumentException("Invalid arrival intent.");
            var i=new NavigationMemberIdentity(r.ReadInt64(),r.ReadInt64(),r.ReadInt64(),r.ReadInt64(),r.ReadInt64());
            if(i.Incarnation<1||i.MobilityRevision<0||i.GroupId<1||i.OrderRevision<1||i.MembershipRevision<1)throw new ArgumentException("Invalid arrival owner.");
            var from=ArrivalLocation(r);var to=ArrivalLocation(r);
            if(!from.Position.Equals(row.Start)||!to.Position.Equals(row.Goal))throw new ArgumentException("Arrival typed intent mismatch.");
            var movement=SameGeometry(row.Geometry,navigationGeometry)?navigationGeometry:LoadGeometry(row.Geometry);
            var basis=SameGeometry(row.BaseGeometry,navigationGeometry)?navigationGeometry:LoadGeometry(row.BaseGeometry);
            return new NavigationRequest(row.Session,row.Request,row.Entity,row.Order,LoadProfile(row),movement,row.Start,row.Goal,basis,row.HeldIdentity,i,from,to);
        }
        private static string ArrivalProviderAliasBinding(long input,long[] working)=>NavContract.Digest(w=>{w.Write(input);w.Write(working.Length);foreach(long id in working)w.Write(id);});
        private void WriteArrivalAuthority(BinaryWriter w)
        {
                w.Write(0x41525231);w.Write(committedHostRoot!=null&&physicalArrivalEnabled?4:3);w.Write(arrivalSequence);w.Write(arrivalCommitSequence);
                w.Write(arrivalProviderSequence);w.Write(arrivalLeaseSequence);w.Write(arrivalAcquisitionSequence);w.Write(arrivalRetirementSequence);w.Write(arrivalReleaseSequence);
                w.Write(ArrivalProviderCapacity(arrivalProviders));w.Write(ArrivalProviderCapacity(arrivalProviderIds));w.Write(ArrivalLeaseCapacity);
                Array(w,arrivalAttempts.Values.ToArray(),a=>{
                    w.Write(a.Id);w.Write(a.Group);w.Write(a.Epoch);w.Write(a.Published);w.Write((int)a.SnapshotStatus);String(w,a.Frontier);String(w,a.SnapshotPhase);w.Write(a.SnapshotCandidate);w.Write(a.SnapshotOccupancy);w.Write(a.SnapshotMember);w.Write(a.Retiring);String(w,a.RetirementReason??"");w.Write(a.LegacyProviderAccounting);w.Write(a.Retirement);w.Write(a.Retiring&&a.Working||a.RestoredRetiringCompute);
                    Array(w,a.Intents,r=>WriteArrivalIntent(w,r));
                    var input=a.Input;String(w,input.TopologyIdentity);String(w,input.ProfileIdentity);String(w,input.StableKey);
                    String(w,input.Source.Id);w.Write(input.Source.Revision);ArrivalLocation(w,input.Source.Requested);
                    Array(w,input.Source.Candidates.ToArray(),p=>ArrivalLocation(w,p));
                    // Each initial immutable working binding is reconstructed from its
                    // exact saved geometry/profile/HOLD rather than current actor state.
                    Array(w,input.Members.ToArray(),m=>{
                        w.Write(m.EntityId);w.Write(m.Incarnation);w.Write(m.Order);w.Write(m.MobilityRevision);ArrivalLocation(w,m.Origin);
                        String(w,m.Clearance.Id);w.Write(m.Clearance.Revision);String(w,m.Clearance.MobilityClass);w.Write(m.Clearance.MobilityRevision);Number(w,m.Radius);
                        w.Write(m.WorldGeneration);w.Write(m.GroupId);w.Write(m.RootOrderRevision);w.Write(m.CommandSequence);w.Write(m.IssuedTick);
                        w.Write(m.ActivationRevision);w.Write(m.MemberOrderRevision);w.Write(m.RequestSequence);String(w,m.HoldBinding);
                    });
                    String(w,input.ProviderIdentity);String(w,input.Profile.ExactProfileBinding);Number(w,input.Profile.GridCell);
                    w.Write(a.InputProviderId);Array(w,a.WorkingProviderIds,id=>w.Write(id));String(w,ArrivalProviderAliasBinding(a.InputProviderId,a.WorkingProviderIds));
                    Array(w,SaveProfile(input.ArrivalProfile),p=>Number(w,p));String(w,input.ArrivalProfile.ProfileId);w.Write(input.ArrivalProfile.Revision);
                    w.Write(input.Limits.MaxSamples);w.Write(input.Limits.MaxNodes);w.Write(input.Limits.MaxArcs);w.Write(input.MaxLabels);
                    Array(w,input.Occupancy.Occupants.ToArray(),o=>{
                        w.Write((int)o.Kind);w.Write(o.ActorId);w.Write(o.Incarnation);ArrivalLocation(w,o.Location);Number(w,o.Radius);w.Write(o.BlocksPlacement);String(w,o.Identity);
                        w.Write(o.ReservationKind.HasValue);if(o.ReservationKind.HasValue)w.Write((int)o.ReservationKind.Value);
                        w.Write(o.RequestSequence);w.Write(o.HistoricalExecution);GroupOrderWire.WriteLocation(w,o.ActorTarget);
                    });
                    Array(w,a.Journal.ToArray(),s=>{w.Write(s.Epoch);w.Write(s.External);w.Write(s.Allocation);w.Write(s.Graph);w.Write(s.Field);w.Write(s.Connectors);w.Write(s.Bookkeeping);w.Write(s.Maintenance);});
                    WriteReadyArrival(w,a.SnapshotStatus==NavArrivalStatus.Ready?a.Work.SaveReady():null);
                    Array(w,a.Projections?.OrderBy(p=>p.Key).ToArray()??System.Array.Empty<KeyValuePair<long,ArrivalProjection>>(),p=>{
                        w.Write(p.Key);var s=p.Value;w.Write(s.Leg);w.Write(s.Replay);ArrivalLocation(w,s.Expected);ArrivalLocation(w,s.At);
                        Array(w,s.Points,v=>WorldWire.Write(w,v));Array(w,s.Typed,v=>String(w,v));Array(w,s.Observed,v=>String(w,v));
                    });
                });
                Array(w,arrivalProjectionLeases.Values.OrderBy(l=>l.Acquisition).ToArray(),l=>{
                    foreach(var value in new[]{l.Token,l.Acquisition,l.Retirement,l.World,l.Attempt,l.Request,l.Entity,l.Incarnation,l.Order,l.ProviderId})w.Write(value);
                    w.Write(l.Retiring);String(w,l.Reason??"");String(w,l.Binding);w.Write(l.PointsCapacity);w.Write(l.TypedCapacity);w.Write(l.ObservedCapacity);
                });
                Array(w,arrivalLedger.OrderBy(row=>row.Key).Select(row=>row.Value).ToArray(),s=>{
                    foreach(var n in new[]{s.CommitOrder,s.Id,s.World,s.Group,s.Root,s.Command,s.IssuedTick,s.Activation,s.Entity,s.Incarnation,s.Mobility,s.Request,s.Order})w.Write(n);
                    ArrivalLocation(w,s.Endpoint);Number(w,s.Radius);String(w,s.Proof);String(w,s.Provider);String(w,s.Profile);String(w,s.Hold);w.Write((int)s.State);
                });
        }
        // This writer consumes sealed capture leaves only. Live Session tables
        // may change while stream growth/admission callbacks execute off gate.
        private static void WriteFrozenArrivalAuthority(BinaryWriter w,NavigationPreparedCapture capture,INavStorageReservation reservation)
        {
            var catalog=capture.State.CaptureArrivalCatalog;var leases=capture.State.CaptureLeaseCatalog;var d=catalog.Data;
            if((leases?.Count??0)!=catalog.LeaseCount)throw new ArgumentException("Frozen arrival lease coverage differs from catalog.");
            w.Write(0x41525231);w.Write(4);w.Write(d.Attempts);w.Write(d.Commits);w.Write(d.Providers);w.Write(d.Leases);w.Write(d.Acquisitions);w.Write(d.Retirements);w.Write(d.Releases);
            w.Write(catalog.ReferenceCapacity);w.Write(catalog.IdCapacity);w.Write(capture.WireLeaseCapacity);
            w.Write(catalog.AttemptCount);
            for(int n=0;n<catalog.AttemptCount;n++){
                NavigationArrivalAttemptSnapshot a=null;long id=catalog.SortedAttempt(n);for(int i=0;i<catalog.AttemptCount;i++)if(catalog.Attempt(i).Data.Id==id){a=catalog.Attempt(i);break;}
                if(a==null)throw new ArgumentException("Missing frozen arrival attempt.");var v=a.Data;var input=a.Work.PhysicalInput;
                w.Write(v.Id);w.Write(v.Group);w.Write(v.Epoch);w.Write(v.Published);w.Write((int)v.Status);String(w,a.Frontier.Value);String(w,a.Phase.Value);w.Write(v.Candidate);w.Write(v.Occupancy);w.Write(v.Member);w.Write(v.Retiring);String(w,a.CurrentReason??"");w.Write(v.LegacyProviderAccounting);w.Write(v.Retirement);w.Write(v.RetiringCompute);
                w.Write(a.Intents.Requests.Length);foreach(var intent in a.Intents.Requests)WriteFrozenArrivalIntent(w,intent);
                String(w,input.TopologyIdentity);String(w,input.ProfileIdentity);String(w,input.StableKey);String(w,input.Source.Id);w.Write(input.Source.Revision);ArrivalLocation(w,input.Source.Requested);
                w.Write(input.Source.Candidates.Count);for(int i=0;i<input.Source.Candidates.Count;i++)ArrivalLocation(w,input.Source.Candidates[i]);
                w.Write(input.Members.Count);for(int i=0;i<input.Members.Count;i++){var m=input.Members[i];
                    w.Write(m.EntityId);w.Write(m.Incarnation);w.Write(m.Order);w.Write(m.MobilityRevision);ArrivalLocation(w,m.Origin);String(w,m.Clearance.Id);w.Write(m.Clearance.Revision);String(w,m.Clearance.MobilityClass);w.Write(m.Clearance.MobilityRevision);Number(w,m.Radius);
                    w.Write(m.WorldGeneration);w.Write(m.GroupId);w.Write(m.RootOrderRevision);w.Write(m.CommandSequence);w.Write(m.IssuedTick);w.Write(m.ActivationRevision);w.Write(m.MemberOrderRevision);w.Write(m.RequestSequence);String(w,m.HoldBinding);
                }
                String(w,input.ProviderIdentity);String(w,input.Profile.ExactProfileBinding);Number(w,input.Profile.GridCell);w.Write(v.InputProviderId);w.Write(a.WorkingCount);for(int i=0;i<a.WorkingCount;i++)w.Write(a.WorkingId(i));
                using(NavStorageScratch.Reserve(reservation,NavContract.DigestScratchBytes(checked(12L+8L*a.WorkingCount))))String(w,NavContract.Digest(inner=>{inner.Write(v.InputProviderId);inner.Write(a.WorkingCount);for(int i=0;i<a.WorkingCount;i++)inner.Write(a.WorkingId(i));}));
                WriteFrozenArrivalProfile(w,input.ArrivalProfile);String(w,input.ArrivalProfile.ProfileId);w.Write(input.ArrivalProfile.Revision);w.Write(input.Limits.MaxSamples);w.Write(input.Limits.MaxNodes);w.Write(input.Limits.MaxArcs);w.Write(input.MaxLabels);
                w.Write(input.Occupancy.Occupants.Count);for(int i=0;i<input.Occupancy.Occupants.Count;i++){var o=input.Occupancy.Occupants[i];
                    w.Write((int)o.Kind);w.Write(o.ActorId);w.Write(o.Incarnation);ArrivalLocation(w,o.Location);Number(w,o.Radius);w.Write(o.BlocksPlacement);String(w,o.Identity);w.Write(o.ReservationKind.HasValue);if(o.ReservationKind.HasValue)w.Write((int)o.ReservationKind.Value);w.Write(o.RequestSequence);w.Write(o.HistoricalExecution);GroupOrderWire.WriteLocation(w,o.ActorTarget);
                }
                w.Write(a.History.Count);for(int i=0;i<a.History.Count;i++){var h=a.History[i];w.Write(h.Epoch);w.Write(h.External);w.Write(h.Allocation);w.Write(h.Graph);w.Write(h.Field);w.Write(h.Connectors);w.Write(h.Bookkeeping);w.Write(h.Maintenance);}
                WriteFrozenReadyArrival(w,a,reservation);
                w.Write(0); // Physical arrival attempts do not own legacy projections.
            }
            w.Write(leases?.Count??0);for(int n=0;n<(leases?.Count??0);n++){
                NavigationArrivalLeaseSnapshot leaf=null;long token=leases.AcquisitionToken(n);for(int i=0;i<leases.Count;i++)if(leases.Lease(i).Data.Token==token){leaf=leases.Lease(i);break;}
                if(leaf==null)throw new ArgumentException("Missing frozen arrival lease.");var l=leaf.Data;
                w.Write(l.Token);w.Write(l.Acquisition);w.Write(l.Retirement);w.Write(l.World);w.Write(l.Attempt);w.Write(l.Request);w.Write(l.Entity);w.Write(l.Incarnation);w.Write(l.Order);w.Write(l.Provider);w.Write(l.Retiring);String(w,leaf.CurrentReason??"");String(w,leaf.Binding.Value);w.Write(0);w.Write(0);w.Write(0);
            }
            w.Write(capture.LedgerRows.Length);foreach(var row in capture.LedgerRows){
                w.Write(row.CommitOrder);w.Write(row.Id);w.Write(row.World);w.Write(row.Group);w.Write(row.Root);w.Write(row.Command);w.Write(row.IssuedTick);w.Write(row.Activation);w.Write(row.Entity);w.Write(row.Incarnation);w.Write(row.Mobility);w.Write(row.Request);w.Write(row.Order);
                ArrivalLocation(w,row.Endpoint);Number(w,row.Radius);String(w,row.Proof);String(w,row.Provider);String(w,row.Profile);String(w,row.Hold);w.Write((int)row.State);
            }
        }
        private static void WriteFrozenArrivalProfile(BinaryWriter w,NavigationProfile profile)
        {w.Write(profile.WireValueCount);for(int i=0;i<profile.WireValueCount;i++)Number(w,profile.WireValueAt(i));}
        private static void WriteFrozenArrivalGeometry(BinaryWriter w,NavGeometry geometry)
        {w.Write(true);Number(w,geometry.HalfExtent);w.Write(geometry.Revision);w.Write(geometry.Obstacles.Count);for(int i=0;i<geometry.Obstacles.Count;i++)WorldWire.Write(w,geometry.Obstacles[i]);}
        private static void WriteFrozenArrivalIntent(BinaryWriter w,NavigationRequest r)
        {
            w.Write(true);w.Write(r.Session);w.Write(r.Request);w.Write(r.Order);w.Write(r.Entity);WorldWire.Write(w,r.Start);WorldWire.Write(w,r.Goal);String(w,r.ProfileId);w.Write(r.ProfileRevision);WriteFrozenArrivalProfile(w,r.Profile);WriteFrozenArrivalGeometry(w,r.Geometry);WriteFrozenArrivalGeometry(w,r.BaseGeometry);String(w,r.HeldIdentity);w.Write(false);w.Write(false);w.Write(0);w.Write(0);
            var m=r.MemberIdentity;w.Write(m.Incarnation);w.Write(m.MobilityRevision);w.Write(m.GroupId);w.Write(m.OrderRevision);w.Write(m.MembershipRevision);ArrivalLocation(w,r.StartLocation.Value);ArrivalLocation(w,r.GoalLocation.Value);
        }
        private void RestoreArrivalAuthority(NavigationSessionState state,NavigationRequest[] requests,NavigationHostRoot decodedHost=null,NavigationArrivalCatalog catalog=null,NavigationArrivalLeaseCatalog leaseCatalog=null)
        {
            var indices=state.AllocationRequestIndices??System.Array.Empty<int>();
            if(state.ArrivalAuthority==null){if(indices.Length!=0)throw new ArgumentException("Missing arrival tail.");InitializeLiveArrivalTables();return;}
            foreach(int index in indices){
                if(index<0||index>=requests.Length)throw new ArgumentException("Invalid allocation request index.");
                var r=requests[index];
                if(pending.ContainsKey(r.Entity)||allocating.ContainsKey(r.Entity)||!orders.TryGetValue(r.Entity,out var order)||order!=r.Order||
                    r.Session!=Generation||!CurrentMember(r)||!(ReferenceEquals(r.BaseGeometry,navigationGeometry)||decodedHost?.AuthorityGeometry!=null&&ReferenceEquals(r.BaseGeometry.StorageRoot,decodedHost.AuthorityGeometry))||r.ProfileId!=profile.ProfileId||r.ProfileRevision!=profile.Revision||
                    !retainedGoals.TryGetValue(r.Entity,out var goal)||!goal.Equals(r.Goal)||!Member(r.Entity).GoalLocation.Equals(r.GoalLocation))throw new ArgumentException("Invalid allocating owner.");
                allocating.Add(r.Entity,r);
            }
            Unpack(state.ArrivalAuthority,r=>{
                if(r.ReadInt32()!=0x41525231)throw new ArgumentException("Unknown arrival wire.");
                int version=r.ReadInt32();if(version!=2&&version!=3&&version!=4)throw new ArgumentException("Unsupported arrival extension version.");
                if(version==4&&decodedHost==null)throw new ArgumentException("Arrival4 requires the same host BODY.");
                physicalArrivalEnabled=version==4;
                arrivalSequence=r.ReadInt64();arrivalCommitSequence=r.ReadInt64();if(arrivalSequence<0||arrivalCommitSequence<0)throw new ArgumentException("Invalid arrival sequence.");
                int providerCapacity=0,idCapacity=0,leaseCapacity=0;
                if(version>=3){
                    arrivalProviderSequence=r.ReadInt64();arrivalLeaseSequence=r.ReadInt64();arrivalAcquisitionSequence=r.ReadInt64();arrivalRetirementSequence=r.ReadInt64();arrivalReleaseSequence=r.ReadInt64();
                    providerCapacity=r.ReadInt32();idCapacity=r.ReadInt32();leaseCapacity=r.ReadInt32();
                    if(arrivalProviderSequence<0||arrivalLeaseSequence<0||arrivalAcquisitionSequence!=arrivalLeaseSequence||arrivalRetirementSequence<0||arrivalReleaseSequence<0||arrivalReleaseSequence>arrivalLeaseSequence||
                        providerCapacity<0||idCapacity<0||leaseCapacity<0||providerCapacity>65536||idCapacity>65536||leaseCapacity>65536)throw new ArgumentException("Invalid arrival owner ledger capacities/sequences.");
                    if(catalog==null&&(EnsureArrivalProviderCapacity(arrivalProviders,providerCapacity)!=providerCapacity||EnsureArrivalProviderCapacity(arrivalProviderIds,idCapacity)!=idCapacity)||catalog!=null&&(catalog.ReferenceCapacity!=providerCapacity||catalog.IdCapacity!=idCapacity)||EnsureArrivalLeaseCapacity(leaseCapacity)!=leaseCapacity)throw new ArgumentException("Invalid rounded owner capacities.");
                }
                ILayeredNavigationProvider ResolveProvider(long id,NavigationRequest recipe,ILayeredNavigationProvider physical=null){
                    if(id<1||id>arrivalProviderSequence)throw new ArgumentException("Invalid provider recipe reference.");
                    if(arrivalProviderIds.TryGetValue(id,out var known)){if(version==4&&!ReferenceEquals(physical,known.Provider))throw new ArgumentException("Arrival4 provider alias mismatch.");return known.Provider;}
                    if(version!=4&&ArrivalRetainedBytes+ProspectiveArrivalProviderBytes(recipe.Geometry)>LayeredNavigationScheduler.MaxRetainedBytes)throw new ArgumentException("Provider recipe exceeds retained admission.");
                    var provider=version==4?physical??throw new ArgumentException("Missing direct arrival provider owner."):ArrivalProvider(recipe.Geometry,recipe.HeldIdentity);var row=new ArrivalProviderRecord{Id=id,Provider=provider};
                    arrivalProviders.Add(provider,row);arrivalProviderIds.Add(id,row);return provider;
                }
                var attempts=Array(r,()=>{
                    var a=new ArrivalAttempt(version==4){Id=r.ReadInt64(),Group=r.ReadInt64(),Epoch=r.ReadInt64(),Published=Boolean(r),SnapshotStatus=EnumValue<NavArrivalStatus>(r),Frontier=String(r),SnapshotPhase=String(r),SnapshotCandidate=r.ReadInt32(),SnapshotOccupancy=r.ReadInt32(),SnapshotMember=r.ReadInt32()};
                    if(a.Id<1||a.Id>arrivalSequence||a.Group<1||a.Epoch<0||a.Frontier==null||a.Frontier.Length!=64||a.SnapshotPhase==null||a.SnapshotCandidate<0||a.SnapshotCandidate>64||a.SnapshotOccupancy<0||a.SnapshotMember<0)throw new ArgumentException("Invalid arrival attempt identity.");
                    if(version>=3){a.Retiring=Boolean(r);a.RetirementReason=String(r);a.LegacyProviderAccounting=Boolean(r);a.Retirement=r.ReadInt64();a.RestoredRetiringCompute=Boolean(r);
                        if(a.Retiring&&(string.IsNullOrEmpty(a.RetirementReason)||a.Retirement<1||a.Retirement>arrivalRetirementSequence)||!a.Retiring&&(a.RetirementReason!=""||a.Retirement!=0||a.RestoredRetiringCompute))throw new ArgumentException("Invalid retiring primitive owner.");
                    }else a.LegacyProviderAccounting=true;
                    NavigationArrivalWork physicalWork=null;
                    if(version==4){if(!decodedHost.ArrivalWork.TryGetValue(a.Id,out var owner)||!(owner is NavigationArrivalWork direct))throw new ArgumentException("Missing direct arrival work.");physicalWork=direct;}
                    a.Intents=Array(r,()=>ReadArrivalIntent(r));
                    string topology=String(r),profileIdentity=String(r),sealedInput=String(r),source=String(r);int revision=r.ReadInt32();var requested=ArrivalLocation(r);
                    var candidates=Array(r,()=>ArrivalLocation(r));
                    var members=Array(r,()=>{
                        long entity=r.ReadInt64(),incarnation=r.ReadInt64(),order=r.ReadInt64(),mobility=r.ReadInt64();var from=ArrivalLocation(r);
                        string id=String(r);int cr=r.ReadInt32();string mc=String(r);int mr=r.ReadInt32();double radius=Number(r);
                        return new NavArrivalMember(entity,incarnation,order,mobility,from,new NavClearanceProfile(id,cr,mc,mr,radius),
                            r.ReadInt64(),r.ReadInt64(),r.ReadInt64(),r.ReadInt64(),r.ReadInt64(),r.ReadInt64(),r.ReadInt64(),r.ReadInt64(),String(r));
                    });
                    string providerKey=String(r),graphProfile=String(r);double cell=Number(r);
                    long inputProviderId=0;long[] workingProviderIds=null;
                    if(version>=3){inputProviderId=r.ReadInt64();workingProviderIds=Array(r,()=>r.ReadInt64());string aliases=String(r);
                        if(workingProviderIds.Length!=a.Intents.Length||workingProviderIds.Length==0||aliases!=ArrivalProviderAliasBinding(inputProviderId,workingProviderIds))throw new ArgumentException("Invalid input physical aliases.");
                    }
                    var values=Array(r,()=>Number(r));string profileId=String(r);int pr=r.ReadInt32();
                    var terms=LoadProfile(new NavigationRequestState{ProfileId=profileId,ProfileRevision=pr,ProfileValues=values});
                    var limits=new NavGraphCompileLimits(r.ReadInt32(),r.ReadInt32(),r.ReadInt32());int labels=r.ReadInt32();
                    var occupants=Array(r,()=>{
                        var kind=EnumValue<NavArrivalOccupantKind>(r);long entity=r.ReadInt64(),incarnation=r.ReadInt64();var at=ArrivalLocation(r);double radius=Number(r);bool blocks=Boolean(r);string identity=String(r);
                        NavArrivalReservationKind? stateKind=Boolean(r)?(NavArrivalReservationKind?)EnumValue<NavArrivalReservationKind>(r):null;
                        return new NavArrivalOccupant(kind,entity,incarnation,at,radius,blocks,identity,stateKind,r.ReadInt64(),r.ReadInt64(),GroupOrderWire.ReadLocation(r));
                    });
                    var orderedRecipes=a.Intents.OrderBy(i=>i.Entity).ToArray();
                    var bindings=orderedRecipes.Select((i,index)=>new NavArrivalWorkingBinding(i.Entity,version>=3?ResolveProvider(workingProviderIds[index],i,version==4?physicalWork.PhysicalInput.WorkingBindings.Single(b=>b.Entity==i.Entity).Provider:null):ArrivalProvider(i.Geometry,i.HeldIdentity),
                        new NavGraphProfile(NavigationAdmission.ExactProfileBinding(i.Profile),"distance",1,i.Profile.GridCell))).ToArray();
                    var initialProvider=version>=3?ResolveProvider(inputProviderId,orderedRecipes[0],version==4?physicalWork.PhysicalInput.Provider:null):bindings[0].Provider;
                    if(bindings.Length==0||bindings[0].Provider.Binding!=providerKey||bindings[0].Profile.ExactProfileBinding!=graphProfile||bindings[0].Profile.GridCell!=cell||
                        a.Intents.Any(i=>i.MemberIdentity.GroupId!=a.Group)||members.Length!=a.Intents.Length)throw new ArgumentException("Invalid saved arrival binding.");
                    foreach(var m in members){var i=a.Intents.SingleOrDefault(i=>i.Request==m.RequestSequence);
                        if(i==null||m.EntityId!=i.Entity||m.Incarnation!=i.MemberIdentity.Incarnation||m.MobilityRevision!=i.MemberIdentity.MobilityRevision||m.MemberOrderRevision!=i.MemberIdentity.OrderRevision||m.GroupId!=a.Group||m.Radius!=i.Profile.Radius||!m.Origin.Equals(i.StartLocation))throw new ArgumentException("Invalid saved arrival member.");}
                    a.Input=new NavArrivalInput(topology,profileIdentity,initialProvider,bindings[0].Profile,terms,limits,
                        new NavArrivalCandidateSource(source,revision,requested,candidates),members,new NavArrivalOccupancySnapshot(Generation,occupants),labels,bindings,true);
                    if(a.Input.StableKey!=sealedInput)throw new ArgumentException("Arrival input digest mismatch.");
                    var journal=Array(r,()=>new ArrivalAdvance{Epoch=r.ReadInt64(),External=r.ReadInt64(),Allocation=r.ReadInt32(),Graph=r.ReadInt32(),Field=r.ReadInt32(),Connectors=r.ReadInt32(),Bookkeeping=r.ReadInt32(),Maintenance=r.ReadInt32()});
                    if(journal.Length!=a.Epoch)throw new ArgumentException("Invalid arrival journal epoch.");
                    for(int n=0;n<journal.Length;n++){var s=journal[n];if(s.Epoch!=n||s.External<0||s.External>LayeredNavigationScheduler.MaxRetainedBytes||s.Allocation<1||s.Allocation>262144||s.Graph!=s.Allocation||s.Field!=s.Allocation||s.Connectors!=s.Allocation||s.Bookkeeping!=s.Allocation||s.Maintenance!=256)throw new ArgumentException("Invalid arrival quota journal.");}
                    if(version!=4)a.LegacyJournal.AddRange(journal);
                    var ready=ReadReadyArrival(r,a.Input);
                    foreach(var p in Array(r,()=>{
                        long key=r.ReadInt64();var s=new ArrivalProjection{Leg=r.ReadInt32(),Replay=r.ReadInt32(),Expected=ArrivalLocation(r),At=ArrivalLocation(r),
                            Points=Array(r,()=>ReadPoint(r)),Typed=Array(r,()=>String(r)),Observed=Array(r,()=>String(r))};
                        if(s.Leg<0||s.Replay<0||s.Replay>s.Points.Length||s.Leg<s.Points.Length+s.Typed.Length||s.Observed.Any(v=>v==null)||s.Typed.Any(v=>v==null))throw new ArgumentException("Invalid arrival projection cursor.");
                        return new KeyValuePair<long,ArrivalProjection>(key,s);
                    })){
                        if(!a.Published||!a.Intents.Any(i=>i.Request==p.Key)||a.Projections!=null&&a.Projections.ContainsKey(p.Key))throw new ArgumentException("Invalid arrival projection owner.");if(a.Projections==null)a.Projections=new Dictionary<long,ArrivalProjection>();a.Projections.Add(p.Key,p.Value);
                    }
                    for(int n=0;n<a.Intents.Length;n++)if(allocating.TryGetValue(a.Intents[n].Entity,out var canonical)&&canonical.Request==a.Intents[n].Request){
                        var i=a.Intents[n];
                        if(i.Order!=canonical.Order||!i.Start.Equals(canonical.Start)||!i.Goal.Equals(canonical.Goal)||
                            !i.StartLocation.Equals(canonical.StartLocation)||!i.GoalLocation.Equals(canonical.GoalLocation)||
                            i.HeldIdentity!=canonical.HeldIdentity||NavigationAdmission.ExactProfileBinding(i.Profile)!=NavigationAdmission.ExactProfileBinding(canonical.Profile)||
                            i.MemberIdentity.GroupId!=canonical.MemberIdentity.GroupId||i.MemberIdentity.Incarnation!=canonical.MemberIdentity.Incarnation||
                            i.MemberIdentity.OrderRevision!=canonical.MemberIdentity.OrderRevision||i.MemberIdentity.MobilityRevision!=canonical.MemberIdentity.MobilityRevision||
                            !SameGeometry(SaveGeometry(i.Geometry),canonical.Geometry)||!SameGeometry(SaveGeometry(i.BaseGeometry),canonical.BaseGeometry))throw new ArgumentException("Contradictory canonical allocation intent.");
                        a.Intents[n]=canonical;
                    }
                    if(version==4){
                        if(!decodedHost.ArrivalWork.TryGetValue(a.Id,out var owned)||!(owned is NavigationArrivalWork actual)||actual.PhysicalInput.StableKey!=a.Input.StableKey)
                            throw new ArgumentException("Missing or contradictory direct arrival work owner.");
                        a.Input=actual.PhysicalInput;a.Work=actual;
                        if(!decodedHost.ArrivalHistory.TryGetValue(a.Id,out var history)||!(history is NavigationHostArrivalHistory actualHistory)||actualHistory.Count!=journal.Length||!actualHistory.SequenceEqual(journal))throw new ArgumentException("Arrival4 grant history differs from BODY.");
                        a.History=actualHistory;a.LegacyJournal=null;
                        if(!decodedHost.ArrivalIntentSets.TryGetValue(a.Id,out var set)||!(set is NavigationHostArrivalIntents originalIntents)||originalIntents.Requests.Length!=a.Intents.Length)throw new ArgumentException("Missing direct original intent view.");
                        for(int n=0;n<a.Intents.Length;n++){
                            if(!decodedHost.ArrivalIntents.TryGetValue(a.Intents[n].Request,out var intentOwner)||!(intentOwner is NavigationHostRequest physical)||
                                physical.Request.Entity!=a.Intents[n].Entity||physical.Request.Order!=a.Intents[n].Order||!physical.Request.Start.Equals(a.Intents[n].Start)||!physical.Request.Goal.Equals(a.Intents[n].Goal))
                                throw new ArgumentException("Missing physical original intent owner.");
                            if(!ReferenceEquals(physical.Request,originalIntents.Requests[n]))throw new ArgumentException("Original intent array alias differs from BODY.");
                            a.Intents[n]=physical.Request;
                        }
                        a.Intents=originalIntents.Requests;
                    }else a.Work=ready==null?new NavigationArrivalWork(a.Input):NavigationArrivalWork.RestoreReady(a.Input,ready);
                    if((a.SnapshotStatus==NavArrivalStatus.Ready)!=(ready!=null)||ready!=null&&ready.Epoch!=a.Epoch-1)throw new ArgumentException("Missing or contradictory ready checkpoint.");
                    OwnArrivalInput(a);
                    if(version>=3&&(a.InputProviderId!=inputProviderId||!a.WorkingProviderIds.SequenceEqual(workingProviderIds)))throw new ArgumentException("Inconsistent restored alias identity.");
                    a.Reconstructing=version!=4&&ready==null&&journal.Length>0;
                    if(!a.Reconstructing&&(ArrivalFrontier(a.Work)!=a.Frontier||a.Work.WorkPhase!=a.SnapshotPhase||a.Work.CandidateCursor!=a.SnapshotCandidate||a.Work.OccupancyCursor!=a.SnapshotOccupancy||a.Work.MemberCursor!=a.SnapshotMember))throw new ArgumentException("Invalid initial arrival frontier.");
                    return a;
                });
                foreach(var original in attempts){if(arrivalAttempts.ContainsKey(original.Id))throw new ArgumentException("Duplicate arrival attempt.");var a=version==4&&catalog==null?RetainPhysicalAttemptEdges(original,original.Work,(NavigationHostArrivalIntents)decodedHost.ArrivalIntentSets[original.Id],original.History):original;try{arrivalAttempts.Add(a.Id,a);}catch{if(version==4&&catalog==null)ReleasePhysicalAttemptEdges(a);throw;}}
                if(version>=3){
                    foreach(var lease in Array(r,()=>new ArrivalProviderLease{Session=this,Token=r.ReadInt64(),Acquisition=r.ReadInt64(),Retirement=r.ReadInt64(),World=r.ReadInt64(),Attempt=r.ReadInt64(),Request=r.ReadInt64(),Entity=r.ReadInt64(),Incarnation=r.ReadInt64(),Order=r.ReadInt64(),ProviderId=r.ReadInt64(),Retiring=Boolean(r),Reason=String(r),Binding=String(r),PointsCapacity=r.ReadInt32(),TypedCapacity=r.ReadInt32(),ObservedCapacity=r.ReadInt32()})){
                        if(lease.Token<1||lease.Token>arrivalLeaseSequence||lease.Acquisition<1||lease.Acquisition>arrivalAcquisitionSequence||lease.World!=Generation||lease.Binding!=ProjectionLeaseBinding(lease)||
                            lease.Retiring&&(lease.Retirement<1||lease.Retirement>arrivalRetirementSequence||string.IsNullOrEmpty(lease.Reason))||!lease.Retiring&&(lease.Retirement!=0||lease.Reason!="")||
                            arrivalProjectionLeases.Values.Any(l=>l.Acquisition==lease.Acquisition||l.Request==lease.Request)||!arrivalAttempts.TryGetValue(lease.Attempt,out var attempt)||
                            !arrivalProviderIds.TryGetValue(lease.ProviderId,out var provider)||!attempt.Published||attempt.SnapshotStatus!=NavArrivalStatus.Ready||
                            version!=4&&!attempt.Projections.ContainsKey(lease.Request))throw new ArgumentException("Unknown/duplicate projection owner or missing pinned recipe.");
                        int pointsCapacity=lease.PointsCapacity,typedCapacity=lease.TypedCapacity,observedCapacity=lease.ObservedCapacity;
                        if(version==4&&(pointsCapacity!=0||typedCapacity!=0||observedCapacity!=0))throw new ArgumentException("Arrival4 legacy capacity fields must be canonical zero; physical capacities belong to BODY.");
                        var progress=version==4?null:attempt.Projections[lease.Request];
                        if(version!=4&&(pointsCapacity<progress.Points.Length||typedCapacity<progress.Typed.Length||observedCapacity<progress.Observed.Length||pointsCapacity>2097152||typedCapacity>2097152||observedCapacity>2097152))
                            throw new ArgumentException("Invalid allocated arrival projection capacities.");
                        var intent=attempt.Intents.SingleOrDefault(i=>i.Request==lease.Request);
                        if(intent==null||intent.Entity!=lease.Entity||intent.MemberIdentity.Incarnation!=lease.Incarnation||intent.Order!=lease.Order||
                            !ReferenceEquals(provider.Provider,attempt.Input.WorkingBindings.Single(b=>b.Entity==lease.Entity).Provider))throw new ArgumentException("Inconsistent projection provider alias/member.");
                        lease.Provider=provider.Provider;RetainArrivalProvider(lease.Provider,lease.ProviderId);
                        if(version==4){
                            var physicalRow=decodedHost.Rows.Select(e=>(NavigationHostRow)e.Value).SingleOrDefault(row=>row.ArrivalToken==lease.Token);
                            if(physicalRow==null||physicalRow.Request.Session!=lease.World||physicalRow.Request.Request!=lease.Request||physicalRow.Request.Entity!=lease.Entity||
                                !ReferenceEquals(physicalRow.Provider,lease.Provider)||!ReferenceEquals(physicalRow.Solved.Route,attempt.Work.Proposals.Single(p=>p.Member.RequestSequence==lease.Request).Route))
                                throw new ArgumentException("Arrival4 projection link differs from actual host row.");
                            lease.Attached=true;arrivalProjectionLeases.Add(lease.Token,lease);if(lease.Retiring)arrivalRetirements.Add(lease.Retirement,lease);continue;
                        }
                        var proposal=attempt.Work.Proposals.Single(p=>p.Member.RequestSequence==lease.Request);
                        var projected=pending.TryGetValue((int)lease.Entity,out var currentRequest)&&currentRequest.Request==lease.Request?currentRequest:
                            new NavigationRequest(intent.Session,intent.Request,intent.Entity,intent.Order,intent.Profile,intent.Geometry,intent.Start,proposal.Endpoint.Position,intent.BaseGeometry,intent.HeldIdentity,intent.MemberIdentity,proposal.Route.Subscription.Origin,proposal.Endpoint);
                        var binding=attempt.Input.WorkingFor(proposal.Member);var scheduled=NavScheduledRequest.ForSingleton(binding.Provider,proposal.Member.Clearance,binding.Profile,attempt.Input.Limits,proposal.Route.Subscription,attempt.Input.MaxLabels);
                        if(ArrivalRetainedBytes+1536L+16L*pointsCapacity+8L*(typedCapacity+observedCapacity)>LayeredNavigationScheduler.MaxRetainedBytes)throw new ArgumentException("Projection recipe exceeds retained admission before allocation.");
                        lease.Projection=CertifiedRouteKernel.CreateArrivalProjection(projected,scheduled,proposal.Route,progress,lease,RoutePort,terrain?.Id??NavLocation.FlatSurface,pointsCapacity,typedCapacity,observedCapacity);
                        arrivalProjectionLeases.Add(lease.Token,lease);if(lease.Retiring)arrivalRetirements.Add(lease.Retirement,lease);
                    }
                    if(catalog==null&&(ArrivalProviderCapacity(arrivalProviders)!=providerCapacity||ArrivalProviderCapacity(arrivalProviderIds)!=idCapacity)||ArrivalLeaseCapacity!=leaseCapacity||ArrivalRetainedBytes>LayeredNavigationScheduler.MaxRetainedBytes)
                        throw new ArgumentException("Incomplete owner capacity checkpoint or retained overflow.");
                }
                foreach(var s in Array(r,()=>new NavigationArrivalReservation{CommitOrder=r.ReadInt64(),Id=r.ReadInt64(),World=r.ReadInt64(),Group=r.ReadInt64(),Root=r.ReadInt64(),Command=r.ReadInt64(),IssuedTick=r.ReadInt64(),Activation=r.ReadInt64(),Entity=r.ReadInt64(),Incarnation=r.ReadInt64(),Mobility=r.ReadInt64(),Request=r.ReadInt64(),Order=r.ReadInt64(),Endpoint=ArrivalLocation(r),Radius=Number(r),Proof=String(r),Provider=String(r),Profile=String(r),Hold=String(r),State=EnumValue<NavArrivalReservationKind>(r)})){
                    if(s.CommitOrder<1||s.CommitOrder>arrivalCommitSequence||arrivalLedger.Values.Any(v=>v.CommitOrder==s.CommitOrder)||s.Id!=s.Request||s.Request<1||s.Request>requestSequence||s.World!=Generation||s.Group<1||s.Root<1||s.Activation<1||s.Incarnation<1||s.Mobility<0||s.Radius<=0||s.Proof==null||s.Proof.Length!=64||s.Provider==null||s.Profile==null||s.Profile.Length!=64||
                        !Crowd.TryGet((int)s.Entity,out var actor)||actor.Incarnation!=s.Incarnation||actor.Radius!=s.Radius||arrivalLedger.ContainsKey(s.Id)||
                        s.State==NavArrivalReservationKind.PendingRequest&&(!pending.TryGetValue((int)s.Entity,out var active)||active.Request!=s.Request||!active.GoalLocation.Equals(s.Endpoint))||
                        s.State==NavArrivalReservationKind.InstalledExecution&&(actor.InstalledRequest!=s.Request||!actor.GoalLocation.Equals(s.Endpoint)))throw new ArgumentException("Invalid reservation owner/state.");
                    if(!arrivalAttempts.Values.Any(a=>a.Published&&a.Intents.Any(i=>i.Request==s.Request&&i.Entity==s.Entity)))throw new ArgumentException("Missing reservation proof reconstruction.");
                    // The wire value authenticates the role; the BODY request or
                    // actor supplies its exact retained owner, never a new equal name.
                    if(version==4)s.Endpoint=s.State==NavArrivalReservationKind.PendingRequest?pending[(int)s.Entity].GoalLocation.Value:actor.GoalLocation.Value;
                    arrivalLedger.Add(s.Id,s);
                }
                foreach(var lease in arrivalProjectionLeases.Values){
                    bool live=arrivalLedger.TryGetValue(lease.Request,out var owner)&&owner.State==NavArrivalReservationKind.PendingRequest;
                    if(live==lease.Retiring)throw new ArgumentException("Projection lease contradicts gameplay ownership.");
                }
                foreach(var attempt in arrivalAttempts.Values.Where(a=>a.Retiring))if(attempt.Intents.Any(i=>arrivalLedger.ContainsKey(i.Request)||allocating.Values.Any(r0=>r0.Request==i.Request))||!attempt.RestoredRetiringCompute&&!arrivalProjectionLeases.Values.Any(l=>l.Attempt==attempt.Id))throw new ArgumentException("Invalid pinned nonauthoritative recipe.");
                foreach(var completed in arrivalAttempts.Values.Where(a=>!a.Reconstructing))ValidateArrivalReconstruction(completed);
                foreach(var r0 in allocating.Values)if(!arrivalAttempts.Values.Any(a=>!a.Published&&a.Intents.Any(i=>i.Request==r0.Request))){
                    // An accepted/retried request can be saved before its first input
                    // snapshot; EnsureArrivalAttempts creates work on its next pump.
                    if(Member(r0.Entity).Phase!=GroupMemberPhase.Allocating)throw new ArgumentException("Missing allocation phase.");
                }
                return 0;
            });
            if(catalog!=null)RestoreLiveArrivalCatalog(catalog,decodedHost);
            if(leaseCatalog!=null)RestoreLiveArrivalLeaseCatalog(leaseCatalog,catalog,decodedHost);
        }
    }
}
