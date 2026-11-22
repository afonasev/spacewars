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
        private readonly double[] worldX = new double[PlayableVision.RasterResolution];
        private readonly double[] worldZ = new double[PlayableVision.RasterResolution];
        private readonly double[] sourceDistanceX = new double[PlayableVision.RasterResolution];
        private double sampledHalfWidth = double.NaN, sampledHalfDepth = double.NaN;
        private readonly int[] activePixels;
        private readonly int[] changedOpacity;
        private int changedOpacityCount;
        private int activeCount;
        private bool targetInputsValid;
        private readonly Color32[] pixels;
        private long revision=-1;
        private int team=int.MinValue;
        private bool animating,publicShown;
        private TeamVisionSnapshot rasterView;
        public Texture2D Texture{get;}
        public long TargetBuilds{get;private set;}
        public long TargetSourceSamples{get;private set;}
        public long Uploads{get;private set;}
        public long ScannedFrames{get;private set;}
        public long TargetPixels{get;private set;}
        public long TransitionPixels{get;private set;}
        public long RasterReuses{get;private set;}
        internal byte OpacityAt(int index)=>pixels[index].r;
        // A consumer that missed an upload must use the complete current raster.
        internal bool TryReadOpacityChanges(long previousUpload, out ReadOnlySpan<int> indices)
        {
            indices = default;
            if (previousUpload == Uploads) return true;
            if (previousUpload != Uploads - 1 || changedOpacityCount < 0) return false;
            indices = changedOpacity.AsSpan(0, changedOpacityCount);
            return true;
        }
        public PlayableFogMask(PlayableProfile profile)
        {
            this.profile=profile;int count=PlayableVision.RasterResolution*PlayableVision.RasterResolution;
            current=new float[count];target=new float[count];visibility=new float[count];lastVisibility=new float[count];lastCoverage=new byte[count];activePixels=new int[count];changedOpacity=new int[count];pixels=new Color32[count];
            Texture=new Texture2D(PlayableVision.RasterResolution,PlayableVision.RasterResolution,TextureFormat.RGBA32,false,true)
                {name="Team fog mask",filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};
            Reset();
        }
        public void Reset()
        {
            revision=-1;team=int.MinValue;animating=false;publicShown=false;activeCount=0;targetInputsValid=false;rasterView=null;
            for(int i=0;i<current.Length;i++){current[i]=target[i]=(float)profile.FogUnseenOpacity;pixels[i]=new Color32(Byte(current[i]),0,0,255);}
            Upload(-1);
        }
        public void ShowPublic()
        {
            if(publicShown)return;publicShown=true;
            for(int i=0;i<current.Length;i++){current[i]=target[i]=0;pixels[i]=new Color32(0,255,0,255);}
            animating=false;activeCount=0;targetInputsValid=false;rasterView=null;Upload(-1);
        }
        private static byte Byte(float value)=>(byte)Mathf.RoundToInt(Mathf.Clamp01(value)*255);
        private void Upload(int opacityChanges)
        {
            Texture.SetPixels32(pixels);
            Texture.Apply(false, false);
            changedOpacityCount = opacityChanges;
            Uploads++;
        }
        public bool Update(TeamVisionSnapshot view,float seconds)=>Update(view,seconds,null);
        public bool Update(TeamVisionSnapshot view,float seconds,PlayableFogMask rasterSource)
        {
            if(view==null)return false;
            bool dirty=false;
            if(revision!=view.FogRevision||team!=view.Team)
            {
                team=view.Team;revision=view.FogRevision;TargetBuilds++;animating=true;
                // Only the immutable input raster is shared. Each camera retains
                // its own fade, explored coverage, texture and reset lifecycle.
                if(rasterSource!=null&&ReferenceEquals(rasterSource.rasterView,view)&&
                    rasterSource.profile.FogEdgeFeather==profile.FogEdgeFeather)
                {
                    Array.Copy(rasterSource.visibility,visibility,visibility.Length);
                    RasterReuses++;
                }
                else RasterVisibility(view);
                rasterView=view;
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
            int opacityChanges = 0;
            for(int n=0;n<count;n++)
            {
                int i=full?n:activePixels[n];TransitionPixels++;
                current[i]=Mathf.MoveTowards(current[i],target[i],target[i]<current[i]?reveal:conceal);
                if(current[i]!=target[i]){activePixels[nextCount++]=i;animating=true;}
                byte value=Byte(current[i]);
                if (pixels[i].r != value)
                {
                    pixels[i].r = value;
                    changedOpacity[opacityChanges++] = i;
                    dirty = true;
                }
            }
            activeCount=nextCount;
            if(dirty)Upload(opacityChanges);return dirty;
        }
        private void RasterVisibility(TeamVisionSnapshot view)
        {
            const int size=PlayableVision.RasterResolution;
            if (sampledHalfWidth != view.HalfWidth || sampledHalfDepth != view.HalfDepth)
            {
                sampledHalfWidth = view.HalfWidth;
                sampledHalfDepth = view.HalfDepth;
                for (int i = 0; i < size; i++)
                {
                    double center = (i + .5) / size * 2 - 1;
                    worldX[i] = center * sampledHalfWidth;
                    worldZ[i] = center * sampledHalfDepth;
                }
            }
            Array.Clear(visibility,0,visibility.Length);
            double feather = profile.FogEdgeFeather;
            // Sources affect only their radius plus feather. Accumulate in the
            // original source order so every pixel keeps identical float math.
            for(int sourceIndex=0;sourceIndex<view.Sources.Count;sourceIndex++)
            {
                var source=view.Sources[sourceIndex];
                double reach=source.Radius+feather;
                int left=Bound(source.Position.X-reach,view.HalfWidth,size,-1);
                int right=Bound(source.Position.X+reach,view.HalfWidth,size,1);
                int bottom=Bound(source.Position.Z-reach,view.HalfDepth,size,-1);
                int top=Bound(source.Position.Z+reach,view.HalfDepth,size,1);
                // Keep the original point and radius arithmetic. Each column and
                // row's squared distance is shared by the other axis.
                for (int x = left; x <= right; x++)
                {
                    double dx = worldX[x] - source.Position.X;
                    sourceDistanceX[x] = dx * dx;
                }
                for (int y = bottom; y <= top; y++)
                {
                    double dz = worldZ[y] - source.Position.Z;
                    double distanceZ = dz * dz;
                    int row = y * size;
                    for (int x = left; x <= right; x++)
                    {
                        int i = row + x;
                        // Max cannot change a fully visible pixel.
                        if (visibility[i] == 1) continue;
                        double distance = Math.Sqrt(sourceDistanceX[x] + distanceZ);
                        visibility[i] = Math.Max(visibility[i], (float)Math.Max(0, Math.Min(1, (reach - distance) / feather)));
                        TargetSourceSamples++;
                    }
                }
            }
        }
        // One conservative pixel on either side also covers rounding at cell edges.
        private static int Bound(double coordinate,double half,int size,int margin)
            =>(int)Math.Max(0,Math.Min(size-1,Math.Floor((coordinate+half)/(2*half)*size)+margin));
        public void Dispose(){if(Texture){if(Application.isPlaying)UnityEngine.Object.Destroy(Texture);else UnityEngine.Object.DestroyImmediate(Texture);}}
    }
}
