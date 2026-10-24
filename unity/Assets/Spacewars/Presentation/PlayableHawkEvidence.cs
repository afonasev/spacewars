#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.UIElements;
namespace Spacewars.Presentation
{
    public sealed partial class PlayableBootstrap
    {
        private IEnumerator CaptureHawkCombatEvidence()
        {
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-hawkGroundEvidence")>=0){yield return CaptureHawkGroundEvidence();yield break;}
            float startup=Time.realtimeSinceStartup+90;
            while((view==null||inLobby||preparing)&&Time.realtimeSinceStartup<startup)yield return null;
            if(view==null||inLobby||preparing||localPresentations.Count!=2)throw new InvalidOperationException("Hawk combat Player ordinary entry timeout");
            Screen.SetResolution(1440,900,FullScreenMode.Windowed);yield return new WaitForSecondsRealtime(.5f);
            float deadline=Time.realtimeSinceStartup+100;var seats=localPresentations.ToArray();var starts=seats.Select(seat=>seat.view.Buildings.Single(b=>b.Owner==seat.view.Owner&&b.Kind==PlayableBuildingKind.Headquarters)).ToArray();var middle=new NavPoint((starts[0].Position.X+starts[1].Position.X)/2,(starts[0].Position.Z+starts[1].Position.Z)/2);
            void Focus(PlayableBootstrap seat,NavPoint point,double height){seat.cameraView.orthographicSize=(float)(profile.CameraMinZoom*1.5);var at=seat.world.AirGroundPoint(point);at.y=(float)height;float depth=seat.cameraView.WorldToViewportPoint(at).z;seat.cameraView.transform.position+=at-seat.cameraView.ViewportToWorldPoint(new Vector3(.5f,.5f,depth));seat.RememberKeyboardCamera();}
            IEnumerator Frame(string name){yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(evidence,name+".png"));yield return null;}
            bool[] ordered={false,false},moved={false,false};int[] phase={-1,-1},aircraft={0,0};bool shot=false,damage=false,death=false;int frame=0;float next=0;bool recording=false;var times=new List<string>{"frame,seconds"};string frames=Path.Combine(evidence,"combat-frames");Directory.CreateDirectory(frames);
            for(int i=0;i<2;i++){var seat=seats[i];int slot=seat.view.Sites.Single(s=>s.Site.Id==starts[i].SiteId).Site.Slots[0].Id;var buildReceipt=runtime.TrySubmit(new PlayableCommand(generation,++seat.sequence,seat.LocalOwnerId,PlayableCommandKind.BuildAt,Array.Empty<int>(),siteId:starts[i].SiteId,slotId:slot,parentId:starts[i].Id,buildingKind:PlayableBuildingKind.Hangar));if(!buildReceipt.Accepted)throw new InvalidOperationException("Hawk Player build rejected "+buildReceipt.Status);}
            while(Time.realtimeSinceStartup<deadline)
            {
                for(int i=0;i<2;i++)
                {
                    var seat=seats[i];var current=runtime.ParticipantView(seat.LocalOwnerId);var hangar=current.Buildings.FirstOrDefault(b=>b.Owner==current.Owner&&b.Kind==PlayableBuildingKind.Hangar);
                    if(!ordered[i]&&hangar?.Phase==ConstructionPhase.Ready){ordered[i]=true;seat.selection.Clear();seat.selection.Add(hangar.Id);seat.battleDismissed=true;seat.Submit(PlayableCommandKind.QueueHawk,new[]{hangar.Id});}
                    var hawk=current.Entities.FirstOrDefault(e=>e.Owner==current.Owner&&e.Kind==PlayableEntityKind.Hawk);
                    if(hangar!=null&&hawk==null&&!moved[i])Focus(seat,hangar.Position,profile.HawkClearance/2);
                    if(hangar!=null&&phase[i]!=hangar.LaunchPhase){phase[i]=hangar.LaunchPhase;if(i==0&&phase[i]>0){recording=true;yield return Frame("launch-phase-"+phase[i]);}}
                    if(hawk!=null&&!moved[i]){moved[i]=true;aircraft[i]=hawk.Id;seat.selection.Clear();seat.selection.Add(hawk.Id);seat.Submit(PlayableCommandKind.AttackMove,new[]{hawk.Id},new NavPoint(middle.X+(i==0?-3.5:3.5),middle.Z));}
                    if(hawk!=null)Focus(seat,hawk.Position,hawk.Altitude);
                    if(!shot&&current.Projectiles.Any(x=>x.Kind==PlayableEntityKind.Hawk)){shot=true;yield return Frame("02-paired-rockets");}
                    if(!damage&&hawk!=null&&hawk.Health<profile.HawkHealth){damage=true;yield return Frame("03-air-damage-projection");}
                    if(current.Sounds.Any(e=>e.Kind==PlayableSoundKind.Destroyed&&e.Weapon==PlayableEntityKind.Hawk&&e.Altitude>0))death=true;
                }
                if(recording&&Time.realtimeSinceStartup>=next){yield return new WaitForEndOfFrame();times.Add(frame+","+Time.realtimeSinceStartup.ToString("R",System.Globalization.CultureInfo.InvariantCulture));ScreenCapture.CaptureScreenshot(Path.Combine(frames,"frame-"+(frame++).ToString("D4")+".png"));next=Time.realtimeSinceStartup+.1f;}
                if(death){yield return Frame("04-airborne-destruction");yield return new WaitForSecondsRealtime(.4f);yield return Frame("05-air-debris-fade");break;}yield return null;
            }
            File.WriteAllLines(Path.Combine(evidence,"combat-frame-times.csv"),times);
            string result="produced="+moved.All(x=>x)+" rockets="+shot+" damaged="+damage+" airborneDeath="+death+" physicalDevices=false";File.WriteAllText(Path.Combine(evidence,"hawk-combat-result.txt"),result);
            if(!moved.All(x=>x)||!shot||!damage||!death)throw new InvalidOperationException("Hawk combat Player FAIL "+result);Debug.Log("HAWK_COMBAT_PLAYER_PASS "+result);Quit();
        }
        private IEnumerator CaptureHawkFlightEvidence()
        {
            float deadline=Time.realtimeSinceStartup+180;
            while((view==null||inLobby||preparing)&&Time.realtimeSinceStartup<deadline)yield return null;
            if(view==null||inLobby||preparing)throw new InvalidOperationException("Hawk evidence ordinary entry timeout");
            Screen.SetResolution(1440,900,FullScreenMode.Windowed);yield return new WaitForSecondsRealtime(.5f);
            void Choose(string id){var button=battleRing.Q<Button>("radial-"+id);if(button==null)throw new InvalidOperationException("Missing Hawk radial "+id);button.Focus();using(var evt=NavigationSubmitEvent.GetPooled()){evt.target=button;button.SendEvent(evt);}}
            void Focus(NavPoint p,double height=0){cameraView.orthographicSize=(float)profile.CameraMinZoom;var target=world.Point(p)+Vector3.up*(float)height;cameraView.transform.position=target+new Vector3((float)profile.CameraOffsetX,(float)profile.CameraHeight,(float)profile.CameraOffsetZ);cameraView.transform.LookAt(target);RememberKeyboardCamera();}
            IEnumerator Frame(string name){yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(evidence,name+".png"));yield return null;}
            var home=view.Sites.First(s=>s.Owner==view.Owner&&s.Ready&&s.Site.Slots.Count>0);selectedSite=home.Site.Id;selectedSlot=home.Site.Slots[0].Id;selection.Clear();Focus(home.Site.Slots[0].Position);
            yield return new WaitForSecondsRealtime(.5f);Choose("build:"+selectedSite+":"+selectedSlot+":"+PlayableBuildingKind.Hangar);
            while(!view.Buildings.Any(b=>b.Owner==view.Owner&&b.Kind==PlayableBuildingKind.Hangar&&b.Phase==ConstructionPhase.Ready)&&Time.realtimeSinceStartup<deadline)yield return null;
            var hangar=view.Buildings.First(b=>b.Owner==view.Owner&&b.Kind==PlayableBuildingKind.Hangar);selection.Clear();selection.Add(hangar.Id);battleDismissed=false;Focus(hangar.Position);
            yield return Frame("01-hangar-closed");
            // Wait for normal income; production remains the ordinary named profile.
            while(view.ExactCredits<2*profile.HawkCreditCost&&Time.realtimeSinceStartup<deadline)yield return null;
            yield return new WaitForSecondsRealtime(.3f);Choose(hangar.Id+":hawk");Choose(hangar.Id+":hawk");
            yield return new WaitForSecondsRealtime(1);yield return Frame("02-hawk-paid-queue");
            string frames=Path.Combine(evidence,"flight-frames");Directory.CreateDirectory(frames);int frame=0,phase=-1;bool recording=false;float nextFrame=0;int aircraft=0;bool arrived=false;var frameTimes=new List<string>{"frame,seconds"};
            while(Time.realtimeSinceStartup<deadline)
            {
                hangar=view.Buildings.First(b=>b.Id==hangar.Id);var hawk=view.Entities.FirstOrDefault(e=>e.Owner==view.Owner&&e.Kind==PlayableEntityKind.Hawk);
                if(hangar.LaunchPhase>0&&!recording){recording=true;Focus(hangar.Position,profile.HawkClearance/2);}
                if(hangar.LaunchPhase!=phase){phase=hangar.LaunchPhase;if(recording)yield return Frame("phase-"+phase);}
                if(recording&&Time.realtimeSinceStartup>=nextFrame){yield return new WaitForEndOfFrame();frameTimes.Add(frame+","+Time.realtimeSinceStartup.ToString("R",System.Globalization.CultureInfo.InvariantCulture));ScreenCapture.CaptureScreenshot(Path.Combine(frames,"frame-"+(frame++).ToString("D4")+".png"));nextFrame=Time.realtimeSinceStartup+.1f;}
                if(hawk!=null&&aircraft==0){aircraft=hawk.Id;selection.Clear();selection.Add(hawk.Id);Submit(PlayableCommandKind.Move,new[]{hawk.Id},new NavPoint(hangar.Position.X-8,hangar.Position.Z));}
                if(aircraft!=0&&hawk!=null&&hawk.NavigationOutcome==NavigationOutcome.Arrived&&hangar.LaunchPhase==0){arrived=true;Focus(hawk.Position,hawk.Altitude-world.Point(hawk.Position).y);yield return Frame("03-hawk-selected-airborne");break;}
                yield return null;
            }
            if(aircraft==0||!arrived)throw new InvalidOperationException("Hawk evidence did not reach arrival and roof closure");
            File.WriteAllLines(Path.Combine(evidence,"flight-frame-times.csv"),frameTimes);
            File.WriteAllText(Path.Combine(evidence,"hawk-flight-result.json"),"{\"result\":\"PASS\",\"surface\":\"ordinary Development Player\",\"frames\":"+frame+",\"aircraft\":"+aircraft+",\"physical_devices\":false}");Debug.Log("HAWK_FLIGHT_PLAYER_PASS");Quit();
        }
    }
}
#endif
