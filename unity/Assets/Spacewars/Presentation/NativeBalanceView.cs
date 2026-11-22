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
        private readonly Dictionary<string,string> invalid=new Dictionary<string,string>();
        private bool dirty;
        private string section="Юниты",entity=OfflinePadWorldActions.VeprName,expanded="Юниты";
        private const string Changes="Изменения";
        private static readonly string[] Categories={Changes,"Юниты","Здания",NativeBalanceLabels.Research,"Экономика",NativeBalanceLabels.Escalation,NativeBalanceLabels.Mechanics,"ИИ","Звук"};
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
            Page.RegisterCallback<GeometryChangedEvent>(_=>{bool compact=Page.resolvedStyle.width<900;LayoutColumns();sidebar.style.width=compact?208:238;title.style.fontSize=compact?18:22;title.style.marginBottom=4;card.style.paddingLeft=card.style.paddingRight=card.style.paddingTop=card.style.paddingBottom=10;});
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
            audio.Validate();ModelDetailProfile.UseSession(store.SelectedModelDetail);openProvingGround(snapshot,audio);
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
            try{selected=store.Selected;original=selected.CopyData();draft=selected.CopyData();aiOriginal=store.SelectedAiProfile.CopyData();aiDraft=store.SelectedAiProfile.CopyData();audioOriginal=store.SelectedAudio;audioDraft=JsonUtility.FromJson<GameplayAudioProfile>(JsonUtility.ToJson(audioOriginal));invalid.Clear();dirty=false;Refresh();RenderFields();}
            catch(Exception e){draft=null;original=null;invalid.Clear();dirty=false;Refresh();grid.Clear();status.text="Версия недоступна: "+e.Message+". Продолжает действовать: "+store.ActiveIdentity+". Исходные данные сохранены; выберите другую версию или релизный Default.";}
        }
        private static string Number(double value)=>value.ToString("0.######",CultureInfo.InvariantCulture);
        private static string Value(double stored,string unit)=>Number(stored*NativeBalanceLabels.DisplayScale(unit));
        private static string WithUnit(string value,string unit)=>value+" "+NativeBalanceLabels.Unit(unit);
        private static bool TryNumber(string input,out double value)=>
            double.TryParse(input?.Replace(',','.'),NumberStyles.Float,CultureInfo.InvariantCulture,out value)
            && !double.IsNaN(value)&&!double.IsInfinity(value);
        private static double ReadGameplayValue(string input,PlayableProfileField field,double origin)
        {
            if(!TryNumber(input,out var displayed))throw new ArgumentException("Введите число.");
            double scale=NativeBalanceLabels.DisplayScale(field.Unit),stored=displayed/scale;
            NativeBalanceStore.CheckStep(stored,origin,field.Step,field.Path);
            if(scale==1)return stored;
            // The displayed angular value is rounded. Recover the authored step without
            // introducing fractional storage values (in particular for integer milliseconds).
            double steps=Math.Round((stored-origin)/field.Step);
            if(steps==0)return origin;
            double stepped=origin+steps*field.Step;
            if(Math.Abs(stepped-field.Minimum)<1e-9)return field.Minimum;
            if(Math.Abs(stepped-field.Maximum)<1e-9)return field.Maximum;
            return Math.Round(stepped,12);
        }
        private static string ParameterHelp(string description,string path,string unit,double min,double max,double step)=>
            description.TrimEnd().TrimEnd('.')+".\nДиапазон: "+WithUnit(Value(min,unit)+"–"+Value(max,unit),unit)+
            " · Шаг: "+WithUnit(Value(step,unit),unit)+"\nПрименение: следующая игра.\n"+path;
        private void BindParameterHelp(VisualElement row,TextField field,string tip,string id)
        {
            row.tooltip=field.tooltip=tip;
            field.RegisterCallback<FocusInEvent>(_=>ShowHelp(row,tip));
            field.RegisterCallback<FocusOutEvent>(_=>help.style.display=DisplayStyle.None);
            var info=new Button(){text="?",name="lab-info-"+id,tooltip=tip};
            info.AddToClassList("lab-info");
            row.Add(info);
            info.clicked+=()=>{info.Focus();ShowHelp(row,tip);};
            info.RegisterCallback<PointerEnterEvent>(_=>ShowHelp(row,tip));
            info.RegisterCallback<PointerLeaveEvent>(_=>{if(help.parent==row&&Page.focusController?.focusedElement!=info)help.style.display=DisplayStyle.None;});
            info.RegisterCallback<FocusInEvent>(_=>ShowHelp(row,tip));
            info.RegisterCallback<FocusOutEvent>(_=>{if(help.parent==row)help.style.display=DisplayStyle.None;});
        }
        private bool CompactEscalation=>draft?.orbitalProgressionVersion==OrbitalAbilities.CompactProgressionVersion;
        private bool VisibleGameplayField(PlayableProfileField field)=>NativeBalanceFields.LabEditable(field)&&
            (!CompactEscalation||field.FieldName!=nameof(PlayableProfileData.orbitalThreshold5));
        private string GameplayLabel(PlayableProfileField field)=>CompactEscalation&&field.FieldName==nameof(PlayableProfileData.orbitalThresholdMax)
            ?"Урон от IV до MAX":NativeBalanceLabels.Label(field);
        private string GameplayHelp(PlayableProfileField field)=>CompactEscalation&&field.FieldName==nameof(PlayableProfileData.orbitalThresholdMax)
            ?"Урон по противникам для перехода с IV до MAX. Порог не зависит от предыдущих ступеней.":NativeBalanceLabels.Help(field);
        private bool Changed(PlayableProfileField field)=>field.Read(original)!=field.Read(draft)||invalid.ContainsKey(field.Path);
        private bool Changed(AiProfileField field)=>aiOriginal!=null&&aiDraft!=null&&(aiOriginal.fields.Single(v=>v.path==field.Path).value!=aiDraft.fields.Single(v=>v.path==field.Path).value||invalid.ContainsKey(field.Path));
        private IEnumerable<string> Differences()
        {
            string Difference(string label,string path,string unit,double before,double after)=>
                label+" ["+path+"]: "+Value(before,unit)+" → "+WithUnit(invalid.TryGetValue(path,out var raw)?raw:Value(after,unit),unit);
            foreach(var f in PlayableProfileMetadata.Fields.Where(f=>VisibleGameplayField(f)&&Changed(f)).OrderBy(NativeBalanceLabels.Order))
                yield return Difference(GameplayLabel(f),f.Path,f.Unit,f.Read(original),f.Read(draft));
            foreach(var f in NativeBalanceFields.AiLabFields.Where(Changed))
                yield return Difference(f.Label,f.Path,f.Unit,aiOriginal.fields.Single(v=>v.path==f.Path).value,aiDraft.fields.Single(v=>v.path==f.Path).value);
            foreach(var f in GameplayAudioMetadata.Fields.Where(f=>f.Read(audioOriginal)!=f.Read(audioDraft)||invalid.ContainsKey(f.Path)))
                yield return Difference("Звук / "+f.Label,f.Path,f.Unit,f.Read(audioOriginal),f.Read(audioDraft));
        }
        private int ChangeCount=>original==null?0:Differences().Count();
        private void SetDirty(){dirty=ChangeCount>0;save.SetEnabled(dirty&&store.CanEdit);RenderTree();if(section==Changes&&string.IsNullOrEmpty(search.value))Page.schedule.Execute(()=>{string key=(Page.focusController?.focusedElement as VisualElement)?.name;var offset=fields.scrollOffset;RenderFields();fields.scrollOffset=offset;if(key!=null)Page.Q(key)?.Focus();});}
        private void Save()=>Try(()=>{if(invalid.Count>0)throw new ArgumentException("Исправьте ввод: "+string.Join(", ",invalid.Keys));store.Save(draft,audioDraft,aiDraft);Load();status.text="Версия сохранена в "+store.StoragePath+". Следующая игра: "+store.ActiveIdentity+". Git commit/push не выполнялись кнопкой сохранения.";});
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
        private IEnumerable<string> Objects(string category)
        {
            var gameplay=PlayableProfileMetadata.Fields.Where(VisibleGameplayField).Where(f=>NativeBalanceLabels.Section(f)==category).Select(NativeBalanceLabels.Entity);
            var extra=category=="ИИ"?NativeBalanceFields.AiLabFields.Select(NativeBalanceLabels.AiEntity):Array.Empty<string>();
            var objects=extra.Concat(gameplay).Distinct().ToList();
            string cerberus=OfflinePadWorldActions.Name(PlayableEntityKind.Cerberus);
            if(category=="Юниты"&&objects.Remove(cerberus))
                objects.Add(cerberus);
            if(category==NativeBalanceLabels.Research&&objects.Contains("Штурмовые орудия")&&objects.Remove("Ракетная установка"))
                objects.Insert(objects.IndexOf("Штурмовые орудия")+1,"Ракетная установка");
            return objects;
        }
        private bool ObjectChanged(string category,string obj=null)
        {
            return PlayableProfileMetadata.Fields.Where(VisibleGameplayField).Any(f=>NativeBalanceLabels.Section(f)==category&&(obj==null||NativeBalanceLabels.Entity(f)==obj)&&Changed(f))||
                category=="ИИ"&&NativeBalanceFields.AiLabFields.Any(f=>(obj==null||NativeBalanceLabels.AiEntity(f)==obj)&&Changed(f));
        }
        private void OpenObject(string category,string obj,bool clearSearch=false)
        {
            section=expanded=category;entity=obj;
            if(clearSearch)search.SetValueWithoutNotify("");
            RenderFields();navigation.SetScope(Page,Back,fields.Query<TextField>().First());
        }
        private static bool UnitUpgradePortrait(string glyph)=>glyph is "tankHybridUpgraded" or "explorerUpgraded" or "shkval-guidance-3" or "hawkUpgraded";
        private static VisualElement ObjectIcon(string glyph)
        {
            VisualElement icon;
            if(UnitUpgradePortrait(glyph))
            {
                var artwork=new RadialArtwork();artwork.Set(glyph,upgraded:true);icon=artwork;
            }
            else icon=new OrbitalGlyph(glyph);
            icon.pickingMode=PickingMode.Ignore;icon.style.width=24;icon.style.height=24;icon.style.flexShrink=0;icon.style.marginRight=6;
            return icon;
        }
        private void RenderTree()
        {
            groups.Clear();
            foreach(var category in Categories)
            {
                string choice=category;bool branch=choice!=Changes&&choice!="Звук";
                var header=OrbitalTheme.Action((branch?(expanded==choice?"▾  ":"▸  "):"")+choice+(choice==Changes?" · "+ChangeCount:""),()=>{
                    expanded=branch&&expanded!=choice?choice:null;section=choice;
                    if(branch&&!Objects(choice).Contains(entity))entity=Objects(choice).FirstOrDefault();
                    RenderFields();navigation.SetScope(Page,Back,Page.Q<Button>("lab-section-"+choice));
                },"lab-section-"+choice,choice==section);
                header.AddToClassList("lab-tree-section");
                header.EnableInClassList("lab-dirty",choice==Changes?ChangeCount>0:choice=="Звук"?GameplayAudioMetadata.Fields.Any(f=>f.Read(audioOriginal)!=f.Read(audioDraft)||invalid.ContainsKey(f.Path)):ObjectChanged(choice));
                if(choice==Changes)header.style.fontSize=13;
                header.tooltip=choice==Changes?"Все отличия черновика от выбранной сохранённой версии, включая название.":"Открыть раздел. Остальные ветви дерева свернутся.";groups.Add(header);
                if(!branch||expanded!=choice)continue;
                var children=new VisualElement(){name="lab-children-"+choice};children.AddToClassList("lab-tree-children");groups.Add(children);
                foreach(var item in Objects(choice))
                {
                    string obj=item;var action=OrbitalTheme.Action("",()=>OpenObject(choice,obj),"lab-object-"+obj.Replace('/','-').Replace(' ','-'),obj==entity&&choice==section);
                    string glyph=NativeBalanceLabels.Glyph(obj);
                    if(glyph!=null)action.Add(ObjectIcon(glyph));
                    action.style.height=StyleKeyword.Auto;var label=new Label(obj){pickingMode=PickingMode.Ignore};label.AddToClassList("lab-tree-label");label.style.fontSize=14;label.style.flexShrink=1;label.style.whiteSpace=WhiteSpace.Normal;action.Add(label);action.EnableInClassList("lab-dirty",ObjectChanged(choice,obj));children.Add(action);

                }
            }
        }
        private void RenderFields()
        {
            if(draft==null){grid.Clear();return;}
            var available=PlayableProfileMetadata.Fields.Where(VisibleGameplayField).OrderBy(NativeBalanceLabels.Order).ToArray();var aiAvailable=NativeBalanceFields.AiLabFields;RenderTree();
            grid.Clear();fields.scrollOffset=Vector2.zero;string query=search.value?.Trim()??"";
            var visible=available.Where(f=>query.Length==0?(section==Changes?Changed(f):NativeBalanceLabels.Section(f)==section&&NativeBalanceLabels.Entity(f)==entity):(NativeBalanceLabels.Section(f)+" "+f.Path+" "+GameplayLabel(f)+" "+GameplayHelp(f)+" "+NativeBalanceLabels.Entity(f)).IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0).ToArray();
            context.style.display=section==Changes&&query.Length==0?DisplayStyle.None:DisplayStyle.Flex;
            context.text=query.Length>0?"Поиск по всем разделам · "+visible.Length+" параметров":section==Changes?Changes+" · "+ChangeCount:section+" / "+entity+" · "+visible.Length+" параметров";
            var aiVisible=aiAvailable.Where(f=>query.Length==0?(section=="ИИ"&&NativeBalanceLabels.AiEntity(f)==entity||section==Changes&&Changed(f)):("ИИ "+f.Group+" "+f.Path+" "+f.Label+" "+f.Description).IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0).ToArray();
            var audioVisible=GameplayAudioMetadata.Fields.Where(f=>query.Length==0?(section=="Звук"||section==Changes&&(f.Read(audioOriginal)!=f.Read(audioDraft)||invalid.ContainsKey(f.Path))):("Звук "+f.Label+" "+f.Path+" "+f.Description).IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0).ToArray();
            context.text=query.Length>0?"Поиск по всем разделам · "+(visible.Length+aiVisible.Length+audioVisible.Length)+" параметров":section==Changes?Changes+" · "+ChangeCount:section=="Звук"?"Звук / Игровой микс · "+audioVisible.Length+" параметров":section+" / "+entity+" · "+(visible.Length+aiVisible.Length)+" параметров";

            help.style.display=DisplayStyle.None;
            if(visible.Length==0&&aiVisible.Length==0&&audioVisible.Length==0)grid.Add(OrbitalTheme.Text(query.Length==0&&section==Changes?"Нет несохранённых изменений.":"Параметры не найдены. Попробуйте другое название.","orbital-muted"));
            if(query.Length==0&&(section=="Юниты"||section=="Здания"))
            {
                var related=NativeBalanceLabels.RelatedResearch(entity);
                if(related.Length>0)
                {
                    var links=new VisualElement(){name="lab-related-research"};links.style.width=Length.Percent(100);links.style.flexShrink=0;links.style.flexDirection=FlexDirection.Row;links.style.flexWrap=Wrap.Wrap;grid.Add(links);
                    foreach(var target in related)
                    {
                        string research=target;
                        string glyph=NativeBalanceLabels.Glyph(research);
                        bool portraitLink=glyph==ModelPortrait.RocketLauncherArtwork||UnitUpgradePortrait(glyph);
                        string label="Исследования → "+research;
                        var link=OrbitalTheme.Action(portraitLink?"":label,()=>OpenObject(NativeBalanceLabels.Research,research,true),"lab-research-link-"+research.Replace(' ','-'));
                        if(portraitLink)
                        {
                            link.style.flexDirection=FlexDirection.Row;
                            link.style.alignItems=Align.Center;
                            link.Add(ObjectIcon(glyph));
                            var text=new Label(label){pickingMode=PickingMode.Ignore};text.style.whiteSpace=WhiteSpace.Normal;text.style.flexShrink=1;link.Add(text);
                        }
                        link.style.maxWidth=Length.Percent(100);link.style.whiteSpace=WhiteSpace.Normal;link.tooltip="Открыть исследование. Текущий черновик сохранится.";links.Add(link);
                    }
                }
            }
            if(aiVisible.Length>0)
            {
                if(section=="ИИ")grid.Add(OrbitalTheme.Text("После сохранения настройки применятся к новым матчам; текущий матч не меняется.","orbital-muted"));
                if(section=="ИИ")grid.Add(OrbitalTheme.Action("Сбросить ИИ по умолчанию",()=>Try(()=>{var defaults=NativeBalanceFields.InitialAiOpeningProfile();foreach(var field in NativeBalanceFields.AiLabFields){NativeBalanceFields.Write(field,aiDraft,defaults.Value(field.Path));invalid.Remove(field.Path);}SetDirty();RenderFields();}),"lab-ai-reset"));
                grid.Add(OrbitalTheme.Text("ИИ / "+NativeBalanceLabels.AiEntity(aiVisible[0]),"lab-subheading"));

                foreach(var field in aiVisible)
                {
                    string tip=ParameterHelp(field.Description,field.Path,field.Unit,field.Minimum,field.Maximum,field.Step);
                    var row=new VisualElement(){tooltip=tip};row.AddToClassList("lab-parameter-row");row.style.flexShrink=0;row.style.width=Length.Percent(TwoColumns?50:100);row.style.paddingRight=8;grid.Add(row);
                    var editor=new TextField(field.Label){name="lab-ai-"+field.Path.Replace('.','-'),isDelayed=true,tooltip=tip};editor.AddToClassList("lab-parameter");row.Add(editor);editor.isReadOnly=!store.CanEdit;
                    if(section==Changes&&query.Length==0)row.Add(new Label("было: "+WithUnit(Value(aiOriginal.fields.Single(v=>v.path==field.Path).value,field.Unit),field.Unit)){name="lab-before-ai-"+field.Path.Replace('.','-')});
                    void Show(){editor.SetValueWithoutNotify(invalid.TryGetValue(field.Path,out var raw)?raw:Value(aiDraft.fields.Single(v=>v.path==field.Path).value,field.Unit));}
                    var unit=new Label(NativeBalanceLabels.Unit(field.Unit)){tooltip=tip};unit.AddToClassList("lab-unit");row.Add(unit);
                    editor.RegisterValueChangedCallback(e=>{try{if(!TryNumber(e.newValue,out var display))throw new ArgumentException("Введите число.");double value=display/NativeBalanceLabels.DisplayScale(field.Unit);NativeBalanceStore.CheckStep(value,aiOriginal.fields.Single(v=>v.path==field.Path).value,field.Step,field.Path);NativeBalanceFields.Write(field,aiDraft,value);invalid.Remove(field.Path);SetDirty();}catch(Exception error){invalid[field.Path]=e.newValue;SetDirty();status.text=field.Path+": "+error.Message;}});Show();
                    foreach(int direction in new[]{-1,1}){int delta=direction;var adjust=OrbitalTheme.Action(delta<0?"−":"+",()=>Try(()=>{var current=aiDraft.fields.Single(v=>v.path==field.Path).value;NativeBalanceFields.Write(field,aiDraft,Math.Round(Math.Max(field.Minimum,Math.Min(field.Maximum,current+delta*field.Step)),6));invalid.Remove(field.Path);SetDirty();Show();}));adjust.AddToClassList("lab-adjust");adjust.focusable=false;adjust.SetEnabled(store.CanEdit);adjust.tooltip=tip;row.Add(adjust);}
                    BindParameterHelp(row,editor,tip,field.Path);
                }
            }
            foreach(var bucket in visible.GroupBy(f=>query.Length==0&&section!=Changes?NativeBalanceLabels.Bucket(f):NativeBalanceLabels.Section(f)+" / "+NativeBalanceLabels.Entity(f)+" / "+NativeBalanceLabels.Bucket(f)))
            {
                var heading=OrbitalTheme.Text(bucket.Key,"lab-subheading");heading.style.width=Length.Percent(100);heading.style.flexShrink=0;grid.Add(heading);
                foreach(var descriptor in bucket)
                {
                    var field=descriptor;string tip=ParameterHelp(GameplayHelp(field),field.Path,field.Unit,field.Minimum,field.Maximum,field.Step);
                    var row=new VisualElement(){tooltip=tip};row.AddToClassList("lab-parameter-row");row.style.flexShrink=0;row.style.width=Length.Percent(TwoColumns?50:100);row.style.paddingRight=8;grid.Add(row);
                    var button=new TextField(GameplayLabel(field)){name="lab-field-"+field.FieldName,isDelayed=true,tooltip=tip};button.AddToClassList("lab-parameter");row.Add(button);
                    if(section==Changes&&query.Length==0){var before=new Label("было: "+WithUnit(Value(field.Read(original),field.Unit),field.Unit)){name="lab-before-"+field.FieldName,tooltip="Значение в выбранной сохранённой версии"};before.AddToClassList("lab-before");row.Add(before);}
                    button.isReadOnly=!store.CanEdit;
                    void Show(){button.SetValueWithoutNotify(invalid.TryGetValue(field.Path,out var raw)?raw:Value(field.Read(draft),field.Unit));}
                    button.userData=new Action<int>(step=>{if(!store.CanEdit)return;Try(()=>{field.Write(draft,Math.Round(Math.Max(field.Minimum,Math.Min(field.Maximum,field.Read(draft)+step*field.Step)),6));invalid.Remove(field.Path);SetDirty();Show();});});
                    button.RegisterValueChangedCallback(e=>{
                        try{field.Write(draft,ReadGameplayValue(e.newValue,field,field.Read(original)));invalid.Remove(field.Path);SetDirty();}
                        catch(Exception error){invalid[field.Path]=e.newValue;SetDirty();status.text=field.Path+": "+error.Message;}
                    });Show();
                    var unit=new Label(NativeBalanceLabels.Unit(field.Unit)){tooltip=tip};unit.AddToClassList("lab-unit");row.Add(unit);
                    foreach(int direction in new[]{-1,1}){int delta=direction;var adjust=OrbitalTheme.Action(delta<0?"−":"+",()=>((Action<int>)button.userData)(delta));adjust.AddToClassList("lab-adjust");adjust.tooltip=tip;adjust.focusable=false;adjust.SetEnabled(store.CanEdit);row.Add(adjust);}
                    BindParameterHelp(row,button,tip,field.FieldName);
                }
            }
            if(audioVisible.Length>0){var heading=OrbitalTheme.Text("Звук / Игровой микс","lab-subheading");heading.style.width=Length.Percent(100);grid.Add(heading);}
            foreach(var field in audioVisible)
            {
                var row=new VisualElement();row.AddToClassList("lab-parameter-row");row.style.width=Length.Percent(TwoColumns?50:100);grid.Add(row);
                string tip=ParameterHelp(field.Description,field.Path,field.Unit,field.Minimum,field.Maximum,field.Step);
                var editor=new TextField(field.Label){name=field.Path,isDelayed=true,tooltip=tip};editor.AddToClassList("lab-parameter");editor.SetValueWithoutNotify(invalid.TryGetValue(field.Path,out var raw)?raw:Number(field.Read(audioDraft)));editor.isReadOnly=!store.CanEdit;row.Add(editor);
                editor.RegisterValueChangedCallback(e=>{try{if(!TryNumber(e.newValue,out var v))throw new ArgumentException("Введите число.");NativeBalanceStore.CheckStep(v,field.Read(audioOriginal),field.Step,field.Path);field.Write(audioDraft,v);invalid.Remove(field.Path);SetDirty();}catch(Exception error){invalid[field.Path]=e.newValue;SetDirty();status.text=field.Path+": "+error.Message;}});
                if(section==Changes&&query.Length==0){var before=new Label("было: "+WithUnit(Value(field.Read(audioOriginal),field.Unit),field.Unit));before.AddToClassList("lab-before");row.Add(before);}
                var unit=new Label(NativeBalanceLabels.Unit(field.Unit));unit.AddToClassList("lab-unit");row.Add(unit);
                editor.userData=new Action<int>(step=>{if(!store.CanEdit)return;editor.value=Number(field.Read(audioDraft)+step*field.Step);});
                foreach(int direction in new[]{-1,1}){int delta=direction;var adjust=OrbitalTheme.Action(delta<0?"−":"+",()=>((Action<int>)editor.userData)(delta));adjust.AddToClassList("lab-adjust");adjust.focusable=false;adjust.SetEnabled(store.CanEdit);row.Add(adjust);}
                BindParameterHelp(row,editor,tip,field.Path);
            }
        }
    }
}
