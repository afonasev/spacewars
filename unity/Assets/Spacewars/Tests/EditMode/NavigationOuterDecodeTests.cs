using System;
using System.Linq;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class NavigationOuterDecodeTests
    {
        private const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
        private static readonly Type Domain=typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.PlayableDomain",true);
        private static byte[] Captured()
        {
            var domain=Activator.CreateInstance(Domain,F,null,new object[]{PlayableProfile.Default,1L,false},null);
            var session=(NavigationSession)Domain.GetProperty("Navigation",F).GetValue(domain);
            int unit=(int)Domain.GetMethod("SpawnUnit",F).Invoke(domain,new object[]{new NavPoint(-18,-14),PlayableOwner.Player,PlayableEntityKind.Tank});
            Assert.True(session.Move(unit,new NavPoint(-10,-14)));
            var geometry=(NavGeometry)Domain.GetProperty("Geometry",F).GetValue(domain);
            var kernel=new CertifiedRouteKernel();kernel.Continue=()=>false;
            kernel.Pump(new PlayableRouteBinding(geometry,PlayableProfile.Default,session.Admission,session.RoutePort),1);
            var bytes=(byte[])Domain.GetMethod("CaptureWorldBytes",F).Invoke(domain,new object[]{7,"outer64"});
            kernel.Clear();Domain.GetMethod("DestroyOwnedNavigation",F).Invoke(domain,null);return bytes;
        }
        private static object Restore(byte[] bytes,NavigationRetentionBudget budget)
            =>Domain.GetMethod("RestoreWorldBytesAdmitted",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{bytes,PlayableProfile.Default,7,"outer64",budget});
        private static PlayableWorldState SmallEnvelope()=>new PlayableWorldState{Seed=7,Generation=1,Tick=11,SourceIdentity="input-scope",Binding=new byte[]{1},Participants=new byte[]{2},Domain=new byte[137],Navigation=new byte[79],AiAuthority=new byte[]{3},RuntimeTransport=new byte[]{4},Vision=new[]{new byte[43]}};
        private static NavOwnedStorage[] OuterBirths(NavigationDecodeTransaction transaction)
        {
            var scope=typeof(NavigationDecodeTransaction).GetField("scope",F).GetValue(transaction);
            var births=new List<NavOwnedStorage>();
            for(var owner=(NavOwnedStorage)scope.GetType().GetField("births",F).GetValue(scope);owner!=null;owner=owner.BirthNext)births.Add(owner);
            return births.ToArray();
        }
        [Test] public void ParsedSectionsRemainOuterOwnedAfterDefensiveEnvelopeInputScopeEnds()
        {
            var bytes=SmallEnvelope().Encode();var budget=new NavigationRetentionBudget();
            using(var outer=new NavigationDecodeTransaction(budget)){
                var state=PlayableWorldState.Decode(bytes,outer);var births=OuterBirths(outer);
                Assert.AreEqual(1,budget.ActiveScopes,"input scope resolved before Decode returns");Assert.AreEqual(NavigationRetentionBudget.ScopeBytes,budget.HeldBytes);
                Assert.AreEqual(births.Sum(owner=>owner.StorageBytes),budget.PhysicalBytes,"only outer section/DTO births remain paid");
                var arrays=births.OfType<NavigationDecodeTransaction.PrivateValue<byte[]>>().ToArray();
                Assert.False(arrays.Any(owner=>owner.Value.Length==bytes.Length),"full defensive envelope is already released");
                foreach(var section in new[]{state.Binding,state.Participants,state.Domain,state.Navigation,state.AiAuthority,state.RuntimeTransport,state.Vision[0]})Assert.True(arrays.Any(owner=>ReferenceEquals(owner.Value,section)),"every returned section retains its original outer owner");
                Assert.True(births.OfType<NavigationDecodeTransaction.PrivateValue<PlayableWorldState>>().Any(owner=>ReferenceEquals(owner.Value,state)));
                CollectionAssert.AreEqual(bytes,state.Encode());
            }
            Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
        }
        [Test] public void DefensiveEnvelopeInputIsIsolatedDuringAdmittedParseAndPublicDecode()
        {
            var bytes=SmallEnvelope().Encode();var original=(byte[])bytes.Clone();var budget=new NavigationRetentionBudget();bool mutated=false;
            using(var outer=new NavigationDecodeTransaction(budget)){
                budget.AllocationAdmission=_=>{if(!mutated&&budget.PhysicalBytes>0){mutated=true;Array.Clear(bytes,0,bytes.Length);}return true;};
                try{var state=PlayableWorldState.Decode(bytes,outer);Assert.True(mutated,"caller changes after private clone admission, before outer DTO allocation");CollectionAssert.AreEqual(original,state.Encode());}
                finally{budget.AllocationAdmission=null;}
            }
            Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
            var publicInput=(byte[])original.Clone();var publicState=PlayableWorldState.Decode(publicInput);Array.Clear(publicInput,0,publicInput.Length);CollectionAssert.AreEqual(original,publicState.Encode());
        }
        [TestCase("input-scope")][TestCase("clone")][TestCase("domain-section")]
        public void InputScopeCloneAndLaterSectionRefusalsKeepCallerAndOuterCleanup(string site)
        {
            var bytes=SmallEnvelope().Encode();var original=(byte[])bytes.Clone();var budget=new NavigationRetentionBudget();int calls=0,denied=0;
            long section=NavStorageLayout.Charge(NavStorageLayout.ObjectBytesBound(typeof(NavigationDecodeTransaction.PrivateValue<byte[]>))+32L+137);
            using(var outer=new NavigationDecodeTransaction(budget)){
                budget.AllocationAdmission=request=>{calls++;bool refuse=site=="input-scope"?calls==1:site=="clone"?calls==2:request==section;if(refuse)denied++;return !refuse;};
                try{Assert.Throws<InvalidOperationException>(()=>PlayableWorldState.Decode(bytes,outer));Assert.AreEqual(1,denied);CollectionAssert.AreEqual(original,bytes);Assert.AreEqual(1,budget.ActiveScopes,"failed nested input scope is resolved; outer remains live");}
                finally{budget.AllocationAdmission=null;}
            }
            Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
        }
        [TestCase(false)][TestCase(true)] public void AdmittedEnvelopeDigestOrTruncationFailureReleasesInputScope(bool truncated)
        {
            var bytes=SmallEnvelope().Encode();if(truncated)Array.Resize(ref bytes,bytes.Length-1);else bytes[bytes.Length-1]^=1;
            var original=(byte[])bytes.Clone();var budget=new NavigationRetentionBudget();
            using(var outer=new NavigationDecodeTransaction(budget)){Assert.Throws<ArgumentException>(()=>PlayableWorldState.Decode(bytes,outer));Assert.Zero(budget.PhysicalBytes);Assert.AreEqual(1,budget.ActiveScopes);CollectionAssert.AreEqual(original,bytes);}
            Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
        }
        [Test] public void PrivateNavigationSectionEndsBeforeBodyWhileOtherOwnersRemainUsable()
        {
            var bytes=Captured();var expected=PlayableWorldState.Decode(bytes);var budget=new NavigationRetentionBudget();
            using(var outer=new NavigationDecodeTransaction(budget))using(var input=new NavigationDecodeTransaction(budget)){
                var state=PlayableWorldState.DecodeForRestore(bytes,outer,input);CollectionAssert.AreEqual(bytes,state.Encode());
                var section=OuterBirths(input).OfType<NavigationDecodeTransaction.PrivateValue<byte[]>>().Single();Assert.AreSame(state.Navigation,section.Value);Assert.AreEqual(1,OuterBirths(input).Length,"only Navigation byte payload belongs to short section scope");
                Assert.False(OuterBirths(outer).OfType<NavigationDecodeTransaction.PrivateValue<byte[]>>().Any(owner=>ReferenceEquals(owner.Value,state.Navigation)));
                var nav=WorldWire.UnpackPrivate(state.Navigation,0,state.Navigation.Length,r=>WorldWire.ReadNavigationSessionState(r),outer);
                Assert.AreSame(outer,nav.DecodeHostOwner.Transaction);Assert.AreSame(nav.HostAuthority,nav.DecodeHostOwner.Value);var owners=OuterBirths(outer);long before=budget.PhysicalBytes,charge=section.StorageBytes;
                state.Navigation=null;input.Dispose();Assert.Zero(section.References);Assert.Null(section.Value);Assert.AreEqual(before-charge,budget.PhysicalBytes);Assert.AreEqual(1,budget.ActiveScopes);
                foreach(var owner in owners)Assert.Greater(owner.References,0,"other sections/DTO and private Host input remain outer-owned");
                CollectionAssert.AreEqual(expected.Domain,state.Domain);CollectionAssert.AreEqual(expected.Binding,state.Binding);CollectionAssert.AreEqual(expected.Participants,state.Participants);Assert.NotNull(nav.Geometry);Assert.NotNull(nav.HostAuthority);
                bool bodyOpened=false;int scopesBeforeBody=-1;budget.AllocationAdmission=request=>{if(!bodyOpened&&request==NavigationRetentionBudget.ScopeBytes){bodyOpened=true;scopesBeforeBody=budget.ActiveScopes;}return true;};
                NavigationHostRoot host=null;NavigationAuthorityOwners authority=null;
                try{NavigationSession.DecodeDetachedHostAuthority(nav,outer,out host,out authority,out _);Assert.True(bodyOpened);Assert.AreEqual(1,scopesBeforeBody,"Navigation-section scope gone before BODY scope opens");Assert.NotNull(host);Assert.NotNull(authority);}
                finally{budget.AllocationAdmission=null;host?.Release();authority?.Release();}
                Assert.AreEqual(1,budget.ActiveScopes);
            }
            Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
        }
        [Test] public void PrivateNavigationInputPreservesMutationIsolationAndGeneralDecodeOwnership()
        {
            var bytes=SmallEnvelope().Encode();var original=(byte[])bytes.Clone();var budget=new NavigationRetentionBudget();bool mutated=false;
            using(var outer=new NavigationDecodeTransaction(budget))using(var input=new NavigationDecodeTransaction(budget)){
                budget.AllocationAdmission=_=>{if(!mutated&&budget.PhysicalBytes>0){mutated=true;Array.Clear(bytes,0,bytes.Length);}return true;};
                try{var state=PlayableWorldState.DecodeForRestore(bytes,outer,input);Assert.True(mutated);CollectionAssert.AreEqual(original,state.Encode());Assert.AreSame(state.Navigation,OuterBirths(input).OfType<NavigationDecodeTransaction.PrivateValue<byte[]>>().Single().Value);}
                finally{budget.AllocationAdmission=null;}
            }
            Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
            using(var outer=new NavigationDecodeTransaction(budget)){var state=PlayableWorldState.Decode(original,outer);Assert.True(OuterBirths(outer).OfType<NavigationDecodeTransaction.PrivateValue<byte[]>>().Any(owner=>ReferenceEquals(owner.Value,state.Navigation)));CollectionAssert.AreEqual(original,state.Encode());}
            Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
        }
        [TestCase("section-scope")][TestCase("section")][TestCase("later-dto")]
        public void PrivateRestoreNavigationScopeSectionAndLaterDtoRefusalCleanUp(string site)
        {
            var bytes=Captured();var expected=(byte[])bytes.Clone();var navigation=PlayableWorldState.Decode(bytes).Navigation;var budget=new NavigationRetentionBudget();int calls=0,denied=0;bool sectionSeen=false;
            long sectionCharge=NavStorageLayout.Charge(NavStorageLayout.ObjectBytesBound(typeof(NavigationDecodeTransaction.PrivateValue<byte[]>))+32L+navigation.Length);
            budget.AllocationAdmission=request=>{
                calls++;bool refuse=site=="section-scope"?calls==2:site=="section"?request==sectionCharge:sectionSeen&&new System.Diagnostics.StackTrace().GetFrames().Any(frame=>frame.GetMethod().Name=="ReadNavigationSessionState");
                if(request==sectionCharge)sectionSeen=true;if(refuse){denied++;return false;}return true;
            };
            try{var error=Assert.Throws<TargetInvocationException>(()=>Restore(bytes,budget));Assert.IsInstanceOf<InvalidOperationException>(error.InnerException);Assert.AreEqual(1,denied);CollectionAssert.AreEqual(expected,bytes);}
            finally{budget.AllocationAdmission=null;}
            Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
        }
        [TestCase(false)][TestCase(true)] public void PrivateNavigationRestoreDigestAndTruncationCleanUp(bool truncated)
        {
            var bytes=Captured();if(truncated)Array.Resize(ref bytes,bytes.Length-1);else bytes[bytes.Length-1]^=1;var expected=(byte[])bytes.Clone();var budget=new NavigationRetentionBudget();
            var error=Assert.Throws<TargetInvocationException>(()=>Restore(bytes,budget));Assert.IsInstanceOf<ArgumentException>(error.InnerException);CollectionAssert.AreEqual(expected,bytes);Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
        }
        [Test] public void PrivateNavigationInputRejectsForeignLedgerBeforeDecodeAdmission()
        {
            var a=new NavigationRetentionBudget();var b=new NavigationRetentionBudget();int calls=0;
            using(var outer=new NavigationDecodeTransaction(a))using(var input=new NavigationDecodeTransaction(b)){
                a.AllocationAdmission=_=>{calls++;return true;};b.AllocationAdmission=a.AllocationAdmission;
                try{Assert.Throws<ArgumentException>(()=>PlayableWorldState.DecodeForRestore(SmallEnvelope().Encode(),outer,input));Assert.Zero(calls);}
                finally{a.AllocationAdmission=null;b.AllocationAdmission=null;}
            }
            Assert.Zero(a.PhysicalBytes);Assert.Zero(a.HeldBytes);Assert.Zero(a.ActiveScopes);Assert.Zero(b.PhysicalBytes);Assert.Zero(b.HeldBytes);Assert.Zero(b.ActiveScopes);
        }
        [Test] public void ActualWorldRestoreUsesOuterLedgerForPrivateInputAndInstalledHost()
        {
            var bytes=Captured();var budget=new NavigationRetentionBudget();int allocations=0;
            budget.AllocationAdmission=_=>{allocations++;return true;};
            var domain=Restore(bytes,budget);var session=(NavigationSession)Domain.GetProperty("Navigation",F).GetValue(domain);
            Assert.AreSame(budget,session.PhysicalHostBudget);Assert.Greater(allocations,10);
            Assert.Greater(budget.PhysicalBytes,0);Assert.Greater(budget.PeakRetainedBytes,budget.RetainedBytes,"outer input/DTO staging overlapped the installed BODY");
            Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
            var encoded=(byte[])Domain.GetMethod("CaptureWorldBytes",F).Invoke(domain,new object[]{7,"outer64"});CollectionAssert.AreEqual(bytes,encoded);
            session.ReleaseCommittedHostRoot();Assert.Greater(budget.PhysicalBytes,0,"Session authority remains live after host-only release");Domain.GetMethod("DestroyOwnedNavigation",F).Invoke(domain,null);Assert.Zero(budget.PhysicalBytes);
        }
        [TestCase(1)][TestCase(2)][TestCase(3)] public void RefusalBeforePrivateInputLeavesCallerBytesAndLedgerUntouched(int allocation)
        {
            var bytes=Captured();var original=(byte[])bytes.Clone();var budget=new NavigationRetentionBudget();int seen=0;
            budget.AllocationAdmission=_=>++seen!=allocation;
            var error=Assert.Throws<TargetInvocationException>(()=>Restore(bytes,budget));Assert.IsInstanceOf<InvalidOperationException>(error.InnerException);
            CollectionAssert.AreEqual(original,bytes);Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
        }
        [Test] public void EachAdmittedOuterRestoreSiteAbortsCandidateWithoutLeaking()
        {
            var bytes=Captured();var original=(byte[])bytes.Clone();int sites=0;var countBudget=new NavigationRetentionBudget();
            countBudget.AllocationAdmission=_=>{sites++;return true;};var counted=Restore(bytes,countBudget);
            Domain.GetMethod("DestroyOwnedNavigation",F).Invoke(counted,null);Assert.Zero(countBudget.PhysicalBytes);Assert.Greater(sites,10);
            TestContext.WriteLine("Observed outer restore admission sites: "+sites);
            for(int refusal=1;refusal<=sites;refusal++){
                var budget=new NavigationRetentionBudget();int seen=0,target=refusal;budget.AllocationAdmission=_=>++seen!=target;
                var error=Assert.Throws<TargetInvocationException>(()=>Restore(bytes,budget),"admitted site "+refusal);
                Assert.IsInstanceOf<InvalidOperationException>(error.InnerException,"admitted site "+refusal);
                CollectionAssert.AreEqual(original,bytes);Assert.Zero(budget.PhysicalBytes,"admitted site "+refusal);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
            }
        }
        [Test] public void EveryActualWorldCaptureAdmissionRefusalReleasesPrivateOutputAndKeepsWorld()
        {
            var domain=Activator.CreateInstance(Domain,F,null,new object[]{PlayableProfile.Default,1L,false},null);
            var session=(NavigationSession)Domain.GetProperty("Navigation",F).GetValue(domain);
            byte[] Capture()=>(byte[])Domain.GetMethod("CaptureWorldBytes",F).Invoke(domain,new object[]{7,"capture64"});
            var bytes=Capture();var budget=session.PhysicalHostBudget;long live=budget.PhysicalBytes;int sites=0;
            try{
                budget.AllocationAdmission=_=>{sites++;return true;};CollectionAssert.AreEqual(bytes,Capture());budget.AllocationAdmission=null;
                Assert.Greater(sites,10);Assert.AreEqual(live,budget.PhysicalBytes);
                TestContext.WriteLine("Actual world capture admission sites: "+sites);
                for(int refusal=1;refusal<=sites;refusal++){
                    int seen=0,target=refusal;budget.AllocationAdmission=_=>++seen!=target;
                    var error=Assert.Throws<TargetInvocationException>(()=>Capture(),"capture site "+refusal);
                    Assert.IsInstanceOf<InvalidOperationException>(error.InnerException,"capture site "+refusal);
                    budget.AllocationAdmission=null;Assert.AreEqual(live,budget.PhysicalBytes,"capture site "+refusal);
                    Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);CollectionAssert.AreEqual(bytes,Capture());
                }
            }finally{budget.AllocationAdmission=null;Domain.GetMethod("DestroyOwnedNavigation",F).Invoke(domain,null);}
            Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
        }
        [Test] public void ActualWorldSectionsStayChargedThroughEncodingAndRepeatedCapture()
        {
            var domain=Activator.CreateInstance(Domain,F,null,new object[]{PlayableProfile.Default,1L,false},null);
            var session=(NavigationSession)Domain.GetProperty("Navigation",F).GetValue(domain);var budget=session.PhysicalHostBudget;
            PlayableWorldState Capture()=>(PlayableWorldState)Domain.GetMethod("CaptureWorldState",F).Invoke(domain,new object[]{7,"capture64"});
            long Unique(PlayableWorldState a,PlayableWorldState b=null)=>NavigationStorageOwnershipTests.UniqueWithSession(session,
                a.CaptureNavigation.CaptureRoot,a.CaptureNavigation.CaptureAuthority,a.CaptureNavigation.CaptureOutput,a.CaptureNavigation.CaptureArrivalOutput,a.CaptureNavigation.CaptureDto,
                a.CaptureDomain,a.CaptureNavigationBytes,a.CaptureBinding,a.CaptureParticipants,a.CaptureVision,
                b?.CaptureNavigation.CaptureRoot,b?.CaptureNavigation.CaptureAuthority,b?.CaptureNavigation.CaptureOutput,b?.CaptureNavigation.CaptureArrivalOutput,b?.CaptureNavigation.CaptureDto,
                b?.CaptureDomain,b?.CaptureNavigationBytes,b?.CaptureBinding,b?.CaptureParticipants,b?.CaptureVision,
                (NavOwnedStorage)typeof(NavigationSession).GetField("authorityOwners",F).GetValue(session),
                (NavOwnedStorage)typeof(NavigationSession).GetField("groupCommandBacking",F).GetValue(session),
                (NavOwnedStorage)typeof(NavigationSession).GetField("transportOwner",F).GetValue(session),
                (NavOwnedStorage)session.Answers.GetType().GetField("retention",F).GetValue(session.Answers));
            using(var first=Capture()){
                CertifiedPhysicalPumpTests.AssertCaptureDtoLayout(first.CaptureNavigation);
                Assert.AreEqual(Unique(first),budget.PhysicalBytes);long live=budget.PhysicalBytes;
                var bytes=first.Encode();CollectionAssert.AreEqual(WorldCaptureEncodingTests.Original(first),bytes);Assert.AreEqual(live,budget.PhysicalBytes);
                using(var second=Capture()){
                    CertifiedPhysicalPumpTests.AssertCaptureDtoLayout(second.CaptureNavigation);
                    Assert.AreEqual(Unique(first,second),budget.PhysicalBytes);CollectionAssert.AreEqual(bytes,second.Encode());
                    Domain.GetMethod("DestroyOwnedNavigation",F).Invoke(domain,null);
                    Assert.AreEqual(Unique(first,second),budget.PhysicalBytes);CollectionAssert.AreEqual(bytes,first.Encode());
                }
                Assert.AreEqual(Unique(first),budget.PhysicalBytes);
            }
            Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
        }
        [TestCase(false)][TestCase(true)] public void OwnedWorldSectionReplacementRetainsOldOutputOnEveryRefusal(bool ai)
        {
            var domain=Activator.CreateInstance(Domain,F,null,new object[]{PlayableProfile.Default,1L,false},null);
            var session=(NavigationSession)Domain.GetProperty("Navigation",F).GetValue(domain);var budget=session.PhysicalHostBudget;
            using(var state=(PlayableWorldState)Domain.GetMethod("CaptureWorldState",F).Invoke(domain,new object[]{7,"capture64"})){
                Action<Action<System.IO.BinaryWriter>> replace=ai?state.SetAiCapture:state.SetTransportCapture;
                replace(w=>{WorldWire.String(w,"old private \u043f\u043e\u043b\u0435");w.Write(new byte[17]);});
                var old=ai?state.CaptureAi:state.CaptureTransport;var oldBytes=(byte[])old.Bytes.Clone();
                long stable=budget.PhysicalBytes;int sites=0;
                budget.AllocationAdmission=_=>{sites++;return true;};
                // Count real replacement operations on a different live section, preserving old.
                if(ai)state.SetTransportCapture(w=>{WorldWire.String(w,"replacement private section");w.Write(new byte[1025]);});
                else state.SetAiCapture(w=>{WorldWire.String(w,"replacement private section");w.Write(new byte[1025]);});
                budget.AllocationAdmission=null;
                long live=budget.PhysicalBytes;Assert.Greater(sites,3);
                try{
                    for(int refusal=1;refusal<=sites;refusal++){
                        int seen=0,target=refusal;budget.AllocationAdmission=_=>++seen!=target;
                        Assert.Throws<InvalidOperationException>(()=>replace(w=>{WorldWire.String(w,"replacement private section");w.Write(new byte[1025]);}),"replacement site "+refusal);
                        budget.AllocationAdmission=null;Assert.AreSame(old,ai?state.CaptureAi:state.CaptureTransport);
                        CollectionAssert.AreEqual(oldBytes,ai?state.AiAuthority:state.RuntimeTransport);Assert.AreEqual(live,budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
                    }
                    replace(w=>{WorldWire.String(w,"replacement private section");w.Write(new byte[1025]);});
                    Assert.AreEqual(0,old.References);Assert.Null(old.Bytes);
                    Assert.Greater(budget.PhysicalBytes,stable);
                    CollectionAssert.AreEqual(WorldCaptureEncodingTests.Original(state),state.Encode());
                }finally{budget.AllocationAdmission=null;Domain.GetMethod("DestroyOwnedNavigation",F).Invoke(domain,null);}
            }
            Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
        }
        [TestCase(0)][TestCase(1)][TestCase(7)][TestCase(8)][TestCase(9)][TestCase(1025)]
        public void CaptureByteAggregateBoundsConcreteObjectAndActualArrayBacking(int length)
        {
            var budget=new NavigationRetentionBudget();
            using(var output=NavigationCaptureBytes.CreateOwned(length,budget,_=>new byte[length])){
                long objectBytes=NavStorageLayout.ObjectBytesBound(output.GetType());
                long backingBytes=32L+((output.Bytes.LongLength+7)/8)*8;
                Assert.GreaterOrEqual(output.StorageBytes,NavStorageLayout.Charge(objectBytes+backingBytes));
                Assert.AreEqual(output.StorageBytes,budget.PhysicalBytes);
            }
            Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
        }
        [TestCase(0)][TestCase(1)][TestCase(8)] public void CaptureVisionAggregateBoundsBothActualReferenceArrays(int count)
        {
            var budget=new NavigationRetentionBudget();
            using(var output=NavigationCaptureVision.CreateOwned(count,budget,vision=>{
                for(int i=0;i<count;i++)vision.Set(i,NavigationCaptureBytes.CreateOwned(i+1,budget,_=>new byte[i+1]));
            })){
                var owners=(Array)typeof(NavigationCaptureVision).GetField("owners",F).GetValue(output);
                long objectBytes=NavStorageLayout.ObjectBytesBound(output.GetType());
                long backingBytes=32L+8L*owners.LongLength+32L+8L*output.Bytes.LongLength;
                Assert.GreaterOrEqual(output.StorageBytes,NavStorageLayout.Charge(objectBytes+backingBytes));
                Assert.AreEqual(NavigationStorageOwnershipTests.Unique(output),budget.PhysicalBytes);
            }
            Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
        }
        private sealed class ZeroBytes : IReadOnlyList<byte>
        {
            internal ZeroBytes(int count){Count=count;}public int Count{get;}public byte this[int index]=>0;
            public IEnumerator<byte> GetEnumerator(){for(int i=0;i<Count;i++)yield return 0;}IEnumerator IEnumerable.GetEnumerator()=>GetEnumerator();
        }
        [Test] public void ActualOuterInputRefusesAgainstLiveOwnedNeighborBeforeOriginal64MiB()
        {
            var bytes=Captured();var original=(byte[])bytes.Clone();Assert.Greater(bytes.Length,1024);
            var budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Admission,0,0,out var scope));
            NavRetainedArray<byte> neighbor;
            using(scope){
                int count=checked((int)(LayeredNavigationScheduler.MaxRetainedBytes-budget.RetainedBytes-NavStorageLayout.Charge(96)-1024));
                neighbor=NavRetainedArray<byte>.Copy(new ZeroBytes(count),scope.OwnedReservation().TryReserve);scope.Attach(neighbor);scope.Commit(neighbor,_=>{});
            }
            try{
                Assert.Greater(neighbor.Count,60*1024*1024);long live=budget.PhysicalBytes;
                var error=Assert.Throws<TargetInvocationException>(()=>Restore(bytes,budget));Assert.IsInstanceOf<InvalidOperationException>(error.InnerException);
                Assert.AreEqual(live,budget.PhysicalBytes,"failed decode must retain exactly the live neighboring owner");
                Assert.LessOrEqual(budget.PeakRetainedBytes,LayeredNavigationScheduler.MaxRetainedBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);CollectionAssert.AreEqual(original,bytes);
            }finally{neighbor.Release();}
            Assert.Zero(budget.PhysicalBytes);Assert.AreEqual(NavigationRetentionBudget.RootBytes,budget.RetainedBytes);
        }
    }
}
