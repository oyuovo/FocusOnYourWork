using System;
using System.Collections.Generic;
using System.Linq;

namespace FocusOnYourWork.Core
{
    public sealed class Cultivator
    {
        public string Id { get; set; }
        public long CultivationMs { get; set; }
        public bool Active { get; set; }
        public string JoinedUtc { get; set; }
        public string AscendedUtc { get; set; }
        public string LastTask { get; set; }
    }
    public sealed class DrawReceipt
    {
        public string Id { get; set; }
        public string CharacterId { get; set; }
        public bool Duplicate { get; set; }
        public bool Choice { get; set; }
    }
    public sealed class SectData
    {
        public int Capacity { get; set; }
        public int MountainLevel { get; set; }
        public int DuplicateStreak { get; set; }
        public string PendingDrawId { get; set; }
        public string SceneTheme { get; set; }
        public string UiTheme { get; set; }
        public List<Cultivator> Roster { get; set; }
        public List<DrawReceipt> Draws { get; set; }
        public List<string> Memorials { get; set; }
        public SectData()
        {
            Capacity = 1; MountainLevel = 1; SceneTheme = "default"; UiTheme = "default";
            Roster = new List<Cultivator>(); Draws = new List<DrawReceipt>(); Memorials = new List<string>();
        }
    }
    public sealed class CultivatorDefinition
    {
        public string Id { get; private set; }
        public string Name { get; private set; }
        public string Path { get; private set; }
        public string ThemeName { get; private set; }
        public string Story { get; private set; }
        public CultivatorDefinition(string id, string name, string path, string theme, string story)
        { Id = id; Name = name; Path = path; ThemeName = theme; Story = story; }
    }
    public static class SectGame
    {
        public const long AscensionMs = 99L * 60 * 60000;
        public const long DrawCost = 120 * Rules.UnitsPerMaterial;
        public const long DuplicateRefund = 12 * Rules.UnitsPerMaterial;
        private static readonly CultivatorDefinition[] definitions = {
            new CultivatorDefinition("sword", "沈照", "剑修", "云海剑峰", "他将剑放在檐下，先学会听风。每次归来，剑锋与心境都更清明。"),
            new CultivatorDefinition("herb", "苏苓", "草木修", "花木洞天", "她记得每一株草木的名字。在寂静的日子里，种子也在扎根。"),
            new CultivatorDefinition("star", "闻星", "观星修", "星河天阙", "他把星图留在窗前，不急于求得答案，只认真记下每次新的发现。"),
            new CultivatorDefinition("ink", "江砚", "墨修", "烟雨书山", "她以墨入道。一页页写过的纸，比任何捷径都更接近心中的山水。"),
            new CultivatorDefinition("flame", "陆焰", "炼器修", "赤霞丹境", "他守着炉火，懂得什么时候添柴，什么时候停下来等待。"),
            new CultivatorDefinition("snow", "白霁", "霜华修", "雪照琼台", "她在雪中看见四季。漫长的寂静没有抹去来路，反而让它更加清楚。")
        };
        public static CultivatorDefinition[] Characters { get { return (CultivatorDefinition[])definitions.Clone(); } }
        public static CultivatorDefinition Character(string id) { return definitions.FirstOrDefault(x => x.Id == id); }
        public static string Realm(long ms)
        {
            long[] hours = { 0, 1, 6, 15, 30, 48, 66, 84, 99 };
            string[] names = { "炼气", "筑基", "金丹", "元婴", "化神", "炼虚", "合体", "大乘", "飞升" };
            for (int i = hours.Length - 1; i >= 0; i--) if (ms >= hours[i] * 3600000L) return names[i];
            return names[0];
        }
        public static long NextRealmMs(long ms)
        {
            foreach (long h in new long[] { 1, 6, 15, 30, 48, 66, 84, 99 }) if (h * 3600000L > ms) return h * 3600000L;
            return AscensionMs;
        }
        private static void Idle(SaveData save)
        { if (save.Pending != null) throw new InvalidOperationException("请先结束并保存这轮专注，再管理宗门。"); }
        private static void Spend(SaveData save, long units)
        { if (save.WalletUnits < units) throw new InvalidOperationException("材料不足，需要 " + Rules.Materials(units) + "，当前 " + Rules.Materials(save.WalletUnits) + "。"); save.WalletUnits -= units; }
        private static void Fill(SectData sect)
        {
            int free = sect.Capacity - sect.Roster.Count(x => x.Active);
            foreach (Cultivator c in sect.Roster.Where(x => !x.Active && x.AscendedUtc == null).Take(free)) c.Active = true;
        }
        private static void Join(SectData sect, string id)
        {
            if (Character(id) == null || sect.Roster.Any(x => x.Id == id)) throw new InvalidOperationException("请选择尚未收录的人物。");
            sect.Roster.Add(new Cultivator { Id = id, JoinedUtc = DateTime.UtcNow.ToString("o") }); Fill(sect);
        }
        public static void ChooseStarter(SaveData save, string id)
        {
            Idle(save); if (save.Sect.Roster.Count != 0) throw new InvalidOperationException("初始人物已选择。"); Join(save.Sect, id);
        }
        public static long HousingPrice(SectData sect) { return sect.Capacity == 1 ? 360 * Rules.UnitsPerMaterial : sect.Capacity == 2 ? 900 * Rules.UnitsPerMaterial : 0; }
        public static long MountainPrice(SectData sect) { return sect.MountainLevel == 1 ? 180 * Rules.UnitsPerMaterial : sect.MountainLevel == 2 ? 540 * Rules.UnitsPerMaterial : 0; }
        public static void UpgradeHousing(SaveData save)
        {
            Idle(save); long cost = HousingPrice(save.Sect); if (cost == 0) throw new InvalidOperationException("已有三个修炼位。");
            Spend(save, cost); save.Sect.Capacity++; Fill(save.Sect);
        }
        public static void UpgradeMountain(SaveData save)
        {
            Idle(save); long cost = MountainPrice(save.Sect); if (cost == 0) throw new InvalidOperationException("观云台已开放。");
            Spend(save, cost); save.Sect.MountainLevel++;
        }
        // An injected roll keeps the rule deterministic. The desktop uses cryptographic randomness.
        public static DrawReceipt Draw(SaveData save, string requestId, int roll)
        {
            Idle(save);
            if (String.IsNullOrWhiteSpace(requestId) || requestId.Length > 80 || roll < 0 || roll >= definitions.Length) throw new ArgumentException("抽取请求无效。");
            SectData sect = save.Sect;
            DrawReceipt prior = sect.Draws.FirstOrDefault(x => x.Id == requestId); if (prior != null) return prior;
            if (sect.Roster.Count == 0) throw new InvalidOperationException("请先选择一名初始人物。");
            if (sect.PendingDrawId != null) throw new InvalidOperationException("请先完成已付费的保底选择。");
            if (sect.Roster.Count == definitions.Length) throw new InvalidOperationException("本期人物已全部收录，材料将保留。");
            Spend(save, DrawCost);
            var receipt = new DrawReceipt { Id = requestId, Choice = sect.DuplicateStreak == 4 };
            sect.Draws.Add(receipt);
            if (receipt.Choice) { sect.PendingDrawId = requestId; return receipt; }
            receipt.CharacterId = definitions[roll].Id;
            receipt.Duplicate = sect.Roster.Any(x => x.Id == receipt.CharacterId);
            if (receipt.Duplicate) { save.WalletUnits = checked(save.WalletUnits + DuplicateRefund); sect.DuplicateStreak++; }
            else { Join(sect, receipt.CharacterId); sect.DuplicateStreak = 0; }
            return receipt;
        }
        public static void ChoosePity(SaveData save, string requestId, string characterId)
        {
            Idle(save); SectData sect = save.Sect;
            DrawReceipt receipt = sect.Draws.FirstOrDefault(x => x.Id == requestId && x.Choice);
            if (receipt == null) throw new InvalidOperationException("保底记录不存在。");
            if (receipt.CharacterId != null) {
                if (receipt.CharacterId != characterId) throw new InvalidOperationException("这次保底已经选择。"); return;
            }
            if (sect.PendingDrawId != requestId) throw new InvalidOperationException("保底状态不一致。");
            Join(sect, characterId); receipt.CharacterId = characterId; sect.PendingDrawId = null; sect.DuplicateStreak = 0;
        }
        public static void PrioritizeWaiting(SaveData save, string id)
        {
            Idle(save); var c = save.Sect.Roster.FirstOrDefault(x => x.Id == id && !x.Active && x.AscendedUtc == null);
            if (c == null) throw new InvalidOperationException("只能调整候修人物的顺序。");
            save.Sect.Roster.Remove(c); save.Sect.Roster.Insert(0, c);
        }
        public static bool ThemeUnlocked(SectData sect, string id)
        { return id == "default" || sect.Roster.Any(x => x.Id == id && x.AscendedUtc != null); }
        public static void SetTheme(SaveData save, string id, bool ui)
        {
            Idle(save); if (!ThemeUnlocked(save.Sect, id)) throw new InvalidOperationException("该外观将在对应人物飞升后解锁。");
            if (ui) save.Sect.UiTheme = id; else save.Sect.SceneTheme = id;
        }
        public static void ToggleMemorial(SaveData save, string id)
        {
            Idle(save); var s = save.Sect;
            if (s.Memorials.Contains(id)) { s.Memorials.Remove(id); return; }
            if (id == "default" || !ThemeUnlocked(s, id)) throw new InvalidOperationException("飞升人物才能留下祖师纪念。");
            if (s.Memorials.Count >= s.MountainLevel - 1) throw new InvalidOperationException("请先升级灵山，开放更多纪念位置。");
            s.Memorials.Add(id);
        }
        public static void Progress(SectData sect, long effectiveMs, string recordedUtc, string task)
        {
            if (effectiveMs < 0) throw new ArgumentOutOfRangeException("effectiveMs");
            Fill(sect);
            while (effectiveMs > 0)
            {
                var active = sect.Roster.Where(x => x.Active).ToArray(); if (active.Length == 0) break;
                long step = Math.Min(effectiveMs, active.Min(x => AscensionMs - x.CultivationMs));
                if (step <= 0) throw new InvalidOperationException("修炼状态无效。");
                foreach (var c in active) {
                    c.CultivationMs += step; c.LastTask = task;
                    if (c.CultivationMs == AscensionMs) { c.Active = false; c.AscendedUtc = recordedUtc; }
                }
                effectiveMs -= step; Fill(sect);
            }
        }
        public static void Validate(SectData s)
        {
            if (s == null || s.Roster == null || s.Draws == null || s.Memorials == null || s.Capacity < 1 || s.Capacity > 3 || s.MountainLevel < 1 || s.MountainLevel > 3 ||
                s.DuplicateStreak < 0 || s.DuplicateStreak > 4 || s.Roster.Count > definitions.Length || s.Draws.Count > 25 ||
                s.Roster.Any(x => x == null) || s.Draws.Any(x => x == null) ||
                s.Roster.Select(x => x.Id).Distinct().Count() != s.Roster.Count || s.Draws.Select(x => x.Id).Distinct().Count() != s.Draws.Count)
                throw new InvalidOperationException("宗门存档无效。");
            DateTime stamp;
            foreach (var c in s.Roster)
                if (Character(c.Id) == null || c.CultivationMs < 0 || c.CultivationMs > AscensionMs || !DateTime.TryParse(c.JoinedUtc, out stamp) ||
                    (c.AscendedUtc != null && (!DateTime.TryParse(c.AscendedUtc, out stamp) || c.Active || c.CultivationMs != AscensionMs)) ||
                    (c.AscendedUtc == null && c.CultivationMs == AscensionMs)) throw new InvalidOperationException("人物存档无效。");
            if (s.Roster.Count(x => x.Active) != Math.Min(s.Capacity, s.Roster.Count(x => x.AscendedUtc == null))) throw new InvalidOperationException("修炼位状态无效。");
            foreach (var d in s.Draws)
                if (String.IsNullOrWhiteSpace(d.Id) || d.Id.Length > 80 || (d.CharacterId != null && !s.Roster.Any(x => x.Id == d.CharacterId)) ||
                    (d.CharacterId == null && (!d.Choice || d.Id != s.PendingDrawId)) || (d.Choice && d.Duplicate)) throw new InvalidOperationException("结缘记录无效。");
            if (s.PendingDrawId != null && (s.DuplicateStreak != 4 || s.Draws.Count(x => x.Id == s.PendingDrawId && x.CharacterId == null && x.Choice) != 1))
                throw new InvalidOperationException("保底存档无效。");
            // Reconstruct the fixed pool and pity counter from receipts, even when the waiting order changes.
            var known = new HashSet<string>(s.Roster.Select(x => x.Id).Except(s.Draws.Where(x => !x.Duplicate && x.CharacterId != null).Select(x => x.CharacterId)));
            if (known.Count != (s.Roster.Count == 0 ? 0 : 1)) throw new InvalidOperationException("初始人物或结缘记录不完整。");
            int streak = 0;
            for (int i = 0; i < s.Draws.Count; i++) {
                var d = s.Draws[i];
                if (known.Count == 0 || known.Count == definitions.Length || d.Choice != (streak == 4)) throw new InvalidOperationException("结缘保底顺序无效。");
                if (d.CharacterId == null) {
                    if (i != s.Draws.Count - 1) throw new InvalidOperationException("未完成的保底必须是最后一次结缘。");
                    continue;
                }
                if (d.Duplicate != known.Contains(d.CharacterId)) throw new InvalidOperationException("结缘结果与人物记录不一致。");
                if (d.Duplicate) streak++;
                else { known.Add(d.CharacterId); streak = 0; }
            }
            if (streak != s.DuplicateStreak || known.Count != s.Roster.Count) throw new InvalidOperationException("结缘进度不一致。");
            if (!ThemeUnlocked(s, s.SceneTheme) || !ThemeUnlocked(s, s.UiTheme) || s.Memorials.Count > s.MountainLevel - 1 || s.Memorials.Distinct().Count() != s.Memorials.Count ||
                s.Memorials.Any(x => x == "default" || !ThemeUnlocked(s, x))) throw new InvalidOperationException("外观或纪念存档无效。");
        }
    }
}
