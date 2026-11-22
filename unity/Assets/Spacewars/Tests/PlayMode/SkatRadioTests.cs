using System.Collections.Generic;
using NUnit.Framework;
using System.Linq;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.UIElements;

public sealed class SkatRadioTests
{
    private static PlayableSnapshot Frame(PlayableOwner owner, string id, int explorers = 0, int tanks = 0, PlayableEntitySnapshot extraUnit = null)
    {
        var drone = new PlayableEntitySnapshot(42, owner, PlayableEntityKind.Skat, new NavPoint(0,0), 20, false, 0, 0, 0);
        var units = new List<PlayableEntitySnapshot> { drone };
        void Add(PlayableEntityKind kind, int count)
        {
            for (int i = 0; i < count; i++)
                units.Add(new PlayableEntitySnapshot(42+units.Count,owner,kind,new NavPoint(i,0),20,false,0,0,0));
        }
        Add(PlayableEntityKind.Explorer, explorers);
        Add(PlayableEntityKind.Tank, tanks);
        if (extraUnit != null) units.Add(extraUnit);
        return new PlayableSnapshot("test",1,1,7,1,1,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,0,null,
            units.ToArray(),System.Array.Empty<PlayableBuildingSnapshot>(),System.Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,owner:owner,ownerId:id);
    }

    [Test]
    public void ApprovedBankSharedAcrossSeatsSkipsBusyOrdersAndRotatesSpeakers()
    {
        Assert.IsTrue(NativeSilentStartup.IsSilent);
        var clips = Resources.LoadAll<AudioClip>("Sfx/UnitVoices");
        Assert.AreEqual(32, clips.Count(c => c.name.StartsWith("hawk-")));
        Assert.AreEqual(42, clips.Count(c => c.name.StartsWith("skat-")));
        Assert.AreEqual(31, clips.Count(c => c.name.StartsWith("tank-")));
        Assert.AreEqual(24, clips.Count(c => c.name.StartsWith("explorer-")));
        var host = new GameObject("Muted radio");
        try
        {
            var source = NativeQuietStartup.AddSource(host);
            var radio = new UnitRadio(source);
            var first = Frame(PlayableOwner.Player,"a");
            var second = Frame(PlayableOwner.Enemy,"b");
            var move = new PlayableCommand(1,1,"a",PlayableCommandKind.Move,new[]{42});
            var hold = new PlayableCommand(1,1,"b",PlayableCommandKind.Hold,new[]{42});
            double now = Time.unscaledTimeAsDouble;
            radio.Order(move,first); radio.Order(hold,second); radio.Flush(true,false,1,now);
            Assert.That(source.clip.name, Does.StartWith("skat-move-").Or.EqualTo("skat-common-1"));
            var previous = source.clip;
            radio.Order(hold,second); radio.Flush(true,false,1,now);
            Assert.AreSame(previous,source.clip);
            radio.Flush(false,false,1,now); Assert.IsNull(source.clip);
            radio.Order(move,first); radio.Order(hold,second); radio.Flush(true,false,1,now);
            Assert.That(source.clip.name, Does.StartWith("skat-hold-").Or.EqualTo("skat-common-1"));
            radio.Reset();
            radio.Order(move,second); radio.Flush(true,false,1,now);
            Assert.IsNull(source.clip);
            radio.Select(first,new[]{42},now); radio.Flush(true,false,1,now);
            Assert.That(source.clip.name, Does.StartWith("skat-select-").Or.EqualTo("skat-rare-1"));
        }
        finally { Object.DestroyImmediate(host); }
    }

    [Test]
    public void HawkReportsAnAppliedFlightAtArrivalAndKeepsRelayOrdersDistinct()
    {
        const int pilot = 70;
        var command = new PlayableCommand(1,5,"a",PlayableCommandKind.Move,new[]{pilot}).AsHuman();
        var flight = new GameplayAudioPlayer.RadioOrder { Relay = true };
        flight.Applied(command);
        PlayableSnapshot At(NavigationOutcome outcome, long sequence = 5)
        {
            var stamp = new PlayableOrderStamp(pilot,PlayableOwner.Player,1,1,sequence,1,PlayableOrderOrigin.Human,
                PlayableCommandKind.Move,null,0,0);
            var hawk = new PlayableEntitySnapshot(pilot,PlayableOwner.Player,PlayableEntityKind.Hawk,default,10,
                outcome == NavigationOutcome.Moving,0,0,0,navigationOutcome:outcome,orderStamp:stamp);
            return Frame(PlayableOwner.Player,"a",extraUnit:hawk);
        }
        Assert.AreEqual(0,GameplayAudioPlayer.ObserveFlight(flight,At(NavigationOutcome.Moving)));
        Assert.AreEqual(-1,GameplayAudioPlayer.ObserveFlight(flight,At(NavigationOutcome.Arrived,6)));
        var arrived = At(NavigationOutcome.Arrived);
        Assert.AreEqual(pilot,GameplayAudioPlayer.ObserveFlight(flight,arrived));
        var host = new GameObject("Hawk radio");
        try
        {
            double now = 0;
            var source = NativeQuietStartup.AddSource(host);
            var radio = new UnitRadio(source, () => now);
            radio.Order(command,arrived,relay:true);
            radio.Flush(true,false,1,now);
            Assert.That(source.clip.name,Does.StartWith("hawk-relay-").Or.EqualTo("hawk-common-1"));
            now += source.clip.length + .1;
            radio.Arrived(arrived,pilot);
            radio.Flush(true,false,1,now);
            Assert.That(source.clip.name,Does.StartWith("hawk-arrived-"));
            for (int i=0;i<6;i++)
            {
                now += source.clip.length + .1;
                radio.Select(arrived,new[]{pilot},now);
                radio.Flush(true,false,1,now);
                Assert.That(source.clip.name,Does.StartWith("hawk-select-"));
            }
        }
        finally { Object.DestroyImmediate(host); }
    }

    private sealed class RadioRoll : System.Random
    {
        internal int Percentile;
        public override int Next(int maxValue) => maxValue == 100 ? Percentile : 0;
    }

    [Test]
    public void ShkvalUsesItsFourSignatureLinesOnlyOnTheRareRollAndMatchingCue()
    {
        Assert.AreEqual(30,Resources.LoadAll<AudioClip>("Sfx/UnitVoices").Count(c=>c.name.StartsWith("shkval-")));
        const int id = 71;
        var owner = PlayableOwner.Player;
        var move = new PlayableCommand(1,5,"a",PlayableCommandKind.Move,new[]{id}).AsHuman();
        var stamp = new PlayableOrderStamp(id,owner,1,1,5,1,PlayableOrderOrigin.Human,move.Kind,null,0,0);
        var receipt = new PlayableSpatialCompletion(stamp,1,1,default(NavPoint),NavLocation.FlatSurface,1,2);
        var unit = new PlayableEntitySnapshot(id,owner,PlayableEntityKind.Shkval,default,10,false,0,0,0,orderStamp:stamp,completion:receipt);
        var view = Frame(owner,"a",extraUnit:unit);
        var group = new GameplayAudioPlayer.RadioOrder();
        group.Applied(move);
        Assert.AreEqual(id,GameplayAudioPlayer.ObserveFlight(group,view));
        var host = new GameObject("Shkval radio");
        try
        {
            double now = 1;
            var roll = new RadioRoll();
            var source = NativeQuietStartup.AddSource(host);
            var radio = new UnitRadio(source,()=>now,roll);
            void Check(string cue, System.Action offer, bool signature)
            {
                for(int i=0;i<100;i++)
                {
                    roll.Percentile=i;
                    offer();
                    radio.Flush(true,false,1,now);
                    Assert.That(source.clip.name,Does.StartWith("shkval-"+cue+"-"));
                    Assert.AreEqual(signature && i==99,source.clip.name.EndsWith("-signature"));
                    now+=source.clip.length+.1;
                }
            }
            Check("select",()=>radio.Select(view,new[]{id},now),true);
            Check("arrived",()=>radio.Arrived(view,id),true);
            void Order(PlayableCommandKind kind,string cue,bool signature=false,PlayableOrderMode mode=PlayableOrderMode.Replace)
            {
                var command=new PlayableCommand(1,5,"a",kind,new[]{id},mode:mode);
                Check(cue,()=>radio.Order(command,view),signature);
            }
            Order(PlayableCommandKind.Move,"move",true);
            Order(PlayableCommandKind.Attack,"attack",true);
            Order(PlayableCommandKind.Follow,"follow");
            Order(PlayableCommandKind.AttackMove,"advance");
            Order(PlayableCommandKind.Stop,"stop");
            Order(PlayableCommandKind.Hold,"hold");
            Order(PlayableCommandKind.Move,"queue",mode:PlayableOrderMode.Append);
        }
        finally { Object.DestroyImmediate(host); }
    }

    [TestCase(PlayableEntityKind.Igla)]
    [TestCase(PlayableEntityKind.Cerberus)]
    public void OfficerUsesApprovedRecordingsOnlyOnMatchingOrders(PlayableEntityKind kind)
    {
        const int id = 72;
        bool igla = kind == PlayableEntityKind.Igla;
        string prefix = kind.ToString().ToLowerInvariant() + "-";
        var unit = new PlayableEntitySnapshot(id, PlayableOwner.Player, kind, default, 10, false, 0, 0, 0);
        var view = Frame(PlayableOwner.Player, "a", extraUnit: unit);
        var host = new GameObject(kind + " radio");
        try
        {
            double now = 1;
            var roll = new RadioRoll();
            var source = NativeQuietStartup.AddSource(host);
            var radio = new UnitRadio(source, () => now, roll);
            void Check(System.Action offer, string normal, string rare = null)
            {
                var observed = new HashSet<string>();
                for (int i = 0; i < 102; i++)
                {
                    roll.Percentile = System.Math.Min(i, 99);
                    offer();
                    radio.Flush(true, false, 1, now);
                    var allowed = roll.Percentile == 99 && rare != null ? rare : normal;
                    Assert.That(source.clip.name, Does.StartWith(prefix));
                    string key = source.clip.name.Substring(prefix.Length);
                    CollectionAssert.Contains(allowed.Split(' '), key);
                    observed.Add(key);
                    now += source.clip.length + .1;
                }
                CollectionAssert.AreEquivalent((rare == null ? normal : normal + " " + rare).Split(' '), observed);
            }
            void Order(PlayableCommandKind commandKind, string normal, string rare = null, PlayableOrderMode mode = PlayableOrderMode.Replace)
            {
                var command = new PlayableCommand(1, 5, "a", commandKind, new[]{id}, mode: mode);
                Check(() => radio.Order(command, view), normal, rare);
            }
            Check(() => radio.Select(view, new[]{id}, now), "select-1 select-2", igla ? "rare-select rare-short" : "rare-select");
            Order(PlayableCommandKind.Move, "move-1 move-2");
            Order(PlayableCommandKind.Attack, "attack-1 attack-2", igla ? "rare-attack" : "rare-attack rare-warn");
            Order(PlayableCommandKind.AttackMove, "advance", igla ? null : "rare-advance");
            Order(PlayableCommandKind.Follow, "follow");
            Order(PlayableCommandKind.Stop, igla ? "stop" : "stop stop-2");
            Order(PlayableCommandKind.Hold, igla ? "hold-1 hold-2" : "hold-1", igla ? "rare-hold" : null);
            Order(PlayableCommandKind.Move, "queue", mode: PlayableOrderMode.Append);
        }
        finally { Object.DestroyImmediate(host); }
    }

    [TestCase(1, 20)]
    [TestCase(16, 30)]
    public void MixedOrdersRotateActualClipsWithoutCountingBusyOrders(int explorers, int expectedExplorerReplies)
    {
        var host = new GameObject("Mixed squad radio");
        try
        {
            double now = 1;
            var source = NativeQuietStartup.AddSource(host);
            var radio = new UnitRadio(source, () => now);
            var view = Frame(PlayableOwner.Player, "a", explorers);
            var ids = view.Entities.Select(e => e.Id).ToArray();
            var move = new PlayableCommand(1,1,"a",PlayableCommandKind.Move,ids);
            string previous = null;
            int explorerReplies = 0;
            for (int i = 0; i < 40; i++)
            {
                radio.Order(move,view);
                radio.Flush(true,false,1,now);
                var clip = source.clip;
                string speaker = clip.name.Split('-')[0];
                if (explorers == 1) Assert.AreNotEqual(previous,speaker);
                if (speaker == "explorer") explorerReplies++;
                else Assert.AreEqual("skat",speaker);
                previous = speaker;
                radio.Order(move,view);
                radio.Flush(true,false,1,now);
                Assert.AreSame(clip,source.clip);
                now += clip.length + .05;
            }
            Assert.AreEqual(expectedExplorerReplies,explorerReplies);
            var repair = new PlayableCommand(1,2,"a",PlayableCommandKind.RepairTarget,ids);
            radio.Order(repair,view); radio.Flush(true,false,1,now);
            Assert.That(source.clip.name, Does.StartWith("skat-"));
        }
        finally { Object.DestroyImmediate(host); }
    }

    [Test]
    public void ExplorerRepeatClicksUseItsFiveJokesWithoutRepeatingThePreviousJoke()
    {
        var host = new GameObject("Explorer repeat clicks");
        try
        {
            double now = 1;
            var source = NativeQuietStartup.AddSource(host);
            var radio = new UnitRadio(source, () => now);
            var view = Frame(PlayableOwner.Player, "a", 1);
            string previous = null;
            for (int i=1;i<=40;i++)
            {
                radio.Select(view,new[]{43},now); radio.Flush(true,false,1,now);
                if (i%4 == 0)
                {
                    Assert.That(source.clip.name, Does.Match("^explorer-rare-[4-8]$"));
                    Assert.AreNotEqual(previous,source.clip.name);
                    previous=source.clip.name;
                }
                now += source.clip.length + .05;
            }
        }
        finally { Object.DestroyImmediate(host); }
    }

    [TestCase(1, 15)]
    [TestCase(16, 30)]
    public void ThreeUnitTypesSharePlayedOrders(int tanks, int tankReplies)
    {
        var host = new GameObject("Three-speaker squad");
        try
        {
            double now = 1;
            var source = NativeQuietStartup.AddSource(host);
            var radio = new UnitRadio(source, () => now);
            var view = Frame(PlayableOwner.Player, "a", explorers:1, tanks:tanks);
            var ids = view.Entities.Select(e => e.Id).ToArray();
            var kinds = new[]{PlayableCommandKind.Move, PlayableCommandKind.AttackMove, PlayableCommandKind.Hold,
                PlayableCommandKind.Follow, PlayableCommandKind.Stop, PlayableCommandKind.Attack};
            var replies = new List<string>();
            int eachOther = tanks == 1 ? 15 : 10;
            for (int i=0; i<tankReplies+2*eachOther; i++)
            {
                var command = new PlayableCommand(1,i+1,"a",kinds[i%kinds.Length],ids);
                radio.Order(command,view); radio.Flush(true,false,1,now);
                var clip = source.clip;
                replies.Add(clip.name.Split('-')[0]);
                radio.Order(command,view); radio.Flush(true,false,1,now);
                Assert.AreSame(clip,source.clip);
                now += clip.length + .05;
            }
            Assert.AreEqual(tankReplies,replies.Count(s => s == "tank"));
            Assert.AreEqual(eachOther,replies.Count(s => s == "skat"));
            Assert.AreEqual(eachOther,replies.Count(s => s == "explorer"));
            if (tanks == 1)
                for (int i=2; i<replies.Count; i++) Assert.AreEqual(3,replies.Skip(i-2).Take(3).Distinct().Count());
        }
        finally { Object.DestroyImmediate(host); }
    }

    [Test]
    public void RepeatedTankSelectionPlaysItsOwnJokeAndFollowUp()
    {
        var host = new GameObject("Tank radio");
        try
        {
            double now = 1;
            var source = NativeQuietStartup.AddSource(host);
            var radio = new UnitRadio(source, () => now);
            var view = Frame(PlayableOwner.Player, "a", tanks:1);
            var tank = view.Entities.Single(e => e.Kind == PlayableEntityKind.Tank);
            for (int i=1;i<=5;i++)
            {
                radio.Select(view,new[]{tank.Id},now); radio.Flush(true,false,1,now);
                if (i <= 3) Assert.That(source.clip.name, Does.StartWith("tank-select-"));
                else Assert.AreEqual(i == 4 ? "tank-rare-1" : "tank-rare-2",source.clip.name);
                now += source.clip.length + .05;
            }
        }
        finally { Object.DestroyImmediate(host); }
    }

    [TestCase(1,40,40)]
    [TestCase(100,60,20)]
    public void GroupReplyFrequencyCapsAtThreeToOne(int drones,int expectedDrone,int other)
    {
        var counts = new Dictionary<PlayableEntityKind,int>{{PlayableEntityKind.Skat,drones},{PlayableEntityKind.Tank,1}};
        var credits = new Dictionary<PlayableEntityKind,double>();
        int replies = 0;
        for (int i=0;i<80;i++) if (UnitRadio.NextType(counts,credits)==PlayableEntityKind.Skat) replies++;
        Assert.AreEqual(expectedDrone,replies);
        Assert.AreEqual(other,80-replies);
    }

    [Test]
    public void UnitVoiceSettingsAreIndependentOfAnnouncer()
    {
        var page = new VisualElement();
        NativeAudioSettings.AddSliders(page);
        Assert.NotNull(page.Q<Toggle>("settings-audio-unitVoice-enabled"));
        Assert.NotNull(page.Q<Slider>("settings-audio-unitVoice"));
        Assert.NotNull(page.Q<Toggle>("settings-audio-voice-enabled"));
        Assert.NotNull(page.Q<Slider>("settings-audio-voice"));
    }
}
