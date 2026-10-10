using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Spacewars.Input
{
    // Physical devices map to game actions here; simulation never polls devices.
    public sealed class PlayableInput : MonoBehaviour
    {
        public Action<Vector2, Vector2, bool> Select;
        public Action<Vector2> SelectSameType;
        // Index zero selects the whole army; other indices assign/recall groups.
        public Action<int, bool, double> SelectionKey;
        public Action ResetCamera;
        public float DoubleClickMilliseconds = 450, SelectionDragPixels = 5;
        public bool AttackMode => attackMode;
        public Action<Vector2, bool, bool> Order;
        public Action Hold, Stop, TogglePause, Restart, FocusLost, Capture, ToggleMap, CloseMap;
        // 0 world, 1 compact, 2 tactical, 3 tactical backdrop (consumes input).
        public Func<Vector2,int> MapAt;
        public Action<int,Vector2,Vector2,bool> MapSelect;
        public Action<int,Vector2,bool,bool> MapOrder;
        private int mapDrag;
        public Action<Vector2> Pan, EdgePan;
        public Func<Vector2,Vector2> EdgePanAxis;
        public Action<float> Zoom;
        public Action<Vector2, Vector2, bool> Drag;
        public Action<bool> AttackModeChanged;
        public Func<Vector2, bool> IsPointerOverUi;
        public Func<bool> IsKeyboardInUi;
        public Func<bool> CancelContext;
        public Func<bool> CanIssueAttack;
        public bool SourceSeatSemantics;
        public Mouse AssignedMouse;
        public Keyboard AssignedKeyboard;
        private Mouse SeatMouse {get{var device=AssignedMouse??Mouse.current;return device?.added==true?device:null;}}
        private Keyboard SeatKeyboard {get{var device=AssignedKeyboard??Keyboard.current;return device?.added==true?device:null;}}
        public Func<bool> CanReadWorld;
        private bool WorldAllowed=>WorldInputEnabled&&(CanReadWorld?.Invoke()!=false);
        private bool CommandAllowed=>WorldAllowed&&CommandInputEnabled;
        public bool WorldInputEnabled = true;
        // Observer viewports retain camera/map navigation while command gestures stay inert.
        public bool CommandInputEnabled = true;
        public bool HasFocus { get; private set; } = true;
        private bool dragging, attackMode;
        private Vector2 origin;
        private Vector2 lastClickPosition;
        private double lastClickTime = double.NegativeInfinity;
        private bool previousMergeSetting;
        private readonly Queue<PointerEdge> pointerEdges = new Queue<PointerEdge>();
        private struct PointerEdge
        {
            public Vector2 Position;
            public bool Left, Down, Shift, Append, AttackAction;
            public int Group;
            public bool SelectionAction, Assign, Home;
            public double Time;
            public Key Command;
        }
        private void OnEnable()
        {
            // Each button edge owns its event position; merging loses that ordering.
            previousMergeSetting = InputSystem.settings.disableRedundantEventsMerging;
            InputSystem.settings.disableRedundantEventsMerging = true;
            InputSystem.onEvent += ReadPointerEvent;
        }
        private void OnDisable()
        {
            InputSystem.onEvent -= ReadPointerEvent;
            InputSystem.settings.disableRedundantEventsMerging = previousMergeSetting;
            pointerEdges.Clear();
            ClearMode();
        }
        private void ReadPointerEvent(InputEventPtr evt, InputDevice device)
        {
            if (device is Keyboard keyboardEvent && keyboardEvent == SeatKeyboard &&
                (evt.IsA<StateEvent>() || evt.IsA<DeltaStateEvent>()))
            {
                if (HasFocus)
                {
                    bool Pressed(Key key) => keyboardEvent[key].ReadValueFromEvent(evt, out var value) && value > 0 && !keyboardEvent[key].isPressed;
                    bool Held(Key key) => keyboardEvent[key].ReadValueFromEvent(evt, out var value) ? value > 0 : keyboardEvent[key].isPressed;
                    foreach(var command in new[]{Key.Escape,Key.F12,Key.Tab,Key.H,Key.S})
                        if(Pressed(command))pointerEdges.Enqueue(new PointerEdge {Command=command});
                    if(WorldAllowed)
                    {
                        if(CommandAllowed&&Pressed(Key.A))pointerEdges.Enqueue(new PointerEdge {AttackAction=true});
                        bool modifier = Held(Key.LeftCtrl) || Held(Key.RightCtrl) || Held(Key.LeftShift) || Held(Key.RightShift);
                        if (!Held(Key.LeftAlt) && !Held(Key.RightAlt) && !Held(Key.LeftMeta) && !Held(Key.RightMeta))
                        {
                            for (int index = 1; index <= 9; index++)
                                if (CommandAllowed&&(Pressed((Key)((int)Key.Digit1 + index - 1)) || Pressed((Key)((int)Key.Numpad1 + index - 1))))
                                    pointerEdges.Enqueue(new PointerEdge { SelectionAction = true, Group = index, Assign = modifier, Time = evt.time });
                            if (CommandAllowed&&!modifier && (Pressed(Key.F2) || Pressed(Key.Digit0) || Pressed(Key.Numpad0)))
                                pointerEdges.Enqueue(new PointerEdge { SelectionAction = true, Time = evt.time });
                        }
                        if (Pressed(Key.Home)) pointerEdges.Enqueue(new PointerEdge { Home = true });
                    }
                }
                return;
            }
            if (!(device is Mouse mouse) || mouse != SeatMouse ||
                (!evt.IsA<StateEvent>() && !evt.IsA<DeltaStateEvent>())) return;
            if (!HasFocus || !WorldAllowed) return;
            Vector2 position = mouse.position.ReadValueFromEvent(evt, out var point) ? point : mouse.position.ReadValue();
            var keyboard = SeatKeyboard;
            bool append = keyboard != null && (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed);
            bool shift = append || keyboard != null && (keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed);
            // onEvent runs before device state is applied, so the current button is the prior state.
            // Simultaneous edges in one device event use a stable LMB-then-RMB tie break.
            if (mouse.leftButton.ReadValueFromEvent(evt, out var left) && (left > 0) != mouse.leftButton.isPressed)
                pointerEdges.Enqueue(new PointerEdge { Position = position, Left = true, Down = left > 0, Shift = shift, Append = append, Time = evt.time });
            if (mouse.rightButton.ReadValueFromEvent(evt, out var right) && (right > 0) != mouse.rightButton.isPressed)
                pointerEdges.Enqueue(new PointerEdge { Position = position, Left = false, Down = right > 0, Shift = shift, Append = append });
        }
        public void ClearMode() { pointerEdges.Clear(); lastClickTime = double.NegativeInfinity; ResetMode(); }
        private void ResetMode() { attackMode = false; dragging = false; mapDrag = 0; AttackModeChanged?.Invoke(false); Drag?.Invoke(origin, origin, false); }
        private void Update() { Poll(); }
        private void OnApplicationFocus(bool focused) { SetFocus(focused); }
        public void SetFocus(bool focused)
        {
            if (HasFocus == focused) return;
            HasFocus = focused;
            if (!focused) { pointerEdges.Clear(); ClearMode(); CloseMap?.Invoke(); FocusLost?.Invoke(); }
        }
        public void Poll()
        {
            if (!HasFocus) { pointerEdges.Clear(); return; }
            var keyboard = SeatKeyboard;
            while (pointerEdges.Count > 0)
            {
                var edge=pointerEdges.Dequeue();
                if(!HasFocus){pointerEdges.Clear();ClearMode();return;}
                bool uiFocus=IsKeyboardInUi?.Invoke()==true;
                if(edge.Command!=Key.None)
                {
                    if(uiFocus)continue;
                    if(edge.Command==Key.Escape){if(attackMode)ResetMode();else if(CancelContext?.Invoke()!=true)TogglePause?.Invoke();}
                    else if(edge.Command==Key.F12)Capture?.Invoke();
                    else if(WorldAllowed)
                    {
                        lastClickTime=double.NegativeInfinity;ResetMode();
                        if(edge.Command==Key.Tab)ToggleMap?.Invoke();
                        else if(CommandAllowed&&edge.Command==Key.H)Hold?.Invoke();
                        else if(CommandAllowed&&edge.Command==Key.S)Stop?.Invoke();
                    }
                }
                else if(WorldAllowed)DispatchPointerEdge(edge);
            }
            if (!WorldAllowed) { ClearMode(); CloseMap?.Invoke(); return; }
            if (keyboard != null && IsKeyboardInUi?.Invoke()!=true)
            {
                var axis = new Vector2((keyboard.rightArrowKey.isPressed ? 1 : 0) - (keyboard.leftArrowKey.isPressed ? 1 : 0),
                    (keyboard.upArrowKey.isPressed ? 1 : 0) - (keyboard.downArrowKey.isPressed ? 1 : 0));
                if (axis.sqrMagnitude > 0) Pan?.Invoke(axis.normalized);
            }
            var mouse = SeatMouse;
            if (mouse == null) return;
            Vector2 position = mouse.position.ReadValue();
            if (dragging) Drag?.Invoke(origin, position, true);
            if (mapDrag > 0) Drag?.Invoke(origin, position, mapDrag == 2);
            if ((MapAt?.Invoke(position) ?? 0) > 0 || IsPointerOverUi?.Invoke(position) == true) return;
            if(IsKeyboardInUi?.Invoke()!=true)
            {
                var edgeAxis=EdgePanAxis?.Invoke(position)??Vector2.zero;
                if(edgeAxis.sqrMagnitude>0)EdgePan?.Invoke(edgeAxis);
            }
            float scroll = mouse.scroll.ReadValue().y;
            if (scroll != 0) Zoom?.Invoke(scroll);
        }
        private void DispatchPointerEdge(PointerEdge edge)
        {
            if (edge.AttackAction&&CommandAllowed)
            {
                if (IsKeyboardInUi?.Invoke() != true) { attackMode = true; AttackModeChanged?.Invoke(attackMode); }
                return;
            }
            if ((edge.SelectionAction&&CommandAllowed) || edge.Home)
            {
                if (IsKeyboardInUi?.Invoke() != true)
                {
                    if (edge.Home) ResetCamera?.Invoke();
                    else SelectionKey?.Invoke(edge.Group, edge.Assign, edge.Time);
                }
                return;
            }
            Vector2 position = edge.Position;
            int map = MapAt?.Invoke(position) ?? 0;
            bool overUi = IsPointerOverUi?.Invoke(position) == true;
            if(edge.Down&&(map>0||overUi))lastClickTime=double.NegativeInfinity;
            if (!edge.Left)
            {
                if(!CommandInputEnabled){ResetMode();return;}
                if (!edge.Down) return;
                lastClickTime=double.NegativeInfinity;
                if (map > 0)
                {
                    bool cancel = attackMode;
                    ResetMode();
                    if (!cancel && map < 3&&CommandAllowed) MapOrder?.Invoke(map, position, false, edge.Append);
                }
                else if (!overUi) { bool cancel=attackMode; ResetMode(); if(!cancel&&CommandAllowed)Order?.Invoke(position, false, edge.Append); }
                return;
            }
            if (edge.Down)
            {
                if (map > 0)
                {
                    if (map < 3)
                    {
                    if (attackMode&&CommandAllowed) { if(CanIssueAttack?.Invoke()!=false){MapOrder?.Invoke(map, position, true, edge.Append); ResetMode();} }
                        else { origin = position; mapDrag = map; }
                    }
                }
                else if (!overUi)
                {
                    if (attackMode&&CommandAllowed) { if(CanIssueAttack?.Invoke()!=false){Order?.Invoke(position, true, edge.Append); ResetMode();} }
                    else { origin = position; dragging = true; }
                }
                return;
            }
            if (mapDrag > 0)
            {
                int source = mapDrag;
                mapDrag = 0;
                Drag?.Invoke(origin, position, false);
                if (map == source) MapSelect?.Invoke(source, origin, position, CommandAllowed&&edge.Shift);
            }
            if (dragging)
            {
                dragging = false;
                Drag?.Invoke(origin, position, false);
                if (map == 0 && !overUi)
                {
                    bool click = (position - origin).magnitude <= SelectionDragPixels;
                    bool doubleClick = click && edge.Time - lastClickTime <= DoubleClickMilliseconds / 1000d && (position - lastClickPosition).magnitude <= SelectionDragPixels;
                    if (doubleClick && SelectSameType != null) { SelectSameType(position); lastClickTime = double.NegativeInfinity; }
                    else if(CommandAllowed) { Select?.Invoke(origin, position, edge.Shift); lastClickTime = click ? edge.Time : double.NegativeInfinity; lastClickPosition = position; }
                }
            }
        }
    }
}
