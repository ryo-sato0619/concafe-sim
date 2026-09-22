using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Concafe.Simulation.Json
{
    /// <summary>
    /// 読み込んだ JSON の値。Simulation 層は UnityEngine を参照できないため
    /// （不変条件 9）、JsonUtility も JsonConvert も使えない。balance.json が
    /// 扱う範囲（オブジェクト・配列・数値・文字列・真偽・null）だけを持つ。
    /// </summary>
    public sealed class JsonValue
    {
        public enum Kind { Null, Bool, Number, String, Array, Object }

        public Kind Type { get; }

        readonly bool boolValue;
        readonly double numberValue;
        readonly string stringValue;
        readonly List<JsonValue> arrayValue;
        readonly Dictionary<string, JsonValue> objectValue;

        JsonValue(Kind type, bool b, double n, string s,
                  List<JsonValue> a, Dictionary<string, JsonValue> o)
        {
            Type = type;
            boolValue = b;
            numberValue = n;
            stringValue = s;
            arrayValue = a;
            objectValue = o;
        }

        public static readonly JsonValue Null =
            new JsonValue(Kind.Null, false, 0, null, null, null);

        public static JsonValue Bool(bool v) =>
            new JsonValue(Kind.Bool, v, 0, null, null, null);

        public static JsonValue Number(double v) =>
            new JsonValue(Kind.Number, false, v, null, null, null);

        public static JsonValue String(string v) =>
            new JsonValue(Kind.String, false, 0, v, null, null);

        public static JsonValue Array(List<JsonValue> v) =>
            new JsonValue(Kind.Array, false, 0, null, v, null);

        public static JsonValue Object(Dictionary<string, JsonValue> v) =>
            new JsonValue(Kind.Object, false, 0, null, null, v);

        public bool IsNull => Type == Kind.Null;

        public int Count =>
            Type == Kind.Array ? arrayValue.Count :
            Type == Kind.Object ? objectValue.Count : 0;

        /// <summary>配列の要素。範囲外は Null を返す（呼び出し側で潰さない）。</summary>
        public JsonValue this[int index] =>
            Type == Kind.Array && index >= 0 && index < arrayValue.Count
                ? arrayValue[index]
                : Null;

        /// <summary>オブジェクトのメンバ。無い場合は Null を返す。</summary>
        public JsonValue this[string key] =>
            Type == Kind.Object && objectValue.TryGetValue(key, out var v) ? v : Null;

        public bool Has(string key) =>
            Type == Kind.Object && objectValue.ContainsKey(key);

        public IEnumerable<JsonValue> Items()
        {
            if (Type != Kind.Array) yield break;
            foreach (var v in arrayValue) yield return v;
        }

        public IEnumerable<KeyValuePair<string, JsonValue>> Members()
        {
            if (Type != Kind.Object) yield break;
            foreach (var kv in objectValue) yield return kv;
        }

        public bool AsBool(bool fallback = false) =>
            Type == Kind.Bool ? boolValue : fallback;

        public double AsDouble(double fallback = 0) =>
            Type == Kind.Number ? numberValue : fallback;

        public string AsString(string fallback = null) =>
            Type == Kind.String ? stringValue : fallback;
    }

    public sealed class JsonParseException : Exception
    {
        public int Line { get; }
        public int Column { get; }

        public JsonParseException(string message, int line, int column)
            : base($"{message} ({line} 行 {column} 文字目)")
        {
            Line = line;
            Column = column;
        }
    }

    /// <summary>
    /// 最小の再帰下降 JSON パーサ。balance.json / easter_eggs.json / careers.json
    /// を読むためだけのもので、規格の完全な実装ではない。
    /// </summary>
    public static class JsonParser
    {
        public static JsonValue Parse(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));

            var state = new Cursor(text);
            state.SkipWhitespace();
            var value = ParseValue(ref state);
            state.SkipWhitespace();

            if (!state.AtEnd)
                throw state.Error("値の後ろに余分な文字がある");

            return value;
        }

        struct Cursor
        {
            public readonly string Text;
            public int Index;
            public int Line;
            public int Column;

            public Cursor(string text)
            {
                Text = text;
                Index = 0;
                Line = 1;
                Column = 1;
            }

            public bool AtEnd => Index >= Text.Length;
            public char Current => Text[Index];

            public char Advance()
            {
                char c = Text[Index++];
                if (c == '\n') { Line++; Column = 1; }
                else { Column++; }
                return c;
            }

            public void SkipWhitespace()
            {
                while (!AtEnd)
                {
                    char c = Current;
                    if (c == ' ' || c == '\t' || c == '\r' || c == '\n') Advance();
                    else break;
                }
            }

            public JsonParseException Error(string message) =>
                new JsonParseException(message, Line, Column);
        }

        static JsonValue ParseValue(ref Cursor c)
        {
            if (c.AtEnd) throw c.Error("値が来るべきところで入力が終わった");

            switch (c.Current)
            {
                case '{': return ParseObject(ref c);
                case '[': return ParseArray(ref c);
                case '"': return JsonValue.String(ParseString(ref c));
                case 't': ParseLiteral(ref c, "true"); return JsonValue.Bool(true);
                case 'f': ParseLiteral(ref c, "false"); return JsonValue.Bool(false);
                case 'n': ParseLiteral(ref c, "null"); return JsonValue.Null;
                default: return JsonValue.Number(ParseNumber(ref c));
            }
        }

        static JsonValue ParseObject(ref Cursor c)
        {
            c.Advance(); // '{'
            var map = new Dictionary<string, JsonValue>();

            c.SkipWhitespace();
            if (!c.AtEnd && c.Current == '}') { c.Advance(); return JsonValue.Object(map); }

            while (true)
            {
                c.SkipWhitespace();
                if (c.AtEnd || c.Current != '"') throw c.Error("キーの文字列が必要");

                string key = ParseString(ref c);
                if (map.ContainsKey(key)) throw c.Error($"キー \"{key}\" が重複している");

                c.SkipWhitespace();
                if (c.AtEnd || c.Current != ':') throw c.Error("キーの後に ':' が必要");
                c.Advance();

                c.SkipWhitespace();
                map[key] = ParseValue(ref c);

                c.SkipWhitespace();
                if (c.AtEnd) throw c.Error("オブジェクトが閉じていない");

                if (c.Current == ',') { c.Advance(); continue; }
                if (c.Current == '}') { c.Advance(); break; }
                throw c.Error("',' か '}' が必要");
            }

            return JsonValue.Object(map);
        }

        static JsonValue ParseArray(ref Cursor c)
        {
            c.Advance(); // '['
            var list = new List<JsonValue>();

            c.SkipWhitespace();
            if (!c.AtEnd && c.Current == ']') { c.Advance(); return JsonValue.Array(list); }

            while (true)
            {
                c.SkipWhitespace();
                list.Add(ParseValue(ref c));

                c.SkipWhitespace();
                if (c.AtEnd) throw c.Error("配列が閉じていない");

                if (c.Current == ',') { c.Advance(); continue; }
                if (c.Current == ']') { c.Advance(); break; }
                throw c.Error("',' か ']' が必要");
            }

            return JsonValue.Array(list);
        }

        static string ParseString(ref Cursor c)
        {
            c.Advance(); // '"'
            var sb = new StringBuilder();

            while (true)
            {
                if (c.AtEnd) throw c.Error("文字列が閉じていない");
                char ch = c.Advance();

                if (ch == '"') break;

                if (ch != '\\') { sb.Append(ch); continue; }

                if (c.AtEnd) throw c.Error("エスケープが閉じていない");
                char esc = c.Advance();
                switch (esc)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        sb.Append(ParseUnicodeEscape(ref c));
                        break;
                    default:
                        throw c.Error($"未知のエスケープ '\\{esc}'");
                }
            }

            return sb.ToString();
        }

        static char ParseUnicodeEscape(ref Cursor c)
        {
            int value = 0;
            for (int i = 0; i < 4; i++)
            {
                if (c.AtEnd) throw c.Error("\\u の桁数が足りない");
                char h = c.Advance();
                int digit;
                if (h >= '0' && h <= '9') digit = h - '0';
                else if (h >= 'a' && h <= 'f') digit = h - 'a' + 10;
                else if (h >= 'A' && h <= 'F') digit = h - 'A' + 10;
                else throw c.Error($"'{h}' は 16 進数ではない");
                value = value * 16 + digit;
            }
            return (char)value;
        }

        static double ParseNumber(ref Cursor c)
        {
            int start = c.Index;

            if (!c.AtEnd && c.Current == '-') c.Advance();

            bool sawDigit = false;
            while (!c.AtEnd && c.Current >= '0' && c.Current <= '9') { c.Advance(); sawDigit = true; }

            if (!c.AtEnd && c.Current == '.')
            {
                c.Advance();
                while (!c.AtEnd && c.Current >= '0' && c.Current <= '9') { c.Advance(); sawDigit = true; }
            }

            if (!sawDigit) throw c.Error("数値として読めない");

            if (!c.AtEnd && (c.Current == 'e' || c.Current == 'E'))
            {
                c.Advance();
                if (!c.AtEnd && (c.Current == '+' || c.Current == '-')) c.Advance();

                bool sawExpDigit = false;
                while (!c.AtEnd && c.Current >= '0' && c.Current <= '9') { c.Advance(); sawExpDigit = true; }
                if (!sawExpDigit) throw c.Error("指数部の桁が無い");
            }

            string slice = c.Text.Substring(start, c.Index - start);

            // InvariantCulture を明示する。ロケールによっては '.' が
            // 小数点として扱われず、balance.json が丸ごと壊れて読まれる。
            if (!double.TryParse(slice, NumberStyles.Float, CultureInfo.InvariantCulture, out double result))
                throw c.Error($"数値 \"{slice}\" を解釈できない");

            return result;
        }

        static void ParseLiteral(ref Cursor c, string literal)
        {
            foreach (char expected in literal)
            {
                if (c.AtEnd || c.Current != expected)
                    throw c.Error($"\"{literal}\" が必要");
                c.Advance();
            }
        }
    }
}
