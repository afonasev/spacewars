using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using Newtonsoft.Json;
using Spacewars.Runtime;
using Spacewars.Simulation.Ai;
using UnityEngine.TestTools;

public sealed class NativeAiFactoryFundingDiagnosticPlayModeTests
{
    [UnityTest,Timeout(300000)]
    public IEnumerator OriginalTwoFighterFactoryFundingCapture()=>Capture(false);

    [UnityTest,Timeout(300000)]
    public IEnumerator BufferedTwoFighterFactoryFundingCapture()=>Capture(true);

    [UnityTest,Timeout(300000)]
    public IEnumerator TerminalOnlyTwoFighterFactoryFundingCapture()
    {
        string path=Environment.GetEnvironmentVariable("SPACEWARS_QA_FACTORY_FUNDING_OUTPUT");
        Assert.False(string.IsNullOrWhiteSpace(path),"explicit owned diagnostic output is required");
        Assert.True(Path.IsPathRooted(path));
        Assert.False(File.Exists(path),"never overwrite an earlier capture");
        Assert.True(Directory.Exists(Path.GetDirectoryName(path)));
        Assert.IsNull(PlayableAiOwnerLoop.CurrentSafeFundingReadbackForTests,"terminal-only capture must not enable per-decision funding hooks");
        object terminal=null;
        var fixture=new NativeAiLongGrowthPlayModeTests();
        fixture.TerminalReadbackForTests=(authority,runtime)=>{
            // Detached checkpoints already published by the ordinary AI. No live-ledger
            // enumeration, file IO or JSON during the match. Tick identities are retained.
            terminal=new{RuntimeTick=runtime.Latest.Tick,Checkpoints=authority.CaptureDiagnosticCheckpoints(),
                Frame=runtime.SpectatorFrame(PlayableRuntime.LocalSpectatorId)};
        };
        var original=fixture.HumanSettingsCrossingsTwoFightersDevelopUsefulProductionAndScience();
        try{
            while(original.MoveNext())yield return original.Current;
        }finally{
            try{(original as IDisposable)?.Dispose();}
            finally{
                fixture.TerminalReadbackForTests=null;
                if(terminal!=null){
                    // Original cleanup has stopped/disposed runtime before serialization.
                    string json=JsonConvert.SerializeObject(terminal);
                    byte[] bytes=new UTF8Encoding(false).GetBytes(json);
                    Assert.LessOrEqual(bytes.LongLength,64L*1024*1024,"bounded terminal QA output");
                    using(var file=new FileStream(path,FileMode.CreateNew,FileAccess.Write))file.Write(bytes,0,bytes.Length);
                    TestContext.Out.WriteLine("AI_FACTORY_TERMINAL_CAPTURE bytes="+bytes.Length+" path="+path);
                }else TestContext.Out.WriteLine("AI_FACTORY_TERMINAL_CAPTURE NotReached; retain original setup/runtime failure");
            }
        }
    }

    private IEnumerator Capture(bool buffered)
    {
        string path=Environment.GetEnvironmentVariable("SPACEWARS_QA_FACTORY_FUNDING_OUTPUT");
        Assert.False(string.IsNullOrWhiteSpace(path),"explicit owned diagnostic output is required");
        Assert.True(Path.IsPathRooted(path));
        Assert.False(File.Exists(path),"never overwrite an earlier capture");
        Assert.True(Directory.Exists(Path.GetDirectoryName(path)));
        var method=typeof(NativeAiLongGrowthPlayModeTests).GetMethod("CrossingsGrowth",BindingFlags.Instance|BindingFlags.NonPublic);
        Assert.NotNull(method,"execute the unchanged original scenario and all its assertions");
        var original=(IEnumerator)method.Invoke(new NativeAiLongGrowthPlayModeTests(),new object[]{AiDifficulty.Fighter});
        var previous=PlayableAiOwnerLoop.CurrentSafeFundingReadbackForTests;
        var previousOwner=PlayableAiOwnerLoop.CurrentSafeFundingOwnerFilterForTests;
        var previousStage=PlayableAiOwnerLoop.CurrentSafeFundingStageFilterForTests;
        Assert.IsNull(previous,"this exact diagnostic owns its test-only observer");
        var stages=new HashSet<string>{"ExpansionFinancialAdmissionInputs","TechnologyAllocation","ExpansionFinancialAllocation","ActualCandidatesBeforeSelection","ActualSelection"};
        int records=0;
        // Fixed, non-expandable QA payload buffer; unrelated to Nav's physical budget.
        const int CapturePayloadBytes=64*1024*1024;
        var buffer=buffered?new MemoryStream(new byte[CapturePayloadBytes],0,CapturePayloadBytes,true,true):null;
        if(buffer!=null)buffer.SetLength(0);
        Stream destination=buffered?(Stream)buffer:new FileStream(path,FileMode.CreateNew,FileAccess.Write);
        using(var writer=new StreamWriter(destination,new UTF8Encoding(false))){
            writer.AutoFlush=true;
            PlayableAiOwnerLoop.CurrentSafeFundingOwnerFilterForTests=null;
            PlayableAiOwnerLoop.CurrentSafeFundingStageFilterForTests=(_,stage)=>stages.Contains(stage);
            PlayableAiOwnerLoop.CurrentSafeFundingReadbackForTests=json=>{lock(writer){writer.WriteLine(json);records++;}};
            try{
                while(original.MoveNext())yield return original.Current;
            }finally{
                // The original iterator stops/disposes its runtime in its own finally.
                // Restore observers after that cleanup, retaining the original failure.
                try{(original as IDisposable)?.Dispose();}
                finally{
                    PlayableAiOwnerLoop.CurrentSafeFundingReadbackForTests=previous;
                    PlayableAiOwnerLoop.CurrentSafeFundingOwnerFilterForTests=previousOwner;
                    PlayableAiOwnerLoop.CurrentSafeFundingStageFilterForTests=previousStage;
                    writer.Flush();
                    if(buffered){
                        // The match has stopped before the first capture-file write.
                        Assert.LessOrEqual(buffer.Length,CapturePayloadBytes);
                        buffer.Position=0;
                        using(var file=new FileStream(path,FileMode.CreateNew,FileAccess.Write))buffer.CopyTo(file);
                    }
                    TestContext.Out.WriteLine("AI_FACTORY_FUNDING_CAPTURE records="+records+" buffered="+buffered+" path="+path);
                }
            }
        }
    }
}
