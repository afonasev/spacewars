using System;
using System.Linq;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Spacewars.Presentation
{
    public sealed partial class PlayableBootstrap
    {
        private VisualElement abilityTargetHeader,abilityTargetControls;
        private OrbitalPlaceholderImage abilityTargetIcon;
        private Label abilityTargetTitle,abilityTargetStatus,abilityTargetApply,abilityTargetCancel;
        private NativeControllerGlyph abilityApplyGlyph,abilityCancelGlyph;
        private int abilityHoveredTarget;
        private bool abilityTargetValid;
        private string abilityTargetFailure;
        private double abilityCompletionUntil;
        private NavPoint? abilityPreviousPoint;
        private int abilityPreviousTarget;

        private void EnsureAbilityTargetingHud()
        {
            if(abilityTargetHeader!=null)return;
            var art=RadialCardProfile.Load();
            VisualElement Panel(string name)
            {
                var panel=new VisualElement{name=name,pickingMode=PickingMode.Ignore};panel.style.position=Position.Absolute;
                panel.style.backgroundColor=OrbitalTheme.Panel;panel.style.flexDirection=FlexDirection.Row;panel.style.alignItems=Align.Center;
                panel.style.borderLeftWidth=art.titleAccentWidth;panel.style.borderRightWidth=panel.style.borderTopWidth=panel.style.borderBottomWidth=1;
                panel.style.borderLeftColor=OrbitalTheme.Cyan;panel.style.borderRightColor=panel.style.borderTopColor=panel.style.borderBottomColor=OrbitalTheme.Line;
                panel.style.paddingLeft=panel.style.paddingRight=art.statColumnPadding;panel.style.paddingTop=panel.style.paddingBottom=art.labelGap;
                panel.style.display=DisplayStyle.None;root.Add(panel);return panel;
            }
            Label Label(string name,float size)
            {
                var label=new Label{name=name,pickingMode=PickingMode.Ignore};label.style.color=OrbitalTheme.Ink;label.style.fontSize=size;
                label.style.whiteSpace=WhiteSpace.Normal;label.style.marginLeft=label.style.marginRight=label.style.marginTop=label.style.marginBottom=0;return label;
            }
            abilityTargetHeader=Panel("ability-target-header");
            abilityTargetIcon=new OrbitalPlaceholderImage(1){pickingMode=PickingMode.Ignore};abilityTargetIcon.style.width=abilityTargetIcon.style.height=art.utilityIconSize;abilityTargetIcon.style.flexShrink=0;abilityTargetIcon.style.marginRight=art.statColumnPadding;abilityTargetHeader.Add(abilityTargetIcon);
            var body=new VisualElement{pickingMode=PickingMode.Ignore};body.style.flexGrow=1;body.style.flexShrink=1;body.style.minWidth=0;abilityTargetHeader.Add(body);
            abilityTargetTitle=Label("ability-target-title",art.valueSize);abilityTargetTitle.style.unityFontStyleAndWeight=FontStyle.Bold;body.Add(abilityTargetTitle);
            abilityTargetStatus=Label("ability-target-status",art.textSize);body.Add(abilityTargetStatus);
            abilityTargetControls=Panel("ability-target-controls");abilityTargetControls.style.flexWrap=Wrap.Wrap;
            abilityApplyGlyph=new NativeControllerGlyph("A",()=>SeatPad,contextual:false);abilityTargetControls.Add(abilityApplyGlyph);
            abilityTargetApply=Label("ability-target-apply",art.valueSize);abilityTargetApply.style.marginRight=art.padding;abilityTargetControls.Add(abilityTargetApply);
            abilityCancelGlyph=new NativeControllerGlyph("B",()=>SeatPad,contextual:false);abilityTargetControls.Add(abilityCancelGlyph);
            abilityTargetCancel=Label("ability-target-cancel",art.valueSize);abilityTargetControls.Add(abilityTargetCancel);
        }
        private NavPoint? AbilityCursorPoint(out int target)
        {
            target=0;if(!scanTargeting)return null;
            if(scanTargetPad){target=mapOpen?0:OrdinaryPadHit();return padCursorGround;}
            var mouse=input?.AssignedMouse??Mouse.current;if(mouse==null)return null;
            var at=mouse.position.ReadValue();int map=MapAt(at);
            if(map==3||map==0&&(OverUi(at)||!cameraView.pixelRect.Contains(at)))return null;
            if(map==0)target=Pick(at,false);
            return map>0?MapGround(map,at):Ground(at);
        }
        private string HackTargetFeedback(int id,out bool valid)
        {
            valid=false;
            var entity=view.Entities.FirstOrDefault(e=>e.Id==id);
            var building=view.Buildings.FirstOrDefault(e=>e.Id==id);
            if(entity==null&&building==null)return "Наведитесь на вражеского юнита или шахту";
            var point=entity!=null?entity.Position:building.Position;
            if(!view.Vision.IsVisible(point))return "Цель должна быть в текущем обзоре";
            var owner=entity!=null?entity.Owner:building.Owner;
            if(!IsOpponent(owner))return owner==view.Owner?"Нельзя захватить свой объект":"Нельзя захватить союзный или нейтральный объект";
            if(building!=null&&building.Kind!=PlayableBuildingKind.Mine)return "Из зданий можно захватить только шахту";
            double ratio=entity!=null?PlayableUnitRules.HealthRatio(profile,entity):building.Health/TerritoryRules.MaximumHealth(profile,building);
            double threshold=ScanData.orbitalHackHealthPercent;
            if(ratio<=0)return "Цель уничтожена";
            if(ratio*100>=threshold)return "HP "+(ratio*100).ToString("0.#")+"% — нужно ниже "+threshold.ToString("0.#")+"%";
            valid=true;return (entity!=null?"Юнит":"Шахта")+" · HP "+(ratio*100).ToString("0.#")+"% · можно захватить";
        }
        private string AbilityTargetFeedback(NavPoint? point,int target,out bool valid)
        {
            valid=false;
            if(!point.HasValue)return scanTargetAbility==OrbitalAbilities.ControlHack?"Наведитесь на вражеского юнита или шахту":"Наведитесь на точку на поле или карте";
            if(scanTargetAbility==OrbitalAbilities.ControlHack)return HackTargetFeedback(target,out valid);
            if(scanTargetAbility==OrbitalAbilities.SystemFailure&&!view.Vision.IsVisible(point.Value))return "Сбой систем — только в текущем обзоре";
            if(scanTargetAbility==2&&!view.Vision.IsVisible(point.Value))return "Установка мины — только в текущем обзоре";
            valid=true;
            return (scanTargetAbility==OrbitalAbilities.SystemFailure?"Два ближайших больших здания и все башни · радиус ":scanTargetAbility==2?"Радиус взрыва: ":"Радиус обзора: ")+TargetPreviewRadius.ToString("0.#")+" м";
        }
        private void UpdateAbilityTargetingHud()
        {
            EnsureAbilityTargetingHud();
            if(scanTargeting)noticeLabel.style.display=DisplayStyle.None; // Avoid a second instruction below the controls.
            bool completed=!scanTargeting&&Time.unscaledTimeAsDouble<abilityCompletionUntil;
            bool show=(scanTargeting||completed)&&CanOrbital&&!orbitalOpen&&!ResultsVisible;
            abilityTargetHeader.style.display=show?DisplayStyle.Flex:DisplayStyle.None;
            abilityTargetControls.style.display=show&&scanTargeting?DisplayStyle.Flex:DisplayStyle.None;
            if(!show){abilityHoveredTarget=0;return;}
            var point=AbilityCursorPoint(out int target);
            if(target==0&&point.HasValue&&scanTargetAbility==OrbitalAbilities.ControlHack)target=HackTargetAt(point.Value);
            if(target!=abilityPreviousTarget||point.HasValue!=abilityPreviousPoint.HasValue||point.HasValue&&abilityPreviousPoint.HasValue&&
                (Math.Abs(point.Value.X-abilityPreviousPoint.Value.X)>double.Epsilon||Math.Abs(point.Value.Z-abilityPreviousPoint.Value.Z)>double.Epsilon))abilityTargetFailure=null;
            abilityPreviousTarget=target;abilityPreviousPoint=point;abilityHoveredTarget=target;
            string feedback=AbilityTargetFeedback(point,target,out abilityTargetValid);
            abilityTargetIcon.SetId(scanTargetAbility);
            abilityTargetTitle.text=OrbitalAbilities.Name(scanTargetAbility)+(completed?" · применено":" · выбор цели");
            abilityTargetStatus.text=completed?(scanTargetAbility==OrbitalAbilities.SystemFailure?"Системы отключены":scanTargetAbility==OrbitalAbilities.ControlHack?"Контроль передан вам":scanTargetAbility==2?"Мина установлена":"Область сканируется"):
                scanPendingSequence>=0?"Применение…":abilityTargetFailure??feedback;
            abilityTargetStatus.style.color=completed||abilityTargetValid?OrbitalTheme.Cyan:OrbitalTheme.Amber;
            string action=scanTargetAbility==OrbitalAbilities.SystemFailure?"Отключить":scanTargetAbility==OrbitalAbilities.ControlHack?"Захватить":scanTargetAbility==2?"Установить":"Сканировать";
            abilityTargetApply.text=scanTargetPad?action:"ЛКМ — "+action;
            abilityTargetCancel.text=scanTargetPad?"Отменить":"ПКМ — отменить";
            // Persist the mode for each badge's recurring Refresh, rather than racing
            // its scheduled display update with a temporary style override.
            abilityApplyGlyph.SetMenuVisibility(scanTargetPad,SeatPad);
            abilityCancelGlyph.SetMenuVisibility(scanTargetPad,SeatPad);
            abilityTargetApply.style.color=abilityTargetValid&&scanPendingSequence<0?OrbitalTheme.Ink:OrbitalTheme.Muted;
            var art=RadialCardProfile.Load();float gap=art.statColumnPadding,w=root.contentRect.width,h=root.contentRect.height;
            float width=Mathf.Max(0,Mathf.Min(art.dockWidth+art.utilityIconSize,w-gap*2));
            abilityTargetHeader.style.left=gap;abilityTargetHeader.style.width=width;
            float headerTop=Mathf.Max(gap,top.worldBound.yMax-root.worldBound.yMin+gap);
            var shared=(localCoordinator??this).sharedMapFrame;
            if(shared!=null&&shared.resolvedStyle.display!=DisplayStyle.None){
                var a=shared.worldBound;float left=root.worldBound.xMin+gap;
                float height=Mathf.Max(art.utilityIconSize+gap*2,abilityTargetHeader.resolvedStyle.height);
                if(a.xMin<left+width&&a.xMax>left&&a.yMin<root.worldBound.yMin+headerTop+height&&a.yMax>root.worldBound.yMin+headerTop)
                    headerTop=a.yMax-root.worldBound.yMin+gap;
            }
            abilityTargetHeader.style.top=headerTop;
            abilityTargetControls.style.left=Mathf.Max(gap,(w-width)/2);abilityTargetControls.style.width=width;
            float bottom=gap;
            foreach(var anchor in new[]{armyRegion,(localCoordinator??this).sharedMapFrame,localHumanCount==1?compactMap?.parent:null}){
                if(anchor==null||anchor.resolvedStyle.display==DisplayStyle.None||anchor.worldBound.height<=0)continue;
                var a=anchor.worldBound;float left=root.worldBound.xMin+Mathf.Max(gap,(w-width)/2);
                if(a.xMin<left+width&&a.xMax>left&&a.yMin>root.worldBound.yMin&&a.yMin<root.worldBound.yMax)bottom=Mathf.Max(bottom,h-(a.yMin-root.worldBound.yMin)+gap);
            }
            abilityTargetControls.style.bottom=bottom;
            // Keep the map usable but layer hints above map artwork, inside this seat root.
            abilityTargetHeader.BringToFront();if(scanTargeting)abilityTargetControls.BringToFront();
        }
        private void DrawHackTarget(MeshGenerationContext context)
        {
            if(!scanTargeting||scanTargetAbility!=OrbitalAbilities.ControlHack||abilityHoveredTarget==0||mapOpen)return;
            var entity=view.Entities.FirstOrDefault(e=>e.Id==abilityHoveredTarget);
            var building=view.Buildings.FirstOrDefault(b=>b.Id==abilityHoveredTarget);
            if(entity==null&&building==null)return;
            var point=entity!=null?world.EntityPoint(entity):world.Point(building.Position);
            if(!view.Vision.IsVisible(entity!=null?entity.Position:building.Position))return;
            var projected=cameraView.WorldToViewportPoint(point);if(projected.z<=0)return;
            float radius=(float)(entity!=null?PlayableUnitRules.Radius(profile,entity.Kind):TerritoryRules.Radius(profile,building.Kind));
            var edge=cameraView.WorldToViewportPoint(point+cameraView.transform.right*radius);
            float size=Mathf.Max(RadialCardProfile.Load().utilityIconSize/2,Mathf.Abs(edge.x-projected.x)*root.contentRect.width);
            var center=new Vector2(projected.x*root.contentRect.width,(1-projected.y)*root.contentRect.height);
            var art=RadialCardProfile.Load();var p=context.painter2D;p.strokeColor=abilityTargetValid?OrbitalTheme.Cyan:OrbitalTheme.Amber;p.lineWidth=art.opticalEdgeInset;
            foreach(int x in new[]{-1,1})foreach(int y in new[]{-1,1}){
                var corner=center+new Vector2(x,y)*size;
                p.BeginPath();p.MoveTo(corner-new Vector2(x*Mathf.Min(size/2,art.statColumnPadding),0));p.LineTo(corner);p.LineTo(corner-new Vector2(0,y*Mathf.Min(size/2,art.statColumnPadding)));p.Stroke();
            }
        }
    }
}
