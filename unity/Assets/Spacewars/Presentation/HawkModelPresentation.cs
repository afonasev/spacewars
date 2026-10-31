using System;
using System.Linq;
using Spacewars.Simulation;
using UnityEngine;
namespace Spacewars.Presentation
{
    // Presentation only. No offsets are applied to the body or authoritative muzzle sockets.
    public sealed class HawkModelPresentation : MonoBehaviour
    {
        Transform baseGroup,upgradeGroup,mainRotor,tailRotor;
        readonly Transform[] nozzles=new Transform[2],covers=new Transform[2];
        readonly Vector3[] coverRest=new Vector3[2];
        readonly double[] shotAt={double.NegativeInfinity,double.NegativeInfinity};
        readonly GameObject[] flashes=new GameObject[2];
        long lastShot=-1,generation=long.MinValue; bool bound; Material flashMaterial;
        public HawkVisualProfile Profile {get;private set;}
        public bool Upgraded {get;private set;}
        public void Bind()
        {
            if(bound)return;bound=true;Profile=HawkVisualProfile.Load();
            var nodes=GetComponentsInChildren<Transform>(true);
            Transform Node(string n)=>nodes.FirstOrDefault(x=>x.name==n);
            baseGroup=Node("baseOnly");upgradeGroup=Node("upgradedOnly");mainRotor=Node("mainRotor");tailRotor=Node("tailRotor");
            if(!baseGroup||!upgradeGroup) return; // Original comparison asset has no new articulation.
            flashMaterial=new Material(Shader.Find("Universal Render Pipeline/Unlit"));flashMaterial.SetColor("_BaseColor",new Color(1,.63f,.16f));
            for(int i=0;i<2;i++)
            {
                string side=i==0?"Left":"Right";nozzles[i]=Node("nozzle"+side);covers[i]=Node("reload"+side);coverRest[i]=covers[i].localPosition;
                var g=GameObject.CreatePrimitive(PrimitiveType.Sphere);g.name="Hawk launch pulse "+side;g.transform.SetParent(Node("muzzle"+side),false);g.layer=gameObject.layer;
                var collider=g.GetComponent<Collider>();collider.enabled=false;if(Application.isPlaying)Destroy(collider);else DestroyImmediate(collider);
                g.GetComponent<Renderer>().sharedMaterial=flashMaterial;g.SetActive(false);flashes[i]=g;
            }
        }
        public void SetUpgrade(bool value){Bind();Upgraded=value;if(baseGroup)baseGroup.gameObject.SetActive(!value);if(upgradeGroup)upgradeGroup.gameObject.SetActive(value);}
        public void Observe(PlayableEntitySnapshot entity,PlayableSnapshot snapshot)
        {
            Bind();if(generation!=snapshot.Generation){generation=snapshot.Generation;lastShot=-1;shotAt[0]=shotAt[1]=double.NegativeInfinity;}
            SetUpgrade(entity.Upgraded);
            foreach(var shot in snapshot.Sounds)
                if(shot.Id>lastShot&&shot.Kind==PlayableSoundKind.Shot&&shot.Weapon==PlayableEntityKind.Hawk&&shot.UnitId==entity.Id&&shot.ShotSide>=0&&shot.ShotSide<2)
                {shotAt[shot.ShotSide]=shot.Tick/30d;lastShot=shot.Id;}
        }
        public void Trigger(int side,double time){Bind();if(side<0||side>1)throw new ArgumentOutOfRangeException(nameof(side));shotAt[side]=time;}
        public void Render(double rotorTime,double effectTime,float pitch,float roll,float rotorSpeed)
        {
            Bind();if(mainRotor)mainRotor.localRotation=Quaternion.Euler(0,(float)(rotorTime*rotorSpeed),0);if(tailRotor)tailRotor.localRotation=Quaternion.Euler((float)(rotorTime*rotorSpeed),0,0);
            for(int i=0;i<2;i++)
            {
                if(nozzles[i])nozzles[i].localRotation=Quaternion.Euler(Mathf.Clamp(-pitch*Profile.nozzleResponse,-Profile.nozzleAngle,Profile.nozzleAngle),Mathf.Clamp(roll*Profile.nozzleResponse,-Profile.nozzleAngle,Profile.nozzleAngle),0);
                float age=(float)(effectTime-shotAt[i]);float cycle=age>=0&&age<Profile.mechanismSeconds?Mathf.Sin(age/Profile.mechanismSeconds*Mathf.PI):0;
                if(covers[i])covers[i].localPosition=coverRest[i]+Vector3.back*(cycle*Profile.reloadTravel);
                if(flashes[i]){bool active=age>=0&&age<Profile.launchSeconds;flashes[i].SetActive(active);if(active)flashes[i].transform.localScale=new Vector3(1,1,2)*Profile.launchSize*(1-age/Profile.launchSeconds);}
            }
        }
        void OnDestroy(){if(flashMaterial){if(Application.isPlaying)Destroy(flashMaterial);else DestroyImmediate(flashMaterial);}}
    }
}
