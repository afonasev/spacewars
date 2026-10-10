using System;
using UnityEngine;

namespace Spacewars.Presentation
{
    // Runs gain ramps on the audio thread, so a slow rendering frame cannot extend a gap.
    public sealed class MusicGainEnvelope : MonoBehaviour
    {
        private sealed class Ramp
        {
            public readonly double Start,Duration;
            public readonly float From,To;
            public Ramp(double start,double duration,float from,float to){Start=start;Duration=duration;From=from;To=to;}
            public float At(double time)=>From+(To-From)*(float)Math.Max(0,Math.Min(1,(time-Start)/Duration));
        }
        private volatile Ramp ramp=new Ramp(0,1,0,0);
        private volatile float gain;
        public volatile bool SuppressOutput;
        private int sampleRate;
        private double firstAudible=-1,lastAudible=-1;
        public double FirstAudibleDsp=>System.Threading.Volatile.Read(ref firstAudible);
        public double LastAudibleDsp=>System.Threading.Volatile.Read(ref lastAudible);
        public float FactorAt(double dspTime)=>ramp.At(dspTime);
        public void SetGain(float value)=>gain=value;
        public void Open(double start,double seconds)
        {System.Threading.Volatile.Write(ref firstAudible,-1);System.Threading.Volatile.Write(ref lastAudible,-1);ramp=new Ramp(start,seconds,0,1);}
        public void FadeTo(float target,double at,double seconds){ramp=new Ramp(at,seconds,FactorAt(at),target);}
        public void Shift(double seconds){var old=ramp;ramp=new Ramp(old.Start+seconds,old.Duration,old.From,old.To);}
        private void Awake(){sampleRate=AudioSettings.outputSampleRate;}
        private void OnAudioFilterRead(float[] data,int channels)
        {
            var current=ramp;float level=gain;double start=AudioSettings.dspTime;
            double first=System.Threading.Volatile.Read(ref firstAudible),last=System.Threading.Volatile.Read(ref lastAudible);
            for(int frame=0,index=0;index<data.Length;frame++)
            {
                float scale=level*current.At(start+(double)frame/sampleRate);
                bool audible=false;
                for(int channel=0;channel<channels;channel++,index++)
                {data[index]*=scale;if(Math.Abs(data[index])>1e-5f)audible=true;}
                if(audible)
                {
                    double time=start+(double)frame/sampleRate;
                    if(first<0)first=time;last=time;
                }
            }
            System.Threading.Volatile.Write(ref firstAudible,first);System.Threading.Volatile.Write(ref lastAudible,last);
            // QA can measure the rendered signal while keeping the shared physical output silent.
            if(SuppressOutput)Array.Clear(data,0,data.Length);
        }
    }
}
