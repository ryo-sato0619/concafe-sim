using System;
using System.Collections.Generic;
using Concafe.Simulation.Json;
using Concafe.Simulation.Model;

namespace Concafe.Simulation.Balance
{
    public sealed class CareerException : Exception
    {
        public IReadOnlyList<string> Problems { get; }

        public CareerException(IReadOnlyList<string> problems)
            : base("careers.json に問題がある:" + Environment.NewLine +
                   "  - " + string.Join(Environment.NewLine + "  - ", problems))
        {
            Problems = problems;
        }
    }

    public static class CareerLoader
    {
        public const int SupportedSchemaVersion = 1;

        public static CareerSet Load(string json)
        {
            JsonValue root;
            try
            {
                root = JsonParser.Parse(json);
            }
            catch (JsonParseException e)
            {
                throw new CareerException(new[] { "JSON として読めない: " + e.Message });
            }

            var problems = new List<string>();

            int version = (int)root["schemaVersion"].AsDouble(-1);
            if (version != SupportedSchemaVersion)
                problems.Add($"schemaVersion が {version}。対応しているのは {SupportedSchemaVersion}");

            var grad = ReadGraduation(root["graduation"], problems);
            var caps = ReadCaps(root["caps"], problems);
            var careers = ReadCareers(root["careers"], problems);

            // 企画書の 10 種がすべて揃っていること。1 つ欠けると、
            // そのモラルの使い道が消えてしまう。
            foreach (CareerKind kind in Enum.GetValues(typeof(CareerKind)))
            {
                bool found = false;
                foreach (var c in careers) if (c.Kind == kind) { found = true; break; }
                if (!found) problems.Add($"進路 {kind} が careers.json に無い");
            }

            if (problems.Count > 0) throw new CareerException(problems);

            return new CareerSet
            {
                SchemaVersion = version,
                Graduation = grad,
                Caps = caps,
                Careers = careers,
            };
        }

        static GraduationBalance ReadGraduation(JsonValue node, List<string> problems)
        {
            if (node.Type != JsonValue.Kind.Object)
            {
                problems.Add("graduation が無いかオブジェクトではない");
                return new GraduationBalance();
            }

            var g = new GraduationBalance
            {
                MinWeeks = (int)node["minWeeks"].AsDouble(-1),
                MinMorale = node["minMorale"].AsDouble(-1),
                MinAge = (int)node["minAge"].AsDouble(-1),
                CapReachedRatio = node["capReachedRatio"].AsDouble(-1),
            };

            if (g.MinWeeks <= 0) problems.Add("graduation.minWeeks が 0 以下");
            if (g.MinMorale < 0 || g.MinMorale > 100) problems.Add("graduation.minMorale が 0〜100 の外");
            if (g.MinAge < 18) problems.Add("graduation.minAge が 18 未満");
            if (g.CapReachedRatio <= 0 || g.CapReachedRatio > 1)
                problems.Add("graduation.capReachedRatio が 0〜1 の外");

            return g;
        }

        static CareerCaps ReadCaps(JsonValue node, List<string> problems)
        {
            if (node.Type != JsonValue.Kind.Object)
            {
                problems.Add("caps が無いかオブジェクトではない");
                return new CareerCaps();
            }

            var c = new CareerCaps
            {
                DrinkCostMultiplierMin = node["drinkCostMultiplierMin"].AsDouble(-1),
                LooksBonusMax = node["looksBonusMax"].AsDouble(-1),
                CareRecoverMultiplierMax = node["careRecoverMultiplierMax"].AsDouble(-1),
                WeatherReliefMax = node["weatherReliefMax"].AsDouble(-1),
                RecruitCapBonusMax = node["recruitCapBonusMax"].AsDouble(-1),
                TrainingBooksPerWeekMax = (int)node["trainingBooksPerWeekMax"].AsDouble(-1),
                PremiumBottlesPerWeekMax = (int)node["premiumBottlesPerWeekMax"].AsDouble(-1),
                LimitedGoodsPerWeekMax = (int)node["limitedGoodsPerWeekMax"].AsDouble(-1),
            };

            if (c.DrinkCostMultiplierMin <= 0 || c.DrinkCostMultiplierMin > 1)
                problems.Add("caps.drinkCostMultiplierMin が 0〜1 の外");
            if (c.LooksBonusMax < 0) problems.Add("caps.looksBonusMax が負");
            if (c.CareRecoverMultiplierMax < 1)
                problems.Add("caps.careRecoverMultiplierMax が 1 未満");
            if (c.WeatherReliefMax < 0 || c.WeatherReliefMax > 1)
                problems.Add("caps.weatherReliefMax が 0〜1 の外");
            if (c.RecruitCapBonusMax < 0) problems.Add("caps.recruitCapBonusMax が負");
            if (c.TrainingBooksPerWeekMax < 0) problems.Add("caps.trainingBooksPerWeekMax が負");
            if (c.PremiumBottlesPerWeekMax < 0) problems.Add("caps.premiumBottlesPerWeekMax が負");
            if (c.LimitedGoodsPerWeekMax < 0) problems.Add("caps.limitedGoodsPerWeekMax が負");

            return c;
        }

        static List<CareerDefinition> ReadCareers(JsonValue node, List<string> problems)
        {
            var list = new List<CareerDefinition>();
            if (node.Type != JsonValue.Kind.Array)
            {
                problems.Add("careers が無いか配列ではない");
                return list;
            }

            var seenIds = new HashSet<string>(StringComparer.Ordinal);
            var seenKinds = new HashSet<CareerKind>();

            int i = 0;
            foreach (var c in node.Items())
            {
                string path = $"careers[{i}]";
                i++;

                string id = c["id"].AsString();
                if (string.IsNullOrEmpty(id)) { problems.Add($"{path}.id が無い"); continue; }
                if (!seenIds.Add(id)) problems.Add($"{path}.id \"{id}\" が重複している");

                if (!TryParseCareer(id, out var kind))
                {
                    problems.Add($"{path}.id \"{id}\" は知らない進路");
                    continue;
                }
                if (!seenKinds.Add(kind))
                    problems.Add($"{path} の進路 {kind} が重複している");

                var weights = new Dictionary<StatKind, double>();
                var wNode = c["weights"];
                if (wNode.Type != JsonValue.Kind.Object)
                {
                    problems.Add($"{path}.weights がオブジェクトではない");
                }
                else
                {
                    foreach (var kv in wNode.Members())
                    {
                        if (!TryParseStat(kv.Key, out var stat))
                        {
                            problems.Add($"{path}.weights.{kv.Key} は知らないパラメータ名");
                            continue;
                        }
                        double w = kv.Value.AsDouble(-1);
                        if (w < 0) { problems.Add($"{path}.weights.{kv.Key} が負"); continue; }
                        weights[stat] = w;
                    }
                }
                if (weights.Count == 0)
                    problems.Add($"{path}.weights が空。この進路は絶対に選ばれない");

                var eNode = c["effect"];
                var def = new CareerDefinition
                {
                    Id = id,
                    Kind = kind,
                    Name = c["name"].AsString(id),
                    Weights = weights,
                    Note = c["note"].AsString(string.Empty),
                    EffectValue = eNode["value"].AsDouble(-1),
                    EffectAmountYen = (long)eNode["amountYen"].AsDouble(0),
                };

                if (!TryParseEffect(eNode["kind"].AsString(), out var effectKind))
                    problems.Add($"{path}.effect.kind \"{eNode["kind"].AsString()}\" は知らない効果");
                else
                    def.EffectKind = effectKind;

                if (def.EffectValue < 0) problems.Add($"{path}.effect.value が無いか負");

                if (def.EffectKind == CareerEffectKind.Congratulatory && def.EffectAmountYen <= 0)
                    problems.Add($"{path}.effect.amountYen が無い。ご祝儀の額が決まらない");

                if (def.EffectKind == CareerEffectKind.DrinkCost &&
                    (def.EffectValue <= 0 || def.EffectValue >= 1))
                    problems.Add($"{path}.effect.value が 0〜1 未満の外。原価が下がらない");

                if (def.EffectKind == CareerEffectKind.CareRecover && def.EffectValue <= 1)
                    problems.Add($"{path}.effect.value が 1 以下。回復量が増えない");

                list.Add(def);
            }

            return list;
        }

        static bool TryParseCareer(string id, out CareerKind kind)
        {
            switch (id)
            {
                case "idol": kind = CareerKind.Idol; return true;
                case "drinkWholesaler": kind = CareerKind.DrinkWholesaler; return true;
                case "publisher": kind = CareerKind.Publisher; return true;
                case "costumeDesigner": kind = CareerKind.CostumeDesigner; return true;
                case "scout": kind = CareerKind.Scout; return true;
                case "counselor": kind = CareerKind.Counselor; return true;
                case "webMarketer": kind = CareerKind.WebMarketer; return true;
                case "distillery": kind = CareerKind.Distillery; return true;
                case "familyBusiness": kind = CareerKind.FamilyBusiness; return true;
                case "marriedRich": kind = CareerKind.MarriedRich; return true;
                default: kind = CareerKind.Idol; return false;
            }
        }

        static bool TryParseEffect(string name, out CareerEffectKind kind)
        {
            switch (name)
            {
                case "drinkCost": kind = CareerEffectKind.DrinkCost; return true;
                case "looksBonus": kind = CareerEffectKind.LooksBonus; return true;
                case "careRecover": kind = CareerEffectKind.CareRecover; return true;
                case "weatherRelief": kind = CareerEffectKind.WeatherRelief; return true;
                case "recruitCap": kind = CareerEffectKind.RecruitCap; return true;
                case "trainingBook": kind = CareerEffectKind.TrainingBook; return true;
                case "premiumBottle": kind = CareerEffectKind.PremiumBottle; return true;
                case "limitedGoods": kind = CareerEffectKind.LimitedGoods; return true;
                case "oneDayManager": kind = CareerEffectKind.OneDayManager; return true;
                case "congratulatory": kind = CareerEffectKind.Congratulatory; return true;
                default: kind = CareerEffectKind.LooksBonus; return false;
            }
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
    }
}
