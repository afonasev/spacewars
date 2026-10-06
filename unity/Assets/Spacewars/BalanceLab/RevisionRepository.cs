using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Spacewars.BalanceLab
{
    public sealed class RepositoryView
    {
        readonly CanonicalRevision[] revisions;
        readonly JObject names;
        public StoreToken Token { get; }
        public StoreToken PublicationToken { get; }
        public RevisionIdentity Release { get; }
        public RevisionIdentity LastStarted { get; }
        public IReadOnlyList<CanonicalRevision> Revisions => Array.AsReadOnly(revisions);
        internal RepositoryView(CanonicalRevision[] revisions, JObject names, StoreToken token, StoreToken publication, RevisionIdentity release, RevisionIdentity last)
        { this.revisions = revisions; this.names = (JObject)names.DeepClone(); Token = token; PublicationToken = publication; Release = release; LastStarted = last; }
        public CanonicalRevision Resolve(RevisionIdentity identity) => revisions.FirstOrDefault(r => r.Identity.Equals(identity)) ?? throw new FormatException("Missing exact revision " + identity);
        public string DisplayName(string id) => (string)names[id] ?? revisions.Last(r => r.Identity.Id == id).Name;
        public JObject Materialize(RevisionIdentity identity)
        { var snapshot = Resolve(identity).Snapshot; snapshot["ref"]["name"] = DisplayName(identity.Id); snapshot["profile"]["name"] = DisplayName(identity.Id); return snapshot; }
    }
    public interface IBalanceRevisionRepository
    {
        RepositoryView Read();
        RepositoryView CreateFromExactRevision(RevisionIdentity source, string name, StoreToken expected);
        RepositoryView SaveNextRevision(RevisionIdentity source, JObject unifiedDraft, StoreToken expected);
        RepositoryView RenameProfile(string id, string name, StoreToken expected);
        RepositoryView DeleteProfile(string id, string confirmedName, StoreToken expected);
        RepositoryView AssignDeviceRelease(RevisionIdentity identity, StoreToken expected);
        RepositoryView PublishRevision(RevisionIdentity identity, StoreToken expectedHistory, StoreToken expectedPublication);
        RepositoryView ImportLegacy(LegacyImport import, StoreToken expected);
    }
    public sealed class RevisionRepository : IBalanceRevisionRepository
    {
        readonly VerifiedCatalog shipped;
        readonly RevisionNormalizer normalizer;
        readonly RepositoryCapabilities capabilities;
        readonly AtomicStore history, publication;
        readonly Func<DateTime> localClock;
        readonly Func<string> nextId;
        public RevisionRepository(VerifiedCatalog shipped, RevisionNormalizer normalizer, RepositoryCapabilities capabilities, Func<DateTime> localClock, Func<string> nextId, AtomicStore history = null, AtomicStore publication = null)
        {
            this.shipped = shipped; this.normalizer = normalizer; this.capabilities = capabilities; this.localClock = localClock; this.nextId = nextId;
            this.history = history ?? (capabilities.CanSave ? new AtomicStore(capabilities.HistoryPath, EmptyState) : null);
            this.publication = publication ?? (capabilities.CanPublish ? new AtomicStore(capabilities.PublicationPath, () => throw new System.IO.IOException("Configured publication catalog absent"), rawDocument: true) : null);
        }
        static JObject EmptyState() => new JObject { ["schemaVersion"] = 1, ["records"] = new JArray(), ["names"] = new JObject(), ["receipts"] = new JObject() };
        StoreRead State() => history?.Read() ?? new StoreRead(EmptyState(), new StoreToken(0, "readonly"));
        sealed class Published
        { public List<CanonicalRevision> Revisions; public RevisionIdentity Release; public StoreToken Token; public JObject Document; }
        Published PublishedNow()
        {
            if (publication == null) return new Published { Revisions = shipped.Revisions.ToList(), Release = shipped.Release };
            var read = publication.Read(); var m = read.Payload;
            if (!RevisionIdentity.Schema(m["schemaVersion"], 1) || !(m["revisions"] is JArray array)) throw new FormatException("Invalid publication document");
            var list = array.Select(v => new CanonicalRevision(v)).ToList(); var identities = new HashSet<RevisionIdentity>();
            foreach (var revision in list) { if (!identities.Add(revision.Identity)) throw new FormatException("Duplicate publication"); normalizer.Normalize(revision); }
            var release = RevisionIdentity.Read(m["releaseRef"]); if (!identities.Contains(release)) throw new FormatException("Missing published release");
            // Existing trusted published revisions cannot be replaced by an edited dev manifest.
            foreach (var old in shipped.Revisions) if (!list.Any(r => r.Identity.Equals(old.Identity) && ImmutableHash(r) == ImmutableHash(old))) throw new FormatException("Published snapshot changed");
            return new Published { Revisions = list, Release = release, Token = read.Token, Document = m };
        }
        List<CanonicalRevision> Revisions(StoreRead read, Published pub)
        {
            var state = read.Payload;
            if (!RevisionIdentity.Schema(state["schemaVersion"], 1) || !(state["records"] is JArray records) || !(state["names"] is JObject) || !(state["receipts"] is JObject)) throw new FormatException("Invalid history payload");
            var all = pub.Revisions.ToList(); var localSeen = new HashSet<RevisionIdentity>();
            foreach (JObject record in records)
            {
                var bytes = Convert.FromBase64String(RevisionIdentity.Text(record["originalBytesBase64"]));
                if (ContractCodec.Hash(bytes) != (string)record["originalSha256"]) throw new FormatException("Original record corruption");
                var revision = new CanonicalRevision(record["revision"], bytes);
                if (revision.RawFingerprint != (string)record["rawSha256"] || !localSeen.Add(revision.Identity)) throw new FormatException("History integrity");
                normalizer.Normalize(revision);
                var known = all.FirstOrDefault(r => r.Identity.Equals(revision.Identity));
                if (known != null)
                {
                    var materialized = revision;
                    if (state["names"][revision.Identity.Id] != null) { var renamed = revision.Snapshot; renamed["ref"]["name"] = state["names"][revision.Identity.Id].DeepClone(); renamed["profile"]["name"] = state["names"][revision.Identity.Id].DeepClone(); materialized = new CanonicalRevision(renamed); }
                    if (ImmutableHash(known) != ImmutableHash(materialized)) throw new FormatException("Conflicting history snapshot");
                }
                else all.Add(revision);
            }
            foreach (var name in ((JObject)state["names"]).Properties())
                if (!all.Any(r => r.Identity.Id == name.Name) || pub.Revisions.Any(r => r.Identity.Id == name.Name && r.Name != (string)name.Value) || string.IsNullOrWhiteSpace((string)name.Value)) throw new FormatException("Invalid name overlay");
            return all;
        }
        static string ImmutableHash(CanonicalRevision revision)
        { var snapshot = revision.Snapshot; snapshot["ref"]["source"] = "release"; return ContractCodec.Fingerprint(snapshot); }
        RepositoryView View(StoreRead state, Published pub)
        {
            var all = Revisions(state, pub); var payload = state.Payload;
            var release = pub.Release;
            if (capabilities.CanAssignDeviceRelease && payload["assignedReleaseRef"] != null)
            { var preference = RevisionIdentity.Read(payload["assignedReleaseRef"]); if (pub.Revisions.Any(r => r.Identity.Equals(preference))) release = preference; }
            var last = payload["lastStartedRef"] == null ? null : RevisionIdentity.Read(payload["lastStartedRef"]);
            if (last != null && !all.Any(r => r.Identity.Equals(last))) last = null;
            return new RepositoryView(all.ToArray(), (JObject)payload["names"], state.Token, pub.Token, release, last);
        }
        public RepositoryView Read() => View(State(), PublishedNow());
        RepositoryView Mutate(Func<RepositoryView> operation)
        { Writable(); return AtomicStore.Exclusive(history.Path + ".repository", operation); }
        void Writable() { if (!capabilities.CanSave || history == null) throw new InvalidOperationException("Repository storage is read-only"); }
        StoreRead Expected(StoreToken expected)
        { Writable(); var state = State(); if (!state.Token.Equals(expected)) throw new StoreConflictException(); return state; }
        static JObject Record(CanonicalRevision revision) => new JObject { ["revision"] = revision.Snapshot, ["originalBytesBase64"] = Convert.ToBase64String(revision.OriginalBytes), ["originalSha256"] = revision.OriginalBytesHash, ["rawSha256"] = revision.RawFingerprint };
        RepositoryView Commit(StoreRead before, JObject payload, Published pub)
        { var candidate = new StoreRead(payload, before.Token); View(candidate, pub); return View(history.Commit(before.Token, payload), PublishedNow()); }
        void Name(RepositoryView view, string name, string except = null)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new FormatException("Empty profile name");
            if (view.Revisions.Select(r => r.Identity.Id).Distinct().Any(id => id != except && string.Equals(view.DisplayName(id).Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase))) throw new FormatException("Duplicate profile name");
        }
        string CreatedOn() => localClock().ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);
        public RepositoryView CreateFromExactRevision(RevisionIdentity source, string name, StoreToken expected) => Mutate(() => CreateFromExactRevisionCore(source, name, expected));
        RepositoryView CreateFromExactRevisionCore(RevisionIdentity source, string name, StoreToken expected)
        {
            var before = Expected(expected); var pub = PublishedNow(); var view = View(before, pub); Name(view, name);
            var baseRevision = view.Resolve(source); var snapshot = normalizer.SavedSnapshot(baseRevision);
            string id = nextId(); if (view.Revisions.Any(r => r.Identity.Id == id)) throw new FormatException("Profile id collision");
            snapshot["ref"]["id"] = id; snapshot["ref"]["name"] = name.Trim(); snapshot["ref"]["revision"] = 1; snapshot["ref"]["source"] = "local"; snapshot["ref"]["createdOn"] = CreatedOn();
            snapshot["profile"]["name"] = name.Trim(); snapshot["profile"]["revision"] = 1;
            if (pub.Revisions.Any(r => r.Identity.Equals(source))) snapshot["baseReleaseId"] = source.Id;
            var revision = new CanonicalRevision(snapshot); normalizer.Normalize(revision);
            var payload = before.Payload; ((JArray)payload["records"]).Add(Record(revision)); return Commit(before, payload, pub);
        }
        public RepositoryView SaveNextRevision(RevisionIdentity source, JObject unifiedDraft, StoreToken expected) => Mutate(() => SaveNextRevisionCore(source, unifiedDraft, expected));
        RepositoryView SaveNextRevisionCore(RevisionIdentity source, JObject unifiedDraft, StoreToken expected)
        {
            var before = Expected(expected); var pub = PublishedNow(); var view = View(before, pub); view.Resolve(source);
            // Caller cannot smuggle identity/name/date changes through a draft.
            var draft = new CanonicalRevision(unifiedDraft);
            if (!draft.Identity.Equals(source) || draft.Name != view.DisplayName(source.Id)) throw new FormatException("Draft identity/name protected");
            var original = view.Materialize(source);
            if (!JToken.DeepEquals(draft.Snapshot["ref"], original["ref"]) || !JToken.DeepEquals(draft.Snapshot["aiProfileRef"], original["aiProfileRef"]) || !JToken.DeepEquals(draft.Snapshot["baseReleaseId"], original["baseReleaseId"]) || !JToken.DeepEquals(draft.Snapshot["aiProfile"]["schemaVersion"], original["aiProfile"]["schemaVersion"])) throw new FormatException("Draft date/source protected");
            var normalized = normalizer.SavedSnapshot(draft);
            long maximum = view.Revisions.Where(r => r.Identity.Id == source.Id).Max(r => r.Identity.Revision);
            if (maximum >= 9007199254740991L) throw new FormatException("Revision exhausted"); long next = maximum + 1;
            normalized["ref"]["revision"] = next; normalized["ref"]["source"] = "local"; normalized["ref"]["createdOn"] = CreatedOn(); normalized["profile"]["revision"] = next;
            normalized["aiProfile"]["revision"] = next;
            // Preserve unknown AI reference fields while updating authoritative identity.
            normalized["aiProfileRef"]["id"] = normalized["aiProfile"]["id"].DeepClone(); normalized["aiProfileRef"]["revision"] = next; normalized["aiProfileRef"]["source"] = "candidate";
            var saved = new CanonicalRevision(normalized); normalizer.Normalize(saved);
            var payload = before.Payload; ((JArray)payload["records"]).Add(Record(saved)); return Commit(before, payload, pub);
        }
        void LocalProfile(string id, RepositoryView view, Published pub)
        { if (pub.Revisions.Any(r => r.Identity.Id == id)) throw new InvalidOperationException("Published profile protected"); if (!view.Revisions.Any(r => r.Identity.Id == id)) throw new FormatException("Missing profile"); }
        public RepositoryView RenameProfile(string id, string name, StoreToken expected) => Mutate(() => RenameProfileCore(id, name, expected));
        RepositoryView RenameProfileCore(string id, string name, StoreToken expected)
        {
            var before = Expected(expected); var pub = PublishedNow(); var view = View(before, pub); LocalProfile(id, view, pub); Name(view, name, id);
            var payload = before.Payload; payload["names"][id] = name.Trim(); return Commit(before, payload, pub);
        }
        public RepositoryView DeleteProfile(string id, string confirmedName, StoreToken expected) => Mutate(() => DeleteProfileCore(id, confirmedName, expected));
        RepositoryView DeleteProfileCore(string id, string confirmedName, StoreToken expected)
        {
            var before = Expected(expected); var pub = PublishedNow(); var view = View(before, pub); LocalProfile(id, view, pub);
            if (confirmedName != view.DisplayName(id)) throw new InvalidOperationException("Confirm whole profile by exact name");
            var payload = before.Payload;
            foreach (var record in ((JArray)payload["records"]).Where(r => (string)r["revision"]["ref"]["id"] == id).ToArray()) record.Remove();
            ((JObject)payload["names"]).Remove(id);
            foreach (var key in new[] { "lastStartedRef", "assignedReleaseRef" }) if ((string)payload[key]?["id"] == id) payload.Remove(key);
            return Commit(before, payload, pub);
        }
        public RepositoryView AssignDeviceRelease(RevisionIdentity identity, StoreToken expected) => Mutate(() => AssignDeviceReleaseCore(identity, expected));
        RepositoryView AssignDeviceReleaseCore(RevisionIdentity identity, StoreToken expected)
        {
            if (!capabilities.CanAssignDeviceRelease) throw new InvalidOperationException("Device release assignment unavailable");
            var before = Expected(expected); var pub = PublishedNow(); if (!pub.Revisions.Any(r => r.Identity.Equals(identity))) throw new InvalidOperationException("Only published exact revisions may be assigned");
            var payload = before.Payload; payload["assignedReleaseRef"] = identity.ToJson(); return Commit(before, payload, pub);
        }
        public RepositoryView RememberStarted(RevisionIdentity identity, StoreToken expected) => Mutate(() => RememberStartedCore(identity, expected));
        RepositoryView RememberStartedCore(RevisionIdentity identity, StoreToken expected)
        { var before = Expected(expected); var pub = PublishedNow(); View(before, pub).Resolve(identity); var payload = before.Payload; payload["lastStartedRef"] = identity.ToJson(); return Commit(before, payload, pub); }
        public RepositoryView PublishRevision(RevisionIdentity identity, StoreToken expectedHistory, StoreToken expectedPublication) => Mutate(() => PublishRevisionCore(identity, expectedHistory, expectedPublication));
        RepositoryView PublishRevisionCore(RevisionIdentity identity, StoreToken expectedHistory, StoreToken expectedPublication)
        {
            if (!capabilities.CanPublish || publication == null) throw new InvalidOperationException("Publication requires trusted development capability");
            var before = Expected(expectedHistory); var pub = PublishedNow(); if (!pub.Token.Equals(expectedPublication)) throw new StoreConflictException();
            var view = View(before, pub); var selected = view.Resolve(identity);
            if (view.DisplayName(identity.Id) != selected.Name) selected = new CanonicalRevision(view.Materialize(identity));
            normalizer.Normalize(selected);
            var document = (JObject)pub.Document.DeepClone(); var existing = pub.Revisions.FirstOrDefault(r => r.Identity.Equals(identity));
            if (existing != null && ImmutableHash(existing) != ImmutableHash(selected)) throw new FormatException("Published immutable conflict");
            if (existing == null) ((JArray)document["revisions"]).Add(selected.Snapshot);
            document["releaseRef"] = identity.ToJson();
            // Publication is one atomic document, independent of local draft state.
            publication.Commit(pub.Token, document);
            return Read();
        }
        public RepositoryView ImportLegacy(LegacyImport import, StoreToken expected) => Mutate(() => ImportLegacyCore(import, expected));
        RepositoryView ImportLegacyCore(LegacyImport import, StoreToken expected)
        {
            var before = Expected(expected); var pub = PublishedNow(); var view = View(before, pub); var payload = before.Payload;
            if (payload["receipts"][import.Receipt] != null) return view;
            foreach (var revision in import.Revisions)
            {
                var known = view.Revisions.FirstOrDefault(r => r.Identity.Equals(revision.Identity));
                if (known != null) { if (ImmutableHash(known) != ImmutableHash(revision)) throw new FormatException("Import conflict " + revision.Identity); continue; }
                normalizer.Normalize(revision); ((JArray)payload["records"]).Add(Record(revision));
            }
            var preferences = import.Preferences;
            if (capabilities.CanAssignDeviceRelease && payload["assignedReleaseRef"] == null && preferences["assignedReleaseRef"] != null && pub.Revisions.Any(r => r.Identity.Equals(RevisionIdentity.Read(preferences["assignedReleaseRef"]))))
                payload["assignedReleaseRef"] = preferences["assignedReleaseRef"].DeepClone();
            if (payload["lastStartedRef"] == null && preferences["lastStartedRef"] != null && View(new StoreRead(payload, before.Token), pub).Revisions.Any(r => r.Identity.Equals(RevisionIdentity.Read(preferences["lastStartedRef"]))))
                payload["lastStartedRef"] = preferences["lastStartedRef"].DeepClone();
            var receipt = new JObject { ["payloadBase64"] = Convert.ToBase64String(import.OriginalPayload) };
            if (import.OriginalStandaloneAi != null) receipt["standaloneAiBase64"] = Convert.ToBase64String(import.OriginalStandaloneAi);
            payload["receipts"][import.Receipt] = receipt;
            return Commit(before, payload, pub);
        }
    }
}
