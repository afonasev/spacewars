using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Spacewars.Input;
using Spacewars.Runtime;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;

namespace Spacewars.Presentation
{
    // Geometry/colors implement Content/fixture-v1.json; they are diagnostic content,
    // not simulation or camera tuning. All movement/camera rates come from profile.
    public sealed class FoundationBootstrap : MonoBehaviour
    {
        private FoundationProfile profile;
        private MatchRuntime runtime;
        private FoundationInput controls;
        private Camera mainCamera;
        private readonly List<Camera> cameras = new List<Camera>();
        private Transform tank;
        private Renderer body;
        private Material material;
        private Label status;
        private VisualElement hud;
        private bool selected, pauseIntent, restarting, shuttingDown;
        private long generation, sequence;
        private float shutdownStarted;
        // Technical fault-report deadline, not gameplay timing; never aborts the worker.
        private const float ShutdownWarningSeconds = 3f;
        private readonly List<float> frames = new List<float>();
        private readonly List<double> ticks = new List<double>();
        private long measuredTick = -1;
        private int cameraCount = 1;
        private string evidencePath;
        private string manualEvidencePath;
        private float startTime;
        private string notice = "ЛКМ — выбрать танк";
        private readonly List<string> actions = new List<string>();
        private int smokeStage;
        private long moveTick;
        private double movedX;

        private void Start()
        {
            try
            {
                var json = Resources.Load<TextAsset>("FoundationProfile");
                if (json == null) throw new InvalidOperationException("FoundationProfile is missing");
                profile = FoundationProfile.Create(JsonUtility.FromJson<FoundationProfileData>(json.text));
                AudioListener.volume = 0;
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = -1;
                startTime = Time.realtimeSinceStartup;
                ParseArguments();
                CreateWorld();
                CreateHud();
                controls = gameObject.AddComponent<FoundationInput>();
                controls.SelectAt = Select;
                controls.MoveAt = Move;
                controls.Stop = StopUnit;
                controls.TogglePause = TogglePause;
                controls.Restart = Restart;
                controls.FocusLost = () => SetPause(true);
                controls.Pan = Pan;
                controls.Zoom = Zoom;
                controls.IsPointerOverUi = OverHud;
                StartSession();
            }
            catch (Exception e) { Debug.LogException(e); if (status == null) CreateHud(); status.text = "Не удалось запустить срез: " + e.Message; enabled = false; }
        }

        private void Record(string action)
        {
            if (actions.Count == 256) actions.RemoveAt(0);
            var view=runtime?.Latest;
            actions.Add(action + (view==null ? "" : " generation="+view.Generation+" tick="+view.Tick+" x="+view.Entity.X+" z="+view.Entity.Z+" paused="+view.Paused));
            if(manualEvidencePath!=null) File.WriteAllLines(Path.Combine(manualEvidencePath,"native-input-actions.txt"),actions);
        }

        private void ParseArguments()
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-foundationManualEvidence") manualEvidencePath = args[i + 1];
                if (args[i] == "-foundationEvidence") evidencePath = args[i + 1];
                if (args[i] == "-foundationCameras") cameraCount = int.Parse(args[i + 1]);
            }
            if (cameraCount != 1 && cameraCount != 2 && cameraCount != 4) throw new ArgumentException("Camera count must be 1,2,4");
            if (evidencePath != null) Directory.CreateDirectory(evidencePath);
            if (manualEvidencePath != null) Directory.CreateDirectory(manualEvidencePath);
        }

        private void CreateWorld()
        {
            material = Resources.Load<Material>("RuntimeLit");
            if (!material) throw new InvalidOperationException("RuntimeLit material missing");
            RenderSettings.ambientLight = new Color(.6f, .67f, .76f);
            var light = new GameObject("Sun").AddComponent<Light>();
            light.type = LightType.Directional; light.intensity = 1.5f;
            light.transform.rotation = Quaternion.Euler(50, -35, 0);
            Shape("Ground", null, new Vector3(0,-.15f,0), new Vector3((float)profile.GroundHalfExtent*2,.2f,(float)profile.GroundHalfExtent*2), new Color(.12f,.19f,.23f));
            // Static fixture grid: content-only marks, no colliders or simulation cells.
            for (int i=-20;i<=20;i+=4)
            {
                Shape("Grid X", null, new Vector3(i,-.035f,0),new Vector3(.025f,.01f,40),new Color(.22f,.3f,.34f),false);
                Shape("Grid Z", null, new Vector3(0,-.035f,i),new Vector3(40,.01f,.025f),new Color(.22f,.3f,.34f),false);
            }
            tank = new GameObject("Owned tank • entity 1").transform;
            body = Shape("Hull", tank, new Vector3(0,.4f,0),new Vector3(1.4f,.65f,2),new Color(.2f,.68f,.75f)).GetComponent<Renderer>();
            Shape("Turret",tank,new Vector3(0,.9f,0),new Vector3(.8f,.45f,.8f),new Color(.3f,.8f,.84f));
            Shape("Barrel",tank,new Vector3(0,1,.9f),new Vector3(.2f,.2f,1.3f),new Color(.72f,.84f,.87f));
            Shape("Track L",tank,new Vector3(-.8f,.28f,0),new Vector3(.3f,.4f,2.2f),new Color(.06f,.09f,.12f));
            Shape("Track R",tank,new Vector3(.8f,.28f,0),new Vector3(.3f,.4f,2.2f),new Color(.06f,.09f,.12f));
            for (int i=0;i<cameraCount;i++)
            {
                var camera = new GameObject("Camera " + i).AddComponent<Camera>();
                camera.transform.position = new Vector3(0,(float)profile.CameraHeight,(float)profile.CameraOffsetZ);
                camera.transform.rotation = Quaternion.Euler((float)profile.CameraPitchDegrees,0,0);
                camera.orthographic = true; camera.orthographicSize=(float)profile.CameraZoom;
                camera.backgroundColor=new Color(.045f,.065f,.09f);camera.clearFlags=CameraClearFlags.SolidColor;
                camera.GetUniversalAdditionalCameraData();
                camera.rect = cameraCount==1 ? new Rect(0,0,1,1) : cameraCount==2 ? new Rect(i*.5f,0,.5f,1) : new Rect(i%2*.5f,i/2*.5f,.5f,.5f);
                cameras.Add(camera);
            }
            mainCamera=cameras[0];mainCamera.tag="MainCamera";
        }
        private GameObject Shape(string name,Transform parent,Vector3 position,Vector3 scale,Color color,bool collider=true)
        {
            var obj=GameObject.CreatePrimitive(PrimitiveType.Cube);obj.name=name;obj.transform.SetParent(parent,false);
            obj.transform.localPosition=position;obj.transform.localScale=scale;
            obj.GetComponent<Renderer>().sharedMaterial=material;
            var props=new MaterialPropertyBlock();props.SetColor("_BaseColor",color);obj.GetComponent<Renderer>().SetPropertyBlock(props);
            if(!collider) Destroy(obj.GetComponent<Collider>());
            return obj;
        }
        private void CreateHud()
        {
            var panel=ScriptableObject.CreateInstance<PanelSettings>(); panel.themeStyleSheet=Resources.Load<ThemeStyleSheet>("FoundationTheme"); panel.scaleMode=PanelScaleMode.ScaleWithScreenSize;
            panel.referenceResolution=new Vector2Int(1280,800);
            var doc=gameObject.AddComponent<UIDocument>();doc.panelSettings=panel;
            var root=doc.rootVisualElement;root.pickingMode=PickingMode.Ignore;
            root.style.unityFont=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            root.style.fontSize=15;
            hud=new VisualElement();hud.style.position=Position.Absolute;hud.style.left=20;hud.style.top=20;
            hud.style.width=370;hud.style.paddingLeft=18;hud.style.paddingRight=18;hud.style.paddingTop=14;hud.style.paddingBottom=14;
            hud.style.backgroundColor=new Color(.035f,.06f,.09f,.94f);hud.style.color=new Color(.87f,.93f,.97f);
            root.Add(hud);
            var title=new Label("SPACEWARS / UNITY");title.style.fontSize=23;title.style.height=38;title.style.unityTextAlign=TextAnchor.MiddleLeft;title.style.unityFontStyleAndWeight=FontStyle.Bold;hud.Add(title);
            var subtitle=new Label("Foundation · диагностический срез");subtitle.style.fontSize=13;subtitle.style.height=24;subtitle.style.color=new Color(.44f,.77f,.82f);hud.Add(subtitle);
            status=new Label();status.style.whiteSpace=WhiteSpace.Normal;status.style.marginTop=12;status.style.marginBottom=10;hud.Add(status);
            hud.Add(new Label("ЛКМ: выбор · ПКМ: движение · S: стоп\nСтрелки: камера · Колесо: масштаб\nEscape: пауза · R: начать заново"));
            var pause=new Button(TogglePause){text="Пауза / продолжить"};pause.style.marginTop=12;hud.Add(pause);
            hud.Add(new Button(Restart){text="Начать заново"});
            hud.Add(new Button(()=>{shuttingDown=true;shutdownStarted=Time.realtimeSinceStartup;runtime?.RequestStop();StartCoroutine(QuitWhenStopped());}){text="Выйти"});
            var disclaimer=new Label("Без навигации, боя и экономики.\nИгровой паритет пока не проверен.");disclaimer.style.marginTop=10;disclaimer.style.color=new Color(.66f,.72f,.78f);hud.Add(disclaimer);
            hud.Query<Button>().ForEach(button => { button.style.height=36; button.style.backgroundColor=new Color(.12f,.23f,.29f); button.style.color=Color.white; });
        }
        private bool OverHud(Vector2 position)
        {
            if(hud?.panel==null)return false;
            return hud.worldBound.Contains(RuntimePanelUtils.ScreenToPanel(hud.panel,new Vector2(position.x,Screen.height-position.y)));
        }
        private void StartSession()
        {
            runtime=new MatchRuntime(profile,++generation,19092026);sequence=0;selected=false;pauseIntent=false;restarting=false;
            notice="ЛКМ — выбрать танк";Record("start generation="+generation);
        }
        private void Select(Vector2 point)
        {
            selected=Physics.Raycast(mainCamera.ScreenPointToRay(point),out var hit) && hit.transform.IsChildOf(tank);
            notice=selected?"Танк выбран. ПКМ — назначить цель":"Танк не выбран";Record("selected="+selected);
        }
        private void Move(Vector2 point)
        {
            if(!selected)return;
            var ray=mainCamera.ScreenPointToRay(point);
            if(new Plane(Vector3.up,Vector3.zero).Raycast(ray,out var distance))
            {
                var target=ray.GetPoint(distance);Submit(FoundationCommandType.Move,target.x,target.z);
            }
        }
        private void StopUnit(){if(selected)Submit(FoundationCommandType.Stop,0,0);}
        private void Submit(FoundationCommandType type,double x,double z)
        {
            if(runtime==null||pauseIntent||restarting||shuttingDown)return;
            var result=runtime.TrySubmit(new GameCommand(1,generation,++sequence,"player-1","tank-1",type,x,z));
            notice=result.Accepted ? (type == FoundationCommandType.Move ? "Движение принято" : "Стоп принят") : "Команда отклонена: "+result.Status;Record(notice);
        }
        private void TogglePause(){SetPause(!pauseIntent);}
        private void SetPause(bool pause)
        {
            if(runtime==null||restarting)return;pauseIntent=pause;controls.WorldInputEnabled=!pause;runtime.RequestPause(pause);Record("pause="+pause);
        }
        private void Restart()
        {
            if(restarting||shuttingDown)return; if(runtime==null){ UnityEngine.SceneManagement.SceneManager.LoadScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex); return; } restarting=true;shutdownStarted=Time.realtimeSinceStartup;controls.WorldInputEnabled=false;runtime?.RequestStop();Record("restart requested");
        }
        private void Pan(Vector2 axis)
        {
            Record("pan="+axis);
            foreach(var camera in cameras)camera.transform.position+=new Vector3(axis.x,0,axis.y)*(float)profile.CameraPanSpeed*Time.unscaledDeltaTime;
        }
        private void Zoom(float scroll)
        {
            Record("zoom="+scroll);
            foreach(var camera in cameras)camera.orthographicSize=Mathf.Clamp(camera.orthographicSize-scroll*(float)profile.CameraZoomSpeed,(float)profile.CameraMinZoom,(float)profile.CameraMaxZoom);
        }
        private void Update()
        {
            if(runtime==null)return;
            if(restarting&&runtime.IsStopped){runtime.Dispose();StartSession();}
            var snapshot=runtime.Latest;
            if(snapshot==null)return;
            controls.WorldInputEnabled=!pauseIntent&&!restarting&&!shuttingDown&&string.IsNullOrEmpty(snapshot.Failure);
            tank.position=new Vector3((float)snapshot.Entity.X,0,(float)snapshot.Entity.Z);
            var props=new MaterialPropertyBlock();props.SetColor("_BaseColor",selected?new Color(.65f,.97f,.83f):new Color(.2f,.68f,.75f));body.SetPropertyBlock(props);
            foreach(var receipt in runtime.DrainReceipts()) { Record(receipt.ToString()); }
            status.text=(restarting && Time.realtimeSinceStartup-shutdownStarted>ShutdownWarningSeconds ? "Остановка симуляции задерживается. Новый матч ещё не запущен.\n" : "")+(pauseIntent?"Пауза":"Танк "+(snapshot.Entity.Moving?"движется":"ожидает"))+"\n"+notice+(string.IsNullOrEmpty(snapshot.Failure)?"":"\nОшибка: "+snapshot.Failure);
            if(evidencePath!=null)RunDiagnostic(snapshot);
        }
        private void RunDiagnostic(FoundationSnapshot snapshot)
        {
            float elapsed=Time.realtimeSinceStartup-startTime;
            if(elapsed>1){frames.Add(Time.unscaledDeltaTime*1000);if(snapshot.Tick!=measuredTick){ticks.Add(snapshot.LastTickMilliseconds);measuredTick=snapshot.Tick;}}
            if(elapsed>1&&smokeStage==0){selected=true;Submit(FoundationCommandType.Move,8,6);smokeStage=1;moveTick=snapshot.Tick;}
            if(elapsed>3&&smokeStage==1){movedX=snapshot.Entity.X;StopUnit();smokeStage=2;}
            if(elapsed>4&&smokeStage==2){SetPause(true);smokeStage=3;}
            // Capture a settled paused UI, not the same frame that changes the label.
            if(elapsed>4.5f&&smokeStage==3){ScreenCapture.CaptureScreenshot(Path.Combine(evidencePath,"native-"+cameraCount+"-camera.png"));smokeStage=4;}
            if(elapsed>5&&smokeStage==4){SetPause(false);smokeStage=5;}
            if(elapsed>6&&smokeStage==5){WriteEvidence(snapshot);smokeStage=6;runtime.RequestStop();shuttingDown=true;StartCoroutine(QuitWhenStopped());}
        }
        private void WriteEvidence(FoundationSnapshot snapshot)
        {
            var data=new Evidence{cameraCount=cameraCount,profile="unity-foundation-diagnostic-v1@1",source="ab1d42d5b1e8af104509efb6d204a4c310be00f1 + foundation working tree",unity=Application.unityVersion,device=SystemInfo.processorType,gpu=SystemInfo.graphicsDeviceName,width=Screen.width,height=Screen.height,tick=snapshot.Tick,moveStartTick=moveTick,movedX=movedX,frameMs=frames.ToArray(),tickMs=ticks.ToArray(),actions=actions.ToArray(),allocatedBytes=GC.GetTotalMemory(false),gen0Collections=GC.CollectionCount(0),failure=snapshot.Failure};
            File.WriteAllText(Path.Combine(evidencePath,"native-"+cameraCount+"-camera.json"),JsonUtility.ToJson(data,true));
            Debug.Log("FOUNDATION_NATIVE_DIAGNOSTIC_COMPLETE cameras="+cameraCount+" tick="+snapshot.Tick+" movedX="+movedX);
        }
        [Serializable]private sealed class Evidence
        {
            public int cameraCount,width,height,gen0Collections;public string profile,source,unity,device,gpu,failure;public long tick,moveStartTick,allocatedBytes;public double movedX;public float[] frameMs;public double[] tickMs;public string[] actions;
        }
        private IEnumerator QuitWhenStopped(){while(runtime!=null&&!runtime.IsStopped){ if(status!=null && Time.realtimeSinceStartup-shutdownStarted>ShutdownWarningSeconds) status.text="Ожидание остановки симуляции…"; yield return null; }Application.Quit();}
        private void OnDestroy(){runtime?.RequestStop();}
        private void OnApplicationQuit(){runtime?.RequestStop();}
    }
}
