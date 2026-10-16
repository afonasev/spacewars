using System;
using System.Collections.Generic;
namespace Spacewars.Simulation
{
    public sealed partial class NavArrivalCandidateSource : INavStorageFacade
    {
        private NavDeclarationStorage ownedSource;
        private readonly NavLocation[] publicCandidates;
        NavOwnedStorage INavStorageFacade.StorageRoot=>ownedSource;
        internal NavOwnedStorage StorageRoot=>ownedSource;
        private static readonly long LayoutBase=Math.Max(512,NavStorageLayout.ObjectBytesBound(typeof(NavDeclarationStorage))+NavStorageLayout.ObjectBytesBound(typeof(NavArrivalCandidateSource))+160+48+32);
        private static long Arrays(int count)=>checked(2L*NavStorageLayout.ValueBytes(typeof(NavLocation))*count);
        private NavArrivalCandidateSource(string id,int revision,NavLocation requested,NavLocation[] privateValues,NavLocation[] publicValues,string key)
        {Id=id;Revision=revision;Requested=requested;candidates=privateValues;publicCandidates=publicValues;Candidates=Array.AsReadOnly(publicValues);StableKey=key;}
        private static string Key(string id,int revision,NavLocation requested,NavLocation[] values)=>NavContract.Digest(w=>{w.Write(id);w.Write(revision);NavContract.Location(w,requested);w.Write(values.Length);foreach(var value in values)NavContract.Location(w,value);});
        private static long Wire(string id,NavLocation requested,IReadOnlyList<NavLocation> values)
        {long bytes=checked(NavContract.StringWireBytes(id)+8+NavContract.LocationWireBytes(requested));for(int n=0;n<values.Count;n++)bytes=checked(bytes+NavContract.LocationWireBytes(values[n]));return bytes;}
        internal static NavArrivalCandidateSource CopyOwned(NavArrivalCandidateSource source,INavStorageReservation reservation)
        {
            if(source==null)throw new ArgumentNullException(nameof(source));if(source.StorageRoot!=null)NavStorageAdmission.RequireOwned(reservation,source.StorageRoot);source.Requested.RequireName(reservation);int count=source.candidates.Length;long bytes=checked(LayoutBase+Arrays(count)+NavProviderCopy.TextBytes(source.Id));if(source.Requested.NameReference.Owner==null)bytes=checked(bytes+NavProviderCopy.TextBytes(source.Requested.SurfaceId));
            for(int n=0;n<count;n++){source.candidates[n].RequireName(reservation);if(source.candidates[n].NameReference.Owner==null)bytes=checked(bytes+NavProviderCopy.TextBytes(source.candidates[n].SurfaceId));}
            using(NavStorageScratch.Reserve(reservation,checked(8192+8L*(count+1)+NavContract.DigestScratchBytes(Wire(source.Id,source.Requested,source.candidates)))))
            using(var allocation=new NavStorageAllocation(reservation.TryReserve)){
                allocation.Reserve(bytes);var priv=new NavLocation[count];var pub=new NavLocation[count];var owners=new NavOwnedStorage[checked(count+1)];owners[0]=source.Requested.NameReference.Owner;var requested=owners[0]==null?NavProviderCopy.Location(source.Requested):source.Requested;
                for(int n=0;n<count;n++){owners[n+1]=source.candidates[n].NameReference.Owner;priv[n]=owners[n+1]==null?NavProviderCopy.Location(source.candidates[n]):source.candidates[n];pub[n]=priv[n];}
                NavRetainedArray<NavOwnedStorage> retained=null;
                try{
                    retained=NavRetainedArray<NavOwnedStorage>.Copy(owners,reservation.TryReserve);string id=NavProviderCopy.Text(source.Id),key=Key(id,source.Revision,requested,priv);var value=new NavArrivalCandidateSource(id,source.Revision,requested,priv,pub,key);if(key!=source.StableKey)throw new ArgumentException("Owned source digest changed.");var storage=allocation.Register(new NavDeclarationStorage(bytes));storage.First=NavPhysicalMacro.Acquire(storage,retained);storage.Facade=value;value.ownedSource=storage;value.BindOwnNames(retained);storage.Retain();return value;
                }finally{Array.Clear(owners,0,owners.Length);retained?.Release();}
            }
        }
        // Requested is backed by its original auto-property; private construction
        // sets the selector after the facade is attached, without a self edge.
        private void BindOwnNames(NavRetainedArray<NavOwnedStorage> owners)
        {
            if(owners[0]==null)Requested=new NavLocation(Requested.Position,new NavNameReference(ownedSource,NavNameRole.ArrivalSourceRequestedSurface));
            for(int n=0;n<candidates.Length;n++)if(owners[n+1]==null){candidates[n]=new NavLocation(candidates[n].Position,new NavNameReference(ownedSource,NavNameRole.ArrivalSourceCandidateSurface,n));publicCandidates[n]=candidates[n];}
        }
        internal void WritePhysicalBody(NavPhysicalBodyWriter writer,NavDeclarationStorage storage)
        {
            if(!ReferenceEquals(storage,ownedSource)||!(storage.First is NavRetainedArray<NavOwnedStorage> owners)||owners.Count!=candidates.Length+1||storage.Second!=null||storage.Third!=null||storage.Fourth!=null||storage.Fifth!=null||storage.Sixth!=null||storage.Seventh!=null||storage.Eighth!=null||publicCandidates==null||ReferenceEquals(candidates,publicCandidates)||publicCandidates.Length!=candidates.Length)throw new ArgumentException("Physical source ownership/backings contradiction.");
            long bytes=checked(LayoutBase+Arrays(candidates.Length)+NavProviderCopy.TextBytes(Id));NavPhysicalTextPlan.WritePlan(writer,Id);writer.Output.Write(candidates.Length);
            for(int n=-1;n<candidates.Length;n++){var value=n<0?Requested:candidates[n];bool own=owners[n+1]==null;var role=n<0?NavNameRole.ArrivalSourceRequestedSurface:NavNameRole.ArrivalSourceCandidateSurface;
                if(own){if(!ReferenceEquals(value.NameReference.Owner,storage)||value.NameReference.Role!=role||value.NameReference.Index!=(n<0?-1:n))throw new ArgumentException("Physical source own selector contradiction.");bytes=checked(bytes+NavProviderCopy.TextBytes(value.SurfaceId));}else if(!ReferenceEquals(owners[n+1],value.NameReference.Owner))throw new ArgumentException("Physical source external owner contradiction.");value.NameReference.Validate(value.SurfaceId,storage);
                if(n>=0&&(value.Position.X!=publicCandidates[n].Position.X||value.Position.Z!=publicCandidates[n].Position.Z||!ReferenceEquals(value.SurfaceId,publicCandidates[n].SurfaceId)||value.NameReference.Role!=publicCandidates[n].NameReference.Role||value.NameReference.Index!=publicCandidates[n].NameReference.Index||!ReferenceEquals(value.NameReference.Owner,publicCandidates[n].NameReference.Owner)))throw new ArgumentException("Physical source public view contradiction.");writer.Output.Write(own);if(own)NavPhysicalTextPlan.WritePlan(writer,value.SurfaceId);
            }
            if(storage.StorageBytes!=NavStorageLayout.Charge(bytes))throw new ArgumentException("Physical source layout contradiction.");NavPhysicalMacro.WriteEdge(writer,storage,owners);NavPhysicalTextPlan.WriteData(writer,Id);writer.Output.Write(Revision);
            for(int n=-1;n<candidates.Length;n++){var value=n<0?Requested:candidates[n];if(owners[n+1]==null){NavContract.Point(writer.Output,value.Position);NavPhysicalTextPlan.WriteData(writer,value.SurfaceId);}else NavPhysicalNamedValue.WriteLocation(writer,value);}NavPhysicalTextPlan.WriteData(writer,NavContract.Sha256(StableKey));
        }
        internal static NavDeclarationStorage ReadPhysicalBody(NavPhysicalBodyReader reader)
        {
            var idPlan=NavPhysicalTextPlan.Read(reader);int count=reader.Count(int.MaxValue-1);if(count==0)throw new ArgumentException("Empty physical source.");long plansPosition=reader.Input.BaseStream.Position,bytes=checked(LayoutBase+Arrays(count)+idPlan.Bytes);
            for(int n=-1;n<count;n++){bool own=NavPhysicalMacro.ReadBoolean(reader);if(own)bytes=checked(bytes+NavPhysicalTextPlan.Read(reader).Bytes);}
            var owners=NavPhysicalMacro.ReadEdge<NavRetainedArray<NavOwnedStorage>>(reader);if(owners.Count!=count+1)throw new ArgumentException("Physical source owner count mismatch.");
            using(var allocation=new NavStorageAllocation(reader.Reservation.TryReserve)){
                allocation.Reserve(bytes);
                using(NavStorageScratch.Reserve(reader.Reservation,checked(8192+128L*count))){
                    string id=idPlan.ReadData(reader);int revision=reader.Input.ReadInt32();var priv=new NavLocation[count];var pub=new NavLocation[count];NavLocation requested=default;long planCursor=plansPosition;var distinct=new HashSet<NavLocation>();
                    try{
                        for(int n=-1;n<count;n++){long dataPosition=reader.Input.BaseStream.Position;reader.Input.BaseStream.Position=planCursor;bool own=NavPhysicalMacro.ReadBoolean(reader);var plan=own?NavPhysicalTextPlan.Read(reader):default;planCursor=reader.Input.BaseStream.Position;reader.Input.BaseStream.Position=dataPosition;if(own!=(owners[n+1]==null))throw new ArgumentException("Physical source owner mode mismatch.");var value=own?new NavLocation(NavPhysicalGeometryValue.Point(reader),plan.ReadData(reader)):NavPhysicalNamedValue.ReadLocation(reader);if(!own&&(value.NameReference.Owner==null||!ReferenceEquals(owners[n+1],value.NameReference.Owner)))throw new ArgumentException("Physical source external selector mismatch.");if(n<0)requested=value;else{if(!distinct.Add(value))throw new ArgumentException("Duplicate physical candidate.");priv[n]=value;pub[n]=value;}}
                        if(string.IsNullOrWhiteSpace(id)||id.Length>256||revision<1)throw new ArgumentException("Invalid physical source identity.");var chars=new char[64];for(int n=0;n<64;n++)chars[n]=(char)reader.Input.ReadUInt16();string expected=NavContract.Sha256(new string(chars));
                        using(NavStorageScratch.Reserve(reader.Reservation,NavContract.DigestScratchBytes(Wire(id,requested,priv)))){
                            string key=Key(id,revision,requested,priv);if(key!=expected)throw new ArgumentException("Physical source digest mismatch.");var value=new NavArrivalCandidateSource(id,revision,requested,priv,pub,key);var storage=allocation.Register(new NavDeclarationStorage(bytes));storage.First=NavPhysicalMacro.Acquire(storage,owners);storage.Facade=value;value.ownedSource=storage;value.BindOwnNames(owners);return storage;
                        }
                    }finally{distinct.Clear();}
                }
            }
        }
    }
}
