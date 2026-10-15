using System;
using UnityEngine;

namespace Spacewars.Presentation
{
    public static class NativeQuietStartup
    {
        public static bool IsQuietArguments(string[] arguments)=>Array.IndexOf(arguments,"-muted")>=0||Array.IndexOf(arguments,"-noaudio")>=0;
        public static readonly bool Enabled=IsQuietArguments(Environment.GetCommandLineArgs());
        public static AudioSource AddSource(GameObject host)
        {
            // Resolve the switch before audio component initialization. Unity's native
            // switch already selects quiet output before any managed startup runs.
            bool quiet=Enabled;
            var source=host.AddComponent<AudioSource>();
            source.playOnAwake=false;
            source.mute=quiet; // Set before assigning any clip or scheduling playback.
            return source;
        }
    }
}
