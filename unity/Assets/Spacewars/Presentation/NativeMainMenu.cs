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
        private Label hints;
        private string endpoint,token;
        private Label stateLabel,versionLabel;
        private Button playButton,updateButton,checkButton,restartButton;
        private UpdateState current;
        private PanelSettings panel;
        private bool busy;
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
            card.Add(OrbitalTheme.Action("Лаборатория геймдизайна",()=>Laboratory?.Invoke(),"main-laboratory"));
            updateButton=OrbitalTheme.Action("Обновить",()=>StartCoroutine(UpdateGame()),"main-update");card.Add(updateButton);
            card.Add(OrbitalTheme.Action("Выйти",()=>Application.Quit(),"main-exit"));
            var footer=new VisualElement();footer.AddToClassList("orbital-menu-footer");card.Add(footer);
            versionLabel=OrbitalTheme.Text("Версия "+Application.version,"orbital-muted");footer.Add(versionLabel);
            stateLabel=OrbitalTheme.Text(string.IsNullOrEmpty(endpoint)?"Проверка обновлений доступна в установленной игре.":"Проверяем наличие обновления…","orbital-muted");footer.Add(stateLabel);
            var updateActions=new VisualElement();updateActions.AddToClassList("orbital-toolbar");footer.Add(updateActions);
            checkButton=OrbitalTheme.Action("Проверить",()=>StartCoroutine(CheckGame()),"main-check");updateActions.Add(checkButton);checkButton.SetEnabled(!string.IsNullOrEmpty(endpoint));checkButton.style.display=string.IsNullOrEmpty(endpoint)?DisplayStyle.None:DisplayStyle.Flex;
            restartButton=OrbitalTheme.Action("Перезапустить",()=>StartCoroutine(RestartGame()),"main-restart");updateActions.Add(restartButton);restartButton.style.display=DisplayStyle.None;
            hints=OrbitalTheme.Text("","orbital-hints");footer.Add(hints);
            ScreenRoot.RegisterCallback<GeometryChangedEvent>(_=>{
                bool small=ScreenRoot.resolvedStyle.width<850;card.style.left=Length.Percent(small?8:53);card.style.width=Length.Percent(small?84:40);
                card.style.top=Length.Percent(ScreenRoot.resolvedStyle.height<650?3:10);
                var logo=card.Q<Label>(className:"orbital-logo");logo.style.fontSize=ScreenRoot.resolvedStyle.height<650?36:52;
            });
            updateButton.SetEnabled(false);RestoreFocus(playButton);
            if(!string.IsNullOrEmpty(endpoint))StartCoroutine(Poll());
        }
        public void RestoreFocus(VisualElement preferred=null)
        {ScreenRoot.style.display=DisplayStyle.Flex;Navigation.SetScope(ScreenRoot,null,preferred??playButton,hints);}
        private void OpenSettings()
        {
            ScreenRoot.style.display=DisplayStyle.None;
            NativeSettingsView.Open(HostRoot??GetComponent<UIDocument>().rootVisualElement,Navigation,()=>RestoreFocus(card.Q<Button>("main-settings")));
        }
        private void Update(){if(ownsNavigation)Navigation?.Tick();}
        private IEnumerator Request(string path,string method,Action<UpdateState> done)
        {
            using(var r=new UnityWebRequest(endpoint+path,method)){r.downloadHandler=new DownloadHandlerBuffer();r.timeout=15;r.SetRequestHeader("Authorization","Bearer "+token);yield return r.SendWebRequest();if(r.result!=UnityWebRequest.Result.Success){stateLabel.text="Не удалось связаться с обновлением. Установленная игра доступна.";done?.Invoke(null);yield break;}var s=JsonUtility.FromJson<UpdateState>(r.downloadHandler.text);done?.Invoke(s);}
        }
        private IEnumerator Poll()
        {
            yield return Request("/ready","POST",s=>current=s);
            while(true){if(busy){yield return new WaitForSecondsRealtime(.1f);continue;}yield return Request("/status","GET",s=>{if(s!=null&&!busy){current=s;ShowStatus();}});yield return new WaitForSecondsRealtime(.5f);}
        }
        private void ShowStatus()
        {
            if(current==null)return;versionLabel.text="Версия "+current.version;var applying=current.state=="applying";
            playButton.SetEnabled(!applying&&!busy);checkButton.SetEnabled(!busy&&current.state!="checking"&&current.state!="downloading"&&current.state!="prepared"&&!applying);restartButton.style.display=current.state=="prepared"?DisplayStyle.Flex:DisplayStyle.None;updateButton.SetEnabled(!busy&&(current.state=="available"||current.state=="error"));
            switch(current.state){case "available":stateLabel.text="Доступна версия "+current.available+". Нажмите «Обновить», чтобы скачать изменения.";break;case "downloading":stateLabel.text="Загрузка: "+(current.total>0?Math.Round(100d*current.done/current.total):0)+"%. Неизменённые части используются повторно.";break;case "checking":stateLabel.text="Проверяем наличие обновления…";break;case "prepared":stateLabel.text="Обновление готово. Можно продолжить играть; новая версия установится после выхода. Или нажмите «Перезапустить».";break;case "current":stateLabel.text="Установлена актуальная версия.";break;case "offline":stateLabel.text="Проверка недоступна. Можно играть в установленную версию.";break;case "error":stateLabel.text="Обновление не удалось. Рабочая версия сохранена. Можно повторить.";break;case "applying":stateLabel.text="Устанавливаем обновление…";break;}
            if(!string.IsNullOrEmpty(current.notice))stateLabel.text=current.notice+" "+stateLabel.text;
        }
        private IEnumerator EnterGame()
        {
            if(busy)yield break;busy=true;playButton.SetEnabled(false);
            bool allowed=string.IsNullOrEmpty(endpoint);if(!allowed)yield return Request("/play","POST",s=>allowed=s!=null);
            if(allowed){var action=Play;ScreenRoot.style.display=DisplayStyle.None;Navigation.SetScope(null);action?.Invoke();Destroy(gameObject);}else{busy=false;playButton.SetEnabled(true);}
        }
        private IEnumerator CheckGame()
        {
            if(busy)yield break;busy=true;yield return Request("/check","POST",s=>{if(s!=null)current=s;});busy=false;ShowStatus();
        }
        private IEnumerator UpdateGame()
        {
            if(busy||current==null)yield break;busy=true;
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
