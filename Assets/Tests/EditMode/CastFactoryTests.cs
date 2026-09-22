using System.Collections.Generic;
using System.Linq;
using Concafe.Simulation.Balance;
using Concafe.Simulation.Model;
using Concafe.Simulation.Rng;
using Concafe.Simulation.Rules;
using NUnit.Framework;

namespace Concafe.Tests
{
    public class CastFactoryTests
    {
        BalanceData balance;

        [SetUp]
        public void SetUp() => balance = TestBalance.Load();

        Applicant Gen(ulong seed, int tier = 0, int serial = 1) =>
            CastFactory.GenerateApplicant(new GameRandom(seed), balance, tier, serial);

        [Test]
        public void 同じ種と同じ通し番号なら同じ候補になる()
        {
            var a = Gen(777);
            var b = Gen(777);

            Assert.AreEqual(a.Id, b.Id);
            Assert.AreEqual(a.Age, b.Age);
            Assert.AreEqual(a.BirthWeekOfYear, b.BirthWeekOfYear);
            Assert.AreEqual(a.StaminaTier, b.StaminaTier);
            Assert.AreEqual(a.ScandalResist, b.ScandalResist);

            foreach (var kind in Stats.AllKinds)
            {
                Assert.AreEqual(a.Cap[kind], b.Cap[kind]);
                Assert.AreEqual(a.InitialStats[kind], b.InitialStats[kind]);
                Assert.AreEqual(a.GrowthRate[kind], b.GrowthRate[kind]);
            }
        }

        [Test]
        public void 初期年齢が十八から二十五に収まる()
        {
            for (ulong seed = 1; seed <= 300; seed++)
            {
                var a = Gen(seed);
                Assert.GreaterOrEqual(a.Age, balance.CastGen.MinAge);
                Assert.LessOrEqual(a.Age, balance.CastGen.MaxAge);
            }
        }

        [Test]
        public void 誕生週が年内に散らばる()
        {
            var weeks = new HashSet<int>();
            for (ulong seed = 1; seed <= 500; seed++)
            {
                int w = Gen(seed).BirthWeekOfYear;
                Assert.GreaterOrEqual(w, 0);
                Assert.Less(w, balance.Time.WeeksPerYear);
                weeks.Add(w);
            }

            // 全員が同時に誕生日を迎える作りになっていないこと（設計書 2）。
            Assert.Greater(weeks.Count, 30, "誕生週が偏っている");
        }

        [Test]
        public void 疲労_Tier_が一から五に収まり全段階が出る()
        {
            var tiers = new HashSet<int>();
            for (ulong seed = 1; seed <= 1000; seed++)
            {
                int t = Gen(seed).StaminaTier;
                Assert.GreaterOrEqual(t, 1);
                Assert.LessOrEqual(t, balance.Fatigue.TierCount);
                tiers.Add(t);
            }

            CollectionAssert.AreEquivalent(new[] { 1, 2, 3, 4, 5 }, tiers.OrderBy(x => x));
        }

        [Test]
        public void 初期値が上限を超えない()
        {
            for (ulong seed = 1; seed <= 500; seed++)
            {
                var a = Gen(seed);
                foreach (var kind in Stats.AllKinds)
                    Assert.LessOrEqual(a.InitialStats[kind], a.Cap[kind], $"seed={seed} {kind}");
            }
        }

        [Test]
        public void 上限が範囲に収まる()
        {
            double max = balance.CastGen.StatMax;
            for (ulong seed = 1; seed <= 500; seed++)
            {
                var a = Gen(seed);
                foreach (var kind in Stats.AllKinds)
                {
                    Assert.GreaterOrEqual(a.Cap[kind], 1.0);
                    Assert.LessOrEqual(a.Cap[kind], max);
                }
            }
        }

        [Test]
        public void 成長率が零にならない()
        {
            for (ulong seed = 1; seed <= 500; seed++)
            {
                var a = Gen(seed);
                foreach (var kind in Stats.AllKinds)
                    Assert.GreaterOrEqual(a.GrowthRate[kind], 0.05,
                        "成長率 0 だと永久に伸びない");
            }
        }

        [Test]
        public void 求人費が高いほど上限の平均が上がる()
        {
            var means = new double[balance.Recruit.Count];

            for (int tier = 0; tier < balance.Recruit.Count; tier++)
            {
                double sum = 0;
                int n = 0;
                for (ulong seed = 1; seed <= 400; seed++)
                {
                    var a = Gen(seed, tier);
                    foreach (var kind in Stats.AllKinds) { sum += a.Cap[kind]; n++; }
                }
                means[tier] = sum / n;
            }

            for (int i = 1; i < means.Length; i++)
                Assert.Greater(means[i], means[i - 1],
                    $"ティア {i} の上限平均がティア {i - 1} を上回っていない");
        }

        [Test]
        public void 隠しパラメータが零から百に収まる()
        {
            for (ulong seed = 1; seed <= 500; seed++)
            {
                int r = Gen(seed).ScandalResist;
                Assert.GreaterOrEqual(r, 0);
                Assert.LessOrEqual(r, 100);
            }
        }

        [Test]
        public void 外見が設定の範囲に収まる()
        {
            var app = balance.Appearance;

            for (ulong seed = 1; seed <= 500; seed++)
            {
                var look = Gen(seed).Look;

                Assert.That(look.Body, Is.InRange(0, app.BodyVariants - 1));
                Assert.That(look.Dress, Is.InRange(0, app.DressVariants - 1));
                Assert.That(look.HairBack, Is.InRange(0, app.HairBackVariants - 1));
                Assert.That(look.HairFront, Is.InRange(0, app.HairFrontVariants - 1));
                Assert.That(look.Eye, Is.InRange(0, app.EyeVariants - 1));
                Assert.That(look.Accessory, Is.InRange(0, app.AccessoryVariants),
                    "0 はアクセサリ無し");

                Assert.That(look.SkinColor, Is.InRange(0, app.SkinColors - 1));
                Assert.That(look.DressColor, Is.InRange(0, app.DressColors - 1));
                Assert.That(look.HairColor, Is.InRange(0, app.HairColors - 1));
                Assert.That(look.EyeColor, Is.InRange(0, app.EyeColors - 1));
            }
        }

        [Test]
        public void 外見が個体ごとに散らばる()
        {
            var seen = new HashSet<string>();
            for (ulong seed = 1; seed <= 300; seed++)
            {
                var l = Gen(seed).Look;
                seen.Add($"{l.Body}/{l.Dress}/{l.HairBack}/{l.HairFront}/{l.Eye}/" +
                         $"{l.Accessory}/{l.SkinColor}/{l.DressColor}/{l.HairColor}/{l.EyeColor}");
            }

            Assert.Greater(seen.Count, 250, "パーツ合成の組み合わせが偏っている");
        }

        // ---- 採用 ---------------------------------------------------------

        [Test]
        public void 採用で候補の値が引き継がれる()
        {
            var a = Gen(123);
            var c = CastFactory.Hire(a, "ハナコ", balance, EasterEggSet.Empty, currentWeek: 42);

            Assert.AreEqual(a.Id, c.Id);
            Assert.AreEqual("ハナコ", c.StageName);
            Assert.AreEqual(a.Age, c.Age);
            Assert.AreEqual(a.BirthWeekOfYear, c.BirthWeekOfYear);
            Assert.AreEqual(a.StaminaTier, c.StaminaTier);
            Assert.AreEqual(a.ScandalResist, c.ScandalResist);
            Assert.AreEqual(42, c.HiredWeek);

            foreach (var kind in Stats.AllKinds)
            {
                Assert.AreEqual(a.InitialStats[kind], c.Current[kind]);
                Assert.AreEqual(a.Cap[kind], c.Cap[kind]);
                Assert.AreEqual(a.GrowthRate[kind], c.GrowthRate[kind]);
            }
        }

        [Test]
        public void 採用直後は疲労も信頼度も売上も零()
        {
            var c = CastFactory.Hire(Gen(5), "ハナコ", balance, EasterEggSet.Empty, 0);

            Assert.AreEqual(0.0, c.Fatigue);
            Assert.AreEqual(0.0, c.Trust, "不変条件 17: 信頼度は素質ではない");
            Assert.AreEqual(0L, c.LifetimeSalesYen);
        }

        [Test]
        public void イースターエッグが無くても採用できる()
        {
            var c = CastFactory.Hire(Gen(5), "ニシキ", balance, null, 0);
            Assert.IsNull(c.EasterEggId);
        }
    }
}
