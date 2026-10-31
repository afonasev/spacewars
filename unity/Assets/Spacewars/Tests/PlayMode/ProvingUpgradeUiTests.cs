using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Presentation;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object=UnityEngine.Object;

public sealed class ProvingUpgradeUiTests
{
    const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
    [UnityTest] public IEnumerator BothSidesExposeEveryNewResearchAndLocalCenterUpgrade()
    {
        var setup=new ProvingGroundSetup(PlayableProfile.Default);
        for(int side=0;side<2;side++){
            var site=setup.Profile.AuthoredMap.Sites(setup.Profile)[side];
            Assert.IsNull(setup.PlaceBuilding(side,PlayableBuildingKind.Factory,site.Slots[0].Position));
            Assert.IsNull(setup.PlaceBuilding(side,PlayableBuildingKind.ScientificCenter,site.Slots[1].Position));
        }
        var runtime=new PlayableRuntime(setup.Configuration(),105,startPaused:true);var host=new GameObject("Proving upgrades UI review");var hud=host.AddComponent<PlayableBootstrap>();hud.enabled=false;
        var type=typeof(PlayableBootstrap);void Set(string n,object v)=>type.GetField(n,F).SetValue(hud,v);T Get<T>(string n)=>(T)type.GetField(n,F).GetValue(hud);object Call(string n,params object[] a)=>type.GetMethods(F).Single(m=>m.Name==n&&m.GetParameters().Length==a.Length).Invoke(hud,a);
        RenderTexture target=null;
        try{
            float deadline=Time.realtimeSinceStartup+30;
            while((!runtime.IsPrepared||runtime.Latest==null)&&Time.realtimeSinceStartup<deadline)yield return null;
            Assert.True(runtime.IsPrepared);Assert.NotNull(runtime.Latest);
            Set("profile",setup.Profile);Call("CreateHud");Set("runtime",runtime);Set("provingGround",setup);Set("view",runtime.Latest);
            var root=Get<VisualElement>("root");var panel=host.GetComponent<UIDocument>().panelSettings;target=new RenderTexture(1600,1000,24);target.Create();panel.targetTexture=target;root.style.width=1600/panel.scale;root.style.height=1000/panel.scale;root.style.backgroundColor=OrbitalTheme.Background;
            var tabs=Get<string[]>("provingTabs");tabs[0]=tabs[1]="Исследования";Call("CreateProvingGroundUi");yield return null;yield return null;
            // ConfigurePanel applies the target-texture scale on the first layout event.
            root.style.width=1600/panel.scale;root.style.height=1000/panel.scale;yield return null;yield return null;
            foreach(int side in new[]{0,1}){
                foreach(var kind in new[]{PlayableResearchKind.ReinforcedConcrete,PlayableResearchKind.AdvancedReinforcedConcrete,PlayableResearchKind.ImprovedWeaponSystems,PlayableResearchKind.AdvancedWeaponSystems,PlayableResearchKind.UltimateWeaponSystems,PlayableResearchKind.ImprovedDefensiveSystems,PlayableResearchKind.AdvancedDefensiveSystems,PlayableResearchKind.UltimateDefensiveSystems}){
                    Assert.NotNull(root.Q<Toggle>("proving-research-"+side+"-"+kind));Assert.NotNull(root.Q<Label>("proving-research-label-"+side+"-"+kind));
                    var bounds=root.Q<Toggle>("proving-research-"+side+"-"+kind).worldBound;Assert.GreaterOrEqual(bounds.xMin,0);Assert.LessOrEqual(bounds.xMax,1600/panel.scale+.5f);
                }
                string owner=side==0?"player-1":"enemy-1";var view=runtime.ParticipantView(owner);int center=view.Buildings.Single(b=>(int)b.Owner==side&&b.Kind==PlayableBuildingKind.ScientificCenter).Id;
                Assert.True(OfflinePadWorldActions.Building(view,setup.Profile,center,freeEconomy:true).Any(a=>a.Command?.Kind==PlayableCommandKind.QueueResearch&&a.Command.Research==PlayableResearchKind.ScientificCenterUpgrade),"local center upgrade action on both sides");
                var row=root.Q<VisualElement>("proving-research-row-"+side+"-AdvancedReinforcedConcrete");root.Q<VisualElement>("proving-side-"+side).Q<ScrollView>().ScrollTo(row);
            }
            yield return null;yield return null;
            var old=RenderTexture.active;RenderTexture.active=target;var pixels=new Texture2D(1600,1000,TextureFormat.RGBA32,false);pixels.ReadPixels(new Rect(0,0,1600,1000),0,0);pixels.Apply();RenderTexture.active=old;
            var output=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.local/qa/concrete-levels"));Directory.CreateDirectory(output);File.WriteAllBytes(Path.Combine(output,"proving-both-sides.png"),pixels.EncodeToPNG());Object.Destroy(pixels);
        }finally{runtime.RequestStop();Set("runtime",null);if(target){target.Release();Object.Destroy(target);}Object.Destroy(host);}
    }
}
