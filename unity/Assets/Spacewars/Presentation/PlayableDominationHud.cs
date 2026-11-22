using System;
using System.Linq;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.UIElements;

namespace Spacewars.Presentation
{
    public sealed partial class PlayableBootstrap
    {
        private DominationFlag dominationFlag;

        private void UpdateDominationFlag()
        {
            if(localCoordinator!=null||panelRoot==null)return;
            if(view?.Domination is not PlayableDominationSnapshot state||inLobby||preparing||MenuOwnsInput||ResultsVisible)
            {
                if(dominationFlag!=null)dominationFlag.style.display=DisplayStyle.None;
                return;
            }
            if(dominationFlag==null)
            {
                dominationFlag=new DominationFlag();
                panelRoot.Add(dominationFlag);
            }
            Color TeamColor(int team)
            {
                var participant=view.Participants.Select((entry,slot)=>(entry.Team,slot)).FirstOrDefault(entry=>entry.Team==team);
                return team==0?DominationFlag.Neutral:LobbyPaint((PlayableOwner)participant.slot);
            }
            dominationFlag.style.display=DisplayStyle.Flex;
            dominationFlag.UpdateValue(state,TeamColor(state.ScoringTeam),TeamColor(state.LeadingTeam),view.Tick);
        }

        private sealed class DominationFlag : VisualElement
        {
            internal static readonly Color Neutral=new Color(.69f,.73f,.76f);
            private Color fill,frame;
            private readonly Label score;
            internal DominationFlag()
            {
                name="domination-flag";
                pickingMode=PickingMode.Ignore;
                style.position=Position.Absolute;
                style.left=Length.Percent(50);
                style.translate=new Translate(Length.Percent(-50),0,0);
                style.top=0;
                style.width=50;
                style.height=62;
                score=new Label("—") { pickingMode=PickingMode.Ignore };
                score.style.marginLeft=score.style.marginRight=0;
                score.style.marginTop=14;
                score.style.unityTextAlign=TextAnchor.MiddleCenter;
                score.style.unityFontStyleAndWeight=FontStyle.Bold;
                score.style.fontSize=19;
                Add(score);
                generateVisualContent+=Paint;
            }

            internal void UpdateValue(PlayableDominationSnapshot state,Color team,Color incoming,long tick)
            {
                bool falling=state.Score>0&&!state.Rising;
                float pulse=falling?.35f*(1-Mathf.Cos(tick*Mathf.PI/30)):0;
                var nextFill=Color.Lerp(state.ScoringTeam==0?Neutral:Color.Lerp(team,new Color(.05f,.1f,.13f),.40f),Color.white,pulse);
                var nextFrame=falling?incoming:new Color(.05f,.10f,.13f);
                if(fill!=nextFill||frame!=nextFrame){fill=nextFill;frame=nextFrame;MarkDirtyRepaint();}
                string text=state.Score<=0&&!state.Rising?"—":Math.Max(0,Math.Ceiling(state.SecondsToVictory-1e-9)).ToString();
                if(score.text!=text)score.text=text;
                score.style.color=state.Rising&&state.SecondsToVictory<=10+1e-9?new Color(1,.35f,.42f):
                    state.WarningActive?new Color(1,.70f,.35f):
                    state.ScoringTeam==0||pulse>.45f?new Color(.05f,.1f,.13f):Color.white;
                score.style.unityTextOutlineColor=new Color(.05f,.1f,.13f);
                score.style.unityTextOutlineWidth=state.ScoringTeam!=0&&pulse<=.45f?1:0;
            }

            private void Paint(MeshGenerationContext context)
            {
                var painter=context.painter2D;
                painter.fillColor=fill;
                painter.strokeColor=frame;
                painter.lineWidth=3;
                const float inset=1.5f;
                var w=contentRect.width;
                var h=contentRect.height;
                painter.BeginPath();
                painter.MoveTo(new Vector2(inset,inset));
                painter.LineTo(new Vector2(w-inset,inset));
                painter.LineTo(new Vector2(w-inset,h-12));
                painter.LineTo(new Vector2(w/2,h-2));
                painter.LineTo(new Vector2(inset,h-12));
                painter.ClosePath();
                painter.Fill();
                painter.Stroke();
            }
        }
    }
}
