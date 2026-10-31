using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Input;
using Spacewars.Simulation;

namespace Spacewars.Presentation
{
    public sealed class RadialCompositionItem
    {
        public string Artwork,Title;public int Count;public bool Upgraded;
        public string Signature=>Artwork+":"+Count+":"+Upgraded;
    }
    public static class AuxiliaryWheelPresentation
    {
        public static int[] GroupSlots(KeyboardControlGroups groups,KeyboardSelectable[] entities)
        {
            var profile=RadialCardProfile.Load();
            return groups.VisibleSlots(entities,(int)profile.minimumGroupSlots,(int)profile.spareGroupSlots);
        }
        public static OfflinePadAction[] Groups(KeyboardControlGroups groups,IEnumerable<int> selection,KeyboardSelectable[] entities,PlayableSnapshot view,bool assign,IReadOnlyList<int> slots,string completedId=null)
        {
            var selected=KeyboardControlGroups.Normalize(selection,entities);
            return groups.PadActions(selected,entities,assign,slots).Select(s=>
            {
                int index=int.Parse(s.Id);var members=groups.Members(index,entities);
                var merged=KeyboardControlGroups.Normalize(members.Concat(selected),entities);
                bool completed=s.Id==completedId;
                var data=new RadialActionPresentation{Title=completed?"Сохранено ✓":"Группа "+index,Count=members.Length,AssignmentComplete=completed};
                data.ReplaceCount=selected.Length;data.AddCount=merged.Except(members).Count();data.ResultCount=merged.Length;
                data.Note=completed?"Состав уже сохранён":members.Length==0?(s.CanAssign?"Удерживайте RB, чтобы назначить":"Группа пуста"):null;
                {
                    var ids=new HashSet<int>(members);
                    data.Composition=view.Entities.Where(e=>e.Owner==view.Owner&&e.Health>0&&ids.Contains(e.Id)).GroupBy(e=>e.Kind).OrderBy(g=>(int)g.Key).Select(g=>
                    {bool upgraded=RadialActionPresentation.Upgraded(g.Key,view.OwnerResearch);return new RadialCompositionItem{Title=OfflinePadWorldActions.Name(g.Key),Artwork=RadialActionPresentation.UnitArtwork(g.Key,upgraded),Upgraded=upgraded,Count=g.Count()};}).Concat(view.Buildings.Where(b=>b.Owner==view.Owner&&b.Health>0&&ids.Contains(b.Id)).Select(b=>new RadialCompositionItem{Title=OfflinePadWorldActions.Name(b.Kind),Artwork=OrbitalPrecision.BuildingGlyph(b.Kind),Count=1})).ToArray();

                }
                var action=new OfflinePadAction(s.Id,data.Title,s.Enabled){Presentation=data};
                action.Sector.RecallEnabled=s.RecallEnabled;action.Sector.CanAssign=s.CanAssign;action.Sector.CanAdd=s.CanAdd;return action;
            }).ToArray();
        }
        public static OfflinePadAction[] Bases(PlayableSnapshot view,int originalId)
        {
            var buildings=view.Buildings.Where(b=>b.Owner==view.Owner&&b.Health>0&&b.Phase==ConstructionPhase.Ready&&TerritoryRules.Center(b.Kind)).ToArray();
            var points=buildings.Select(b=>new OfflinePadBase(b.Id,b.Position.X,b.Position.Z,b.Id==originalId)).ToArray();
            var ordered=OfflinePadGroups.Bases(points,originalId,0,Math.Max(1,points.Length),out _);
            return ordered.Select((s,index)=>
            {
                var b=buildings.FirstOrDefault(v=>v.Id.ToString()==s.Id);
                string label=b==null?"Штаб уничтожен":b.Id==originalId?"Первый штаб":OfflinePadWorldActions.Name(b.Kind)+" "+(index+1);
                return new OfflinePadAction(s.Id,label,s.Enabled){Point=b==null?(NavPoint?)null:b.Position,Presentation=new RadialActionPresentation{Title=label,Description=s.Enabled?"Переход камеры к вашей базе. Выделение сохраняется.":null,Artwork=b==null?"headquarters":OrbitalPrecision.BuildingGlyph(b.Kind),Note=s.Enabled?null:"Эта база больше недоступна"}};
            }).ToArray();
        }
    }
}
