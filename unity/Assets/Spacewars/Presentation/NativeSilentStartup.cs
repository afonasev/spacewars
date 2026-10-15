using System;
using UnityEngine;

namespace Spacewars.Presentation
{
    /// <summary>Process-lifetime startup switch for automated/native silent sessions.</summary>
    public static class NativeSilentStartup
    {
        public static readonly bool IsSilent = HasSilentFlag(Environment.GetCommandLineArgs());

        public static bool HasSilentFlag(string[] arguments)
            => HasNoAudioFlag(arguments) || HasMutedFlag(arguments);

        public static bool HasNoAudioFlag(string[] arguments)
            => HasFlag(arguments, "-noaudio");

        public static bool HasMutedFlag(string[] arguments)
            => HasFlag(arguments, "-muted");

        private static bool HasFlag(string[] arguments, string flag)
        {
            if (arguments == null) return false;
            foreach (var argument in arguments)
                if (string.Equals(argument, flag, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
        private static void InitializeBeforeSplashScreen()
        {
            // Force command-line parsing before any scene bootstrap can create project audio.
            _ = IsSilent;
        }
    }
}
