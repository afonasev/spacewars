using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Security.Cryptography;
using NUnit.Framework;
using Newtonsoft.Json.Linq;
using Spacewars.Runtime;
using Spacewars.Presentation;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class CrowdMotionProfileTests
    {
        internal static PlayableProfile NonDefault(int revision=1)
        {var d=PlayableProfile.Default.CopyData();d.revision=revision;d.crowdMotionHeadingIntervals=32;d.crowdMotionSpeedBands=3;d.crowdMotionLookaheadSeconds=1.5;d.crowdMotionNeighborCap=12;return PlayableProfile.Create(d);}
        private static void Empty(NavigationRetentionBudget b){Assert.Zero(b.PhysicalBytes);Assert.Zero(b.HeldBytes);Assert.Zero(b.ActiveScopes);}
        private static void Destroy(PlayableDomain d){if(d==null)return;d.Navigation.ReleaseCommittedHostRoot();d.DestroyOwnedNavigation();Empty(d.Navigation.PhysicalHostBudget);}
        [Test] public void DefaultV1KeepsLegacy14AndExactProfileBindingTranscript()
        {
            var p=PlayableProfile.Default.Navigation;Assert.True(p.HasCrowdV1Defaults);Assert.AreEqual(14,p.WireValueCount);Assert.AreEqual(14,NavigationProfile.Metadata.Count);
            var original=NavigationProfile.Metadata.Select(f=>f.Read(p)).ToArray();CollectionAssert.AreEqual(original,p.CaptureWireValues());
            using(var m=new MemoryStream()){
                using(var w=new BinaryWriter(m,Encoding.UTF8,true)){w.Write(p.ProfileId);w.Write(p.Revision);foreach(double v in original)w.Write(v);}
                using(var h=SHA256.Create())Assert.AreEqual(BitConverter.ToString(h.ComputeHash(m.ToArray())).Replace("-","").ToLowerInvariant(),NavigationAdmission.ExactProfileBinding(p));
            }
            Assert.AreEqual(4,PlayableProfileMetadata.CrowdFields.Count);foreach(var f in PlayableProfileMetadata.CrowdFields)Assert.True(NativeBalanceFields.Editable(f));
            Assert.AreEqual(PlayableProfileMetadata.PreCrowdFields.Count+4,PlayableProfileMetadata.Fields.Count);
        }
        [Test] public void NonDefault18ForUnitAndDetachedValuesKeepAllFourSettingsWithoutInputAlias()
        {
            var p=NonDefault().Navigation;Assert.False(p.HasCrowdV1Defaults);Assert.AreEqual(18,p.WireValueCount);var values=p.CaptureWireValues();var original=(double[])values.Clone();
            var restored=NavigationProfile.FromWireValues(p.ProfileId,p.Revision,values);Array.Clear(values,0,values.Length);CollectionAssert.AreEqual(original,restored.CaptureWireValues());
            var unit=restored.ForUnit(.9,3.2,2.5);CollectionAssert.AreEqual(original.Skip(14).ToArray(),unit.CaptureWireValues().Skip(14).ToArray());Assert.AreEqual(.9,unit.Radius);Assert.AreEqual(3.2,unit.TankSpeed);Assert.AreEqual(2.5,unit.TankTurnSpeed);
            Assert.False(p.MatchesWireValues(NavigationProfile.Default.CaptureWireValues()),"old14 never matches expected nondefault settings");
        }
        [TestCase("length")][TestCase("redundant")][TestCase("fraction")][TestCase("nonfinite")]
        public void MalformedCrowdDtoFormsRejectWithoutNormalizingCaller(string kind)
        {
            var p=NonDefault().Navigation;double[] v=p.CaptureWireValues();
            if(kind=="length")Array.Resize(ref v,15);
            else if(kind=="redundant"){v=NavigationProfile.Default.CaptureWireValues().Concat(new[]{16d,2d,1.2,8d}).ToArray();}
            else if(kind=="fraction")v[14]=16.5;else v[16]=double.NaN;
            var before=(double[])v.Clone();Assert.Throws<ArgumentException>(()=>NavigationProfile.FromWireValues(p.ProfileId,p.Revision,v));CollectionAssert.AreEqual(before,v);
        }
        [Test] public void NonDefaultWorldRestoresExactAndKeepsFixedNextStep()
        {
            var p=NonDefault();var a=new PlayableDomain(p,71,false);PlayableDomain b=null;
            try{var bytes=a.CaptureWorldBytes(7,"crowd-profile");b=PlayableDomain.RestoreWorldBytes(bytes,p,7,"crowd-profile");CollectionAssert.AreEqual(bytes,b.CaptureWorldBytes(7,"crowd-profile"));a.Step(1d/30);b.Step(1d/30);CollectionAssert.AreEqual(a.CaptureWorldBytes(7,"crowd-profile"),b.CaptureWorldBytes(7,"crowd-profile"));}
            finally{Destroy(b);Destroy(a);}
        }
        private static byte[] HistoricalNonDefaultWorld(out PlayableProfile current)
        {
            var a=new PlayableDomain(NonDefault(),71,false);try{var data=PlayableProfile.Default.CopyData();data.revision=2;current=PlayableProfile.Create(data);Assert.IsNull(a.ValidateBalance(current));a.ApplyBalance(current);return a.CaptureWorldBytes(7,"crowd-profile");}finally{Destroy(a);}
        }
        [Test] public void HistoricalNonDefaultThenDefaultKeepsExplicitTermRegistryAndExactRestore()
        {
            var bytes=HistoricalNonDefaultWorld(out var p);var state=PlayableWorldState.Decode(bytes);Assert.AreEqual(-1,BitConverter.ToInt32(state.Domain,0));Assert.AreEqual(1,BitConverter.ToInt32(state.Domain,4));Assert.AreEqual(2,BitConverter.ToInt32(state.Domain,8));Assert.True(p.Navigation.HasCrowdV1Defaults);
            var b=PlayableDomain.RestoreWorldBytes(bytes,p,7,"crowd-profile");try{CollectionAssert.AreEqual(bytes,b.CaptureWorldBytes(7,"crowd-profile"));}finally{Destroy(b);}
        }
        [TestCase("marker")][TestCase("version")][TestCase("count")]
        public void MalformedExtendedTermRegistryRejectsAndCleans(string kind)
        {
            var bytes=HistoricalNonDefaultWorld(out var p);var s=PlayableWorldState.Decode(bytes);int offset=kind=="marker"?0:kind=="version"?4:8;BitConverter.GetBytes(kind=="marker"?-2:kind=="version"?2:0).CopyTo(s.Domain,offset);var budget=new NavigationRetentionBudget();Assert.Throws<ArgumentException>(()=>PlayableDomain.RestoreWorldBytesAdmitted(s.Encode(),p,7,"crowd-profile",budget));Empty(budget);
        }
        [Test] public void LegacyPositiveRegistryCannotInheritExpectedNonDefaultCrowdValues()
        {
            var a=new PlayableDomain(PlayableProfile.Default,71,false);try{var s=PlayableWorldState.Decode(a.CaptureWorldBytes(7,"crowd-profile"));Assert.Greater(BitConverter.ToInt32(s.Domain,0),0);var p=NonDefault();s.Binding=WorldWire.Binding(p);var budget=new NavigationRetentionBudget();var error=Assert.Throws<ArgumentException>(()=>PlayableDomain.RestoreWorldBytesAdmitted(s.Encode(),p,7,"crowd-profile",budget));StringAssert.Contains("Current transaction profile mismatch",error.Message);Empty(budget);}finally{Destroy(a);}
        }
        [Test] public void RedundantDefaultTermExtensionRejectsInsteadOfChangingOldGrammar()
        {
            var a=new PlayableDomain(PlayableProfile.Default,71,false);try{
                var s=PlayableWorldState.Decode(a.CaptureWorldBytes(7,"crowd-profile"));int end;
                using(var r=new BinaryReader(new MemoryStream(s.Domain,false))){Assert.AreEqual(1,r.ReadInt32());r.ReadInt32();WorldWire.String(r);foreach(var f in PlayableProfileMetadata.PreCrowdFields)WorldWire.Number(r);end=(int)r.BaseStream.Position;}
                var old=s.Domain;s.Domain=WorldWire.Pack(w=>{w.Write(-1);w.Write(1);w.Write(1);w.Write(old,4,end-4);foreach(double v in new[]{16d,2d,1.2,8d})w.Write(v);w.Write(old,end,old.Length-end);});var budget=new NavigationRetentionBudget();var error=Assert.Throws<ArgumentException>(()=>PlayableDomain.RestoreWorldBytesAdmitted(s.Encode(),PlayableProfile.Default,7,"crowd-profile",budget));StringAssert.Contains("Redundant default crowd term registry",error.Message);Empty(budget);
            }finally{Destroy(a);}
        }
        [Test] public void TargetedOwnedCrowdProfileRefusalLeavesCallerAndLedgerUntouched()
        {
            var p=NonDefault().Navigation;var original=p.CaptureWireValues();var b=new NavigationRetentionBudget();Assert.True(b.TryOpenScope(NavigationPreparationKind.Admission,1,0,out var s));b.AllocationAdmission=_=>false;
            try{Assert.Throws<InvalidOperationException>(()=>NavigationProfile.CopyOwned(p,s.OwnedReservation()));CollectionAssert.AreEqual(original,p.CaptureWireValues());}
            finally{b.AllocationAdmission=null;s.Discard();}Empty(b);
        }
        private static JObject Package(JObject values)=>new JObject{["schema"]=2,["defaultId"]=NativeBalanceStore.DefaultId,["profiles"]=new JArray(new JObject{["id"]=NativeBalanceStore.DefaultId,["name"]="Default",["release"]=new JArray(1),["versions"]=new JArray(new JObject{["revision"]=1,["date"]=null,["data"]=values})})};
        [TestCase(false)][TestCase(true)] public void StoredCrowdMissingKeysUseFixedV1AndExplicitValuesSurviveFutureBaseline(bool explicitValues)
        {
            var future=NonDefault();var values=JObject.FromObject(explicitValues?future.CopyData():PlayableProfile.Default.CopyData());if(!explicitValues)foreach(var key in new[]{"crowdMotionHeadingIntervals","crowdMotionSpeedBands","crowdMotionLookaheadSeconds","crowdMotionNeighborCap"})values.Remove(key);
            string dir=Path.Combine(Path.GetTempPath(),"spacewars-crowd-store-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
            try{var store=new NativeBalanceStore(future,Path.Combine(dir,"store.json"),false,Package(values));var p=store.Selected.Navigation;Assert.AreEqual(!explicitValues,p.HasCrowdV1Defaults);CollectionAssert.AreEqual((explicitValues?future.Navigation:PlayableProfile.Default.Navigation).CaptureWireValues().Skip(14).ToArray(),p.CaptureWireValues().Skip(14).ToArray());}
            finally{Directory.Delete(dir,true);}
        }
        [TestCase(false)][TestCase(true)] public void StoredPresentNullOrFractionalCrowdValueStillRejects(bool fractional)
        {
            var values=JObject.FromObject(PlayableProfile.Default.CopyData());values["crowdMotionHeadingIntervals"]=fractional?new JValue(16.5):JValue.CreateNull();string dir=Path.Combine(Path.GetTempPath(),"spacewars-crowd-store-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
            try{var action=new TestDelegate(()=>new NativeBalanceStore(PlayableProfile.Default,Path.Combine(dir,"store.json"),false,Package(values)).Compile(new JObject{["revision"]=1,["data"]=values}));if(fractional)Assert.Throws<ArgumentException>(action);else Assert.Throws<FormatException>(action);}
            finally{Directory.Delete(dir,true);}
        }
        [Test] public void ActualResourceBootstrapLoadsV1CrowdSettings()
        {
            // Foundation tests deliberately have no engine reference; invoke the actual resource loader.
            var loader=typeof(NativeBalanceStore).Assembly.GetType("Spacewars.Presentation.FoundryGreybox",true).GetMethod("LoadProfile",System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Static);
            Assert.NotNull(loader);var profile=(PlayableProfile)loader.Invoke(null,null);Assert.True(profile.Navigation.HasCrowdV1Defaults);
        }
    }
    public sealed partial class NavigationPhysicalBodyTests
    {
        [TestCase(false)][TestCase(true)] public void CrowdProfileBody14Or18KeepsOwnedAliasesExactBytesAndRealCharge(bool extended)
        {
            var source=extended?CrowdMotionProfileTests.NonDefault().Navigation:NavigationProfile.Default;var hot=new NavigationRetentionBudget();NavRetainedArray<NavOwnedStorage> root=null;var cold=new NavigationRetentionBudget();NavRetainedArray<NavOwnedStorage> restored=null;
            try{
                Assert.True(hot.TryOpenScope(NavigationPreparationKind.Admission,1,1,out var birth));using(birth){var p=NavigationProfile.CopyOwned(source,birth.OwnedReservation());Assert.AreEqual(extended?NavPhysicalStorageKind.ArrivalProfileCrowdV1:NavPhysicalStorageKind.ArrivalProfile,((INavPhysicalBody)p.StorageRoot).PhysicalTag.Storage);Assert.GreaterOrEqual(p.StorageRoot.StorageBytes,NavStorageLayout.Charge(NavStorageLayout.ObjectBytesBound(typeof(NavigationProfile))+NavStorageLayout.ObjectBytesBound(typeof(NavDeclarationStorage))));root=NavRetainedArray<NavOwnedStorage>.Copy(new[]{p.StorageRoot,p.StorageRoot},birth.TryReserve);p.StorageRoot.ReleaseOwner();birth.Attach(root);birth.Commit(root,_=>{});}
                var bytes=CaptureGraph(hot,root);Assert.AreEqual(Unique(root),hot.PhysicalBytes);Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var decode));using(decode){restored=(NavRetainedArray<NavOwnedStorage>)Decode(bytes,decode.OwnedReservation());decode.Attach(restored);decode.Commit(restored,_=>{});}
                Assert.AreSame(restored[0],restored[1]);var p2=(NavigationProfile)((NavDeclarationStorage)restored[0]).Facade;Assert.AreEqual(extended?18:14,p2.WireValueCount);CollectionAssert.AreEqual(source.CaptureWireValues(),p2.CaptureWireValues());Assert.AreEqual(hot.PhysicalBytes,cold.PhysicalBytes);CollectionAssert.AreEqual(bytes,CaptureGraph(cold,restored));
            }finally{root?.Release();restored?.Release();Assert.Zero(hot.PhysicalBytes);Assert.Zero(hot.HeldBytes);Assert.Zero(hot.ActiveScopes);Assert.Zero(cold.PhysicalBytes);Assert.Zero(cold.HeldBytes);Assert.Zero(cold.ActiveScopes);}
        }
    }
}
