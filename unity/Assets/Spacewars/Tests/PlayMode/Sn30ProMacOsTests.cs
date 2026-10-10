#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
using System.Linq;
using System.Runtime.InteropServices;
using NUnit.Framework;
using Spacewars.Input;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.Layouts;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Utilities;

namespace Spacewars.Input.Tests
{
    public sealed class Sn30ProMacOsTests : InputTestFixture
    {
        [StructLayout(LayoutKind.Explicit, Size = 16)]
        private struct Report : IInputStateTypeInfo
        {
            public FourCC format => new FourCC('H', 'I', 'D');
            [FieldOffset(0)] public byte id;
            [FieldOffset(1)] public ushort lx;
            [FieldOffset(3)] public ushort ly;
            [FieldOffset(5)] public ushort rx;
            [FieldOffset(7)] public ushort ry;
            [FieldOffset(9)] public ushort lt;
            [FieldOffset(11)] public ushort rt;
            [FieldOffset(13)] public byte hat;
            [FieldOffset(14)] public ushort buttons;
            public static Report Neutral => new Report { id = 1, lx = 32767, ly = 32767, rx = 32767, ry = 32767 };
        }

        [SetUp] public override void Setup() { base.Setup(); Sn30ProMacOsSupport.Register(); }

        private static InputDeviceDescription Description(string product = "8Bitdo SN30 Pro", int productId = 0x02e0) =>
            new InputDeviceDescription { interfaceName = "HID", product = product,
                capabilities = "{\"vendorId\":1118,\"productId\":" + productId + "}" };
        private static void Send(Gamepad pad, Report report) { InputSystem.QueueStateEvent(pad, report); InputSystem.Update(); }

        [Test] public void DeviceIsDiscoveredAsGamepadAndReconnects()
        {
            Sn30ProMacOsSupport.Register(); // Domain/play initialization is idempotent.
            var pad = InputSystem.AddDevice(Description()) as Gamepad;
            Assert.NotNull(pad); Assert.AreEqual(Sn30ProMacOsSupport.Layout, pad.layout);
            Assert.True(Gamepad.all.Any(device => device == pad));
            Send(pad, Report.Neutral); Assert.Less(pad.leftStick.ReadValue().magnitude, .01f);
            InputSystem.RemoveDevice(pad); Assert.False(Gamepad.all.Any(device => device == pad));
            var reconnected = InputSystem.AddDevice(Description()) as Gamepad;
            Assert.NotNull(reconnected); Assert.True(Gamepad.all.Any(device => device == reconnected));
            var report = Report.Neutral; report.buttons = 128; Send(reconnected, report);
            Assert.True(reconnected.startButton.wasPressedThisFrame);
        }

        [Test] public void TenRawButtonsHaveIndependentBindingsAndReleaseEdges()
        {
            var pad = (Gamepad)InputSystem.AddDevice(Description());
            var controls = new ButtonControl[] { pad.buttonSouth, pad.buttonEast, pad.buttonWest, pad.buttonNorth,
                pad.leftShoulder, pad.rightShoulder, pad.selectButton, pad.startButton, pad.leftStickButton, pad.rightStickButton };
            for (int bit = 0; bit < controls.Length; bit++)
            {
                var report = Report.Neutral; report.buttons = (ushort)(1 << bit); Send(pad, report);
                for (int index = 0; index < controls.Length; index++) Assert.AreEqual(index == bit, controls[index].isPressed, "button " + bit + " maps to " + index);
                Assert.True(controls[bit].wasPressedThisFrame);
                Send(pad, Report.Neutral); Assert.True(controls[bit].wasReleasedThisFrame);
            }
        }

        [Test] public void RawAxesTriggersAndHatUseObservedDescriptor()
        {
            var pad = (Gamepad)InputSystem.AddDevice(Description());
            var report = Report.Neutral; report.lx = 65535; report.ly = 0; report.rx = 0; report.ry = 65535;
            report.lt = 1023; report.rt = 1023; report.hat = 2; Send(pad, report);
            Assert.Greater(pad.leftStick.ReadValue().x, .6f); Assert.Greater(pad.leftStick.ReadValue().y, .6f);
            Assert.Less(pad.rightStick.ReadValue().x, -.6f); Assert.Less(pad.rightStick.ReadValue().y, -.6f);
            Assert.AreEqual(1, pad.leftTrigger.ReadValue(), .01f); Assert.AreEqual(1, pad.rightTrigger.ReadValue(), .01f);
            Assert.True(pad.dpad.up.isPressed); Assert.True(pad.dpad.right.isPressed); Assert.False(pad.dpad.down.isPressed);
            Send(pad, Report.Neutral); Assert.AreEqual(0, pad.dpad.ReadValue().sqrMagnitude); Assert.AreEqual(0, pad.leftTrigger.ReadValue());
        }

        [Test] public void MatcherLeavesXboxAndOtherSn30ModesUntouched()
        {
            Assert.AreEqual("XboxOneGampadMacOSWireless", InputSystem.TryFindMatchingLayout(Description("Xbox Wireless Controller")));
            Assert.AreNotEqual(Sn30ProMacOsSupport.Layout, InputSystem.TryFindMatchingLayout(Description(productId: 0x028e)));
            Assert.AreNotEqual(Sn30ProMacOsSupport.Layout, InputSystem.TryFindMatchingLayout(Description("8Bitdo Pro 2")));
        }
    }
}
#endif
