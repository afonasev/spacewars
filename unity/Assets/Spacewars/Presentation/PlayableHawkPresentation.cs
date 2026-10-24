using System;
using System.Linq;
using Spacewars.Simulation;
using UnityEngine;
namespace Spacewars.Presentation
{
    public sealed partial class PlayableWorld
    {
        private double aircraftAnimationTime,aircraftLastRenderTime;private long aircraftGeneration=-1;
        public void AdvanceAircraftAnimation(PlayableSnapshot view,double now,double speed=1)
        {
            if(aircraftGeneration!=view.Generation){aircraftGeneration=view.Generation;aircraftLastRenderTime=now;aircraftAnimationTime=0;}
            if(!view.Paused&&view.Status==RuntimeStatus.Running)aircraftAnimationTime+=Math.Max(0,now-aircraftLastRenderTime)*speed;
            aircraftLastRenderTime=now;
        }
        public Vector3 AirGroundPoint(NavPoint p)=>new Vector3((float)p.X,(float)(profile.AuthoredMap?.SupportAt(p)?.HeightAt(p)??0),(float)p.Z);
        public Vector3 EntityPoint(PlayableEntitySnapshot e)=>e.Kind==PlayableEntityKind.Hawk?new Vector3((float)e.Position.X,(float)e.Altitude,(float)e.Position.Z):Point(e.Position);
        public void UpdateAircraft(Actor actor,PlayableEntitySnapshot e,PlayableSnapshot view,bool selected=false,PlayableDisplayPose? display=null)
        {
            if(e.Kind!=PlayableEntityKind.Hawk)return;
            var pose=display??new PlayableDisplayPose(e.Position,e.HullHeading,e.TurretHeading,e.Altitude,e.Pitch,e.Roll);var position=pose.Position;
            actor.Health.parent.position+=Vector3.up*((float)pose.Altitude-AirGroundPoint(position).y);
            actor.Root.position=new Vector3((float)position.X,(float)pose.Altitude,(float)position.Z);actor.Root.rotation=Quaternion.identity;
            actor.Hull.localRotation=Quaternion.Euler((float)pose.Pitch,90-(float)pose.Hull*Mathf.Rad2Deg,(float)pose.Roll);
            foreach(var t in actor.Hull.GetComponentsInChildren<Transform>(true))
            {
                if(t.name=="mainRotor")t.localRotation=Quaternion.Euler(0,(float)(aircraftAnimationTime*profile.HawkRotorSpeed),0);
                if(t.name=="tailRotor")t.localRotation=Quaternion.Euler((float)(aircraftAnimationTime*profile.HawkRotorSpeed),0,0);
            }
            // Projection is anchored at the terrain, independent of the body altitude/tilt. It is never painted onto roofs.
            bool terrain=profile.AuthoredMap==null||profile.AuthoredMap.SupportAt(position)!=null;
            bool obstructed=view.Buildings.Any(b=>b.Phase!=ConstructionPhase.Pending&&DistanceAir(position,b.Position)<TerritoryRules.Radius(profile,b.Kind))||view.Entities.Any(u=>u.Id!=e.Id&&u.Kind!=PlayableEntityKind.Hawk&&DistanceAir(position,u.Position)<PlayableUnitRules.Radius(profile,u.Kind));
            actor.Selection.SetActive(terrain&&!obstructed);actor.Selection.transform.position=AirGroundPoint(position)+Vector3.up*(float)profile.SelectionRingLift;actor.Selection.transform.rotation=Quaternion.identity;
            if(terrain&&!obstructed)ProjectAirRing(actor,position);
            // Health outline is independent of the projection circle, including selection over a roof.
            var outline=actor.Health.parent.Find("Health outline").GetComponent<Renderer>();var block=new MaterialPropertyBlock();outline.GetPropertyBlock(block);block.SetColor("_BaseColor",selected?OrbitalTheme.Cyan:Color.black);outline.SetPropertyBlock(block);
        }
        private void ProjectAirRing(Actor actor,NavPoint position)
        {
            // Fixed render tessellation, not gameplay tuning. Sampling the actual support
            // keeps the analytic circle above ramps instead of clipping half a flat quad.
            const int cells=8;const int side=cells+1;
            if(!actor.AirProjection)
            {
                actor.AirProjection=new Mesh{name="Aircraft terrain projection"};actor.AirProjectionVertices=new Vector3[side*side];var uv=new Vector2[side*side];var triangles=new int[cells*cells*6];int k=0;
                for(int z=0;z<side;z++)for(int x=0;x<side;x++)uv[z*side+x]=new Vector2(x/(float)cells,z/(float)cells);
                for(int z=0;z<cells;z++)for(int x=0;x<cells;x++){int a=z*side+x;triangles[k++]=a;triangles[k++]=a+side;triangles[k++]=a+side+1;triangles[k++]=a;triangles[k++]=a+side+1;triangles[k++]=a+1;}
                actor.AirProjection.vertices=actor.AirProjectionVertices;actor.AirProjection.uv=uv;actor.AirProjection.triangles=triangles;actor.Selection.GetComponent<MeshFilter>().sharedMesh=actor.AirProjection;
            }
            float radius=actor.Selection.transform.localScale.x;float center=AirGroundPoint(position).y;
            for(int z=0;z<side;z++)for(int x=0;x<side;x++)
            {
                float localX=2*x/(float)cells-1,localZ=2*z/(float)cells-1;var point=new NavPoint(position.X+localX*radius,position.Z+localZ*radius);
                actor.AirProjectionVertices[z*side+x]=new Vector3(localX,AirGroundPoint(point).y-center,localZ);
            }
            actor.AirProjection.vertices=actor.AirProjectionVertices;actor.AirProjection.RecalculateBounds();
        }
        static double DistanceAir(NavPoint a,NavPoint b)=>Math.Sqrt((a.X-b.X)*(a.X-b.X)+(a.Z-b.Z)*(a.Z-b.Z));
        public void UpdateHangar(Actor actor,PlayableBuildingSnapshot b,long tick)
        {
            if(b.Kind!=PlayableBuildingKind.Hangar)return;
            float progress=(float)Math.Min(1,b.LaunchTime/profile.HawkRoofSeconds);
            float open=b.LaunchPhase==0?0:b.LaunchPhase==1?progress:b.LaunchPhase==6?1-progress:1;
            float platform=b.LaunchPhase==2?(float)Math.Min(1,b.LaunchTime/profile.HawkPlatformSeconds):b.LaunchPhase==3||b.LaunchPhase==4?1:b.LaunchPhase==5?1-(float)Math.Min(1,b.LaunchTime/profile.HawkPlatformSeconds):0;
            float scale=(float)BuildingScale("hangar");
            float rest=(float)HawkHangarGeometry.RestPlatformTop;
            // Buildings sit on the visual apron; authority heights start at terrain.
            float raised=((float)HawkHangarGeometry.PlatformHeight(profile)-ApronArt.apronHeight)/scale;
            float platformTop=Mathf.Lerp(rest,raised,platform);
            foreach(var t in actor.Hull.GetComponentsInChildren<Transform>(true))
            {
                if(t.name.StartsWith("roofPetal",StringComparison.Ordinal)&&int.TryParse(t.name.Substring(9),out int i)){var shutter=t.GetComponent<HangarRetractingPetal>();if(!shutter)shutter=t.gameObject.AddComponent<HangarRetractingPetal>();shutter.SetOpen(open,i);}
                else if(t.name=="irisBacking")t.gameObject.SetActive(open<.01f);
                else if(t.name=="launchPlatform")t.localPosition=Vector3.up*(platformTop-rest);
            }
            var staged=actor.Hull.Find("stagedHawk");bool visible=b.LaunchPhase>=1&&b.LaunchPhase<=3;
            if(!staged&&visible){var prefab=Resources.Load<GameObject>("StyleA/hawk");staged=UnityEngine.Object.Instantiate(prefab,actor.Hull,false).transform;staged.name="stagedHawk";staged.localScale=Vector3.one*((float)profile.HawkModelScale/scale);Layer(staged.gameObject);actor.PresentationPaint=null;}
            if(staged){staged.gameObject.SetActive(visible);staged.rotation=Quaternion.Euler(0,90-(float)b.Heading*Mathf.Rad2Deg,0);staged.localPosition=Vector3.up*(platformTop-(float)(HawkHangarGeometry.HawkSoleHeight*profile.HawkModelScale)/scale);var spin=staged.GetComponent<HawkStagedRotor>();if(!spin)spin=staged.gameObject.AddComponent<HawkStagedRotor>();spin.Render(b.LaunchPhase,Math.Min(1,b.LaunchTime/profile.HawkSpinSeconds),aircraftAnimationTime,profile.HawkRotorSpeed);}
        }
    }
}
