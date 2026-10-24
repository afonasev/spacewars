using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Spacewars.BalanceLab;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Spacewars.Tests.PlayMode
{
    public sealed class NavigationCorridorActivationTests
    {
        private const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        private string directory;
        private static PlayableProfile Baseline()=>PlayableProfile.Create(JsonUtility.FromJson<PlayableProfileData>(Resources.Load<TextAsset>("PlayableProfile").text),new ThreeCrossingsMap(JsonUtility.FromJson<ThreeCrossingsProfileData>(Resources.Load<TextAsset>("ThreeCrossingsProfile").text)));
        private static JToken Canonical(JToken value)=>ContractCodec.Parse(ContractCodec.Write(value));
        private static void Retain(string name,byte[] bytes){var output=Environment.GetEnvironmentVariable("SPACEWARS_ACTIVATION_OUTPUT");if(!string.IsNullOrWhiteSpace(output)){Directory.CreateDirectory(output);File.WriteAllBytes(Path.Combine(output,name),bytes);}}
        private static JObject Package()=>JObject.Parse(Resources.Load<TextAsset>("NativeLabCatalog").text);
        [SetUp] public void Setup(){directory=Path.Combine(Path.GetTempPath(),"spacewars-corridor-activation-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);}
        [TearDown] public void Teardown(){if(Directory.Exists(directory))Directory.Delete(directory,true);NativeGameplayAudioProfile.Reload();}
        private static void GuardNew(NativeBalanceStore store){Assert.AreEqual(NativeBalanceStore.NewMatchProfileId,store.SelectedId);Assert.AreEqual(1,store.SelectedVersion);Assert.AreEqual(PlayableProfile.RequiredProfileId,store.Active.ProfileId);StringAssert.Contains("["+NativeBalanceStore.NewMatchProfileId+"] @1",store.ActiveIdentity);Assert.AreEqual(15,store.Active.Navigation.GroupCorridorMaximumExcessPercent);Assert.False(store.CanEdit);}
        [TestCase("fresh")][TestCase("player")][TestCase("existing-authoring")]
        public void StorePackagedActivationPreservesHistoryExactProfilesAndCopy(string mode)
        {
            var baseline=Baseline();var package=Package();var release=package["profiles"].Single(p=>(string)p["id"]==NativeBalanceStore.DefaultId);var newRow=package["profiles"].Single(p=>(string)p["id"]==NativeBalanceStore.NewMatchProfileId);var original=release["versions"].Single(v=>(int)v["revision"]==1);var data=(JObject)newRow["versions"][0]["data"].DeepClone();data.Remove("groupCorridorMaximumExcessPercent");Assert.True(JToken.DeepEquals(Canonical(data),Canonical(original["data"])),"new numeric policy15 is the only balance change");Assert.True(JToken.DeepEquals(Canonical(newRow["versions"][0]["audio"]),Canonical(original["audio"])));
            var originalBytes=System.Text.Encoding.UTF8.GetBytes(original.ToString(Formatting.None));Retain(mode+"-original-catalog-row.json",originalBytes);string originalHash=ContractCodec.Hash(originalBytes);
            string path=Path.Combine(directory,"history.json");NativeBalanceStore store;byte[] saved=null;string oldBinding=null;
            if(mode=="existing-authoring"){
                var oldPackage=(JObject)package.DeepClone();((JArray)oldPackage["profiles"]).Single(p=>(string)p["id"]==NativeBalanceStore.NewMatchProfileId).Remove();
                var previous=new NativeBalanceStore(baseline,path,true,oldPackage);previous.Select(NativeBalanceStore.DefaultId,1);oldBinding=NavigationAdmission.ExactProfileBinding(previous.Active.Navigation);previous.Copy("Моя сохранённая версия");saved=File.ReadAllBytes(path);Retain(mode+"-original-authoring-history.json",saved);
                store=NativeBalanceStore.ForApplication(baseline,path);CollectionAssert.AreEqual(saved,File.ReadAllBytes(path),"application activation never rewrites old authoring history");Assert.True(store.Profiles.Any(p=>(string)p["name"]=="Моя сохранённая версия"));
            }else if(mode=="player"){
                store=new NativeBalanceStore(baseline,path,false,package);Assert.AreEqual(NativeBalanceStore.DefaultId,store.SelectedId,"general constructor semantics unchanged");store.Select(NativeBalanceStore.NewMatchProfileId,1);
            }else store=NativeBalanceStore.ForApplication(baseline,path);
            GuardNew(store);var protectedRow=store.SelectedRow.DeepClone();var active=store.Active;var activeBinding=NavigationAdmission.ExactProfileBinding(active.Navigation);Assert.Throws<InvalidOperationException>(()=>store.Rename("Нельзя"));Assert.Throws<InvalidOperationException>(()=>store.Delete());Assert.True(JToken.DeepEquals(Canonical(protectedRow),Canonical(store.SelectedRow)));
            store.Copy("Независимая копия");Assert.True(store.CanEdit);Assert.AreEqual(15,store.Active.Navigation.GroupCorridorMaximumExcessPercent);Assert.AreNotEqual(NativeBalanceStore.NewMatchProfileId,store.SelectedId);
            store.ResetRelease();Assert.AreEqual(NativeBalanceStore.DefaultId,store.SelectedId);Assert.Zero(store.Active.Navigation.GroupCorridorMaximumExcessPercent);if(oldBinding!=null)Assert.AreEqual(oldBinding,NavigationAdmission.ExactProfileBinding(store.Active.Navigation));Assert.AreEqual(activeBinding,NavigationAdmission.ExactProfileBinding(active.Navigation),"existing immutable profile object cannot rebind");Assert.AreEqual(15,active.Navigation.GroupCorridorMaximumExcessPercent);if(mode=="existing-authoring")Assert.True(JToken.DeepEquals(Canonical(original),Canonical(store.Profiles.Single(p=>(string)p["id"]==NativeBalanceStore.DefaultId)["versions"][0])));
            CollectionAssert.AreEqual(originalBytes,System.Text.Encoding.UTF8.GetBytes(Package()["profiles"].Single(p=>(string)p["id"]==NativeBalanceStore.DefaultId)["versions"][0].ToString(Formatting.None)),"original catalog row bytes remain unchanged");
            TestContext.WriteLine("A2_ACTIVATION_STORE "+JsonConvert.SerializeObject(new{mode,OriginalCatalogRowSha256=originalHash,OriginalAuthoringHistorySha256=saved==null?null:ContractCodec.Hash(saved),OldExact=store.Active.Navigation.GroupCorridorMaximumExcessPercent,NewFrozen=active.Navigation.GroupCorridorMaximumExcessPercent,activeBinding,HistoryPreserved=saved!=null}));
        }
        [Test] public void OldMissingPolicyDoesNotInheritNewReleasePolicy()
        {
            var baseline=Baseline();var package=Package();var profile=package["profiles"].Single(p=>(string)p["id"]==NativeBalanceStore.DefaultId);var old=profile["versions"][0].DeepClone();var next=(JObject)old.DeepClone();next["revision"]=2;next["data"]["revision"]=2;next["data"]["groupCorridorMaximumExcessPercent"]=15;((JArray)profile["versions"]).Add(next);profile["release"]=new JArray(2);
            var store=new NativeBalanceStore(baseline,Path.Combine(directory,"inheritance.json"),true,package);Assert.AreEqual(15,store.Active.Navigation.GroupCorridorMaximumExcessPercent);store.Select(NativeBalanceStore.DefaultId,1);Assert.Zero(store.Active.Navigation.GroupCorridorMaximumExcessPercent);Assert.True(JToken.DeepEquals(Canonical(old),Canonical(store.SelectedRow)));var data=baseline.CopyData();data.groupCorridorMaximumExcessPercent=0;Assert.AreEqual(NavigationAdmission.ExactProfileBinding(PlayableProfile.Create(data,baseline.AuthoredMap).Navigation),NavigationAdmission.ExactProfileBinding(store.Active.Navigation));
        }
        [UnityTest,Timeout(180000)] public IEnumerator NormalPrepareLobbyMatchUsesPackagedPolicyAndInitialGroupRequest()
        {
            GameObject host=null;PlayableBootstrap app=null;PlayableRuntime runtime=null;UnityHostRouteService service=null;NavigationSession navigation=null;object domain=null;object census=null;var records=new System.Collections.Generic.List<object>();var gate=new object();bool seen=false,policy=false;string stage="created";long sequence=0,groupSequence=-1;int[] tanks=Array.Empty<int>();
            Keyboard ownedKeyboard=null;Mouse ownedMouse=null;NativeBalanceStore activationStore=null;bool preparationQueued=false;int runtimeEntries=0;var entryStates=new System.Collections.Generic.List<object>();var artifactErrors=new System.Collections.Generic.List<string>();
            object Inventory()=>InputSystem.devices.Select(d=>(object)new{d.deviceId,d.layout,d.displayName,d.added}).ToArray();var originalInventory=Inventory();
            var hook=typeof(LayeredNavigationScheduler).GetField("CertifiedQueryReadyObserver",BindingFlags.Static|BindingFlags.NonPublic);Delegate previous=(Delegate)hook.GetValue(null);Assert.IsNull(previous,"do not replace foreign observer");
            T Get<T>(string name)=>(T)typeof(PlayableBootstrap).GetField(name,Hidden).GetValue(app);void Put(string name,object value)=>typeof(PlayableBootstrap).GetField(name,Hidden).SetValue(app,value);object Call(string name,params object[] args)=>typeof(PlayableBootstrap).GetMethods(Hidden).Single(m=>m.Name==name&&m.GetParameters().Length==args.Length).Invoke(app,args);
            void Entry(string marker,bool protect=false)
            {
                try{var keyboard=app==null?null:Get<Keyboard>("lobbyKeyboard");var mouse=app==null?null:Get<Mouse>("lobbyMouse");var input=app==null?null:Get<Spacewars.Input.PlayableInput>("input");var setup=app==null?null:Get<NativeLobbyConfiguration>("lobbySetup");var current=app==null?null:Get<PlayableProfile>("profile");bool pad=setup?.Participants?.Count(p=>p.Human)==1&&Gamepad.all.Count>0;
                    var loading=app==null?null:Get<VisualElement>("loadingScreen");var item=new{Marker=marker,Inventory=Inventory(),OriginalInventory=originalInventory,OwnedKeyboard=ownedKeyboard?.deviceId,OwnedMouse=ownedMouse?.deviceId,Keyboard=keyboard?.deviceId,KeyboardAdded=keyboard?.added,Mouse=mouse?.deviceId,MouseAdded=mouse?.added,AssignedKeyboard=input?.AssignedKeyboard?.deviceId,AssignedMouse=input?.AssignedMouse?.deviceId,ValidationReason=setup==null||current==null?null:setup.Validate(current,keyboard?.added==true||pad,mouse?.added==true||pad),LobbyStatus=app==null?null:Get<Label>("lobbyStatus")?.text,LaunchEnabled=app==null?(bool?)null:Get<Button>("launchButton")?.enabledSelf,InLobby=app==null?(bool?)null:Get<bool>("inLobby"),Preparing=app==null?(bool?)null:Get<bool>("preparing"),PreparingPresentation=app==null?(bool?)null:Get<bool>("preparingPresentation"),MatchSetupPresent=app!=null&&Get<NativeLobbyConfiguration>("matchSetup")!=null,RuntimePresent=app!=null&&Get<PlayableRuntime>("runtime")!=null,LoadingText=loading?.Q<Label>("loading-status")?.text,LoadingDisplay=loading?.style.display.value.ToString(),CatalogIdentity=activationStore?.ActiveIdentity,SelectedQuality=activationStore?.HasActive==true?(int?)activationStore.Active.Navigation.GroupCorridorMaximumExcessPercent:null,SelectedProfileBinding=activationStore?.HasActive==true?NavigationAdmission.ExactProfileBinding(activationStore.Active.Navigation):null,CurrentProfileBinding=current==null?null:NavigationAdmission.ExactProfileBinding(current.Navigation),preparationQueued,runtimeEntries};entryStates.Add(item);var output=Environment.GetEnvironmentVariable("SPACEWARS_ACTIVATION_OUTPUT");if(!string.IsNullOrWhiteSpace(output)){Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,"entry-primitives.json"),JsonConvert.SerializeObject(new{originalInventory,entryStates,artifactErrors},Formatting.Indented));}
                }catch(Exception error){artifactErrors.Add(marker+": "+error);TestContext.WriteLine("A2_ENTRY_FLUSH_FAILURE "+marker+" "+error);if(!protect)throw;}
            }
            double deadline=Time.realtimeSinceStartupAsDouble+180;
            IEnumerator Until(Func<bool> ready,string phase){stage=phase;while(!ready()&&Time.realtimeSinceStartupAsDouble<deadline){Call("UpdateFrame");runtime=Get<PlayableRuntime>("runtime");if(runtime!=null){if(runtimeEntries==0){runtimeEntries=1;Entry("runtime-first-observed");}Assert.IsNull(runtime.Latest?.Failure,phase);Assert.False(runtime.IsStopped,phase);}yield return null;}Assert.True(ready(),phase+" tick="+runtime?.Latest?.Tick);}
            Action<NavScheduledRequest,object> observe=(request,work)=>{lock(gate){if(request.Subscription.CommandSequence!=groupSequence||!tanks.Contains((int)request.Subscription.EntityId))return;seen=true;policy=request.CertifiedGroupCorridor;records.Add(new{request.Subscription.EntityId,request.Subscription.CommandSequence,request.GroupQuery,request.CertifiedGroupCorridor,request.CompileKey,request.ArtifactKey,request.Profile.ExactProfileBinding});}};
            try{
                Entry("before-owned-device-setup");ownedKeyboard=InputSystem.AddDevice<Keyboard>();ownedMouse=InputSystem.AddDevice<Mouse>();ownedKeyboard.MakeCurrent();ownedMouse.MakeCurrent();
                host=new GameObject("Normal corridor activation");app=host.AddComponent<PlayableBootstrap>();app.enabled=false;var baseline=Baseline();Put("profile",baseline);var store=NativeBalanceStore.ForApplication(baseline,Path.Combine(directory,"normal-history.json"));activationStore=store;GuardNew(store);Put("balanceStore",store);
                Call("CreateWorld");Call("CreateHud");Call("CreateSpectatorHud");Put("input",host.AddComponent<Spacewars.Input.PlayableInput>());Call("BindLocalKeyboardInput");Call("CreateLobby");Call("ShowLobby");var setup=Get<NativeLobbyConfiguration>("lobbySetup");setup.HasExplicitSeed=true;setup.ExplicitSeed=19092026;Call("RebuildRoster");Entry("before-launch");
                Assert.AreSame(ownedKeyboard,Get<Keyboard>("lobbyKeyboard"));Assert.AreSame(ownedMouse,Get<Mouse>("lobbyMouse"));Assert.True(ownedKeyboard.added&&ownedMouse.added);Assert.AreSame(ownedKeyboard,Get<Spacewars.Input.PlayableInput>("input").AssignedKeyboard);Assert.AreSame(ownedMouse,Get<Spacewars.Input.PlayableInput>("input").AssignedMouse);
                Assert.IsNull(setup.Validate(Get<PlayableProfile>("profile"),ownedKeyboard.added,ownedMouse.added),Get<Label>("lobbyStatus").text);Assert.True(Get<Button>("launchButton").enabledSelf,Get<Label>("lobbyStatus").text);Assert.True(Get<bool>("inLobby"));Assert.False(Get<bool>("preparing"));GuardNew(store);
                Call("LaunchLobbyMatch");Entry("after-launch");Assert.True(Get<bool>("preparing"),"normal Launch must enter preparation");Assert.True(Get<bool>("preparingPresentation"));Assert.NotNull(Get<NativeLobbyConfiguration>("matchSetup"));preparationQueued=true;Entry("preparation-entry-confirmed");
                yield return Until(()=>Get<PlayableRuntime>("runtime")!=null&&!Get<bool>("preparing"),"normal PrepareLobbyMatch");runtime=Get<PlayableRuntime>("runtime");service=Get<UnityHostRouteService>("routeService");var active=runtime.Latest.ActiveProfile;Assert.AreEqual(PlayableProfile.RequiredProfileId,active.ProfileId);StringAssert.Contains(NativeBalanceStore.NewMatchProfileId+" @1",active.DisplayName);Assert.AreEqual(1,active.Revision);Assert.AreEqual(15,active.Navigation.GroupCorridorMaximumExcessPercent);Assert.AreEqual(19092026,runtime.Latest.Seed);Assert.True(active.AuthoredMap is ThreeCrossingsMap);
                var frozen=NavigationAdmission.ExactProfileBinding(active.Navigation);store.ResetRelease();Assert.Zero(store.Active.Navigation.GroupCorridorMaximumExcessPercent);Assert.AreNotEqual(frozen,NavigationAdmission.ExactProfileBinding(store.Active.Navigation));Assert.AreSame(active,runtime.Latest.ActiveProfile);Assert.AreEqual(frozen,NavigationAdmission.ExactProfileBinding(runtime.Latest.ActiveProfile.Navigation),"store changes never rebind current match");
                // A normal lobby starts with an Explorer. Obtain the two Tank
                // members through unchanged build/production ingress, not spawn edits.
                var home=runtime.Latest.Buildings.Single(b=>b.Owner==PlayableOwner.Player&&b.Kind==PlayableBuildingKind.Headquarters);var slot=active.AuthoredMap.Sites(active).Single(s=>s.Id==home.SiteId).Slots[0];
                Assert.True(runtime.TrySubmit(new PlayableCommand(runtime.Generation,++sequence,runtime.Latest.OwnerId,PlayableCommandKind.BuildAt,Array.Empty<int>(),slot.Position,siteId:home.SiteId,slotId:slot.Id,buildingKind:PlayableBuildingKind.Factory,parentId:home.Id)).Accepted);
                yield return Until(()=>runtime.Latest.Buildings.Any(b=>b.Owner==PlayableOwner.Player&&b.Kind==PlayableBuildingKind.Factory&&b.Phase==ConstructionPhase.Ready),"normal factory ready");var factory=runtime.Latest.Buildings.Single(b=>b.Owner==PlayableOwner.Player&&b.Kind==PlayableBuildingKind.Factory);
                for(int i=1;i<=2;i++){yield return Until(()=>runtime.Latest.Credits>=active.TankCreditCost,"normal earned production credits");Assert.True(runtime.TrySubmit(new PlayableCommand(runtime.Generation,++sequence,runtime.Latest.OwnerId,PlayableCommandKind.QueueTank,new[]{factory.Id})).Accepted);int count=i;yield return Until(()=>runtime.Latest.Entities.Count(e=>e.Owner==PlayableOwner.Player&&e.Kind==PlayableEntityKind.Tank)>=count,"normal tank production "+i);}
                tanks=runtime.Latest.Entities.Where(e=>e.Owner==PlayableOwner.Player&&e.Kind==PlayableEntityKind.Tank).Select(e=>e.Id).OrderBy(x=>x).Take(2).ToArray();groupSequence=++sequence;hook.SetValue(null,Delegate.CreateDelegate(hook.FieldType,observe.Target,observe.Method));
                Assert.True(runtime.TrySubmit(new PlayableCommand(runtime.Generation,groupSequence,runtime.Latest.OwnerId,PlayableCommandKind.Move,tanks,new NavPoint(36,46))).Accepted);yield return Until(()=>{lock(gate)return seen;},"first actual normal group request policy");lock(gate)Assert.True(policy,"initial actual group request must select mode96");Assert.AreEqual(frozen,NavigationAdmission.ExactProfileBinding(runtime.Latest.ActiveProfile.Navigation));stage="activation-assertions-passed";
            }finally{
                Entry("finally-before-cleanup",true);try{
                hook.SetValue(null,previous);runtime=runtime??(app==null?null:Get<PlayableRuntime>("runtime"));service=service??(app==null?null:Get<UnityHostRouteService>("routeService"));
                var worker=service==null?null:(CertifiedRouteWorker)typeof(UnityHostRouteService).GetField("worker",Hidden).GetValue(service);
                if(runtime!=null){domain=typeof(PlayableRuntime).GetField("domain",Hidden).GetValue(runtime);navigation=(NavigationSession)domain.GetType().GetProperty("Navigation",Hidden).GetValue(domain);runtime.Dispose();}service?.Dispose();if(runtime!=null)Assert.True(SpinWait.SpinUntil(()=>runtime.IsStopped&&(worker==null||worker.IsStopped),5000));if(host!=null)UnityEngine.Object.DestroyImmediate(host);
                if(navigation!=null){navigation.ReleaseCommittedHostRoot();domain.GetType().GetMethod("DestroyOwnedNavigation",Hidden).Invoke(domain,null);census=new{navigation.PhysicalHostBudget.PhysicalBytes,navigation.PhysicalHostBudget.HeldBytes,navigation.PhysicalHostBudget.ActiveScopes};}
                }finally{
                    try{if(ownedMouse?.added==true)InputSystem.RemoveDevice(ownedMouse);}finally{if(ownedKeyboard?.added==true)InputSystem.RemoveDevice(ownedKeyboard);}
                    var report=new{stage,preparationQueued,runtimeEntries,seen,policy,tanks,groupSequence,tick=runtime?.Latest?.Tick,profile=runtime?.Latest?.ActiveProfile?.ProfileId,profileVersion=runtime?.Latest?.ActiveProfile?.DisplayName,originalInventory,FinalInventory=Inventory(),OwnedKeyboardRemoved=ownedKeyboard?.added!=true,OwnedMouseRemoved=ownedMouse?.added!=true,entryStates=entryStates.ToArray(),artifactErrors=artifactErrors.ToArray(),records=records.ToArray(),census};TestContext.WriteLine("A2_NORMAL_ACTIVATION "+JsonConvert.SerializeObject(report));var output=Environment.GetEnvironmentVariable("SPACEWARS_ACTIVATION_OUTPUT");if(!string.IsNullOrWhiteSpace(output)){Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,"activation.json"),JsonConvert.SerializeObject(report,Formatting.Indented));}
                }
                if(navigation!=null){Assert.Zero(navigation.PhysicalHostBudget.PhysicalBytes);Assert.Zero(navigation.PhysicalHostBudget.HeldBytes);Assert.Zero(navigation.PhysicalHostBudget.ActiveScopes);}
            }
        }
    }
}
