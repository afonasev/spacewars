using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using Spacewars.BalanceLab;
using Spacewars.Simulation;
using UnityEngine;

namespace Spacewars.Presentation
{
    public sealed class NativeBalanceStore
    {
        private readonly AtomicStore store;
        private readonly PlayableProfile baseline;
        private StoreRead read;
        public JObject State=>read.Payload;
        public NativeBalanceStore(PlayableProfile baseline,string path=null)
        {
            this.baseline=baseline;
            store=new AtomicStore(path??System.IO.Path.Combine(Application.persistentDataPath,"native-balance-v1.json"),()=>new JObject{
                ["schema"]=1,["selected"]=baseline.Revision,["revisions"]=new JArray(Row(baseline.DisplayName,baseline.CopyData()))});
            Reload();
        }
        private static JObject Row(string name,PlayableProfileData data)=>new JObject{["name"]=name,["revision"]=data.revision,["data"]=JObject.Parse(JsonUtility.ToJson(data))};
        public void Reload()
        {
            read=store.Read();var state=read.Payload;
            if((int?)state["schema"]!=1||!(state["revisions"] is JArray rows)||rows.Count==0)throw new FormatException("Неверный формат лаборатории.");
            var ids=new System.Collections.Generic.HashSet<int>();
            foreach(var row in rows){var compiled=Compile(row);if(!ids.Add(compiled.Revision))throw new FormatException("Повтор ревизии.");}
            if(!ids.Contains((int)state["selected"]))throw new FormatException("Выбранная ревизия отсутствует.");
        }
        public PlayableProfile Compile(JToken row)
        {
            var data=JsonUtility.FromJson<PlayableProfileData>(row["data"].ToString());
            if(row["data"]["matchScoreEarnedCreditsDivisor"]==null)data.matchScoreEarnedCreditsDivisor=2;
            if(data.revision!=(int)row["revision"])throw new FormatException("Ревизия не соответствует данным.");
            var result=PlayableProfile.Create(data,baseline.AuthoredMap,(string)row["name"]);
            var error=NativeBalanceFields.ValidateLiveDifference(baseline,result);if(error!=null)throw new FormatException(error);return result;
        }
        public PlayableProfile Resolve(int revision)=>Compile(State["revisions"].Single(r=>(int)r["revision"]==revision));
        public PlayableProfile Selected=>Resolve((int)State["selected"]);
        public PlayableProfile Save(string name,PlayableProfileData draft)
        {
            if(string.IsNullOrWhiteSpace(name)||name.Length>64)throw new ArgumentException("Название: от 1 до 64 символов.");
            var state=State;var data=NativeBalanceFields.Copy(draft);data.revision=state["revisions"].Max(r=>(int)r["revision"])+1;
            var row=Row(name.Trim(),data);var result=Compile(row);((JArray)state["revisions"]).Add(row);
            read=store.Commit(read.Token,state);return result;
        }
        public void Select(int revision){Resolve(revision);var state=State;state["selected"]=revision;read=store.Commit(read.Token,state);}
    }
}
