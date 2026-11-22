using System;
using UnityEngine;
namespace Spacewars.Presentation
{
    // Presentation-only style; no gameplay Balance Lab fields or strike parameters.
    [Serializable] public sealed class NuclearTargetingProfile
    {
        public string id="nuclear-aim-amber-v1";
        public int revision=1;
        public Color validColor=new Color(.97f,.77f,.42f,1),invalidColor=new Color(1,.3f,.15f,1);
        public Color glyphColor=new Color(.96f,.94f,.87f,1),panelColor=new Color(.035f,.075f,.10f,.9f);
        public float ringWidth=2,fillOpacity=.035f,innerInset=.06f,innerArcDegrees=24,tickPixels=10;
        public float iconPixels=25,compactIconPixels=12,labelFontPixels=12,labelWidthPixels=178,reasonFontPixels=10;
        public float mapLabelMinPixels=350,maxIconRadiusRatio=.55f,minIconPixels=3;
        public float aimDotPixels=3,aimDotGapPixels=3;
        public Color missileOutlineColor=new Color(.02f,.03f,.04f,1);
        public float missileOutlinePixels=2;
        public void Validate()
        {
            if(id!="nuclear-aim-amber-v1"||revision!=1)throw new ArgumentException("Invalid nuclear aiming style identity");
            foreach(float v in new[]{ringWidth,tickPixels,iconPixels,compactIconPixels,labelFontPixels,labelWidthPixels,reasonFontPixels,mapLabelMinPixels,minIconPixels,aimDotPixels,aimDotGapPixels,missileOutlinePixels})
                if(float.IsNaN(v)||float.IsInfinity(v)||v<=0)throw new ArgumentException("Invalid nuclear aiming style size");
            if(!(fillOpacity>=0&&fillOpacity<=.15f&&innerInset>0&&innerInset<.5f&&innerArcDegrees>0&&innerArcDegrees<90&&maxIconRadiusRatio>0&&maxIconRadiusRatio<.65f))throw new ArgumentException("Invalid nuclear aiming style geometry");
        }
        public static NuclearTargetingProfile Load()
        {
            var asset=Resources.Load<TextAsset>("NuclearTargetingStyle");
            if(!asset)throw new InvalidOperationException("Missing nuclear aiming style");
            var value=JsonUtility.FromJson<NuclearTargetingProfile>(asset.text);value.Validate();return value;
        }
    }
}
