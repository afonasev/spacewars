using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
using UnityEngine;

namespace Spacewars.Presentation
{
    // Existing Style A entity models are content, not collision or gameplay numbers.
    // Only domain snapshots set roots, hull/turret orientation, HP and construction state.
    public sealed partial class PlayableWorld : System.IDisposable
    {
        public sealed class Actor
        {
            public Transform Root, Hull, Turret, Turbine, Launcher;
            public bool RefineryUpgraded,UnitUpgraded;public Color? PresentationPaint;
            public GameObject Selection;
            public Transform Health;
        }
        private readonly Material material;
        public PlayableFogMask Fog{get;}
        public System.Func<PlayableOwner?,Color> OwnerPaint {get;set;}
        private PlayableProfile profile;
        public void Rebind(PlayableProfile next){profile=next;foreach(var id in new List<int>(actors.Keys))Remove(id);}
        private readonly Transform root;
        private int presentationLayer;
        public void SetPresentationLayer(int layer){presentationLayer=layer;Layer(root.gameObject);}
        private void Layer(GameObject value){foreach(var part in value.GetComponentsInChildren<Transform>(true))part.gameObject.layer=presentationLayer;}
        private readonly Dictionary<int, Actor> actors = new Dictionary<int, Actor>();
        public IReadOnlyDictionary<int, Actor> Actors => actors;
        private static readonly Color Ally = new Color(.19f,.72f,.77f), Enemy = new Color(.93f,.34f,.23f), Dark = new Color(.09f,.13f,.16f);
        public PlayableWorld(Transform parent, PlayableProfile profile)
        {
            this.profile=profile;double extent=profile.ArenaHalfExtent;
            root = new GameObject("Battlefield").transform; root.SetParent(parent,false);
            var shader=Resources.Load<Shader>("TerritoryFog");
            if(!shader)throw new System.InvalidOperationException("Missing territory fog shader");
            material=new Material(shader);Fog=new PlayableFogMask(profile);
            material.SetColor("_FogColor",new Color((float)profile.FogTintR,(float)profile.FogTintG,(float)profile.FogTintB));material.SetTexture("_FogMask",Fog.Texture);material.SetVector("_FogBounds",new Vector4((float)extent,(float)extent,0,0));
            if(profile.AuthoredMap is FoundryMap foundry){CreateFoundry(foundry);return;}
            if(profile.AuthoredMap is ThreeCrossingsMap crossings){CreateThreeCrossings(crossings);return;}
            Part("Basalt arena", root, new Vector3(0,-.24f,0),new Vector3((float)extent*2,.4f,(float)extent*2),new Color(.20f,.25f,.25f));
            for (int i=-(int)extent;i<extent;i+=4)
            {
                Part("Paving joint",root,new Vector3(i,-.025f,0),new Vector3(.025f,.015f,(float)extent*2),new Color(.27f,.32f,.31f));
                Part("Paving joint",root,new Vector3(0,-.025f,i),new Vector3((float)extent*2,.015f,.025f),new Color(.27f,.32f,.31f));
            }
            for(int edge=-1;edge<=1;edge+=2)
            {
                Part("Boundary",root,new Vector3((float)extent*edge,.15f,0),new Vector3(.3f,.3f,(float)extent*2),new Color(.57f,.40f,.22f));
                Part("Boundary",root,new Vector3(0,.15f,(float)extent*edge),new Vector3((float)extent*2,.3f,.3f),new Color(.57f,.40f,.22f));
            }
        }
        public void Obstacle(NavObstacle obstacle)
        {
            float x=(float)((obstacle.MinX+obstacle.MaxX)/2),z=(float)((obstacle.MinZ+obstacle.MaxZ)/2);
            float w=(float)(obstacle.MaxX-obstacle.MinX),d=(float)(obstacle.MaxZ-obstacle.MinZ);
            Part("Fortification",root,new Vector3(x,(float)profile.BallisticWallHeight/2,z),new Vector3(w,(float)profile.BallisticWallHeight,d),new Color(.29f,.33f,.32f));
            Part("Fortification cap",root,new Vector3(x,(float)profile.BallisticWallHeight-.09f,z),new Vector3(w,.18f,d),new Color(.43f,.45f,.39f));
        }
        public void Pad(int id,NavPoint pos)
        {
            var t=new GameObject("Build pad "+id).transform;t.SetParent(root,false);t.localPosition=Point(pos);
            Part("Foundation plate",t,new Vector3(0,.02f,0),new Vector3(4.2f,.06f,4.2f),new Color(.34f,.39f,.38f));
            foreach(float side in new[]{-1f,1f})
            {
                Part("Pad paint",t,new Vector3(side*2,.07f,0),new Vector3(.12f,.03f,4),Ally);
                Part("Pad paint",t,new Vector3(0,.07f,side*2),new Vector3(4,.03f,.12f),Ally);
            }
        }
        public Actor Tank(int id,bool friendly,PlayableEntityKind kind=PlayableEntityKind.Tank)
        {
            var a=Base(id,friendly);
            a.Hull=Model(kind==PlayableEntityKind.Shkval?"shkval":kind==PlayableEntityKind.Explorer?"explorer":"tank",a.Root,friendly);
            // Manifest geometry radius, not a tuning value; model radius/scale are profile-owned.
            a.Hull.localScale=Vector3.one*(float)(kind==PlayableEntityKind.Shkval?profile.ShkvalModelRadius*profile.ShkvalModelScale/2.05:kind==PlayableEntityKind.Explorer?profile.ExplorerModelRadius*profile.ExplorerModelScale/1.0773:profile.TankModelRadius*profile.TankModelScale/1.585);
            a.Turret=a.Hull.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="turretYaw");
            a.Launcher=a.Hull.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name=="launcherPitch");
            return a;
        }
        public void UpdateResearchModel(Actor a,PlayableEntitySnapshot e)
        {
            if(a.UnitUpgraded==e.Upgraded)return;
            Object.Destroy(a.Hull.gameObject);
            string key=e.Kind==PlayableEntityKind.Tank?(e.Upgraded?"tankUpgraded":"tank"):e.Kind==PlayableEntityKind.Explorer?(e.Upgraded?"explorerUpgraded":"explorer"):(e.Upgraded?"shkval-guidance-3":"shkval");
            a.Hull=Model(key,a.Root,e.Owner==PlayableOwner.Player);
            // Approved variant bounds equal their baseline manifest bounds.
            a.Hull.localScale=Vector3.one*(float)(e.Kind==PlayableEntityKind.Shkval?profile.ShkvalModelRadius*profile.ShkvalModelScale/2.05:e.Kind==PlayableEntityKind.Explorer?profile.ExplorerModelRadius*profile.ExplorerModelScale/1.0773:profile.TankModelRadius*profile.TankModelScale/1.585);
            a.Turret=a.Hull.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="turretYaw");
            a.Launcher=a.Hull.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name=="launcherPitch");a.UnitUpgraded=e.Upgraded;a.PresentationPaint=null;
            if(e.Kind==PlayableEntityKind.Tank&&e.Upgraded)
                foreach(var r in a.Hull.GetComponentsInChildren<Renderer>(true))for(int i=0;i<r.sharedMaterials.Length;i++)if(r.sharedMaterials[i].name.EndsWith("chassis-emissive"))
                {var block=new MaterialPropertyBlock();block.SetColor("_EmissionColor",r.sharedMaterials[i].GetColor("_EmissionColor")*(float)profile.TankChassisGlowIntensity);r.SetPropertyBlock(block,i);}
        }
        public Actor Building(int id,string kind,bool friendly,bool upgraded=false)
        {
            var a=Base(id,friendly);string key=BuildingKey(kind,upgraded);a.RefineryUpgraded=upgraded;
            var model=Model(key,a.Root,friendly);a.Hull=model;
            // Authored heights from src/game/manifest.ts preserve web normalization.
            double scale=BuildingScale(key);
            model.localScale=Vector3.one*(float)scale;
            // Authored model front is +Z; presentation facing follows the starting camera, independent of domain exits.
            model.localRotation=BuildingFacing;a.Turbine=model.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name=="turbineYaw");
            return a;
        }
        public void FaceBuilding(Actor actor,Camera camera=null)=>actor.Hull.rotation=Facing(camera);
        private Quaternion Facing(Camera camera)
        {
            var bearing=camera?Vector3.ProjectOnPlane(-camera.transform.forward,Vector3.up).normalized:Vector3.zero;
            return bearing!=Vector3.zero?Quaternion.LookRotation(bearing,Vector3.up):BuildingFacing;
        }
        private Quaternion BuildingFacing
        {
            get
            {
                var towardCamera=new Vector3((float)profile.CameraOffsetX,0,(float)profile.CameraOffsetZ);
                // A directly overhead camera has no horizontal bearing; retain a stable south-facing front.
                return Quaternion.LookRotation(towardCamera==Vector3.zero?Vector3.back:towardCamera,Vector3.up);
            }
        }
        private static string BuildingKey(string kind,bool upgraded)=>kind=="ScientificCenter"?"scientificCenter":kind=="Refinery"&&upgraded?"refineryUpgraded":kind.ToLowerInvariant();
        public void UpdateRefineryModel(Actor actor,PlayableBuildingSnapshot building,long tick)
        {
            if(building.Kind!=PlayableBuildingKind.Refinery)return;
            if(actor.RefineryUpgraded!=building.RefineryUpgraded)
            {
                Object.Destroy(actor.Hull.gameObject);
                string key=BuildingKey(building.Kind.ToString(),building.RefineryUpgraded);
                actor.Hull=Model(key,actor.Root,building.Owner==PlayableOwner.Player);
                actor.Hull.localScale=Vector3.one*(float)BuildingScale(key);actor.Hull.localRotation=BuildingFacing;
                actor.RefineryUpgraded=building.RefineryUpgraded;actor.PresentationPaint=null;
                actor.Turbine=actor.Hull.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name=="turbineYaw");
            }
            // Animation follows simulation time, so pause also freezes the turbine.
            if(actor.Turbine)actor.Turbine.localRotation=Quaternion.Euler(0,0,-(float)(tick/30d*profile.RefineryTurbineSpeed%360));
        }
        private double BuildingScale(string key)=>key=="scientificCenter"?profile.ScienceModelHeightMeters/1.6407*profile.ScienceModelScale:key=="factory"?profile.FactoryModelHeightMeters/2.06*profile.FactoryModelScale:
                (key=="refinery"||key=="refineryUpgraded")?profile.RefineryModelHeightMeters/2.545*profile.RefineryModelScale:
                key=="outpost"?profile.OutpostModelHeightMeters/4.5094*profile.OutpostModelScale:
                key=="mine"?profile.MineModelHeightMeters/1.9945*profile.MineModelScale:
                profile.HeadquartersModelHeightMeters/4.2*profile.HeadquartersModelScale;
        public static void PaintOwner(Actor actor,Color owner)
        {
            if(actor.PresentationPaint.HasValue&&actor.PresentationPaint.Value==owner)return;actor.PresentationPaint=owner;
            foreach(var target in new[]{actor.Health.gameObject,actor.Selection}){var renderer=target.GetComponent<Renderer>();var block=new MaterialPropertyBlock();renderer.GetPropertyBlock(block);block.SetColor("_BaseColor",owner);renderer.SetPropertyBlock(block);}
            foreach(var renderer in actor.Hull.GetComponentsInChildren<Renderer>(true))for(int slot=0;slot<renderer.sharedMaterials.Length;slot++)
            {
                var role=renderer.sharedMaterials[slot].name;if(!role.EndsWith("team-primary")&&!role.EndsWith("team-emissive"))continue;
                var block=new MaterialPropertyBlock();block.SetColor("_BaseColor",owner);if(role.EndsWith("team-emissive"))block.SetColor("_EmissionColor",owner*2.2f);renderer.SetPropertyBlock(block,slot);
            }
        }
        private Transform Model(string key,Transform parent,bool friendly)
        {
            var prefab=Resources.Load<GameObject>("StyleA/"+key);
            if(!prefab)throw new System.InvalidOperationException("Missing approved Style A model: "+key);
            var model=Object.Instantiate(prefab,parent,false);Layer(model);var owner=friendly?Ally:Enemy;
            foreach(var renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                var materials=renderer.sharedMaterials;
                for(int slot=0;slot<materials.Length;slot++)
                {
                    var role=materials[slot].name;
                    if(!role.EndsWith("team-primary")&&!role.EndsWith("team-emissive"))continue;
                    var properties=new MaterialPropertyBlock();properties.SetColor("_BaseColor",owner);
                    // Existing web team-light material convention, not a simulation parameter.
                    if(role.EndsWith("team-emissive"))properties.SetColor("_EmissionColor",owner*2.2f);
                    renderer.SetPropertyBlock(properties,slot);
                }
            }
            return model.transform;
        }
        private Actor Base(int id,bool friendly)
        {
            var a=new Actor();a.Root=new GameObject("Entity "+id).transform;a.Root.SetParent(root,false);
            a.Selection=Part("Selection",a.Root,new Vector3(0,.07f,0),new Vector3(2.4f,.035f,2.4f),friendly?Ally:Enemy,PrimitiveType.Cylinder);a.Selection.SetActive(false);
            var healthRoot=new GameObject("Health").transform;healthRoot.SetParent(root,false);healthRoot.localPosition=new Vector3(0,3.8f,0);
            Part("Health background",healthRoot,Vector3.zero,new Vector3(2,.13f,.17f),Dark);
            a.Health=Part("Health fill",healthRoot,new Vector3(0,.01f,-.01f),new Vector3(2,.15f,.18f),friendly?Ally:Enemy).transform;
            actors[id]=a;return a;
        }
        public static void UpdateHealth(Actor actor,Camera camera,float ratio)
        {
            // Preserve existing visual dimensions. Camera basis and left anchoring are
            // presentation invariants; never inherit model heading/construction scale.
            var healthRoot=actor.Health.parent;
            healthRoot.gameObject.SetActive(NativeUserSettings.AlwaysHealth||ratio<.999f||actor.Selection.activeSelf);
            healthRoot.position=actor.Root.position+Vector3.up*3.8f;
            healthRoot.rotation=camera.transform.rotation;
            float width=2f*Mathf.Clamp01(ratio);
            actor.Health.localScale=new Vector3(width,.15f,.18f);
            actor.Health.localPosition=new Vector3((width-2f)*.5f,.01f,-.01f);
            actor.Health.gameObject.SetActive(width>0);
        }
        public void Remove(int id){if(actors.TryGetValue(id,out var a)){ReleaseWorldObject(a.Health.parent.gameObject);ReleaseWorldObject(a.Root.gameObject);actors.Remove(id);}}
        public void Clear(){Fog.Reset();ClearMemories();ClearPads();foreach(var a in actors.Values){ReleaseWorldObject(a.Health.parent.gameObject);ReleaseWorldObject(a.Root.gameObject);}actors.Clear();}
        public void Dispose(){Clear();Fog.Dispose();surfaceMaterials?.Dispose();if(foundryRoadMask)ReleaseWorldObject(foundryRoadMask);if(foundryLavaHeat)ReleaseWorldObject(foundryLavaHeat);foreach(var mesh in terrainMeshes)ReleaseWorldObject(mesh);terrainMeshes.Clear();ReleaseWorldObject(material);if(root)ReleaseWorldObject(root.gameObject);}
        public GameObject Part(string name,Transform parent,Vector3 position,Vector3 scale,Color color,PrimitiveType shape=PrimitiveType.Cube)
        {
            var g=GameObject.CreatePrimitive(shape);g.name=name;g.layer=presentationLayer;g.transform.SetParent(parent,false);g.transform.localPosition=position;g.transform.localScale=scale;
            ReleaseWorldObject(g.GetComponent<Collider>());g.GetComponent<Renderer>().sharedMaterial=material;
            var properties=new MaterialPropertyBlock();properties.SetColor("_BaseColor",color);g.GetComponent<Renderer>().SetPropertyBlock(properties);return g;
        }
        private static void ReleaseWorldObject(Object value){if(Application.isPlaying)Object.Destroy(value);else Object.DestroyImmediate(value);}
        public Vector3 Point(NavPoint p)=>new Vector3((float)p.X,(float)(profile.AuthoredMap?.SurfaceHeight(p)??0),(float)p.Z);
    }
}
