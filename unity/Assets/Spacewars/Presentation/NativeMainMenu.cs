using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;

namespace Spacewars.Presentation
{
    // The launcher owns update UI, downloading, trust and activation. Only hidden startup admission remains here.
    public sealed class NativeMainMenu : MonoBehaviour
    {
        [Serializable] private sealed class AdmissionState { public string state; }
        public Action Play, Laboratory, Campaign;
        public UnityEngine.InputSystem.Gamepad EntryGamepad {get;private set;}
        public VisualElement HostRoot;
        public NativeMenuNavigation Navigation;
        public VisualElement ScreenRoot { get; private set; }
        private bool ownsNavigation;
        private VisualElement card;
        private string endpoint,token;
        private Label launchStatus;
        private Button playButton;
        private PanelSettings panel;
        public NativeLocalControlSettings LocalControls;
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
            card.Add(OrbitalTheme.Action("Кампания",OpenCampaign,"main-campaign"));
            var network=OrbitalTheme.Action("Сетевая игра",()=>{},"main-network");network.SetEnabled(false);network.style.display=DisplayStyle.None;network.tooltip="Пока недоступно";card.Add(network);
            var settings=OrbitalTheme.Action("Настройки",()=>OpenSettings(),"main-settings");card.Add(settings);
            card.Add(OrbitalTheme.Action("Лаборатория геймдизайна",()=>Laboratory?.Invoke(),"main-laboratory"));
            card.Add(OrbitalTheme.Action("Выйти",()=>Application.Quit(),"main-exit"));
            launchStatus=OrbitalTheme.Text("","orbital-muted");launchStatus.name="main-launch-status";launchStatus.style.whiteSpace=WhiteSpace.Normal;launchStatus.style.display=DisplayStyle.None;card.Add(launchStatus);
            ScreenRoot.RegisterCallback<GeometryChangedEvent>(_=>{
                bool small=ScreenRoot.resolvedStyle.width<850;card.style.left=Length.Percent(small?8:53);card.style.width=Length.Percent(small?84:40);
                bool compact=ScreenRoot.resolvedStyle.height<650;
                card.style.top=Length.Percent(compact?3:10);
                foreach(var element in card.Children())if(element is Button action){action.style.height=compact?42:46;action.style.fontSize=compact?17:19;}
                var logo=card.Q<Label>(className:"orbital-logo");logo.style.fontSize=ScreenRoot.resolvedStyle.height<650?36:52;
            });
            RestoreFocus(playButton);
            if(!string.IsNullOrEmpty(endpoint))StartCoroutine(RequestAdmission("/ready",null));
        }
        private void OpenCampaign()
        {
            ScreenRoot.style.display=DisplayStyle.None;
            var host=HostRoot??GetComponent<UIDocument>().rootVisualElement;
            var page=OrbitalTheme.Screen(host,"campaign-menu",true);var content=OrbitalTheme.Card(page,"campaign-card");
            content.Add(OrbitalTheme.Text("КАМПАНИЯ","orbital-title"));
            var list=new VisualElement();content.Add(list);
            var brief=new VisualElement{name="campaign-briefing"};brief.style.display=DisplayStyle.None;content.Add(brief);
            list.Add(OrbitalTheme.Action("Спорная территория · "+(PlayerPrefs.GetInt("campaign.disputed-territory.completed",0)==1?"пройдена":"не пройдена"),()=>{list.style.display=DisplayStyle.None;brief.style.display=DisplayStyle.Flex;Navigation.SetScope(page,Back,brief.Q<Button>("campaign-start"));},"campaign-mission-1",true));
            brief.Add(OrbitalTheme.Text("Спорная территория","orbital-title"));var description=OrbitalTheme.Text(Spacewars.Simulation.CampaignReconOpening.Briefing);description.style.whiteSpace=WhiteSpace.Normal;brief.Add(description);
            brief.Add(OrbitalTheme.Action("Начать миссию",()=>StartCoroutine(EnterGame(()=>{page.RemoveFromHierarchy();Campaign?.Invoke();})),"campaign-start",true));
            void Back(){page.RemoveFromHierarchy();RestoreFocus(card.Q<Button>("main-campaign"));}
            content.Add(OrbitalTheme.Action("Назад",Back,"campaign-back"));Navigation.SetScope(page,Back,list.Q<Button>());
        }
        public void RestoreFocus(VisualElement preferred=null)
        {ScreenRoot.style.display=DisplayStyle.Flex;Navigation.SetScope(ScreenRoot,null,preferred??playButton);}
        private void OpenSettings()
        {
            ScreenRoot.style.display=DisplayStyle.None;
            NativeSettingsView.Open(HostRoot??GetComponent<UIDocument>().rootVisualElement,Navigation,()=>RestoreFocus(card.Q<Button>("main-settings")),LocalControls,closed=>StartCoroutine(OpenHistory(closed)));
        }
        private void Update(){if(ownsNavigation)Navigation?.Tick();}
        private void LateUpdate(){if(ownsNavigation)UnityEngine.Cursor.visible=!Application.isFocused||Navigation?.UsingGamepad!=true;}
        private void OnDisable(){if(ownsNavigation)UnityEngine.Cursor.visible=true;}
        private IEnumerator RequestAdmission(string path,Action<bool> done)
        {
            using(var request=new UnityWebRequest(endpoint+path,"POST"))
            {
                request.downloadHandler=new DownloadHandlerBuffer();request.timeout=15;request.SetRequestHeader("Authorization","Bearer "+token);
                yield return request.SendWebRequest();
                bool allowed=false;
                if(request.result==UnityWebRequest.Result.Success)
                {
                    try{allowed=!string.IsNullOrEmpty(JsonUtility.FromJson<AdmissionState>(request.downloadHandler.text)?.state);}
                    catch(ArgumentException){}
                }
                done?.Invoke(allowed);
            }
        }
        private IEnumerator EnterGame(Action route=null)
        {
            if(busy)yield break;EntryGamepad=Navigation.UsingGamepad?Navigation.CurrentGamepad:null;busy=true;playButton.SetEnabled(false);launchStatus.style.display=DisplayStyle.None;
            bool allowed=string.IsNullOrEmpty(endpoint);if(!allowed)yield return RequestAdmission("/play",s=>allowed=s);
            if(allowed){var action=route??Play;ScreenRoot.style.display=DisplayStyle.None;Navigation.SetScope(null);action?.Invoke();Destroy(gameObject);}else{busy=false;playButton.SetEnabled(true);launchStatus.text="Не удалось начать игру. Попробуйте ещё раз.";launchStatus.style.display=DisplayStyle.Flex;}
        }
        private NativeReleaseHistoryView.History historyCache;
        private NativeReleaseHistoryView historyView;
        private UnityWebRequest historyRequest;
        private IEnumerator OpenHistory(Action closed)
        {
            var root=HostRoot??GetComponent<UIDocument>().rootVisualElement;
            ScreenRoot.style.display=DisplayStyle.None;
            var view=historyView=new NativeReleaseHistoryView(root,Navigation,()=>{historyRequest?.Abort();closed();},routine=>StartCoroutine(routine),endpoint,token);
            if(historyCache==null&&!string.IsNullOrEmpty(endpoint))
            {
                using(var request=UnityWebRequest.Get(endpoint+"/history"))
                {
                    historyRequest=request;request.timeout=30;request.SetRequestHeader("Authorization","Bearer "+token);
                    yield return request.SendWebRequest();historyRequest=null;
                    if(request.result==UnityWebRequest.Result.Success)
                    {try{historyCache=JsonUtility.FromJson<NativeReleaseHistoryView.History>(request.downloadHandler.text);}catch(ArgumentException){}}
                }
            }
            if(!view.IsClosed)view.SetHistory(historyCache);
        }
        private void OnDestroy(){historyRequest?.Abort();historyView?.Dispose();ScreenRoot?.RemoveFromHierarchy();if(ownsNavigation)Navigation?.Dispose();if(panel)Destroy(panel);}
    }
}
