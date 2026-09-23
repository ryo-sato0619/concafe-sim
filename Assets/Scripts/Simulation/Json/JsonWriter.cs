using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Concafe.Simulation.Json
{
    /// <summary>
    /// JSON を組み立てて文字列にする。セーブデータの書き出しに使う。
    ///
    /// JsonUtility も JsonConvert も使えない（不変条件 9）ので自前で持つ。
    /// 読み取り側の <see cref="JsonParser"/> と対になっている。
    ///
    /// **数値は往復できる形で書く。** 丸めた表記で書き出すと、セーブしてロードした
    /// だけでパラメータがわずかに動く。480 週を跨ぐゲームでその誤差が積もると、
    /// セーブの有無で結果が変わってしまう。
    /// </summary>
    public sealed class JsonWriter
    {
        readonly StringBuilder sb = new StringBuilder();
        readonly Stack<bool> hasMembers = new Stack<bool>();
        readonly bool pretty;
        int depth;

        public JsonWriter(bool pretty = true) { this.pretty = pretty; }

        /// <summary>配列の要素として、または最上位で開く。</summary>
        public JsonWriter BeginObject() { Separator(); return Open('{'); }
        public JsonWriter BeginArray() { Separator(); return Open('['); }

        /// <summary>名前付きのメンバとして開く。</summary>
        public JsonWriter BeginObject(string name) { Key(name); return Open('{'); }
        public JsonWriter BeginArray(string name) { Key(name); return Open('['); }

        public JsonWriter EndObject() => End('}');
        public JsonWriter EndArray() => End(']');

        public JsonWriter Value(string name, string v) { Key(name); WriteString(v); return this; }
        public JsonWriter Value(string name, bool v) { Key(name); sb.Append(v ? "true" : "false"); return this; }
        public JsonWriter Value(string name, long v) { Key(name); sb.Append(v.ToString(CultureInfo.InvariantCulture)); return this; }
        public JsonWriter Value(string name, int v) => Value(name, (long)v);
        public JsonWriter Value(string name, double v) { Key(name); WriteNumber(v); return this; }

        /// <summary>
        /// ulong をそのまま書く。乱数の状態など、double に入れると
        /// 精度が落ちる値のため。
        /// </summary>
        public JsonWriter Value(string name, ulong v)
        {
            Key(name);
            sb.Append(v.ToString(CultureInfo.InvariantCulture));
            return this;
        }

        /// <summary>配列の要素（キー無し）。</summary>
        public JsonWriter Item(string v) { Separator(); WriteString(v); return this; }
        public JsonWriter Item(long v) { Separator(); sb.Append(v.ToString(CultureInfo.InvariantCulture)); return this; }
        public JsonWriter Item(double v) { Separator(); WriteNumber(v); return this; }

        public override string ToString() => sb.ToString();

        /// <summary>
        /// 区切りは呼び出し側で済ませてから来ること。
        /// ここで <see cref="Separator"/> を呼ぶと、名前付きのメンバで
        /// 「"header": ,」のように区切りが二重に出る。
        /// </summary>
        JsonWriter Open(char open)
        {
            sb.Append(open);
            hasMembers.Push(false);
            depth++;
            return this;
        }

        JsonWriter End(char close)
        {
            depth--;
            bool any = hasMembers.Pop();
            if (any) NewLine();
            sb.Append(close);
            return this;
        }

        void Key(string name)
        {
            Separator();
            WriteString(name);
            sb.Append(pretty ? ": " : ":");
        }

        /// <summary>要素の区切り。直前に何か書いていればカンマを挟む。</summary>
        void Separator()
        {
            if (hasMembers.Count == 0) return;

            bool any = hasMembers.Pop();
            if (any) sb.Append(',');
            hasMembers.Push(true);
            NewLine();
        }

        void NewLine()
        {
            if (!pretty) return;
            sb.Append('\n');
            sb.Append(' ', depth * 2);
        }

        void WriteNumber(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v))
                throw new ArgumentException($"JSON に書けない数値: {v}", nameof(v));

            // R は往復を保証する書式。整数値でも指数表記にならないよう、
            // 小数部を持たない場合は普通の整数として書く。
            if (v == Math.Floor(v) && Math.Abs(v) < 1e15)
                sb.Append(((long)v).ToString(CultureInfo.InvariantCulture));
            else
                sb.Append(v.ToString("R", CultureInfo.InvariantCulture));
        }

        void WriteString(string v)
        {
            if (v == null) { sb.Append("null"); return; }

            sb.Append('"');
            foreach (char c in v)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        // 制御文字だけをエスケープする。日本語はそのまま書く
                        // （\u エスケープにすると人が読めなくなる）。
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }
}
