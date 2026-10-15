using System;
using Spacewars.Simulation;
using UnityEngine;

namespace Spacewars.Presentation
{
    // Presentation-only transitions. Gameplay never reads this texture or waits for its fade.
    public sealed class PlayableFogMask : IDisposable
    {
        private readonly PlayableProfile profile;
        private readonly float[] current,target,visibility,lastVisibility;
        private readonly byte[] lastCoverage;
        private readonly int[] activePixels;
        private int activeCount;
        private bool targetInputsValid;
        private readonly Color32[] pixels;
        private long revision=-1;
        private int team=int.MinValue;
        private bool animating,publicShown;
        public Texture2D Texture{get;}
        public long TargetBuilds{get;private set;}
        public long TargetSourceSamples{get;private set;}
        public long Uploads{get;private set;}
        public long ScannedFrames{get;private set;}
        public long TargetPixels{get;private set;}
        public long TransitionPixels{get;private set;}
        internal byte OpacityAt(int index)=>pixels[index].r;
        public PlayableFogMask(PlayableProfile profile)
        {
            this.profile=profile;int count=PlayableVision.RasterResolution*PlayableVision.RasterResolution;
            current=new float[count];target=new float[count];visibility=new float[count];lastVisibility=new float[count];lastCoverage=new byte[count];activePixels=new int[count];pixels=new Color32[count];
            Texture=new Texture2D(PlayableVision.RasterResolution,PlayableVision.RasterResolution,TextureFormat.RGBA32,false,true)
                {name="Team fog mask",filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};
            Reset();
        }
        public void Reset()
        {
            revision=-1;team=int.MinValue;animating=false;publicShown=false;activeCount=0;targetInputsValid=false;
            for(int i=0;i<current.Length;i++){current[i]=target[i]=(float)profile.FogUnseenOpacity;pixels[i]=new Color32(Byte(current[i]),0,0,255);}
            Upload();
        }
        public void ShowPublic()
        {
            if(publicShown)return;publicShown=true;
            for(int i=0;i<current.Length;i++){current[i]=target[i]=0;pixels[i]=new Color32(0,255,0,255);}
            animating=false;activeCount=0;targetInputsValid=false;Upload();
        }
        private static byte Byte(float value)=>(byte)Mathf.RoundToInt(Mathf.Clamp01(value)*255);
        private void Upload(){Texture.SetPixels32(pixels);Texture.Apply(false,false);Uploads++;}
        public bool Update(TeamVisionSnapshot view,float seconds)
        {
            if(view==null)return false;
            bool dirty=false;
            if(revision!=view.FogRevision||team!=view.Team)
            {
                team=view.Team;revision=view.FogRevision;TargetBuilds++;animating=true;
                int size=PlayableVision.RasterResolution;
                Array.Clear(visibility,0,visibility.Length);
                // Sources affect only their radius plus feather. Accumulate in the
                // original source order so every pixel keeps identical float math.
                for(int sourceIndex=0;sourceIndex<view.Sources.Count;sourceIndex++)
                {
                    var source=view.Sources[sourceIndex];
                    double reach=source.Radius+profile.FogEdgeFeather;
                    int left=Bound(source.Position.X-reach,view.HalfWidth,size,-1);
                    int right=Bound(source.Position.X+reach,view.HalfWidth,size,1);
                    int bottom=Bound(source.Position.Z-reach,view.HalfDepth,size,-1);
                    int top=Bound(source.Position.Z+reach,view.HalfDepth,size,1);
                    for(int y=bottom;y<=top;y++)for(int x=left;x<=right;x++)
                    {
                        int i=y*size+x;
                        // Max cannot change a fully visible pixel.
                        if(visibility[i]==1)continue;
                        double wx=((x+.5)/size*2-1)*view.HalfWidth,wz=((y+.5)/size*2-1)*view.HalfDepth;
                        double dx=wx-source.Position.X,dz=wz-source.Position.Z;
                        visibility[i]=Math.Max(visibility[i],(float)Math.Max(0,Math.Min(1,(source.Radius+profile.FogEdgeFeather-Math.Sqrt(dx*dx+dz*dz))/profile.FogEdgeFeather)));
                        TargetSourceSamples++;
                    }
                }
                activeCount=0;
                for(int i=0;i<target.Length;i++)
                {
                    float visible=visibility[i];byte coverage=view.Coverage[i];
                    if(!targetInputsValid||visible!=lastVisibility[i]||coverage!=lastCoverage[i]){
                        float explored=coverage/255f;
                        target[i]=Mathf.Lerp(Mathf.Lerp((float)profile.FogUnseenOpacity,(float)profile.FogExploredOpacity,explored),0,visible);
                        byte g=Byte(explored);if(pixels[i].g!=g){pixels[i].g=g;dirty=true;}
                        lastVisibility[i]=visible;lastCoverage[i]=coverage;TargetPixels++;
                    }
                    if(current[i]!=target[i])activePixels[activeCount++]=i;
                }
                targetInputsValid=true;
            }
            if(!animating)return false;
            ScannedFrames++;animating=false;
            float reveal=seconds*1000/(float)profile.FogRevealMs,conceal=seconds*1000/(float)profile.FogConcealMs;
            // Preserve the original nonstandard negative/NaN input behavior too.
            bool full=seconds<0||float.IsNaN(seconds);int count=full?current.Length:activeCount,nextCount=0;
            for(int n=0;n<count;n++)
            {
                int i=full?n:activePixels[n];TransitionPixels++;
                current[i]=Mathf.MoveTowards(current[i],target[i],target[i]<current[i]?reveal:conceal);
                if(current[i]!=target[i]){activePixels[nextCount++]=i;animating=true;}
                byte value=Byte(current[i]);
                if(pixels[i].r!=value){pixels[i].r=value;dirty=true;}
            }
            activeCount=nextCount;
            if(dirty)Upload();return dirty;
        }
        // One conservative pixel on either side also covers rounding at cell edges.
        private static int Bound(double coordinate,double half,int size,int margin)
            =>(int)Math.Max(0,Math.Min(size-1,Math.Floor((coordinate+half)/(2*half)*size)+margin));
        public void Dispose(){if(Texture){if(Application.isPlaying)UnityEngine.Object.Destroy(Texture);else UnityEngine.Object.DestroyImmediate(Texture);}}
    }
}
