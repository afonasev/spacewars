using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Spacewars.Input.Tests
{
    public sealed partial class OrdinaryLocalPadTests
    {
        [UnityTest,Timeout(150000)] public IEnumerator RelayCrossingsMenuLoadAndRepeatUseRealPreparedWorld()
        {
            string previousCache=PreparedMapCache.DirectoryOverride;
            PreparedMapCache.DirectoryOverride=Path.GetFullPath("../.local/relay-integration/menu-initial-cache-"+Guid.NewGuid().ToString("N"));
            bool requirePrepared=Environment.GetEnvironmentVariable("RELAY_REQUIRE_PREPARED")=="1";
            try{
            var keyboard=InputSystem.AddDevice<Keyboard>();var mouse=InputSystem.AddDevice<Mouse>();keyboard.MakeCurrent();mouse.MakeCurrent();pads=Array.Empty<Gamepad>();
            host=new GameObject("Relay ordinary menu load");app=host.AddComponent<PlayableBootstrap>();app.enabled=false;Put(app,"profile",PlayableProfile.ThreeCrossingsDefault);Call(app,"CreateWorld");Call(app,"CreateHud");Call(app,"CreateSpectatorHud");Put(app,"input",host.AddComponent<PlayableInput>());Call(app,"BindLocalKeyboardInput");Call(app,"CreateLobby");Call(app,"ShowLobby");
            var choice=Get<VisualElement>(app,"lobbyScreen").Q<DropdownField>("lobby-map-choice");choice.value=choice.choices.Single(v=>v.Contains("Переправа"));
            var draft=Get<NativeLobbyConfiguration>(app,"lobbySetup");draft.HasExplicitSeed=true;draft.ExplicitSeed=7112026;
            for(int i=0;i<draft.Participants.Count;i++){draft.Participants[i].Human=false;draft.Participants[i].Team=i<draft.Participants.Count/2?1:2;}
            string output=Path.GetFullPath("../.local/relay-integration/ui");Directory.CreateDirectory(output);double[] times=new double[2];
            // CreateWorld above prepares the initial diagnostic lobby world. The
            // actual menu compiles its named new-match profile; measure and assert
            // only that launch/reload against a fresh, separately owned cache.
            PreparedMapCache.DirectoryOverride=Path.GetFullPath("../.local/relay-integration/menu-cache-"+Guid.NewGuid().ToString("N"));
            for(int pass=0;pass<2;pass++)
            {
                var timer=System.Diagnostics.Stopwatch.StartNew();Call(app,"RebuildRoster");Call(app,"LaunchLobbyMatch");yield return Wait(()=>Runtime!=null&&!Get<bool>(app,"preparing"),"relay menu load "+pass);
                Call(app,"UpdateFrame");var world=Get<PlayableWorld>(app,"world");var camera=Get<Camera>(app,"cameraView");camera.Render();timer.Stop();times[pass]=timer.Elapsed.TotalMilliseconds;
                bool worldHit=world.PreparationCacheHit,mapHit=Get<PlayableMapTerrain>(app,"mapTerrain").PreparationCacheHit;
                File.AppendAllText(Path.Combine(output,requirePrepared?"menu-prepared.jsonl":"menu-before-bake.jsonl"),"{\"pass\":"+pass+",\"menu_to_frame_ms\":"+times[pass].ToString(System.Globalization.CultureInfo.InvariantCulture)+",\"world_cache_hit\":"+worldHit.ToString().ToLowerInvariant()+",\"minimap_cache_hit\":"+mapHit.ToString().ToLowerInvariant()+"}\n");
                if(requirePrepared){Assert.True(worldHit,"prepared environment");Assert.True(mapHit,"prepared minimap");Assert.False(Directory.Exists(PreparedMapCache.DirectoryOverride)&&Directory.GetFiles(PreparedMapCache.DirectoryOverride,"*ThreeCrossings*").Length>0,"packaged defaults must load without local regeneration");}
                Assert.AreEqual(2,world.GraphicsRoot.GetComponentsInChildren<RelayTowerPresentation>().Length,"spectator sees both permanent relays");
                Runtime.RequestPause(true);yield return Wait(()=>Runtime.Latest.Paused,"freeze map inspection");Call(app,"UpdateFrame");
                var marks=(System.Collections.Generic.IReadOnlyList<PlayableMapMark>)typeof(PlayableMapSurface).GetField("marks",F).GetValue(Get<PlayableMapSurface>(app,"compactMap"));Assert.AreEqual(2,marks.Count(m=>m.Kind==PlayableBuildingKind.RelayTower));
                if(pass==0)
                {
                    void SpectatorBounds(){
                        // Overview intentionally hides the human credit label. Use
                        // the spectator HUD contract, preserving human HUD checks.
                        Assert.True(Get<bool>(app,"spectatorMode"));Assert.AreEqual(DisplayStyle.None,Get<Label>(app,"creditsLabel").style.display.value);
                        var bounds=Get<VisualElement>(app,"root").worldBound;var summary=Get<VisualElement>(app,"spectatorPanel");
                        Assert.AreEqual(DisplayStyle.Flex,summary.style.display.value);Inside(bounds,summary.worldBound,"spectator summary");
                        Assert.Greater(summary.worldBound.width,100);Assert.Greater(summary.worldBound.height,40);Inside(bounds,Get<PlayableMapSurface>(app,"compactMap").worldBound,"relay minimap");
                    }
                    camera.orthographicSize=9;camera.transform.position=new Vector3(-32,13,-15);camera.transform.LookAt(new Vector3(-40,1.5f,0));
                    var previousEvidence=Environment.GetEnvironmentVariable("ORDINARY_PAD_EVIDENCE");Environment.SetEnvironmentVariable("ORDINARY_PAD_EVIDENCE",output);try{yield return Capture("relay-ordinary-hud",context:()=>{camera.orthographicSize=9;camera.transform.position=new Vector3(-32,13,-15);camera.transform.LookAt(new Vector3(-40,1.5f,0));},captureChecks:SpectatorBounds);
                    camera.orthographicSize=70;camera.transform.position=new Vector3(0,145,-90);camera.transform.LookAt(Vector3.zero);yield return Capture("relay-map-overview",context:()=>{camera.orthographicSize=70;camera.transform.position=new Vector3(0,145,-90);camera.transform.LookAt(Vector3.zero);},captureChecks:SpectatorBounds);
                    }finally{Environment.SetEnvironmentVariable("ORDINARY_PAD_EVIDENCE",previousEvidence);}
                    Call(app,"ReturnToLobby");yield return Wait(()=>Runtime==null&&Get<bool>(app,"inLobby"),"return to lobby for repeat load");
                }
            }
            File.WriteAllText(Path.Combine(output,"menu-loading.json"),"{\"first_editor_process_menu_to_frame_ms\":"+times[0].ToString(System.Globalization.CultureInfo.InvariantCulture)+",\"repeat_menu_to_frame_ms\":"+times[1].ToString(System.Globalization.CultureInfo.InvariantCulture)+",\"seed\":7112026,\"mode\":\"Editor exclusive; existing cache, no Player benchmark\"}");
            }finally{PreparedMapCache.DirectoryOverride=previousCache;}
        }
    }
}
