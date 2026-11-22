using System.Collections.Generic;
using Spacewars.Runtime;
using UnityEngine;

namespace Spacewars.Presentation
{
    public sealed partial class PlayableBootstrap
    {
        private readonly System.Random defeatRandom=new System.Random();
        private readonly HashSet<string> usedDefeatLines=new HashSet<string>();
        private int nextAiDefeat;
        private long lastAiDefeatTick=-1;
        private double defeatReadUntil;

        private void ResetAiDefeatLines()
        {
            nextAiDefeat=0;
            lastAiDefeatTick=-1;
            defeatReadUntil=0;
            usedDefeatLines.Clear();
            gameplayAudio?.Speech?.ResetSpeech();
        }

        private void PresentAiDefeatLines()
        {
            if(runtime==null||matchChat==null||matchSetup==null||inLobby||preparing||
                restarting||returningToLobby||quitting||ResultsVisible||campaignConfiguration!=null)return;
            if(paused&&runtime.Result==null)return;

            BindGameplayAudioRuntime();
            var speech=gameplayAudio?.Speech;
            double now=Time.unscaledTimeAsDouble;
            var defeats=runtime.AiDefeats;
            while(nextAiDefeat<defeats.Count&&speech?.FarewellPlaying(now)!=true)
            {
                var defeat=defeats[nextAiDefeat++];
                var line=AiDefeatLines.Choose(defeat,defeatRandom,usedDefeatLines);
                if(!matchChat.SendAiPublicLine(defeat.Slot,line.Text,now,farewell:true))continue;
                usedDefeatLines.Add(line.Key);
                lastAiDefeatTick=defeat.Tick;
                defeatReadUntil=now+AiDefeatLines.ReadingSeconds;
                speech?.PlayFarewell(line.Key,now);
            }
        }

        private bool WaitingForAiDefeatLine(MatchResult result)
        {
            var defeats=runtime.AiDefeats;
            if(defeats.Count==0||defeats[defeats.Count-1].Tick!=result.Tick||result.Manual)return false;
            double now=Time.unscaledTimeAsDouble;
            return nextAiDefeat<defeats.Count||lastAiDefeatTick==result.Tick&&
                (now<defeatReadUntil||gameplayAudio?.Speech?.FarewellPlaying(now)==true);
        }
    }
}
