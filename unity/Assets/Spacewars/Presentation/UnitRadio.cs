using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
using UnityEngine;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Spacewars.Input.PlayModeTests")]

namespace Spacewars.Presentation
{
    // Clip names correspond to the approved takes in Resources/Sfx/UnitVoices.
    internal sealed class UnitRadio
    {
        internal sealed class Lines
        {
            internal readonly string[] Short, Long, Rare;
            internal Lines(string shortLines, string longLines = "", string rare = "")
            {
                Short = Keys(shortLines); Long = Keys(longLines); Rare = Keys(rare);
            }
            static string[] Keys(string list) => list.Split(new[]{' '}, StringSplitOptions.RemoveEmptyEntries);
        }

        static readonly Dictionary<string, Lines> Skat = new Dictionary<string, Lines>
        {
            ["select"] = new Lines("select-1 select-3 select-4", "select-2 select-5", "rare-1"),
            ["move"] = new Lines("common-1 move-1", "move-2 move-3 move-4"),
            ["follow"] = new Lines("common-1 follow-1", "follow-2 follow-3"),
            ["advance"] = new Lines("common-1 advance-1", "advance-2 advance-3"),
            ["attack"] = new Lines("common-1 attack-1", "attack-2 attack-3"),
            ["stop"] = new Lines("common-1 stop-2", "stop-1 stop-3"),
            ["hold"] = new Lines("common-1 hold-1", "hold-2 hold-3"),
            ["repair"] = new Lines("common-1 repair-1", "repair-2 repair-3 repair-4 repair-5 repair-6", "rare-2"),
            ["building"] = new Lines("common-1 building-1", "building-2 building-3", "rare-3"),
            ["queue"] = new Lines("common-1 queue-1 queue-3", "queue-2")
        };

        static readonly Dictionary<string, Lines> Explorer = new Dictionary<string, Lines>
        {
            ["select"] = new Lines("select-1 select-2", "select-3", "rare-1"),
            ["move"] = new Lines("move-1 move-2", "move-3"),
            ["follow"] = new Lines("follow-1", "follow-2", "rare-2"),
            ["advance"] = new Lines("advance-1", "advance-2"),
            ["attack"] = new Lines("attack-2", "attack-1"),
            ["stop"] = new Lines("stop-1", "stop-2"),
            ["hold"] = new Lines("hold-1", "hold-2", "rare-3"),
            ["queue"] = new Lines("select-2", "select-2")
        };
        static readonly Dictionary<string, Lines> Tank = new Dictionary<string, Lines>
        {
            ["select"] = new Lines("select-1 select-2", "select-3"),
            ["move"] = new Lines("move-1 move-2 move-3", "move-4", "rare-3"),
            ["follow"] = new Lines("follow-1 follow-2", "follow-3", "rare-4"),
            ["advance"] = new Lines("advance-1 advance-2", "advance-3", "rare-6"),
            ["attack"] = new Lines("attack-1 attack-2", "attack-3"),
            ["stop"] = new Lines("stop-1 stop-2", "stop-3"),
            ["hold"] = new Lines("hold-1 hold-2", "hold-3", "rare-5"),
            ["queue"] = new Lines("queue-1 queue-2", "queue-3")
        };
        static readonly Dictionary<string, Lines> Hawk = new Dictionary<string, Lines>
        {
            ["select"] = new Lines("select-1 select-2 select-3", "select-4"),
            ["move"] = new Lines("move-1 move-2 move-3 common-1", "move-4"),
            ["follow"] = new Lines("follow-1 follow-2 common-2", "follow-3"),
            ["advance"] = new Lines("advance-1 common-1 common-2", "advance-2 advance-3"),
            ["attack"] = new Lines("attack-1 attack-2 attack-3", "attack-4"),
            ["stop"] = new Lines("stop-2 stop-3 common-2", "stop-1"),
            ["hold"] = new Lines("hold-2 hold-3 common-1", "hold-1"),
            ["queue"] = new Lines("queue-2 common-2", "queue-1"),
            ["relay"] = new Lines("relay-2 common-1", "relay-1"),
            ["arrived"] = new Lines("arrived-2", "arrived-1")
        };
        static readonly Dictionary<string, Lines> Cerberus = new Dictionary<string, Lines>
        {
            ["select"] = new Lines("select-1 select-2", rare: "rare-select"),
            ["move"] = new Lines("move-1 move-2"),
            ["attack"] = new Lines("attack-1 attack-2", rare: "rare-attack rare-warn"),
            ["advance"] = new Lines("advance", rare: "rare-advance"),
            ["follow"] = new Lines("follow"),
            ["stop"] = new Lines("stop stop-2"),
            ["hold"] = new Lines("hold-1"),
            ["queue"] = new Lines("queue")
        };
        static readonly Dictionary<string, Lines> Igla = new Dictionary<string, Lines>
        {
            ["select"] = new Lines("select-1 select-2", rare: "rare-select rare-short"),
            ["move"] = new Lines("move-1 move-2"),
            ["attack"] = new Lines("attack-1 attack-2", rare: "rare-attack"),
            ["advance"] = new Lines("advance"),
            ["follow"] = new Lines("follow"),
            ["stop"] = new Lines("stop"),
            ["hold"] = new Lines("hold-1 hold-2", rare: "rare-hold"),
            ["queue"] = new Lines("queue")
        };
        static readonly Dictionary<string, Lines> Shkval = new Dictionary<string, Lines>
        {
            ["select"] = new Lines("select-1 select-2 select-3", rare: "select-signature"),
            ["move"] = new Lines("move-1 move-2 move-3", rare: "move-signature"),
            ["follow"] = new Lines("follow-1 follow-2 follow-3"),
            ["advance"] = new Lines("advance-1 advance-2 advance-3"),
            ["attack"] = new Lines("attack-1 attack-2 attack-3", rare: "attack-signature"),
            ["stop"] = new Lines("stop-1 stop-2 stop-3"),
            ["hold"] = new Lines("hold-1 hold-2 hold-3"),
            ["queue"] = new Lines("queue-1 queue-2 queue-3"),
            ["arrived"] = new Lines("arrived-1 arrived-2", rare: "arrived-signature")
        };
        static readonly Dictionary<PlayableEntityKind,string[]> ClickLines = new Dictionary<PlayableEntityKind,string[]>
        {
            [PlayableEntityKind.Skat] = new[]{ "rare-4", "rare-5" },
            [PlayableEntityKind.Explorer] = new[]{ "rare-4", "rare-5", "rare-6", "rare-7", "rare-8" },
            [PlayableEntityKind.Tank] = new[]{ "rare-1", "rare-2" }
        };
        readonly Dictionary<PlayableEntityKind, Dictionary<string, Lines>> banks =
            new Dictionary<PlayableEntityKind, Dictionary<string, Lines>>
            {
                [PlayableEntityKind.Skat] = Skat, [PlayableEntityKind.Explorer] = Explorer, [PlayableEntityKind.Tank] = Tank, [PlayableEntityKind.Hawk] = Hawk,
                [PlayableEntityKind.Cerberus] = Cerberus, [PlayableEntityKind.Igla] = Igla, [PlayableEntityKind.Shkval] = Shkval
            };
        readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        readonly AudioSource source;
        readonly Func<double> clock;
        readonly System.Random random;
        readonly Dictionary<string, Request> candidates = new Dictionary<string, Request>();
        readonly Dictionary<string, Dictionary<PlayableEntityKind,double>> credits = new Dictionary<string, Dictionary<PlayableEntityKind,double>>();
        readonly Dictionary<string, Repeat> repeats = new Dictionary<string, Repeat>();
        sealed class Repeat { internal int Unit, Count; internal bool FollowUp; internal string LastJoke; internal double At; }
        sealed class Request
        {
            internal string Cue;
            internal bool Relay;
            internal Dictionary<PlayableEntityKind,int> Counts;
            internal Repeat Repeat;
        }
        string lastOwner, lastKey;
        double until;
        public UnitRadio(AudioSource channel, Func<double> clock = null, System.Random random = null)
        {
            source = channel;
            this.random = random ?? new System.Random();
            this.clock = clock ?? (() => Time.unscaledTimeAsDouble);
            foreach (var bank in banks)
                foreach (var key in bank.Value.Values.SelectMany(l => l.Short.Concat(l.Long).Concat(l.Rare))
                    .Concat(ClickLines.TryGetValue(bank.Key, out var jokes) ? jokes : Array.Empty<string>()).Distinct())
                {
                    string id = bank.Key.ToString().ToLowerInvariant() + "-" + key;
                    clips[id] = Resources.Load<AudioClip>("Sfx/UnitVoices/" + id);
                    if (!clips[id]) throw new InvalidOperationException("Missing radio recording: " + id);
                }
        }

        internal static bool IsOrder(PlayableCommandKind kind) => kind == PlayableCommandKind.Move
            || kind == PlayableCommandKind.AttackMove || kind == PlayableCommandKind.Attack || kind == PlayableCommandKind.Follow
            || kind == PlayableCommandKind.Stop || kind == PlayableCommandKind.Hold || kind == PlayableCommandKind.RepairTarget;

        static string Cue(PlayableCommand command, PlayableSnapshot view)
        {
            switch (command.Kind)
            {
                case PlayableCommandKind.Move: return command.Mode == PlayableOrderMode.Append ? "queue" : "move";
                case PlayableCommandKind.AttackMove: return command.Mode == PlayableOrderMode.Append ? "queue" : "advance";
                case PlayableCommandKind.Attack: return command.Mode == PlayableOrderMode.Append ? "queue" : "attack";
                case PlayableCommandKind.Follow: return "follow";
                case PlayableCommandKind.Stop: return "stop";
                case PlayableCommandKind.Hold: return "hold";
                case PlayableCommandKind.RepairTarget: return view.Buildings.Any(b => b.Id == command.TargetId) ? "building" : "repair";
                default: return null;
            }
        }

        public void Order(PlayableCommand command, PlayableSnapshot view, IEnumerable<int> recipients = null, bool relay = false)
        {
            if (view == null || view.Generation != command.Generation || view.OwnerId != command.PlayerId) return;
            string cue = Cue(command, view);
            if (cue != null) Offer(view, recipients ?? command.EntityIds, cue, null, relay);
        }

        public void Select(PlayableSnapshot view, ICollection<int> ids, double now)
        {
            if (view == null || ids == null) return;
            if (!repeats.TryGetValue(view.OwnerId, out var repeat)) repeats[view.OwnerId] = repeat = new Repeat();
            int id = ids.Count == 1 ? ids.First() : 0;
            if (id == 0 || repeat.Unit != id || now - repeat.At > 5)
            { repeat.Count = 0; repeat.FollowUp = false; }
            repeat.Unit = id; repeat.At = now; repeat.Count++;
            Offer(view, ids, "select", id == 0 ? null : repeat);
        }

        void Offer(PlayableSnapshot view, IEnumerable<int> ids, string cue, Repeat repeat, bool relay = false)
        {
            if (clock() < until || !NativeAudioSettings.UnitVoiceEnabled || NativeAudioSettings.UnitVoice <= 0) return;
            var selected = ids as HashSet<int> ?? new HashSet<int>(ids);
            var counts = view.Entities.Where(e => e.Owner == view.Owner && selected.Contains(e.Id) && banks.TryGetValue(e.Kind, out var bank) && bank.ContainsKey(cue))
                .GroupBy(e => e.Kind).ToDictionary(g => g.Key, g => g.Count());
            if (counts.Count == 0) return;
            if (cue == "select" && candidates.ContainsKey(view.OwnerId)) return;
            candidates[view.OwnerId] = new Request { Counts = counts, Cue = cue, Repeat = repeat, Relay = relay };
        }

        public void Arrived(PlayableSnapshot view, int pilot)
        {
            if (!candidates.ContainsKey(view.OwnerId)) Offer(view, new[]{pilot}, "arrived", null);
        }

        // Smooth weighted round robin uses only types which can answer this event.
        internal static PlayableEntityKind NextType(Dictionary<PlayableEntityKind,int> counts, Dictionary<PlayableEntityKind,double> credit)
        {
            int smallest = counts.Values.Min();
            double total = 0, highest = double.NegativeInfinity;
            PlayableEntityKind winner = default;
            foreach (var pair in counts.OrderBy(p => p.Key))
            {
                double weight = Math.Min(3, Math.Sqrt((double)pair.Value / smallest));
                credit.TryGetValue(pair.Key, out double current);
                credit[pair.Key] = current += weight; total += weight;
                if (current > highest) { highest = current; winner = pair.Key; }
            }
            credit[winner] -= total;
            return winner;
        }

        public void Flush(bool active, bool otherSpeech, float gain, double now)
        {
            source.volume = GameplayAudioPlayer.DiagnosticSuppressOutput ? 0 : gain * NativeAudioSettings.UnitVoice;
            if (!active || otherSpeech || !NativeAudioSettings.UnitVoiceEnabled || NativeAudioSettings.UnitVoice <= 0)
            { if (source.clip) { source.Stop(); source.clip = null; } until = 0; candidates.Clear(); return; }
            if (candidates.Count == 0) return;
            if (now < until) { candidates.Clear(); return; }
            var owners = candidates.Keys.OrderBy(s => s, StringComparer.Ordinal).ToArray();
            string owner = owners.FirstOrDefault(s => string.CompareOrdinal(s,lastOwner) > 0) ?? owners[0];
            var request = candidates[owner];
            candidates.Clear();
            if (!credits.TryGetValue(owner, out var credit)) credits[owner] = credit = new Dictionary<PlayableEntityKind,double>();
            var kind = NextType(request.Counts, credit);
            var lines = banks[kind][kind == PlayableEntityKind.Hawk && request.Relay && request.Cue == "move" ? "relay" : request.Cue];
            string prefix = kind.ToString().ToLowerInvariant() + "-";
            var repeat = ClickLines.ContainsKey(kind) ? request.Repeat : null;
            string key;
            if (repeat != null && repeat.FollowUp)
            { key = ClickLines[kind][1]; repeat.FollowUp = false; repeat.Count = 0; }
            else if (repeat != null && repeat.Count >= 4)
            {
                if (kind == PlayableEntityKind.Skat || kind == PlayableEntityKind.Tank)
                {
                    key = ClickLines[kind][0]; repeat.FollowUp = true;
                }
                else
                {
                    var jokes = ClickLines[kind].Where(k => k != repeat.LastJoke).ToArray();
                    repeat.LastJoke = key = jokes[random.Next(jokes.Length)];
                    repeat.Count = 0;
                }
            }
            else
            {
                int roll = random.Next(100);
                bool rareEligible = lines.Rare.Length > 0 && (!ClickLines.ContainsKey(kind) || request.Cue != "select" || repeat?.Count >= 2);
                var pool = lines.Short;
                if (roll == 99 && rareEligible) pool = lines.Rare;
                else if (roll >= 80 && lines.Long.Length > 0) pool = lines.Long;
                var choices = pool.Where(k => prefix + k != lastKey).ToArray();
                if (choices.Length == 0) choices = pool;
                key = choices[random.Next(choices.Length)];
            }
            lastOwner = owner; lastKey = prefix + key;
            source.clip = clips[lastKey]; source.Play(); until = now + source.clip.length;
        }

        public void Reset()
        {
            source.Stop(); source.clip = null; until = 0; lastOwner = lastKey = null;
            credits.Clear(); repeats.Clear(); candidates.Clear();
        }
    }
}
