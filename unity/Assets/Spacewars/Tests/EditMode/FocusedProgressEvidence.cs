using System;
using System.IO;
using NUnit.Framework.Interfaces;
using UnityEngine.TestRunner;

[assembly: TestRunCallback(typeof(Spacewars.Tests.EditMode.FocusedProgressEvidence))]
namespace Spacewars.Tests.EditMode
{
    // Opt-in leaf receipts keep long focused QA observable without changing tests.
    public sealed class FocusedProgressEvidence : ITestRunCallback
    {
        private readonly string directory;
        public FocusedProgressEvidence()
        {
            var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,"-focusedProgressEvidence");
            if(i>=0&&i+1<args.Length){directory=args[i+1];Directory.CreateDirectory(directory);}
        }
        public void RunStarted(ITest testsToRun){}
        public void RunFinished(ITestResult results){}
        public void TestStarted(ITest test){if(directory!=null&&!test.IsSuite)File.AppendAllText(Path.Combine(directory,"progress.txt"),"START "+test.FullName+Environment.NewLine);}
        public void TestFinished(ITestResult result)
        {
            if(directory==null||result.Test.IsSuite)return;
            File.WriteAllText(Path.Combine(directory,"case-"+result.Test.Id+".xml"),result.ToXml(true).OuterXml);
            File.AppendAllText(Path.Combine(directory,"progress.txt"),"END "+result.Test.FullName+" "+result.ResultState+Environment.NewLine);
        }
    }
}
