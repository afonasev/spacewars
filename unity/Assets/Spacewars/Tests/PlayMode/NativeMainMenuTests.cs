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
            CollectionAssert.AreEqual(new[]{"В бой!","Сетевая игра","Настройки","Лаборатория геймдизайна","Обновить","Выйти","Проверить","Перезапустить"},actions.ConvertAll(b=>b.text));
            Assert.False(actions[1].enabledSelf);Assert.That(root.Q<Button>("main-restart").style.display.value,Is.EqualTo(DisplayStyle.None));Assert.False(root.Q<Button>("main-check").enabledSelf);
            Assert.False(root.Query<Label>().ToList().Exists(l=>l.text.Contains("ТЕРРИТОРИЯ")));
            Assert.That(played,Is.False,"Displaying the main menu must not start a match.");
            Assert.That(actions[4].enabledSelf,Is.False,"A Player without the trusted installed launcher must not start updating.");
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

}
