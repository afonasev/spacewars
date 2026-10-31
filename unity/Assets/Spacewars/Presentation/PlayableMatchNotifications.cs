using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.UIElements;
namespace Spacewars.Presentation
{
    public sealed partial class PlayableBootstrap
    {
        private readonly NativeMatchNotifications matchNotifications=new NativeMatchNotifications();
        private Label matchNotification;
        private void UpdateMatchNotifications()
        {
            if(root==null||view==null)return;
            if(matchNotification==null){
                matchNotification=new Label{name="match-notification",pickingMode=PickingMode.Ignore};
                matchNotification.style.position=Position.Absolute;matchNotification.style.top=StyleKeyword.Auto;
                matchNotification.style.fontSize=14;matchNotification.style.marginLeft=matchNotification.style.marginRight=matchNotification.style.marginTop=matchNotification.style.marginBottom=0;
                matchNotification.style.whiteSpace=WhiteSpace.Normal;root.Add(matchNotification);
                root.RegisterCallback<GeometryChangedEvent>(_=>PositionMatchNotification());
                compactMap?.parent.RegisterCallback<GeometryChangedEvent>(_=>PositionMatchNotification());
            }
            bool visible=!inLobby&&!preparing&&!paused&&!MenuOwnsInput&&!ResultsVisible;
            if(!visible){matchNotification.style.display=DisplayStyle.None;return;}
            var data=matchNotifications.Settings(profile);matchNotifications.Update(view,spectatorMode&&!spectatorPerspective.HasValue,Time.unscaledTimeAsDouble,profile);
            var entry=matchNotifications.Visible(Time.unscaledTimeAsDouble,profile);
            PositionMatchNotification();
            matchNotification.style.unityTextOutlineColor=Color.black;matchNotification.style.unityTextOutlineWidth=(float)data.chatOutlineWidth;
            matchNotification.style.textShadow=new TextShadow{color=Color.black,offset=new Vector2(0,(float)data.chatShadowOffset),blurRadius=(float)data.chatShadowBlur};
            matchNotification.style.color=Color.white;
            matchNotification.text=entry?.Text??"";matchNotification.style.display=entry!=null?DisplayStyle.Flex:DisplayStyle.None;
        }
        private void PositionMatchNotification()
        {
            if(matchNotification==null||root==null)return;
            var data=matchNotifications.Settings(profile);var frame=compactMap?.parent;
            float edge=frame!=null?frame.style.width.value.value:(float)profile.MinimapCompactSize;float frameBottom=0;
            if(frame!=null&&frame.resolvedStyle.display!=DisplayStyle.None){
                var measured=root.WorldToLocal(new Vector2(frame.worldBound.xMax,frame.worldBound.yMax));
                if(!float.IsNaN(measured.x)&&!float.IsInfinity(measured.x))edge=measured.x;
                if(!float.IsNaN(measured.y)&&!float.IsInfinity(measured.y))frameBottom=Mathf.Max(0,root.contentRect.yMax-measured.y)+frame.resolvedStyle.borderBottomWidth;
            }
            matchNotification.style.left=edge+(float)data.notificationBottomPixels;
            matchNotification.style.right=(float)data.notificationBottomPixels;
            matchNotification.style.bottom=frameBottom;
        }
        private void FocusMatchNotification(bool previous)
        {
            if(view==null||cameraView==null||chatOpen||paused||MenuOwnsInput||ResultsVisible||inLobby||preparing||signalWheelOpen||PadAuxiliaryWheel||battleGestures.Mode=="buildingWheel"||!localInputFocused||input?.HasFocus!=true)return;
            var point=matchNotifications.Focus(previous,Time.unscaledTimeAsDouble,profile);if(!point.HasValue)return;
            var center=Ground(SeatScreenCenter);cameraView.transform.position+=new Vector3((float)(point.Value.X-center.X),0,(float)(point.Value.Z-center.Z));
            padCursorGround=Ground(SeatScreenCenter);battleCursor=SeatScreenCenter;
        }
    }
}
