using System;
using System.Diagnostics;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
 public sealed class NativeProfileCopyTests
 {
  [Test] public void EveryFieldCopiesExactlyAndRemainsIndependent()
  {
   var source=new PlayableProfileData();var fields=typeof(PlayableProfileData).GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
   int i=0;
   foreach(var field in fields){
    Assert.True(field.IsPublic,field.Name);Assert.True(field.FieldType.IsValueType||field.FieldType==typeof(string),field.Name+" requires an explicit copy policy");
    object value=field.FieldType==typeof(double)?(object)(++i+.125):field.FieldType==typeof(int)?++i:field.FieldType==typeof(bool)?(object)true:"value-"+(++i);
    field.SetValue(source,value);
   }
   var copy=NativeBalanceFields.Copy(source);Assert.AreNotSame(source,copy);
   foreach(var field in fields){var expected=field.GetValue(source);Assert.AreEqual(expected,field.GetValue(copy),field.Name);field.SetValue(copy,field.FieldType.IsValueType?Activator.CreateInstance(field.FieldType):null);Assert.AreEqual(expected,field.GetValue(source),field.Name+" aliased");}
  }
  [Test] public void ProfileExportCannotMutateImmutableProfile()
  {
   var profile=PlayableProfile.Default;var original=profile.CopyData();var edited=profile.CopyData();edited.tankHealth=1;edited.profileId="edited";
   Assert.AreEqual(original.tankHealth,profile.CopyData().tankHealth);Assert.AreEqual(original.profileId,profile.CopyData().profileId);
  }
  [Test] public void CompareFieldCopyCostWithOriginalReflectionPath()
  {
   var source=PlayableProfile.Default.CopyData();var fields=typeof(PlayableProfileData).GetFields();PlayableProfileData result=null;
   for(int i=0;i<20;i++)result=NativeBalanceFields.Copy(source);
   long allocated=GC.GetAllocatedBytesForCurrentThread();var watch=Stopwatch.StartNew();
   for(int i=0;i<1000;i++){result=new PlayableProfileData();foreach(var f in fields)f.SetValue(result,f.GetValue(source));}
   watch.Stop();double oldMs=watch.Elapsed.TotalMilliseconds;long oldBytes=GC.GetAllocatedBytesForCurrentThread()-allocated;
   allocated=GC.GetAllocatedBytesForCurrentThread();watch.Restart();for(int i=0;i<1000;i++)result=NativeBalanceFields.Copy(source);watch.Stop();
   TestContext.WriteLine("PROFILE_COPY fields="+fields.Length+" iterations=1000 original_ms="+oldMs+" current_ms="+watch.Elapsed.TotalMilliseconds+" original_bytes="+oldBytes+" current_bytes="+(GC.GetAllocatedBytesForCurrentThread()-allocated));GC.KeepAlive(result);
  }
 }
}
