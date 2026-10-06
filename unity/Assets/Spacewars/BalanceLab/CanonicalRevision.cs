using System;
using System.Globalization;
using Newtonsoft.Json.Linq;

namespace Spacewars.BalanceLab
{
    public sealed class RevisionIdentity : IEquatable<RevisionIdentity>
    {
        public string Id { get; }
        public long Revision { get; }
        public RevisionIdentity(string id, long revision)
        { if (string.IsNullOrWhiteSpace(id) || revision < 1 || revision > 9007199254740991L) throw new FormatException("Invalid revision identity"); Id = id; Revision = revision; }
        public JObject ToJson() => new JObject { ["id"] = Id, ["revision"] = Revision };
        public bool Equals(RevisionIdentity other) => other != null && Id == other.Id && Revision == other.Revision;
        public override bool Equals(object other) => Equals(other as RevisionIdentity);
        public override int GetHashCode() => Id.GetHashCode() ^ Revision.GetHashCode();
        public override string ToString() => Id + "@" + Revision.ToString(CultureInfo.InvariantCulture);
        public static RevisionIdentity Read(JToken token)
        { var o = Object(token); return new RevisionIdentity(Text(o["id"]), SafeInteger(o["revision"])); }
        internal static JObject Object(JToken t) => t as JObject ?? throw new FormatException("Expected object");
        internal static string Text(JToken t) => t?.Type == JTokenType.String && !string.IsNullOrWhiteSpace((string)t) ? (string)t : throw new FormatException("Expected nonempty string");
        internal static long SafeInteger(JToken t)
        { if (t == null || (t.Type != JTokenType.Integer && t.Type != JTokenType.Float)) throw new FormatException("Expected integer"); var v = (double)t; if (v < 1 || v > 9007199254740991d || Math.Truncate(v) != v) throw new FormatException("Unsafe identity"); return (long)v; }
        internal static bool Schema(JToken t, params int[] supported) => t != null && (t.Type == JTokenType.Integer || t.Type == JTokenType.Float) && Array.Exists(supported, version => (double)t == version);
        internal static void Date(JToken t)
        {
            if (t == null) return;
            var s = Text(t);
            if (s.Length != 8 || !DateTime.TryParseExact(s, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) || date.Year < 100) throw new FormatException("Invalid revision date");
        }
    }
    /// <summary>Original bytes and deep copies never escape by reference. Unknown fields are data, not runtime coverage.</summary>
    public sealed class CanonicalRevision
    {
        readonly byte[] original;
        readonly JObject raw;
        public RevisionIdentity Identity { get; }
        public RevisionIdentity AiIdentity { get; }
        public string Name { get; }
        public string RawFingerprint { get; }
        public string OriginalBytesHash { get; }
        public byte[] OriginalBytes => (byte[])original.Clone();
        public JObject Snapshot => (JObject)raw.DeepClone();
        public CanonicalRevision(byte[] bytes) : this(ContractCodec.Parse(bytes), bytes) { }
        public CanonicalRevision(JToken value) : this(value, ContractCodec.Utf8.GetBytes(ContractCodec.Write(value))) { }
        internal CanonicalRevision(JToken value, byte[] bytes)
        {
            raw = (JObject)RevisionIdentity.Object(value).DeepClone();
            if (!RevisionIdentity.Schema(raw["schemaVersion"], 2)) throw new FormatException("Unsupported ProfileRevision schema");
            var r = RevisionIdentity.Object(raw["ref"]); var p = RevisionIdentity.Object(raw["profile"]);
            var a = RevisionIdentity.Object(raw["aiProfile"]); var ar = RevisionIdentity.Object(raw["aiProfileRef"]);
            Identity = RevisionIdentity.Read(r); AiIdentity = RevisionIdentity.Read(ar); Name = RevisionIdentity.Text(r["name"]);
            var source = RevisionIdentity.Text(r["source"]); var aiSource = RevisionIdentity.Text(ar["source"]);
            if (source != "local" && source != "release" || aiSource != "candidate" && aiSource != "release") throw new FormatException("Invalid source identity");
            if (RevisionIdentity.Text(p["name"]) != Name || RevisionIdentity.SafeInteger(p["revision"]) != Identity.Revision || !AiIdentity.Equals(RevisionIdentity.Read(a))) throw new FormatException("Mismatched snapshot identities");
            if (!RevisionIdentity.Schema(a["schemaVersion"], 1, 2)) throw new FormatException("Unsupported AI schema");
            RevisionIdentity.Date(r["createdOn"]);
            // Traverse through writer also rejects non-JSON/nonfinite unknown fields.
            var originalTree = RevisionIdentity.Object(ContractCodec.Parse(bytes));
            var comparable = (JObject)raw.DeepClone();
            if (originalTree["aiProfile"] == null && RevisionIdentity.Schema(originalTree["schemaVersion"], 1, 2))
            { comparable.Remove("aiProfile"); comparable.Remove("aiProfileRef"); comparable["schemaVersion"] = originalTree["schemaVersion"].DeepClone(); }
            if (ContractCodec.Write(originalTree) != ContractCodec.Write(comparable)) throw new FormatException("Original bytes differ from immutable record");
            RawFingerprint = ContractCodec.Fingerprint(raw); original = (byte[])bytes.Clone(); OriginalBytesHash = ContractCodec.Hash(original);
        }
    }
}

namespace Spacewars.BalanceLab
{
    /// <summary>Hashes are supplied by trusted bootstrap/build configuration, never read as trust from the pack itself.</summary>
    public sealed class CatalogTrust
    {
        public string PackHash { get; }
        public string ProvenanceHash { get; }
        public CatalogTrust(string packHash, string provenanceHash)
        { if (packHash?.Length != 64 || provenanceHash?.Length != 64) throw new ArgumentException("Trusted build hashes required"); PackHash = packHash; ProvenanceHash = provenanceHash; }
    }
    public sealed class VerifiedCatalog
    {
        readonly JObject pack, provenance;
        readonly byte[] manifestBytes;
        readonly System.Collections.Generic.List<CanonicalRevision> revisions = new System.Collections.Generic.List<CanonicalRevision>();
        public RevisionIdentity Release { get; }
        public string PackHash { get; }
        public byte[] OriginalManifest => (byte[])manifestBytes.Clone();
        public JObject Metadata => (JObject)pack["metadata"].DeepClone();
        public JObject Catalogs => (JObject)pack["catalogs"].DeepClone();
        public JObject Provenance => (JObject)provenance.DeepClone();
        public System.Collections.Generic.IReadOnlyList<CanonicalRevision> Revisions => revisions.AsReadOnly();
        public VerifiedCatalog(byte[] packBytes, byte[] provenanceBytes, CatalogTrust trust, RevisionNormalizer normalizer)
        {
            PackHash = ContractCodec.Hash(packBytes);
            if (trust == null || PackHash != trust.PackHash || ContractCodec.Hash(provenanceBytes) != trust.ProvenanceHash) throw new FormatException("Build trust mismatch");
            pack = RevisionIdentity.Object(ContractCodec.Parse(packBytes)); provenance = RevisionIdentity.Object(ContractCodec.Parse(provenanceBytes));
            if (!RevisionIdentity.Schema(pack["schemaVersion"], 1) || !RevisionIdentity.Schema(provenance["schemaVersion"], 1) || (string)pack["contract"] != "spacewars-published-balance-lab" || (string)pack["consumption"] != "disconnected" || (string)provenance["packSha256"] != PackHash) throw new FormatException("Unsupported/corrupt pack");
            manifestBytes = ContractCodec.Utf8.GetBytes((string)pack["source"]?["bytesUtf8"] ?? throw new FormatException("Missing source bytes"));
            if ((string)pack["source"]["path"] != "balance/releases.json" || ContractCodec.Hash(manifestBytes) != (string)pack["source"]["sha256"] || ContractCodec.Write(ContractCodec.Parse(manifestBytes)) != ContractCodec.Write(pack["rawCatalog"])) throw new FormatException("Manifest integrity");
            if (ContractCodec.Fingerprint(pack["metadata"]) != (string)pack["metadataSha256"]) throw new FormatException("Metadata integrity");
            var catalog = RevisionIdentity.Object(pack["rawCatalog"]);
            if (!RevisionIdentity.Schema(catalog["schemaVersion"], 1)) throw new FormatException("Unsupported manifest");
            Release = RevisionIdentity.Read(catalog["releaseRef"]);
            if (!Release.Equals(RevisionIdentity.Read(pack["releaseRef"]))) throw new FormatException("Release mismatch");
            var seen = new System.Collections.Generic.HashSet<RevisionIdentity>();
            var raws = catalog["revisions"] as JArray ?? throw new FormatException("Missing catalog revisions");
            var entries = pack["revisions"] as JArray ?? throw new FormatException("Missing entries");
            if (raws.Count == 0 || raws.Count != entries.Count) throw new FormatException("Catalog count");
            for (int i = 0; i < raws.Count; i++)
            {
                var raw = new CanonicalRevision(raws[i]); var entry = entries[i];
                if (!seen.Add(raw.Identity) || ContractCodec.Write(raws[i]["ref"]) != ContractCodec.Write(entry["ref"]) || ContractCodec.Write(raws[i]["aiProfileRef"]) != ContractCodec.Write(entry["aiProfileRef"]) || raw.RawFingerprint != (string)entry["rawSha256"]) throw new FormatException("Revision integrity");
                var effective = normalizer.Normalize(raw);
                if (effective.EffectiveFingerprint != (string)entry["effectiveSha256"] || ContractCodec.Write(effective.Snapshot) != ContractCodec.Write(entry["effective"])) throw new FormatException("Effective drift");
                revisions.Add(raw);
            }
            if (!seen.Contains(Release)) throw new FormatException("Missing exact release");
            var sourceSeen = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal); bool manifestFound = false;
            foreach (JObject source in provenance["sources"] as JArray ?? throw new FormatException("Missing provenance"))
            {
                var path = RevisionIdentity.Text(source["path"]); var hash = RevisionIdentity.Text(source["sha256"]);
                if (path.StartsWith("/", StringComparison.Ordinal) || path.Contains("..") || path.Contains("\\") || hash.Length != 64 || !sourceSeen.Add(path)) throw new FormatException("Invalid provenance entry");
                if (path == "balance/releases.json") { manifestFound = hash == (string)pack["source"]["sha256"]; }
                // Original exporter/source hashes are historical provenance. Native trust,
                // manifest, metadata and revision integrity are verified above.
            }
            if (!manifestFound || (string)provenance["resource"]?["currentManifestSha256"] != (string)pack["source"]["sha256"]) throw new FormatException("Provenance manifest mismatch");
            // Diagnostic Resource mismatch remains visible; does not become a parity certificate.
        }
    }
}
