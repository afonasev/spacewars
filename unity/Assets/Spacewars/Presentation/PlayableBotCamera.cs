using System.Linq;
using Spacewars.Simulation;
using UnityEngine;
namespace Spacewars.Presentation
{
    public sealed partial class PlayableBootstrap
    {
        private bool BotSeat=>localBinding!=null&&!localBinding.Human;
        private bool spectatorManualCamera;
        private bool BotCameraLocked=>BotSeat||spectatorMode&&spectatorPerspective.HasValue&&!spectatorManualCamera;
        private void ReleaseSpectatorCamera()
        {
            if(!spectatorMode||!spectatorPerspective.HasValue||spectatorManualCamera)return;
            spectatorManualCamera=true;
            botCameraOwner=null;
            botCameraTarget=null;
        }
        private void PanCameraManually(Vector3 displacement)
        {
            if(BotSeat||displacement.sqrMagnitude==0)return;
            ReleaseSpectatorCamera();
            cameraView.transform.position+=displacement;
        }
        private void ZoomCameraManually(float size)
        {
            if(BotSeat||cameraView.orthographicSize==size)return;
            ReleaseSpectatorCamera();
            cameraView.orthographicSize=size;
        }
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
                if(botCameraTarget.HasValue)MoveBotCamera(botCameraTarget.Value,true,0);
            }
            if(paused||inLobby||preparing||ResultsVisible)return;
            var activity=runtime?.BotCameraFocus(owner);botCameraProfile=botCameraProfile??SplitScreenHudProfile.Load();
            if(activity!=null&&activity.Generation==view.Generation&&activity.Tick<=view.Tick&&activity.Sequence!=botCameraSequence&&Time.unscaledTime>=botCameraNextFocus){
                botCameraTarget=activity.Position;botCameraSequence=activity.Sequence;botCameraNextFocus=Time.unscaledTime+botCameraProfile.botCameraHold;
            }
            if(botCameraTarget.HasValue)MoveBotCamera(botCameraTarget.Value,false,Time.unscaledDeltaTime);
        }
        private void MoveBotCamera(NavPoint target, bool immediate, float deltaTime)
        {
            var center = Ground(SeatScreenCenter);
            var displacement = new Vector3((float)(target.X - center.X), 0, (float)(target.Z - center.Z));
            var cameraTransform = cameraView.transform;
            var destination = cameraTransform.position + displacement;

            if (spectatorMode && !immediate)
            {
                // Measure the pan in viewport widths and heights at the current zoom.
                const float snapDistanceInScreens = 2;
                var projected = cameraTransform.InverseTransformDirection(displacement);
                float height = 2 * cameraView.orthographicSize;
                float width = height * cameraView.aspect;
                float distanceInScreens = Mathf.Max(Mathf.Abs(projected.x) / width, Mathf.Abs(projected.y) / height);
                // A rotated camera can round a pan at exactly two screens slightly above the limit.
                immediate = distanceInScreens > snapDistanceInScreens
                    && !Mathf.Approximately(distanceInScreens, snapDistanceInScreens);
            }

            if (immediate)
            {
                cameraTransform.position = destination;
                return;
            }

            botCameraProfile ??= SplitScreenHudProfile.Load();
            float speedMultiplier = spectatorMode ? Mathf.Max(1, (float)spectatorRate) : 1;
            cameraTransform.position = Vector3.MoveTowards(cameraTransform.position, destination,
                botCameraProfile.botCameraSpeed * speedMultiplier * deltaTime);
        }
    }
}
