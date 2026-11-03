using System;
using System.Diagnostics;

namespace Spacewars.Runtime
{
    internal enum SalvoPhase { BeginTick,Flight,Contact,Victims,Refused,Published,Damage,Routes }

    // Thread-local QA measurements. All call sites compile out of a release Player.
    internal static class SalvoTrace
    {
        private struct Totals
        {
            internal int Flights,Hits,Deaths,Changes,Refusals;
            internal long Stamp,Contact,Victims,Geometry,Damage,Routes;
        }

        private static readonly bool Enabled=Environment.GetEnvironmentVariable("SPACEWARS_SALVO_TIMING")=="1";
        [ThreadStatic] private static Totals tick;

        [Conditional("UNITY_EDITOR"),Conditional("DEVELOPMENT_BUILD")]
        internal static void Record(SalvoPhase phase,int count=0)
        {
            if(!Enabled)return;
            if(phase==SalvoPhase.BeginTick){tick=default;return;}
            long now=Stopwatch.GetTimestamp(),elapsed=now-tick.Stamp;
            tick.Stamp=now;
            switch(phase)
            {
                case SalvoPhase.Flight: tick.Flights++;break;
                case SalvoPhase.Contact: tick.Contact+=elapsed;tick.Hits+=count;break;
                case SalvoPhase.Victims: tick.Victims+=elapsed;tick.Changes+=count;break;
                case SalvoPhase.Refused: tick.Geometry+=elapsed;tick.Refusals++;break;
                case SalvoPhase.Published: tick.Geometry+=elapsed;tick.Deaths+=count;break;
                case SalvoPhase.Damage: tick.Damage+=elapsed;break;
                case SalvoPhase.Routes: tick.Routes+=elapsed;break;
            }
        }

        [Conditional("UNITY_EDITOR"),Conditional("DEVELOPMENT_BUILD")]
        internal static void Report(long number)
        {
            if(!Enabled||tick.Flights==0)return;
            double ms=1000d/Stopwatch.Frequency;
            Console.WriteLine(FormattableString.Invariant($"SALVO tick={number} flights={tick.Flights} hits={tick.Hits} deaths={tick.Deaths} changes={tick.Changes} refusals={tick.Refusals} contact={tick.Contact*ms:F3} victims={tick.Victims*ms:F3} geometry={tick.Geometry*ms:F3} damage={tick.Damage*ms:F3} reconcile={tick.Routes*ms:F3}"));
        }
    }
}
