using System.Linq;
using Spacewars.Simulation;
using UnityEngine;
namespace Spacewars.Presentation
{
    public sealed partial class PlayableBootstrap
    {
        private bool BotSeat=>localBinding!=null&&!localBinding.Human;
        private bool BotCameraLocked=>BotSeat||spectatorMode&&spectatorPerspective.HasValue;
        private string botCameraOwner;
        private long botCameraSequence=-1,botCameraGeneration=-1;
        private NavPoint? botCameraTarget;
        private float botCameraNextFocus;
        private SplitScreenHudProfile botCameraProfile;
        private void UpdateBotCamera()
        {
            if(!BotCameraLocked||view==null||cameraView==null){botCameraOwner=null;botCameraTarget=null;return;}
            string owner=view.OwnerId;
            if(botCameraOwner!=owner||botCameraGeneration!=view.Generation){
                botCameraOwner=owner;botCameraGeneration=view.Generation;botCameraSequence=-1;botCameraNextFocus=0;
                botCameraTarget=view.Buildings.FirstOrDefault(b=>b.Owner==view.Owner&&b.Kind==PlayableBuildingKind.Headquarters)?.Position;
                if(botCameraTarget.HasValue)MoveBotCamera(botCameraTarget.Value,true);
            }
            if(paused||inLobby||preparing||ResultsVisible)return;
            var activity=runtime?.BotCameraFocus(owner);botCameraProfile=botCameraProfile??SplitScreenHudProfile.Load();
            if(activity!=null&&activity.Generation==view.Generation&&activity.Tick<=view.Tick&&activity.Sequence!=botCameraSequence&&Time.unscaledTime>=botCameraNextFocus){
                botCameraTarget=activity.Position;botCameraSequence=activity.Sequence;botCameraNextFocus=Time.unscaledTime+botCameraProfile.botCameraHold;
            }
            if(botCameraTarget.HasValue)MoveBotCamera(botCameraTarget.Value,false);
        }
        private void MoveBotCamera(NavPoint target,bool immediate)
        {
            var center=Ground(SeatScreenCenter);var destination=cameraView.transform.position+new Vector3((float)(target.X-center.X),0,(float)(target.Z-center.Z));
            cameraView.transform.position=immediate?destination:Vector3.MoveTowards(cameraView.transform.position,destination,(botCameraProfile??(botCameraProfile=SplitScreenHudProfile.Load())).botCameraSpeed*Time.unscaledDeltaTime);
        }
    }
}
