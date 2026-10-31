using System;
using System.Linq;
using UnityEngine;

namespace Spacewars.Presentation
{
    // Reusable visual articulation only. An ordinary-match authority can supply targets/timing later.
    public sealed class SentryTurretRig : MonoBehaviour
    {
        public Transform Yaw { get; private set; }
        public Transform Pitch { get; private set; }
        public Transform Left { get; private set; }
        public Transform Right { get; private set; }
        public float YawDegrees { get; private set; }
        public float ElevationDegrees { get; private set; }
        public bool Turning { get; private set; }
        public Vector3 Forward => ((Left.position + Right.position) * .5f - Pitch.position).normalized;
        Quaternion restYaw, restPitch;
        Vector3 restForward, pitchAxis;
        bool bound;
        public void Bind()
        {
            if (bound) return;
            var nodes = GetComponentsInChildren<Transform>(true);
            Yaw = nodes.Single(t => t.name == "turretYaw"); Pitch = nodes.Single(t => t.name == "weaponPitch");
            Left = nodes.Single(t => t.name == "muzzleLeft"); Right = nodes.Single(t => t.name == "muzzleRight");
            restYaw = Yaw.localRotation; restPitch = Pitch.localRotation;
            // Infer the authored axes from the sockets so glTF coordinate conversion cannot invert aiming.
            restForward = transform.InverseTransformDirection(Forward); restForward.y = 0; restForward.Normalize();
            pitchAxis = Pitch.parent.InverseTransformDirection(Vector3.Cross(Forward, transform.up).normalized);
            if (restForward.sqrMagnitude < .9f || pitchAxis.sqrMagnitude < .9f) throw new InvalidOperationException("Invalid turret rig axes");
            bound = true;
        }
        public void ResetAim()
        {
            Bind(); Yaw.localRotation = restYaw; Pitch.localRotation = restPitch;
            YawDegrees = 0; ElevationDegrees = 0; Turning = false;
        }
        public void AimAt(Vector3 target, float seconds, SentryTurretPreviewProfile profile)
        {
            Bind();
            if (!float.IsFinite(seconds) || seconds <= 0 || !float.IsFinite(target.x) || !float.IsFinite(target.y) || !float.IsFinite(target.z))
                throw new ArgumentOutOfRangeException(nameof(seconds));
            var delta = transform.InverseTransformDirection(target - Pitch.position);
            float flat = new Vector2(delta.x, delta.z).magnitude;
            // Directly overhead has no unique azimuth. Retain the previous yaw at this pole.
            float desiredYaw = flat > 1e-5f ? Vector3.SignedAngle(restForward, new Vector3(delta.x, 0, delta.z), Vector3.up) : YawDegrees;
            float desiredPitch = Mathf.Clamp(Mathf.Atan2(delta.y, flat) * Mathf.Rad2Deg, -profile.depressionDegrees, 90);
            float beforeYaw = YawDegrees, beforePitch = ElevationDegrees;
            YawDegrees = Mathf.MoveTowardsAngle(YawDegrees, desiredYaw, profile.yawDegreesPerSecond * seconds);
            ElevationDegrees = Mathf.MoveTowards(ElevationDegrees, desiredPitch, profile.pitchDegreesPerSecond * seconds);
            Turning = Mathf.Abs(Mathf.DeltaAngle(beforeYaw, YawDegrees)) > 1e-4f || Mathf.Abs(beforePitch - ElevationDegrees) > 1e-4f;
            // Both axes are expressed in the respective parent frame, ahead of authored rest rotation.
            var yawAxis = Yaw.parent.InverseTransformDirection(transform.up);
            Yaw.localRotation = Quaternion.AngleAxis(YawDegrees, yawAxis) * restYaw;
            Pitch.localRotation = Quaternion.AngleAxis(ElevationDegrees, pitchAxis) * restPitch;
        }
        public void SetWorldAngles(double heading,double elevation)
        {
            Bind();
            var direction=transform.InverseTransformDirection(new Vector3((float)System.Math.Cos(heading),0,(float)System.Math.Sin(heading)));
            YawDegrees=Vector3.SignedAngle(restForward,direction,Vector3.up);ElevationDegrees=(float)(elevation*Mathf.Rad2Deg);
            var yawAxis=Yaw.parent.InverseTransformDirection(transform.up);
            Yaw.localRotation=Quaternion.AngleAxis(YawDegrees,yawAxis)*restYaw;
            Pitch.localRotation=Quaternion.AngleAxis(ElevationDegrees,pitchAxis)*restPitch;
        }
        public float Spread(float settledDegrees, SentryTurretPreviewProfile profile) => settledDegrees * (Turning ? profile.rotatingSpreadMultiplier : 1);
    }
}
