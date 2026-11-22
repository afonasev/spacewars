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
        internal static readonly Color InvalidOrbitalPreview = new Color(1,.3f,.15f);
        private int scanTargetAbility=1;
        private double TargetPreviewRadius=>NuclearTargeting?ScanData.orbitalNuclearRadius:RepairNanobotsTargeting?ScanData.orbitalNanobotsRadius:scanTargetAbility==OrbitalAbilities.Stasis?ScanData.orbitalStasisRadius:scanTargetAbility==OrbitalAbilities.SystemFailure?ScanData.orbitalSystemFailureRadius:scanTargetAbility==2?ScanData.orbitalMineBlastRadius:ScanData.orbitalScanRadius;
        private bool BoostTargeting=>scanTargetAbility==OrbitalAbilities.AcceleratedProduction;
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
            if(ReinforcementTargeting&&ability!=OrbitalAbilities.Reinforcement)CancelScanTarget();
            abilityTargetFailure=null;abilityCompletionUntil=0;scanTargetAbility=ability;scanTargetPad=pad;scanTargeting=true;scanReleased=false;scanGeneration=generation;
            if(orbitalOpen)CloseOrbital();input.ClearMode();battleGestures.Cancel();battleRing.style.display=DisplayStyle.None;root.Focus();
            notice=""; // The persistent targeting header owns ability instructions.
        }
        private void BeginMineTarget(bool pad)=>BeginOrbitalTarget(pad,2);
        private bool CancelScanTarget()
        {
            if(!scanTargeting)return false;ReturnReinforcementPayment();abilityTargetFailure=null;abilityCompletionUntil=0;scanTargeting=false;scanPendingSequence=-1;scanAppliedTick=-1;notice="";
            compactMap?.SetScanPreview(null,0);tacticalMap?.SetScanPreview(null,0);
            world?.HideReinforcementPreviewModels();
            compactMap?.SetReinforcementPreview(null,default,0,false);tacticalMap?.SetReinforcementPreview(null,default,0,false);return true;
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
            if(scanTargetAbility==OrbitalAbilities.ControlHack&&entityId==0){notice="Нужна видимая вражеская шахта или юнит";abilityTargetFailure=notice;return;}
            if(scanTargetAbility==OrbitalAbilities.ControlHack){
                string feedback=HackTargetFeedback(entityId,out bool valid);
                if(!valid){notice=feedback;abilityTargetFailure=feedback;return;}
            }
            if(BoostTargeting){
                if(entityId==0)entityId=BoostTargetAt(point);
                string feedback=BoostTargetFeedback(entityId,out bool valid);
                if(!valid){abilityTargetFailure=feedback;return;}
            }
            if(scanTargetAbility==OrbitalAbilities.SystemFailure&&!view.Vision.IsVisible(point)){notice="Сбой систем — только в текущем обзоре";abilityTargetFailure=notice;return;}
            if(scanTargetAbility==2&&view.Vision.IsVisible(point)&&MineTooCloseToVisibleEnemy(point))return;
            if(ReinforcementTargeting||scanTargetAbility==OrbitalAbilities.Stasis||RepairNanobotsTargeting||NuclearTargeting)
            {
                var feedback=AbilityTargetFeedback(point,entityId,out bool allowed);
                if(!allowed){abilityTargetFailure=feedback;return;}
            }
            abilityTargetFailure=null;
            var intent=new PlayableCommand(generation,0,LocalOwnerId,PlayableCommandKind.UseOrbital,scanTargetAbility==OrbitalAbilities.ControlHack||BoostTargeting||scanTargetAbility==OrbitalAbilities.Stasis&&StasisHawk(entityId)!=null?new[]{entityId}:null,target:point,targetId:scanTargetAbility);
            if((RepairNanobotsTargeting||NuclearTargeting||scanTargetAbility==OrbitalAbilities.Stasis&&StasisHawk(entityId)==null) &&
                TryRepairTarget(point,out var repairLocation))
                intent=intent.WithResolvedLocation(repairLocation);
            QueueHumanIntent(intent,(sent,result)=>{
            if(!result.Accepted){notice=Friendly(result.Status);return;}
            scanPendingSequence=sent.Sequence;scanPendingPoint=point;scanPendingTab=tab;noticeSequence=sent.Sequence;});
        }
        private void ObserveScanReceipts(IEnumerable<PlayableCommandReceipt> receipts)
        {
            ObserveReinforcementReceipts(receipts);
            if(scanPendingSequence<0)return;
            var receipt=receipts.FirstOrDefault(r=>r.Sequence==scanPendingSequence&&r.OwnerId==LocalOwnerId);if(receipt==null)return;
            if(receipt.Status!=PlayableCommandStatus.Applied){
                scanPendingSequence=-1;
                notice=scanTargetAbility==2
                    ? view.Vision.IsVisible(scanPendingPoint)&&MineTooCloseToVisibleEnemy(scanPendingPoint)?MineTooCloseMessage:"Здесь нельзя установить мину"
                    : ReinforcementTargeting&&receipt.Status==PlayableCommandStatus.InvalidTarget?"Здесь нельзя разместить всю волну":Friendly(receipt.Status);
                abilityTargetFailure=notice;
                return;
            }
            scanAppliedTick=receipt.AppliedTick;CompleteScanReceipt();
        }
        private void CompleteScanReceipt()
        {
            // Receipt draining can race a snapshot read. Do not close TAB onto the
            // older fog projection; wait until this owner's applied state arrives.
            if(scanAppliedTick<0||view==null||view.Tick<scanAppliedTick||!view.Orbital.Abilities.Any(a=>a.Id==scanTargetAbility&&a.Remaining>0))return;
            scanPendingSequence=scanAppliedTick=-1;
            if(scanPendingTab){CloseMap();if(scanTargetAbility!=OrbitalAbilities.ControlHack)FocusScanPoint(scanPendingPoint);}
            abilityCompletionUntil=Time.unscaledTimeAsDouble+ScanData.orbitalUiPulseSeconds;abilityTargetFailure=null;scanTargeting=false;notice="";compactMap?.SetScanPreview(null,0);tacticalMap?.SetScanPreview(null,0);
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
            if(pad.startButton.wasPressedThisFrame){if(!ReinforcementTargeting)CancelScanTarget();(localCoordinator??this).PauseFrom(this,true);}
            else if(pad.buttonEast.wasPressedThisFrame)CancelScanTarget();
            else if(pad.selectButton.wasPressedThisFrame)ToggleMap();
            else if(pad.buttonSouth.wasPressedThisFrame)ConfirmOrbitalTarget(padCursorGround,mapOpen,mapOpen?0:OrdinaryPadHit());
            return true;
        }
        private void UpdateScanPresentation()
        {
            if(root==null||view==null)return;
            if(scanGeneration!=generation){reinforcementCancelling=false;CancelScanTarget();}
            else if(!CanOrbital&&!ReinforcementTargeting)CancelScanTarget();
            RestoreReinforcementTargeting();
            CompleteScanReceipt();
            UpdateAbilityTargetingHud();
            if(scanLayer==null){scanLayer=new VisualElement{name="orbital-scan-world",pickingMode=PickingMode.Ignore};AbsoluteFill(scanLayer);scanLayer.style.overflow=Overflow.Hidden;scanLayer.generateVisualContent+=DrawScanWorld;root.Insert(0,scanLayer);}
            NavPoint? preview=ScanPreviewPoint();UpdateRepairSigns();UpdateNuclearPresentation();bool paint=view.Orbital.NuclearStrikes.Count>0||view.Orbital.RepairRims.Count>0||repairSigns.Count>0||view.ScanTriangles.Count>0||view.ScanLines.Count>0||preview.HasValue||scanTargeting&&(scanTargetAbility==OrbitalAbilities.ControlHack||BoostTargeting);
            bool show=paint&&!mapOpen&&!inLobby&&!preparing&&!MenuOwnsInput&&!ResultsVisible;
            scanLayer.style.display=show?DisplayStyle.Flex:DisplayStyle.None;if(show)scanLayer.MarkDirtyRepaint();if(!paint)scanProjectionCache.Clear();
            SetOrbitalMapPreview(compactMap,preview);
            SetOrbitalMapPreview(tacticalMap,preview);
            bool reinforcement=ReinforcementTargeting&&preview.HasValue;
            bool valid=false;
            var points=reinforcement?ReinforcementPreview(preview.Value,out valid):null;
            var kind=view.Orbital.ReinforcementQuote.Kind;
            bool showModels=reinforcement&&show&&!orbitalOpen&&CanOrbital;
            world?.SetReinforcementPreviewModels(showModels?points:null,kind,
                showModels&&RadialActionPresentation.Upgraded(kind,view.OwnerResearch),valid,cameraView);
            double footprint=reinforcement?PlayableUnitRules.Radius(profile,kind):0;
            compactMap?.SetReinforcementPreview(points,kind,footprint,valid);
            tacticalMap?.SetReinforcementPreview(points,kind,footprint,valid);
        }
        private void SetOrbitalMapPreview(PlayableMapSurface map,NavPoint? point)
        {
            if(map==null)return;
            int? count=point.HasValue&&scanTargetAbility==OrbitalAbilities.Stasis?StasisPreviewTargets(point.Value).Length:(int?)null;
            bool blocked=point.HasValue&&(count.HasValue?count.Value==0:(scanTargetAbility==2||RepairNanobotsTargeting||NuclearTargeting)&&!abilityTargetValid);
            map.SetNuclearPreview(NuclearTargeting&&point.HasValue,blocked?abilityTargetStatus?.text:null);
            map.SetScanPreview(ReinforcementTargeting?null:point,TargetPreviewRadius,blocked,count);
        }
        private PlayableEntitySnapshot StasisHawk(int id) =>
            id == 0 ? null : view.Entities.FirstOrDefault(unit => unit.Id == id && PlayableUnitRules.Aircraft(unit.Kind));

        private int[] StasisPreviewTargets(NavPoint point,int hawkId = 0)
        {
            NavLocation? ground = hawkId == 0 && TryRepairTarget(point,out var location) ? location : (NavLocation?)null;
            return OrbitalStasis.SelectVisible(point,profile,ScanData,view.Vision,view.Entities,hawkId,ground);
        }
        private void DrawStasisTargets(MeshGenerationContext context,NavPoint center,PlayableEntitySnapshot hawk)
        {
            var painter=context.painter2D;
            painter.strokeColor=OrbitalTheme.Cyan;
            painter.lineWidth=(float)ScanData.orbitalMinePreviewWidth;
            foreach(int id in StasisPreviewTargets(center,hawk?.Id??0))
            {
                if(!world.Actors.TryGetValue(id,out var actor))continue;
                var unit=view.Entities.First(u=>u.Id==id);
                float radius=(float)PlayableUnitRules.Radius(profile,unit.Kind)*1.2f;
                painter.BeginPath();
                for(int i=0;i<=32;i++)
                {
                    float angle=i*Mathf.PI/16;
                    var at=cameraView.WorldToViewportPoint(actor.Root.position+new Vector3(radius*Mathf.Cos(angle),.08f,radius*Mathf.Sin(angle)));
                    var pixel=new Vector2(at.x*root.contentRect.width,(1-at.y)*root.contentRect.height);
                    if(i==0)painter.MoveTo(pixel);else painter.LineTo(pixel);
                }
                painter.Stroke();
            }
        }
        private void DrawStasisAirRing(Painter2D painter,PlayableEntitySnapshot hawk)
        {
            painter.strokeColor=abilityTargetValid?OrbitalTheme.Cyan:InvalidOrbitalPreview;
            painter.lineWidth=(float)ScanData.orbitalMinePreviewWidth;
            float height=(float)(hawk.Altitude + PlayableUnitRules.AircraftBodyHeight(profile,hawk.Kind)/2);
            painter.BeginPath();
            for(int i=0;i<64;i++)
            {
                float angle=i*Mathf.PI/32;
                var pixel=RepairPixel(new Vector3((float)hawk.Position.X+Mathf.Cos(angle)*(float)TargetPreviewRadius,
                    height,(float)hawk.Position.Z+Mathf.Sin(angle)*(float)TargetPreviewRadius));
                if(i==0)painter.MoveTo(pixel);else painter.LineTo(pixel);
            }
            painter.ClosePath();
            painter.Stroke();
        }

        private NavPoint? ScanPreviewPoint()
        {
            if(!scanTargeting||scanTargetAbility==OrbitalAbilities.ControlHack||BoostTargeting)return null;
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
            DrawHackTarget(context);
            DrawScanFaces(context,view.ScanTriangles,Project,scanProjectionCache);
            foreach(var segment in view.ScanLines)DrawScanLine(painter,Project(segment.From),Project(segment.To),(float)segment.Opacity,(float)ScanData.orbitalScanLineWidth);
            DrawRepairEffects(painter);
            DrawNuclearStrikes(context);
            if(ReinforcementTargeting)return;
            var preview=ScanPreviewPoint();
            if(preview.HasValue&&NuclearTargeting)
            {
                var origin=TryRepairTarget(preview.Value,out var nuclearCenter)?NuclearGround(nuclearCenter):world.Point(preview.Value);
                NuclearTargetArtwork.Draw(context,angle=>RepairPixel(origin+new Vector3(Mathf.Cos(angle)*(float)TargetPreviewRadius,0,Mathf.Sin(angle)*(float)TargetPreviewRadius)),RepairPixel(origin),abilityTargetValid,false,abilityTargetValid?null:abilityTargetStatus?.text);
                return;
            }
            if(preview.HasValue&&RepairNanobotsTargeting)
            {
                DrawRepairPreview(painter,preview.Value);
                return;
            }
            if(preview.HasValue&&scanTargetAbility==OrbitalAbilities.Stasis)
            {
                var hawk=StasisHawk(abilityHoveredTarget);
                DrawStasisTargets(context,preview.Value,hawk);
                if(hawk!=null)
                {
                    DrawStasisAirRing(painter,hawk);
                    return;
                }
            }
            if(preview.HasValue&&scanTargetAbility==OrbitalAbilities.SystemFailure)DrawSystemFailureTargets(context,Project,preview.Value);
            if(preview.HasValue)for(int i=0;i<128;i++){
                NavPoint At(int step)=>new NavPoint(preview.Value.X+Math.Cos(step*Math.PI/64)*TargetPreviewRadius,preview.Value.Z+Math.Sin(step*Math.PI/64)*TargetPreviewRadius);
                if(scanTargetAbility==2||scanTargetAbility==OrbitalAbilities.SystemFailure||scanTargetAbility==OrbitalAbilities.Stasis){
                    painter.strokeColor=(scanTargetAbility==OrbitalAbilities.SystemFailure?view.Vision.IsVisible(preview.Value):abilityTargetValid)?OrbitalTheme.Cyan:InvalidOrbitalPreview;painter.lineWidth=(float)ScanData.orbitalMinePreviewWidth;painter.BeginPath();painter.MoveTo(Project(At(i)));painter.LineTo(Project(At(i+1)));painter.Stroke();
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
        internal static void DrawScanLine(Painter2D painter,Vector2 from,Vector2 to,float opacity,float width,Color? tint=null)
        {var color=tint??OrbitalTheme.Cyan;color.a=opacity;painter.strokeColor=color;painter.lineWidth=width;painter.BeginPath();painter.MoveTo(from);painter.LineTo(to);painter.Stroke();}
    }
}
