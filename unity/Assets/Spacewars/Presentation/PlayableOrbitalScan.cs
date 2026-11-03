using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Spacewars.Presentation
{
    public sealed partial class PlayableBootstrap
    {
        private int scanTargetAbility=1;
        private double TargetPreviewRadius=>scanTargetAbility==2?ScanData.orbitalMineBlastRadius:ScanData.orbitalScanRadius;
        private bool scanTargeting,scanTargetPad,scanReleased,scanPendingTab;
        private long scanPendingSequence=-1,scanAppliedTick=-1,scanGeneration=-1;
        private NavPoint scanPendingPoint;
        private VisualElement scanLayer;
        private readonly Dictionary<(double,double),Vector2> scanProjectionCache=new Dictionary<(double,double),Vector2>();
        private PlayableProfile scanProfile;private PlayableProfileData scanData;
        private PlayableProfileData ScanData{get{if(!ReferenceEquals(scanProfile,profile)){scanProfile=profile;scanData=profile.CopyData();}return scanData;}}
        private void BeginScanTarget(bool pad)=>BeginOrbitalTarget(pad,1);
        private void BeginOrbitalTarget(bool pad,int ability)
        {
            scanTargetAbility=ability;scanTargetPad=pad;scanTargeting=true;scanReleased=false;scanGeneration=generation;
            if(orbitalOpen)CloseOrbital();input.ClearMode();battleGestures.Cancel();battleRing.style.display=DisplayStyle.None;root.Focus();
            notice=scanTargetAbility==OrbitalAbilities.ControlHack?"Взлом управления · выберите вражескую шахту или юнита":scanTargetAbility==2?"Мина-ловушка · выберите видимую точку":"Сканирование · выберите точку";
        }
        private void BeginMineTarget(bool pad)=>BeginOrbitalTarget(pad,2);
        private bool CancelScanTarget()
        {
            if(!scanTargeting)return false;scanTargeting=false;scanPendingSequence=-1;scanAppliedTick=-1;notice="";
            compactMap?.SetScanPreview(null,0);tacticalMap?.SetScanPreview(null,0);return true;
        }
        private bool ScanPointer(Vector2 at,bool left,bool down,bool alt)
        {
            if(!scanTargeting)return false;
            if(scanTargetPad)return true;
            if(down){if(!left)CancelScanTarget();else if(MapAt(at)!=3&&(MapAt(at)>0||!OverUi(at)))ConfirmOrbitalTarget(MapAt(at)>0?MapGround(MapAt(at),at):Ground(at),MapAt(at)==2,MapAt(at)==0?Pick(at,false):0);}
            return true;
        }
        private int HackTargetAt(NavPoint point)
        {
            double Distance(NavPoint p)=>(p.X-point.X)*(p.X-point.X)+(p.Z-point.Z)*(p.Z-point.Z);
            return view.Entities.Where(e=>e.Health>0&&view.Vision.IsVisible(e.Position)&&Distance(e.Position)<=Math.Pow(PlayableUnitRules.Radius(profile,e.Kind),2)).Select(e=>new{e.Id,e.Position})
                .Concat(view.Buildings.Where(b=>b.Health>0&&b.Kind==PlayableBuildingKind.Mine&&view.Vision.IsVisible(b.Position)&&Distance(b.Position)<=Math.Pow(TerritoryRules.Radius(profile,b.Kind),2)).Select(b=>new{b.Id,b.Position}))
                .OrderBy(e=>Distance(e.Position)).ThenBy(e=>e.Id).Select(e=>e.Id).FirstOrDefault();
        }
        private void ConfirmScan(NavPoint point,bool tab)=>ConfirmOrbitalTarget(point,tab,0);
        private void ConfirmOrbitalTarget(NavPoint point,bool tab,int entityId)
        {
            if(!scanTargeting||scanPendingSequence>=0||!CanOrbital)return;
            if(scanTargetAbility==OrbitalAbilities.ControlHack&&entityId==0)entityId=HackTargetAt(point);
            if(scanTargetAbility==OrbitalAbilities.ControlHack&&entityId==0){notice="Нужна видимая вражеская шахта или юнит";return;}
            long next=++sequence;
            var result=runtime.TrySubmit(new PlayableCommand(generation,next,LocalOwnerId,PlayableCommandKind.UseOrbital,scanTargetAbility==OrbitalAbilities.ControlHack?new[]{entityId}:null,target:point,targetId:scanTargetAbility).AsHuman());
            if(!result.Accepted){notice=Friendly(result.Status);return;}
            scanPendingSequence=next;scanPendingPoint=point;scanPendingTab=tab;noticeSequence=next;
        }
        private void ObserveScanReceipts(IEnumerable<PlayableCommandReceipt> receipts)
        {
            if(scanPendingSequence<0)return;
            var receipt=receipts.FirstOrDefault(r=>r.Sequence==scanPendingSequence&&r.OwnerId==LocalOwnerId);if(receipt==null)return;
            if(receipt.Status!=PlayableCommandStatus.Applied){scanPendingSequence=-1;notice=Friendly(receipt.Status);return;}
            scanAppliedTick=receipt.AppliedTick;CompleteScanReceipt();
        }
        private void CompleteScanReceipt()
        {
            // Receipt draining can race a snapshot read. Do not close TAB onto the
            // older fog projection; wait until this owner's applied state arrives.
            if(scanAppliedTick<0||view==null||view.Tick<scanAppliedTick||!view.Orbital.Abilities.Any(a=>a.Id==scanTargetAbility&&a.Remaining>0))return;
            scanPendingSequence=scanAppliedTick=-1;
            if(scanPendingTab){CloseMap();if(scanTargetAbility!=OrbitalAbilities.ControlHack)FocusScanPoint(scanPendingPoint);}
            scanTargeting=false;notice="";compactMap?.SetScanPreview(null,0);tacticalMap?.SetScanPreview(null,0);
        }
        private void FocusScanPoint(NavPoint point)
        {
            var destination=world.Point(point);var ray=cameraView.ScreenPointToRay(SeatScreenCenter);
            if(new Plane(Vector3.up,destination).Raycast(ray,out float distance)){
                var hit=ray.GetPoint(distance);cameraView.transform.position+=new Vector3(destination.x-hit.x,0,destination.z-hit.z);
            }
            padCursorGround=point;battleCursor=SeatScreenCenter;
        }
        private bool PollScanTargetPad(Gamepad pad)
        {
            if(!scanTargeting||!scanTargetPad)return false;
            if(!scanReleased){if(!pad.buttonSouth.isPressed&&!pad.buttonEast.isPressed&&!pad.leftTrigger.isPressed)scanReleased=true;return true;}
            if(pad.startButton.wasPressedThisFrame){CancelScanTarget();(localCoordinator??this).PauseFrom(this,true);}
            else if(pad.buttonEast.wasPressedThisFrame)CancelScanTarget();
            else if(pad.selectButton.wasPressedThisFrame)ToggleMap();
            else if(pad.buttonSouth.wasPressedThisFrame)ConfirmOrbitalTarget(padCursorGround,mapOpen,mapOpen?0:OrdinaryPadHit());
            return true;
        }
        private void UpdateScanPresentation()
        {
            if(root==null||view==null)return;
            if(scanGeneration!=generation||!CanOrbital)CancelScanTarget();
            CompleteScanReceipt();
            if(scanLayer==null){scanLayer=new VisualElement{name="orbital-scan-world",pickingMode=PickingMode.Ignore};AbsoluteFill(scanLayer);scanLayer.style.overflow=Overflow.Hidden;scanLayer.generateVisualContent+=DrawScanWorld;root.Insert(0,scanLayer);}
            NavPoint? preview=ScanPreviewPoint();bool paint=view.ScanTriangles.Count>0||view.ScanLines.Count>0||preview.HasValue;
            bool show=paint&&!mapOpen&&!inLobby&&!preparing&&!MenuOwnsInput&&!ResultsVisible;
            scanLayer.style.display=show?DisplayStyle.Flex:DisplayStyle.None;if(show)scanLayer.MarkDirtyRepaint();if(!paint)scanProjectionCache.Clear();
            compactMap?.SetScanPreview(preview,TargetPreviewRadius);tacticalMap?.SetScanPreview(preview,TargetPreviewRadius);
        }
        private NavPoint? ScanPreviewPoint()
        {
            if(!scanTargeting||scanTargetAbility==OrbitalAbilities.ControlHack)return null;
            if(scanTargetPad)return padCursorGround;
            var mouse=input?.AssignedMouse??Mouse.current;if(mouse==null)return null;var at=mouse.position.ReadValue();int map=MapAt(at);
            if(map==3||map==0&&OverUi(at))return null;
            return map>0?MapGround(map,at):Ground(at);
        }
        private void DrawScanWorld(MeshGenerationContext context)
        {
            if(view==null||world==null||cameraView==null)return;
            Vector2 Project(NavPoint point){var v=cameraView.WorldToViewportPoint(world.Point(point));return new Vector2(v.x*root.contentRect.width,(1-v.y)*root.contentRect.height);}
            var painter=context.painter2D;
            DrawScanFaces(context,view.ScanTriangles,Project,scanProjectionCache);
            foreach(var segment in view.ScanLines)DrawScanLine(painter,Project(segment.From),Project(segment.To),(float)segment.Opacity,(float)ScanData.orbitalScanLineWidth);
            var preview=ScanPreviewPoint();if(preview.HasValue)for(int i=0;i<128;i++){
                NavPoint At(int step)=>new NavPoint(preview.Value.X+Math.Cos(step*Math.PI/64)*TargetPreviewRadius,preview.Value.Z+Math.Sin(step*Math.PI/64)*TargetPreviewRadius);
                if(scanTargetAbility==2){
                    painter.strokeColor=view.Vision.IsVisible(preview.Value)?OrbitalTheme.Cyan:new Color(1,.3f,.15f);painter.lineWidth=(float)ScanData.orbitalMinePreviewWidth;painter.BeginPath();painter.MoveTo(Project(At(i)));painter.LineTo(Project(At(i+1)));painter.Stroke();
                }else DrawScanLine(painter,Project(At(i)),Project(At(i+1)),1,(float)ScanData.orbitalScanLineWidth);
            }
        }
        internal static void DrawScanFaces(MeshGenerationContext context,IReadOnlyList<OrbitalScanTriangle> faces,Func<NavPoint,Vector2> project,Dictionary<(double,double),Vector2> projections=null)
        {
            // One indexed mesh avoids per-face Painter2D antialias seams; vertex colors
            // interpolate the continuous light field. Chunking respects UInt16 indices.
            projections?.Clear();
            Vector2 Project(NavPoint p)
            {
                if(projections==null)return project(p);
                var key=(p.X,p.Z);if(projections.TryGetValue(key,out var found))return found;
                var value=project(p);projections.Add(key,value);return value;
            }
            const int chunkFaces=16000;
            for(int first=0;first<faces.Count;first+=chunkFaces){
                int count=Math.Min(chunkFaces,faces.Count-first);var mesh=context.Allocate(count*3,count*3);
                void VertexAt(Vector2 point,double opacity,double highlight){var color=Color.Lerp(OrbitalTheme.Cyan,Color.white,(float)highlight);color.a=Mathf.Clamp01((float)opacity);mesh.SetNextVertex(new Vertex{position=new Vector3(point.x,point.y,Vertex.nearZ),tint=color});}
                for(int i=0;i<count;i++){
                    var f=faces[first+i];var a=Project(f.A);var b=Project(f.B);var c=Project(f.C);
                    VertexAt(a,f.Opacity,f.Highlight);VertexAt(b,f.OpacityB,f.HighlightB);VertexAt(c,f.OpacityC,f.HighlightC);
                    ushort index=(ushort)(i*3);mesh.SetNextIndex(index);
                    bool clockwise=(b.x-a.x)*(c.y-a.y)-(b.y-a.y)*(c.x-a.x)>=0;
                    mesh.SetNextIndex((ushort)(index+(clockwise?1:2)));mesh.SetNextIndex((ushort)(index+(clockwise?2:1)));
                }
            }
        }
        internal static void DrawScanLine(Painter2D painter,Vector2 from,Vector2 to,float opacity,float width)
        {var color=OrbitalTheme.Cyan;color.a=opacity;painter.strokeColor=color;painter.lineWidth=width;painter.BeginPath();painter.MoveTo(from);painter.LineTo(to);painter.Stroke();}
    }
}
