using System;
using System.IO;
using System.Linq;
using Spacewars.Runtime;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    internal static class QueueLegacyLayout
    {
        // Synthetic layout exclusion test. Independent actual producer files are tested separately.
        internal static byte[] Convert(byte[] bytes,PlayableProfile profile,bool preA1,int actorCount)
        {
            var world=PlayableWorldState.Decode(bytes);var assembly=typeof(PlayableRuntime).Assembly;
            world.Binding=(byte[])assembly.GetType("Spacewars.Runtime.WorldWire",true).GetMethod("Binding",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic).Invoke(null,new object[]{profile,true});
            var legacyFields=(PlayableProfileField[])assembly.GetType("Spacewars.Runtime.LegacyQueueCodec",true).GetProperty("Fields",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic).GetValue(null);
            var legacyPaths=legacyFields.Select(field=>field.Path).ToArray();
            using(var input=new MemoryStream(world.Domain))using(var reader=new BinaryReader(input))using(var output=new MemoryStream())using(var writer=new BinaryWriter(output)){
                int count=reader.ReadInt32();writer.Write(count);
                for(int k=0;k<count;k++){writer.Write(reader.ReadInt32());int size=reader.ReadInt32();writer.Write(size);if(size>=0)writer.Write(reader.ReadBytes(size));foreach(var field in PlayableProfileMetadata.Fields){var value=reader.ReadBytes(8);if(legacyPaths.Contains(field.Path))writer.Write(value);}}
                int start=(int)input.Position,tail=Tag(world.Domain,0x53505231);writer.Write(world.Domain.Skip(start).Take(tail-start).ToArray());world.Domain=output.ToArray();
            }
            int group=Tag(world.Navigation,0x47525031);
            if(BitConverter.ToInt32(world.Navigation,group+4)>=7){
                var current=WorldWire.Unpack(world.Navigation,WorldWire.ReadNavigationSessionState);
                int marchTail=4+current.Groups.Sum(g=>37+12*g.Members.Length);
                world.Navigation=world.Navigation.Take(world.Navigation.Length-marchTail).ToArray();
                BitConverter.GetBytes(6).CopyTo(world.Navigation,group+4);
            }
            if(BitConverter.ToInt32(world.Navigation,group+4)>=4){
                using(var stream=new MemoryStream(world.Navigation))using(var reader=new BinaryReader(stream)){
                    var state=(NavigationSessionState)assembly.GetType("Spacewars.Runtime.WorldWire",true).GetMethod("ReadNavigationSessionState",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic).Invoke(null,new object[]{reader});
                    if(state.Crowd.Units.Any(u=>u.FailedRequest!=0))throw new ArgumentException("Synthetic legacy actor trim requires no failure payload.");
                    int tail=12+8*state.GroupKeys.Length+4*state.MemberGroupKeys.Length+state.GroupLayouts.Sum(layout=>4+4*layout.Length);world.Navigation=world.Navigation.Take(world.Navigation.Length-tail).ToArray();
                }
            }
            if(preA1)world.Navigation=world.Navigation.Take(group).ToArray();
            else {BitConverter.GetBytes(2).CopyTo(world.Navigation,group+4);world.Navigation=world.Navigation.Take(world.Navigation.Length-4-37*actorCount).ToArray();}
            return world.Encode();
        }
        // Positive prior queue codec fixture: v3 carried only TargetId among the
        // optional command constructor fields. Build its exact byte layout explicitly.
        internal static byte[] QueueFourToThree(byte[] domain)
        {
            int tag=Tag(domain,0x54514631);using(var input=new MemoryStream(domain))using(var r=new BinaryReader(input))using(var output=new MemoryStream())using(var w=new BinaryWriter(output)){
                w.Write(domain.Take(tag).ToArray());input.Position=tag;w.Write(r.ReadInt32());r.ReadInt32();w.Write(3);w.Write(r.ReadInt64());int count=r.ReadInt32();w.Write(count);
                void Text(){int size=r.ReadInt32();w.Write(size);if(size>=0)w.Write(r.ReadBytes(size));}
                for(int i=0;i<count;i++){w.Write(r.ReadBytes(48));Text();w.Write(r.ReadBytes(24));r.ReadBytes(20);w.Write(r.ReadInt32());r.ReadBytes(16);w.Write(r.ReadInt32());Text();w.Write(r.ReadBytes(16));bool location=r.ReadBoolean();w.Write(location);if(location){w.Write(r.ReadBytes(16));Text();}int ids=r.ReadInt32();w.Write(ids);w.Write(r.ReadBytes(ids*4));}
                w.Write(r.ReadBytes((int)(input.Length-input.Position)));return output.ToArray();
            }
        }
        internal static int Tag(byte[] bytes,int tag){for(int k=0;k<bytes.Length-3;k++)if(BitConverter.ToInt32(bytes,k)==tag)return k;throw new ArgumentException("Missing test layout tag.");}
    }
}
