using System.Collections;
using NUnit.Framework;
using Spacewars.Input;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

namespace Spacewars.Input.Tests
{
    public sealed class FoundationInputPlayModeTests : InputTestFixture
    {
        private GameObject gameObject;
        private FoundationInput input;
        private Keyboard keyboard;
        private Mouse mouse;

        [SetUp]
        public override void Setup()
        {
            base.Setup();
            gameObject = new GameObject("FoundationInputPlayModeTests");
            input = gameObject.AddComponent<FoundationInput>();
            keyboard = InputSystem.AddDevice<Keyboard>();
            mouse = InputSystem.AddDevice<Mouse>();
            keyboard.MakeCurrent();
            mouse.MakeCurrent();
            input.SetFocus(true);
        }

        [TearDown]
        public override void TearDown()
        {
            Object.DestroyImmediate(gameObject);
            base.TearDown();
        }

        [Test]
        public void PointerOverUiSuppressesWorldMouseCommandsAndZoom()
        {
            var selectCount = 0;
            var moveCount = 0;
            var zoomCount = 0;
            input.SelectAt = _ => selectCount++;
            input.MoveAt = _ => moveCount++;
            input.Zoom = _ => zoomCount++;
            input.IsPointerOverUi = _ => true;

            QueueMouse(new Vector2(120f, 80f), 3, 2f);

            Assert.That(selectCount, Is.Zero);
            Assert.That(moveCount, Is.Zero);
            Assert.That(zoomCount, Is.Zero);
        }

        [Test]
        public void DisabledWorldInputStillAllowsPauseAndRestart()
        {
            var stopCount = 0;
            var pauseCount = 0;
            var restartCount = 0;
            input.WorldInputEnabled = false;
            input.Stop = () => stopCount++;
            input.TogglePause = () => pauseCount++;
            input.Restart = () => restartCount++;

            QueueKeyboard(Key.S, Key.Escape, Key.R);

            Assert.That(stopCount, Is.Zero);
            Assert.That(pauseCount, Is.EqualTo(1));
            Assert.That(restartCount, Is.EqualTo(1));
        }

        [Test]
        public void FocusLossPausesOnceAndRejectsAllCommands()
        {
            var focusLostCount = 0;
            var pauseCount = 0;
            var selectCount = 0;
            input.FocusLost = () => focusLostCount++;
            input.TogglePause = () => pauseCount++;
            input.SelectAt = _ => selectCount++;

            input.SetFocus(false);
            input.SetFocus(false);
            QueueMouse(new Vector2(10f, 10f), 1, 0f);
            QueueKeyboard(Key.Escape);

            Assert.That(focusLostCount, Is.EqualTo(1));
            Assert.That(pauseCount, Is.Zero);
            Assert.That(selectCount, Is.Zero);
            Assert.That(input.HasFocus, Is.False);
        }

        [Test]
        public void CommandsPanAndZoomUseRawPointerAndRawScroll()
        {
            Vector2? selectedAt = null;
            Vector2? movedAt = null;
            Vector2? pan = null;
            float? zoom = null;
            var stopCount = 0;
            input.SelectAt = value => selectedAt = value;
            input.MoveAt = value => movedAt = value;
            input.Stop = () => stopCount++;
            input.Pan = value => pan = value;
            input.Zoom = value => zoom = value;

            QueueMouse(new Vector2(320f, 240f), 3, -4f);
            QueueKeyboard(Key.S, Key.UpArrow, Key.RightArrow);

            Assert.That(selectedAt, Is.EqualTo(new Vector2(320f, 240f)));
            Assert.That(movedAt, Is.EqualTo(new Vector2(320f, 240f)));
            Assert.That(stopCount, Is.EqualTo(1));
            Assert.That(pan, Is.EqualTo(new Vector2(1f, 1f).normalized));
            Assert.That(zoom, Is.EqualTo(-4f));
        }

        [UnityTest]
        public IEnumerator QueuedMousePressIsReadDuringTheNextPlayerFrame()
        {
            var selectCount = 0;
            input.SelectAt = _ => selectCount++;

            InputSystem.QueueStateEvent(mouse, new MouseState
            {
                position = new Vector2(48f, 32f),
                buttons = 1
            });

            yield return null;

            Assert.That(selectCount, Is.EqualTo(1));
        }

        private void QueueMouse(Vector2 position, ushort buttons, float scrollY)
        {
            InputSystem.QueueStateEvent(mouse, new MouseState
            {
                position = position,
                buttons = buttons,
                scroll = new Vector2(0f, scrollY)
            });
            InputSystem.Update();
            input.Poll();
            InputSystem.QueueStateEvent(mouse, new MouseState { position = position });
            InputSystem.Update();
        }

        private void QueueKeyboard(params Key[] keys)
        {
            var state = new KeyboardState(keys);
            InputSystem.QueueStateEvent(keyboard, state);
            InputSystem.Update();
            input.Poll();
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            InputSystem.Update();
        }
    }
}
