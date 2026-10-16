using System;
using System.Collections.Generic;
namespace Spacewars.Simulation
{
    public sealed partial class NavArrivalOccupancySnapshot : INavStorageFacade
    {
        private NavDeclarationStorage ownedOccupancy;
        NavOwnedStorage INavStorageFacade.StorageRoot=>ownedOccupancy;
        internal NavOwnedStorage StorageRoot=>ownedOccupancy;
        private static readonly long LayoutBase=Math.Max(512,NavStorageLayout.ObjectBytesBound(typeof(NavDeclarationStorage))+NavStorageLayout.ObjectBytesBound(typeof(NavArrivalOccupancySnapshot))+160+32);
        private NavArrivalOccupancySnapshot(long generation,NavRetainedArray<NavArrivalOccupant> priv,NavRetainedArray<NavArrivalOccupant> pub,string key)
        {Generation=generation;rows=priv.ReadOnlyBacking;Occupants=Array.AsReadOnly(pub.ReadOnlyBacking);StableKey=key;}
        private static int Compare(NavArrivalOccupant a,NavArrivalOccupant b){int c=a.ActorId.CompareTo(b.ActorId);if(c!=0)return c;c=a.Kind.CompareTo(b.Kind);return c!=0?c:string.CompareOrdinal(a.StableKey,b.StableKey);}
        private static string Key(long generation,IReadOnlyList<NavArrivalOccupant> rows)=>NavContract.Digest(w=>{w.Write(generation);w.Write(rows.Count);for(int n=0;n<rows.Count;n++)w.Write(rows[n].StableKey);});
        private static void Validate(long generation,IReadOnlyList<NavArrivalOccupant> values,INavStorageReservation reservation)
        {
            if(generation<1)throw new ArgumentException("Invalid owned occupancy generation.");for(int n=0;n<values.Count;n++){var value=values[n];if(value==null)throw new ArgumentException("Null owned occupancy row.");NavStorageAdmission.RequireOwned(reservation,value.StorageRoot);if(n>0&&Compare(values[n-1],value)>=0)throw new ArgumentException("Invalid canonical occupancy order.");}
        }
        internal static NavArrivalOccupancySnapshot CopyOwned(NavArrivalOccupancySnapshot source,IReadOnlyList<NavArrivalOccupant> ownedRows,INavStorageReservation reservation)
        {
            if(source==null||ownedRows==null||source.rows.Length!=ownedRows.Count)throw new ArgumentException("Owned occupancy row shape mismatch.");if(source.StorageRoot!=null)NavStorageAdmission.RequireOwned(reservation,source.StorageRoot);Validate(source.Generation,ownedRows,reservation);for(int n=0;n<ownedRows.Count;n++)if(source.rows[n].StableKey!=ownedRows[n].StableKey||source.StorageRoot!=null&&!ReferenceEquals(source.rows[n],ownedRows[n]))throw new ArgumentException("Owned occupancy selector changed.");
            using(NavStorageScratch.Reserve(reservation,checked(8192+NavContract.DigestScratchBytes(12L+65L*ownedRows.Count))))
            using(var allocation=new NavStorageAllocation(reservation.TryReserve)){
                allocation.Reserve(LayoutBase);NavRetainedArray<NavArrivalOccupant> priv=null,pub=null;
                try{priv=NavRetainedArray<NavArrivalOccupant>.Copy(ownedRows,reservation.TryReserve);pub=NavRetainedArray<NavArrivalOccupant>.Copy(ownedRows,reservation.TryReserve);string key=Key(source.Generation,ownedRows);if(key!=source.StableKey)throw new ArgumentException("Owned occupancy digest changed.");var value=new NavArrivalOccupancySnapshot(source.Generation,priv,pub,key);var storage=allocation.Register(new NavDeclarationStorage(LayoutBase));storage.First=NavPhysicalMacro.Acquire(storage,priv);storage.Second=NavPhysicalMacro.Acquire(storage,pub);storage.Facade=value;value.ownedOccupancy=storage;storage.Retain();return value;}
                finally{priv?.Release();pub?.Release();}
            }
        }
        internal void WritePhysicalBody(NavPhysicalBodyWriter writer,NavDeclarationStorage storage)
        {
            if(!ReferenceEquals(storage,ownedOccupancy)||!(storage.First is NavRetainedArray<NavArrivalOccupant> priv)||!(storage.Second is NavRetainedArray<NavArrivalOccupant> pub)||ReferenceEquals(priv,pub)||!ReferenceEquals(rows,priv.ReadOnlyBacking)||pub.Count!=rows.Length||Occupants.Count!=rows.Length||storage.Third!=null||storage.Fourth!=null||storage.Fifth!=null||storage.Sixth!=null||storage.Seventh!=null||storage.Eighth!=null||storage.StorageBytes!=NavStorageLayout.Charge(LayoutBase)||Generation<1)throw new ArgumentException("Physical occupancy ownership/views contradiction.");
            for(int n=0;n<rows.Length;n++)if(rows[n]==null||!ReferenceEquals(rows[n],pub[n])||!ReferenceEquals(rows[n],Occupants[n])||n>0&&Compare(rows[n-1],rows[n])>=0)throw new ArgumentException("Physical occupancy alias/order contradiction.");writer.Output.Write(Generation);NavPhysicalMacro.WriteEdge(writer,storage,priv);NavPhysicalMacro.WriteEdge(writer,storage,pub);NavPhysicalTextPlan.WriteData(writer,NavContract.Sha256(StableKey));
        }
        internal static NavDeclarationStorage ReadPhysicalBody(NavPhysicalBodyReader reader)
        {
            long generation=reader.Input.ReadInt64();var priv=NavPhysicalMacro.ReadEdge<NavRetainedArray<NavArrivalOccupant>>(reader);var pub=NavPhysicalMacro.ReadEdge<NavRetainedArray<NavArrivalOccupant>>(reader);if(ReferenceEquals(priv,pub)||priv.Count!=pub.Count)throw new ArgumentException("Physical occupancy backing shape mismatch.");Validate(generation,priv,reader.Reservation);for(int n=0;n<priv.Count;n++)if(!ReferenceEquals(priv[n],pub[n]))throw new ArgumentException("Physical occupancy row alias mismatch.");
            using(NavStorageScratch.Reserve(reader.Reservation,checked(8192+NavContract.DigestScratchBytes(12L+65L*priv.Count))))
            using(var allocation=new NavStorageAllocation(reader.Reservation.TryReserve)){
                allocation.Reserve(LayoutBase);var chars=new char[64];for(int n=0;n<64;n++)chars[n]=(char)reader.Input.ReadUInt16();string expected=NavContract.Sha256(new string(chars)),key=Key(generation,priv);if(key!=expected)throw new ArgumentException("Physical occupancy digest mismatch.");var value=new NavArrivalOccupancySnapshot(generation,priv,pub,key);var storage=allocation.Register(new NavDeclarationStorage(LayoutBase));storage.First=NavPhysicalMacro.Acquire(storage,priv);storage.Second=NavPhysicalMacro.Acquire(storage,pub);storage.Facade=value;value.ownedOccupancy=storage;return storage;
            }
        }
    }
}
