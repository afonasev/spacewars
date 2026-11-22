using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.Rendering;
namespace Spacewars.Presentation
{
    public sealed class RepairTowerPresentation:MonoBehaviour
    {
        // Keep the airborne beam rasterizable at match and split-screen zoom levels.
        const float MinimumAirBeamPixels = 2;

        SentryTurretRig rig;Transform emitter;LineRenderer beam;
        public LineRenderer Beam=>beam;
        public void Initialize()
        {
            if(rig)return;
            rig=GetComponent<SentryTurretRig>();rig.Bind();
            emitter=GetComponentsInChildren<Transform>(true).Single(t=>t.name=="repairEmitter");
            var node=new GameObject("Green repair beam"){layer=gameObject.layer};node.transform.SetParent(transform,false);
            beam=node.AddComponent<LineRenderer>();beam.positionCount=2;beam.useWorldSpace=true;
            beam.shadowCastingMode=ShadowCastingMode.Off;beam.receiveShadows=false;
            beam.sharedMaterial=Resources.Load<Material>("SentryTargetLaser");beam.enabled=false;
        }
        public void Drive(PlayableBuildingSnapshot building,PlayableSnapshot view,PlayableProfile profile,
            Camera camera=null,IReadOnlyDictionary<int,PlayableWorld.Actor> actors=null)
        {
            Initialize();var state=building.Healing;
            if(state!=null)rig.SetWorldAngles(state.Heading,state.Pitch);
            var target=state?.TargetId>0?view.Entities.FirstOrDefault(e=>e.Id==state.TargetId):null;
            var structure=state?.TargetId>0?view.Buildings.FirstOrDefault(candidate=>candidate.Id==state.TargetId):null;
            beam.enabled=building.Phase==ConstructionPhase.Ready&&building.Health>0&&building.PrivateState?.Lifecycle?.Selling!=true&&(target!=null||structure!=null);
            if(!beam.enabled)return;

            float width=(float)profile.TurretLaserWidth;
            Vector3 end;
            if(structure!=null)
            {
                double ground=profile.AuthoredMap?.SurfaceHeight(structure.Position)??0;
                end=new Vector3((float)structure.Position.X,
                    (float)(ground+PlayableUnitRules.AirBuildingHeight(profile,structure.Kind)/2),(float)structure.Position.Z);
            }
            else if(PlayableUnitRules.Aircraft(target.Kind))
            {
                var position=actors!=null&&actors.TryGetValue(target.Id,out var actor)
                    ?actor.Root.position
                    :new Vector3((float)target.Position.X,(float)target.Altitude,(float)target.Position.Z);
                end=position+Vector3.up*(float)(PlayableUnitRules.AircraftBodyHeight(profile,target.Kind)/2);
                if(camera&&camera.orthographic&&camera.scaledPixelHeight>0)
                    width=Mathf.Max(width,MinimumAirBeamPixels*2*camera.orthographicSize/camera.scaledPixelHeight);
            }
            else
            {
                double height=(profile.AuthoredMap?.SurfaceHeight(target.Position)??0)+(profile.AuthoredMap?.DirectFireHeight??1);
                end=new Vector3((float)target.Position.X,(float)height,(float)target.Position.Z);
            }
            beam.startWidth=beam.endWidth=width;
            var block=new MaterialPropertyBlock();block.SetColor("_BaseColor",new Color(.015f,1,.06f)*(float)profile.TurretLaserIntensity);beam.SetPropertyBlock(block);
            beam.SetPosition(0,emitter.position);beam.SetPosition(1,end);
        }
    }
}
