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

namespace Spacewars.Tests.PlayMode
{
    public sealed class RelayTowerUiTests
    {
        [UnityTest] public IEnumerator ResourceGraphContainsCapturedRelayAndLossWithAntennaGlyph()
        {
            const BindingFlags f=BindingFlags.Instance|BindingFlags.NonPublic;
            var profile=PlayableProfile.ThreeCrossingsDefault;var domain=new PlayableDomain(profile,915,false);
            object Call(string name,params object[] args)=>typeof(PlayableDomain).GetMethod(name,f).Invoke(domain,args);
            GameObject root=null;PanelSettings panel=null;RenderTexture target=null;
            try
            {
                var site=domain.Snapshot(1,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,71).Sites.Single(s=>s.Site.Id==7).Site;
                int own=(int)Call("SpawnUnit",new NavPoint(site.Position.X+2.5,site.Position.Z),PlayableOwner.Player,PlayableEntityKind.Explorer);
                for(int i=0;i<210;i++)Call("Step",1d/30);
                Call("Damage",own,100000);
                Call("SpawnUnit",new NavPoint(site.Position.X-2.5,site.Position.Z),PlayableOwner.Enemy,PlayableEntityKind.Explorer);
                for(int i=0;i<390;i++)Call("Step",1d/30);
                Call("FinishManually");var result=domain.Result;
                Assert.AreEqual(2,result.Facts.Count(v=>v.Kind==MatchFactKind.RelayCaptured));Assert.AreEqual(1,result.Facts.Count(v=>v.Kind==MatchFactKind.RelayNeutralized));
                root=new GameObject("Relay result graph");var doc=root.AddComponent<UIDocument>();panel=ScriptableObject.CreateInstance<PanelSettings>();panel.themeStyleSheet=Resources.Load<ThemeStyleSheet>("FoundationTheme");panel.scaleMode=PanelScaleMode.ConstantPixelSize;
                target=new RenderTexture(1400,900,24);target.Create();panel.targetTexture=target;doc.panelSettings=panel;
                var host=doc.rootVisualElement;host.style.unityFont=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");OrbitalTheme.Install(host);
                var ui=new PostMatchResultsView(result,()=>{},()=>{},()=>{});host.Add(ui.Root);ui.ShowTab(1);
                for(int i=0;i<8;i++)yield return null;
                var graph=ui.Root.Q<VisualElement>("result-graph-0");Assert.NotNull(graph);
                var relayIcons=ui.Root.Query<OrbitalGlyph>().ToList().Where(g=>g.Kind=="relay-tower").ToArray();Assert.IsNotEmpty(relayIcons);
                foreach(var icon in relayIcons){Assert.NotNull(icon.Q<ModelPortrait>("glyph-model").Texture,"same model portrait pipeline as other buildings");Assert.NotNull(DockArtwork.TextureFor("relay-tower-outline"));}
                var graphEvent=typeof(PostMatchResultsView).GetMethod("GraphEvent",f);
                foreach(var fact in result.Facts.Where(v=>v.Kind==MatchFactKind.RelayCaptured||v.Kind==MatchFactKind.RelayNeutralized))Assert.True((bool)graphEvent.Invoke(ui,new object[]{0,fact}));
                string output=Environment.GetEnvironmentVariable("RELAY_UI_EVIDENCE")??Path.GetFullPath("../.local/relay-integration/ui");Directory.CreateDirectory(output);
                var old=RenderTexture.active;RenderTexture.active=target;var image=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);
                try{image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();Assert.Greater(image.GetPixels32().Where((_,i)=>i%173==0).Select(c=>(int)c.r<<16|(int)c.g<<8|c.b).Distinct().Count(),8);File.WriteAllBytes(Path.Combine(output,"relay-resource-graph.png"),image.EncodeToPNG());}
                finally{RenderTexture.active=old;UnityEngine.Object.DestroyImmediate(image);}
                var prefab=Resources.Load<GameObject>("StyleA/relay-tower");var model=UnityEngine.Object.Instantiate(prefab);try
                {var motion=model.GetComponent<RelayTowerPresentation>();motion.Sample(Color.cyan,2);float angle=motion.YawDegrees;motion.Sample(null,2);Assert.AreEqual(angle,motion.YawDegrees);motion.Sample(Color.cyan,3);Assert.Greater(motion.YawDegrees,angle);}
                finally{UnityEngine.Object.DestroyImmediate(model);}
            }
            finally
            {
                if(panel)panel.targetTexture=null;if(target){target.Release();UnityEngine.Object.DestroyImmediate(target);}if(root)UnityEngine.Object.DestroyImmediate(root);if(panel)UnityEngine.Object.DestroyImmediate(panel);
                domain.Navigation.ReleaseCommittedHostRoot();domain.DestroyOwnedNavigation();
            }
        }
    }
}
