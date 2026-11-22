using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Spacewars.Input.Tests
{
    public sealed partial class OrdinaryLocalPadTests
    {
        [Test] public void NuclearWarningFillDoesNotBridgeSeparateVisionSources()
        {
            var style=NuclearWarningProfile.Load();style.Validate();
            var sources=new[]{new VisionSource(new NavPoint(-2,0),1),new VisionSource(new NavPoint(2,0),1)};
            Assert.False(NuclearWarningArtwork.VisibleTriangle(new NavPoint(-2,0),new NavPoint(2,0),new NavPoint(2,.5),sources),
                "A triangle spanning two visible islands must not paint the hidden gap between them.");
            var mesh=NuclearWarningArtwork.BuildFill(new NavPoint(0,0),4,sources);
            Assert.Greater(mesh.Indices.Length,0,"Visible islands still receive the warning fill.");
            for(int i=0;i<mesh.Indices.Length;i+=3)
            {
                var a=mesh.Points[mesh.Indices[i]];var b=mesh.Points[mesh.Indices[i+1]];var c=mesh.Points[mesh.Indices[i+2]];
                Assert.True(sources.Any(source=>new[]{a,b,c}.All(point=>
                    (point.X-source.Position.X)*(point.X-source.Position.X)+(point.Z-source.Position.Z)*(point.Z-source.Position.Z)<=source.Radius*source.Radius)));
            }
            Assert.IsEmpty(NuclearWarningArtwork.BuildFill(new NavPoint(0,0),4,Array.Empty<VisionSource>()).Indices);
        }
        [UnityTest] public IEnumerator NuclearLaunchShowsSoftWarningContractionAndDescent()
        {
            yield return StartOrdinary(1);var seat=Seats[0];var snapshot=Get<PlayableSnapshot>(seat,"view");
            var profile=Get<PlayableProfile>(seat,"profile");var terms=profile.CopyData();
            var target=snapshot.Entities.First(unit=>unit.Owner==snapshot.Owner).Position;
            var opponent=(PlayableOwner)(snapshot.Participants.First(participant=>participant.Team!=snapshot.Team).LogicalPlayer-1);
            Runtime.RequestPause(true);yield return Wait(()=>Runtime.Latest.Paused,"pause nuclear setup");
            var domain=(PlayableDomain)typeof(PlayableRuntime).GetField("domain",F).GetValue(Runtime);var type=domain.GetType();
            type.GetMethod("CreditOrbital",F).Invoke(domain,new object[]{(PlayableOwner?)snapshot.Owner,opponent,
                terms.orbitalThreshold1+terms.orbitalThreshold2+terms.orbitalThreshold3+terms.orbitalThreshold4+terms.orbitalThreshold5});
            // Ordinary matches use the current stock progression; legacy diagnostic profiles retain five choices.
            var choices=terms.orbitalProgressionVersion==OrbitalAbilities.CompactProgressionVersion
                ?new[]{1,3,4,OrbitalAbilities.NuclearStrike}:new[]{1,2,3,4,OrbitalAbilities.NuclearStrike};
            foreach(int ability in choices)
                Assert.AreEqual(PlayableCommandStatus.Applied,type.GetMethod("ChooseOrbital",F).Invoke(domain,new object[]{snapshot.Owner,ability}),
                    $"ability {ability}, progression {terms.orbitalProgressionVersion}");
            Runtime.RequestPause(false);
            yield return Wait(()=>Get<PlayableSnapshot>(seat,"view").Orbital.Abilities.Any(a=>a.Id==OrbitalAbilities.NuclearStrike),"nuclear unlocked");
            Call(seat,"FocusScanPoint",target);Call(seat,"BeginOrbitalTarget",true,OrbitalAbilities.NuclearStrike);PadCursor(seat,target);
            Call(seat,"UpdateScanPresentation");Assert.True(Get<bool>(seat,"abilityTargetValid"));Call(seat,"ConfirmScan",target,false);
            OrbitalNuclearSnapshot Strike()=>Get<PlayableSnapshot>(seat,"view").Orbital.NuclearStrikes.SingleOrDefault();
            yield return Wait(()=>Strike()?.Phase==OrbitalNuclearPhase.Warning&&Strike().Progress>.1,"early warning");
            Runtime.RequestPause(true);yield return Wait(()=>Runtime.Latest.Paused,"freeze early warning");
            double early=Strike().Progress;Assert.AreEqual(terms.orbitalNuclearRadius,Strike().Radius);
            TestContext.WriteLine($"Loaded {snapshot.ProfileId} v{snapshot.ProfileRevision}; Warning visual nuclear-warning-soft-v1 r1; seed 19092026; radius {Strike().Radius}; early {early}");
            yield return Capture("nuclear-warning-early",captureWidth:1280,captureHeight:800,captureChecks:()=>Assert.AreEqual(OrbitalNuclearPhase.Warning,Strike().Phase));
            var camera=Get<Camera>(seat,"cameraView");var rotation=camera.transform.rotation;
            camera.transform.RotateAround(Get<PlayableWorld>(seat,"world").Point(target),Vector3.up,70);
            yield return Capture("nuclear-warning-rotated",captureWidth:1280,captureHeight:800,captureChecks:()=>Assert.AreEqual(early,Strike().Progress));
            camera.transform.RotateAround(Get<PlayableWorld>(seat,"world").Point(target),Vector3.up,-70);camera.transform.rotation=rotation;
            Runtime.RequestPause(false);yield return Wait(()=>Strike()?.Phase==OrbitalNuclearPhase.Warning&&Strike().Progress>.65,"late warning");
            Runtime.RequestPause(true);yield return Wait(()=>Runtime.Latest.Paused,"freeze late warning");Assert.Greater(Strike().Progress,early);
            yield return Capture("nuclear-warning-late",captureWidth:1280,captureHeight:800,captureChecks:()=>Assert.AreEqual(OrbitalNuclearPhase.Warning,Strike().Phase));
            Runtime.RequestPause(false);yield return Wait(()=>Strike()?.Phase==OrbitalNuclearPhase.Descent,"rocket descent");
            Runtime.RequestPause(true);yield return Wait(()=>Runtime.Latest.Paused,"freeze rocket descent");
            yield return Capture("nuclear-warning-descent",captureWidth:1280,captureHeight:800,captureChecks:()=>{
                Assert.AreEqual(OrbitalNuclearPhase.Descent,Strike().Phase);Assert.AreEqual(terms.orbitalNuclearRadius,Strike().Radius);
            });
            Runtime.RequestStop();yield return Wait(()=>Runtime.IsStopped,"nuclear warning stop");
        }
        [Test] public void NuclearAimingStyleHasDistinctValidityColorsAndVisualIdentity()
        {
            var style=NuclearTargetingProfile.Load();style.Validate();
            Assert.AreEqual("nuclear-aim-amber-v1",style.id);Assert.AreEqual(1,style.revision);
            Assert.Greater(style.validColor.g,style.invalidColor.g,"Amber legal aim must not look like red denial");
            Assert.Greater(style.validColor.r,style.validColor.b);Assert.Greater(style.validColor.g,style.validColor.b);
            Assert.Less(style.fillOpacity,.1f,"The radius must keep terrain and units readable");
        }
        [UnityTest] public IEnumerator NuclearAimingDistinguishesLegalAndUnseenTargetsOnWorldAndTab()
        {
            yield return StartOrdinary(1);var seat=Seats[0];var view=Get<PlayableSnapshot>(seat,"view");
            var legal=view.Entities.First(unit=>unit.Owner==view.Owner).Position;
            var unseen=view.PublicSites.First(site=>!view.Vision.IsVisible(site.Position)).Position;
            Call(seat,"BeginOrbitalTarget",true,OrbitalAbilities.NuclearStrike);
            var root=Get<VisualElement>(seat,"root");
            void Aim(NavPoint point,bool valid)
            {
                PadCursor(seat,point);Call(seat,"UpdateScanPresentation");
                Assert.AreEqual(valid,Get<bool>(seat,"abilityTargetValid"));
                Assert.That(root.Q<Label>("ability-target-title").text,Does.Contain("Ядерный удар"));
                Assert.True(Get<PlayableMapSurface>(seat,"compactMap").NuclearPreview);
            }
            Call(seat,"FocusScanPoint",legal);Aim(legal,true);
            yield return Capture("nuclear-aim-valid",()=>Aim(legal,true),1280,800,captureChecks:()=>Assert.True(Get<bool>(seat,"abilityTargetValid")));
            Call(seat,"FocusScanPoint",unseen);Aim(unseen,false);
            StringAssert.Contains("обзоре",root.Q<Label>("ability-target-status").text);
            yield return Capture("nuclear-aim-invalid",()=>Aim(unseen,false),1280,800,captureChecks:()=>Assert.False(Get<bool>(seat,"abilityTargetValid")));
            long sequence=Get<long>(seat,"sequence");Call(seat,"ConfirmScan",unseen,false);
            Assert.AreEqual(sequence,Get<long>(seat,"sequence"),"Forbidden targeting must not submit a strike");
            Call(seat,"ToggleMap");Aim(legal,true);
            yield return Capture("nuclear-aim-tab",()=>Aim(legal,true),1280,800,captureChecks:()=>{
                Assert.True(Get<PlayableMapSurface>(seat,"tacticalMap").NuclearPreview);Assert.True(Get<bool>(seat,"abilityTargetValid"));
            });
            Call(seat,"CancelScanTarget");
            Assert.False(Get<PlayableMapSurface>(seat,"compactMap").NuclearPreview);Assert.False(Get<PlayableMapSurface>(seat,"tacticalMap").NuclearPreview);
            Assert.AreEqual(sequence,Get<long>(seat,"sequence"),"Cancelling a preview must not submit a strike");
        }
        [UnityTest] public IEnumerator MouseAbilityHintsStayStableWithoutGamepad()
        {
            yield return StartOrdinary(1,configureRoster:d=>d.Participants[0].DeviceId=0);
            foreach(var pad in UnityEngine.InputSystem.Gamepad.all.ToArray())UnityEngine.InputSystem.InputSystem.RemoveDevice(pad);
            Assert.Zero(UnityEngine.InputSystem.Gamepad.all.Count,"Mouse scenario has no gamepad, including virtual devices.");
            var seat=Seats[0];
            Put(seat,"provingGround",new ProvingGroundSetup(Get<PlayableProfile>(seat,"profile")));
            foreach(int ability in new[]{1,2,OrbitalAbilities.SystemFailure,OrbitalAbilities.ControlHack}){
                Call(seat,"BeginOrbitalTarget",false,ability);Call(seat,"UpdateAbilityTargetingHud");
                var root=Get<VisualElement>(seat,"root");var controls=root.Q("ability-target-controls");
                Assert.AreEqual(DisplayStyle.Flex,controls.style.display.value);
                var glyphs=controls.Query<NativeControllerGlyph>().ToList();Assert.AreEqual(2,glyphs.Count);
                for(int frame=0;frame<3;frame++){
                    // This is the same Refresh invoked by each badge's recurring 80 ms job.
                    foreach(var glyph in glyphs)glyph.Refresh(null);
                    yield return new WaitForSecondsRealtime(.1f);
                    StringAssert.StartsWith("ЛКМ — ",root.Q<Label>("ability-target-apply").text);
                    Assert.AreEqual("ПКМ — отменить",root.Q<Label>("ability-target-cancel").text);
                    foreach(var glyph in glyphs)Assert.AreEqual(DisplayStyle.None,glyph.style.display.value,"Scheduled badge refresh must retain mouse visibility.");
                }
                Call(seat,"CancelScanTarget");
            }
            Runtime.RequestStop();yield return Wait(()=>Runtime.IsStopped,"mouse targeting stop");
        }
        [UnityTest] public IEnumerator StasisPreviewAndCastUseTheApplyingViewport()
        {
            yield return StartOrdinary(2);
            var seat=Seats[0];
            var other=Seats[1];
            var profile=Get<PlayableProfile>(seat,"profile");
            var owner=Get<PlayableSnapshot>(seat,"view").Owner;
            var opponent=Get<PlayableSnapshot>(other,"view").Owner;
            Runtime.RequestPause(true);
            yield return Wait(()=>Runtime.Latest.Paused,"pause stasis fixture");
            var domain=(PlayableDomain)typeof(PlayableRuntime).GetField("domain",F).GetValue(Runtime);
            var type=domain.GetType();
            var data=profile.CopyData();
            type.GetMethod("CreditOrbital",F).Invoke(domain,new object[]{
                (PlayableOwner?)owner,opponent,data.orbitalThreshold1+data.orbitalThreshold2+data.orbitalThreshold3
            });
            foreach(int ability in new[]{1,2,OrbitalAbilities.Stasis})
                Assert.AreEqual(PlayableCommandStatus.Applied,type.GetMethod("ChooseOrbital",F).Invoke(domain,new object[]{owner,ability}));

            Runtime.RequestPause(false);
            yield return Wait(()=>Get<PlayableSnapshot>(seat,"view").Orbital.Abilities.Any(a=>a.Id==OrbitalAbilities.Stasis),"stasis chosen");
            var unit=Get<PlayableSnapshot>(seat,"view").Entities.First(u=>u.Owner==owner);
            var root=Get<VisualElement>(seat,"root");
            var world=Get<PlayableWorld>(seat,"world");
            Call(seat,"FocusScanPoint",unit.Position);
            Get<Camera>(seat,"cameraView").orthographicSize=6;
            Call(seat,"BeginOrbitalTarget",true,OrbitalAbilities.Stasis);

            void Aim()
            {
                PadCursor(seat,unit.Position);
                Call(seat,"UpdateScanPresentation");
                Assert.True(Get<bool>(seat,"abilityTargetValid"));
                StringAssert.Contains("Целей:",root.Q<Label>("ability-target-status").text);
                Assert.False(Get<bool>(other,"scanTargeting"));
            }

            Aim();
            yield return Capture("stasis-targeting",Aim,1280,800,captureChecks:()=>Assert.True(Get<bool>(seat,"abilityTargetValid")));
            Call(seat,"ConfirmScan",unit.Position,false);
            yield return Wait(()=>!Get<bool>(seat,"scanTargeting")&&
                Get<PlayableSnapshot>(seat,"view").Entities.Any(u=>u.Id==unit.Id&&u.StasisRemaining>0),"stasis applied");
            Assert.Greater(Get<PlayableSnapshot>(seat,"view").Orbital.Abilities.Single(a=>a.Id==OrbitalAbilities.Stasis).Remaining,0);
            yield return Wait(()=>world.Actors[unit.Id].Root.Find("Stasis Cylinder")!=null,"cylinder visible on the target");
            yield return Capture("stasis-cast",captureWidth:1280,captureHeight:800,
                captureChecks:()=>Assert.IsNotNull(world.Actors[unit.Id].Root.Find("Stasis Cylinder")));
            Runtime.RequestStop();
            yield return Wait(()=>Runtime.IsStopped,"stasis fixture stop");
        }
        [UnityTest]
        public IEnumerator DirectStasisAimHitsHawkModelAndSparesUnitsBelow()
        {
            yield return StartOrdinary(2,configureRoster:draft=>draft.Participants[0].DeviceId=0);
            var seat=Seats[0];
            var snapshot=Get<PlayableSnapshot>(seat,"view");
            var profile=Get<PlayableProfile>(seat,"profile");
            var ground=snapshot.Entities.First(unit=>unit.Owner==snapshot.Owner);
            var opponent=Get<PlayableSnapshot>(Seats[1],"view").Owner;
            const int stasis=OrbitalAbilities.Stasis;
            Runtime.RequestPause(true);
            yield return Wait(()=>Runtime.Latest.Paused,"pause Hawk aim setup");
            var domain=(PlayableDomain)typeof(PlayableRuntime).GetField("domain",F).GetValue(Runtime);
            var type=domain.GetType();
            var data=profile.CopyData();
            type.GetMethod("CreditOrbital",F).Invoke(domain,new object[]{
                (PlayableOwner?)snapshot.Owner,opponent,data.orbitalThreshold1+data.orbitalThreshold2+data.orbitalThreshold3
            });
            foreach(int ability in new[]{1,2,stasis})
                Assert.AreEqual(PlayableCommandStatus.Applied,type.GetMethod("ChooseOrbital",F).Invoke(domain,new object[]{snapshot.Owner,ability}));
            int hawk=(int)type.GetMethod("SpawnUnit",F).Invoke(domain,new object[]{ground.Position,snapshot.Owner,PlayableEntityKind.Hawk});
            Runtime.RequestPause(false);
            var canOrbital=typeof(PlayableBootstrap).GetProperty("CanOrbital",F);
            yield return Wait(()=>Get<PlayableSnapshot>(seat,"view").Entities.Any(unit=>unit.Id==hawk)&&
                (bool)canOrbital.GetValue(seat),"Hawk visible and seat ready to target");
            var world=Get<PlayableWorld>(seat,"world");
            var camera=Get<Camera>(seat,"cameraView");
            var entity=Get<PlayableSnapshot>(seat,"view").Entities.Single(unit=>unit.Id==hawk);
            var bounds=new object[]{camera,entity,default(Rect)};
            Assert.True((bool)typeof(PlayableWorld).GetMethod("HawkScreenBounds",F).Invoke(world,bounds));
            var cursor=((Rect)bounds[2]).center;
            Assert.AreEqual(hawk,Call(seat,"Pick",cursor,false));
            Assert.AreEqual(0,Call(seat,"MapAt",cursor));
            Assert.False((bool)Call(seat,"OverUi",cursor),"The model is below the pointer on the battlefield.");
            var input=Get<PlayableInput>(seat,"input");
            var mouse=input.AssignedMouse??UnityEngine.InputSystem.Mouse.current;
            Assert.NotNull(mouse);
            Call(seat,"BeginOrbitalTarget",false,stasis);
            UnityEngine.InputSystem.LowLevel.InputState.Change(mouse.position,cursor);
            Assert.AreEqual(cursor,mouse.position.ReadValue());
            Assert.True((bool)canOrbital.GetValue(seat));
            var aim=new object[]{0};
            Assert.IsNotNull(Call(seat,"AbilityCursorPoint",aim));
            Assert.AreEqual(hawk,aim[0]);
            Call(seat,"UpdateScanPresentation");
            Assert.AreEqual(hawk,Get<int>(seat,"abilityHoveredTarget"));
            Assert.True(Get<bool>(seat,"abilityTargetValid"));
            var root=Get<VisualElement>(seat,"root");
            StringAssert.Contains("воздух",root.Q<Label>("ability-target-status").text);
            Call(seat,"ScanPointer",cursor,true,true,false);
            yield return Wait(()=>Get<PlayableSnapshot>(seat,"view").Entities.Any(unit=>unit.Id==hawk&&unit.StasisRemaining>0),
                "direct Hawk cast");
            Assert.Zero(Get<PlayableSnapshot>(seat,"view").Entities.Single(unit=>unit.Id==ground.Id).StasisRemaining);
            Assert.NotNull(Resources.Load<Texture2D>("OrbitalIcons/repair-nanobots"));
            Runtime.RequestStop();
            yield return Wait(()=>Runtime.IsStopped,"Hawk aim test stop");
        }

        [UnityTest] public IEnumerator MineTargetingWarnsBeforeSubmittingNearVisibleEnemy()
        {
            yield return StartOrdinary(2);
            var seat = Seats[0];
            var initial = Get<PlayableSnapshot>(seat,"view");
            var opponent = Get<PlayableSnapshot>(Seats[1],"view").Owner;
            var origin = initial.Entities.First(u=>u.Owner==initial.Owner).Position;
            var profile = Get<PlayableProfile>(seat,"profile");
            Runtime.RequestPause(true);
            yield return Wait(()=>Runtime.Latest.Paused,"pause mine fixture");
            var domain = (PlayableDomain)typeof(PlayableRuntime).GetField("domain",F).GetValue(Runtime);
            var type = domain.GetType();
            var point = Enumerable.Range(0,8).Select(i=>new NavPoint(origin.X+3*Math.Cos(i*Math.PI/4),origin.Z+3*Math.Sin(i*Math.PI/4)))
                .First(p=>profile.AuthoredMap.SupportsFootprint(p,profile.TankCollisionRadius)&&domain.Geometry.IsFree(p,profile.TankCollisionRadius));
            int enemy = (int)type.GetMethod("SpawnUnit",F).Invoke(domain,new object[]{point,opponent,PlayableEntityKind.Tank});
            type.GetMethod("CreditOrbital",F).Invoke(domain,new object[]{(PlayableOwner?)initial.Owner,opponent,250d});
            Assert.AreEqual(PlayableCommandStatus.Applied,type.GetMethod("ChooseOrbital",F).Invoke(domain,new object[]{initial.Owner,2}));
            Runtime.RequestPause(false);
            yield return Wait(()=>Get<PlayableSnapshot>(seat,"view").Entities.Any(u=>u.Id==enemy)&&
                Get<PlayableSnapshot>(seat,"view").Orbital.Abilities.Any(a=>a.Id==2),"visible enemy and mine ability");
            Call(seat,"BeginOrbitalTarget",true,2);
            var root = Get<VisualElement>(seat,"root");
            void Aim()
            {
                var view = Get<PlayableSnapshot>(seat,"view");
                PadCursor(seat,view.Entities.Single(u=>u.Id==enemy).Position);
                Call(seat,"UpdateScanPresentation");
                Assert.False(Get<bool>(seat,"abilityTargetValid"));
                Assert.AreEqual("Слишком близко к врагу",root.Q<Label>("ability-target-status").text);
            }
            Aim();
            long sequence = Get<long>(seat,"sequence");
            Call(seat,"ConfirmScan",Get<NavPoint>(seat,"padCursorGround"),false);
            Assert.AreEqual(sequence,Get<long>(seat,"sequence"));
            Assert.True(Get<bool>(seat,"scanTargeting"));
            yield return Capture("mine-too-close",Aim,1600,1000,captureChecks:()=>Inside(root.worldBound,root.Q("ability-target-header").worldBound,"mine warning"));
            Runtime.RequestStop();
            yield return Wait(()=>Runtime.IsStopped,"mine warning stop");
        }
        [UnityTest]
        public IEnumerator ReinforcementTargetingAndRefundBelongToTheApplyingSeat()
        {
            yield return StartOrdinary(2);
            var seat = Seats[0];
            var other = Seats[1];
            var profile = Get<PlayableProfile>(seat, "profile");
            var first = Get<PlayableSnapshot>(seat, "view").Owner;
            var second = Get<PlayableSnapshot>(other, "view").Owner;
            Runtime.RequestPause(true);
            yield return Wait(()=>Runtime.Latest.Paused,"pause reinforcement UI setup");
            var domain = (PlayableDomain)typeof(PlayableRuntime).GetField("domain", F).GetValue(Runtime);
            var type = domain.GetType();
            var data = profile.CopyData();
            type.GetMethod("CreditOrbital", F).Invoke(domain,new object[] {
                (PlayableOwner?)first,second,data.orbitalThreshold1+data.orbitalThreshold2+data.orbitalThreshold3
            });
            type.GetMethod("AddCredits", F).Invoke(domain,new object[] { first, 1500d });
            foreach(int ability in new[] { 1,2,OrbitalAbilities.Reinforcement })
                Assert.AreEqual(PlayableCommandStatus.Applied,type.GetMethod("ChooseOrbital", F).Invoke(domain,new object[] { first,ability }));
            Runtime.RequestPause(false);
            yield return Wait(()=>Get<PlayableSnapshot>(seat,"view").Orbital.Abilities.Any(a=>a.Id==OrbitalAbilities.Reinforcement),
                "local owner sees reinforcement");
            Call(seat,"BeginReinforcementTarget",true);
            yield return Wait(()=>Get<PlayableSnapshot>(seat,"view").Orbital.ReinforcementQuote.Reserved&&Get<bool>(seat,"scanTargeting"),
                "authority reserves the applying seat's next wave");
            Call(seat,"UpdateAbilityTargetingHud");
            var root=Get<VisualElement>(seat,"root");
            StringAssert.Contains("Призыв подкрепления",root.Q<Label>("ability-target-title").text);
            Assert.False(Get<bool>(other,"scanTargeting"));
            Assert.False(Get<PlayableSnapshot>(other,"view").Orbital.ReinforcementQuote.Reserved);

            var origin=Get<PlayableSnapshot>(seat,"view").Entities.First(unit=>unit.Owner==first).Position;
            bool Fits(NavPoint point)
            {
                var arguments=new object[]{point,false};
                var positions=(NavPoint[])Call(seat,"ReinforcementPreview",arguments);
                Assert.AreEqual(3,positions.Length);
                return (bool)arguments[1];
            }
            Assert.False(Fits(origin),"occupied by an existing unit");
            var free=Enumerable.Range(0,24).Select(i=>new NavPoint(origin.X+3.2*Math.Cos(i*Math.PI/12),
                origin.Z+3.2*Math.Sin(i*Math.PI/12))).First(Fits);
            Call(seat,"FocusScanPoint",free);
            Get<Camera>(seat,"cameraView").orthographicSize=7f;
            var world=Get<PlayableWorld>(seat,"world");
            Transform[] Previews(PlayableWorld targetWorld)=>targetWorld.GraphicsRoot
                .Cast<Transform>().Where(t=>t.name=="Reinforcement Placement Preview"&&t.gameObject.activeSelf).ToArray();

            void Aim(NavPoint point, bool allowed)
            {
                PadCursor(seat,point);
                Call(seat,"UpdateScanPresentation");
                Assert.AreEqual(allowed,Get<bool>(seat,"abilityTargetValid"));
                var models=Previews(world);
                var points=(NavPoint[])Call(seat,"ReinforcementPreview",new object[]{point,false});
                Assert.AreEqual(points.Length,models.Length);
                Assert.IsEmpty(Previews(Get<PlayableWorld>(other,"world")));
                for(int i=0;i<models.Length;i++)
                {
                    Assert.Less(Vector3.Distance(world.Point(points[i]),models[i].position),.001f);
                    Assert.False(models[i].GetComponentsInChildren<Collider>().Any(c=>c.enabled));
                    var renderers=models[i].GetComponentsInChildren<MeshRenderer>().Where(r=>r.enabled).ToArray();
                    Assert.IsNotEmpty(renderers);
                    var block=new MaterialPropertyBlock();
                    foreach(var renderer in renderers)
                    {
                        Assert.IsNotNull(renderer.GetComponent<MeshFilter>()?.sharedMesh);
                        Assert.AreEqual(Get<Camera>(seat,"cameraView").cullingMask &
                            (1<<renderer.gameObject.layer),1<<renderer.gameObject.layer);
                        for(int slot=0;slot<renderer.sharedMaterials.Length;slot++)
                        {
                            var material=renderer.sharedMaterials[slot];
                            Assert.AreEqual(3000,material.renderQueue);
                            renderer.GetPropertyBlock(block,slot);
                            var color=block.GetColor("_BaseColor");
                            Assert.That(color.a,Is.EqualTo(.5f).Within(.001f));
                            Assert.AreEqual(allowed,color.b>color.r);
                        }
                    }
                }
            }

            Aim(free,true);
            yield return Capture("reinforcement-models-valid",()=>Aim(free,true),1280,720,
                captureChecks:()=>Assert.True(Get<bool>(seat,"abilityTargetValid")));
            Aim(origin,false);
            yield return Capture("reinforcement-models-blocked",()=>Aim(origin,false),1280,720,
                captureChecks:()=>Assert.False(Get<bool>(seat,"abilityTargetValid")));
            Aim(free,true);
            Call(seat,"ToggleMap");
            Call(seat,"UpdateScanPresentation");
            Assert.IsEmpty(Previews(world));
            Assert.True(Get<bool>(seat,"scanTargeting"),"opening the tactical map keeps targeting");
            Assert.True(Get<PlayableSnapshot>(seat,"view").Orbital.ReinforcementQuote.Reserved);
            Call(seat,"CancelScanTarget");
            yield return Wait(()=>!Get<PlayableSnapshot>(seat,"view").Orbital.ReinforcementQuote.Reserved,"paid wave refunded");
            Assert.False(Get<bool>(seat,"scanTargeting"));
            Assert.IsEmpty(Previews(world));
            Assert.AreEqual(PlayableEntityKind.Explorer,Get<PlayableSnapshot>(seat,"view").Orbital.ReinforcementQuote.Kind);

            var explorerTexture=Resources.Load<Texture2D>("OrbitalIcons/reinforcement-explorers");
            var tankTexture=Resources.Load<Texture2D>("OrbitalIcons/reinforcement-tanks");
            Assert.NotNull(explorerTexture);
            Assert.NotNull(tankTexture);
            Call(seat,"UpdateOrbitalHud");
            var icon=root.Q("orbital-slot-"+OrbitalAbilities.Reinforcement).Q("ability-artwork");
            Assert.AreSame(explorerTexture,icon.style.backgroundImage.value.texture);
            Call(seat,"BeginReinforcementTarget",true);
            yield return Wait(()=>Get<PlayableSnapshot>(seat,"view").Orbital.ReinforcementQuote.Reserved&&Get<bool>(seat,"scanTargeting"),
                "reserve the same explorer wave after cancellation");
            Call(seat,"ConfirmOrbitalTarget",free,false,0);
            yield return Wait(()=>Get<PlayableSnapshot>(seat,"view").Orbital.ReinforcementQuote.Kind==PlayableEntityKind.Tank,
                "successful reinforcement advances the authority's next wave");
            Call(seat,"UpdateOrbitalHud");
            Assert.AreSame(tankTexture,icon.style.backgroundImage.value.texture);
            Assert.AreEqual(PlayableEntityKind.Explorer,Get<PlayableSnapshot>(other,"view").Orbital.ReinforcementQuote.Kind);
            Runtime.RequestStop();
            yield return Wait(()=>Runtime.IsStopped,"reinforcement UI fixture stop");
        }
        [UnityTest]
        public IEnumerator RepairNanobotsCastFromBothMapsAndShowLocalRepairFeedback()
        {
            yield return StartOrdinary(2,configureRoster:draft=>draft.Participants[0].DeviceId=0);
            const int repair = OrbitalAbilities.RepairNanobots;
            Runtime.RequestPause(true);
            yield return Wait(()=>Runtime.Latest.Paused,"pause repair field setup");
            var domain=(PlayableDomain)typeof(PlayableRuntime).GetField("domain",F).GetValue(Runtime);
            var type=domain.GetType();
            foreach(var seat in Seats)
            {
                var snapshot=Get<PlayableSnapshot>(seat,"view");
                var data=Get<PlayableProfile>(seat,"profile").CopyData();
                var enemy=Seats.Where(candidate=>candidate!=seat).Select(candidate=>Get<PlayableSnapshot>(candidate,"view").Owner).Single();
                type.GetMethod("CreditOrbital",F).Invoke(domain,new object[]{
                    (PlayableOwner?)snapshot.Owner,enemy,data.orbitalThreshold1+data.orbitalThreshold2+data.orbitalThreshold3
                });
                foreach(int ability in new[]{1,2,repair})
                    Assert.AreEqual(PlayableCommandStatus.Applied,type.GetMethod("ChooseOrbital",F).Invoke(domain,new object[]{snapshot.Owner,ability}));
                var unit=snapshot.Entities.First(candidate=>candidate.Owner==snapshot.Owner);
                type.GetMethod("Damage",F).Invoke(domain,new object[]{unit.Id,20});
            }
            Runtime.RequestPause(false);
            yield return Wait(()=>Seats.All(seat=>Get<PlayableSnapshot>(seat,"view").Orbital.Abilities.Any(slot=>slot.Id==repair)),
                "both local seats can cast");
            for(int index=0;index<Seats.Length;index++)
            {
                var seat=Seats[index];
                var other=Seats[1-index];
                var snapshot=Get<PlayableSnapshot>(seat,"view");
                var unit=snapshot.Entities.First(candidate=>candidate.Owner==snapshot.Owner);
                var root=Get<VisualElement>(seat,"root");
                Debug.Log($"Repair Nanobots QA profile {snapshot.ProfileId}@{snapshot.ProfileRevision}, seed {snapshot.Seed}");
                Call(seat,"FocusScanPoint",unit.Position);
                Get<Camera>(seat,"cameraView").orthographicSize=7;
                bool tactical=index==1;
                Call(seat,"BeginOrbitalTarget",tactical,repair);
                if(tactical) Call(seat,"ToggleMap");
                PadCursor(seat,unit.Position);
                var feedbackArgs=new object[]{(NavPoint?)unit.Position,0,false};
                StringAssert.Contains("В зоне раненых: 1",(string)Call(seat,"AbilityTargetFeedback",feedbackArgs));
                Assert.True((bool)feedbackArgs[2]);
                Assert.False(Get<bool>(other,"scanTargeting"));
                var surface=tactical?Get<PlayableMapSurface>(seat,"tacticalMap"):
                    (PlayableMapSurface)seat.GetType().GetProperty("CompactInputMap",F).GetValue(seat);
                Assert.IsNotNull(surface);
                yield return null;
                var local=(Vector2)typeof(PlayableMapSurface).GetMethod("Point",F).Invoke(surface,new object[]{unit.Position});
                var panel=surface.LocalToWorld(local);
                var zero=RuntimePanelUtils.ScreenToPanel(root.panel,Vector2.zero);
                var scale=RuntimePanelUtils.ScreenToPanel(root.panel,new Vector2(Screen.width,Screen.height))-zero;
                var mouse=new Vector2((panel.x-zero.x)*Screen.width/scale.x,
                    Screen.height-(panel.y-zero.y)*Screen.height/scale.y);
                Assert.AreEqual(tactical?2:1,(int)Call(seat,"MapAt",mouse));
                if(tactical)
                {
                    var at=(NavPoint)Call(seat,"MapGround",2,mouse);
                    PadCursor(seat,at);
                    Call(seat,"PollScanTargetPad",pads[index]);
                    Set(pads[index].buttonSouth,1f,queueEventOnly:true);
                    UnityEngine.InputSystem.InputSystem.Update();
                    Call(seat,"PollScanTargetPad",pads[index]);
                    Set(pads[index].buttonSouth,0f,queueEventOnly:true);
                    UnityEngine.InputSystem.InputSystem.Update();
                }
                else
                {
                    Call(seat,"ScanPointer",mouse,true,true,false);
                }
                yield return Wait(()=>!Get<bool>(seat,"scanTargeting")&&
                    Get<PlayableSnapshot>(seat,"view").Orbital.RepairedUnits.Contains(unit.Id),"repair field applied");
                Assert.False(Get<bool>(seat,"mapOpen"));
                Assert.IsNotEmpty(Get<PlayableSnapshot>(seat,"view").Orbital.RepairRims);
                Assert.Greater(Get<PlayableSnapshot>(seat,"view").Entities.Single(candidate=>candidate.Id==unit.Id).ExactHealth,
                    unit.ExactHealth);
                if(index==0)
                {
                    Assert.IsEmpty(Get<PlayableSnapshot>(other,"view").Orbital.RepairRims);
                    var camera=Get<Camera>(seat,"cameraView");
                    var anchor=Get<PlayableWorld>(seat,"world").Point(unit.Position);
                    camera.orthographicSize=9;
                    float depth=camera.WorldToViewportPoint(anchor).z;
                    camera.transform.position+=anchor-camera.ViewportToWorldPoint(new Vector3(.5f,.5f,depth));
                    void VerifyRepairOverlay()
                    {
                        Call(seat,"UpdateScanPresentation");
                        Assert.AreEqual(DisplayStyle.Flex,root.Q("orbital-scan-world").style.display.value,
                            "a visible active field keeps the effects layer on");
                        Assert.IsNotEmpty(Get<IList>(seat,"repairSigns"));
                    }
                    VerifyRepairOverlay();
                    yield return Capture("repair-nanobots-active",VerifyRepairOverlay,
                        captureWidth:1280,captureHeight:800,refreshWorld:false,captureChecks:VerifyRepairOverlay);
                }
            }
            Runtime.RequestStop();
            yield return Wait(()=>Runtime.IsStopped,"repair field fixture stop");
        }

        [UnityTest] public IEnumerator AbilityTargetingSingleViewport()=>AbilityTargetingLayout(1);
        [UnityTest] public IEnumerator AbilityTargetingFourViewports()=>AbilityTargetingLayout(4);
        private IEnumerator AbilityTargetingLayout(int count)
        {
            yield return StartOrdinary(count);
            var data=Get<PlayableProfile>(app,"profile").CopyData();
            foreach(int ability in new[]{1,2,OrbitalAbilities.ControlHack}){
                foreach(var seat in Seats){
                    var model=new OrbitalEscalationState();model.Damage(250,data);Assert.True(model.Choose(ability));
                    Put(seat,"view",Get<PlayableSnapshot>(seat,"view").WithOrbital(model.Snapshot(data)));
                    Call(seat,"BeginOrbitalTarget",true,ability);
                    var point=Get<PlayableSnapshot>(seat,"view").Entities.First(e=>e.Owner==Get<PlayableSnapshot>(seat,"view").Owner).Position;PadCursor(seat,point);
                }
                void Seed(){foreach(var seat in Seats){var model=new OrbitalEscalationState();model.Damage(250,data);model.Choose(ability);Put(seat,"view",Get<PlayableSnapshot>(seat,"view").WithOrbital(model.Snapshot(data)));Call(seat,"UpdateOrbitalHud");Call(seat,"UpdateScanPresentation");}}
                Seed();
                yield return Capture("targeting-"+count+"-ability-"+ability,Seed,captureWidth:1280,captureHeight:720,refreshWorld:false,captureChecks:()=>{
                    foreach(var seat in Seats){
                        var root=Get<VisualElement>(seat,"root");var header=root.Q("ability-target-header");var controls=root.Q("ability-target-controls");
                        Assert.AreEqual(DisplayStyle.Flex,header.style.display.value);Inside(root.worldBound,header.worldBound,"target header");Inside(root.worldBound,controls.worldBound,"apply/cancel");
                        Assert.False(header.worldBound.Overlaps(controls.worldBound));Assert.AreEqual(DisplayStyle.None,Get<Label>(seat,"noticeLabel").style.display.value,"targeting instructions have one owner");
                        StringAssert.Contains(OrbitalAbilities.Name(ability),root.Q<Label>("ability-target-title").text);
                        Assert.AreEqual("Отменить",root.Q<Label>("ability-target-cancel").text);
                        foreach(var glyph in controls.Query<NativeControllerGlyph>().ToList()){glyph.Refresh(pads[Array.IndexOf(Seats,seat)]);Assert.AreEqual(DisplayStyle.Flex,glyph.style.display.value,"apply AND cancel badges stay visible after device refresh");}
                        var shared=Get<VisualElement>(app,"sharedMapFrame");if(shared!=null&&shared.style.display.value!=DisplayStyle.None)Assert.False(header.worldBound.Overlaps(shared.worldBound),"target header clears common minimap");
                        Assert.True(root.Q("orbital-slot-"+ability).ClassListContains("ability-targeting-active"));
                        if(ability==OrbitalAbilities.ControlHack)Assert.False(Get<bool>(seat,"abilityTargetValid"),"own unit cannot be hacked");
                        else StringAssert.Contains(ability==1?"Радиус обзора":"Радиус взрыва",root.Q<Label>("ability-target-status").text);
                    }
                });
                foreach(var seat in Seats){Call(seat,"CancelScanTarget");Call(seat,"UpdateScanPresentation");Assert.AreEqual(DisplayStyle.None,Get<VisualElement>(seat,"root").Q("ability-target-header").style.display.value);Assert.Zero(Get<PlayableSnapshot>(seat,"view").Orbital.Abilities[0].Remaining);}
            }
            Runtime.RequestStop();yield return Wait(()=>Runtime.IsStopped,"targeting layout stop");
        }
    }
}
