using System;
using Spacewars.Simulation;
using UnityEngine;

namespace Spacewars.Presentation
{
    // One private field per rendered world. The same texture serves every facade and LOD.
    internal sealed class AntennaCityWindowLights : IDisposable
    {
        const int Size=64;
        const int UpdateTicks=4;
        static readonly int Activity=Shader.PropertyToID("_CityWindowActivity");
        static readonly int Blend=Shader.PropertyToID("_CityWindowBlend");
        readonly Material material;
        // Red and green contain the previous and next samples for smooth sub-tick fades.
        readonly byte[] pixels=new byte[Size*Size*2];
        readonly float[] brightness=new float[Size*Size], target=new float[Size*Size], combat=new float[Size*Size];
        // Keep quiet blocks out of the CPU update. This list holds each grid cell at most once.
        readonly int[] active=new int[Size*Size];
        readonly bool[] marked=new bool[Size*Size];
        int activeCount;
        readonly AntennaCityWindowProfile settings;
        readonly float cellsPerMetre;
        readonly Texture2D texture;
        readonly RenderTexture finishMap;
        long generation=-1,lastTick=-1,lastSound;
        string observer;
        int team;
        bool wasOverview,interpolating;

        public AntennaCityWindowLights(Material material,double halfExtent,AntennaCityWindowProfile settings)
        {
            this.material=material;this.settings=settings;
            cellsPerMetre=(float)(Size/(2*halfExtent));
            texture=new Texture2D(Size,Size,TextureFormat.RG16,false,true){
                name="Nova City observed street activity",wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Bilinear};
            // Sample at roughly three texels per metre to retain the road repair corners.
            int side=Mathf.Clamp(Mathf.NextPowerOfTwo(Mathf.CeilToInt((float)halfExtent*5)),256,2048);
            finishMap=new RenderTexture(side,side,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear){
                name="Nova City static surface finish",wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Bilinear};
            var activeTarget=RenderTexture.active;
            try{Graphics.Blit(null,finishMap,material,material.FindPass("CityFinishMap"));}
            finally{RenderTexture.active=activeTarget;}
            material.SetTexture(Shader.PropertyToID("_CityFinishMap"),finishMap);
            material.EnableKeyword("SPACEWARS_CITY_WINDOWS");
            material.SetTexture(Activity,texture);
        }

        public void Reset()=>lastTick=-1;

        public void Update(PlayableSnapshot view,bool overview,double seconds)
        {
            bool reset=lastTick<0||view.Generation!=generation||view.Tick<lastTick||
                view.OwnerId!=observer||view.Team!=team||overview!=wasOverview;
            if(!reset&&view.Tick-lastTick<UpdateTicks){
                if(interpolating)material.SetFloat(Blend,Mathf.Clamp01((float)(seconds*30-lastTick)/UpdateTicks));
                return;
            }
            float elapsed=reset?0:(view.Tick-lastTick)/30f;
            if(reset){
                Array.Clear(combat,0,combat.Length);
                Array.Clear(brightness,0,brightness.Length);
                Array.Clear(pixels,0,pixels.Length);
                Array.Clear(target,0,target.Length);
                Array.Clear(marked,0,marked.Length);
                activeCount=0;
                lastSound=0;
            }
            generation=view.Generation;observer=view.OwnerId;team=view.Team;wasOverview=overview;lastTick=view.Tick;
            float decay=elapsed/settings.fadeSeconds;
            for(int n=0;n<activeCount;n++){
                int i=active[n];
                target[i]=0;
                combat[i]=Mathf.Max(0,combat[i]-decay);
            }
            for(int i=0;i<view.Buildings.Count;i++){
                var b=view.Buildings[i];
                if(b.Kind==PlayableBuildingKind.Headquarters&&b.Phase==ConstructionPhase.Ready&&b.Health>0&&Visible(b.Position))
                    Stamp(target,b.Position,settings.baseRadius,settings.baseActivity,false);
            }
            // At startup only the sparse HQ pattern is already awake.
            if(reset)Array.Copy(target,brightness,target.Length);
            for(int i=0;i<view.Entities.Count;i++){
                var e=view.Entities[i];
                if(e.Moving&&!PlayableUnitRules.Aircraft(e.Kind)&&Visible(e.Position))
                    Stamp(target,e.Position,settings.trafficRadius,settings.trafficActivity,false);
            }
            for(int i=0;i<view.Sounds.Count;i++){
                var sound=view.Sounds[i];
                if(sound.Id<=lastSound)continue;
                lastSound=sound.Id;
                if((sound.Kind==PlayableSoundKind.Shot||sound.Kind==PlayableSoundKind.Impact)&&Visible(sound.Position))
                    Stamp(combat,sound.Position,settings.battleRadius,settings.shotActivity,true);
            }
            bool dirty=reset;
            interpolating=false;
            for(int n=activeCount-1;n>=0;n--){
                int i=active[n];
                float desired=Mathf.Max(target[i],combat[i]);
                brightness[i]=Mathf.MoveTowards(brightness[i],desired,desired>brightness[i]?elapsed/settings.wakeSeconds:decay);
                byte value=(byte)Mathf.RoundToInt(255*brightness[i]);
                int pixel=2*i;
                byte previous=reset?value:pixels[pixel+1];
                dirty|=pixels[pixel]!=previous||pixels[pixel+1]!=value;
                interpolating|=previous!=value;
                pixels[pixel]=previous;
                pixels[pixel+1]=value;
                if(desired==0&&brightness[i]==0&&previous==0){
                    marked[i]=false;
                    active[n]=active[--activeCount];
                }
            }
            if(dirty){
                texture.LoadRawTextureData(pixels);
                texture.Apply(false,false);
                material.SetFloat(Blend,0);
            }

            bool Visible(NavPoint point)=>view.Vision!=null&&view.Vision.IsVisible(point);
        }

        void Stamp(float[] field,NavPoint point,float metres,float level,bool accumulate)
        {
            float x=(float)point.X*cellsPerMetre+Size*.5f-.5f;
            float y=(float)point.Z*cellsPerMetre+Size*.5f-.5f;
            float radius=metres*cellsPerMetre;
            float inverseRadiusSquared=1/(radius*radius);
            int left=Mathf.Max(0,Mathf.CeilToInt(x-radius)),right=Mathf.Min(Size-1,Mathf.FloorToInt(x+radius));
            int bottom=Mathf.Max(0,Mathf.CeilToInt(y-radius)),top=Mathf.Min(Size-1,Mathf.FloorToInt(y+radius));
            for(int row=bottom;row<=top;row++){
                float rowFalloff=1-(row-y)*(row-y)*inverseRadiusSquared;
                for(int col=left;col<=right;col++){
                    float weight=rowFalloff-(col-x)*(col-x)*inverseRadiusSquared;
                    if(weight<=0)continue;
                    int index=row*Size+col;
                    if(!marked[index]){marked[index]=true;active[activeCount++]=index;}
                    float value=weight*level;
                    field[index]=accumulate?Mathf.Max(field[index],Mathf.Min(settings.battleActivity*weight,field[index]+value)):Mathf.Max(field[index],value);
                }
            }
        }
        public void Dispose()
        {
            finishMap.Release();
            if(Application.isPlaying){UnityEngine.Object.Destroy(texture);UnityEngine.Object.Destroy(finishMap);}
            else{UnityEngine.Object.DestroyImmediate(texture);UnityEngine.Object.DestroyImmediate(finishMap);}
        }
    }

    [Serializable]
    internal sealed class AntennaCityWindowProfile
    {
        public float baseRadius,baseActivity,trafficRadius,trafficActivity,battleRadius,battleActivity,shotActivity,wakeSeconds,fadeSeconds;
        public static AntennaCityWindowProfile Load()=>JsonUtility.FromJson<AntennaCityWindowProfile>(
            Resources.Load<TextAsset>("AntennaCityWindowProfile").text);
    }
}
