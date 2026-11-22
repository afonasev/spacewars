using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
using UnityEngine;
namespace Spacewars.Presentation
{
    // Derived mechanics only: sampled authority displacement drives both track loops.
    // The dimensions below reproduce authored mesh geometry, not movement tuning.
    public sealed class TankMechanismPresentation : MonoBehaviour
    {
        const float Straight=1.72f,Radius=.30f,TrackX=.84f,TrackY=.335f,WheelRadius=.245f;
        const float Perimeter=2*Straight+2*Mathf.PI*Radius;
        const float SmallestNormal=1.17549435e-38f;
        Transform[][] links=new Transform[2][],wheels=new Transform[2][];
        Quaternion[][] wheelRest=new Quaternion[2][];
        float[][] linkX=new float[2][],wheelScaleY=new float[2][];
#if DEVELOPMENT_BUILD || UNITY_EDITOR
        private static readonly bool StockSamplingRequested =
            Environment.GetEnvironmentVariable("SPACEWARS_QA_TRACK_STOCK_SAMPLING") == "1";
        internal bool? FastSamplingForQa { get; set; }
#endif
        readonly double[] displayedTravel={double.NaN,double.NaN};
        Quaternion upperRotation,lowerRotation;
        float upperSin,upperCos=1;
        Transform barrel,rotorL,rotorR;Vector3 barrelRest;
        GameObject flash;Material flashMaterial;TankMechanismProfile art;
        float renderedRecoil,renderedFlashPhase,renderedRotor;
        PlayableDisplayPose previous;bool initialized;long generation=-1,lastTick=-1,lastShot=-1;
        float appliedModelScale=1;
        float frontRadius=Radius,rearRadius=Radius,frontHeight=TrackY,rearHeight=TrackY;
        float frontZ=Straight/2,rearZ=-Straight/2,upperAngle,upperLength=Straight,lowerLength=Straight;
        public float TrackLoopLength {get;private set;}=Perimeter;
        double left,right,shotTime=double.NegativeInfinity;
        public void Initialize()
        {
            art=TankMechanismProfile.Load();transform.localScale*=art.modelScale/appliedModelScale;appliedModelScale=art.modelScale;var nodes=GetComponentsInChildren<Transform>(true);
            var frontGuide=nodes.SingleOrDefault(t=>t.name=="trackFrontGuide");
            var rearGuide=nodes.SingleOrDefault(t=>t.name=="trackRearGuide");
            if((frontGuide==null)!=(rearGuide==null))throw new InvalidOperationException("Incomplete track guides");
            if(frontGuide!=null)
            {
                frontZ=frontGuide.localPosition.z;rearZ=rearGuide.localPosition.z;
                frontHeight=frontGuide.localPosition.y;rearHeight=rearGuide.localPosition.y;
                frontRadius=frontGuide.localScale.x;rearRadius=rearGuide.localScale.x;
                lowerLength=frontZ-rearZ;
                if(frontRadius<=0||rearRadius<=0||lowerLength<=0||Mathf.Abs(frontHeight-frontRadius-rearHeight+rearRadius)>.0001f)
                    throw new InvalidOperationException("Invalid authored track guides");
                float rise=rearHeight-frontHeight;
                upperAngle=Mathf.Atan2(rise,lowerLength)+Mathf.Asin((rearRadius-frontRadius)/Mathf.Sqrt(lowerLength*lowerLength+rise*rise));
                upperLength=Vector2.Distance(new Vector2(frontZ+frontRadius*Mathf.Sin(upperAngle),frontHeight+frontRadius*Mathf.Cos(upperAngle)),new Vector2(rearZ+rearRadius*Mathf.Sin(upperAngle),rearHeight+rearRadius*Mathf.Cos(upperAngle)));
                TrackLoopLength=upperLength+lowerLength+frontRadius*(Mathf.PI-upperAngle)+rearRadius*(Mathf.PI+upperAngle);
            }
            upperSin=Mathf.Sin(upperAngle);upperCos=Mathf.Cos(upperAngle);
            upperRotation=Quaternion.Euler(upperAngle*Mathf.Rad2Deg,0,0);lowerRotation=Quaternion.Euler(Mathf.PI*Mathf.Rad2Deg,0,0);
            for(int side=0;side<2;side++){
                string suffix=side==0?"L":"R";
                links[side]=Enumerable.Range(0,36).Select(i=>nodes.Single(t=>t.name=="tread"+suffix+i.ToString("D2"))).ToArray();
                wheels[side]=Enumerable.Range(0,6).Select(i=>nodes.Single(t=>t.name=="wheel"+suffix+i)).ToArray();
                wheelRest[side]=wheels[side].Select(t=>t.localRotation).ToArray();
                linkX[side]=links[side].Select(t=>t.localPosition.x).ToArray();
                wheelScaleY[side]=wheels[side].Select(t=>t.localScale.y).ToArray();
                displayedTravel[side]=double.NaN;
            }
            barrel=nodes.Single(t=>t.name=="barrelRecoil");barrelRest=barrel.localPosition;
            rotorL=nodes.FirstOrDefault(t=>t.name=="engineRotorL");rotorR=nodes.FirstOrDefault(t=>t.name=="engineRotorR");
            flash=GameObject.CreatePrimitive(PrimitiveType.Quad);flash.name="Observed muzzle flash";
            if(Application.isPlaying)Destroy(flash.GetComponent<Collider>());else DestroyImmediate(flash.GetComponent<Collider>());flash.transform.SetParent(nodes.Single(t=>t.name=="muzzle"),false);
            flashMaterial=new Material(Resources.Load<Shader>("TankMuzzle"));flashMaterial.SetColor("_BaseColor",Color.white);flash.GetComponent<Renderer>().sharedMaterial=flashMaterial;flash.GetComponent<Renderer>().shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;flash.GetComponent<Renderer>().receiveShadows=false;flash.SetActive(false);
            flash.transform.localScale=Vector3.one*art.flashSize*2;
            renderedRecoil=renderedFlashPhase=renderedRotor=float.NaN;
        }
        public void Drive(PlayableDisplayPose pose,PlayableSnapshot view,int unitId)
            => DriveWithShotIndex(pose, view, unitId, null);

        internal void DriveWithShotIndex(PlayableDisplayPose pose, PlayableSnapshot view, int unitId, TankShotIndex shots)
        {
            if(art==null)Initialize();
            bool reset=!initialized||generation!=view.Generation||view.Tick<lastTick;
            if(!reset&&!view.Paused&&(pose.Hull!=previous.Hull||!pose.Position.Equals(previous.Position))){
                double yaw=Angle(pose.Hull-previous.Hull),mid=previous.Hull+yaw*.5;
                double travel=((pose.Position.X-previous.Position.X)*Math.Cos(mid)+(pose.Position.Z-previous.Position.Z)*Math.Sin(mid))/transform.lossyScale.x;
                left+=travel+yaw*TrackX;right+=travel-yaw*TrackX;
            }
            if(reset){left=right=0;shotTime=double.NegativeInfinity;}
            double time=view.Tick/30d;
            if (shots == null) ObserveShots(view.Sounds, unitId, view.Paused, reset, ref lastShot, ref shotTime);
            else shots.Observe(view.Sounds, unitId, view.Paused, reset, ref lastShot, ref shotTime);
            bool fastSampling = TrackLoopLength >= SmallestNormal && TrackLoopLength < float.MaxValue * .5f;
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            fastSampling &= FastSamplingForQa ?? !StockSamplingRequested;
#endif
            for(int side=0;side<2;side++){
                double distance=side==0?left:right;
                if(!reset&&displayedTravel[side]==distance)continue;
                float phase=(float)(distance%TrackLoopLength);
                for(int i=0;i<36;i++)
                {
                    float z, y, angle;
                    if (fastSampling) SampleWrappedTrack(WrapLinkDistance(i*TrackLoopLength/36+phase), out z, out y, out angle);
                    else SampleTrack(i*TrackLoopLength/36+phase, out z, out y, out angle);
                    var rotation=angle==upperAngle?upperRotation:angle==Mathf.PI?lowerRotation:Quaternion.Euler(angle*Mathf.Rad2Deg,0,0);
                    links[side][i].SetLocalPositionAndRotation(new Vector3(linkX[side][i],y,z),rotation);
                }
                for(int i=0;i<6;i++)wheels[side][i].localRotation=Quaternion.AngleAxis((float)(distance/(WheelRadius*wheelScaleY[side][i])*Mathf.Rad2Deg),Vector3.right)*wheelRest[side][i];
                displayedTravel[side]=distance;
            }
            float age=(float)(time-shotTime),recoil=age>=0&&age<art.recoilSeconds?art.recoilTravel*(1-age/art.recoilSeconds):0;
            if(reset||recoil!=renderedRecoil){barrel.localPosition=barrelRest+Vector3.back*recoil;renderedRecoil=recoil;}
            bool flashing=age>=0&&age<art.flashSeconds;
            if(flash.activeSelf!=flashing)flash.SetActive(flashing);
            float flashPhase=Mathf.Clamp01(age/art.flashSeconds);
            if(reset||flashPhase!=renderedFlashPhase){flashMaterial.SetFloat("_Phase",flashPhase);renderedFlashPhase=flashPhase;}
            if(rotorL||rotorR)
            {
                float rotor=(float)((left+right)*.5*art.rotorDegreesPerMeter%360);
                if(reset||rotor!=renderedRotor)
                {
                    if(rotorL)rotorL.localRotation=Quaternion.Euler(0,0,rotor);
                    if(rotorR)rotorR.localRotation=Quaternion.Euler(0,0,-rotor);
                    renderedRotor=rotor;
                }
            }
            initialized=true;previous=pose;generation=view.Generation;lastTick=view.Tick;
        }
        private static void ObserveShots(IReadOnlyList<PlayableSoundEvent> sounds, int unitId, bool paused, bool reset,
            ref long lastShot, ref double shotTime)
        {
            if (reset) lastShot = sounds.Count == 0 ? -1 : sounds.Max(sound => sound.Id);
            foreach (var sound in sounds)
            {
                if (sound.Id <= lastShot) continue;
                if (!paused && sound.Kind == PlayableSoundKind.Shot && sound.Weapon == PlayableEntityKind.Tank && sound.UnitId == unitId)
                    shotTime = sound.Tick / 30d;
                lastShot = sound.Id;
            }
        }

        public void SampleTrack(float distance,out float z,out float y,out float angle) =>
            SampleWrappedTrack(Mathf.Repeat(distance,TrackLoopLength),out z,out y,out angle);

        private float WrapLinkDistance(float distance)
        {
            float length = TrackLoopLength;
            // A live link starts inside the loop and adds one signed remainder.
            // Use the same +/- length as Repeat. Preserve its general path for
            // values outside that range and for possible subnormal division.
            if (distance > 0 && distance < length) return distance;
            if (distance >= length && distance < 2f * length) return distance - length;
            if (distance >= -length && distance < -SmallestNormal * length) return distance + length;
            return Mathf.Repeat(distance, length);
        }

        private void SampleWrappedTrack(float s,out float z,out float y,out float angle)
        {
            if(s<upperLength){z=rearZ+rearRadius*upperSin+s*upperCos;y=rearHeight+rearRadius*upperCos-s*upperSin;angle=upperAngle;return;}
            s-=upperLength;float arc=frontRadius*(Mathf.PI-upperAngle);
            if(s<arc){angle=upperAngle+s/frontRadius;z=frontZ+frontRadius*Mathf.Sin(angle);y=frontHeight+frontRadius*Mathf.Cos(angle);return;}
            s-=arc;
            if(s<lowerLength){z=frontZ-s;y=frontHeight-frontRadius;angle=Mathf.PI;return;}
            angle=Mathf.PI+(s-lowerLength)/rearRadius;z=rearZ+rearRadius*Mathf.Sin(angle);y=rearHeight+rearRadius*Mathf.Cos(angle);
        }
        static double Angle(double a)=>Math.Atan2(Math.Sin(a),Math.Cos(a));
        public static void Point(float distance,out float z,out float y,out float angle)
        {
            float s=Mathf.Repeat(distance,Perimeter),half=Straight/2;
            if(s<Straight){z=s-half;y=TrackY+Radius;angle=0;return;}s-=Straight;
            if(s<Mathf.PI*Radius){angle=s/Radius;z=half+Radius*Mathf.Sin(angle);y=TrackY+Radius*Mathf.Cos(angle);return;}s-=Mathf.PI*Radius;
            if(s<Straight){z=half-s;y=TrackY-Radius;angle=Mathf.PI;return;}
            angle=(s-Straight)/Radius;z=-half-Radius*Mathf.Sin(angle);y=TrackY-Radius*Mathf.Cos(angle);angle+=Mathf.PI;
        }
        void OnDestroy(){if(flashMaterial){if(Application.isPlaying)Destroy(flashMaterial);else DestroyImmediate(flashMaterial);}}
    }

    // A seat receives an immutable journal already filtered to its own visibility.
    internal sealed class TankShotIndex
    {
        private IReadOnlyList<PlayableSoundEvent> indexedSounds;
        private readonly Dictionary<int, PlayableSoundEvent> latestShot = new Dictionary<int, PlayableSoundEvent>();
        private long latestEventId;

        internal void Observe(IReadOnlyList<PlayableSoundEvent> sounds, int unitId, bool paused, bool reset,
            ref long lastShot, ref double shotTime)
        {
            if (!ReferenceEquals(indexedSounds, sounds))
            {
                latestShot.Clear();
                latestEventId = long.MinValue;
                for (int i = 0; i < sounds.Count; i++)
                {
                    var sound = sounds[i];
                    // The old cursor advances on every event, including other units
                    // and non-shots. Out-of-order and duplicate IDs cannot fire later.
                    if (sound.Id <= latestEventId) continue;
                    latestEventId = sound.Id;
                    if (sound.Kind == PlayableSoundKind.Shot && sound.Weapon == PlayableEntityKind.Tank)
                        latestShot[sound.UnitId] = sound;
                }
                indexedSounds = sounds;
            }

            if (reset)
            {
                lastShot = sounds.Count == 0 ? -1 : latestEventId;
                return;
            }
            if (latestEventId <= lastShot) return;
            if (!paused && latestShot.TryGetValue(unitId, out var shot) && shot.Id > lastShot)
                shotTime = shot.Tick / 30d;
            lastShot = latestEventId;
        }
    }
}
