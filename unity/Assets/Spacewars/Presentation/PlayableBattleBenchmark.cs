#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.UIElements;
using Newtonsoft.Json;

namespace Spacewars.Presentation
{
    public sealed partial class PlayableBootstrap
    {
        private bool battleBenchmark;

        private IEnumerator WaitForBench(Func<bool> ready, string phase)
        {
            double end=Time.realtimeSinceStartupAsDouble+90;
            while(!ready()){
                if(!string.IsNullOrEmpty(runtime?.Latest?.Failure))throw new InvalidOperationException(runtime.Latest.Failure);
                if(Time.realtimeSinceStartupAsDouble>end)throw new TimeoutException(phase);
                yield return null;
            }
        }

        private IEnumerator RunBattleBenchmark(string[] args)
        {
            int arg=Array.IndexOf(args,"-battleBenchmark");
            if(evidence==null||arg+1>=args.Length)throw new ArgumentException("Supply -playableEvidence directory and -battleBenchmark lineup");
            string lineup=args[arg+1];
            int durationArg=Array.IndexOf(args,"-battleSeconds");
            int seconds=durationArg<0?30:int.Parse(args[durationArg+1]);
            if(seconds<1||seconds>120)throw new ArgumentOutOfRangeException(nameof(seconds));
            var mixed=new[]{PlayableEntityKind.Tank,PlayableEntityKind.Explorer,PlayableEntityKind.Shkval,PlayableEntityKind.Hawk};
            int count=lineup=="mixed10"?10:20;
            int countArg=Array.IndexOf(args,"-battleCount");
            if(countArg>=0)count=int.Parse(args[countArg+1]);
            if(count!=10&&count!=20&&count!=40)throw new ArgumentOutOfRangeException(nameof(count));
            var kinds=lineup=="mixed"||lineup=="mixed10"?mixed:new[]{(PlayableEntityKind)Enum.Parse(typeof(PlayableEntityKind),lineup)};
            yield return WaitForBench(()=>!initializingMenu&&inLobby&&nativeMainMenu?.ScreenRoot!=null,"menu");
            // The saved display mode overrides Unity command-line dimensions during startup.
            int width=Array.IndexOf(args,"-screen-width"),height=Array.IndexOf(args,"-screen-height");
            if(width>=0&&height>=0)Screen.SetResolution(int.Parse(args[width+1]),int.Parse(args[height+1]),FullScreenMode.Windowed);
            yield return null;
            balanceStore=NativeBalanceStore.ForApplication(profile,Path.Combine(evidence,"balance.json"));
            OpenLaboratory();
            menuNavigation.ActivateElement(laboratoryPage.Page.Q<Button>("lab-proving-ground"));
            yield return WaitForBench(()=>!provingGroundBusy&&view!=null&&view.Tick>0,"empty proving ground");
            // Seed via the polygon's validated recipe, then rebuild through its normal host/presentation lifecycle.
            for(int side=0;side<2;side++)
                for(int i=0;i<count;i++){
                    var point=new NavPoint((side==0?-1:1)*(4+4*(i%4)),4*(i/4-(count==40?4:2)));
                    var error=provingGround.PlaceUnit(side,kinds[i%kinds.Length],point);
                    if(error!=null)throw new InvalidOperationException("slot "+i+": "+error);
                }
            RebuildProvingGround();
            yield return WaitForBench(()=>!provingGroundBusy&&runtime!=null&&runtime.DiagnosticGlobalUnits==2*count,"full armies");
            for(int side=0;side<2;side++){
                var own=ProvingView(side).Entities.Where(e=>(int)e.Owner==side).ToArray();
                if(own.Length!=count||kinds.Any(k=>own.Count(e=>e.Kind==k)!=Enumerable.Range(0,count).Count(i=>kinds[i%kinds.Length]==k)))
                    throw new InvalidOperationException("Incorrect roster on side "+side);
            }
            input.enabled=false;
            var target=world.Point(new NavPoint(0,0));
            cameraView.orthographicSize=(float)profile.CameraOrthoSize;
            cameraView.transform.position=target+new Vector3((float)profile.CameraOffsetX,(float)profile.CameraHeight,(float)profile.CameraOffsetZ);
            cameraView.transform.LookAt(target);
            RememberKeyboardCamera();
            long startTick=view.Tick;
            int startFrame=Time.frameCount;
            double start=Time.realtimeSinceStartupAsDouble;
            StartProvingGround();
            if(!provingGroundBattle)throw new InvalidOperationException("Opening attack rejected");
            long firstCombatTick=-1;
            int firstCombatFrame=-1,peakProjectiles=0,peakVisibleWrecks=0;
            while(Time.realtimeSinceStartupAsDouble-start<seconds){
                yield return null;
                if(runtime.IsStopped||!string.IsNullOrEmpty(view.Failure))throw new InvalidOperationException(view.Failure??"Host stopped");
                peakVisibleWrecks=Math.Max(peakVisibleWrecks,view.Wrecks.Count);
                peakProjectiles=Math.Max(peakProjectiles,view.Projectiles.Count);
                if(firstCombatTick<0&&(runtime.DiagnosticGlobalUnits<2*count||view.Entities.Any(e=>e.Health<e.MaxHealth))){
                    firstCombatTick=view.Tick;
                    firstCombatFrame=Time.frameCount;
                }
            }
            var result=new{lineup,count,seconds,profile=profile.DisplayName,profileRevision=profile.Revision,view.Seed,view.Generation,
                width=Screen.width,height=Screen.height,startRealtime=start,startTick,endTick=view.Tick,startFrame,endFrame=Time.frameCount,
                elapsed=Time.realtimeSinceStartupAsDouble-start,firstCombatTick,firstCombatFrame,peakProjectiles,peakVisibleWrecks,noWrecks=Array.IndexOf(args,"-battleNoWrecks")>=0,
                endUnits=runtime.DiagnosticGlobalUnits,errors=view.Metrics.Errors,dropped=routeFrames.Dropped};
            File.WriteAllText(Path.Combine(evidence,"battle.json"),JsonConvert.SerializeObject(result,Formatting.Indented));
            if(firstCombatTick<0||view.Tick<=startTick||view.Metrics.Errors!=0||routeFrames.Dropped!=0)
                throw new InvalidOperationException("Incomplete combat sample: "+JsonConvert.SerializeObject(result));
            Quit();
        }
    }
}
#endif
