using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Spacewars.Input.Tests
{
    public sealed partial class OrdinaryLocalPadTests
    {
        // These fixtures retain the exact human-chat contract while automatic AI narration coexists.
        NativeMatchChat.Message[] HumanChat(NativeMatchChat chat,int viewer,bool spectator)=>chat.Visible(viewer,spectator,Time.unscaledTimeAsDouble).Where(m=>!m.IsAi).ToArray();
        VisualElement[] HumanChatRows(PlayableBootstrap seat)
        {
            var human=new HashSet<VisualElement>(Get<Dictionary<NativeMatchChat.Message,VisualElement>>(seat,"chatLines").Where(pair=>!pair.Key.IsAi).Select(pair=>pair.Value));
            return Get<VisualElement>(seat,"chatFeed").Children().Where(human.Contains).ToArray();
        }
        void ChatKeys(PlayableBootstrap seat,params Key[] keys)
        {InputSystem.QueueStateEvent(Keyboard.current,new KeyboardState(keys));InputSystem.Update();Get<PlayableInput>(seat,"input").Poll();}
        void ChatKey(PlayableBootstrap seat,KeyCode key,EventModifiers modifiers=EventModifiers.None)
        {
            var field=Get<TextField>(seat,"chatInput");using(var evt=KeyDownEvent.GetPooled('\0',key,modifiers)){evt.target=field;field.SendEvent(evt);}
        }
        [UnityTest] public IEnumerator ChatTeamPublicFocusAndIndependentPad()
        {
            yield return StartOrdinary(2,allied:true);var author=Seats[0];var ally=Seats[1];
            var chat=Get<NativeMatchChat>(app,"matchChat");var root=Get<VisualElement>(author,"root");root.Focus();yield return null;
            ChatKeys(author,Key.Enter);Assert.True(Get<bool>(author,"chatOpen"));Assert.AreEqual(1,Get<int>(author,"chatChannel"));ChatKeys(author);
            var camera=Get<Camera>(author,"cameraView");var before=camera.transform.position;int stops=0,orders=0,selectionKeys=0;
            var input=Get<PlayableInput>(author,"input");input.Stop=()=>stops++;input.Order=(_,__,___)=>orders++;input.SelectionKey=(_,__,___)=>selectionKeys++;
            ChatKeys(author,Key.S,Key.A,Key.Digit1,Key.RightArrow);Assert.AreEqual(before,camera.transform.position);Assert.Zero(stops);Assert.Zero(orders);Assert.Zero(selectionKeys);Assert.False(input.AttackMode);
            var padBefore=Get<Vector2>(ally,"battleCursor");Set(pads[1].rightStick,Vector2.right);InputSystem.Update();yield return null;Call(app,"UpdateFrame");Set(pads[1].rightStick,Vector2.zero);InputSystem.Update();
            Assert.AreNotEqual(padBefore,Get<Vector2>(ally,"battleCursor"),"Other local device must keep working during compose");ChatKeys(author);
            Get<TextField>(author,"chatInput").value="Держим левый проход";yield return null;ChatKey(author,KeyCode.Return,EventModifiers.Shift);Assert.False(Get<bool>(author,"chatOpen"));
            Assert.AreEqual(1,HumanChat(chat,1,false).Length);Assert.IsEmpty(HumanChat(chat,3,false));Assert.IsEmpty(HumanChat(chat,0,true));
            ChatKeys(author);root.Focus();ChatKeys(author,Key.LeftShift,Key.Enter);Assert.AreEqual(NativeMatchChat.Public,Get<int>(author,"chatChannel"));ChatKeys(author);
            Get<TextField>(author,"chatInput").value="Всем удачи!";yield return null;ChatKey(author,KeyCode.Return);
            Assert.AreEqual("Всем удачи!",HumanChat(chat,0,true).Single().Text);Call(app,"UpdateFrame");
            Assert.AreEqual(2,HumanChatRows(ally).Length);Assert.AreEqual(chat.Visible(1,false,Time.unscaledTimeAsDouble).Length,Get<VisualElement>(ally,"chatFeed").childCount);Assert.True(Get<VisualElement>(author,"chatFeed").Query<Label>().ToList().All(l=>l.name=="chat-message"&&!l.parseEscapeSequences));
            yield return Capture("native-chat-allies-and-public");
            ChatKeys(author);root.Focus();ChatKeys(author,Key.Enter);Get<TextField>(author,"chatInput").value="не отправлять";yield return null;ChatKeys(author,Key.RightArrow);before=camera.transform.position;ChatKey(author,KeyCode.Escape);Assert.False(Get<bool>(author,"chatOpen"));
            ChatKeys(author,Key.RightArrow);Assert.AreEqual(before,camera.transform.position,"Closing waits for neutral input");ChatKeys(author);ChatKeys(author,Key.RightArrow);Assert.AreNotEqual(before,camera.transform.position);ChatKeys(author);
            root.Focus();ChatKeys(author,Key.Enter);Get<TextField>(author,"chatInput").value="пауза";Call(app,"Pause",true);Assert.False(Get<bool>(author,"chatOpen"));Assert.IsEmpty(Get<TextField>(author,"chatInput").value);Call(app,"Pause",false);ChatKeys(author);
            root.Focus();ChatKeys(author,Key.Enter);Get<TextField>(author,"chatInput").value="фокус";input.SetFocus(false);Assert.False(Get<bool>(author,"chatOpen"));Assert.IsEmpty(Get<TextField>(author,"chatInput").value);input.SetFocus(true);Call(app,"Pause",false);ChatKeys(author);
            Put(author,"spectatorMode",true);root.Focus();ChatKeys(author,Key.Enter);Assert.True(Get<bool>(author,"chatOpen"));Assert.AreEqual(NativeMatchChat.Spectators,Get<int>(author,"chatChannel"));ChatKeys(author);Call(author,"CloseChat",true);ChatKeys(author);
            root.Focus();ChatKeys(author,Key.LeftShift,Key.Enter);Assert.True(Get<bool>(author,"chatOpen"));Assert.AreEqual(NativeMatchChat.Public,Get<int>(author,"chatChannel"));ChatKeys(author);Call(author,"CloseChat",true);ChatKeys(author);Put(author,"spectatorMode",false);
            Call(app,"ResetChat");Assert.IsEmpty(Get<NativeMatchChat>(app,"matchChat").Visible(0,false,Time.unscaledTimeAsDouble));
        }
        struct ChatGlyph {public uint Character;public float X;public int Line;public Color32 Color;}
        ChatGlyph[] ChatGlyphs(Label label)
        {
            const BindingFlags flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
            var handle=typeof(TextElement).GetProperty("uitkTextHandle",flags).GetValue(label);
            handle.GetType().GetMethod("AddToPermanentCacheAndGenerateMesh",flags).Invoke(handle,null);
            var info=handle.GetType().BaseType.GetProperty("textInfo",flags).GetValue(handle);
            object Field(object value,string field)=>value.GetType().GetField(field,flags).GetValue(value);
            int count=(int)Field(info,"characterCount");var elements=(Array)Field(info,"textElementInfo");
            return Enumerable.Range(0,count).Select(i=>
            {
                var element=elements.GetValue(i);return new ChatGlyph{Character=Convert.ToUInt32(Field(element,"character")),X=Convert.ToSingle(Field(element,"origin")),Line=Convert.ToInt32(Field(element,"lineNumber")),Color=(Color32)Field(element,"color")};
            }).Where(g=>g.Character!=0&&g.Character!=3).ToArray();
        }
        string ChatPlain(ChatGlyph[] glyphs)=>string.Concat(glyphs.Select(g=>char.ConvertFromUtf32((int)g.Character)));
        void AssertChatFlow()
        {
            var messages=HumanChat(Get<NativeMatchChat>(app,"matchChat"),0,false);
            foreach(var seat in Seats)
            {
                var feed=Get<VisualElement>(seat,"chatFeed");var rows=HumanChatRows(seat);Assert.AreEqual(3,rows.Length);
                var root=Get<VisualElement>(seat,"root");var panel=Get<VisualElement>(seat,"chatPanel");
                Assert.AreEqual(root.worldBound.xMin+root.worldBound.width*.2f,panel.worldBound.xMin,.5f,"Chat starts at20% of its own viewport");
                for(int index=0;index<rows.Length;index++)
                {
                    var row=(Label)rows[index];var message=messages[index];var tag=NativeMatchChat.AudienceLabel(message.Team);
                    Assert.That(tag,Is.EqualTo("[All]").Or.EqualTo("[Team]").Or.EqualTo("[Obs]"));
                    Assert.AreEqual(feed.worldBound.xMin,row.worldBound.xMin,.5f,"All messages start at the common left edge");
                    Assert.AreEqual(feed.contentRect.width,row.worldBound.width,.5f,"Natural wrapping uses the full feed width");
                    Assert.Zero(row.resolvedStyle.backgroundColor.a,"Feed has no backplate");Assert.False(row.isElided,"Full author name is retained");
                    Assert.Greater(row.style.textShadow.value.blurRadius,0);Assert.AreEqual(Color.black,row.resolvedStyle.unityTextOutlineColor);
                    var glyphs=ChatGlyphs(row);Assert.AreEqual(tag+" "+message.Name+": "+message.Text,ChatPlain(glyphs),"Trusted formatting preserves the complete literal message");
                    var paint=Color.white;if(message.Color>=0)ColorUtility.TryParseHtmlString(NativeLobbyConfiguration.ColorHex[message.Color],out paint);
                    for(int i=0;i<glyphs.Length;i++)Assert.AreEqual((Color32)(i>tag.Length&&i<=tag.Length+message.Name.Length+1?paint:Color.white),glyphs[i].Color,"Only name and colon carry the author color");
                    foreach(var line in glyphs.GroupBy(g=>g.Line))Assert.AreEqual(glyphs[0].X,line.First().X,.5f,"Every wrapped continuation returns to the left edge");
                    Assert.Greater(row.worldBound.width,0);Assert.LessOrEqual(row.worldBound.xMax,feed.worldBound.xMax+.5f);
                    var minimap=Get<VisualElement>(app,"sharedMapFrame");Assert.False(row.worldBound.Overlaps(minimap.worldBound),"Wrapped text must remain clear of the shared minimap");
                }
                Assert.Greater(ChatGlyphs((Label)feed[2]).Select(g=>g.Line).Distinct().Count(),1,"Long message actually wraps");
            }
        }
        [UnityTest] public IEnumerator ChatColumnsWrapAndFadeInRealHud()
        {
            yield return StartOrdinary(2,allied:true,configureRoster:setup=>{setup.Participants[0].Name="Ян";setup.Participants[1].Name="Александр Оченьдлинный";});
            foreach(var seat in Seats)Get<PlayableInput>(seat,"input").EdgePanAxis=_=>Vector2.zero;
            var active=Get<PlayableProfile>(app,"profile").CopyData();Assert.AreEqual(4,active.chatReadableSeconds);Assert.AreEqual(2,active.chatFadeSeconds);Assert.AreEqual(20,active.chatLeftPercent);double now=0;
            void PopulateAfterRender()
            {
                // Start the unchanged 4+2s clock after camera/shader preparation, not before it.
                Call(app,"ResetChat");var chat=Get<NativeMatchChat>(app,"matchChat");now=Time.unscaledTimeAsDouble;
                Assert.True(chat.Send(0,NativeMatchChat.Public,"Привет!",now));
                Assert.True(chat.Send(1,1,"Держим левый проход.",now));
                Assert.True(chat.Send(1,NativeMatchChat.Public,"Длинное сообщение переносится по всей ширине ленты: имя не обрезается, а каждая новая строка начинается с общего левого края.",now));
                Call(app,"UpdateFrame");
            }
            yield return Capture("F3-chat-columns-wide",AssertChatFlow,beforeFrames:PopulateAfterRender);
            yield return Capture("F3-chat-columns-narrow",AssertChatFlow,1280,800,beforeFrames:PopulateAfterRender);
            var data=Get<PlayableProfile>(app,"profile").CopyData();
            double target=now+data.chatReadableSeconds+data.chatFadeSeconds*.25;
            while(Time.unscaledTimeAsDouble<target){Call(app,"UpdateFrame");yield return null;}
            Call(app,"UpdateFrame");
            foreach(var seat in Seats)foreach(var row in HumanChatRows(seat))Assert.That(row.resolvedStyle.opacity,Is.GreaterThan(0).And.LessThan(1),"Real HUD rows gradually fade");
            yield return Capture("F3-chat-fading");
            while(Time.unscaledTimeAsDouble<now+data.chatReadableSeconds+data.chatFadeSeconds+.1){Call(app,"UpdateFrame");yield return null;}
            Call(app,"UpdateFrame");foreach(var seat in Seats)Assert.Zero(HumanChatRows(seat).Length);
            const string hostile="<color=#00ff00>буквально</color> </noparse> <sprite=0> <b>текст</b>";
            Call(app,"ResetChat");var literalChat=Get<NativeMatchChat>(app,"matchChat");Assert.True(literalChat.RegisterSpectator(-9,"<b>Имя</b></noparse>"));
            Assert.True(literalChat.Send(-9,NativeMatchChat.Public,hostile,Time.unscaledTimeAsDouble));Call(app,"UpdateFrame");
            for(int frame=0;frame<8;frame++)yield return null;
            foreach(var seat in Seats)
            {
                var label=(Label)HumanChatRows(seat).Single();var glyphs=ChatGlyphs(label);
                Assert.AreEqual("[All] <b>Имя</b></noparse>: "+hostile,ChatPlain(glyphs),"Both user names and bodies keep literal markup, including closing noparse tags");
                Assert.False(glyphs.Any(g=>g.Color.g==255&&g.Color.r==0&&g.Color.b==0),"User markup cannot recolor text");
            }

        }
        void AssertObserverCapture()
        {
            var root=Get<VisualElement>(app,"root");var summary=Get<VisualElement>(app,"spectatorPanel");var chat=Get<VisualElement>(app,"chatPanel");
            Assert.AreEqual(DisplayStyle.Flex,summary.resolvedStyle.display);Inside(root.worldBound,summary.worldBound,"observer summary");Inside(root.worldBound,chat.worldBound,"observer chat");
            Assert.AreEqual(DisplayStyle.None,Get<VisualElement>(app,"top").resolvedStyle.display,"Observer has a summary instead of player resources");Assert.False(Get<PlayableInput>(app,"input").CommandInputEnabled);
            Assert.AreEqual(Color.white,Get<Label>(app,"chatChannelLabel").resolvedStyle.color);
        }
        [UnityTest] public IEnumerator LocalSpectatorComposesObserverAndPublicWithoutTeamAccess()
        {
            yield return StartOrdinary(0,configureRoster:setup=>{foreach(var participant in setup.Participants)participant.Human=false;});
            Assert.True(Get<bool>(app,"spectatorMode"));Assert.True(Get<NativeLobbyConfiguration>(app,"matchSetup").Spectator);
            Call(app,"CreateSpectatorHud");Call(app,"UpdateFrame");
            var chat=Get<NativeMatchChat>(app,"matchChat");Assert.True(chat.CanWrite(-1));Assert.False(chat.CanWrite(0),"Local observer must not impersonate AI slot0");Assert.True(chat.RegisterSpectator(-2,"Другой зритель"));
            var root=Get<VisualElement>(app,"root");var input=Get<PlayableInput>(app,"input");input.EdgePanAxis=_=>Vector2.zero;root.Focus();yield return null;
            ChatKeys(app,Key.Enter);Assert.True(Get<bool>(app,"chatOpen"));Assert.AreEqual(NativeMatchChat.Spectators,Get<int>(app,"chatChannel"));Assert.AreEqual("[Obs]:",Get<Label>(app,"chatChannelLabel").text);ChatKeys(app);
            var camera=Get<Camera>(app,"cameraView");var position=camera.transform.position;ChatKeys(app,Key.RightArrow,Key.S);Assert.AreEqual(position,camera.transform.position);Assert.False(input.CommandInputEnabled);ChatKeys(app);
            Get<TextField>(app,"chatInput").value="Только зрителям";yield return null;
            yield return Capture("F3-observer-compose",captureChecks:AssertObserverCapture);ChatKey(app,KeyCode.Return);
            Assert.AreEqual("Только зрителям",HumanChat(chat,-2,true).Single().Text);Assert.IsEmpty(HumanChat(chat,0,false));
            ChatKeys(app);root.Focus();ChatKeys(app,Key.LeftShift,Key.Enter);Assert.AreEqual(NativeMatchChat.Public,Get<int>(app,"chatChannel"));Assert.AreEqual("[All]:",Get<Label>(app,"chatChannelLabel").text);ChatKeys(app);
            Get<TextField>(app,"chatInput").value="Публичное сообщение зрителя";yield return null;ChatKey(app,KeyCode.Return);
            Assert.AreEqual("Публичное сообщение зрителя",HumanChat(chat,0,false).Single().Text);Assert.False(chat.Send(-1,1,"forbidden team",Time.unscaledTimeAsDouble));
            Call(app,"UpdateFrame");yield return Capture("F3-observer-feed",captureChecks:AssertObserverCapture);
            ChatKeys(app);Call(app,"SetSpectatorPerspective",PlayableOwner.Enemy);root.Focus();ChatKeys(app,Key.Enter);Assert.True(Get<bool>(app,"chatOpen"));Assert.AreEqual(NativeMatchChat.Spectators,Get<int>(app,"chatChannel"));
            ChatKeys(app);Get<TextField>(app,"chatInput").value="отменить";yield return null;ChatKey(app,KeyCode.Escape);Assert.False(Get<bool>(app,"chatOpen"));
        }
    }
}
