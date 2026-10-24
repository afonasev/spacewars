using System;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    // The registered B row directly owns its canonical input, exact provider
    // and the recipe shared with the scheduler's physical work graph.
    internal class NavigationHostRow : NavOwnedStorage, INavPhysicalBody
    {
        private static readonly long LayoutBytes=Math.Max(256,NavStorageLayout.ObjectBytesBound(typeof(NavigationHostRow)));
        private static readonly bool ReplayLayoutMatches=
            NavStorageLayout.ObjectBytesBound(typeof(ReplayOnlyRow))==NavStorageLayout.ObjectBytesBound(typeof(NavigationHostRow));
        private NavigationHostRequest requestRoot;
        private NavigationHostProvider providerRoot;
        private NavDeclarationStorage recipeRoot;
        private NavScheduledCompletion solvedRoot;
        private NavigationHostProjection projectionRoot;
        private NavigationHostAnswer answerRoot;
        internal NavigationRequest Request=>requestRoot.Request;
        internal NavigationHostRequest RequestOwner=>requestRoot;
        internal NavDeclarationStorage RecipeOwner=>recipeRoot;
        internal NavigationHostProvider ProviderOwner=>providerRoot;
        internal ILayeredNavigationProvider Provider=>providerRoot.Provider;
        internal NavScheduledRequest Scheduled=>(NavScheduledRequest)recipeRoot.Facade;
        internal NavigationAnswer Answer=>answerRoot?.Answer;
        internal NavigationHostAnswer AnswerOwner=>answerRoot;
        internal NavScheduledCompletion Solved=>solvedRoot;
        internal NavigationHostProjection ProjectionOwner=>projectionRoot;
        internal long ProviderIdentity {get;private set;}
        internal long ArrivalToken {get;private set;}
        internal void SetArrivalToken(long token){if(token<1||ArrivalToken!=0||solvedRoot!=null||answerRoot!=null)throw new ArgumentException("Invalid initial arrival owner link.");ArrivalToken=token;}
        internal bool MatchesCatalog(NavNameKey key,NavOwnedStorage request,NavOwnedStorage provider)
            =>ReferenceEquals(requestRoot,request)&&ReferenceEquals(providerRoot,provider)&&
              ReferenceEquals(key.Reference.Owner,recipeRoot)&&
              ReferenceEquals(key.Value,Scheduled.Subscription.StableKey);
        internal bool MatchesDelivered(NavNameKey key)
            =>answerRoot!=null&&ReferenceEquals(key.Reference.Owner,recipeRoot)&&
              ReferenceEquals(key.Value,Scheduled.Subscription.StableKey)&&
              ReferenceEquals(answerRoot.RequestOwner,requestRoot);
        private bool released;
        internal override bool IsImmutableCaptureRoot=>requestRoot!=null&&providerRoot!=null&&recipeRoot!=null;
        private sealed class ReplayOnlyRow : NavigationHostRow
        {
            internal ReplayOnlyRow(bool ownerReference):base(ownerReference){}
        }
        internal bool ReplayOnly=>this is ReplayOnlyRow;
        public NavPhysicalTag PhysicalTag=>new NavPhysicalTag(NavPhysicalStorageKind.HostRow,value:ReplayOnly?NavPhysicalValueKind.ReplayOnlyProjectionV1:NavPhysicalValueKind.None);

        private NavigationHostRow(bool ownerReference):base(LayoutBytes)
        {if(ownerReference)Retain();}
        private static NavigationHostRow Allocate(INavStorageReservation reservation,bool ownerReference,bool replayOnly=false)
        {
            using(var allocation=new NavStorageAllocationContext(reservation.TryReserve)){
                if(replayOnly&&!ReplayLayoutMatches)
                    throw new InvalidOperationException("Replay row layout changed.");
                allocation.Reserve(LayoutBytes);var value=replayOnly?new ReplayOnlyRow(ownerReference):new NavigationHostRow(ownerReference);allocation.Register(value);return value;
            }
        }

        private void Own(NavigationHostRequest request,NavigationHostProvider provider,NavDeclarationStorage recipe)
        {
            requestRoot=NavPhysicalMacro.Acquire(this,request);
            providerRoot=NavPhysicalMacro.Acquire(this,provider);
            recipeRoot=NavPhysicalMacro.Acquire(this,recipe);
        }
        internal NavigationHostRow WithAnswer(NavigationHostAnswer answer,INavStorageReservation reservation)
        {
            NavStorageAdmission.RequireOwned(reservation,this);
            NavStorageAdmission.RequireOwned(reservation,answer);
            if(answerRoot!=null||!ReferenceEquals(answer.RequestOwner,requestRoot)||
                answer.CorridorOwner!=null&&
                (!ReferenceEquals(answer.CorridorOwner.ProviderOwner,providerRoot)||
                 !ReferenceEquals(answer.CorridorOwner.TypedRoute,solvedRoot?.Route)))
                throw new ArgumentException("Host row answer request changed.");
            var next=Allocate(reservation,true,ReplayOnly);
            try{
                next.Own(requestRoot,providerRoot,recipeRoot);
                next.solvedRoot=NavPhysicalMacro.Acquire(next,solvedRoot);
                next.answerRoot=NavPhysicalMacro.Acquire(next,answer);
                next.ProviderIdentity=ProviderIdentity;next.ArrivalToken=ArrivalToken;return next;
            }catch{next.ReleaseStorage();throw;}
        }
        internal NavigationHostRow WithSolved(NavScheduledCompletion solved,INavStorageReservation reservation)
        {
            NavStorageAdmission.RequireOwned(reservation,this);
            NavStorageAdmission.RequireOwned(reservation,solved);
            if(answerRoot!=null||solvedRoot!=null||solved.Status!=NavSolveStatus.Ready||
                solved.Route==null||!ReferenceEquals(solved.Subscription,Scheduled.Subscription))
                throw new ArgumentException("Solved host row differs from its scheduler recipe.");
            var next=Allocate(reservation,true,ReplayOnly);
            NavigationHostProjection projection=null;
            try{
                next.Own(requestRoot,providerRoot,recipeRoot);
                next.solvedRoot=NavPhysicalMacro.Acquire(next,solved);
                projection=NavigationHostProjection.CreateOwned(Scheduled.Subscription.Origin,reservation,ReplayOnly&&Provider is PartitionNavigationProvider);
                projection.SealPrepared();
                next.projectionRoot=NavPhysicalMacro.Acquire(next,projection);
                next.ProviderIdentity=ProviderIdentity;next.ArrivalToken=ArrivalToken;return next;
            }catch{next.ReleaseStorage();throw;}
            finally{projection?.ReleaseStorage();}
        }
        internal NavigationHostRow WithProjection(NavigationHostProjection projection,
            INavStorageReservation reservation)
        {
            NavStorageAdmission.RequireOwned(reservation,this);
            NavStorageAdmission.RequireOwned(reservation,projection);
            if(answerRoot!=null||solvedRoot?.Route==null||projectionRoot==null||
                !projection.IsImmutableCaptureRoot||projection.ReplayOnly&&!ReplayOnly)
                throw new ArgumentException("Invalid prepared physical projection step.");
            var next=Allocate(reservation,true,ReplayOnly);
            try{
                next.Own(requestRoot,providerRoot,recipeRoot);
                next.solvedRoot=NavPhysicalMacro.Acquire(next,solvedRoot);
                next.projectionRoot=NavPhysicalMacro.Acquire(next,projection);
                next.ProviderIdentity=ProviderIdentity;next.ArrivalToken=ArrivalToken;
                projection.ValidateReplay(next,reservation);return next;
            }catch{next.ReleaseStorage();throw;}
        }
        internal enum ProjectionTransition { Leg, Replay, LegacyReset }
        internal NavigationHostRow AdvanceProjection(ProjectionTransition transition,
            NavNameReference portal,NavLocation reached,NavNameReference[] observed,
            INavStorageReservation reservation)
        {
            NavStorageAdmission.RequireOwned(reservation,this);
            var source=projectionRoot;
            if(answerRoot!=null||solvedRoot?.Route==null||source==null||!source.IsImmutableCaptureRoot)
                throw new ArgumentException("Missing sealed projection source.");
            NavigationHostProjection step=null;NavigationHostRow next=null;
            try{
                if(transition==ProjectionTransition.LegacyReset){
                    if(!source.ReplayOnly)throw new ArgumentException("Legacy projection cannot retry compaction.");
                    step=NavigationHostProjection.CreateOwned(Scheduled.Subscription.Origin,reservation);
                }else{
                    step=source.ForkOwned(reservation);
                    if(transition==ProjectionTransition.Leg){
                        if(source.ReplayCursor!=0||source.LegCursor>=solvedRoot.Route.Legs.Count)
                            throw new ArgumentException("Invalid local leg transition.");
                        step.AdvanceLeg(solvedRoot.Route.Legs[source.LegCursor],portal,
                            source.LegCursor==0?null:solvedRoot.Route.Legs[source.LegCursor-1]);
                    }else if(transition==ProjectionTransition.Replay){
                        if(source.LegCursor!=solvedRoot.Route.Legs.Count||!(Provider is PartitionNavigationProvider))
                            throw new ArgumentException("Invalid local Replay transition.");
                        step.AdvanceReplay(reached,observed);
                    }else throw new ArgumentException("Unknown local projection transition.");
                }
                step.SealPrepared();
                next=Allocate(reservation,true,ReplayOnly);next.Own(requestRoot,providerRoot,recipeRoot);
                next.solvedRoot=NavPhysicalMacro.Acquire(next,solvedRoot);
                next.projectionRoot=NavPhysicalMacro.Acquire(next,step);
                next.ProviderIdentity=ProviderIdentity;next.ArrivalToken=ArrivalToken;return next;
            }catch{next?.ReleaseStorage();throw;}
            finally{step?.ReleaseStorage();}
        }
        private static void Validate(NavigationHostRequest request,NavigationHostProvider provider,
            NavScheduledRequest recipe,long providerIdentity)
        {
            if(request==null||provider==null||recipe==null||providerIdentity<1||
                !ReferenceEquals(request.Request.Geometry,provider.Geometry)||
                !ReferenceEquals(recipe.Provider,provider.Provider)||
                recipe.Subscription.WorldGeneration!=request.Request.Session||
                recipe.Subscription.RequestSequence!=request.Request.Request||
                recipe.Subscription.EntityId!=request.Request.Entity||
                !recipe.Subscription.Origin.Position.Equals(request.Request.Start)||
                !recipe.Subscription.AssignedEndpoint.Position.Equals(request.Request.Goal))
                throw new ArgumentException("Host row request/provider/recipe identity changed.");
        }
        internal static NavigationHostRow CreateRegistered(NavigationHostRequest request,
            NavigationHostProvider provider,long providerIdentity,NavScheduledRequest recipe,
            INavStorageReservation reservation,bool replayOnly=false)
        {
            if(reservation==null||recipe==null)throw new ArgumentNullException(reservation==null?nameof(reservation):nameof(recipe));
            NavStorageAdmission.RequireOwned(reservation,request);
            NavStorageAdmission.RequireOwned(reservation,provider);
            if(recipe.StorageRoot!=null)NavStorageAdmission.RequireOwned(reservation,recipe.StorageRoot);
            var copy=recipe;
            bool newRecipe=false;
            var owner=Allocate(reservation,true,replayOnly);
            try{
                if(recipe?.StorageRoot==null){copy=NavScheduledRequest.CopyOwned(recipe,provider.Provider,reservation);newRecipe=true;}
                NavStorageAdmission.RequireOwned(reservation,copy.StorageRoot);
                Validate(request,provider,copy,providerIdentity);
                owner.Own(request,provider,(NavDeclarationStorage)copy.StorageRoot);
                owner.ProviderIdentity=providerIdentity;
                return owner;
            }catch{owner.ReleaseStorage();throw;}
            finally{if(newRecipe)copy.StorageRoot.Release();}
        }
        public void WritePhysicalBody(NavPhysicalBodyWriter writer)
        {
            Validate(requestRoot,providerRoot,Scheduled,ProviderIdentity);
            if(solvedRoot!=null&&(solvedRoot.Status!=NavSolveStatus.Ready||
                !ReferenceEquals(solvedRoot.Subscription,Scheduled.Subscription))||
                projectionRoot!=null&&(solvedRoot==null||answerRoot!=null||projectionRoot.ReplayOnly&&!ReplayOnly)||
                answerRoot!=null&&(!ReferenceEquals(answerRoot.RequestOwner,requestRoot)||
                answerRoot.CorridorOwner!=null&&
                (!ReferenceEquals(answerRoot.CorridorOwner.ProviderOwner,providerRoot)||
                 !ReferenceEquals(answerRoot.CorridorOwner.TypedRoute,solvedRoot?.Route))))
                throw new ArgumentException("Physical host row answer identity contradiction.");
            if(StorageBytes!=NavStorageLayout.Charge(LayoutBytes))
                throw new ArgumentException("Physical host row layout contradiction.");
            if(projectionRoot?.ReplayOnly==true)projectionRoot.ValidateReplay(this,writer.Reservation);
            else projectionRoot?.ValidateAgainst(this);
            writer.Output.Write(ProviderIdentity);writer.Output.Write(ArrivalToken);
            NavPhysicalMacro.WriteEdge(writer,this,requestRoot);
            NavPhysicalMacro.WriteEdge(writer,this,providerRoot);
            NavPhysicalMacro.WriteEdge(writer,this,recipeRoot);
            NavPhysicalMacro.WriteEdge(writer,this,solvedRoot,true);
            NavPhysicalMacro.WriteEdge(writer,this,projectionRoot,true);
            NavPhysicalMacro.WriteEdge(writer,this,answerRoot,true);
        }
        internal static NavigationHostRow ReadPhysicalBody(NavPhysicalBodyReader reader,bool replayOnly=false)
        {
            long identity=reader.Input.ReadInt64(),arrival=reader.Input.ReadInt64();if(arrival<0)throw new ArgumentException("Invalid arrival owner link.");
            var request=NavPhysicalMacro.ReadEdge<NavigationHostRequest>(reader);
            var provider=NavPhysicalMacro.ReadEdge<NavigationHostProvider>(reader);
            var recipe=NavPhysicalMacro.ReadEdge<NavDeclarationStorage>(reader);
            var solved=NavPhysicalMacro.ReadEdge<NavScheduledCompletion>(reader,true);
            var projection=NavPhysicalMacro.ReadEdge<NavigationHostProjection>(reader,true);
            var answer=NavPhysicalMacro.ReadEdge<NavigationHostAnswer>(reader,true);
            if(!(recipe.Facade is NavScheduledRequest scheduled))throw new ArgumentException("Missing physical host recipe.");
            Validate(request,provider,scheduled,identity);
            if(solved!=null&&(solved.Status!=NavSolveStatus.Ready||
                !ReferenceEquals(solved.Subscription,scheduled.Subscription)))
                throw new ArgumentException("Host row solved route/recipe mismatch.");
            if(projection!=null&&(solved==null||answer!=null||projection.ReplayOnly&&!replayOnly))
                throw new ArgumentException("Host row projection phase mismatch.");
            if(answer!=null&&(!ReferenceEquals(answer.RequestOwner,request)||
                answer.CorridorOwner!=null&&
                (!ReferenceEquals(answer.CorridorOwner.ProviderOwner,provider)||
                 !ReferenceEquals(answer.CorridorOwner.TypedRoute,solved?.Route))))
                throw new ArgumentException("Host row answer request/provider mismatch.");
            var owner=Allocate(reader.Reservation,false,replayOnly);
            owner.Own(request,provider,recipe);
            owner.solvedRoot=NavPhysicalMacro.Acquire(owner,solved);
            owner.projectionRoot=NavPhysicalMacro.Acquire(owner,projection);
            owner.answerRoot=NavPhysicalMacro.Acquire(owner,answer);owner.ProviderIdentity=identity;owner.ArrivalToken=arrival;
            projection?.ValidateReplay(owner,reader.Reservation);
            return owner;
        }
        internal void ReleaseStorage(){if(released)return;released=true;Release();}
        internal override void ReleaseOwner()=>ReleaseStorage();
        protected override void ReleaseEdges(){requestRoot?.Release();providerRoot?.Release();recipeRoot?.Release();solvedRoot?.Release();projectionRoot?.Release();answerRoot?.Release();}
    }
}
