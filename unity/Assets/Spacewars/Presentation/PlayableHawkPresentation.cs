using System;
using System.Linq;
using Spacewars.Simulation;
using UnityEngine;
namespace Spacewars.Presentation
{
    public sealed partial class PlayableWorld
    {
        // The Skat mesh was authored nose-first along local -Z.
        private static readonly Quaternion SkatModelHalfTurn=Quaternion.AngleAxis(180,Vector3.up);
        private static Quaternion OrientUnitModel(PlayableEntityKind kind,Quaternion pose)=>
            kind==PlayableEntityKind.Skat?pose*SkatModelHalfTurn:pose;

        public Vector3 AirGroundPoint(NavPoint p)=>new Vector3((float)p.X,(float)(profile.AuthoredMap?.SupportAt(p)?.HeightAt(p)??0),(float)p.Z);
        public Vector3 EntityPoint(PlayableEntitySnapshot e)=>PlayableUnitRules.Aircraft(e.Kind)?new Vector3((float)e.Position.X,(float)e.Altitude,(float)e.Position.Z):Point(e.Position);
        internal bool HawkScreenBounds(Camera camera,PlayableEntitySnapshot entity,out Rect bounds)
        {
            var position=EntityPoint(entity);
            var orientation=Quaternion.Euler((float)entity.Pitch,90-(float)entity.HullHeading*Mathf.Rad2Deg,(float)entity.Roll);
            float radius=(float)profile.HawkSelectionRadius,height=(float)PlayableUnitRules.AircraftBodyHeight(profile,entity.Kind);
            var model=new Bounds(Vector3.up*height/2,new Vector3(2*radius,height,2*radius));
            var toWorld=Matrix4x4.TRS(position,orientation,Vector3.one);
            if(entity.Kind==PlayableEntityKind.Skat&&actors.TryGetValue(entity.Id,out var drone)&&drone.DetailLod)
            {
                model=drone.DetailLod.ReferenceBounds;
                toWorld=drone.Hull.localToWorldMatrix;
            }
            if(actors.TryGetValue(entity.Id,out var actor)&&actor.Hawk&&actor.Hawk.PickBounds.size!=Vector3.zero)
            {
                model=actor.Hawk.PickBounds;
                toWorld=actor.Hull.localToWorldMatrix;
            }
            var min=new Vector2(float.PositiveInfinity,float.PositiveInfinity);
            var max=new Vector2(float.NegativeInfinity,float.NegativeInfinity);
            for(int x=-1;x<=1;x+=2)
            for(int y=-1;y<=1;y+=2)
            for(int z=-1;z<=1;z+=2)
            {
                var corner=toWorld.MultiplyPoint3x4(model.center+Vector3.Scale(model.extents,new Vector3(x,y,z)));
                var screen=camera.WorldToScreenPoint(corner);
                if(screen.z<camera.nearClipPlane||screen.z>camera.farClipPlane)continue;
                min=Vector2.Min(min,screen);max=Vector2.Max(max,screen);
            }
            bounds=Rect.MinMaxRect(min.x,min.y,max.x,max.y);
            return min.x<=max.x&&camera.pixelRect.Overlaps(bounds);
        }

        internal int PickHawk(Camera camera,PlayableSnapshot view,Vector2 point,bool onlyOwn)
        {
            int hit=0;float closest=float.PositiveInfinity;
            foreach(var entity in view.Entities)
            {
                if(!PlayableUnitRules.Aircraft(entity.Kind)||entity.Health<=0||(onlyOwn&&entity.Owner!=view.Owner)||
                    !HawkScreenBounds(camera,entity,out var bounds)||!bounds.Contains(point))continue;
                float distance=(point-bounds.center).sqrMagnitude;
                if(distance<closest){closest=distance;hit=entity.Id;}
            }
            return hit;
        }
        internal bool HawkInCircle(Camera camera,PlayableEntitySnapshot entity,Vector2 center,float radius)
        {
            if(!HawkScreenBounds(camera,entity,out var bounds))return false;
            var nearest=new Vector2(Mathf.Clamp(center.x,bounds.xMin,bounds.xMax),Mathf.Clamp(center.y,bounds.yMin,bounds.yMax));
            return (center-nearest).sqrMagnitude<=radius*radius;
        }
        public void UpdateAircraft(Actor actor,PlayableEntitySnapshot e,PlayableSnapshot view,bool selected=false,PlayableDisplayPose? display=null)
        {
            if(!PlayableUnitRules.Aircraft(e.Kind))return;
            var pose=display??new PlayableDisplayPose(e.Position,e.HullHeading,e.TurretHeading,e.Altitude,e.Pitch,e.Roll);var position=pose.Position;
            actor.Health.parent.position+=Vector3.up*((float)pose.Altitude-AirGroundPoint(position).y);
            actor.Root.position=new Vector3((float)position.X,(float)pose.Altitude,(float)position.Z);actor.Root.rotation=Quaternion.identity;
            actor.Hull.localRotation=OrientUnitModel(e.Kind,
                Quaternion.Euler((float)pose.Pitch,90-(float)pose.Hull*Mathf.Rad2Deg,(float)pose.Roll));
            if(e.Kind==PlayableEntityKind.Hawk)
            {
                if(!actor.Hawk)actor.Hawk=actor.Hull.gameObject.AddComponent<HawkModelPresentation>();
                actor.Hawk.Observe(e,view);actor.UnitUpgraded=e.Upgraded;
                actor.Hawk.Render(AnimationSeconds(view),view.Tick/30d,(float)pose.Pitch,(float)pose.Roll,(float)profile.HawkRotorSpeed);
            }
            else UpdateDroneBeam(actor, e, view);
            // Projection is anchored at the terrain, independent of the body altitude/tilt. It is never painted onto roofs.
            bool terrain=profile.AuthoredMap==null||profile.AuthoredMap.SupportAt(position)!=null;
            bool obstructed=view.Buildings.Any(b=>b.Phase!=ConstructionPhase.Pending&&DistanceAir(position,b.Position)<TerritoryRules.Radius(profile,b.Kind))||view.Entities.Any(u=>u.Id!=e.Id&&!PlayableUnitRules.Aircraft(u.Kind)&&DistanceAir(position,u.Position)<PlayableUnitRules.Radius(profile,u.Kind));
            bool showProjection=selected&&terrain&&!obstructed;
            actor.Selection.SetActive(showProjection);actor.Selection.transform.position=AirGroundPoint(position)+Vector3.up*(float)profile.SelectionRingLift;actor.Selection.transform.rotation=Quaternion.identity;
            if(showProjection)ProjectAirRing(actor,position);
            // Health outline is independent of the projection circle, including selection over a roof.
            var outline=actor.Health.parent.Find("Health outline").GetComponent<Renderer>();var block=new MaterialPropertyBlock();outline.GetPropertyBlock(block);block.SetColor("_BaseColor",selected?OrbitalTheme.Cyan:Color.black);outline.SetPropertyBlock(block);
        }
        private void UpdateDroneBeam(Actor actor, PlayableEntitySnapshot drone, PlayableSnapshot view)
        {
            var node = actor.Hull.Find("Skat beam");
            if (!node)
            {
                node = new GameObject("Skat beam").transform;
                node.SetParent(actor.Hull, false);
                node.gameObject.layer = actor.Hull.gameObject.layer;
                var created = node.gameObject.AddComponent<LineRenderer>();
                created.positionCount = 2;
                created.useWorldSpace = true;
                created.sharedMaterial = Resources.Load<Material>("SentryTargetLaser");
                created.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                created.receiveShadows = false;
                var tint = new MaterialPropertyBlock();
                tint.SetColor("_BaseColor", new Color(.015f, 1, .06f) * (float)profile.TurretLaserIntensity);
                created.SetPropertyBlock(tint);
            }

            var beam = node.GetComponent<LineRenderer>();
            var unit = view.Entities.FirstOrDefault(target => target.Id == drone.TargetId);
            var building = view.Buildings.FirstOrDefault(target => target.Id == drone.TargetId);
            beam.enabled = unit != null || building != null;
            if (!beam.enabled) return;
            beam.startWidth = beam.endWidth = (float)profile.TurretLaserWidth;
            float targetHeight = unit != null && PlayableUnitRules.Aircraft(unit.Kind)
                ? (float)PlayableUnitRules.AircraftBodyHeight(profile, unit.Kind) / 2 : .45f;
            var endpoint = unit != null ? EntityPoint(unit) + Vector3.up * targetHeight :
                Point(building.Position) + Vector3.up * (float)(PlayableUnitRules.AirBuildingHeight(profile, building.Kind) / 2);
            beam.SetPosition(0, actor.RepairEmitter.position);
            beam.SetPosition(1, endpoint);
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
            var kind = b.LaunchKind ?? PlayableEntityKind.Hawk;
            var parkedDrone = actor.Hull.Find("stagedSkat");
            if (kind == PlayableEntityKind.Skat && b.LaunchPhase >= 1 && b.LaunchPhase <= 3)
            {
                var old = actor.Hull.Find("stagedHawk");
                if (old) old.gameObject.SetActive(false);
                if (!parkedDrone)
                {
                    parkedDrone = Model("skat", actor.Hull, true);
                    parkedDrone.name = "stagedSkat";
                    parkedDrone.localScale = Vector3.one * (float)PlayableUnitRules.SkatModelScale / scale;
                    actor.PresentationPaint = null;
                }
                parkedDrone.gameObject.SetActive(true);
                parkedDrone.rotation = OrientUnitModel(kind,Quaternion.Euler(0,90-(float)b.Heading*Mathf.Rad2Deg,0));
                parkedDrone.localPosition = Vector3.up * platformTop;
                return;
            }
            if (parkedDrone) parkedDrone.gameObject.SetActive(false);
            var staged=actor.Hull.Find("stagedHawk");bool visible=b.LaunchPhase>=1&&b.LaunchPhase<=3;
            if(!staged&&visible){var prefab=Resources.Load<GameObject>("StyleA/hawk");staged=UnityEngine.Object.Instantiate(prefab,actor.Hull,false).transform;staged.name="stagedHawk";staged.localScale=Vector3.one*((float)profile.HawkModelScale/scale);Layer(staged.gameObject);actor.PresentationPaint=null;}
            if(staged){var visual=staged.GetComponent<HawkModelPresentation>();if(!visual)visual=staged.gameObject.AddComponent<HawkModelPresentation>();visual.SetUpgrade(b.LaunchUpgraded);staged.gameObject.SetActive(visible);staged.rotation=Quaternion.Euler(0,90-(float)b.Heading*Mathf.Rad2Deg,0);staged.localPosition=Vector3.up*(platformTop-(float)(HawkHangarGeometry.HawkSoleHeight*profile.HawkModelScale)/scale);var spin=staged.GetComponent<HawkStagedRotor>();if(!spin)spin=staged.gameObject.AddComponent<HawkStagedRotor>();spin.Render(b.LaunchPhase,Math.Min(1,b.LaunchTime/profile.HawkSpinSeconds),matchAnimationTick==tick?matchAnimationSeconds:tick/30d,profile.HawkRotorSpeed);}
        }
    }
}
