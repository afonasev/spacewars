using System;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Spacewars.BalanceLab;

namespace Spacewars.Tests.EditMode.BalanceLab
{
    static class Data
    {
        public static readonly string Root = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), Directory.Exists("Assets") ? ".." : "."));
        public static byte[] Pack => File.ReadAllBytes(Path.Combine(Root, "unity/Assets/Spacewars/Content/BalanceLabContract/catalog.json"));
        public static byte[] Provenance => File.ReadAllBytes(Path.Combine(Root, "unity/Assets/Spacewars/Content/BalanceLabContract/provenance.json"));
        public static byte[] Fixture(string name) => File.ReadAllBytes(Path.Combine(Root, name == "normalizer-rules.json" ? "unity/Assets/Spacewars/BalanceLab" : "unity/Assets/Spacewars/Tests/EditMode/BalanceLab/Fixtures", name));
        public static JObject PackTree => (JObject)ContractCodec.Parse(Pack);
        public static RevisionNormalizer Normalizer => new RevisionNormalizer(Fixture("normalizer-rules.json"), ContractCodec.Hash(Fixture("normalizer-rules.json")), (JObject)PackTree["metadata"], (JObject)PackTree["catalogs"]);
        public static VerifiedCatalog Catalog => new VerifiedCatalog(Pack, Provenance, new CatalogTrust(ContractCodec.Hash(Pack), ContractCodec.Hash(Provenance)), Normalizer);
    }
    [TestFixture]
    public sealed class ContractCodecTests
    {
        [Test]
        public void PublishedPackRoundtripsAllFourAndPreservesOriginalManifest()
        {
            var catalog = Data.Catalog; Assert.That(catalog.Revisions.Count, Is.EqualTo(4));
            Assert.That(catalog.OriginalManifest, Is.EqualTo(File.ReadAllBytes(Path.Combine(Data.Root, "unity/Tests/Fixtures/BalanceLab/catalog-manifest.json"))));
            Assert.That(catalog.Release.ToString(), Is.EqualTo("local:main:1788708872201:1@5"));
            Assert.That((bool)catalog.Provenance["resource"]["manifestHashMatches"], Is.False);
            foreach (var r in catalog.Revisions) { Assert.That(new CanonicalRevision(r.OriginalBytes).RawFingerprint, Is.EqualTo(r.RawFingerprint)); var copy = r.Snapshot; copy["unknown"] = 1; Assert.That(r.Snapshot["unknown"], Is.Null); }
        }
        [Test]
        public void JavascriptBinary64AndUtf16Goldens()
        {
            var g = ContractCodec.Parse(Data.Fixture("canonical-goldens.json"));
            Assert.That(((JArray)g["numbers"]).Count, Is.EqualTo(2056));
            foreach (var row in g["numbers"])
            { var value = BitConverter.Int64BitsToDouble(unchecked((long)Convert.ToUInt64((string)row["bits"], 16))); Assert.That(ContractCodec.Number(value), Is.EqualTo((string)row["expected"]), (string)row["bits"]); Assert.That((double)ContractCodec.Parse((string)row["expected"]), Is.EqualTo(value), "parse " + (string)row["bits"]); }
            foreach (var row in g["trees"]) Assert.That(ContractCodec.Write(ContractCodec.Parse((string)row["input"])), Is.EqualTo((string)row["expected"]));
            Assert.That(ContractCodec.Write(ContractCodec.Parse("9007199254740993")), Is.EqualTo("9007199254740992"));
        }
        [TestCase("{\"a\":1,\"a\":2}")]
        [TestCase("{\"a\":1,\"\\u0061\":2}")]
        [TestCase("[1,]")]
        [TestCase("{} {}")]
        [TestCase("{'a':1}")]
        [TestCase("/*x*/{}")]
        [TestCase("[NaN]")]
        [TestCase("[1e999]")]
        [TestCase("[01]")]
        [TestCase("[+1]")]
        [TestCase("[1.]")]
        [TestCase("\"\\x20\"")]
        [TestCase("\u00a0{}")]
        public void InvalidJsonFailsClosed(string text) => Assert.Catch<Exception>(() => ContractCodec.Parse(text));
        [Test]
        public void UnsupportedIdentityDateSchemaAndCorruptionReject()
        {
            var original = Data.Catalog.Revisions[0].Snapshot;
            foreach (var mutate in new Action<JObject>[] { v => v["schemaVersion"] = 9, v => v["schemaVersion"] = "2", v => v["aiProfile"]["schemaVersion"] = "1", v => v["profile"]["name"] = 2, v => v["ref"]["revision"] = 9007199254740992d, v => v["ref"]["createdOn"] = "20260230", v => v["ref"]["source"] = "device", v => v["aiProfileRef"]["id"] = "foreign" })
            { var v = (JObject)original.DeepClone(); mutate(v); Assert.Throws<FormatException>(() => new CanonicalRevision(v)); }
            var corrupt = Data.Pack; corrupt[12] ^= 1; Assert.Throws<FormatException>(() => new VerifiedCatalog(corrupt, Data.Provenance, new CatalogTrust(ContractCodec.Hash(Data.Pack), ContractCodec.Hash(Data.Provenance)), Data.Normalizer));
        }
        [Test]
        public void DatesAndUnknownTypeMetadataStayData()
        {
            var raw = Data.Catalog.Revisions[0].Snapshot; raw["unknown"] = ContractCodec.Parse("{\"$type\":\"Arbitrary.Type\",\"date\":\"2026-10-02T00:00:00Z\",\"surrogate\":\"\\ud800\"}");
            var bytes = Encoding.UTF8.GetBytes(ContractCodec.Write(raw) + " \n"); var r = new CanonicalRevision(bytes);
            Assert.That(r.OriginalBytes, Is.EqualTo(bytes)); Assert.That(r.Snapshot["unknown"]["date"].Type, Is.EqualTo(JTokenType.String));
            Assert.That(((string)r.Snapshot["unknown"]["surrogate"])[0], Is.EqualTo('\ud800'));
        }
    }
    [TestFixture]
    public sealed class NormalizerTests
    {
        [Test]
        public void AllPublishedAndMissingFieldCopiesMatchWebOracle()
        {
            var cases = (JArray)ContractCodec.Parse(Data.Fixture("normalizer-parity.json")); Assert.That(cases.Count, Is.EqualTo(8));
            foreach (var row in cases) { var raw = new CanonicalRevision(row["raw"]); var bytes = raw.OriginalBytes; var value = Data.Normalizer.Normalize(raw); Assert.That(ContractCodec.Write(value.Snapshot), Is.EqualTo(ContractCodec.Write(row["effective"]))); Assert.That(raw.OriginalBytes, Is.EqualTo(bytes)); }
        }
        [Test]
        public void ExplicitInvalidNumbersNullsBooleansAndRelationsNeverDefault()
        {
            var source = Data.Catalog.Revisions.Last().Snapshot;
            foreach (var mutate in new Action<JObject>[] { v => v["profile"]["system"]["cameraMinZoom"] = 99999, v => v["profile"]["system"]["gamepad"] = JValue.CreateNull(), v => v["profile"]["units"]["tank"]["firesWhileMoving"] = "true", v => v["aiProfile"]["tactics"]["competitiveEnabled"] = -1, v => v["profile"]["map"]["bridgeFlankWidth"] = v["profile"]["map"]["bridgeStandardWidth"].DeepClone(), v => v["aiProfile"]["personality"]["aggression"]["min"] = 1 })
            { var draft = (JObject)source.DeepClone(); mutate(draft); Assert.Throws<FormatException>(() => Data.Normalizer.Normalize(new CanonicalRevision(draft))); }
        }
        [Test]
        public void NormalizerFingerprintDriftRejects()
        { var rules = Data.Fixture("normalizer-rules.json"); rules[3] ^= 1; Assert.Throws<FormatException>(() => new RevisionNormalizer(rules, ContractCodec.Hash(Data.Fixture("normalizer-rules.json")), (JObject)Data.PackTree["metadata"], (JObject)Data.PackTree["catalogs"])); }
    }
    [TestFixture]
    public sealed class RepositoryTests
    {
        string dir;
        RevisionRepository repo;
        [SetUp] public void Setup() { dir = Path.Combine(Path.GetTempPath(), "spacewars-u9-" + Guid.NewGuid().ToString("N")); repo = Make(RepositoryCapabilities.Installed(dir)); }
        RevisionRepository Make(RepositoryCapabilities caps, AtomicStore store = null) => new RevisionRepository(Data.Catalog, Data.Normalizer, caps, () => new DateTime(2026, 10, 2, 0, 0, 1), () => "local:fixture:" + Guid.NewGuid().ToString("N"), store);
        [TearDown] public void Cleanup() { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        [Test]
        public void CreateCopySaveOlderFullGameplayAiAndUnknownAtMaximumPlusOne()
        {
            var v = repo.Read(); var source = v.Revisions[0];
            v = repo.CreateFromExactRevision(source.Identity, "Copy", v.Token); var local = v.Revisions.Last();
            var rawBefore = local.RawFingerprint; var draft = v.Materialize(local.Identity); draft["profile"]["economy"]["startingCredits"] = 700; draft["aiProfile"]["economy"]["tankResearchBankThreshold"] = 500; draft["unknown"] = new JObject { ["kept"] = "yes" };
            v = repo.SaveNextRevision(local.Identity, draft, v.Token); v = repo.SaveNextRevision(local.Identity, draft, v.Token);
            var saved = v.Revisions.Last(); Assert.That(saved.Identity.Revision, Is.EqualTo(3)); Assert.That(saved.AiIdentity.Revision, Is.EqualTo(3)); Assert.That((double)saved.Snapshot["aiProfile"]["economy"]["tankResearchBankThreshold"], Is.EqualTo(500)); Assert.That((double)saved.Snapshot["profile"]["economy"]["startingCredits"], Is.EqualTo(700)); Assert.That((string)saved.Snapshot["unknown"]["kept"], Is.EqualTo("yes")); Assert.That(v.Resolve(local.Identity).RawFingerprint, Is.EqualTo(rawBefore)); Assert.That((string)saved.Snapshot["ref"]["createdOn"], Is.EqualTo("20261002"));
        }
        [Test]
        public void PublishedProtectionAndRenameOverlayPreserveDatesIdsBytes()
        {
            var v = repo.Read(); Assert.Throws<InvalidOperationException>(() => repo.RenameProfile(v.Release.Id, "renamed", v.Token)); Assert.Throws<InvalidOperationException>(() => repo.DeleteProfile(v.Release.Id, "main", v.Token));
            v = repo.CreateFromExactRevision(v.Release, "Local", v.Token); var local = v.Revisions.Last(); var before = local.OriginalBytes;
            v = repo.RenameProfile(local.Identity.Id, "Renamed", v.Token);
            Assert.That(v.DisplayName(local.Identity.Id), Is.EqualTo("Renamed")); Assert.That(v.Resolve(local.Identity).OriginalBytes, Is.EqualTo(before)); Assert.That(v.Resolve(local.Identity).Name, Is.EqualTo("Local"));
            Assert.Throws<InvalidOperationException>(() => repo.DeleteProfile(local.Identity.Id, "wrong", v.Token));
            v = repo.RememberStarted(local.Identity, v.Token); v = repo.DeleteProfile(local.Identity.Id, "Renamed", v.Token); Assert.That(v.LastStarted, Is.Null); Assert.That(v.Release, Is.EqualTo(Data.Catalog.Release));
        }
        [Test]
        public void DeviceReleaseOnlyPublishedAndNoImplicitReleaseOrHistoryOverwrite()
        {
            var v = repo.Read(); var old = v.Revisions[0].Identity; v = repo.AssignDeviceRelease(old, v.Token); Assert.That(repo.Read().Release, Is.EqualTo(old));
            v = repo.CreateFromExactRevision(old, "Unpublished", v.Token); var local = v.Revisions.Last(); Assert.That(v.Release, Is.EqualTo(old));
            Assert.Throws<InvalidOperationException>(() => repo.AssignDeviceRelease(local.Identity, v.Token)); Assert.Throws<InvalidOperationException>(() => repo.PublishRevision(local.Identity, v.Token, null));
            Assert.Throws<ArgumentException>(() => RepositoryCapabilities.Development(".")); var readOnly = Make(RepositoryCapabilities.ReadOnly()); Assert.Throws<InvalidOperationException>(() => readOnly.CreateFromExactRevision(old, "No", readOnly.Read().Token));
        }
        [Test]
        public void DraftSurvivesConflictAndIoAndIdentityDateChangesReject()
        {
            var v = repo.Read(); var source = v.Release; var draft = v.Materialize(source); var before = ContractCodec.Write(draft);
            var stale = v.Token; v = repo.AssignDeviceRelease(v.Revisions[0].Identity, v.Token); Assert.Throws<StoreConflictException>(() => repo.SaveNextRevision(source, draft, stale)); Assert.That(ContractCodec.Write(draft), Is.EqualTo(before));
            draft["ref"]["createdOn"] = "20261001"; Assert.Throws<FormatException>(() => repo.SaveNextRevision(source, draft, v.Token));
            var store = new AtomicStore(Path.Combine(dir, "failed.json"), () => new JObject { ["schemaVersion"] = 1, ["records"] = new JArray(), ["names"] = new JObject(), ["receipts"] = new JObject() }, point => { if (point == "flushed") throw new IOException("fixture"); });
            var fail = Make(RepositoryCapabilities.Installed(dir), store); var initial = fail.Read(); var goodDraft = initial.Materialize(initial.Release); var bytes = ContractCodec.Write(goodDraft); Assert.Throws<StoreCommitException>(() => fail.SaveNextRevision(initial.Release, goodDraft, initial.Token)); Assert.That(ContractCodec.Write(goodDraft), Is.EqualTo(bytes)); Assert.That(fail.Read().Token.Generation, Is.Zero);
        }
        [Test]
        public void ExplicitLegacyImportIdempotencyConflictAndNoMigrationDate()
        {
            var raw = Data.Catalog.Revisions[0].Snapshot; raw["ref"]["id"] = "legacy:id"; ((JObject)raw["ref"]).Remove("createdOn"); raw["schemaVersion"] = 1; raw.Remove("aiProfile"); raw.Remove("aiProfileRef");
            var history = new JObject { ["schemaVersion"] = 1, ["profiles"] = new JObject { ["legacy:id"] = new JArray(raw) } }; var bytes = Encoding.UTF8.GetBytes(ContractCodec.Write(history));
            var import = LegacyImporter.Read(bytes, Encoding.UTF8.GetBytes("{bad}"), Data.Normalizer); var v = repo.ImportLegacy(import, repo.Read().Token); var token = v.Token; v = repo.ImportLegacy(import, token); Assert.That(v.Token, Is.EqualTo(token)); Assert.That(v.Revisions.Last().Snapshot["ref"]["createdOn"], Is.Null);
            raw["profile"]["economy"]["startingCredits"] = 800; history["profiles"]["legacy:id"] = new JArray(raw); var conflict = LegacyImporter.Read(Encoding.UTF8.GetBytes(ContractCodec.Write(history)), null, Data.Normalizer); Assert.Throws<FormatException>(() => repo.ImportLegacy(conflict, token)); Assert.That(repo.Read().Token, Is.EqualTo(token));
        }
        [Test]
        public void RenameThenPublishProtectsProfileAndRetainsOriginalLocalRecord()
        {
            Directory.CreateDirectory(Path.Combine(dir, "balance")); File.WriteAllBytes(Path.Combine(dir, "balance/releases.json"), Data.Catalog.OriginalManifest);
            var dev = Make(RepositoryCapabilities.Development(dir)); var v = dev.Read(); v = dev.CreateFromExactRevision(v.Release, "Before", v.Token); var local = v.Revisions.Last();
            v = dev.RenameProfile(local.Identity.Id, "After", v.Token); v = dev.PublishRevision(local.Identity, v.Token, v.PublicationToken);
            Assert.That(v.DisplayName(local.Identity.Id), Is.EqualTo("After")); Assert.That(dev.Read().Release, Is.EqualTo(local.Identity));
            Assert.Throws<InvalidOperationException>(() => dev.DeleteProfile(local.Identity.Id, "After", v.Token));
        }
        [Test]
        public void MalformedStandaloneAiOnlyDefaultsForMissingAiAndValidLegacyAiIsAttached()
        {
            var raw = Data.Catalog.Revisions[0].Snapshot; raw["ref"]["id"] = "legacy-ai"; raw["schemaVersion"] = 1; raw.Remove("aiProfile"); raw.Remove("aiProfileRef");
            var history = new JObject { ["schemaVersion"] = 1, ["profiles"] = new JObject { ["legacy-ai"] = new JArray(raw) } }; var bytes = Encoding.UTF8.GetBytes(ContractCodec.Write(history));
            var ai = Data.Catalog.Revisions.Last().Snapshot["aiProfile"]; ai["economy"]["tankResearchBankThreshold"] = -1;
            var imported = LegacyImporter.Read(bytes, Encoding.UTF8.GetBytes(ContractCodec.Write(ai)), Data.Normalizer); Assert.That(imported.Revisions[0].AiIdentity.Id, Is.EqualTo((string)ContractCodec.Parse(Data.Fixture("normalizer-rules.json"))["defaultAi"]["id"]));
            ai["economy"]["tankResearchBankThreshold"] = 500;
            imported = LegacyImporter.Read(bytes, Encoding.UTF8.GetBytes(ContractCodec.Write(ai)), Data.Normalizer); Assert.That((double)imported.Revisions[0].Snapshot["aiProfile"]["economy"]["tankResearchBankThreshold"], Is.EqualTo(500));
        }
        [Test]
        public void LegacyPreferencesRespectDeviceAndDevelopmentAuthorityAndPreserveStandaloneBytes()
        {
            var release = Data.Catalog.Revisions[0].Identity;
            var history = new JObject { ["schemaVersion"] = 3, ["profiles"] = new JObject(), ["assignedReleaseRef"] = release.ToJson(), ["lastStartedRef"] = release.ToJson() };
            var standalone = Encoding.UTF8.GetBytes(" { malformed legacy AI } ");
            var import = LegacyImporter.Read(Encoding.UTF8.GetBytes(ContractCodec.Write(history)), standalone, Data.Normalizer);
            Assert.That(import.OriginalStandaloneAi, Is.EqualTo(standalone)); var v = repo.ImportLegacy(import, repo.Read().Token); Assert.That(v.Release, Is.EqualTo(release)); Assert.That(v.LastStarted, Is.EqualTo(release));
            var stored = ContractCodec.Parse(File.ReadAllBytes(RepositoryCapabilities.Installed(dir).HistoryPath)); Assert.That(Convert.FromBase64String((string)stored["payload"]["receipts"][import.Receipt]["standaloneAiBase64"]), Is.EqualTo(standalone));
            Directory.CreateDirectory(Path.Combine(dir, "balance")); File.WriteAllBytes(Path.Combine(dir, "balance/releases.json"), Data.Catalog.OriginalManifest);
            var dev = Make(RepositoryCapabilities.Development(dir)); var dv = dev.ImportLegacy(import, dev.Read().Token); Assert.That(dv.Release, Is.EqualTo(Data.Catalog.Release));
        }
        [Test]
        public void CopyAndSavePreserveOpaqueFutureCollectionMembers()
        {
            var v = repo.Read(); var draft = v.Materialize(v.Release);
            draft["profile"]["units"]["future-unit"] = new JObject { ["unknown"] = "preserve" };
            draft["aiProfile"]["personality"]["future-trait"] = new JObject { ["unknown"] = "opaque" };
            draft["aiProfile"]["openings"]["weights"]["future-opening"] = 123;
            v = repo.SaveNextRevision(v.Release, draft, v.Token); var saved = v.Revisions.Last();
            Assert.That((string)saved.Snapshot["profile"]["units"]["future-unit"]["unknown"], Is.EqualTo("preserve"));
            Assert.That((string)saved.Snapshot["aiProfile"]["personality"]["future-trait"]["unknown"], Is.EqualTo("opaque"));
            Assert.That((double)saved.Snapshot["aiProfile"]["openings"]["weights"]["future-opening"], Is.EqualTo(123));
            v = repo.CreateFromExactRevision(saved.Identity, "Opaque copy", v.Token); var copy = v.Revisions.Last();
            Assert.That((string)copy.Snapshot["profile"]["units"]["future-unit"]["unknown"], Is.EqualTo("preserve"));
            Assert.That((string)copy.Snapshot["aiProfile"]["personality"]["future-trait"]["unknown"], Is.EqualTo("opaque"));
            var effective = Data.Normalizer.Normalize(copy); Assert.That(effective.Snapshot["profile"]["units"]["future-unit"], Is.Null); Assert.That(effective.HasRuntimeProjection, Is.False);
        }
        [Test]
        public void DevelopmentPublishesExactSnapshotAndPointerAtomically()
        {
            Directory.CreateDirectory(Path.Combine(dir, "balance")); File.WriteAllBytes(Path.Combine(dir, "balance/releases.json"), Data.Catalog.OriginalManifest);
            var dev = Make(RepositoryCapabilities.Development(dir)); var v = dev.Read(); v = dev.CreateFromExactRevision(v.Release, "Developer", v.Token); var local = v.Revisions.Last();
            var token = v.PublicationToken; v = dev.PublishRevision(local.Identity, v.Token, token); Assert.That(v.Release, Is.EqualTo(local.Identity)); Assert.That(v.Revisions.Count, Is.EqualTo(5)); Assert.Throws<InvalidOperationException>(() => dev.RenameProfile(local.Identity.Id, "blocked", v.Token));
            Assert.Throws<StoreConflictException>(() => dev.PublishRevision(local.Identity, v.Token, token)); var manifest = ContractCodec.Parse(File.ReadAllBytes(Path.Combine(dir, "balance/releases.json"))); Assert.That(RevisionIdentity.Read(manifest["releaseRef"]), Is.EqualTo(local.Identity)); Assert.That(((JArray)manifest["revisions"]).Count, Is.EqualTo(5));
        }
    }
    [TestFixture]
    public sealed class AtomicStoreTests
    {
        string dir, path;
        [SetUp] public void Setup() { dir = Path.Combine(Path.GetTempPath(), "spacewars-u9-disk-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir); path = Path.Combine(dir, "state.json"); }
        [TearDown] public void Cleanup() { Directory.Delete(dir, true); }
        AtomicStore Store(Action<string> hook = null) => new AtomicStore(path, () => new JObject { ["fixture"] = 0 }, hook);
        [Test]
        public void StaleHashGenerationAndMalformedCommittedStateFailClosed()
        { var s = Store(); var initial = s.Read(); var next = s.Commit(initial.Token, new JObject { ["fixture"] = 1 }); Assert.That(next.Token.Generation, Is.EqualTo(1)); Assert.Throws<StoreConflictException>(() => s.Commit(initial.Token, new JObject())); File.WriteAllText(path, "{truncated"); Assert.Catch<Exception>(() => s.Read()); Assert.Catch<Exception>(() => s.RecoverTemps()); }
        [TestCase("before-write", false)] [TestCase("before-flush", false)] [TestCase("flushed", false)] [TestCase("replaced", true)] [TestCase("readback", true)]
        public void InjectedFailureNeverReturnsSuccessAndReadbackReconciles(string phase, bool replaced)
        {
            var s = Store(); var initial = s.Commit(s.Read().Token, new JObject { ["fixture"] = 1 }); var failed = Store(p => { if (p == phase) throw new IOException("injected"); });
            var error = Assert.Throws<StoreCommitException>(() => failed.Commit(initial.Token, new JObject { ["fixture"] = 2 })); Assert.That(error.ReplacementOccurred, Is.EqualTo(replaced)); Assert.That((double)s.Read().Payload["fixture"], Is.EqualTo(replaced ? 2 : 1)); Assert.That(Directory.GetFiles(dir, "*.pending-*").Length, Is.Zero);
        }
    }
}
