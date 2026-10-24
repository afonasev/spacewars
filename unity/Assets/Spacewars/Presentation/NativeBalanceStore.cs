using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using Spacewars.BalanceLab;
using Spacewars.Simulation;
using UnityEngine;

namespace Spacewars.Presentation
{
    // Capabilities come from trusted bootstrap code, never from a profile or a writable flag.
    public sealed class NativeBalanceStore
    {
        public const string DefaultId="spacewars-default";
        public const string NewMatchProfileId="spacewars-certified-group-corridor115-v1";
        public const int NewMatchProfileVersion=1;
        public const string ProjectRelativePath="design/native-lab/profiles.json";
        private readonly AtomicStore disk;
        private readonly PlayableProfile baseline;
        private readonly JObject package;
        private StoreRead read;
        private string selectedId,activeId;
        private PlayableProfile activeProfile;
        private GameplayAudioProfile activeAudio;
        private int selectedVersion,activeVersion;
        public bool Authoring {get;}
        public string StoragePath=>disk.Path;
        public string Problem {get;private set;}
        public string SelectedId=>selectedId;
        public int SelectedVersion=>selectedVersion;
        public string ActiveIdentity=>activeId==null?"Нет активной версии":Name(activeId)+" ["+activeId+"] @"+activeVersion;
        public bool HasActive=>activeId!=null;
        public JObject Document=>read?.Payload??Empty();
        private JToken PackagedNewMatchProfile=>package["profiles"].SingleOrDefault(p=>(string)p["id"]==NewMatchProfileId);
        public JArray Profiles {
            get {
                var packaged=((JArray)package["profiles"]).Children();
                if(Authoring&&read==null)return (JArray)package["profiles"].DeepClone();
                var authored=((JArray)Document["profiles"]).Children();
                if(Authoring&&PackagedNewMatchProfile is JToken activation){
                    var existing=authored.SingleOrDefault(p=>(string)p["id"]==NewMatchProfileId);
                    if(existing!=null&&!JToken.DeepEquals(existing,activation))throw new FormatException("История использует защищённый ID профиля групповых маршрутов; данные сохранены без изменений.");
                    return new JArray(authored.Where(p=>(string)p["id"]!=NewMatchProfileId).Concat(new[]{activation}).Select(p=>p.DeepClone()));
                }
                return new JArray((Authoring?Enumerable.Empty<JToken>():packaged).Concat(authored).Select(p=>p.DeepClone()));
            }
        }
        public JObject SelectedProfile=>(JObject)Profiles.Single(p=>(string)p["id"]==selectedId);
        public bool CanEdit=>read!=null&&!(selectedId==NewMatchProfileId&&PackagedNewMatchProfile!=null)&&(Authoring||!((JArray)package["profiles"]).Any(p=>(string)p["id"]==selectedId));
        public JArray Versions=>(JArray)SelectedProfile["versions"];
        public JObject SelectedRow=>(JObject)Versions.Single(v=>(int)v["revision"]==selectedVersion);
        public string SelectedName=>Name(selectedId);
        // Compatibility view for retained native callers; immutable storage is profile-scoped.
        public JObject State=>new JObject{["revisions"]=Versions,["selected"]=selectedVersion};
        public PlayableProfile Selected=>Compile(SelectedRow);
        public PlayableProfile Active=>HasActive?activeProfile:throw new InvalidOperationException("Выберите корректную сохранённую версию или восстановите релизный Default.");
        public GameplayAudioProfile SelectedAudio=>Audio(SelectedRow);
        public GameplayAudioProfile ActiveAudio=>HasActive?JsonUtility.FromJson<GameplayAudioProfile>(JsonUtility.ToJson(activeAudio)):throw new InvalidOperationException("Нет активной версии.");
        private static JObject Empty()=>new JObject{["schema"]=2,["defaultId"]=DefaultId,["profiles"]=new JArray()};
        public static NativeBalanceStore ForApplication(PlayableProfile baseline,string qaPath=null)
        {
#if UNITY_EDITOR
            var result=new NativeBalanceStore(baseline,qaPath??Path.GetFullPath(Path.Combine(Application.dataPath,"../..",ProjectRelativePath)),true,legacyPath:qaPath==null?Path.Combine(Application.persistentDataPath,"native-balance-v1.json"):null);
#else
            var result=new NativeBalanceStore(baseline,qaPath??Path.Combine(Application.persistentDataPath,"native-balance-v2.json"),false,legacyPath:qaPath==null?Path.Combine(Application.persistentDataPath,"native-balance-v1.json"):null);
#endif
            if(qaPath==null&&PlayerPrefs.HasKey(NativeGameplayAudioProfile.Key))try{result.ImportLegacyAudio(PlayerPrefs.GetString(NativeGameplayAudioProfile.Key));}catch(Exception e){result.Problem="Прежний звуковой эксперимент сохранён без изменений: "+e.Message;}
            // The application selects the new immutable version only when creating
            // its menu store. Existing runtimes retain their own profile objects.
            if(result.PackagedNewMatchProfile!=null)result.Select(NewMatchProfileId,NewMatchProfileVersion);
            if(result.HasActive)NativeGameplayAudioProfile.UseSession(result.ActiveAudio);
            return result;
        }
        public NativeBalanceStore(PlayableProfile baseline,string path=null,bool authoring=true,JObject package=null,string legacyPath=null)
        {
            this.baseline=baseline;Authoring=authoring;
            this.package=(JObject)ContractCodec.Parse(ContractCodec.Write(package??LoadPackage(baseline)));ValidateShape(this.package);
            disk=new AtomicStore(path??Path.Combine(Application.persistentDataPath,"native-balance-v2.json"),()=>authoring?(JObject)this.package.DeepClone():Empty());
            Reload();
            if(read!=null&&legacyPath!=null&&File.Exists(legacyPath)&&!((JArray)(Document["legacyImports"]??new JArray())).Any(v=>(string)v==ContractCodec.Hash(File.ReadAllBytes(legacyPath))))
            {
                try{ImportLegacy(legacyPath);}catch(Exception e){Problem="Старая история сохранена без изменений: "+e.Message;}
            }
            ResetInitial();
        }
        private static JObject LoadPackage(PlayableProfile baseline)
        {
            var resource=Resources.Load<TextAsset>("NativeLabCatalog");if(resource!=null)return JObject.Parse(resource.text);
            return new JObject{["schema"]=2,["defaultId"]=DefaultId,["profiles"]=new JArray(new JObject{["id"]=DefaultId,["name"]="Default",["release"]=new JArray(1),["versions"]=new JArray(new JObject{["revision"]=1,["date"]=null,["data"]=JObject.Parse(JsonUtility.ToJson(baseline.CopyData())),["audio"]=JObject.Parse(Resources.Load<TextAsset>("GameplayAudioProfile").text)})})};
        }
        private static void ValidateShape(JObject doc)
        {
            if((int?)doc["schema"]!=2||(string)doc["defaultId"]!=DefaultId||!(doc["profiles"] is JArray profiles))throw new FormatException("Неверный формат каталога лаборатории.");
            var ids=new System.Collections.Generic.HashSet<string>();
            foreach(var p in profiles)
            {
                if(p["id"]?.Type!=JTokenType.String||p["name"]?.Type!=JTokenType.String)throw new FormatException("ID и имя профиля должны быть строками.");
                var id=(string)p["id"];if(string.IsNullOrWhiteSpace(id)||!ids.Add(id))throw new FormatException("Повтор или пустой ID профиля.");ValidateName((string)p["name"]);
                if(!(p["versions"] is JArray versions)||versions.Count==0||!(p["release"] is JArray marks))throw new FormatException("Профиль без истории или отметок.");
                var numbers=versions.Select(v=>VersionNumber(v["revision"])).ToArray();if(numbers.Any(n=>n<1)||numbers.Distinct().Count()!=numbers.Length||versions.Any(v=>!(v["data"] is JObject)))throw new FormatException("Повреждена история версий.");
                if(marks.Select(VersionNumber).Distinct().Count()!=marks.Count||marks.Any(m=>!numbers.Contains((int)m)))throw new FormatException("Отметка ссылается на отсутствующую версию.");
            }
        }
        private static int VersionNumber(JToken token)
        {
            if(token==null||(token.Type!=JTokenType.Integer&&token.Type!=JTokenType.Float))throw new FormatException("Номер версии должен быть целым числом.");
            double value=(double)token;if(double.IsNaN(value)||double.IsInfinity(value)||value<1||value>int.MaxValue||value!=Math.Truncate(value))throw new FormatException("Номер версии должен быть положительным целым числом.");return (int)value;
        }
        private static void ValidateName(string name){if(string.IsNullOrWhiteSpace(name)||name.Length>64)throw new ArgumentException("Название: от 1 до 64 символов.");}
        public void Reload()
        {
            try{
                var next=disk.Read();ValidateShape(next.Payload);
                if(!Authoring&&((JArray)next.Payload["profiles"]).Any(p=>((JArray)package["profiles"]).Any(b=>(string)b["id"]==(string)p["id"])||((JArray)p["release"]).Count>0))throw new FormatException("Локальная история пытается изменить каталог поставки.");
                read=next;Problem=null;
            }catch(Exception e){Problem="История недоступна и не перезаписана: "+e.Message;}
        }
        private string Name(string id)=>(string)Profiles.Single(p=>(string)p["id"]==id)["name"];
        private JObject Row(string id,int version)=>(JObject)Profiles.Single(p=>(string)p["id"]==id)["versions"].Single(v=>(int)v["revision"]==version);
        private JObject ReleaseRow()
        {
            var source=Authoring&&read!=null?Document:package;var p=source["profiles"].SingleOrDefault(v=>(string)v["id"]==DefaultId);
            if(p==null||!p["release"].Any())throw new InvalidOperationException("Нет релизной версии Default. Поставьте отметку у корректной версии Default.");
            int version=p["release"].Max(v=>(int)v);return (JObject)p["versions"].Single(v=>(int)v["revision"]==version);
        }
        public string ReleaseProblem {get{try{Compile(ReleaseRow(),DefaultId);Audio(ReleaseRow());return null;}catch(Exception e){return e.Message;}}}
        private void ResetInitial()
        {
            try{var row=ReleaseRow();Select(DefaultId,(int)row["revision"]);}
            catch(Exception e){Problem=Problem??e.Message;var p=Profiles.FirstOrDefault();if(p!=null){selectedId=(string)p["id"];selectedVersion=p["versions"].Max(v=>(int)v["revision"]);}activeId=null;}
        }
        public void ResetRelease(){var row=ReleaseRow();Select(DefaultId,(int)row["revision"]);}
        public PlayableProfile Compile(JToken row)=>Compile(row,selectedId??DefaultId);
        private static JObject ProfileJson(PlayableProfileData data){var json=JObject.Parse(JsonUtility.ToJson(data));if(data.navigationMaxAccruedCredits==0)json.Remove(nameof(PlayableProfileData.navigationMaxAccruedCredits));return json;}
        private PlayableProfile Compile(JToken row,string id)
        {
            var defaults=JObject.Parse(JsonUtility.ToJson(baseline.CopyData()));
            JObject released=null;try{released=(JObject)ReleaseRow()["data"];}catch(InvalidOperationException){}
            if(released!=null)foreach(var p in released.Properties())defaults[p.Name]=p.Value.DeepClone();
            var values=(JObject)row["data"];var known=typeof(PlayableProfileData).GetFields().ToDictionary(f=>f.Name);
            // Old saved rows are v1, not an inheritance of future release defaults.
            // Presence is deliberate: explicit null/invalid values still validate below.
            if(values.Property(nameof(PlayableProfileData.navigationMaxAccruedCredits))==null)defaults.Remove(nameof(PlayableProfileData.navigationMaxAccruedCredits));
            if(values.Property(nameof(PlayableProfileData.groupCorridorMaximumExcessPercent))==null)defaults[nameof(PlayableProfileData.groupCorridorMaximumExcessPercent)]=0;
            if(values.Property(nameof(PlayableProfileData.crowdMotionHeadingIntervals))==null)defaults[nameof(PlayableProfileData.crowdMotionHeadingIntervals)]=NavigationProfile.CrowdV1HeadingIntervals;
            if(values.Property(nameof(PlayableProfileData.crowdMotionSpeedBands))==null)defaults[nameof(PlayableProfileData.crowdMotionSpeedBands)]=NavigationProfile.CrowdV1SpeedBands;
            if(values.Property(nameof(PlayableProfileData.crowdMotionLookaheadSeconds))==null)defaults[nameof(PlayableProfileData.crowdMotionLookaheadSeconds)]=NavigationProfile.CrowdV1LookaheadSeconds;
            if(values.Property(nameof(PlayableProfileData.crowdMotionNeighborCap))==null)defaults[nameof(PlayableProfileData.crowdMotionNeighborCap)]=NavigationProfile.CrowdV1NeighborCap;
            foreach(var p in values.Properties())
            {
                if(!known.TryGetValue(p.Name,out var f))throw new FormatException("Удалённый параметр: "+p.Name);
                bool numeric=f.FieldType==typeof(double)||f.FieldType==typeof(float)||f.FieldType==typeof(int);
                if(numeric&&(p.Value.Type!=JTokenType.Integer&&p.Value.Type!=JTokenType.Float)||!numeric&&f.FieldType==typeof(string)&&p.Value.Type!=JTokenType.String&&p.Value.Type!=JTokenType.Null)throw new FormatException("Несовместимый тип: "+p.Name);
                if(f.FieldType==typeof(int)&&(double)p.Value!=Math.Truncate((double)p.Value))throw new FormatException("Требуется целое число: "+p.Name);
                if(p.Name==nameof(PlayableProfileData.navigationMaxAccruedCredits)&&((double)p.Value<PlayableProfileMetadata.AccruedCreditField.Minimum||(double)p.Value>PlayableProfileMetadata.AccruedCreditField.Maximum))throw new FormatException("Недопустимый лимит накопленного бюджета навигации.");
                defaults[p.Name]=p.Value.DeepClone();
            }
            var data=JsonUtility.FromJson<PlayableProfileData>(defaults.ToString());data.revision=(int)row["revision"];
            foreach(var f in PlayableProfileMetadata.Fields.Where(NativeBalanceFields.Editable))CheckStep(f.Read(data),f.Read(baseline.CopyData()),f.Step,f.Path);
            var result=PlayableProfile.Create(data,baseline.AuthoredMap,Name(id)+" · "+id+" @"+(int)row["revision"]);
            var error=NativeBalanceFields.ValidateLiveDifference(baseline,result);if(error!=null)throw new FormatException(error);return result;
        }
        private GameplayAudioProfile Audio(JToken row)
        {
            var values=JObject.Parse(Resources.Load<TextAsset>("GameplayAudioProfile").text);
            try{if(ReleaseRow()["audio"] is JObject release)foreach(var p in release.Properties())values[p.Name]=p.Value.DeepClone();}catch(InvalidOperationException){}
            if(row["audio"] is JObject audio)foreach(var p in audio.Properties())
            {
                var field=typeof(GameplayAudioProfile).GetField(p.Name);if(field==null)throw new FormatException("Удалённый параметр audio."+p.Name);
                if(field.FieldType!=typeof(string)&&p.Value.Type!=JTokenType.Float&&p.Value.Type!=JTokenType.Integer)throw new FormatException("Несовместимый тип audio."+p.Name);if(field.FieldType==typeof(int)&&(double)p.Value!=Math.Truncate((double)p.Value))throw new FormatException("Требуется целое число: audio."+p.Name);values[p.Name]=p.Value.DeepClone();
            }
            var result=JsonUtility.FromJson<GameplayAudioProfile>(values.ToString());result.Validate();foreach(var f in GameplayAudioMetadata.Fields)f.Validate(f.Read(result));return result;
        }
        public void Select(string id,int version)
        {
            var row=Row(id,version);selectedId=id;selectedVersion=version;var compiled=Compile(row,id);var sound=Audio(row);activeId=id;activeVersion=version;activeProfile=compiled;activeAudio=sound;
        }
        public void SelectProfile(string id){var p=Profiles.Single(v=>(string)v["id"]==id);Select(id,p["versions"].Max(v=>(int)v["revision"]));}
        public void Select(int version)=>Select(selectedId,version);
        public PlayableProfile Resolve(int version)=>Compile(Row(selectedId,version));
        private JObject Mutable(out JObject doc)
        {
            if(read==null)throw new InvalidOperationException(Problem);if(!CanEdit)throw new InvalidOperationException("Профиль поставки защищён. Создайте независимую копию.");doc=Document;return (JObject)doc["profiles"].Single(p=>(string)p["id"]==selectedId);
        }
        private void Commit(JObject doc){if(read==null)throw new InvalidOperationException(Problem);ValidateShape(doc);read=disk.Commit(read.Token,doc);Problem=null;}
        public PlayableProfile Save(PlayableProfileData draft,GameplayAudioProfile audio)
        {
            var p=Mutable(out var doc);var baseData=Selected.CopyData();var baseAudio=SelectedAudio;if(PlayableProfileMetadata.Fields.Where(NativeBalanceFields.Editable).All(f=>f.Read(draft)==f.Read(baseData))&&GameplayAudioMetadata.Fields.All(f=>f.Read(audio)==f.Read(baseAudio)))throw new ArgumentException("Нет изменений для новой версии.");var data=NativeBalanceFields.Copy(draft);data.revision=p["versions"].Max(v=>(int)v["revision"])+1;
            var row=new JObject{["revision"]=data.revision,["date"]=DateTime.Now.ToString("yyyy-MM-dd"),["data"]=ProfileJson(data),["audio"]=JObject.Parse(JsonUtility.ToJson(audio))};
            var compiled=Compile(row);Audio(row);
            foreach(var f in PlayableProfileMetadata.Fields.Where(NativeBalanceFields.Editable))CheckStep(f.Read(data),f.Read(baseData),f.Step,f.Path);
            foreach(var f in GameplayAudioMetadata.Fields)CheckStep(f.Read(audio),f.Read(baseAudio),f.Step,f.Path);
            ((JArray)p["versions"]).Add(row);Commit(doc);Select(selectedId,data.revision);return compiled;
        }
        public static void CheckStep(double value,double origin,double step,string key){double n=(value-origin)/step;if(double.IsNaN(n)||double.IsInfinity(n)||Math.Abs(n-Math.Round(n))>1e-4)throw new ArgumentException(key+": значение не соответствует шагу "+step);}
        public PlayableProfile Save(string name,PlayableProfileData draft){if(name!=SelectedName)Copy(name);return Save(draft,SelectedAudio);}
        public void Copy(string name)
        {
            ValidateName(name);var id="local-"+Guid.NewGuid().ToString("N");var row=(JObject)SelectedRow.DeepClone();row["revision"]=1;row["date"]=DateTime.Now.ToString("yyyy-MM-dd");row["data"]=ProfileJson(Selected.CopyData());row["audio"]=JObject.Parse(JsonUtility.ToJson(SelectedAudio));
            var doc=Document;((JArray)doc["profiles"]).Add(new JObject{["id"]=id,["name"]=name.Trim(),["release"]=new JArray(),["versions"]=new JArray(row)});Commit(doc);Select(id,1);
        }
        public void Rename(string name){ValidateName(name);var p=Mutable(out var doc);p["name"]=name.Trim();Commit(doc);if(activeId==selectedId&&activeProfile!=null)activeProfile=PlayableProfile.Create(activeProfile.CopyData(),activeProfile.AuthoredMap,name.Trim()+" · "+activeId+" @"+activeVersion);}
        public void Delete()
        {
            var p=Mutable(out var doc);if(selectedId==DefaultId)throw new InvalidOperationException("Основной Default нельзя удалить.");bool wasActive=selectedId==activeId;if(wasActive)ReleaseRow();p.Remove();Commit(doc);if(wasActive)ResetRelease();else{selectedId=activeId??DefaultId;selectedVersion=activeId!=null?activeVersion:Profiles.Single(v=>(string)v["id"]==DefaultId)["versions"].Max(v=>(int)v["revision"]);}
        }
        public bool IsMarked=>SelectedProfile["release"].Any(v=>(int)v==selectedVersion);
        public void Mark(bool marked)
        {
            if(!Authoring)throw new InvalidOperationException("Релизные отметки доступны только автору.");if(marked){Compile(SelectedRow);Audio(SelectedRow);}var p=Mutable(out var doc);var marks=(JArray)p["release"];
            if(marked&&!IsMarked)marks.Add(selectedVersion);if(!marked)marks.Where(v=>(int)v==selectedVersion).ToList().ForEach(v=>v.Remove());Commit(doc);
        }
        public JObject ExportPackage()
        {
            if(!Authoring)throw new InvalidOperationException("Экспорт доступен только автору.");if(Problem!=null)throw new InvalidOperationException(Problem);if(ReleaseProblem!=null)throw new InvalidOperationException(ReleaseProblem);
            var result=Empty();foreach(var p in Profiles.Where(p=>p["release"].Any()))
            {var copy=(JObject)p.DeepClone();copy["versions"]=new JArray(p["versions"].Where(v=>p["release"].Any(m=>(int)m==(int)v["revision"])).Select(v=>v.DeepClone()));foreach(var v in copy["versions"]){Compile(v,(string)p["id"]);Audio(v);}((JArray)result["profiles"]).Add(copy);}return result;
        }
        public void ImportLegacyAudio(string text)
        {
            string hash=ContractCodec.Hash(System.Text.Encoding.UTF8.GetBytes(text));var doc=Document;
            if(((JArray)(doc["legacyAudioImports"]??new JArray())).Any(v=>(string)v==hash))return;
            var row=(JObject)ReleaseRow().DeepClone();row["revision"]=1;row["date"]=null;row["audio"]=JObject.Parse(text);Audio(row);
            string id="legacy-audio-"+hash.Substring(0,20);
            ((JArray)doc["profiles"]).Add(new JObject{["id"]=id,["name"]="Звук · прежний эксперимент",["release"]=new JArray(),["versions"]=new JArray(row)});
            doc["legacyAudioImports"]=new JArray(((JArray)(doc["legacyAudioImports"]??new JArray())).Select(v=>v.DeepClone()).Concat(new[]{new JValue(hash)}));Commit(doc);
        }
        private void ImportLegacy(string path)
        {
            var legacy=new AtomicStore(path,()=>new JObject()).Read().Payload;if((int?)legacy["schema"]!=1)throw new FormatException("Неизвестная старая схема.");var doc=Document;
            foreach(var old in legacy["revisions"])
            {
                string id="legacy-"+ContractCodec.Hash(System.Text.Encoding.UTF8.GetBytes(old.ToString())).Substring(0,20);
                if(doc["profiles"].Any(p=>(string)p["id"]==id))continue;
                ((JArray)doc["profiles"]).Add(new JObject{["id"]=id,["name"]=(string)old["name"],["release"]=new JArray(),["versions"]=new JArray(new JObject{["revision"]=old["revision"].DeepClone(),["date"]=old["date"]?.DeepClone(),["data"]=old["data"].DeepClone()})});
            }
            doc["legacyImports"]=new JArray(((JArray)(doc["legacyImports"]??new JArray())).Select(v=>v.DeepClone()).Concat(new[]{new JValue(ContractCodec.Hash(File.ReadAllBytes(path)))}));
            Commit(doc);
        }
    }
}
