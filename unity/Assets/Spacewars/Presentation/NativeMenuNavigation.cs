using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Spacewars.Presentation
{
    // Exactly one active scope owns menu input. World input is gated by the caller.
    public sealed class NativeMenuNavigation : IDisposable
    {
        private readonly VisualElement root;
        private readonly Func<Gamepad> gamepad;
        private readonly Func<bool> worldOwnsInput,keyboardAllowed;
        public Func<bool> Focused;
        // Directional world input can stay active while a HUD button retains submit focus.
        public Func<bool> WorldOwnsNavigation;
        private VisualElement scope;
        private Action back;
        private Label hints;
        private bool dispatching,armed;
        private readonly HashSet<KeyCode> pressedKeys=new HashSet<KeyCode>();
        private readonly HashSet<NavigationSubmitEvent> ownedSubmits=new HashSet<NavigationSubmitEvent>();
        private Vector2 held;
        private float repeatAt;
        private string lastFocus;
        public Gamepad CurrentGamepad=>gamepad();
        public bool UsingGamepad { get; private set; }
        public bool Active => scope!=null;
        public VisualElement Scope => scope;
        // A route may consume Start for its own ready action instead of treating it as Back.
        public Func<Gamepad,bool> Start;
        public Func<Gamepad,bool> North;
        public NativeMenuNavigation(VisualElement root):this(root,null){}
        public NativeMenuNavigation(VisualElement root,Func<Gamepad> gamepad,Func<bool> worldOwnsInput=null,Func<bool> keyboardAllowed=null)
        {
            this.root=root;this.gamepad=gamepad??(()=>Gamepad.current);this.worldOwnsInput=worldOwnsInput;this.keyboardAllowed=keyboardAllowed;
            root.RegisterCallback<KeyDownEvent>(KeyDown,TrickleDown.TrickleDown);
            root.RegisterCallback<KeyUpEvent>(KeyUp,TrickleDown.TrickleDown);root.RegisterCallback<WheelEvent>(Wheel,TrickleDown.TrickleDown);
            root.RegisterCallback<NavigationMoveEvent>(BlockMove,TrickleDown.TrickleDown);
            root.RegisterCallback<NavigationSubmitEvent>(BlockSubmit,TrickleDown.TrickleDown);
            root.RegisterCallback<NavigationCancelEvent>(BlockCancel,TrickleDown.TrickleDown);
            root.RegisterCallback<PointerDownEvent>(PointerDown,TrickleDown.TrickleDown);
        }
        public void UseGamepad(){UsingGamepad=true;UpdateHints();}
        public void SetScope(VisualElement element,Action onBack=null,VisualElement preferred=null,Label hintLabel=null)
        {
            if(scope!=null)foreach(var label in scope.Query<Label>(name:"menu-pad-glyph").ToList())label.RemoveFromHierarchy();
            scope=element;back=onBack;hints=hintLabel;armed=false;held=Vector2.zero;
            if(scope==null)return;
            Focus(Visible(preferred)?preferred:Controls().FirstOrDefault());UpdateHints();
        }
        public void Tick()
        {
            if(scope==null||!(Focused?.Invoke()??Application.isFocused))return;
            TickGamepad(gamepad(),Time.unscaledTime);
            if(keyboardAllowed?.Invoke()!=false&&Mouse.current?.delta.ReadValue().sqrMagnitude>0){UsingGamepad=false;UpdateHints();}
        }
        private bool DropdownOpen=>root.panel?.visualTree.Q(className:"unity-base-dropdown")!=null;
        private void TickGamepad(Gamepad pad,float now)
        {
            // Native popups live outside the menu scope and own their focus/input until closed.
            if(scope==null||DropdownOpen)return;
            if(pad==null){armed=true;return;}
            Vector2 axis=pad.dpad.ReadValue();if(axis.sqrMagnitude<.1f)axis=pad.leftStick.ReadValue();
            axis=axis.magnitude<.55f?Vector2.zero:Mathf.Abs(axis.x)>Mathf.Abs(axis.y)?new Vector2(Mathf.Sign(axis.x),0):new Vector2(0,Mathf.Sign(axis.y));
            bool north=pad.buttonNorth.isPressed;
            bool confirm=pad.buttonSouth.isPressed,cancel=pad.buttonEast.isPressed||pad.startButton.isPressed;
            if(axis!=Vector2.zero||pad.buttonNorth.wasPressedThisFrame||pad.buttonSouth.wasPressedThisFrame||pad.buttonEast.wasPressedThisFrame||pad.startButton.wasPressedThisFrame){UseGamepad();}
            if(!armed){if(axis==Vector2.zero&&!confirm&&!cancel&&!north)armed=true;return;}
            if(axis!=Vector2.zero&&(axis!=held||now>=repeatAt))
            {Move(axis);repeatAt=now+(axis!=held?.35f:.10f);}
            held=axis;
            if(pad.buttonEast.wasPressedThisFrame){Back();return;}
            if(pad.startButton.wasPressedThisFrame)
            {
                if(Start?.Invoke(pad)!=true)Back();
                return;
            }
            if(pad.buttonNorth.wasPressedThisFrame&&North?.Invoke(pad)==true)return;
            if(pad.buttonSouth.wasPressedThisFrame)Activate();
            var focused=root.focusController?.focusedElement as VisualElement;
            if(UsingGamepad&&(!Visible(focused)||!scope.Contains(focused)))Focus(Controls().FirstOrDefault(x=>x.name==lastFocus)??Controls().FirstOrDefault());
        }
        public void Move(Vector2 direction)
        {
            var controls=Controls();if(controls.Count==0)return;
            var focused=Current(controls);
            if(scope.ClassListContains("orbital-keyboard")&&focused!=null)
            {
                var axis=new Vector2(direction.x,-direction.y);var origin=focused.worldBound.center;
                var next=controls.Where(x=>x!=focused&&Vector2.Dot(x.worldBound.center-origin,axis)>1)
                    .OrderBy(x=>{var delta=x.worldBound.center-origin;return Vector2.Dot(delta,axis)+4*Mathf.Abs(delta.x*axis.y-delta.y*axis.x);}).FirstOrDefault();
                Focus(next??focused);return;
            }
            if(focused is ScrollView scroll&&scroll.name=="controls-scroll"&&direction.y!=0)
            {float before=scroll.scrollOffset.y;float after=Mathf.Clamp(before-direction.y*80,0,Mathf.Max(0,scroll.contentContainer.resolvedStyle.height-scroll.contentViewport.resolvedStyle.height));if(Mathf.Abs(after-before)>.5f){scroll.scrollOffset=new Vector2(0,after);return;}}
            if(direction.x!=0&&Adjust(focused,(int)Mathf.Sign(direction.x)))return;
            int index=controls.IndexOf(focused),step=direction.y>0||direction.x<0?-1:1;
            Focus(controls[(index<0?0:(index+step+controls.Count)%controls.Count)]);
        }
        public void Activate()
        {
            var element=Current(Controls());if(!Visible(element))return;
            if(element is LobbyColorField color){ActivateElement(color.Trigger);return;}
            if(element is TextField text){OpenKeyboard(text);return;}
            if(element is Toggle toggle){toggle.value=!toggle.value;return;}
            if(element is DropdownField dropdown){Adjust(dropdown,1);return;}
            ActivateElement(element);
        }
        public void ActivateElement(VisualElement element)
        {
            dispatching=true;
            try{using(var evt=NavigationSubmitEvent.GetPooled()){evt.target=element;ownedSubmits.Add(evt);element.SendEvent(evt);}}
            finally{dispatching=false;}
        }
        public void Back(){armed=false;back?.Invoke();}
        private bool Adjust(VisualElement element,int step)
        {
            if(element?.userData is Action<int> custom){custom(step);return true;}
            if(element is Slider slider){slider.value=Mathf.Clamp(slider.value+step*(slider.highValue-slider.lowValue)/20f,slider.lowValue,slider.highValue);return true;}
            if(element is SliderInt integer){integer.value=Mathf.Clamp(integer.value+step,integer.lowValue,integer.highValue);return true;}
            if(element is DropdownField dropdown&&dropdown.choices.Count>0){dropdown.index=(dropdown.index+step+dropdown.choices.Count)%dropdown.choices.Count;return true;}
            if(element is Toggle toggle){toggle.value=step>0;return true;}
            return false;
        }
        private VisualElement Current(List<VisualElement> controls)
        {
            var focused=root.focusController?.focusedElement as VisualElement;
            return controls.FirstOrDefault(x=>x==focused||x.Contains(focused))??controls.FirstOrDefault();
        }
        private List<VisualElement> Controls()=>scope==null?new List<VisualElement>():scope.Query<VisualElement>().ToList().Where(x=>(x is LobbyColorField||x is Button&&x.GetFirstAncestorOfType<LobbyColorField>()==null||x is Toggle||x is Slider||x is SliderInt||x is DropdownField||x is TextField||x is ScrollView&&x.name=="controls-scroll")&&Visible(x)).ToList();
        public static bool Visible(VisualElement element)
        {
            if(element==null||!element.enabledInHierarchy||!element.focusable)return false;
            for(var e=element;e!=null;e=e.parent)if(e.style.display.value==DisplayStyle.None||e.resolvedStyle.display==DisplayStyle.None||e.resolvedStyle.visibility==Visibility.Hidden)return false;
            return true;
        }
        private void Focus(VisualElement element)
        {
            if(element==null)return;element.Focus();lastFocus=element.name;
            for(var parent=element.parent;parent!=null;parent=parent.parent)if(parent is ScrollView scroll){scroll.ScrollTo(element);break;}UpdateHints();
        }
        private void KeyUp(KeyUpEvent evt)=>pressedKeys.Remove(evt.keyCode);
        private void Wheel(WheelEvent evt){if(keyboardAllowed?.Invoke()!=false){UsingGamepad=false;UpdateHints();}}
        private void KeyDown(KeyDownEvent evt)
        {
            if(scope==null||dispatching||DropdownOpen)return;if(keyboardAllowed?.Invoke()==false){evt.StopImmediatePropagation();root.focusController?.IgnoreEvent(evt);return;}UsingGamepad=false;UpdateHints();
            // Ordinary typing remains native; only escape/tab leave a text editor.
            bool typing=evt.target is VisualElement target&&(target is TextField||target.GetFirstAncestorOfType<TextField>()!=null);
            bool oneShot=evt.keyCode==KeyCode.Escape||!typing&&(evt.keyCode==KeyCode.Return||evt.keyCode==KeyCode.KeypadEnter||evt.keyCode==KeyCode.Space);
            if(oneShot&&!pressedKeys.Add(evt.keyCode)){evt.StopImmediatePropagation();root.focusController?.IgnoreEvent(evt);return;}
            if(evt.keyCode==KeyCode.Escape)Back();
            else if(evt.keyCode==KeyCode.Tab)Move(evt.shiftKey?Vector2.up:Vector2.down);
            else if(!typing&&evt.keyCode==KeyCode.UpArrow)Move(Vector2.up);
            else if(!typing&&evt.keyCode==KeyCode.DownArrow)Move(Vector2.down);
            else if(!typing&&evt.keyCode==KeyCode.LeftArrow)Move(Vector2.left);
            else if(!typing&&evt.keyCode==KeyCode.RightArrow)Move(Vector2.right);
            else if(!typing&&(evt.keyCode==KeyCode.Return||evt.keyCode==KeyCode.KeypadEnter||evt.keyCode==KeyCode.Space))Activate();
            else return;
            evt.StopImmediatePropagation();root.focusController?.IgnoreEvent(evt);
        }
        private void BlockMove(NavigationMoveEvent evt){if((Active||worldOwnsInput?.Invoke()==true||WorldOwnsNavigation?.Invoke()==true)&&!dispatching&&!DropdownOpen){evt.StopImmediatePropagation();root.focusController?.IgnoreEvent(evt);}}
        private void BlockSubmit(NavigationSubmitEvent evt){if(ownedSubmits.Remove(evt))return;if(!DropdownOpen&&(Active||worldOwnsInput?.Invoke()==true))evt.StopImmediatePropagation();}
        private void BlockCancel(NavigationCancelEvent evt){if((Active||worldOwnsInput?.Invoke()==true)&&!dispatching&&!DropdownOpen)evt.StopImmediatePropagation();}
        private void PointerDown(PointerDownEvent evt){if(keyboardAllowed?.Invoke()==false)return;UsingGamepad=false;UpdateHints();}
        private void UpdateHints()
        {
            var pad=gamepad();
            if(scope!=null)
            {
                foreach(var label in scope.Query<Label>(name:"menu-pad-glyph").ToList())label.RemoveFromHierarchy();
                if(UsingGamepad)foreach(var button in scope.Query<Button>().ToList())
                {
                    if(!Visible(button))continue;bool isBack=back!=null&&(button.name?.EndsWith("-back")==true||button.name?.EndsWith("-cancel")==true);
                    bool isReset=scope.name=="settings-screen"&&button.name=="settings-reset-all";
                    var focused=root.focusController?.focusedElement;var color=button.GetFirstAncestorOfType<LobbyColorField>();
                    if(!isReset&&!isBack&&focused!=button&&(color==null||focused!=color))continue;
                    var glyph=new Label(isReset?NativeControllerGlyph.TopSymbol(pad):NativeControllerGlyph.Symbol(isBack?"B":"A",pad)){name="menu-pad-glyph",pickingMode=PickingMode.Ignore};glyph.style.position=Position.Absolute;glyph.style.right=5;glyph.style.top=5;glyph.style.fontSize=11;button.Add(glyph);
                }
            }
            if(hints==null)return;
            hints.text=UsingGamepad?(NativeControllerGlyph.Symbol("A",pad)+"  Выбрать    "+NativeControllerGlyph.Symbol("B",pad)+"  Назад    ↔  Изменить"):"";
        }
        private void OpenKeyboard(TextField field)
        {
            var oldScope=scope;var oldBack=back;var oldHints=hints;string original=field.value;string editing=original;bool numeric=field.userData is Action<int>;
            var keyboard=new VisualElement();keyboard.AddToClassList("orbital-keyboard");root.Add(keyboard);
            var preview=OrbitalTheme.Text(field.value,"orbital-title");keyboard.Add(preview);
            var keys=new VisualElement();keys.AddToClassList("orbital-keyboard-keys");keyboard.Add(keys);
            bool russian=true;
            Action close=()=>{keyboard.RemoveFromHierarchy();SetScope(oldScope,oldBack,field,oldHints);};
            void Rebuild(){keys.Clear();foreach(char c in (numeric?"":russian?"АБВГДЕЁЖЗИЙКЛМНОПРСТУФХЦЧШЩЪЫЬЭЮЯ":"ABCDEFGHIJKLMNOPQRSTUVWXYZ")+"0123456789-_.") {char key=c;keys.Add(OrbitalTheme.Action(key.ToString(),()=>{editing+=key;preview.text=editing;}));}}
            Rebuild();var actions=new VisualElement();actions.AddToClassList("orbital-toolbar");keyboard.Add(actions);
            if(!numeric)actions.Add(OrbitalTheme.Action("Пробел",()=>{editing+=" ";preview.text=editing;}));
            actions.Add(OrbitalTheme.Action("⌫",()=>{if(editing.Length>0)editing=editing.Substring(0,editing.Length-1);preview.text=editing;}));
            if(!numeric)actions.Add(OrbitalTheme.Action("RU / EN",()=>{russian=!russian;Rebuild();SetScope(keyboard,()=>{field.value=original;close();});}));
            actions.Add(OrbitalTheme.Action("Готово",()=>{field.value=editing;close();},primary:true));
            SetScope(keyboard,()=>{field.value=original;close();});
        }
        public void Dispose()
        {
            root.UnregisterCallback<KeyDownEvent>(KeyDown,TrickleDown.TrickleDown);
            root.UnregisterCallback<KeyUpEvent>(KeyUp,TrickleDown.TrickleDown);root.UnregisterCallback<WheelEvent>(Wheel,TrickleDown.TrickleDown);
            root.UnregisterCallback<NavigationMoveEvent>(BlockMove,TrickleDown.TrickleDown);
            root.UnregisterCallback<NavigationSubmitEvent>(BlockSubmit,TrickleDown.TrickleDown);
            root.UnregisterCallback<NavigationCancelEvent>(BlockCancel,TrickleDown.TrickleDown);
            root.UnregisterCallback<PointerDownEvent>(PointerDown,TrickleDown.TrickleDown);scope=null;
        }
    }
}
