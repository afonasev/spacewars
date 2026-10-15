using System;
using System.Linq;
using Spacewars.Simulation;
using Spacewars.BalanceLab;
using UnityEngine;
using UnityEngine.UIElements;

namespace Spacewars.Presentation
{
    public sealed class NativeBalanceView
    {
        public VisualElement Page {get;}
        private readonly NativeBalanceStore store;
        private readonly NativeMenuNavigation navigation;
        private readonly Action closed;
        private readonly Func<PlayableProfile,string> apply;
        private PlayableProfile selected;
        private PlayableProfileData draft, original;
        private bool dirty;
        private string section="Юниты", entity="Танк";
        private string expanded="Юниты";
        private const string Changes="Несохранённые изменения";
        private static readonly string[] Categories={Changes,"Юниты","Здания","Экономика","Графика","Правила","Звук"};
        private readonly Label context;
        private readonly Action openAudio;
        private readonly Label status;
        private readonly TextField name,search;
        private readonly ScrollView fields;
        private readonly VisualElement groups;
        private readonly VisualElement grid;
        private readonly DropdownField revisions;
        private readonly Button save,applyButton;
        public NativeBalanceView(VisualElement root,NativeMenuNavigation navigation,NativeBalanceStore store,Action closed,Func<PlayableProfile,string> apply,PlayableProfile currentMatch=null)
        {
            this.navigation=navigation;this.store=store;this.closed=closed;this.apply=apply;
            Page=OrbitalTheme.Screen(root,"laboratory-screen");var card=OrbitalTheme.Card(Page);card.AddToClassList("orbital-lab-card");
            var title=OrbitalTheme.Text("ЛАБОРАТОРИЯ ГЕЙМДИЗАЙНА","orbital-title");card.Add(title);
            status=OrbitalTheme.Text("Параметры игры и юнитов. Карта и геометрия не редактируются.","orbital-muted");card.Add(status);
            var controls=new VisualElement(){name="lab-controls"};controls.AddToClassList("lab-controls");card.Add(controls);
            var profileControls=new VisualElement(){name="lab-profile-controls"};profileControls.AddToClassList("lab-profile-controls");controls.Add(profileControls);
            name=new TextField("Профиль"){name="lab-name",maxLength=64,tooltip="Название профиля для новой сохранённой ревизии."};name.RegisterValueChangedCallback(_=>SetDirty());profileControls.Add(name);
            var resetName=OrbitalTheme.Action("Сбросить имя",()=>name.value=selected.DisplayName,"lab-reset-name");resetName.tooltip="Отменить изменение названия профиля, сохранив изменения параметров.";profileControls.Add(resetName);
            var revisionControls=new VisualElement(){name="lab-revision-controls"};revisionControls.AddToClassList("lab-revision-controls");controls.Add(revisionControls);
            revisions=new DropdownField("Ревизия"){name="lab-revisions"};revisionControls.Add(revisions);
            save=OrbitalTheme.Action("Сохранить версию",Save,"lab-save",true);revisionControls.Add(save);
            revisionControls.Add(OrbitalTheme.Action("Отменить",()=>Load(selected),"lab-reset"));
            applyButton=OrbitalTheme.Action("Применить",Apply,"lab-apply");revisionControls.Add(applyButton);
            revisionControls.Add(OrbitalTheme.Action("Для нового матча",()=>Try(()=>{store.Select(selected.Revision);status.text="Для следующего матча: "+selected.DisplayName+" · "+selected.Revision;}),"lab-select"));
            openAudio=()=>{Page.style.display=DisplayStyle.None;NativeGameplayAudioProfile.Open(root,navigation,()=>{Page.style.display=DisplayStyle.Flex;navigation.SetScope(Page,Back,save);});};
            revisionControls.Add(OrbitalTheme.Action("Назад",Back,"lab-back"));
            search=new TextField("Поиск"){name="lab-search"};search.RegisterValueChangedCallback(_=>RenderFields());card.Add(search);
            var body=new VisualElement();body.style.flexDirection=FlexDirection.Row;body.style.flexGrow=1;body.style.minHeight=80;body.style.flexShrink=1;card.Add(body);
            var sidebar=new ScrollView();sidebar.name="lab-tree";sidebar.style.width=238;sidebar.style.flexShrink=0;body.Add(sidebar);groups=sidebar.contentContainer;groups.AddToClassList("orbital-lab-groups");sidebar.horizontalScrollerVisibility=ScrollerVisibility.Hidden;groups.style.width=Length.Percent(100);
            fields=new ScrollView(){name="lab-parameters",horizontalScrollerVisibility=ScrollerVisibility.Hidden};fields.contentContainer.style.width=Length.Percent(100);fields.contentContainer.style.minWidth=0;fields.style.flexGrow=1;fields.style.minWidth=0;fields.style.marginLeft=10;body.Add(fields);
            grid=new VisualElement();grid.style.width=Length.Percent(100);grid.style.flexDirection=FlexDirection.Row;grid.style.flexWrap=Wrap.Wrap;grid.style.alignContent=Align.FlexStart;fields.Add(grid);
            context=OrbitalTheme.Text("","lab-context");card.Insert(card.IndexOf(body),context);
            Page.RegisterCallback<GeometryChangedEvent>(_=>{bool compact=Page.resolvedStyle.width<900;LayoutColumns();sidebar.style.width=compact?190:238;title.style.fontSize=compact?18:22;title.style.marginBottom=4;card.style.paddingLeft=card.style.paddingRight=card.style.paddingTop=card.style.paddingBottom=10;});
            RefreshRevisions();selected=currentMatch??store.Selected;Load(selected);
            if(currentMatch!=null)status.text="В матче: "+currentMatch.DisplayName+" · "+currentMatch.Revision+". Применение — после продолжения.";
            revisions.RegisterValueChangedCallback(e=>{if(dirty){status.text="Сохраните или отмените черновик перед сменой версии.";RefreshRevisions();return;}int index=revisions.choices.IndexOf(e.newValue);var row=store.State["revisions"][index];Load(store.Compile(row));});
            navigation.SetScope(Page,Back,revisions);
        }
        private void RefreshRevisions()
        {
            revisions.choices=store.State["revisions"].Select(r=>((int)r["revision"]).ToString()).ToList();
            if(selected!=null)revisions.SetValueWithoutNotify(selected.Revision.ToString());
        }
        private void Load(PlayableProfile profile)
        {
            selected=profile;original=profile.CopyData();draft=profile.CopyData();name.SetValueWithoutNotify(profile.DisplayName);dirty=false;RefreshRevisions();
            save.SetEnabled(false);applyButton.SetEnabled(apply!=null);RenderFields();
        }
        private static string Number(double value)=>value.ToString("0.###",System.Globalization.CultureInfo.InvariantCulture);
        private bool Changed(PlayableProfileField field)=>field.Read(original)!=field.Read(draft);
        private bool NameChanged=>name.value!=selected.DisplayName;
        private int ChangeCount=>PlayableProfileMetadata.Fields.Count(f=>NativeBalanceFields.Editable(f)&&Changed(f))+(NameChanged?1:0);
        private void SetDirty()
        {
            dirty=ChangeCount>0;save?.SetEnabled(dirty);applyButton?.SetEnabled(!dirty&&apply!=null);RenderTree();
            if(section==Changes&&string.IsNullOrWhiteSpace(search.value))
                Page.schedule.Execute(()=>{
                    var focused=Page.focusController?.focusedElement as VisualElement;
                    var editor=focused as TextField??focused?.GetFirstAncestorOfType<TextField>();string key=editor?.name;
                    var offset=fields.scrollOffset;RenderFields();fields.scrollOffset=offset;
                    if(key!=null)Page.Q<TextField>(key)?.Focus();
                });
        }
        private void Save()=>Try(()=>{var result=store.Save(name.value,draft);Load(result);status.text="Сохранена версия "+result.DisplayName+" · "+result.Revision;});
        private void Apply()=>Try(()=>{var error=apply?.Invoke(selected);status.text=error??"Применение после продолжения матча";});
        private void Try(Action action)
        {
            try{action();}
            catch(StoreConflictException){store.Reload();RefreshRevisions();status.text="Список версий изменился и обновлён. Черновик сохранён на экране — повторите сохранение.";}
            catch(ArgumentOutOfRangeException){status.text="Введите число в указанном диапазоне.";}
            catch(Exception e){status.text=e.Message;}
        }
        private void Back()
        {
            if(dirty){status.text="Есть несохранённые изменения. Сохраните новую версию или нажмите «Отменить».";return;}
            Page.RemoveFromHierarchy();closed();
        }
        private void LayoutColumns()
        {
            if(grid==null)return;
            foreach(var row in grid.Children().Where(e=>e.ClassListContains("lab-parameter-row")))row.style.width=Length.Percent(TwoColumns?50:100);
        }
        private bool TwoColumns=>Page.resolvedStyle.width>=1120&&!(section==Changes&&string.IsNullOrWhiteSpace(search.value));
        private void RenderTree()
        {
            var available=PlayableProfileMetadata.Fields.Where(NativeBalanceFields.Editable).ToArray();groups.Clear();
            foreach(var category in Categories)
            {
                string choice=category;bool branch=choice!=Changes&&choice!="Звук";
                var header=OrbitalTheme.Action((branch?(expanded==choice?"▾  ":"▸  "):"")+choice+(choice==Changes?" · "+ChangeCount:""),()=>{
                    if(choice=="Звук"){openAudio();return;}
                    expanded=branch&&expanded!=choice?choice:null;section=choice;
                    if(branch){var objects=available.Where(f=>NativeBalanceLabels.Section(f)==choice).Select(NativeBalanceLabels.Entity).Distinct().ToArray();if(!objects.Contains(entity))entity=objects.FirstOrDefault();}
                    RenderFields();navigation.SetScope(Page,Back,Page.Q<Button>("lab-section-"+choice));
                },"lab-section-"+choice,choice==section);
                header.AddToClassList("lab-tree-section");header.EnableInClassList("lab-dirty",choice==Changes?ChangeCount>0:available.Any(f=>NativeBalanceLabels.Section(f)==choice&&Changed(f)));if(choice==Changes)header.style.fontSize=13;header.tooltip=choice==Changes?"Все отличия черновика от выбранной сохранённой версии, включая название.":"Открыть раздел. Остальные ветви дерева свернутся.";groups.Add(header);
                if(!branch||expanded!=choice)continue;
                var children=new VisualElement(){name="lab-children-"+choice};children.AddToClassList("lab-tree-children");groups.Add(children);
                foreach(var item in available.Where(f=>NativeBalanceLabels.Section(f)==choice).Select(NativeBalanceLabels.Entity).Distinct())
                {
                    string obj=item;
                    var action=OrbitalTheme.Action("",()=>{section=choice;entity=obj;RenderFields();navigation.SetScope(Page,Back,fields.Query<TextField>().First());},"lab-object-"+obj,obj==entity&&choice==section);
                    var icon=new OrbitalGlyph(NativeBalanceLabels.Glyph(obj)){pickingMode=PickingMode.Ignore};icon.style.width=24;icon.style.height=24;icon.style.marginRight=6;action.Add(icon);
                    action.Add(new Label(obj){pickingMode=PickingMode.Ignore});action.EnableInClassList("lab-dirty",available.Any(f=>NativeBalanceLabels.Section(f)==choice&&NativeBalanceLabels.Entity(f)==obj&&Changed(f)));children.Add(action);
                }
            }
        }
        private void RenderFields()
        {
            var available=PlayableProfileMetadata.Fields.Where(NativeBalanceFields.Editable).ToArray();RenderTree();
            grid.Clear();fields.scrollOffset=Vector2.zero;string query=search.value?.Trim()??"";
            var visible=available.Where(f=>query.Length==0?(section==Changes?Changed(f):NativeBalanceLabels.Section(f)==section&&NativeBalanceLabels.Entity(f)==entity):(f.Path+" "+NativeBalanceLabels.Label(f)+" "+NativeBalanceLabels.Help(f)+" "+NativeBalanceLabels.Entity(f)).IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0).ToArray();
            context.style.display=section==Changes&&query.Length==0?DisplayStyle.None:DisplayStyle.Flex;
            context.text=query.Length>0?"Поиск по всем разделам · "+visible.Length+" параметров":section==Changes?Changes+" · "+ChangeCount:section+" / "+entity+" · "+visible.Length+" параметров";
            if(query.Length==0&&section==Changes&&NameChanged){var rename=OrbitalTheme.Text("Название: «"+selected.DisplayName+"» → «"+name.value+"»","lab-name-change");rename.name="lab-name-change";rename.style.width=Length.Percent(100);grid.Add(rename);}
            if(visible.Length==0&&!(query.Length==0&&section==Changes&&NameChanged))grid.Add(OrbitalTheme.Text(query.Length==0&&section==Changes?"Нет несохранённых изменений.":"Параметры не найдены. Попробуйте другое название.","orbital-muted"));
            foreach(var bucket in visible.GroupBy(f=>query.Length==0&&section!=Changes?NativeBalanceLabels.Bucket(f):NativeBalanceLabels.Section(f)+" / "+NativeBalanceLabels.Entity(f)+" / "+NativeBalanceLabels.Bucket(f)))
            {
                var heading=OrbitalTheme.Text(bucket.Key,"lab-subheading");heading.style.width=Length.Percent(100);heading.style.flexShrink=0;grid.Add(heading);
                foreach(var descriptor in bucket)
                {
                    var field=descriptor;string tip=NativeBalanceLabels.Help(field)+"\nДиапазон: "+field.Minimum+"–"+field.Maximum+" "+NativeBalanceLabels.Unit(field.Unit)+" · шаг "+field.Step;
                    var row=new VisualElement(){tooltip=tip};row.AddToClassList("lab-parameter-row");row.style.flexShrink=0;row.style.width=Length.Percent(TwoColumns?50:100);row.style.paddingRight=8;grid.Add(row);
                    var button=new TextField(NativeBalanceLabels.Label(field)){name="lab-field-"+field.FieldName,isDelayed=true,tooltip=tip};button.AddToClassList("lab-parameter");row.Add(button);
                    if(section==Changes&&query.Length==0){var before=new Label("было: "+Number(field.Read(original))){name="lab-before-"+field.FieldName,tooltip="Значение в выбранной сохранённой версии"};before.AddToClassList("lab-before");row.Add(before);}
                    void Show(){button.SetValueWithoutNotify(field.Read(draft).ToString("0.###",System.Globalization.CultureInfo.InvariantCulture));}
                    button.userData=new Action<int>(step=>Try(()=>{field.Write(draft,Math.Round(Math.Max(field.Minimum,Math.Min(field.Maximum,field.Read(draft)+step*field.Step)),6));SetDirty();Show();}));
                    button.RegisterValueChangedCallback(e=>{Try(()=>{if(!double.TryParse(e.newValue,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var value))throw new ArgumentException("Введите число.");field.Write(draft,value);SetDirty();});Show();});Show();
                    var unit=new Label(NativeBalanceLabels.Unit(field.Unit)){tooltip=tip};unit.AddToClassList("lab-unit");row.Add(unit);
                    foreach(int direction in new[]{-1,1}){int delta=direction;var adjust=OrbitalTheme.Action(delta<0?"−":"+",()=>((Action<int>)button.userData)(delta));adjust.AddToClassList("lab-adjust");adjust.tooltip=tip;adjust.focusable=false;row.Add(adjust);}
                    var info=new Label("?"){tooltip=tip};info.AddToClassList("lab-info");row.Add(info);
                }
            }
        }
    }
}
