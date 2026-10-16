using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    internal static class NavigationArrivalPhysicalInput
    {
        // This is the admitted initial input construction, before any work is
        // run. Existing owned inputs are retained through their exact owners.
        internal static NavArrivalInput CopyInitial(NavArrivalInput source,INavStorageReservation reservation)
        {
            if(source==null||source.Members.Count==0||source.WorkingBindings.Count!=source.Members.Count||source.Occupancy==null)throw new ArgumentException("Invalid initial arrival input shape.");
            void Facade(INavStorageFacade value){if(value?.StorageRoot!=null)NavStorageAdmission.RequireOwned(reservation,value.StorageRoot);}
            void Metadata(NavOwnedStorage value){if(value!=null)NavStorageAdmission.RequireOwned(reservation,value);}
            Facade(source);Facade(source.Provider as INavStorageFacade);Facade(source.ArrivalProfile);Facade(source.Source);Facade(source.Occupancy);
            Metadata(source.Profile.MetadataOwner);Metadata(source.Limits.MetadataOwner);source.Source.Requested.RequireName(reservation);
            foreach(var candidate in source.Source.Candidates)candidate.RequireName(reservation);
            foreach(var member in source.Members){Facade(member);Metadata(member.Clearance.MetadataOwner);member.Origin.RequireName(reservation);}
            foreach(var binding in source.WorkingBindings){Facade(binding);Facade(binding.Provider as INavStorageFacade);Metadata(binding.Profile.MetadataOwner);}
            foreach(var occupant in source.Occupancy.Occupants){Facade(occupant);occupant.Location.RequireName(reservation);if(occupant.ActorTarget.HasValue)occupant.ActorTarget.Value.RequireName(reservation);}
            foreach(var member in source.Members)if(source.WorkingBindings.Count(b=>b.Entity==member.EntityId)!=1)throw new ArgumentException("Invalid initial working selectors.");
            if(!source.WorkingBindings.Any(b=>ReferenceEquals(b.Provider,source.Provider)))throw new ArgumentException("Missing initial provider selector.");
            if(source.StorageRoot!=null){source.StorageRoot.Retain();return source;}
            using(NavStorageScratch.Reserve(reservation,checked(8192L+512L*source.Members.Count+256L*source.Occupancy.Occupants.Count))){
                var providers=new Dictionary<ILayeredNavigationProvider,ILayeredNavigationProvider>();
                var bindings=new NavArrivalWorkingBinding[source.WorkingBindings.Count];
                var members=new NavArrivalMember[source.Members.Count];
                var metadata=new List<NavGraphMetadata>();
                var profiles=new Dictionary<NavGraphProfile,NavGraphProfile>();
                var clearances=new Dictionary<NavClearanceProfile,NavClearanceProfile>();
                var ownedBindings=new Dictionary<long,NavArrivalWorkingBinding>();
                NavGraphCompileLimits ownedLimits=null;
                var occupants=new NavArrivalOccupant[source.Occupancy.Occupants.Count];
                NavigationProfile arrival=null;NavArrivalCandidateSource candidates=null;NavArrivalOccupancySnapshot occupancy=null;
                try{
                    var common=NavGraphMetadata.CopyOptionalUnowned(null,source.Profile,source.Limits,reservation);if(common!=null)metadata.Add(common);
                    profiles.Add(source.Profile,source.Profile.MetadataOwner!=null?source.Profile:common.Profile);
                    ownedLimits=source.Limits.MetadataOwner!=null?source.Limits:common.Limits;
                    for(int n=0;n<members.Length;n++){
                        var member=source.Members[n];var original=source.WorkingFor(member);
                        if(!providers.TryGetValue(original.Provider,out var provider)){
                            var providerRoot=(original.Provider as INavStorageFacade)?.StorageRoot;
                            if(providerRoot!=null){provider=original.Provider;providerRoot.Retain();}
                            else if(original.Provider is LayeredNavigationProvider flat)provider=LayeredNavigationProvider.CopyOwned(flat,reservation);
                            else if(original.Provider is PartitionNavigationProvider partition)provider=PartitionNavigationProvider.CopyOwned(partition,reservation);
                            else throw new ArgumentException("Unsupported actual arrival provider.");
                            providers.Add(original.Provider,provider);
                        }
                        if(!profiles.TryGetValue(original.Profile,out var ownedProfile)){
                            var meta=NavGraphMetadata.CopyOptionalUnowned(null,original.Profile,null,reservation);if(meta!=null)metadata.Add(meta);
                            ownedProfile=original.Profile.MetadataOwner!=null?original.Profile:meta.Profile;profiles.Add(original.Profile,ownedProfile);
                        }
                        if(!clearances.TryGetValue(member.Clearance,out var clearance)){
                            var meta=NavGraphMetadata.CopyOptionalUnowned(member.Clearance,null,null,reservation);if(meta!=null)metadata.Add(meta);
                            clearance=member.Clearance.MetadataOwner!=null?member.Clearance:meta.Clearance;clearances.Add(member.Clearance,clearance);
                        }
                        if(member.StorageRoot!=null){members[n]=member;member.StorageRoot.Retain();}else members[n]=NavArrivalMember.CopyOwned(member,clearance,reservation);
                        NavArrivalWorkingBinding working;
                        if(original.StorageRoot!=null){working=original;original.StorageRoot.Retain();}else working=NavArrivalWorkingBinding.CreateOwned(member.EntityId,provider,ownedProfile,reservation);
                        ownedBindings.Add(member.EntityId,working);
                    }
                    for(int n=0;n<bindings.Length;n++)bindings[n]=ownedBindings[source.WorkingBindings[n].Entity];
                    if(source.ArrivalProfile.StorageRoot!=null){arrival=source.ArrivalProfile;arrival.StorageRoot.Retain();}else arrival=NavigationProfile.CopyOwned(source.ArrivalProfile,reservation);
                    if(source.Source.StorageRoot!=null){candidates=source.Source;candidates.StorageRoot.Retain();}else candidates=NavArrivalCandidateSource.CopyOwned(source.Source,reservation);
                    for(int n=0;n<occupants.Length;n++){var occupant=source.Occupancy.Occupants[n];if(occupant.StorageRoot!=null){occupants[n]=occupant;occupant.StorageRoot.Retain();}else occupants[n]=NavArrivalOccupant.CopyOwned(occupant,reservation);}
                    if(source.Occupancy.StorageRoot!=null){occupancy=source.Occupancy;occupancy.StorageRoot.Retain();}else occupancy=NavArrivalOccupancySnapshot.CopyOwned(source.Occupancy,occupants,reservation);
                    return NavArrivalInput.CopyOwned(source,providers[source.Provider],profiles[source.Profile],arrival,ownedLimits,candidates,members,occupancy,bindings,reservation);
                }finally{
                    occupancy?.StorageRoot.Release();candidates?.StorageRoot.Release();arrival?.StorageRoot.Release();
                    foreach(var occupant in occupants)occupant?.StorageRoot.Release();
                    foreach(var binding in ownedBindings.Values)binding.StorageRoot.Release();
                    foreach(var member in members)member?.StorageRoot.Release();
                    foreach(var meta in metadata)meta?.Release();
                    foreach(var provider in providers.Values)((INavStorageFacade)provider).StorageRoot.Release();
                }
            }
        }
    }
}
