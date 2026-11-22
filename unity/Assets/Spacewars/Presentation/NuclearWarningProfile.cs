using System;
using UnityEngine;

namespace Spacewars.Presentation
{
    // Presentation only. Strike radius, timing and the existing line-width control stay authoritative.
    [Serializable] public sealed class NuclearWarningProfile
    {
        public string id="nuclear-warning-soft-v1";
        public int revision=1;
        public Color color=new Color(1,.385f,.34f,1),outline=new Color(.045f,.025f,.025f,.9f);
        public float centerOpacity=.045f,edgeOpacity=.12f,lipOpacity=.22f;
        public float widthScale=.68f,outlinePixels=1.5f,glowPixels=4,glowOpacity=.16f;
        public float innerWidth=1.2f,innerOpacity=.8f,innerStart=.72f,innerEnd=.06f;
        public float dotPixels=3,tickPixels=8;
        public void Validate()
        {
            if(id!="nuclear-warning-soft-v1"||revision!=1)throw new ArgumentException("Invalid nuclear warning identity");
            foreach(float value in new[]{widthScale,outlinePixels,glowPixels,innerWidth,dotPixels,tickPixels})
                if(float.IsNaN(value)||float.IsInfinity(value)||value<=0)throw new ArgumentException("Invalid nuclear warning size");
            if(!(centerOpacity>=0&&centerOpacity<=edgeOpacity&&edgeOpacity<=lipOpacity&&lipOpacity<=.25f&&
                glowOpacity>=0&&glowOpacity<=.25f&&innerOpacity>0&&innerOpacity<=1&&innerEnd>0&&innerEnd<innerStart&&innerStart<1))
                throw new ArgumentException("Invalid nuclear warning opacity or geometry");
        }
        public static NuclearWarningProfile Load()
        {
            var asset=Resources.Load<TextAsset>("NuclearWarningStyle");
            if(!asset)throw new InvalidOperationException("Missing nuclear warning style");
            var value=JsonUtility.FromJson<NuclearWarningProfile>(asset.text);value.Validate();return value;
        }
    }
}
