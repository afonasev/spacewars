using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using Spacewars.BalanceLab;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;
using UnityEngine;

namespace Spacewars.Presentation
{
    // Capabilities come from trusted bootstrap code, never from a profile or a writable flag.
    public sealed class NativeBalanceStore
    {
        public const string DefaultId="spacewars-default";
        // Legacy import tag; runtime AI identity belongs to its balance profile.
        public const string AiProfileId="ai-openings-midgame-v1";
        public const string CorridorExperimentId="spacewars-certified-group-corridor115-v1";
        public const int CorridorExperimentVersion=10;
        public const string ProjectRelativePath="design/native-lab/profiles.json";
        private readonly AtomicStore disk;
        private readonly PlayableProfile baseline;
        private readonly JObject package;
        private StoreRead read;
        private string selectedId,activeId;
        private PlayableProfile activeProfile;
        private GameplayAudioProfile activeAudio;
        private ModelDetailProfile activeModelDetail;
        private AiProfile activeAiProfile;
        private int selectedVersion,activeVersion;
        public bool Authoring {get;}
        public string StoragePath=>disk.Path;
        public string Problem {get;private set;}
        public string SelectedId=>selectedId;
        public int SelectedVersion=>selectedVersion;
        public string ActiveIdentity=>activeId==null?"Нет активной версии":Name(activeId)+" ["+activeId+"] @"+activeVersion;
        public bool HasActive=>activeId!=null;
        public JObject Document=>read?.Payload??Empty();
        private JToken PackagedCorridorExperiment=>package["profiles"].SingleOrDefault(p=>(string)p["id"]==CorridorExperimentId);
        public JArray Profiles {
            get {
                var packaged=((JArray)package["profiles"]).Children();
                if(Authoring&&read==null)return (JArray)package["profiles"].DeepClone();
                var authored=((JArray)Document["profiles"]).Children();
                if(Authoring&&PackagedCorridorExperiment is JToken activation){
                    var existing=authored.SingleOrDefault(p=>(string)p["id"]==CorridorExperimentId);
                    if(existing!=null&&!MatchesPackagedHistory(existing,activation))throw new FormatException("История использует защищённый ID профиля групповых маршрутов; данные сохранены без изменений.");
                    return new JArray(authored.Where(p=>(string)p["id"]!=CorridorExperimentId).Concat(new[]{activation}).Select(p=>p.DeepClone()));
                }
                return new JArray((Authoring?Enumerable.Empty<JToken>():packaged).Concat(authored).Select(p=>p.DeepClone()));
            }
        }
        private static bool MatchesPackagedHistory(JToken stored,JToken packaged)
        {
            if(JToken.DeepEquals(stored,packaged))return true;
            var revisions=new System.Collections.Generic.HashSet<int>(stored["versions"].Select(row=>(int)row["revision"]));
            var expected=packaged.DeepClone();
            expected["versions"]=new JArray(packaged["versions"].Where(row=>revisions.Contains((int)row["revision"])));
            expected["release"]=new JArray(packaged["release"].Where(mark=>revisions.Contains((int)mark)));
            return JToken.DeepEquals(stored,expected);
        }
        public JObject SelectedProfile=>(JObject)Profiles.Single(p=>(string)p["id"]==selectedId);
        public bool CanEdit=>read!=null&&!(selectedId==CorridorExperimentId&&PackagedCorridorExperiment!=null)&&(Authoring||!((JArray)package["profiles"]).Any(p=>(string)p["id"]==selectedId));
        public JArray Versions=>(JArray)SelectedProfile["versions"];
        public JObject SelectedRow=>(JObject)Versions.Single(v=>(int)v["revision"]==selectedVersion);
        public string SelectedName=>Name(selectedId);
        // Compatibility view for retained native callers; immutable storage is profile-scoped.
        public JObject State=>new JObject{["revisions"]=Versions,["selected"]=selectedVersion};
        public PlayableProfile Selected=>Compile(SelectedRow);
        public PlayableProfile Active=>HasActive?activeProfile:throw new InvalidOperationException("Выберите корректную сохранённую версию или восстановите релизный Default.");
        public GameplayAudioProfile SelectedAudio=>Audio(SelectedRow);
        public ModelDetailProfile SelectedModelDetail=>Detail(SelectedRow);
        public ModelDetailProfile ActiveModelDetail=>HasActive?activeModelDetail.Copy():throw new InvalidOperationException("Нет активной версии LOD.");
        public GameplayAudioProfile ActiveAudio=>HasActive?JsonUtility.FromJson<GameplayAudioProfile>(JsonUtility.ToJson(activeAudio)):throw new InvalidOperationException("Нет активной версии.");
        public AiProfile ActiveAiProfile=>HasActive?new AiProfile(activeAiProfile.CopyData()):throw new InvalidOperationException("Нет активной версии.");
        public AiProfile SelectedAiProfile=>ReadAiProfile(SelectedRow,selectedId);
        private static JObject Empty()=>new JObject{["schema"]=2,["defaultId"]=DefaultId,["profiles"]=new JArray()};
        public static NativeBalanceStore ForApplication(PlayableProfile baseline,string qaPath=null)
        {
#if UNITY_EDITOR
            var result=new NativeBalanceStore(baseline,qaPath??Path.GetFullPath(Path.Combine(Application.dataPath,"../..",ProjectRelativePath)),true,legacyPath:qaPath==null?Path.Combine(Application.persistentDataPath,"native-balance-v1.json"):null,startOnRelease:true);
#else
            var result=new NativeBalanceStore(baseline,qaPath??Path.Combine(Application.persistentDataPath,"native-balance-v2.json"),false,legacyPath:qaPath==null?Path.Combine(Application.persistentDataPath,"native-balance-v1.json"):null,startOnRelease:true);
#endif
            if(qaPath==null&&PlayerPrefs.HasKey(NativeGameplayAudioProfile.Key))try{result.ImportLegacyAudio(PlayerPrefs.GetString(NativeGameplayAudioProfile.Key));}catch(Exception e){result.Problem="Прежний звуковой эксперимент сохранён без изменений: "+e.Message;}
            if(result.HasActive){NativeGameplayAudioProfile.UseSession(result.ActiveAudio);ModelDetailProfile.UseSession(result.ActiveModelDetail);}
            return result;
        }
        public NativeBalanceStore(PlayableProfile baseline,string path=null,bool authoring=true,JObject package=null,string legacyPath=null,bool startOnRelease=false)
        {
            this.baseline=baseline;Authoring=authoring;
            this.package=(JObject)ContractCodec.Parse(ContractCodec.Write(package??LoadPackage(baseline)));ValidateShape(this.package);
            disk=new AtomicStore(path??Path.Combine(Application.persistentDataPath,"native-balance-v2.json"),()=>authoring?(JObject)this.package.DeepClone():Empty());
            Reload();
            if(read!=null&&legacyPath!=null&&File.Exists(legacyPath)&&!((JArray)(Document["legacyImports"]??new JArray())).Any(v=>(string)v==ContractCodec.Hash(File.ReadAllBytes(legacyPath))))
            {
                try{ImportLegacy(legacyPath);}catch(Exception e){Problem="Старая история сохранена без изменений: "+e.Message;}
            }
            ResetInitial(startOnRelease);
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
        private JObject ReleaseRow(bool forDefaults=false)
        {
            var source=Authoring&&read!=null?Document:package;var p=source["profiles"].SingleOrDefault(v=>(string)v["id"]==DefaultId);
            if(p==null||!p["release"].Any())throw new InvalidOperationException("Нет релизной версии Default. Поставьте отметку у корректной версии Default.");
            // Existing sparse versions continue inheriting the same values after a new Default is released.
            var defaultsVersion=forDefaults?(int?)(source["profileDefaultsVersion"]??package["profileDefaultsVersion"]):null;
            int version=defaultsVersion??p["release"].Max(v=>(int)v);
            return (JObject)p["versions"].Single(v=>(int)v["revision"]==version);
        }
        public string ReleaseProblem {get{try{Compile(ReleaseRow(),DefaultId);Audio(ReleaseRow());Detail(ReleaseRow());ReadAiProfile(ReleaseRow(),DefaultId);return null;}catch(Exception e){return e.Message;}}}
        private void ResetInitial(bool startOnRelease)
        {
            try{var selection=startOnRelease?null:Document["selection"] as JObject;string id=(string)selection?["id"];int? version=(int?)selection?["version"];if(id!=null&&version.HasValue&&Profiles.Any(p=>(string)p["id"]==id&&p["versions"].Any(v=>(int)v["revision"]==version.Value))){Select(id,version.Value);return;}}
            catch(Exception e){Problem=Problem??"Последняя сохранённая версия недоступна: "+e.Message;}
            try{var row=ReleaseRow();Select(DefaultId,(int)row["revision"]);}
            catch(Exception e){Problem=Problem??e.Message;var p=Profiles.FirstOrDefault();if(p!=null){selectedId=(string)p["id"];selectedVersion=p["versions"].Max(v=>(int)v["revision"]);}activeId=null;}
        }
        public void ResetRelease(){var row=ReleaseRow();Select(DefaultId,(int)row["revision"]);}
        public PlayableProfile Compile(JToken row)=>Compile(row,selectedId??DefaultId);
        private static JObject ProfileJson(PlayableProfileData data){var json=JObject.Parse(JsonUtility.ToJson(data));if(data.navigationMaxAccruedCredits==0)json.Remove(nameof(PlayableProfileData.navigationMaxAccruedCredits));return json;}
        private static void ImportLegacyMaxThreshold(JObject defaults,JObject values)
        {
            const string legacy="orbitalMaxThresholdMultiplier",absolute="orbitalThresholdMax",previous="orbitalThreshold5";
            double Number(JToken token,string name,double min,double max)
            {
                if(token==null||(token.Type!=JTokenType.Integer&&token.Type!=JTokenType.Float))throw new FormatException("Несовместимый тип: "+name);
                double value=(double)token;
                if(double.IsNaN(value)||double.IsInfinity(value)||value<min||value>max)throw new FormatException("Недопустимый параметр: "+name);
                return value;
            }
            // Compile a copy: historical rows retain their original bytes and effective value.
            // The retired schema default (2) is fixed import provenance, not a live balance knob.
            if(values.Property(legacy)!=null){double multiplier=Number(values[legacy],legacy,1,10);CheckStep(multiplier,2,.1,legacy);}
            if(values.Property(absolute)==null&&(values.Property(legacy)!=null||values.Property(previous)!=null||defaults.Property(legacy)!=null))
            {
                double multiplier=Number(values[legacy]??defaults[legacy]??new JValue(2),legacy,1,10);CheckStep(multiplier,2,.1,legacy);
                values[absolute]=Number(values[previous]??defaults[previous],previous,1,100000)*multiplier;
            }
            values.Remove(legacy);defaults.Remove(legacy);
        }
        private PlayableProfile Compile(JToken row,string id)
        {
            var defaults=JObject.Parse(JsonUtility.ToJson(baseline.CopyData()));
            JObject released=null;try{released=(JObject)ReleaseRow(forDefaults:true)["data"];}catch(InvalidOperationException){}
            if(released!=null)foreach(var p in released.Properties())defaults[p.Name]=p.Value.DeepClone();
            var values=(JObject)row["data"].DeepClone();ImportLegacyMaxThreshold(defaults,values);
            // Resolve old missing terms from this row's effective parents, even if the
            // application baseline or a future catalog default already has explicit values.
            foreach(var field in PlayableProfileMetadata.NewUnitTermFields)
                defaults.Remove(field.FieldName);
            // Historical rows omitted relay income when its original fixed default was 10.
            // Preserve that historical term; revised profiles store their income explicitly.
            if(values.Property(nameof(PlayableProfileData.relayIncomePerPeriod))==null)defaults[nameof(PlayableProfileData.relayIncomePerPeriod)]=10;
            // Historical mine profiles paid a permanent full rate.
            if(values.Property(nameof(PlayableProfileData.mineReserveCredits))==null)defaults[nameof(PlayableProfileData.mineReserveCredits)]=0;
            if(values.Property(nameof(PlayableProfileData.mineDepletedIncomePercent))==null)defaults[nameof(PlayableProfileData.mineDepletedIncomePercent)]=20;
            var known=typeof(PlayableProfileData).GetFields().ToDictionary(f=>f.Name);
            // Old saved rows are v1, not an inheritance of future release defaults.
            // Presence is deliberate: explicit null/invalid values still validate below.
            if(values.Property(nameof(PlayableProfileData.navigationMaxAccruedCredits))==null)defaults.Remove(nameof(PlayableProfileData.navigationMaxAccruedCredits));
            if(values.Property(nameof(PlayableProfileData.groupCorridorMaximumExcessPercent))==null)defaults[nameof(PlayableProfileData.groupCorridorMaximumExcessPercent)]=0;
            if(values.Property(nameof(PlayableProfileData.crowdMotionHeadingIntervals))==null)defaults[nameof(PlayableProfileData.crowdMotionHeadingIntervals)]=NavigationProfile.CrowdV1HeadingIntervals;
            if(values.Property(nameof(PlayableProfileData.crowdMotionSpeedBands))==null)defaults[nameof(PlayableProfileData.crowdMotionSpeedBands)]=NavigationProfile.CrowdV1SpeedBands;
            if(values.Property(nameof(PlayableProfileData.crowdMotionLookaheadSeconds))==null)defaults[nameof(PlayableProfileData.crowdMotionLookaheadSeconds)]=NavigationProfile.CrowdV1LookaheadSeconds;
            if(values.Property(nameof(PlayableProfileData.crowdMotionNeighborCap))==null)defaults[nameof(PlayableProfileData.crowdMotionNeighborCap)]=NavigationProfile.CrowdV1NeighborCap;
            if(values.Property(nameof(PlayableProfileData.shkvalGuidanceFireIntervalMultiplier))==null)
                defaults[nameof(PlayableProfileData.shkvalGuidanceFireIntervalMultiplier)]=1;
            // A legacy row projects its own effective values, including when
            // the current released row already has independent terms.
            if(values.Property(nameof(PlayableProfileData.orbitalProgressionVersion))==null)
                defaults[nameof(PlayableProfileData.orbitalProgressionVersion)]=0;
            if(values.Property(nameof(PlayableProfileData.independentLabStatsVersion))==null)
                defaults[nameof(PlayableProfileData.independentLabStatsVersion)]=0;
            foreach(var p in values.Properties())
            {
                if(!known.TryGetValue(p.Name,out var f))throw new FormatException("Удалённый параметр: "+p.Name);
                bool numeric=f.FieldType==typeof(double)||f.FieldType==typeof(float)||f.FieldType==typeof(int);
                if(numeric&&(p.Value.Type!=JTokenType.Integer&&p.Value.Type!=JTokenType.Float)||!numeric&&f.FieldType==typeof(string)&&p.Value.Type!=JTokenType.String&&p.Value.Type!=JTokenType.Null)throw new FormatException("Несовместимый тип: "+p.Name);
                if(f.FieldType==typeof(int)&&(double)p.Value!=Math.Truncate((double)p.Value))throw new FormatException("Требуется целое число: "+p.Name);
                if(p.Name==nameof(PlayableProfileData.navigationMaxAccruedCredits)&&((double)p.Value<PlayableProfileMetadata.AccruedCreditField.Minimum||(double)p.Value>PlayableProfileMetadata.AccruedCreditField.Maximum))throw new FormatException("Недопустимый лимит накопленного бюджета навигации.");
                defaults[p.Name]=p.Value.DeepClone();
            }
            // The backend belongs to this application's new-match engine,
            // not to editable Lab balance. Keep old rows exact on disk while
            // materializing them with the current startup baseline. Invalid
            // serialized engine values still fail instead of being normalized.
            var backend=defaults[nameof(PlayableProfileData.groundRouteBackend)];
            if(backend==null||(backend.Type!=JTokenType.Integer&&backend.Type!=JTokenType.Float)||((double)backend!=0&&(double)backend!=1))throw new FormatException("Неизвестный навигационный backend в сохранённом профиле.");
            defaults[nameof(PlayableProfileData.groundRouteBackend)]=baseline.UsesRecastGroundRoutes?1:0;
            // Import old relative profiles once into explicit independent terms.
            // Saved copies include these fields, so subsequent mobile tuning is isolated.
            void TowerDefault(string field,double value){if(values.Property(field)==null&&released?.Property(field)==null)defaults[field]=value;}
            double Number(string field)=>(double)defaults[field];
            TowerDefault("turretHealth",Number("tankHealth")*Number("turretHealthMultiplier"));
            TowerDefault("turretRange",Number("explorerRange")*Number("turretRangeMultiplier"));
            TowerDefault("turretDamage",Number("explorerDamage"));
            TowerDefault("turretBurstSize",Number("explorerAssaultBurstSize"));
            TowerDefault("turretBurstShotIntervalMs",Number("explorerBurstShotIntervalMs"));
            TowerDefault("turretBurstPauseMs",Number("explorerBurstPauseMs"));
            TowerDefault("turretSpreadDeg",Number("explorerAssaultSpreadDeg"));
            TowerDefault("turretProjectileSpeed",Number("explorerProjectileSpeed"));
            TowerDefault("gaussDamage",Number("tankHealth")*Number("gaussTankDamageFraction"));
            double oldRange=Number("explorerRange")*Number("turretRangeMultiplier");
            TowerDefault("gaussRange",oldRange+(Number("shkvalBaseRange")-oldRange)*Number("gaussRangeBlend"));
            var data=JsonUtility.FromJson<PlayableProfileData>(defaults.ToString());data.revision=(int)row["revision"];
            PlayableProfileMetadata.ProjectIndependentLabStats(data);
            foreach(var field in PlayableProfileMetadata.NewUnitTermFields)
                if(values.Property(field.FieldName)!=null&&(double)values[field.FieldName]==0)
                    throw new FormatException("Недопустимый параметр: "+field.Path);
            var baselineData=baseline.CopyData();
            foreach(var f in PlayableProfileMetadata.Fields.Where(NativeBalanceFields.Editable).Except(PlayableProfileMetadata.IndependentLabFields))
                CheckStep(f.Read(data),f.Read(baselineData),f.Step,f.Path);
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
        private ModelDetailProfile Detail(JToken row)
        {
            var values=JObject.Parse(Resources.Load<TextAsset>("ModelDetailProfile").text);
            void Apply(JToken data){if(data==null)return;if(!(data is JObject obj))throw new FormatException("Неверный профиль modelDetail.");foreach(var pair in obj.Properties()){
                var field=typeof(ModelDetailProfile).GetField(pair.Name);if(field==null||field.IsStatic)throw new FormatException("Неизвестный параметр modelDetail."+pair.Name);
                if(field.FieldType==typeof(string)){if(pair.Value.Type!=JTokenType.String)throw new FormatException("Ожидается строка modelDetail."+pair.Name);}
                else if(pair.Value.Type!=JTokenType.Float&&pair.Value.Type!=JTokenType.Integer)throw new FormatException("Ожидается число modelDetail."+pair.Name);
                if(field.FieldType==typeof(int)&&(double)pair.Value!=Math.Truncate((double)pair.Value))throw new FormatException("Ожидается целое modelDetail."+pair.Name);
                values[pair.Name]=pair.Value.DeepClone();
            }}
            JToken release=null;try{release=ReleaseRow()["modelDetail"];}catch(InvalidOperationException){}Apply(release);Apply(row["modelDetail"]);
            var profile=JsonUtility.FromJson<ModelDetailProfile>(values.ToString());profile.Validate();return profile;
        }
        private static JObject AiJson(AiProfileData data)=>JObject.Parse(JsonUtility.ToJson(data));
        private AiProfile ReadAiProfile(JToken row,string id)
        {
            var data=NativeBalanceFields.InitialAiOpeningProfile().CopyData();data.id=id;data.revision=(int)row["revision"];
            var saved=row["aiProfile"] as JObject;
            if(saved==null)return new AiProfile(data);
            if(saved["schemaVersion"]!=null&&(int)saved["schemaVersion"]!=data.schemaVersion)throw new FormatException("Неподдерживаемая схема AI-профиля.");
            if(saved["id"]!=null&&(string)saved["id"]!=AiProfileId&&(string)saved["id"]!=id)throw new FormatException("Сохранён другой AI-профиль.");
            if(saved["revision"]!=null)VersionNumber(saved["revision"]); // Validate legacy metadata; the containing row owns the version.
            if((string)saved["id"]==id&&(int?)saved["revision"]!=data.revision)
                throw new FormatException("Версия ИИ не совпадает с общей версией профиля.");
            var values=(saved["fields"] as JArray)?.ToDictionary(v=>(string)v["path"],v=>v,StringComparer.Ordinal);
            if(saved["fields"]!=null&&values==null)throw new FormatException("Повреждены параметры AI-профиля.");
            if(values!=null)
            {
                var fields=AiProfileMetadata.Fields.ToDictionary(f=>f.Path,StringComparer.Ordinal);
                if(values.Keys.Any(path=>!fields.ContainsKey(path)))throw new FormatException("Сохранён неизвестный параметр AI-профиля.");
                foreach(var field in data.fields)
                {
                    if(!values.TryGetValue(field.path,out var token))continue;
                    var value=token["value"];if(value==null||(value.Type!=JTokenType.Integer&&value.Type!=JTokenType.Float))throw new FormatException("Некорректное значение AI-профиля: "+field.path);
                    field.value=(double)value;fields[field.path].Validate(field.value);
                }
            }
            return new AiProfile(data);
        }
        public void Select(string id,int version)
        {
            var row=Row(id,version);selectedId=id;selectedVersion=version;var compiled=Compile(row,id);var sound=Audio(row);var detail=Detail(row);var ai=ReadAiProfile(row,id);activeModelDetail=detail;activeId=id;activeVersion=version;activeProfile=compiled;activeAudio=sound;activeAiProfile=ai;
        }
        public void SelectProfile(string id){var p=Profiles.Single(v=>(string)v["id"]==id);Select(id,p["versions"].Max(v=>(int)v["revision"]));}
        public void Select(int version)=>Select(selectedId,version);
        public PlayableProfile Resolve(int version)=>Compile(Row(selectedId,version));
        private JObject Mutable(out JObject doc)
        {
            if(read==null)throw new InvalidOperationException(Problem);if(!CanEdit)throw new InvalidOperationException("Профиль поставки защищён. Создайте независимую копию.");doc=Document;return (JObject)doc["profiles"].Single(p=>(string)p["id"]==selectedId);
        }
        private void Commit(JObject doc){if(read==null)throw new InvalidOperationException(Problem);ValidateShape(doc);read=disk.Commit(read.Token,doc);Problem=null;}
        public PlayableProfile Save(PlayableProfileData draft,GameplayAudioProfile audio,AiProfileData aiDraft=null,ModelDetailProfile modelDetail=null)
        {
            if(draft.groundRouteBackend!=(baseline.UsesRecastGroundRoutes?1:0))throw new ArgumentException("Навигационный backend не редактируется в лаборатории.");
            var p=Mutable(out var doc);var baseData=Selected.CopyData();var baseAudio=SelectedAudio;var baseDetail=SelectedModelDetail;var detail=(modelDetail??baseDetail).Copy();var detailFields=detail.Parameters().ToArray();var baseDetailFields=baseDetail.Parameters().ToArray();bool detailChanged=detailFields.Where((v,i)=>v.Read()!=baseDetailFields[i].Read()).Any();var baseAi=SelectedAiProfile;var ai=aiDraft==null?baseAi:new AiProfile(aiDraft);if(ai.Id!=selectedId&&ai.Id!=AiProfileId)throw new ArgumentException("Параметры ИИ принадлежат другому профилю.");bool aiChanged=NativeBalanceFields.AiOpeningFields.Any(f=>ai.Value(f.Path)!=baseAi.Value(f.Path));if(PlayableProfileMetadata.Fields.Where(NativeBalanceFields.Editable).All(f=>f.Read(draft)==f.Read(baseData))&&GameplayAudioMetadata.Fields.All(f=>f.Read(audio)==f.Read(baseAudio))&&!aiChanged&&!detailChanged)throw new ArgumentException("Нет изменений для новой версии.");var data=NativeBalanceFields.Copy(draft);data.revision=p["versions"].Max(v=>(int)v["revision"])+1;
            PlayableProfileMetadata.ProjectNewUnitTerms(data);
            aiDraft=ai.CopyData();aiDraft.id=selectedId;aiDraft.revision=data.revision;
            ai=new AiProfile(aiDraft);
            var row=new JObject{["revision"]=data.revision,["date"]=DateTime.Now.ToString("yyyy-MM-dd"),["data"]=ProfileJson(data),["audio"]=JObject.Parse(JsonUtility.ToJson(audio)),["aiProfile"]=AiJson(aiDraft),["modelDetail"]=JObject.Parse(JsonUtility.ToJson(detail))};
            var compiled=Compile(row);Audio(row);Detail(row);
            for(int i=0;i<detailFields.Length;i++)CheckStep(detailFields[i].Read(),baseDetailFields[i].Read(),detailFields[i].Step,detailFields[i].Path);
            foreach(var f in PlayableProfileMetadata.Fields.Where(NativeBalanceFields.Editable))CheckStep(f.Read(data),f.Read(baseData),f.Step,f.Path);
            foreach(var f in GameplayAudioMetadata.Fields)CheckStep(f.Read(audio),f.Read(baseAudio),f.Step,f.Path);
            foreach(var f in NativeBalanceFields.AiOpeningFields)CheckStep(ai.Value(f.Path),baseAi.Value(f.Path),f.Step,f.Path);
            ((JArray)p["versions"]).Add(row);doc["selection"]=new JObject{["id"]=selectedId,["version"]=data.revision};Commit(doc);Select(selectedId,data.revision);return compiled;
        }
        public static void CheckStep(double value,double origin,double step,string key){double n=(value-origin)/step;if(double.IsNaN(n)||double.IsInfinity(n)||Math.Abs(n-Math.Round(n))>1e-4)throw new ArgumentException(key+": значение не соответствует шагу "+step);}
        public PlayableProfile Save(string name,PlayableProfileData draft){if(name!=SelectedName)Copy(name);return Save(draft,SelectedAudio);}
        public void Copy(string name)
        {
            ValidateName(name);var id="local-"+Guid.NewGuid().ToString("N");var row=(JObject)SelectedRow.DeepClone();row["revision"]=1;row["date"]=DateTime.Now.ToString("yyyy-MM-dd");row["data"]=ProfileJson(Selected.CopyData());row["audio"]=JObject.Parse(JsonUtility.ToJson(SelectedAudio));row["modelDetail"]=JObject.Parse(JsonUtility.ToJson(SelectedModelDetail));
            var ai=SelectedAiProfile.CopyData();ai.id=id;ai.revision=1;row["aiProfile"]=AiJson(ai);row["data"]["revision"]=1;row.Remove("releaseVersion");
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
            if(!Authoring)throw new InvalidOperationException("Релизные отметки доступны только автору.");if(marked){Compile(SelectedRow);Audio(SelectedRow);Detail(SelectedRow);ReadAiProfile(SelectedRow,selectedId);}var p=Mutable(out var doc);var marks=(JArray)p["release"];
            if(marked&&!IsMarked)marks.Add(selectedVersion);if(!marked)marks.Where(v=>(int)v==selectedVersion).ToList().ForEach(v=>v.Remove());Commit(doc);
        }
        public JObject ExportPackage()
        {
            if(!Authoring)throw new InvalidOperationException("Экспорт доступен только автору.");if(Problem!=null)throw new InvalidOperationException(Problem);if(ReleaseProblem!=null)throw new InvalidOperationException(ReleaseProblem);
            var result=Empty();
            var defaultsVersion=Document["profileDefaultsVersion"]??package["profileDefaultsVersion"];
            if(defaultsVersion!=null)result["profileDefaultsVersion"]=defaultsVersion.DeepClone();
            foreach(var profile in Profiles.Where(p=>p["release"].Any()))
            {
                var copy=(JObject)profile.DeepClone();
                int? compatibilityVersion=(string)profile["id"]==DefaultId?(int?)defaultsVersion:null;
                copy["versions"]=new JArray(profile["versions"].Where(row=>
                    (int)row["revision"]==compatibilityVersion||profile["release"].Any(mark=>(int)mark==(int)row["revision"])).Select(row=>row.DeepClone()));
                foreach(var row in copy["versions"]){Compile(row,(string)profile["id"]);Audio(row);Detail(row);ReadAiProfile(row,(string)profile["id"]);}
                ((JArray)result["profiles"]).Add(copy);
            }
            return result;
        }
        // A shipping catalog has one self-contained snapshot. Authoring/diagnostic
        // exports above retain history and must never be used as build input.
        public JObject ExportReleasePackage(string releaseVersion)
        {
            if(!Authoring)throw new InvalidOperationException("Экспорт доступен только автору.");
            if(string.IsNullOrWhiteSpace(releaseVersion)||!System.Text.RegularExpressions.Regex.IsMatch(releaseVersion,@"^\d+\.\d+\.\d+$"))
                throw new ArgumentException("Нужна версия релиза в формате X.Y.Z.");
            if(Problem!=null)throw new InvalidOperationException(Problem);
            if(ReleaseProblem!=null)throw new InvalidOperationException(ReleaseProblem);
            var source=ReleaseRow();int revision=(int)source["revision"];
            var row=new JObject{
                ["revision"]=revision,["date"]=source["date"]?.DeepClone(),["releaseVersion"]=releaseVersion,
                ["data"]=ProfileJson(Compile(source,DefaultId).CopyData()),
                ["audio"]=JObject.Parse(JsonUtility.ToJson(Audio(source))),
                ["aiProfile"]=AiJson(ReadAiProfile(source,DefaultId).CopyData()),
                ["modelDetail"]=JObject.Parse(JsonUtility.ToJson(Detail(source)))
            };
            var result=Empty();result["releaseVersion"]=releaseVersion;result["profileDefaultsVersion"]=revision;
            ((JArray)result["profiles"]).Add(new JObject{["id"]=DefaultId,["name"]="Spacewars "+releaseVersion,
                ["release"]=new JArray(revision),["versions"]=new JArray(row)});
            ValidateShape(result);return result;
        }
        public void ImportLegacyAudio(string text)
        {
            string hash=ContractCodec.Hash(System.Text.Encoding.UTF8.GetBytes(text));var doc=Document;
            if(((JArray)(doc["legacyAudioImports"]??new JArray())).Any(v=>(string)v==hash))return;
            var row=(JObject)ReleaseRow().DeepClone();row["revision"]=1;row["date"]=null;row["audio"]=JObject.Parse(text);Audio(row);
            string id="legacy-audio-"+hash.Substring(0,20);
            var ai=ReadAiProfile(ReleaseRow(),DefaultId).CopyData();ai.id=id;ai.revision=1;row["aiProfile"]=AiJson(ai);row["data"]["revision"]=1;row.Remove("releaseVersion");
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
