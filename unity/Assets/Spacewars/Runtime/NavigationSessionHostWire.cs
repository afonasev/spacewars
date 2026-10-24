using System;
using System.IO;
using System.Linq;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    public sealed partial class NavigationSession
    {
        // This nested BODY path is admitted to the Session's physical ledger.
        // The outer world/private-input transaction still has a separate gate.
        private void InitializePhysicalHostForCapture()
        {
            NavGeometry observed;long operation;
            using(EnterTransport()){
                if(!physicalArrivalEnabled||committedHostRoot!=null)return;
                if(commandGroup!=null||admissionBatchDepth!=0)throw new InvalidOperationException("Cannot capture an open navigation admission batch.");
                observed=navigationGeometry;operation=authorityOperationIdentity;
            }
            if(!TryOpenPhysicalHostScope(NavigationPreparationKind.Capture,Generation,Admission.PublicationIdentity,out var scope))throw new InvalidOperationException("Empty host capture admission refused.");
            NavigationHostRoot root=null;NavPagedList<long> bank=null;NavigationCreditPublication holder=null;
            using(scope){try{
                root=CreatePhysicalHostRoot(scope.OwnedReservation());root.StageAuthorityGeometry(observed,scope.OwnedReservation());StageRetainedPhysicalArrivals(root,scope.OwnedReservation());
                root.SetFrontier(new CertifiedRouteKernel.PumpFrontier(CertifiedRouteKernel.PumpStage.Idle,0,0,0,0,0,0,0,0,0),0,0);
                root.SealPrepared();
                if(accruedCreditsEnabled){
                    bank=NavigationAccruedCredits.Create(Generation,accruedCreditCap,scope.OwnedReservation());holder=NavigationCreditPublication.Create(root,bank,scope.OwnedReservation());scope.Attach(holder);
                    scope.Publish(holder,_=>{using(EnterTransport()){
                        if(operation!=authorityOperationIdentity||!ReferenceEquals(observed,navigationGeometry)||committedHostRoot!=null||creditBank!=null)throw new PhysicalIngressRefused("Accrued host birth lost source.");
                        root.Retain();bank.Retain();committedHostRoot=root;creditBank=bank;
                    }});scope.ResolveCommitted();
                }else{scope.Attach(root);PublishPhysicalHostRoot(scope,null,root,()=>{if(operation!=authorityOperationIdentity||!ReferenceEquals(observed,navigationGeometry))throw new InvalidOperationException("Authority changed during empty host capture.");});}
            }finally{holder?.Dispose();bank?.ReleaseStorage();root?.ReleaseStorage();}}
        }
        private NavigationCaptureBytes CaptureHostAuthority(NavigationHostRoot root,NavigationAuthorityOwners authority,NavigationSessionState state)
        {
            if(!TryOpenPhysicalHostScope(NavigationPreparationKind.Capture,root.Epoch,
                admissionPublicationIdentity,out var scope))throw new InvalidOperationException("Host capture admission refused.");
            using(scope){
                NavigationCaptureBytes output=null;
                try{
                    NavigationAuthorityOwners snapshot=null;
                    try{
                        using(NavStorageScratch.Reserve(scope.OwnedReservation(),checked(32L+4L*state.Requests.Length+32L+8L*(state.GroupLayouts.Length+state.CollectionLayouts.Length+state.CaptureExtraLayouts.Length)))){
                            var roles=new int[state.Requests.Length];for(int i=0;i<roles.Length;i++)roles[i]=CatalogRoleMask(state,i);
                            snapshot=NavigationAuthorityOwners.CreateCatalogOwned(authority,state.CaptureRequests,state.CaptureGeometries,state.CaptureGroups,state.CaptureIdentities,state.CaptureCrowdNames,state.CaptureCrowdKeys,state.CaptureCrowdPoints,state.CaptureCrowdFlags,roles,state.GroupLayouts.Concat(state.CollectionLayouts).Concat(state.CaptureExtraLayouts).ToArray(),state.CaptureTerrainNames,state.CaptureActorGlobal,state.CaptureActorLocal,state.CaptureInstalled,scope.OwnedReservation(),state.CaptureArrivalCatalog,state.CaptureLeaseCatalog,state.CaptureProbeAnswers,state.CaptureMailboxAnswers,state.CaptureBarrierAnswers,state.CaptureAnswerTrackingLayout,state.CaptureAnswerTrackingOwners);
                        }
                        if(root.AccruedCredits)NavigationAccruedCredits.Validate(state.CaptureBank,root,Generation,state.RequestSequence,long.MaxValue-1);
                        NavOwnedStorage envelope=root.AccruedCredits?(NavOwnedStorage)NavigationAccruedHostEnvelope.Create(root,snapshot,state.CaptureTraffic,state.CaptureBank,state.AdmissionPublicationIdentity,scope.OwnedReservation()):NavigationHostEnvelope.CreateOwned(root,snapshot,scope.OwnedReservation(),state.AdmissionPublicationIdentity,state.CaptureTraffic);
                    try{output=NavigationCaptureBytes.Write(scope.OwnedReservation(),
                        writer=>NavPhysicalBodyWire.WriteTo(writer,envelope,scope.OwnedReservation()));}
                    finally{envelope.ReleaseOwner();}
                        scope.Attach(output);scope.Commit(output,_=>{});
                        snapshot.Retain();var pinnedCore=state.CaptureAuthority;state.CaptureAuthority=snapshot;pinnedCore?.Release();
                        state.CaptureTerrainNames?.Release();state.CaptureTerrainNames=null;return output;
                    }finally{snapshot?.ReleaseStorage();}
                }catch{output?.Dispose();throw;}
            }
        }
        internal static void DecodeDetachedHostAuthority(byte[] bytes,NavigationRetentionBudget budget,out NavigationHostRoot host,out NavigationAuthorityOwners authority)
            =>DecodeDetachedHostAuthority(bytes,budget,out host,out authority,out _);
        internal static void DecodeDetachedHostAuthority(byte[] bytes,NavigationRetentionBudget budget,out NavigationHostRoot host,out NavigationAuthorityOwners authority,out long publication)
        {DecodeDetachedHostAuthority(bytes,budget,null,out host,out authority,out publication,out var traffic,out var bank);traffic?.Release();if(bank!=null){bank.Release();host?.Release();authority?.Release();host=null;authority=null;throw new ArgumentException("Accrued decode requires a matching bank owner.");}}
        internal static void DecodeDetachedHostAuthority(NavigationSessionState state,NavigationDecodeTransaction transaction,out NavigationHostRoot host,out NavigationAuthorityOwners authority,out long publication)
        {
            if(state==null||transaction==null)throw new ArgumentNullException();
            if(state.HostAuthority==null&&state.DecodeHostOwner==null){DecodeDetachedHostAuthority((byte[])null,transaction.Budget,out host,out authority,out publication);return;}
            transaction.RequirePrivateBytes(state.DecodeHostOwner,state.HostAuthority);
            DecodeDetachedHostAuthority(state.HostAuthority,transaction.Budget,state.DecodeHostOwner,out host,out authority,out publication,out var traffic,out var bank);state.PreparedTraffic=traffic;state.PreparedBank=bank;
        }
        private static void DecodeDetachedHostAuthority(byte[] bytes,NavigationRetentionBudget budget,NavigationDecodeTransaction.PrivateValue<byte[]> privateOwner,out NavigationHostRoot host,out NavigationAuthorityOwners authority,out long publication,out NavigationTrafficAuthority traffic,out NavPagedList<long> bank)
        {
            host=null;authority=null;traffic=null;bank=null;publication=0;if(bytes==null)return;
            if(bytes.Length<1||bytes.Length>32*1024*1024)throw new ArgumentException("Invalid private host BODY input.");
            if(budget==null||!budget.TryOpenScope(NavigationPreparationKind.Decode,0,0,out var scope))throw new InvalidOperationException("Host decode admission refused.");
            NavOwnedStorage decoded=null;
            using(scope){
                bool retained=false;
                try{
                    if(privateOwner!=null){privateOwner.Transaction.RequirePrivateBytes(privateOwner,bytes);NavStorageAdmission.RequireOwned(scope.OwnedReservation(),privateOwner);privateOwner.Retain();retained=true;}
                    using(NavStorageScratch.Reserve(scope.OwnedReservation(),checked(512L+(privateOwner==null?bytes.Length:0)))){
                        var input=privateOwner==null?(byte[])bytes.Clone():bytes;
                        using(var reader=new BinaryReader(new MemoryStream(input,false))){
                            decoded=NavPhysicalBodyWire.ReadFrom(reader,scope.OwnedReservation(),NavigationHostBodyExtension.Instance);
                            if(!(decoded is NavigationHostRoot)&&!(decoded is NavigationHostEnvelope)&&!(decoded is NavigationAccruedHostEnvelope)||reader.BaseStream.Position!=reader.BaseStream.Length)throw new ArgumentException("Invalid host BODY tail.");
                        }
                    }
                    scope.Attach(decoded);scope.Commit(decoded,_=>{});
                    if(decoded is NavigationAccruedHostEnvelope accrued){publication=accrued.PublicationIdentity;host=accrued.Host;authority=accrued.Authority;traffic=accrued.Traffic;bank=accrued.Bank;bank.Retain();traffic?.Retain();host.Retain();authority.Retain();accrued.ReleaseStorage();}
                    else if(decoded is NavigationHostEnvelope envelope){publication=envelope.PublicationIdentity;host=envelope.Host;authority=envelope.Authority;traffic=envelope.Traffic;traffic?.Retain();host.Retain();authority.Retain();envelope.ReleaseStorage();}
                    else{host=(NavigationHostRoot)decoded;if(host.AccruedCredits)throw new ArgumentException("Accrued host requires its envelope and bank.");}
                }catch{decoded?.ReleaseOwner();throw;}
                finally{if(retained)privateOwner.Release();}
            }
        }
        private static int CatalogRoleMask(NavigationSessionState state,int index)
        {
            int role=0;
            if((state.AllocationRequestIndices??Array.Empty<int>()).Contains(index))role|=1;
            if((state.RegisteredRequestIndices??Array.Empty<int>()).Contains(index))role|=2;
            if((state.DeliveredRequestIndices??Array.Empty<int>()).Contains(index))role|=4;
            if((state.PendingRequestIndices??Array.Empty<int>()).Contains(index))role|=8;
            if((state.ProbeRequestIndices??Array.Empty<int>()).Contains(index))role|=16;
            if((state.RequestMailboxIndices??Array.Empty<int>()).Contains(index))role|=32;
            if((state.AnswerMailbox??Array.Empty<NavigationAnswerState>()).Any(a=>a.RequestIndex==index))role|=64;
            if((state.BarrierAnswers??Array.Empty<NavigationAnswerState>()).Any(a=>a.RequestIndex==index))role|=128;
            if((state.ProbeAnswers??Array.Empty<NavigationAnswerState>()).Any(a=>a.RequestIndex==index))role|=256;
            if(role==0||(role&1)!=0&&(role&6)!=0||(role&6)==6)throw new ArgumentException("Invalid authority request role partition.");return role;
        }
        // The outer catalog index and its role select the exact BODY owner.
        // Values/bindings never select a substitute owner for hydration.
        private static NavigationHostRequest BodyRequestOwnerAt(NavigationHostRoot root,NavigationAuthorityOwners authority,NavigationSessionState state,int index)
        {
            if(authority?.HasCatalog==true){
                if(authority.RequestCatalog.Count!=state.Requests.Length||index<0||index>=authority.RequestCatalog.Count||authority.RequestRoles[index]!=CatalogRoleMask(state,index)||!(authority.RequestCatalog[index] is NavigationHostRequest owner))throw new ArgumentException("BODY request catalog index/role mismatch.");
                return owner;
            }
            if(root==null)return null;
            var saved=state.Requests[index];if(saved==null)throw new ArgumentException("Null navigation catalog record.");
            bool allocation=(state.AllocationRequestIndices??Array.Empty<int>()).Contains(index);
            bool registered=(state.RegisteredRequestIndices??Array.Empty<int>()).Contains(index);
            bool delivered=(state.DeliveredRequestIndices??Array.Empty<int>()).Contains(index);
            if((allocation?1:0)+(registered?1:0)+(delivered?1:0)>1)throw new ArgumentException("Overlapping BODY request catalog roles.");
            if(allocation){
                if(!root.ArrivalIntents.TryGetValue(saved.Request,out var entry)||!(entry is NavigationHostRequest intent))throw new ArgumentException("Missing BODY allocation request role.");
                return intent;
            }
            if(!registered&&!delivered)return null;
            NavigationHostRequest found=null;
            foreach(var entry in registered?root.Rows:root.Delivered){
                var row=(NavigationHostRow)entry.Value;var request=row.Request;
                if(request.Session!=saved.Session||request.Request!=saved.Request)continue;
                if(found!=null)throw new ArgumentException("Duplicate BODY request owner for catalog role.");found=row.RequestOwner;
            }
            return found??throw new ArgumentException("Missing BODY request catalog owner.");
        }
        // Session:Request may name original and assigned arrival payloads. The
        // role is authoritative BODY/catalog metadata, never a value alias.
        private static int RequestCatalogRole(NavigationHostRoot root,NavigationAuthorityOwners authority,NavigationSessionState state,int index)
        {
            if((state.AllocationRequestIndices??Array.Empty<int>()).Contains(index))return 1;
            var owner=BodyRequestOwnerAt(root,authority,state,index);
            if(owner!=null){
                foreach(var entry in root.Rows){var row=(NavigationHostRow)entry.Value;if(ReferenceEquals(row.RequestOwner,owner)&&row.ArrivalToken>0)return 2;}
                foreach(var entry in root.Delivered){var row=(NavigationHostRow)entry.Value;if(ReferenceEquals(row.RequestOwner,owner)&&row.ArrivalToken>0)return 2;}
            }
            return state.Requests[index].Entity<0?3:0;
        }
        private static void ValidateBodyGroupCatalog(NavigationAuthorityOwners authority,NavigationSessionState state)
        {
            if(authority?.HasCatalog!=true)return;
            if(state.Groups==null||state.GroupKeys==null||state.GroupLayouts==null||state.CollectionLayouts==null||authority.GroupCatalog.Count!=state.Groups.Length||authority.GroupCatalog.Count!=state.GroupKeys.Length||authority.CollectionCatalog.Count!=authority.ExpectedCollectionCount)throw new ArgumentException("Missing mandatory BODY group/collection catalog.");
            if(authority.HasProbeStorage&&(authority.MailboxAnswerCatalog.Count!=state.AnswerMailbox.Length||authority.BarrierAnswerCatalog.Count!=(state.BarrierAnswers?.Length??0)))throw new ArgumentException("Answer BODY role count differs from DTO.");
            for(int i=0;i<authority.GroupCatalog.Count;i++){
                var version=(NavigationAuthorityGroup)authority.GroupCatalog[i];if(version.Data.GroupId!=state.GroupKeys[i])throw new ArgumentException("BODY group catalog index/key mismatch.");
                var row=state.Groups.SingleOrDefault(g=>g.GroupId==state.GroupKeys[i]);
                if(row==null||row.Members==null||!version.Data.BitwiseSame(new NavigationAuthorityGroup.Values(row))||version.OwnerId!=row.OwnerId||version.Source!=row.Source||version.OriginalLocation?.SurfaceId!=row.OriginalLocation?.SurfaceId||version.Members.Count!=row.Members.Length)throw new ArgumentException("Outer group differs from BODY owner.");
                for(int m=0;m<row.Members.Length;m++){var member=(NavigationAuthorityMember)version.Members[m];var saved=row.Members[m];if(saved==null||!member.Data.BitwiseSame(new NavigationAuthorityMember.Values(saved))||member.ContactPhase!=saved.ContactPhase||member.GoalLocation?.SurfaceId!=saved.GoalLocation?.SurfaceId)throw new ArgumentException("Outer member differs from BODY role/index.");}
            }
            if(state.GroupLayouts.Length!=2||state.CollectionLayouts.Length!=6)throw new ArgumentException("Invalid BODY collection catalog count.");
            for(int i=0;i<8;i++){var expected=i<2?state.GroupLayouts[i]:state.CollectionLayouts[i-2];if(expected==null||!((NavRetainedArray<int>)authority.CollectionCatalog[i]).SequenceEqual(expected))throw new ArgumentException("Outer collection layout differs from BODY.");}
        }
        private static bool SameBodyMember(NavigationMemberIdentity a,NavigationMemberIdentity b)
            =>a==null||b==null?a==b:a.Incarnation==b.Incarnation&&a.MobilityRevision==b.MobilityRevision&&a.GroupId==b.GroupId&&a.OrderRevision==b.OrderRevision&&a.MembershipRevision==b.MembershipRevision;
        private static void ValidateBodyRequestCatalog(NavigationRequestState saved,NavigationHostRequest owner)
        {
            var r=owner.Request;
            if(saved.Session!=r.Session||saved.Request!=r.Request||saved.Entity!=r.Entity||saved.Order!=r.Order||
                !saved.Start.Equals(r.Start)||!saved.Goal.Equals(r.Goal)||!saved.StartLocation.Equals(r.StartLocation)||!saved.GoalLocation.Equals(r.GoalLocation)||
                saved.ProfileId!=r.ProfileId||saved.ProfileRevision!=r.ProfileRevision||saved.HeldIdentity!=r.HeldIdentity||saved.CertifiedProviderBinding!=r.CertifiedProviderBinding||
                !SameBodyMember(saved.MemberIdentity,r.MemberIdentity)||!SameGeometry(saved.Geometry,r.Geometry)||!SameGeometry(saved.BaseGeometry,r.BaseGeometry)||
                saved.ProfileValues==null||saved.ProfileValues.Length!=r.Profile.WireValueCount)throw new ArgumentException("Outer request catalog differs from BODY owner.");
            if(!r.Profile.MatchesWireValues(saved.ProfileValues))throw new ArgumentException("Outer request profile differs from BODY owner.");
        }
        private NavGeometry[] BodyFirstGeometryCatalog(NavigationHostRoot root,NavigationAuthorityOwners authority,NavigationSessionState state)
        {
            var catalog=new NavGeometry[state.GeometryTable.Length];
            if(authority?.HasCatalog==true){
                if(authority.GeometryCatalog.Count!=catalog.Length)throw new ArgumentException("BODY geometry catalog count mismatch.");
                for(int i=0;i<catalog.Length;i++){if(!(authority.GeometryCatalog[i] is NavGeometry.OwnedBuffers owner)||!SameGeometry(state.GeometryTable[i],owner.Facade))throw new ArgumentException("BODY geometry catalog index mismatch.");catalog[i]=owner.Facade;}
                if(!ReferenceEquals(catalog[0].StorageRoot,authority.Geometry)||!ReferenceEquals(catalog[state.NavigationGeometryIndex].StorageRoot,authority.NavigationGeometry))throw new ArgumentException("BODY core geometry catalog alias mismatch.");
            }
            catalog[0]=geometry;catalog[state.NavigationGeometryIndex]=navigationGeometry;
            void Bind(int index,NavGeometry facade){
                if(index<0||index>=catalog.Length||facade.StorageRoot==null||!SameGeometry(state.GeometryTable[index],facade))throw new ArgumentException("Invalid BODY geometry catalog role.");
                var previous=catalog[index];
                if(previous!=null&&!ReferenceEquals(previous.StorageRoot,facade.StorageRoot))throw new ArgumentException("Distinct BODY geometry owners alias one catalog index.");
                catalog[index]=facade;
            }
            for(int index=0;index<state.Requests.Length;index++){
                var owner=BodyRequestOwnerAt(root,authority,state,index);if(owner==null)continue;
                ValidateBodyRequestCatalog(state.Requests[index],owner);
                Bind(state.Requests[index].GeometryIndex,owner.Request.Geometry);Bind(state.Requests[index].BaseGeometryIndex,owner.Request.BaseGeometry);
            }
            for(int index=0;index<catalog.Length;index++)if(catalog[index]==null){if(authority?.HasCatalog==true)throw new ArgumentException("Missing mandatory BODY geometry catalog entry.");catalog[index]=LoadGeometry(state.GeometryTable[index]);}
            return catalog;
        }
        private static void BindRestoredHostCatalog(NavigationHostRoot root,NavigationAuthorityOwners authority,NavigationSessionState state,NavigationRequest[] restored)
        {
            var registered=state.RegisteredRequestIndices??Array.Empty<int>();var delivered=state.DeliveredRequestIndices??Array.Empty<int>();
            var indices=registered.Concat(delivered).ToArray();
            if(root==null){if(indices.Length!=0)throw new ArgumentException("Host catalog without BODY.");return;}
            if(root.AuthorityGeometry!=null&&!state.GeometryTable.Any(g=>SameGeometry(g,root.AuthorityGeometry.Facade)))throw new ArgumentException("Host geometry owner is outside captured authority history.");
            foreach(int index in state.AllocationRequestIndices??Array.Empty<int>()){
                if(index<0||index>=restored.Length)throw new ArgumentException("Invalid allocating catalog index.");
                if(authority?.HasCatalog==true){if(!ReferenceEquals(restored[index],BodyRequestOwnerAt(root,authority,state,index).Request))throw new ArgumentException("Allocating catalog owner alias mismatch.");continue;}
                if(!root.ArrivalIntents.TryGetValue(restored[index].Request,out var entry)||!(entry is NavigationHostRequest intent)||
                    intent.Request.Entity!=restored[index].Entity||intent.Request.Order!=restored[index].Order||!intent.Request.Start.Equals(restored[index].Start)||!intent.Request.Goal.Equals(restored[index].Goal))throw new ArgumentException("Invalid allocating physical intent catalog.");
                restored[index]=intent.Request;
            }
            if(registered.Length!=root.Rows.Count||delivered.Length!=root.Delivered.Count)throw new ArgumentException("Host request catalog size mismatch.");
            var seen=new System.Collections.Generic.HashSet<long>();
            foreach(int index in indices){
                if(index<0||index>=restored.Length||!seen.Add(restored[index].Request))throw new ArgumentException("Duplicate host catalog index.");
                var expected=restored[index];
                var row=root.Rows.Concat(root.Delivered).Select(e=>(NavigationHostRow)e.Value).SingleOrDefault(r=>r.Request.Session==expected.Session&&r.Request.Request==expected.Request);
                if(row==null||row.Request.Entity!=expected.Entity||row.Request.Order!=expected.Order||
                    !row.Request.Start.Equals(expected.Start)||!row.Request.Goal.Equals(expected.Goal)||
                    row.Request.ProfileId!=expected.ProfileId||row.Request.ProfileRevision!=expected.ProfileRevision||
                    row.Request.CertifiedProviderBinding!=expected.CertifiedProviderBinding||
                    !ReferenceEquals(row.Request.Geometry.StorageRoot,expected.Geometry.StorageRoot)||
                    !ReferenceEquals(row.Request.BaseGeometry.StorageRoot,expected.BaseGeometry.StorageRoot))throw new ArgumentException("Host request catalog differs from BODY.");
                if(registered.Contains(index)!=root.Rows.Any(e=>ReferenceEquals(e.Value,row)))throw new ArgumentException("Host catalog partition differs from BODY.");
                restored[index]=row.Request;
            }
        }
        private static bool SameHostCorridor(NavCorridorDescriptor a,NavCorridorDescriptor b)
        {
            if(a==null||b==null)return a==b;
            // Compare the complete wire descriptor, including group sections.
            return a.StableKey==b.StableKey;
        }
        private static NavigationAnswer ResolveRestoredAnswerRole(NavRetainedArray<NavOwnedStorage> catalog,int index,NavigationHostRoot root,NavigationRequest request,NavigationAnswerState saved)
        {
            if(catalog==null)return ResolveRestoredHostAnswer(root,request,saved);
            if(index<0||index>=catalog.Count)throw new ArgumentException("Missing mailbox answer BODY role.");
            if(catalog[index]==null){var legacy=ResolveRestoredHostAnswer(root,request,saved);if(legacy.StorageOwner!=null)throw new ArgumentException("Missing exact owned mailbox answer role.");return legacy;}
            if(!(catalog[index] is NavigationHostAnswer owner)||!ReferenceEquals(owner.RequestOwner,request.StorageOwner)||!ReferenceEquals(owner.Answer.Request,request)||owner.Answer.Status!=saved.Status||!owner.Answer.ReadOnlyRouteBacking.SequenceEqual(saved.Route)||!SameHostCorridor(owner.Answer.Corridor,saved.Corridor))throw new ArgumentException("Mailbox answer differs from exact BODY role.");
            return owner.Answer;
        }
        private static NavigationAnswer ResolveRestoredHostAnswer(NavigationHostRoot root,NavigationRequest request,NavigationAnswerState saved)
        {
            var row=root?.Delivered.Select(e=>(NavigationHostRow)e.Value).SingleOrDefault(r=>ReferenceEquals(r.Request,request));
            if(row==null)return new NavigationAnswer(request,saved.Route,saved.Status,saved.Corridor);
            if(row.Answer.Status!=saved.Status||!row.Answer.CopyRoute().SequenceEqual(saved.Route)||!SameHostCorridor(row.Answer.Corridor,saved.Corridor))
                throw new ArgumentException("Delivered answer facade differs from BODY.");
            return row.Answer;
        }
        private void BindInstalledHostOwners(NavigationHostRoot root)
        {
            if(root==null)return;
            foreach(var installed in installedExecutions.Values){
                var row=root.Delivered.Select(e=>(NavigationHostRow)e.Value).SingleOrDefault(r=>r.Request.Entity==installed.Entity&&r.Request.Request==installed.Corridor.Request);
                if(row==null)continue;
                if(installed.StorageOwner is NavigationInstalledExecutionOwner owned){
                    if(!ReferenceEquals(owned.Request,row.RequestOwner)||!ReferenceEquals(owned.Corridor,row.AnswerOwner?.CorridorOwner)||!ReferenceEquals(installed.Corridor,row.Answer.Corridor)||!ReferenceEquals(installed.Profile,row.Request.Profile)||!ReferenceEquals(installed.MovementGeometry,row.Request.Geometry)||!ReferenceEquals(installed.BaseGeometry,row.Request.BaseGeometry)||!ReferenceEquals(installed.HeldIdentity,row.Request.HeldIdentity))throw new ArgumentException("Installed historical BODY edges differ from delivered roles.");
                    continue;
                }
                if(!SameHostCorridor(row.Answer.Corridor,installed.Corridor)||row.Request.ProfileId!=installed.Profile.ProfileId||
                    row.Request.ProfileRevision!=installed.Profile.Revision||!SaveProfile(row.Request.Profile).SequenceEqual(SaveProfile(installed.Profile))||
                    !SameGeometry(SaveGeometry(row.Request.Geometry),installed.MovementGeometry)||
                    !SameGeometry(SaveGeometry(row.Request.BaseGeometry),installed.BaseGeometry))throw new ArgumentException("Installed use differs from delivered owner.");
                installed.Corridor=row.Answer.Corridor;installed.Profile=row.Request.Profile;
                installed.MovementGeometry=row.Request.Geometry;installed.BaseGeometry=row.Request.BaseGeometry;installed.HeldIdentity=row.Request.HeldIdentity;
            }
        }
        private void InstallRestoredHost(NavigationHostRoot root,NavigationSessionState state,NavigationRequest[] requests)
        {
            if(root==null)return;
            if(committedHostRoot!=null)throw new ArgumentException("Host destination already owns a BODY.");
            if(liveRegistrations==null||liveRegistrations.Count!=0)throw new ArgumentException("Cold registration destination is not its empty common factory.");
            foreach(int index in state.RegisteredRequestIndices){
                var request=requests[index];var row=root.Rows.Select(e=>(NavigationHostRow)e.Value).Single(r=>ReferenceEquals(r.Request,request));liveRegistrations.AppendColdOwned(row);transportOwner.Transfer(request);
            }
            committedHostRoot=root;
        }
    }
    // Private capture bytes have a real physical owner, independent of the
    // serialization scope. Only TransferPublic ends accounting at a caller boundary.
    internal sealed class NavigationCaptureBytes : NavOwnedStorage, IDisposable
    {
        private static readonly long LayoutBase=NavStorageLayout.ObjectBytesBound(typeof(NavigationCaptureBytes));
        private static long Layout(int length)=>checked(LayoutBase+32L+((length+7L)/8)*8);
        internal byte[] Bytes {get;private set;}
        private bool released;
        private NavigationCaptureBytes(int length):base(Layout(length)){Retain();}
        internal static NavigationCaptureBytes Allocate(int length,INavStorageReservation reservation,Func<byte[]> create)
        {
            if(length<0||length>WorldWire.MaxBytes||create==null)throw new ArgumentException("Invalid capture output length.");
            using(var allocation=new NavStorageAllocation(reservation.TryReserve)){
                allocation.Reserve(Layout(length));
                var owner=allocation.Register(new NavigationCaptureBytes(length));
                try{owner.Bytes=create();if(owner.Bytes==null||owner.Bytes.Length!=length)throw new ArgumentException("Capture output size changed.");return owner;}
                catch{owner.Dispose();throw;}
            }
        }
        internal static NavigationCaptureBytes Write(INavStorageReservation reservation,Action<BinaryWriter> write)
        {
            using(NavStorageScratch.Reserve(reservation,512))
            using(var stream=new NavigationCaptureStream(reservation)){
                using(var writer=new BinaryWriter(stream,System.Text.Encoding.UTF8,true))write(writer);
                return Allocate(checked((int)stream.Length),reservation,stream.ToArray);
            }
        }
        internal static NavigationCaptureBytes CreateOwned(int length,NavigationRetentionBudget budget,Func<INavStorageReservation,byte[]> create)
        {
            if(budget==null||!budget.TryOpenScope(NavigationPreparationKind.Capture,0,0,out var scope))throw new InvalidOperationException("Capture output admission refused.");
            using(scope){
                NavigationCaptureBytes output=null;
                try{output=Allocate(length,scope.OwnedReservation(),()=>create(scope.OwnedReservation()));scope.Attach(output);scope.Commit(output,_=>{});return output;}
                catch{output?.Dispose();throw;}
            }
        }
        internal byte[] TransferPublic()
        {if(released)throw new ObjectDisposedException(nameof(NavigationCaptureBytes));var bytes=Bytes;Dispose();return bytes;}
        public void Dispose(){if(released)return;released=true;Release();}
        internal override void ReleaseOwner()=>Dispose();
        protected override void ReleaseEdges(){Bytes=null;}
    }
    internal sealed class NavigationCaptureVision : NavOwnedStorage, IDisposable
    {
        private NavigationCaptureBytes[] owners;
        internal byte[][] Bytes {get;private set;}
        private bool released;
        private static readonly long LayoutBase=NavStorageLayout.ObjectBytesBound(typeof(NavigationCaptureVision));
        private static long Layout(int count)=>checked(LayoutBase+64L+16L*count);
        private NavigationCaptureVision(int count):base(Layout(count))
        {owners=new NavigationCaptureBytes[count];Bytes=new byte[count][];Retain();}
        internal static NavigationCaptureVision CreateOwned(int count,NavigationRetentionBudget budget,Action<NavigationCaptureVision> populate)
        {
            if(count<0||count>8||budget==null||!budget.TryOpenScope(NavigationPreparationKind.Capture,0,0,out var scope))throw new InvalidOperationException("Vision capture admission refused.");
            using(scope){NavigationCaptureVision output=null;
                try{using(var allocation=new NavStorageAllocation(scope.OwnedReservation().TryReserve)){
                    allocation.Reserve(Layout(count));output=allocation.Register(new NavigationCaptureVision(count));
                }
                populate(output);scope.Attach(output);scope.Commit(output,_=>{});return output;
                }catch{output?.Dispose();throw;}
            }
        }
        internal void Set(int index,NavigationCaptureBytes owner)
        {
            if(released||owner==null||owners[index]!=null)throw new ArgumentException("Invalid vision capture owner.");
            RequireEdge(owner);owners[index]=owner;Bytes[index]=owner.Bytes;
        }
        public void Dispose(){if(released)return;released=true;Release();}
        internal override void ReleaseOwner()=>Dispose();
        protected override void ReleaseEdges(){foreach(var owner in owners)owner?.Dispose();owners=null;Bytes=null;}
    }
    // Admit old/new capacity overlap before the underlying MemoryStream grows.
    internal sealed class NavigationCaptureStream : MemoryStream
    {
        internal readonly INavStorageReservation Reservation;
        private IDisposable capacity,infrastructure;
        private readonly bool fixedBacking;
        internal NavigationCaptureStream(INavStorageReservation reservation){Reservation=reservation;}
        // The caller's final NavigationCaptureBytes owner pays for the array.
        // The same 512-byte infrastructure allowance as Write covers the stream
        // and BinaryWriter; no capacity lease or ToArray copy is needed here.
        internal NavigationCaptureStream(INavStorageReservation reservation,byte[] backing,int payloadLength)
            :base(backing,0,payloadLength,true,false)
        {Reservation=reservation??throw new ArgumentNullException(nameof(reservation));fixedBacking=true;infrastructure=NavStorageScratch.Reserve(reservation,512);}
        private void Grow(int count)
        {
            long needed=Math.Max(Length,checked(Position+count));
            if(needed>WorldWire.MaxBytes)throw new ArgumentException("Capture wire cap exceeded.");
            if(fixedBacking){if(needed>Capacity)throw new ArgumentException("Fixed capture payload exceeded.");return;}
            if(needed<=Capacity)return;
            int next=(int)Math.Max(needed,Math.Min(WorldWire.MaxBytes,Math.Max(256L,2L*Capacity)));
            var admitted=NavStorageScratch.Reserve(Reservation,checked(64L+next));
            try{Capacity=next;}catch{admitted.Dispose();throw;}
            capacity?.Dispose();capacity=admitted;
        }
        public override void Write(byte[] buffer,int offset,int count){Grow(count);base.Write(buffer,offset,count);}
        public override void WriteByte(byte value){Grow(1);base.WriteByte(value);}
        protected override void Dispose(bool disposing){base.Dispose(disposing);capacity?.Dispose();capacity=null;infrastructure?.Dispose();infrastructure=null;}
    }

}
