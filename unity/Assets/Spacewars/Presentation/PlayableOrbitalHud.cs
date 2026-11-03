using System;
using System.Linq;
using Spacewars.Input;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
namespace Spacewars.Presentation
{
    public sealed partial class PlayableBootstrap
    {
        private VisualElement orbitalPanel,orbitalSlots,orbitalLevelRow,orbitalModal,orbitalCards,orbitalControls;
        private Label orbitalLevel,orbitalLtHint,orbitalXpTooltip,orbitalMenuTitle,orbitalHint;
        private Button orbitalLevelButton,orbitalSwitch,orbitalClose;
        private OrbitalProgressRing orbitalProgress;
        private OrbitalBattleRing orbitalRing;
        private bool orbitalOpen,orbitalMenuPad,orbitalShowChoices,orbitalReleaseGate,orbitalStickReady,orbitalLevelHovered;
        private string orbitalControlsSignature;private int orbitalFocus;private string orbitalSignature;private long orbitalGeneration=-1;
        private bool CanOrbital=>view!=null&&!spectatorMode&&!BotSeat&&!view.OwnerEliminated&&!inLobby&&!preparing&&!returningToLobby&&!paused&&!chatOpen&&!MenuOwnsInput&&localInputFocused&&input?.CommandInputEnabled==true&&view.Outcome==PlayableMatchOutcome.Playing;
        private void EnsureOrbitalUi()
        {
            if(orbitalPanel!=null)return;
            orbitalPanel=new VisualElement{name="orbital-panel",pickingMode=PickingMode.Ignore};orbitalPanel.style.position=Position.Absolute;orbitalPanel.style.flexDirection=FlexDirection.Column;orbitalPanel.style.alignItems=Align.FlexStart;
            orbitalSlots=new VisualElement{name="orbital-slots",pickingMode=PickingMode.Ignore};orbitalSlots.style.alignItems=Align.Center;orbitalSlots.style.flexShrink=0;orbitalPanel.Add(orbitalSlots);
            orbitalLevelRow=new VisualElement{name="orbital-level-row",pickingMode=PickingMode.Ignore};orbitalLevelRow.style.position=Position.Relative;orbitalLevelRow.style.flexShrink=0;orbitalPanel.Add(orbitalLevelRow);
            orbitalLevelButton=new Button(()=>OpenOrbital(false)){name="orbital-level-button"};orbitalLevelButton.style.position=Position.Absolute;orbitalLevelButton.style.left=orbitalLevelButton.style.top=0;orbitalLevelButton.style.backgroundColor=Color.clear;orbitalLevelButton.style.backgroundImage=StyleKeyword.None;
            orbitalLevelButton.style.borderLeftWidth=orbitalLevelButton.style.borderRightWidth=orbitalLevelButton.style.borderTopWidth=orbitalLevelButton.style.borderBottomWidth=0;
            orbitalLevelButton.style.marginLeft=orbitalLevelButton.style.marginRight=orbitalLevelButton.style.marginTop=orbitalLevelButton.style.marginBottom=orbitalLevelButton.style.paddingLeft=orbitalLevelButton.style.paddingRight=orbitalLevelButton.style.paddingTop=orbitalLevelButton.style.paddingBottom=0;
            orbitalLevelButton.RegisterCallback<PointerEnterEvent>(_=>orbitalLevelHovered=true);orbitalLevelButton.RegisterCallback<PointerLeaveEvent>(_=>orbitalLevelHovered=false);
            orbitalLevelButton.RegisterCallback<PointerDownEvent>(e=>e.StopPropagation());orbitalLevelButton.RegisterCallback<PointerUpEvent>(e=>e.StopPropagation());orbitalLevelRow.Add(orbitalLevelButton);
            orbitalProgress=new OrbitalProgressRing{name="orbital-progress"};orbitalProgress.style.position=Position.Absolute;orbitalProgress.style.left=orbitalProgress.style.right=orbitalProgress.style.top=orbitalProgress.style.bottom=0;orbitalLevelButton.Add(orbitalProgress);
            orbitalLevel=new Label("0"){name="orbital-level",pickingMode=PickingMode.Ignore};orbitalLevel.style.position=Position.Absolute;orbitalLevel.style.left=orbitalLevel.style.right=orbitalLevel.style.top=orbitalLevel.style.bottom=0;orbitalLevel.style.unityTextAlign=TextAnchor.MiddleCenter;orbitalLevel.style.unityFontStyleAndWeight=FontStyle.Bold;
            orbitalLevel.style.marginLeft=orbitalLevel.style.marginRight=orbitalLevel.style.marginTop=orbitalLevel.style.marginBottom=orbitalLevel.style.paddingLeft=orbitalLevel.style.paddingRight=orbitalLevel.style.paddingTop=orbitalLevel.style.paddingBottom=0;orbitalLevelButton.Add(orbitalLevel);
            orbitalLtHint=new Label("LT"){name="orbital-lt-hint",pickingMode=PickingMode.Ignore};orbitalLtHint.style.position=Position.Absolute;orbitalLtHint.style.fontSize=10;orbitalLtHint.style.color=Ink;orbitalLevelRow.Add(orbitalLtHint);root.Add(orbitalPanel);
            orbitalXpTooltip=new Label{name="orbital-xp-tooltip",pickingMode=PickingMode.Ignore};orbitalXpTooltip.style.position=Position.Absolute;orbitalXpTooltip.style.fontSize=RadialCardProfile.Load().textSize;orbitalXpTooltip.style.color=Ink;orbitalXpTooltip.style.backgroundColor=OrbitalTheme.Panel;orbitalXpTooltip.style.paddingLeft=orbitalXpTooltip.style.paddingRight=8;orbitalXpTooltip.style.paddingTop=orbitalXpTooltip.style.paddingBottom=5;orbitalXpTooltip.style.whiteSpace=WhiteSpace.Normal;orbitalXpTooltip.style.display=DisplayStyle.None;root.Add(orbitalXpTooltip);
            orbitalModal=new VisualElement{name="orbital-modal"};orbitalModal.style.position=Position.Absolute;orbitalModal.style.left=orbitalModal.style.right=orbitalModal.style.top=orbitalModal.style.bottom=0;orbitalModal.style.backgroundColor=new Color(.01f,.025f,.04f,.87f);orbitalModal.style.display=DisplayStyle.None;
            orbitalModal.RegisterCallback<PointerDownEvent>(e=>e.StopPropagation());orbitalModal.RegisterCallback<PointerUpEvent>(e=>e.StopPropagation());orbitalModal.RegisterCallback<KeyDownEvent>(e=>{if(e.keyCode==KeyCode.Escape){CloseOrbital();e.StopImmediatePropagation();}});
            orbitalMenuTitle=new Label();orbitalMenuTitle.style.position=Position.Absolute;orbitalMenuTitle.style.top=24;orbitalMenuTitle.style.width=Length.Percent(100);orbitalMenuTitle.style.unityTextAlign=TextAnchor.MiddleCenter;orbitalMenuTitle.style.fontSize=20;orbitalModal.Add(orbitalMenuTitle);
            orbitalCards=new VisualElement{name="orbital-cards"};orbitalCards.style.position=Position.Absolute;orbitalCards.style.flexDirection=FlexDirection.Row;orbitalCards.style.justifyContent=Justify.Center;orbitalModal.Add(orbitalCards);
            orbitalRing=new OrbitalBattleRing{name="orbital-ring"};orbitalRing.InvokeAction=(id,hold)=>{if(orbitalMenuPad&&int.TryParse(id,out var value))SubmitOrbital(false,value);};orbitalModal.Add(orbitalRing);
            orbitalHint=new Label();orbitalHint.style.position=Position.Absolute;orbitalHint.style.bottom=44;orbitalHint.style.width=Length.Percent(100);orbitalHint.style.unityTextAlign=TextAnchor.MiddleCenter;orbitalHint.style.fontSize=12;orbitalModal.Add(orbitalHint);
            orbitalControls=new VisualElement{name="orbital-controls"};orbitalControls.style.position=Position.Absolute;orbitalControls.style.bottom=10;orbitalControls.style.left=orbitalControls.style.right=12;orbitalControls.style.flexDirection=FlexDirection.Row;orbitalControls.style.flexWrap=Wrap.Wrap;orbitalControls.style.justifyContent=Justify.Center;orbitalModal.Add(orbitalControls);
            orbitalClose=new Button(CloseOrbital);
            orbitalSwitch=new Button(()=>{if(!orbitalMenuPad)return;orbitalShowChoices=!orbitalShowChoices;orbitalFocus=0;orbitalSignature=null;RefreshOrbitalMenu();}){name="orbital-switch"};root.Add(orbitalModal);
        }
        private bool OpenOrbital()=>OpenOrbital(battleController);
        private bool OpenOrbital(bool controller)
        {
            if(!CanOrbital||signalWheelOpen||!controller&&(!SeatUsesKeyboard||view.Orbital.PendingLevel==0))return false;
            CancelScanTarget();EnsureOrbitalUi();orbitalReleaseGate=false;orbitalOpen=true;orbitalMenuPad=controller;orbitalShowChoices=view.Orbital.PendingLevel>0;orbitalFocus=0;orbitalStickReady=false;orbitalSignature=null;orbitalLevelHovered=false;EndSelectionRosterFocus();
            battleGestures.Cancel();battleRing.style.display=DisplayStyle.None;if(padAuxiliaryRing!=null)padAuxiliaryRing.style.display=DisplayStyle.None;
            orbitalXpTooltip.style.display=DisplayStyle.None;orbitalModal.style.display=DisplayStyle.Flex;orbitalModal.BringToFront();orbitalModal.focusable=true;orbitalModal.Focus();RefreshOrbitalMenu();return true;
        }
        private void CloseOrbital()
        {if(!orbitalOpen)return;orbitalOpen=false;orbitalReleaseGate=true;orbitalModal.style.display=DisplayStyle.None;battleGestures.Cancel();root.Focus();}
        private void SubmitOrbital(bool choose,int id)=>SubmitOrbitalCommand(choose,id,false);
        private void ApplyOrbitalFromHud(int id)=>SubmitOrbitalCommand(false,id,true);
        private void SubmitOrbitalCommand(bool choose,int id,bool fromHud)
        {
            if(!CanOrbital||!fromHud&&!orbitalOpen||fromHud&&(!SeatUsesKeyboard||orbitalOpen))return;
            if(choose?!view.Orbital.Options.Contains(id):view.Orbital.CommonRemaining>1e-9||!view.Orbital.Abilities.Any(a=>a.Id==id&&a.Remaining<=1e-9))return;
            if(!choose&&(id==1||id==2||id==OrbitalAbilities.SystemFailure||id==OrbitalAbilities.ControlHack)){BeginOrbitalTarget(!fromHud&&orbitalMenuPad,id);return;}
            var command=new PlayableCommand(generation,++sequence,LocalOwnerId,choose?PlayableCommandKind.ChooseOrbital:PlayableCommandKind.UseOrbital,null,targetId:id);
            var result=runtime.TrySubmit(command);noticeSequence=sequence;notice=result.Accepted?"":Friendly(result.Status);if(result.Accepted){if(orbitalOpen)CloseOrbital();else root.Focus();}
        }
        private bool OrbitalHudAtPanel(Vector2 point)
        {
            if(orbitalPanel==null||orbitalPanel.resolvedStyle.display==DisplayStyle.None)return false;
            return orbitalLevelButton.worldBound.Contains(point)||orbitalSlots.Children().Any(e=>e.worldBound.Contains(point));
        }
        private void UpdateOrbitalHud()
        {
            EnsureOrbitalUi();
            if(orbitalGeneration!=view.Generation){CloseOrbital();orbitalGeneration=view.Generation;orbitalSignature=null;orbitalLevelHovered=false;}
            bool visible=!inLobby&&!preparing&&!returningToLobby&&!ResultsVisible&&!spectatorMode&&!view.OwnerEliminated&&view.Outcome==PlayableMatchOutcome.Playing;
            orbitalPanel.style.display=visible?DisplayStyle.Flex:DisplayStyle.None;
            if(!CanOrbital){CloseOrbital();orbitalReleaseGate=false;}
            if(SeatUsesKeyboard&&orbitalReleaseGate){var mouse=input?.AssignedMouse??Mouse.current;if(mouse==null||!mouse.leftButton.isPressed&&!mouse.rightButton.isPressed)orbitalReleaseGate=false;}
            if(!visible){orbitalXpTooltip.style.display=DisplayStyle.None;return;}
            var state=view.Orbital;var data=profile.CopyData();float h=root.contentRect.height,w=root.contentRect.width;bool local=localHumanCount>1;bool right=local&&cameraView!=null&&cameraView.rect.center.x>.5f;
            var anchor=local?armyRegion:compactMap?.parent;float bottomY=h-6;
            if(anchor!=null&&anchor.resolvedStyle.display!=DisplayStyle.None&&anchor.worldBound.height>0)bottomY=anchor.worldBound.yMin-root.worldBound.yMin-6;
            var sharedMap=(localCoordinator??this).sharedMapFrame;
            if(local&&sharedMap!=null&&sharedMap.resolvedStyle.display!=DisplayStyle.None){
                var bounds=sharedMap.worldBound;float stackWidth=Mathf.Max((float)data.orbitalPanelWidth,(float)data.orbitalLevelFontSize*1.6f+(float)data.orbitalUiRingWidth*2+30);float stackLeft=right?root.worldBound.xMax-6-stackWidth:root.worldBound.xMin+6,stackRight=stackLeft+stackWidth;
                if(bounds.xMin<stackRight&&bounds.xMax>stackLeft&&bounds.yMin>root.worldBound.yMin&&bounds.yMin<root.worldBound.yMax)bottomY=Mathf.Min(bottomY,bounds.yMin-root.worldBound.yMin-6);
            }
            float gap=RadialCardProfile.Load().selectionGap,font=(float)data.orbitalLevelFontSize,icon=(float)data.orbitalIconSize;
            float header=Mathf.Max(36,top.worldBound.yMax-root.worldBound.yMin+gap),desired=font*1.6f+(float)data.orbitalUiRingWidth*2+state.Abilities.Count*(icon+gap);
            float fit=Mathf.Clamp((bottomY-header)/Mathf.Max(1,desired),.35f,1);font*=fit;icon*=fit;gap*=fit;
            float badge=font*1.6f+(float)data.orbitalUiRingWidth*2*fit;bool controller=battleController&&SeatPad!=null;
            float width=Mathf.Max((float)data.orbitalPanelWidth,badge+(controller?24+gap:0));
            orbitalPanel.style.width=width;orbitalLevelRow.style.width=width;orbitalLevelRow.style.height=badge;
            orbitalLevelButton.style.left=right?width-badge:0;
            orbitalLevelButton.style.width=orbitalLevelButton.style.height=orbitalLevelButton.style.minHeight=badge;orbitalLevelButton.style.borderTopLeftRadius=orbitalLevelButton.style.borderTopRightRadius=orbitalLevelButton.style.borderBottomLeftRadius=orbitalLevelButton.style.borderBottomRightRadius=badge/2;
            orbitalLevel.style.fontSize=state.Level==OrbitalEscalationState.MaxLevel?font*(float)data.orbitalUiMaxFontScale:font;orbitalLevel.text=state.Level==OrbitalEscalationState.MaxLevel?"MAX":state.Level.ToString();orbitalProgress.Fraction=state.Level==OrbitalEscalationState.MaxLevel?1:(float)(state.Progress/Math.Max(1,state.Threshold));orbitalProgress.LineWidth=(float)data.orbitalUiRingWidth*fit;orbitalProgress.Pending=state.PendingLevel>0;
            Color levelColor=state.PendingLevel>0?Color.Lerp(OrbitalTheme.Ink,OrbitalTheme.Amber,(float)(.5+.5*Math.Sin(Time.unscaledTimeAsDouble*2*Math.PI/data.orbitalUiPulseSeconds))):OrbitalTheme.Ink;
            orbitalLevel.style.color=levelColor;orbitalProgress.Color=state.PendingLevel>0?levelColor:Cyan;
            orbitalLevelButton.tooltip=state.PendingLevel>0?"Выбрать новую способность":"Эскалация";
            orbitalLtHint.text=NativeControllerGlyph.Symbol("LT",SeatPad);orbitalLtHint.style.display=controller?DisplayStyle.Flex:DisplayStyle.None;orbitalLtHint.style.left=right?width-badge-gap-24:badge+gap;orbitalLtHint.style.top=(badge-16)/2;
            var abilities=state.Abilities.Reverse().ToArray();string signature=string.Join(",",abilities.Select(a=>a.Id));
            if(orbitalSlots.userData as string!=signature){
                orbitalSlots.Clear();orbitalSlots.userData=signature;
                foreach(var ability in abilities){
                    int id=ability.Id;var button=new Button(()=>ApplyOrbitalFromHud(id)){name="orbital-slot-"+id};OrbitalTheme.StyleButton(button);button.style.flexShrink=0;button.style.backgroundColor=OrbitalTheme.Panel;button.style.backgroundImage=StyleKeyword.None;button.style.overflow=Overflow.Hidden;
                    button.style.marginLeft=button.style.marginRight=button.style.marginTop=0;button.style.paddingLeft=button.style.paddingRight=button.style.paddingTop=button.style.paddingBottom=0;
                    button.style.borderTopWidth=button.style.borderBottomWidth=button.style.borderLeftWidth=button.style.borderRightWidth=1;
                    button.RegisterCallback<PointerDownEvent>(e=>e.StopPropagation());button.RegisterCallback<PointerUpEvent>(e=>e.StopPropagation());
                    var sweep=new OrbitalCooldownSweep{name="cooldown"};AbsoluteFill(sweep);button.Add(sweep);
                    var image=new OrbitalPlaceholderImage(id){name="ability-artwork",pickingMode=PickingMode.Ignore};AbsoluteFill(image);button.Add(image);
                    var seconds=new Label{name="seconds",pickingMode=PickingMode.Ignore};AbsoluteFill(seconds);seconds.style.unityFontStyleAndWeight=FontStyle.Bold;seconds.style.unityTextAlign=TextAnchor.MiddleCenter;seconds.style.color=Ink;seconds.style.marginLeft=seconds.style.marginRight=seconds.style.marginTop=seconds.style.marginBottom=seconds.style.paddingLeft=seconds.style.paddingRight=seconds.style.paddingTop=seconds.style.paddingBottom=0;button.Add(seconds);orbitalSlots.Add(button);
                }
            }
            orbitalSlots.style.marginLeft=(right?width-badge:0)+(badge-icon)/2;
            foreach(var ability in abilities){
                var button=orbitalSlots.Q<Button>("orbital-slot-"+ability.Id);button.style.width=button.style.height=button.style.minHeight=icon;button.style.marginBottom=gap;
                float corner=(float)data.orbitalUiCornerRadius*fit;button.style.borderTopLeftRadius=button.style.borderTopRightRadius=button.style.borderBottomLeftRadius=button.style.borderBottomRightRadius=corner;
                double wait=Math.Max(ability.Remaining,state.CommonRemaining);bool cooling=wait>1e-9;
                var sweep=button.Q<OrbitalCooldownSweep>();sweep.Fraction=(float)(ability.Remaining>=state.CommonRemaining?1-ability.Recovery/Math.Max(.001,ability.Duration):1-state.CommonRemaining/Math.Max(.001,data.orbitalCommonSeconds));sweep.style.display=cooling?DisplayStyle.Flex:DisplayStyle.None;
                button.Q("ability-artwork").style.opacity=cooling?(float)data.orbitalUiCoolingArtworkOpacity:1;
                var seconds=button.Q<Label>("seconds");seconds.text=cooling?Math.Ceiling(wait).ToString():"";seconds.style.fontSize=icon*.45f;
                bool targeting=scanTargeting&&scanTargetAbility==ability.Id;
                button.EnableInClassList("ability-targeting-active",targeting);
                button.style.borderLeftWidth=button.style.borderRightWidth=button.style.borderTopWidth=button.style.borderBottomWidth=targeting?RadialCardProfile.Load().titleAccentWidth:1;
                button.style.backgroundColor=targeting?Color.Lerp(OrbitalTheme.Panel,Cyan,RadialCardProfile.Load().opticalSelectionTint):OrbitalTheme.Panel;
                var edge=targeting?OrbitalTheme.Ink:cooling?OrbitalTheme.Line:Cyan;button.style.borderLeftColor=button.style.borderRightColor=button.style.borderTopColor=button.style.borderBottomColor=edge;
                button.tooltip=OrbitalAbilities.Name(ability.Id)+(targeting?" · выбор цели":cooling?" · "+Math.Ceiling(wait)+" с":" · готова");
            }
            orbitalPanel.style.left=right?new StyleLength(StyleKeyword.Auto):new StyleLength(6);orbitalPanel.style.right=right?new StyleLength(6):new StyleLength(StyleKeyword.Auto);orbitalPanel.style.bottom=Mathf.Max(6,h-bottomY);orbitalPanel.style.top=StyleKeyword.Auto;
            bool tooltip=orbitalLevelHovered&&!controller&&!orbitalOpen&&CanOrbital;orbitalXpTooltip.style.display=tooltip?DisplayStyle.Flex:DisplayStyle.None;
            if(tooltip){
                orbitalXpTooltip.text=state.Level==OrbitalEscalationState.MaxLevel?"MAX · максимум\nВосстановление ×"+data.orbitalFinalSpeed:"Уровень "+state.Level+"\n"+state.Progress.ToString("0.##")+" / "+state.Threshold.ToString("0.##")+" до следующего уровня";
                float tw=Mathf.Min(220,w-12),th=Mathf.Max(40,orbitalXpTooltip.resolvedStyle.height);var b=orbitalLevelButton.worldBound;b.position-=root.worldBound.position;orbitalXpTooltip.style.width=tw;orbitalXpTooltip.style.left=b.xMax+tw+gap<=w?b.xMax+gap:Mathf.Max(6,b.xMin-tw-gap);orbitalXpTooltip.style.top=Mathf.Clamp(b.center.y-th/2,6,Mathf.Max(6,h-th-6));orbitalXpTooltip.BringToFront();
            }
            if(orbitalOpen){orbitalModal.BringToFront();RefreshOrbitalMenu();}
        }
        private static void AbsoluteFill(VisualElement element){element.style.position=Position.Absolute;element.style.left=element.style.right=element.style.top=element.style.bottom=0;}
        private void RefreshOrbitalMenu()
        {
            if(!orbitalOpen)return;var state=view.Orbital;
            if(state.PendingLevel==0)orbitalShowChoices=false;
            float w=root.contentRect.width,h=root.contentRect.height;
            orbitalMenuTitle.text=orbitalShowChoices?"Уровень "+state.PendingLevel+" · выберите поддержку":"Орбитальная поддержка";
            orbitalHint.text="";
            bool two=orbitalMenuPad&&state.PendingLevel>0&&state.Abilities.Count>0;
            string controlsKey=orbitalMenuPad+":"+orbitalShowChoices+":"+two+":"+SeatPad?.deviceId;
            if(orbitalControlsSignature!=controlsKey){
                orbitalControlsSignature=controlsKey;orbitalControls.Clear();
                void Hint(string key,string caption){var hint=new RadialControlHint(key,caption,orbitalMenuPad,SeatPad);hint.style.marginLeft=hint.style.marginRight=10;orbitalControls.Add(hint);}
                void Action(Button button,string key,string caption){button.text="";button.Clear();button.style.backgroundColor=Color.clear;button.style.backgroundImage=StyleKeyword.None;button.style.borderLeftWidth=button.style.borderRightWidth=button.style.borderTopWidth=button.style.borderBottomWidth=0;button.style.marginLeft=button.style.marginRight=10;button.style.paddingLeft=button.style.paddingRight=button.style.paddingTop=button.style.paddingBottom=0;button.Add(new RadialControlHint(key,caption,orbitalMenuPad,SeatPad));orbitalControls.Add(button);}
                Hint(orbitalMenuPad?"LS":"Мышь","Выбор");Hint(orbitalMenuPad?"A":"ЛКМ",orbitalShowChoices?"Подтвердить":"Применить");
                if(two)Action(orbitalSwitch,"X",orbitalShowChoices?"Способности":"Новый выбор");
                Action(orbitalClose,orbitalMenuPad?"B":"Esc","Закрыть");
            }
            orbitalCards.style.display=orbitalShowChoices?DisplayStyle.Flex:DisplayStyle.None;orbitalRing.style.display=orbitalShowChoices||!orbitalMenuPad?DisplayStyle.None:DisplayStyle.Flex;
            if(orbitalShowChoices){
                string sig=string.Join(",",state.Options);var data=profile.CopyData();var art=RadialCardProfile.Load();
                float cardWidth=Mathf.Min((float)data.orbitalCardWidth,(w-40)/Math.Max(1,state.Options.Count)-16);
                float fit=Mathf.Min(1,Mathf.Min(cardWidth/(float)data.orbitalCardWidth,Mathf.Max(.3f,(h-200)/((float)data.orbitalCardHeight+140))));
                float cardHeight=(float)data.orbitalCardHeight*fit;
                if(orbitalSignature!=sig){orbitalSignature=sig;orbitalCards.Clear();foreach(int value in state.Options){int id=value;int index=orbitalCards.childCount;var column=OrbitalChoiceCard.Create(id,data,()=>SubmitOrbital(true,id));
                    void Focus(){orbitalFocus=index;RefreshOrbitalMenu();}
                    column.Q<Button>().RegisterCallback<PointerEnterEvent>(_=>Focus());column.Q<Button>().RegisterCallback<FocusInEvent>(_=>Focus());orbitalCards.Add(column);}}
                float cardTop=Mathf.Max(65,(h-cardHeight-125)/2);
                // Descriptions/stats and device hints have different measured heights per viewport.
                // Fit the artwork card into the remaining space, rather than letting its tail cover the footer.
                float footerTop=orbitalControls.worldBound.yMin-root.worldBound.yMin;
                float tail=orbitalCards.Children().Select(c=>Mathf.Max(0,c.worldBound.height-c.Q<Button>().worldBound.height)).DefaultIfEmpty(0).Max();
                if(footerTop>cardTop&&tail>0)cardHeight=Mathf.Min(cardHeight,Mathf.Max(0,footerTop-cardTop-art.notePadding-tail));
                orbitalCards.style.left=0;orbitalCards.style.width=w;orbitalCards.style.top=cardTop;
                for(int i=0;i<orbitalCards.childCount;i++){
                    var column=orbitalCards[i];column.style.width=cardWidth;
                    var card=column.Q<Button>();card.style.width=cardWidth;card.style.height=cardHeight;
                    card.style.paddingLeft=card.style.paddingRight=card.style.paddingTop=card.style.paddingBottom=art.padding*fit;
                    var header=card.Q("orbital-choice-header");header.style.paddingLeft=header.style.paddingRight=header.style.paddingTop=header.style.paddingBottom=art.statColumnPadding*fit;
                    var accent=card.Q("orbital-choice-accent");accent.style.height=art.titleSize*fit;accent.style.marginRight=art.statColumnPadding*fit;
                    card.Q<OrbitalCardFinish>().Focused=i==orbitalFocus;
                    var artwork=card.Q<OrbitalPlaceholderImage>();artwork.style.width=Mathf.Min((float)data.orbitalCardArtworkSize*fit,cardWidth-art.padding*2);
                    column.Q<Label>("orbital-choice-title").style.fontSize=Mathf.Max(art.textSize,art.titleSize*fit);
                    column.Q<Label>("orbital-choice-description").style.fontSize=art.textSize;
                }
            }else if(orbitalMenuPad){
                var actions=state.Abilities.Select(a=>new OfflinePadAction(a.Id.ToString(),OrbitalAbilities.Name(a.Id).Replace("Поддержка ",""),a.Remaining<=1e-9&&state.CommonRemaining<=1e-9,(a.Id==1||a.Id==2||a.Id==OrbitalAbilities.SystemFailure?"Выберите точку · ":a.Id==OrbitalAbilities.ControlHack?"Выберите шахту или юнита · ":"Заглушка · ")+Math.Ceiling(Math.Max(a.Remaining,state.CommonRemaining))+" с"){Presentation=new RadialActionPresentation{Title=OrbitalAbilities.Name(a.Id),Artwork="orbital:"+a.Id,Note=(a.Id==1||a.Id==2||a.Id==OrbitalAbilities.SystemFailure?"Выберите точку · ":a.Id==OrbitalAbilities.ControlHack?"Выберите шахту или юнита · ":"Заглушка · ")+(Math.Max(a.Remaining,state.CommonRemaining)>1e-9?Math.Ceiling(Math.Max(a.Remaining,state.CommonRemaining))+" с":"готова")}}).ToArray();
                float size=Mathf.Min(280,Mathf.Min(w-28,h-135));orbitalRing.Resize(size);orbitalRing.style.left=(w-size)/2;orbitalRing.style.top=Mathf.Max(60,(h-size)/2);orbitalRing.ConfigureInput(true,SeatPad);
                orbitalRing.Set(actions,"Поддержка",orbitalFocus>=0&&orbitalFocus<actions.Length?actions[orbitalFocus].Sector.Id:null,0,"");orbitalRing.FitAuxiliary(w,h);
                if(actions.Length==0)orbitalHint.text="Наносите урон врагам, чтобы открыть поддержку";
            }
        }
        private bool PollOrbitalPad(Gamepad pad)
        {
            if(!CanOrbital){CloseOrbital();return false;}
            if(orbitalReleaseGate){if(!pad.buttonSouth.isPressed&&!pad.buttonEast.isPressed&&!pad.buttonWest.isPressed&&!pad.leftTrigger.isPressed&&pad.leftStick.ReadValue().magnitude<NativeDevicePreferences.Deadzone)orbitalReleaseGate=false;return true;}
            if(pad.leftTrigger.wasPressedThisFrame){if(orbitalOpen)CloseOrbital();else{battleController=true;OpenOrbital(true);}return true;}
            if(!orbitalOpen)return false;
            if(pad.startButton.wasPressedThisFrame){CloseOrbital();(localCoordinator??this).PauseFrom(this,true);return true;}
            if(pad.buttonEast.wasPressedThisFrame){CloseOrbital();return true;}
            if(pad.buttonWest.wasPressedThisFrame&&view.Orbital.PendingLevel>0&&view.Orbital.Abilities.Count>0){orbitalShowChoices=!orbitalShowChoices;orbitalFocus=0;orbitalSignature=null;}
            var ls=pad.leftStick.ReadValue();if(ls.magnitude<NativeDevicePreferences.Deadzone)orbitalStickReady=true;
            int count=orbitalShowChoices?view.Orbital.Options.Count:view.Orbital.Abilities.Count;
            if(count>0){
                if(orbitalShowChoices){if(pad.dpad.left.wasPressedThisFrame||orbitalStickReady&&ls.x<-.5f){orbitalFocus=(orbitalFocus+count-1)%count;orbitalStickReady=false;}if(pad.dpad.right.wasPressedThisFrame||orbitalStickReady&&ls.x>.5f){orbitalFocus=(orbitalFocus+1)%count;orbitalStickReady=false;}}
                else {int? slot=OfflinePadGestures.RingSector(ls.x,-ls.y,count,battleInputProfile.radialDeadzone);orbitalFocus=slot??-1;}
                if(pad.buttonSouth.wasPressedThisFrame&&orbitalFocus>=0){int id=orbitalShowChoices?view.Orbital.Options[orbitalFocus]:view.Orbital.Abilities[orbitalFocus].Id;SubmitOrbital(orbitalShowChoices,id);}
            }
            RefreshOrbitalMenu();return true;
        }
    }
    internal sealed class OrbitalPlaceholderImage:VisualElement
    {
        private int id;private Texture2D abilityIcon;private static Texture2D scanIcon,hackIcon,mineIcon;
        internal void SetId(int value){if(id==value)return;id=value;ApplyArtwork();MarkDirtyRepaint();}
        internal OrbitalPlaceholderImage(int id){this.id=id;ApplyArtwork();generateVisualContent+=Draw;}
        private void ApplyArtwork()
        {
            abilityIcon=null;
            if(id==1)abilityIcon=scanIcon=scanIcon??Resources.Load<Texture2D>("OrbitalIcons/scan");
            else if(id==2)abilityIcon=mineIcon=mineIcon??Resources.Load<Texture2D>("OrbitalIcons/mine-trap");
            else if(id==OrbitalAbilities.ControlHack)abilityIcon=hackIcon=hackIcon??Resources.Load<Texture2D>("OrbitalIcons/control-hack");
            if(abilityIcon!=null){style.backgroundImage=abilityIcon;style.unityBackgroundScaleMode=ScaleMode.ScaleToFit;}else style.backgroundImage=StyleKeyword.None;
        }
        private void Draw(MeshGenerationContext context)
        {
            if(abilityIcon!=null)return;
            var p=context.painter2D;var center=contentRect.center;float r=Mathf.Min(contentRect.width,contentRect.height)*.31f;
            if(id==OrbitalAbilities.SystemFailure){
                p.strokeColor=OrbitalTheme.Cyan;p.lineWidth=Mathf.Max(1,r/5);p.BeginPath();
                for(int i=0;i<=24;i++){float a=(-60+300*i/24f)*Mathf.Deg2Rad;var at=center+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*r;if(i==0)p.MoveTo(at);else p.LineTo(at);}p.Stroke();
                p.BeginPath();p.MoveTo(center+new Vector2(0,-r*1.2f));p.LineTo(center);p.Stroke();return;
            }
            if(id==2){
                p.fillColor=new Color(.16f,.22f,.25f);p.strokeColor=OrbitalTheme.Cyan;p.lineWidth=2;p.BeginPath();
                for(int i=0;i<6;i++){var v=center+new Vector2(Mathf.Cos(i*Mathf.PI/3),Mathf.Sin(i*Mathf.PI/3))*r;if(i==0)p.MoveTo(v);else p.LineTo(v);}p.ClosePath();p.Fill();p.Stroke();
                p.BeginPath();p.Arc(center,r*.5f,0,360);p.Stroke();p.strokeColor=new Color(1,.6f,.15f);p.BeginPath();p.MoveTo(center+new Vector2(-r*.25f,r*.25f));p.LineTo(center+new Vector2(r*.25f,r*.25f));p.Stroke();return;
            }
            p.strokeColor=OrbitalTheme.Cyan;p.lineWidth=2;p.BeginPath();p.Arc(center,r,0,360);p.Stroke();
            p.BeginPath();p.MoveTo(center+new Vector2(-r*1.4f,r*.5f));p.LineTo(center+new Vector2(r*1.4f,-r*.5f));p.Stroke();
            p.fillColor=OrbitalTheme.Cyan;p.BeginPath();p.Arc(center,r*.18f,0,360);p.Fill();
            for(int i=0;i<OrbitalAbilities.Tier(id);i++){var at=center+new Vector2((i-1)*r*.45f,r*1.3f);p.BeginPath();p.MoveTo(at);p.LineTo(at+new Vector2(r*.25f,0));p.Stroke();}
        }
    }
}
