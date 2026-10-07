using System;
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
            Assert.That(actions.ConvertAll(b=>b.text),Is.EqualTo(new[]{"В бой!","Обновить","Проверить","Перезапустить","Выйти"}));
            Assert.That(actions[3].style.display.value,Is.EqualTo(DisplayStyle.None),"Restart is visible only for a prepared update.");
            Assert.That(played,Is.False,"Displaying the main menu must not start a match.");
            Assert.That(actions[1].enabledSelf,Is.False,"A Player without the trusted installed launcher must not start updating.");
            Assert.That(actions[0].enabledSelf,Is.True,"Offline installed gameplay remains available.");
        }
        finally{UnityEngine.Object.DestroyImmediate(go);Environment.SetEnvironmentVariable("SPACEWARS_UPDATE_ENDPOINT",endpoint);}
    }
}
