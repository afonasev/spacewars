using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.UIElements;

public sealed class NativeLobbyTeamUiTests
{
    private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    private GameObject host;
    private PlayableBootstrap app;
    private T Get<T>(string name)=>(T)typeof(PlayableBootstrap).GetField(name,Flags).GetValue(app);
    private void Call(string name,params object[] args)=>typeof(PlayableBootstrap).GetMethod(name,Flags).Invoke(app,args);
    [SetUp] public void Setup()
    {
        host=new GameObject("Lobby team test");app=host.AddComponent<PlayableBootstrap>();app.enabled=false;
        typeof(PlayableBootstrap).GetField("profile",Flags).SetValue(app,PlayableProfile.ThreeCrossingsDefault);
        Call("CreateWorld");Call("CreateHud");Call("CreateSpectatorHud");
        typeof(PlayableBootstrap).GetField("input",Flags).SetValue(app,host.AddComponent<Spacewars.Input.PlayableInput>());
        Call("CreateLobby");Call("ShowLobby");
    }
    [TearDown] public void Teardown()
    {
        Object.DestroyImmediate(host);
    }
    [Test] public void JoinAndBotActionsShowMapAwareTeamsWithoutChangingExistingSeats()
    {
        var lobby=Get<VisualElement>("lobbyScreen");
        lobby.Q<DropdownField>("lobby-map-choice").value=MossHighlandMap.DisplayName;
        var setup=Get<NativeLobbyConfiguration>("lobbySetup");
        setup.Participants.RemoveAt(0); // Leave teams 2, 3, 4; the joining human must get 1.
        Call("JoinLobbyWithKeyboardMouse");
        CollectionAssert.AreEqual(new[]{2,3,4,1},setup.Participants.Select(p=>p.Team));
        Assert.AreEqual("1",lobby.Q<DropdownField>("lobby-team-3").value);
        setup.Participants.RemoveAt(1);Call("AddLobbyParticipant",false,null);
        Assert.AreEqual(3,setup.Participants.Last().Team);
        Assert.AreEqual("3",lobby.Q<DropdownField>("lobby-team-3").value);
        lobby.Q<DropdownField>("lobby-map-choice").value="Огненный разлом";
        setup.Participants.Clear();
        for(int i=0;i<6;i++)Call("AddLobbyParticipant",false,null);
        CollectionAssert.AreEqual(new[]{1,2,1,2,1,2},setup.Participants.Select(p=>p.Team));
        for(int i=0;i<6;i++)Assert.AreEqual((i%2+1).ToString(),lobby.Q<DropdownField>("lobby-team-"+i).value);
    }
}
