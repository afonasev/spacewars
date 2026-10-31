using System.Linq;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.Rendering;
namespace Spacewars.Presentation
{
    public sealed class RepairTowerPresentation:MonoBehaviour
    {
        SentryTurretRig rig;Transform emitter;LineRenderer beam;
        public LineRenderer Beam=>beam;
        public void Initialize()
        {
            if(rig)return;
            rig=GetComponent<SentryTurretRig>();rig.Bind();
            emitter=GetComponentsInChildren<Transform>(true).Single(t=>t.name=="repairEmitter");
            var node=new GameObject("Green repair beam");node.transform.SetParent(transform,false);
            beam=node.AddComponent<LineRenderer>();beam.positionCount=2;beam.useWorldSpace=true;
            beam.shadowCastingMode=ShadowCastingMode.Off;beam.receiveShadows=false;
            beam.sharedMaterial=Resources.Load<Material>("SentryTargetLaser");beam.enabled=false;
        }
        public void Drive(PlayableBuildingSnapshot building,PlayableSnapshot view,PlayableProfile profile)
        {
            Initialize();var state=building.Healing;
            if(state!=null)rig.SetWorldAngles(state.Heading,state.Pitch);
            var target=state?.TargetId>0?view.Entities.FirstOrDefault(e=>e.Id==state.TargetId):null;
            beam.enabled=building.Phase==ConstructionPhase.Ready&&building.Health>0&&building.PrivateState?.Lifecycle?.Selling!=true&&target!=null;
            beam.startWidth=beam.endWidth=(float)profile.TurretLaserWidth;
            var block=new MaterialPropertyBlock();block.SetColor("_BaseColor",new Color(.015f,1,.06f)*(float)profile.TurretLaserIntensity);beam.SetPropertyBlock(block);
            if(!beam.enabled)return;
            double height=target.Kind==PlayableEntityKind.Hawk?target.Altitude+1.8145018368959427*profile.HawkModelScale/2:(profile.AuthoredMap?.SurfaceHeight(target.Position)??0)+(profile.AuthoredMap?.DirectFireHeight??1);
            beam.SetPosition(0,emitter.position);beam.SetPosition(1,new Vector3((float)target.Position.X,(float)height,(float)target.Position.Z));
        }
    }
}
