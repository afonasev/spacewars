#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Globalization;

namespace Spacewars.Runtime
{
    // QA clock envelope, supplied by the runner only after Player admission.
    // It does not alter simulation time, quality, quotas or world state.
    public sealed class PlayerQaDeadline
    {
        public long FinishUtcMilliseconds{get;}
        public long HardUtcMilliseconds{get;}
        public PlayerQaDeadline(long finishUtcMilliseconds,long hardUtcMilliseconds)
        {
            if(finishUtcMilliseconds<=0||hardUtcMilliseconds<=finishUtcMilliseconds)throw new ArgumentException("Invalid QA deadline envelope.");
            FinishUtcMilliseconds=finishUtcMilliseconds;HardUtcMilliseconds=hardUtcMilliseconds;
        }
        public static PlayerQaDeadline Parse(string text)
        {
            var lines=text.Trim().Split('\n');
            if(lines.Length!=2)throw new ArgumentException("Invalid QA deadline file.");
            return new PlayerQaDeadline(long.Parse(lines[0].Trim(),CultureInfo.InvariantCulture),long.Parse(lines[1].Trim(),CultureInfo.InvariantCulture));
        }
        public bool ShouldFinish(long nowUtcMilliseconds)=>nowUtcMilliseconds>=FinishUtcMilliseconds;
        public bool ShouldCapture(long nowUtcMilliseconds)=>nowUtcMilliseconds>=FinishUtcMilliseconds-2000;
        public static bool ReadyToQuit(bool authorityStopped,bool routeWorkerStopped,NativeRouteFrameTelemetry frames)=>
            authorityStopped&&routeWorkerStopped&&(frames==null||frames.IsStopped);
    }
}
#endif
