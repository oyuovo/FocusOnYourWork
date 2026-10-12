using System;
using System.Collections.Generic;

namespace FocusOnYourWork.Core
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

    public sealed class SaveData
    {
        public int Version { get; set; }
        public string SectName { get; set; }
        public long WalletUnits { get; set; }
        public long TotalEffectiveMs { get; set; }
        public List<SessionRecord> History { get; set; }
        public SessionData Pending { get; set; }
        public SectData Sect { get; set; }

        public SaveData()
        {
            Version = 3;
            SectName = "我的宗门";
            History = new List<SessionRecord>();
            Sect = new SectData();
        }
    }

    public static class Rules
    {
        public const long UnitsPerMaterial = 60000;
        public const long GraceMs = 10000;
        public const long MaxSampleMs = 2500;
        public static string Materials(long units) { return (units / (decimal)UnitsPerMaterial).ToString("0.00"); }
    }
}
