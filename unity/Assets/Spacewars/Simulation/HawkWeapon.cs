using System;
using System.Collections.Generic;

namespace Spacewars.Simulation
{
    public readonly struct HawkLaunch
    {
        public readonly int Target, Side;
        public readonly double Offset;
        public HawkLaunch(int target, int side, double offset) { Target = target; Side = side; Offset = offset; }
    }

    // Pure weapon authority; the preview adapter supplies targets/movement and consumes launches.
    // This does not register an aircraft in the ordinary match or its persistence schema.
    public sealed class HawkWeapon
    {
        readonly double preparation, interval, reload;
        public double StoppedFor { get; private set; }
        public double ReloadRemaining { get; private set; }
        public double ShotDelayRemaining { get; private set; }
        public int LockedTarget { get; private set; }
        public bool SecondPending => LockedTarget != 0;
        public HawkWeapon(double preparationSeconds, double shotIntervalSeconds, double reloadSeconds)
        {
            if (!Positive(preparationSeconds) || !Positive(shotIntervalSeconds) || !Positive(reloadSeconds))
                throw new ArgumentException("Hawk timing values must be finite and positive");
            preparation = preparationSeconds; interval = shotIntervalSeconds; reload = reloadSeconds;
        }
        static bool Positive(double n) => n > 0 && !double.IsNaN(n) && !double.IsInfinity(n);
        void BeginReload() { LockedTarget = 0; ShotDelayRemaining = 0; ReloadRemaining = reload; }
        void Elapse(double dt)
        {
            StoppedFor = Math.Min(preparation, StoppedFor + dt);
            ReloadRemaining = Math.Max(0, ReloadRemaining - dt);
            ShotDelayRemaining = Math.Max(0, ShotDelayRemaining - dt);
        }
        public IReadOnlyList<HawkLaunch> Advance(double dt, bool stationary, int candidate, Func<int, bool> valid)
        {
            if (!Positive(dt)) throw new ArgumentOutOfRangeException(nameof(dt));
            if (valid == null) throw new ArgumentNullException(nameof(valid));
            var launches = new List<HawkLaunch>();
            if (!stationary)
            {
                if (SecondPending) BeginReload();
                ReloadRemaining = Math.Max(0, ReloadRemaining - dt); StoppedFor = 0;
                return launches;
            }
            double left = dt, offset = 0;
            while (left > 0)
            {
                if (SecondPending && !valid(LockedTarget)) BeginReload();
                if (!SecondPending && (candidate <= 0 || !valid(candidate))) { Elapse(left); break; }
                double wait = SecondPending ? ShotDelayRemaining : Math.Max(ReloadRemaining, preparation - StoppedFor);
                double step = Math.Min(left, wait);
                Elapse(step); left -= step; offset += step;
                if (wait - step > 1e-12) break; // Floating-point boundary tolerance, not a timing knob.
                if (SecondPending)
                {
                    launches.Add(new HawkLaunch(LockedTarget, 1, offset)); BeginReload();
                }
                else
                {
                    LockedTarget = candidate; ShotDelayRemaining = interval;
                    launches.Add(new HawkLaunch(LockedTarget, 0, offset));
                }
            }
            return launches;
        }
    }

    public sealed class HawkRocketFlight
    {
        public int Target { get; }
        public BallisticPoint Position { get; private set; }
        public BallisticPoint Direction { get; private set; }
        public double RemainingLife { get; private set; }
        readonly double speed, turn;
        public HawkRocketFlight(int target, BallisticPoint position, BallisticPoint direction, double speed, double radiansPerSecond, double lifetime)
        {
            if (target <= 0 || !Finite(position) || !Finite(direction) || Length(direction) < 1e-12 ||
                !Finite(speed) || speed <= 0 || !Finite(radiansPerSecond) || radiansPerSecond < 0 || !Finite(lifetime) || lifetime <= 0)
                throw new ArgumentException("Invalid Hawk rocket state");
            Target = target; Position = position; Direction = Normal(direction);
            this.speed = speed; turn = radiansPerSecond; RemainingLife = lifetime;
        }
        public double Speed=>speed;
        public double TurnSpeed=>turn;
        public static HawkRocketFlight Restore(int target,BallisticPoint position,BallisticPoint direction,double speed,double radiansPerSecond,double lifetime)
        {
            var result=new HawkRocketFlight(target,position,direction,speed,radiansPerSecond,lifetime);
            if(Math.Abs(Length(direction)-1)>1e-9)throw new ArgumentException("Saved rocket direction must be normalized.");
            result.Direction=direction;return result;
        }
        public void Advance(double dt, BallisticPoint? target)
        {
            if (!Finite(dt) || dt < 0 || (target.HasValue && !Finite(target.Value))) throw new ArgumentException("Invalid rocket step");
            dt = Math.Min(dt, RemainingLife);
            if (target.HasValue)
            {
                var delta = Sub(target.Value, Position);
                if (Length(delta) > 1e-12)
                {
                    var desired = Normal(delta);
                    double dot = Math.Max(-1, Math.Min(1, Dot(Direction, desired)));
                    double angle = Math.Acos(dot), limit = turn * dt;
                    if (angle <= limit) Direction = desired;
                    else if (limit > 0)
                    {
                        var tangent = Sub(desired, Scale(Direction, dot));
                        // Stable perpendicular handles exactly opposite targets without NaN/snap.
                        if (Length(tangent) < 1e-12)
                        {
                            var axis = Math.Abs(Direction.Y) < .9 ? new BallisticPoint(0, 1, 0) : new BallisticPoint(1, 0, 0);
                            tangent = Sub(axis, Scale(Direction, Dot(axis, Direction)));
                        }
                        Direction = Normal(Add(Scale(Direction, Math.Cos(limit)), Scale(Normal(tangent), Math.Sin(limit))));
                    }
                }
            }
            Position = Add(Position, Scale(Direction, speed * dt)); RemainingLife -= dt;
        }
        // Relative-motion segment vs sphere, used by the preview damage adapter and focused tests.
        public static bool Contact(BallisticPoint from, BallisticPoint to, BallisticPoint centerBefore, BallisticPoint centerAfter, double radius, out double fraction)
        {
            if (!Finite(radius) || radius <= 0) throw new ArgumentException("Invalid contact radius");
            var origin = Sub(from, centerBefore); var delta = Sub(Sub(to, from), Sub(centerAfter, centerBefore));
            double c = Dot(origin, origin) - radius * radius;
            if (c <= 0) { fraction = 0; return true; }
            double a = Dot(delta, delta), b = 2 * Dot(origin, delta), disc = b * b - 4 * a * c;
            fraction = 0;
            if (a < 1e-18 || disc < 0) return false;
            fraction = (-b - Math.Sqrt(disc)) / (2 * a);
            return fraction >= 0 && fraction <= 1;
        }
        public static BallisticPoint Add(BallisticPoint a, BallisticPoint b) => new BallisticPoint(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static BallisticPoint Sub(BallisticPoint a, BallisticPoint b) => new BallisticPoint(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static BallisticPoint Scale(BallisticPoint p, double n) => new BallisticPoint(p.X * n, p.Y * n, p.Z * n);
        public static double Dot(BallisticPoint a, BallisticPoint b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
        public static double Length(BallisticPoint p) => Math.Sqrt(Dot(p, p));
        public static BallisticPoint Normal(BallisticPoint p) => Scale(p, 1 / Length(p));
        static bool Finite(double n) => !double.IsNaN(n) && !double.IsInfinity(n);
        static bool Finite(BallisticPoint p) => Finite(p.X) && Finite(p.Y) && Finite(p.Z);
    }
}
