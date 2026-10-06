using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using Newtonsoft.Json.Linq;

namespace Spacewars.BalanceLab
{
    public sealed class StoreToken : IEquatable<StoreToken>
    {
        public long Generation { get; }
        public string Hash { get; }
        public StoreToken(long generation, string hash) { Generation = generation; Hash = hash; }
        public bool Equals(StoreToken other) => other != null && Generation == other.Generation && Hash == other.Hash;
        public override bool Equals(object other) => Equals(other as StoreToken);
        public override int GetHashCode() => Generation.GetHashCode() ^ Hash.GetHashCode();
    }
    public sealed class StoreRead
    {
        readonly JObject payload;
        public StoreToken Token { get; }
        public JObject Payload => (JObject)payload.DeepClone();
        internal StoreRead(JObject payload, StoreToken token) { this.payload = (JObject)payload.DeepClone(); Token = token; }
    }
    public sealed class StoreConflictException : IOException { public StoreConflictException() : base("Repository changed; reload before saving. Draft retained.") { } }
    public sealed class StoreCommitException : IOException
    {
        public bool ReplacementOccurred { get; }
        public StoreCommitException(bool replacementOccurred, Exception inner) : base("Write/readback failed; reload to reconcile. Draft retained.", inner) { ReplacementOccurred = replacementOccurred; }
    }
    /// <summary>Cooperating writers only. A noncooperating editor can race replacement; readback detects observed drift.</summary>
    public sealed class AtomicStore
    {
        static readonly ConcurrentDictionary<string, object> Gates = new ConcurrentDictionary<string, object>(StringComparer.Ordinal);
        readonly string path;
        readonly bool rawDocument;
        readonly Func<JObject> initial;
        readonly Action<string> checkpoint;
        public string Path => path;
        public AtomicStore(string absolutePath, Func<JObject> initialPayload, Action<string> checkpoint = null, bool rawDocument = false)
        { if (!System.IO.Path.IsPathRooted(absolutePath)) throw new ArgumentException("Absolute injected storage path required"); path = System.IO.Path.GetFullPath(absolutePath); initial = initialPayload; this.checkpoint = checkpoint; this.rawDocument = rawDocument; }
        internal static T Exclusive<T>(string absolutePath, Func<T> operation)
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(absolutePath));
            lock (Gates.GetOrAdd(absolutePath, _ => new object()))
            using (var writer = new FileStream(absolutePath + ".writer-lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite))
            { writer.Lock(0, 1); try { return operation(); } finally { writer.Unlock(0, 1); } }
        }
        public StoreRead Read()
        {
            byte[] bytes;
            try { bytes = File.ReadAllBytes(path); }
            catch (FileNotFoundException) { return new StoreRead(initial(), new StoreToken(0, "absent")); }
            catch (DirectoryNotFoundException) { return new StoreRead(initial(), new StoreToken(0, "absent")); }
            var doc = RevisionIdentity.Object(ContractCodec.Parse(bytes));
            if (!RevisionIdentity.Schema(doc["schemaVersion"], 1)) throw new FormatException("Unsupported state document");
            long generation = rawDocument && doc["generation"] == null ? 0 : RevisionIdentity.SafeInteger(doc["generation"]);
            return new StoreRead(rawDocument ? doc : RevisionIdentity.Object(doc["payload"]), new StoreToken(generation, ContractCodec.Hash(bytes)));
        }
        public StoreRead Commit(StoreToken expected, JObject payload)
        {
            if (expected == null) throw new ArgumentNullException(nameof(expected));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            lock (Gates.GetOrAdd(path, _ => new object()))
            using (var writer = new FileStream(path + ".writer-lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite))
            {
                // OS byte-range lock; OS releases it on process crash. Lockfile inode is never deleted.
                writer.Lock(0, 1);
                string temp = path + ".pending-" + Guid.NewGuid().ToString("N"); bool replaced = false;
                try
                {
                    checkpoint?.Invoke("locked");
                    var before = Read(); if (!before.Token.Equals(expected)) throw new StoreConflictException();
                    if (expected.Generation >= 9007199254740991L) throw new FormatException("Generation exhausted");
                    var document = rawDocument ? (JObject)payload.DeepClone() : new JObject { ["schemaVersion"] = 1, ["payload"] = payload.DeepClone() };
                    document["generation"] = expected.Generation + 1;
                    var bytes = ContractCodec.Utf8.GetBytes(ContractCodec.Write(document) + "\n");
                    checkpoint?.Invoke("before-write");
                    using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    { file.Write(bytes, 0, bytes.Length); checkpoint?.Invoke("before-flush"); file.Flush(true); }
                    checkpoint?.Invoke("flushed");
                    // Compare again to detect external edits observed during preparation.
                    if (!Read().Token.Equals(expected)) throw new StoreConflictException();
                    if (expected.Hash == "absent") File.Move(temp, path); else File.Replace(temp, path, null);
                    replaced = true; checkpoint?.Invoke("replaced");
                    var readback = Read();
                    if (readback.Token.Generation != expected.Generation + 1 || readback.Token.Hash != ContractCodec.Hash(bytes)) throw new IOException("Committed readback differs");
                    checkpoint?.Invoke("readback"); return readback;
                }
                catch (StoreConflictException) { throw; }
                catch (Exception ex) { throw new StoreCommitException(replaced, ex); }
                finally { if (File.Exists(temp)) File.Delete(temp); writer.Unlock(0, 1); }
            }
        }
        // Unfinished temps are never promoted. Only this store's pending files are cleaned under its lock.
        public void RecoverTemps()
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            lock (Gates.GetOrAdd(path, _ => new object()))
            using (var writer = new FileStream(path + ".writer-lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite))
            { writer.Lock(0, 1); try { Read(); foreach (var temp in Directory.GetFiles(System.IO.Path.GetDirectoryName(path), System.IO.Path.GetFileName(path) + ".pending-*")) File.Delete(temp); } finally { writer.Unlock(0, 1); } }
        }
    }
}
