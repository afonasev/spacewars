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
            public CerberusPresentation Cerberus;
            public IglaPresentation Igla;
            public Transform Root, Hull, Turret, Turbine, Launcher; public ShkvalMechanismPresentation ShkvalMechanism;
            public Transform[] LoadedRockets;
            public TankMechanismPresentation TankMechanism;
            public TankDistantLod TankLod;
            public ModelDetailLod DetailLod;
            public Mesh AirProjection;public Vector3[] AirProjectionVertices;
            public Transform RepairEmitter;
            public ActorMaterialProfile MaterialFinish;
            public FactoryModelPresentation Factory;
            public double FactoryHeading=TerritoryRules.BuildingHeading;
            public ArmoredMinePresentation Mine;
            public HawkModelPresentation Hawk;
            public GaussTowerPresentation Gauss;public RepairTowerPresentation Healing;public SentryTurretPresentation Sentry;public Transform FactoryStaged;public ModelDetailLod FactoryStagedLod;public TankDistantLod FactoryStagedTankLod;public TankMechanismPresentation FactoryStagedMechanism;public PlayableEntityKind FactoryStagedKind;public bool FactoryStagedUpgraded;
            public bool RefineryUpgraded,UnitUpgraded,ExplorerRocketLauncher; internal MeshFilter[] ExplorerRocketMeshes; internal int ExplorerRocketDetailLevel; internal PlayableOwner ExplorerRocketOwner;public Color? PresentationPaint;
            public GameObject Selection;
            public bool UnitSelection;
            public Transform Health;
            internal PlayableProfile HealthProfile;
            internal Transform HealthOutline,HealthBackground;
            internal Transform[] HealthDividers;
            internal HealthDividerBatch HealthDividerBatch;
            internal float HealthPixelsPerUnit;
            internal int HealthWidthPixels,HealthHeightPixels;
            internal Transform HealthModel;
            internal Renderer[] HealthModelRenderers;
            internal PlayableDisplayPose? AppliedTankPose;
            internal Dictionary<Renderer, Material[]> ReinforcementOriginalMaterials;
            internal MeshRenderer ReinforcementCylinder,StasisCylinder;
        }
        private static readonly int MatchAnimationTime=Shader.PropertyToID("_MatchAnimationTime");
        private AntennaCityWindowLights windowLights;
        private double matchAnimationSeconds;
        private long matchAnimationGeneration=-1,matchAnimationTick=-1;

        public void UpdateMatchAnimation(PlayableSnapshot view,double? snapshotAge=null,double rate=1,bool overview=false)
        {
            const double ticksPerSecond=30;
            double tick=view.Tick;
            if(snapshotAge.HasValue&&tick>0&&!view.Paused&&view.Status==RuntimeStatus.Running&&view.Outcome==PlayableMatchOutcome.Playing)
            {
                // Interpolate the last published step; never extrapolate while the host is stalled.
                tick=tick-1+System.Math.Clamp(snapshotAge.Value*rate*ticksPerSecond,0,1);
            }
            double seconds=tick/ticksPerSecond;
            if(matchAnimationGeneration==view.Generation&&matchAnimationTick==view.Tick)
                seconds=System.Math.Max(seconds,matchAnimationSeconds);

            matchAnimationGeneration=view.Generation;
            matchAnimationTick=view.Tick;
            matchAnimationSeconds=seconds;
            material.SetFloat(MatchAnimationTime,(float)seconds);
            if(cityMaterial&&profile.AuthoredMap is AntennaCityMap city){
                windowLights??=new AntennaCityWindowLights(cityMaterial,city.HalfExtent,AntennaCityWindowProfile.Load());
                windowLights.Update(view,overview,seconds);
            }
            if (terrainCacheYardMaterial) terrainCacheYardMaterial.SetFloat(MatchAnimationTime, (float)seconds);
            if(mossTerrainMaterial)mossTerrainMaterial.SetFloat(MatchAnimationTime,(float)seconds);
        }

        private double AnimationSeconds(PlayableSnapshot view)=>
            matchAnimationGeneration==view.Generation&&matchAnimationTick==view.Tick?matchAnimationSeconds:view.Tick/30d;

        private readonly Material material;
        private GameObject campaignGateVisual;
        public void SetCampaignGate(bool closed){if(campaignGateVisual!=null)campaignGateVisual.SetActive(closed);}
        private Material selectionMaterial;
        private Mesh selectionQuad;
        public PlayableFogMask Fog{get;}
        public System.Func<PlayableOwner?,Color> OwnerPaint {get;set;}
        private PlayableProfile profile;
        public ActorMaterialProfile MaterialFinish {get;set;}=ActorMaterialProfile.Load();
        public Transform GraphicsRoot=>root;
        internal Material TerrainCacheMaterial => material;
        internal int TerrainCacheRevision { get; private set; }
        private Material terrainCacheYardMaterial;
        private Renderer[] terrainCacheYardRenderers;

        internal Material PrepareTerrainCacheYardMaterial(out Renderer[] yardRenderers)
        {
            if (!terrainCacheYardMaterial)
            {
                terrainCacheYardMaterial = new Material(material);
                terrainCacheYardMaterial.EnableKeyword("SPACEWARS_QA_STOCK_YARD");
                var properties = new MaterialPropertyBlock();
                var renderers = new List<Renderer>();
                foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (renderer.sharedMaterial != material) continue;
                    renderer.GetPropertyBlock(properties);
                    if (properties.GetFloat("_YardObject") > .5f)
                        renderers.Add(renderer);
                }
                terrainCacheYardRenderers = renderers.ToArray();
            }
            yardRenderers = terrainCacheYardRenderers;
            return terrainCacheYardMaterial;
        }

        private void DisposeTerrainCacheMaterial()
        {
            if (terrainCacheYardMaterial) ReleaseWorldObject(terrainCacheYardMaterial);
        }
        public bool HybridTanks {get;set;}=true;
        public void ApplyGraphics(ActorMaterialProfile finish,IndustrialLightingProfile lighting)
        {
            finish.Validate();lighting.Validate();MaterialFinish=finish;wreckKeyColor=Color.HSVToRGB(lighting.sunHue,lighting.sunSaturation,1);
            TerrainCacheRevision++;
            var industrial=new Vector4(lighting.steelHighlight,lighting.steelExponent,lighting.concreteHighlight,lighting.concreteExponent);
            foreach(var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                // Terrain stores color, role and pad bounds on the renderer. A new
                // per-material block would hide that entire block, not merge with it.
                var block=new MaterialPropertyBlock();renderer.GetPropertyBlock(block);
                block.SetVector("_IndustrialFinish",industrial);renderer.SetPropertyBlock(block);
                for(int slot=0;slot<renderer.sharedMaterials.Length;slot++)
                {
                    renderer.GetPropertyBlock(block,slot);
                    if(block.isEmpty)continue;
                    block.SetVector("_IndustrialFinish",industrial);renderer.SetPropertyBlock(block,slot);
                }
            }
            foreach(var actor in actors.Values)
            {
                actor.MaterialFinish=finish;
                foreach(var renderer in ActorModelRenderers(actor))for(int slot=0;slot<renderer.sharedMaterials.Length;slot++)
                {
                    var source=renderer.sharedMaterials[slot];var block=new MaterialPropertyBlock();renderer.GetPropertyBlock(block,slot);
                    var owner=actor.PresentationPaint;if(!owner.HasValue&&(source.name.EndsWith("team-primary")||source.name.EndsWith("team-emissive")))owner=block.GetColor("_BaseColor");
                    finish.Apply(renderer,slot,owner,preserveOwnerPaint:true);
                    if(actor.UnitUpgraded&&source.name.EndsWith("chassis-emissive")){renderer.GetPropertyBlock(block,slot);{ActorMaterialProfile.RecordColorInput(renderer,slot,"_EmissionColor",source.GetColor("_EmissionColor")*(float)profile.TankChassisGlowIntensity*finish.emissionGain);block.SetColor("_EmissionColor",source.GetColor("_EmissionColor")*(float)profile.TankChassisGlowIntensity*finish.emissionGain);}renderer.SetPropertyBlock(block,slot);}
                }
                if(actor.Factory)actor.Factory.RefreshLights();
            }
            foreach(var wreck in unitWrecks.Values)if(wreck.State.HasGround&&wreck.Model)foreach(var renderer in wreck.Model.GetComponentsInChildren<MeshRenderer>())finish.ApplyWreck(renderer,WreckKeyColor);
        }
        public void Rebind(PlayableProfile next){windowLights?.Reset();ClearReinforcementPreviewModels();ClearMines();ClearAbilityEffects();ClearTowerRange();ClearGroundTrails();ClearUnitWrecks();ClearBuildingDestructions();ClearPads();ClearRallyFlags();profile=next;ApplyDockRelayCaptureRadius();PrepareRelayGround();wreckKeyColor=null;WreckProfile.lifetimeSeconds=(float)next.UnitWreckLifetimeSeconds;foreach(var id in new List<int>(actors.Keys))Remove(id);ReleaseWorldObject(selectionMaterial);selectionMaterial=null;ReleaseWorldObject(selectionQuad);selectionQuad=null;}
        private readonly Transform root;
        private int presentationLayer;
        public void SetPresentationLayer(int layer){presentationLayer=layer;Layer(root.gameObject);}
        public void Layer(GameObject value){foreach(var part in value.GetComponentsInChildren<Transform>(true))part.gameObject.layer=presentationLayer;}
        private readonly Dictionary<int, Actor> actors = new Dictionary<int, Actor>();
        public IReadOnlyDictionary<int, Actor> Actors => actors;
        private static readonly Color Ally = new Color(.19f,.72f,.77f), Enemy = new Color(.93f,.34f,.23f), Dark = new Color(.09f,.13f,.16f);
        public PlayableWorld(Transform parent, PlayableProfile profile, MossHighlandArtProfile mossArt=null,bool staged=false,NullDockArtProfile dockArt=null)
        {
            this.profile=profile;WreckProfile.lifetimeSeconds=(float)profile.UnitWreckLifetimeSeconds;double extent=profile.ArenaHalfExtent;
            root = new GameObject("Battlefield").transform; root.SetParent(parent,false);
            var shader=Resources.Load<Shader>("TerritoryFog");
            if(!shader)throw new System.InvalidOperationException("Missing territory fog shader");
            material=new Material(shader);IndustrialLightingProfile.Load(profile.AuthoredMap?.Id).ApplyMaterial(material);Fog=new PlayableFogMask(profile);
            material.SetColor("_FogColor",new Color((float)profile.FogTintR,(float)profile.FogTintG,(float)profile.FogTintB));material.SetTexture("_FogMask",Fog.Texture);material.SetVector("_FogBounds",new Vector4((float)extent,(float)extent,0,0));
            Preparation=PrepareTerrainAndDetail(mossArt,dockArt);
            if(!staged){try{LoadingPreparation.Drain(Preparation);}catch{Dispose();throw;}}
        }
        public System.Collections.IEnumerator Preparation{get;}
        private System.Collections.IEnumerator PrepareTerrain(MossHighlandArtProfile mossArt,NullDockArtProfile dockArt)
        {
            double extent=profile.ArenaHalfExtent;
            if(profile.AuthoredMap is DeafMirrorMap mirror){CreateDeafMirror(mirror);yield break;}
            if(profile.AuthoredMap is AntennaCityMap city){yield return CreateAntennaCity(city);yield break;}
            if(profile.AuthoredMap is NullDockMap dock){yield return CreateNullDock(dock,dockArt);yield break;}
            if(profile.AuthoredMap is CrimsonGroveMap grove){yield return CreateCrimsonGrove(grove);yield break;}
            if(profile.AuthoredMap is WhiteMeridianMap meridian){yield return CreateWhiteMeridian(meridian);yield break;}
            if(profile.AuthoredMap is MossHighlandMap moss){yield return CreateMossHighland(moss,mossArt);yield break;}
            if(profile.AuthoredMap is BasaltCrownMap basalt){yield return CreateBasaltCrown(basalt);yield break;}
            if(profile.AuthoredMap is FoundryMap foundry){
                yield return CreateFoundry(foundry);
                // Final privately owned terrain/detail meshes only; Foundry generators may
                // deform/rebuild their templates and remain independently owned.
                foreach(var filter in root.GetComponentsInChildren<MeshFilter>())
                    if(terrainMeshes.Contains(filter.sharedMesh))
                        FinalProceduralMesh.IndexOwned(filter.sharedMesh);
                yield break;
            }
            if(profile.AuthoredMap is ThreeCrossingsMap crossings)
            {yield return CreateThreeCrossings(crossings);yield break;}
            if(profile.AuthoredMap is CampaignOpeningMap campaign)
            {
                var earth=Resources.Load<Texture2D>("Environment/NaturalFrontier/Earth");material.SetTexture("_EarthTex",earth);
                var ground=Part("Outer worlds desert",root,new Vector3(0,-.2f,0),new Vector3((float)extent*2,.4f,(float)extent*2),new Color(.64f,.49f,.31f));
                foreach(var rock in campaign.Solids)
                {
                    var mesh=TerrainMesh.Prism(rock,0,profile.BallisticWallHeight);FinalProceduralMesh.IndexOwned(mesh);terrainMeshes.Add(mesh);
                    var obj=new GameObject("Canyon rock");obj.transform.SetParent(root,false);obj.AddComponent<MeshFilter>().sharedMesh=mesh;
                    obj.AddComponent<MeshRenderer>().sharedMaterial=material;var properties=new MaterialPropertyBlock();properties.SetColor("_BaseColor",new Color(.38f,.29f,.20f));obj.GetComponent<Renderer>().SetPropertyBlock(properties);
                }
                var gate=CampaignReconOpening.Gate;
                campaignGateVisual=Part("Canyon gate",root,new Vector3((float)((gate.MinX+gate.MaxX)/2),(float)profile.BallisticWallHeight/2,(float)((gate.MinZ+gate.MaxZ)/2)),new Vector3((float)(gate.MaxX-gate.MinX),(float)profile.BallisticWallHeight,(float)(gate.MaxZ-gate.MinZ)),new Color(.46f,.36f,.23f));
                yield break;
            }
            if(profile.AuthoredMap is AiTestMap)
            {
                var texture=Resources.Load<Texture2D>("Environment/NaturalFrontier/Earth");
                if(!texture)throw new System.InvalidOperationException("Missing AI test ground texture");
                material.SetTexture("_EarthTex",texture);
                var ground=Part("AI test open ground",root,new Vector3(0,-.2f,0),new Vector3((float)extent*2,.4f,(float)extent*2),Color.white);
                var properties=new MaterialPropertyBlock();properties.SetColor("_BaseColor",Color.white);properties.SetFloat("_SurfaceRole",-1);ground.GetComponent<Renderer>().SetPropertyBlock(properties);
                yield break;
            }
            Part("Basalt arena", root, new Vector3(0,-.24f,0),new Vector3((float)extent*2,.4f,(float)extent*2),new Color(.20f,.25f,.25f));
            for (int i=-(int)extent;i<extent;i+=4)
            {
                Part("Paving joint",root,new Vector3(i,-.025f,0),new Vector3(.025f,.015f,(float)extent*2),new Color(.27f,.32f,.31f));
                Part("Paving joint",root,new Vector3(0,-.025f,i),new Vector3((float)extent*2,.015f,.025f),new Color(.27f,.32f,.31f));
            }
            if(profile.AuthoredMap is ProvingGroundMap)
                Part("Proving ground midline",root,new Vector3(0,-.01f,0),new Vector3((float)profile.ProvingGroundDividerWidth,.015f,(float)extent*2),new Color(.97f,.94f,.82f));
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
        private string UnitModelKey(PlayableEntityKind kind,bool upgraded=false)=>kind==PlayableEntityKind.Igla?"Igla/close":kind==PlayableEntityKind.Cerberus?"Cerberus/cerberus0":kind==PlayableEntityKind.Skat?"skat":kind==PlayableEntityKind.Hawk?"hawk":
            kind==PlayableEntityKind.Tank?(HybridTanks?(upgraded?"tankHybridUpgraded":"tankHybrid"):(upgraded?"tankUpgraded":"tank")):
            kind==PlayableEntityKind.Explorer?(upgraded?"explorerUpgraded":"explorer"):(upgraded?"shkval-guidance-3":"shkval");
        private float UnitModelScale(PlayableEntityKind kind)=>(float)(kind==PlayableEntityKind.Igla?PlayableUnitRules.IglaModelScale:kind==PlayableEntityKind.Cerberus?PlayableUnitRules.CerberusModelScale(profile):kind==PlayableEntityKind.Skat?PlayableUnitRules.SkatModelScale:kind==PlayableEntityKind.Hawk?profile.HawkModelScale:kind==PlayableEntityKind.Shkval?profile.ShkvalModelRadius*profile.ShkvalModelScale/2.05:kind==PlayableEntityKind.Explorer?profile.ExplorerModelRadius*profile.ExplorerModelScale/1.0773:profile.TankModelRadius*profile.TankModelScale/1.585);
        public Actor Tank(int id,bool friendly,PlayableEntityKind kind=PlayableEntityKind.Tank)
        {
            var a=Base(id,friendly,true,kind);
            a.Hull=Model(UnitModelKey(kind),a.Root,friendly);
            // Manifest geometry radius, not a tuning value; model radius/scale are profile-owned.
            a.Hull.localScale=Vector3.one*UnitModelScale(kind);
            if (kind == PlayableEntityKind.Skat)
                a.RepairEmitter = a.Hull.GetComponentsInChildren<Transform>(true).Single(t => t.name == "repairEmitter");
            a.Turret=kind==PlayableEntityKind.Igla||kind==PlayableEntityKind.Cerberus||PlayableUnitRules.Aircraft(kind)?new GameObject("airAim").transform:a.Hull.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="turretYaw");if(kind==PlayableEntityKind.Igla||kind==PlayableEntityKind.Cerberus||PlayableUnitRules.Aircraft(kind))a.Turret.SetParent(a.Hull,false);
            a.Launcher=a.Hull.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name=="launcherPitch");if(kind==PlayableEntityKind.Explorer)BindExplorerPitch(a);
            if(kind==PlayableEntityKind.Igla)a.Igla=a.Hull.GetComponent<IglaPresentation>();
            if(kind==PlayableEntityKind.Cerberus)a.Cerberus=a.Hull.GetComponent<CerberusPresentation>();
            BindTankMechanism(a,kind);
            return a;
        }
        private void BindTankMechanism(Actor a,PlayableEntityKind kind){a.TankLod=a.Hull.GetComponent<TankDistantLod>();a.DetailLod=a.Hull.GetComponent<ModelDetailLod>();if(kind==PlayableEntityKind.Shkval){a.ShkvalMechanism=a.Hull.gameObject.AddComponent<ShkvalMechanismPresentation>();a.ShkvalMechanism.Initialize(a.Launcher);}if(kind==PlayableEntityKind.Tank&&HybridTanks){a.TankMechanism=a.Hull.gameObject.AddComponent<TankMechanismPresentation>();a.TankMechanism.Initialize();}}
        private static void BindExplorerPitch(Actor actor)
        {
            var children=actor.Turret.Cast<Transform>().ToArray();var pitch=new GameObject("weaponPitch").transform;pitch.SetParent(actor.Turret,false);
            foreach(var child in children)child.SetParent(pitch,false);actor.Launcher=pitch;
        }
        public Vector3 DirectMuzzle(Actor actor,PlayableEntityKind kind,int barrel)
        {
            if(kind==PlayableEntityKind.Explorer)
            {
                string socket=actor.UnitUpgraded?(barrel==0?"muzzleLeft":"muzzleRight"):"muzzle";
                return actor.Launcher.Find(socket).position;
            }
            if(HybridTanks)return actor.Turret.Find("barrelRecoil/muzzle").position;
            // The legacy tank mesh has no socket. Both variants share this barrel tip.
            return actor.Turret.TransformPoint(new Vector3(0,.18f,1.935f));
        }
        public void AimExplorer(Actor actor,PlayableEntitySnapshot entity)
        {if(actor.Igla)actor.Igla.Display(entity,matchAnimationSeconds);else if(actor.Cerberus)actor.Cerberus.Display(entity,profile.CerberusAngle,OwnerPaint?.Invoke(entity.Owner)??(entity.Owner==PlayableOwner.Player?Ally:Enemy),matchAnimationSeconds);else if(entity.Kind==PlayableEntityKind.Shkval&&actor.ShkvalMechanism)actor.ShkvalMechanism.Aim(entity.WeaponPitch);else if(entity.Kind==PlayableEntityKind.Explorer&&actor.Launcher)actor.Launcher.localRotation=Quaternion.Euler(-(float)entity.WeaponPitch*Mathf.Rad2Deg,0,0);}
        public void ApplyUnitPose(Actor actor,PlayableEntityKind kind,PlayableDisplayPose pose)
        {
            // Writing an unchanged parent transform dirties the tank's skinned hierarchy.
            var previous=kind==PlayableEntityKind.Tank?actor.AppliedTankPose:null;
            if(!previous.HasValue||!previous.Value.Position.Equals(pose.Position))
            {
                var point = pose.Position;
                Vector3 position;
                NavPoint slope = default;
                if (PlayableUnitRules.Aircraft(kind))
                    position = AirGroundPoint(point);
                else if (profile.AuthoredMap is ThreeCrossingsMap crossings)
                {
                    var surface = crossings.SamplePresentationSurface(point);
                    position = new Vector3((float)point.X, (float)surface.Height, (float)point.Z);
                    slope = surface.Gradient;
                }
                else
                {
                    position = Point(point);
                    slope = profile.AuthoredMap?.SurfaceGradient(point) ?? default;
                }
                var rotation=Quaternion.FromToRotation(Vector3.up,new Vector3((float)-slope.X,1,(float)-slope.Z).normalized);
                actor.Root.SetPositionAndRotation(position,rotation);
            }
            if(!previous.HasValue||previous.Value.Hull!=pose.Hull)
                actor.Hull.localRotation=Quaternion.Euler(0,90-(float)pose.Hull*Mathf.Rad2Deg,0);
            double aim=pose.Hull-pose.Turret;
            if(!previous.HasValue||previous.Value.Hull-previous.Value.Turret!=aim)
                actor.Turret.localRotation=Quaternion.Euler(0,(float)aim*Mathf.Rad2Deg,0);
            if(kind==PlayableEntityKind.Tank)actor.AppliedTankPose=pose;
        }
        public void UpdateResearchModel(Actor a,PlayableEntitySnapshot e)
        {
            if(PlayableUnitRules.Aircraft(e.Kind)||a.UnitUpgraded==e.Upgraded&&a.ExplorerRocketLauncher==e.RocketLauncher)return;
            a.AppliedTankPose=null;
            Object.Destroy(a.Hull.gameObject);
            string key=UnitModelKey(e.Kind,e.Upgraded);
            a.Hull=Model(key,a.Root,e.Owner==PlayableOwner.Player);
            // Approved variant bounds equal their baseline manifest bounds.
            a.Hull.localScale=Vector3.one*UnitModelScale(e.Kind);
            a.Turret=a.Hull.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="turretYaw");
            a.Launcher=a.Hull.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name=="launcherPitch");if(e.Kind==PlayableEntityKind.Explorer)BindExplorerPitch(a);a.UnitUpgraded=e.Upgraded;a.PresentationPaint=null;BindTankMechanism(a,e.Kind);
            a.ExplorerRocketLauncher=e.RocketLauncher;
            a.ExplorerRocketMeshes=null;
            a.ExplorerRocketOwner=e.Owner;
            if(e.RocketLauncher)BuildExplorerRocketLauncher(a);
            if(e.Kind==PlayableEntityKind.Tank&&e.Upgraded)ApplyTankUpgradeGlow(a.Hull);
        }
        private void ApplyTankUpgradeGlow(Transform model)
        {
                foreach(var r in model.GetComponentsInChildren<Renderer>(true))for(int i=0;i<r.sharedMaterials.Length;i++)if(r.sharedMaterials[i].name.EndsWith("chassis-emissive"))
                {var block=new MaterialPropertyBlock();r.GetPropertyBlock(block,i);{ActorMaterialProfile.RecordColorInput(r,i,"_EmissionColor",r.sharedMaterials[i].GetColor("_EmissionColor")*(float)profile.TankChassisGlowIntensity*(MaterialFinish?.emissionGain??1));block.SetColor("_EmissionColor",r.sharedMaterials[i].GetColor("_EmissionColor")*(float)profile.TankChassisGlowIntensity*(MaterialFinish?.emissionGain??1));}r.SetPropertyBlock(block,i);}
        }
        public Actor Building(int id,string kind,bool friendly,bool upgraded=false)
        {
            var a=Base(id,friendly);a.MaterialFinish=MaterialFinish;string key=BuildingKey(kind,upgraded);a.RefineryUpgraded=upgraded;
            var model=Model(key,a.Root,friendly);a.Hull=model;a.DetailLod=model.GetComponent<ModelDetailLod>();
            // Authored heights from src/game/manifest.ts preserve web normalization.
            double scale=BuildingScale(key);
            model.localScale=Vector3.one*(float)scale;
            // Authored front is +Z; FaceBuilding aligns factories with the authoritative exit heading.
            model.rotation=Facing(null,key=="mine");UpdateBuildingSelection(a,key);a.Turbine=model.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name=="turbineYaw");
            if(key=="gauss-tower"){a.Gauss=model.gameObject.AddComponent<GaussTowerPresentation>();a.Gauss.Initialize();}
            if(key=="repair-tower"){a.Healing=model.gameObject.AddComponent<RepairTowerPresentation>();a.Healing.Initialize();}
            if(key=="sentry-turret"){a.Sentry=model.gameObject.AddComponent<SentryTurretPresentation>();a.Sentry.Initialize();}
            a.Factory=model.GetComponent<FactoryModelPresentation>();
            a.Mine=model.GetComponent<ArmoredMinePresentation>();if(a.Mine)a.Mine.Initialize();
            if(a.Factory){a.Factory.ExternallyDriven=true;a.Factory.Initialize();a.Factory.SetOwnerColor(friendly?Ally:Enemy);}
            return a;
        }
        public void UpdateFactoryModel(Actor actor,PlayableBuildingSnapshot building,PlayableSnapshot snapshot)
        {
            UpdateRefineryModel(actor,building,snapshot.Tick);
            if(actor.Mine)actor.Mine.Drive(building,snapshot.Tick,building.Id,actor.MaterialFinish,
                snapshot.Sites.Any(s=>s.CenterId==building.Id&&s.MineDepleted),AnimationSeconds(snapshot));
            if(actor.Factory){actor.FactoryHeading=building.LaunchPhase>FactoryReleaseRules.Closed?building.Heading:TerritoryRules.BuildingHeading;actor.Factory.Drive(building,snapshot.Tick,profile,AnimationSeconds(snapshot));UpdateFactoryVehicle(actor,building,snapshot);}
        }
        public void FaceBuilding(Actor actor,Camera camera=null)
        {
            if(actor.Factory)actor.Hull.rotation=Quaternion.Euler(0,90-(float)actor.FactoryHeading*Mathf.Rad2Deg,0);else actor.Hull.rotation=SiteFacing(actor.Root.position,camera,actor.Mine!=null);
            var p=actor.Root.position;actor.Root.position=Point(new NavPoint(p.x,p.z))+Vector3.up*ApronArt.apronHeight;
        }
        private Quaternion SiteFacing(Vector3 position,Camera camera,bool mine)
            => SiteFacing(new NavPoint(position.x,position.z),camera,mine);
        private Quaternion SiteFacing(NavPoint position,Camera camera,bool mine)
        {
            EnsureSiteFrames();
            if(siteFrames.TryGetValue(SiteFrameKey(position),out var frame))return frame;
            return Facing(camera,mine);
        }
        private ArmoredMineProfile mineArt;
        private Quaternion Facing(Camera camera,bool mine=false)=>mine&&!(profile.AuthoredMap is DeafMirrorMap)
            ?BuildingFacing*Quaternion.Euler(0,(mineArt??(mineArt=ArmoredMineProfile.Load())).facingYawDegrees,0):BuildingFacing;
        // Authored fronts are +Z; mines add their approved visual offset to world south, independent of camera orbit.
        private Quaternion BuildingFacing=>Quaternion.LookRotation(Vector3.back,Vector3.up);
        private static string BuildingKey(string kind,bool upgraded)=>kind=="GaussTower"?"gauss-tower":kind=="RepairTower"?"repair-tower":kind=="SentryTurret"?"sentry-turret":kind=="ScientificCenter"?(upgraded?"research-scanner-upgraded":"research-scanner"):kind=="Refinery"&&upgraded?"refineryUpgraded":kind=="Factory"&&upgraded?"factoryUpgraded":kind=="Hangar"&&upgraded?"hangarUpgraded":kind.ToLowerInvariant();
        public void UpdateRefineryModel(Actor actor,PlayableBuildingSnapshot building,long tick)
        {
            if(building.Kind!=PlayableBuildingKind.Refinery&&building.Kind!=PlayableBuildingKind.ScientificCenter&&building.Kind!=PlayableBuildingKind.Factory&&building.Kind!=PlayableBuildingKind.Hangar)return;
            if(actor.RefineryUpgraded!=building.ModelUpgraded)
            {
                actor.Hull.gameObject.SetActive(false);Object.Destroy(actor.Hull.gameObject);
                string key=BuildingKey(building.Kind.ToString(),building.ModelUpgraded);
                actor.Hull=Model(key,actor.Root,building.Owner==PlayableOwner.Player);actor.DetailLod=actor.Hull.GetComponent<ModelDetailLod>();
                actor.Hull.localScale=Vector3.one*(float)BuildingScale(key);actor.Hull.rotation=BuildingFacing;
                UpdateBuildingSelection(actor,key);actor.RefineryUpgraded=building.ModelUpgraded;actor.PresentationPaint=null;
                actor.Turbine=actor.Hull.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name=="turbineYaw");
                actor.Factory=actor.Hull.GetComponent<FactoryModelPresentation>();
                if(actor.Factory){actor.Factory.ExternallyDriven=true;actor.Factory.Initialize();actor.Factory.SetOwnerColor(building.Owner==PlayableOwner.Player?Ally:Enemy);}
            }
            // Animation follows simulation time, so pause also freezes the turbine.
            if(actor.Turbine)actor.Turbine.localRotation=Quaternion.Euler(0,0,-(float)(tick/30d*profile.RefineryTurbineSpeed%360));
        }
        private double BuildingScale(string key)=>BuildingCombatGeometry.ModelScale(profile,key=="gauss-tower"?PlayableBuildingKind.GaussTower:(key=="research-scanner"||key=="research-scanner-upgraded")?PlayableBuildingKind.ScientificCenter:key=="repair-tower"?PlayableBuildingKind.RepairTower:key=="sentry-turret"?PlayableBuildingKind.SentryTurret:key=="factoryUpgraded"?PlayableBuildingKind.Factory:key=="hangarUpgraded"?PlayableBuildingKind.Hangar:key=="refineryUpgraded"?PlayableBuildingKind.Refinery:(PlayableBuildingKind)System.Enum.Parse(typeof(PlayableBuildingKind),key,true));
        private static IEnumerable<Renderer> ActorModelRenderers(Actor actor)=>actor.Hull.GetComponentsInChildren<Renderer>(true).Concat(actor.FactoryStaged?actor.FactoryStaged.GetComponentsInChildren<Renderer>(true):System.Array.Empty<Renderer>());
        public static void PaintOwner(Actor actor,Color owner)
        {
            if(actor.PresentationPaint.HasValue&&actor.PresentationPaint.Value==owner)return;actor.PresentationPaint=owner;
            if(actor.Factory)actor.Factory.SetOwnerColor(owner);
            foreach(var target in actor.UnitSelection?new[]{actor.Health.gameObject}:new[]{actor.Health.gameObject,actor.Selection}){var renderer=target.GetComponent<Renderer>();var block=new MaterialPropertyBlock();renderer.GetPropertyBlock(block);block.SetColor("_BaseColor",owner);renderer.SetPropertyBlock(block);}
            foreach(var renderer in ActorModelRenderers(actor))for(int slot=0;slot<renderer.sharedMaterials.Length;slot++)
            {
                var role=renderer.sharedMaterials[slot].name;if(!role.EndsWith("team-primary")&&!role.EndsWith("team-emissive"))continue;
                var block=new MaterialPropertyBlock();renderer.GetPropertyBlock(block,slot);{ActorMaterialProfile.RecordColorInput(renderer,slot,"_BaseColor",owner);block.SetColor("_BaseColor",owner);}if(role.EndsWith("team-emissive")){ActorMaterialProfile.RecordColorInput(renderer,slot,"_EmissionColor",owner*(actor.MaterialFinish?.TeamEmissionFor(role)??2.2f));block.SetColor("_EmissionColor",owner*(actor.MaterialFinish?.TeamEmissionFor(role)??2.2f));}renderer.SetPropertyBlock(block,slot);
            }
        }
        public static string ModelAssetKey(string key)=>key=="factory"?"factory-armored":key=="headquarters"?"headquarters-a":key=="mine"?"mine-armored":key=="outpost"?"outpost-b":key;
        public static GameObject ModelPrefab(string key)
        {
            if(key=="factoryUpgraded")return Resources.Load<GameObject>("ProducerEnergy/factory-armored-energy");
            if(key=="hangarUpgraded")return Resources.Load<GameObject>("ProducerEnergy/hangar-energy");
            string asset=ModelAssetKey(key);
            return Resources.Load<GameObject>("StyleA/Lod/"+asset)??Resources.Load<GameObject>("StyleA/"+asset);
        }
        public static Material[] BindSkatMaterials(Renderer renderer)
        {
            var materials=renderer.sharedMaterials;
            for (int slot = 0; slot < materials.Length; slot++)
            {
                string role = materials[slot].name;
                if (role == "skat-emitter") continue;
                string sharedRole = role switch
                {
                    "skat-team-primary" => "team-primary",
                    "skat-trim" => "neutral-metal",
                    "skat-structure" => "structure",
                    "skat-warning" => "warning",
                    _ => throw new System.InvalidOperationException("Unexpected Skat material: " + role)
                };
                materials[slot] = Resources.Load<Material>("StyleA/hawk-" + sharedRole);
                if (!materials[slot])
                    throw new System.InvalidOperationException("Missing shared Skat material: " + sharedRole);
            }
            renderer.sharedMaterials=materials;
            return materials;
        }

        private Transform Model(string key,Transform parent,bool friendly)
        {
            var prefab=key=="skat"?Resources.Load<GameObject>("StyleA/Skat/close"):ModelPrefab(key);
            if(!prefab)throw new System.InvalidOperationException("Missing approved Style A model: "+key);
            var model=key=="Igla/close"?IglaPresentation.Create(parent):key=="Cerberus/cerberus0"?CerberusPresentation.Create(parent,false):Object.Instantiate(prefab,parent,false);Layer(model);var owner=friendly?Ally:Enemy;
            foreach(var renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                var materials=renderer.sharedMaterials;
                if(key=="Cerberus/cerberus0")
                {
                    for(int i=0;i<materials.Length;i++)
                    {
                        string role=materials[i].name;
                        if(role=="team-primary"||role=="neutral-metal"||role=="structure"||role=="system-emissive")
                            materials[i]=Resources.Load<Material>("StyleA/tankHybrid-"+role);
                    }
                    renderer.sharedMaterials=materials;
                }
                if(key=="skat") materials=BindSkatMaterials(renderer);
                for(int slot=0;slot<materials.Length;slot++)
                {
                    var role=materials[slot].name;
                    if(MaterialFinish!=null)MaterialFinish.Apply(renderer,slot,owner);
                    if(!role.EndsWith("team-primary")&&!role.EndsWith("team-emissive"))continue;
                    var properties=new MaterialPropertyBlock();renderer.GetPropertyBlock(properties,slot);{ActorMaterialProfile.RecordColorInput(renderer,slot,"_BaseColor",owner);properties.SetColor("_BaseColor",owner);}
                    // Existing web team-light material convention, not a simulation parameter.
                    if(role.EndsWith("team-emissive")){ActorMaterialProfile.RecordColorInput(renderer,slot,"_EmissionColor",owner*(MaterialFinish?.TeamEmissionFor(role)??2.2f));properties.SetColor("_EmissionColor",owner*(MaterialFinish?.TeamEmissionFor(role)??2.2f));}
                    renderer.SetPropertyBlock(properties,slot);
                }
            }
            if (key == "skat")
            {
                var filter = model.GetComponentInChildren<MeshFilter>();
                var normal = Resources.Load<GameObject>("StyleA/Skat/normal").GetComponentInChildren<MeshFilter>().sharedMesh;
                var far = Resources.Load<GameObject>("StyleA/Skat/far").GetComponentInChildren<MeshFilter>().sharedMesh;
                var close = filter.sharedMesh;
                model.AddComponent<ModelDetailLod>().Configure(new Renderer[] { filter.GetComponent<Renderer>() },
                    new[] { close }, new[] { normal }, new[] { far }, close.bounds);
            }
            return model.transform;
        }
        private GameObject UnitRing(Transform parent,PlayableEntityKind kind)
        {
            if(!selectionMaterial)
            {
                var shader=Resources.Load<Shader>("UnitSelectionRing");
                if(!shader)throw new System.InvalidOperationException("Missing unit selection ring shader");
                selectionMaterial=new Material(shader);
                selectionMaterial.SetTexture("_FogMask",Fog.Texture);
                selectionMaterial.SetVector("_FogBounds",new Vector4((float)profile.ArenaHalfExtent,(float)profile.ArenaHalfExtent,0,0));
                // Four vertices suffice: the shader draws an analytic, antialiased circle.
                selectionQuad=new Mesh{name="Selection ring quad"};
                selectionQuad.vertices=new[]{new Vector3(-1,0,-1),new Vector3(-1,0,1),new Vector3(1,0,1),new Vector3(1,0,-1)};
                selectionQuad.uv=new[]{new Vector2(0,0),new Vector2(0,1),new Vector2(1,1),new Vector2(1,0)};
                selectionQuad.triangles=new[]{0,1,2,0,2,3};selectionQuad.RecalculateBounds();
            }
            double modelRadius=PlayableUnitRules.Aircraft(kind)?profile.HawkSelectionRadius:kind==PlayableEntityKind.Explorer?profile.ExplorerModelRadius*profile.ExplorerModelScale:
                kind==PlayableEntityKind.Shkval?profile.ShkvalModelRadius*profile.ShkvalModelScale:profile.TankModelRadius*profile.TankModelScale;
            float radius=(float)(modelRadius+profile.SelectionRingPadding+profile.SelectionRingWidth);
            var g=new GameObject("Selection");g.layer=presentationLayer;g.transform.SetParent(parent,false);
            g.transform.localPosition=Vector3.up*(float)profile.SelectionRingLift;
            g.transform.localScale=new Vector3(radius,1,radius);
            g.AddComponent<MeshFilter>().sharedMesh=selectionQuad;
            var renderer=g.AddComponent<MeshRenderer>();renderer.sharedMaterial=selectionMaterial;
            renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;renderer.receiveShadows=false;
            var block=new MaterialPropertyBlock();block.SetFloat("_RingWidth",(float)profile.SelectionRingWidth/radius);renderer.SetPropertyBlock(block);
            if(PlayableUnitRules.Aircraft(kind))g.SetActive(false);
            return g;
        }
        private void UpdateBuildingSelection(Actor actor,string key)
        {
            var half=ApronModelHalf(key);
            float radius=half.magnitude+(float)(profile.BuildingSelectionPadding+profile.BuildingSelectionWidth);
            actor.Selection.transform.localScale=new Vector3(radius,1,radius);
            var renderer=actor.Selection.GetComponent<Renderer>();var block=new MaterialPropertyBlock();renderer.GetPropertyBlock(block);
            block.SetFloat("_RingWidth",(float)profile.BuildingSelectionWidth/radius);renderer.SetPropertyBlock(block);
        }
        private Actor Base(int id,bool friendly,bool unit=false,PlayableEntityKind kind=PlayableEntityKind.Tank)
        {
            var a=new Actor{MaterialFinish=MaterialFinish};a.Root=new GameObject("Entity "+id).transform;a.Root.SetParent(root,false);
            a.UnitSelection=unit;
            a.Selection=UnitRing(a.Root,kind);a.Selection.SetActive(false);
            var healthRoot=new GameObject("Health").transform;healthRoot.SetParent(root,false);healthRoot.localPosition=new Vector3(0,3.8f,0);
            a.HealthProfile=profile;
            float outline=(float)profile.HealthBarOutlineWidth,width=(float)profile.HealthBarWidth,height=(float)profile.HealthBarHeight;
            // Z layers only prevent coplanar faces; they are technical rendering offsets.
            a.HealthOutline=Part("Health outline",healthRoot,new Vector3(0,0,.02f),new Vector3(width+2*outline,height+2*outline,.01f),Color.black).transform;
            a.HealthBackground=Part("Health background",healthRoot,Vector3.zero,new Vector3(width,height,.01f),Dark).transform;
            a.Health=Part("Health fill",healthRoot,new Vector3(0,0,-.02f),new Vector3(width,height,.01f),friendly?Ally:Enemy).transform;
            a.HealthDividers=new Transform[profile.HealthBarCells-1];
            float cellWidth=(width-(profile.HealthBarCells-1)*(float)profile.HealthBarCellGap)/profile.HealthBarCells;
            for(int cell=1;cell<profile.HealthBarCells;cell++)
                a.HealthDividers[cell-1]=Part("Health divider "+cell,healthRoot,new Vector3(-width/2+cell*cellWidth+(cell-.5f)*(float)profile.HealthBarCellGap,0,-.04f),new Vector3((float)profile.HealthBarCellGap,height,.01f),Color.black).transform;
            foreach(var renderer in healthRoot.GetComponentsInChildren<Renderer>()){renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;renderer.receiveShadows=false;}
            a.HealthDividerBatch=new HealthDividerBatch(healthRoot,a.HealthDividers);
            actors[id]=a;return a;
        }
        internal readonly struct HealthBarView
        {
            internal readonly ActorCameraProjection Projection;
            internal readonly ModelDetailProfile DetailProfile;
            internal readonly Vector3 Up, AbsoluteUp, Right;
            internal readonly Quaternion Rotation;
            internal readonly float OrthographicPixelsPerUnit;
            internal readonly bool HideDistant, AlwaysShow;

            internal HealthBarView(Camera camera)
            {
                Projection = new ActorCameraProjection(camera);
                DetailProfile = ModelDetailProfile.Current;
                var cameraTransform = camera.transform;
                Up = cameraTransform.up;
                AbsoluteUp = new Vector3(Mathf.Abs(Up.x), Mathf.Abs(Up.y), Mathf.Abs(Up.z));
                Right = Projection.Orthographic ? default : cameraTransform.right;
                Rotation = cameraTransform.rotation;
                OrthographicPixelsPerUnit = Projection.Orthographic ? camera.pixelHeight / (2 * Projection.OrthographicSize) : 0;
                HideDistant = NativeUserSettings.HideDistantHealth;
                AlwaysShow = NativeUserSettings.AlwaysHealth;
            }
        }

        internal static void UpdateHealth(Actor actor, in HealthBarView view, float ratio, bool? selected = null)
        {
            if (actor.DetailLod) actor.DetailLod.Select(view.Projection, view.DetailProfile);
            else if (actor.TankLod) actor.TankLod.Select(view.Projection);
            if (actor.FactoryStaged && actor.FactoryStaged.gameObject.activeSelf)
            {
                if (actor.FactoryStagedLod) actor.FactoryStagedLod.Select(view.Projection, view.DetailProfile);
                else if (actor.FactoryStagedTankLod) actor.FactoryStagedTankLod.Select(view.Projection);
            }
            var profile = actor.HealthProfile;
            var healthRoot = actor.Health.parent;
            Vector3 position = actor.Root.position;
            float viewHalfHeight = view.Projection.Orthographic ? view.Projection.OrthographicSize :
                Mathf.Max(0, view.Projection.Depth(position)) * view.Projection.HalfFieldTangent;
            bool distant = view.HideDistant && viewHalfHeight >= view.DetailProfile.healthHideZoom;
            bool visible = !distant && (view.AlwaysShow || ratio < .999f || (selected ?? actor.Selection.activeSelf));
            healthRoot.gameObject.SetActive(visible);
            // Reveal lays out the bar from the live model, including a replacement while hidden.
            if (!visible) return;
            if (actor.HealthModel != actor.Hull)
            {
                actor.HealthModel = actor.Hull;
                actor.HealthModelRenderers = actor.Hull ? actor.Hull.GetComponentsInChildren<MeshRenderer>(true) : System.Array.Empty<Renderer>();
            }
            // The silhouette includes moving barrels and excludes inactive meshes and effects.
            float top = 0;
            foreach (var renderer in actor.HealthModelRenderers)
            {
                if (!renderer || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                var bounds = renderer.bounds;
                top = Mathf.Max(top, Vector3.Dot(view.Up, bounds.center - position) + Vector3.Dot(view.AbsoluteUp, bounds.extents));
            }
            float height = (float)profile.HealthBarHeight;
            healthRoot.position = position + view.Up * (top + (float)profile.HealthBarModelGap + height / 2 + (float)profile.HealthBarOutlineWidth);
            healthRoot.rotation = view.Rotation;
            var camera = view.Projection.Camera;
            var healthPosition = healthRoot.position;
            float pixelsPerUnit = view.Projection.Orthographic ? view.OrthographicPixelsPerUnit :
                Mathf.Abs(camera.WorldToScreenPoint(healthPosition + view.Right).x - camera.WorldToScreenPoint(healthPosition).x);
            LayoutHealthCells(actor, camera, pixelsPerUnit, out float fullWidth, out height);
            float width = fullWidth * Mathf.Clamp01(ratio);
            actor.Health.localScale = new Vector3(width, height, .01f);
            actor.Health.localPosition = new Vector3((width - fullWidth) * .5f, 0, -.02f);
            actor.Health.gameObject.SetActive(width > 0);
        }

        public static void UpdateHealth(Actor actor, Camera camera, float ratio, bool? selected = null)
            => UpdateHealth(actor, new HealthBarView(camera), ratio, selected);

        private static void LayoutHealthCells(Actor actor, Camera camera, float pixelsPerUnit, out float width, out float height)
        {
            var profile=actor.HealthProfile;var root=actor.Health.parent;
            // One pixel is the raster's smallest visible cell/gap/edge, not a design
            // tuning value. Quantize each shared dimension once, never each boundary.
            pixelsPerUnit=Mathf.Max(Mathf.Epsilon,pixelsPerUnit);
            bool layoutChanged=actor.HealthPixelsPerUnit!=pixelsPerUnit;
            if(layoutChanged)
            {
                int gapPixels=Mathf.Max(1,Mathf.RoundToInt((float)profile.HealthBarCellGap*pixelsPerUnit));
                int cellPixels=Mathf.Max(1,Mathf.RoundToInt(((float)profile.HealthBarWidth*pixelsPerUnit-(profile.HealthBarCells-1)*gapPixels)/profile.HealthBarCells));
                actor.HealthWidthPixels=profile.HealthBarCells*cellPixels+(profile.HealthBarCells-1)*gapPixels;
                actor.HealthHeightPixels=Mathf.Max(1,Mathf.RoundToInt((float)profile.HealthBarHeight*pixelsPerUnit));
                actor.HealthPixelsPerUnit=pixelsPerUnit;
                int outlinePixels=Mathf.Max(1,Mathf.RoundToInt((float)profile.HealthBarOutlineWidth*pixelsPerUnit));
                actor.HealthBackground.localScale=new Vector3(actor.HealthWidthPixels/pixelsPerUnit,actor.HealthHeightPixels/pixelsPerUnit,.01f);
                actor.HealthOutline.localScale=new Vector3((actor.HealthWidthPixels+2*outlinePixels)/pixelsPerUnit,(actor.HealthHeightPixels+2*outlinePixels)/pixelsPerUnit,.01f);
                float dividerWidth=gapPixels/pixelsPerUnit,dividerHeight=actor.HealthHeightPixels/pixelsPerUnit;
                for(int cell=1;cell<profile.HealthBarCells;cell++)
                {
                    var divider=actor.HealthDividers[cell-1];
                    divider.localPosition=new Vector3((-actor.HealthWidthPixels*.5f+cell*cellPixels+(cell-.5f)*gapPixels)/pixelsPerUnit,0,-.04f);
                    divider.localScale=new Vector3(dividerWidth,dividerHeight,.01f);
                }
                actor.HealthDividerBatch?.RefreshIfChanged();
            }
            width=actor.HealthWidthPixels/pixelsPerUnit;height=actor.HealthHeightPixels/pixelsPerUnit;
            // Position remains camera- and actor-dependent even when cell geometry is reused.
            var screen=camera.WorldToScreenPoint(root.position);
            screen.x=Mathf.Round(screen.x-actor.HealthWidthPixels*.5f)+actor.HealthWidthPixels*.5f;
            screen.y=Mathf.Round(screen.y-actor.HealthHeightPixels*.5f)+actor.HealthHeightPixels*.5f;
            root.position=camera.ScreenToWorldPoint(screen);
        }
        public void Remove(int id){RemoveRallyFlag(id);if(actors.TryGetValue(id,out var a)){a.HealthDividerBatch?.Dispose();a.HealthDividerBatch=null;if(a.AirProjection)ReleaseWorldObject(a.AirProjection);ReleaseWorldObject(a.Health.parent.gameObject);ReleaseWorldObject(a.Root.gameObject);actors.Remove(id);}}
        public void Clear(){ClearReinforcementPreviewModels();ClearMines();ClearAbilityEffects();ClearTowerRange();ResetEnvironmentDetail();ClearGroundTrails();ClearUnitWrecks();ClearBuildingDestructions();ClearRallyFlags();Fog.Reset();ClearMemories();ClearPads();foreach(var a in actors.Values){a.HealthDividerBatch?.Dispose();a.HealthDividerBatch=null;if(a.AirProjection)ReleaseWorldObject(a.AirProjection);ReleaseWorldObject(a.Health.parent.gameObject);ReleaseWorldObject(a.Root.gameObject);}actors.Clear();}
        public void Dispose(){
            windowLights?.Dispose();windowLights=null;
            ClearRelayGround();
            DisposeTerrainCacheMaterial();DisposeExplorerRocketMeshes();
            foreach(string name in PreparedTextures)PreparedMapCache.ForgetPixels(PreparedField(name).GetValue(this) as Texture2D);Clear();DisposeReinforcementMaterials();if(stasisMaterial)ReleaseWorldObject(stasisMaterial);if(preparedScene!=null){preparedScene.Dispose();ForgetPreparedFields();}if(cityMaterial)ReleaseWorldObject(cityMaterial);if(dockMaterial)ReleaseWorldObject(dockMaterial);if(dockSpaceMaterial)ReleaseWorldObject(dockSpaceMaterial);if(groveMaterial)ReleaseWorldObject(groveMaterial);if(groveSurfaceMask)ReleaseWorldObject(groveSurfaceMask);if(meridianMaterial)ReleaseWorldObject(meridianMaterial);if(meridianRoadMask)ReleaseWorldObject(meridianRoadMask);wreckFlashVisuals.Dispose();collapseVisuals.Dispose();Fog.Dispose();if(mossPaintMask)ReleaseWorldObject(mossPaintMask);if(mossAccentMask)ReleaseWorldObject(mossAccentMask);if(mossTransitionMask)ReleaseWorldObject(mossTransitionMask);if(mossTerrainMaterial)ReleaseWorldObject(mossTerrainMaterial);if(basaltPaintMask)ReleaseWorldObject(basaltPaintMask);if(basaltAccentMask)ReleaseWorldObject(basaltAccentMask);if(basaltTerrainMaterial)ReleaseWorldObject(basaltTerrainMaterial);surfaceMaterials?.Dispose();if(foundryRoadMask)ReleaseWorldObject(foundryRoadMask);if(foundryLavaHeat)ReleaseWorldObject(foundryLavaHeat);if(foundryRouteMask)ReleaseWorldObject(foundryRouteMask);foreach(var mesh in terrainMeshes)ReleaseWorldObject(mesh);terrainMeshes.Clear();if(apronMaterial)ReleaseWorldObject(apronMaterial);if(capturePadMaterial)ReleaseWorldObject(capturePadMaterial);ReleaseWorldObject(selectionMaterial);ReleaseWorldObject(selectionQuad);ReleaseWorldObject(material);if(root)ReleaseWorldObject(root.gameObject);}
        public GameObject Part(string name,Transform parent,Vector3 position,Vector3 scale,Color color,PrimitiveType shape=PrimitiveType.Cube)
        {
            var g=GameObject.CreatePrimitive(shape);g.name=name;g.layer=presentationLayer;g.transform.SetParent(parent,false);g.transform.localPosition=position;g.transform.localScale=scale;
            ReleaseWorldObject(g.GetComponent<Collider>());g.GetComponent<Renderer>().sharedMaterial=material;
            var properties=new MaterialPropertyBlock();properties.SetColor("_BaseColor",color);g.GetComponent<Renderer>().SetPropertyBlock(properties);return g;
        }
        private static void ReleaseWorldObject(Object value){if(Application.isPlaying)Object.Destroy(value);else Object.DestroyImmediate(value);}
        public Vector3 Point(NavPoint p)
        {
            double height = profile.AuthoredMap is ThreeCrossingsMap crossings
                ? crossings.SamplePresentationSurface(p).Height
                : profile.AuthoredMap?.SurfaceHeight(p) ?? 0;
            return new Vector3((float)p.X, (float)height, (float)p.Z);
        }
    }
}
