using System;
using System.IO;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    // Per-call admission, never a process/thread decoder registry. The private
    // envelope, nested decoder objects and installed BODY use this one ledger.
    internal sealed class NavigationDecodeTransaction : IDisposable
    {
        internal readonly NavigationRetentionBudget Budget;
        private readonly NavigationRetentionBudget.Scope scope;
        internal INavStorageReservation Reservation=>scope.OwnedReservation();
        internal NavigationDecodeTransaction(NavigationRetentionBudget budget=null)
        {
            Budget=budget??new NavigationRetentionBudget();
            if(!Budget.TryOpenScope(NavigationPreparationKind.Decode,0,0,out scope))throw new InvalidOperationException("Outer navigation decode admission refused.");
        }
        internal sealed class PrivateValue<T> : NavOwnedStorage
        {
            internal T Value;
            internal readonly NavigationDecodeTransaction Transaction;
            internal override bool IsImmutableCaptureRoot=>false;
            internal PrivateValue(long bytes,NavigationDecodeTransaction transaction):base(bytes){Transaction=transaction;}
            protected override void ReleaseEdges(){Value=default;}
        }
        private bool disposed;
        internal PrivateValue<T> OwnValue<T>(long payloadBytes,Func<T> create)
        {
            if(disposed)throw new ObjectDisposedException(nameof(NavigationDecodeTransaction));
            using(var allocation=new NavStorageAllocation(Reservation.TryReserve)){
                long bytes=checked(NavStorageLayout.ObjectBytesBound(typeof(PrivateValue<T>))+payloadBytes);
                allocation.Reserve(bytes);var owner=allocation.Register(new PrivateValue<T>(bytes,this));owner.Value=create();return owner;
            }
        }
        internal T Own<T>(long payloadBytes,Func<T> create)=>OwnValue(payloadBytes,create).Value;
        internal void RequirePrivateBytes(PrivateValue<byte[]> owner,byte[] bytes)
        {
            if(disposed||owner==null||!ReferenceEquals(owner.Transaction,this)||!ReferenceEquals(owner.Value,bytes))throw new ArgumentException("Invalid private decode BODY owner.");
            NavStorageAdmission.RequireOwned(Reservation,owner);
        }
        internal T[] Array<T>(int length,Func<T[]> create)
            =>Own(checked(32L+(long)NavStorageLayout.ValueBytes(typeof(T))*length),create);
        internal T Record<T>(Func<T> create) where T:class=>Own(NavStorageLayout.ObjectBytesBound(typeof(T)),create);
        internal NavPolygon Polygon(NavPoint[] points)
        {
            var value=NavPolygon.CreateOwned(points,Reservation);
            // Staging owns the birth, not a persistent owner reference. Any
            // surviving geometry must acquire the actual polygon edge.
            value.StorageRoot.ReleaseOwner();return value;
        }
        internal string String(int utf8Bytes,Func<string> create)=>Own(checked(32L+2L*utf8Bytes),create);
        public void Dispose(){if(disposed)return;disposed=true;scope.Discard();}
    }
    internal sealed class NavigationAdmittedReader : BinaryReader
    {
        internal readonly NavigationDecodeTransaction Transaction;
        internal NavigationAdmittedReader(Stream input,NavigationDecodeTransaction transaction):base(input,System.Text.Encoding.UTF8){Transaction=transaction;}
    }
}
