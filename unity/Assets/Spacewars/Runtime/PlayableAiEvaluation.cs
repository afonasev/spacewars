using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    // Diagnostic serialization, never an input to policy or player presentation.
    public static class PlayableAiCanonical
    {
        public static string Hash(string text)
        {using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-","").ToLowerInvariant();}
        public static string Encode(object value)
        {
            if(value==null)return "null";
            if(value is string s)return s.Length.ToString(CultureInfo.InvariantCulture)+":"+s;
            var type=value.GetType();
            if(value is double d)return d.ToString("R",CultureInfo.InvariantCulture);
            if(value is float f32)return f32.ToString("R",CultureInfo.InvariantCulture);
            if(type.IsEnum||type.IsPrimitive||value is decimal)return Convert.ToString(value,CultureInfo.InvariantCulture);
            if(value is IEnumerable list){var b=new StringBuilder("[");foreach(var item in list)b.Append(Encode(item)).Append(';');return b.Append(']').ToString();}
            var parts=new SortedDictionary<string,object>(StringComparer.Ordinal);
            foreach(var p in type.GetProperties(BindingFlags.Instance|BindingFlags.Public))if(p.GetIndexParameters().Length==0)parts.Add(p.Name,p.GetValue(value));
            foreach(var f in type.GetFields(BindingFlags.Instance|BindingFlags.Public))parts.Add(f.Name,f.GetValue(value));
            return type.Name+"{"+string.Join(";",parts.Select(p=>p.Key+"="+Encode(p.Value)))+"}";
        }
    }

    public sealed class PlayableAiEvaluationEvent
    {
        public PlayableAiEvaluationEvent(PlayableAiObservation observation,PlayableAiAction action,string policy,long ordinal,long due,
            PlayableAiDeliveryStatus status,long sequence,long? delivery,long? receipt,long? application,PlayableCommandStatus? runtimeStatus,string message)
        {
            RuntimeStatus=runtimeStatus;Message=message;
            Owner=observation.OwnerId;Generation=observation.Generation;Ordinal=ordinal;Policy=policy;
            ObservationTick=observation.Tick;ActionTick=observation.Tick;DueTick=due;DeliveryTick=delivery;ReceiptTick=receipt;ApplicationTick=application;
            Status=status;Sequence=sequence;Observation=PlayableAiCanonical.Encode(observation);Action=PlayableAiCanonical.Encode(action);
            Kind=action.Kind;EntityIds=action.CopyEntityIds();
        }
        public PlayableCommandStatus? RuntimeStatus{get;} public string Message{get;}
        public string Owner{get;} public long Generation{get;} public long Ordinal{get;} public string Policy{get;}
        public long ObservationTick{get;} public long ActionTick{get;} public long DueTick{get;}
        public long? DeliveryTick{get;} public long? ReceiptTick{get;} public long? ApplicationTick{get;}
        public PlayableAiDeliveryStatus Status{get;} public long Sequence{get;}
        public string Observation{get;} public string Action{get;} public PlayableCommandKind Kind{get;}
        public int[] EntityIds{get;}
    }

}
