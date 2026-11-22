using System;
using Spacewars.Simulation;
using UnityEngine;

namespace Spacewars.Presentation
{
    public sealed partial class PlayableBootstrap
    {
        private readonly System.Random greetingRandom = new System.Random();
        private AiGreetingLines.Greeting[] greetings = Array.Empty<AiGreetingLines.Greeting>();
        private int nextAiGreeting;
        private double greetingElapsed, nextGreetingAt;
        private double? greetingClock;

        private void ResetAiGreetings()
        {
            greetings = matchSetup?.Participants == null
                ? Array.Empty<AiGreetingLines.Greeting>()
                : AiGreetingLines.Select(matchSetup.Participants, greetingRandom);
            nextAiGreeting = 0;
            greetingElapsed = 0;
            nextGreetingAt = AiGreetingLines.StartDelaySeconds;
            greetingClock = null;
        }

        private void PresentAiGreetings()
        {
            if (nextAiGreeting >= greetings.Length) return;
            if (runtime == null || view == null || view.Generation != runtime.Generation ||
                inLobby || preparing || paused || view.Paused || restarting || returningToLobby ||
                quitting || runtime.IsStopped || runtime.Result != null ||
                view.Outcome != PlayableMatchOutcome.Playing || !string.IsNullOrEmpty(view.Failure))
            {
                greetingClock = null;
                return;
            }

            double now = Time.unscaledTimeAsDouble;
            if (greetingClock.HasValue) greetingElapsed += now - greetingClock.Value;
            greetingClock = now;
            if (greetingElapsed < nextGreetingAt) return;

            var speech = gameplayAudio?.Speech;
            if (speech?.CanPlayGreeting(now) == false) return;

            var greeting = greetings[nextAiGreeting++];
            var clip = Resources.Load<AudioClip>("Sfx/Voice/" + greeting.Phrase.Key);
            if (matchChat.SendAiPublicLine(greeting.Slot, greeting.Phrase.Text, now))
                speech?.PlayGreeting(greeting.Phrase.Key, now);
            nextGreetingAt = greetingElapsed + (clip ? clip.length : 0) + AiGreetingLines.GapSeconds;
        }
    }
}
