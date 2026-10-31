using System.Collections.Generic;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.UIElements;

namespace Spacewars.Presentation
{
    public sealed partial class PlayableBootstrap
    {
        private readonly HashSet<int> dragSelectionPreview=new HashSet<int>();
        private Vector2 dragSelectionFrom,dragSelectionTo;
        private bool dragSelectionActive,worldDragPreviewActive;
        private VisualElement[] dragFrameCorners;
        private PlayableProfile dragFrameProfile;

        private void CreateDragFrame()
        {
            dragBox=new VisualElement{name="drag-selection-box",pickingMode=PickingMode.Ignore};
            dragBox.style.position=Position.Absolute;dragBox.style.display=DisplayStyle.None;
            dragBox.style.overflow=Overflow.Hidden;
            dragFrameCorners=new VisualElement[4];
            for(int i=0;i<dragFrameCorners.Length;i++)
            {
                var corner=new VisualElement{name="drag-selection-corner-"+i,pickingMode=PickingMode.Ignore};
                corner.style.position=Position.Absolute;
                if((i&1)==0)corner.style.left=0;else corner.style.right=0;
                if((i&2)==0)corner.style.top=0;else corner.style.bottom=0;
                dragBox.Add(corner);dragFrameCorners[i]=corner;
            }
            StyleDragFrame();root.Add(dragBox);
        }
        private void StyleDragFrame()
        {
            if(profile==null||dragFrameProfile==profile)return;
            dragFrameProfile=profile;
            var green=new Color((float)profile.SelectionFrameRed,(float)profile.SelectionFrameGreen,(float)profile.SelectionFrameBlue);
            var fill=green;fill.a=(float)profile.SelectionFrameFillOpacity;dragBox.style.backgroundColor=fill;
            var line=green;line.a=(float)profile.SelectionFrameLineOpacity;
            dragBox.style.borderLeftColor=dragBox.style.borderRightColor=dragBox.style.borderTopColor=dragBox.style.borderBottomColor=line;
            dragBox.style.borderLeftWidth=dragBox.style.borderRightWidth=dragBox.style.borderTopWidth=dragBox.style.borderBottomWidth=(float)profile.SelectionFrameLineWidth;
            for(int i=0;i<dragFrameCorners.Length;i++)
            {
                var corner=dragFrameCorners[i];corner.style.width=corner.style.height=(float)profile.SelectionFrameCornerLength;
                corner.style.borderLeftColor=corner.style.borderRightColor=corner.style.borderTopColor=corner.style.borderBottomColor=green;
                if((i&1)==0)corner.style.borderLeftWidth=(float)profile.SelectionFrameCornerWidth;else corner.style.borderRightWidth=(float)profile.SelectionFrameCornerWidth;
                if((i&2)==0)corner.style.borderTopWidth=(float)profile.SelectionFrameCornerWidth;else corner.style.borderBottomWidth=(float)profile.SelectionFrameCornerWidth;
            }
        }
        private void DrawDrag(Vector2 a,Vector2 b,bool active)
        {
            dragSelectionFrom=a;dragSelectionTo=b;
            dragSelectionActive=active&&(b-a).magnitude>(profile?.SelectionDragPixels??0);
            RefreshDragSelection();
            if(root?.panel==null||dragBox==null)return;
            StyleDragFrame();dragBox.style.display=dragSelectionActive?DisplayStyle.Flex:DisplayStyle.None;
            var x=PanelPoint(a);var y=PanelPoint(b);
            dragBox.style.left=Mathf.Min(x.x,y.x);dragBox.style.top=Mathf.Min(x.y,y.y);
            dragBox.style.width=Mathf.Abs(x.x-y.x);dragBox.style.height=Mathf.Abs(x.y-y.y);
        }
        private IEnumerable<int> BoxSelectionUnits(Vector2 from,Vector2 to)
        {
            if(view==null||world==null||cameraView==null)yield break;
            float left=Mathf.Min(from.x,to.x),right=Mathf.Max(from.x,to.x),bottom=Mathf.Min(from.y,to.y),top=Mathf.Max(from.y,to.y);
            foreach(var entity in view.Entities)if(entity.Owner==view.Owner&&entity.Health>0)
            {
                var point=cameraView.WorldToScreenPoint(world.EntityPoint(entity));
                if(point.z>0&&point.x>=left&&point.x<=right&&point.y>=bottom&&point.y<=top)yield return entity.Id;
            }
        }
        private void RefreshDragSelection()
        {
            dragSelectionPreview.Clear();
            worldDragPreviewActive=dragSelectionActive&&view!=null&&!spectatorMode&&input!=null&&input.HasFocus&&input.WorldInputEnabled&&input.CommandInputEnabled&&!input.PlacementInputEnabled
                &&(input.MapAt?.Invoke(dragSelectionFrom)??0)==0&&(input.MapAt?.Invoke(dragSelectionTo)??0)==0&&input.IsPointerOverUi?.Invoke(dragSelectionTo)!=true;
            if(!worldDragPreviewActive)return;
            if(input.SelectionAppendHeld)foreach(var entity in view.Entities)if(entity.Owner==view.Owner&&entity.Health>0&&selection.Contains(entity.Id))dragSelectionPreview.Add(entity.Id);
            foreach(int id in BoxSelectionUnits(dragSelectionFrom,dragSelectionTo))dragSelectionPreview.Add(id);
        }
        private bool DisplayedSelection(int id)=>worldDragPreviewActive?dragSelectionPreview.Contains(id):selection.Contains(id);
    }
}
