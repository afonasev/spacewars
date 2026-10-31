using System;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Spacewars.Presentation
{
    // Only the active menu scope consumes these hooks; the caller retains device ownership.
    public interface INativeMenuScope
    {
        bool Move(Vector2 direction);
        bool Submit(VisualElement focused);
        bool Cancel();
        bool ReadGamepad(Gamepad pad,float now);
        bool IncludesControl(VisualElement control);
        string Hints(Gamepad pad);
    }
    public sealed partial class PostMatchResultsView
    {
        private bool inspecting,usingPad;
        private VisualElement shoulderLeft,shoulderRight;
        private Label shoulderLeftSymbol,shoulderRightSymbol;
        private float lastPadTime,nextEventAt;
        private int selectedStack=-1;
        bool INativeMenuScope.IncludesControl(VisualElement control)
        {
            if(popup!=null)return popup.Contains(control)&&control.GetFirstAncestorOfType<OrbitalGlyph>()==null&&control.parent?.ClassListContains("result-focusable")!=true;
            return inspecting||!control.ClassListContains("result-event-marker");
        }
        bool INativeMenuScope.Move(Vector2 direction)
        {
            if(popup!=null)return false;
            if(!inspecting)return false;
            if(direction.y!=0)
            {
                activeMetric=(Math.Max(0,activeMetric)+(direction.y>0?3:1))%4;selectedStack=-1;graphs[activeMetric].Focus();InspectTime(cursorTime);return true;
            }
            StepEvent(direction.x<0?-1:1);return true;
        }
        private void StepEvent(int direction)
        {
            var chart=graphs[Math.Max(0,activeMetric)];var stacks=chart.Stacks.OrderBy(s=>s.Time).ThenBy(s=>s.Facts[0].Sequence).ToArray();
            if(stacks.Length==0){InspectTime(cursorTime+direction);return;}
            int next=direction>0?Array.FindIndex(stacks,s=>s.Time>cursorTime+1e-6):Array.FindLastIndex(stacks,s=>s.Time<cursorTime-1e-6);
            // Select separately even when several stacks share exactly the same timestamp.
            if(selectedStack>=0&&selectedStack<stacks.Length)next=Mathf.Clamp(selectedStack+direction,0,stacks.Length-1);
            if(next<0)next=direction>0?stacks.Length-1:0;selectedStack=next;InspectTime(stacks[next].Time);stacks[next].Anchor.Focus();
        }
        bool INativeMenuScope.Submit(VisualElement focused)
        {
            if(focused?.userData is Action action){action();return true;}
            int graph=graphs.FindIndex(g=>g==focused);
            if(graph<0)return false;
            if(!inspecting){inspecting=true;activeMetric=graph;selectedStack=-1;InspectTime(cursorTime);return true;}
            var stack=graphs[activeMetric].Stacks.OrderBy(s=>Math.Abs(s.Time-cursorTime)).ThenBy(s=>s.Facts[0].Sequence).FirstOrDefault();
            if(stack!=null)OpenEvents(stack.Anchor,stack.Facts);return true;
        }
        bool INativeMenuScope.Cancel()
        {
            if(popup!=null){CloseDetails(true);return true;}
            if(inspecting){inspecting=false;selectedStack=-1;graphTip.style.display=DisplayStyle.None;if(activeMetric>=0&&activeMetric<graphs.Count)graphs[activeMetric].Focus();return true;}
            return false;
        }
        bool INativeMenuScope.ReadGamepad(Gamepad pad,float now)
        {
            float dt=lastPadTime>0?Mathf.Clamp(now-lastPadTime,0,.1f):0;lastPadTime=now;
            if(pad.leftShoulder.wasPressedThisFrame||pad.rightShoulder.wasPressedThisFrame)
            {
                usingPad=true;int next=(SelectedTab+(pad.rightShoulder.wasPressedThisFrame?1:2))%3;ShowTab(next);
                VisualElement preferred=rememberedFocus.TryGetValue(next,out var id)?Root.Q(id):null;(preferred??tabs[next]).Focus();return true;
            }
            if(pad.rightStick.ReadValue().sqrMagnitude>.08f)
            {
                usingPad=true;var focused=Root.focusController?.focusedElement as VisualElement;
                var scroll=popupScroll??focused?.GetFirstAncestorOfType<ScrollView>()??focused?.parent?.Q<ScrollView>()??content.Q<ScrollView>();
                if(scroll!=null){var axis=pad.rightStick.ReadValue();scroll.scrollOffset=new Vector2(Mathf.Clamp(scroll.scrollOffset.x+axis.x*dt*420,0,Math.Max(0,scroll.horizontalScroller.highValue)),Mathf.Clamp(scroll.scrollOffset.y-axis.y*dt*420,0,Math.Max(0,scroll.verticalScroller.highValue)));}
            }
            if(!inspecting||popup!=null)return false;
            var stick=pad.leftStick.ReadValue();if(Math.Abs(stick.x)>.2f){selectedStack=-1;InspectTime(cursorTime+stick.x*dt*Math.Max(1,Result.Duration/8));}
            Vector2 dpad=pad.dpad.ReadValue();
            if(dpad.sqrMagnitude>.2f&&now>=nextEventAt){((INativeMenuScope)this).Move(dpad);nextEventAt=now+.18f;}
            return true;
        }
        string INativeMenuScope.Hints(Gamepad pad)
        {
            usingPad=true;bool sony=pad!=null&&pad.layout.IndexOf("Dual",StringComparison.OrdinalIgnoreCase)>=0;bool nintendo=pad!=null&&pad.layout.IndexOf("Switch",StringComparison.OrdinalIgnoreCase)>=0;
            shoulderLeftSymbol.text=sony?"L1":nintendo?"L":"LB";shoulderRightSymbol.text=sony?"R1":nintendo?"R":"RB";
            tabs[0].style.paddingLeft=56;shoulderLeft.style.display=shoulderRight.style.display=DisplayStyle.Flex;return "";
        }
        public void HideGamepadHints(){usingPad=false;tabs[0].style.paddingLeft=12;shoulderLeft.style.display=shoulderRight.style.display=DisplayStyle.None;}
    }
}
