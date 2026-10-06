using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Spacewars.Input
{
    /// <summary>
    /// Translates native keyboard and mouse state into diagnostic fixture intents.
    /// It has no dependency on simulation or presentation types.
    /// </summary>
    public sealed class FoundationInput : MonoBehaviour
    {
        public Action<Vector2> SelectAt;
        public Action<Vector2> MoveAt;
        public Action Stop;
        public Action TogglePause;
        public Action Restart;
        public Action<Vector2> Pan;
        public Action<float> Zoom;
        public Func<Vector2, bool> IsPointerOverUi;
        public Action FocusLost;

        public bool WorldInputEnabled { get; set; } = true;
        public bool HasFocus { get; private set; } = true;

        private void Update()
        {
            Poll();
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            SetFocus(hasFocus);
        }

        /// <summary>
        /// Reads the current Input System state once. Public for deterministic input tests.
        /// </summary>
        public void Poll()
        {
            if (!HasFocus)
            {
                return;
            }

            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.escapeKey.wasPressedThisFrame)
                {
                    TogglePause?.Invoke();
                }

                if (keyboard.rKey.wasPressedThisFrame)
                {
                    Restart?.Invoke();
                }
            }

            if (!WorldInputEnabled)
            {
                return;
            }

            if (keyboard != null)
            {
                if (keyboard.sKey.wasPressedThisFrame)
                {
                    Stop?.Invoke();
                }

                var pan = new Vector2(
                    (keyboard.rightArrowKey.isPressed ? 1f : 0f) - (keyboard.leftArrowKey.isPressed ? 1f : 0f),
                    (keyboard.upArrowKey.isPressed ? 1f : 0f) - (keyboard.downArrowKey.isPressed ? 1f : 0f));
                if (pan.sqrMagnitude > 0f)
                {
                    Pan?.Invoke(pan.normalized);
                }
            }

            var mouse = Mouse.current;
            if (mouse == null)
            {
                return;
            }

            var pointerPosition = mouse.position.ReadValue();
            if (IsPointerOverUi?.Invoke(pointerPosition) == true)
            {
                return;
            }

            if (mouse.leftButton.wasPressedThisFrame)
            {
                SelectAt?.Invoke(pointerPosition);
            }

            if (mouse.rightButton.wasPressedThisFrame)
            {
                MoveAt?.Invoke(pointerPosition);
            }

            var scrollY = mouse.scroll.ReadValue().y;
            if (!Mathf.Approximately(scrollY, 0f))
            {
                Zoom?.Invoke(scrollY);
            }
        }

        /// <summary>
        /// Updates focus state for Unity lifecycle and input-test injection.
        /// </summary>
        public void SetFocus(bool hasFocus)
        {
            if (HasFocus == hasFocus)
            {
                return;
            }

            HasFocus = hasFocus;
            if (!hasFocus)
            {
                FocusLost?.Invoke();
            }
        }
    }
}
