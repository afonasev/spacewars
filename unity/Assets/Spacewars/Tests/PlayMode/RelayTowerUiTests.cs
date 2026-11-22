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
        [UnityTest] public IEnumerator ResourceGraphShowsRelayLossOnlyForItsFormerOwnerInSoloView()
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
                var relayIcons=ui.Root.Query<OrbitalGlyph>().ToList().Where(g=>g.Kind=="relay-tower").ToArray();
                Assert.AreEqual(2,relayIcons.Length,"Each completed capture has one marker across all graphs.");
                foreach(var capture in result.Facts.Where(v=>v.Kind==MatchFactKind.RelayCaptured))
                {
                    var marker=graph.Q("result-event-stack-"+capture.Sequence);
                    Assert.NotNull(marker);
                    var icon=marker.Q<OrbitalGlyph>();
                    Assert.AreEqual("relay-tower",icon.Kind);
                    Assert.NotNull(icon.Q<ModelPortrait>("glyph-model").Texture,"same model portrait pipeline as other buildings");
                    Assert.NotNull(DockArtwork.TextureFor("relay-tower-outline"));
                }
                var loss=result.Facts.Single(v=>v.Kind==MatchFactKind.RelayNeutralized);
                Assert.IsNull(graph.Q("result-event-stack-"+loss.Sequence));
                string output=Environment.GetEnvironmentVariable("RELAY_UI_EVIDENCE")??Path.GetFullPath("../.local/relay-integration/ui");
                Directory.CreateDirectory(output);
                void Capture(string filename)
                {
                    var previous=RenderTexture.active;
                    RenderTexture.active=target;
                    var image=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);
                    try
                    {
                        image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);
                        image.Apply();
                        Assert.Greater(image.GetPixels32().Where((_,i)=>i%173==0).Select(c=>(int)c.r<<16|(int)c.g<<8|c.b).Distinct().Count(),8);
                        File.WriteAllBytes(Path.Combine(output,filename),image.EncodeToPNG());
                    }
                    finally{RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(image);}
                }
                Capture("relay-resource-graph.png");

                var formerOwner=result.Players.Single(p=>p.Owner==loss.Owner);
                var otherPlayer=result.Players.Single(p=>p.Owner!=loss.Owner);
                var formerFilter=ui.Root.Q<Toggle>("result-filter-"+formerOwner.Slot);
                var otherFilter=ui.Root.Q<Toggle>("result-filter-"+otherPlayer.Slot);
                var formerCapture=result.Facts.Single(v=>v.Kind==MatchFactKind.RelayCaptured&&v.Owner==formerOwner.Owner);
                var otherCapture=result.Facts.Single(v=>v.Kind==MatchFactKind.RelayCaptured&&v.Owner==otherPlayer.Owner);
                otherFilter.value=false;
                var lostMarker=graph.Q("result-event-stack-"+loss.Sequence);
                Assert.NotNull(lostMarker?.Q("result-removal-cross"));
                Assert.IsNull(lostMarker.Q("result-event-slash"));
                Assert.NotNull(graph.Q("result-event-stack-"+formerCapture.Sequence));
                Assert.IsNull(graph.Q("result-event-stack-"+otherCapture.Sequence));
                for(int i=0;i<3;i++)yield return null;
                Capture("relay-resource-graph-single-player.png");

                var hideEvents=ui.Root.Q<Toggle>("result-hide-events");
                hideEvents.value=true;
                Assert.IsNull(graph.Q("result-event-stack-"+loss.Sequence));
                hideEvents.value=false;
                Assert.NotNull(graph.Q("result-event-stack-"+loss.Sequence));
                formerFilter.value=false;
                Assert.IsNull(graph.Q("result-event-stack-"+loss.Sequence));
                otherFilter.value=true;
                Assert.IsNull(graph.Q("result-event-stack-"+loss.Sequence));
                Assert.NotNull(graph.Q("result-event-stack-"+otherCapture.Sequence));
                formerFilter.value=true;
                Assert.IsNull(graph.Q("result-event-stack-"+loss.Sequence));
                Assert.NotNull(graph.Q("result-event-stack-"+formerCapture.Sequence));

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
