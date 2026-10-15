using System.Collections;
using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Spacewars.Presentation;
using Spacewars.Simulation;

namespace Spacewars.Tests.PlayMode
{
    public sealed class NativeSilentStartupContractTests
    {
        [Test]
        public void NoAudioFlagIsAnExactCommandLineToken()
        {
            Assert.IsTrue(NativeSilentStartup.HasNoAudioFlag(new[] { "Game", "-noaudio" }));
            Assert.IsTrue(NativeSilentStartup.HasNoAudioFlag(new[] { "-NOAUDIO" }));
            Assert.IsFalse(NativeSilentStartup.HasNoAudioFlag(new[] { "Game", "-noaudio-extra" }));
            Assert.IsFalse(NativeSilentStartup.HasNoAudioFlag(new[] { "Game" }));
            Assert.IsFalse(NativeSilentStartup.HasNoAudioFlag(null));
        }

        [Test]
        public void MutedFlagIsAnExactCommandLineTokenAndEnablesAppSilentMode()
        {
            Assert.IsTrue(NativeSilentStartup.HasMutedFlag(new[] { "Game", "-muted" }));
            Assert.IsTrue(NativeSilentStartup.HasMutedFlag(new[] { "-MUTED" }));
            Assert.IsFalse(NativeSilentStartup.HasMutedFlag(new[] { "Game", "-muted-extra" }));
            Assert.IsTrue(NativeSilentStartup.HasSilentFlag(new[] { "Game", "-muted" }));
            Assert.IsTrue(NativeSilentStartup.HasSilentFlag(new[] { "Game", "-noaudio" }));
            Assert.IsFalse(NativeSilentStartup.HasSilentFlag(new[] { "Game" }));
        }

        [UnityTest]
        public IEnumerator SilentProcessDoesNotCreateProjectAudioSourcesOrMusicChildren()
        {
            Assert.IsTrue(NativeSilentStartup.IsSilent,
                "This startup contract fixture must be launched by the native QA route with -noaudio -muted.");
            var arguments = Environment.GetCommandLineArgs();
            Assert.IsTrue(NativeSilentStartup.HasNoAudioFlag(arguments), "Unity must receive -noaudio at process start.");
            Assert.IsTrue(NativeSilentStartup.HasMutedFlag(arguments), "The app must receive -muted at process start.");
            var host = new GameObject("Silent startup contract");
            var musicKey = NativeAudioSettings.Prefix + "music";
            var effectsKey = NativeAudioSettings.Prefix + "effects";
            var notificationsKey = NativeAudioSettings.Prefix + "notifications";
            const string masterKey = "spacewars.ui.volume";
            var hadMusic = PlayerPrefs.HasKey(musicKey); var savedMusic = PlayerPrefs.GetFloat(musicKey, 1);
            var hadEffects = PlayerPrefs.HasKey(effectsKey); var savedEffects = PlayerPrefs.GetFloat(effectsKey, 1);
            var hadNotifications = PlayerPrefs.HasKey(notificationsKey); var savedNotifications = PlayerPrefs.GetFloat(notificationsKey, 1);
            var hadMaster = PlayerPrefs.HasKey(masterKey); var savedMaster = PlayerPrefs.GetFloat(masterKey, 0);
            var listenerVolume = AudioListener.volume;
            try
            {
                NativeAudioSettings.Music = .13f;
                NativeAudioSettings.Effects = .27f;
                NativeAudioSettings.Notifications = .41f;
                NativeUserSettings.Volume = .63f;
                var music = host.AddComponent<AdaptiveMusicPlayer>();
                music.Initialize();
                music.Tick(GameMusicMode.Menu, 0, 0, 0, false, .016);
                var effects = host.AddComponent<GameplayAudioPlayer>();
                effects.Initialize();
                GameplayAudioPlayer.Ui();
                effects.Selection("owner-1", "selection-1");
                effects.PresentationState(1, true, false, false);
                effects.Command(new PlayableCommand(1, 1, "owner-1", PlayableCommandKind.Stop,
                    Array.Empty<int>(), origin: PlayableOrderOrigin.Human), true);
                effects.Receipt(new PlayableCommandReceipt(1, 1, PlayableCommandStatus.Rejected,
                    "bounded silent fixture", 0, "owner-1"), 1);
                effects.Tick(Array.Empty<GameplayAudioScreen>(), false, .016);
                yield return null;
                Assert.AreEqual(0, host.GetComponentsInChildren<AudioSource>(true).Length,
                    "Silent startup must stop before loading/creating music, effects, or notification sources.");
                Assert.AreEqual(0, host.transform.childCount,
                    "Silent startup must not create scheduled music bank children.");
                AssertPreferenceUnchanged(musicKey, hadMusic, savedMusic);
                AssertPreferenceUnchanged(effectsKey, hadEffects, savedEffects);
                AssertPreferenceUnchanged(notificationsKey, hadNotifications, savedNotifications);
                AssertPreferenceUnchanged(masterKey, hadMaster, savedMaster);
                Assert.AreEqual(listenerVolume, AudioListener.volume,
                    "Silent startup must not alter the app listener volume.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        private static void AssertPreferenceUnchanged(string key, bool hadKey, float value)
        {
            Assert.AreEqual(hadKey, PlayerPrefs.HasKey(key), "Saved preference key changed: " + key);
            if (hadKey) Assert.AreEqual(value, PlayerPrefs.GetFloat(key), "Saved preference value changed: " + key);
        }
    }
}
