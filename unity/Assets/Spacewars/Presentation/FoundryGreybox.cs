using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Runtime;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.InputSystem;
namespace Spacewars.Presentation
{
    // Explicit greybox review scene, using the ordinary six-owner runtime and production models.
    // Revealed terrain and selectable owner perspectives are inspection tools, not gameplay fog rules.
    public sealed class FoundryGreybox : MonoBehaviour
    {
        private PlayableProfile profile; private FoundryMap map;private PlayableWorld world;private PlayableRuntime runtime;
        private UnityHostRouteService routes; private Camera cameraView; private int owner; private long sequence;
        private readonly HashSet<int> selection=new HashSet<int>(); private string notice="";private bool paused;
        public static PlayableProfile LoadProfile()=>PlayableProfile.Create(JsonUtility.FromJson<PlayableProfileData>(Resources.Load<TextAsset>("PlayableProfile").text),new FoundryMap(JsonUtility.FromJson<FoundryProfileData>(Resources.Load<TextAsset>("FoundryProfile").text)),"Чёрная плавильня v2");
        private void Start()
        {
            AudioListener.volume=0;Application.runInBackground=true;profile=LoadProfile();map=(FoundryMap)profile.AuthoredMap;
            RenderSettings.ambientLight=new Color(.65f,.65f,.66f);var sun=new GameObject("Foundry sun").AddComponent<Light>();sun.transform.SetParent(transform);sun.type=LightType.Directional;sun.intensity=1.4f;sun.transform.rotation=Quaternion.Euler(55,-30,0);
            world=new PlayableWorld(transform,profile);world.InspectionSites();world.InspectionReveal();
            cameraView=new GameObject("Greybox camera").AddComponent<Camera>();cameraView.transform.SetParent(transform);cameraView.orthographic=true;cameraView.orthographicSize=(float)(map.HalfExtent*.97);cameraView.nearClipPlane=.1f;cameraView.farClipPlane=600;cameraView.transform.position=new Vector3(0,300,0);cameraView.transform.rotation=Quaternion.Euler(90,0,0);cameraView.backgroundColor=new Color(.08f,.09f,.1f);
            routes=new UnityHostRouteService();runtime=new PlayableRuntime(map.Configuration(profile,19092026),1,startPaused:true);paused=true;
        }
        private PlayableSnapshot View=>runtime?.ParticipantView("foundry-"+(owner+1));
        private void Update()
        {
            if(runtime==null)return;routes.Service(runtime,profile.NavigationRequestsPerFrame);
            var keyboard=Keyboard.current;var mouse=Mouse.current;
            if(keyboard!=null)
            {
                for(int i=0;i<6;i++)if(keyboard[(Key)((int)Key.Digit1+i)].wasPressedThisFrame){owner=i;selection.Clear();}
                if(keyboard.spaceKey.wasPressedThisFrame){paused=!paused;runtime.RequestPause(paused);}
                if(keyboard.rKey.wasPressedThisFrame){runtime.RequestStop();routes.Dispose();world.Clear();routes=new UnityHostRouteService();runtime=new PlayableRuntime(map.Configuration(profile,19092026),++generation,startPaused:true);sequence=0;paused=true;selection.Clear();}
                if(keyboard.fKey.wasPressedThisFrame)BuildAtCursor(mouse);
                if(keyboard.eKey.wasPressedThisFrame)Submit(PlayableCommandKind.QueueExplorer,selection.ToArray());
                if(keyboard.tKey.wasPressedThisFrame)Submit(PlayableCommandKind.QueueTank,selection.ToArray());
                if(keyboard.sKey.wasPressedThisFrame)Submit(PlayableCommandKind.Stop,selection.ToArray());
                Vector3 pan=new Vector3((keyboard.dKey.isPressed?1:0)-(keyboard.aKey.isPressed?1:0),0,(keyboard.wKey.isPressed?1:0)-(keyboard.sKey.isPressed?1:0));cameraView.transform.position+=pan*(float)profile.CameraPanSpeed*Time.unscaledDeltaTime;
            }
            if(mouse!=null&&mouse.position.ReadValue().y<Screen.height-90)
            {
                if(mouse.leftButton.wasPressedThisFrame&&TryGround(mouse.position.ReadValue(),out var point)){var v=View;selection.Clear();var unit=v.Entities.Where(e=>e.Owner==v.Owner).OrderBy(e=>Distance(e.Position,point)).FirstOrDefault();var building=v.Buildings.Where(b=>b.Owner==v.Owner).OrderBy(b=>Distance(b.Position,point)).FirstOrDefault();if(unit!=null&&Distance(unit.Position,point)<5)selection.Add(unit.Id);else if(building!=null&&Distance(building.Position,point)<7)selection.Add(building.Id);}
                if(mouse.rightButton.wasPressedThisFrame&&TryGround(mouse.position.ReadValue(),out var target))Submit(PlayableCommandKind.Move,selection.ToArray(),target);
                cameraView.orthographicSize=Mathf.Clamp(cameraView.orthographicSize-mouse.scroll.ReadValue().y*.05f,20,(float)map.HalfExtent);
            }
            // Merge only published perspectives in an explicitly revealed inspection view.
            var frame=runtime.OfflineFrame;if(frame==null)return;var entities=frame.Views.Values.SelectMany(v=>v.Entities).GroupBy(e=>e.Id).Select(g=>g.First());var buildings=frame.Views.Values.SelectMany(v=>v.Buildings).GroupBy(b=>b.Id).Select(g=>g.First());
            var alive=new HashSet<int>();
            foreach(var e in entities){alive.Add(e.Id);if(!world.Actors.TryGetValue(e.Id,out var actor))actor=world.Tank(e.Id,e.Owner==PlayableOwner.Player,e.Kind);actor.Root.position=world.Point(e.Position);var slope=map.SurfaceGradient(e.Position);actor.Root.rotation=Quaternion.FromToRotation(Vector3.up,new Vector3((float)-slope.X,1,(float)-slope.Z).normalized);actor.Hull.localRotation=Quaternion.Euler(0,90-(float)e.HullHeading*Mathf.Rad2Deg,0);actor.Selection.SetActive(selection.Contains(e.Id));PlayableWorld.PaintOwner(actor,(int)e.Owner<3?Color.cyan:new Color(1,.35f,.45f));}
            foreach(var b in buildings){alive.Add(b.Id);if(b.Phase==ConstructionPhase.Pending)continue;if(!world.Actors.TryGetValue(b.Id,out var actor))actor=world.Building(b.Id,b.Kind.ToString(),(int)b.Owner<3,b.RefineryUpgraded);actor.Root.position=world.Point(b.Position);actor.Root.rotation=Quaternion.Euler(0,-(float)b.Heading*Mathf.Rad2Deg,0);actor.Root.localScale=new Vector3(1,Mathf.Lerp(.2f,1,(float)b.Progress),1);world.FaceBuilding(actor,cameraView);actor.Selection.SetActive(selection.Contains(b.Id));PlayableWorld.PaintOwner(actor,(int)b.Owner<3?Color.cyan:new Color(1,.35f,.45f));}
            foreach(int id in world.Actors.Keys.ToArray())if(!alive.Contains(id))world.Remove(id);
        }
        private long generation=1;
        private static double Distance(NavPoint a,NavPoint b)=>Math.Sqrt((a.X-b.X)*(a.X-b.X)+(a.Z-b.Z)*(a.Z-b.Z));
        private bool TryGround(Vector2 cursor,out NavPoint hit)
        {
            var ray=cameraView.ScreenPointToRay(cursor);double best=double.PositiveInfinity;hit=default(NavPoint);
            foreach(var support in map.Supports){double denominator=ray.direction.y-support.Gradient.X*ray.direction.x-support.Gradient.Z*ray.direction.z;if(Math.Abs(denominator)<1e-9)continue;double t=(support.HeightAt(new NavPoint(ray.origin.x,ray.origin.z))-ray.origin.y)/denominator;var q=ray.GetPoint((float)t);var p=new NavPoint(q.x,q.z);if(t>=0&&t<best&&support.Contains(p)){best=t;hit=p;}}return !double.IsPositiveInfinity(best)&&map.SupportsFootprint(hit,0);
        }
        private void BuildAtCursor(Mouse mouse)
        {
            if(owner!=0){notice="Управление доступно для A1; остальные места — перспективы ИИ.";return;}
            if(mouse==null||!TryGround(mouse.position.ReadValue(),out var q))return;var v=View;
            var slot=v.DiscoveredSites.SelectMany(s=>s.Slots.Select(t=>new{Site=s,Slot=t})).OrderBy(s=>Distance(s.Slot.Position,q)).FirstOrDefault();if(slot==null||Distance(slot.Slot.Position,q)>6)return;
            var parent=v.Buildings.FirstOrDefault(b=>b.SiteId==slot.Site.Id&&b.SlotId==0&&b.Owner==v.Owner);if(parent==null)return;
            var result=runtime.TrySubmit(new PlayableCommand(generation,++sequence,"foundry-"+(owner+1),PlayableCommandKind.BuildAt,Array.Empty<int>(),siteId:slot.Site.Id,slotId:slot.Slot.Id,buildingKind:PlayableBuildingKind.Factory,parentId:parent.Id));notice=result.Status.ToString();
        }
        private void Submit(PlayableCommandKind kind,int[] ids,NavPoint target=default(NavPoint)){if(owner!=0){notice="Управление доступно для A1; остальные места — перспективы ИИ.";return;}notice=runtime.TrySubmit(new PlayableCommand(generation,++sequence,"foundry-"+(owner+1),kind,ids,target)).Status.ToString();}
        private void OnGUI()
        {
            GUI.Box(new Rect(0,0,Screen.width,88),"ЧЁРНАЯ ПЛАВИЛЬНЯ · greybox v2 · обзор раскрыт для осмотра");
            GUI.Label(new Rect(12,25,Screen.width-24,28),"1–6 перспектива · управление A1 · Space пауза · ЛКМ выбор · ПКМ путь/захват · F фабрика под курсором · E/T производство · R перезапуск · WASD/колесо камера");
            GUI.Label(new Rect(12,53,Screen.width-24,28),$"{(owner<3?"A":"B")}{owner%3+1} · команда {(owner<3?1:2)} · {(paused?"ПАУЗА":"МАТЧ")} · credits {View?.Credits} · tick {View?.Tick} · {notice}");
        }
        private void OnDestroy(){runtime?.RequestStop();routes?.Dispose();world?.Dispose();}
    }
}
