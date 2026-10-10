using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Spacewars.Presentation
{
    public sealed partial class PlayableBootstrap
    {
        private const string CrossingsLobbyName="Переправа",FoundryLobbyName="Огненный разлом",AiTestLobbyName="ИИ-полигон · 8";
        private const string CrossingsLobbyDescription="Холодная река рассекает каменное плато, оставляя лишь три пути на другой берег. Среди скал и высоких уступов каждый проход становится рубежом, который нельзя отдать.";
        private const string FoundryLobbyDescription="Над остывшими промышленными площадками дрожит жар лавовой реки. Между шахтами и укреплёнными высотами тишина держится лишь до первых выстрелов.";
        private NativeLobbyConfiguration lobbySetup = new NativeLobbyConfiguration(), matchSetup;
        private VisualElement menuScreen, lobbyScreen, lobbyColumns, rosterPanel, mapPanel, mapInfo, loadingScreen;
        private Label lobbyStatus, humanReady, aiReady;
        private ScrollView rosterScroll;
        private Button addBotButton, joinButton;
        private string lobbyActionError;
        private bool lobbyPadArmed, lobbyRosterEdited;
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
                var p=ctx.painter2D; p.fillColor=new Color(.025f,.05f,.065f,.88f); p.strokeColor=LobbyLine; p.lineWidth=1;
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
            page.style.paddingBottom=58;OrbitalTheme.Footer(page);root.Add(page); return page;
        }
        private Label LobbyText(string text, int size=14, Color? color=null)
        {
            var label=new Label(text);label.style.whiteSpace=WhiteSpace.Normal;label.style.fontSize=size;
            label.style.color=color??Ink;label.style.marginBottom=8;return label;
        }
        private Button LobbyAction(string text, Action action)
        {
            var button=new Button(action);var caption=new Label(text){pickingMode=PickingMode.Ignore};caption.style.whiteSpace=WhiteSpace.NoWrap;caption.style.marginLeft=caption.style.marginRight=0;button.Add(caption);StyleAction(button);button.style.width=210;button.style.minWidth=0;button.style.flexShrink=0;button.style.whiteSpace=WhiteSpace.NoWrap;
            button.style.height=42;button.style.backgroundColor=new Color(.067f,.106f,.125f);
            button.style.borderLeftColor=LobbyLine;button.style.borderRightColor=LobbyLine;
            button.style.borderTopColor=LobbyLine;button.style.borderBottomColor=LobbyLine;
            return button;
        }
        private void CreateLobby()
        {
            hudLobbyButton.style.display=DisplayStyle.None;modalLobbyButton.style.display=DisplayStyle.None;
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
            main.Add(LobbyText("Выберите карту, подготовьте состав и начните бой."));
            main.Add(LobbyAction("Локальная игра",ShowLobby));
            main.Add(LobbyAction("Выйти из приложения",()=>Application.Quit()));
            lobbyScreen=MenuPage();lobbyScreen.name="premium-lobby";var shade=new VisualElement{pickingMode=PickingMode.Ignore};shade.style.position=Position.Absolute;shade.style.left=shade.style.right=shade.style.top=shade.style.bottom=0;shade.style.backgroundColor=new Color(0,.02f,.03f,.55f);lobbyScreen.Insert(0,shade);lobbyScreen.style.backgroundImage=Resources.Load<Texture2D>("OrbitalBackdrop");lobbyScreen.style.paddingLeft=lobbyScreen.style.paddingRight=38;
            var header=new VisualElement();header.style.flexDirection=FlexDirection.Row;header.style.alignItems=Align.Center;
            var back=LobbyAction("Главное меню",ShowMainMenu);back.name="lobby-back";back.Insert(0,new NativeControllerGlyph("B"));header.Add(back);lobbyScreen.Add(header);
            var preparationTitle=LobbyText("Подготовка матча",34);preparationTitle.name="lobby-title";lobbyScreen.Add(preparationTitle);
            lobbyColumns=new VisualElement();lobbyColumns.style.flexGrow=1;lobbyColumns.style.flexDirection=FlexDirection.Row;
            lobbyColumns.style.marginTop=18;lobbyColumns.style.marginBottom=18;lobbyScreen.Add(lobbyColumns);
            rosterPanel=LobbyPanel();rosterPanel.style.flexGrow=3;rosterPanel.style.flexBasis=0;rosterPanel.style.marginRight=18;lobbyColumns.Add(rosterPanel);
            mapPanel=LobbyPanel();mapPanel.style.flexGrow=1;mapPanel.style.minWidth=250;mapPanel.style.flexBasis=0;lobbyColumns.Add(mapPanel);
            mapInfo=new VisualElement();mapPanel.Add(mapInfo);
            mapInfo.Add(LobbyText("КАРТА МАТЧА",12,Muted));
            var maps=new DropdownField("Карта",new List<string>{CrossingsLobbyName,FoundryLobbyName,AiTestLobbyName},0){name="lobby-map-choice"};StyleLobbyField(maps);mapInfo.Add(maps);
            maps.RegisterValueChangedCallback(evt=>SelectLobbyMap(evt.newValue==FoundryLobbyName,evt.newValue==AiTestLobbyName));
            var capacity=LobbyText("2 игровых места",14);capacity.name="lobby-map-capacity";mapInfo.Add(capacity);
            lobbyTerrain=new PlayableMapTerrain(profile);lobbyTerrain.ShowPublicTerrain();
            lobbyPreview=new PlayableMapSurface(profile,lobbyTerrain.Texture){OwnerPaint=LobbyPaint};
            lobbyPreview.style.flexGrow=1;lobbyPreview.style.minHeight=130;lobbyPreview.style.maxHeight=360;
            lobbyPreview.style.marginTop=14;lobbyPreview.style.marginBottom=14;mapPanel.Add(lobbyPreview);
            // Public authored landmarks, no simulation entities, fog or camera frame.
            lobbySetup.InitializeParticipants();RefreshLobbyPreview();
            var description=LobbyText(CrossingsLobbyDescription,14,Muted);description.name="lobby-map-description";mapPanel.Add(description);
            var footer=new VisualElement();footer.style.flexDirection=FlexDirection.Row;footer.style.alignItems=Align.Center;lobbyScreen.Add(footer);
            lobbyStatus=LobbyText("",14,Amber);lobbyStatus.name="lobby-validation";lobbyStatus.style.flexGrow=1;lobbyStatus.style.flexShrink=1;footer.Add(lobbyStatus);
            launchButton=LobbyAction("Начать матч  ·  Start",LaunchLobbyMatch);launchButton.name="lobby-launch";launchButton.style.width=330;launchButton.Insert(0,new NativeControllerGlyph("A"));
            launchButton.style.height=50;launchButton.style.backgroundColor=new Color(.04f,.35f,.42f);launchButton.style.color=Color.white;launchButton.style.fontSize=19;launchButton.style.unityFontStyleAndWeight=FontStyle.Bold;
            launchButton.style.borderLeftWidth=launchButton.style.borderRightWidth=launchButton.style.borderTopWidth=launchButton.style.borderBottomWidth=2;launchButton.style.borderLeftColor=launchButton.style.borderRightColor=launchButton.style.borderTopColor=launchButton.style.borderBottomColor=LobbyCyan;footer.Add(launchButton);
            loadingScreen=OrbitalTheme.Loading(root,"Подготовка матча…");
            loadingScreen.style.display=DisplayStyle.None;
            lobbyScreen.RegisterCallback<GeometryChangedEvent>(_=>ResizeLobby());
            lobbyScreen.RegisterCallback<KeyDownEvent>(evt=>{if(evt.keyCode==KeyCode.Escape){ShowMainMenu();evt.StopImmediatePropagation();}});
            BindLobbyDevices();RebuildRoster();ShowMainMenu();
        }
        private PlayableProfile ComposeLobbyProfile(PlayableProfile balance)
        {
            IPlayableTerrain map=lobbySetup.AiTestMap?new AiTestMap():lobbySetup.Foundry?new FoundryMap(JsonUtility.FromJson<FoundryProfileData>(Resources.Load<TextAsset>("FoundryProfile").text)):(IPlayableTerrain)new ThreeCrossingsMap(JsonUtility.FromJson<ThreeCrossingsProfileData>(Resources.Load<TextAsset>("ThreeCrossingsProfile").text));
            return PlayableProfile.Create(balance.CopyData(),map,balance.DisplayName);
        }
        private void SelectLobbyMap(bool foundry,bool aiTest=false)
        {
            bool defaults=!lobbyRosterEdited;
            lobbySetup.Foundry=foundry;lobbySetup.AiTestMap=aiTest;if(defaults){lobbySetup.Participants=null;lobbySetup.InitializeParticipants();}
            EnsureBalanceStore();profile=ComposeLobbyProfile(balanceStore.Selected);
            lobbyTerrain?.Dispose();lobbyTerrain=new PlayableMapTerrain(profile);lobbyTerrain.ShowPublicTerrain();lobbyPreview.Rebind(profile,lobbyTerrain.Texture);
            mapInfo.Q<Label>("lobby-map-capacity").text=aiTest?"8 игровых мест":foundry?"6 игровых мест":"2 игровых места";
            mapPanel.Q<Label>("lobby-map-description").text=aiTest?"Открытый полигон для проверки ИИ: восемь стартов, шахты и точки расширения. Без стен и перепадов высоты.":foundry?FoundryLobbyDescription:CrossingsLobbyDescription;
            RebuildRoster();RefreshLobbyPreview();RefreshLobbyValidation();
        }
        private void RecreateMapPresentation()
        {
            world?.Dispose();world=new PlayableWorld(transform,profile){OwnerPaint=LobbyPaint};mapTerrain?.Dispose();mapTerrain=new PlayableMapTerrain(profile);
            compactMap.Rebind(profile,mapTerrain.Texture);tacticalMap.Rebind(profile,mapTerrain.Texture);
            cameraView.transform.position=new Vector3((float)profile.CameraOffsetX,(float)profile.CameraHeight,(float)profile.CameraOffsetZ);cameraView.transform.LookAt(Vector3.zero);
            var home=profile.Headquarters(LocalOwner);cameraView.transform.position+=new Vector3((float)home.X,(float)profile.AuthoredMap.SurfaceHeight(home),(float)home.Z);
            RememberKeyboardCamera();
        }
        private bool IsOpponent(PlayableOwner owner)=>view.Participants.Count==0?owner!=view.Owner:(int)owner>=0&&(int)owner<view.Participants.Count&&view.Participants[(int)owner].Team!=view.Team;
        private void RefreshLobbyPreview()
        {
            lobbyPreview.SetPublic(profile.AuthoredMap.Sites(profile).Select(s=>new PlayableMapMark(s.Id,s.Position,s.Kind,
                MapMarkState.Ready,s.Kind==PlayableBuildingKind.Headquarters&&s.Id<=lobbySetup.Participants.Count?(PlayableOwner?)(s.Id-1):null)).ToArray(),profile.ArenaHalfExtent);
        }
        private Color LobbyPaint(PlayableOwner? owner)
        {
            var setup=matchSetup??lobbySetup;int i=setup.ParticipantColor(owner.HasValue?(int)owner.Value:0);
            if(!owner.HasValue)return Color.white;
            ColorUtility.TryParseHtmlString(NativeLobbyConfiguration.ColorHex[i],out var color);return color;
        }
        private void RebuildRoster()
        {
            lobbySetup.InitializeParticipants();humanReady=aiReady=null;
            rosterPanel.Clear();
            var heading=new VisualElement();heading.style.flexDirection=FlexDirection.Row;
            var title=LobbyText("УЧАСТНИКИ",13,Muted);title.style.flexGrow=1;heading.Add(title);
            int humans=lobbySetup.Participants.Count(p=>p.Human);
            var summary=LobbyText(humans+" человека · "+(lobbySetup.Participants.Count-humans)+" бота",13,Muted);summary.name="lobby-roster-summary";heading.Add(summary);rosterPanel.Add(heading);
            rosterScroll=OrbitalTheme.Scroll();rosterScroll.style.flexGrow=1;rosterScroll.style.minHeight=0;rosterPanel.Add(rosterScroll);
            foreach(var participant in lobbySetup.Participants)ParticipantRow(participant);
            var toolbar=new VisualElement();toolbar.style.flexDirection=FlexDirection.Row;toolbar.style.flexShrink=0;toolbar.style.marginTop=12;rosterPanel.Add(toolbar);
            joinButton=LobbyAction("Присоединиться",JoinLobbyWithKeyboardMouse);joinButton.name="lobby-join";joinButton.Insert(0,new NativeControllerGlyph("Y"));toolbar.Add(joinButton);
            addBotButton=LobbyAction("Добавить бота",()=>AddLobbyParticipant(false,null));addBotButton.name="lobby-add-bot";addBotButton.Insert(0,new NativeControllerGlyph("X"));toolbar.Add(addBotButton);
            var reason=LobbyText(lobbySetup.Spectator?"РЕЖИМ ЗРИТЕЛЯ · В составе только боты":humans==1?"ОБЫЧНЫЙ МАТЧ · Мышь и геймпад одновременно":"ЛОКАЛЬНАЯ ИГРА · Каждому человеку — своё устройство",13,LobbyCyan);reason.style.flexShrink=0;reason.style.marginTop=8;reason.name="lobby-mode-reason";rosterPanel.Add(reason);
            ResizeLobby();RefreshLobbyValidation();
        }
        private void ParticipantRow(NativeLobbyParticipant participant)
        {
            int index=lobbySetup.Participants.IndexOf(participant);
            var row=new VisualElement{name="lobby-participant-"+index};row.AddToClassList("lobby-seat");row.AddToClassList("premium-participant");rosterScroll.Add(row);
            ColorUtility.TryParseHtmlString(NativeLobbyConfiguration.ColorHex[participant.Color],out var paint);row.style.borderLeftColor=paint;
            var identity=new VisualElement();identity.style.flexGrow=2;identity.style.flexBasis=0;identity.style.minWidth=120;row.Add(identity);
            var name=new TextField{value=participant.Name,maxLength=24,name=participant.Human&&index==0?"lobby-human-name":!participant.Human&&index==1?"lobby-ai-name":"lobby-name-"+index};StyleLobbyField(name);name.style.height=38;name.style.minHeight=38;name.style.marginBottom=0;identity.Add(name);var role=LobbyText(participant.Human?"Человек":"Бот",12,Muted);role.style.height=16;role.style.marginBottom=0;identity.Add(role);
            name.RegisterValueChangedCallback(evt=>{lobbyRosterEdited=true;participant.Name=evt.newValue;if(index==0&&participant.Human)lobbySetup.HumanName=evt.newValue;RefreshLobbyValidation();});
            var color=new DropdownField(NativeLobbyConfiguration.ColorNames.ToList(),participant.Color){name="lobby-color-"+index};StyleLobbyField(color);color.style.width=130;row.Add(color);
            color.RegisterValueChangedCallback(evt=>{lobbyRosterEdited=true;participant.Color=color.index;ColorUtility.TryParseHtmlString(NativeLobbyConfiguration.ColorHex[participant.Color],out var c);row.style.borderLeftColor=c;RefreshLobbyPreview();RefreshLobbyValidation();});
            var team=new DropdownField(Enumerable.Range(1,8).Select(n=>n.ToString()).ToList(),participant.Team-1){name="lobby-team-"+index};StyleLobbyField(team);team.style.width=80;row.Add(team);team.RegisterValueChangedCallback(evt=>{lobbyRosterEdited=true;participant.Team=team.index+1;RefreshLobbyValidation();});
            if(participant.Human)
            {
                var pads=Gamepad.all.ToArray();var choices=new List<string>{"Мышь + геймпад"};choices.AddRange(pads.Select((pad,i)=>"Геймпад "+(i+1)+" · "+pad.displayName));
                int deviceIndex=participant.DeviceId==0?0:Array.FindIndex(pads,pad=>pad.deviceId==participant.DeviceId)+1;
                if(participant.DeviceId!=0&&deviceIndex==0){choices.Add("Ожидание контроллера");deviceIndex=choices.Count-1;}
                var device=new DropdownField(choices,deviceIndex){name="lobby-device-"+index};StyleLobbyField(device);device.style.flexGrow=3;device.style.flexBasis=0;device.style.minWidth=145;row.Add(device);
                device.RegisterValueChangedCallback(evt=>{int id=device.index==0?0:device.index<=pads.Length?pads[device.index-1].deviceId:participant.DeviceId;if(lobbySetup.Participants.Any(p=>p!=participant&&p.Human&&p.DeviceId==id)){device.SetValueWithoutNotify(evt.previousValue);lobbyActionError="Это устройство уже назначено другому участнику.";}else{lobbyRosterEdited=true;participant.DeviceId=id;lobbyActionError=null;}RefreshLobbyValidation();});
            }
            else
            {
                var difficulty=new DropdownField(NativeLobbyConfiguration.DifficultyNames.ToList(),(int)participant.Difficulty){name=index==1?"lobby-ai-difficulty":"lobby-ai-difficulty-"+index};StyleLobbyField(difficulty);difficulty.style.flexGrow=3;difficulty.style.flexBasis=0;difficulty.style.minWidth=145;row.Add(difficulty);
                difficulty.RegisterValueChangedCallback(evt=>{lobbyRosterEdited=true;participant.Difficulty=(AiDifficulty)difficulty.index;if(index==1)lobbySetup.Difficulty=participant.Difficulty;RefreshLobbyValidation();});
            }
            var remove=new Button(()=>{lobbyRosterEdited=true;lobbySetup.Participants.Remove(participant);lobbyActionError=null;RebuildRoster();RefreshLobbyPreview();}){text="×",name="lobby-remove-"+index,tooltip="Удалить участника"};
            remove.style.width=38;remove.style.minWidth=38;remove.style.height=42;remove.style.paddingLeft=remove.style.paddingRight=0;remove.style.backgroundColor=Color.clear;
            remove.style.borderLeftWidth=remove.style.borderRightWidth=remove.style.borderTopWidth=remove.style.borderBottomWidth=0;remove.style.color=Ink;remove.style.fontSize=26;remove.style.unityTextAlign=TextAnchor.MiddleCenter;row.Add(remove);
        }
        private void AddLobbyParticipant(bool human,Gamepad pad)
        {
            lobbyActionError=lobbySetup.AddParticipant(human,pad?.deviceId??0,lobbySetup.Capacity(profile));
            if(lobbyActionError==null){lobbyRosterEdited=true;RebuildRoster();RefreshLobbyPreview();var row=rosterScroll.Q<VisualElement>("lobby-participant-"+(lobbySetup.Participants.Count-1));row?.Q<TextField>()?.Focus();rosterScroll.ScrollTo(row);}
            RefreshLobbyValidation();
        }
        private void JoinLobbyWithKeyboardMouse()
        {
            if(lobbySetup.Participants.Any(participant=>participant.Human&&participant.DeviceId==0))
            {
                lobbyActionError=null;RefreshLobbyValidation();return;
            }
            AddLobbyParticipant(true,null);
        }
        private void PollLobbyControllers()
        {
            if(!inLobby||!localInputFocused||menuNavigation?.Scope!=lobbyScreen)return;
            if(!lobbyPadArmed){lobbyPadArmed=Gamepad.all.All(p=>!p.buttonWest.isPressed&&!p.buttonNorth.isPressed);return;}
            foreach(var pad in Gamepad.all)
            {
                if(pad.buttonNorth.wasPressedThisFrame)AddLobbyParticipant(true,pad);
                else if(pad.buttonWest.wasPressedThisFrame)AddLobbyParticipant(false,null);
            }
            foreach(var glyph in lobbyScreen.Query<NativeControllerGlyph>().ToList())glyph.Refresh(Gamepad.current);
        }
        private bool StartLobbyMatchFromGamepad(Gamepad pad)
        {
            if(menuNavigation?.Scope!=lobbyScreen)return false;
            var focused=root.focusController?.focusedElement as VisualElement;
            bool editing=focused is TextField||focused?.GetFirstAncestorOfType<TextField>()!=null||
                focused is DropdownField||focused?.GetFirstAncestorOfType<DropdownField>()!=null;
            if(editing||preparing||!launchButton.enabledSelf)return true;
            LaunchLobbyMatch();
            return true;
        }
        private void StyleLobbyField(VisualElement field)
        {
            field.style.marginBottom=8;field.style.minHeight=42;field.style.color=Ink;
            var label=field.Q<Label>(className:"unity-base-field__label");if(label!=null){label.style.minWidth=65;label.style.flexShrink=0;label.style.color=Muted;}
            var inputElement=field.Q(className:"unity-base-field__input");
            if(inputElement!=null){inputElement.style.backgroundColor=new Color(.067f,.106f,.125f);inputElement.style.color=Ink;inputElement.style.minHeight=36;}
        }
        // Menu packing dimensions keep a square preview and footer apart; they do not tune gameplay, camera or combat readability.
        private void ResizeLobby()
        {
            if(lobbyScreen==null||rosterPanel==null)return;
            float width=lobbyScreen.resolvedStyle.width,height=lobbyScreen.resolvedStyle.height;
            bool narrow=width>0&&width<1000;bool compact=height>0&&height<740;
            lobbyScreen.style.paddingTop=compact?16:24;
            lobbyScreen.Q<Label>("lobby-title").style.fontSize=compact?26:34;
            lobbyColumns.style.marginTop=lobbyColumns.style.marginBottom=compact?8:18;
            if(narrow&&compact){mapPanel.style.paddingTop=mapPanel.style.paddingBottom=8;}
            lobbyColumns.style.flexDirection=narrow?FlexDirection.Column:FlexDirection.Row;
            rosterPanel.style.marginRight=narrow?0:18;rosterPanel.style.marginBottom=narrow?12:0;
            mapPanel.style.flexDirection=narrow?FlexDirection.Row:FlexDirection.Column;
            mapPanel.style.flexGrow=narrow?0:1;mapPanel.style.height=narrow?new StyleLength(compact?116:174):new StyleLength(StyleKeyword.Auto);
            lobbyPreview.style.width=narrow?new StyleLength(compact?100:150):new StyleLength(StyleKeyword.Auto);lobbyPreview.style.height=narrow?new StyleLength(compact?100:150):new StyleLength(StyleKeyword.Auto);
            lobbyPreview.style.minHeight=narrow?(compact?100:150):130;lobbyPreview.style.maxHeight=narrow?(compact?100:150):360;lobbyPreview.style.flexGrow=narrow?0:1;lobbyPreview.style.flexShrink=0;
            lobbyPreview.style.marginTop=lobbyPreview.style.marginBottom=narrow?0:14;mapInfo.style.flexGrow=narrow?1:0;mapInfo.style.flexBasis=narrow?new StyleLength(0f):new StyleLength(StyleKeyword.Auto);mapInfo.style.marginRight=narrow?12:0;
            mapPanel.style.flexBasis=narrow?new StyleLength(StyleKeyword.Auto):new StyleLength(0f);mapPanel.style.flexShrink=0;mapPanel.style.paddingTop=mapPanel.style.paddingBottom=narrow?(compact?8:12):18;lobbyColumns.style.minHeight=0;rosterPanel.style.minHeight=120;rosterPanel.style.flexShrink=1;
            mapPanel.Q<Label>("lobby-map-description").style.display=narrow?DisplayStyle.None:DisplayStyle.Flex;
            foreach(var row in rosterPanel.Query<VisualElement>(className:"premium-participant").ToList()){row.style.flexWrap=width<700?Wrap.Wrap:Wrap.NoWrap;row.style.height=width<700?new StyleLength(StyleKeyword.Auto):new StyleLength(74);foreach(var field in row.Query<VisualElement>(className:"unity-base-field").ToList()){field.style.marginBottom=0;field.style.height=38;field.style.minHeight=38;}}
        }
        private void RefreshLobbyValidation()
        {
            if(lobbyStatus==null)return;
            bool oneHuman=lobbySetup.Participants.Count(p=>p.Human)==1;bool padReady=oneHuman&&Gamepad.all.Count>0;var error=lobbySetup.Validate(profile,lobbyKeyboard?.added==true||padReady,lobbyMouse?.added==true||padReady);
            if(error==null&&!oneHuman&&lobbySetup.Participants.Any(p=>p.Human&&p.DeviceId>0&&!Gamepad.all.Any(pad=>pad.deviceId==p.DeviceId)))error="Ожидание назначенного контроллера.";
            lobbyStatus.text=error??lobbyActionError??("Состав готов · "+lobbySetup.Participants.Count+" / "+lobbySetup.Capacity(profile)+(lobbySetup.Spectator?" · Зритель":""));
            lobbyStatus.style.color=error==null?LobbyCyan:devicesReady?Danger:Amber;
            launchButton.SetEnabled(error==null);
            if(humanReady!=null){humanReady.text=devicesReady?"Мышь и клавиатура  ·  Готов":"Мышь и клавиатура  ·  Ожидание устройства";humanReady.style.color=devicesReady?Muted:Amber;}
        }
        private void BindLobbyDevices()
        {
            lobbyKeyboard=Keyboard.current;lobbyMouse=Mouse.current;
            input.AssignedKeyboard=lobbyKeyboard;input.AssignedMouse=lobbyMouse;
        }
        private IEnumerator StartEvidenceMatch()
        {
            float deadline=Time.realtimeSinceStartup+30;
            if(nativeMainMenuEnabled)
            {
                while(nativeMainMenu?.ScreenRoot==null&&Time.realtimeSinceStartup<deadline)yield return null;
                if(nativeMainMenu?.ScreenRoot==null){Record("evidence entry FAIL main menu timeout");Quit();yield break;}
                yield return new WaitForEndOfFrame();
                ScreenCapture.CaptureScreenshot(Path.Combine(evidence,"entry-main.png"));
                nativeMainMenu.ScreenRoot.Q<Button>("main-play").Focus();menuNavigation.Activate();
            }
            else ShowLobby();
            while(lobbyScreen.style.display.value!=DisplayStyle.Flex&&Time.realtimeSinceStartup<deadline)yield return null;
            if(lobbyScreen.style.display.value!=DisplayStyle.Flex){Record("evidence entry FAIL lobby timeout");Quit();yield break;}
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(evidence,"entry-lobby.png"));
            launchButton.Focus();menuNavigation.Activate();
            while((inLobby||preparing)&&Time.realtimeSinceStartup<deadline)yield return null;
            if(inLobby||preparing||runtime==null){Record("evidence entry FAIL match timeout");Quit();yield break;}
            Record("evidence entry PASS main/lobby/match");
        }
        private void ShowMainMenu()
        {
            inLobby=true;menuScreen.style.display=nativeMainMenuEnabled?DisplayStyle.None:DisplayStyle.Flex;
            if(menuNavigation!=null)menuNavigation.Start=null;
            lobbyScreen.style.display=DisplayStyle.None;SetMatchUi(false);
            if(nativeMainMenuEnabled&&nativeMainMenu==null)
            {
                nativeMainMenu=new GameObject("Spacewars main menu").AddComponent<NativeMainMenu>();
                nativeMainMenu.HostRoot=root;nativeMainMenu.LocalControls=LocalControlSettings;nativeMainMenu.Navigation=menuNavigation;nativeMainMenu.Play=ShowLobby;nativeMainMenu.Laboratory=OpenLaboratory;
            }
        }
        private void ShowLobby(){inLobby=true;lobbyPadArmed=false;matchSetup=null;ResetSpectatorPresentation();menuScreen.style.display=DisplayStyle.None;lobbyScreen.style.display=DisplayStyle.Flex;SetMatchUi(false);RefreshLobbyValidation();RefreshLobbyPreview();if(menuNavigation!=null)menuNavigation.Start=StartLobbyMatchFromGamepad;menuNavigation?.SetScope(lobbyScreen,ShowMainMenu,launchButton);}
        private void SetMatchUi(bool visible){top.style.display=bottom.style.display=visible?DisplayStyle.Flex:DisplayStyle.None;modal.style.display=DisplayStyle.None;if(!visible){if(spectatorPanel!=null)spectatorPanel.style.display=DisplayStyle.None;if(spectatorTooltip!=null)spectatorTooltip.style.display=DisplayStyle.None;}input.WorldInputEnabled=visible;input.CommandInputEnabled=visible&&matchSetup?.Spectator!=true;}
        private void LaunchLobbyMatch()
        {
            RefreshLobbyValidation();if(!launchButton.enabledSelf||preparing)return;
            if(menuNavigation!=null)menuNavigation.Start=null;menuNavigation?.SetScope(null);matchSetup=lobbySetup.Copy();preparing=true;loadingScreen.style.display=DisplayStyle.Flex;lobbyScreen.style.display=DisplayStyle.None;
            StartCoroutine(PrepareLobbyMatch());
        }
        private IEnumerator PrepareLobbyMatch()
        {
            yield return null; // Present actual preparation state before creating authority.
            if(!TryPrepareLobbySession())yield break;
            while(runtime.Latest==null)yield return null;
            ReadPresentationFrame();ServiceRoutes();Render();UpdateHud();UpdateMaps();
            yield return null; // First prepared frame; also progresses in autonomous batch Editor QA.
            inLobby=false;preparing=false;menuNavigation?.SetScope(null);loadingScreen.style.display=DisplayStyle.None;SetMatchUi(true);Pause(false);
            Record("lobby launch "+matchSetup.MatchHumanName+" team="+matchSetup.HumanTeam+" vs "+matchSetup.MatchAiName+" team="+matchSetup.AiTeam);
        }
        private bool TryPrepareLobbySession()
        {
            try{EnsureBalanceStore();profile=ComposeLobbyProfile(balanceStore.Selected);RecreateMapPresentation();StartSession();paused=true;return true;}
            catch(Exception error)
            {
                Debug.LogException(error);runtime?.RequestStop();runtime=null;preparing=false;
                loadingScreen.style.display=DisplayStyle.None;ShowLobby();
                lobbyStatus.text="Не удалось подготовить матч. Проверьте журнал и повторите запуск.";lobbyStatus.style.color=Danger;
                return false;
            }
        }
        private void ReturnToLobby(){if(localCoordinator!=null){localCoordinator.ReturnToLobby();return;}if(inLobby||returningToLobby)return;returningToLobby=true;input.WorldInputEnabled=false;runtime?.RequestStop();}
        private void UpdateLobby()
        {
            if(inLobby){input.WorldInputEnabled=false;PollLobbyControllers();RefreshLobbyValidation();}
            else if(matchSetup!=null&&!matchSetup.Spectator&&!paused){var missing=localPresentations.FirstOrDefault(seat=>!seat.SeatReady);if(missing!=null){PauseFrom(missing,true);missing.notice="Подключите устройство игрока «"+missing.localBinding.Name+"» и продолжите матч.";}}
        }
    }
}
