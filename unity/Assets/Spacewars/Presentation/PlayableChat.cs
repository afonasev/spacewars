using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Runtime;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.UIElements;

namespace Spacewars.Presentation
{
    public sealed partial class PlayableBootstrap
    {
        private NativeMatchChat matchChat;
        private NativeTeamSignals teamSignals;
        private long lastAiCommunication;
        private NativeTeamSignals SharedSignals=>(localCoordinator??this).teamSignals;
        private PlayableTeamSignal[] VisibleAiSignals=>SharedSignals?.Visible(view.Team,IsChatSpectator,view.Tick)??Array.Empty<PlayableTeamSignal>();

        private VisualElement chatPanel,chatFeed,chatCompose;
        private TextField chatInput;
        private Label chatChannelLabel;
        private bool chatOpen;
        private int chatChannel,chatOpenedFrame;
        private readonly Dictionary<NativeMatchChat.Message,VisualElement> chatLines=new Dictionary<NativeMatchChat.Message,VisualElement>();
        private NativeMatchChat SharedChat=>(localCoordinator??this).matchChat;
        private const int LocalChatObserverId=-1;
        private int ChatAuthorId=>IsChatSpectator?LocalChatObserverId:localRosterIndex;
        private bool IsChatSpectator=>spectatorMode||matchSetup?.Spectator==true;
        private void ResetChat()
        {
            CloseChat();chatLines.Clear();chatFeed?.Clear();
            if(localCoordinator==null)
            {
                matchChat=new NativeMatchChat(matchSetup,profile);
                teamSignals=new NativeTeamSignals(matchSetup);lastAiCommunication=0;
                if(matchSetup?.Spectator==true)matchChat.RegisterSpectator(LocalChatObserverId,"Зритель");
            }
            if(chatPanel!=null)chatPanel.style.left=Length.Percent((float)profile.CopyData().chatLeftPercent);
        }
        private void CreateChatHud()
        {
            chatPanel=new VisualElement{name="match-chat",pickingMode=PickingMode.Ignore};
            chatPanel.style.position=Position.Absolute;chatPanel.style.left=Length.Percent((float)profile.CopyData().chatLeftPercent);// Preserve the existing central anchor; wrapped rows grow upward away from the bottom HUD.
            chatPanel.style.bottom=Length.Percent(100-57);chatPanel.style.width=Length.Percent(65);chatPanel.style.maxWidth=640;chatPanel.style.display=DisplayStyle.None;
            root.Add(chatPanel);chatFeed=new VisualElement{name="chat-feed",pickingMode=PickingMode.Ignore};chatPanel.Add(chatFeed);
            chatCompose=new VisualElement{name="chat-compose"};chatCompose.style.flexDirection=FlexDirection.Row;chatCompose.style.backgroundColor=PanelColor;chatCompose.style.paddingLeft=8;chatCompose.style.paddingRight=8;chatCompose.style.paddingTop=5;chatCompose.style.paddingBottom=5;chatCompose.style.display=DisplayStyle.None;chatPanel.Add(chatCompose);
            chatChannelLabel=new Label();chatChannelLabel.style.color=Amber;chatCompose.Add(chatChannelLabel);
            chatInput=new TextField{name="chat-input",maxLength=NativeMatchChat.TextLimit,multiline=false};chatInput.style.flexGrow=1;chatInput.style.minWidth=0;chatInput.style.marginTop=0;chatInput.style.marginBottom=0;chatCompose.Add(chatInput);
            chatInput.RegisterValueChangedCallback(evt=>{var normalized=NativeMatchChat.Normalize(evt.newValue);if(normalized!=evt.newValue)chatInput.SetValueWithoutNotify(normalized);});
            chatInput.RegisterCallback<KeyDownEvent>(evt=>
            {
                if(!chatOpen)return;
                if(evt.keyCode==KeyCode.Escape){evt.PreventDefault();CloseChat();}
                else if(evt.keyCode==KeyCode.Return||evt.keyCode==KeyCode.KeypadEnter){evt.PreventDefault();if(Time.frameCount!=chatOpenedFrame)SubmitChat();}
                evt.StopImmediatePropagation();
            },TrickleDown.TrickleDown);
            // Clicking outside the field cancels the draft, without turning that click into an order.
            chatInput.RegisterCallback<FocusOutEvent>(_=>{if(chatOpen)CloseChat(false);});
        }
        private void BindChatInput()
        {
            input.OpenChat=OpenChat;input.IsTextEntryActive=()=>chatOpen;
            var prior=input.FocusLost;input.FocusLost=()=>{CloseChat();prior?.Invoke();};
        }
        private void OpenChat(bool all)
        {
            if(chatOpen||!SeatUsesKeyboard||MenuOwnsInput||view==null||view.Outcome!=Spacewars.Simulation.PlayableMatchOutcome.Playing||SharedChat==null)return;
            if(IsChatSpectator&&!SharedChat.CanWrite(ChatAuthorId))SharedChat.RegisterSpectator(ChatAuthorId,"Зритель");
            if(!SharedChat.CanWrite(ChatAuthorId))return;
            input.ClearMode();battleGestures.Cancel();CloseMap();
            chatChannel=all?NativeMatchChat.Public:SharedChat.DefaultChannel(ChatAuthorId);
            chatChannelLabel.text=NativeMatchChat.AudienceLabel(chatChannel)+":";chatChannelLabel.style.color=Color.white;
            chatInput.SetValueWithoutNotify("");chatOpen=true;chatOpenedFrame=Time.frameCount;
            chatPanel.style.display=chatCompose.style.display=DisplayStyle.Flex;chatInput.Focus();
        }
        private void SubmitChat()
        {
            if(chatOpen)SharedChat?.Send(ChatAuthorId,chatChannel,chatInput.value,Time.unscaledTimeAsDouble);
            CloseChat();UpdateChatHud();
        }
        private void CloseChat(bool restoreFocus=true)
        {
            if(!chatOpen)return;chatOpen=false;chatInput?.SetValueWithoutNotify("");if(chatCompose!=null)chatCompose.style.display=DisplayStyle.None;
            input?.SuppressUntilNeutral();battleGestures.Cancel();if(restoreFocus)root?.Focus();
        }
        private void UpdateChatHud()
        {
            if(chatPanel==null)return;
            if(localCoordinator==null)ReadAiCommunications();
            bool visible=!inLobby&&!preparing&&!paused&&!ResultsVisible&&view!=null;
            if(!visible){CloseChat();chatPanel.style.display=DisplayStyle.None;return;}
            double now=Time.unscaledTimeAsDouble;
            var messages=SharedChat?.Visible(ChatAuthorId,IsChatSpectator,now)??Array.Empty<NativeMatchChat.Message>();
            var keep=new HashSet<NativeMatchChat.Message>(messages);
            foreach(var key in new List<NativeMatchChat.Message>(chatLines.Keys))if(!keep.Contains(key)){chatLines[key].RemoveFromHierarchy();chatLines.Remove(key);}
            foreach(var message in messages)
            {
                if(!chatLines.TryGetValue(message,out var line))
                {
                    var paint=Ink;if(message.Color>=0)ColorUtility.TryParseHtmlString(Spacewars.Simulation.NativeLobbyConfiguration.ColorHex[message.Color],out paint);
                    // Escape every user '<' independently: even a literal </noparse> cannot close our markup.
                    string Literal(string value)=>value.Replace("<","<noparse><</noparse>");
                    var text=new Label(NativeMatchChat.AudienceLabel(message.Team)+" <color=#"+ColorUtility.ToHtmlStringRGB(paint)+"><b>"+Literal(message.Name)+":</b></color> "+Literal(message.Text))
                        {name="chat-message",tooltip=message.Name,enableRichText=true,parseEscapeSequences=false,pickingMode=PickingMode.Ignore};
                    text.style.fontSize=14;text.style.color=Color.white;text.style.whiteSpace=WhiteSpace.Normal;text.style.unityTextAlign=TextAnchor.UpperLeft;
                    text.style.marginLeft=text.style.marginRight=text.style.marginTop=0;text.style.marginBottom=3;
                    text.style.paddingLeft=text.style.paddingRight=text.style.paddingTop=text.style.paddingBottom=0;
                    var appearance=profile.CopyData();text.style.unityTextOutlineColor=Color.black;text.style.unityTextOutlineWidth=(float)appearance.chatOutlineWidth;
                    text.style.textShadow=new TextShadow{color=Color.black,offset=new Vector2(0,(float)appearance.chatShadowOffset),blurRadius=(float)appearance.chatShadowBlur};
                    if(message.Point.HasValue)
                    {
                        text.pickingMode=PickingMode.Position;text.tooltip="Перейти к цели";
                        text.RegisterCallback<PointerDownEvent>(evt=>{if(evt.button!=0)return;evt.StopImmediatePropagation();FocusChatTarget(message.Point.Value);});
                    }
                    line=text;chatLines.Add(message,line);chatFeed.Add(line);
                }
                line.style.opacity=(float)SharedChat.Opacity(message,now);
            }
            chatPanel.style.display=chatOpen||messages.Length>0?DisplayStyle.Flex:DisplayStyle.None;
        }

        private void ReadAiCommunications()
        {
            if(runtime==null||matchChat==null||matchSetup==null||view==null)return;
            var viewers=IsChatSpectator?new[]{PlayableRuntime.LocalSpectatorId}:
                matchSetup.Participants.Select((p,i)=>new{p.Human,Id=i==0?"player-1":"foundry-"+(i+1)}).Where(p=>p.Human).Select(p=>p.Id).ToArray();
            foreach(var evt in viewers.SelectMany(runtime.AiCommunications).GroupBy(e=>e.Id).Select(g=>g.First()).OrderBy(e=>e.Id))
            {
                if(evt.Id<=lastAiCommunication)continue;lastAiCommunication=evt.Id;
                PresentAiCommunication(evt);
            }
        }
        private bool PresentAiCommunication(AiCommunication evt)
        {
            if(evt.Generation!=view.Generation||view.Tick-evt.Tick>profile.CopyData().aiChatMaxAgeSeconds*30)return false;
            int author=evt.Author=="player-1"?0:evt.Author.StartsWith("foundry-")&&int.TryParse(evt.Author.Substring(8),out int n)?n-1:-1;
            if(author<0&&evt.Author=="enemy-1")author=1;
            double now=Time.unscaledTimeAsDouble;
            if(!matchChat.SendAi(author,evt,now))return false;
            if(evt.Point.HasValue)teamSignals.Publish(evt.Id,author,evt.Point.Value,evt.Kind,view.Tick,profile);
            return true;
        }

        private void FocusChatTarget(NavPoint point)
        {
            if(cameraView==null||view==null||paused||ResultsVisible||MenuOwnsInput||chatOpen)return;
            var target=world.Point(point);var ray=cameraView.ScreenPointToRay(SeatScreenCenter);
            // Intersect at the destination height: translating from the current ground
            // hit drifts along Z when source and target lie on different terraces.
            if(!new Plane(Vector3.up,target).Raycast(ray,out float distance))return;
            var offset=target-ray.GetPoint(distance);offset.y=0;cameraView.transform.position+=offset;
            battleGestures.Cancel();input?.SuppressUntilNeutral();root.Focus();
        }
        private bool ChatMessageAtPanel(Vector2 point)=>chatFeed!=null&&chatPanel.resolvedStyle.display!=DisplayStyle.None&&
            chatLines.Any(pair=>pair.Key.Point.HasValue&&pair.Value.worldBound.Contains(point)&&pair.Value.resolvedStyle.opacity>0);

    }
}
