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
    private static IEnumerator Capture(NativeMainMenu menu,string name,int width=1280,int height=800)
    {
        string output=Environment.GetEnvironmentVariable("LOBBY_LAYOUT_EVIDENCE");if(string.IsNullOrEmpty(output))yield break;
        var panel=menu.GetComponent<UIDocument>().panelSettings;var texture=new RenderTexture(width,height,24);texture.Create();panel.targetTexture=texture;
        try
        {
            // A fresh capture target needs every retained UI element repainted, even if its layout did not change.
            foreach(var element in menu.ScreenRoot.Query<VisualElement>().ToList())element.MarkDirtyRepaint();
            for(int i=0;i<5;i++)yield return null;
            var copyright=menu.ScreenRoot.Q<Label>("menu-copyright");
            Assert.AreEqual(DisplayStyle.Flex,copyright.resolvedStyle.display,name+" copyright display");
            Debug.Log(name+" copyright bounds "+copyright.worldBound+" screen "+menu.ScreenRoot.worldBound);
            Assert.Greater(copyright.worldBound.height,0,name+" copyright height");
            Assert.LessOrEqual(copyright.worldBound.yMax,menu.ScreenRoot.worldBound.yMax,name+" copyright viewport");
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
    [UnityTest] public IEnumerator MenuKeepsUpdateInFooterAndDoesNotStartMatch()
    {
        string endpoint=Environment.GetEnvironmentVariable("SPACEWARS_UPDATE_ENDPOINT");Environment.SetEnvironmentVariable("SPACEWARS_UPDATE_ENDPOINT",null);
        var go=new GameObject("main menu contract");var menu=go.AddComponent<NativeMainMenu>();bool played=false;menu.Play=()=>played=true;
        yield return null;
        try
        {
            var root=go.GetComponent<UIDocument>().rootVisualElement;
            var actions=root.Query<Button>().ToList();
            CollectionAssert.AreEqual(new[]{"В бой!","Кампания","Сетевая игра","Настройки","Управление","Что нового","Лаборатория геймдизайна","Выйти","Обновить","Отменить загрузку","Перезапустить"},actions.ConvertAll(b=>b.text));
            Assert.False(actions[2].enabledSelf);Assert.That(root.Q<Button>("main-restart").style.display.value,Is.EqualTo(DisplayStyle.None));Assert.IsNull(root.Q<Button>("main-check"));
            Assert.IsNull(root.Q(className:"orbital-hints"));
            Assert.AreEqual(DisplayStyle.Flex,root.Q("main-update-panel").style.display.value,"Panel is visible before startup status is received");
            Assert.AreEqual(DisplayStyle.None,root.Q<Button>("main-update").style.display.value);
            Assert.True(root.Q<Button>("main-update").parent.parent.ClassListContains("orbital-menu-footer"));
            Assert.False(root.Query<Label>().ToList().Exists(l=>l.text.Contains("ТЕРРИТОРИЯ")));
            Assert.That(played,Is.False,"Displaying the main menu must not start a match.");
            Assert.That(root.Q<Button>("main-update").enabledSelf,Is.False,"A Player without the trusted installed launcher must not start updating.");
            Assert.That(actions[0].enabledSelf,Is.True,"Offline installed gameplay remains available.");
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

    [UnityTest] public IEnumerator DownloadAndPreparedUpdateKeepPlayAvailableUntilExplicitRestart()
    {
        var endpoint=Environment.GetEnvironmentVariable("SPACEWARS_UPDATE_ENDPOINT");Environment.SetEnvironmentVariable("SPACEWARS_UPDATE_ENDPOINT",null);
        var go=new GameObject("update state test");var menu=go.AddComponent<NativeMainMenu>();yield return null;
        try{
            var type=typeof(NativeMainMenu);var stateType=type.GetNestedType("UpdateState",BindingFlags.NonPublic);
            foreach(string state in new[]{"checking","offline","current","downloading","prepared"}){
                var value=Activator.CreateInstance(stateType,true);stateType.GetField("state").SetValue(value,state);stateType.GetField("version").SetValue(value,"test");
                type.GetField("current",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(menu,value);type.GetMethod("ShowStatus",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(menu,null);
                Assert.AreEqual(state=="current"?DisplayStyle.None:DisplayStyle.Flex,menu.ScreenRoot.Q("main-update-panel").style.display.value);
                Assert.AreEqual(state=="checking"?DisplayStyle.Flex:DisplayStyle.None,menu.ScreenRoot.Q("main-update-spinner").style.display.value);
                Assert.AreEqual(DisplayStyle.None,menu.ScreenRoot.Q<Button>("main-update").style.display.value);
                Assert.True(menu.ScreenRoot.Q<Button>("main-play").enabledSelf);Assert.IsNull(menu.ScreenRoot.Q<Button>("main-check"));
                Assert.AreEqual(state=="prepared"?DisplayStyle.Flex:DisplayStyle.None,menu.ScreenRoot.Q<Button>("main-restart").style.display.value);
                if(state=="checking"||state=="offline"||state=="current")yield return Capture(menu,"update-"+state);
            }
        }finally{UnityEngine.Object.DestroyImmediate(go);Environment.SetEnvironmentVariable("SPACEWARS_UPDATE_ENDPOINT",endpoint);}
    }

    [UnityTest] public IEnumerator UpdateProgressAndRequestFailureRemainVisible()
    {
        var endpoint=Environment.GetEnvironmentVariable("SPACEWARS_UPDATE_ENDPOINT");Environment.SetEnvironmentVariable("SPACEWARS_UPDATE_ENDPOINT",null);
        var go=new GameObject("update feedback test");var menu=go.AddComponent<NativeMainMenu>();yield return null;
        try{
            var type=typeof(NativeMainMenu);var stateType=type.GetNestedType("UpdateState",BindingFlags.NonPublic);
            var value=Activator.CreateInstance(stateType,true);
            stateType.GetField("state").SetValue(value,"downloading");stateType.GetField("version").SetValue(value,"test");
            type.GetField("current",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(menu,value);
            var show=type.GetMethod("ShowStatus",BindingFlags.Instance|BindingFlags.NonPublic);
            show.Invoke(menu,null);
            var status=menu.ScreenRoot.Q<Label>("main-update-status");var progress=menu.ScreenRoot.Q<ProgressBar>("main-update-progress");
            StringAssert.Contains("Подготовка файлов",status.text);Assert.AreEqual(DisplayStyle.Flex,progress.style.display.value);
            stateType.GetField("total").SetValue(value,4L*1048576);stateType.GetField("done").SetValue(value,1048576L);show.Invoke(menu,null);
            Assert.AreEqual(25,progress.value);StringAssert.Contains("25%",status.text);StringAssert.Contains("МБ",status.text);
            stateType.GetField("done").SetValue(value,4L*1048576);show.Invoke(menu,null);StringAssert.Contains("Проверяем",status.text);
            type.GetField("requestError",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(menu,"Запрос отклонён: HTTP 409");
            stateType.GetField("state").SetValue(value,"available");show.Invoke(menu,null);
            Assert.AreEqual("Запрос отклонён: HTTP 409",status.text,"Polling must not erase the failed click.");Assert.True(menu.ScreenRoot.Q<Button>("main-update").enabledSelf);
            Assert.AreEqual(DisplayStyle.None,progress.style.display.value);
            Assert.AreEqual(DisplayStyle.Flex,menu.ScreenRoot.Q<Button>("main-update").style.display.value);
            type.GetField("requestError",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(menu,null);
            stateType.GetField("available").SetValue(value,"1.0.1");show.Invoke(menu,null);yield return Capture(menu,"update-available");
            stateType.GetField("available").SetValue(value,null);
            type.GetField("requestError",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(menu,null);
            stateType.GetField("state").SetValue(value,"error");stateType.GetField("error").SetValue(value,"downloaded chunk rejected");show.Invoke(menu,null);
            StringAssert.Contains("downloaded chunk rejected",status.text);Assert.False(menu.ScreenRoot.Q<Button>("main-update").enabledSelf,"Discovery failures cannot retry an absent candidate.");
        }finally{UnityEngine.Object.DestroyImmediate(go);Environment.SetEnvironmentVariable("SPACEWARS_UPDATE_ENDPOINT",endpoint);}
    }

    [UnityTest] public IEnumerator FailedUpdateHttpRequestDoesNotRevertToAvailableText()
    {
        var saved=Environment.GetEnvironmentVariable("SPACEWARS_UPDATE_ENDPOINT");Environment.SetEnvironmentVariable("SPACEWARS_UPDATE_ENDPOINT",null);
        var go=new GameObject("update HTTP failure test");var menu=go.AddComponent<NativeMainMenu>();yield return null;
        var listener=new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback,0);listener.Start();
        var server=System.Threading.Tasks.Task.Run(()=>{
            using(var client=listener.AcceptTcpClient())using(var stream=client.GetStream()){
                var reader=new System.IO.StreamReader(stream);string line;while(!string.IsNullOrEmpty(line=reader.ReadLine())){}
                var bytes=System.Text.Encoding.ASCII.GetBytes("HTTP/1.1 409 Conflict\r\nContent-Length: 11\r\nConnection: close\r\n\r\nunavailable");stream.Write(bytes,0,bytes.Length);
            }
        });
        try{
            var type=typeof(NativeMainMenu);var stateType=type.GetNestedType("UpdateState",BindingFlags.NonPublic);var value=Activator.CreateInstance(stateType,true);
            stateType.GetField("state").SetValue(value,"available");stateType.GetField("available").SetValue(value,"1.0.1");
            type.GetField("current",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(menu,value);
            type.GetField("endpoint",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(menu,"http://127.0.0.1:"+((System.Net.IPEndPoint)listener.LocalEndpoint).Port);
            type.GetField("token",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(menu,"fixture-token");
            menu.StartCoroutine((IEnumerator)type.GetMethod("UpdateGame",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(menu,null));
            var status=menu.ScreenRoot.Q<Label>("main-update-status");StringAssert.Contains("Начинаем скачивание",status.text);Assert.False(menu.ScreenRoot.Q<Button>("main-update").enabledSelf);
            var deadline=Time.realtimeSinceStartup+20;while((bool)type.GetField("busy",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(menu)&&Time.realtimeSinceStartup<deadline)yield return null;
            Assert.False((bool)type.GetField("busy",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(menu));StringAssert.Contains("409",status.text);
            type.GetMethod("ShowStatus",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(menu,null);StringAssert.Contains("409",status.text);
            Assert.True(menu.ScreenRoot.Q<Button>("main-update").enabledSelf,"Failed update must allow retry");
            while(!server.IsCompleted&&Time.realtimeSinceStartup<deadline)yield return null;
            Assert.True(server.IsCompleted,"HTTP fixture must finish within the original request deadline");Assert.False(server.IsFaulted);
        }finally{listener.Stop();UnityEngine.Object.DestroyImmediate(go);Environment.SetEnvironmentVariable("SPACEWARS_UPDATE_ENDPOINT",saved);}
    }

    [UnityTest] public IEnumerator HistoryLoadsInstalledAndRecentVersionsAndRestoresMainFocus()
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
            yield return menu.StartCoroutine((IEnumerator)type.GetMethod("OpenHistory",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(menu,null));
            Assert.True(server.IsCompleted&&!server.IsFaulted,server.Exception?.ToString());
            var root=go.GetComponent<UIDocument>().rootVisualElement;var page=root.Q("release-history");var choice=page.Q<DropdownField>("history-version");Assert.AreEqual(2,choice.choices.Count);StringAssert.Contains("установлена",choice.choices[1]);
            choice.index=1;Assert.True(page.Query<Label>().ToList().Exists(l=>l.text.Contains("Предыдущая версия.")));choice.index=0;
            var panel=go.GetComponent<UIDocument>().panelSettings;target=new RenderTexture(800,620,24);target.Create();panel.targetTexture=target;root.style.width=800;root.style.height=620;
            for(int i=0;i<5;i++)yield return null;
            Assert.LessOrEqual(page.Q<Button>("history-back").worldBound.yMax,620);
            string output=Environment.GetEnvironmentVariable("LOBBY_LAYOUT_EVIDENCE");if(!string.IsNullOrEmpty(output)){
                var prior=RenderTexture.active;RenderTexture.active=target;var png=new Texture2D(800,620,TextureFormat.RGB24,false);try{png.ReadPixels(new Rect(0,0,800,620),0,0);png.Apply();System.IO.Directory.CreateDirectory(output);System.IO.File.WriteAllBytes(System.IO.Path.Combine(output,"history-fixture-800x620.png"),png.EncodeToPNG());}finally{RenderTexture.active=prior;UnityEngine.Object.Destroy(png);}}
            menu.Navigation.Back();Assert.IsNull(root.Q("release-history"));Assert.AreSame(root.Q<Button>("main-history"),root.focusController.focusedElement);
        }
        finally {listener.Stop();if(target){go.GetComponent<UIDocument>().panelSettings.targetTexture=null;target.Release();UnityEngine.Object.Destroy(target);}UnityEngine.Object.DestroyImmediate(go);Environment.SetEnvironmentVariable("SPACEWARS_UPDATE_ENDPOINT",saved);}
    }

    [UnityTest] public IEnumerator StatusFailuresStopSpinnerAndRecoverAutomatically()
    {
        var saved=Environment.GetEnvironmentVariable("SPACEWARS_UPDATE_ENDPOINT");Environment.SetEnvironmentVariable("SPACEWARS_UPDATE_ENDPOINT",null);
        var go=new GameObject("status recovery test");var menu=go.AddComponent<NativeMainMenu>();yield return null;
        var listener=new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback,0);listener.Start();
        var server=System.Threading.Tasks.Task.Run(()=>{
            foreach(var body in new[]{"unavailable","invalid JSON","{\"state\":\"current\",\"version\":\"test\"}"})
            using(var client=listener.AcceptTcpClient())using(var stream=client.GetStream()){
                var reader=new System.IO.StreamReader(stream);while(!string.IsNullOrEmpty(reader.ReadLine())){}
                var bytes=System.Text.Encoding.ASCII.GetBytes("HTTP/1.1 "+(body=="unavailable"?"503 Unavailable":"200 OK")+"\r\nContent-Length: "+body.Length+"\r\nConnection: close\r\n\r\n"+body);stream.Write(bytes,0,bytes.Length);
            }
        });
        try
        {
            var type=typeof(NativeMainMenu);var stateType=type.GetNestedType("UpdateState",BindingFlags.NonPublic);var value=Activator.CreateInstance(stateType,true);
            stateType.GetField("state").SetValue(value,"checking");type.GetField("current",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(menu,value);
            type.GetField("endpoint",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(menu,"http://127.0.0.1:"+((System.Net.IPEndPoint)listener.LocalEndpoint).Port);
            type.GetField("token",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(menu,"fixture-token");
            var show=type.GetMethod("ShowStatus",BindingFlags.Instance|BindingFlags.NonPublic);show.Invoke(menu,null);
            for(int i=0;i<3;i++)
            {
                // Invoke Request with a null callback; inspect/recover status through ShowStatus.
                yield return (IEnumerator)type.GetMethod("Request",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(menu,new object[]{"/status","GET",null});
                show.Invoke(menu,null);
                Assert.AreEqual(DisplayStyle.Flex,menu.ScreenRoot.Q("main-update-panel").style.display.value,"Errors and ongoing checks remain visible");
                if(i<2){StringAssert.Contains("Не удалось",menu.ScreenRoot.Q<Label>("main-update-status").text);Assert.AreEqual(DisplayStyle.None,menu.ScreenRoot.Q("main-update-spinner").style.display.value);}
                else{StringAssert.Contains("Проверяем",menu.ScreenRoot.Q<Label>("main-update-status").text);Assert.AreEqual(DisplayStyle.Flex,menu.ScreenRoot.Q("main-update-spinner").style.display.value);}
            }
        }
        finally{listener.Stop();UnityEngine.Object.DestroyImmediate(go);Environment.SetEnvironmentVariable("SPACEWARS_UPDATE_ENDPOINT",saved);}
    }

}
