using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using Spacewars.Runtime;
using Spacewars.Simulation;
internal static class Program {
    static void Require(bool ok,string why){if(!ok)throw new Exception(why);}
    static OfflineMatchConfiguration Config(){
        var rows=File.ReadAllLines("unity/Tests/Fixtures/native-offline-participant-authority/source-fixture.tsv").Skip(1).Select(s=>s.Split('|')).ToArray();
        double N(string s)=>double.Parse(s,System.Globalization.CultureInfo.InvariantCulture);
        var starts=rows.Where(x=>x[0]=="S").Select(x=>new OfflineStart(x[1],int.Parse(x[2]),new NavPoint(N(x[3]),N(x[4])),new NavPoint(N(x[5]),N(x[6])),pin:int.Parse(x[7])==0?(int?)null:int.Parse(x[7]))).ToArray();
        var sites=starts.Select(s=>new TerritorySite(s.SiteId,PlayableBuildingKind.Headquarters,s.Position,rows.Where(x=>x[0]=="L"&&int.Parse(x[1])==s.SiteId).Select(x=>new TerritorySlot(int.Parse(x[2]),new NavPoint(N(x[3]),N(x[4])),N(x[5]))).ToArray())).Concat(new[]{new TerritorySite(9,PlayableBuildingKind.Outpost,new NavPoint(0,0),rows.Where(x=>x[0]=="L"&&x[1]=="9").Select(x=>new TerritorySlot(int.Parse(x[2]),new NavPoint(N(x[3]),N(x[4])),N(x[5]))).ToArray())}).ToArray();
        var costs=new double[8,8];foreach(var r in rows.Where(x=>x[0]=="R"))costs[int.Parse(r[1]),int.Parse(r[2])]=N(r[3]);
        return new OfflineMatchConfiguration(PlayableProfile.Default,"source-flat-authority-v1","native-flat-eight-starts-v1","native:flat-genesis-fixture",19092026,new[]{new OfflineParticipant("owner-11",1,3,OfflineControl.Human),new OfflineParticipant("owner-28",2,7,OfflineControl.Human)},starts,sites,Array.Empty<NavObstacle>(),costs,new[]{"spectator-99"});
    }

    static void Main(string[] args){try{var report=new List<object>();var config=Config();foreach(bool replay in new[]{false,true})foreach(bool asynchronous in new[]{false,true}){
        var a=new PlayableAuthorityTick(config,71,asynchronousRoutes:asynchronous);var k=new CertifiedRouteKernel{PhysicalOperationsPerPump=1};
        try{
            var replayField=typeof(NavigationSession).GetField("replayOnlyProjection",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
            bool mainLegacyControl=args.Length>1&&args[1]=="main-legacy-control";
            if(mainLegacyControl||!replay)replayField?.SetValue(a.Domain.Navigation,false);else if(replayField==null)throw new InvalidOperationException("Baseline has no Replay policy: select explicit main-legacy-control.");
            var n=a.Domain.Navigation;string id="legacy-"+replay+"-"+asynchronous;
            File.WriteAllBytes(Path.Combine(args[0],id+"-prebirth.bin"),a.CaptureBytes());
            var actor=n.Crowd.Units.First();Require(n.Move(actor.Id,new NavPoint(actor.Position.X+3,actor.Position.Z)),"move");a.TryAdvance();
            for(int t=0;t<4;t++){k.Pump(a.NavigationBinding,4);File.WriteAllBytes(Path.Combine(args[0],id+"-next-"+t+".bin"),a.CaptureBytes());}
            for(int t=0;t<20000;t++){var root=n.PinCommittedHostRoot();bool done=root.QuantumCompleted;root.Release();if(done)break;k.Pump(a.NavigationBinding,4);}
            File.WriteAllBytes(Path.Combine(args[0],id+"-complete.bin"),a.CaptureBytes());
            var r=n.PinCommittedHostRoot();try{report.Add(new{replay,asynchronous,mainLegacyControl,actualReplay=mainLegacyControl?false:replay,physical=n.PhysicalHostBudget.PhysicalBytes,retained=n.PhysicalHostBudget.RetainedBytes,hostCharge=r.StorageBytes,tag=r.PhysicalTag.Encoded});}finally{r.Release();}
        }finally{k.Clear();a.Stop();a.Domain.Navigation.DestroyNavigationAuthority();Require(a.Domain.Navigation.PhysicalHostBudget.PhysicalBytes==0,"legacy terminal ledger0");}
    }File.WriteAllText(Path.Combine(args[0],"result.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));}catch(Exception e){Console.Error.WriteLine(e);Environment.ExitCode=1;}}
}
