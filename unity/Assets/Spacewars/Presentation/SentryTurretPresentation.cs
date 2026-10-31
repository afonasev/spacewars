using System.Linq;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.Rendering;
namespace Spacewars.Presentation
{
    // One shared asset material; two-point depth-tested beam per turret. No gameplay or target selection here.
    public sealed class SentryTurretPresentation : MonoBehaviour
    {
        SentryTurretRig rig;Transform emitter;LineRenderer laser;int revision=-1;
        public bool LaserVisible=>laser&&laser.enabled;
        public LineRenderer Laser=>laser;
        public void Initialize()
        {
            if(rig)return;
            rig=GetComponent<SentryTurretRig>();rig.Bind();
            emitter=GetComponentsInChildren<Transform>(true).Single(t=>t.name=="sensor-lens");
            var go=new GameObject("Red target laser");go.transform.SetParent(transform,false);
            laser=go.AddComponent<LineRenderer>();laser.positionCount=2;laser.useWorldSpace=true;laser.numCapVertices=0;laser.numCornerVertices=0;
            laser.shadowCastingMode=ShadowCastingMode.Off;laser.receiveShadows=false;
            laser.sharedMaterial=Resources.Load<Material>("SentryTargetLaser");
            if(!laser.sharedMaterial)throw new System.InvalidOperationException("Missing SentryTargetLaser material");
            laser.enabled=false;
        }
        public void Drive(PlayableBuildingSnapshot building,PlayableSnapshot view,PlayableProfile profile)
        {
            Initialize();
            if(revision!=profile.Revision)
            {
                revision=profile.Revision;laser.startWidth=laser.endWidth=(float)profile.TurretLaserWidth;
                var block=new MaterialPropertyBlock();block.SetColor("_BaseColor",new Color(1,.003f,.003f)*(float)profile.TurretLaserIntensity);laser.SetPropertyBlock(block);
            }
            var state=building.Turret;
            if(state!=null)rig.SetWorldAngles(state.Heading,state.Pitch);
            bool ready=building.Phase==ConstructionPhase.Ready&&building.Health>0&&building.PrivateState?.Lifecycle?.Selling!=true&&state?.TargetId>0;
            Vector3 end=default;
            if(ready)
            {
                var target=view.Entities.FirstOrDefault(e=>e.Id==state.TargetId);
                var structure=view.Buildings.FirstOrDefault(b=>b.Id==state.TargetId);
                if(target!=null)
                {
                    double height=profile.AuthoredMap?.SurfaceHeight(target.Position)??0;
                    height=target.Kind==PlayableEntityKind.Hawk?target.Altitude+1.8145018368959427*profile.HawkModelScale/2:height+(profile.AuthoredMap?.DirectFireHeight??1);
                    end=new Vector3((float)target.Position.X,(float)height,(float)target.Position.Z);
                }
                else if(structure!=null)
                {
                    double ground=profile.AuthoredMap?.SurfaceHeight(structure.Position)??0;
                    var aim=BuildingCombatGeometry.Pose(profile,structure.Kind,structure.RefineryUpgraded,structure.Position,ground).Closest(new BallisticPoint(emitter.position.x,emitter.position.y,emitter.position.z));
                    end=new Vector3((float)aim.X,(float)aim.Y,(float)aim.Z);
                }
                else ready=false; // Observer-safe projection only; never resolve a hidden domain target.
            }
            laser.enabled=ready;
            if(ready){laser.SetPosition(0,emitter.position);laser.SetPosition(1,end);}
        }
    }
}
