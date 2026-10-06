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
        public Action<Vector2, bool> Order;
        public Action Hold, Stop, TogglePause, Restart, FocusLost, Capture, ToggleMap, CloseMap;
        // 0 world, 1 compact, 2 tactical, 3 tactical backdrop (consumes input).
        public Func<Vector2,int> MapAt;
        public Action<int,Vector2,Vector2,bool> MapSelect;
        public Action<int,Vector2,bool> MapOrder;
        private int mapDrag;
        public Action<Vector2> Pan;
        public Action<float> Zoom;
        public Action<Vector2, Vector2, bool> Drag;
        public Action<bool> AttackModeChanged;
        public Func<Vector2, bool> IsPointerOverUi;
        public Func<bool> IsKeyboardInUi;
        public bool SourceSeatSemantics;
        public Mouse AssignedMouse;
        public Keyboard AssignedKeyboard;
        private Mouse SeatMouse {get{var device=AssignedMouse??Mouse.current;return device?.added==true?device:null;}}
        private Keyboard SeatKeyboard {get{var device=AssignedKeyboard??Keyboard.current;return device?.added==true?device:null;}}
        public Func<bool> CanReadWorld;
        private bool WorldAllowed=>WorldInputEnabled&&(CanReadWorld?.Invoke()!=false);
        public bool WorldInputEnabled = true;
        public bool HasFocus { get; private set; } = true;
        private bool dragging, attackMode;
        private Vector2 origin;
        private bool previousMergeSetting;
        private readonly Queue<PointerEdge> pointerEdges = new Queue<PointerEdge>();
        private struct PointerEdge
        {
            public Vector2 Position;
            public bool Left, Down, Shift;
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
            if (!(device is Mouse mouse) || mouse != SeatMouse ||
                (!evt.IsA<StateEvent>() && !evt.IsA<DeltaStateEvent>())) return;
            if (!HasFocus || !WorldAllowed) return;
            Vector2 position = mouse.position.ReadValueFromEvent(evt, out var point) ? point : mouse.position.ReadValue();
            var keyboard = SeatKeyboard;
            bool shift = keyboard != null && (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed || SourceSeatSemantics&&(keyboard.leftCtrlKey.isPressed||keyboard.rightCtrlKey.isPressed));
            // onEvent runs before device state is applied, so the current button is the prior state.
            // Simultaneous edges in one device event use a stable LMB-then-RMB tie break.
            if (mouse.leftButton.ReadValueFromEvent(evt, out var left) && (left > 0) != mouse.leftButton.isPressed)
                pointerEdges.Enqueue(new PointerEdge { Position = position, Left = true, Down = left > 0, Shift = shift });
            if (mouse.rightButton.ReadValueFromEvent(evt, out var right) && (right > 0) != mouse.rightButton.isPressed)
                pointerEdges.Enqueue(new PointerEdge { Position = position, Left = false, Down = right > 0, Shift = shift });
        }
        public void ClearMode() { pointerEdges.Clear(); ResetMode(); }
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
            bool uiFocus = IsKeyboardInUi?.Invoke() == true;
            if (keyboard != null && !uiFocus)
            {
                if (keyboard.escapeKey.wasPressedThisFrame) { if (attackMode) ClearMode(); else TogglePause?.Invoke(); }
                if (keyboard.rKey.wasPressedThisFrame) Restart?.Invoke();
                if (keyboard.f12Key.wasPressedThisFrame) Capture?.Invoke();
            }
            if (!WorldAllowed) { pointerEdges.Clear(); ClearMode(); CloseMap?.Invoke(); return; }
            if (keyboard != null && !uiFocus)
            {
                if (keyboard.tabKey.wasPressedThisFrame) { ClearMode(); ToggleMap?.Invoke(); }
                if (keyboard.hKey.wasPressedThisFrame) { ClearMode(); Hold?.Invoke(); }
                if (keyboard.sKey.wasPressedThisFrame) { ClearMode(); Stop?.Invoke(); }
                if (keyboard.aKey.wasPressedThisFrame) { attackMode = SourceSeatSemantics || !attackMode; AttackModeChanged?.Invoke(attackMode); }
                var axis = new Vector2((keyboard.rightArrowKey.isPressed ? 1 : 0) - (keyboard.leftArrowKey.isPressed ? 1 : 0),
                    (keyboard.upArrowKey.isPressed ? 1 : 0) - (keyboard.downArrowKey.isPressed ? 1 : 0));
                if (axis.sqrMagnitude > 0) Pan?.Invoke(axis.normalized);
            }
            var mouse = SeatMouse;
            if (mouse == null) return;
            Vector2 position = mouse.position.ReadValue();
            while (pointerEdges.Count > 0)
            {
                var edge = pointerEdges.Dequeue();
                if (!HasFocus || !WorldAllowed) { pointerEdges.Clear(); ClearMode(); break; }
                DispatchPointerEdge(edge);
            }
            if (dragging) Drag?.Invoke(origin, position, true);
            if (mapDrag > 0) Drag?.Invoke(origin, position, mapDrag == 2);
            if ((MapAt?.Invoke(position) ?? 0) > 0 || IsPointerOverUi?.Invoke(position) == true) return;
            float scroll = mouse.scroll.ReadValue().y;
            if (scroll != 0) Zoom?.Invoke(scroll);
        }
        private void DispatchPointerEdge(PointerEdge edge)
        {
            Vector2 position = edge.Position;
            int map = MapAt?.Invoke(position) ?? 0;
            bool overUi = IsPointerOverUi?.Invoke(position) == true;
            if (!edge.Left)
            {
                if (!edge.Down) return;
                if (map > 0)
                {
                    bool cancel = attackMode;
                    ResetMode();
                    if (!cancel && map < 3) MapOrder?.Invoke(map, position, false);
                }
                else if (!overUi) { bool cancel=attackMode; ResetMode(); if(!SourceSeatSemantics||!cancel)Order?.Invoke(position, false); }
                return;
            }
            if (edge.Down)
            {
                if (map > 0)
                {
                    if (map < 3)
                    {
                        if (attackMode) { MapOrder?.Invoke(map, position, true); ResetMode(); }
                        else { origin = position; mapDrag = map; }
                    }
                }
                else if (!overUi)
                {
                    if (attackMode) { Order?.Invoke(position, true); ResetMode(); }
                    else { origin = position; dragging = true; }
                }
                return;
            }
            if (mapDrag > 0)
            {
                int source = mapDrag;
                mapDrag = 0;
                Drag?.Invoke(origin, position, false);
                if (map == source) MapSelect?.Invoke(source, origin, position, edge.Shift);
            }
            if (dragging)
            {
                dragging = false;
                Drag?.Invoke(origin, position, false);
                if (map == 0 && !overUi) Select?.Invoke(origin, position, edge.Shift);
            }
        }
    }
}
