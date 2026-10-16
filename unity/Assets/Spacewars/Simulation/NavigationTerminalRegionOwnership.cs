using System;

namespace Spacewars.Simulation
{
    public sealed partial class NavTerminalRegion : INavStorageFacade
    {
        private NavDeclarationStorage ownedRegion;
        NavOwnedStorage INavStorageFacade.StorageRoot=>ownedRegion;
        internal NavOwnedStorage StorageRoot=>ownedRegion;
        internal static long CopyScratchBytes(NavTerminalRegion source)
        {
            long wire=checked(NavContract.StringWireBytes(source.Id)+4L+NavContract.LocationWireBytes(source.Requested)+4L);
            for(int n=0;n<source.Candidates.Count;n++)wire=checked(wire+NavContract.LocationWireBytes(source.Candidates[n]));
            return checked(8192L+128L*source.Candidates.Count+NavContract.DigestScratchBytes(wire));
        }
        internal static NavTerminalRegion CopyOwned(NavTerminalRegion source,INavStorageReservation reservation)
        {
            if(source==null||reservation==null)throw new ArgumentNullException();
            if(source.StorageRoot!=null)NavStorageAdmission.RequireOwned(reservation,source.StorageRoot);source.Requested.RequireName(reservation);foreach(var location in source.Candidates)location.RequireName(reservation);
            int count=source.Candidates.Count;
            long bytes=checked(LayoutBase+64L*count+NavProviderCopy.TextBytes(source.Id)+NavProviderCopy.TextBytes(source.Requested.SurfaceId));
            for(int n=0;n<count;n++)bytes=checked(bytes+NavProviderCopy.TextBytes(source.Candidates[n].SurfaceId));
            using(var scratch=NavStorageScratch.Reserve(reservation,CopyScratchBytes(source)))
            using(var allocation=new NavStorageAllocation(reservation.TryReserve)){
                allocation.Reserve(bytes);var storage=allocation.Register(new NavDeclarationStorage(bytes));
                var candidates=new NavLocation[count];for(int n=0;n<count;n++)candidates[n]=NavProviderCopy.Location(source.Candidates[n]);
                var value=new NavTerminalRegion(NavProviderCopy.Text(source.Id),source.Revision,NavProviderCopy.Location(source.Requested),candidates){ownedRegion=storage};
                if(value.StableKey!=source.StableKey)throw new ArgumentException("Owned terminal identity changed.");storage.Facade=value;value.BindOwnedLocations();storage.Retain();return value;
            }
        }
    }
}
