using System;
using System.Collections;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using NUnit.Framework;
using Spacewars.Presentation;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

public sealed class ReleaseHistoryCoroutineHost : MonoBehaviour { }
public sealed class ReleaseHistoryMediaTests
{
    private GameObject host;
    private PanelSettings panel;
    private RenderTexture target;
    private NativeMenuNavigation navigation;
    private NativeReleaseHistoryView view;
    private VisualElement root;
    private HttpListener listener;
    private Thread server;
    private byte[] image;
    private string endpoint;
    private volatile bool fail,delay;
    private int calls;
    private readonly ManualResetEvent release=new ManualResetEvent(false);
    private Action restorePreferences;

    [SetUp] public void Setup()
    {
        calls=0;fail=false;delay=false;release.Reset();
        restorePreferences=NativePreferenceWriter.Capture();NativeDevicePreferences.UiScale=1;
        host=new GameObject("Release history fixture");var runner=host.AddComponent<ReleaseHistoryCoroutineHost>();
        panel=ScriptableObject.CreateInstance<PanelSettings>();panel.themeStyleSheet=Resources.Load<ThemeStyleSheet>("FoundationTheme");
        target=new RenderTexture(1280,800,24);target.Create();panel.targetTexture=target;
        var document=host.AddComponent<UIDocument>();document.panelSettings=panel;root=document.rootVisualElement;OrbitalTheme.ConfigurePanel(panel,root);
        navigation=new NativeMenuNavigation(root);
        var path=Environment.GetEnvironmentVariable("SPACEWARS_HISTORY_FIXTURE_IMAGE");
        if(!string.IsNullOrEmpty(path))image=File.ReadAllBytes(path);
        else {var texture=new Texture2D(320,160);image=texture.EncodeToPNG();UnityEngine.Object.DestroyImmediate(texture);}
        var socket=new TcpListener(IPAddress.Loopback,0);socket.Start();var port=((IPEndPoint)socket.LocalEndpoint).Port;socket.Stop();
        endpoint="http://127.0.0.1:"+port;listener=new HttpListener();listener.Prefixes.Add(endpoint+"/");listener.Start();
        server=new Thread(()=>
        {
            try {while(listener.IsListening)
            {
                var context=listener.GetContext();Interlocked.Increment(ref calls);
                if(delay)release.WaitOne(10000);
                context.Response.StatusCode=fail?404:200;
                if(!fail){context.Response.ContentType="image/png";context.Response.ContentLength64=image.Length;context.Response.OutputStream.Write(image,0,image.Length);}
                context.Response.Close();
            }}catch(HttpListenerException){}catch(ObjectDisposedException){}catch(IOException){}
        });server.IsBackground=true;server.Start();
        view=new NativeReleaseHistoryView(root,navigation,()=>navigation.SetScope(null),routine=>runner.StartCoroutine(routine),endpoint,"fixture-token");
    }
    [TearDown] public void Cleanup()
    {
        release.Set();view.Dispose();listener.Close();server.Join(1000);navigation.Dispose();
        UnityEngine.Object.DestroyImmediate(host);UnityEngine.Object.DestroyImmediate(panel);target.Release();UnityEngine.Object.DestroyImmediate(target);restorePreferences();PlayerPrefs.Save();release.Reset();
    }
    private NativeReleaseHistoryView.History Notes(bool textOnlyFirst=false)
    {
        var illustrated=new NativeReleaseHistoryView.ReleaseNote {version="9.8.7",date="Диагностическая запись",ru="Описание",blocks_ru=new[]{
            new NativeReleaseHistoryView.Block{kind="heading",text="Интерфейс"},
            new NativeReleaseHistoryView.Block{kind="item",text="История показывает иллюстрации рядом с изменениями."},
            new NativeReleaseHistoryView.Block{kind="image",text="Архивный кадр настроек · пример вложения",asset="notes-fixture.png"},
            new NativeReleaseHistoryView.Block{kind="item",text="Текстовые изменения сохраняются без изображения."},
            new NativeReleaseHistoryView.Block{kind="text",text=string.Join("\n",new[]{"Дополнительные изменения остаются читаемыми.","Запись 1", "Запись 2", "Запись 3", "Запись 4", "Запись 5", "Запись 6", "Запись 7", "Запись 8", "Запись 9", "Запись 10", "Запись 11"})}}};
        var old=new NativeReleaseHistoryView.ReleaseNote{version="1.0.0",ru="Старое текстовое описание."};
        return new NativeReleaseHistoryView.History{releases=textOnlyFirst?new[]{old,illustrated}:new[]{illustrated,old}};
    }
    private IEnumerator Until(Func<bool> condition)
    {
        float deadline=Time.realtimeSinceStartup+10;
        while(!condition()&&Time.realtimeSinceStartup<deadline)yield return null;
        Assert.IsTrue(condition(),"History fixture timed out");
        yield return null;yield return null;
    }
    private void Capture(string name)
    {
        var directory=Environment.GetEnvironmentVariable("SPACEWARS_HISTORY_CAPTURE");if(string.IsNullOrEmpty(directory))return;
        Directory.CreateDirectory(directory);var previous=RenderTexture.active;RenderTexture.active=target;
        var capture=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);capture.ReadPixels(new Rect(0,0,target.width,target.height),0,0);capture.Apply();
        File.WriteAllBytes(Path.Combine(directory,name+".png"),capture.EncodeToPNG());UnityEngine.Object.DestroyImmediate(capture);RenderTexture.active=previous;
    }
    [UnityTest] public IEnumerator PreviewEnlargesAndBackRestoresScrollFocusAndReleasesTexture()
    {
        view.SetHistory(Notes());var button=view.Page.Q<Button>("history-image-0");
        yield return Until(()=>button.enabledSelf);
        var preview=button.Q<Image>();Assert.NotNull(preview.image);Assert.AreEqual(ScaleMode.ScaleToFit,preview.scaleMode);
        var scroll=view.Page.Q<ScrollView>("history-scroll");yield return null;Capture("inline-1280x800");
        scroll.scrollOffset=new Vector2(0,20);yield return null;var offset=scroll.scrollOffset;var texture=preview.image;
        navigation.ActivateElement(button);yield return null;yield return null;
        Assert.NotNull(root.Q("history-enlarged"));Assert.AreSame(texture,root.Q<Image>("history-enlarged-image").image);Capture("enlarged-1280x800");
        navigation.Back();yield return null;
        Assert.IsNull(root.Q("history-enlarged"));Assert.AreSame(view.Page,navigation.Scope);Assert.AreSame(button,root.focusController.focusedElement);Assert.AreEqual(offset,scroll.scrollOffset);
        view.Page.Q<DropdownField>("history-version").index=1;yield return null;
        Assert.IsTrue(texture==null,"Selected-release texture is released");Assert.AreEqual(1,calls);
    }
    [UnityTest] public IEnumerator TextOnlyIsLazyAndFailedImageKeepsTextAndNavigation()
    {
        fail=true;view.SetHistory(Notes(true));yield return null;Assert.AreEqual(0,calls);Assert.IsNull(view.Page.Q("history-image-0"));
        Assert.IsTrue(view.Page.Q<ScrollView>("history-scroll").Query<Label>().ToList().Exists(l=>l.text.Contains("Старое текстовое")));
        view.Page.Q<DropdownField>("history-version").index=1;
        yield return Until(()=>view.Page.Q<Label>("history-image-status-0").text.Contains("недоступно"));
        Assert.IsTrue(view.Page.Q<ScrollView>("history-scroll").Query<Label>().ToList().Exists(l=>l.text.Contains("Текстовые изменения")));
        Assert.IsFalse(view.Page.Q<Button>("history-image-0").enabledSelf);Capture("unavailable-1280x800");
        navigation.Move(Vector2.down);Assert.AreSame(view.Page.Q<ScrollView>("history-scroll"),root.focusController.focusedElement);
        navigation.Move(Vector2.down);Assert.Greater(view.Page.Q<ScrollView>("history-scroll").scrollOffset.y,0);
        navigation.Back();Assert.IsTrue(view.IsClosed);Assert.IsNull(root.Q("release-history"));
    }
    [UnityTest] public IEnumerator SwitchingReleaseCancelsPendingImageWithoutStaleElements()
    {
        delay=true;view.SetHistory(Notes());yield return Until(()=>calls>0);
        view.Page.Q<DropdownField>("history-version").index=1;release.Set();yield return null;yield return null;yield return null;
        Assert.IsNull(view.Page.Q<Image>());Assert.IsNull(view.Page.Q("history-image-status-0"));Assert.IsFalse(view.IsClosed);
        view.Close();Assert.IsTrue(view.IsClosed);
    }
}
