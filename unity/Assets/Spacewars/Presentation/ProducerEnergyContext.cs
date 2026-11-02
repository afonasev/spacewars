using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Spacewars.Runtime;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Spacewars.Presentation
{
    // Explicit model review scene: actual game world renderer, map, scales,
    // materials and rigs. Posed models, no match simulation or navigation claim.
    public sealed class ProducerEnergyContext : MonoBehaviour
    {
        sealed class BaseView
        {
            public TerritorySite site;public Color color;public string hex;
            public PlayableWorld.Actor factory,hangar;public Transform factoryBase,hangarBase,factoryNew,hangarNew;
            public readonly List<PlayableWorld.Actor> actors=new();
        }
        PlayableWorld world;PlayableProfile profile;Camera cameraView;RenderTexture target;
        readonly List<BaseView> bases=new();string output;int nextId=700000;
        [Serializable] sealed class Evidence {public string classification="NATIVE_PLAYER_POSED_BASE_MODEL_REVIEW_NOT_ORDINARY_MATCH";public string map,profile,buildGuid;public bool muted;public string[] colors;public string[] captures;}
        readonly List<string> captures=new();
        void Start(){StartCoroutine(RunGuarded());}
        IEnumerator RunGuarded()
        {
            var run=Run();
            while(true){object current;try{if(!run.MoveNext())break;current=run.Current;}catch(Exception e){Debug.LogException(e);File.WriteAllText(Path.Combine(output??Application.persistentDataPath,"producer-energy-failure.txt"),e.ToString());Application.Quit(1);yield break;}yield return current;}
        }
        Transform Candidate(PlayableWorld.Actor actor,string key,Color color)
        {
            var model=Instantiate(Resources.Load<GameObject>("ProducerEnergy/"+key+"-energy"),actor.Root,false).transform;
            model.localPosition=actor.Hull.localPosition;model.localRotation=actor.Hull.localRotation;model.localScale=actor.Hull.localScale;
            world.Layer(model.gameObject);
            foreach(var r in model.GetComponentsInChildren<Renderer>(true))for(int i=0;i<r.sharedMaterials.Length;i++)world.MaterialFinish.Apply(r,i,color);
            var effect=model.GetComponent<FactoryModelPresentation>();if(effect){effect.ExternallyDriven=true;effect.Initialize();effect.SetOwnerColor(color);effect.Sample(FactoryModelPresentation.Phase.Idle,0,0);}
            return model;
        }
        void Upgrade(BaseView b,bool upgraded)
        {
            b.factoryBase.gameObject.SetActive(!upgraded);b.hangarBase.gameObject.SetActive(!upgraded);b.factoryNew.gameObject.SetActive(upgraded);b.hangarNew.gameObject.SetActive(upgraded);
            b.factory.Hull=upgraded?b.factoryNew:b.factoryBase;b.hangar.Hull=upgraded?b.hangarNew:b.hangarBase;
            foreach(var a in new[]{b.factory,b.hangar}){a.DetailLod=a.Hull.GetComponent<ModelDetailLod>();a.Factory=a.Hull.GetComponent<FactoryModelPresentation>();a.PresentationPaint=null;PlayableWorld.PaintOwner(a,b.color);}
        }
        PlayableWorld.Actor Building(BaseView b,string kind,NavPoint point,bool upgraded=false)
        {
            var a=world.Building(nextId++,kind,true,upgraded);a.Root.position=world.Point(point);world.FaceBuilding(a);PlayableWorld.PaintOwner(a,b.color);
            a.Health.parent.gameObject.SetActive(false);a.Selection.SetActive(false);b.actors.Add(a);return a;
        }
        void Focus(Vector3 point,float zoom)
        {
            cameraView.orthographicSize=zoom;cameraView.transform.position=point+new Vector3((float)profile.CameraOffsetX,(float)profile.CameraHeight,(float)profile.CameraOffsetZ);cameraView.transform.LookAt(point);
            foreach(var b in bases)foreach(var a in b.actors)if(a.DetailLod)a.DetailLod.Select(cameraView);
        }
        void Capture(string name)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(output,name+".png")));
            cameraView.Render();var previous=RenderTexture.active;RenderTexture.active=target;var pixels=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);
            try{pixels.ReadPixels(new Rect(0,0,target.width,target.height),0,0);pixels.Apply();File.WriteAllBytes(Path.Combine(output,name+".png"),pixels.EncodeToPNG());captures.Add(name+".png");}
            finally{RenderTexture.active=previous;Destroy(pixels);}
        }
        IEnumerator Run()
        {
            var args=Environment.GetCommandLineArgs();int index=Array.IndexOf(args,"-producerEnergyOutput");
            if(index<0||index+1>=args.Length)throw new InvalidOperationException("Owned output path required");output=args[index+1];Directory.CreateDirectory(output);
            if(!NativeSilentStartup.IsSilent)throw new InvalidOperationException("Model review must start muted");
            var map=new FoundryMap(JsonUtility.FromJson<FoundryProfileData>(Resources.Load<TextAsset>("FoundryProfile").text));
            profile=PlayableProfile.Create(JsonUtility.FromJson<PlayableProfileData>(Resources.Load<TextAsset>("PlayableProfile").text),map);
            var sun=new GameObject("Game sun").AddComponent<Light>();sun.transform.SetParent(transform,false);IndustrialLightingProfile.Load(map.Id).ApplyScene(sun);
            world=new PlayableWorld(transform,profile,staged:true);yield return world.Preparation;world.Fog.ShowPublic();
            cameraView=new GameObject("Review camera").AddComponent<Camera>();cameraView.transform.SetParent(transform,false);cameraView.orthographic=true;cameraView.nearClipPlane=.1f;cameraView.farClipPlane=600;
            cameraView.clearFlags=CameraClearFlags.SolidColor;cameraView.backgroundColor=new Color(.06f,.075f,.09f);cameraView.gameObject.AddComponent<UniversalAdditionalCameraData>();
            target=new RenderTexture(1920,1080,24){antiAliasing=4};target.Create();cameraView.targetTexture=target;
            var sites=map.Sites(profile).Where(s=>s.Kind==PlayableBuildingKind.Headquarters).ToArray();
            var snapshots=new List<PlayableBuildingSnapshot>();var states=new List<TerritorySiteSnapshot>();
            for(int i=0;i<6;i++)
            {
                var site=sites[i];string hex=NativeLobbyConfiguration.ColorHex[i];ColorUtility.TryParseHtmlString(hex,out var color);
                var b=new BaseView{site=site,color=color,hex=hex};bases.Add(b);
                var hq=Building(b,"Headquarters",site.Position);states.Add(new TerritorySiteSnapshot(site,(PlayableOwner)i,(PlayableOwner)i,1,false,hq.Root.GetInstanceID(),true));
                // Current authored base slots; foreground pair shows both producers together.
                var slots=site.Slots.OrderBy(s=>s.Position.Z).ThenBy(s=>s.Position.X).ToArray();
                string[] kinds={"Factory","Hangar","ScientificCenter","Refinery"};
                for(int s=0;s<Math.Min(kinds.Length,slots.Length);s++)
                {
                    var a=Building(b,kinds[s],slots[s].Position,s==2);var kind=(PlayableBuildingKind)Enum.Parse(typeof(PlayableBuildingKind),kinds[s]);
                    snapshots.Add(new PlayableBuildingSnapshot(nextId-1,(PlayableOwner)i,kind,slots[s].Position,100,1,0,0,default,site.Id,slots[s].Id));
                    if(s==0)b.factory=a;if(s==1)b.hangar=a;
                }
                snapshots.Add(new PlayableBuildingSnapshot(hq.Root.GetInstanceID(),(PlayableOwner)i,PlayableBuildingKind.Headquarters,site.Position,100,1,0,0,default,site.Id));
                b.factoryBase=b.factory.Hull;b.hangarBase=b.hangar.Hull;b.factoryNew=Candidate(b.factory,"factory-armored",color);b.hangarNew=Candidate(b.hangar,"hangar",color);Upgrade(b,true);
                var pair=(b.factory.Root.position+b.hangar.Root.position)*.5f;
                var unitKinds=new[]{PlayableEntityKind.Tank,PlayableEntityKind.Explorer,PlayableEntityKind.Shkval,PlayableEntityKind.Hawk};
                for(int u=0;u<4;u++)
                {
                    var a=world.Tank(nextId++,true,unitKinds[u]);var p=u==3?new NavPoint(pair.x+6.5,pair.z-6.5):new NavPoint(pair.x+(u-1.5)*2.7,pair.z-5.0);
                    a.Root.position=world.Point(p);if(u==3)a.Root.position+=Vector3.up*1.5f;
                    a.Hull.rotation=Quaternion.LookRotation(Vector3.back,Vector3.up);PlayableWorld.PaintOwner(a,color);a.Health.parent.gameObject.SetActive(false);a.Selection.SetActive(false);b.actors.Add(a);
                }
            }
            world.OwnerPaint=o=>o.HasValue?bases[(int)o.Value].color:Color.white;
            var snapshot=new PlayableSnapshot("producer-energy-context",1,1,7,1,1,RuntimeStatus.Running,true,PlayableMatchOutcome.Playing,0,new NavGeometry(profile.ArenaHalfExtent,Array.Empty<NavObstacle>(),1),Array.Empty<PlayableEntitySnapshot>(),snapshots.ToArray(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,states.ToArray());
            world.RenderSites(snapshot);world.ApplyGraphics(world.MaterialFinish,IndustrialLightingProfile.Load(map.Id));
            yield return null;yield return null;
            for(int i=0;i<bases.Count;i++)
            {
                var b=bases[i];var targetPoint=world.Point(b.site.Position)+Vector3.back*2+Vector3.up*.8f;
                Focus(targetPoint,(float)profile.CameraOrthoSize);Capture("base-"+i+"-normal");
                Focus(targetPoint,11);Capture("base-"+i+"-detail");
                var pair=(b.factory.Root.position+b.hangar.Root.position)*.5f+Vector3.up*.7f;
                Focus(pair,6.1f);Capture("pair-"+i+"-upgraded");
                Upgrade(b,false);Focus(pair,6.1f);Capture("pair-"+i+"-base");Upgrade(b,true);
            }
            var sample=bases[0];
            foreach(var actor in new[]{sample.factory,sample.hangar})
            {
                string key=actor==sample.factory?"factory":"hangar";Focus(actor.Root.position+Vector3.up*.8f,3.4f);
                for(int tier=0;tier<3;tier++){actor.DetailLod.SetDetail(tier);Capture(key+"-lod"+tier);}
                actor.DetailLod.SetDetail(0);
                if(actor.Factory)
                {
                    for(int frame=0;frame<13;frame++){actor.Factory.Sample(FactoryModelPresentation.Phase.Rollout,frame/12f,1.4f);Capture("motion/factory-"+frame.ToString("D2"));}
                    actor.Factory.Sample(FactoryModelPresentation.Phase.Working,0,1.4f);Capture("factory-working");
                }
                else
                {
                    for(int frame=0;frame<13;frame++)
                    {
                        world.UpdateHangar(actor,new PlayableBuildingSnapshot(1,PlayableOwner.Player,PlayableBuildingKind.Hangar,default,100,1,0,0,default,launchPhase:1,launchTime:profile.HawkRoofSeconds*frame/12,launchKind:PlayableEntityKind.Hawk),frame);
                        actor.PresentationPaint=null;PlayableWorld.PaintOwner(actor,sample.color);Capture("motion/hangar-"+frame.ToString("D2"));
                    }
                    world.UpdateHangar(actor,new PlayableBuildingSnapshot(1,PlayableOwner.Player,PlayableBuildingKind.Hangar,default,100,1,0,0,default,launchPhase:3,launchKind:PlayableEntityKind.Hawk),20);actor.PresentationPaint=null;PlayableWorld.PaintOwner(actor,sample.color);Capture("hangar-platform-raised");
                }
            }
            var report=new Evidence{map=map.Id,profile=profile.ProfileId,buildGuid=Application.buildGUID,muted=true,colors=bases.Select(b=>b.hex).ToArray(),captures=captures.ToArray()};
            File.WriteAllText(Path.Combine(output,"context-result.json"),JsonUtility.ToJson(report,true));Debug.Log("PRODUCER_ENERGY_CONTEXT_PASS");Application.Quit(0);
        }
        void OnDestroy(){if(cameraView)cameraView.targetTexture=null;if(target){target.Release();Destroy(target);}world?.Dispose();}
    }
}
