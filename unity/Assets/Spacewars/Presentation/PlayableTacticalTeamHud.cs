using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Runtime;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.UIElements;

namespace Spacewars.Presentation
{
    public sealed partial class PlayableBootstrap
    {
        private const float TacticalInset=12, TacticalGap=12, TacticalTeamWidth=320, TacticalToggleWidth=110;
        private const float TacticalToggleHeight=28, TacticalHeadingHeight=20, TacticalRowHeight=42;
        private VisualElement tacticalTeam, tacticalTeamBody;
        private ScrollView tacticalTeamRows;
        private Button tacticalTeamToggle;
        private readonly List<TacticalTeamRow> tacticalRows=new List<TacticalTeamRow>();
        private readonly List<TacticalOpponentRow> tacticalOpponents=new List<TacticalOpponentRow>();
        private float tacticalContentHeight;
        private OfflinePresentationFrame tacticalTeamFrame;
        private bool tacticalTeamCollapsed, tacticalTeamReplacesHud;
        private bool TacticalTeamActive=>mapOpen&&!spectatorMode&&matchSetup?.Spectator!=true&&
            campaignConfiguration==null&&provingGround==null&&matchSetup?.Participants!=null&&ownerFrame!=null&&view!=null;

        private void CreateTacticalTeam()
        {
            tacticalTeam=Panel();
            tacticalTeam.name="tactical-team";
            StyleRegion(tacticalTeam);
            tacticalTeam.style.paddingTop=tacticalTeam.style.paddingBottom=0;
            tacticalTeam.style.paddingLeft=tacticalTeam.style.paddingRight=0;
            tacticalTeam.style.display=DisplayStyle.None;

            tacticalTeamToggle=Button("",ToggleTacticalTeam);
            tacticalTeamToggle.name="tactical-team-toggle";
            tacticalTeamToggle.focusable=false;
            tacticalTeamToggle.style.height=TacticalToggleHeight;
            tacticalTeamToggle.style.minHeight=TacticalToggleHeight;
            tacticalTeamToggle.style.flexShrink=0;
            tacticalTeamToggle.style.marginLeft=tacticalTeamToggle.style.marginRight=0;
            tacticalTeamToggle.style.marginTop=tacticalTeamToggle.style.marginBottom=0;
            tacticalTeamToggle.style.fontSize=12;
            tacticalTeamToggle.style.whiteSpace=WhiteSpace.NoWrap;
            tacticalTeamToggle.style.paddingLeft=tacticalTeamToggle.style.paddingRight=6;
            tacticalTeamToggle.style.unityTextAlign=TextAnchor.MiddleRight;
            tacticalTeam.Add(tacticalTeamToggle);

            tacticalTeamBody=new VisualElement{name="tactical-team-body"};
            tacticalTeamBody.style.flexShrink=1;
            tacticalTeamBody.style.minHeight=0;
            tacticalTeam.Add(tacticalTeamBody);
            tacticalTeamRows=new ScrollView(ScrollViewMode.Vertical){name="tactical-team-rows",focusable=false};
            tacticalTeamRows.style.flexShrink=1;
            tacticalTeamRows.style.minHeight=0;
            tacticalTeamRows.verticalScrollerVisibility=ScrollerVisibility.Hidden;
            tacticalTeamBody.Add(tacticalTeamRows);
            tacticalOverlay.Add(tacticalTeam);
        }

        private void ToggleTacticalTeam()
        {
            if(!TacticalTeamActive)return;
            tacticalTeamCollapsed=!tacticalTeamCollapsed;
            SyncTacticalTeam();
            LayoutTacticalMap();
        }

        private void SyncTacticalTeam()
        {
            bool active=TacticalTeamActive;
            tacticalTeam.style.display=active?DisplayStyle.Flex:DisplayStyle.None;
            if(active)
            {
                top.style.display=DisplayStyle.None;
                armyComposition.style.display=DisplayStyle.None;
                tacticalTeamToggle.text=tacticalTeamCollapsed?"Участники  RT ▾":"УЧАСТНИКИ  RT ▴";
                tacticalTeamBody.style.display=tacticalTeamCollapsed?DisplayStyle.None:DisplayStyle.Flex;
                if(!tacticalTeamCollapsed)RefreshTacticalTeam();
            }
            else if(tacticalTeamReplacesHud)
            {
                top.style.display=inLobby||preparing?DisplayStyle.None:DisplayStyle.Flex;
            }
            tacticalTeamReplacesHud=active;
        }

        private void RefreshTacticalTeam()
        {
            if(!TacticalTeamActive||tacticalTeamCollapsed||ReferenceEquals(tacticalTeamFrame,ownerFrame))return;
            if(tacticalTeamFrame!=null&&tacticalTeamFrame.Generation!=ownerFrame.Generation)tacticalTeamRows.scrollOffset=Vector2.zero;
            tacticalTeamFrame=ownerFrame;
            int row=0;float height=0;
            // Only read teammate perspectives, in stable roster order before sorting.
            var teammates=view.Participants.Where(p=>p.Team==view.Team)
                .Select(p=>ownerFrame.Views[p.Id])
                .OrderByDescending(p=>p.Owner==view.Owner)
                .ThenByDescending(p=>p.Population.Living);
            foreach(var teammate in teammates)
            {
                if(row==tacticalRows.Count)
                {
                    var added=new TacticalTeamRow();
                    tacticalRows.Add(added);
                    tacticalTeamRows.Add(added.Root);
                }
                var item=tacticalRows[row++];
                item.Root.style.display=DisplayStyle.Flex;
                item.Set(teammate,matchSetup.ParticipantName((int)teammate.Owner),LobbyPaint(teammate.Owner),teammate.Owner==view.Owner);
                height+=TacticalRowHeight+(teammate.Owner==view.Owner?2*TacticalHeadingHeight:0);
            }
            for(int i=row;i<tacticalRows.Count;i++)tacticalRows[i].Root.style.display=DisplayStyle.None;
            int opponent=0;
            // Public metadata is sufficient for opponents. Never read ownerFrame.Views for them.
            foreach(var team in view.Participants.Where(p=>p.Team!=view.Team).GroupBy(p=>p.Team))
            {
                bool firstInTeam=true;
                foreach(var participant in team)
                {
                    if(opponent==tacticalOpponents.Count)
                    {
                        var added=new TacticalOpponentRow();tacticalOpponents.Add(added);tacticalTeamRows.Add(added.Root);
                    }
                    var item=tacticalOpponents[opponent];
                    item.Set(participant,matchSetup.ParticipantName(participant.LogicalPlayer-1),LobbyPaint((PlayableOwner)(participant.LogicalPlayer-1)),firstInTeam,ownerFrame.EliminatedOwners.Contains(participant.Id));
                    item.Root.BringToFront();height+=item.Height;opponent++;firstInTeam=false;
                }
            }
            for(int i=opponent;i<tacticalOpponents.Count;i++)tacticalOpponents[i].Root.style.display=DisplayStyle.None;
            if(Mathf.Approximately(tacticalContentHeight,height))return;
            tacticalContentHeight=height;
            LayoutTacticalMap();
        }

        private void LayoutTacticalTeamMap()
        {
            float width=Mathf.Max(0,tacticalOverlay.contentRect.width+tacticalOverlay.resolvedStyle.paddingLeft+tacticalOverlay.resolvedStyle.paddingRight-2*TacticalInset);
            // Device diagnostics sit above the player viewports and may straddle a split seam.
            float topInset=NativeDevicePreferences.ShowFps||NativeDevicePreferences.ShowTps?36:TacticalInset;
            float height=Mathf.Max(0,tacticalOverlay.contentRect.height+tacticalOverlay.resolvedStyle.paddingTop+tacticalOverlay.resolvedStyle.paddingBottom-topInset-TacticalInset);
            // The regions are computed for the local viewport, not the full screen.
            float panelWidth=Mathf.Min(width,tacticalTeamCollapsed?TacticalToggleWidth:TacticalTeamWidth);
            float desiredHeight=tacticalTeamCollapsed?TacticalToggleHeight:TacticalToggleHeight+tacticalContentHeight+2;
            float below=Mathf.Min(width,height-desiredHeight-TacticalGap);
            float beside=Mathf.Min(width-panelWidth-TacticalGap,height);
            bool side=beside>=below;
            float panelHeight=Mathf.Min(desiredHeight,side?height:Mathf.Max(TacticalToggleHeight,height*.62f));
            float availableWidth=side?width-panelWidth-TacticalGap:width;
            float availableHeight=side?height:height-panelHeight-TacticalGap;
            float size=Mathf.Max(20,Mathf.Min(availableWidth,availableHeight));
            float x=TacticalInset+(availableWidth-size)/2;
            float y=topInset+(availableHeight-size)/2+(side?0:panelHeight+TacticalGap);
            if(tacticalTeamCollapsed)
            {
                float centered=Mathf.Min(width,height);
                if((width-centered)/2>=panelWidth+TacticalGap||(height-centered)/2>=panelHeight+TacticalGap)
                {
                    size=centered;
                    x=TacticalInset+(width-size)/2;
                    y=topInset+(height-size)/2;
                }
            }
            tacticalTeam.style.width=panelWidth;
            tacticalTeam.style.height=panelHeight;
            tacticalTeam.style.top=topInset;
            tacticalTeam.style.right=TacticalInset;
            var frame=tacticalMap.parent;
            frame.style.position=Position.Absolute;
            frame.style.left=x;
            frame.style.top=y;
            tacticalMap.style.width=tacticalMap.style.height=size-20;
        }

        private static void StyleRosterElimination(VisualElement row,bool eliminated,Color activeBackground)
        {
            row.EnableInClassList("roster-eliminated",eliminated);
            row.style.backgroundColor=eliminated?new Color(.13f,.15f,.16f,.95f):activeBackground;
            row.style.opacity=eliminated?.5f:1;
        }

        private sealed class TacticalOpponentRow
        {
            public readonly VisualElement Root=new VisualElement();
            private readonly Label team,name;
            public float Height{get;private set;}
            public TacticalOpponentRow()
            {
                Root.style.flexShrink=0;Root.AddToClassList("tactical-opponent");
                team=TacticalTeamRow.Cell("",0);team.name="tactical-opponent-team";team.style.height=TacticalHeadingHeight;
                name=TacticalTeamRow.Cell("",0);name.name="tactical-player-name";name.style.height=28;name.style.paddingTop=5;
                foreach(var label in new[]{team,name}){label.style.paddingLeft=label.style.paddingRight=8;Root.Add(label);}
                name.style.unityFontStyleAndWeight=FontStyle.Bold;
            }
            public void Set(OfflineParticipant participant,string playerName,Color color,bool firstInTeam,bool eliminated)
            {
                Root.name="tactical-player-"+participant.Id;Root.userData=participant.Team;Root.style.display=DisplayStyle.Flex;
                var shade=Color.HSVToRGB(Mathf.Repeat(participant.Team*.618034f,1),.24f,.16f);shade.a=1;
                Root.style.backgroundColor=shade;
                team.text="КОМАНДА "+participant.Team;team.style.display=firstInTeam?DisplayStyle.Flex:DisplayStyle.None;
                team.style.color=new Color(.66f,.76f,.81f);team.style.unityFontStyleAndWeight=FontStyle.Bold;
                name.text=playerName;name.style.color=color;
                StyleRosterElimination(name,eliminated,shade);
                Height=28+(firstInTeam?TacticalHeadingHeight:0);
                Root.style.height=Height;
            }
        }

        private sealed class TacticalTeamRow
        {
            private static readonly PlayableEntityKind[] Kinds={PlayableEntityKind.Explorer,PlayableEntityKind.Tank,PlayableEntityKind.Shkval,PlayableEntityKind.Hawk,PlayableEntityKind.Igla,PlayableEntityKind.Cerberus};
            public readonly VisualElement Root=new VisualElement();
            private readonly Label name,credits,income,population;
            private readonly VisualElement abilities,heading,status;
            private readonly Label[] counts=new Label[Kinds.Length];
            private readonly VisualElement[] unitCells=new VisualElement[Kinds.Length];
            private readonly int[] living=new int[Kinds.Length];
            private readonly List<int> abilityIds=new List<int>();

            public TacticalTeamRow()
            {
                Root.style.height=TacticalRowHeight;
                Root.style.flexShrink=0;
                heading=new VisualElement{name="tactical-allies-heading"};
                var section=Cell("СОЮЗНИКИ",0);section.style.height=TacticalHeadingHeight;section.style.paddingLeft=8;
                heading.Add(section);
                var columns=Line(TacticalHeadingHeight);
                columns.Add(Cell("Игрок",0,true));columns.Add(Cell("Кредиты",21));columns.Add(Cell("Доход",20));columns.Add(Cell("Лимит ↓",22));
                heading.Add(columns);Root.Add(heading);
                status=new VisualElement{name="tactical-player-status"};status.style.flexShrink=0;Root.Add(status);
                var values=Line(20);
                name=Cell("",0,true);name.name="tactical-player-name";
                credits=Cell("",21);
                income=Cell("",20);
                population=Cell("",22);population.name="tactical-player-population";
                values.Add(name);values.Add(credits);values.Add(income);values.Add(population);
                status.Add(values);

                var details=Line(22);
                for(int i=0;i<Kinds.Length;i++)
                {
                    var unitCell=new VisualElement{name="tactical-unit-"+Kinds[i],pickingMode=PickingMode.Ignore};
                    unitCell.style.flexDirection=FlexDirection.Row;
                    unitCell.style.alignItems=Align.Center;
                    unitCells[i]=unitCell;
                    var portrait=new ModelPortrait("tactical-unit-portrait");
                    portrait.style.width=portrait.style.height=18;
                    portrait.Set(RadialActionPresentation.UnitArtwork(Kinds[i],false),fitContent:true);
                    unitCell.Add(portrait);
                    counts[i]=Cell("",0);
                    counts[i].style.minWidth=17;
                    unitCell.Add(counts[i]);
                    details.Add(unitCell);
                }
                abilities=new VisualElement{name="tactical-player-abilities"};
                abilities.style.flexGrow=1;
                abilities.style.flexDirection=FlexDirection.Row;
                abilities.style.justifyContent=Justify.FlexEnd;
                details.Add(abilities);
                status.Add(details);
            }

            public static VisualElement Line(float height)
            {
                var line=new VisualElement();
                line.style.height=height;
                line.style.flexShrink=0;
                line.style.flexDirection=FlexDirection.Row;
                line.style.alignItems=Align.Center;
                line.style.paddingLeft=line.style.paddingRight=8;
                return line;
            }

            public static Label Cell(string value,int width,bool expand=false)
            {
                var label=new Label(value){pickingMode=PickingMode.Ignore};
                label.style.color=Ink;
                label.style.fontSize=11;
                label.style.marginLeft=label.style.marginRight=label.style.marginTop=label.style.marginBottom=0;
                label.style.paddingLeft=label.style.paddingRight=0;
                label.style.whiteSpace=WhiteSpace.NoWrap;
                label.style.overflow=Overflow.Hidden;
                label.style.textOverflow=TextOverflow.Ellipsis;
                if(width>0){label.style.width=Length.Percent(width);label.style.unityTextAlign=TextAnchor.MiddleRight;}
                if(expand){label.style.flexGrow=1;label.style.minWidth=0;}
                return label;
            }

            public void Set(PlayableSnapshot participant,string playerName,Color color,bool self)
            {
                Root.name="tactical-player-"+participant.OwnerId;
                heading.style.display=self?DisplayStyle.Flex:DisplayStyle.None;
                Root.style.height=TacticalRowHeight+(self?2*TacticalHeadingHeight:0);
                var activeBackground=self?new Color(.14f,.23f,.28f):new Color(.1f,.16f,.20f);
                Root.style.backgroundColor=activeBackground;
                StyleRosterElimination(status,participant.OwnerEliminated,activeBackground);
                name.text=self?"Вы · "+playerName:playerName;
                name.style.color=color;
                name.style.unityFontStyleAndWeight=FontStyle.Bold;
                credits.text=participant.Credits.ToString();
                income.text="+"+participant.IncomePerSecond.ToString("0.#")+"/с";
                population.text=participant.Population.Living+"/"+participant.Population.Capacity;
                Array.Clear(living,0,living.Length);
                foreach(var entity in participant.Entities)
                {
                    if(entity.Owner!=participant.Owner||entity.Health<=0)continue;
                    int kind=Array.IndexOf(Kinds,entity.Kind);
                    if(kind>=0)living[kind]++;
                }
                for(int i=0;i<living.Length;i++)
                {
                    counts[i].text=living[i].ToString();
                    unitCells[i].style.display=living[i]>0?DisplayStyle.Flex:DisplayStyle.None;
                }
                UpdateAbilities(participant.Orbital);
            }

            private void UpdateAbilities(OrbitalEscalationSnapshot state)
            {
                if(abilityIds.Count!=state.Abilities.Count||!abilityIds.SequenceEqual(state.Abilities.Select(a=>a.Id)))
                {
                    abilities.Clear();
                    abilityIds.Clear();
                    foreach(var ability in state.Abilities)
                    {
                        var tile=new VisualElement{name="tactical-ability-"+ability.Id,pickingMode=PickingMode.Ignore};
                        tile.style.width=tile.style.height=18;
                        tile.style.marginLeft=2;
                        tile.style.backgroundColor=new Color(.07f,.12f,.16f);
                        var art=new OrbitalPlaceholderImage(ability.Id){pickingMode=PickingMode.Ignore};
                        AbsoluteFill(art);
                        tile.Add(art);
                        var seconds=Cell("",0);
                        AbsoluteFill(seconds);
                        seconds.style.unityTextAlign=TextAnchor.MiddleCenter;
                        seconds.style.unityFontStyleAndWeight=FontStyle.Bold;
                        tile.Add(seconds);
                        abilities.Add(tile);
                        abilityIds.Add(ability.Id);
                    }
                }
                for(int i=0;i<state.Abilities.Count;i++)
                {
                    var ability=state.Abilities[i];
                    var tile=abilities[i];
                    double wait=Math.Max(ability.Remaining,state.CommonRemaining);
                    bool cooling=wait>1e-9;
                    var art=(OrbitalPlaceholderImage)tile[0];
                    art.SetId(ability.Id,state.ReinforcementQuote.Kind);
                    art.style.opacity=cooling?.35f:1;
                    ((Label)tile[1]).text=cooling?Math.Ceiling(wait).ToString():"";
                    tile.style.borderBottomWidth=cooling?0:1;
                    tile.style.borderBottomColor=Cyan;
                }
            }
        }
    }
}
