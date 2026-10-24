using System;
namespace Spacewars.Simulation
{
    public struct HawkMotionSettings
    {
        public double ForwardSpeed, ReverseSpeed, StrafeSpeed, Acceleration, Braking, ReverseSeconds;
        public double YawSpeed, YawAcceleration, CruisePitch, AccelerationPitch, StrafeBank, TurnBank, AttitudeSpeed;
        public void Validate()
        {
            foreach (double n in new[] { ForwardSpeed, ReverseSpeed, StrafeSpeed, Acceleration, Braking, ReverseSeconds,
                YawSpeed, YawAcceleration, CruisePitch, AccelerationPitch, StrafeBank, TurnBank, AttitudeSpeed })
                if (double.IsNaN(n) || double.IsInfinity(n) || n <= 0) throw new ArgumentException("Motion settings must be finite and positive");
            if (ReverseSpeed >= ForwardSpeed) throw new ArgumentException("Reverse must be slower than forward");
        }
    }
    // Preview motion only. No ordinary-match pathfinding, persistence or physics authority is changed.
    public sealed class HawkMotion
    {
        readonly HawkMotionSettings settings;
        public double X { get; private set; }
        public double Z { get; private set; }
        public double VelocityX { get; private set; }
        public double VelocityZ { get; private set; }
        public double Yaw { get; private set; }
        public double YawVelocity { get; private set; }
        public double Pitch { get; private set; }
        public double Roll { get; private set; }
        public double ReverseHeld { get; private set; }
        public bool ReverseExhausted => ReverseHeld >= settings.ReverseSeconds;
        public double Speed => Math.Sqrt(VelocityX * VelocityX + VelocityZ * VelocityZ);
        public HawkMotion(HawkMotionSettings settings) { settings.Validate(); this.settings = settings; }
        static double Clamp(double n, double min, double max) => Math.Max(min, Math.Min(max, n));
        static double Approach(double a, double b, double step) => a + Clamp(b - a, -step, step);
        public void Advance(double seconds, double forward, double strafe, double turn)
        {
            foreach (double v in new[] { seconds, forward, strafe, turn })
                if (double.IsNaN(v) || double.IsInfinity(v)) throw new ArgumentException("Nonfinite motion input");
            if (seconds <= 0) throw new ArgumentOutOfRangeException(nameof(seconds));
            forward = Clamp(forward, -1, 1); strafe = Clamp(strafe, -1, 1); turn = Clamp(turn, -1, 1);
            // Fixed maximum integration step is a numerical bound, not designer tuning.
            for (double remaining = seconds; remaining > 1e-12;)
            {
                double dt = Math.Min(remaining, 1d / 120); remaining -= dt;
                Step(dt, forward, strafe, turn);
            }
        }
        void Step(double dt, double forward, double strafe, double turn)
        {
            if (forward < 0)
            {
                double allowed = Math.Min(dt, Math.Max(0, settings.ReverseSeconds - ReverseHeld));
                ReverseHeld = Math.Min(settings.ReverseSeconds, ReverseHeld + dt);
                forward *= allowed / dt;
            }
            else ReverseHeld = 0;
            YawVelocity = Approach(YawVelocity, turn * settings.YawSpeed, settings.YawAcceleration * dt);
            Yaw += YawVelocity * dt;
            double angle = Yaw * Math.PI / 180, sin = Math.Sin(angle), cos = Math.Cos(angle);
            double localForward = forward * (forward < 0 ? settings.ReverseSpeed : settings.ForwardSpeed);
            double localRight = strafe * settings.StrafeSpeed;
            double inputLength = Math.Sqrt(forward * forward + strafe * strafe);
            if (inputLength > 1) { localForward /= inputLength; localRight /= inputLength; }
            double desiredX = localRight * cos + localForward * sin, desiredZ = -localRight * sin + localForward * cos;
            double dx = desiredX - VelocityX, dz = desiredZ - VelocityZ, difference = Math.Sqrt(dx * dx + dz * dz);
            bool braking = desiredX * VelocityX + desiredZ * VelocityZ < Speed * Speed;
            double limit = (braking ? settings.Braking : settings.Acceleration) * dt;
            double ratio = difference > limit ? limit / difference : 1;
            double ax = dx * ratio / dt, az = dz * ratio / dt;
            VelocityX += dx * ratio; VelocityZ += dz * ratio;
            X += VelocityX * dt; Z += VelocityZ * dt;
            double forwardVelocity = VelocityX * sin + VelocityZ * cos;
            double forwardAcceleration = ax * sin + az * cos, rightAcceleration = ax * cos - az * sin;
            double pitch = forwardVelocity / settings.ForwardSpeed * settings.CruisePitch
                + Clamp(forwardAcceleration / settings.Acceleration, -1, 1) * settings.AccelerationPitch;
            double roll = -Clamp(rightAcceleration / settings.Acceleration, -1, 1) * settings.StrafeBank
                - YawVelocity / settings.YawSpeed * Math.Min(1, Speed / settings.ForwardSpeed) * settings.TurnBank;
            Pitch = Approach(Pitch, pitch, settings.AttitudeSpeed * dt);
            Roll = Approach(Roll, roll, settings.AttitudeSpeed * dt);
        }
    }
}
