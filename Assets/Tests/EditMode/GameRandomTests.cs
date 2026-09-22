using System.Collections.Generic;
using System.Linq;
using Concafe.Simulation.Rng;
using NUnit.Framework;

namespace Concafe.Tests
{
    public class GameRandomTests
    {
        [Test]
        public void 同じ種からは同じ列が出る()
        {
            var a = new GameRandom(12345);
            var b = new GameRandom(12345);

            for (int i = 0; i < 1000; i++)
                Assert.AreEqual(a.NextULong(), b.NextULong());
        }

        [Test]
        public void 違う種からは違う列が出る()
        {
            var a = new GameRandom(1);
            var b = new GameRandom(2);

            int same = 0;
            for (int i = 0; i < 100; i++)
                if (a.NextULong() == b.NextULong()) same++;

            Assert.AreEqual(0, same);
        }

        [Test]
        public void 状態を保存して復元すると続きが一致する()
        {
            var original = new GameRandom(999);
            for (int i = 0; i < 37; i++) original.NextULong();

            var restored = GameRandom.Restore(original.State);

            for (int i = 0; i < 100; i++)
                Assert.AreEqual(original.NextULong(), restored.NextULong());
        }

        [Test]
        public void NextDouble_は_0以上1未満に収まる()
        {
            var r = new GameRandom(7);

            for (int i = 0; i < 100_000; i++)
            {
                double v = r.NextDouble();
                Assert.GreaterOrEqual(v, 0.0);
                Assert.Less(v, 1.0);
            }
        }

        [Test]
        public void NextInt_は範囲に収まり全ての値が出る()
        {
            var r = new GameRandom(42);
            var seen = new HashSet<int>();

            for (int i = 0; i < 10_000; i++)
            {
                int v = r.NextInt(3, 8);
                Assert.GreaterOrEqual(v, 3);
                Assert.Less(v, 8);
                seen.Add(v);
            }

            CollectionAssert.AreEquivalent(new[] { 3, 4, 5, 6, 7 }, seen.OrderBy(x => x).ToArray());
        }

        [Test]
        public void NextInt_の分布が概ね一様()
        {
            var r = new GameRandom(2024);
            var counts = new int[10];
            const int n = 200_000;

            for (int i = 0; i < n; i++) counts[r.NextInt(0, 10)]++;

            foreach (int c in counts)
                Assert.That(c, Is.EqualTo(n / 10).Within(n / 10 * 0.1),
                    "一様分布から 10% 以上ずれている");
        }

        [Test]
        public void WeightedPick_が重みに従う()
        {
            var r = new GameRandom(555);
            var weights = new double[] { 1, 3, 0, 6 };
            var counts = new int[4];
            const int n = 100_000;

            for (int i = 0; i < n; i++) counts[r.WeightedPick(weights)]++;

            Assert.AreEqual(0, counts[2], "重み 0 は選ばれない");
            Assert.That(counts[0] / (double)n, Is.EqualTo(0.1).Within(0.01));
            Assert.That(counts[1] / (double)n, Is.EqualTo(0.3).Within(0.01));
            Assert.That(counts[3] / (double)n, Is.EqualTo(0.6).Within(0.01));
        }

        [Test]
        public void WeightedPick_は重みが全て零なら_マイナス1()
        {
            var r = new GameRandom(1);
            Assert.AreEqual(-1, r.WeightedPick(new double[] { 0, 0, 0 }));
            Assert.AreEqual(-1, r.WeightedPick(new double[0]));
        }

        [Test]
        public void Chance_の確率が概ね合う()
        {
            var r = new GameRandom(31337);
            int hit = 0;
            const int n = 200_000;

            for (int i = 0; i < n; i++) if (r.Chance(0.25)) hit++;

            Assert.That(hit / (double)n, Is.EqualTo(0.25).Within(0.01));
        }

        [Test]
        public void Chance_の境界は乱数を持ち上げない()
        {
            var r = new GameRandom(1);
            ulong before = r.State;

            Assert.IsFalse(r.Chance(0.0));
            Assert.IsTrue(r.Chance(1.0));

            Assert.AreEqual(before, r.State, "0 と 1 では乱数を消費しない");
        }

        [Test]
        public void NextGaussian_の平均と標準偏差が概ね合う()
        {
            var r = new GameRandom(8080);
            const int n = 200_000;
            double sum = 0, sumSq = 0;

            for (int i = 0; i < n; i++)
            {
                double v = r.NextGaussian(50, 10);
                sum += v;
                sumSq += v * v;
            }

            double mean = sum / n;
            double variance = sumSq / n - mean * mean;

            Assert.That(mean, Is.EqualTo(50.0).Within(0.2));
            Assert.That(System.Math.Sqrt(variance), Is.EqualTo(10.0).Within(0.2));
        }

        [Test]
        public void NextGaussianClamped_が範囲を出ない()
        {
            var r = new GameRandom(606);

            for (int i = 0; i < 50_000; i++)
            {
                double v = r.NextGaussianClamped(50, 100, 0, 100);
                Assert.GreaterOrEqual(v, 0.0);
                Assert.LessOrEqual(v, 100.0);
            }
        }

        [Test]
        public void ストリームは互いに独立している()
        {
            // 設計書 9: 注文処理の呼び出し回数が変わっても天候が変わらないこと。
            var a = RandomStreams.FromSeed(1234);
            var b = RandomStreams.FromSeed(1234);

            // 片方だけ注文の乱数を大量に消費する。
            for (int i = 0; i < 10_000; i++) a.Orders.NextULong();

            for (int i = 0; i < 100; i++)
                Assert.AreEqual(b.Weather.NextULong(), a.Weather.NextULong(),
                    "注文の消費が天候に影響している");
        }

        [Test]
        public void ストリームは互いに違う列を持つ()
        {
            var s = RandomStreams.FromSeed(77);

            var first = new[]
            {
                s.CastGen.NextULong(),
                s.Weather.NextULong(),
                s.Orders.NextULong(),
                s.Events.NextULong(),
            };

            CollectionAssert.AllItemsAreUnique(first);
        }

        [Test]
        public void ストリームを保存して復元できる()
        {
            var s = RandomStreams.FromSeed(2468);
            for (int i = 0; i < 50; i++) { s.CastGen.NextULong(); s.Weather.NextULong(); }

            var restored = RandomStreams.Restore(
                s.CastGen.State, s.Weather.State, s.Orders.State, s.Events.State);

            Assert.AreEqual(s.CastGen.NextULong(), restored.CastGen.NextULong());
            Assert.AreEqual(s.Weather.NextULong(), restored.Weather.NextULong());
            Assert.AreEqual(s.Orders.NextULong(), restored.Orders.NextULong());
            Assert.AreEqual(s.Events.NextULong(), restored.Events.NextULong());
        }
    }
}
