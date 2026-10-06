using System;
using System.Collections.Generic;

namespace Spacewars.Simulation
{
    [Serializable]
    public sealed class FoundationProfileData
    {
        public int schemaVersion;
        public string profileId;
        public int revision;
        public string sourceCommit;
        public string sourceProfileId;
        public int sourceProfileRevision;
        public string sourceManifestSha256;
        public double cameraPanSpeed;
        public double cameraPitchDegrees;
        public double cameraHeight;
        public double cameraOffsetZ;
        public double cameraZoom;
        public double cameraMinZoom;
        public double cameraMaxZoom;
        public double cameraZoomSpeed;
        public double moveSpeed;
        public double groundHalfExtent;
    }

    public sealed class FoundationProfile
    {
        public const string RequiredProfileId = "unity-foundation-diagnostic-v1";
        public const int RequiredSchemaVersion = 1;

        private FoundationProfile(FoundationProfileData data)
        {
            SchemaVersion = data.schemaVersion;
            ProfileId = data.profileId;
            Revision = data.revision;
            SourceCommit = data.sourceCommit;
            SourceProfileId = data.sourceProfileId;
            SourceProfileRevision = data.sourceProfileRevision;
            SourceManifestSha256 = data.sourceManifestSha256;
            CameraPanSpeed = data.cameraPanSpeed;
            CameraPitchDegrees = data.cameraPitchDegrees;
            CameraHeight = data.cameraHeight;
            CameraOffsetZ = data.cameraOffsetZ;
            CameraZoom = data.cameraZoom;
            CameraMinZoom = data.cameraMinZoom;
            CameraMaxZoom = data.cameraMaxZoom;
            CameraZoomSpeed = data.cameraZoomSpeed;
            MoveSpeed = data.moveSpeed;
            GroundHalfExtent = data.groundHalfExtent;
        }

        public int SchemaVersion { get; private set; }
        public string ProfileId { get; private set; }
        public int Revision { get; private set; }
        public string SourceCommit { get; private set; }
        public string SourceProfileId { get; private set; }
        public int SourceProfileRevision { get; private set; }
        public string SourceManifestSha256 { get; private set; }
        public double CameraPanSpeed { get; private set; }
        public double CameraPitchDegrees { get; private set; }
        public double CameraHeight { get; private set; }
        public double CameraOffsetZ { get; private set; }
        public double CameraZoom { get; private set; }
        public double CameraMinZoom { get; private set; }
        public double CameraMaxZoom { get; private set; }
        public double CameraZoomSpeed { get; private set; }
        public double MoveSpeed { get; private set; }
        public double GroundHalfExtent { get; private set; }

        public static FoundationProfile Create(FoundationProfileData data)
        {
            Validate(data);
            return new FoundationProfile(data);
        }

        public static void Validate(FoundationProfileData data)
        {
            if (data == null) throw new ArgumentNullException("data");
            if (data.schemaVersion != RequiredSchemaVersion) throw new ArgumentException("Unsupported foundation profile schema.", "data");
            if (data.profileId != RequiredProfileId) throw new ArgumentException("Unexpected foundation profile id.", "data");
            if (data.revision < 1 || String.IsNullOrWhiteSpace(data.sourceCommit) || String.IsNullOrWhiteSpace(data.sourceProfileId)
                || data.sourceProfileRevision < 1 || String.IsNullOrWhiteSpace(data.sourceManifestSha256))
                throw new ArgumentException("Foundation profile provenance is incomplete.", "data");
            foreach (FoundationProfileField field in FoundationProfileMetadata.Fields)
            {
                double value = field.Read(data);
                if (Double.IsNaN(value) || Double.IsInfinity(value) || value < field.Minimum || value > field.Maximum)
                    throw new ArgumentException("Invalid foundation profile field: " + field.Path, "data");
            }
            if (data.cameraMinZoom > data.cameraZoom || data.cameraZoom > data.cameraMaxZoom)
                throw new ArgumentException("Camera zoom must be inside its range.", "data");
        }
    }

    public sealed class FoundationProfileField
    {
        private readonly Func<FoundationProfileData, double> read;

        public FoundationProfileField(string path, string group, string label, string description, string unit, double minimum, double maximum, double step, Func<FoundationProfileData, double> read)
        {
            Path = path; Group = group; Label = label; Description = description; Unit = unit;
            Minimum = minimum; Maximum = maximum; Step = step; this.read = read;
        }

        public string Path { get; private set; }
        public string Group { get; private set; }
        public string Label { get; private set; }
        public string Description { get; private set; }
        public string Unit { get; private set; }
        public double Minimum { get; private set; }
        public double Maximum { get; private set; }
        public double Step { get; private set; }
        public double Read(FoundationProfileData data) { return read(data); }
    }

    public static class FoundationProfileMetadata
    {
        public static readonly IReadOnlyList<FoundationProfileField> Fields = new[]
        {
            new FoundationProfileField("camera.panSpeed", "Camera", "Pan speed", "Camera ground-pan speed.", "m/s", 0.1, 50, 0.1, p => p.cameraPanSpeed),
            new FoundationProfileField("camera.pitchDegrees", "Camera", "Pitch", "Diagnostic camera pitch.", "degrees", 10, 85, 1, p => p.cameraPitchDegrees),
            new FoundationProfileField("camera.height", "Camera", "Height", "Diagnostic camera height.", "m", 1, 100, 1, p => p.cameraHeight),
            new FoundationProfileField("camera.offsetZ", "Camera", "Z offset", "Diagnostic camera Z offset.", "m", -100, 100, 1, p => p.cameraOffsetZ),
            new FoundationProfileField("camera.zoom", "Camera", "Default zoom", "Initial diagnostic camera zoom.", "m", 6, 30, 0.1, p => p.cameraZoom),
            new FoundationProfileField("camera.minZoom", "Camera", "Minimum zoom", "Closest permitted camera zoom.", "m", 1, 30, 0.1, p => p.cameraMinZoom),
            new FoundationProfileField("camera.maxZoom", "Camera", "Maximum zoom", "Farthest permitted camera zoom.", "m", 6, 100, 0.1, p => p.cameraMaxZoom),
            new FoundationProfileField("camera.zoomSpeed", "Camera", "Zoom speed", "Wheel zoom multiplier.", "ratio", 0.001, 1, 0.001, p => p.cameraZoomSpeed),
            new FoundationProfileField("movement.speed", "Diagnostic movement", "Move speed", "Flat-fixture movement speed; not release tank kinematics.", "m/s", 0.1, 50, 0.1, p => p.moveSpeed),
            new FoundationProfileField("fixture.groundHalfExtent", "Diagnostic fixture", "Ground half extent", "Accepted move square half extent.", "m", 1, 1000, 1, p => p.groundHalfExtent),
        };
    }

    public static class FoundationFixtureGeometry
    {
        // Technical one-unit fixture values, deliberately outside gameplay balance.
        public const string PlayerId = "player-1";
        public const string EntityId = "tank-1";
        public const double InitialX = 0d;
        public const double InitialZ = 0d;
    }
}
