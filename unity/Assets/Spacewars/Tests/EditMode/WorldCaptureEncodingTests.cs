using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Security.Cryptography;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
namespace Spacewars.Tests.EditMode {
public sealed class WorldCaptureEncodingTests {
// Independent original envelope recipe: freeze byte ordering and section digests.
static byte[] Original(PlayableWorldState s){
byte[] payload;
using(var m=new MemoryStream()){
using(var w=new BinaryWriter(m,Encoding.UTF8,true)){
w.Write(0x53575731);w.Write(s.Version);w.Write(s.Seed);w.Write(s.Generation);w.Write(s.Tick);
var text=Encoding.UTF8.GetBytes(s.SourceIdentity);w.Write(text.Length);w.Write(text);
foreach(var section in new[]{s.Binding,s.Participants,s.Domain,s.Navigation,s.AiAuthority,s.RuntimeTransport}){w.Write(section.Length);w.Write(section);using(var h=SHA256.Create())w.Write(h.ComputeHash(section));}
w.Write(s.Vision.Length);foreach(var section in s.Vision){w.Write(section.Length);w.Write(section);using(var h=SHA256.Create())w.Write(h.ComputeHash(section));}
}payload=m.ToArray();}
using(var h=SHA256.Create())return payload.Concat(h.ComputeHash(payload)).ToArray();}
[TestCase(false)][TestCase(true)]public void CurrentEnvelopeIsByteExactWithOriginalRecipe(bool large){
var section=Enumerable.Range(0,large?131072:0).Select(i=>(byte)(i*31)).ToArray();
var s=new PlayableWorldState{Seed=7,Generation=71,Tick=123,SourceIdentity="o1-capture-byte-contract",Binding=new byte[]{1,2,3},Participants=new byte[]{4},Domain=section,Navigation=new byte[]{5,6},AiAuthority=new byte[]{7,8},RuntimeTransport=new byte[]{9},Vision=new[]{new byte[65536],new byte[]{10}}};
var original=Original(s);CollectionAssert.AreEqual(original,s.Encode());CollectionAssert.AreEqual(original,PlayableWorldState.Decode(original).Encode());
// Corrupt a section while re-signing the outer envelope: section digest must reject it.
var bad=(byte[])original.Clone();int sectionStart=4+4+4+8+8+4+Encoding.UTF8.GetByteCount(s.SourceIdentity)+4;bad[sectionStart]^=1;
using(var h=SHA256.Create())Buffer.BlockCopy(h.ComputeHash(bad,0,bad.Length-32),0,bad,bad.Length-32,32);
Assert.Throws<ArgumentException>(()=>PlayableWorldState.Decode(bad));
// The outer digest remains independently enforced.
bad=(byte[])original.Clone();bad[bad.Length-1]^=1;Assert.Throws<ArgumentException>(()=>PlayableWorldState.Decode(bad));
}
[TestCase(0)][TestCase(65536)]public void CoverageBulkWireIsByteExactWithOriginalByteArray(int count){
var bytes=Enumerable.Range(0,count).Select(i=>(byte)(i*17)).ToArray();byte[] original,actual;
using(var m=new MemoryStream()){using(var w=new BinaryWriter(m,Encoding.UTF8,true)){w.Write(bytes.Length);foreach(byte b in bytes)w.Write(b);}original=m.ToArray();}
var wire=typeof(PlayableWorldState).Assembly.GetType("Spacewars.Runtime.WorldWire",true);
var write=wire.GetMethod("ByteArray",BindingFlags.NonPublic|BindingFlags.Static,null,new[]{typeof(BinaryWriter),typeof(byte[])},null);
using(var m=new MemoryStream()){using(var w=new BinaryWriter(m,Encoding.UTF8,true))write.Invoke(null,new object[]{w,bytes});actual=m.ToArray();}
CollectionAssert.AreEqual(original,actual);
var read=wire.GetMethod("ByteArray",BindingFlags.NonPublic|BindingFlags.Static,null,new[]{typeof(BinaryReader)},null);
using(var m=new MemoryStream(original))using(var r=new BinaryReader(m)){CollectionAssert.AreEqual(bytes,(byte[])read.Invoke(null,new object[]{r}));Assert.AreEqual(m.Length,m.Position);}
}
[TestCase(-1)][TestCase(262145)][TestCase(3)]public void CoverageBulkWireRetainsLengthAndTruncationGuards(int count){
var wire=typeof(PlayableWorldState).Assembly.GetType("Spacewars.Runtime.WorldWire",true);
var read=wire.GetMethod("ByteArray",BindingFlags.NonPublic|BindingFlags.Static,null,new[]{typeof(BinaryReader)},null);
using(var m=new MemoryStream()){using(var w=new BinaryWriter(m,Encoding.UTF8,true)){w.Write(count);w.Write(new byte[2]);}m.Position=0;
using(var r=new BinaryReader(m))Assert.IsInstanceOf<ArgumentException>(Assert.Throws<TargetInvocationException>(()=>read.Invoke(null,new object[]{r})).InnerException);}
}
}}
