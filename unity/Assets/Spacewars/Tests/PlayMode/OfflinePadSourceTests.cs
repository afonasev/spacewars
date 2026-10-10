using System.IO;
using System.Linq;
using NUnit.Framework;
using Spacewars.Input;
using UnityEngine;
namespace Spacewars.Tests.PlayMode
{
    public sealed class OfflinePadSourceTests
    {
        [Test] public void NativeGesturesRetainNeutralReconnectWithUpdatedBCommand()
        {
            var profile=JsonUtility.FromJson<NativeLocalInputProfile>(Resources.Load<TextAsset>("NativeLocalInputProfile").text);profile.Validate();
            var root=new DirectoryInfo(Directory.GetCurrentDirectory());while(root!=null&&!File.Exists(Path.Combine(root.FullName,"unity/Tests/Fixtures/native-two-local-human/source-input.tsv")))root=root.Parent;Assert.NotNull(root);
            OfflinePadGestures gesture=null;string name=null;int tested=0;
            foreach(var line in File.ReadAllLines(Path.Combine(root.FullName,"unity/Tests/Fixtures/native-two-local-human/source-input.tsv")).Skip(1)){
                var row=line.Split('|');if(name!=row[0]){name=row[0];gesture=new OfflinePadGestures();}
                var intents=gesture.Step(double.Parse(row[1],System.Globalization.CultureInfo.InvariantCulture),int.Parse(row[2]),row[3]=="1",row[4]=="1",profile,row[5]=="1");
                Assert.AreEqual(row[6],gesture.Map?"tacticalMap":"world",line);Assert.AreEqual(row[7]=="attackMove"?"context":row[7],string.Join(",",intents),line);tested++;
            }Assert.AreEqual(41,tested);
        }
        [Test] public void RemovedGamepadFailsClosedBeforeNeutralControlQueries()
        {
            var device=UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Gamepad>();UnityEngine.InputSystem.InputSystem.RemoveDevice(device);
            var neutral=typeof(Spacewars.Presentation.OfflineTwoLocalBootstrap).GetMethod("Neutral",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic);
            Assert.AreEqual(false,neutral.Invoke(null,new object[]{device}));
        }
    }
}
