using System;
using System.Reflection;
using System.Collections;
using NUnit.Framework;
using Spacewars.Presentation;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

public sealed class NativeMainMenuTests
{
    private static IEnumerator Capture(NativeMainMenu menu,string name,int width=1280,int height=800,VisualElement screen=null)
    {
        string output=Environment.GetEnvironmentVariable("LOBBY_LAYOUT_EVIDENCE");if(string.IsNullOrEmpty(output))yield break;
        var panel=menu.GetComponent<UIDocument>().panelSettings;var texture=new RenderTexture(width,height,24);texture.Create();panel.targetTexture=texture;
        try
        {
            screen=screen??menu.ScreenRoot;
            // A fresh capture target needs every retained UI element repainted, even if its layout did not change.
            foreach(var element in screen.Query<VisualElement>().ToList())element.MarkDirtyRepaint();
            for(int i=0;i<5;i++)yield return null;
            var copyright=screen.Q<Label>("menu-copyright");
            Assert.AreEqual(DisplayStyle.Flex,copyright.resolvedStyle.display,name+" copyright display");
            Debug.Log(name+" copyright bounds "+copyright.worldBound+" screen "+screen.worldBound);
            Assert.Greater(copyright.worldBound.height,0,name+" copyright height");
            Assert.LessOrEqual(copyright.worldBound.yMax,screen.worldBound.yMax,name+" copyright viewport");
            var prior=RenderTexture.active;RenderTexture.active=texture;var png=new Texture2D(width,height,TextureFormat.RGB24,false);
            try{png.ReadPixels(new Rect(0,0,width,height),0,0);png.Apply();System.IO.Directory.CreateDirectory(output);System.IO.File.WriteAllBytes(System.IO.Path.Combine(output,name+".png"),png.EncodeToPNG());}
            finally{RenderTexture.active=prior;UnityEngine.Object.Destroy(png);}
        }
        finally{panel.targetTexture=null;texture.Release();UnityEngine.Object.Destroy(texture);}
    }
    [UnityTest] public IEnumerator ReleaseFooterRemainsReadableInStandardAndCompactWindows()
    {
        var saved=Environment.GetEnvironmentVariable("SPACEWARS_UPDATE_ENDPOINT");Environment.SetEnvironmentVariable("SPACEWARS_UPDATE_ENDPOINT",null);
        var go=new GameObject("release footer layout");var menu=go.AddComponent<NativeMainMenu>();yield return null;
        try
        {
            // Example metadata exercises the complete release caption without inventing a dev release date.
            menu.ScreenRoot.Q<Label>("menu-copyright").text=OrbitalTheme.FormatFooter("1.2.3","2026-10-13");
            foreach(var size in new[]{new Vector2Int(1280,800),new Vector2Int(800,620)})
                yield return Capture(menu,"release-footer-example-"+size.x+"x"+size.y,size.x,size.y);
        }
        finally{UnityEngine.Object.DestroyImmediate(go);Environment.SetEnvironmentVariable("SPACEWARS_UPDATE_ENDPOINT",saved);}
    }
    [UnityTest] public IEnumerator MenuHasNoUpdaterAndDoesNotStartMatch()
    {
        string endpoint=Environment.GetEnvironmentVariable("SPACEWARS_UPDATE_ENDPOINT");Environment.SetEnvironmentVariable("SPACEWARS_UPDATE_ENDPOINT",null);
        var go=new GameObject("main menu contract");var menu=go.AddComponent<NativeMainMenu>();bool played=false;menu.Play=()=>played=true;
        yield return null;
        try
        {
            var root=go.GetComponent<UIDocument>().rootVisualElement;
            var actions=root.Query<Button>().ToList();
            CollectionAssert.AreEqual(new[]{"В бой!","Кампания","Сетевая игра","Настройки","Лаборатория геймдизайна","Выйти"},actions.ConvertAll(b=>b.text));
            Assert.False(actions[2].enabledSelf);Assert.IsNull(root.Q(className:"orbital-hints"));
            foreach(string name in new[]{"main-update-panel","main-update-status","main-update-spinner","main-update-progress","main-update","main-cancel-update","main-restart","main-check"})Assert.IsNull(root.Q(name),name);
            Assert.False(root.Query<Label>().ToList().Exists(l=>l.text.IndexOf("обновлен",StringComparison.OrdinalIgnoreCase)>=0));
            Assert.False(root.Query<Label>().ToList().Exists(l=>l.text.Contains("ТЕРРИТОРИЯ")));
            Assert.That(played,Is.False,"Displaying the main menu must not start a match.");
            Assert.That(actions[0].enabledSelf,Is.True,"Offline installed gameplay remains available.");
            yield return Capture(menu,"launcher-only-main-menu");
        }
        finally{UnityEngine.Object.DestroyImmediate(go);Environment.SetEnvironmentVariable("SPACEWARS_UPDATE_ENDPOINT",endpoint);}
    }
    [UnityTest] public IEnumerator CampaignStartUsesInstalledPlayAdmissionAndKeepsBriefingOnRefusal()
    {
        var saved=Environment.GetEnvironmentVariable("SPACEWARS_UPDATE_ENDPOINT");Environment.SetEnvironmentVariable("SPACEWARS_UPDATE_ENDPOINT",null);
        var go=new GameObject("campaign installed play gate");var menu=go.AddComponent<NativeMainMenu>();int launched=0;menu.Campaign=()=>launched++;yield return null;
        var listener=new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback,0);listener.Start();string requested=null;
        var server=System.Threading.Tasks.Task.Run(()=>{using(var client=listener.AcceptTcpClient())using(var stream=client.GetStream()){var reader=new System.IO.StreamReader(stream);requested=reader.ReadLine();string line;while(!string.IsNullOrEmpty(line=reader.ReadLine())){}var bytes=System.Text.Encoding.ASCII.GetBytes("HTTP/1.1 409 Conflict\r\nContent-Length: 11\r\nConnection: close\r\n\r\nunavailable");stream.Write(bytes,0,bytes.Length);}});
        try
        {
            var root=go.GetComponent<UIDocument>().rootVisualElement;var field=typeof(NativeMainMenu).GetField("endpoint",BindingFlags.Instance|BindingFlags.NonPublic);field.SetValue(menu,"http://127.0.0.1:"+((System.Net.IPEndPoint)listener.LocalEndpoint).Port);
            menu.Navigation.ActivateElement(root.Q<Button>("main-campaign"));menu.Navigation.ActivateElement(root.Q<Button>("campaign-mission-1"));menu.Navigation.ActivateElement(root.Q<Button>("campaign-start"));
            double until=Time.realtimeSinceStartupAsDouble+5;while((!server.IsCompleted||(bool)typeof(NativeMainMenu).GetField("busy",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(menu))&&Time.realtimeSinceStartupAsDouble<until)yield return null;
            Assert.True(server.IsCompleted&&!server.IsFaulted,server.Exception?.ToString());StringAssert.StartsWith("POST /play ",requested);Assert.AreEqual(0,launched);Assert.IsNotNull(root.Q("campaign-briefing"),"denied launch retains the briefing for retry");
            field.SetValue(menu,null);menu.Navigation.ActivateElement(root.Q<Button>("campaign-start"));yield return null;Assert.AreEqual(1,launched);Assert.IsNull(root.Q("campaign-menu"));
        }
        finally{listener.Stop();UnityEngine.Object.DestroyImmediate(go);Environment.SetEnvironmentVariable("SPACEWARS_UPDATE_ENDPOINT",saved);}
    }




    [UnityTest] public IEnumerator LauncherReadinessDoesNotExposeUpdatesOrPollStatus()
    {
        var saved=Environment.GetEnvironmentVariable("SPACEWARS_UPDATE_ENDPOINT");var savedToken=Environment.GetEnvironmentVariable("SPACEWARS_UPDATE_TOKEN");
        var listener=new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback,0);listener.Start();
        Environment.SetEnvironmentVariable("SPACEWARS_UPDATE_ENDPOINT","http://127.0.0.1:"+((System.Net.IPEndPoint)listener.LocalEndpoint).Port);Environment.SetEnvironmentVariable("SPACEWARS_UPDATE_TOKEN","fixture-token");
        string first=null,authorization=null;
        var server=System.Threading.Tasks.Task.Run(()=>{using(var client=listener.AcceptTcpClient())using(var stream=client.GetStream()){
            var reader=new System.IO.StreamReader(stream);first=reader.ReadLine();string line;while(!string.IsNullOrEmpty(line=reader.ReadLine()))if(line.StartsWith("Authorization:"))authorization=line;
            var body="{\"state\":\"available\",\"available\":\"99.0\"}";var bytes=System.Text.Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: "+body.Length+"\r\nConnection: close\r\n\r\n"+body);stream.Write(bytes,0,bytes.Length);
        }});
        var go=new GameObject("launcher readiness without updater UI");var menu=go.AddComponent<NativeMainMenu>();
        try{
            double deadline=Time.realtimeSinceStartupAsDouble+15;while(!server.IsCompleted&&Time.realtimeSinceStartupAsDouble<deadline)yield return null;
            Assert.True(server.IsCompleted&&!server.IsFaulted,server.Exception?.ToString());StringAssert.StartsWith("POST /ready ",first);Assert.AreEqual("Authorization: Bearer fixture-token",authorization);
            yield return new WaitForSecondsRealtime(.65f);
            Assert.False(listener.Pending(),"Game must not poll /status after readiness.");Assert.IsNull(menu.ScreenRoot.Q("main-update-panel"));Assert.True(menu.ScreenRoot.Q<Button>("main-play").enabledSelf);
            Assert.False(menu.ScreenRoot.Query<Label>().ToList().Exists(l=>l.text.Contains("99.0")||l.text.IndexOf("обновлен",StringComparison.OrdinalIgnoreCase)>=0));
        }finally{listener.Stop();UnityEngine.Object.DestroyImmediate(go);Environment.SetEnvironmentVariable("SPACEWARS_UPDATE_ENDPOINT",saved);Environment.SetEnvironmentVariable("SPACEWARS_UPDATE_TOKEN",savedToken);}
    }

    [UnityTest] public IEnumerator HistoryLoadsFromSettingsAndRestoresSettingsFocus()
    {
        var saved=Environment.GetEnvironmentVariable("SPACEWARS_UPDATE_ENDPOINT");Environment.SetEnvironmentVariable("SPACEWARS_UPDATE_ENDPOINT",null);
        var go=new GameObject("release history HTTP UI fixture");var menu=go.AddComponent<NativeMainMenu>();yield return null;
        var listener=new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback,0);listener.Start();
        string installed=Application.version;
        var server=System.Threading.Tasks.Task.Run(()=>{
            using(var client=listener.AcceptTcpClient())using(var stream=client.GetStream())
            {
                var reader=new System.IO.StreamReader(stream);var first=reader.ReadLine();Assert.IsTrue(first.StartsWith("GET /history "),first);while(!string.IsNullOrEmpty(reader.ReadLine())){}
                var body="{\"releases\":[{\"version\":\"1.0.1\",\"date\":\"2026-10-15\",\"ru\":\"Улучшены настройки и обновления.\",\"en\":\"Settings and updates improved.\"},{\"version\":\""+installed+"\",\"date\":\"2026-10-01\",\"ru\":\"Предыдущая версия.\",\"en\":\"Previous version.\"}]}";
                var data=System.Text.Encoding.UTF8.GetBytes(body);var header=System.Text.Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: application/json; charset=utf-8\r\nContent-Length: "+data.Length+"\r\nConnection: close\r\n\r\n");stream.Write(header,0,header.Length);stream.Write(data,0,data.Length);
            }
        });
        RenderTexture target=null;
        try
        {
            var type=typeof(NativeMainMenu);type.GetField("endpoint",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(menu,"http://127.0.0.1:"+((System.Net.IPEndPoint)listener.LocalEndpoint).Port);
            var hostRoot=go.GetComponent<UIDocument>().rootVisualElement;
            Assert.IsNull(hostRoot.Q("main-controls"));Assert.IsNull(hostRoot.Q("main-history"));
            menu.Navigation.ActivateElement(hostRoot.Q<Button>("main-settings"));
            var settings=hostRoot.Q("settings-screen");Assert.NotNull(settings);
            menu.Navigation.ActivateElement(settings.Q<Button>("settings-tab-Управление"));
            menu.Navigation.ActivateElement(settings.Q<Button>("settings-controls"));
            Assert.AreEqual(DisplayStyle.None,settings.style.display.value);
            menu.Navigation.Back();yield return null;
            Assert.AreEqual(DisplayStyle.Flex,settings.style.display.value);
            Assert.AreSame(settings.Q<Button>("settings-controls"),hostRoot.focusController.focusedElement);
            yield return Capture(menu,"settings-with-history-800x620",800,620,settings);
            menu.Navigation.ActivateElement(settings.Q<Button>("settings-history"));
            float deadline=Time.realtimeSinceStartup+30;
            while(hostRoot.Q<DropdownField>("history-version").choices.Count==0&&Time.realtimeSinceStartup<deadline)yield return null;
            Assert.True(server.IsCompleted&&!server.IsFaulted,server.Exception?.ToString());
            var root=go.GetComponent<UIDocument>().rootVisualElement;var page=root.Q("release-history");var choice=page.Q<DropdownField>("history-version");Assert.AreEqual(2,choice.choices.Count);StringAssert.Contains("установлена",choice.choices[1]);
            choice.index=1;Assert.True(page.Query<Label>().ToList().Exists(l=>l.text.Contains("Предыдущая версия.")));choice.index=0;
            var panel=go.GetComponent<UIDocument>().panelSettings;target=new RenderTexture(800,620,24);target.Create();panel.targetTexture=target;root.style.width=800;root.style.height=620;
            for(int i=0;i<5;i++)yield return null;
            Assert.LessOrEqual(page.Q<Button>("history-back").worldBound.yMax,620);
            string output=Environment.GetEnvironmentVariable("LOBBY_LAYOUT_EVIDENCE");if(!string.IsNullOrEmpty(output)){
                var prior=RenderTexture.active;RenderTexture.active=target;var png=new Texture2D(800,620,TextureFormat.RGB24,false);try{png.ReadPixels(new Rect(0,0,800,620),0,0);png.Apply();System.IO.Directory.CreateDirectory(output);System.IO.File.WriteAllBytes(System.IO.Path.Combine(output,"history-fixture-800x620.png"),png.EncodeToPNG());}finally{RenderTexture.active=prior;UnityEngine.Object.Destroy(png);}}
            menu.Navigation.Back();Assert.IsNull(root.Q("release-history"));Assert.AreSame(settings.Q<Button>("settings-history"),root.focusController.focusedElement);
            Assert.AreEqual(DisplayStyle.Flex,settings.style.display.value);
            Assert.AreEqual(DisplayStyle.None,menu.ScreenRoot.style.display.value);
            menu.Navigation.Back();Assert.IsNull(root.Q("settings-screen"));
            Assert.AreSame(root.Q<Button>("main-settings"),root.focusController.focusedElement);
        }
        finally {listener.Stop();if(target){go.GetComponent<UIDocument>().panelSettings.targetTexture=null;target.Release();UnityEngine.Object.Destroy(target);}UnityEngine.Object.DestroyImmediate(go);Environment.SetEnvironmentVariable("SPACEWARS_UPDATE_ENDPOINT",saved);}
    }


}
