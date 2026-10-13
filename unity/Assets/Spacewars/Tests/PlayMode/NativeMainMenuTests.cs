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
    [UnityTest] public IEnumerator MenuShowsSeparateUpdateAndDoesNotStartMatch()
    {
        string endpoint=Environment.GetEnvironmentVariable("SPACEWARS_UPDATE_ENDPOINT");Environment.SetEnvironmentVariable("SPACEWARS_UPDATE_ENDPOINT",null);
        var go=new GameObject("main menu contract");var menu=go.AddComponent<NativeMainMenu>();bool played=false;menu.Play=()=>played=true;
        yield return null;
        try
        {
            var root=go.GetComponent<UIDocument>().rootVisualElement;
            var actions=root.Query<Button>().ToList();
            CollectionAssert.AreEqual(new[]{"В бой!","Сетевая игра","Настройки","Управление","Лаборатория геймдизайна","Обновить","Выйти","Проверить","Перезапустить"},actions.ConvertAll(b=>b.text));
            Assert.False(actions[1].enabledSelf);Assert.That(root.Q<Button>("main-restart").style.display.value,Is.EqualTo(DisplayStyle.None));Assert.False(root.Q<Button>("main-check").enabledSelf);
            Assert.False(root.Query<Label>().ToList().Exists(l=>l.text.Contains("ТЕРРИТОРИЯ")));
            Assert.That(played,Is.False,"Displaying the main menu must not start a match.");
            Assert.That(root.Q<Button>("main-update").enabledSelf,Is.False,"A Player without the trusted installed launcher must not start updating.");
            Assert.That(actions[0].enabledSelf,Is.True,"Offline installed gameplay remains available.");
        }
        finally{UnityEngine.Object.DestroyImmediate(go);Environment.SetEnvironmentVariable("SPACEWARS_UPDATE_ENDPOINT",endpoint);}
    }
    [UnityTest] public IEnumerator DownloadAndPreparedUpdateKeepPlayAvailableUntilExplicitRestart()
    {
        var endpoint=Environment.GetEnvironmentVariable("SPACEWARS_UPDATE_ENDPOINT");Environment.SetEnvironmentVariable("SPACEWARS_UPDATE_ENDPOINT",null);
        var go=new GameObject("update state test");var menu=go.AddComponent<NativeMainMenu>();yield return null;
        try{
            var type=typeof(NativeMainMenu);var stateType=type.GetNestedType("UpdateState",BindingFlags.NonPublic);
            foreach(string state in new[]{"downloading","prepared"}){
                var value=Activator.CreateInstance(stateType,true);stateType.GetField("state").SetValue(value,state);stateType.GetField("version").SetValue(value,"test");
                type.GetField("current",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(menu,value);type.GetMethod("ShowStatus",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(menu,null);
                Assert.True(menu.ScreenRoot.Q<Button>("main-play").enabledSelf);Assert.False(menu.ScreenRoot.Q<Button>("main-check").enabledSelf);
                Assert.AreEqual(state=="prepared"?DisplayStyle.Flex:DisplayStyle.None,menu.ScreenRoot.Q<Button>("main-restart").style.display.value);
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
            Assert.True(menu.ScreenRoot.Q<Button>("main-update").enabledSelf);Assert.True(server.IsCompleted);Assert.False(server.IsFaulted);
        }finally{listener.Stop();UnityEngine.Object.DestroyImmediate(go);Environment.SetEnvironmentVariable("SPACEWARS_UPDATE_ENDPOINT",saved);}
    }

}
