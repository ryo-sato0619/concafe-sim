using System;
using System.Collections.Generic;
using Concafe.Simulation.Balance;
using Concafe.Simulation.Json;
using Concafe.Simulation.Model;

namespace Concafe.Simulation.Save
{
    public sealed class SaveException : Exception
    {
        public SaveException(string message) : base(message) { }
    }

    /// <summary>
    /// セーブデータの見出し。スロット一覧に出す分だけを持つ。
    /// 本体を読まずに一覧を描けるよう、先頭に固めて置いてある。
    /// </summary>
    public sealed class SaveHeader
    {
        public int SchemaVersion;
        /// <summary>保存した時刻。Simulation 層は時計を読まないので呼び出し側が渡す。</summary>
        public string SavedAt;
        public int Year;
        public int Month;
        public int AbsoluteWeek;
        public long CashYen;
        public int CastCount;
        public int CastSlots;
        public int AlumniCount;
        public GameOverReason GameOver;
    }

    /// <summary>
    /// <see cref="GameState"/> と JSON 文字列の相互変換（段階 6）。
    ///
    /// ファイルには触れない。**保存先は呼び出し側の責任**である。
    /// Simulation 層に System.IO を持ち込むと WebGL で動かなくなるうえ、
    /// テストから保存を差し替えられなくなる。
    ///
    /// <para>
    /// <b>U-10 決着: 乱数の状態は保存しない。</b>
    /// ロードのたびに <see cref="Decode"/> へ新しい種を渡すので、同じセーブから
    /// 何度やり直しても結果が変わる。**リセマラを許容する**という判断である。
    /// </para>
    /// <para>
    /// ただし**その週の天候と予報は保存する。** 週頭に予報を見せてから
    /// シフトを組ませているので、ロードで天候まで引き直すと、
    /// プレイヤーが見た予報が後出しの嘘になる。引き直るのは
    /// 予報より後（注文・イベント・炎上・離脱・求人の質）だけである。
    /// </para>
    /// </summary>
    public static class SaveCodec
    {
        public const int SupportedSchemaVersion = 1;

        // ---- 書き出し -------------------------------------------------------

        public static string Encode(GameState state, string savedAt = null)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));

            var w = new JsonWriter();
            w.BeginObject();

            w.Value("schemaVersion", SupportedSchemaVersion);
            w.Value("savedAt", savedAt ?? "");

            // 見出し。スロット一覧のために本体と重複して持つ。
            w.BeginObject("header");
            var cal = state.Calendar;
            w.Value("year", cal.YearOf(state.Week));
            w.Value("month", cal.MonthOf(state.Week));
            w.Value("absoluteWeek", state.Week.AbsoluteWeek);
            w.Value("cashYen", state.Shop.CashYen);
            w.Value("castCount", state.Shop.Roster.Count);
            w.Value("castSlots", state.Shop.CastSlots);
            w.Value("alumniCount", state.Shop.Alumni.Count);
            w.Value("gameOver", state.GameOver.ToString());
            w.EndObject();

            w.Value("absoluteWeek", state.Week.AbsoluteWeek);
            w.Value("gameOver", state.GameOver.ToString());
            w.Value("nextCastSerial", state.NextCastSerial);
            w.Value("weekPurchaseYen", state.WeekPurchaseYen);
            w.Value("trainingBooks", state.TrainingBooks);

            // 天候と予報は保存する（U-10 の但し書き）。
            w.BeginArray("weather");
            foreach (var x in state.Weather) w.Item(x.ToString());
            w.EndArray();

            w.BeginArray("forecast");
            foreach (var x in state.Forecast) w.Item(x.ToString());
            w.EndArray();

            WriteGifts(w, state.PendingGifts);

            w.BeginArray("pendingEggEvents");
            foreach (var e in state.PendingEggEvents) WriteEggEvent(w, e);
            w.EndArray();

            WriteShop(w, state.Shop);

            w.EndObject();
            return w.ToString();
        }

        static void WriteGifts(JsonWriter w, AlumniGiftRecord g)
        {
            w.BeginObject("pendingGifts");
            if (g != null)
            {
                w.Value("trainingBooks", g.TrainingBooks);
                w.Value("premiumBottles", g.PremiumBottles);
                w.Value("limitedGoods", g.LimitedGoods);
                w.Value("oneDayManager", g.OneDayManager);
                w.Value("congratulatoryYen", g.CongratulatoryYen);
            }
            w.EndObject();
        }

        static void WriteEggEvent(JsonWriter w, EggEventRecord e)
        {
            w.BeginObject();
            w.Value("castId", e.CastId);
            w.Value("stageName", e.StageName);
            w.Value("eventId", e.EventId);
            w.Value("title", e.Title);
            w.Value("absentDays", e.AbsentDays);
            w.Value("shopMoraleLost", e.ShopMoraleLost);
            w.BeginArray("body");
            if (e.Body != null) foreach (string line in e.Body) w.Item(line);
            w.EndArray();
            w.EndObject();
        }

        static void WriteShop(JsonWriter w, Shop shop)
        {
            w.BeginObject("shop");
            w.Value("cashYen", shop.CashYen);
            w.Value("castSlots", shop.CastSlots);
            w.Value("managerAp", shop.ManagerAp);

            w.BeginArray("facilityLevels");
            foreach (int level in shop.Facilities.ToArray()) w.Item(level);
            w.EndArray();

            w.BeginArray("stock");
            foreach (var kv in shop.Stock.Entries())
            {
                w.BeginObject();
                w.Value("item", kv.Key.Item.ToString());
                w.Value("tier", kv.Key.TierIndex);
                w.Value("count", kv.Value);
                w.EndObject();
            }
            w.EndArray();

            w.BeginArray("roster");
            foreach (var c in shop.Roster) WriteCast(w, c);
            w.EndArray();

            w.BeginArray("applicants");
            foreach (var a in shop.Applicants) WriteApplicant(w, a);
            w.EndArray();

            w.BeginArray("alumni");
            foreach (var a in shop.Alumni) WriteAlumnus(w, a);
            w.EndArray();

            w.EndObject();
        }

        static void WriteStats(JsonWriter w, string name, Stats s)
        {
            w.BeginObject(name);
            w.Value("regulars", s.Regulars);
            w.Value("fame", s.Fame);
            w.Value("talk", s.Talk);
            w.Value("alcohol", s.Alcohol);
            w.Value("looks", s.Looks);
            w.Value("morale", s.Morale);
            w.EndObject();
        }

        static void WriteLook(JsonWriter w, Appearance a)
        {
            w.BeginObject("look");
            if (a != null)
            {
                w.Value("body", a.Body);
                w.Value("dress", a.Dress);
                w.Value("hairBack", a.HairBack);
                w.Value("hairFront", a.HairFront);
                w.Value("eye", a.Eye);
                w.Value("accessory", a.Accessory);
                w.Value("skinColor", a.SkinColor);
                w.Value("dressColor", a.DressColor);
                w.Value("hairColor", a.HairColor);
                w.Value("eyeColor", a.EyeColor);
            }
            w.EndObject();
        }

        static void WriteCast(JsonWriter w, Cast c)
        {
            w.BeginObject();
            w.Value("id", c.Id);
            w.Value("stageName", c.StageName);
            w.Value("age", c.Age);
            w.Value("birthWeekOfYear", c.BirthWeekOfYear);
            w.Value("hiredWeek", c.HiredWeek);
            w.Value("staminaTier", c.StaminaTier);
            w.Value("fatigue", c.Fatigue);
            w.Value("scandalResist", c.ScandalResist);
            w.Value("trust", c.Trust);
            w.Value("lifetimeSalesYen", c.LifetimeSalesYen);
            w.Value("absentDays", c.AbsentDays);
            w.Value("lastWeekAlcoholDrinks", c.LastWeekAlcoholDrinks);
            w.Value("easterEggId", c.EasterEggId);
            WriteStats(w, "current", c.Current);
            WriteStats(w, "cap", c.Cap);
            WriteStats(w, "growthRate", c.GrowthRate);
            WriteLook(w, c.Look);
            w.EndObject();
        }

        static void WriteApplicant(JsonWriter w, Applicant a)
        {
            w.BeginObject();
            w.Value("id", a.Id);
            w.Value("age", a.Age);
            w.Value("birthWeekOfYear", a.BirthWeekOfYear);
            w.Value("staminaTier", a.StaminaTier);
            w.Value("scandalResist", a.ScandalResist);
            w.Value("interviewed", a.Interviewed);
            WriteStats(w, "initialStats", a.InitialStats);
            WriteStats(w, "cap", a.Cap);
            WriteStats(w, "growthRate", a.GrowthRate);
            WriteLook(w, a.Look);
            w.EndObject();
        }

        static void WriteAlumnus(JsonWriter w, Alumnus a)
        {
            w.BeginObject();
            w.Value("id", a.Id);
            w.Value("stageName", a.StageName);
            w.Value("career", a.Career.ToString());
            w.Value("graduatedWeek", a.GraduatedWeek);
            w.Value("lifetimeSalesYen", a.LifetimeSalesYen);
            WriteStats(w, "finalStats", a.FinalStats);
            w.EndObject();
        }

        // ---- 見出しだけ読む -------------------------------------------------

        /// <summary>
        /// スロット一覧用。本体を組み立てずに見出しだけ取る。
        /// 壊れたデータでも例外を投げずに null を返す。一覧が 1 つの
        /// 壊れたファイルで丸ごと開けなくなるのを避けるため。
        /// </summary>
        public static SaveHeader PeekHeader(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;

            JsonValue root;
            try { root = JsonParser.Parse(json); }
            catch (JsonParseException) { return null; }

            var h = root["header"];
            if (h.IsNull) return null;

            return new SaveHeader
            {
                SchemaVersion = (int)root["schemaVersion"].AsDouble(-1),
                SavedAt = root["savedAt"].AsString(""),
                Year = (int)h["year"].AsDouble(),
                Month = (int)h["month"].AsDouble(),
                AbsoluteWeek = (int)h["absoluteWeek"].AsDouble(),
                CashYen = (long)h["cashYen"].AsDouble(),
                CastCount = (int)h["castCount"].AsDouble(),
                CastSlots = (int)h["castSlots"].AsDouble(),
                AlumniCount = (int)h["alumniCount"].AsDouble(),
                GameOver = ParseEnum(h["gameOver"].AsString(), GameOverReason.None),
            };
        }

        // ---- 読み込み -------------------------------------------------------

        /// <summary>
        /// セーブから復元する。
        ///
        /// <paramref name="freshSeed"/> は**ロードのたびに違う値**を渡すこと。
        /// 乱数の状態を保存していないので、ここに渡した種がその後の展開を決める
        /// （U-10）。同じ種を渡せば同じ展開になるため、テストはそれを使う。
        /// </summary>
        public static GameState Decode(
            string json, BalanceData balance, EasterEggSet eggs, CareerSet careers,
            ulong freshSeed)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new SaveException("セーブデータが空");
            if (balance == null) throw new ArgumentNullException(nameof(balance));

            JsonValue root;
            try { root = JsonParser.Parse(json); }
            catch (JsonParseException e) { throw new SaveException("JSON として読めない: " + e.Message); }

            int version = (int)root["schemaVersion"].AsDouble(-1);
            if (version != SupportedSchemaVersion)
                throw new SaveException(
                    $"セーブの schemaVersion が {version}。対応しているのは {SupportedSchemaVersion}");

            var state = new GameState(balance, eggs, careers, freshSeed);

            state.Week = new GameWeek((int)root["absoluteWeek"].AsDouble());
            state.GameOver = ParseEnum(root["gameOver"].AsString(), GameOverReason.None);
            state.NextCastSerial = (int)root["nextCastSerial"].AsDouble(1);
            state.WeekPurchaseYen = (long)root["weekPurchaseYen"].AsDouble();
            state.TrainingBooks = (int)root["trainingBooks"].AsDouble();

            ReadWeather(root["weather"], state.Weather);
            ReadWeather(root["forecast"], state.Forecast);

            state.PendingGifts = ReadGifts(root["pendingGifts"]);
            state.PendingEggEvents = ReadEggEvents(root["pendingEggEvents"]);

            ReadShop(root["shop"], state.Shop, balance);
            state.RefreshAlumniEffects();

            return state;
        }

        static void ReadWeather(JsonValue node, Weather[] into)
        {
            int i = 0;
            foreach (var v in node.Items())
            {
                if (i >= into.Length) break;
                into[i++] = ParseEnum(v.AsString(), Model.Weather.Sunny);
            }
        }

        static AlumniGiftRecord ReadGifts(JsonValue node) => new AlumniGiftRecord
        {
            TrainingBooks = (int)node["trainingBooks"].AsDouble(),
            PremiumBottles = (int)node["premiumBottles"].AsDouble(),
            LimitedGoods = (int)node["limitedGoods"].AsDouble(),
            OneDayManager = node["oneDayManager"].AsBool(),
            CongratulatoryYen = (long)node["congratulatoryYen"].AsDouble(),
        };

        static List<EggEventRecord> ReadEggEvents(JsonValue node)
        {
            var list = new List<EggEventRecord>();
            foreach (var v in node.Items())
            {
                var body = new List<string>();
                foreach (var line in v["body"].Items()) body.Add(line.AsString(""));

                list.Add(new EggEventRecord
                {
                    CastId = v["castId"].AsString(),
                    StageName = v["stageName"].AsString(),
                    EventId = v["eventId"].AsString(),
                    Title = v["title"].AsString(),
                    AbsentDays = (int)v["absentDays"].AsDouble(),
                    ShopMoraleLost = v["shopMoraleLost"].AsDouble(),
                    Body = body,
                });
            }
            return list;
        }

        static void ReadShop(JsonValue node, Shop shop, BalanceData balance)
        {
            if (node.IsNull) throw new SaveException("shop が無い");

            shop.CashYen = (long)node["cashYen"].AsDouble();
            shop.CastSlots = (int)node["castSlots"].AsDouble(balance.Shop.InitialCastSlots);
            shop.ManagerAp = (int)node["managerAp"].AsDouble();

            var levels = new List<int>();
            foreach (var v in node["facilityLevels"].Items()) levels.Add((int)v.AsDouble());
            shop.Facilities.Restore(levels.ToArray());

            shop.Stock.Clear();
            foreach (var v in node["stock"].Items())
            {
                var item = ParseEnum(v["item"].AsString(), StockItem.SoftDrink);
                int tier = (int)v["tier"].AsDouble(-1);
                int count = (int)v["count"].AsDouble();
                if (count <= 0) continue;

                shop.Stock.Add(
                    StockItems.IsBottle(item) ? StockKey.Bottle(item, tier) : StockKey.Simple(item),
                    count);
            }

            shop.Roster.Clear();
            foreach (var v in node["roster"].Items()) shop.Roster.Add(ReadCast(v));

            shop.Applicants.Clear();
            foreach (var v in node["applicants"].Items()) shop.Applicants.Add(ReadApplicant(v));

            shop.Alumni.Clear();
            foreach (var v in node["alumni"].Items()) shop.Alumni.Add(ReadAlumnus(v));
        }

        static Stats ReadStats(JsonValue node) => new Stats
        {
            Regulars = node["regulars"].AsDouble(),
            Fame = node["fame"].AsDouble(),
            Talk = node["talk"].AsDouble(),
            Alcohol = node["alcohol"].AsDouble(),
            Looks = node["looks"].AsDouble(),
            Morale = node["morale"].AsDouble(),
        };

        static Appearance ReadLook(JsonValue node) => new Appearance
        {
            Body = (int)node["body"].AsDouble(),
            Dress = (int)node["dress"].AsDouble(),
            HairBack = (int)node["hairBack"].AsDouble(),
            HairFront = (int)node["hairFront"].AsDouble(),
            Eye = (int)node["eye"].AsDouble(),
            Accessory = (int)node["accessory"].AsDouble(),
            SkinColor = (int)node["skinColor"].AsDouble(),
            DressColor = (int)node["dressColor"].AsDouble(),
            HairColor = (int)node["hairColor"].AsDouble(),
            EyeColor = (int)node["eyeColor"].AsDouble(),
        };

        static Cast ReadCast(JsonValue v)
        {
            var c = new Cast
            {
                Id = v["id"].AsString(),
                StageName = v["stageName"].AsString(),
                Age = (int)v["age"].AsDouble(),
                BirthWeekOfYear = (int)v["birthWeekOfYear"].AsDouble(),
                HiredWeek = (int)v["hiredWeek"].AsDouble(),
                StaminaTier = (int)v["staminaTier"].AsDouble(1),
                Fatigue = v["fatigue"].AsDouble(),
                ScandalResist = (int)v["scandalResist"].AsDouble(),
                Trust = v["trust"].AsDouble(),
                LifetimeSalesYen = (long)v["lifetimeSalesYen"].AsDouble(),
                AbsentDays = (int)v["absentDays"].AsDouble(),
                LastWeekAlcoholDrinks = (int)v["lastWeekAlcoholDrinks"].AsDouble(),
                EasterEggId = v["easterEggId"].AsString(),
                Look = ReadLook(v["look"]),
            };
            c.Current = ReadStats(v["current"]);
            c.Cap = ReadStats(v["cap"]);
            c.GrowthRate = ReadStats(v["growthRate"]);
            return c;
        }

        static Applicant ReadApplicant(JsonValue v)
        {
            var a = new Applicant
            {
                Id = v["id"].AsString(),
                Age = (int)v["age"].AsDouble(),
                BirthWeekOfYear = (int)v["birthWeekOfYear"].AsDouble(),
                StaminaTier = (int)v["staminaTier"].AsDouble(1),
                ScandalResist = (int)v["scandalResist"].AsDouble(),
                Interviewed = v["interviewed"].AsBool(),
                Look = ReadLook(v["look"]),
            };
            a.InitialStats = ReadStats(v["initialStats"]);
            a.Cap = ReadStats(v["cap"]);
            a.GrowthRate = ReadStats(v["growthRate"]);
            return a;
        }

        static Alumnus ReadAlumnus(JsonValue v)
        {
            var a = new Alumnus
            {
                Id = v["id"].AsString(),
                StageName = v["stageName"].AsString(),
                Career = ParseEnum(v["career"].AsString(), CareerKind.Idol),
                GraduatedWeek = (int)v["graduatedWeek"].AsDouble(),
                LifetimeSalesYen = (long)v["lifetimeSalesYen"].AsDouble(),
            };
            a.FinalStats = ReadStats(v["finalStats"]);
            return a;
        }

        /// <summary>
        /// 列挙の名前で保存しているのは、順序を変えても壊れないようにするため。
        /// 知らない名前が来たら既定値に落とす（セーブが開けなくなるより良い）。
        /// </summary>
        static T ParseEnum<T>(string name, T fallback) where T : struct
        {
            if (string.IsNullOrEmpty(name)) return fallback;
            return Enum.TryParse(name, out T v) ? v : fallback;
        }
    }
}
