using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;

namespace Spacewars.Presentation
{
    // The host owns the job. This small observer survives menu destruction.
    public sealed class NativeUpdateProgress : MonoBehaviour
    {
        [Serializable] private sealed class State { public string state; public long done,total; }
        private static NativeUpdateProgress instance;
        private string endpoint,token;
        private PanelSettings panel;
        public static void Show(string endpoint,string token)
        {
            if(instance!=null||string.IsNullOrEmpty(endpoint))return;
            instance=new GameObject("Update progress").AddComponent<NativeUpdateProgress>();instance.endpoint=endpoint;instance.token=token;DontDestroyOnLoad(instance.gameObject);
        }
        private IEnumerator Start()
        {
            panel=ScriptableObject.CreateInstance<PanelSettings>();panel.themeStyleSheet=Resources.Load<ThemeStyleSheet>("FoundationTheme");panel.sortingOrder=100;
            var doc=gameObject.AddComponent<UIDocument>();doc.panelSettings=panel;var root=doc.rootVisualElement;root.pickingMode=PickingMode.Ignore;
            var label=new Label();label.pickingMode=PickingMode.Ignore;label.style.position=Position.Absolute;label.style.right=16;label.style.bottom=12;label.style.paddingLeft=12;label.style.paddingRight=12;label.style.paddingTop=8;label.style.paddingBottom=8;label.style.backgroundColor=new Color(.025f,.044f,.065f,.95f);label.style.color=new Color(.6f,.92f,1);label.style.unityFont=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");root.Add(label);
            while(true)
            {
                using(var r=UnityWebRequest.Get(endpoint+"/status"))
                {
                    r.timeout=10;r.SetRequestHeader("Authorization","Bearer "+token);yield return r.SendWebRequest();
                    if(r.result==UnityWebRequest.Result.Success)
                    {
                        var s=JsonUtility.FromJson<State>(r.downloadHandler.text);label.style.display=s.state=="downloading"||s.state=="prepared"?DisplayStyle.Flex:DisplayStyle.None;
                        label.text=s.state=="prepared"?"Обновление готово · установится после выхода":"Обновление · "+(s.total>0?Math.Round(100d*s.done/s.total):0)+"%";
                    }
                }
                yield return new WaitForSecondsRealtime(1);
            }
        }
        private void OnDestroy(){if(panel)Destroy(panel);if(instance==this)instance=null;}
    }
}
