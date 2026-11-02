using System;
using System.Linq;
using UnityEngine;

namespace Spacewars.Presentation
{
    // The authoritative site presenter supplies ownership and accumulated
    // controlled MATCH time. No wall clock,
    // Update(), capture logic, income, navigation, collider or save mutation.
    public sealed class RelayTowerPresentation : MonoBehaviour
    {
        Transform rotor;
        Quaternion bind;
        Renderer[] surfaces;
        ActorMaterialProfile finish;
        Color? previousOwner;
        bool painted;
        double angle;
        public RelayTowerProfile Profile {get;private set;}
        public float YawDegrees=>(float)angle;
        public bool Controlled {get;private set;}
        public void Initialize()
        {
            if(rotor)return;
            Profile=RelayTowerProfile.Load();
            rotor=GetComponentsInChildren<Transform>(true).Single(t=>t.name=="antennaYaw");
            bind=rotor.localRotation;
            surfaces=GetComponentsInChildren<Renderer>(true);
            finish=ActorMaterialProfile.Load();
        }
        public void Sample(Color? owner,double controlledMatchSeconds)
        {
            if(!double.IsFinite(controlledMatchSeconds)||controlledMatchSeconds<0)throw new ArgumentOutOfRangeException(nameof(controlledMatchSeconds));
            Drive(owner,0);angle=controlledMatchSeconds*Profile.rotationDegreesPerSecond%360;
            rotor.localRotation=bind*Quaternion.AngleAxis((float)angle,Vector3.up);
        }
        public void Drive(Color? owner,double deltaMatchSeconds)
        {
            if(!double.IsFinite(deltaMatchSeconds)||deltaMatchSeconds<0)throw new ArgumentOutOfRangeException(nameof(deltaMatchSeconds));
            Initialize();Controlled=owner.HasValue;
            if(!painted||previousOwner!=owner)
            {
                var color=owner??Color.gray;
                foreach(var r in surfaces)for(int slot=0;slot<r.sharedMaterials.Length;slot++)
                {
                    finish.Apply(r,slot,color);
                    if(!owner.HasValue&&r.sharedMaterials[slot].name.EndsWith("team-emissive",StringComparison.Ordinal))
                    {
                        var block=new MaterialPropertyBlock();r.GetPropertyBlock(block,slot);
                        block.SetColor("_EmissionColor",Color.black);r.SetPropertyBlock(block,slot);
                    }
                }
                previousOwner=owner;painted=true;
            }
            // Neutralization freezes the current angle; recapture resumes there.
            // Zero match delta also freezes on pause. No global-time catch-up.
            if(Controlled)angle=(angle+deltaMatchSeconds*Profile.rotationDegreesPerSecond)%360;
            rotor.localRotation=bind*Quaternion.AngleAxis((float)angle,Vector3.up);
        }
    }
}
