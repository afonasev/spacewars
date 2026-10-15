using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;

namespace Spacewars.Presentation
{
    // Release-only main menu. The launcher owns downloading, trust and activation.
    public sealed class NativeMainMenu : MonoBehaviour
    {
        [Serializable] private sealed class UpdateState { public string version,available,state,error,notice; public long done,total; }
        public Action Play, Laboratory;
        public VisualElement HostRoot;
        public NativeMenuNavigation Navigation;
        public VisualElement ScreenRoot { get; private set; }
        private bool ownsNavigation;
        private VisualElement card;
        private string endpoint,token;
        private Label stateLabel,versionLabel;
        private Button playButton,updateButton,restartButton;
        private UpdateState current;
        private PanelSettings panel;
        public NativeLocalControlSettings LocalControls;
        private bool busy;
        private string requestError,statusError;
        private ProgressBar updateProgress;
        private VisualElement checkingSpinner,updatePanel;
        private void Start()
        {
            endpoint=Environment.GetEnvironmentVariable("SPACEWARS_UPDATE_ENDPOINT");token=Environment.GetEnvironmentVariable("SPACEWARS_UPDATE_TOKEN");
            VisualElement root=HostRoot;
            if(root==null){panel=ScriptableObject.CreateInstance<PanelSettings>();panel.themeStyleSheet=Resources.Load<ThemeStyleSheet>("FoundationTheme");panel.scaleMode=PanelScaleMode.ScaleWithScreenSize;panel.referenceResolution=new Vector2Int(1280,800);var doc=gameObject.AddComponent<UIDocument>();doc.panelSettings=panel;root=doc.rootVisualElement;OrbitalTheme.ConfigurePanel(panel,root);}
            OrbitalTheme.Install(root);ScreenRoot=OrbitalTheme.Screen(root,"native-main-menu",true);
            if(Navigation==null){Navigation=new NativeMenuNavigation(root);ownsNavigation=true;}
            card=new VisualElement();card.AddToClassList("orbital-main-stack");ScreenRoot.Add(card);
            card.Add(OrbitalTheme.Text("SPACEWARS","orbital-logo"));
            playButton=OrbitalTheme.Action("В бой!",()=>StartCoroutine(EnterGame()),"main-play",true);card.Add(playButton);
            var network=OrbitalTheme.Action("Сетевая игра",()=>{},"main-network");network.SetEnabled(false);network.tooltip="Пока недоступно";card.Add(network);
            var settings=OrbitalTheme.Action("Настройки",()=>OpenSettings(),"main-settings");card.Add(settings);
            card.Add(OrbitalTheme.Action("Управление",()=>{ScreenRoot.style.display=DisplayStyle.None;NativeControlsHelp.Open(HostRoot??GetComponent<UIDocument>().rootVisualElement,Navigation,()=>RestoreFocus(card.Q<Button>("main-controls")));},"main-controls"));
            card.Add(OrbitalTheme.Action("Лаборатория геймдизайна",()=>Laboratory?.Invoke(),"main-laboratory"));
            card.Add(OrbitalTheme.Action("Выйти",()=>Application.Quit(),"main-exit"));
            var footer=updatePanel=new VisualElement {name="main-update-panel"};footer.AddToClassList("orbital-menu-footer");card.Add(footer);
            versionLabel=OrbitalTheme.Text("v"+Application.version,"orbital-muted");footer.Add(versionLabel);
            stateLabel=OrbitalTheme.Text(string.IsNullOrEmpty(endpoint)?"Проверка обновлений доступна в установленной игре.":"Проверяем наличие обновления…","orbital-muted");stateLabel.name="main-update-status";stateLabel.style.whiteSpace=WhiteSpace.Normal;footer.Add(stateLabel);
            checkingSpinner=new VisualElement {name="main-update-spinner",pickingMode=PickingMode.Ignore};
            checkingSpinner.style.width=checkingSpinner.style.height=18;checkingSpinner.style.marginTop=6;
            checkingSpinner.generateVisualContent+=ctx=>{var painter=ctx.painter2D;painter.strokeColor=new Color(.3f,.8f,.85f);painter.lineWidth=2;painter.BeginPath();float angle=Time.realtimeSinceStartup*240;painter.Arc(new Vector2(9,9),7,angle,angle+270);painter.Stroke();};
            checkingSpinner.schedule.Execute(()=>checkingSpinner.MarkDirtyRepaint()).Every(33);
            checkingSpinner.style.display=string.IsNullOrEmpty(endpoint)?DisplayStyle.None:DisplayStyle.Flex;footer.Add(checkingSpinner);
            updateProgress=new ProgressBar {name="main-update-progress",lowValue=0,highValue=100};updateProgress.style.display=DisplayStyle.None;footer.Add(updateProgress);
            var updateActions=new VisualElement();updateActions.AddToClassList("orbital-toolbar");footer.Add(updateActions);
            updateButton=OrbitalTheme.Action("Обновить",()=>StartCoroutine(UpdateGame()),"main-update");updateActions.Add(updateButton);updateButton.style.display=DisplayStyle.None;
            restartButton=OrbitalTheme.Action("Перезапустить",()=>StartCoroutine(RestartGame()),"main-restart");updateActions.Add(restartButton);restartButton.style.display=DisplayStyle.None;
            ScreenRoot.RegisterCallback<GeometryChangedEvent>(_=>{
                bool small=ScreenRoot.resolvedStyle.width<850;card.style.left=Length.Percent(small?8:53);card.style.width=Length.Percent(small?84:40);
                bool compact=ScreenRoot.resolvedStyle.height<650;
                card.style.top=Length.Percent(compact?3:10);
                foreach(var element in card.Children())if(element is Button action){action.style.height=compact?42:46;action.style.fontSize=compact?17:19;}
                var logo=card.Q<Label>(className:"orbital-logo");logo.style.fontSize=ScreenRoot.resolvedStyle.height<650?36:52;
            });
            updateButton.SetEnabled(false);RestoreFocus(playButton);
            if(!string.IsNullOrEmpty(endpoint))StartCoroutine(Poll());
        }
        public void RestoreFocus(VisualElement preferred=null)
        {ScreenRoot.style.display=DisplayStyle.Flex;Navigation.SetScope(ScreenRoot,null,preferred??playButton);}
        private void OpenSettings()
        {
            ScreenRoot.style.display=DisplayStyle.None;
            NativeSettingsView.Open(HostRoot??GetComponent<UIDocument>().rootVisualElement,Navigation,()=>RestoreFocus(card.Q<Button>("main-settings")),LocalControls);
        }
        private void Update(){if(ownsNavigation)Navigation?.Tick();}
        private void LateUpdate(){if(ownsNavigation)UnityEngine.Cursor.visible=!Application.isFocused||Navigation?.UsingGamepad!=true;}
        private void OnDisable(){if(ownsNavigation)UnityEngine.Cursor.visible=true;}
        private IEnumerator Request(string path,string method,Action<UpdateState> done)
        {
            using(var r=new UnityWebRequest(endpoint+path,method))
            {
                r.downloadHandler=new DownloadHandlerBuffer();r.timeout=15;r.SetRequestHeader("Authorization","Bearer "+token);
                yield return r.SendWebRequest();
                UpdateState state=null;string error=null;
                if(r.result!=UnityWebRequest.Result.Success)error=r.error;
                else
                {
                    try{state=JsonUtility.FromJson<UpdateState>(r.downloadHandler.text);}
                    catch(ArgumentException){error="Некорректный ответ службы обновления";}
                    if(state==null||string.IsNullOrEmpty(state.state)){error="Не удалось получить статус обновления";state=null;}
                }
                if(error!=null)
                {
                    var message="Не удалось получить статус обновления ("+error+"). Проверьте подключение. Установленная игра доступна.";
                    if(path=="/status"||path=="/ready")statusError=message;else requestError=message;
                    updatePanel.style.display=DisplayStyle.Flex;checkingSpinner.style.display=DisplayStyle.None;stateLabel.text=message;done?.Invoke(null);yield break;
                }
                if(path=="/status"||path=="/ready")statusError=null;
                done?.Invoke(state);
            }
        }
        private IEnumerator Poll()
        {
            yield return Request("/ready","POST",s=>current=s);
            while(true){if(busy){yield return new WaitForSecondsRealtime(.1f);continue;}yield return Request("/status","GET",s=>{if(s!=null&&!busy){if(current==null||s.state!=current.state)requestError=null;current=s;ShowStatus();}});yield return new WaitForSecondsRealtime(.5f);}
        }
        private void ShowStatus()
        {
            if(current==null)return;
            updatePanel.style.display=current.state=="current"&&string.IsNullOrEmpty(requestError)&&string.IsNullOrEmpty(statusError)?DisplayStyle.None:DisplayStyle.Flex;
            versionLabel.text="v"+current.version;var applying=current.state=="applying";
            playButton.SetEnabled(!applying&&!busy);restartButton.style.display=current.state=="prepared"?DisplayStyle.Flex:DisplayStyle.None;updateButton.SetEnabled(!busy&&(current.state=="available"||current.state=="error"&&!string.IsNullOrEmpty(current.available)));
            bool candidate=current.state=="available"||current.state=="error"&&!string.IsNullOrEmpty(current.available);
            updateButton.style.display=candidate?DisplayStyle.Flex:DisplayStyle.None;
            checkingSpinner.style.display=current.state=="checking"&&string.IsNullOrEmpty(requestError)&&string.IsNullOrEmpty(statusError)?DisplayStyle.Flex:DisplayStyle.None;
            bool downloading=current.state=="downloading";
            updateProgress.style.display=downloading?DisplayStyle.Flex:DisplayStyle.None;
            updateProgress.value=current.total>0?Mathf.Clamp((float)(100d*current.done/current.total),0,100):0;
            updateProgress.title=current.total>0?Math.Round(updateProgress.value)+"%":"Подготовка…";
            switch(current.state){case "available":stateLabel.text="Доступна версия "+current.available+". Нажмите «Обновить», чтобы скачать изменения.";break;case "downloading":stateLabel.text=current.total<=0?"Подготовка файлов к обновлению…":current.done>=current.total?"Загрузка завершена. Проверяем обновление…":"Загрузка и подготовка: "+Math.Round(100d*current.done/current.total)+"% ("+(current.done/1048576d).ToString("F1")+" / "+(current.total/1048576d).ToString("F1")+" МБ). Неизменённые части используются повторно.";break;case "checking":stateLabel.text="Проверяем наличие обновления…";break;case "prepared":stateLabel.text="Обновление готово. Можно продолжить играть; новая версия установится после выхода. Или нажмите «Перезапустить».";break;case "current":stateLabel.text="Установлена актуальная версия.";break;case "offline":stateLabel.text="Нет подключения к службе обновления. Можно играть в установленную версию.";break;case "error":stateLabel.text="Обновление не удалось. Рабочая версия сохранена. Можно повторить. "+current.error;break;case "applying":stateLabel.text="Устанавливаем обновление…";break;}
            if(!string.IsNullOrEmpty(requestError))stateLabel.text=requestError;
            if(!string.IsNullOrEmpty(statusError))stateLabel.text=statusError;
            if(!string.IsNullOrEmpty(current.notice))stateLabel.text=current.notice+" "+stateLabel.text;
        }
        private IEnumerator EnterGame()
        {
            if(busy)yield break;busy=true;playButton.SetEnabled(false);
            bool allowed=string.IsNullOrEmpty(endpoint);if(!allowed)yield return Request("/play","POST",s=>allowed=s!=null);
            if(allowed){var action=Play;ScreenRoot.style.display=DisplayStyle.None;Navigation.SetScope(null);action?.Invoke();Destroy(gameObject);}else{busy=false;playButton.SetEnabled(true);}
        }
        private IEnumerator UpdateGame()
        {
            if(busy||current==null)yield break;requestError=null;busy=true;ShowStatus();stateLabel.text="Начинаем скачивание обновления…";
            yield return Request("/update","POST",s=>{if(s!=null)current=s;});busy=false;ShowStatus();
        }
        private IEnumerator RestartGame()
        {
            if(busy||current==null||current.state!="prepared")yield break;busy=true;
            bool accepted=false;yield return Request("/commit","POST",s=>accepted=s!=null);
            if(accepted)Application.Quit();else{busy=false;ShowStatus();}
        }
        private void OnDestroy(){ScreenRoot?.RemoveFromHierarchy();if(ownsNavigation)Navigation?.Dispose();if(panel)Destroy(panel);}
    }
}
