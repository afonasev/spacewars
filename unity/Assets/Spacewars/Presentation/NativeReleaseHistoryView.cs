using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;

namespace Spacewars.Presentation
{
    // Presentation only. The authenticated updater owns release association and disk caching.
    public sealed class NativeReleaseHistoryView : IDisposable
    {
        [Serializable] public sealed class History { public ReleaseNote[] releases; }
        [Serializable] public sealed class ReleaseNote { public string version,date,ru,en; public Block[] blocks_ru,blocks_en; }
        [Serializable] public sealed class Block { public string kind,text,asset; }
        public VisualElement Page { get; }
        public bool IsClosed { get; private set; }
        private readonly VisualElement root;
        private readonly NativeMenuNavigation navigation;
        private readonly Action closed;
        private readonly Action<IEnumerator> run;
        private readonly string endpoint,token;
        private readonly DropdownField versions;
        private readonly ScrollView scroll;
        private readonly List<Texture2D> textures=new List<Texture2D>();
        private readonly List<UnityWebRequest> requests=new List<UnityWebRequest>();
        private History history;
        private VisualElement enlarged;
        private int generation;

        public NativeReleaseHistoryView(VisualElement root,NativeMenuNavigation navigation,Action closed,Action<IEnumerator> run,string endpoint,string token)
        {
            this.root=root;this.navigation=navigation;this.closed=closed;this.run=run;this.endpoint=endpoint;this.token=token;
            Page=OrbitalTheme.Screen(root,"release-history");
            var card=OrbitalTheme.Card(Page);card.style.width=960;card.style.maxWidth=Length.Percent(94);card.style.height=Length.Percent(94);card.style.paddingTop=card.style.paddingBottom=16;card.style.minHeight=0;
            card.Add(OrbitalTheme.Text("ЧТО НОВОГО","orbital-title"));
            versions=new DropdownField("Версия"){name="history-version"};card.Add(versions);
            scroll=OrbitalTheme.Scroll();scroll.name="history-scroll";scroll.focusable=true;scroll.style.flexGrow=1;scroll.style.flexShrink=1;scroll.style.minHeight=0;card.Add(scroll);
            AddText("Получаем историю версий…");versions.SetEnabled(false);
            card.Add(OrbitalTheme.Action("Назад",Close,"history-back"));
            versions.RegisterValueChangedCallback(_=>Show());
            Page.RegisterCallback<DetachFromPanelEvent>(_=>Dispose());
            navigation.SetScope(Page,Close,versions);
        }
        private Label AddText(string value,string style="orbital-muted")
        {
            var label=OrbitalTheme.Text(value??"",style);label.enableRichText=false;label.style.whiteSpace=WhiteSpace.Normal;label.style.flexShrink=0;label.style.marginBottom=8;scroll.Add(label);return label;
        }
        public void SetHistory(History value)
        {
            if(IsClosed)return;
            history=value;
            if(history?.releases==null||history.releases.Length==0)
            {scroll.Clear();AddText("История версий недоступна. Можно продолжить играть; попробуйте открыть её позже.");return;}
            versions.choices=new List<string>();foreach(var note in history.releases)versions.choices.Add(note.version+(note.version==Application.version?" · установлена":""));
            versions.SetEnabled(true);versions.SetValueWithoutNotify(versions.choices[0]);Show();navigation.SetScope(Page,Close,versions);
        }
        private void ClearMedia()
        {
            generation++;
            foreach(var request in requests)request.Abort();requests.Clear();
            foreach(var texture in textures)if(texture!=null)UnityEngine.Object.Destroy(texture);textures.Clear();
        }
        private void Show()
        {
            ClearMedia();scroll.Clear();scroll.scrollOffset=Vector2.zero;
            var note=history.releases[Mathf.Max(0,versions.index)];
            AddText("Версия "+note.version+" · "+(string.IsNullOrEmpty(note.date)?"дата не указана":note.date));
            var blocks=note.blocks_ru;
            if(blocks==null||blocks.Length==0)
            {AddText(string.IsNullOrWhiteSpace(note.ru)?"Описание на русском для этого исторического релиза отсутствует.":note.ru);return;}
            var images=new List<(Block block,Button button,Label status)>();
            foreach(var block in blocks)
            {
                if(block.kind!="image")
                {
                    var label=AddText((block.kind=="item"?"• ":"")+block.text,block.kind=="heading"?"orbital-subtitle":"orbital-body");
                    if(block.kind=="heading"){label.style.fontSize=23;label.style.marginTop=12;}
                    continue;
                }
                var button=OrbitalTheme.Action("",()=>{},"history-image-"+images.Count);button.style.height=StyleKeyword.Auto;button.style.minHeight=0;button.style.flexShrink=0;button.style.paddingTop=button.style.paddingBottom=6;scroll.Add(button);button.SetEnabled(false);
                var status=AddText((block.text??"")+" · Загружаем изображение…");status.name="history-image-status-"+images.Count;
                images.Add((block,button,status));
            }
            if(images.Count>0)run(LoadImages(note.version,images,generation));
        }
        private IEnumerator LoadImages(string version,List<(Block block,Button button,Label status)> images,int expectedGeneration)
        {
            foreach(var item in images)
            {
                if(IsClosed||expectedGeneration!=generation)yield break;
                if(string.IsNullOrEmpty(endpoint)){item.status.text=item.block.text+" · Изображение недоступно";continue;}
                var address=endpoint+"/history-image?version="+UnityWebRequest.EscapeURL(version)+"&asset="+UnityWebRequest.EscapeURL(item.block.asset??"");
                using(var request=UnityWebRequest.Get(address))
                {
                    request.timeout=30;request.SetRequestHeader("Authorization","Bearer "+token);requests.Add(request);
                    yield return request.SendWebRequest();requests.Remove(request);
                    if(IsClosed||expectedGeneration!=generation)yield break;
                    Texture2D texture=null;
                    if(request.result==UnityWebRequest.Result.Success)
                    {
                        var data=request.downloadHandler.data;
                        if(data!=null&&data.Length<=8*1024*1024)
                        {
                            texture=new Texture2D(2,2,TextureFormat.RGBA32,false);
                            if(!texture.LoadImage(data,true)){UnityEngine.Object.Destroy(texture);texture=null;}
                        }
                    }
                    if(texture==null){item.status.text=item.block.text+" · Изображение недоступно";continue;}
                    textures.Add(texture);
                    var image=new Image {image=texture,scaleMode=ScaleMode.ScaleToFit,pickingMode=PickingMode.Ignore};image.style.width=Length.Percent(100);image.style.flexShrink=0;item.button.Add(image);
                    void Resize(){float width=item.button.resolvedStyle.width-16;if(width>0)image.style.height=Mathf.Min(Mathf.Clamp(Page.resolvedStyle.height*.3f,140,280),width*texture.height/texture.width);}
                    item.button.RegisterCallback<GeometryChangedEvent>(_=>Resize());Resize();
                    item.button.tooltip=item.block.text+" · Увеличить";item.button.SetEnabled(true);
                    item.button.clicked+=()=>Enlarge(texture,item.block.text,item.button);
                    item.status.text=item.block.text+" · Нажмите, чтобы увеличить";
                }
            }
        }
        private void Enlarge(Texture2D texture,string caption,Button source)
        {
            if(enlarged!=null||IsClosed)return;
            var offset=scroll.scrollOffset;
            enlarged=OrbitalTheme.Screen(root,"history-enlarged");var card=OrbitalTheme.Card(enlarged);
            card.style.width=Length.Percent(94);card.style.height=Length.Percent(90);card.style.minHeight=0;
            var image=new Image {name="history-enlarged-image",image=texture,scaleMode=ScaleMode.ScaleToFit};image.style.flexGrow=1;image.style.minHeight=0;card.Add(image);
            var label=OrbitalTheme.Text(caption??"","orbital-muted");label.enableRichText=false;label.style.whiteSpace=WhiteSpace.Normal;card.Add(label);
            void Back(){enlarged.RemoveFromHierarchy();enlarged=null;navigation.SetScope(Page,Close,source);scroll.scrollOffset=offset;}
            var back=OrbitalTheme.Action("Назад",Back,"history-enlarged-back");card.Add(back);navigation.SetScope(enlarged,Back,back);
        }
        public void Close(){if(IsClosed)return;Dispose();Page.RemoveFromHierarchy();closed();}
        public void Dispose(){if(IsClosed)return;IsClosed=true;ClearMedia();enlarged?.RemoveFromHierarchy();enlarged=null;}
    }
}
