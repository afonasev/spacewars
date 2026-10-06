using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Spacewars.BalanceLab
{
    /// <summary>DOM only; never materializes input types. U8 Object.keys().sort()/JSON.stringify contract.</summary>
    public static class ContractCodec
    {
        internal static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        public static string Hash(byte[] bytes)
        { using (var sha = SHA256.Create()) return string.Concat(sha.ComputeHash(bytes).Select(b => b.ToString("x2"))); }
        public static string Fingerprint(JToken value) => Hash(Utf8.GetBytes(Write(value)));
        public static JToken Parse(byte[] bytes) => Parse(Utf8.GetString(bytes));
        public static JToken Parse(string text)
        {
            // Json.NET accepts non-JSON syntax and replaces lone escaped surrogates. A grammar
            // preflight substitutes strings, preserving every UTF16 code unit in a side table.
            var scan = new Grammar(text);
            var safe = scan.Run();
            using (var reader = new JsonTextReader(new StringReader(safe))
                { DateParseHandling = DateParseHandling.None, FloatParseHandling = FloatParseHandling.Double, MaxDepth = 128 })
            {
                var tree = JToken.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                if (reader.Read()) throw new FormatException("Trailing JSON content");
                return Restore(tree, scan.Strings, scan.Numbers);
            }
        }
        static JToken Restore(JToken t, List<string> strings, List<string> numbers)
        {
            if (t is JObject o)
            {
                var result = new JObject();
                foreach (var p in o.Properties()) result.Add(strings[int.Parse(p.Name, CultureInfo.InvariantCulture)], Restore(p.Value, strings, numbers));
                return result;
            }
            if (t is JArray a) return new JArray(a.Select(v => Restore(v, strings, numbers)));
            if (t.Type == JTokenType.String) return new JValue(strings[int.Parse((string)t, CultureInfo.InvariantCulture)]);
            if (t.Type == JTokenType.Integer || t.Type == JTokenType.Float)
            {
                return new JValue(ParseNumber(numbers[(int)t]));
            }
            return t.DeepClone();
        }
        public static string Write(JToken tree) { var b = new StringBuilder(); Write(tree, b); return b.ToString(); }
        static void Write(JToken t, StringBuilder b)
        {
            if (t is JObject o)
            {
                b.Append('{'); bool comma = false;
                foreach (var p in o.Properties().OrderBy(p => p.Name, StringComparer.Ordinal))
                { if (comma) b.Append(','); comma = true; Quote(p.Name, b); b.Append(':'); Write(p.Value, b); }
                b.Append('}');
            }
            else if (t is JArray a)
            { b.Append('['); for (int i = 0; i < a.Count; i++) { if (i > 0) b.Append(','); Write(a[i], b); } b.Append(']'); }
            else switch (t.Type)
            {
                case JTokenType.String: Quote((string)t, b); break;
                case JTokenType.Null: b.Append("null"); break;
                case JTokenType.Boolean: b.Append((bool)t ? "true" : "false"); break;
                case JTokenType.Integer:
                case JTokenType.Float:
                    var v = ((JValue)t).Value;
                    b.Append(Number(v is BigInteger big ? (double)big : Convert.ToDouble(v, CultureInfo.InvariantCulture))); break;
                default: throw new FormatException("Non-JSON token " + t.Type);
            }
        }
        static void Quote(string s, StringBuilder b)
        {
            b.Append('"');
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                switch (c)
                {
                    case '"': b.Append("\\\""); break;
                    case '\\': b.Append("\\\\"); break;
                    case '\b': b.Append("\\b"); break;
                    case '\f': b.Append("\\f"); break;
                    case '\n': b.Append("\\n"); break;
                    case '\r': b.Append("\\r"); break;
                    case '\t': b.Append("\\t"); break;
                    default:
                        if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
                        { b.Append(c); b.Append(s[++i]); }
                        else if (c < 32 || char.IsSurrogate(c)) b.Append("\\u").Append(((int)c).ToString("x4"));
                        else b.Append(c);
                        break;
                }
            }
            b.Append('"');
        }
        // Exact decimal -> binary64 rounding. Json.NET supplies the DOM; this lexical
        // conversion avoids Mono-specific Parse rounding, including subnormal numbers.
        static double ParseNumber(string text)
        {
            bool negative = text[0] == '-'; if (negative) text = text.Substring(1);
            int eIndex = text.IndexOfAny(new[] { 'e', 'E' });
            string mantissa = eIndex < 0 ? text : text.Substring(0, eIndex);
            int point = mantissa.IndexOf('.'); int fraction = point < 0 ? 0 : mantissa.Length - point - 1;
            string digits = mantissa.Replace(".", "").TrimStart('0');
            if (digits.Length == 0) return BitConverter.Int64BitsToDouble(negative ? long.MinValue : 0);
            string expText = eIndex < 0 ? "0" : text.Substring(eIndex + 1);
            if (!long.TryParse(expText, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var exponent))
            { if (expText[0] == '-') return BitConverter.Int64BitsToDouble(negative ? long.MinValue : 0); throw new FormatException("Number overflow"); }
            if (exponent > 1000000) throw new FormatException("Number overflow");
            if (exponent < -1000000) return BitConverter.Int64BitsToDouble(negative ? long.MinValue : 0);
            long scaleLong = exponent - fraction, magnitude = digits.Length + scaleLong;
            if (magnitude > 309) throw new FormatException("Number overflow");
            if (magnitude < -324) return BitConverter.Int64BitsToDouble(negative ? long.MinValue : 0);
            int scale = checked((int)scaleLong);
            BigInteger n = BigInteger.Parse(digits, CultureInfo.InvariantCulture), d = BigInteger.One;
            if (scale >= 0) n *= BigInteger.Pow(10, scale); else d = BigInteger.Pow(10, -scale);
            int power = BitLength(n) - BitLength(d);
            if (power >= 0 ? n < (d << power) : (n << -power) < d) power--;
            if (power > 1023) throw new FormatException("Number overflow");
            int shift = power < -1022 ? 1074 : 52 - power;
            BigInteger sn = shift >= 0 ? n << shift : n, sd = shift < 0 ? d << -shift : d;
            var q = BigInteger.DivRem(sn, sd, out var remainder); int compare = (2 * remainder).CompareTo(sd);
            if (compare > 0 || (compare == 0 && !q.IsEven)) q++;
            long bits;
            if (power < -1022) bits = (long)q;
            else
            {
                if (q == (BigInteger.One << 53)) { q >>= 1; power++; }
                if (power > 1023) throw new FormatException("Number overflow");
                bits = ((long)(power + 1023) << 52) | ((long)q & 0xfffffffffffffL);
            }
            if (negative) bits |= long.MinValue;
            return BitConverter.Int64BitsToDouble(bits);
        }
        static int BitLength(BigInteger value)
        { var bytes = value.ToByteArray(); int last = bytes.Length - 1; while (last > 0 && bytes[last] == 0) last--; int bits = last * 8; byte b = bytes[last]; while (b > 0) { bits++; b >>= 1; } return bits; }
        // Exact rational shortest-decimal search. Binary64 midpoint inclusion follows the
        // even significand rule; no dependency on Mono's G/R formatter or locale.
        public static string Number(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) throw new FormatException("Nonfinite number");
            if (value == 0) return "0";
            string sign = value < 0 ? "-" : ""; value = Math.Abs(value);
            long bits = BitConverter.DoubleToInt64Bits(value);
            Rational(bits, out var n, out var d);
            Rational(bits - 1, out var pn, out var pd);
            BigInteger nn, nd;
            if (bits == 0x7fefffffffffffffL) { nn = BigInteger.One << 1024; nd = BigInteger.One; }
            else Rational(bits + 1, out nn, out nd);
            var lowN = n * pd + pn * d; var lowD = 2 * d * pd;
            var highN = n * nd + nn * d; var highD = 2 * d * nd;
            bool inclusive = (bits & 1) == 0;
            int exponent = (int)Math.Floor(Math.Log10(value));
            while (ComparePower(n, d, exponent) < 0) exponent--;
            while (ComparePower(n, d, exponent + 1) >= 0) exponent++;
            for (int precision = 1; precision <= 17; precision++)
            {
                int scale = exponent - precision + 1;
                var sn = scale < 0 ? n * BigInteger.Pow(10, -scale) : n;
                var sd = scale > 0 ? d * BigInteger.Pow(10, scale) : d;
                var q = BigInteger.DivRem(sn, sd, out var rem);
                int cmp = (2 * rem).CompareTo(sd);
                if (cmp > 0 || (cmp == 0 && !q.IsEven)) q++;
                BigInteger? chosen = null; BigInteger distance = 0;
                foreach (var candidate in new[] { q, q - 1, q + 1 })
                {
                    if (candidate <= 0) continue;
                    var cn = scale > 0 ? candidate * BigInteger.Pow(10, scale) : candidate;
                    var cd = scale < 0 ? BigInteger.Pow(10, -scale) : BigInteger.One;
                    int lo = (cn * lowD).CompareTo(lowN * cd), hi = (cn * highD).CompareTo(highN * cd);
                    if (lo < 0 || hi > 0 || (!inclusive && (lo == 0 || hi == 0))) continue;
                    var delta = BigInteger.Abs(candidate * sd - sn);
                    if (!chosen.HasValue || delta < distance || (delta == distance && candidate.IsEven)) { chosen = candidate; distance = delta; }
                }
                if (!chosen.HasValue) continue;
                var digits = chosen.Value.ToString(CultureInfo.InvariantCulture);
                while (digits.Length > 1 && digits.EndsWith("0", StringComparison.Ordinal)) { digits = digits.Substring(0, digits.Length - 1); scale++; }
                int point = digits.Length + scale;
                if (point > 0 && point <= 21) return sign + (point >= digits.Length ? digits + new string('0', point - digits.Length) : digits.Insert(point, "."));
                if (point <= 0 && point > -6) return sign + "0." + new string('0', -point) + digits;
                int e = point - 1;
                return sign + digits[0] + (digits.Length > 1 ? "." + digits.Substring(1) : "") + "e" + (e >= 0 ? "+" : "") + e.ToString(CultureInfo.InvariantCulture);
            }
            throw new FormatException("Cannot encode binary64");
        }
        static int ComparePower(BigInteger n, BigInteger d, int e) => e >= 0 ? n.CompareTo(d * BigInteger.Pow(10, e)) : (n * BigInteger.Pow(10, -e)).CompareTo(d);
        static void Rational(long bits, out BigInteger n, out BigInteger d)
        {
            int e = (int)((bits >> 52) & 2047);
            n = bits & 0xfffffffffffffL;
            if (e != 0) n += BigInteger.One << 52;
            int power = e == 0 ? -1074 : e - 1075;
            if (power >= 0) { n <<= power; d = BigInteger.One; } else d = BigInteger.One << -power;
        }
        sealed class Grammar
        {
            readonly string text; int i; readonly StringBuilder safe = new StringBuilder();
            public readonly List<string> Strings = new List<string>();
            public readonly List<string> Numbers = new List<string>();
            public Grammar(string text) { this.text = text ?? throw new ArgumentNullException(nameof(text)); }
            void Space() { while (i < text.Length && " \t\r\n".IndexOf(text[i]) >= 0) i++; }
            void Expect(char c) { Space(); if (i == text.Length || text[i++] != c) throw new FormatException("Expected " + c); safe.Append(c); }
            public string Run() { Value(0); Space(); if (i != text.Length) throw new FormatException("Trailing JSON"); return safe.ToString(); }
            string String()
            {
                Space(); if (i == text.Length || text[i++] != '"') throw new FormatException("Expected string");
                var b = new StringBuilder(); bool closed = false;
                while (i < text.Length)
                {
                    char c = text[i++]; if (c == '"') { closed = true; break; }
                    if (c < 32) throw new FormatException("Control character");
                    if (c == '\\')
                    {
                        if (i == text.Length) throw new FormatException("Escape"); c = text[i++];
                        switch (c)
                        {
                            case '"': case '\\': case '/': break;
                            case 'b': c = '\b'; break; case 'f': c = '\f'; break; case 'n': c = '\n'; break; case 'r': c = '\r'; break; case 't': c = '\t'; break;
                            case 'u':
                                if (i + 4 > text.Length || !ushort.TryParse(text.Substring(i, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var code)) throw new FormatException("Unicode escape");
                                c = (char)code; i += 4; break;
                            default: throw new FormatException("Escape");
                        }
                    }
                    b.Append(c);
                }
                if (!closed) throw new FormatException("Unterminated string");
                safe.Append('"').Append(Strings.Count).Append('"'); var s = b.ToString(); Strings.Add(s); return s;
            }
            void Value(int depth)
            {
                if (depth > 128) throw new FormatException("JSON depth"); Space();
                if (i == text.Length) throw new FormatException("Missing value");
                char c = text[i];
                if (c == '"') { String(); return; }
                if (c == '{' || c == '[')
                {
                    bool obj = c == '{'; char end = obj ? '}' : ']'; i++; safe.Append(c); Space();
                    var keys = new HashSet<string>(StringComparer.Ordinal);
                    if (i < text.Length && text[i] == end) { i++; safe.Append(end); return; }
                    while (true)
                    {
                        if (obj) { if (!keys.Add(String())) throw new FormatException("Duplicate JSON key"); Expect(':'); }
                        Value(depth + 1); Space();
                        if (i < text.Length && text[i] == end) { i++; safe.Append(end); return; }
                        Expect(',');
                    }
                }
                foreach (var literal in new[] { "true", "false", "null" })
                    if (text.Substring(i).StartsWith(literal, StringComparison.Ordinal)) { i += literal.Length; safe.Append(literal); return; }
                int start = i;
                if (text[i] == '-') i++;
                if (i == text.Length) throw new FormatException("Number");
                if (text[i] == '0') i++;
                else { if (text[i] < '1' || text[i] > '9') throw new FormatException("Number"); Digits(); }
                if (i < text.Length && text[i] == '.') { i++; RequiredDigits(); }
                if (i < text.Length && (text[i] == 'e' || text[i] == 'E')) { i++; if (i < text.Length && (text[i] == '+' || text[i] == '-')) i++; RequiredDigits(); }
                Numbers.Add(text.Substring(start, i - start)); safe.Append(Numbers.Count - 1);
            }
            void Digits() { while (i < text.Length && text[i] >= '0' && text[i] <= '9') i++; }
            void RequiredDigits() { int start = i; Digits(); if (i == start) throw new FormatException("Number digits"); }
        }
    }
}
