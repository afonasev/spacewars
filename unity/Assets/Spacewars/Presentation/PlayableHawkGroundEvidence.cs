#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using Spacewars.Simulation;
using UnityEngine;
namespace Spacewars.Presentation
{
    public sealed partial class PlayableBootstrap
    {
        private IEnumerator CaptureHawkGroundEvidence()
        {
            float startup=Time.realtimeSinceStartup+90;
            while((view==null||inLobby||preparing)&&Time.realtimeSinceStartup<startup)yield return null;
            if(view==null||inLobby||preparing||localPresentations.Count!=2)throw new InvalidOperationException("Hawk ground ordinary entry timeout");
            Screen.SetResolution(1440,900,FullScreenMode.Windowed);yield return new WaitForSecondsRealtime(.5f);
            var seats=localPresentations.ToArray();var air=seats[0];var ground=seats[1];
            var home=air.view.Buildings.Single(b=>b.Owner==air.view.Owner&&b.Kind==PlayableBuildingKind.Headquarters);
            var explorer=ground.view.Entities.First(e=>e.Owner==ground.view.Owner&&e.Kind==PlayableEntityKind.Explorer);int targetId=explorer.Id;double initialGround=explorer.Health;
            // Starting unit and ordinary paid hangar production. No injected entities,
            // terrain/fog changes, grants, or privileged attack-target information.
            var destination=explorer.Position;
            ground.Submit(PlayableCommandKind.Hold,new[]{targetId});
            int slot=air.view.Sites.Single(s=>s.Site.Id==home.SiteId).Site.Slots[0].Id;
            var receipt=runtime.TrySubmit(new PlayableCommand(generation,++air.sequence,air.LocalOwnerId,PlayableCommandKind.BuildAt,Array.Empty<int>(),siteId:home.SiteId,slotId:slot,parentId:home.Id,buildingKind:PlayableBuildingKind.Hangar));
            if(!receipt.Accepted)throw new InvalidOperationException("Ground witness hangar rejected");
            void Focus(PlayableBootstrap seat,Vector3 at,Vector3? peer=null){
                seat.cameraView.orthographicSize=(float)(profile.CameraMinZoom*1.5);
                if(peer.HasValue){var delta=peer.Value-at;at=(at+peer.Value)/2;seat.cameraView.orthographicSize=Mathf.Max(seat.cameraView.orthographicSize,Mathf.Abs(Vector3.Dot(delta,seat.cameraView.transform.up)),Mathf.Abs(Vector3.Dot(delta,seat.cameraView.transform.right))/seat.cameraView.aspect);}
                // Keep the authored viewing distance. Reusing target depth lets a
                // long flight cross the near plane even while centered in XY.
                seat.cameraView.transform.position=at+new Vector3((float)profile.CameraOffsetX,(float)profile.CameraHeight,(float)profile.CameraOffsetZ);seat.RememberKeyboardCamera();
            }
            IEnumerator Frame(string name){yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(evidence,name+".png"));}
            float deadline=Time.realtimeSinceStartup+100;int hawkId=0,phase=-1;bool ordered=false,rocket=false,aa=false,groundDamage=false,airDamage=false;float verifiedAt=-1;HawkVideoCapture capture=null;
            var telemetry=new List<string>{"seconds,tick,hawk,altitude,groundHealth,airHealth,rocket,explorerAA"};
            while(Time.realtimeSinceStartup<deadline)
            {
                var av=air.view;var gv=ground.view;var hangar=av.Buildings.FirstOrDefault(b=>b.Owner==av.Owner&&b.Kind==PlayableBuildingKind.Hangar);
                var hawk=av.Entities.FirstOrDefault(e=>e.Owner==av.Owner&&e.Kind==PlayableEntityKind.Hawk);explorer=gv.Entities.FirstOrDefault(e=>e.Id==targetId);
                if(!ordered&&hangar?.Phase==ConstructionPhase.Ready){ordered=true;air.selection.Clear();air.selection.Add(hangar.Id);air.Submit(PlayableCommandKind.QueueHawk,new[]{hangar.Id});}
                air.battleDismissed=true;ground.battleDismissed=true;
                if(hangar!=null&&hawkId==0)Focus(air,air.world.AirGroundPoint(hangar.Position)+Vector3.up*(float)(profile.HawkClearance/2));
                if(explorer!=null){var visibleAir=gv.Entities.FirstOrDefault(e=>e.Id==hawkId);Vector3? airPoint=visibleAir!=null&&ground.world.Actors.TryGetValue(hawkId,out var shownAir)?shownAir.Root.position:(Vector3?)null;Focus(ground,ground.world.EntityPoint(explorer),airPoint);}
                if(hangar!=null&&hangar.LaunchPhase!=phase){phase=hangar.LaunchPhase;if(phase>0){if(capture==null){capture=gameObject.AddComponent<HawkVideoCapture>();capture.Begin(evidence);}yield return Frame("ground-launch-phase-"+phase);}}
                if(hawk!=null&&hawkId==0){hawkId=hawk.Id;air.selection.Clear();air.selection.Add(hawkId);air.Submit(PlayableCommandKind.AttackMove,new[]{hawkId},destination);ground.selection.Clear();ground.selection.Add(targetId);}
                if(hawk!=null&&air.world.Actors.TryGetValue(hawk.Id,out var actor)){var visibleGround=av.Entities.FirstOrDefault(e=>e.Id==targetId);Focus(air,actor.Root.position,visibleGround!=null?(Vector3?)air.world.EntityPoint(visibleGround):null);}
                bool newRocket=av.Projectiles.Any(p=>p.Kind==PlayableEntityKind.Hawk&&p.TargetId==targetId);
                bool newAA=gv.Projectiles.Any(p=>p.Kind==PlayableEntityKind.Explorer&&p.TargetId==hawkId);
                if(newRocket&&!rocket){rocket=true;yield return Frame("ground-01-hawk-rockets");}
                if(newAA&&!aa){aa=true;yield return Frame("ground-02-explorer-aa");}
                if(explorer!=null&&explorer.Health<initialGround)groundDamage=true;
                if(hawk!=null&&hawk.Health<profile.HawkHealth)airDamage=true;
                if(capture!=null)telemetry.Add(Time.realtimeSinceStartupAsDouble.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+","+av.Tick+","+hawkId+","+(hawk?.Altitude??0)+","+(explorer?.Health??0)+","+(hawk?.Health??0)+","+rocket+","+aa);
                if(rocket&&aa&&groundDamage&&airDamage&&verifiedAt<0){verifiedAt=Time.realtimeSinceStartup;yield return Frame("ground-03-mutual-damage");}
                if(verifiedAt>0&&Time.realtimeSinceStartup>verifiedAt+6)break;
                yield return null;
            }
            if(capture!=null)yield return capture.Finish();
            File.WriteAllLines(Path.Combine(evidence,"ground-telemetry.csv"),telemetry);
            string result="produced="+(hawkId!=0)+" groundRocket="+rocket+" explorerAA="+aa+" groundDamage="+groundDamage+" airDamage="+airDamage+" physicalDevices=false";
            File.WriteAllText(Path.Combine(evidence,"hawk-ground-result.txt"),result);
            if(hawkId==0||!rocket||!aa||!groundDamage||!airDamage)throw new InvalidOperationException("Hawk ground FAIL "+result);
            Debug.Log("HAWK_GROUND_PLAYER_PASS "+result);Quit();
        }
    }
}
#endif
