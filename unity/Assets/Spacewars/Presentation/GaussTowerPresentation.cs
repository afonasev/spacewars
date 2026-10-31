using System.Collections.Generic;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.Rendering;
namespace Spacewars.Presentation
{
    // All phases come from authority snapshots; pause/restore cannot advance a local effects clock.
    public sealed class GaussTowerPresentation : MonoBehaviour
    {
        SentryTurretRig rig;LineRenderer beam,halo,filament,leftCharge,rightCharge,flash;
        Vector3 restPitch;MaterialPropertyBlock block;
        readonly List<(Renderer renderer,int slot,Color emission)> emitters=new List<(Renderer,int,Color)>();
        public LineRenderer Beam=>beam;
        public float ChargeLevel{get;private set;}
        public float Envelope{get;private set;}
        public void Initialize()
        {
            if(rig)return;block=new MaterialPropertyBlock();rig=GetComponent<SentryTurretRig>();rig.Bind();restPitch=rig.Pitch.localPosition;
            beam=Line("White energy core");halo=Line("Soft energy halo");filament=Line("Energy filaments");leftCharge=Line("Left rail charge");rightCharge=Line("Right rail charge");flash=Line("Muzzle energy bloom");
            foreach(var renderer in GetComponentsInChildren<MeshRenderer>(true))
                for(int i=0;i<renderer.sharedMaterials.Length;i++)if(renderer.sharedMaterials[i].name.Contains("gauss-emissive"))emitters.Add((renderer,i,renderer.sharedMaterials[i].GetColor("_EmissionColor")));
        }
        LineRenderer Line(string name)
        {
            var child=new GameObject(name);child.transform.SetParent(transform,false);var line=child.AddComponent<LineRenderer>();line.positionCount=2;line.useWorldSpace=true;line.textureMode=LineTextureMode.Stretch;line.numCapVertices=6;
            line.shadowCastingMode=ShadowCastingMode.Off;line.receiveShadows=false;line.sharedMaterial=Resources.Load<Material>("GaussEnergy");line.enabled=false;return line;
        }
        public void Drive(PlayableBuildingSnapshot b,PlayableProfile profile)
        {
            Initialize();var s=b.Turret;rig.Pitch.localPosition=restPitch;if(s!=null){rig.SetWorldAngles(s.Heading,s.Pitch);rig.Pitch.localPosition=restPitch-rig.Pitch.parent.InverseTransformDirection(rig.Forward)*(float)s.Recoil;}
            bool active=b.Phase==ConstructionPhase.Ready&&b.Health>0&&b.PrivateState?.Lifecycle?.Selling!=true&&s!=null;
            ChargeLevel=active?(float)s.Charge:0;float charge=ChargeLevel*ChargeLevel;
            float t=active&&s.BeamSeconds>0?Mathf.Clamp01(1-(float)(s.BeamSeconds/profile.GaussBeamSeconds)):1;
            float rise=(float)profile.GaussBeamRiseFraction;
            Envelope=t<rise?Mathf.SmoothStep(0,1,t/rise):1-Mathf.SmoothStep(0,1,(t-rise)/(1-rise));
            float width=(float)(profile.GaussBeamRadius*2),energy=(float)profile.GaussBeamIntensity;
            // Shot endpoints stay fixed in world space; recoil never sweeps the damage path.
            Vector3 start=active?Point(s.BeamStart):default,end=active?Point(s.BeamEnd):default;
            bool shot=active&&s.BeamSeconds>0;
            Draw(beam,shot,start,end,width*Mathf.Lerp(.3f,1,Envelope),new Color(1,1,1,Envelope)*new Color(energy,energy,energy,1),t,0);
            Draw(halo,shot,start,end,width*(float)profile.GaussBeamGlowWidth*Mathf.Lerp(.6f,1,Envelope),new Color(.58f,.82f,1,Envelope*.4f),t,0);
            Draw(filament,shot,start,end,width*2,new Color(.8f,.93f,1,Envelope*.6f),t,1);
            Vector3 forward=rig.Forward;float scale=transform.lossyScale.x;
            var glow=new Color(.65f,.88f,1,charge);glow*=new Color((float)profile.GaussChargeIntensity,(float)profile.GaussChargeIntensity,(float)profile.GaussChargeIntensity,1);
            // Authored rail span follows the same existing socket/pivot geometry at every LOD.
            Draw(leftCharge,active&&charge>0,rig.Left.position-forward*(float)GaussTowerGeometry.BarrelOffset*scale,rig.Left.position,width*2,glow,charge,1);
            Draw(rightCharge,active&&charge>0,rig.Right.position-forward*(float)GaussTowerGeometry.BarrelOffset*scale,rig.Right.position,width*2,glow,charge,1);
            var muzzle=(rig.Left.position+rig.Right.position)*.5f;float bloom=Mathf.Max(charge,Envelope);
            Draw(flash,active&&bloom>0,muzzle-forward*width,muzzle+forward*width,width*(float)profile.GaussBeamGlowWidth,new Color(.8f,.93f,1,bloom*.7f),t,0);
            foreach(var item in emitters){item.renderer.GetPropertyBlock(block,item.slot);block.SetColor("_EmissionColor",item.emission*(1+charge*(float)profile.GaussChargeIntensity+Envelope*energy));item.renderer.SetPropertyBlock(block,item.slot);}
        }
        void Draw(LineRenderer line,bool enabled,Vector3 a,Vector3 b,float width,Color color,float phase,float ripple)
        {
            line.enabled=enabled;if(!enabled)return;line.startWidth=line.endWidth=width;line.SetPosition(0,a);line.SetPosition(1,b);
            block.Clear();block.SetColor("_BaseColor",color);block.SetFloat("_Phase",phase);block.SetFloat("_Ripple",ripple);line.SetPropertyBlock(block);
        }
        static Vector3 Point(BallisticPoint p)=>new Vector3((float)p.X,(float)p.Y,(float)p.Z);
    }
}
