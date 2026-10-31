using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Spacewars.Simulation;
using Spacewars.BalanceLab;
using UnityEngine;
using UnityEngine.UIElements;
using Spacewars.Simulation.Ai;

namespace Spacewars.Presentation
{
    public sealed class NativeBalanceView
    {
        public VisualElement Page {get;}
        private readonly NativeBalanceStore store;
        private readonly NativeMenuNavigation navigation;
        private readonly Action closed;
        private readonly Action<PlayableProfile,GameplayAudioProfile> openProvingGround;
        private PlayableProfile selected;
        private PlayableProfileData draft,original;
        private AiProfileData aiDraft,aiOriginal;
        private GameplayAudioProfile audioDraft,audioOriginal;
        private ModelDetailProfile detailDraft,detailOriginal;
        private const string LodGroup="Детализация (LOD)";
        private readonly Dictionary<string,string> invalid=new Dictionary<string,string>();
        private bool dirty;
        private string section="Юниты",entity="Танк",expanded="Юниты";
        private const string Changes="Изменения";
        private static readonly string[] Categories={Changes,"Юниты","Здания","Экономика","Графика","Правила","ИИ","Звук"};
        private readonly Label context,status,help;
        private readonly TextField search;
        private readonly ScrollView fields;
        private readonly VisualElement groups,grid;
        private readonly DropdownField profiles,revisions;
        private readonly Button save,rename,delete,mark;
        public NativeBalanceView(VisualElement root,NativeMenuNavigation navigation,NativeBalanceStore store,Action closed,PlayableProfile currentMatch=null,Action<PlayableProfile,GameplayAudioProfile> openProvingGround=null)
        {
            if(currentMatch!=null)throw new InvalidOperationException("Лаборатория недоступна в активном матче.");
            this.navigation=navigation;this.store=store;this.closed=closed;this.openProvingGround=openProvingGround;
            Page=OrbitalTheme.Screen(root,"laboratory-screen");var card=OrbitalTheme.Card(Page);card.AddToClassList("orbital-lab-card");
            var title=OrbitalTheme.Text("ЛАБОРАТОРИЯ ГЕЙМДИЗАЙНА","orbital-title");card.Add(title);
            status=OrbitalTheme.Text("","orbital-muted");status.name="lab-status";card.Add(status);
            var controls=new VisualElement(){name="lab-controls"};controls.AddToClassList("lab-controls");card.Add(controls);
            var profileControls=new VisualElement(){name="lab-profile-controls"};profileControls.AddToClassList("lab-profile-controls");controls.Add(profileControls);
            profiles=new DropdownField("Профиль"){name="lab-profiles"};profileControls.Add(profiles);
            profileControls.Add(OrbitalTheme.Action("Копировать",()=>Guard(()=>NameDialog("Имя независимой копии",store.SelectedName+" · копия",v=>{store.Copy(v);Load();})),"lab-copy"));
            rename=OrbitalTheme.Action("Переименовать",()=>Guard(()=>NameDialog("Переименовать профиль",store.SelectedName,v=>{store.Rename(v);Load();})),"lab-rename");profileControls.Add(rename);
            delete=OrbitalTheme.Action("Удалить",()=>Guard(()=>Confirm("Удалить профиль «"+store.SelectedName+"» и всю его историю? Если профиль активен, следующая игра вернётся к релизному Default.",()=>{store.Delete();Load();})),"lab-delete");profileControls.Add(delete);
            var revisionControls=new VisualElement(){name="lab-revision-controls"};revisionControls.AddToClassList("lab-revision-controls");controls.Add(revisionControls);
            revisions=new DropdownField("Версия"){name="lab-revisions"};revisionControls.Add(revisions);
            save=OrbitalTheme.Action("Сохранить",Save,"lab-save",true);revisionControls.Add(save);
            revisionControls.Add(OrbitalTheme.Action("Очистить",()=>Guard(Load),"lab-reset"));
            mark=OrbitalTheme.Action("В релиз",()=>Guard(()=>{store.Mark(!store.IsMarked);Refresh();}),"lab-release");revisionControls.Add(mark);
            var searchRow=new VisualElement();searchRow.style.flexDirection=FlexDirection.Row;card.Add(searchRow);
            search=new TextField("Поиск"){name="lab-search"};search.style.flexGrow=1;search.RegisterValueChangedCallback(_=>RenderFields());searchRow.Add(search);
            var body=new VisualElement();body.style.flexDirection=FlexDirection.Row;body.style.flexGrow=1;body.style.minHeight=80;body.style.flexShrink=1;card.Add(body);
            var sidebar=new ScrollView(){name="lab-tree"};sidebar.style.width=238;sidebar.style.flexShrink=0;body.Add(sidebar);groups=sidebar.contentContainer;groups.AddToClassList("orbital-lab-groups");sidebar.horizontalScrollerVisibility=ScrollerVisibility.Hidden;groups.style.width=Length.Percent(100);
            fields=new ScrollView(){name="lab-parameters",horizontalScrollerVisibility=ScrollerVisibility.Hidden};fields.contentContainer.style.width=Length.Percent(100);fields.contentContainer.style.minWidth=0;fields.style.flexGrow=1;fields.style.minWidth=0;fields.style.marginLeft=10;body.Add(fields);
            grid=new VisualElement();grid.style.width=Length.Percent(100);grid.style.flexDirection=FlexDirection.Row;grid.style.flexWrap=Wrap.Wrap;grid.style.alignContent=Align.FlexStart;grid.style.alignItems=Align.FlexStart;fields.Add(grid);
            context=OrbitalTheme.Text("","lab-context");card.Insert(card.IndexOf(body),context);
            help=OrbitalTheme.Text("","lab-inline-help");help.name="lab-focus-help";help.style.width=Length.Percent(100);help.style.display=DisplayStyle.None;
            Page.RegisterCallback<GeometryChangedEvent>(_=>{bool compact=Page.resolvedStyle.width<900;LayoutColumns();sidebar.style.width=compact?190:238;title.style.fontSize=compact?18:22;title.style.marginBottom=4;card.style.paddingLeft=card.style.paddingRight=card.style.paddingTop=card.style.paddingBottom=10;});
            var actions=new VisualElement(){name="lab-actions"};actions.style.flexDirection=FlexDirection.Row;actions.style.flexShrink=0;actions.style.marginTop=8;card.Add(actions);
            actions.Add(OrbitalTheme.Action("Назад",Back,"lab-back"));
            if(openProvingGround!=null)actions.Add(OrbitalTheme.Action("Открыть полигон",()=>Try(OpenProvingGround),"lab-proving-ground",true));
            Load();
            profiles.RegisterValueChangedCallback(e=>{var id=(string)store.Profiles[profiles.choices.IndexOf(e.newValue)]["id"];Refresh();Guard(()=>{try{store.SelectProfile(id);}finally{Load();}});});
            revisions.RegisterValueChangedCallback(e=>{int index=revisions.choices.IndexOf(e.newValue);int version=(int)store.Versions.OrderBy(v=>(int)v["revision"]).ElementAt(index)["revision"];Refresh();Guard(()=>{try{store.Select(version);}finally{Load();}});});
            navigation.SetScope(Page,Back,profiles);
        }
        public void RestoreFromProvingGround()
        {Page.style.display=DisplayStyle.Flex;navigation.SetScope(Page,Back,Page.Q<Button>("lab-proving-ground"));}
        private void OpenProvingGround()
        {
            if(draft==null||invalid.Count>0)throw new ArgumentException("Исправьте ошибки параметров перед запуском полигона.");
            var snapshot=PlayableProfile.Create(NativeBalanceFields.Copy(draft),new ProvingGroundMap(),selected.DisplayName+" · полигон");
            var audio=JsonUtility.FromJson<GameplayAudioProfile>(JsonUtility.ToJson(audioDraft));
            audio.Validate();detailDraft.Validate();ModelDetailProfile.UseSession(detailDraft);openProvingGround(snapshot,audio);
        }
        private void Refresh()
        {
            profiles.choices=store.Profiles.Select(p=>(string)p["name"]+" · "+((string)p["id"]).Substring(0,Math.Min(12,((string)p["id"]).Length))).ToList();profiles.SetValueWithoutNotify(profiles.choices[store.Profiles.Select(p=>(string)p["id"]).ToList().IndexOf(store.SelectedId)]);
            revisions.choices=store.Versions.OrderBy(v=>(int)v["revision"]).Select(v=>((int)v["revision"])+((string)v["date"]==null?" · дата неизвестна":" · "+(string)v["date"])+(store.SelectedProfile["release"].Any(m=>(int)m==(int)v["revision"])?" · релиз":"")).ToList();
            revisions.SetValueWithoutNotify(revisions.choices[store.Versions.OrderBy(v=>(int)v["revision"]).Select(v=>(int)v["revision"]).ToList().IndexOf(store.SelectedVersion)]);
            rename.SetEnabled(store.CanEdit);delete.SetEnabled(store.CanEdit&&store.SelectedId!=NativeBalanceStore.DefaultId);mark.style.display=store.Authoring?DisplayStyle.Flex:DisplayStyle.None;mark.EnableInClassList("lab-release-selected",store.IsMarked);mark.text=store.IsMarked?"✓ В релиз":"В релиз";mark.tooltip=store.IsMarked?"Версия включена в релиз. Нажмите, чтобы снять отметку.":"Включить выбранную версию в релиз.";
            status.text=(store.Problem??store.ReleaseProblem)??"Следующая игра: "+store.ActiveIdentity+". "+(store.Authoring?"Авторские файлы: "+store.StoragePath+"; запись файла не является commit.":store.CanEdit?"Ваш локальный эксперимент.":"Профиль поставки защищён — создайте копию.");
            save.SetEnabled(dirty&&store.CanEdit);
        }
        private void Load()
        {
            try{selected=store.Selected;original=selected.CopyData();draft=selected.CopyData();aiOriginal=store.SelectedAiProfile.CopyData();aiDraft=store.SelectedAiProfile.CopyData();detailOriginal=store.SelectedModelDetail;detailDraft=detailOriginal.Copy();audioOriginal=store.SelectedAudio;audioDraft=JsonUtility.FromJson<GameplayAudioProfile>(JsonUtility.ToJson(audioOriginal));invalid.Clear();dirty=false;Refresh();RenderFields();}
            catch(Exception e){draft=null;original=null;invalid.Clear();dirty=false;Refresh();grid.Clear();status.text="Версия недоступна: "+e.Message+". Продолжает действовать: "+store.ActiveIdentity+". Исходные данные сохранены; выберите другую версию или релизный Default.";}
        }
        private static string Number(double value)=>value.ToString("0.###",CultureInfo.InvariantCulture);
        private static string AiRangeUnit(AiProfileField field)=>field.Unit switch{"weight"=>"баллов","buildings"=>"зданий","units"=>"юнитов","changes"=>"смен",_=>NativeBalanceLabels.Unit(field.Unit)};
        private static string AiUnit(AiProfileField field,double value)=>field.Unit switch
        {
            "weight"=>CountForm(value,"балл","балла","баллов"),"buildings"=>CountForm(value,"здание","здания","зданий"),
            "units"=>CountForm(value,"юнит","юнита","юнитов"),"changes"=>CountForm(value,"смена","смены","смен"),
            _=>NativeBalanceLabels.Unit(field.Unit)
        };
        private static string CountForm(double value,string one,string few,string many)
        {if(value!=Math.Truncate(value))return many;long number=Math.Abs((long)value);long lastTwo=number%100;if(lastTwo>=11&&lastTwo<=14)return many;switch(number%10){case 1:return one;case 2:case 3:case 4:return few;default:return many;}}
        private bool Changed(PlayableProfileField field)=>field.Read(original)!=field.Read(draft)||invalid.ContainsKey(field.Path);
        private bool Changed(AiProfileField field)=>aiOriginal!=null&&aiDraft!=null&&(aiOriginal.fields.Single(v=>v.path==field.Path).value!=aiDraft.fields.Single(v=>v.path==field.Path).value||invalid.ContainsKey(field.Path));
        private static IEnumerable<SurfaceParameter> DetailFields(ModelDetailProfile profile)=>profile.Parameters().Select(f=>{float scale=f.Unit=="screen height"?100:1;return new SurfaceParameter(f.Path,f.Group,f.Label,f.Description,scale==100?"%":f.Unit,f.Minimum*scale,f.Maximum*scale,f.Step*scale,()=>f.Read()*scale,v=>f.Write(v/scale));});
        private bool DetailChanged(SurfaceParameter field)=>field.Read()!=DetailFields(detailOriginal).Single(f=>f.Path==field.Path).Read()||invalid.ContainsKey(field.Path);
        private IEnumerable<string> Differences()
        {
            foreach(var f in PlayableProfileMetadata.Fields.Where(f=>NativeBalanceFields.Editable(f)&&Changed(f)))yield return NativeBalanceLabels.Label(f)+" ["+f.Path+"]: "+Number(f.Read(original))+" → "+(invalid.TryGetValue(f.Path,out var raw)?raw:Number(f.Read(draft)));
            foreach(var f in NativeBalanceFields.AiOpeningFields.Where(Changed))yield return f.Label+" ["+f.Path+"]: "+Number(aiOriginal.fields.Single(v=>v.path==f.Path).value)+" → "+(invalid.TryGetValue(f.Path,out var raw)?raw:Number(aiDraft.fields.Single(v=>v.path==f.Path).value));
            foreach(var f in DetailFields(detailDraft).Where(DetailChanged))yield return "Графика / "+f.Group+" / "+f.Label+" ["+f.Path+"]: "+Number(DetailFields(detailOriginal).Single(v=>v.Path==f.Path).Read())+" → "+(invalid.TryGetValue(f.Path,out var raw)?raw:Number(f.Read()))+" "+f.Unit;
            foreach(var f in GameplayAudioMetadata.Fields.Where(f=>f.Read(audioOriginal)!=f.Read(audioDraft)||invalid.ContainsKey(f.Path)))yield return "Звук / "+f.Label+" ["+f.Path+"]: "+Number(f.Read(audioOriginal))+" → "+(invalid.TryGetValue(f.Path,out var raw)?raw:Number(f.Read(audioDraft)));
        }
        private int ChangeCount=>original==null?0:Differences().Count();
        private void SetDirty(){dirty=ChangeCount>0;save.SetEnabled(dirty&&store.CanEdit);RenderTree();if(section==Changes&&string.IsNullOrEmpty(search.value))Page.schedule.Execute(()=>{string key=(Page.focusController?.focusedElement as VisualElement)?.name;var offset=fields.scrollOffset;RenderFields();fields.scrollOffset=offset;if(key!=null)Page.Q(key)?.Focus();});}
        private void Save()=>Try(()=>{if(invalid.Count>0)throw new ArgumentException("Исправьте ввод: "+string.Join(", ",invalid.Keys));store.Save(draft,audioDraft,aiDraft,detailDraft);Load();status.text="Версия сохранена в "+store.StoragePath+". Следующая игра: "+store.ActiveIdentity+". Git commit/push не выполнялись кнопкой сохранения.";});
        private void Try(Action action){try{action();}catch(StoreConflictException){store.Reload();Refresh();status.text="История изменилась в другом процессе. Черновик сохранён; проверьте новую базу перед повтором.";}catch(Exception e){status.text=e.Message;}}
        private void Back()=>Guard(()=>{Page.RemoveFromHierarchy();closed();});
        private void Guard(Action action)
        {
            if(!dirty){Try(action);return;}
            Confirm("Несохранённые изменения будут сброшены:\n"+string.Join("\n",Differences()),()=>{Load();action();},"Сбросить изменения и продолжить");
        }
        private void Confirm(string message,Action action,string accept="Подтвердить")
        {
            var root=Page.parent;var focus=Page.focusController?.focusedElement as VisualElement;var overlay=OrbitalTheme.Screen(root,"lab-confirm");var card=OrbitalTheme.Card(overlay);var scroll=new ScrollView(){name="lab-confirm-diff"};scroll.style.flexShrink=1;scroll.style.minHeight=0;scroll.Add(OrbitalTheme.Text(message,"orbital-body"));card.Add(scroll);
            Action close=()=>{overlay.RemoveFromHierarchy();navigation.SetScope(Page,Back,focus!=null&&Page.Contains(focus)?focus:profiles);};
            var cancel=OrbitalTheme.Action("Продолжить редактирование",close,"lab-confirm-cancel");card.Add(cancel);card.Add(OrbitalTheme.Action(accept,()=>{close();Try(action);},"lab-confirm-accept"));navigation.SetScope(overlay,close,cancel);
        }
        private void NameDialog(string title,string initial,Action<string> action)
        {
            var overlay=OrbitalTheme.Screen(Page.parent,"lab-name-dialog");var card=OrbitalTheme.Card(overlay);var input=new TextField(title){name="lab-name",maxLength=64,value=initial};card.Add(input);
            var error=OrbitalTheme.Text("","orbital-muted");card.Add(error);Action close=()=>{overlay.RemoveFromHierarchy();navigation.SetScope(Page,Back,profiles);};
            card.Add(OrbitalTheme.Action("Отменить",close,"lab-name-cancel"));card.Add(OrbitalTheme.Action("Сохранить",()=>{try{action(input.value);close();}catch(Exception e){error.text=e.Message;}},"lab-name-accept"));navigation.SetScope(overlay,close,input);
        }
        private void ShowHelp(VisualElement row,string text)
        {
            var previous=help.parent;if(previous!=null){help.RemoveFromHierarchy();previous.style.flexWrap=Wrap.NoWrap;}
            row.style.flexWrap=Wrap.Wrap;row.Add(help);help.text=text;help.style.display=DisplayStyle.Flex;
            fields.schedule.Execute(()=>{if(row.panel!=null&&help.parent==row)fields.ScrollTo(row);});
        }
        private void LayoutColumns()
        {
            if(grid==null)return;
            foreach(var row in grid.Children().Where(e=>e.ClassListContains("lab-parameter-row")))row.style.width=Length.Percent(TwoColumns?50:100);
        }
        private bool TwoColumns=>Page.resolvedStyle.width>=1120&&!(section==Changes&&string.IsNullOrWhiteSpace(search.value));
        private void RenderTree()
        {
            var available=PlayableProfileMetadata.Fields.Where(NativeBalanceFields.Editable).ToArray();var aiAvailable=NativeBalanceFields.AiOpeningFields;groups.Clear();
            foreach(var category in Categories)
            {
                string choice=category;bool branch=choice!=Changes&&choice!="Звук";
                var header=OrbitalTheme.Action((branch?(expanded==choice?"▾  ":"▸  "):"")+choice+(choice==Changes?" · "+ChangeCount:""),()=>{

                    expanded=branch&&expanded!=choice?choice:null;section=choice;
                    if(branch){var objects=choice=="ИИ"?aiAvailable.Select(f=>f.Group).Distinct().ToArray():(choice=="Графика"?DetailFields(detailDraft).Select(f=>f.Group).Distinct():Array.Empty<string>()).Concat(available.Where(f=>NativeBalanceLabels.Section(f)==choice).Select(NativeBalanceLabels.Entity).Distinct()).ToArray();if(!objects.Contains(entity))entity=objects.FirstOrDefault();}
                    RenderFields();navigation.SetScope(Page,Back,Page.Q<Button>("lab-section-"+choice));
                },"lab-section-"+choice,choice==section);
                header.AddToClassList("lab-tree-section");header.EnableInClassList("lab-dirty",choice==Changes?ChangeCount>0:choice=="Звук"?GameplayAudioMetadata.Fields.Any(f=>f.Read(audioOriginal)!=f.Read(audioDraft)||invalid.ContainsKey(f.Path)):choice=="ИИ"?aiAvailable.Any(Changed):(choice=="Графика"&&DetailFields(detailDraft).Any(DetailChanged)||available.Any(f=>NativeBalanceLabels.Section(f)==choice&&Changed(f))));if(choice==Changes)header.style.fontSize=13;header.tooltip=choice==Changes?"Все отличия черновика от выбранной сохранённой версии, включая название.":"Открыть раздел. Остальные ветви дерева свернутся.";groups.Add(header);
                if(!branch||expanded!=choice)continue;
                var children=new VisualElement(){name="lab-children-"+choice};children.AddToClassList("lab-tree-children");groups.Add(children);
                var items=choice=="ИИ"?aiAvailable.Select(f=>f.Group).Distinct():(choice=="Графика"?DetailFields(detailDraft).Select(f=>f.Group).Distinct():Array.Empty<string>()).Concat(available.Where(f=>NativeBalanceLabels.Section(f)==choice).Select(NativeBalanceLabels.Entity).Distinct());
                foreach(var item in items)
                {
                    string obj=item;string visibleName=choice=="ИИ"?"Дебюты и мидгейм":obj;
                    var action=OrbitalTheme.Action("",()=>{section=choice;entity=obj;RenderFields();navigation.SetScope(Page,Back,fields.Query<TextField>().First());},"lab-object-"+obj.Replace('/','-').Replace(' ','-'),obj==entity&&choice==section);
                    var icon=new OrbitalGlyph(NativeBalanceLabels.Glyph(visibleName)){pickingMode=PickingMode.Ignore};icon.style.width=24;icon.style.height=24;icon.style.marginRight=6;action.Add(icon);
                    action.Add(new Label(visibleName){pickingMode=PickingMode.Ignore});action.EnableInClassList("lab-dirty",choice=="ИИ"?aiAvailable.Any(f=>f.Group==obj&&Changed(f)):(DetailFields(detailDraft).Any(f=>f.Group==obj)?DetailFields(detailDraft).Where(f=>f.Group==obj).Any(DetailChanged):available.Any(f=>NativeBalanceLabels.Section(f)==choice&&NativeBalanceLabels.Entity(f)==obj&&Changed(f))));children.Add(action);
                }
            }
        }
        private void RenderFields()
        {
            if(draft==null){grid.Clear();return;}
            var available=PlayableProfileMetadata.Fields.Where(NativeBalanceFields.Editable).ToArray();var aiAvailable=NativeBalanceFields.AiOpeningFields;RenderTree();
            grid.Clear();fields.scrollOffset=Vector2.zero;string query=search.value?.Trim()??"";
            var visible=available.Where(f=>query.Length==0?(section==Changes?Changed(f):NativeBalanceLabels.Section(f)==section&&NativeBalanceLabels.Entity(f)==entity):(NativeBalanceLabels.Section(f)+" "+f.Path+" "+NativeBalanceLabels.Label(f)+" "+NativeBalanceLabels.Help(f)+" "+NativeBalanceLabels.Entity(f)).IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0).ToArray();
            context.style.display=section==Changes&&query.Length==0?DisplayStyle.None:DisplayStyle.Flex;
            context.text=query.Length>0?"Поиск по всем разделам · "+visible.Length+" параметров":section==Changes?Changes+" · "+ChangeCount:section+" / "+entity+" · "+visible.Length+" параметров";
            var aiVisible=aiAvailable.Where(f=>query.Length==0?(section=="ИИ"&&f.Group==entity||section==Changes&&Changed(f)):("ИИ "+f.Group+" "+f.Path+" "+f.Label+" "+f.Description).IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0).ToArray();
            var audioVisible=GameplayAudioMetadata.Fields.Where(f=>query.Length==0?(section=="Звук"||section==Changes&&(f.Read(audioOriginal)!=f.Read(audioDraft)||invalid.ContainsKey(f.Path))):("Звук "+f.Label+" "+f.Path+" "+f.Description).IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0).ToArray();
            var detailVisible=DetailFields(detailDraft).Where(f=>query.Length==0?(section=="Графика"&&entity==f.Group||section==Changes&&DetailChanged(f)):("Графика LOD детализация "+f.Group+" "+f.Path+" "+f.Label+" "+f.Description).IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0).ToArray();
            context.text=query.Length>0?"Поиск по всем разделам · "+(visible.Length+aiVisible.Length+audioVisible.Length+detailVisible.Length)+" параметров":section==Changes?Changes+" · "+ChangeCount:section=="Звук"?"Звук / Игровой микс · "+audioVisible.Length+" параметров":section=="ИИ"?"ИИ / Дебюты и мидгейм · "+aiVisible.Length+" параметров":section+" / "+entity+" · "+(visible.Length+detailVisible.Length)+" параметров";
            help.style.display=DisplayStyle.None;
            if(visible.Length==0&&aiVisible.Length==0&&audioVisible.Length==0&&detailVisible.Length==0)grid.Add(OrbitalTheme.Text(query.Length==0&&section==Changes?"Нет несохранённых изменений.":"Параметры не найдены. Попробуйте другое название.","orbital-muted"));
            if(detailVisible.Length>0){
                var hint=OrbitalTheme.Text("LOD: размер модели относительно высоты кадра. Следы: часть машин оставляет сплошной след, остальные — нет. Изменения применяются в следующей игре или в полигоне.","orbital-muted");hint.name="lab-lod-help";hint.style.whiteSpace=WhiteSpace.Normal;hint.style.width=Length.Percent(100);grid.Add(hint);
                foreach(var field in detailVisible){
                    var row=new VisualElement();row.AddToClassList("lab-parameter-row");row.style.width=Length.Percent(TwoColumns?50:100);grid.Add(row);
                    string tip=field.Description+" Диапазон: "+Number(field.Minimum)+"–"+Number(field.Maximum)+" "+field.Unit+", шаг "+Number(field.Step)+" "+field.Unit+". "+field.Path;
                    var editor=new TextField(field.Label){name=field.Path,isDelayed=true,tooltip=tip,isReadOnly=!store.CanEdit};editor.AddToClassList("lab-parameter");row.Add(editor);
                    void Show()=>editor.SetValueWithoutNotify(invalid.TryGetValue(field.Path,out var raw)?raw:Number(field.Read()));Show();
                    editor.RegisterCallback<FocusInEvent>(_=>ShowHelp(row,tip));editor.RegisterCallback<FocusOutEvent>(_=>help.style.display=DisplayStyle.None);
                    editor.RegisterValueChangedCallback(e=>{try{
                        if(!float.TryParse(e.newValue.Replace(',','.'),NumberStyles.Float,CultureInfo.InvariantCulture,out var value)||float.IsNaN(value)||float.IsInfinity(value)||value<field.Minimum||value>field.Maximum)throw new ArgumentException("Введите число в диапазоне "+Number(field.Minimum)+"–"+Number(field.Maximum)+" "+field.Unit+".");
                        NativeBalanceStore.CheckStep(value,DetailFields(detailOriginal).Single(f=>f.Path==field.Path).Read(),field.Step,field.Path);field.Write(value);invalid.Remove(field.Path);SetDirty();
                    }catch(Exception error){invalid[field.Path]=e.newValue;SetDirty();status.text=error.Message;}});
                    if(section==Changes&&query.Length==0){var before=new Label("было: "+Number(DetailFields(detailOriginal).Single(f=>f.Path==field.Path).Read())+" "+field.Unit);before.AddToClassList("lab-before");row.Add(before);}
                    var unit=new Label(field.Unit);unit.AddToClassList("lab-unit");row.Add(unit);
                    foreach(int direction in new[]{-1,1}){int delta=direction;var button=OrbitalTheme.Action(delta<0?"−":"+",()=>{editor.value=Number(Math.Max(field.Minimum,Math.Min(field.Maximum,field.Read()+delta*field.Step)));});button.AddToClassList("lab-adjust");button.focusable=false;button.SetEnabled(store.CanEdit);row.Add(button);}
                }
            }
            if(aiVisible.Length>0)
            {
                if(section=="ИИ")grid.Add(OrbitalTheme.Text("После сохранения настройки применятся к новым матчам; текущий матч не меняется.","orbital-muted"));
                if(section=="ИИ")grid.Add(OrbitalTheme.Action("Сбросить ИИ по умолчанию",()=>Try(()=>{aiDraft=NativeBalanceFields.InitialAiOpeningProfile().CopyData();invalid.Keys.Where(k=>k.StartsWith("openingsMidgame.",StringComparison.Ordinal)).ToList().ForEach(k=>invalid.Remove(k));SetDirty();RenderFields();}),"lab-ai-reset"));
                grid.Add(OrbitalTheme.Text("ИИ / Дебюты и мидгейм","lab-subheading"));
                foreach(var field in aiVisible)
                {
                    string tip=field.Description+"\nДиапазон: "+Number(field.Minimum)+"–"+Number(field.Maximum)+" "+AiRangeUnit(field)+" · шаг "+Number(field.Step);
                    var row=new VisualElement(){tooltip=tip};row.AddToClassList("lab-parameter-row");row.style.flexShrink=0;row.style.width=Length.Percent(TwoColumns?50:100);row.style.paddingRight=8;grid.Add(row);
                    var editor=new TextField(field.Label){name="lab-ai-"+field.Path.Replace('.','-'),isDelayed=true,tooltip=tip};editor.AddToClassList("lab-parameter");row.Add(editor);editor.isReadOnly=!store.CanEdit;
                    if(section==Changes&&query.Length==0)row.Add(new Label("было: "+Number(aiOriginal.fields.Single(v=>v.path==field.Path).value)){name="lab-before-ai-"+field.Path.Replace('.','-')});
                    void Show(){editor.SetValueWithoutNotify(invalid.TryGetValue(field.Path,out var raw)?raw:Number(aiDraft.fields.Single(v=>v.path==field.Path).value));}
                    var unit=new Label(AiUnit(field,field.Initial)){tooltip=tip};unit.AddToClassList("lab-unit");row.Add(unit);
                    void UpdateUnit()=>unit.text=AiUnit(field,aiDraft.fields.Single(v=>v.path==field.Path).value);
                    editor.RegisterCallback<FocusInEvent>(_=>ShowHelp(row,tip+" · "+field.Path+" · Применение: следующий новый матч."));editor.RegisterCallback<FocusOutEvent>(_=>help.style.display=DisplayStyle.None);
                    editor.RegisterValueChangedCallback(e=>{try{if(!double.TryParse(e.newValue,NumberStyles.Float,CultureInfo.InvariantCulture,out var value))throw new ArgumentException("Введите число: "+field.Path);NativeBalanceStore.CheckStep(value,aiOriginal.fields.Single(v=>v.path==field.Path).value,field.Step,field.Path);NativeBalanceFields.Write(field,aiDraft,value);invalid.Remove(field.Path);UpdateUnit();SetDirty();}catch(Exception error){invalid[field.Path]=e.newValue;SetDirty();status.text=field.Path+": "+error.Message;}});Show();
                    foreach(int direction in new[]{-1,1}){int delta=direction;var adjust=OrbitalTheme.Action(delta<0?"−":"+",()=>Try(()=>{var current=aiDraft.fields.Single(v=>v.path==field.Path).value;NativeBalanceFields.Write(field,aiDraft,Math.Round(Math.Max(field.Minimum,Math.Min(field.Maximum,current+delta*field.Step)),6));invalid.Remove(field.Path);UpdateUnit();SetDirty();Show();}));adjust.AddToClassList("lab-adjust");adjust.focusable=false;adjust.SetEnabled(store.CanEdit);adjust.tooltip=tip;row.Add(adjust);}
                }
            }
            foreach(var bucket in visible.GroupBy(f=>query.Length==0&&section!=Changes?NativeBalanceLabels.Bucket(f):NativeBalanceLabels.Section(f)+" / "+NativeBalanceLabels.Entity(f)+" / "+NativeBalanceLabels.Bucket(f)))
            {
                var heading=OrbitalTheme.Text(bucket.Key,"lab-subheading");heading.style.width=Length.Percent(100);heading.style.flexShrink=0;grid.Add(heading);
                foreach(var descriptor in bucket)
                {
                    var field=descriptor;string tip=NativeBalanceLabels.Help(field)+"\nДиапазон: "+field.Minimum+"–"+field.Maximum+" "+NativeBalanceLabels.Unit(field.Unit)+" · шаг "+field.Step;
                    var row=new VisualElement(){tooltip=tip};row.AddToClassList("lab-parameter-row");row.style.flexShrink=0;row.style.width=Length.Percent(TwoColumns?50:100);row.style.paddingRight=8;grid.Add(row);
                    var button=new TextField(NativeBalanceLabels.Label(field)){name="lab-field-"+field.FieldName,isDelayed=true,tooltip=tip};button.AddToClassList("lab-parameter");row.Add(button);
                    if(section==Changes&&query.Length==0){var before=new Label("было: "+Number(field.Read(original))){name="lab-before-"+field.FieldName,tooltip="Значение в выбранной сохранённой версии"};before.AddToClassList("lab-before");row.Add(before);}
                    button.isReadOnly=!store.CanEdit;
                    button.RegisterCallback<FocusInEvent>(_=>ShowHelp(row,tip+" · "+field.Path+" · Применение: следующая игра."));button.RegisterCallback<FocusOutEvent>(_=>help.style.display=DisplayStyle.None);
                    void Show(){button.SetValueWithoutNotify(invalid.TryGetValue(field.Path,out var raw)?raw:Number(field.Read(draft)));}
                    button.userData=new Action<int>(step=>{if(!store.CanEdit)return;Try(()=>{field.Write(draft,Math.Round(Math.Max(field.Minimum,Math.Min(field.Maximum,field.Read(draft)+step*field.Step)),6));invalid.Remove(field.Path);SetDirty();Show();});});
                    button.RegisterValueChangedCallback(e=>{
                        try{if(!double.TryParse(e.newValue,NumberStyles.Float,CultureInfo.InvariantCulture,out var value))throw new ArgumentException("Введите число: "+field.Path);NativeBalanceStore.CheckStep(value,field.Read(original),field.Step,field.Path);field.Write(draft,value);invalid.Remove(field.Path);SetDirty();}
                        catch(Exception error){invalid[field.Path]=e.newValue;SetDirty();status.text=field.Path+": "+error.Message;}
                    });Show();
                    var unit=new Label(NativeBalanceLabels.Unit(field.Unit)){tooltip=tip};unit.AddToClassList("lab-unit");row.Add(unit);
                    foreach(int direction in new[]{-1,1}){int delta=direction;var adjust=OrbitalTheme.Action(delta<0?"−":"+",()=>((Action<int>)button.userData)(delta));adjust.AddToClassList("lab-adjust");adjust.tooltip=tip;adjust.focusable=false;adjust.SetEnabled(store.CanEdit);row.Add(adjust);}
                    var info=new Label("?"){tooltip=tip};info.AddToClassList("lab-info");row.Add(info);
                }
            }
            if(audioVisible.Length>0){var heading=OrbitalTheme.Text("Звук / Игровой микс","lab-subheading");heading.style.width=Length.Percent(100);grid.Add(heading);}
            foreach(var field in audioVisible)
            {
                var row=new VisualElement();row.AddToClassList("lab-parameter-row");row.style.width=Length.Percent(TwoColumns?50:100);grid.Add(row);
                string tip=field.Description+" · "+field.Path+" · Диапазон: "+field.Minimum+"–"+field.Maximum+" "+field.Unit+" · шаг "+field.Step+" · Применение: следующая игра.";
                var editor=new TextField(field.Label){name=field.Path,isDelayed=true,tooltip=tip};editor.AddToClassList("lab-parameter");editor.SetValueWithoutNotify(invalid.TryGetValue(field.Path,out var raw)?raw:Number(field.Read(audioDraft)));editor.isReadOnly=!store.CanEdit;row.Add(editor);
                editor.RegisterCallback<FocusInEvent>(_=>ShowHelp(row,tip));editor.RegisterCallback<FocusOutEvent>(_=>help.style.display=DisplayStyle.None);
                editor.RegisterValueChangedCallback(e=>{try{if(!double.TryParse(e.newValue,NumberStyles.Float,CultureInfo.InvariantCulture,out var v))throw new ArgumentException("Введите число.");NativeBalanceStore.CheckStep(v,field.Read(audioOriginal),field.Step,field.Path);field.Write(audioDraft,v);invalid.Remove(field.Path);SetDirty();}catch(Exception error){invalid[field.Path]=e.newValue;SetDirty();status.text=field.Path+": "+error.Message;}});
                if(section==Changes&&query.Length==0){var before=new Label("было: "+Number(field.Read(audioOriginal)));before.AddToClassList("lab-before");row.Add(before);}
                var unit=new Label(field.Unit);unit.AddToClassList("lab-unit");row.Add(unit);
                editor.userData=new Action<int>(step=>{if(!store.CanEdit)return;editor.value=Number(field.Read(audioDraft)+step*field.Step);});
                foreach(int direction in new[]{-1,1}){int delta=direction;var adjust=OrbitalTheme.Action(delta<0?"−":"+",()=>((Action<int>)editor.userData)(delta));adjust.AddToClassList("lab-adjust");adjust.focusable=false;adjust.SetEnabled(store.CanEdit);row.Add(adjust);}
            }
        }
    }
}
