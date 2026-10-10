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
        private PlayableProfileData draft;
        private bool dirty;
        private string group;
        private readonly Label status;
        private readonly TextField name,search;
        private readonly ScrollView fields;
        private readonly VisualElement groups;
        private readonly DropdownField revisions;
        private readonly Button save,applyButton;
        private readonly Label hints;
        public NativeBalanceView(VisualElement root,NativeMenuNavigation navigation,NativeBalanceStore store,Action closed,Func<PlayableProfile,string> apply,PlayableProfile currentMatch=null)
        {
            this.navigation=navigation;this.store=store;this.closed=closed;this.apply=apply;
            Page=OrbitalTheme.Screen(root,"laboratory-screen");var card=OrbitalTheme.Card(Page);card.AddToClassList("orbital-lab-card");
            var title=OrbitalTheme.Text("ЛАБОРАТОРИЯ ГЕЙМДИЗАЙНА","orbital-title");card.Add(title);
            status=OrbitalTheme.Text("Параметры игры и юнитов. Карта и геометрия не редактируются.","orbital-muted");card.Add(status);
            revisions=new DropdownField("Сохранённая версия"){name="lab-revisions"};card.Add(revisions);
            name=new TextField("Название"){name="lab-name",maxLength=64};name.RegisterValueChangedCallback(_=>SetDirty());card.Add(name);
            var toolbar=new VisualElement();toolbar.AddToClassList("orbital-toolbar");toolbar.AddToClassList("orbital-lab-toolbar");card.Add(toolbar);
            save=OrbitalTheme.Action("Сохранить версию",Save,"lab-save",true);toolbar.Add(save);
            toolbar.Add(OrbitalTheme.Action("Отменить",()=>Load(selected),"lab-reset"));
            applyButton=OrbitalTheme.Action("Применить",Apply,"lab-apply");toolbar.Add(applyButton);
            toolbar.Add(OrbitalTheme.Action("Для нового матча",()=>Try(()=>{store.Select(selected.Revision);status.text="Для следующего матча: "+selected.DisplayName+" · "+selected.Revision;}),"lab-select"));
            toolbar.Add(OrbitalTheme.Action("Звук",()=>{Page.style.display=DisplayStyle.None;NativeGameplayAudioProfile.Open(root,navigation,()=>{Page.style.display=DisplayStyle.Flex;navigation.SetScope(Page,Back,save,hints);});},"lab-audio"));
            toolbar.Add(OrbitalTheme.Action("Назад",Back,"lab-back"));
            search=new TextField("Поиск параметра"){name="lab-search"};search.RegisterValueChangedCallback(_=>RenderFields());card.Add(search);
            var body=new VisualElement();body.style.flexDirection=FlexDirection.Row;body.style.flexGrow=1;body.style.minHeight=80;card.Add(body);
            var sidebar=new ScrollView();sidebar.style.width=210;sidebar.style.flexShrink=0;body.Add(sidebar);groups=sidebar.contentContainer;groups.AddToClassList("orbital-lab-groups");sidebar.horizontalScrollerVisibility=ScrollerVisibility.Hidden;groups.style.width=Length.Percent(100);
            fields=new ScrollView(){name="lab-parameters",horizontalScrollerVisibility=ScrollerVisibility.Hidden};fields.contentContainer.style.width=Length.Percent(100);fields.contentContainer.style.minWidth=0;fields.style.flexGrow=1;fields.style.minWidth=0;fields.style.marginLeft=16;body.Add(fields);
            hints=OrbitalTheme.Text("","orbital-hints");card.Add(hints);
            Page.RegisterCallback<GeometryChangedEvent>(_=>{bool compact=Page.resolvedStyle.width<900;sidebar.style.width=compact?160:210;title.style.fontSize=compact?26:32;title.style.marginBottom=compact?12:20;card.style.paddingLeft=card.style.paddingRight=card.style.paddingTop=card.style.paddingBottom=compact?16:24;});
            RefreshRevisions();selected=currentMatch??store.Selected;Load(selected);
            if(currentMatch!=null)status.text="В матче: "+currentMatch.DisplayName+" · "+currentMatch.Revision+". Применение — после продолжения.";
            revisions.RegisterValueChangedCallback(e=>{if(dirty){status.text="Сохраните или отмените черновик перед сменой версии.";RefreshRevisions();return;}int index=revisions.choices.IndexOf(e.newValue);var row=store.State["revisions"][index];Load(store.Compile(row));});
            navigation.SetScope(Page,Back,revisions,hints);
        }
        private void RefreshRevisions()
        {
            revisions.choices=store.State["revisions"].Select(r=>(string)r["name"]+" · "+(int)r["revision"]).ToList();
            if(selected!=null)revisions.SetValueWithoutNotify(selected.DisplayName+" · "+selected.Revision);
        }
        private void Load(PlayableProfile profile)
        {
            selected=profile;draft=profile.CopyData();name.SetValueWithoutNotify(profile.DisplayName);dirty=false;RefreshRevisions();
            save.SetEnabled(false);applyButton.SetEnabled(apply!=null);RenderFields();
        }
        private void SetDirty(){dirty=true;save?.SetEnabled(true);applyButton?.SetEnabled(false);}
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
        private static string GroupLabel(string value)
        {
            switch(value){case "Tank":case "Combat":return "Танк";case "Explorer":return "Исследователь";case "Economy":return "Экономика";case "Army":return "Армия";case "Territory":return "Здания и доход";case "Capture":return "Захват";case "Science and refinery":return "Наука и переработка";case "Construction":return "Строительство";case "Buildings":return "Здания";case "Artillery":return "Артиллерия";case "Production":return "Производство";case "Research / tank chassis":return "Исследования · Танк";case "Research / Explorer":return "Исследования · Исследователь";case "Research / Shkval":return "Исследования · Шквал";case "Vision and map":return "Обзор";case "Follow":return "Следование";case "Behavior":return "Поведение";case "System":return "Автооборона";default:return value;}
        }
        private void RenderFields()
        {
            var available=PlayableProfileMetadata.Fields.Where(NativeBalanceFields.Editable).ToArray();
            var categories=available.Select(f=>GroupLabel(f.Group)).Distinct().ToArray();if(group==null||!categories.Contains(group))group=categories[0];
            groups.Clear();foreach(var category in categories){string choice=category;groups.Add(OrbitalTheme.Action(choice,()=>{group=choice;search.SetValueWithoutNotify("");RenderFields();navigation.SetScope(Page,Back,fields.Query<TextField>().First(),hints);},"lab-group-"+choice,choice==group));}
            fields.Clear();string query=search.value?.Trim()??"";
            foreach(var descriptor in available.Where(f=>query.Length==0?GroupLabel(f.Group)==group:(f.Path+" "+NativeBalanceLabels.Label(f)+" "+f.Description+" "+GroupLabel(f.Group)).IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0))
            {
                var field=descriptor;var row=new VisualElement();row.style.marginBottom=8;fields.Add(row);
                var editRow=new VisualElement();editRow.style.flexDirection=FlexDirection.Row;row.Add(editRow);
                var button=new TextField(NativeBalanceLabels.Label(field)){name="lab-field-"+field.FieldName,isDelayed=true};button.style.flexGrow=1;button.style.flexShrink=1;button.style.flexBasis=0;button.style.minWidth=0;button.style.marginBottom=0;button.labelElement.style.minWidth=0;button.labelElement.style.width=Length.Percent(58);button.labelElement.style.flexShrink=1;editRow.Add(button);
                void Show(){button.SetValueWithoutNotify(field.Read(draft).ToString("0.###",System.Globalization.CultureInfo.InvariantCulture));}
                button.userData=new Action<int>(step=>Try(()=>{field.Write(draft,Math.Round(Math.Max(field.Minimum,Math.Min(field.Maximum,field.Read(draft)+step*field.Step)),6));SetDirty();Show();}));
                button.RegisterValueChangedCallback(e=>{Try(()=>{if(!double.TryParse(e.newValue,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var value))throw new ArgumentException("Введите число.");field.Write(draft,value);SetDirty();});Show();});Show();
                var range=OrbitalTheme.Text("Диапазон: "+field.Minimum+"–"+field.Maximum+" "+NativeBalanceLabels.Unit(field.Unit)+"  ·  шаг "+field.Step,"orbital-muted");range.style.marginTop=4;range.style.marginBottom=4;row.Add(range);
                foreach(int direction in new[]{-1,1}){int delta=direction;var adjust=OrbitalTheme.Action(delta<0?"−":"+",()=>((Action<int>)button.userData)(delta));adjust.style.width=40;adjust.style.paddingLeft=0;adjust.style.paddingRight=0;adjust.style.unityTextAlign=TextAnchor.MiddleCenter;adjust.style.marginLeft=6;adjust.focusable=false;editRow.Add(adjust);}
            }
        }
    }
}
