using System;
using System.IO;

namespace Spacewars.Simulation
{
    // Persisted derived strings have an owning edge independently of the
    // recipe that supplied their digest inputs. Temporary digest buffers are
    // reserved before the original C# digest implementation is executed.
    internal sealed partial class NavOwnedText : NavOwnedStorage
    {
        internal readonly string Value;
        private NavOwnedText(string value,long bytes):base(bytes){Value=value;}
        protected override void ReleaseEdges(){}
        internal static NavOwnedText Copy(string source,INavStorageReservation reservation)
        {
            if(source==null||reservation==null)throw new ArgumentNullException();
            using(var scratch=NavStorageScratch.Reserve(reservation,checked(128L+2L*source.Length)))
            using(var allocation=new NavStorageAllocation(reservation.TryReserve)){
                long bytes=checked(128L+NavProviderCopy.TextBytes(source));allocation.Reserve(bytes);
                var text=allocation.Register(new NavOwnedText(NavProviderCopy.Text(source),bytes));text.Retain();return text;
            }
        }
        internal static NavOwnedText Digest(Action<BinaryWriter> write,long wireBytes,INavStorageReservation reservation)
        {
            if(write==null||reservation==null)throw new ArgumentNullException();
            using(var scratch=NavStorageScratch.Reserve(reservation,NavContract.DigestScratchBytes(wireBytes)))
            using(var allocation=new NavStorageAllocation(reservation.TryReserve)){
                const long bytes=280;allocation.Reserve(bytes);var text=allocation.Register(new NavOwnedText(NavContract.Digest(write),bytes));text.Retain();return text;
            }
        }
    }
}
