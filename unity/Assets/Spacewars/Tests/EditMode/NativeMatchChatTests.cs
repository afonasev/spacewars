using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class NativeMatchChatTests
    {
        NativeLobbyConfiguration Roster()=>new NativeLobbyConfiguration{Participants=new List<NativeLobbyParticipant>{
            new NativeLobbyParticipant{Human=true,Name="Первый",Team=1,Color=0},
            new NativeLobbyParticipant{Human=true,Name="Второй",Team=1,Color=1},
            new NativeLobbyParticipant{Human=true,Name="Третий",Team=2,Color=2},
            new NativeLobbyParticipant{Human=false,Team=2,Color=3}}};
        [Test] public void OriginalTeamsAndRolesEnforceRecipientsAndAuthors()
        {
            var roster=Roster();var chat=new NativeMatchChat(roster,PlayableProfile.Default);
            Assert.AreEqual(1,chat.DefaultChannel(0));Assert.AreEqual(2,chat.DefaultChannel(2),"AI ally counts");
            roster.Participants[1].Team=2;roster.Participants[0].Name="Changed";
            Assert.True(chat.Send(0,1,"secret",0));Assert.True(chat.Send(0,NativeMatchChat.Public,"hello",0));
            Assert.AreEqual(2,chat.Visible(1,false,0).Length);Assert.AreEqual(1,chat.Visible(2,false,0).Length);
            Assert.AreEqual("hello",chat.Visible(0,true,0).Single().Text,"Spectator perspective cannot grant team access");
            Assert.AreEqual("Первый",chat.Visible(0,false,0)[0].Name,"Roster snapshot is immutable");
            Assert.False(chat.Send(0,2,"forged team",0));Assert.False(chat.Send(3,NativeMatchChat.Public,"AI",0));Assert.False(chat.Send(-1,NativeMatchChat.Public,"spectator",0));
            var solo=new NativeMatchChat(new NativeLobbyConfiguration{Participants=new List<NativeLobbyParticipant>{new NativeLobbyParticipant{Human=true,Team=1},new NativeLobbyParticipant{Team=2}}},PlayableProfile.Default);
            Assert.AreEqual(NativeMatchChat.Public,solo.DefaultChannel(0));
        }
        [Test] public void SpectatorsUsePrivateAndPublicChannelsWithoutTeamAccess()
        {
            var chat=new NativeMatchChat(Roster(),PlayableProfile.Default);
            Assert.True(chat.RegisterSpectator(-10,"Анна"));Assert.True(chat.RegisterSpectator(-11,"Борис"));
            Assert.False(chat.RegisterSpectator(0,"Игрок"));Assert.False(chat.RegisterSpectator(-10,"Другой"));
            Assert.AreEqual(NativeMatchChat.Spectators,chat.DefaultChannel(-10));
            Assert.True(chat.Send(-10,NativeMatchChat.Spectators,"Зрительское обсуждение",0));
            Assert.True(chat.Send(-10,NativeMatchChat.Public,"observer public",0));Assert.False(chat.Send(-10,1,"team forbidden",0));
            Assert.False(chat.Send(0,NativeMatchChat.Spectators,"player forbidden",0));Assert.False(chat.Send(3,NativeMatchChat.Spectators,"AI forbidden",0));
            Assert.True(chat.Send(0,1,"team secret",0));Assert.True(chat.Send(0,NativeMatchChat.Public,"public",0));
            CollectionAssert.AreEqual(new[]{"Зрительское обсуждение","observer public","public"},chat.Visible(-11,true,0).Select(m=>m.Text));
            Assert.AreEqual("Анна",chat.Visible(-11,true,0)[0].Name);Assert.AreEqual(-1,chat.Visible(-11,true,0)[0].Color);
            CollectionAssert.AreEqual(new[]{"observer public","team secret","public"},chat.Visible(1,false,0).Select(m=>m.Text));
            CollectionAssert.AreEqual(new[]{"observer public","public"},chat.Visible(2,false,0).Select(m=>m.Text));
        }
        [Test] public void CompactLabelsAndV2ProfileHaveRequestedDefaults()
        {
            Assert.AreEqual("[All]",NativeMatchChat.AudienceLabel(NativeMatchChat.Public));Assert.AreEqual("[Obs]",NativeMatchChat.AudienceLabel(NativeMatchChat.Spectators));
            Assert.AreEqual("[Team]",NativeMatchChat.AudienceLabel(1));Assert.AreEqual("[Team]",NativeMatchChat.AudienceLabel(8));
            var data=PlayableProfile.Default.CopyData();Assert.AreEqual(20,data.chatLeftPercent);Assert.AreEqual(4,data.chatReadableSeconds);Assert.AreEqual(2,data.chatFadeSeconds);Assert.AreEqual(.25,data.chatEvictionSeconds);
            Assert.AreEqual(.2,data.chatOutlineWidth);Assert.AreEqual(1,data.chatShadowOffset);Assert.AreEqual(2,data.chatShadowBlur);
            foreach(var path in new[]{"outlineWidth","shadowOffset","shadowBlur"})Assert.True(NativeBalanceFields.Editable(PlayableProfileMetadata.Fields.Single(f=>f.Path=="presentation.chat."+path)));
            var field=PlayableProfileMetadata.Fields.Single(f=>f.Path=="presentation.chat.leftPercent");Assert.AreEqual(20,field.Read(data));Assert.True(NativeBalanceFields.Editable(field));
            data.chatShadowBlur=7;Assert.Throws<ArgumentException>(()=>PlayableProfile.Create(data));data.chatShadowBlur=2;
            data.chatLeftPercent=36;Assert.Throws<ArgumentException>(()=>PlayableProfile.Create(data));
        }
        [Test] public void FadeIsContinuousAndEvictionStartsAtCurrentOpacity()
        {
            var profile=PlayableProfile.Default;var data=profile.CopyData();var chat=new NativeMatchChat(Roster(),profile);
            chat.Send(0,NativeMatchChat.Public,"first",0);var first=chat.Visible(0,false,0).Single();
            Assert.AreEqual(1,chat.Opacity(first,data.chatReadableSeconds));
            Assert.AreEqual(.75,chat.Opacity(first,data.chatReadableSeconds+data.chatFadeSeconds*.25),1e-8);
            Assert.AreEqual(.5,chat.Opacity(first,data.chatReadableSeconds+data.chatFadeSeconds*.5),1e-8);
            double now=data.chatReadableSeconds+data.chatFadeSeconds*.5;
            for(int i=0;i<(int)data.chatVisibleLimit;i++)chat.Send(0,NativeMatchChat.Public,"new"+i,now);
            Assert.AreEqual(.5,chat.Opacity(first,now),1e-8);
            Assert.AreEqual(.25,chat.Opacity(first,now+data.chatEvictionSeconds/2),1e-8);
            Assert.AreEqual(0,chat.Opacity(first,now+data.chatEvictionSeconds),1e-8);
        }
        [Test] public void TextNormalizesWithoutInterpretingMarkupAndEmptyMessagesAreRejected()
        {
            var chat=new NativeMatchChat(Roster(),PlayableProfile.Default);
            Assert.False(chat.Send(0,1," \r\n ",0));Assert.True(chat.Send(0,1,"  <b>Привет</b>\nмир  ",0));
            Assert.AreEqual("<b>Привет</b> мир",chat.Visible(0,false,0).Single().Text);
            Assert.AreEqual(160,NativeMatchChat.Normalize(new string('я',200)).Length);
            Assert.AreEqual(159,NativeMatchChat.Normalize(new string('я',159)+"😀").Length);
        }
        [Test] public void FadeAndBurstRemainBoundedAndNewMatchesStartEmpty()
        {
            var profile=PlayableProfile.Default;var data=profile.CopyData();var chat=new NativeMatchChat(Roster(),profile);
            for(int i=0;i<200;i++)chat.Send(0,1,"m"+i,0);
            Assert.LessOrEqual(chat.Visible(0,false,0).Length,2*(int)data.chatVisibleLimit);
            Assert.AreEqual((int)data.chatVisibleLimit,chat.Visible(0,false,data.chatEvictionSeconds+.01).Length);
            Assert.AreEqual(.5,chat.Opacity(chat.Visible(0,false,0).Last(),data.chatReadableSeconds+data.chatFadeSeconds/2),1e-8);
            Assert.IsEmpty(chat.Visible(0,false,data.chatReadableSeconds+data.chatFadeSeconds+.01));
            Assert.IsEmpty(new NativeMatchChat(Roster(),profile).Visible(0,false,0));
            data.chatVisibleLimit=2.5;Assert.Throws<ArgumentException>(()=>PlayableProfile.Create(data));
        }
    }
}
