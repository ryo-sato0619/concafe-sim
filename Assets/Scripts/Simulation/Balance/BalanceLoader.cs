using System;
using System.Collections.Generic;
using System.Globalization;
using Concafe.Simulation.Json;
using Concafe.Simulation.Model;

namespace Concafe.Simulation.Balance
{
    public sealed class BalanceException : Exception
    {
        public IReadOnlyList<string> Problems { get; }

        public BalanceException(IReadOnlyList<string> problems)
            : base("balance.json に問題がある:" + Environment.NewLine +
                   "  - " + string.Join(Environment.NewLine + "  - ", problems))
        {
            Problems = problems;
        }
    }

    /// <summary>
    /// balance.json を型付きで読み、同時に妥当性を検査する。
    ///
    /// 検査を読み込みと同じ場所に置いているのは、壊れた定数がシミュレーションに
    /// 流れ込むと、バランスがおかしいのか計算式が間違っているのかを
    /// 切り分けられなくなるためである。ここで落として原因を 1 つに絞る。
    /// </summary>
    public static class BalanceLoader
    {
        public const int SupportedSchemaVersion = 2;

        public static BalanceData Load(string json)
        {
            JsonValue root;
            try
            {
                root = JsonParser.Parse(json);
            }
            catch (JsonParseException e)
            {
                throw new BalanceException(new[] { "JSON として読めない: " + e.Message });
            }

            var problems = new List<string>();
            var data = Read(root, problems);

            Validate(data, problems);

            if (problems.Count > 0) throw new BalanceException(problems);
            return data;
        }

        // ---- 読み取り -------------------------------------------------------

        static BalanceData Read(JsonValue root, List<string> problems)
        {
            var d = new BalanceData
            {
                SchemaVersion = Int(root, "schemaVersion", problems, "schemaVersion"),
            };

            var time = Section(root, "time", problems);
            d.Time = new TimeBalance
            {
                WeeksPerYear = Int(time, "time.weeksPerYear", problems, "weeksPerYear"),
                WeeksPerMonth = Int(time, "time.weeksPerMonth", problems, "weeksPerMonth"),
                YearsPerRun = Int(time, "time.yearsPerRun", problems, "yearsPerRun"),
            };

            var shop = Section(root, "shop", problems);
            d.Shop = new ShopBalance
            {
                InitialCashYen = Yen(shop, "shop.initialCashYen", problems, "initialCashYen"),
                WeeklyFixedCostYen = Yen(shop, "shop.weeklyFixedCostYen", problems, "weeklyFixedCostYen"),
                InitialCastSlots = Int(shop, "shop.initialCastSlots", problems, "initialCastSlots"),
                MaxCastSlots = Int(shop, "shop.maxCastSlots", problems, "maxCastSlots"),
                SlotsPerExpansion = Int(shop, "shop.slotsPerExpansion", problems, "slotsPerExpansion"),
                FixedCostPerExpansionYen = Yen(shop, "shop.fixedCostPerExpansionYen", problems, "fixedCostPerExpansionYen"),
            };

            var mgr = Section(root, "manager", problems);
            d.Manager = new ManagerBalance
            {
                ApPerWeek = Int(mgr, "manager.apPerWeek", problems, "apPerWeek"),
                TrainCostAp = Int(mgr, "manager.trainCostAp", problems, "trainCostAp"),
                TrainBaseGain = Num(mgr, "manager.trainBaseGain", problems, "trainBaseGain"),
                CareCostAp = Int(mgr, "manager.careCostAp", problems, "careCostAp"),
                CareRecover = Num(mgr, "manager.careRecover", problems, "careRecover"),
                TrustPerCare = Num(mgr, "manager.trustPerCare", problems, "trustPerCare"),
                InterviewCostAp = Int(mgr, "manager.interviewCostAp", problems, "interviewCostAp"),
                DailyWorkGain = Num(mgr, "manager.dailyWorkGain", problems, "dailyWorkGain"),
            };

            var vis = Section(root, "visitors", problems);
            d.Visitors = new VisitorBalance
            {
                RegularBase = Num(vis, "visitors.regularBase", problems, "regularBase"),
                FameBase = Num(vis, "visitors.fameBase", problems, "fameBase"),
                LooksBonusMax = Num(vis, "visitors.looksBonusMax", problems, "looksBonusMax"),
            };

            var weatherNode = Section(root, "weather", problems);
            var weather = new Dictionary<string, WeatherBalance>(StringComparer.Ordinal);
            foreach (var kv in weatherNode.Members())
            {
                weather[kv.Key] = new WeatherBalance
                {
                    FameFactor = Num(kv.Value, $"weather.{kv.Key}.fameFactor", problems, "fameFactor"),
                    StormFactor = Num(kv.Value, $"weather.{kv.Key}.stormFactor", problems, "stormFactor"),
                    FatigueMul = Num(kv.Value, $"weather.{kv.Key}.fatigueMul", problems, "fatigueMul"),
                };
            }
            d.Weather = weather;

            var ord = Section(root, "orders", problems);
            d.Orders = new OrderBalance
            {
                TalkFloor = Num(ord, "orders.talkFloor", problems, "talkFloor"),
                TalkSpan = Num(ord, "orders.talkSpan", problems, "talkSpan"),
                ChekiRate = Num(ord, "orders.chekiRate", problems, "chekiRate"),
                CastDrinkRate = Num(ord, "orders.castDrinkRate", problems, "castDrinkRate"),
                BottleRate = Num(ord, "orders.bottleRate", problems, "bottleRate"),
            };

            var items = Section(root, "items", problems);
            d.Items = new ItemBalance
            {
                SoftDrink = ReadItem(items, "items.softDrink", problems, "softDrink"),
                Alcohol = ReadItem(items, "items.alcohol", problems, "alcohol"),
                ChekiFilm = ReadItem(items, "items.chekiFilm", problems, "chekiFilm"),
                Limited = ReadItem(items, "items.limited", problems, "limited"),
            };

            d.CastDrinkPriceYen = Yen(Section(root, "castDrink", problems),
                                      "castDrink.priceYen", problems, "priceYen");

            var bottles = Section(root, "bottles", problems);
            d.Bottles = new BottleBalance
            {
                TierPowerTalkWeight = Num(bottles, "bottles.tierPowerTalkWeight", problems, "tierPowerTalkWeight"),
                TierPowerAlcoholWeight = Num(bottles, "bottles.tierPowerAlcoholWeight", problems, "tierPowerAlcoholWeight"),
                Alcoholic = ReadBottleTiers(bottles["alcoholic"], "bottles.alcoholic", problems),
                NonAlcoholic = ReadBottleTiers(bottles["nonAlcoholic"], "bottles.nonAlcoholic", problems),
            };

            var wage = Section(root, "wage", problems);
            d.Wage = new WageBalance
            {
                DailyBaseYen = Yen(wage, "wage.dailyBaseYen", problems, "dailyBaseYen"),
                DailyStatCoefYen = Yen(wage, "wage.dailyStatCoefYen", problems, "dailyStatCoefYen"),
                CommissionRate = Num(wage, "wage.commissionRate", problems, "commissionRate"),
            };

            var fat = Section(root, "fatigue", problems);
            d.Fatigue = new FatigueBalance
            {
                Base = Num(fat, "fatigue.base", problems, "base"),
                PerGuest = Num(fat, "fatigue.perGuest", problems, "perGuest"),
                PerDrink = Num(fat, "fatigue.perDrink", problems, "perDrink"),
                PerBottleTier = Num(fat, "fatigue.perBottleTier", problems, "perBottleTier"),
                RestRecover = Num(fat, "fatigue.restRecover", problems, "restRecover"),
                TierLimits = NumList(fat["tierLimits"], "fatigue.tierLimits", problems),
            };

            d.TrustRelief = Num(Section(root, "trust", problems), "trust.relief", problems, "relief");

            var dep = Section(root, "departure", problems);
            d.Departure = new DepartureBalance
            {
                WarnRatio = Num(dep, "departure.warnRatio", problems, "warnRatio"),
                DefectThreshold = Num(dep, "departure.defectThreshold", problems, "defectThreshold"),
                AntiSpread = Num(dep, "departure.antiSpread", problems, "antiSpread"),
            };

            d.StockLostRegular = Num(Section(root, "stock", problems),
                                     "stock.lostRegular", problems, "lostRegular");

            var sc = Section(root, "scandal", problems);
            d.Scandal = new ScandalBalance
            {
                Base = Num(sc, "scandal.base", problems, "base"),
                PerDrink = Num(sc, "scandal.perDrink", problems, "perDrink"),
                MoraleGuard = Num(sc, "scandal.moraleGuard", problems, "moraleGuard"),
            };

            var recruitNode = root["recruit"];
            var recruit = new List<RecruitTier>();
            if (recruitNode.Type != JsonValue.Kind.Array)
            {
                problems.Add("recruit が配列ではない");
            }
            else
            {
                int i = 0;
                foreach (var t in recruitNode.Items())
                {
                    string path = $"recruit[{i}]";
                    recruit.Add(new RecruitTier
                    {
                        FeeYen = Yen(t, path + ".feeYen", problems, "feeYen"),
                        Candidates = Int(t, path + ".candidates", problems, "candidates"),
                        CapMean = Num(t, path + ".capMean", problems, "capMean"),
                        CapSpread = Num(t, path + ".capSpread", problems, "capSpread"),
                    });
                    i++;
                }
            }
            d.Recruit = recruit;

            var gen = Section(root, "castGen", problems);
            d.CastGen = new CastGenBalance
            {
                MinAge = Int(gen, "castGen.minAge", problems, "minAge"),
                MaxAge = Int(gen, "castGen.maxAge", problems, "maxAge"),
                StaminaTierWeights = NumList(gen["staminaTierWeights"], "castGen.staminaTierWeights", problems),
                InitialStatMean = Num(gen, "castGen.initialStatMean", problems, "initialStatMean"),
                InitialStatSpread = Num(gen, "castGen.initialStatSpread", problems, "initialStatSpread"),
                ScandalResistMean = Num(gen, "castGen.scandalResistMean", problems, "scandalResistMean"),
                ScandalResistSpread = Num(gen, "castGen.scandalResistSpread", problems, "scandalResistSpread"),
                GrowthRateMean = Num(gen, "castGen.growthRateMean", problems, "growthRateMean"),
                GrowthRateSpread = Num(gen, "castGen.growthRateSpread", problems, "growthRateSpread"),
                StatMax = Num(gen, "castGen.statMax", problems, "statMax"),
            };

            var app = Section(root, "appearance", problems);
            d.Appearance = new AppearanceBalance
            {
                BodyVariants = Int(app, "appearance.bodyVariants", problems, "bodyVariants"),
                DressVariants = Int(app, "appearance.dressVariants", problems, "dressVariants"),
                HairBackVariants = Int(app, "appearance.hairBackVariants", problems, "hairBackVariants"),
                HairFrontVariants = Int(app, "appearance.hairFrontVariants", problems, "hairFrontVariants"),
                EyeVariants = Int(app, "appearance.eyeVariants", problems, "eyeVariants"),
                AccessoryVariants = Int(app, "appearance.accessoryVariants", problems, "accessoryVariants"),
                SkinColors = Int(app, "appearance.skinColors", problems, "skinColors"),
                DressColors = Int(app, "appearance.dressColors", problems, "dressColors"),
                HairColors = Int(app, "appearance.hairColors", problems, "hairColors"),
                EyeColors = Int(app, "appearance.eyeColors", problems, "eyeColors"),
            };

            return d;
        }

        static ItemPrice ReadItem(JsonValue parent, string path, List<string> problems, string key)
        {
            var node = parent[key];
            if (node.IsNull) problems.Add($"{path} が無い");
            return new ItemPrice
            {
                CostYen = Yen(node, path + ".costYen", problems, "costYen"),
                PriceYen = Yen(node, path + ".priceYen", problems, "priceYen"),
            };
        }

        static IReadOnlyList<BottleTierBalance> ReadBottleTiers(
            JsonValue node, string path, List<string> problems)
        {
            var list = new List<BottleTierBalance>();
            if (node.Type != JsonValue.Kind.Array)
            {
                problems.Add($"{path} が配列ではない");
                return list;
            }

            int i = 0;
            foreach (var t in node.Items())
            {
                string p = $"{path}[{i}]";
                list.Add(new BottleTierBalance
                {
                    PriceYen = Yen(t, p + ".priceYen", problems, "priceYen"),
                    CostYen = Yen(t, p + ".costYen", problems, "costYen"),
                    Unlock = Num(t, p + ".unlock", problems, "unlock"),
                    FatigueTier = Int(t, p + ".fatigueTier", problems, "fatigueTier"),
                });
                i++;
            }
            return list;
        }

        // ---- 検査 -----------------------------------------------------------

        static void Validate(BalanceData d, List<string> problems)
        {
            if (d.SchemaVersion != SupportedSchemaVersion)
                problems.Add($"schemaVersion が {d.SchemaVersion}。対応しているのは {SupportedSchemaVersion}");

            // 時間
            if (d.Time.WeeksPerYear <= 0) problems.Add("time.weeksPerYear が 0 以下");
            if (d.Time.WeeksPerMonth <= 0) problems.Add("time.weeksPerMonth が 0 以下");
            if (d.Time.YearsPerRun <= 0) problems.Add("time.yearsPerRun が 0 以下");
            if (d.Time.WeeksPerMonth > 0 && d.Time.WeeksPerYear % d.Time.WeeksPerMonth != 0)
                problems.Add("time.weeksPerYear が weeksPerMonth で割り切れない。月の境界が定まらない");

            // 店舗
            if (d.Shop.InitialCashYen <= 0) problems.Add("shop.initialCashYen が 0 以下");
            if (d.Shop.WeeklyFixedCostYen < 0) problems.Add("shop.weeklyFixedCostYen が負");
            if (d.Shop.InitialCastSlots <= 0) problems.Add("shop.initialCastSlots が 0 以下");
            if (d.Shop.MaxCastSlots < d.Shop.InitialCastSlots)
                problems.Add("shop.maxCastSlots が initialCastSlots より小さい");
            if (d.Shop.SlotsPerExpansion <= 0) problems.Add("shop.slotsPerExpansion が 0 以下");

            // 店長 AP
            if (d.Manager.ApPerWeek <= 0) problems.Add("manager.apPerWeek が 0 以下");
            if (d.Manager.TrainCostAp <= 0) problems.Add("manager.trainCostAp が 0 以下");
            if (d.Manager.CareCostAp <= 0) problems.Add("manager.careCostAp が 0 以下");
            if (d.Manager.CareRecover <= 0) problems.Add("manager.careRecover が 0 以下");
            if (d.Manager.TrustPerCare <= 0) problems.Add("manager.trustPerCare が 0 以下");
            if (d.Manager.TrainCostAp > d.Manager.ApPerWeek)
                problems.Add("manager.trainCostAp が週次 AP を超えている。特訓が一度もできない");
            if (d.Manager.CareCostAp > d.Manager.ApPerWeek)
                problems.Add("manager.careCostAp が週次 AP を超えている。ケアが一度もできない");

            // 来客
            if (d.Visitors.RegularBase < 0) problems.Add("visitors.regularBase が負");
            if (d.Visitors.FameBase < 0) problems.Add("visitors.fameBase が負");
            if (d.Visitors.LooksBonusMax < 0) problems.Add("visitors.looksBonusMax が負");

            // 天候
            foreach (var name in WeatherNames.All)
            {
                if (!d.Weather.ContainsKey(name))
                {
                    problems.Add($"weather.{name} が無い");
                    continue;
                }
                var w = d.Weather[name];
                if (w.FameFactor < 0 || w.FameFactor > 1)
                    problems.Add($"weather.{name}.fameFactor が 0〜1 の外");
                if (w.StormFactor < 0 || w.StormFactor > 1)
                    problems.Add($"weather.{name}.stormFactor が 0〜1 の外");
                if (w.FatigueMul <= 0)
                    problems.Add($"weather.{name}.fatigueMul が 0 以下");
            }

            // 設計書 4.1: 雨は固定客を減らさない。ここが 1 未満だと
            // 「集客は天候に影響されない」という要件が静かに破れる。
            if (d.Weather.TryGetValue(WeatherNames.Rain, out var rain) && rain.StormFactor < 1.0)
                problems.Add("weather.rain.stormFactor が 1 未満。雨で固定客が減ってしまう（設計書 4.1）");

            // 注文
            Rate(d.Orders.ChekiRate, "orders.chekiRate", problems);
            Rate(d.Orders.CastDrinkRate, "orders.castDrinkRate", problems);
            Rate(d.Orders.BottleRate, "orders.bottleRate", problems);
            if (d.Orders.TalkFloor < 0) problems.Add("orders.talkFloor が負");
            if (d.Orders.TalkSpan < 0) problems.Add("orders.talkSpan が負");

            // 商品
            Margin(d.Items.SoftDrink, "items.softDrink", problems);
            Margin(d.Items.Alcohol, "items.alcohol", problems);
            Margin(d.Items.ChekiFilm, "items.chekiFilm", problems);
            Margin(d.Items.Limited, "items.limited", problems);
            if (d.CastDrinkPriceYen <= 0) problems.Add("castDrink.priceYen が 0 以下");

            // ボトル
            ValidateBottles(d.Bottles.Alcoholic, "bottles.alcoholic", true, problems);
            ValidateBottles(d.Bottles.NonAlcoholic, "bottles.nonAlcoholic", false, problems);

            double weightSum = d.Bottles.TierPowerTalkWeight + d.Bottles.TierPowerAlcoholWeight;
            if (Math.Abs(weightSum - 1.0) > 1e-9)
                problems.Add($"bottles の tierPower 重みの合計が {Fmt(weightSum)}。1.0 でないと tierPower が 0〜1 に収まらない");
            if (d.Bottles.TierPowerAlcoholWeight <= 0)
                problems.Add("bottles.tierPowerAlcoholWeight が 0 以下。20 歳の覚醒が起きなくなる（不変条件 4）");

            // 給料
            if (d.Wage.DailyBaseYen < 0) problems.Add("wage.dailyBaseYen が負");
            if (d.Wage.DailyStatCoefYen < 0) problems.Add("wage.dailyStatCoefYen が負");
            Rate(d.Wage.CommissionRate, "wage.commissionRate", problems);

            // 疲労
            if (d.Fatigue.Base < 0) problems.Add("fatigue.base が負");
            if (d.Fatigue.PerGuest < 0) problems.Add("fatigue.perGuest が負");
            if (d.Fatigue.PerDrink < 0) problems.Add("fatigue.perDrink が負");
            if (d.Fatigue.PerBottleTier < 0) problems.Add("fatigue.perBottleTier が負");
            if (d.Fatigue.RestRecover < 0) problems.Add("fatigue.restRecover が負");

            var limits = d.Fatigue.TierLimits;
            if (limits.Count == 0)
            {
                problems.Add("fatigue.tierLimits が空");
            }
            else
            {
                for (int i = 0; i < limits.Count; i++)
                {
                    if (limits[i] <= 0)
                        problems.Add($"fatigue.tierLimits[{i}] が 0 以下");
                    if (i > 0 && limits[i] <= limits[i - 1])
                        problems.Add($"fatigue.tierLimits が単調増加でない（[{i}] = {Fmt(limits[i])}）");
                }
            }

            // 信頼度
            if (d.TrustRelief < 0 || d.TrustRelief >= 1)
                problems.Add("trust.relief が 0〜1 未満の外。1 以上だと疲労が溜まらなくなる");

            // 離脱
            if (d.Departure.WarnRatio <= 0 || d.Departure.WarnRatio >= 1)
                problems.Add("departure.warnRatio が 0〜1 の外。予兆が出ないか、常に出る");
            if (d.Departure.DefectThreshold < 0)
                problems.Add("departure.defectThreshold が負");
            if (d.Departure.AntiSpread < 0)
                problems.Add("departure.antiSpread が負");

            // スキャンダル
            Rate(d.Scandal.Base, "scandal.base", problems);
            if (d.Scandal.PerDrink < 0) problems.Add("scandal.perDrink が負");
            Rate(d.Scandal.MoraleGuard, "scandal.moraleGuard", problems);

            // 求人
            if (d.Recruit.Count == 0) problems.Add("recruit が空。キャストを採用できない");
            for (int i = 0; i < d.Recruit.Count; i++)
            {
                var t = d.Recruit[i];
                if (t.FeeYen < 0) problems.Add($"recruit[{i}].feeYen が負");
                if (t.Candidates <= 0) problems.Add($"recruit[{i}].candidates が 0 以下");
                if (t.CapSpread < 0) problems.Add($"recruit[{i}].capSpread が負");
                if (i > 0)
                {
                    if (t.FeeYen <= d.Recruit[i - 1].FeeYen)
                        problems.Add($"recruit[{i}].feeYen が前のティア以下。費用順に並んでいない");
                    if (t.CapMean < d.Recruit[i - 1].CapMean)
                        problems.Add($"recruit[{i}].capMean が前のティアより低い。高い求人費が損になる");
                }
            }

            // キャスト生成
            var g = d.CastGen;
            if (g.MinAge < 18) problems.Add("castGen.minAge が 18 未満");
            if (g.MaxAge < g.MinAge) problems.Add("castGen.maxAge が minAge より小さい");
            if (g.StatMax <= 0) problems.Add("castGen.statMax が 0 以下");
            if (g.InitialStatMean < 0 || g.InitialStatMean > g.StatMax)
                problems.Add("castGen.initialStatMean が 0〜statMax の外");
            if (g.GrowthRateMean <= 0) problems.Add("castGen.growthRateMean が 0 以下");

            if (g.StaminaTierWeights.Count != d.Fatigue.TierCount)
                problems.Add($"castGen.staminaTierWeights の数 ({g.StaminaTierWeights.Count}) が " +
                             $"fatigue.tierLimits の数 ({d.Fatigue.TierCount}) と違う");

            double tierWeightSum = 0;
            for (int i = 0; i < g.StaminaTierWeights.Count; i++)
            {
                if (g.StaminaTierWeights[i] < 0)
                    problems.Add($"castGen.staminaTierWeights[{i}] が負");
                tierWeightSum += g.StaminaTierWeights[i];
            }
            if (g.StaminaTierWeights.Count > 0 && tierWeightSum <= 0)
                problems.Add("castGen.staminaTierWeights の合計が 0。疲労 Tier を抽選できない");

            // 外見
            var a = d.Appearance;
            Positive(a.BodyVariants, "appearance.bodyVariants", problems);
            Positive(a.DressVariants, "appearance.dressVariants", problems);
            Positive(a.HairBackVariants, "appearance.hairBackVariants", problems);
            Positive(a.HairFrontVariants, "appearance.hairFrontVariants", problems);
            Positive(a.EyeVariants, "appearance.eyeVariants", problems);
            Positive(a.SkinColors, "appearance.skinColors", problems);
            Positive(a.DressColors, "appearance.dressColors", problems);
            Positive(a.HairColors, "appearance.hairColors", problems);
            Positive(a.EyeColors, "appearance.eyeColors", problems);
            // accessoryVariants は 0 でよい（アクセサリ無しのみ）。
            if (a.AccessoryVariants < 0) problems.Add("appearance.accessoryVariants が負");
        }

        static void Positive(int v, string path, List<string> problems)
        {
            if (v <= 0) problems.Add($"{path} が 0 以下");
        }

        static void ValidateBottles(IReadOnlyList<BottleTierBalance> tiers, string path,
                                    bool alcoholic, List<string> problems)
        {
            if (tiers.Count == 0)
            {
                problems.Add($"{path} が空");
                return;
            }

            if (tiers[0].Unlock > 0)
                problems.Add($"{path}[0].unlock が 0 より大きい。最安の段階は常に解禁されている必要がある");

            for (int i = 0; i < tiers.Count; i++)
            {
                var t = tiers[i];
                if (t.PriceYen <= 0) problems.Add($"{path}[{i}].priceYen が 0 以下");
                if (t.CostYen < 0) problems.Add($"{path}[{i}].costYen が負");
                if (t.CostYen >= t.PriceYen)
                    problems.Add($"{path}[{i}] の原価が売価以上。売るほど赤字になる");
                if (t.Unlock < 0 || t.Unlock > 1)
                    problems.Add($"{path}[{i}].unlock が 0〜1 の外。tierPower は 0〜1 の値");

                if (alcoholic)
                {
                    if (t.FatigueTier <= 0)
                        problems.Add($"{path}[{i}].fatigueTier が 0 以下。アルコールは酒疲労を生む必要がある");
                }
                else if (t.FatigueTier != 0)
                {
                    // 不変条件: ノンアルコールは酒疲労を生まない（設計書 4.5）。
                    problems.Add($"{path}[{i}].fatigueTier が 0 でない。ノンアルコールは酒疲労を生まない");
                }

                if (i > 0)
                {
                    if (t.PriceYen <= tiers[i - 1].PriceYen)
                        problems.Add($"{path}[{i}].priceYen が前の段階以下。段階が価格順に並んでいない");
                    if (t.Unlock <= tiers[i - 1].Unlock)
                        problems.Add($"{path}[{i}].unlock が前の段階以下。高い段階ほど解禁が遅い必要がある");
                }
            }
        }

        static void Margin(ItemPrice p, string path, List<string> problems)
        {
            if (p.PriceYen <= 0) problems.Add($"{path}.priceYen が 0 以下");
            if (p.CostYen < 0) problems.Add($"{path}.costYen が負");
            if (p.CostYen >= p.PriceYen) problems.Add($"{path} の原価が売価以上。売るほど赤字になる");
        }

        static void Rate(double v, string path, List<string> problems)
        {
            if (v < 0 || v > 1) problems.Add($"{path} が 0〜1 の外 ({Fmt(v)})");
        }

        // ---- 読み取りの補助 -------------------------------------------------

        static JsonValue Section(JsonValue root, string key, List<string> problems)
        {
            var node = root[key];
            if (node.Type != JsonValue.Kind.Object)
            {
                problems.Add($"{key} が無いかオブジェクトではない");
                return JsonValue.Null;
            }
            return node;
        }

        static double Num(JsonValue parent, string path, List<string> problems, string key)
        {
            var node = parent[key];
            if (node.Type != JsonValue.Kind.Number)
            {
                problems.Add($"{path} が無いか数値ではない");
                return 0;
            }
            return node.AsDouble();
        }

        static int Int(JsonValue parent, string path, List<string> problems, string key)
        {
            var node = parent[key];
            if (node.Type != JsonValue.Kind.Number)
            {
                problems.Add($"{path} が無いか数値ではない");
                return 0;
            }
            double v = node.AsDouble();
            if (v != Math.Floor(v))
            {
                problems.Add($"{path} が整数ではない ({Fmt(v)})");
                return 0;
            }
            return (int)v;
        }

        /// <summary>
        /// 金額。不変条件 8 により long で保持する。小数の金額は受け付けない。
        /// </summary>
        static long Yen(JsonValue parent, string path, List<string> problems, string key)
        {
            var node = parent[key];
            if (node.Type != JsonValue.Kind.Number)
            {
                problems.Add($"{path} が無いか数値ではない");
                return 0;
            }
            double v = node.AsDouble();
            if (v != Math.Floor(v))
            {
                problems.Add($"{path} が整数ではない ({Fmt(v)})。金額は整数の円で扱う（不変条件 8）");
                return 0;
            }
            return (long)v;
        }

        static IReadOnlyList<double> NumList(JsonValue node, string path, List<string> problems)
        {
            var list = new List<double>();
            if (node.Type != JsonValue.Kind.Array)
            {
                problems.Add($"{path} が無いか配列ではない");
                return list;
            }

            int i = 0;
            foreach (var v in node.Items())
            {
                if (v.Type != JsonValue.Kind.Number)
                {
                    problems.Add($"{path}[{i}] が数値ではない");
                    list.Add(0);
                }
                else
                {
                    list.Add(v.AsDouble());
                }
                i++;
            }
            return list;
        }

        static string Fmt(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);
    }
}
