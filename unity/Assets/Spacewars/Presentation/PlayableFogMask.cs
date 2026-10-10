using System;
using Spacewars.Simulation;
using UnityEngine;

namespace Spacewars.Presentation
{
    // Presentation-only transitions. Gameplay never reads this texture or waits for its fade.
    public sealed class PlayableFogMask : IDisposable
    {
        private readonly PlayableProfile profile;
        private readonly float[] current,target;
        private readonly Color32[] pixels;
        private long revision=-1;
        private bool animating,publicShown;
        public Texture2D Texture{get;}
        public long TargetBuilds{get;private set;}
        public long Uploads{get;private set;}
        public long ScannedFrames{get;private set;}
        public PlayableFogMask(PlayableProfile profile)
        {
            this.profile=profile;int count=PlayableVision.RasterResolution*PlayableVision.RasterResolution;
            current=new float[count];target=new float[count];pixels=new Color32[count];
            Texture=new Texture2D(PlayableVision.RasterResolution,PlayableVision.RasterResolution,TextureFormat.RGBA32,false,true)
                {name="Team fog mask",filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};
            Reset();
        }
        public void Reset()
        {
            revision=-1;animating=false;publicShown=false;
            for(int i=0;i<current.Length;i++){current[i]=target[i]=(float)profile.FogUnseenOpacity;pixels[i]=new Color32(Byte(current[i]),0,0,255);}
            Upload();
        }
        public void ShowPublic()
        {
            if(publicShown)return;publicShown=true;
            for(int i=0;i<current.Length;i++){current[i]=target[i]=0;pixels[i]=new Color32(0,255,0,255);}
            animating=false;Upload();
        }
        private static byte Byte(float value)=>(byte)Mathf.RoundToInt(Mathf.Clamp01(value)*255);
        private void Upload(){Texture.SetPixels32(pixels);Texture.Apply(false,false);Uploads++;}
        public bool Update(TeamVisionSnapshot view,float seconds)
        {
            if(view==null)return false;
            bool dirty=false;
            if(revision!=view.FogRevision)
            {
                revision=view.FogRevision;TargetBuilds++;animating=true;
                int size=PlayableVision.RasterResolution;
                for(int y=0;y<size;y++)for(int x=0;x<size;x++)
                {
                    int i=y*size+x;double wx=((x+.5)/size*2-1)*view.HalfWidth,wz=((y+.5)/size*2-1)*view.HalfDepth;
                    float visible=0;
                    // IReadOnlyList foreach boxes/allocates an enumerator per pixel on Mono.
                    // Indexed access preserves exact source order and floating-point arithmetic.
                    for(int sourceIndex=0;sourceIndex<view.Sources.Count;sourceIndex++)
                    {
                        var source=view.Sources[sourceIndex];
                        double dx=wx-source.Position.X,dz=wz-source.Position.Z;
                        visible=Math.Max(visible,(float)Math.Max(0,Math.Min(1,(source.Radius+profile.FogEdgeFeather-Math.Sqrt(dx*dx+dz*dz))/profile.FogEdgeFeather)));
                    }
                    float explored=view.Coverage[i]/255f;
                    target[i]=Mathf.Lerp(Mathf.Lerp((float)profile.FogUnseenOpacity,(float)profile.FogExploredOpacity,explored),0,visible);
                    byte g=Byte(explored);if(pixels[i].g!=g){pixels[i].g=g;dirty=true;}
                }
            }
            if(!animating)return false;
            ScannedFrames++;animating=false;
            float reveal=seconds*1000/(float)profile.FogRevealMs,conceal=seconds*1000/(float)profile.FogConcealMs;
            for(int i=0;i<current.Length;i++)
            {
                current[i]=Mathf.MoveTowards(current[i],target[i],target[i]<current[i]?reveal:conceal);
                animating|=current[i]!=target[i];byte value=Byte(current[i]);
                if(pixels[i].r!=value){pixels[i].r=value;dirty=true;}
            }
            if(dirty)Upload();return dirty;
        }
        public void Dispose(){if(Texture){if(Application.isPlaying)UnityEngine.Object.Destroy(Texture);else UnityEngine.Object.DestroyImmediate(Texture);}}
    }
}
