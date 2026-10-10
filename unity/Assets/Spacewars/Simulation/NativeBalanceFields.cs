using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Spacewars.Simulation.Ai;

namespace Spacewars.Simulation
{
    // The laboratory edits only governed current-native rules, never map/topology.
    public static class NativeBalanceFields
    {
        // AI has an independent revision and numeric DTO; gameplay writers never mutate it.
        public static IReadOnlyList<AiProfileField> AiFields=>AiProfileMetadata.Fields;
        public static IReadOnlyList<AiProfileField> AiRosterFields=>AiRosterCatalog.Metadata;
        public static void Write(AiProfileField descriptor,AiProfileData data,double value)
        {
            if(descriptor==null||data?.fields==null||!ReferenceEquals(AiProfileMetadata.Field(descriptor.Path),descriptor))throw new ArgumentException("Unregistered AI field.");
            descriptor.Validate(value);var field=data.fields.Single(f=>f.path==descriptor.Path);field.value=value;
            // Cross-field validation occurs when the immutable candidate snapshot is constructed.
        }
        public static void Write(AiProfileField descriptor,AiRosterDescriptorData[] data,double value)
        {
            if(descriptor==null||data==null||!AiRosterFields.Contains(descriptor))throw new ArgumentException("Unregistered roster field.");
            descriptor.Validate(value);var parts=descriptor.Path.Split('.');
            var target=data.Single(d=>d.kind.ToString()==parts[1]);
            typeof(AiRosterDescriptorData).GetField(parts[2]).SetValue(target,value);
        }
        private static readonly Dictionary<string,FieldInfo> Fields=typeof(PlayableProfileData).GetFields().ToDictionary(f=>f.Name);
        public static PlayableProfileData Copy(PlayableProfileData source)
        {var target=new PlayableProfileData();foreach(var f in Fields.Values)f.SetValue(target,f.GetValue(source));return target;}
        public static void Write(PlayableProfileField descriptor,PlayableProfileData data,double value)
        {
            if(double.IsNaN(value)||double.IsInfinity(value)||value<descriptor.Minimum||value>descriptor.Maximum)throw new ArgumentOutOfRangeException(descriptor.Path);
            if(!Fields.TryGetValue(descriptor.FieldName??"",out var field))throw new InvalidOperationException("Missing native field writer: "+descriptor.Path);
            if(field.FieldType==typeof(int)){if(value!=Math.Truncate(value))throw new ArgumentException("Введите целое число.");field.SetValue(data,(int)value);}
            else field.SetValue(data,value);
        }
        public static bool Editable(PlayableProfileField field)
        {
            string name=field.FieldName??"";
            if(field.Group=="Arena"||field.Group=="Slots"||field.Group=="Minimap"||field.Group=="Fog presentation"||field.Group=="Camera"||field.Group=="Navigation"||field.Group=="Rendering"||field.Group=="Input"||field.Group=="Presentation")return false;
            if(name.StartsWith("minimap")||name.StartsWith("fog")||name=="visionCellSize"||name=="startingCredits"||name=="matchScoreEarnedCreditsDivisor"||name=="ballisticWallHeight"||name=="factoryExitDistance"||name=="defaultRallyDistance"||name=="evacuationClearance"||name=="shkvalBuildingCollisionHeight"||name=="shkvalProjectileRadius")return false;
            if(name=="shkvalMarkerStartRadius"||name=="shkvalMarkerOpacity")return true;
            if(name.Contains("Marker")||name.Contains("Tracer")||name.Contains("Muzzle")||name=="shkvalArcHeight"||name=="shkvalLaunchHeight")return false;
            if(name.Contains("CollisionRadius")||name.Contains("FootprintRadius")||name.Contains("ModelRadius")||name.Contains("ModelScale")||name.Contains("ModelHeight"))return false;
            return true;
        }
        public static string ValidateLiveDifference(PlayableProfile before,PlayableProfile after)
        {
            var a=before.CopyData();var b=after.CopyData();
            foreach(var field in PlayableProfileMetadata.Fields)
                if(!Editable(field)&&field.Read(a)!=field.Read(b))return "Параметр не доступен в лаборатории: "+field.Path;
            if(!ReferenceEquals(before.AuthoredMap,after.AuthoredMap))return "Карта не может изменяться в лаборатории.";
            return null;
        }
    }
}
