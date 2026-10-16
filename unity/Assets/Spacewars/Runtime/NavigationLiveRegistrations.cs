using System;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    // The qualified physical lane has one registration order and one owner.
    // Its fixed concrete backing is reproduced by the cold factory; outer
    // registered indices plus BODY row roles authenticate the occupied order.
    internal sealed class NavigationLiveRegistrations : NavOwnedStorage,System.Collections.Generic.IReadOnlyList<NavigationRequest>
    {
        internal const int Capacity=NavigationSession.RouteRequestCapacity;
        internal struct Entry{internal NavigationHostRequest Request;internal NavDeclarationStorage Recipe;}
        private static readonly long Layout=checked(NavStorageLayout.ObjectBytesBound(typeof(NavigationLiveRegistrations))+32L+Capacity*NavStorageLayout.ValueBytes(typeof(Entry)));
        private readonly Entry[] entries;
        public int Count{get;private set;}
        public NavigationRequest this[int index]=>RequestAt(index);
        public System.Collections.Generic.IEnumerator<NavigationRequest> GetEnumerator(){for(int i=0;i<Count;i++)yield return RequestAt(i);}
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()=>GetEnumerator();
        internal NavigationLiveRegistrations RetiredNext;
        internal int Revision{get;private set;}
        private bool released;
        internal override bool IsImmutableCaptureRoot=>false;
        private NavigationLiveRegistrations():base(Layout){entries=new Entry[Capacity];Retain();}
        internal static NavigationLiveRegistrations CreateOwned(INavStorageReservation r)
        {using(var allocation=new NavStorageAllocation(r.TryReserve)){allocation.Reserve(Layout);return allocation.Register(new NavigationLiveRegistrations());}}
        internal NavigationRequest RequestAt(int index)=>entries[index].Request.Request;
        internal NavigationHostRequest RequestOwnerAt(int index)=>entries[index].Request;
        internal NavDeclarationStorage RecipeOwnerAt(int index)=>entries[index].Recipe;
        internal int IndexOf(NavigationRequest request)
        {for(int i=0;i<Count;i++)if(ReferenceEquals(entries[i].Request.Request,request))return i;return -1;}
        internal bool TryGraphKey(NavigationRequest request,out string key)
        {int index=IndexOf(request);key=index<0?null:((NavScheduledRequest)entries[index].Recipe.Facade).GraphKey;return index>=0;}
        internal bool CanAppend(NavigationHostRequest request,NavDeclarationStorage recipe,int revision,int count)
            =>Revision==revision&&Count==count&&Count<Capacity&&request!=null&&recipe?.Facade is NavScheduledRequest&&ReferenceEquals(request.Request.StorageOwner,request)&&IndexOf(request.Request)<0;
        // The prepared carrier preacquires both edges and transfers them here.
        internal void AppendNoThrow(NavigationHostRequest request,NavDeclarationStorage recipe)
        {entries[Count++]=new Entry{Request=request,Recipe=recipe};Revision++;}
        internal bool CanExtract(int index,NavigationRequest request,int revision)
            =>Revision==revision&&index>=0&&index<Count&&ReferenceEquals(entries[index].Request.Request,request);
        // A value entry is not an intrusive node. Its two owning references
        // transfer into explicit paid retirement storage supplied by caller.
        internal void ExtractNoThrow(int index,out NavigationHostRequest request,out NavDeclarationStorage recipe)
        {
            request=entries[index].Request;recipe=entries[index].Recipe;for(int i=index+1;i<Count;i++)entries[i-1]=entries[i];entries[--Count]=default;Revision++;
        }
        internal void AppendColdOwned(NavigationHostRow row)
        {
            if(row==null||!CanAppend(row.RequestOwner,row.RecipeOwner,Revision,Count))throw new ArgumentException("Cold registration order/owner is invalid.");RequireEdge(row.RequestOwner);RequireEdge(row.RecipeOwner);row.RequestOwner.Retain();row.RecipeOwner.Retain();AppendNoThrow(row.RequestOwner,row.RecipeOwner);
        }
        internal void ReleaseStorage(){if(released)return;released=true;Release();}
        internal override void ReleaseOwner()=>ReleaseStorage();
        protected override void ReleaseEdges(){for(int i=0;i<Count;i++){entries[i].Request.Release();entries[i].Recipe.Release();entries[i]=default;}Count=0;}
    }
}
