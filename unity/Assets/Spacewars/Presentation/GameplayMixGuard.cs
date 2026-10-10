using System;
using System.Threading;
using UnityEngine;

namespace Spacewars.Presentation
{
    // Final listener filter, not a gunshot side-chain. Technical headroom ceiling
    // protects combined music/SFX peaks without altering their normal balance.
    public sealed class GameplayMixGuard : MonoBehaviour
    {
        public const float Ceiling=.8f;
        private float level=1;
        private double inputPeak,outputPeak;
        private int blocks;
        public volatile bool SuppressOutput;
        private sealed class Capture
        {internal float[] Data;internal int Count,Channels;}
        private Capture capture;
        public double InputPeak=>Volatile.Read(ref inputPeak);
        public double OutputPeak=>Volatile.Read(ref outputPeak);
        public int Blocks=>Volatile.Read(ref blocks);
        public void BeginCapture(int samples){Volatile.Write(ref capture,new Capture{Data=new float[samples]});}
        public float[] EndCapture(out int channels)
        {
            var c=Interlocked.Exchange(ref capture,null);channels=c?.Channels??2;
            if(c==null)return Array.Empty<float>();int n=Volatile.Read(ref c.Count);var result=new float[n];Array.Copy(c.Data,result,n);return result;
        }
        public void Process(float[] data,int channels)
        {
            float peak=0;foreach(var x in data)peak=Math.Max(peak,Math.Abs(x));
            float target=peak>Ceiling?Ceiling/peak:1;
            level=Math.Min(target,level+.025f); // Immediate peak protection, gradual block release.
            double outgoing=0;
            for(int i=0;i<data.Length;i++){data[i]*=level;outgoing=Math.Max(outgoing,Math.Abs(data[i]));}
            Volatile.Write(ref inputPeak,Math.Max(InputPeak,peak));Volatile.Write(ref outputPeak,Math.Max(OutputPeak,outgoing));Interlocked.Increment(ref blocks);
            var c=Volatile.Read(ref capture);
            if(c!=null)
            {
                int n=Math.Min(data.Length,c.Data.Length-c.Count);
                c.Channels=channels;Array.Copy(data,0,c.Data,c.Count,n);Volatile.Write(ref c.Count,c.Count+n);
            }
            if(SuppressOutput)Array.Clear(data,0,data.Length);
        }
        private void OnAudioFilterRead(float[] data,int channels)=>Process(data,channels);
    }
}
