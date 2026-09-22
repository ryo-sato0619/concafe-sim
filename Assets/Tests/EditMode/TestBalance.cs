using System.IO;
using Concafe.Simulation.Balance;
using UnityEngine;

namespace Concafe.Tests
{
    /// <summary>テストから実物の balance.json を読む。</summary>
    public static class TestBalance
    {
        public static string Path =>
            System.IO.Path.Combine(Application.dataPath, "Resources", "Data", "balance.json");

        public static string RawJson() => File.ReadAllText(Path);

        public static BalanceData Load() => BalanceLoader.Load(RawJson());
    }
}
