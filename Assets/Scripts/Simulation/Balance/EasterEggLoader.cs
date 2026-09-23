using System;
using System.Collections.Generic;
using Concafe.Simulation.Json;
using Concafe.Simulation.Model;

namespace Concafe.Simulation.Balance
{
    public sealed class EasterEggException : Exception
    {
        public IReadOnlyList<string> Problems { get; }

        public EasterEggException(IReadOnlyList<string> problems)
            : base("easter_eggs.json に問題がある:" + Environment.NewLine +
                   "  - " + string.Join(Environment.NewLine + "  - ", problems))
        {
            Problems = problems;
        }
    }

    public static class EasterEggLoader
    {
        public const int SupportedSchemaVersion = 1;

        /// <summary>疲労 Tier の上限を渡すのは、fixedStaminaTier を範囲検査するため。</summary>
        public static EasterEggSet Load(string json, int staminaTierCount)
        {
            JsonValue root;
            try
            {
                root = JsonParser.Parse(json);
            }
            catch (JsonParseException e)
            {
                throw new EasterEggException(new[] { "JSON として読めない: " + e.Message });
            }

            var problems = new List<string>();

            int version = (int)root["schemaVersion"].AsDouble(-1);
            if (version != SupportedSchemaVersion)
                problems.Add($"schemaVersion が {version}。対応しているのは {SupportedSchemaVersion}");

            var eggs = ReadEggs(root["eggs"], staminaTierCount, problems);
            var events = ReadEvents(root["events"], problems);

            // 未実装の eventId を参照していても落とさない。イベントを書く前に
            // 名前だけ決められる状態を保つ（設計書 6）。空き枠のための仕様である。

            if (problems.Count > 0) throw new EasterEggException(problems);

            return new EasterEggSet
            {
                SchemaVersion = version,
                Eggs = eggs,
                Events = events,
            };
        }

        static List<EasterEgg> ReadEggs(JsonValue node, int staminaTierCount, List<string> problems)
        {
            var list = new List<EasterEgg>();
            if (node.Type != JsonValue.Kind.Array)
            {
                problems.Add("eggs が無いか配列ではない");
                return list;
            }

            var seenIds = new HashSet<string>(StringComparer.Ordinal);
            var seenMatches = new Dictionary<string, string>(StringComparer.Ordinal);

            int i = 0;
            foreach (var e in node.Items())
            {
                string path = $"eggs[{i}]";
                i++;

                string id = e["id"].AsString();
                if (string.IsNullOrEmpty(id))
                {
                    problems.Add($"{path}.id が無い");
                    continue;
                }
                if (!seenIds.Add(id))
                    problems.Add($"{path}.id \"{id}\" が重複している");

                var egg = new EasterEgg
                {
                    Id = id,
                    Enabled = e["enabled"].AsBool(false),
                    DisplayName = e["displayName"].AsString(id),
                    RevealOnHire = e["revealOnHire"].AsBool(true),
                    HireMessage = e["hireMessage"].AsString(string.Empty),
                    Note = e["note"].AsString(string.Empty),
                    EventId = e["eventId"].AsString(),

                    StatBonus = ReadModifier(e["statBonus"], 0.0, path + ".statBonus", problems),
                    StatMultiplier = ReadModifier(e["statMultiplier"], 1.0, path + ".statMultiplier", problems),
                    CapBonus = ReadModifier(e["capBonus"], 0.0, path + ".capBonus", problems),
                    CapMultiplier = ReadModifier(e["capMultiplier"], 1.0, path + ".capMultiplier", problems),
                    GrowthRateMultiplier = ReadModifier(e["growthRateMultiplier"], 1.0,
                                                        path + ".growthRateMultiplier", problems),

                    FixedStaminaTier = e.Has("fixedStaminaTier") ? (int)e["fixedStaminaTier"].AsDouble(0) : 0,
                    FixedScandalResist = e.Has("fixedScandalResist") ? (int)e["fixedScandalResist"].AsDouble(-1) : -1,

                    OnDeparture = ReadDepartureOverride(e["onDeparture"], path, problems),
                    BurnDownOn = ReadBurnDownCondition(e["burnDownOn"], path, problems),
                };

                var matches = new List<string>();
                var matchNode = e["match"];
                if (!matchNode.IsNull && matchNode.Type != JsonValue.Kind.Array)
                {
                    problems.Add($"{path}.match が配列ではない");
                }
                else
                {
                    foreach (var m in matchNode.Items())
                    {
                        string s = m.AsString();
                        if (string.IsNullOrEmpty(s))
                        {
                            problems.Add($"{path}.match に空の文字列がある");
                            continue;
                        }
                        matches.Add(s);
                    }
                }
                egg.Match = matches;

                // 空き枠を有効にしただけで名前を入れ忘れた場合をここで捕まえる。
                // 黙って通すと「有効にしたのに一生発動しない」状態になり、
                // 実装を疑い始めることになる。
                if (egg.Enabled && matches.Count == 0)
                    problems.Add($"{path} は enabled だが match が空。絶対に発動しない。" +
                                 "有効にするなら match に源氏名を入れること");

                // 有効なエッグ同士で候補が衝突すると、どちらが出るかが並び順に依存する。
                if (egg.Enabled)
                {
                    foreach (var m in matches)
                    {
                        string key = EasterEggSet.Normalize(m);
                        if (seenMatches.TryGetValue(key, out string owner))
                            problems.Add($"match \"{m}\" が {owner} と {id} で重複している");
                        else
                            seenMatches[key] = id;
                    }
                }

                if (egg.HasFixedStaminaTier &&
                    (egg.FixedStaminaTier < 1 || egg.FixedStaminaTier > staminaTierCount))
                    problems.Add($"{path}.fixedStaminaTier が 1〜{staminaTierCount} の外");

                if (e.Has("fixedScandalResist") &&
                    (egg.FixedScandalResist < 0 || egg.FixedScandalResist > 100))
                    problems.Add($"{path}.fixedScandalResist が 0〜100 の外");

                if (egg.OnDeparture == DepartureOverride.None && e.Has("burnDownOn"))
                    problems.Add($"{path}.burnDownOn があるのに onDeparture が burnDown でない");

                egg.WeeklyEvents = ReadWeeklyEvents(e["weeklyEvents"], path, problems);

                list.Add(egg);
            }

            return list;
        }

        static List<WeeklyEggEvent> ReadWeeklyEvents(
            JsonValue node, string path, List<string> problems)
        {
            var list = new List<WeeklyEggEvent>();
            if (node.IsNull) return list;

            if (node.Type != JsonValue.Kind.Array)
            {
                problems.Add($"{path}.weeklyEvents が配列ではない");
                return list;
            }

            int i = 0;
            foreach (var w in node.Items())
            {
                string p = $"{path}.weeklyEvents[{i}]";
                i++;

                string id = w["eventId"].AsString();
                if (string.IsNullOrEmpty(id)) { problems.Add($"{p}.eventId が無い"); continue; }

                double chance = w["chance"].AsDouble(-1);
                if (chance <= 0 || chance > 1)
                {
                    problems.Add($"{p}.chance が 0〜1 の外。0 なら一度も起きない");
                    continue;
                }

                list.Add(new WeeklyEggEvent
                {
                    EventId = id,
                    Chance = chance,
                    RequiresAlcoholServed = w["requiresAlcoholServed"].AsBool(false),
                });
            }

            return list;
        }

        static StatModifier ReadModifier(JsonValue node, double fallback,
                                         string path, List<string> problems)
        {
            if (node.IsNull) return StatModifier.Identity(fallback);

            if (node.Type != JsonValue.Kind.Object)
            {
                problems.Add($"{path} がオブジェクトではない");
                return StatModifier.Identity(fallback);
            }

            bool hasAll = false;
            double all = fallback;
            var values = new Dictionary<StatKind, double>();

            foreach (var kv in node.Members())
            {
                if (kv.Value.Type != JsonValue.Kind.Number)
                {
                    problems.Add($"{path}.{kv.Key} が数値ではない");
                    continue;
                }
                double v = kv.Value.AsDouble();

                if (v < 0)
                {
                    problems.Add($"{path}.{kv.Key} が負");
                    continue;
                }

                if (kv.Key == "all")
                {
                    hasAll = true;
                    all = v;
                    continue;
                }

                if (!TryParseStat(kv.Key, out var kind))
                {
                    problems.Add($"{path}.{kv.Key} は知らないパラメータ名");
                    continue;
                }
                values[kind] = v;
            }

            return new StatModifier(fallback, hasAll, all, values);
        }

        static bool TryParseStat(string key, out StatKind kind)
        {
            switch (key)
            {
                case "regulars": kind = StatKind.Regulars; return true;
                case "fame": kind = StatKind.Fame; return true;
                case "talk": kind = StatKind.Talk; return true;
                case "alcohol": kind = StatKind.Alcohol; return true;
                case "looks": kind = StatKind.Looks; return true;
                case "morale": kind = StatKind.Morale; return true;
                default: kind = StatKind.Talk; return false;
            }
        }

        static DepartureOverride ReadDepartureOverride(JsonValue node, string path, List<string> problems)
        {
            string s = node.AsString();
            if (string.IsNullOrEmpty(s) || s == "none") return DepartureOverride.None;
            if (s == "burnDown") return DepartureOverride.BurnDown;

            problems.Add($"{path}.onDeparture \"{s}\" は知らない値（none / burnDown）");
            return DepartureOverride.None;
        }

        static BurnDownCondition ReadBurnDownCondition(JsonValue node, string path, List<string> problems)
        {
            string s = node.AsString();
            if (string.IsNullOrEmpty(s) || s == "any") return BurnDownCondition.Any;
            if (s == "runAway") return BurnDownCondition.RunAwayOnly;

            problems.Add($"{path}.burnDownOn \"{s}\" は知らない値（any / runAway）");
            return BurnDownCondition.Any;
        }

        static Dictionary<string, EasterEggEvent> ReadEvents(JsonValue node, List<string> problems)
        {
            var map = new Dictionary<string, EasterEggEvent>(StringComparer.Ordinal);
            if (node.IsNull) return map;

            if (node.Type != JsonValue.Kind.Array)
            {
                problems.Add("events が配列ではない");
                return map;
            }

            int i = 0;
            foreach (var e in node.Items())
            {
                string path = $"events[{i}]";
                i++;

                string id = e["id"].AsString();
                if (string.IsNullOrEmpty(id))
                {
                    problems.Add($"{path}.id が無い");
                    continue;
                }
                if (map.ContainsKey(id))
                {
                    problems.Add($"{path}.id \"{id}\" が重複している");
                    continue;
                }

                var body = new List<string>();
                foreach (var line in e["body"].Items())
                {
                    string s = line.AsString();
                    if (s != null) body.Add(s);
                }

                var ev = new EasterEggEvent
                {
                    Id = id,
                    Title = e["title"].AsString(string.Empty),
                    Body = body,
                    GameOver = ReadGameOver(e["gameOver"], path, problems),
                };

                var effect = e["effect"];
                if (!effect.IsNull)
                {
                    string kind = effect["kind"].AsString("none");
                    switch (kind)
                    {
                        case "none": ev.Effect = EggEventEffect.None; break;

                        case "absence":
                            ev.Effect = EggEventEffect.Absence;
                            ev.Days = (int)effect["days"].AsDouble(0);
                            if (ev.Days <= 0) problems.Add($"{path}.effect.days が 0 以下");
                            break;

                        case "shopMorale":
                            ev.Effect = EggEventEffect.ShopMorale;
                            ev.Value = effect["value"].AsDouble(0);
                            if (ev.Value <= 0) problems.Add($"{path}.effect.value が 0 以下");
                            break;

                        default:
                            problems.Add($"{path}.effect.kind \"{kind}\" は知らない効果");
                            break;
                    }
                }

                map[id] = ev;
            }

            return map;
        }

        static GameOverReason ReadGameOver(JsonValue node, string path, List<string> problems)
        {
            string s = node.AsString();
            if (string.IsNullOrEmpty(s) || s == "none") return GameOverReason.None;
            if (s == "arson") return GameOverReason.Arson;
            if (s == "cashShortage") return GameOverReason.CashShortage;

            problems.Add($"{path}.gameOver \"{s}\" は知らない値");
            return GameOverReason.None;
        }
    }
}
