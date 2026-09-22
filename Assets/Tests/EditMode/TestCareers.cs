using System.IO;
using Concafe.Simulation.Balance;
using UnityEngine;

namespace Concafe.Tests
{
    /// <summary>テストから実物の careers.json を読む。</summary>
    public static class TestCareers
    {
        public static string Path =>
            System.IO.Path.Combine(Application.dataPath, "Resources", "Data", "careers.json");

        public static string RawJson() => File.ReadAllText(Path);

        public static CareerSet Load() => CareerLoader.Load(RawJson());
    }
}
