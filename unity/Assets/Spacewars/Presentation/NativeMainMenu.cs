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
        public Action Play;
        private string endpoint,token;
        private Label stateLabel,versionLabel;
        private Button playButton,updateButton,checkButton,restartButton;
        private UpdateState current;
        private PanelSettings panel;
        private bool busy;
        private void Start()
        {
            endpoint=Environment.GetEnvironmentVariable("SPACEWARS_UPDATE_ENDPOINT");token=Environment.GetEnvironmentVariable("SPACEWARS_UPDATE_TOKEN");
            panel=ScriptableObject.CreateInstance<PanelSettings>();panel.themeStyleSheet=Resources.Load<ThemeStyleSheet>("FoundationTheme");panel.scaleMode=PanelScaleMode.ScaleWithScreenSize;panel.referenceResolution=new Vector2Int(1280,800);
            var doc=gameObject.AddComponent<UIDocument>();doc.panelSettings=panel;var root=doc.rootVisualElement;
            root.style.backgroundColor=new Color(.025f,.044f,.065f);root.style.justifyContent=Justify.Center;root.style.alignItems=Align.Center;root.style.unityFont=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");root.style.color=new Color(.87f,.94f,.98f);
            var card=new VisualElement();card.style.width=500;card.style.maxWidth=Length.Percent(90);card.style.paddingTop=45;card.style.paddingBottom=45;card.style.paddingLeft=40;card.style.paddingRight=40;card.style.backgroundColor=new Color(.055f,.09f,.12f);root.Add(card);
            var eyebrow=new Label("ТЕРРИТОРИЯ РЕШАЕТ ВСЁ");eyebrow.style.fontSize=13;eyebrow.style.letterSpacing=3;eyebrow.style.color=new Color(.37f,.84f,.94f);card.Add(eyebrow);
            var title=new Label("SPACEWARS");title.style.fontSize=48;title.style.unityFontStyleAndWeight=FontStyle.Bold;title.style.marginTop=12;title.style.marginBottom=28;card.Add(title);
            playButton=ActionButton("В бой!",()=>StartCoroutine(EnterGame()));card.Add(playButton);
            updateButton=ActionButton("Обновить",()=>StartCoroutine(UpdateGame()));card.Add(updateButton);
            checkButton=ActionButton("Проверить",()=>StartCoroutine(CheckGame()));card.Add(checkButton);
            restartButton=ActionButton("Перезапустить",()=>StartCoroutine(RestartGame()));card.Add(restartButton);restartButton.style.display=DisplayStyle.None;
            card.Add(ActionButton("Выйти",()=>Application.Quit()));
            versionLabel=new Label("Версия "+Application.version);versionLabel.style.fontSize=13;versionLabel.style.marginTop=25;versionLabel.style.color=new Color(.54f,.65f,.72f);card.Add(versionLabel);
            stateLabel=new Label(string.IsNullOrEmpty(endpoint)?"Запустите игру через установленный Spacewars для проверки обновлений.":"Проверяем наличие обновления…");stateLabel.style.fontSize=15;stateLabel.style.marginTop=10;stateLabel.style.whiteSpace=WhiteSpace.Normal;card.Add(stateLabel);
            updateButton.SetEnabled(false);playButton.Focus();
            if(!string.IsNullOrEmpty(endpoint))StartCoroutine(Poll());
        }
        private Button ActionButton(string text,Action action)
        {
            var b=new Button(action){text=text};b.style.height=50;b.style.fontSize=20;b.style.marginBottom=10;b.style.backgroundColor=new Color(.08f,.18f,.23f);b.style.color=new Color(.82f,.96f,1);b.style.borderTopWidth=0;b.style.borderBottomWidth=0;b.style.borderLeftWidth=0;b.style.borderRightWidth=0;return b;
        }
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
            if(allowed){var action=Play;NativeUpdateProgress.Show(endpoint,token);GetComponent<UIDocument>().enabled=false;action?.Invoke();Destroy(gameObject);}else{busy=false;playButton.SetEnabled(true);}
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
        private void OnDestroy(){if(panel)Destroy(panel);}
    }
}
