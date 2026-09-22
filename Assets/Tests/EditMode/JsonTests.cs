using Concafe.Simulation.Json;
using NUnit.Framework;

namespace Concafe.Tests
{
    public class JsonTests
    {
        [Test]
        public void オブジェクトと配列を読める()
        {
            var v = JsonParser.Parse(@"{ ""a"": 1, ""b"": [true, false, null], ""c"": ""x"" }");

            Assert.AreEqual(JsonValue.Kind.Object, v.Type);
            Assert.AreEqual(1.0, v["a"].AsDouble());
            Assert.AreEqual(3, v["b"].Count);
            Assert.IsTrue(v["b"][0].AsBool());
            Assert.IsFalse(v["b"][1].AsBool());
            Assert.IsTrue(v["b"][2].IsNull);
            Assert.AreEqual("x", v["c"].AsString());
        }

        [Test]
        public void 無いキーと範囲外の添字は_Null_を返す()
        {
            var v = JsonParser.Parse(@"{ ""a"": [1] }");

            Assert.IsTrue(v["zzz"].IsNull);
            Assert.IsTrue(v["a"][5].IsNull);
            Assert.IsFalse(v.Has("zzz"));
        }

        [Test]
        public void 負数と小数と指数を読める()
        {
            var v = JsonParser.Parse(@"[-1, 0.25, 1e3, -2.5E-2]");

            Assert.AreEqual(-1.0, v[0].AsDouble());
            Assert.AreEqual(0.25, v[1].AsDouble(), 1e-12);
            Assert.AreEqual(1000.0, v[2].AsDouble(), 1e-12);
            Assert.AreEqual(-0.025, v[3].AsDouble(), 1e-12);
        }

        [Test]
        public void エスケープを読める()
        {
            var v = JsonParser.Parse(@"{ ""s"": ""a\""b\\c\ndあ"" }");
            Assert.AreEqual("a\"b\\c\ndあ", v["s"].AsString());
        }

        [Test]
        public void 日本語をそのまま読める()
        {
            var v = JsonParser.Parse(@"{ ""match"": [""ニシキ"", ""にしき"", ""錦""] }");

            Assert.AreEqual("ニシキ", v["match"][0].AsString());
            Assert.AreEqual("にしき", v["match"][1].AsString());
            Assert.AreEqual("錦", v["match"][2].AsString());
        }

        [Test]
        public void 空のオブジェクトと配列を読める()
        {
            Assert.AreEqual(0, JsonParser.Parse("{}").Count);
            Assert.AreEqual(0, JsonParser.Parse("[]").Count);
        }

        [Test]
        public void 壊れた入力は例外になる()
        {
            Assert.Throws<JsonParseException>(() => JsonParser.Parse("{"));
            Assert.Throws<JsonParseException>(() => JsonParser.Parse("{ \"a\" 1 }"));
            Assert.Throws<JsonParseException>(() => JsonParser.Parse("[1, 2"));
            Assert.Throws<JsonParseException>(() => JsonParser.Parse("tru"));
            Assert.Throws<JsonParseException>(() => JsonParser.Parse("{} extra"));
        }

        [Test]
        public void キーの重複は例外になる()
        {
            Assert.Throws<JsonParseException>(() => JsonParser.Parse(@"{ ""a"": 1, ""a"": 2 }"));
        }
    }
}
