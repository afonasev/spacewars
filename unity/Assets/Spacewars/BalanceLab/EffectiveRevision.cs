using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Spacewars.BalanceLab
{
    public sealed class EffectiveRevision
    {
        readonly JObject effective;
        public bool HasRuntimeProjection => false;
        public string NormalizerVersion { get; }
        public string RulesFingerprint { get; }
        public string RawFingerprint { get; }
        public string EffectiveFingerprint { get; }
        public JObject Snapshot => (JObject)effective.DeepClone();
        internal EffectiveRevision(CanonicalRevision raw, JObject value, string version, string rules)
        { effective = (JObject)value.DeepClone(); NormalizerVersion = version; RulesFingerprint = rules; RawFingerprint = raw.RawFingerprint; EffectiveFingerprint = ContractCodec.Fingerprint(effective); }
    }
    /// <summary>Versioned web normalizer recipe and metadata injected by a trusted adapter. No simulation consumers.</summary>
    public sealed class RevisionNormalizer
    {
        readonly JObject rules, metadata, catalogs;
        public string Fingerprint { get; }
        public RevisionNormalizer(byte[] rulesBytes, string expectedHash, JObject metadata, JObject catalogs)
        {
            if (ContractCodec.Hash(rulesBytes) != expectedHash) throw new FormatException("Untrusted normalizer rules");
            rules = RevisionIdentity.Object(ContractCodec.Parse(rulesBytes));
            if (!RevisionIdentity.Schema(rules["schemaVersion"], 1) || (string)rules["normalizerVersion"] != "web-u8-v1") throw new FormatException("Unsupported normalizer");
            if ((string)rules["metadataSha256"] != ContractCodec.Fingerprint(metadata)) throw new FormatException("Normalizer metadata drift");
            this.metadata = (JObject)metadata.DeepClone(); this.catalogs = (JObject)catalogs.DeepClone(); Fingerprint = expectedHash;
        }
        public EffectiveRevision Normalize(CanonicalRevision raw)
        {
            var result = raw.Snapshot;
            result["profile"] = Gameplay(RevisionIdentity.Object(result["profile"]));
            result["aiProfile"] = Ai(RevisionIdentity.Object(result["aiProfile"]));
            Validate(result);
            return new EffectiveRevision(raw, result, (string)rules["normalizerVersion"], Fingerprint);
        }
        internal JObject SavedSnapshot(CanonicalRevision raw)
        {
            var normalized = Normalize(raw).Snapshot;
            // Effective web recipes enumerate known collections. Saved authoring snapshots
            // retain opaque future members as well; they do not acquire runtime semantics.
            var saved = Overlay(raw.Snapshot, normalized);
            // These are recognized migration aliases, not unknown contract fields. Their
            // supported values have moved into canonical units; the source bytes stay immutable.
            ((JObject)saved["profile"]).Remove("tank"); ((JObject)saved["profile"]).Remove("headquarters");
            return saved;
        }
        static JObject Overlay(JObject raw, JObject known)
        {
            var saved = (JObject)raw.DeepClone();
            foreach (var p in known.Properties()) saved[p.Name] = saved[p.Name] is JObject old && p.Value is JObject next ? Overlay(old, next) : p.Value.DeepClone();
            return saved;
        }
        internal JObject DefaultAi => (JObject)rules["defaultAi"].DeepClone();
        internal JObject DefaultAiRef => (JObject)rules["defaultAiRef"].DeepClone();
        static JObject Merge(JToken defaults, JToken source)
        {
            var o = (JObject)defaults.DeepClone();
            if (source != null) { if (!(source is JObject current)) throw new FormatException("Invalid explicit object"); foreach (var p in current.Properties()) o[p.Name] = p.Value.DeepClone(); }
            return o;
        }
        static void Types(JToken defaults, JToken source)
        {
            if (source == null) return;
            if (defaults is JObject o)
            { if (!(source is JObject s)) throw new FormatException("Invalid explicit object"); foreach (var p in o.Properties()) Types(p.Value, s[p.Name]); return; }
            var numeric = defaults.Type == JTokenType.Float || defaults.Type == JTokenType.Integer;
            if (numeric ? source.Type != JTokenType.Float && source.Type != JTokenType.Integer : defaults.Type != source.Type) throw new FormatException("Invalid explicit type");
        }
        JObject Gameplay(JObject s)
        {
            var d = RevisionIdentity.Object(rules["gameplay"]); Types(d, s);
            var p = Merge(d, s);
            foreach (var path in new[] { "economy", "construction", "army", "baseGeneration", "map", "system" }) p[path] = Merge(d[path], s[path]);
            foreach (var path in new[] { "groupMovement", "network", "teamCommunication", "minimap", "gamepad", "follow", "hudScale" }) p["system"][path] = Merge(d["system"][path], s["system"]?[path]);
            foreach (var path in new[] { "buildings", "capture", "entityText", "buildingUpgrades", "units", "unitUpgrades" })
            {
                var collection = new JObject();
                foreach (var entry in ((JObject)d[path]).Properties()) collection[entry.Name] = Merge(entry.Value, s[path]?[entry.Name]);
                p[path] = collection;
            }
            foreach (var kind in new[] { "tank", "explorer", "shkval" })
            {
                var unit = (JObject)p["units"][kind]; var current = s["units"]?[kind];
                unit["weapon"] = Merge(d["units"][kind]["weapon"], current?["weapon"]);
                if (current?["productionDurationSec"] == null)
                    unit["productionDurationSec"] = (kind == "tank" ? s["army"]?["factoryProductionSec"] : kind == "explorer" ? s["headquarters"]?["productionDurationSec"] : null)?.DeepClone() ?? d["units"][kind]["productionDurationSec"].DeepClone();
                if (kind == "tank" && s["tank"] != null)
                {
                    var legacy = RevisionIdentity.Object(s["tank"]);
                    foreach (var entry in legacy.Properties()) if (!new[] { "damage", "fireIntervalMs", "projectileSpeed", "weapon", "productionDurationSec" }.Contains(entry.Name)) unit[entry.Name] = entry.Value.DeepClone();
                    foreach (var key in new[] { "damage", "fireIntervalMs", "projectileSpeed" }) if (legacy[key] != null) unit["weapon"][key] = legacy[key].DeepClone();
                }
            }
            var guns = (JObject)p["unitUpgrades"]["explorerAssaultGuns"];
            foreach (var key in new[] { "baseBurstSize", "baseSpreadDeg" }) if (s["unitUpgrades"]?["explorerAssaultGuns"]?[key] == null) guns[key] = p["units"]["explorer"]["weapon"][key == "baseBurstSize" ? "burstSize" : "spreadDeg"].DeepClone();
            p.Remove("tank"); p.Remove("headquarters"); Types(d, p); return p;
        }
        internal JObject Ai(JObject s)
        {
            var d = RevisionIdentity.Object(rules["ai"]); Types(d, s); var a = Merge(d, s);
            foreach (var path in new[] { "openings", "midgame", "artillery", "economy", "liveness", "scouting", "economicScouting", "utility", "tactics", "evaluation" }) a[path] = Merge(d[path], s[path]);
            foreach (var path in new[] { "difficulties", "personality" })
            {
                var collection = new JObject(); foreach (var entry in ((JObject)d[path]).Properties()) collection[entry.Name] = Merge(entry.Value, s[path]?[entry.Name]); a[path] = collection;
            }
            foreach (var path in new[] { "openings.weights", "openings.deadlinesSec", "midgame.weights", "midgame.transitionCosts", "economy.expansion" }) Set(a, path, Merge(Get(d, path), Get(s, path)));
            var weights = (JObject)a["openings"]["weights"];
            // Web weights explicitly enumerate known openings. Legacy aggression maps only when blind-rush is absent.
            foreach (var key in weights.Properties().Select(v => v.Name).ToArray()) if (d["openings"]["weights"][key] == null) weights.Remove(key);
            if (s["openings"]?["weights"]?["blind-rush"] == null && s["openings"]?["weights"]?["aggression"] != null) weights["blind-rush"] = s["openings"]["weights"]["aggression"].DeepClone();
            var readiness = new JObject();
            foreach (var entry in ((JObject)d["midgame"]["openingReadiness"]).Properties())
            { var r = Merge(entry.Value, s["midgame"]?["openingReadiness"]?[entry.Name]); if (entry.Name == "blind-rush" && s["midgame"]?["openingReadiness"]?["aggression"] != null) r = Merge(r, s["midgame"]["openingReadiness"]["aggression"]); readiness[entry.Name] = r; }
            a["midgame"]["openingReadiness"] = readiness; Types(d, a); return a;
        }
        internal static JToken Get(JToken root, string path) { foreach (var k in path.Split('.')) root = (root as JObject)?[k]; return root; }
        internal static void Set(JObject root, string path, JToken value) { var parts = path.Split('.'); JToken p = root; foreach (var k in parts.Take(parts.Length - 1)) p = p[k]; p[parts.Last()] = value; }
        internal void ValidateAiSnapshot(JObject ai) => Validate(new JObject { ["profile"] = rules["gameplay"].DeepClone(), ["aiProfile"] = ai.DeepClone() });
        void Validate(JObject revision)
        {
            foreach (var domain in new[] { "gameplay", "ai" })
            {
                var root = revision[domain == "gameplay" ? "profile" : "aiProfile"];
                foreach (JObject f in metadata[domain])
                {
                    string path = (string)f["path"]; var v = Get(root, path);
                    if ((string)f["kind"] == "select")
                    { if (!((JArray)f["options"]).Any(o => JToken.DeepEquals(o["value"], v))) throw new FormatException("Invalid option " + path); continue; }
                    if (v == null || v.Type != JTokenType.Float && v.Type != JTokenType.Integer) throw new FormatException("Invalid number " + path);
                    double n = (double)v;
                    if (n < (double)f["min"] || n > (double)f["max"] || double.IsNaN(n) || double.IsInfinity(n)) throw new FormatException("Range " + path);
                    bool integer = domain == "ai" && ((JArray)rules["integerAiPaths"]).Any(x => (string)x == path);
                    if (integer && n != Math.Truncate(n)) throw new FormatException("Integer " + path);
                    bool step = domain == "gameplay" && (((JArray)rules["stepPrefixes"]).Any(x => path.StartsWith((string)x, StringComparison.Ordinal)) || ((JArray)rules["stepPaths"]).Any(x => path == (string)x));
                    double position = (n - (double)f["min"]) / (double)f["step"];
                    if (step && Math.Abs(position - Math.Floor(position + .5)) > 1e-8) throw new FormatException("Step " + path);
                }
                var env = new Dictionary<string, JToken> { ["normalized"] = revision["profile"], ["profile"] = revision["aiProfile"], ["m"] = revision["profile"]["map"], ["tank"] = revision["profile"]["units"]["tank"], ["difficulty"] = revision["aiProfile"]["difficulties"] };
                foreach (JObject rule in rules["constraints"]) if ((string)rule["domain"] == domain) CheckRule(rule, env, 0);
            }
        }
        void CheckRule(JObject rule, Dictionary<string, JToken> env, int level)
        {
            var loops = (JArray)rule["loops"];
            if (level < loops.Count)
            {
                var loop = loops[level]; foreach (var item in (JArray)Eval(loop["values"], env))
                { var next = new Dictionary<string, JToken>(env); var names = (JArray)loop["names"]; for (int j = 0; j < names.Count; j++) next[(string)names[j]] = names.Count == 1 ? item : item[j]; CheckRule(rule, next, level + 1); } return;
            }
            if (((JArray)rule["guards"]).All(g => Truth(Eval(g, env))) && Truth(Eval(rule["condition"], env))) throw new FormatException("Web constraint: " + (string)rule["message"]);
        }
        static bool Truth(JToken t) => t != null && t.Type != JTokenType.Null && (t.Type == JTokenType.Boolean ? (bool)t : t.Type == JTokenType.String ? ((string)t).Length != 0 : t.Type == JTokenType.Float || t.Type == JTokenType.Integer ? (double)t != 0 : true);
        JToken Eval(JToken x, Dictionary<string, JToken> env)
        {
            string op = (string)x["op"];
            switch (op)
            {
                case "literal": return x["value"];
                case "var": if ((string)x["name"] == "undefined") return null; return env[(string)x["name"]];
                case "get": return (Eval(x["base"], env) as JObject)?[(string)x["key"]];
                case "index": return Eval(x["base"], env)?[(string)Eval(x["key"], env)];
                case "array": return new JArray(((JArray)x["items"]).Select(v => Eval(v, env)));
                case "!": return new JValue(!Truth(Eval(x["arg"], env)));
                case "&&": return new JValue(Truth(Eval(x["left"], env)) && Truth(Eval(x["right"], env)));
                case "||": return new JValue(Truth(Eval(x["left"], env)) || Truth(Eval(x["right"], env)));
                case "??": return Eval(x["left"], env) ?? Eval(x["right"], env);
                case "method":
                    var target = Eval(x["base"], env); string method = (string)x["name"];
                    if (method == "trim") return new JValue(((string)target).Trim());
                    var lambda = x["args"][0]; var results = ((JArray)target).Select(v => { var scope = new Dictionary<string, JToken>(env) { [(string)lambda["name"]] = v }; return Truth(Eval(lambda["body"], scope)); });
                    return new JValue(method == "some" ? results.Any(v => v) : results.All(v => v));
                case "call":
                    string call = (string)x["name"]; var arg = Eval(x["args"][0], env);
                    if (call == "Number.isInteger") return new JValue(arg != null && (arg.Type == JTokenType.Float || arg.Type == JTokenType.Integer) && (double)arg == Math.Truncate((double)arg));
                    if (call == "Number.isFinite") return new JValue(arg != null && (arg.Type == JTokenType.Float || arg.Type == JTokenType.Integer) && !double.IsNaN((double)arg) && !double.IsInfinity((double)arg));
                    if (call == "numeric") return new JArray(Numbers(arg));
                    if (call == "Object.values") return new JArray(((JObject)arg).Properties().Select(p => p.Value));
                    if (call == "Object.entries") return new JArray(((JObject)arg).Properties().Select(p => new JArray(p.Name, p.Value)));
                    if (call == "isProjectileTypeId") return new JValue(((JArray)catalogs["projectileTypes"]).Any(p => (string)p["id"] == (string)arg));
                    throw new FormatException("Unsupported rule call");
            }
            var l = Eval(x["left"], env); var r = Eval(x["right"], env);
            if (op == "===") return new JValue(JToken.DeepEquals(l, r));
            if (op == "!==") return new JValue(!JToken.DeepEquals(l, r));
            double left = l == null ? double.NaN : (double)l, right = r == null ? double.NaN : (double)r;
            switch (op)
            { case "+": return new JValue(left + right); case "-": return new JValue(left - right); case "*": return new JValue(left * right); case "/": return new JValue(left / right); case "%": return new JValue(left % right); case ">": return new JValue(left > right); case "<": return new JValue(left < right); case ">=": return new JValue(left >= right); case "<=": return new JValue(left <= right); default: throw new FormatException("Unsupported rule operator " + op); }
        }
        static IEnumerable<JToken> Numbers(JToken t)
        { if (t.Type == JTokenType.Float || t.Type == JTokenType.Integer) yield return t; else if (t is JContainer c) foreach (var v in c.Children()) foreach (var n in Numbers(v is JProperty p ? p.Value : v)) yield return n; }
    }
}
