using System;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    // Session lifetime, distinct from the worker's resumable frontier. This
    // initial core owns the current geometry/profile; request/group/crowd and
    // authority-container owners are added through the same root contract.
    internal sealed partial class NavigationAuthorityOwners : NavOwnedStorage, INavPhysicalBody
    {
        private static readonly long LayoutBytes=Math.Max(256,NavStorageLayout.ObjectBytesBound(typeof(NavigationAuthorityOwners)));
        internal NavGeometry.OwnedBuffers Geometry {get;private set;}
        internal NavGeometry.OwnedBuffers NavigationGeometry {get;private set;}
        internal NavDeclarationStorage Profile {get;private set;}
        internal NavRetainedArray<NavOwnedStorage> RequestCatalog {get;private set;}
        internal NavRetainedArray<NavOwnedStorage> GeometryCatalog {get;private set;}
        internal NavRetainedArray<NavOwnedStorage> GroupCatalog {get;private set;}
        internal NavRetainedArray<int> RequestRoles {get;private set;}
        internal NavRetainedArray<NavOwnedStorage> CollectionCatalog {get;private set;}
        internal NavRetainedArray<NavOwnedStorage> IdentityCatalog {get;private set;}
        internal NavRetainedArray<NavNameReference> CrowdNames {get;private set;}
        internal NavRetainedArray<NavNameReference> TerrainNames {get;private set;}
        internal NavRetainedArray<NavOwnedStorage> ActorGlobalCatalog,ActorLocalCatalog,InstalledCatalog;
        internal NavigationArrivalCatalog ArrivalCatalog{get;private set;}
        internal NavigationArrivalLeaseCatalog LeaseCatalog{get;private set;}
        internal NavRetainedArray<int> CrowdKeys {get;private set;}
        internal NavRetainedArray<NavPoint> CrowdPoints {get;private set;}
        internal NavRetainedArray<int> CrowdFlags {get;private set;}
        internal bool HasCatalog=>RequestCatalog!=null;
        // Tag84 extends only the explicit capacity vector (10 -> 12). No
        // instance fields or old owner layout/payload are changed.
        internal bool HasLedgerLayout=>CollectionCatalog!=null&&CollectionCatalog.Count>=15&&CollectionCatalog[CollectionCatalog.Count-2] is NavRetainedArray<int>&&CollectionCatalog[CollectionCatalog.Count-1] is NavRetainedArray<int>;
        private int CoreCollectionCount=>(CollectionCatalog?.Count??0)-(HasLedgerLayout?2:0);
        internal NavRetainedArray<int> LedgerLayout=>HasLedgerLayout?(NavRetainedArray<int>)CollectionCatalog[CollectionCatalog.Count-2]:null;
        internal NavRetainedArray<int> LedgerRowOrder=>HasLedgerLayout?(NavRetainedArray<int>)CollectionCatalog[CollectionCatalog.Count-1]:null;
        internal bool HasProbeStorage=>(CoreCollectionCount==16||CoreCollectionCount==18)&&CollectionCatalog[12] is NavRetainedArray<int> caps&&caps.Count==12;
        internal int ProbeCapacity=>HasProbeStorage?((NavRetainedArray<int>)CollectionCatalog[12])[10]:-1;
        internal int ProbeAnswerCapacity=>HasProbeStorage?((NavRetainedArray<int>)CollectionCatalog[12])[11]:-1;
        internal NavRetainedArray<NavOwnedStorage> ProbeAnswerCatalog=>HasProbeStorage?CollectionCatalog[13] as NavRetainedArray<NavOwnedStorage>:null;
        internal NavRetainedArray<NavOwnedStorage> MailboxAnswerCatalog=>HasProbeStorage?CollectionCatalog[14] as NavRetainedArray<NavOwnedStorage>:null;
        internal NavRetainedArray<NavOwnedStorage> BarrierAnswerCatalog=>HasProbeStorage?CollectionCatalog[15] as NavRetainedArray<NavOwnedStorage>:null;
        internal bool HasAnswerTracking=>CoreCollectionCount==15||CoreCollectionCount==18;
        internal int BaseCollectionCount=>HasProbeStorage?16:13;
        internal int ExpectedCollectionCount=>BaseCollectionCount+(HasAnswerTracking?2:0)+(HasLedgerLayout?2:0);
        internal NavRetainedArray<int> AnswerTrackingLayout=>HasAnswerTracking?CollectionCatalog[BaseCollectionCount] as NavRetainedArray<int>:null;
        internal NavRetainedArray<NavOwnedStorage> AnswerTrackingOwners=>HasAnswerTracking?CollectionCatalog[BaseCollectionCount+1] as NavRetainedArray<NavOwnedStorage>:null;
        private NavPhysicalStorageKind BaseCatalogKind=>HasProbeStorage?NavPhysicalStorageKind.AuthorityCatalogProbes:LeaseCatalog!=null?NavPhysicalStorageKind.AuthorityCatalogLeases:ArrivalCatalog!=null?NavPhysicalStorageKind.AuthorityCatalogArrival:ActorGlobalCatalog!=null?NavPhysicalStorageKind.AuthorityCatalogRoutes:TerrainNames!=null?NavPhysicalStorageKind.AuthorityCatalogTerrain:NavPhysicalStorageKind.AuthorityCatalog;
        private bool released;
        internal override bool IsImmutableCaptureRoot=>Geometry!=null&&NavigationGeometry!=null&&Profile!=null;
        public NavPhysicalTag PhysicalTag=>new NavPhysicalTag(HasCatalog?(HasLedgerLayout?NavPhysicalStorageKind.AuthorityCatalogLedgerLayout:HasAnswerTracking?NavPhysicalStorageKind.AuthorityCatalogAnswers:BaseCatalogKind):NavPhysicalStorageKind.HostAuthority);
        private NavigationAuthorityOwners(bool owner):base(LayoutBytes){if(owner)Retain();}
        private static NavigationAuthorityOwners Allocate(INavStorageReservation reservation,bool owner)
        {using(var allocation=new NavStorageAllocation(reservation.TryReserve)){allocation.Reserve(LayoutBytes);return allocation.Register(new NavigationAuthorityOwners(owner));}}
        private void Own(NavGeometry.OwnedBuffers geometry,NavGeometry.OwnedBuffers navigation,NavDeclarationStorage profile)
        {Geometry=NavPhysicalMacro.Acquire(this,geometry);NavigationGeometry=NavPhysicalMacro.Acquire(this,navigation);Profile=NavPhysicalMacro.Acquire(this,profile);}
        internal static NavigationAuthorityOwners CreateOwned(NavGeometry geometry,NavGeometry navigation,NavigationProfile profile,INavStorageReservation reservation)
        {
            if(geometry==null||navigation==null||profile==null||reservation==null)throw new ArgumentNullException();
            NavGeometry g=null,n=null;NavigationProfile p=null;bool copiedG=false,copiedN=false,copiedP=false;
            var root=Allocate(reservation,true);
            try{
                g=geometry.StorageRoot==null?NavGeometry.CopyOwned(geometry,reservation):geometry;copiedG=!ReferenceEquals(g,geometry);NavStorageAdmission.RequireOwned(reservation,g.StorageRoot);
                n=ReferenceEquals(geometry,navigation)?g:navigation.StorageRoot==null?NavGeometry.CopyOwned(navigation,reservation):navigation;copiedN=!ReferenceEquals(n,navigation)&&!ReferenceEquals(n,g);NavStorageAdmission.RequireOwned(reservation,n.StorageRoot);
                p=profile.StorageRoot==null?NavigationProfile.CopyOwned(profile,reservation):profile;copiedP=!ReferenceEquals(p,profile);NavStorageAdmission.RequireOwned(reservation,p.StorageRoot);
                root.Own((NavGeometry.OwnedBuffers)g.StorageRoot,(NavGeometry.OwnedBuffers)n.StorageRoot,(NavDeclarationStorage)p.StorageRoot);return root;
            }catch{root.ReleaseStorage();throw;}
            finally{if(copiedG)g?.StorageRoot.Release();if(copiedN)n?.StorageRoot.Release();if(copiedP)p?.StorageRoot.Release();}
        }
        internal static NavigationAuthorityOwners CreateCatalogOwned(NavigationAuthorityOwners core,NavOwnedStorage[] requests,NavOwnedStorage[] geometries,NavOwnedStorage[] groups,NavOwnedStorage[] identities,NavNameReference[] crowdNames,int[] crowdKeys,NavPoint[] crowdPoints,int[] crowdFlags,int[] roles,int[][] layouts,NavRetainedArray<NavNameReference> terrainNames,NavOwnedStorage[] globalRoutes,NavOwnedStorage[] localRoutes,NavOwnedStorage[] installed,INavStorageReservation reservation,NavigationArrivalCatalog arrival=null,NavigationArrivalLeaseCatalog leases=null,NavOwnedStorage[] probeAnswers=null,NavOwnedStorage[] mailboxAnswers=null,NavOwnedStorage[] barrierAnswers=null,int[] answerTrackingLayout=null,NavOwnedStorage[] answerTrackingOwners=null)
        {
            if(core==null||requests==null||geometries==null||groups==null||identities==null||crowdNames==null||crowdKeys==null||crowdPoints==null||crowdFlags==null||crowdNames.Length!=crowdKeys.Length*3||crowdPoints.Length!=crowdNames.Length||crowdFlags.Length!=crowdKeys.Length||roles==null||layouts==null||(layouts.Length!=13&&layouts.Length!=15)||requests.Length!=roles.Length||geometries.Length<1)throw new ArgumentException("Missing mandatory authority catalog.");
            if((probeAnswers!=null)!=(layouts[12]?.Length==12)||(probeAnswers!=null)!=(mailboxAnswers!=null)||(probeAnswers!=null)!=(barrierAnswers!=null))throw new ArgumentException("Producer receipt catalog/capacity mode differs.");
            if((answerTrackingLayout==null)!=(answerTrackingOwners==null))throw new ArgumentException("Incomplete answer tracking catalog.");
            if(globalRoutes==null||localRoutes==null||installed==null||globalRoutes.Length!=crowdKeys.Length||localRoutes.Length!=crowdKeys.Length)throw new ArgumentException("Missing actor route catalog roles.");
            NavStorageAdmission.RequireOwned(reservation,core);NavStorageAdmission.RequireOwned(reservation,terrainNames);
            if(arrival!=null){NavStorageAdmission.RequireOwned(reservation,arrival);if(!arrival.IsImmutableCaptureRoot)throw new ArgumentException("Unsealed arrival catalog.");}
            if(leases!=null){NavStorageAdmission.RequireOwned(reservation,leases);leases.ValidateArrivalCatalog(arrival,reservation);}
            foreach(var owner in requests)if(!(owner is NavigationHostRequest)||!owner.IsImmutableCaptureRoot)throw new ArgumentException("Invalid request catalog owner.");
            foreach(var owner in geometries)if(!(owner is NavGeometry.OwnedBuffers)||!owner.IsImmutableCaptureRoot)throw new ArgumentException("Invalid geometry catalog owner.");
            foreach(var owner in groups)if(!(owner is NavigationAuthorityGroup)||!owner.IsImmutableCaptureRoot)throw new ArgumentException("Invalid group catalog owner.");
            foreach(var owner in identities)if(owner!=null&&(!(owner is NavOwnedText)||!owner.IsImmutableCaptureRoot))throw new ArgumentException("Invalid movement identity name owner.");
            foreach(int role in roles)if(role<=0||role>511)throw new ArgumentException("Invalid request catalog role.");
            if(answerTrackingOwners!=null)foreach(var value in answerTrackingOwners){
                if(!(value is NavigationHostAnswer answer)||!answer.IsImmutableCaptureRoot||answer.Answer.Corridor==null||!ReferenceEquals(answer.Answer.StorageOwner,answer))throw new ArgumentException("Invalid tracked payload before catalog allocation.");
                NavStorageAdmission.RequireOwned(reservation,answer);
            }
            if(layouts.Length==15&&(layouts[13]==null||layouts[14]==null))throw new ArgumentException("Missing arrival ledger layout/permutation.");
            bool ledgerLayout=layouts.Length==15&&(layouts[13].Length!=1||layouts[13][0]!=layouts[14].Length||!IdentityLedgerOrder(layouts[14]));int baseLayouts=13;
            var root=Allocate(reservation,true);
            try{
                root.Own(core.Geometry,core.NavigationGeometry,core.Profile);
                root.ArrivalCatalog=NavPhysicalMacro.Acquire(root,arrival);root.LeaseCatalog=NavPhysicalMacro.Acquire(root,leases);
                root.RequestCatalog=NavRetainedArray<NavOwnedStorage>.Copy(requests,reservation.TryReserve);
                root.GeometryCatalog=NavRetainedArray<NavOwnedStorage>.Copy(geometries,reservation.TryReserve);
                root.GroupCatalog=NavRetainedArray<NavOwnedStorage>.Copy(groups,reservation.TryReserve);
                root.RequestRoles=NavRetainedArray<int>.Copy(roles,reservation.TryReserve);
                root.IdentityCatalog=NavRetainedArray<NavOwnedStorage>.Copy(identities,reservation.TryReserve);
                root.ActorGlobalCatalog=NavRetainedArray<NavOwnedStorage>.Copy(globalRoutes,reservation.TryReserve);root.ActorLocalCatalog=NavRetainedArray<NavOwnedStorage>.Copy(localRoutes,reservation.TryReserve);root.InstalledCatalog=NavRetainedArray<NavOwnedStorage>.Copy(installed,reservation.TryReserve);root.TerrainNames=NavPhysicalMacro.Acquire(root,terrainNames);root.CrowdNames=NavRetainedArray<NavNameReference>.Copy(crowdNames,reservation.TryReserve);root.CrowdKeys=NavRetainedArray<int>.Copy(crowdKeys,reservation.TryReserve);root.CrowdPoints=NavRetainedArray<NavPoint>.Copy(crowdPoints,reservation.TryReserve);root.CrowdFlags=NavRetainedArray<int>.Copy(crowdFlags,reservation.TryReserve);
                using(NavStorageScratch.Reserve(reservation,32L+8L*(baseLayouts+(probeAnswers==null?0:3)+(answerTrackingOwners==null?0:2)+(ledgerLayout?2:0)))){
                    var entries=new NavOwnedStorage[baseLayouts+(probeAnswers==null?0:3)+(answerTrackingOwners==null?0:2)+(ledgerLayout?2:0)];try{
                        for(int i=0;i<baseLayouts;i++)entries[i]=NavRetainedArray<int>.Copy(layouts[i]??throw new ArgumentException("Missing authority collection layout."),reservation.TryReserve);
                        if(probeAnswers!=null){entries[baseLayouts]=NavRetainedArray<NavOwnedStorage>.Copy(probeAnswers,reservation.TryReserve);entries[baseLayouts+1]=NavRetainedArray<NavOwnedStorage>.Copy(mailboxAnswers,reservation.TryReserve);entries[baseLayouts+2]=NavRetainedArray<NavOwnedStorage>.Copy(barrierAnswers,reservation.TryReserve);}
                        if(answerTrackingOwners!=null){entries[entries.Length-2-(ledgerLayout?2:0)]=NavRetainedArray<int>.Copy(answerTrackingLayout,reservation.TryReserve);entries[entries.Length-1-(ledgerLayout?2:0)]=NavRetainedArray<NavOwnedStorage>.Copy(answerTrackingOwners,reservation.TryReserve);}
                        if(ledgerLayout){entries[entries.Length-2]=NavRetainedArray<int>.Copy(layouts[13],reservation.TryReserve);entries[entries.Length-1]=NavRetainedArray<int>.Copy(layouts[14],reservation.TryReserve);}
                        root.CollectionCatalog=NavRetainedArray<NavOwnedStorage>.Copy(entries,reservation.TryReserve);
                    }finally{foreach(var entry in entries)entry?.Release();}
                }
                if(root.HasProbeStorage)root.ValidateProbeStorage(reservation);
                if(root.HasAnswerTracking)root.ValidateAnswerTracking(reservation);
                if(ledgerLayout)root.ValidateLedgerLayout(reservation);
                return root;
            }catch{root.ReleaseStorage();throw;}
        }
        private static bool IdentityLedgerOrder(int[] order){for(int i=0;i<order.Length;i++)if(order[i]!=i)return false;return true;}
        internal void ValidateLedgerLayout(INavStorageReservation reservation)
        {
            var layout=LedgerLayout;var order=LedgerRowOrder;var caps=CollectionCatalog[12] as NavRetainedArray<int>;
            if(!HasLedgerLayout||layout.Count<1||caps==null||caps.Count<10||caps[9]<0||caps[9]>AuthoritySlotMap<long,NavigationArrivalReservation>.MaximumSlots||layout[0]<0||layout[0]>caps[9]||layout.Count-1>layout[0]||order.Count!=layout[0]-(layout.Count-1))throw new ArgumentException("Invalid arrival ledger layout shape/capacity.");
            using(NavStorageScratch.Reserve(reservation,checked(64L+layout[0]+order.Count))){
                var holes=new bool[layout[0]];var seen=new bool[order.Count];
                for(int i=1;i<layout.Count;i++){int slot=layout[i];if(slot<0||slot>=holes.Length||holes[slot])throw new ArgumentException("Invalid arrival ledger free slot.");holes[slot]=true;}
                foreach(int row in order){if(row<0||row>=seen.Length||seen[row])throw new ArgumentException("Invalid arrival ledger row permutation.");seen[row]=true;}
            }
        }
        private void ValidateProbeStorage(INavStorageReservation r)
        {
            if(!HasProbeStorage||ArrivalCatalog==null||LeaseCatalog==null&&ArrivalCatalog.LeaseCount!=0||ProbeAnswerCatalog==null||ProbeAnswerCatalog.Count>ProbeAnswerCapacity||ProbeCapacity<0||ProbeCapacity>AuthoritySlotMap<int,NavigationRequest>.MaximumSlots||!NavigationLiveProbeAnswers.ValidCapacity(ProbeAnswerCapacity))throw new ArgumentException("Invalid owned probe capacities/mode.");
            ValidateAnswerRoles(MailboxAnswerCatalog,64);ValidateAnswerRoles(BarrierAnswerCatalog,128);
            var layout=(NavRetainedArray<int>)CollectionCatalog[6];if(layout.Count<1||layout[0]<0||layout[0]>ProbeCapacity||layout.Count-1>layout[0])throw new ArgumentException("Invalid owned probe slot layout.");
            using(NavStorageScratch.Reserve(r,checked(64L+layout[0]))){
                var seen=new bool[layout[0]];for(int i=1;i<layout.Count;i++){int slot=layout[i];if(slot<0||slot>=seen.Length||seen[slot])throw new ArgumentException("Invalid owned probe free slots.");seen[slot]=true;}
                int count=0;for(int i=0;i<RequestCatalog.Count;i++)if((RequestRoles[i]&16)!=0){var request=((NavigationHostRequest)RequestCatalog[i]).Request;if(request.Entity>=0||request.Session!=ArrivalCatalog.Data.World)throw new ArgumentException("Invalid owned producer request role.");count++;}
                if(count!=layout[0]-(layout.Count-1))throw new ArgumentException("Owned producer slots and request roles differ.");
                for(int i=0;i<ProbeAnswerCatalog.Count;i++){
                    if(!(ProbeAnswerCatalog[i] is NavigationHostAnswer answer)||answer.Answer.Request.Entity>=0||answer.Answer.Request.Session!=ArrivalCatalog.Data.World)throw new ArgumentException("Invalid owned producer receipt role.");
                    bool found=false;for(int n=0;n<RequestCatalog.Count;n++)if(ReferenceEquals(RequestCatalog[n],answer.RequestOwner)&&(RequestRoles[n]&256)!=0){found=true;break;}if(!found)throw new ArgumentException("Producer receipt request is outside exact catalog role.");
                }
            }
        }
        private void ValidateAnswerRoles(NavRetainedArray<NavOwnedStorage> entries,int role)
        {
            if(entries==null||entries.Count>NavMailbox<NavigationAnswer>.Capacity)throw new ArgumentException("Invalid answer BODY role capacity.");
            foreach(var entry in entries){if(entry==null)continue;if(!(entry is NavigationHostAnswer answer))throw new ArgumentException("Invalid answer BODY role kind.");bool found=false;for(int i=0;i<RequestCatalog.Count;i++)if(ReferenceEquals(RequestCatalog[i],answer.RequestOwner)&&(RequestRoles[i]&role)!=0){found=true;break;}if(!found)throw new ArgumentException("Answer request has no exact catalog role.");}
        }
        public void WritePhysicalBody(NavPhysicalBodyWriter writer)
        {
            if(HasLedgerLayout)writer.Output.Write((byte)(HasAnswerTracking?NavPhysicalStorageKind.AuthorityCatalogAnswers:BaseCatalogKind));
            if(HasAnswerTracking)writer.Output.Write((byte)BaseCatalogKind);
            if(!IsImmutableCaptureRoot||StorageBytes!=NavStorageLayout.Charge(LayoutBytes)||!(Profile.Facade is NavigationProfile))throw new ArgumentException("Invalid authority core ownership.");
            NavPhysicalMacro.WriteEdge(writer,this,Geometry);NavPhysicalMacro.WriteEdge(writer,this,NavigationGeometry);NavPhysicalMacro.WriteEdge(writer,this,Profile);
            if(HasCatalog){NavPhysicalMacro.WriteEdge(writer,this,RequestCatalog);NavPhysicalMacro.WriteEdge(writer,this,GeometryCatalog);NavPhysicalMacro.WriteEdge(writer,this,GroupCatalog);NavPhysicalMacro.WriteEdge(writer,this,RequestRoles);NavPhysicalMacro.WriteEdge(writer,this,CollectionCatalog);NavPhysicalMacro.WriteEdge(writer,this,IdentityCatalog);NavPhysicalMacro.WriteEdge(writer,this,CrowdNames);NavPhysicalMacro.WriteEdge(writer,this,CrowdKeys);NavPhysicalMacro.WriteEdge(writer,this,CrowdPoints);NavPhysicalMacro.WriteEdge(writer,this,CrowdFlags);if(TerrainNames!=null)NavPhysicalMacro.WriteEdge(writer,this,TerrainNames);if(ActorGlobalCatalog!=null){NavPhysicalMacro.WriteEdge(writer,this,ActorGlobalCatalog);NavPhysicalMacro.WriteEdge(writer,this,ActorLocalCatalog);NavPhysicalMacro.WriteEdge(writer,this,InstalledCatalog);}if(ArrivalCatalog!=null)NavPhysicalMacro.WriteEdge(writer,this,ArrivalCatalog);if(HasProbeStorage)writer.Output.Write(LeaseCatalog!=null);if(LeaseCatalog!=null)NavPhysicalMacro.WriteEdge(writer,this,LeaseCatalog);}
        }
        internal static NavigationAuthorityOwners ReadPhysicalBody(NavPhysicalBodyReader reader,bool catalog=false,bool terrainCatalog=false,bool routeCatalog=false,bool arrivalCatalog=false,bool leaseCatalog=false,bool probeCatalog=false,bool answerTracking=false,bool ledgerLayout=false)
        {
            if(ledgerLayout){var mode=(NavPhysicalStorageKind)reader.Input.ReadByte();if(mode!=NavPhysicalStorageKind.AuthorityCatalogAnswers&&mode!=NavPhysicalStorageKind.AuthorityCatalogArrival&&mode!=NavPhysicalStorageKind.AuthorityCatalogLeases&&mode!=NavPhysicalStorageKind.AuthorityCatalogProbes)throw new ArgumentException("Invalid ledger layout base catalog mode.");catalog=terrainCatalog=routeCatalog=arrivalCatalog=true;answerTracking=mode==NavPhysicalStorageKind.AuthorityCatalogAnswers;leaseCatalog=mode==NavPhysicalStorageKind.AuthorityCatalogLeases;probeCatalog=mode==NavPhysicalStorageKind.AuthorityCatalogProbes;}
            if(answerTracking){var mode=(NavPhysicalStorageKind)reader.Input.ReadByte();if(mode!=NavPhysicalStorageKind.AuthorityCatalogArrival&&mode!=NavPhysicalStorageKind.AuthorityCatalogLeases&&mode!=NavPhysicalStorageKind.AuthorityCatalogProbes)throw new ArgumentException("Invalid tracked answer base catalog mode.");catalog=terrainCatalog=routeCatalog=arrivalCatalog=true;leaseCatalog=mode==NavPhysicalStorageKind.AuthorityCatalogLeases;probeCatalog=mode==NavPhysicalStorageKind.AuthorityCatalogProbes;}
            var geometry=NavPhysicalMacro.ReadEdge<NavGeometry.OwnedBuffers>(reader);var navigation=NavPhysicalMacro.ReadEdge<NavGeometry.OwnedBuffers>(reader);var profile=NavPhysicalMacro.ReadEdge<NavDeclarationStorage>(reader);
            if(!(profile.Facade is NavigationProfile))throw new ArgumentException("Authority profile owner type mismatch.");
            var root=Allocate(reader.Reservation,false);try{root.Own(geometry,navigation,profile);
                if(catalog){root.RequestCatalog=NavPhysicalMacro.Acquire(root,NavPhysicalMacro.ReadEdge<NavRetainedArray<NavOwnedStorage>>(reader));root.GeometryCatalog=NavPhysicalMacro.Acquire(root,NavPhysicalMacro.ReadEdge<NavRetainedArray<NavOwnedStorage>>(reader));root.GroupCatalog=NavPhysicalMacro.Acquire(root,NavPhysicalMacro.ReadEdge<NavRetainedArray<NavOwnedStorage>>(reader));root.RequestRoles=NavPhysicalMacro.Acquire(root,NavPhysicalMacro.ReadEdge<NavRetainedArray<int>>(reader));root.CollectionCatalog=NavPhysicalMacro.Acquire(root,NavPhysicalMacro.ReadEdge<NavRetainedArray<NavOwnedStorage>>(reader));root.IdentityCatalog=NavPhysicalMacro.Acquire(root,NavPhysicalMacro.ReadEdge<NavRetainedArray<NavOwnedStorage>>(reader));root.CrowdNames=NavPhysicalMacro.Acquire(root,NavPhysicalMacro.ReadEdge<NavRetainedArray<NavNameReference>>(reader));root.CrowdKeys=NavPhysicalMacro.Acquire(root,NavPhysicalMacro.ReadEdge<NavRetainedArray<int>>(reader));root.CrowdPoints=NavPhysicalMacro.Acquire(root,NavPhysicalMacro.ReadEdge<NavRetainedArray<NavPoint>>(reader));root.CrowdFlags=NavPhysicalMacro.Acquire(root,NavPhysicalMacro.ReadEdge<NavRetainedArray<int>>(reader));if(terrainCatalog){root.TerrainNames=NavPhysicalMacro.Acquire(root,NavPhysicalMacro.ReadEdge<NavRetainedArray<NavNameReference>>(reader));if(root.CollectionCatalog.Count!=(probeCatalog?16:13)+(answerTracking?2:0)+(ledgerLayout?2:0))throw new ArgumentException("Invalid terrain catalog collection roles.");}
                    if(routeCatalog){root.ActorGlobalCatalog=NavPhysicalMacro.Acquire(root,NavPhysicalMacro.ReadEdge<NavRetainedArray<NavOwnedStorage>>(reader));root.ActorLocalCatalog=NavPhysicalMacro.Acquire(root,NavPhysicalMacro.ReadEdge<NavRetainedArray<NavOwnedStorage>>(reader));root.InstalledCatalog=NavPhysicalMacro.Acquire(root,NavPhysicalMacro.ReadEdge<NavRetainedArray<NavOwnedStorage>>(reader));
                        if(root.ActorGlobalCatalog.Count!=root.CrowdKeys.Count||root.ActorLocalCatalog.Count!=root.CrowdKeys.Count)throw new ArgumentException("Actor route BODY role count mismatch.");
                        foreach(var entry in root.ActorGlobalCatalog)if(entry!=null&&!(entry is NavRetainedArray<NavPoint>))throw new ArgumentException("Invalid global-route role.");foreach(var entry in root.ActorLocalCatalog)if(entry!=null&&!(entry is NavRetainedArray<NavPoint>))throw new ArgumentException("Invalid local-route role.");foreach(var entry in root.InstalledCatalog)if(!(entry is NavigationInstalledExecutionOwner))throw new ArgumentException("Invalid installed role.");
                    }
                    if(arrivalCatalog)root.ArrivalCatalog=NavPhysicalMacro.Acquire(root,NavPhysicalMacro.ReadEdge<NavigationArrivalCatalog>(reader));
                    if(leaseCatalog||probeCatalog&&reader.Input.ReadBoolean()){root.LeaseCatalog=NavPhysicalMacro.Acquire(root,NavPhysicalMacro.ReadEdge<NavigationArrivalLeaseCatalog>(reader));root.LeaseCatalog.ValidateArrivalCatalog(root.ArrivalCatalog,reader.Reservation);}
                    if(root.HasLedgerLayout!=ledgerLayout)throw new ArgumentException("Ledger layout catalog tag mode differs.");
                    if(ledgerLayout)root.ValidateLedgerLayout(reader.Reservation);
                    if(root.HasAnswerTracking!=answerTracking)throw new ArgumentException("Answer tracking catalog tag mode differs.");
                    if(answerTracking)root.ValidateAnswerTracking(reader.Reservation);
                    if(root.HasProbeStorage!=probeCatalog)throw new ArgumentException("Probe capacity/catalog tag mode differs.");
                    if(probeCatalog)root.ValidateProbeStorage(reader.Reservation);
                    if(root.CrowdNames.Count!=root.CrowdKeys.Count*3||root.CrowdPoints.Count!=root.CrowdNames.Count||root.CrowdFlags.Count!=root.CrowdKeys.Count)throw new ArgumentException("Invalid crowd name/point catalog shape.");
                    if(root.RequestCatalog.Count!=root.RequestRoles.Count||root.GeometryCatalog.Count<1)throw new ArgumentException("Invalid authority catalog shape.");
                    foreach(var entry in root.RequestCatalog)if(!(entry is NavigationHostRequest))throw new ArgumentException("Invalid catalog request kind.");
                    foreach(var entry in root.GeometryCatalog)if(!(entry is NavGeometry.OwnedBuffers))throw new ArgumentException("Invalid catalog geometry kind.");
                    foreach(var entry in root.GroupCatalog)if(!(entry is NavigationAuthorityGroup))throw new ArgumentException("Invalid catalog group kind.");
                    foreach(int role in root.RequestRoles)if(role<=0||role>511)throw new ArgumentException("Invalid catalog request role.");
                    foreach(var entry in root.IdentityCatalog)if(entry!=null&&!(entry is NavOwnedText))throw new ArgumentException("Invalid movement identity catalog kind.");
                    for(int i=0;i<root.CollectionCatalog.Count;i++)if((i>=13&&i<(probeCatalog?16:13)||answerTracking&&i==root.CoreCollectionCount-1)?!(root.CollectionCatalog[i] is NavRetainedArray<NavOwnedStorage>):!(root.CollectionCatalog[i] is NavRetainedArray<int>))throw new ArgumentException("Invalid catalog layout/producer receipt kind.");
                }return root;
            }catch{
                // Allocate(false) leaves only the decode scope's birth. It must
                // release this partial owner, including its acquired edges, once.
                throw;
            }
        }
        internal void ReleaseStorage(){if(released)return;released=true;Release();}
        internal override void ReleaseOwner()=>ReleaseStorage();
        protected override void ReleaseEdges(){LeaseCatalog?.Release();LeaseCatalog=null;ArrivalCatalog?.Release();ArrivalCatalog=null;Geometry?.Release();NavigationGeometry?.Release();Profile?.Release();Geometry=NavigationGeometry=null;Profile=null;ActorGlobalCatalog?.Release();ActorLocalCatalog?.Release();InstalledCatalog?.Release();ActorGlobalCatalog=ActorLocalCatalog=InstalledCatalog=null;RequestCatalog?.Release();GeometryCatalog?.Release();GroupCatalog?.Release();RequestRoles?.Release();CollectionCatalog?.Release();IdentityCatalog?.Release();IdentityCatalog=null;TerrainNames?.Release();TerrainNames=null;CrowdNames?.Release();CrowdKeys?.Release();CrowdPoints?.Release();CrowdFlags?.Release();CrowdNames=null;CrowdKeys=CrowdFlags=null;CrowdPoints=null;RequestCatalog=GeometryCatalog=GroupCatalog=CollectionCatalog=null;RequestRoles=null;}
    }
    internal readonly struct NavigationLiveWriteToken
    {
        internal readonly NavigationSession Session;internal readonly int Thread;internal readonly long Operation;
        internal NavigationLiveWriteToken(NavigationSession session,long operation){Session=session;Thread=System.Threading.Thread.CurrentThread.ManagedThreadId;Operation=operation;}
    }
    internal readonly struct NavigationLiveGroupObservation
    {
        internal readonly NavigationLiveGroup Owner;internal readonly NavigationAuthorityGroup Version;internal readonly NavigationSession Session;internal readonly long Generation;
        internal NavigationLiveGroupObservation(NavigationLiveGroup owner,NavigationAuthorityGroup version,NavigationSession session,long generation){Owner=owner;Version=version;Session=session;Generation=generation;}
        // Exactly one caller releases the two explicit observation pins.
        internal void ReleasePins(){Owner?.Release();Version?.Release();}
    }
    internal enum NavigationLiveStoragePhase { Preparing,SealedForInstall,Live,Retired,Aborted,Released }
    internal sealed class NavigationLiveGroup : NavOwnedStorage
    {
        private static readonly long HeaderBytes=NavStorageLayout.ObjectBytesBound(typeof(NavigationLiveGroup));
        private static long Layout(int count)=>checked(HeaderBytes+NavStorageLayout.ObjectBytesBound(typeof(GroupOrderState))+32L+8L*count+NavStorageLayout.ObjectBytesBound(typeof(GroupMemberState))*count);
        private NavigationRetentionBudget.Scope builderScope;private readonly int builderThread;private readonly object ledger;private readonly long installToken;
        private GroupOrderState record;private NavigationAuthorityGroup version;private NavigationSession authority;private long generation=1;private bool released;
        internal NavigationLiveStoragePhase Phase {get;private set;}
        internal override bool IsImmutableCaptureRoot=>false;
        private NavigationLiveGroup(NavigationRetentionBudget.Scope scope,int count):base(Layout(count)){builderScope=scope;builderThread=System.Threading.Thread.CurrentThread.ManagedThreadId;ledger=scope.Identity;installToken=scope.Token;Phase=NavigationLiveStoragePhase.Preparing;Retain();}
        internal static NavigationLiveGroup CreateOwned(NavigationAuthorityGroup source,NavigationRetentionBudget.Scope scope)
        {
            if(source==null||scope==null||!source.IsImmutableCaptureRoot)throw new ArgumentException("Missing immutable live-group input.");scope.AssertOwnerLive();var reservation=scope.OwnedReservation();NavStorageAdmission.RequireOwned(reservation,source);
            using(var allocation=new NavStorageAllocation(reservation.TryReserve)){
                allocation.Reserve(Layout(source.Members.Count));var owner=allocation.Register(new NavigationLiveGroup(scope,source.Members.Count));
                try{owner.version=NavPhysicalMacro.Acquire(owner,source);var v=source.Data;
                    owner.record=new GroupOrderState{WorldGeneration=v.WorldGeneration,GroupId=v.GroupId,OrderRevision=v.OrderRevision,CommandSequence=v.CommandSequence,IssuedTick=v.IssuedTick,MembershipRevision=v.MembershipRevision,JobId=v.JobId,ActionId=v.ActionId,MarchVersion=v.MarchVersion,MarchProfileRevision=v.MarchProfileRevision,TeamId=v.TeamId,TargetId=v.TargetId,MarchStretch=v.MarchStretch,MarchAnchor=v.MarchAnchor,MarchInitialized=v.MarchInitialized,Origin=v.Origin,Kind=v.Kind,OriginalGoal=v.OriginalGoal,OriginalLocation=source.OriginalLocation,OwnerId=source.OwnerId,Source=source.Source,Members=new GroupMemberState[source.Members.Count]};
                    for(int i=0;i<owner.record.Members.Length;i++){var member=(NavigationAuthorityMember)source.Members[i];var m=member.Data;owner.record.Members[i]=new GroupMemberState{Entity=m.Entity,Slot=m.Slot,Incarnation=m.Incarnation,OrderRevision=m.OrderRevision,MobilityRevision=m.MobilityRevision,Phase=m.Phase,Goal=m.Goal,GoalLocation=member.GoalLocation,HasGoal=m.HasGoal,FormationReleased=m.FormationReleased,Branch=m.Branch,Reservation=m.Reservation,ContactTick=m.ContactTick,ProgressRequest=m.ProgressRequest,Progress=m.Progress,ContactPhase=member.ContactPhase};}
                    return owner;
                }catch{owner.ReleaseStorage();throw;}
            }
        }
        internal void SealForInstall(NavigationRetentionBudget.Scope scope)
        {if(Phase!=NavigationLiveStoragePhase.Preparing||!ReferenceEquals(builderScope,scope)||builderThread!=System.Threading.Thread.CurrentThread.ManagedThreadId)throw new InvalidOperationException("Invalid private live-group builder.");scope.AssertOwnerLive();builderScope=null;Phase=NavigationLiveStoragePhase.SealedForInstall;}
        internal void ValidateInstall(NavigationRetentionBudget.Scope scope)
        {scope.AssertOwnerLive();if(Phase!=NavigationLiveStoragePhase.SealedForInstall||builderThread!=System.Threading.Thread.CurrentThread.ManagedThreadId||!ReferenceEquals(ledger,scope.Identity)||scope.Token!=installToken||scope.IsCancelled)throw new InvalidOperationException("Live-group installation capability mismatch.");}
        internal GroupOrderState PreparedRecord(NavigationRetentionBudget.Scope scope){ValidateInstall(scope);return record;}
        internal bool BelongsToSession(NavigationSession session)=>Phase==NavigationLiveStoragePhase.Live&&ReferenceEquals(authority,session);
        internal void ActivateNoThrow(NavigationSession authorityOwner){authority=authorityOwner;Phase=NavigationLiveStoragePhase.Live;}
        internal void ValidateLiveTransaction(NavigationLiveWriteToken token)
        {if(Phase!=NavigationLiveStoragePhase.Live||authority==null||!authority.MatchesLiveWriteToken(token))throw new InvalidOperationException("Live-group write requires its authority transaction.");}
        internal GroupOrderState ObserveDetached(){if(Phase!=NavigationLiveStoragePhase.Live&&Phase!=NavigationLiveStoragePhase.SealedForInstall)throw new InvalidOperationException("Live-group is not observable.");return record.Copy();}
        internal NavigationAuthorityGroup ImmutableVersion=>version;
        internal NavigationLiveGroupObservation ObserveForCopy(NavigationLiveWriteToken token)
        {ValidateLiveTransaction(token);Retain();version.Retain();return new NavigationLiveGroupObservation(this,version,authority,generation);}
        internal bool MatchesObservation(NavigationLiveGroupObservation observation)
        =>Phase==NavigationLiveStoragePhase.Live&&ReferenceEquals(observation.Owner,this)&&ReferenceEquals(observation.Version,version)&&ReferenceEquals(observation.Session,authority)&&observation.Generation==generation;
        internal void ValidateWrite(NavigationLiveGroupObservation observation,NavigationLiveWriteToken token,int memberIndex)
        {ValidateLiveTransaction(token);if(!MatchesObservation(observation)||generation==long.MaxValue||memberIndex<0||memberIndex>=record.Members.Length)throw new InvalidOperationException("Live group write lost its observed identity/slot.");}
        internal void ApplyMemberNoThrow(NavigationAuthorityGroup next,int index,NavigationAuthorityMember.Values data,string contact,NavLocation? location)
        {
            var member=record.Members[index];member.Entity=data.Entity;member.Slot=data.Slot;member.Incarnation=data.Incarnation;member.OrderRevision=data.OrderRevision;member.MobilityRevision=data.MobilityRevision;member.Phase=data.Phase;member.Goal=data.Goal;member.GoalLocation=location;member.HasGoal=data.HasGoal;member.FormationReleased=data.FormationReleased;member.Branch=data.Branch;member.Reservation=data.Reservation;member.ContactTick=data.ContactTick;member.ProgressRequest=data.ProgressRequest;member.Progress=data.Progress;member.ContactPhase=contact;var group=next.Data;record.MarchVersion=group.MarchVersion;record.MarchProfileRevision=group.MarchProfileRevision;record.MarchStretch=group.MarchStretch;record.MarchAnchor=group.MarchAnchor;record.MarchInitialized=group.MarchInitialized;version=next;generation++;
        }
        internal void ValidateMarchWrite(NavigationLiveGroupObservation observation,NavigationLiveWriteToken token)
        {ValidateLiveTransaction(token);if(!MatchesObservation(observation)||generation==long.MaxValue)throw new InvalidOperationException("March write lost observed group.");}
        internal void ApplyMovementMembersNoThrow(NavigationAuthorityGroup next)
        {
            for(int i=0;i<record.Members.Length;i++){
                var value=((NavigationAuthorityMember)next.Members[i]).Data;var member=record.Members[i];
                member.MobilityRevision=value.MobilityRevision;member.Phase=value.Phase;member.Reservation=value.Reservation;member.Progress=value.Progress;member.ProgressRequest=value.ProgressRequest;
            }
            var data=next.Data;record.MarchVersion=data.MarchVersion;record.MarchProfileRevision=data.MarchProfileRevision;record.MarchStretch=data.MarchStretch;record.MarchAnchor=data.MarchAnchor;record.MarchInitialized=data.MarchInitialized;version=next;generation++;
        }
        internal void ApplyMarchNoThrow(NavigationAuthorityGroup next)
        {
            for(int i=0;i<record.Members.Length;i++){var value=((NavigationAuthorityMember)next.Members[i]).Data;record.Members[i].Progress=value.Progress;record.Members[i].ProgressRequest=value.ProgressRequest;}
            var data=next.Data;record.MarchVersion=data.MarchVersion;record.MarchProfileRevision=data.MarchProfileRevision;record.MarchStretch=data.MarchStretch;record.MarchAnchor=data.MarchAnchor;record.MarchInitialized=data.MarchInitialized;version=next;generation++;
        }
        internal void AbortPrivate(){if(Phase!=NavigationLiveStoragePhase.Preparing&&Phase!=NavigationLiveStoragePhase.SealedForInstall)throw new InvalidOperationException("Cannot abort authoritative live storage.");builderScope=null;Phase=NavigationLiveStoragePhase.Aborted;}
        internal void RetireNoThrow(){authority=null;Phase=NavigationLiveStoragePhase.Retired;}
        internal void ReleaseStorage(){if(released)return;released=true;Release();}
        internal override void ReleaseOwner()=>ReleaseStorage();
        protected override void ReleaseEdges(){version?.Release();version=null;record=null;builderScope=null;authority=null;Phase=NavigationLiveStoragePhase.Released;}
    }
    internal sealed class NavigationPreparedGroupWrite : NavOwnedStorage
    {
        private static readonly long LayoutBytes=NavStorageLayout.ObjectBytesBound(typeof(NavigationPreparedGroupWrite));
        private readonly NavigationLiveGroupObservation observation;private readonly int index;private readonly long installToken;private readonly object ledger;
        private readonly NavigationAuthorityMember.Values values;private readonly string contact;private readonly NavLocation? location;
        private NavigationLiveGroup owner;private NavigationAuthorityGroup expected,next;private bool destinationOwned,retiredOwned,published,released;
        internal NavigationAdmissionStorage Admission;
        internal override bool IsImmutableCaptureRoot=>false;
        private NavigationPreparedGroupWrite(NavigationLiveGroupObservation observation,NavigationAuthorityGroup next,int index,NavigationRetentionBudget.Scope scope):base(LayoutBytes)
        {this.observation=observation;this.index=index;installToken=scope.Token;ledger=scope.Identity;var member=(NavigationAuthorityMember)next.Members[index];values=member.Data;contact=member.ContactPhase;location=member.GoalLocation;Retain();}
        internal static NavigationPreparedGroupWrite CreateOwned(NavigationLiveGroupObservation observation,NavigationAuthorityGroup next,int index,NavigationRetentionBudget.Scope scope)
        {
            if(scope==null||observation.Owner==null||observation.Version==null||next==null||!next.IsImmutableCaptureRoot||!observation.Owner.MatchesObservation(observation)||index<0||index>=next.Members.Count||next.Members.Count!=observation.Version.Members.Count)throw new ArgumentException("Invalid prepared member write observation.");scope.AssertOwnerLive();var reservation=scope.OwnedReservation();NavStorageAdmission.RequireOwned(reservation,observation.Owner);NavStorageAdmission.RequireOwned(reservation,observation.Version);NavStorageAdmission.RequireOwned(reservation,next);
            var old=((NavigationAuthorityMember)observation.Version.Members[index]).Data;var updated=((NavigationAuthorityMember)next.Members[index]).Data;
            if(next.MemberParentIdentity!=observation.Version.VersionIdentity||next.ChangedMemberIndex!=index||old.Entity!=updated.Entity||old.Slot!=updated.Slot||!SameIssuance(next.Data,observation.Version.Data)||!ReferenceEquals(next.OwnerIdOwner,observation.Version.OwnerIdOwner)||!ReferenceEquals(next.SourceOwner,observation.Version.SourceOwner)||!ReferenceEquals(next.OriginalSelector.Owner,observation.Version.OriginalSelector.Owner)||next.OriginalSelector.Role!=observation.Version.OriginalSelector.Role||next.OriginalSelector.Index!=observation.Version.OriginalSelector.Index)throw new ArgumentException("Member write changed group membership/scalars.");
            using(var allocation=new NavStorageAllocation(reservation.TryReserve)){allocation.Reserve(LayoutBytes);var result=allocation.Register(new NavigationPreparedGroupWrite(observation,next,index,scope));try{result.owner=NavPhysicalMacro.Acquire(result,observation.Owner);result.expected=NavPhysicalMacro.Acquire(result,observation.Version);result.next=NavPhysicalMacro.Acquire(result,next);next.Retain();result.destinationOwned=true;return result;}catch{result.ReleaseStorage();throw;}}
        }
        private static bool SameIssuance(NavigationAuthorityGroup.Values next,NavigationAuthorityGroup.Values previous)=>next.SameIssuance(previous);
        internal void ValidatePrepared(NavigationRetentionBudget.Scope scope)
        {scope.AssertOwnerLive();if(published||scope.IsCancelled||scope.Token!=installToken||!ReferenceEquals(scope.Identity,ledger)||!owner.MatchesObservation(observation))throw new InvalidOperationException("Prepared member write lost its capability/source.");}
        internal void ValidateInstall(NavigationRetentionBudget.Scope scope)
        {ValidatePrepared(scope);ValidateInstall(scope,observation.Session.ObserveLiveWriteToken());}
        internal void ValidateInstall(NavigationRetentionBudget.Scope scope,NavigationLiveWriteToken token)
        {scope.AssertOwnerLive();if(published||scope.IsCancelled||scope.Token!=installToken||!ReferenceEquals(scope.Identity,ledger))throw new InvalidOperationException("Prepared write capability mismatch.");owner.ValidateWrite(observation,token,index);}
        internal void ValidateTargetSession(NavigationSession session){if(session==null||!ReferenceEquals(observation.Session,session))throw new InvalidOperationException("Prepared member write belongs to another target Session.");}
        internal void ApplyNoThrow(){owner.ApplyMemberNoThrow(next,index,values,contact,location);destinationOwned=false;retiredOwned=true;published=true;}
        internal void ReleaseRetired(){if(!published)throw new InvalidOperationException("Member write not published.");if(retiredOwned){retiredOwned=false;expected.Release();}}
        internal void ReleaseStorage(){if(released)return;released=true;Release();}
        internal override void ReleaseOwner()=>ReleaseStorage();
        protected override void ReleaseEdges(){Admission?.Release();Admission=null;if(destinationOwned)next?.Release();if(retiredOwned)expected?.Release();owner?.Release();expected?.Release();next?.Release();destinationOwned=retiredOwned=false;owner=null;expected=next=null;}
    }
    // Navigation-only long -> live-group backing; every array is admitted and
    // owned. Occupied-slot order and free-slot LIFO match authority iteration.
    internal sealed class NavigationLiveGroupSlots : NavOwnedStorage
    {
        private static readonly long HeaderBytes=NavStorageLayout.ObjectBytesBound(typeof(NavigationLiveGroupSlots));
        private static int BucketCount(int capacity){int count=1;while(count<capacity*2)count=checked(count*2);return count;}
        private static long ArrayBytes(int count,int width)=>checked(32L+((count*(long)width+7)/8)*8);
        private static long Layout(int capacity)=>checked(HeaderBytes+ArrayBytes(capacity,8)+ArrayBytes(capacity,8)+ArrayBytes(capacity,1)+ArrayBytes(capacity,4)*2+ArrayBytes(capacity,8)*3+ArrayBytes(BucketCount(capacity),4));
        private readonly long[] keys;private readonly NavigationLiveGroup[] values;private readonly bool[] occupied;private readonly int[] free,next,buckets;private readonly NavigationAuthorityGroup[] expectedVersions;private readonly NavigationSession[] expectedSessions;private readonly long[] expectedGenerations;
        private NavigationRetentionBudget.Scope builderScope;private readonly int builderThread;private readonly object ledger;private readonly long installToken;private int slots,freeCount,count;private bool released;
        internal NavigationLiveStoragePhase Phase {get;private set;}
        internal override bool IsImmutableCaptureRoot=>false;
        internal int Capacity=>keys.Length;internal int Slots=>slots;internal int Count=>count;internal int FreeCount=>freeCount;internal int LookupCapacity=>buckets.Length;
        private NavigationLiveGroupSlots(int capacity,NavigationRetentionBudget.Scope scope):base(Layout(capacity))
        {builderScope=scope;builderThread=System.Threading.Thread.CurrentThread.ManagedThreadId;ledger=scope.Identity;installToken=scope.Token;keys=new long[capacity];values=new NavigationLiveGroup[capacity];occupied=new bool[capacity];free=new int[capacity];next=new int[capacity];buckets=new int[BucketCount(capacity)];expectedVersions=new NavigationAuthorityGroup[capacity];expectedSessions=new NavigationSession[capacity];expectedGenerations=new long[capacity];for(int i=0;i<buckets.Length;i++)buckets[i]=-1;for(int i=0;i<next.Length;i++)next[i]=-1;Phase=NavigationLiveStoragePhase.Preparing;Retain();}
        internal static NavigationLiveGroupSlots CreateOwned(int capacity,NavigationRetentionBudget.Scope scope)
        {if(scope==null||capacity<0||capacity>AuthoritySlotMap<long,int>.MaximumSlots)throw new ArgumentException("Invalid navigation slot capacity.");scope.AssertOwnerLive();using(var allocation=new NavStorageAllocation(scope.OwnedReservation().TryReserve)){allocation.Reserve(Layout(capacity));return allocation.Register(new NavigationLiveGroupSlots(capacity,scope));}}
        private int Bucket(long key)=>unchecked((int)((uint)key^(uint)(key>>32)))&(buckets.Length-1);
        private int Find(long key){for(int i=buckets[Bucket(key)];i>=0;i=next[i])if(keys[i]==key)return i;return -1;}
        private void CheckBuilder(NavigationRetentionBudget.Scope scope)
        {if(Phase!=NavigationLiveStoragePhase.Preparing||!ReferenceEquals(scope,builderScope)||builderThread!=System.Threading.Thread.CurrentThread.ManagedThreadId)throw new InvalidOperationException("Invalid private navigation backing builder.");scope.AssertOwnerLive();}
        internal void AddPreparing(long key,NavigationLiveGroup value,NavigationRetentionBudget.Scope scope)
        {
            CheckBuilder(scope);if(key<1||value==null||Find(key)>=0||freeCount==0&&slots==Capacity)throw new ArgumentException("Invalid prepared slot insertion.");NavStorageAdmission.RequireOwned(scope.OwnedReservation(),value);value.ValidateInstall(scope);
            RequireEdge(value);value.Retain();int slot=freeCount>0?free[--freeCount]:slots++,bucket=Bucket(key);keys[slot]=key;values[slot]=value;occupied[slot]=true;next[slot]=buckets[bucket];buckets[bucket]=slot;count++;
        }
        internal void AddObservedPreparing(long key,NavigationLiveGroupObservation observation,NavigationRetentionBudget.Scope scope)
        {
            CheckBuilder(scope);if(key<1||observation.Owner==null||observation.Version==null||!observation.Owner.MatchesObservation(observation)||Find(key)>=0||freeCount==0&&slots==Capacity)throw new ArgumentException("Stale/invalid observed live slot.");NavStorageAdmission.RequireOwned(scope.OwnedReservation(),observation.Owner);NavStorageAdmission.RequireOwned(scope.OwnedReservation(),observation.Version);
            RequireEdge(observation.Owner);RequireEdge(observation.Version);observation.Owner.Retain();observation.Version.Retain();int slot=freeCount>0?free[--freeCount]:slots++,bucket=Bucket(key);keys[slot]=key;values[slot]=observation.Owner;expectedVersions[slot]=observation.Version;expectedSessions[slot]=observation.Session;expectedGenerations[slot]=observation.Generation;occupied[slot]=true;next[slot]=buckets[bucket];buckets[bucket]=slot;count++;
        }
        internal void RemovePreparing(long key,NavigationRetentionBudget.Scope scope)
        {
            CheckBuilder(scope);int slot=Find(key);if(slot<0)throw new ArgumentException("Missing prepared slot.");int bucket=Bucket(key),previous=-1;for(int i=buckets[bucket];i!=slot;i=next[i])previous=i;
            if(previous<0)buckets[bucket]=next[slot];else next[previous]=next[slot];var retired=values[slot];var retiredVersion=expectedVersions[slot];values[slot]=null;expectedVersions[slot]=null;expectedSessions[slot]=null;expectedGenerations[slot]=0;occupied[slot]=false;keys[slot]=0;next[slot]=-1;free[freeCount++]=slot;count--;retired.Release();retiredVersion?.Release();
        }
        private void CheckReadable(){if(Phase==NavigationLiveStoragePhase.Retired||Phase==NavigationLiveStoragePhase.Aborted||Phase==NavigationLiveStoragePhase.Released)throw new InvalidOperationException("Navigation backing is retired/aborted/released.");}
        internal NavigationLiveGroup GroupAtSlot(int slot){CheckReadable();return slot>=0&&slot<slots&&occupied[slot]?values[slot]:null;}
        internal long KeyAtSlot(int slot){CheckReadable();if(slot<0||slot>=slots||!occupied[slot])throw new ArgumentException("Inactive navigation slot.");return keys[slot];}
        internal int FreeAtLifoIndex(int index){CheckReadable();if(index<0||index>=freeCount)throw new ArgumentException("Invalid free slot selector.");return free[freeCount-1-index];}
        internal void SealForInstall(NavigationRetentionBudget.Scope scope){CheckBuilder(scope);builderScope=null;Phase=NavigationLiveStoragePhase.SealedForInstall;}
        internal void ValidatePrepared(NavigationRetentionBudget.Scope scope){scope.AssertOwnerLive();if(Phase!=NavigationLiveStoragePhase.SealedForInstall||builderThread!=System.Threading.Thread.CurrentThread.ManagedThreadId||!ReferenceEquals(ledger,scope.Identity)||scope.Token!=installToken||scope.IsCancelled)throw new InvalidOperationException("Navigation backing installation capability mismatch.");for(int i=0;i<slots;i++)if(occupied[i]){if(expectedVersions[i]!=null){if(!values[i].MatchesObservation(new NavigationLiveGroupObservation(values[i],expectedVersions[i],expectedSessions[i],expectedGenerations[i])))throw new InvalidOperationException("Observed live slot changed during preparation.");}else values[i].ValidateInstall(scope);}}
        internal void ValidateInstall(NavigationRetentionBudget.Scope scope,NavigationSession targetSession=null)
        {ValidatePrepared(scope);for(int i=0;i<slots;i++)if(expectedVersions[i]!=null&&(targetSession==null||!ReferenceEquals(expectedSessions[i],targetSession)))throw new InvalidOperationException("Observed slot belongs to another target Session.");}
        internal void ReleaseObservationPinsAfterInstall()
        {if(Phase!=NavigationLiveStoragePhase.Live)throw new InvalidOperationException("Observation retirement requires installed live backing.");for(int i=0;i<expectedVersions.Length;i++){var retired=expectedVersions[i];expectedVersions[i]=null;expectedSessions[i]=null;expectedGenerations[i]=0;retired?.Release();}}
        internal void ActivateNoThrow()=>Phase=NavigationLiveStoragePhase.Live;
        internal void RetireNoThrow()=>Phase=NavigationLiveStoragePhase.Retired;
        internal void AbortPrivate(){if(Phase!=NavigationLiveStoragePhase.Preparing&&Phase!=NavigationLiveStoragePhase.SealedForInstall)throw new InvalidOperationException("Cannot abort live navigation slots.");builderScope=null;Phase=NavigationLiveStoragePhase.Aborted;}
        internal void ReleaseStorage(){if(released)return;released=true;Release();}
        internal override void ReleaseOwner()=>ReleaseStorage();
        protected override void ReleaseEdges(){for(int i=0;i<values.Length;i++){values[i]?.Release();expectedVersions[i]?.Release();values[i]=null;expectedVersions[i]=null;expectedSessions[i]=null;}builderScope=null;Phase=NavigationLiveStoragePhase.Released;}
    }
    // Runtime install capability, never a BODY/capture-immutable array.
    internal sealed class NavigationPreparedSlotAggregate : NavOwnedStorage
    {
        private static readonly long LayoutBytes=NavStorageLayout.ObjectBytesBound(typeof(NavigationPreparedSlotAggregate));
        internal NavigationLiveGroup Group {get;private set;}internal NavigationLiveGroupSlots Slots {get;private set;}internal NavigationPreparedGroupWrite MemberWrite {get;private set;}private bool released;
        internal override bool IsImmutableCaptureRoot=>false;
        private NavigationPreparedSlotAggregate():base(LayoutBytes){Retain();}
        internal static NavigationPreparedSlotAggregate CreateOwned(NavigationRetentionBudget.Scope scope,NavigationLiveGroup group=null,NavigationLiveGroupSlots slots=null,NavigationPreparedGroupWrite memberWrite=null)
        {
            if(scope==null)throw new ArgumentNullException();scope.AssertOwnerLive();var reservation=scope.OwnedReservation();
            if(group!=null){NavStorageAdmission.RequireOwned(reservation,group);group.ValidateInstall(scope);}if(slots!=null){NavStorageAdmission.RequireOwned(reservation,slots);slots.ValidatePrepared(scope);}if(memberWrite!=null){NavStorageAdmission.RequireOwned(reservation,memberWrite);memberWrite.ValidatePrepared(scope);}
            using(var allocation=new NavStorageAllocation(reservation.TryReserve)){allocation.Reserve(LayoutBytes);var result=allocation.Register(new NavigationPreparedSlotAggregate());try{result.Group=NavPhysicalMacro.Acquire(result,group);result.Slots=NavPhysicalMacro.Acquire(result,slots);result.MemberWrite=NavPhysicalMacro.Acquire(result,memberWrite);return result;}catch{result.ReleaseStorage();throw;}}
        }
        internal void ValidatePrepared(NavigationRetentionBudget.Scope scope){NavStorageAdmission.RequireOwned(scope.OwnedReservation(),this);Group?.ValidateInstall(scope);Slots?.ValidatePrepared(scope);MemberWrite?.ValidatePrepared(scope);}
        internal void ValidateInstall(NavigationRetentionBudget.Scope scope,NavigationSession targetSession=null){ValidatePrepared(scope);Slots?.ValidateInstall(scope,targetSession);if(MemberWrite!=null){MemberWrite.ValidateTargetSession(targetSession);MemberWrite.ValidateInstall(scope);}}
        internal void ApplyNoThrow(NavigationSession session){Group?.ActivateNoThrow(session);Slots?.ActivateNoThrow();MemberWrite?.ApplyNoThrow();}
        internal void ReleaseRetired(){MemberWrite?.ReleaseRetired();Slots?.ReleaseObservationPinsAfterInstall();}
        internal void ReleaseStorage(){if(released)return;released=true;Release();}
        internal override void ReleaseOwner()=>ReleaseStorage();
        protected override void ReleaseEdges(){Group?.Release();Slots?.Release();MemberWrite?.Release();Group=null;Slots=null;MemberWrite=null;}
    }
    internal enum NavigationPublicationMode { AuthorityOnly, HostCreation, HostSwap }
    internal enum NavigationPublicationPhase { Preparing, Sealed, Publishing, Published, Aborted }
    // Runtime-only carrier. It is not the capture envelope and has no BODY tag.
    // Its one prepared-root identity owns both version edges and replacement
    // backing; individual factory/birth/Session/capture references stay distinct.
    internal sealed class NavigationPreparedPublication : NavOwnedStorage
    {
        private static readonly long LayoutBytes=Math.Max(512,NavStorageLayout.ObjectBytesBound(typeof(NavigationPreparedPublication)));
        private readonly NavigationRetentionBudget.Scope scope;
        private readonly int ownerThread;
        private readonly long admissionIdentity;
        internal NavigationHostRoot ExpectedHost {get;private set;}
        internal NavigationAuthorityOwners ExpectedAuthority {get;private set;}
        internal NavigationHostRoot CandidateHost {get;private set;}
        internal NavigationAuthorityOwners CandidateAuthority {get;private set;}
        internal NavigationPreparedSlotAggregate Replacements {get;private set;}
        internal NavigationPublicationMode Mode {get;}
        internal NavigationPublicationPhase Phase {get;private set;}
        private bool released;
        internal override bool IsImmutableCaptureRoot=>false;
        private NavigationPreparedPublication(NavigationRetentionBudget.Scope scope,NavigationPublicationMode mode,long admission):base(LayoutBytes)
        {this.scope=scope;Mode=mode;admissionIdentity=admission;ownerThread=System.Threading.Thread.CurrentThread.ManagedThreadId;Phase=NavigationPublicationPhase.Preparing;Retain();}
        private static void ValidateHostPair(NavigationPublicationMode mode,NavigationHostRoot expected,NavigationHostRoot candidate)
        {
            if(!Enum.IsDefined(typeof(NavigationPublicationMode),mode)||
                mode==NavigationPublicationMode.AuthorityOnly&&(expected!=null||candidate!=null)||
                mode==NavigationPublicationMode.HostCreation&&(expected!=null||candidate==null)||
                mode==NavigationPublicationMode.HostSwap&&(expected==null||candidate==null))throw new ArgumentException("Invalid joint publication host mode.");
        }
        internal static NavRetainedArray<NavOwnedStorage> CopyReplacementsOwned(System.Collections.Generic.IReadOnlyList<NavOwnedStorage> source,NavigationRetentionBudget.Scope scope)
        {
            if(source==null||scope==null)throw new ArgumentNullException();scope.AssertOwnerLive();var reservation=scope.OwnedReservation();
            // No array/header allocation or Retain may precede this preflight.
            // Registered birth-only roots are live too; no hot-reference test.
            for(int index=0;index<source.Count;index++){
                var root=source[index];if(root==null||!root.IsImmutableCaptureRoot)throw new ArgumentException("Prepared replacement needs a sealed concrete owner.");
                NavStorageAdmission.RequireOwned(reservation,root);
            }
            return NavRetainedArray<NavOwnedStorage>.Copy(source,reservation.TryReserve);
        }
        internal static NavigationPreparedPublication CreateSealedOwned(NavigationRetentionBudget.Scope scope,
            NavigationPublicationMode mode,NavigationHostRoot expectedHost,NavigationAuthorityOwners expectedAuthority,
            NavigationHostRoot candidateHost,NavigationAuthorityOwners candidateAuthority,
            NavigationPreparedSlotAggregate replacements,long admission)
        {
            if(scope==null||admission<0||candidateAuthority==null||replacements==null)throw new ArgumentException("Missing joint publication versions/backing.");
            scope.AssertOwnerLive();ValidateHostPair(mode,expectedHost,candidateHost);var reservation=scope.OwnedReservation();
            if(mode!=NavigationPublicationMode.AuthorityOnly&&expectedAuthority==null)throw new ArgumentException("Host publication requires an observed authority version.");
            replacements.ValidatePrepared(scope);
            if(!candidateAuthority.IsImmutableCaptureRoot||candidateHost!=null&&!candidateHost.IsImmutableCaptureRoot)throw new ArgumentException("Unsealed joint publication candidate.");
            if(expectedHost!=null)NavStorageAdmission.RequireOwned(reservation,expectedHost);
            if(expectedAuthority!=null)NavStorageAdmission.RequireOwned(reservation,expectedAuthority);
            if(candidateHost!=null)NavStorageAdmission.RequireOwned(reservation,candidateHost);
            NavStorageAdmission.RequireOwned(reservation,candidateAuthority);NavStorageAdmission.RequireOwned(reservation,replacements);
            using(var allocation=new NavStorageAllocation(reservation.TryReserve)){
                allocation.Reserve(LayoutBytes);var joint=allocation.Register(new NavigationPreparedPublication(scope,mode,admission));
                try{joint.ExpectedHost=NavPhysicalMacro.Acquire(joint,expectedHost);joint.ExpectedAuthority=NavPhysicalMacro.Acquire(joint,expectedAuthority);
                    joint.CandidateHost=NavPhysicalMacro.Acquire(joint,candidateHost);joint.CandidateAuthority=NavPhysicalMacro.Acquire(joint,candidateAuthority);
                    joint.Replacements=NavPhysicalMacro.Acquire(joint,replacements);joint.Phase=NavigationPublicationPhase.Sealed;return joint;
                }catch{joint.ReleaseStorage();throw;}
            }
        }
        // Call under transportGate alongside ALL operation/mailbox/worker guards,
        // before any assignment. This method does not mutate Session slots.
        internal void ValidateSource(NavigationHostRoot host,NavigationAuthorityOwners authority,long admission,NavigationSession targetSession=null)
        {
            if(ownerThread!=System.Threading.Thread.CurrentThread.ManagedThreadId)throw new InvalidOperationException("Joint publication belongs to another thread.");
            scope.AssertOwnerLive();
            if(Phase!=NavigationPublicationPhase.Sealed||scope.IsCancelled||!ReferenceEquals(host,ExpectedHost)||
                !ReferenceEquals(authority,ExpectedAuthority)||admission!=admissionIdentity)throw new InvalidOperationException("Joint publication lost its source version/phase.");
            ValidateHostPair(Mode,host,CandidateHost);Replacements.ValidateInstall(scope,targetSession);
        }
        internal void BeginPublishing()
        {if(ownerThread!=System.Threading.Thread.CurrentThread.ManagedThreadId||Phase!=NavigationPublicationPhase.Sealed)throw new InvalidOperationException("Invalid joint publication phase/thread.");Phase=NavigationPublicationPhase.Publishing;}
        // The publisher has completed its no-throw assignment block; no checks,
        // allocation, callbacks or notification belong after the first write.
        internal void MarkPublishedNoThrow()=>Phase=NavigationPublicationPhase.Published;
        internal void AbortBeforeWrites()
        {if(ownerThread!=System.Threading.Thread.CurrentThread.ManagedThreadId||Phase==NavigationPublicationPhase.Published)throw new InvalidOperationException("Cannot abort published/foreign joint.");Phase=NavigationPublicationPhase.Aborted;}
        internal void ReleaseStorage(){if(released)return;released=true;Release();}
        internal override void ReleaseOwner()=>ReleaseStorage();
        protected override void ReleaseEdges(){ExpectedHost?.Release();ExpectedAuthority?.Release();CandidateHost?.Release();CandidateAuthority?.Release();Replacements?.Release();ExpectedHost=CandidateHost=null;ExpectedAuthority=CandidateAuthority=null;Replacements=null;}
    }
    // One traversal preserves IDs shared by Session authority and host BODY.
    internal sealed class NavigationHostEnvelope : NavOwnedStorage, INavPhysicalBody
    {
        private const long LayoutBytes=192;
        internal NavigationHostRoot Host {get;private set;}
        internal NavigationAuthorityOwners Authority {get;private set;}
        internal long PublicationIdentity {get;private set;}
        private bool released;
        internal override bool IsImmutableCaptureRoot=>Host?.IsImmutableCaptureRoot==true&&Authority?.IsImmutableCaptureRoot==true;
        public NavPhysicalTag PhysicalTag=>new NavPhysicalTag(PublicationIdentity>0?NavPhysicalStorageKind.HostEnvelopePublication:NavPhysicalStorageKind.HostEnvelope);
        private NavigationHostEnvelope(bool owner):base(LayoutBytes){if(owner)Retain();}
        private static NavigationHostEnvelope Allocate(INavStorageReservation reservation,bool owner)
        {using(var allocation=new NavStorageAllocation(reservation.TryReserve)){allocation.Reserve(LayoutBytes);return allocation.Register(new NavigationHostEnvelope(owner));}}
        internal static NavigationHostEnvelope CreateOwned(NavigationHostRoot host,NavigationAuthorityOwners authority,INavStorageReservation reservation,long publication=0)
        {if(publication<0||host==null||authority==null||!host.IsImmutableCaptureRoot||!authority.IsImmutableCaptureRoot)throw new ArgumentException("Missing immutable host/authority roots.");NavStorageAdmission.RequireOwned(reservation,host);NavStorageAdmission.RequireOwned(reservation,authority);var root=Allocate(reservation,true);try{root.PublicationIdentity=publication;root.Host=NavPhysicalMacro.Acquire(root,host);root.Authority=NavPhysicalMacro.Acquire(root,authority);return root;}catch{root.ReleaseStorage();throw;}}
        public void WritePhysicalBody(NavPhysicalBodyWriter writer)
        {if(!IsImmutableCaptureRoot||StorageBytes!=NavStorageLayout.Charge(LayoutBytes))throw new ArgumentException("Invalid host envelope.");if(PublicationIdentity>0)writer.Output.Write(PublicationIdentity);NavPhysicalMacro.WriteEdge(writer,this,Host);NavPhysicalMacro.WriteEdge(writer,this,Authority);}
        internal static NavigationHostEnvelope ReadPhysicalBody(NavPhysicalBodyReader reader,bool publication=false)
        {long identity=publication?reader.Input.ReadInt64():0;if(publication&&identity<1)throw new ArgumentException("Invalid durable admission publication identity.");var host=NavPhysicalMacro.ReadEdge<NavigationHostRoot>(reader);var authority=NavPhysicalMacro.ReadEdge<NavigationAuthorityOwners>(reader);var root=Allocate(reader.Reservation,false);root.PublicationIdentity=identity;root.Host=NavPhysicalMacro.Acquire(root,host);root.Authority=NavPhysicalMacro.Acquire(root,authority);if(!root.IsImmutableCaptureRoot)throw new ArgumentException("Mutable host envelope.");return root;}
        internal void ReleaseStorage(){if(released)return;released=true;Release();}
        internal override void ReleaseOwner()=>ReleaseStorage();
        protected override void ReleaseEdges(){Host?.Release();Authority?.Release();Host=null;Authority=null;}
    }
    public sealed partial class NavigationSession
    {
        internal NavigationLiveWriteToken ObserveLiveWriteToken()
        {if(!System.Threading.Monitor.IsEntered(transportGate))throw new InvalidOperationException("Live write observation requires transport gate.");return new NavigationLiveWriteToken(this,authorityOperationIdentity);}
        internal bool MatchesLiveWriteToken(NavigationLiveWriteToken token)
        =>System.Threading.Monitor.IsEntered(transportGate)&&ReferenceEquals(token.Session,this)&&token.Thread==System.Threading.Thread.CurrentThread.ManagedThreadId&&token.Operation==authorityOperationIdentity;
        private NavigationAuthorityOwners authorityOwners;
        internal NavGeometry AuthorityGeometry=>geometry;
        private void InitializeAuthorityOwners(NavigationRetentionBudget budget,NavigationAuthorityOwners decoded=null)
        {
            physicalHostBudget=budget??new NavigationRetentionBudget();
            if(decoded!=null){
                var savedProfile=(NavigationProfile)decoded.Profile.Facade;
                if(savedProfile.ProfileId!=profile.ProfileId||savedProfile.Revision!=profile.Revision||!System.Linq.Enumerable.SequenceEqual(SaveProfile(savedProfile),SaveProfile(profile)))throw new ArgumentException("Decoded authority profile differs from world binding.");
                if(!decoded.BelongsTo(physicalHostBudget))throw new ArgumentException("Decoded authority belongs to another ledger.");
                decoded.Retain();authorityOwners=decoded;geometry=decoded.Geometry.Facade;navigationGeometry=decoded.NavigationGeometry.Facade;profile=(NavigationProfile)decoded.Profile.Facade;return;
            }
            if(!physicalHostBudget.TryOpenScope(NavigationPreparationKind.Admission,Generation,0,out var scope))throw new InvalidOperationException("Authority initialization admission refused.");
            using(scope){
                var root=NavigationAuthorityOwners.CreateOwned(geometry,navigationGeometry,profile,scope.OwnedReservation());
                NavigationPreparedSlotAggregate replacements=null;NavigationPreparedPublication joint=null;bool published=false;
                try{
                    var nextGeometry=root.Geometry.Facade;var nextNavigation=root.NavigationGeometry.Facade;var nextProfile=(NavigationProfile)root.Profile.Facade;
                    replacements=NavigationPreparedSlotAggregate.CreateOwned(scope);
                    joint=NavigationPreparedPublication.CreateSealedOwned(scope,NavigationPublicationMode.AuthorityOnly,null,null,null,root,replacements,0);
                    scope.Attach(joint);
                    scope.Publish(joint,_=>{
                        using(EnterTransport()){
                            if(!ReferenceEquals(physicalHostBudget,scope.Identity)||commandGroup!=null||admissionBatchDepth!=0)throw new InvalidOperationException("Fresh authority initialization lost its source context.");
                            joint.ValidateSource(committedHostRoot,authorityOwners,admissionPublicationIdentity,this);joint.BeginPublishing();
                            // The factory reference becomes the Session reference.
                            // Everything below is a no-throw pointer/phase assignment.
                            authorityOwners=root;geometry=nextGeometry;navigationGeometry=nextNavigation;profile=nextProfile;
                            joint.MarkPublishedNoThrow();published=true;
                        }
                    });
                    scope.ResolveCommitted();
                }catch{if(!published){joint?.AbortBeforeWrites();root.ReleaseStorage();}throw;}
                finally{joint?.ReleaseStorage();replacements?.ReleaseStorage();}
            }
        }
        private void AdoptDecodedAuthority(NavigationAuthorityOwners decoded)
        {
            if(decoded==null||ReferenceEquals(decoded,authorityOwners))return;
            if(!SameGeometry(SaveGeometry(geometry),decoded.Geometry.Facade)||!SameGeometry(SaveGeometry(navigationGeometry),decoded.NavigationGeometry.Facade)||
                profile.ProfileId!=((NavigationProfile)decoded.Profile.Facade).ProfileId||profile.Revision!=((NavigationProfile)decoded.Profile.Facade).Revision||
                !System.Linq.Enumerable.SequenceEqual(SaveProfile(profile),SaveProfile((NavigationProfile)decoded.Profile.Facade)))throw new ArgumentException("Decoded authority core differs from destination.");
            var old=authorityOwners;decoded.Retain();authorityOwners=decoded;
            geometry=decoded.Geometry.Facade;navigationGeometry=decoded.NavigationGeometry.Facade;profile=(NavigationProfile)decoded.Profile.Facade;
            Crowd.SetGeometry(geometry);Crowd.Rebind(profile);old?.Release();
        }
        private NavigationAuthorityOwners ReplaceAuthorityCore(NavGeometry nextGeometry,NavGeometry nextNavigation,NavigationProfile nextProfile)
        {
            NavigationHostRoot expectedHost;NavigationAuthorityOwners expectedAuthority;
            NavigationRetentionBudget budget;long publication,operation;int batchDepth,allocationThread;
            using(EnterTransport()){
                budget=physicalHostBudget;expectedHost=committedHostRoot;expectedAuthority=authorityOwners;
                publication=admissionPublicationIdentity;operation=authorityOperationIdentity;batchDepth=admissionBatchDepth;allocationThread=hostAllocationThread;
                expectedHost?.Retain();expectedAuthority?.Retain();
            }
            try{
                if(!budget.TryOpenScope(NavigationPreparationKind.Admission,Generation,publication,out var scope))throw new InvalidOperationException("Authority replacement admission refused.");
                using(scope){
                    var root=NavigationAuthorityOwners.CreateOwned(nextGeometry,nextNavigation,nextProfile,scope.OwnedReservation());
                    NavigationPreparedSlotAggregate replacements=null;NavigationPreparedPublication joint=null;bool published=false;
                    try{
                        var nextOwnedGeometry=root.Geometry.Facade;var nextOwnedNavigation=root.NavigationGeometry.Facade;var nextOwnedProfile=(NavigationProfile)root.Profile.Facade;
                        replacements=NavigationPreparedSlotAggregate.CreateOwned(scope);
                        // Existing host is the unchanged candidate in this core-only
                        // transition; neither host edge is implicitly nullable.
                        var mode=expectedHost==null?NavigationPublicationMode.AuthorityOnly:NavigationPublicationMode.HostSwap;
                        joint=NavigationPreparedPublication.CreateSealedOwned(scope,mode,expectedHost,expectedAuthority,expectedHost,root,replacements,publication);
                        scope.Attach(joint);
                        scope.Publish(joint,_=>{
                            using(EnterTransport()){
                                RejectTransferCallbackReentry();
                                if(!ReferenceEquals(physicalHostBudget,budget)||authorityOperationIdentity!=operation||admissionBatchDepth!=batchDepth||hostAllocationThread!=allocationThread)
                                    throw new InvalidOperationException("Authority replacement lost its operation context.");
                                joint.ValidateSource(committedHostRoot,authorityOwners,admissionPublicationIdentity,this);joint.BeginPublishing();
                                authorityOwners=root;geometry=nextOwnedGeometry;navigationGeometry=nextOwnedNavigation;profile=nextOwnedProfile;
                                joint.MarkPublishedNoThrow();published=true;
                            }
                        });
                        scope.ResolveCommitted();
                        // The retired Session reference transfers to the caller,
                        // which keeps it until its dependent rebind has finished.
                        return expectedAuthority;
                    }catch{if(!published){joint?.AbortBeforeWrites();root.ReleaseStorage();}throw;}
                    finally{joint?.ReleaseStorage();replacements?.ReleaseStorage();}
                }
            }finally{expectedHost?.Release();expectedAuthority?.Release();}
        }
        // Host stop leaves Session authority alive. Destruction is explicit and
        // requires the worker's detach/ACK before dropping all Session facades.
        internal void DestroyNavigationAuthority()
        {
            NavigationHostRoot host;NavigationAuthorityOwners authority;NavigationRetentionBudget.Scope staged;NavCrowd retiredCrowd;NavigationInstalledExecutions retiredExecutions;
            using(EnterTransport()){
                if(certifiedHost!=null||hostAllocationThread!=0)throw new InvalidOperationException("Navigation destruction requires detached worker ACK.");
                authorityOperationIdentity=checked(authorityOperationIdentity+1);staged=DetachPreparedCommandNoThrow();host=committedHostRoot;committedHostRoot=null;authority=authorityOwners;authorityOwners=null;
                while(Requests.TryDiscard(out _)){}while(Answers.TryDequeue(out _)){}
                pending.Clear();if(ownedProbes==null)probes.Clear();probeAnswers.Clear();ClearLegacyProbePayloads();ClearBarrierAnswers();registeredRoutes?.Clear();registeredGraphKeys?.Clear();
                groups.Clear();memberGroups.Clear();commandGroup=null;retiredExecutions=installedExecutions;installedExecutions=null;movementIdentities.Clear();
                allocating.Clear();liveArrivalLeaseTable?.ClearRetirementLinksNoThrow();if(liveArrivalLeaseTable==null)arrivalProjectionLeases.Clear();arrivalLedger.Clear();reservations.Clear();retainedGoals.Clear();
                PublishLegacyAdmissionNoThrow(null);retiredCrowd=Crowd;Crowd=null;geometry=navigationGeometry=null;profile=null;Requests.CloseNoThrow();Answers.CloseNoThrow();
            }
            staged?.Discard();ownedAnswerTracking?.Release();ownedAnswerTracking=null;ownedProbes?.Release();ownedProbes=null;liveProbeAnswers?.Release();liveProbeAnswers=null;liveRegistrations?.Release();liveRegistrations=null;liveArrivalLeaseTable?.Release();liveArrivalLeaseTable=null;if(liveArrivalTables!=null){liveArrivalTables.Release();liveArrivalTables=null;}else{foreach(var attempt in arrivalAttempts.Values)ReleasePhysicalAttemptEdges(attempt);arrivalAttempts.Clear();}retiredCrowd?.ReleaseCrowdStorage();retiredExecutions?.Release();terrainNameOwners?.Release();terrainNameOwners=null;Answers.ReleaseRetention();transportOwner?.Release();transportOwner=null;host?.Release();authority?.Release();groupCommandBacking?.ReleaseStorage();groupCommandBacking=null;
        }
    }
}
