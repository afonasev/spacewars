using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Spacewars.Presentation
{
    public sealed partial class PlayableBootstrap
    {
        private NativeLobbyConfiguration lobbySetup = new NativeLobbyConfiguration(), matchSetup;
        private VisualElement menuScreen, lobbyScreen, lobbyColumns, rosterPanel, mapPanel, mapInfo, loadingScreen;
        private Label lobbyStatus, humanReady, aiReady;
        private Button launchButton, hudLobbyButton, modalLobbyButton;
        private PlayableMapTerrain lobbyTerrain;
        private PlayableMapSurface lobbyPreview;
        private bool inLobby, returningToLobby, preparing, nativeMainMenuEnabled;
        private NativeMainMenu nativeMainMenu;
        private string lobbyEvidence;
        private Keyboard lobbyKeyboard;private Mouse lobbyMouse;
        private bool devicesReady => lobbyKeyboard?.added==true && lobbyMouse?.added==true;
        private static readonly Color Graphite = new Color(.035f,.05f,.055f);
        private static readonly Color LobbyLine = new Color(.325f,.388f,.416f);
        private static readonly Color LobbyCyan = new Color(.44f,.85f,.91f);
        private static readonly Color Amber = new Color(.95f,.72f,.34f);

        private VisualElement LobbyPanel()
        {
            var panel = new VisualElement();
            panel.style.paddingLeft = 20; panel.style.paddingRight = 20;
            panel.style.paddingTop = 18; panel.style.paddingBottom = 18;
            // Native painter reproduces the original shell's fine clipped corners.
            panel.generateVisualContent += ctx => {
                float w=panel.layout.width,h=panel.layout.height;
                var p=ctx.painter2D; p.fillColor=Graphite; p.strokeColor=LobbyLine; p.lineWidth=1;
                p.BeginPath();p.MoveTo(new Vector2(10,0));p.LineTo(new Vector2(w,0));p.LineTo(new Vector2(w,h-10));
                p.LineTo(new Vector2(w-10,h));p.LineTo(new Vector2(0,h));p.LineTo(new Vector2(0,10));p.ClosePath();p.Fill();p.Stroke();
            };
            return panel;
        }
        private VisualElement MenuPage()
        {
            var page=new VisualElement(); page.style.position=Position.Absolute;
            page.style.left=0;page.style.right=0;page.style.top=0;page.style.bottom=0;
            page.style.backgroundColor=new Color(.035f,.05f,.055f);
            page.style.paddingLeft=28;page.style.paddingRight=28;page.style.paddingTop=24;page.style.paddingBottom=24;
            root.Add(page); return page;
        }
        private Label LobbyText(string text, int size=14, Color? color=null)
        {
            var label=new Label(text);label.style.whiteSpace=WhiteSpace.Normal;label.style.fontSize=size;
            label.style.color=color??Ink;label.style.marginBottom=8;return label;
        }
        private Button LobbyAction(string text, Action action)
        {
            var button=new Button(action){text=text};StyleAction(button);
            button.style.height=42;button.style.backgroundColor=new Color(.067f,.106f,.125f);
            button.style.borderLeftColor=LobbyLine;button.style.borderRightColor=LobbyLine;
            button.style.borderTopColor=LobbyLine;button.style.borderBottomColor=LobbyLine;
            return button;
        }
        private void CreateLobby()
        {
            hudLobbyButton.style.display=modalLobbyButton.style.display=DisplayStyle.Flex;
            var popupStyle=Resources.Load<StyleSheet>("NativeLobby");
            void ApplyPopupStyle(IPanel panel){if(panel!=null&&popupStyle!=null&&!panel.visualTree.styleSheets.Contains(popupStyle))panel.visualTree.styleSheets.Add(popupStyle);}
            ApplyPopupStyle(root.panel);root.RegisterCallback<AttachToPanelEvent>(evt=>ApplyPopupStyle(evt.destinationPanel));
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            (root.panel?.visualTree??root).RegisterCallback<KeyDownEvent>(evt=>{
                if(evt.keyCode!=KeyCode.F12||lobbyEvidence==null)return;
                Directory.CreateDirectory(lobbyEvidence);
                ScreenCapture.CaptureScreenshot(Path.Combine(lobbyEvidence,"manual-"+Time.frameCount+".png"));
                evt.StopImmediatePropagation();
            },TrickleDown.TrickleDown);
#endif
            menuScreen=MenuPage();menuScreen.style.justifyContent=Justify.Center;menuScreen.style.alignItems=Align.Center;
            var main=LobbyPanel();main.style.width=440;main.style.maxWidth=Length.Percent(100);menuScreen.Add(main);
            main.Add(LobbyText("SPACEWARS",32,LobbyCyan));main.Add(LobbyText("ЛОКАЛЬНАЯ ИГРА",12,Muted));
            main.Add(LobbyText("Подготовьте состав и начните бой на Three Crossings."));
            main.Add(LobbyAction("Локальная игра",ShowLobby));
            main.Add(LobbyAction("Выйти из приложения",()=>Application.Quit()));
            lobbyScreen=MenuPage();
            var header=new VisualElement();header.style.flexDirection=FlexDirection.Row;header.style.alignItems=Align.Center;
            var title=LobbyText("SPACEWARS  /  ЛОКАЛЬНАЯ ИГРА",19,LobbyCyan);title.style.flexGrow=1;header.Add(title);
            header.Add(LobbyAction("Назад  Esc",ShowMainMenu));lobbyScreen.Add(header);
            lobbyColumns=new VisualElement();lobbyColumns.style.flexGrow=1;lobbyColumns.style.flexDirection=FlexDirection.Row;
            lobbyColumns.style.marginTop=18;lobbyColumns.style.marginBottom=18;lobbyScreen.Add(lobbyColumns);
            rosterPanel=LobbyPanel();rosterPanel.style.flexGrow=2;rosterPanel.style.flexBasis=0;rosterPanel.style.marginRight=18;lobbyColumns.Add(rosterPanel);
            mapPanel=LobbyPanel();mapPanel.style.flexGrow=1;mapPanel.style.flexBasis=0;lobbyColumns.Add(mapPanel);
            mapInfo=new VisualElement();mapPanel.Add(mapInfo);
            mapInfo.Add(LobbyText("КАРТА МАТЧА",12,Muted));mapInfo.Add(LobbyText("Three Crossings",26,LobbyCyan));
            mapInfo.Add(LobbyText("2 игровых места · многоуровневая карта",14));
            lobbyTerrain=new PlayableMapTerrain(profile);lobbyTerrain.ShowPublicTerrain();
            lobbyPreview=new PlayableMapSurface(profile,lobbyTerrain.Texture){OwnerPaint=LobbyPaint};
            lobbyPreview.style.flexGrow=1;lobbyPreview.style.minHeight=130;lobbyPreview.style.maxHeight=360;
            lobbyPreview.style.marginTop=14;lobbyPreview.style.marginBottom=14;mapPanel.Add(lobbyPreview);
            // Public authored landmarks, no simulation entities, fog or camera frame.
            RefreshLobbyPreview();
            var description=LobbyText("Три переправы, поднятые платформы и рампы. Захватывайте территории и уничтожьте центры противника.",14,Muted);description.name="lobby-map-description";mapPanel.Add(description);
            var footer=new VisualElement();footer.style.alignItems=Align.Center;lobbyScreen.Add(footer);
            lobbyStatus=LobbyText("",14,Amber);lobbyStatus.name="lobby-validation";footer.Add(lobbyStatus);
            launchButton=LobbyAction("В бой!",LaunchLobbyMatch);launchButton.name="lobby-launch";launchButton.style.width=240;
            launchButton.style.backgroundColor=new Color(.083f,.24f,.275f);launchButton.style.color=LobbyCyan;footer.Add(launchButton);
            loadingScreen=MenuPage();loadingScreen.style.justifyContent=Justify.Center;loadingScreen.style.alignItems=Align.Center;
            loadingScreen.Add(LobbyText("Подготовка боя…",30,LobbyCyan));loadingScreen.Add(LobbyText("Подготовка поля и первого кадра",16,Amber));
            loadingScreen.style.display=DisplayStyle.None;
            lobbyScreen.RegisterCallback<GeometryChangedEvent>(_=>ResizeLobby());
            lobbyScreen.RegisterCallback<KeyDownEvent>(evt=>{if(evt.keyCode==KeyCode.Escape){ShowMainMenu();evt.StopImmediatePropagation();}});
            BindLobbyDevices();RebuildRoster();ShowMainMenu();
        }
        private void RefreshLobbyPreview()
        {
            lobbyPreview.SetPublic(profile.AuthoredMap.Sites(profile).Select(s=>new PlayableMapMark(s.Id,s.Position,s.Kind,
                MapMarkState.Ready,s.Id==1?PlayableOwner.Player:s.Id==2?PlayableOwner.Enemy:(PlayableOwner?)null)).ToArray(),profile.ArenaHalfExtent);
        }
        private Color LobbyPaint(PlayableOwner? owner)
        {
            var setup=matchSetup??lobbySetup;int i=owner==PlayableOwner.Player?setup.HumanColor:setup.AiColor;
            if(!owner.HasValue)return Color.white;
            ColorUtility.TryParseHtmlString(NativeLobbyConfiguration.ColorHex[i],out var color);return color;
        }
        private void RebuildRoster()
        {
            rosterPanel.Clear();rosterPanel.Add(LobbyText("СОСТАВ МАТЧА",18));
            rosterPanel.Add(LobbyText("2 места  /  Человек против ИИ",12,Muted));
            Seat(true);Seat(false);
            var reason=LobbyText("Доступно: 1 человек + ИИ Боец. Геймпад и зритель пока не поддерживаются.",12,Muted);
            reason.name="lobby-mode-reason";reason.style.marginTop=12;rosterPanel.Add(reason);ResizeLobby();
        }
        private void Seat(bool human)
        {
            bool present=human?lobbySetup.HumanPresent:lobbySetup.AiPresent;
            var row=new VisualElement();row.AddToClassList("lobby-seat");row.style.marginTop=16;row.style.paddingTop=12;
            row.style.borderTopWidth=1;row.style.borderTopColor=LobbyLine;rosterPanel.Add(row);
            var head=new VisualElement();head.style.flexDirection=FlexDirection.Row;row.Add(head);
            var name=LobbyText((human?"01  /  ":"02  /  ")+(human?"Игрок":"ИИ"),16,LobbyCyan);name.style.flexGrow=1;head.Add(name);
            if(!present){row.Add(LobbyAction(human?"+ Игрок":"+ ИИ",()=>{if(human)lobbySetup.HumanPresent=true;else lobbySetup.AiPresent=true;RebuildRoster();RefreshLobbyValidation();}));return;}
            var remove=LobbyAction("×",()=>{if(human)lobbySetup.HumanPresent=false;else lobbySetup.AiPresent=false;RebuildRoster();RefreshLobbyValidation();});
            remove.tooltip="Освободить место";remove.style.width=38;head.Add(remove);
            var field=new TextField("Имя"){value=human?lobbySetup.HumanName:lobbySetup.AiName,maxLength=24};
            field.name=human?"lobby-human-name":"lobby-ai-name";
            field.RegisterValueChangedCallback(evt=>{if(human)lobbySetup.HumanName=evt.newValue;else lobbySetup.AiName=evt.newValue;RefreshLobbyValidation();});StyleLobbyField(field);row.Add(field);
            var settings=new VisualElement();settings.style.flexDirection=FlexDirection.Row;settings.style.flexWrap=Wrap.Wrap;row.Add(settings);
            var team=new DropdownField("Команда",Enumerable.Range(1,8).Select(n=>n.ToString()).ToList(),(human?lobbySetup.HumanTeam:lobbySetup.AiTeam)-1);
            team.RegisterValueChangedCallback(evt=>{if(human)lobbySetup.HumanTeam=int.Parse(evt.newValue);else lobbySetup.AiTeam=int.Parse(evt.newValue);RefreshLobbyValidation();});StyleLobbyField(team);team.style.flexGrow=1;team.style.minWidth=120;settings.Add(team);
            var color=new DropdownField("Цвет",NativeLobbyConfiguration.ColorNames.ToList(),human?lobbySetup.HumanColor:lobbySetup.AiColor);
            color.RegisterValueChangedCallback(evt=>{int index=Array.IndexOf(NativeLobbyConfiguration.ColorNames,evt.newValue);if(human)lobbySetup.HumanColor=index;else lobbySetup.AiColor=index;RefreshLobbyPreview();RefreshLobbyValidation();});StyleLobbyField(color);color.style.flexGrow=1;color.style.minWidth=150;settings.Add(color);
            var swatch=new VisualElement();swatch.style.width=12;swatch.style.marginTop=12;swatch.style.marginBottom=8;
            ColorUtility.TryParseHtmlString(NativeLobbyConfiguration.ColorHex[human?lobbySetup.HumanColor:lobbySetup.AiColor],out var paint);swatch.style.backgroundColor=paint;settings.Add(swatch);
            color.RegisterValueChangedCallback(_=>{ColorUtility.TryParseHtmlString(NativeLobbyConfiguration.ColorHex[human?lobbySetup.HumanColor:lobbySetup.AiColor],out var next);swatch.style.backgroundColor=next;});
            if(human){
                var bind=LobbyAction("Назначить мышь и клавиатуру",()=>{BindLobbyDevices();RefreshLobbyValidation();});bind.name="lobby-bind-devices";bind.tooltip="Назначить текущие мышь и клавиатуру этому месту";row.Add(bind);
                humanReady=LobbyText("",12,Amber);row.Add(humanReady);
            }
            else{aiReady=LobbyText("Боец  ·  Готов\nБез бонусов ресурсов или характеристик",12,Muted);row.Add(aiReady);}
        }
        private void StyleLobbyField(VisualElement field)
        {
            field.style.marginBottom=8;field.style.minHeight=42;field.style.color=Ink;
            var label=field.Q<Label>();if(label!=null){label.style.minWidth=65;label.style.color=Muted;}
            var inputElement=field.Q(className:"unity-base-field__input");
            if(inputElement!=null){inputElement.style.backgroundColor=new Color(.067f,.106f,.125f);inputElement.style.color=Ink;inputElement.style.minHeight=36;}
        }
        // Menu packing dimensions keep a square preview and footer apart; they do not tune gameplay, camera or combat readability.
        private void ResizeLobby()
        {
            bool narrow=root.resolvedStyle.width<800;
            bool compact=narrow||root.resolvedStyle.height<860;
            lobbyColumns.style.flexDirection=narrow?FlexDirection.Column:FlexDirection.Row;
            rosterPanel.style.marginRight=narrow?0:18;rosterPanel.style.marginBottom=narrow?12:0;
            rosterPanel.style.flexGrow=narrow?0:2;rosterPanel.style.flexBasis=narrow?new StyleLength(StyleKeyword.Auto):new StyleLength(0f);
            mapPanel.style.flexGrow=narrow?0:1;mapPanel.style.flexShrink=narrow?0:1;
            mapPanel.style.flexBasis=narrow?new StyleLength(StyleKeyword.Auto):new StyleLength(0f);
            mapPanel.style.flexDirection=narrow?FlexDirection.Row:FlexDirection.Column;
            mapPanel.style.height=narrow?new StyleLength(170f):new StyleLength(StyleKeyword.Auto);
            mapInfo.style.flexGrow=narrow?1:0;mapInfo.style.flexBasis=narrow?new StyleLength(0f):new StyleLength(StyleKeyword.Auto);
            mapInfo.style.marginRight=narrow?12:0;
            lobbyPreview.style.minHeight=narrow?150:130;lobbyPreview.style.maxHeight=narrow?150:360;
            lobbyPreview.style.width=narrow?new StyleLength(150f):new StyleLength(StyleKeyword.Auto);
            lobbyPreview.style.height=narrow?new StyleLength(150f):new StyleLength(StyleKeyword.Auto);
            lobbyPreview.style.flexGrow=narrow?0:1;lobbyPreview.style.flexShrink=0;
            lobbyPreview.style.marginTop=lobbyPreview.style.marginBottom=narrow?0:14;
            foreach(var panel in new[]{rosterPanel,mapPanel}){panel.style.paddingLeft=panel.style.paddingRight=narrow?12:20;panel.style.paddingTop=panel.style.paddingBottom=narrow?10:18;panel.MarkDirtyRepaint();}
            foreach(var row in rosterPanel.Query<VisualElement>(className:"lobby-seat").ToList()){row.style.marginTop=compact?6:16;row.style.paddingTop=compact?4:12;}
            foreach(var button in rosterPanel.Query<Button>().ToList())button.style.height=compact?28:42;
            foreach(var field in rosterPanel.Query<VisualElement>(className:"unity-base-field").ToList())
            {
                field.style.minHeight=compact?28:42;field.style.marginBottom=compact?4:8;
                var box=field.Q(className:"unity-base-field__input");if(box!=null)box.style.minHeight=compact?26:36;
            }
            foreach(var label in rosterPanel.Query<Label>().ToList())label.style.marginBottom=compact?3:8;
            var description=mapPanel.Q<Label>("lobby-map-description");if(description!=null)description.style.display=narrow?DisplayStyle.None:DisplayStyle.Flex;
            var reason=rosterPanel.Q<Label>("lobby-mode-reason");if(reason!=null)reason.style.marginTop=compact?4:12;
        }
        private void RefreshLobbyValidation()
        {
            if(lobbyStatus==null)return;
            var error=lobbySetup.Validate(profile,lobbyKeyboard?.added==true,lobbyMouse?.added==true);
            lobbyStatus.text=error??"Состав готов  ·  2 / 2";
            lobbyStatus.style.color=error==null?LobbyCyan:devicesReady?Danger:Amber;
            launchButton.SetEnabled(error==null);
            if(humanReady!=null){humanReady.text=devicesReady?"Мышь и клавиатура  ·  Готов":"Мышь и клавиатура  ·  Ожидание устройства";humanReady.style.color=devicesReady?Muted:Amber;}
        }
        private void BindLobbyDevices()
        {
            lobbyKeyboard=Keyboard.current;lobbyMouse=Mouse.current;
            input.AssignedKeyboard=lobbyKeyboard;input.AssignedMouse=lobbyMouse;
        }
        private void ShowMainMenu()
        {
            inLobby=true;menuScreen.style.display=nativeMainMenuEnabled?DisplayStyle.None:DisplayStyle.Flex;
            lobbyScreen.style.display=DisplayStyle.None;SetMatchUi(false);
            if(nativeMainMenuEnabled&&nativeMainMenu==null)
            {
                nativeMainMenu=new GameObject("Spacewars main menu").AddComponent<NativeMainMenu>();
                nativeMainMenu.Play=ShowLobby;
            }
        }
        private void ShowLobby(){inLobby=true;matchSetup=null;menuScreen.style.display=DisplayStyle.None;lobbyScreen.style.display=DisplayStyle.Flex;SetMatchUi(false);RefreshLobbyValidation();RefreshLobbyPreview();launchButton.Focus();}
        private void SetMatchUi(bool visible){top.style.display=bottom.style.display=visible?DisplayStyle.Flex:DisplayStyle.None;modal.style.display=DisplayStyle.None;input.WorldInputEnabled=visible;}
        private void LaunchLobbyMatch()
        {
            RefreshLobbyValidation();if(!launchButton.enabledSelf||preparing)return;
            matchSetup=lobbySetup.Copy();preparing=true;loadingScreen.style.display=DisplayStyle.Flex;lobbyScreen.style.display=DisplayStyle.None;
            StartCoroutine(PrepareLobbyMatch());
        }
        private IEnumerator PrepareLobbyMatch()
        {
            yield return null; // Present actual preparation state before creating authority.
            if(!TryPrepareLobbySession())yield break;
            while(runtime.Latest==null)yield return null;
            view=runtime.Latest;ServiceRoutes();Render();UpdateHud();UpdateMaps();
            yield return new WaitForEndOfFrame();
            inLobby=false;preparing=false;loadingScreen.style.display=DisplayStyle.None;SetMatchUi(true);Pause(false);
            Record("lobby launch "+matchSetup.MatchHumanName+" team="+matchSetup.HumanTeam+" vs "+matchSetup.MatchAiName+" team="+matchSetup.AiTeam);
        }
        private bool TryPrepareLobbySession()
        {
            try{StartSession();paused=true;return true;}
            catch(Exception error)
            {
                Debug.LogException(error);runtime?.RequestStop();runtime=null;preparing=false;
                loadingScreen.style.display=DisplayStyle.None;ShowLobby();
                lobbyStatus.text="Не удалось подготовить матч. Проверьте журнал и повторите запуск.";lobbyStatus.style.color=Danger;
                return false;
            }
        }
        private void ReturnToLobby(){if(inLobby||returningToLobby)return;returningToLobby=true;input.WorldInputEnabled=false;runtime?.RequestStop();}
        private void UpdateLobby()
        {
            if(inLobby){input.WorldInputEnabled=false;RefreshLobbyValidation();}
            else if(matchSetup!=null&&!devicesReady&&!paused){Pause(true);notice="Ожидание мыши и клавиатуры: "+matchSetup.MatchHumanName;}
        }
    }
}
