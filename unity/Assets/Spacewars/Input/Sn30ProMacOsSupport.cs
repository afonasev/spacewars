using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Layouts;

namespace Spacewars.Input
{
    public static class Sn30ProMacOsSupport
    {
        public const string Layout = "SpacewarsSn30ProMacOS";

#if UNITY_EDITOR_OSX
        [UnityEditor.InitializeOnLoadMethod]
#endif
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void Register()
        {
#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
            // Captured SN30 Pro Bluetooth X-mode HID: 045e:02e0, report 1.
            // Axes/triggers/hat match Xbox One Bluetooth, but its ten buttons
            // are contiguous rather than using Xbox's sparse button bits.
            // Match the exact product too: the IDs are shared with real Xbox pads.
            InputSystem.RegisterLayout(@"{
                ""name"": ""SpacewarsSn30ProMacOS"",
                ""extend"": ""XboxOneGampadMacOSWireless"",
                ""displayName"": ""8Bitdo SN30 Pro"",
                ""controls"": [
                    { ""name"": ""buttonSouth"", ""offset"": 14, ""bit"": 0 },
                    { ""name"": ""buttonEast"", ""offset"": 14, ""bit"": 1 },
                    { ""name"": ""buttonWest"", ""offset"": 14, ""bit"": 2 },
                    { ""name"": ""buttonNorth"", ""offset"": 14, ""bit"": 3 },
                    { ""name"": ""leftShoulder"", ""offset"": 14, ""bit"": 4 },
                    { ""name"": ""rightShoulder"", ""offset"": 14, ""bit"": 5 },
                    { ""name"": ""select"", ""offset"": 14, ""bit"": 6 },
                    { ""name"": ""start"", ""offset"": 14, ""bit"": 7 },
                    { ""name"": ""leftStickPress"", ""offset"": 14, ""bit"": 8 },
                    { ""name"": ""rightStickPress"", ""offset"": 14, ""bit"": 9 }
                ]
            }", name: Layout, matches: new InputDeviceMatcher()
                .WithInterface("HID")
                .WithProduct("^8Bitdo SN30 Pro$")
                .WithCapability("vendorId", 0x045e)
                .WithCapability("productId", 0x02e0));
            foreach (var device in InputSystem.devices)
                if (device.native && device.layout == Layout)
                    Debug.Log("SN30 Pro detected: " + device.description.product + " / " + device.layout + " / Gamepad=" + (device is Gamepad));
#endif
        }
    }
}
