using System;
using System.Collections;
using NUnit.Framework;
using Spacewars.Presentation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
namespace Spacewars.Input.Tests
{
    public sealed partial class OrdinaryLocalPadTests
    {
        [UnityTest] public IEnumerator DevicePreferencesDriveTheActualLocalMatch()
        {
            var restore=NativePreferenceWriter.Capture();int previousFps=Application.targetFrameRate;
            try
            {
                NativeDevicePreferences.Deadzone=.1f;NativeDevicePreferences.InvertX=false;
                yield return StartOrdinary(2);var seat=Seats[1];var camera=Get<Camera>(seat,"cameraView");var right=Vector3.ProjectOnPlane(camera.transform.right,Vector3.up).normalized;
                var before=camera.transform.position;Set(pads[1].leftStick,Vector2.right);InputSystem.Update();yield return null;Call(app,"UpdateFrame");
                Assert.Greater(Vector3.Dot(camera.transform.position-before,right),0,"Ordinary camera direction");
                NativeDevicePreferences.InvertX=true;before=camera.transform.position;yield return null;Call(app,"UpdateFrame");
                Assert.Less(Vector3.Dot(camera.transform.position-before,right),0,"Saved inversion reaches the actual gamepad camera path");
                NativeDevicePreferences.Deadzone=.5f;before=camera.transform.position;Set(pads[1].leftStick,new Vector2(.2f,0));InputSystem.Update();yield return null;Call(app,"UpdateFrame");
                Assert.That(Vector3.Distance(before,camera.transform.position),Is.LessThan(.0001f),"Configured deadzone suppresses analog camera drift");
                NativeDevicePreferences.Deadzone=0;NativeDevicePreferences.InvertX=false;before=camera.transform.position;Set(pads[1].leftStick,Vector2.right);InputSystem.Update();yield return null;Call(app,"UpdateFrame");
                Assert.Greater(Vector3.Dot(camera.transform.position-before,right),0,"Reducing deadzone and restoring direction takes effect without changing the match");
                NativeDevicePreferences.FrameLimit=30;Assert.AreEqual(30,Application.targetFrameRate);
                NativeDevicePreferences.UiScale=1.25f;NativeDevicePreferences.HighContrast=true;
                var root=Get<VisualElement>(app,"root");var panel=host.GetComponent<UIDocument>().panelSettings;OrbitalTheme.ConfigurePanel(panel,root);
                Assert.AreEqual(OrbitalTheme.ReadableScale(Screen.width,Screen.height)*1.25f,panel.scale,.001f);Assert.True(root.ClassListContains("high-contrast"));
            }
            finally {Set(pads[1].leftStick,Vector2.zero);restore();PlayerPrefs.Save();NativeDisplaySettings.ApplyVSync();Application.targetFrameRate=previousFps;}
        }
    }
}
