using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
namespace Spacewars.BalanceLab
{
    public sealed class LegacyImport
    {
        public string Receipt { get; }
        public IReadOnlyList<CanonicalRevision> Revisions { get; }
        public byte[] OriginalPayload => (byte[])bytes.Clone();
        readonly byte[] bytes, standalone;
        readonly JObject preferences;
        public byte[] OriginalStandaloneAi => standalone == null ? null : (byte[])standalone.Clone();
        public JObject Preferences => (JObject)preferences.DeepClone();
        internal LegacyImport(byte[] bytes, byte[] standalone, JObject preferences, List<CanonicalRevision> revisions, string receipt)
        { this.bytes = (byte[])bytes.Clone(); this.standalone = standalone == null ? null : (byte[])standalone.Clone(); this.preferences = (JObject)preferences.DeepClone(); Receipt = receipt; Revisions = revisions.AsReadOnly(); }
    }
    public static class LegacyImporter
    {
        public static LegacyImport Read(byte[] historyBytes, byte[] standaloneAiBytes, RevisionNormalizer normalizer)
        {
            var history = RevisionIdentity.Object(ContractCodec.Parse(historyBytes));
            if (!RevisionIdentity.Schema(history["schemaVersion"], 1, 2, 3)) throw new FormatException("Unsupported legacy history");
            JObject ai = normalizer.DefaultAi, aiRef = normalizer.DefaultAiRef;
            if (standaloneAiBytes != null)
            {
                try
                {
                    var candidate = normalizer.Ai(RevisionIdentity.Object(ContractCodec.Parse(standaloneAiBytes)));
                    var reference = RevisionIdentity.Read(candidate);
                    if (!RevisionIdentity.Schema(candidate["schemaVersion"], 1, 2)) throw new FormatException("AI schema");
                    normalizer.ValidateAiSnapshot(candidate);
                    // Validate AI with a known valid gameplay shell, without masking explicit invalid AI.
                    ai = candidate; aiRef = reference.ToJson(); aiRef["source"] = reference.Id == (string)normalizer.DefaultAi["id"] ? "release" : "candidate";
                }
                catch (Exception ex) when (ex is FormatException || ex is Newtonsoft.Json.JsonException || ex is ArgumentException)
                { ai = normalizer.DefaultAi; aiRef = normalizer.DefaultAiRef; }
            }
            var revisions = new List<CanonicalRevision>(); var seen = new HashSet<RevisionIdentity>();
            foreach (var group in RevisionIdentity.Object(history["profiles"]).Properties())
            {
                if (!(group.Value is JArray array)) throw new FormatException("Legacy revision list");
                foreach (var record in array)
                {
                    var raw = (JObject)RevisionIdentity.Object(record).DeepClone(); var original = ContractCodec.Utf8.GetBytes(ContractCodec.Write(record));
                    if (!RevisionIdentity.Schema(raw["schemaVersion"], 1, 2)) throw new FormatException("Unsupported legacy revision");
                    if (raw["aiProfile"] == null)
                    { raw["aiProfile"] = ai.DeepClone(); raw["aiProfileRef"] = aiRef.DeepClone(); raw["schemaVersion"] = 2; }
                    var canonical = new CanonicalRevision(raw, original);
                    if (canonical.Identity.Id != group.Name || !seen.Add(canonical.Identity)) throw new FormatException("Duplicate/foreign legacy revision");
                    normalizer.Normalize(canonical); revisions.Add(canonical);
                }
            }
            var preferences = new JObject();
            foreach (var key in new[] { "assignedReleaseRef", "lastStartedRef" })
            {
                if (key == "lastStartedRef" && (double)history["schemaVersion"] < 2) continue;
                if (history[key] == null) continue;
                try { preferences[key] = RevisionIdentity.Read(history[key]).ToJson(); }
                catch (FormatException) { /* Invalid legacy preference has no authority; original payload remains preserved. */ }
            }
            return new LegacyImport(historyBytes, standaloneAiBytes, preferences, revisions, ContractCodec.Hash(historyBytes) + ":" + (standaloneAiBytes == null ? "absent" : ContractCodec.Hash(standaloneAiBytes)));
        }
    }
}
