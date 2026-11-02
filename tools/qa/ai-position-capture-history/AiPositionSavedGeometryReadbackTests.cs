using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class AiPositionSavedGeometryReadbackTests
    {
        [Test,Timeout(60000)]
        public void OriginalRepairCaptureGeometryRowsWithoutWorldOrTicks()
        {
            string path=Path.GetFullPath(".local/ai-position-ready/repair-3d54335cc/current.bin");
            byte[] input=File.ReadAllBytes(path);
            using(var sha=System.Security.Cryptography.SHA256.Create())Assert.AreEqual("3100f2538ac7e9733240cdb3e8b13db6337dc9f92e6600815cf42717ef052bcf",BitConverter.ToString(sha.ComputeHash(input)).Replace("-","").ToLowerInvariant());
            using var state=PlayableWorldState.Decode(input);
            using var nav=WorldWire.Unpack(state.Navigation,WorldWire.ReadNavigationSessionState);
            using var stream=new MemoryStream(state.Domain,false);using var reader=new BinaryReader(stream);
            int Count(){int n=reader.ReadInt32();Assert.That(n,Is.InRange(0,10000));return n;}
            double Number()=>WorldWire.Number(reader);
            bool Bool()=>WorldWire.Boolean(reader);
            int terms=reader.ReadInt32(),version=0;bool crowd=terms==-1;
            if(crowd){version=reader.ReadInt32();terms=Count();}Assert.That(terms,Is.InRange(1,10000));
            for(int i=0;i<terms;i++)
            {
                reader.ReadInt32();WorldWire.String(reader);
                foreach(var field in PlayableProfileMetadata.PreCrowdFields.Concat(PlayableProfileMetadata.WreckFields).Concat(PlayableProfileMetadata.HawkFields).Concat(PlayableProfileMetadata.FactoryFields).Concat(PlayableProfileMetadata.TankReverseFields))Number();
                if(crowd)foreach(var field in PlayableProfileMetadata.CrowdFields)Number();
                if(version==2&&Bool())reader.ReadInt32();
            }
            bool patrol=Bool();int? winner=Bool()?(int?)reader.ReadInt32():null;
            int eliminated=Count();for(int i=0;i<eliminated;i++)reader.ReadInt32();
            int sequences=Count();for(int i=0;i<sequences;i++){reader.ReadInt32();reader.ReadInt64();}
            int accounts=Count();for(int i=0;i<accounts;i++){reader.ReadInt32();Number();}
            reader.ReadInt64();reader.ReadInt64();int nextId=reader.ReadInt32();reader.ReadInt32();reader.ReadInt64();reader.ReadInt64();Assert.AreEqual(state.Tick,reader.ReadInt64());
            int incomes=Count();for(int i=0;i<incomes;i++){reader.ReadInt32();Number();}
            Number();Number();Number();Number();int outcome=reader.ReadInt32();
            const BindingFlags flags=BindingFlags.Static|BindingFlags.NonPublic;
            var readUnit=typeof(PlayableDomain).GetMethod("ReadUnit",flags);var readBuilding=typeof(PlayableDomain).GetMethod("ReadBuilding",flags);
            object Field(object value,string name)=>value.GetType().GetField(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).GetValue(value);
            object Facts(object value,params string[] fields)=>fields.ToDictionary(k=>k,k=>Field(value,k));
            var units=new List<object>();int count=Count();for(int i=0;i<count;i++)units.Add(readUnit.Invoke(null,new object[]{reader}));
            var buildings=new List<object>();count=Count();for(int i=0;i<count;i++)buildings.Add(readBuilding.Invoke(null,new object[]{reader}));
            var expected=buildings.Where(b=>(ConstructionPhase)Field(b,"Phase")!=ConstructionPhase.Pending).Select(b=>
            {var at=(NavPoint)Field(b,"Position");double r=TerritoryRules.Radius(PlayableProfile.Default,(PlayableBuildingKind)Field(b,"Kind"));return new NavObstacle(at.X-r,at.Z-r,at.X+r,at.Z+r);}).ToArray();
            var actual=nav.Geometry.Obstacles;
            var missing=expected.Select((o,i)=>new{obstacle=o,index=i}).Where(x=>!actual.Any(o=>o.GeometryEquals(x.obstacle))).ToArray();
            var extra=actual.Select((o,i)=>new{obstacle=o,index=i}).Where(x=>!expected.Any(o=>o.GeometryEquals(x.obstacle))).ToArray();
            var rows=new{state.Version,state.Seed,state.Generation,state.Tick,patrol,winner,outcome,nextId,geometry=nav.Geometry,
                expected,missing,extra,buildingRows=buildings.Select(b=>Facts(b,"Id","Kind","Owner","ParentId","Phase","Health","Position","Healing")),
                unitRows=units.Select(u=>Facts(u,"Id","Kind","Owner","Health","CurrentOrder","LastOrder")),domainPrefixBytes=stream.Position,domainLength=stream.Length,
                qualification="Authenticated primitive prefix via actual readers only; no world, host, ticks, restore patch or geometry relaxation. Wreck suffix not parsed; extras not assigned a cause."};
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(path),"geometry-rows.json"),Newtonsoft.Json.JsonConvert.SerializeObject(rows,Newtonsoft.Json.Formatting.Indented));
            Assert.AreEqual(2703,state.Tick);Assert.Greater(buildings.Count,0);Assert.Greater(units.Count,0);
            CollectionAssert.AreEqual(input,File.ReadAllBytes(path),"readback cannot change input");
        }
    }
}
