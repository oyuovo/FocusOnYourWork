using System;
using System.Collections.Generic;

namespace FocusIsland.Core
{
    public enum Presence { Unknown, Allowed, Away }

    public sealed class SessionData
    {
        public string Id { get; set; }
        public string Task { get; set; }
        public string StartedUtc { get; set; }
        public long TargetMs { get; set; }
        public long EffectiveMs { get; set; }
        public bool Challenge { get; set; }
        public bool BonusEligible { get; set; }
        public int Distractions { get; set; }
        public bool Paused { get; set; }
    }

    public sealed class SessionRecord
    {
        public string Id { get; set; }
        public string Task { get; set; }
        public string StartedUtc { get; set; }
        public string EndedUtc { get; set; }
        public long EffectiveMs { get; set; }
        public long RewardUnits { get; set; }
        public bool ChallengeWon { get; set; }
        public int Distractions { get; set; }
        public bool Recovered { get; set; }
    }

    public sealed class Building
    {
        public int Plot { get; set; }
        public string Kind { get; set; }
        public string Color { get; set; }
    }

    public sealed class SaveData
    {
        public int Version { get; set; }
        public string IslandName { get; set; }
        public string Theme { get; set; }
        public long WalletUnits { get; set; }
        public long TotalEffectiveMs { get; set; }
        public List<Building> Buildings { get; set; }
        public List<SessionRecord> History { get; set; }
        public SessionData Pending { get; set; }

        public SaveData()
        {
            Version = 1;
            IslandName = "我的专注岛";
            Theme = "sage";
            Buildings = new List<Building>();
            History = new List<SessionRecord>();
        }
    }

    public static class Rules
    {
        public const long UnitsPerMaterial = 60000;
        public const long GraceMs = 10000;
        public const long MaxSampleMs = 2500;
        public const int PlotCount = 9;
        public static long Price(string kind)
        {
            switch (kind)
            {
                case "tree": return 5 * UnitsPerMaterial;
                case "tent": return 15 * UnitsPerMaterial;
                case "cottage": return 25 * UnitsPerMaterial;
                default: throw new ArgumentException("未知建筑类型。");
            }
        }
        public static string Name(string kind)
        {
            return kind == "tree" ? "一棵树" : kind == "tent" ? "露营地" : "海边小屋";
        }
        public static string Materials(long units) { return (units / (decimal)UnitsPerMaterial).ToString("0.00"); }
    }
}
